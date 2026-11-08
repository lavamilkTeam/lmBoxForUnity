using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static Lmbox.AIAgent.Commands.Internal.CommandInput;

namespace Lmbox.AIAgent.Commands.Internal
{
    internal static class ObjectCommands
    {
        internal static void Register(Dictionary<string, CommandSpec> c)
        {
            Add(c, "objects.list", "List scene objects including inactive objects; optional name filter.", "name limit:integer cursor:integer", o => { string name = Text(o, "name", ""); Integer(o, "limit", 30, 1, 200); Integer(o, "cursor", 0, 0, 1000000); return () => Page(Objects().Where(g => g.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0).Select(Describe), o); });
            Add(c, "objects.add", "Create empty objects or primitives. Count <= 100; position/rotation/scale/step use {x,y,z}.", "name primitive count:integer position:object rotation:object scale:object step:object parent", o => PreparePlace(o, false));
            Add(c, "objects.place", "Place an existing prefab at specified transforms; count <= 100.", "prefab! name count:integer position:object rotation:object scale:object step:object parent", o => PreparePlace(o, true));
            Add(c, "objects.update", "Update object transform, name or parent. target is a unique name/path/ref.", "target! name position:object rotation:object scale:object parent", o =>
            {
                var go = Target(Text(o, "target"));
                var parent = o["parent"] == null ? go.transform.parent : Target(Text(o, "parent")).transform;
                if (parent != null && (parent == go.transform || parent.IsChildOf(go.transform)))
                    throw new ArgumentException("Parent would create a cycle");
                Vector3 p = Vector(o, "position", go.transform.position), r = Vector(o, "rotation", go.transform.eulerAngles), s = Vector(o, "scale", go.transform.localScale);
                string name = Text(o, "name", go.name);
                return () => { Undo.RecordObject(go, "lmu3d rename"); go.name = name; if (parent != go.transform.parent) Undo.SetTransformParent(go.transform, parent, "lmu3d parent"); Transform(go, p, r, s); PrefabUtility.RecordPrefabInstancePropertyModifications(go); return Describe(go); };
            });
            Add(c, "objects.duplicate", "Duplicate an object and optionally position/rename it.", "target! name position:object", o =>
            {
                var source = Target(Text(o, "target"));
                string name = Text(o, "name", source.name + " Copy");
                Vector3 p = Vector(o, "position", source.transform.position);
                return () => { var go = UnityEngine.Object.Instantiate(source, source.transform.parent); Undo.RegisterCreatedObjectUndo(go, "lmu3d duplicate"); go.name = name; go.transform.position = p; Dirty(go); return Describe(go); };
            });
            Add(c, "objects.remove", "Remove an object with Unity Undo support.", "target!", o => { var go = Target(Text(o, "target")); return () => { string reference = Ref(go); Dirty(go); Undo.DestroyObjectImmediate(go); return Done("removed", reference); }; });
            Add(c, "components.list", "List attached components and their serialized editable properties.", "target! limit:integer cursor:integer", o =>
            {
                var go = Target(Text(o, "target"));
                Integer(o, "limit", 30, 1, 200);
                Integer(o, "cursor", 0, 0, 1000000);
                return () => Page(go.GetComponents<UnityEngine.Component>().Where(x => x != null).Select(component =>
                {
                    var fields = new JArray();
                    using (var serialized = new SerializedObject(component))
                    {
                        var it = serialized.GetIterator();
                        int n = 0;
                        while (it.NextVisible(n == 0) && n++ < 100)
                            fields.Add(DescribeProperty(it));
                    }
                    return new JObject { ["type"] = component.GetType().FullName, ["properties"] = fields };
                }), o);
            });
            Add(c, "components.add", "Add a built-in UnityEngine component by full or unambiguous short type name.", "target! type!", o =>
            {
                var go = Target(Text(o, "target"));
                var type = ComponentType(Text(o, "type"));
                if (type == typeof(Transform))
                    throw new ArgumentException("A GameObject already has a Transform");
                return () => { var comp = Undo.AddComponent(go, type); if (comp == null) throw new InvalidOperationException("Cannot add component"); Dirty(go); return Done("type", type.FullName); };
            });
            Add(c, "components.remove", "Remove one component by type; ambiguous duplicates are rejected.", "target! type!", o =>
            {
                var comp = FindComponent(o);
                if (comp is Transform)
                    throw new ArgumentException("Cannot remove Transform");
                return () => { var go = comp.gameObject; Undo.DestroyObjectImmediate(comp); Dirty(go); return Done("removed", true); };
            });
            Add(c, "components.set", "Set scalar/vector/color serialized properties. Inspect components.list for paths.", "target! type! properties!:object", o =>
            {
                var comp = FindComponent(o);
                var changes = (JObject)o["properties"];
                if (changes.Count == 0 || changes.Count > 32)
                    throw new ArgumentException("Use 1..32 properties");
                using (var so = new SerializedObject(comp))
                    foreach (var item in changes.Properties())
                        Assign(so, item.Name, item.Value);
                return () => { Undo.RecordObject(comp, "lmu3d component"); using (var so = new SerializedObject(comp)) { foreach (var item in changes.Properties()) Assign(so, item.Name, item.Value); so.ApplyModifiedProperties(); } PrefabUtility.RecordPrefabInstancePropertyModifications(comp); Dirty(comp.gameObject); return Done("updated", changes.Count); };
            });
        }
        private static Func<JObject> PreparePlace(JObject o, bool prefab)
        {
            int count = Integer(o, "count", 1, 1, 100);
            string name = Text(o, "name", prefab ? "Instance" : "GameObject");
            Vector3 p = Vector(o, "position", Vector3.zero), r = Vector(o, "rotation", Vector3.zero), s = Vector(o, "scale", Vector3.one), step = Vector(o, "step", Vector3.right);
            var parent = o["parent"] == null ? null : Target(Text(o, "parent")).transform;
            GameObject asset = null;
            PrimitiveType? primitive = null;
            if (prefab)
            {
                asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath(Text(o, "prefab"), true));
                if (asset == null || !PrefabUtility.IsPartOfPrefabAsset(asset))
                    throw new ArgumentException("prefab must be a prefab asset");
            }
            else if (o["primitive"] != null)
                primitive = EnumValue(o, "primitive", PrimitiveType.Cube);
            return () =>
            {
                var items = new JArray();
                for (int i = 0; i < count; i++)
                {
                    var go = prefab ? (GameObject)PrefabUtility.InstantiatePrefab(asset) : primitive.HasValue ? GameObject.CreatePrimitive(primitive.Value) : new GameObject();
                    Undo.RegisterCreatedObjectUndo(go, "lmu3d place");
                    if (parent != null)
                        Undo.SetTransformParent(go.transform, parent, "lmu3d parent");
                    go.name = count == 1 ? name : name + "-" + (i + 1);
                    Transform(go, p + step * i, r, s);
                    items.Add(Describe(go));
                }
                return new JObject { ["created"] = count, ["objects"] = items };
            };
        }
        private static Type ComponentType(string name)
        {
            var matches = TypeCache.GetTypesDerivedFrom<UnityEngine.Component>().Where(t => !t.IsAbstract && !t.ContainsGenericParameters && t.Namespace != null && t.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal) && (t.FullName == name || t.Name == name)).Take(2).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("Unknown or ambiguous built-in component type: " + name);
            return matches[0];
        }
        private static UnityEngine.Component FindComponent(JObject o)
        {
            var comps = Target(Text(o, "target")).GetComponents(ComponentType(Text(o, "type")));
            if (comps.Length != 1)
                throw new ArgumentException("Component missing or ambiguous");
            return comps[0];
        }
        private static JObject DescribeProperty(SerializedProperty property)
        {
            var result = new JObject { ["path"] = property.propertyPath, ["type"] = property.propertyType.ToString(), ["editable"] = property.editable };
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    result["value"] = property.boolValue;
                    break;
                case SerializedPropertyType.Integer:
                    result["value"] = property.longValue;
                    break;
                case SerializedPropertyType.Float:
                    result["value"] = property.doubleValue;
                    break;
                case SerializedPropertyType.String:
                    string text = property.stringValue ?? "";
                    result["value"] = text.Length > 512 ? text.Substring(0, 512) : text;
                    result["truncated"] = text.Length > 512;
                    break;
                case SerializedPropertyType.Enum:
                    result["value"] = property.enumValueIndex;
                    result["enumNames"] = new JArray(property.enumNames);
                    break;
                case SerializedPropertyType.Vector3:
                    result["value"] = Vec(property.vector3Value);
                    break;
                case SerializedPropertyType.Color:
                    result["value"] = "#" + ColorUtility.ToHtmlStringRGBA(property.colorValue);
                    break;
                case SerializedPropertyType.ObjectReference:
                    var value = property.objectReferenceValue;
                    result["value"] = value == null ? JValue.CreateNull() : (JToken)(EditorUtility.IsPersistent(value) ? AssetDatabase.GetAssetPath(value) : value.name);
                    break;
            }
            return result;
        }
        private static void Assign(SerializedObject so, string path, JToken value)
        {
            var p = so.FindProperty(path);
            if (p == null || !p.editable || path == "m_Script" || path == "m_GameObject")
                throw new ArgumentException("Property not editable: " + path);
            var v = new JObject { ["value"] = value.DeepClone() };
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    p.boolValue = Boolean(v, "value", false);
                    break;
                case SerializedPropertyType.Integer:
                    p.intValue = Integer(v, "value", 0, int.MinValue, int.MaxValue);
                    break;
                case SerializedPropertyType.Float:
                    p.floatValue = Number(v, "value", 0);
                    break;
                case SerializedPropertyType.String:
                    p.stringValue = Text(v, "value");
                    break;
                case SerializedPropertyType.Enum:
                    p.enumValueIndex = Integer(v, "value", 0, 0, p.enumNames.Length - 1);
                    break;
                case SerializedPropertyType.Vector3:
                    p.vector3Value = Vector(v, "value", Vector3.zero);
                    break;
                case SerializedPropertyType.Color:
                    p.colorValue = Colour(v, "value", Color.white);
                    break;
                default:
                    throw new ArgumentException("Unsupported property type at " + path + ": " + p.propertyType);
            }
        }
    }
}
