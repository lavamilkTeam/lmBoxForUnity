using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lmbox.AIAgent.Agent;
using Lmbox.AIAgent.LLM;
using Lmbox.AIAgent.Settings;
using Lmbox.AIAgent.Tools;
using UnityEditor;
using UnityEngine;

namespace Lmbox.AIAgent.UI
{
    /// <summary>
    /// Chat window for the AI agent. Menu: Window &gt; AI Agent.
    /// IMGUI-based editor window for Unity 2022.3 LTS.
    /// </summary>
    public class AIAgentWindow : EditorWindow
    {
        [MenuItem("Window/AI Agent")]
        public static void Open()
        {
            var w = GetWindow<AIAgentWindow>(false, "AI Agent", true);
            w.minSize = new Vector2(360, 420);
            w.Show();
            DockHelper.DockNextToInspector(w);
        }

        private class ChatEntry
        {
            public AgentController.UiEvent Event;
            public string Time;
        }

        // Static so the conversation survives window close/reopen and domain reload teardown.
        private static AgentController _controller;
        private static readonly List<ChatEntry> _entries = new List<ChatEntry>();

        private Vector2 _scroll;
        private string _input = "";
        private bool _showSettings;
        private bool _showAdvanced;
        private bool _scrollToBottom;

        private readonly List<Texture2D> _ownedTextures = new List<Texture2D>();

        private GUIStyle _userBubble, _assistantBubble, _toolBubble, _errorBubble, _infoBubble;
        private GUIStyle _headerStyle, _timeStyle, _monoStyle, _inputStyle, _thinkingStyle;

        private void OnEnable()
        {
            EnsureController();
        }

        private void OnDisable()
        {
            foreach (var t in _ownedTextures)
                if (t != null) DestroyImmediate(t);
            _ownedTextures.Clear();
        }

        private static void EnsureController()
        {
            if (_controller != null) return;
            _controller = new AgentController(ToolRegistry.CreateDefault());
            _controller.OnEvent += e =>
            {
                _entries.Add(new ChatEntry { Event = e, Time = DateTime.Now.ToString("HH:mm:ss") });
                foreach (var w in Resources.FindObjectsOfTypeAll<AIAgentWindow>())
                    w._scrollToBottom = true;
                RepaintAll();
            };
            _controller.ConfirmationRequested += ConfirmToolCall;
        }

