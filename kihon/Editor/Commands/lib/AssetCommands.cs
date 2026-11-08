using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static Lmbox.AIAgent.Commands.Internal.CommandInput;

namespace Lmbox.AIAgent.Commands.Internal
{
    internal static class AssetCommands
    {
        internal static void Register(Dictionary<string, CommandSpec> c)
        {
            Add(c, "assets.find", "Search project assets by Unity filter and optional type (Prefab, Material, Texture2D...).", "query type limit:integer cursor:integer", o =>
            {
                string query = Text(o, "query", ""), type = Text(o, "type", "");
                Integer(o, "limit", 30, 1, 200);
                Integer(o, "cursor", 0, 0, 1000000);
                return () => Page(AssetDatabase.FindAssets(query + (type == "" ? "" : " t:" + type), new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal).Select(p => new JObject { ["path"] = p, ["type"] = AssetDatabase.GetMainAssetTypeAtPath(p)?.Name }), o);
            });
            Add(c, "assets.import", "Import/reimport an existing file or folder under Assets.", "path!", o => { string path = AssetPath(Text(o, "path"), true); return () => { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive); return Done("imported", path); }; });
            Add(c, "assets.prefab", "Save target GameObject as a new prefab asset; refuses overwrite.", "target! path!", o => { var go = Target(Text(o, "target")); string path = NewAsset(Text(o, "path"), ".prefab"); return () => { EnsureParent(path); var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out bool success); if (!success || prefab == null) throw new InvalidOperationException("Prefab save failed"); return Done("path", path); }; });
            Add(c, "materials.create", "Create a material with a known shader. properties map shader names to numbers, colours or texture asset paths.", "path! shader! properties:object", o => PrepareMaterial(o, true));
            Add(c, "materials.update", "Update an existing material's shader properties.", "path! properties!:object", o => PrepareMaterial(o, false));
            Add(c, "materials.apply", "Assign an existing material to a renderer slot.", "target! material! slot:integer", o =>
            {
                var renderer = Component<Renderer>(o);
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetPath(Text(o, "material"), true, ".mat"));
                if (material == null)
                    throw new ArgumentException("Material not found");
                int slot = Integer(o, "slot", 0, 0, renderer.sharedMaterials.Length - 1);
                return () => { Undo.RecordObject(renderer, "lmu3d material assignment"); var materials = renderer.sharedMaterials; materials[slot] = material; renderer.sharedMaterials = materials; PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); Dirty(renderer.gameObject); return Done("assigned", Ref(renderer.gameObject)); };
            });
            Add(c, "materials.layer", "Create a TerrainLayer asset from an existing diffuse texture.", "path! texture! tileSize:object", o =>
            {
                string path = NewAsset(Text(o, "path"), ".terrainlayer");
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath(Text(o, "texture"), true));
                if (texture == null)
                    throw new ArgumentException("Texture not found");
                Vector3 size = Vector(o, "tileSize", new Vector3(15, 1, 15));
                if (size.x <= 0 || size.z <= 0)
                    throw new ArgumentException("tileSize x/z must be positive");
                return () => { EnsureParent(path); var layer = new TerrainLayer { diffuseTexture = texture, tileSize = new Vector2(size.x, size.z) }; AssetDatabase.CreateAsset(layer, path); Undo.RegisterCreatedObjectUndo(layer, "lmu3d terrain layer"); return Done("path", path); };
            });
        }
        private static Func<JObject> PrepareMaterial(JObject o, bool create)
        {
            string path = create ? NewAsset(Text(o, "path"), ".mat") : AssetPath(Text(o, "path"), true, ".mat");
            var existing = create ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!create && existing == null)
                throw new ArgumentException("Material not found");
            var shader = create ? Shader.Find(Text(o, "shader")) : existing.shader;
            if (shader == null)
                throw new ArgumentException("Shader not found");
            var props = o["properties"] as JObject ?? new JObject();
            if (props.Count > 32)
                throw new ArgumentException("At most 32 material properties");
            var setters = new List<Action<Material>>();
            foreach (var p in props.Properties())
            {
                int index = shader.FindPropertyIndex(p.Name);
                if (index < 0)
                    throw new ArgumentException("Shader has no property " + p.Name);
                var type = shader.GetPropertyType(index);
                string key = p.Name;
                if (type == UnityEngine.Rendering.ShaderPropertyType.Color)
                {
                    var v = new JObject { [key] = p.Value.DeepClone() };
                    Color value = Colour(v, key, Color.white);
                    setters.Add(m => m.SetColor(key, value));
                }
                else if (type == UnityEngine.Rendering.ShaderPropertyType.Float || type == UnityEngine.Rendering.ShaderPropertyType.Range)
                {
                    var v = new JObject { [key] = p.Value.DeepClone() };
                    float value = Number(v, key, 0);
                    setters.Add(m => m.SetFloat(key, value));
                }
                else if (type == UnityEngine.Rendering.ShaderPropertyType.Texture)
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture>(AssetPath((string)p.Value, true));
                    if (texture == null)
                        throw new ArgumentException("Texture not found for " + key);
                    setters.Add(m => m.SetTexture(key, texture));
                }
                else
                    throw new ArgumentException("Unsupported shader property type: " + type);
            }
            return () => { var material = existing; if (create) { material = new Material(shader); foreach (var set in setters) set(material); EnsureParent(path); AssetDatabase.CreateAsset(material, path); Undo.RegisterCreatedObjectUndo(material, "lmu3d material"); } else { Undo.RecordObject(material, "lmu3d material"); foreach (var set in setters) set(material); EditorUtility.SetDirty(material); } return Done("path", path); };
        }
    }
}
