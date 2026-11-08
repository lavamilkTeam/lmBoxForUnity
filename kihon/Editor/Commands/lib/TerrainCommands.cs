using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static Lmbox.AIAgent.Commands.Internal.CommandInput;

namespace Lmbox.AIAgent.Commands.Internal
{
    internal static class TerrainCommands
    {
        internal static void Register(Dictionary<string, CommandSpec> c, Action<Terrain, int> fillTerrain)
        {
            Add(c, "terrain.add", "Create 1..16 flat terrains; resolution: 33,65,129,257,513,1025. TerrainData saved under Assets/LmBoxGenerated/Terrains.", "name count:integer position:object step:object size:object resolution:integer", o =>
            {
                int count = Integer(o, "count", 1, 1, 16), resolution = Integer(o, "resolution", 129, 33, 1025);
                if ((resolution - 1 & (resolution - 2)) != 0)
                    throw new ArgumentException("resolution must be power-of-two + 1");
                Vector3 size = Vector(o, "size", new Vector3(500, 100, 500));
                if (size.x <= 0 || size.y <= 0 || size.z <= 0)
                    throw new ArgumentException("size axes must be positive");
                Vector3 position = Vector(o, "position", Vector3.zero), step = Vector(o, "step", new Vector3(size.x, 0, 0));
                string name = Text(o, "name", "Terrain");
                return () =>
                {
                    var created = new JArray();
                    for (int i = 0; i < count; i++)
                    {
                        var data = new TerrainData { heightmapResolution = resolution, size = size, alphamapResolution = 128 };
                        string path = "Assets/LmBoxGenerated/Terrains/" + Guid.NewGuid().ToString("N") + ".asset";
                        AssetPath(path);
                        EnsureParent(path);
                        AssetDatabase.CreateAsset(data, path);
                        Undo.RegisterCreatedObjectUndo(data, "lmu3d terrain data");
                        var go = Terrain.CreateTerrainGameObject(data);
                        Undo.RegisterCreatedObjectUndo(go, "lmu3d terrain");
                        go.name = count == 1 ? name : name + "-" + (i + 1);
                        go.transform.position = position + step * i;
                        Dirty(go);
                        var result = Describe(go);
                        result["asset"] = path;
                        created.Add(result);
                    }
                    return new JObject { ["created"] = count, ["terrains"] = created, ["saved"] = false };
                };
            });
            Add(c, "terrain.layers", "Replace terrain layers from existing TerrainLayer asset paths. Existing paint weights are reset to layer 0.", "target! layers!:array", o =>
            {
                var terrain = Component<Terrain>(o);
                if (terrain.terrainData.alphamapResolution > 1024)
                    throw new ArgumentException("Paint map resolution must be <=1024");
                var input = (JArray)o["layers"];
                if (input.Count < 1 || input.Count > 16)
                    throw new ArgumentException("Use 1..16 TerrainLayer assets");
                var layers = input.Select(x => AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetPath((string)x, true, ".terrainlayer"))).ToArray();
                if (layers.Any(x => x == null))
                    throw new ArgumentException("Invalid TerrainLayer asset");
                return () => { Record(terrain); terrain.terrainData.terrainLayers = layers; fillTerrain(terrain, 0); return Done("layers", layers.Length); };
            });
            Add(c, "terrain.paint", "Paint existing terrain layer. Optional world position and radius define a soft circular brush; strength in 0..1.", "target! layer!:integer position:object radius:number strength:number", o =>
            {
                var terrain = Component<Terrain>(o);
                var data = terrain.terrainData;
                if (data == null || data.alphamapLayers == 0)
                    throw new ArgumentException("Terrain needs TerrainLayer assets first");
                if (data.alphamapResolution > 1024 || data.alphamapLayers > 16)
                    throw new ArgumentException("Paint supports resolution <=1024 and <=16 layers");
                int layer = Integer(o, "layer", 0, 0, data.alphamapLayers - 1);
                float strength = Number(o, "strength", 1, 0, 1);
                bool brush = o["radius"] != null || o["position"] != null;
                if (brush && (o["radius"] == null || o["position"] == null))
                    throw new ArgumentException("Brush requires both position and radius");
                float radius = Number(o, "radius", 1, 0.001f);
                Vector3 centre = Vector(o, "position", Vector3.zero);
                return () =>
                {
                    if (!brush && strength == 1)
                    {
                        fillTerrain(terrain, layer);
                        return Done("painted", Ref(terrain.gameObject));
                    }
                    var map = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
                    Record(terrain);
                    for (int y = 0; y < map.GetLength(0); y++)
                        for (int x = 0; x < map.GetLength(1); x++)
                        {
                            float weight = strength;
                            if (brush)
                            {
                                var world = terrain.transform.TransformPoint(new Vector3(x / (float)(map.GetLength(1) - 1) * data.size.x, 0, y / (float)(map.GetLength(0) - 1) * data.size.z));
                                float distance = Vector2.Distance(new Vector2(world.x, world.z), new Vector2(centre.x, centre.z));
                                weight *= Mathf.Clamp01(1 - distance / radius);
                            }
                            for (int l = 0; l < map.GetLength(2); l++)
                                map[y, x, l] *= 1 - weight;
                            map[y, x, layer] += weight;
                        }
                    data.SetAlphamaps(0, 0, map);
                    EditorUtility.SetDirty(data);
                    return Done("painted", Ref(terrain.gameObject));
                };
            });
            Add(c, "terrain.height", "Set terrain heights: kind flat or noise, elevation/amplitude in terrain local world units, frequency > 0, seed integer.", "target! kind elevation:number amplitude:number frequency:number seed:integer", o =>
            {
                var terrain = Component<Terrain>(o);
                var data = terrain.terrainData;
                string kind = Text(o, "kind", "flat");
                if (kind != "flat" && kind != "noise")
                    throw new ArgumentException("kind must be flat or noise");
                float elevation = Number(o, "elevation", 0, 0, data.size.y), amplitude = Number(o, "amplitude", 10, 0, data.size.y), frequency = Number(o, "frequency", 4, 0.001f, 1000);
                int seed = Integer(o, "seed", 0, 0, int.MaxValue);
                if (data.heightmapResolution > 1025)
                    throw new ArgumentException("Height editing currently supports resolution <= 1025");
                return () =>
                {
                    int n = data.heightmapResolution;
                    var heights = new float[n, n];
                    var random = new System.Random(seed);
                    float ox = random.Next(0, 10000), oy = random.Next(0, 10000);
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                            heights[y, x] = Mathf.Clamp01((elevation + (kind == "noise" ? Mathf.PerlinNoise(ox + x / (float)(n - 1) * frequency, oy + y / (float)(n - 1) * frequency) * amplitude : 0)) / data.size.y);
                    Record(terrain);
                    data.SetHeights(0, 0, heights);
                    EditorUtility.SetDirty(data);
                    return Done("updated", Ref(terrain.gameObject));
                };
            });
            Add(c, "terrain.trees", "Append trees from prefab; positions are normalized {x,y,z} in 0..1. Optional widthScale/heightScale, rotation in radians.", "target! prefab! instances!:array", o =>
            {
                var terrain = Component<Terrain>(o);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath(Text(o, "prefab"), true));
                if (prefab == null)
                    throw new ArgumentException("Tree prefab missing");
                var input = (JArray)o["instances"];
                if (input.Count < 1 || input.Count > 10000)
                    throw new ArgumentException("Use 1..10000 trees");
                var trees = input.Select(t => { if (!(t is JObject tree)) throw new ArgumentException("Tree must be an object"); Keys(tree, "position", "widthScale", "heightScale", "rotation"); Vector3 p = Vector(tree, "position", Vector3.zero); if (p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1 || p.z < 0 || p.z > 1) throw new ArgumentException("Tree position must be normalized 0..1"); return new TreeInstance { position = p, widthScale = Number(tree, "widthScale", 1, 0.01f, 100), heightScale = Number(tree, "heightScale", 1, 0.01f, 100), rotation = Number(tree, "rotation", 0), color = Color.white, lightmapColor = Color.white }; }).ToArray();
                return () => { Record(terrain); var prototypes = terrain.terrainData.treePrototypes.ToList(); int index = prototypes.FindIndex(p => p.prefab == prefab); if (index < 0) { index = prototypes.Count; prototypes.Add(new TreePrototype { prefab = prefab }); terrain.terrainData.treePrototypes = prototypes.ToArray(); } for (int i = 0; i < trees.Length; i++) trees[i].prototypeIndex = index; terrain.terrainData.treeInstances = terrain.terrainData.treeInstances.Concat(trees).ToArray(); EditorUtility.SetDirty(terrain.terrainData); return Done("added", trees.Length); };
            });
            Add(c, "terrain.details", "Add a grass texture layer with uniform density 0..16 on the terrain's existing detail grid (<=512); a new terrain without details gets a 128 grid.", "target! texture! density:integer", o =>
            {
                var terrain = Component<Terrain>(o);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath(Text(o, "texture"), true));
                if (texture == null)
                    throw new ArgumentException("Texture missing");
                int density = Integer(o, "density", 4, 0, 16);
                int n = terrain.terrainData.detailResolution;
                if (n == 0)
                    n = 128;
                if (n > 512)
                    throw new ArgumentException("Set a detail resolution in 1..512 first");
                return () => { Record(terrain); if (terrain.terrainData.detailResolution == 0) terrain.terrainData.SetDetailResolution(n, 16); var layers = terrain.terrainData.detailPrototypes.ToList(); int index = layers.Count; layers.Add(new DetailPrototype { prototypeTexture = texture, renderMode = DetailRenderMode.GrassBillboard, minWidth = 1, maxWidth = 2, minHeight = 1, maxHeight = 2, healthyColor = Color.white, dryColor = Color.white }); terrain.terrainData.detailPrototypes = layers.ToArray(); var grid = new int[n, n]; for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) grid[y, x] = density; terrain.terrainData.SetDetailLayer(0, 0, index, grid); EditorUtility.SetDirty(terrain.terrainData); return Done("layer", index); };
            });
        }
        private static void Record(Terrain terrain)
        {
            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "lmu3d terrain");
            Undo.RegisterCompleteObjectUndo(terrain.terrainData.alphamapTextures, "lmu3d terrain paint");
        }
    }
}