        private static void RepaintAll()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<AIAgentWindow>())
                w.Repaint();
        }

        private static Task<bool> ConfirmToolCall(ToolCall call)
        {
            bool ok = EditorUtility.DisplayDialog(
                "AI Agent 请求确认",
                $"AI 想要执行可能覆盖文件或改变编辑器状态的操作：\n\n{call.Name}\n{call.ArgumentsJson}\n\n是否允许？",
                "允许", "拒绝");
            return Task.FromResult(ok);
        }

        // ---------- Styles ----------

        private Texture2D MakeRoundedTex(Color c, int size = 32, int radius = 10)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Distance outside the inscribed "safe" rect; > radius means outside the corner circle.
                float cx = Mathf.Clamp(x, radius, size - 1 - radius);
                float cy = Mathf.Clamp(y, radius, size - 1 - radius);
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radius + 0.5f - d); // ~1px antialiasing edge
                px[y * size + x] = new Color(c.r, c.g, c.b, c.a * a);
            }
            t.SetPixels(px);
            t.Apply();
            _ownedTextures.Add(t);
            return t;
        }

        private GUIStyle Bubble(Color bg, Color fg, TextAnchor anchor, int fontSize = 12)
        {
            var s = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                alignment = anchor,
                fontSize = fontSize,
                padding = new RectOffset(12, 12, 8, 8),
                margin = new RectOffset(8, 8, 3, 3),
                border = new RectOffset(14, 14, 14, 14) // 9-slice: corners stay round when scaled
            };
            s.normal.background = MakeRoundedTex(bg);
            s.normal.textColor = fg;
            return s;
        }

        private static Font LoadMonoFont()
        {
            foreach (var name in new[] { "Menlo", "Consolas", "Courier New", "monospace" })
            {
                try
                {
                    var f = Font.CreateDynamicFontFromOSFont(name, 11);
                    if (f != null) return f;
                }
                catch (Exception) { /* try next */ }
            }
            return null; // fall back to the default editor font
        }

        private void EnsureStyles()
        {
            if (_userBubble != null) return;
            bool dark = EditorGUIUtility.isProSkin;
            var fg = dark ? new Color(0.88f, 0.88f, 0.88f) : new Color(0.15f, 0.15f, 0.15f);

            // iMessage 风格：自己 = 蓝底白字，AI = 灰底
            _userBubble = Bubble(new Color(0.20f, 0.52f, 0.96f, 1f), Color.white, TextAnchor.UpperLeft);
            _assistantBubble = Bubble(
                dark ? new Color(0.24f, 0.24f, 0.26f, 1f) : new Color(0.90f, 0.90f, 0.92f, 1f), fg,
                TextAnchor.UpperLeft);
            _errorBubble = Bubble(
                dark ? new Color(0.55f, 0.20f, 0.20f, 0.55f) : new Color(1f, 0.85f, 0.85f, 1f),
                dark ? new Color(1f, 0.75f, 0.75f) : new Color(0.6f, 0.1f, 0.1f),
                TextAnchor.UpperLeft);
            _infoBubble = Bubble(
                new Color(0.5f, 0.5f, 0.5f, 0.15f),
                dark ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.45f, 0.45f, 0.45f),
                TextAnchor.MiddleCenter, 11);

            _toolBubble = Bubble(
                dark ? new Color(0.13f, 0.20f, 0.15f, 0.9f) : new Color(0.88f, 0.95f, 0.88f, 1f),
                dark ? new Color(0.65f, 0.85f, 0.65f) : new Color(0.2f, 0.45f, 0.2f),
                TextAnchor.UpperLeft, 11);
            _toolBubble.font = LoadMonoFont();

            _monoStyle = new GUIStyle(_toolBubble) { fontSize = 11 };
            _monoStyle.margin = new RectOffset(20, 6, 1, 4);

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                margin = new RectOffset(8, 6, 4, 0)
            };
            _timeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                margin = new RectOffset(0, 8, 4, 0)
            };
            _timeStyle.normal.textColor = dark ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.55f, 0.55f, 0.55f);

            _inputStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                fontSize = 12,
                padding = new RectOffset(8, 8, 8, 8)
            };
            _thinkingStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11 };
        }

        // ---------- GUI ----------

        private void OnGUI()
        {
            EnsureStyles();
            var settings = AgentSettings.Instance;

            DrawToolbar(settings);
            if (_showSettings)
                DrawSettings(settings);
            DrawChat();
            DrawInput();
        }

        private void DrawToolbar(AgentSettings settings)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _showSettings = GUILayout.Toggle(_showSettings, "⚙ 设置", EditorStyles.toolbarButton, GUILayout.Width(60));
            GUILayout.FlexibleSpace();
            var status = string.IsNullOrEmpty(settings.ApiKey) ? "⚠ 未配置 API Key" : settings.Model;
            GUILayout.Label(status, EditorStyles.miniLabel);
            GUILayout.Space(8);
            if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(50)))
                _controller.ClearHistory();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettings(AgentSettings settings)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();

            settings.BaseUrl = EditorGUILayout.TextField("Base URL", settings.BaseUrl);
            settings.ApiKey = EditorGUILayout.PasswordField("API Key", settings.ApiKey);
            settings.Model = EditorGUILayout.TextField("Model", settings.Model);

            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "高级", true);
            if (_showAdvanced)
            {
                EditorGUI.indentLevel++;
                settings.MaxToolIterations = EditorGUILayout.IntSlider("最大工具轮数", settings.MaxToolIterations, 1, 30);
                EditorGUILayout.LabelField("System Prompt", EditorStyles.miniBoldLabel);
                settings.SystemPrompt = EditorGUILayout.TextArea(settings.SystemPrompt, GUILayout.MinHeight(80));
                EditorGUI.indentLevel--;
            }

            if (EditorGUI.EndChangeCheck())
                settings.Save();
            EditorGUILayout.EndVertical();
        }

        private void DrawChat()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (_entries.Count == 0)
            {
                GUILayout.Space(12);
                EditorGUILayout.LabelField("试着对我说：", _headerStyle);
                EditorGUILayout.LabelField("「看看当前场景结构」", _infoBubble);
                EditorGUILayout.LabelField("「创建一个空物体叫 GameManager 并写好单例脚本」", _infoBubble);
                EditorGUILayout.LabelField("「看一下 Console 有没有报错，帮我修掉」", _infoBubble);
            }

            foreach (var entry in _entries)
                DrawEntry(entry);

            if (_controller.IsBusy)
                EditorGUILayout.LabelField("AI 正在思考 / 执行工具…", _thinkingStyle);

            EditorGUILayout.EndScrollView();

            if (_scrollToBottom)
            {
                _scroll.y = float.MaxValue;
                _scrollToBottom = false;
                Repaint();
            }
        }

        private void DrawEntry(ChatEntry entry)
        {
            var e = entry.Event;
            switch (e.Type)
            {
                case AgentController.UiEventType.User:
                    DrawHeader("你", entry.Time, null);
                    DrawBubbleRow(e.Body, _userBubble, right: true);
                    break;

                case AgentController.UiEventType.Assistant:
                    DrawHeader("AI", entry.Time, e.Body);
                    DrawBubbleRow(e.Body, _assistantBubble, right: false);
                    break;

                case AgentController.UiEventType.ToolCall:
                    DrawHeader("🔧 " + e.Title, entry.Time, null);
                    EditorGUILayout.LabelField(PrettyJson(e.Body), _monoStyle);
                    break;

                case AgentController.UiEventType.ToolResult:
                    DrawHeader("↩ " + e.Title, entry.Time, e.Body);
                    EditorGUILayout.LabelField(Trim(e.Body, 1200), _monoStyle);
                    break;

                case AgentController.UiEventType.Error:
                    DrawHeader("错误", entry.Time, e.Body);
                    EditorGUILayout.LabelField(e.Body, _errorBubble);
                    break;

                case AgentController.UiEventType.Info:
                    EditorGUILayout.LabelField(e.Body, _infoBubble);
                    break;
            }
        }

        private void DrawBubbleRow(string text, GUIStyle style, bool right)
        {
            EditorGUILayout.BeginHorizontal();
            if (right) GUILayout.FlexibleSpace();
            float maxW = Mathf.Max(160f, position.width * 0.78f);
            GUILayout.Label(text, style, GUILayout.MaxWidth(maxW));
            if (!right) GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawHeader(string label, string time, string copyText)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, _headerStyle, GUILayout.MinWidth(40));
            if (copyText != null && GUILayout.Button("复制", EditorStyles.miniButton, GUILayout.Width(40)))
                EditorGUIUtility.systemCopyBuffer = copyText;
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(time, _timeStyle, GUILayout.Width(52));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInput()
        {
            EditorGUILayout.BeginHorizontal();

            GUI.SetNextControlName("AIAgentInput");
            _input = EditorGUILayout.TextArea(_input, _inputStyle,
                GUILayout.MinHeight(38), GUILayout.MaxHeight(96));

            // Enter 发送，Shift+Enter 换行
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                !Event.current.shift &&
                GUI.GetNameOfFocusedControl() == "AIAgentInput" &&
                !_controller.IsBusy &&
                !string.IsNullOrWhiteSpace(_input))
            {
                Send();
                Event.current.Use();
            }

            GUI.enabled = !_controller.IsBusy && !string.IsNullOrWhiteSpace(_input);
            if (GUILayout.Button("发送", GUILayout.Width(56), GUILayout.MinHeight(38)))
                Send();
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Enter 发送 · Shift+Enter 换行", EditorStyles.miniLabel);
        }

        private void Send()
        {
            var text = _input.Trim();
            _input = "";
            GUI.FocusControl("AIAgentInput");
            // Fire-and-forget is fine in the editor: continuations resume on the main thread.
            _ = _controller.SendUserMessageAsync(text);
        }

        private static string Trim(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "\n…(已截断)");

        private static string PrettyJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                var token = Newtonsoft.Json.Linq.JToken.Parse(s);
                return token.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch
            {
                return s;
            }
        }
    }
}
