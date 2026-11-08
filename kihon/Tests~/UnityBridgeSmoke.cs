using System;
using System.IO;
using Lmbox.AIAgent.Settings;
using Lmbox.AIAgent.Tools;
using Lmbox.AIAgent.WebAgent;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Copied temporarily into the demo's Assets/Editor by run-unity-smoke.mjs.
public static class LMBoxBridgeSmoke
{
    private static string _ready;
    private static string _stop;
    private static string _reload;
    private static DateTime _deadline;
    public static async void Run()
    {
        try
        {
            _ready = Path.Combine("Library", "LMBoxBridgeSmoke.ready.json");
            _stop = Path.Combine("Library", "LMBoxBridgeSmoke.stop");
            _reload = Path.Combine("Library", "LMBoxBridgeSmoke.reload");
            _deadline = DateTime.UtcNow.AddMinutes(3);
            foreach (var path in new[] { "Assets/../LMBoxSmokeEscape.txt", "Assets/LMBoxSmokeLink/escape.txt" })
            {
                bool rejected = false;
                try { await new WriteScriptTool().ExecuteAsync(new JObject { ["path"] = path, ["content"] = "must not write" }); }
                catch (ArgumentException) { rejected = true; }
                if (!rejected) throw new Exception("Unsafe path accepted: " + path);
            }
            var settings = AgentSettings.Instance;
            settings.BaseUrl = Environment.GetEnvironmentVariable("LMBOX_SMOKE_MODEL_URL");
            settings.Model = "unity-smoke";
            settings.ApiKey = "fixture-only";
            WebAgentServer.OnStateChanged += OnState;
            EditorApplication.update += Tick;
            WebAgentServer.StartOrRestart();
        }
        catch (Exception e) { Fail(e.ToString()); }
    }
    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool("LMBoxBridgeSmoke.Reload", false)) return;
        SessionState.SetBool("LMBoxBridgeSmoke.Reload", false);
        _ready = Path.Combine("Library", "LMBoxBridgeSmoke.ready.json");
        _stop = Path.Combine("Library", "LMBoxBridgeSmoke.stop");
        _reload = Path.Combine("Library", "LMBoxBridgeSmoke.reload");
        _deadline = DateTime.UtcNow.AddMinutes(3);
        var settings = AgentSettings.Instance;
        settings.BaseUrl = Environment.GetEnvironmentVariable("LMBOX_SMOKE_MODEL_URL");
        settings.Model = "unity-smoke";
        settings.ApiKey = "fixture-only";
        WebAgentServer.OnStateChanged += OnState;
        EditorApplication.update += Tick;
    }
    private static void OnState()
    {
        if (WebAgentServer.Current == WebAgentServer.ServerState.Running)
            File.WriteAllText(_ready, new JObject { ["url"] = WebAgentServer.PublicUrl }.ToString());
        if (WebAgentServer.Current == WebAgentServer.ServerState.Failed) Fail(WebAgentServer.LogText);
    }
    private static void Tick()
    {
        if (DateTime.UtcNow > _deadline) { Fail("Smoke test timed out"); return; }
        if (File.Exists(_reload))
        {
            File.Delete(_reload);
            File.Delete(_ready);
            SessionState.SetBool("LMBoxBridgeSmoke.Reload", true);
            EditorUtility.RequestScriptReload();
            return;
        }
        if (!File.Exists(_stop)) return;
        var cube = GameObject.Find("LMBoxBridgeSmokeCube");
        if (cube == null || cube.transform.position != new Vector3(1, 2, 3)) { Fail("Real Unity object was not created at the requested position"); return; }
        if (File.Exists("Assets/LMBoxSmokeDenied.txt")) { Fail("Headless confirmation was bypassed"); return; }
        UnityEngine.Object.DestroyImmediate(cube);
        WebAgentServer.OnStateChanged -= OnState;
        EditorApplication.update -= Tick;
        WebAgentServer.Stop();
        Debug.Log("LMBOX_UNITY_SMOKE_PASSED");
        EditorApplication.Exit(0);
    }
    private static void Fail(string message)
    {
        Debug.LogError("LMBOX_UNITY_SMOKE_FAILED: " + message);
        WebAgentServer.Stop();
        EditorApplication.Exit(1);
    }
}
