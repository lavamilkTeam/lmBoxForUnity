using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lmbox.AIAgent.Commands.Internal
{
    internal sealed class CommandSpec
    {
        internal readonly string Name, Description;
        private readonly Dictionary<string, string> Fields;
        private readonly HashSet<string> Required;
        internal readonly Func<JObject, Func<JObject>> Prepare;
        internal CommandSpec(string name, string description, string fields, Func<JObject, Func<JObject>> prepare)
        {
            Name = name;
            Description = description;
            Prepare = prepare;
            Fields = new Dictionary<string, string>();
            Required = new HashSet<string>();
            foreach (var field in fields.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = field.Split(':');
                var key = parts[0].TrimEnd('!');
                Fields.Add(key, parts.Length > 1 ? parts[1] : "string");
                if (parts[0].EndsWith("!"))
                    Required.Add(key);
            }
        }
        internal void Validate(JObject o)
        {
            CommandInput.Keys(o, Fields.Keys.Concat(new[] { "op", "id" }).ToArray());
            foreach (var key in Required)
                if (o[key] == null)
                    throw new ArgumentException(key + " is required for " + Name);
            foreach (var field in Fields)
            {
                var value = o[field.Key];
                if (value == null)
                    continue;
                bool valid = field.Value == "string" ? value.Type == JTokenType.String : field.Value == "integer" ? value.Type == JTokenType.Integer : field.Value == "number" ? value.Type == JTokenType.Integer || value.Type == JTokenType.Float : field.Value == "boolean" ? value.Type == JTokenType.Boolean : field.Value == "object" ? value is JObject : value is JArray;
                if (!valid)
                    throw new ArgumentException(field.Key + " must be " + field.Value);
            }
        }
        internal JObject Schema() => new JObject { ["command"] = Name, ["description"] = Description, ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JArray(Required.Prepend("op")), ["properties"] = new JObject(Fields.Select(f => new JProperty(f.Key, new JObject { ["type"] = f.Value })).Prepend(new JProperty("op", new JObject { ["const"] = Name })).Append(new JProperty("id", new JObject { ["type"] = "string" }))) };
    }
    internal static class CommandInput
    {
        internal static readonly string Session = Guid.NewGuid().ToString("N");
        internal static void Add(Dictionary<string, CommandSpec> c, string op, string description, string fields, Func<JObject, Func<JObject>> prepare) => c.Add(op, new CommandSpec(op, description, fields, prepare));
        internal static void Keys(JObject o, params string[] allowed)
        {
            foreach (var p in o.Properties())
                if (!allowed.Contains(p.Name))
                    throw new ArgumentException("Unknown field: " + p.Name);
        }
        internal static string Text(JObject o, string key, string fallback = null)
        {
            if (o[key] == null && fallback != null)
                return fallback;
            if (o[key]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)o[key]))
                throw new ArgumentException(key + " must be a nonempty string");
            return (string)o[key];
        }
        internal static int Integer(JObject o, string key, int fallback, int min, int max)
        {
            if (o[key] != null && o[key].Type != JTokenType.Integer)
                throw new ArgumentException(key + " must be an integer");
            long n = o.Value<long?>(key) ?? fallback;
            if (n < min || n > max)
                throw new ArgumentException(key + " must be in " + min + ".." + max);
            return (int)n;
        }
        internal static float Number(JObject o, string key, float fallback, float min = float.MinValue, float max = float.MaxValue)
        {
            if (o[key] != null && o[key].Type != JTokenType.Integer && o[key].Type != JTokenType.Float)
                throw new ArgumentException(key + " must be numeric");
            float n = o.Value<float?>(key) ?? fallback;
            if (float.IsNaN(n) || float.IsInfinity(n) || n < min || n > max)
                throw new ArgumentException("Invalid " + key);
            return n;
        }
        internal static bool Boolean(JObject o, string key, bool fallback)
        {
            if (o[key] != null && o[key].Type != JTokenType.Boolean)
                throw new ArgumentException(key + " must be boolean");
            return o.Value<bool?>(key) ?? fallback;
        }
        internal static Vector3 Vector(JObject o, string key, Vector3 fallback)
        {
            if (o[key] == null)
                return fallback;
            if (!(o[key] is JObject v))
                throw new ArgumentException(key + " must be {x,y,z}");
            Keys(v, "x", "y", "z");
            if (v["x"] == null || v["y"] == null || v["z"] == null)
                throw new ArgumentException(key + " requires all axes");
            return new Vector3(Number(v, "x", 0), Number(v, "y", 0), Number(v, "z", 0));
        }
        internal static Color Colour(JObject o, string key, Color fallback)
        {
            if (o[key] == null)
                return fallback;
            if (!ColorUtility.TryParseHtmlString(Text(o, key), out var c))
                throw new ArgumentException(key + " must be an HTML colour such as #ffffff");
            return c;
        }
        internal static T EnumValue<T>(JObject o, string key, T fallback) where T : struct
        {
            if (o[key] == null)
                return fallback;
            string s = Text(o, key);
            if (!Enum.TryParse<T>(s, true, out var v) || !Enum.IsDefined(typeof(T), v))
                throw new ArgumentException("Invalid " + key);
            return v;
        }
        internal static IEnumerable<GameObject> Objects()
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        yield return t.gameObject;
            }
        }
        internal static string Ref(UnityEngine.Object o) => "session:" + Session + ":" + o.GetInstanceID();
        internal static string Hierarchy(GameObject go) => go.transform.parent == null ? go.name : Hierarchy(go.transform.parent.gameObject) + "/" + go.name;
        internal static GameObject Target(string value)
        {
            if (value.StartsWith("session:"))
            {
                var p = value.Split(':');
                if (p.Length != 3 || p[1] != Session || !int.TryParse(p[2], out var id))
                    throw new ArgumentException("Expired object reference");
                var go = EditorUtility.InstanceIDToObject(id) as GameObject;
                if (go == null || !go.scene.IsValid() || !go.scene.isLoaded)
                    throw new ArgumentException("Object no longer exists");
                return go;
            }
            var matches = Objects().Where(g => Hierarchy(g) == value || g.name == value).Distinct().Take(2).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException(matches.Length == 0 ? "Object not found: " + value : "Ambiguous object; use its ref: " + value);
            return matches[0];
        }
        internal static T Component<T>(JObject o, string key = "target") where T : Component
        {
            var go = Target(Text(o, key));
            var c = go.GetComponent<T>();
            if (c == null)
                throw new ArgumentException("Target lacks " + typeof(T).Name);
            return c;
        }
        internal static JObject Describe(GameObject go) => new JObject { ["ref"] = Ref(go), ["name"] = go.name, ["path"] = Hierarchy(go), ["position"] = Vec(go.transform.position), ["rotation"] = Vec(go.transform.eulerAngles), ["scale"] = Vec(go.transform.localScale) };
        internal static JObject Vec(Vector3 v) => new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };
        internal static void Dirty(GameObject go) => EditorSceneManager.MarkSceneDirty(go.scene);
        internal static JObject Done(string key, JToken value) => new JObject { [key] = value };
        internal static string AssetPath(string value, bool mustExist = false, string extension = null)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("Assets/", StringComparison.Ordinal) || value.Contains('\\') || value.Split('/').Any(p => p == ".." || p == "." || p == ""))
                throw new ArgumentException("Use a project-relative Assets/... path");
            string root = Path.GetFullPath(Application.dataPath), full = Path.GetFullPath(value);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("Asset escapes project");
            NoLinks(full, root);
            if (mustExist && !File.Exists(full) && !Directory.Exists(full))
                throw new ArgumentException("Asset not found: " + value);
            if (extension != null && !value.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Expected " + extension + " path");
            return value;
        }
        internal static void NoLinks(string full, string root)
        {
            for (string p = full; p != null; p = Path.GetDirectoryName(p))
            {
                try
                {
                    if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                        throw new ArgumentException("Symbolic links are not allowed");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                if (p == root)
                    break;
            }
        }
        internal static string NewAsset(string path, string extension)
        {
            AssetPath(path, false, extension);
            if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new ArgumentException("Refusing to overwrite " + path);
            return path;
        }
        internal static void EnsureParent(string path)
        {
            string dir = Path.GetDirectoryName(path).Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(dir))
                return;
            string current = "Assets";
            foreach (string part in dir.Split('/').Skip(1))
            {
                string next = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, part);
                current = next;
            }
        }
        internal static JObject Page(IEnumerable<JObject> source, JObject o)
        {
            int limit = Integer(o, "limit", 30, 1, 200), cursor = Integer(o, "cursor", 0, 0, 1000000);
            var items = source.Skip(cursor).Take(limit + 1).ToArray();
            return new JObject { ["items"] = new JArray(items.Take(limit)), ["truncated"] = items.Length > limit, ["nextCursor"] = items.Length > limit ? (JToken)(cursor + limit) : JValue.CreateNull() };
        }
        internal static void Transform(GameObject go, Vector3 pos, Vector3 rot, Vector3 scale)
        {
            Undo.RecordObject(go.transform, "lmu3d transform");
            go.transform.position = pos;
            go.transform.eulerAngles = rot;
            go.transform.localScale = scale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            Dirty(go);
        }
    }
}
