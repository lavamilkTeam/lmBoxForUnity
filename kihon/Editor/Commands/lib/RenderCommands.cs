using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static Lmbox.AIAgent.Commands.Internal.CommandInput;

namespace Lmbox.AIAgent.Commands.Internal
{
    internal static class RenderCommands
    {
        private static string BakeId, BakeState = "idle", BakeError;
        static RenderCommands()
        {
            Lightmapping.bakeCompleted += () => { if (BakeState == "running") BakeState = BakeError == null ? "completed" : "failed"; };
            Application.logMessageReceived += (message, stack, type) => { if (BakeState == "running" && (type == LogType.Error || type == LogType.Exception)) BakeError = message; };
        }
        internal static void Register(Dictionary<string, CommandSpec> c)
        {
            const string light = "target name type color intensity:number range:number spotAngle:number shadows position:object rotation:object mode";
            Add(c, "lighting.add", "Create a Light: Directional/Point/Spot/Area; color #rrggbb; mode Realtime/Mixed/Baked.", light, o => PrepareLight(o, false));
            Add(c, "lighting.apply", "Update target Light or create a new one if target omitted.", light, o => PrepareLight(o, true));
            Add(c, "lighting.bake", "Start asynchronous light baking of saved loaded scenes. Query lighting.status; cancel with lighting.cancel.", "", o =>
            {
                if (Lightmapping.isRunning)
                    throw new ArgumentException("A lighting bake is already running");
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    if (s.isLoaded && (s.isDirty || string.IsNullOrEmpty(s.path)))
                        throw new ArgumentException("Save loaded scenes before baking");
                }
                return () => { BakeId = Guid.NewGuid().ToString(); BakeError = null; BakeState = "running"; if (!Lightmapping.BakeAsync()) { BakeState = "failed"; throw new InvalidOperationException("Unity refused to start light baking; inspect Console and Lighting Settings"); } return BakeStatus(); };
            });
            Add(c, "lighting.status", "Read asynchronous bake progress and completion state.", "", o => () => BakeStatus());
            Add(c, "lighting.cancel", "Cancel the bake started by this editor CLI session.", "", o => () => { if (BakeState != "running") throw new ArgumentException("No CLI-owned bake is running"); BakeState = "cancelled"; Lightmapping.Cancel(); return BakeStatus(); });
            Add(c, "request.status", "Query a lighting bake request ID from this session.", "id!", o => { string id = Text(o, "id"); return () => { if (id != BakeId) throw new ArgumentException("Unknown request ID or editor session changed; inspect lighting.status"); return BakeStatus(); }; });
            Add(c, "request.cancel", "Cancel a lighting bake request ID from this session.", "id!", o => { string id = Text(o, "id"); return () => { if (id != BakeId || BakeState != "running") throw new ArgumentException("No matching running request"); BakeState = "cancelled"; Lightmapping.Cancel(); return BakeStatus(); }; });
            const string camera = "target name position:object rotation:object fov:number near:number far:number background";
            Add(c, "camera.add", "Create a Camera with transform and field of view.", camera, o => PrepareCamera(o, false));
            Add(c, "camera.update", "Update an existing Camera by target.", camera, o => PrepareCamera(o, true));
            Add(c, "scene.capture", "Capture a camera to PNG (Built-in Render Pipeline). Output defaults to Library/LmBoxCli/artifacts; width/height <=2048.", "camera! width:integer height:integer output", o =>
            {
                var cameraComponent = Component<Camera>(o, "camera");
                int width = Integer(o, "width", 640, 16, 2048), height = Integer(o, "height", 360, 16, 2048);
                if (GraphicsSettings.currentRenderPipeline != null)
                    throw new ArgumentException("Camera capture currently requires the Built-in Render Pipeline");
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new ArgumentException("Capture requires a Unity graphics device; do not use -nographics");
                string output = Text(o, "output", "Library/LmBoxCli/artifacts/" + Guid.NewGuid().ToString("N") + ".png");
                string root = Path.GetFullPath("Library/LmBoxCli/artifacts"), full = Path.GetFullPath(output);
                if (output.StartsWith("Assets/", StringComparison.Ordinal))
                    AssetPath(output, false, ".png");
                else if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !full.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Output must be under Assets/ or Library/LmBoxCli/artifacts and end with .png");
                NoLinks(full, Path.GetDirectoryName(Application.dataPath));
                if (File.Exists(full))
                    throw new ArgumentException("Output already exists");
                return () => Capture(cameraComponent, width, height, full);
            });
        }
        private static Func<JObject> PrepareLight(JObject o, bool update)
        {
            Light existing = o["target"] != null ? Component<Light>(o) : null;
            if (!update && existing != null)
                throw new ArgumentException("lighting.add cannot take target");
            string name = Text(o, "name", existing != null ? existing.name : "Light");
            var type = EnumValue(o, "type", existing != null ? existing.type : LightType.Directional);
            Color colour = Colour(o, "color", existing != null ? existing.color : Color.white);
            float intensity = Number(o, "intensity", existing != null ? existing.intensity : 1, 0, 100000), range = Number(o, "range", existing != null ? existing.range : 10, 0.01f, 100000), angle = Number(o, "spotAngle", existing != null ? existing.spotAngle : 30, 1, 179);
            var shadows = EnumValue(o, "shadows", existing != null ? existing.shadows : LightShadows.None);
            var mode = EnumValue(o, "mode", existing != null ? existing.lightmapBakeType : LightmapBakeType.Realtime);
            Vector3 p = Vector(o, "position", existing != null ? existing.transform.position : Vector3.zero), r = Vector(o, "rotation", existing != null ? existing.transform.eulerAngles : new Vector3(50, -30, 0));
            return () => { var light = existing; if (light == null) { var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "lmu3d light"); light = Undo.AddComponent<Light>(go); } Undo.RecordObject(light.gameObject, "lmu3d light name"); light.name = name; Undo.RecordObject(light, "lmu3d light"); light.type = type; light.color = colour; light.intensity = intensity; light.range = range; light.spotAngle = angle; light.shadows = shadows; light.lightmapBakeType = mode; Transform(light.gameObject, p, r, light.transform.localScale); PrefabUtility.RecordPrefabInstancePropertyModifications(light); return Describe(light.gameObject); };
        }
        private static Func<JObject> PrepareCamera(JObject o, bool update)
        {
            var existing = update ? Component<Camera>(o) : null;
            if (!update && o["target"] != null)
                throw new ArgumentException("camera.add cannot take target");
            string name = Text(o, "name", existing != null ? existing.name : "Camera");
            float fov = Number(o, "fov", existing != null ? existing.fieldOfView : 60, 1, 179), near = Number(o, "near", existing != null ? existing.nearClipPlane : 0.3f, 0.001f), far = Number(o, "far", existing != null ? existing.farClipPlane : 1000, near + 0.001f);
            Vector3 p = Vector(o, "position", existing != null ? existing.transform.position : new Vector3(0, 2, -10)), r = Vector(o, "rotation", existing != null ? existing.transform.eulerAngles : Vector3.zero);
            Color background = Colour(o, "background", existing != null ? existing.backgroundColor : Color.black);
            return () => { var camera = existing; if (camera == null) { var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "lmu3d camera"); camera = Undo.AddComponent<Camera>(go); } Undo.RecordObject(camera.gameObject, "lmu3d camera name"); camera.name = name; Undo.RecordObject(camera, "lmu3d camera"); camera.fieldOfView = fov; camera.nearClipPlane = near; camera.farClipPlane = far; camera.backgroundColor = background; if (o["background"] != null) camera.clearFlags = CameraClearFlags.SolidColor; Transform(camera.gameObject, p, r, camera.transform.localScale); PrefabUtility.RecordPrefabInstancePropertyModifications(camera); return Describe(camera.gameObject); };
        }
        private static JObject BakeStatus()
        {
            if (BakeState == "running" && !Lightmapping.isRunning)
                BakeState = BakeError != null ? "failed" : "unknown";
            return new JObject { ["requestId"] = BakeId, ["status"] = BakeId == null && Lightmapping.isRunning ? "external" : BakeState, ["running"] = Lightmapping.isRunning, ["progress"] = Lightmapping.buildProgress, ["error"] = BakeError };
        }
        private static JObject Capture(Camera camera, int width, int height, string full)
        {
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D image = null;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                using (var file = new FileStream(full, FileMode.CreateNew))
                {
                    var bytes = image.EncodeToPNG();
                    file.Write(bytes, 0, bytes.Length);
                }
                if (full.StartsWith(Application.dataPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    AssetDatabase.ImportAsset("Assets/" + full.Substring(Application.dataPath.Length + 1));
                return new JObject { ["path"] = full, ["width"] = width, ["height"] = height, ["camera"] = Ref(camera.gameObject) };
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); if (image != null) UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
