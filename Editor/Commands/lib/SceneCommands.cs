using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Lmbox.AIAgent.Commands.Internal.CommandInput;

namespace Lmbox.AIAgent.Commands.Internal
{
    internal static class SceneCommands
    {
        internal static void Register(Dictionary<string, CommandSpec> c, Func<int, string, string> readLogs)
        {
            Add(c, "inspect", "Read project version, loaded scenes, packages and recent errors.", "", o => () => new JObject
            {
                ["project"] = Path.GetDirectoryName(Application.dataPath),
                ["unityVersion"] = Application.unityVersion,
                ["session"] = Session,
                ["scenes"] = Scenes(),
                ["packages"] = File.Exists("Packages/manifest.json") ? JObject.Parse(File.ReadAllText("Packages/manifest.json"))["dependencies"] : new JObject(),
                ["errors"] = readLogs(10, "Error"),
                ["compileErrors"] = EditorUtility.scriptCompilationFailed
            });
            Add(c, "status", "Read editor availability.", "", o => () => new JObject { ["session"] = Session, ["playing"] = EditorApplication.isPlaying, ["compiling"] = EditorApplication.isCompiling, ["updating"] = EditorApplication.isUpdating, ["baking"] = Lightmapping.isRunning });
            Add(c, "scene.inspect", "List loaded scenes and root objects.", "limit:integer cursor:integer", o => { Integer(o, "limit", 30, 1, 200); Integer(o, "cursor", 0, 0, 1000000); return () => Page(Objects().Where(g => g.transform.parent == null).Select(Describe), o); });
            Add(c, "scene.new", "Create an additive empty scene and make it active.", "name", o => { string name = Text(o, "name", "Untitled"); return () => { var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); scene.name = name; SceneManager.SetActiveScene(scene); return new JObject { ["name"] = scene.name, ["saved"] = false }; }; });
            Add(c, "scene.open", "Open an existing scene additively and make it active.", "path!", o => { string path = AssetPath(Text(o, "path"), true, ".unity"); return () => { var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); SceneManager.SetActiveScene(scene); return new JObject { ["path"] = scene.path }; }; });
            Add(c, "scene.save", "Save active scene; new scenes require an unused Assets/...unity path.", "path", o =>
            {
                var scene = SceneManager.GetActiveScene();
                string path = Text(o, "path", scene.path);
                AssetPath(path, false, ".unity");
                if (path != scene.path)
                    NewAsset(path, ".unity");
                return () => { EnsureParent(path); if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Scene save failed"); return new JObject { ["path"] = path, ["saved"] = true }; };
            });
            Add(c, "logs", "Read recent Console entries. level: Log, Warning, Error, Exception, Assert.", "level limit:integer", o =>
            {
                int limit = Integer(o, "limit", 20, 1, 100);
                string level = Text(o, "level", "");
                if (level != "" && !new[] { "log", "warning", "error", "exception", "assert" }.Contains(level.ToLowerInvariant()))
                    throw new ArgumentException("Invalid level");
                return () => Done("text", readLogs(limit, level));
            });
            Add(c, "scripts.write", "Write a text asset under Assets; requires confirmation inside Unity. Compilation may reload the editor.", "path! content!", o =>
            {
                string path = AssetPath(Text(o, "path"));
                string content = (string)o["content"];
                if (content.Length > 256 * 1024)
                    throw new ArgumentException("Text content exceeds 256 KiB");
                return () => { Confirm("写入 " + path); AssetPath(path); EnsureParent(path); File.WriteAllText(path, content); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); return new JObject { ["path"] = path, ["compilationVerified"] = false }; };
            });
            Add(c, "editor.menu", "Execute a Unity menu item after confirmation inside Unity.", "path!", o =>
            {
                string path = Text(o, "path");
                return () => { Confirm("执行菜单 " + path); if (!EditorApplication.ExecuteMenuItem(path)) throw new InvalidOperationException("Menu item unavailable"); return Done("executed", path); };
            });
            Add(c, "play.resume", "Resume paused Play Mode.", "", o => () => { EditorApplication.isPaused = false; return Done("paused", false); });
            Add(c, "play.start", "Enter Play Mode.", "", o => () => { EditorApplication.isPlaying = true; return Done("playing", true); });
            Add(c, "play.stop", "Exit Play Mode.", "", o => () => { EditorApplication.isPlaying = false; return Done("playing", false); });
            Add(c, "play.pause", "Pause Play Mode.", "", o => () => { EditorApplication.isPaused = true; return Done("paused", true); });
        }
        private static void Confirm(string action)
        {
            if (Application.isBatchMode || !EditorUtility.DisplayDialog("lmu3d 请求确认", action, "允许", "拒绝"))
                throw new InvalidOperationException("User confirmation required; request denied, no operation performed.");
        }
        private static JArray Scenes()
        {
            var a = new JArray();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                a.Add(new JObject { ["name"] = s.name, ["path"] = s.path, ["dirty"] = s.isDirty, ["loaded"] = s.isLoaded });
            }
            return a;
        }
    }
}
