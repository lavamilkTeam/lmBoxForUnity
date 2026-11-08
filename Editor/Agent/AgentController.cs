using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lmbox.AIAgent.LLM;
using Lmbox.AIAgent.Settings;
using Lmbox.AIAgent.Tools;
using Newtonsoft.Json.Linq;

namespace Lmbox.AIAgent.Agent
{
    /// <summary>
    /// The agent loop: user message → LLM → (tool calls → execute → feed back)*
    /// until the model answers without tool calls or the iteration cap is hit.
    /// UI-agnostic; the editor window subscribes to its events.
    /// </summary>
    public class AgentController
    {
        public enum UiEventType { User, Assistant, ToolCall, ToolResult, Error, Info }

        public struct UiEvent
        {
            public UiEventType Type;
            public string Title;   // e.g. tool name
            public string Body;    // message text / tool args / tool result
        }

        /// <summary>Raised whenever something should be appended to the chat view.</summary>
        public event Action<UiEvent> OnEvent;

        /// <summary>
        /// Raised before executing a tool with RequiresConfirmation == true.
        /// Must return true to allow execution. Assigned by the UI layer.
        /// </summary>
        public Func<ToolCall, Task<bool>> ConfirmationRequested;

        public bool IsBusy { get; private set; }

        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private readonly ToolRegistry _registry;
        private readonly OpenAIChatClient _client = new OpenAIChatClient();

        public AgentController(ToolRegistry registry)
        {
            _registry = registry ?? ToolRegistry.CreateDefault();
        }

        public void ClearHistory()
        {
            _history.Clear();
            Emit(UiEventType.Info, null, "对话已清空。");
        }

        public async Task SendUserMessageAsync(string text)
        {
            if (IsBusy) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            var settings = AgentSettings.Instance;
            _client.BaseUrl = settings.BaseUrl;
            _client.ApiKey = settings.ApiKey;
            _client.Model = settings.Model;

            IsBusy = true;
            try
            {
                if (_history.Count == 0)
                    _history.Add(ChatMessage.System(settings.SystemPrompt));

                _history.Add(ChatMessage.User(text));
                Emit(UiEventType.User, null, text);

                var specs = _registry.BuildSpecs();

                for (int i = 0; i < settings.MaxToolIterations; i++)
                {
                    ChatMessage reply;
                    try
                    {
                        reply = await _client.SendAsync(_history, specs);
                    }
                    catch (Exception e)
                    {
                        Emit(UiEventType.Error, null, e.Message);
                        return;
                    }

                    _history.Add(reply);

                    if (reply.ToolCalls == null || reply.ToolCalls.Count == 0)
                    {
                        Emit(UiEventType.Assistant, null, reply.Content ?? "(空回复)");
                        return;
                    }

                    if (!string.IsNullOrEmpty(reply.Content))
                        Emit(UiEventType.Assistant, null, reply.Content);

                    foreach (var call in reply.ToolCalls)
                    {
                        Emit(UiEventType.ToolCall, call.Name, call.ArgumentsJson);
                        string result = await ExecuteToolCallAsync(call);
                        _history.Add(ChatMessage.ToolResult(call.Id, result));
                        Emit(UiEventType.ToolResult, call.Name, result);
                    }
                }

                Emit(UiEventType.Info, null,
                    $"已达到最大工具调用轮数（{settings.MaxToolIterations}），停止执行。可在设置中调高。");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<string> ExecuteToolCallAsync(ToolCall call)
        {
            var tool = _registry.Find(call.Name);
            if (tool == null)
                return $"error: 未知工具 {call.Name}";

            if (tool.RequiresConfirmation && ConfirmationRequested != null)
            {
                bool allowed = await ConfirmationRequested.Invoke(call);
                if (!allowed)
                    return "用户拒绝了该操作，未执行。";
            }

            try
            {
                JObject args;
                try
                {
                    args = string.IsNullOrEmpty(call.ArgumentsJson)
                        ? new JObject()
                        : JObject.Parse(call.ArgumentsJson);
                }
                catch (Exception e)
                {
                    return $"error: 工具参数 JSON 解析失败: {e.Message}";
                }

                var result = await tool.ExecuteAsync(args);
                return result ?? "(无返回)";
            }
            catch (Exception e)
            {
                return "error: " + e.Message;
            }
        }

        private void Emit(UiEventType type, string title, string body) =>
            OnEvent?.Invoke(new UiEvent { Type = type, Title = title, Body = body });
    }
}
