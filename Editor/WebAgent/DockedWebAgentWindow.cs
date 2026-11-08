using System;
using UnityEditor;
using UnityEngine;

namespace Lmbox.AIAgent.WebAgent
{
    public sealed class DockedWebAgentWindow : EditorWindow
    {
        private DockedWebView _view;
        private string _url;
        private string _error;
        private bool _visible;

        public static void Open()
        {
            // Unity's public docking overload accepts the built-in Inspector type.
            var inspector = typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow");
            var window = inspector != null
                ? GetWindow<DockedWebAgentWindow>("AI Agent", true, inspector)
                : GetWindow<DockedWebAgentWindow>("AI Agent");
            window.minSize = new Vector2(320, 240);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            _visible = true;
            WebAgentServer.OnStateChanged += OnServerChanged;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseView;
            EditorApplication.quitting += ReleaseView;
            EditorApplication.delayCall += EnsureServer;
        }
        private void EnsureServer()
        {
            if (this == null || Application.isBatchMode) return;
            if (WebAgentServer.Current == WebAgentServer.ServerState.Stopped) WebAgentServer.StartOrRestart();
        }
        private void OnDisable()
        {
            WebAgentServer.OnStateChanged -= OnServerChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseView;
            EditorApplication.quitting -= ReleaseView;
            EditorApplication.delayCall -= EnsureServer;
            ReleaseView();
        }
        private void OnBecameVisible() { _visible = true; Repaint(); }
        private void OnBecameInvisible() { _visible = false; _view?.Hide(); }
        private void Update() { if (_visible) Repaint(); }
        private void OnServerChanged()
        {
            if (WebAgentServer.Current != WebAgentServer.ServerState.Running) _view?.Hide();
            if (WebAgentServer.Current == WebAgentServer.ServerState.Running) ReleaseView();
            Repaint();
        }
        private void ReleaseView()
        {
            _view?.Dispose();
            _view = null;
            _url = null;
        }
        private void OnGUI()
        {
            if (WebAgentServer.Current != WebAgentServer.ServerState.Running)
            {
                EditorGUILayout.LabelField(WebAgentServer.StatusMessage);
                if (WebAgentServer.Current == WebAgentServer.ServerState.Failed || WebAgentServer.Current == WebAgentServer.ServerState.Stopped)
                    if (GUILayout.Button("启动")) WebAgentServer.StartOrRestart();
                return;
            }
            if (_error != null)
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
                if (GUILayout.Button("重试")) { _error = null; ReleaseView(); }
                return;
            }
            if (!_visible || Event.current.type != EventType.Repaint) return;
            try
            {
                if (_view == null || _url != WebAgentServer.PublicUrl)
                {
                    ReleaseView();
                    _url = WebAgentServer.PublicUrl;
                    _view = new DockedWebView(_url);
                }
                var origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
                _view.SetRect(new Rect(origin.x, origin.y, position.width, position.height));
            }
            catch (Exception e)
            {
                ReleaseView();
                _error = e.Message;
            }
        }
    }
}
