using System;
using Lmbox.AIAgent.Settings;
using UnityEditor;
using UnityEngine;

namespace Lmbox.AIAgent.WebAgent
{
    // Auxiliary windows only. The main menu opens the Web page directly.
    public class WebAgentWindow : EditorWindow
    {
        [SerializeField] private int _page; // 0: old control panel, 1: settings, 2: logs
        private Vector2 _logScroll;

        [MenuItem("Window/AI Agent (Web)")]
        public static void Open() => WebAgentLauncher.Open();

        [MenuItem("Window/AI Agent Settings")]
        private static void OpenSettings() => OpenPage(1, "AI Agent Settings");

        [MenuItem("Window/AI Agent Logs")]
        private static void OpenLogs() => OpenPage(2, "AI Agent Logs");

        private static void OpenPage(int page, string title)
        {
            var window = GetWindow<WebAgentWindow>(false, title, true);
            window._page = page;
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(360, 180);
            window.Show();
        }

        private void OnEnable()
        {
            WebAgentServer.OnStateChanged += Repaint;
            // Replace a previously saved control-panel window after recompilation.
            EditorApplication.delayCall += ReplaceOldPanel;
        }

        private void ReplaceOldPanel()
        {
            if (this == null || _page != 0) return;
            Close();
            WebAgentLauncher.Open();
        }

        private void OnDisable()
        {
            WebAgentServer.OnStateChanged -= Repaint;
            EditorApplication.delayCall -= ReplaceOldPanel;
        }

        private void OnGUI()
        {
            if (_page == 1)
            {
                var settings = AgentSettings.Instance;
                EditorGUI.BeginChangeCheck();
                settings.BaseUrl = EditorGUILayout.TextField("Base URL", settings.BaseUrl);
                settings.ApiKey = EditorGUILayout.PasswordField("API Key", settings.ApiKey);
                settings.Model = EditorGUILayout.TextField("Model", settings.Model);
                if (EditorGUI.EndChangeCheck()) settings.Save();
                if (GUILayout.Button("应用并重启")) WebAgentServer.StartOrRestart();
                return;
            }
            if (_page != 2) return;
            EditorGUILayout.LabelField(WebAgentServer.StatusMessage);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重启")) WebAgentServer.StartOrRestart();
            if (GUILayout.Button("停止")) WebAgentServer.Stop();
            EditorGUILayout.EndHorizontal();
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll);
            EditorGUILayout.SelectableLabel(WebAgentServer.LogText,
                new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }

    [InitializeOnLoad]
    internal static class WebAgentLauncher
    {
        private const string RestoreKey = "Lmbox.AIAgent.WebChatRestore";
        private static WebViewBridge _webView;
        private static bool _requested;

        static WebAgentLauncher()
        {
            WebAgentServer.OnStateChanged += OnStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                SessionState.SetBool(RestoreKey, _requested && _webView != null);
                DisposeView();
            };
            EditorApplication.quitting += () =>
            {
                SessionState.SetBool(RestoreKey, false);
                DisposeView();
            };
            if (!Application.isBatchMode && SessionState.GetBool(RestoreKey, false))
                EditorApplication.delayCall += Open;
        }

        public static void Open()
        {
            if (Application.isBatchMode) return;
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                DisposeView();
                _requested = false;
                SessionState.SetBool(RestoreKey, false);
                DockedWebAgentWindow.Open();
                return;
            }
            _requested = true;
            if (WebAgentServer.Current == WebAgentServer.ServerState.Running) ShowWeb();
            else if (WebAgentServer.Current != WebAgentServer.ServerState.Starting) WebAgentServer.StartOrRestart();
        }

        private static void OnStateChanged()
        {
            if (!_requested || Application.isBatchMode) return;
            if (WebAgentServer.Current == WebAgentServer.ServerState.Running) ShowWeb();
            else if (WebAgentServer.Current == WebAgentServer.ServerState.Failed)
                EditorUtility.DisplayDialog("AI Agent 启动失败", WebAgentServer.StatusMessage, "确定");
        }

        private static void ShowWeb()
        {
            try
            {
                if (!WebViewBridge.IsAvailable)
                {
                    Application.OpenURL(WebAgentServer.PublicUrl);
                    _requested = false;
                    return;
                }
                if (_webView == null) _webView = new WebViewBridge();
                _webView.OpenFloatingWindow(WebAgentServer.PublicUrl);
            }
            catch (Exception e)
            {
                DisposeView();
                EditorUtility.DisplayDialog("AI Agent 打开失败", (e.InnerException ?? e).Message, "确定");
            }
        }

        private static void DisposeView()
        {
            _webView?.Dispose();
            _webView = null;
        }
    }
}
