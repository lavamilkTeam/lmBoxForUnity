using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Lmbox.AIAgent.WebAgent
{
    /// <summary>
    /// Reflection bridge over gree/unity-webview's WebViewObject (separated mode),
    /// so this package compiles and runs with or without net.gree.unity-webview installed.
    /// Separated mode opens a standalone native WKWebView window; it works in the
    /// editor without entering play mode.
    /// </summary>
    public class WebViewBridge : IDisposable
    {
        public static bool IsAvailable => FindType() != null;

        private static Type FindType()
        {
            // Current releases use a namespace; older releases used a global type.
            foreach (var name in new[] { "Gree.UnityWebView.WebViewObject", "WebViewObject" })
            {
                var t = Type.GetType(name + ", unity-webview");
                if (t != null) return t;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(name);
                    if (t != null) return t;
                }
            }
            return null;
        }

        private GameObject _go;
        private object _instance;
        private Type _type;

        public void OpenFloatingWindow(string url)
        {
            Dispose();
            _type = FindType();
            if (_type == null)
                throw new InvalidOperationException("未安装 net.gree.unity-webview 包。");

            _go = new GameObject("AIAgentWebView") { hideFlags = HideFlags.HideAndDontSave };
            _instance = _go.AddComponent(_type);

            // Init(cb, err, httpErr, ld, started, hooked, cookies, renderProcessGone,
            //      transparent, zoom, ua, radius, androidForceDarkMode,
            //      enableWKWebView, wkContentMode, wkAllowsLinkPreview,
            //      wkAllowsBackForwardNavigationGestures, separated)
            InvokeWithDefaults("Init", ("separated", (object)true));
            Invoke("SetVisibility", true);
            Invoke("LoadURL", url);
        }

        public void Dispose()
        {
            if (_go != null)
            {
                UnityEngine.Object.DestroyImmediate(_go);
                _go = null;
                _instance = null;
            }
        }

        private void Invoke(string name, params object[] args) =>
            _type.GetMethod(name)?.Invoke(_instance, args);

        private void InvokeWithDefaults(string name, params (string param, object value)[] overrides)
        {
            var m = _type.GetMethod(name);
            if (m == null) return;
            var args = m.GetParameters().Select(p =>
            {
                foreach (var (param, value) in overrides)
                    if (p.Name == param) return value;
                return p.HasDefaultValue ? p.DefaultValue : null;
            }).ToArray();
            m.Invoke(_instance, args);
        }
    }
}
