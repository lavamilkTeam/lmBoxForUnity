using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Lmbox.AIAgent.LLM
{
    /// <summary>
    /// Minimal client for any OpenAI-compatible /v1/chat/completions endpoint
    /// (OpenAI, DeepSeek, Moonshot/Kimi, Qwen/DashScope-compatible, Ollama, LM Studio, ...).
    /// Non-streaming for robustness; SSE streaming can be layered on later.
    /// </summary>
    public class OpenAIChatClient
    {
        public string BaseUrl = "https://api.openai.com";
        public string ApiKey = "";
        public string Model = "gpt-4o-mini";
        public int TimeoutSeconds = 120;

        /// <summary>
        /// Sends the full conversation and returns the assistant's reply message
        /// (which may contain tool calls).
        /// </summary>
        public async Task<ChatMessage> SendAsync(List<ChatMessage> messages, List<ToolSpec> tools)
        {
            if (string.IsNullOrEmpty(ApiKey))
                throw new InvalidOperationException("API Key 未配置，请在 AI Agent 窗口的 Settings 中填写。");

            var payload = BuildPayload(messages, tools);
            var url = BaseUrl.TrimEnd('/') + "/v1/chat/completions";

            using (var req = new UnityWebRequest(url, "POST"))
            {
                var body = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + ApiKey);
                req.timeout = TimeoutSeconds;

                var op = req.SendWebRequest();
                while (!op.isDone)
                    await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    var detail = req.downloadHandler != null ? req.downloadHandler.text : "";
                    throw new Exception($"LLM 请求失败: {req.error}\n{Truncate(detail, 500)}");
                }

                return ParseResponse(req.downloadHandler.text);
            }
        }

        private JObject BuildPayload(List<ChatMessage> messages, List<ToolSpec> tools)
        {
            var msgArray = new JArray();
            foreach (var m in messages)
            {
                var j = new JObject { ["role"] = m.Role };
                if (m.Content != null)
                    j["content"] = m.Content;
                if (m.Role == "tool" && m.ToolCallId != null)
                    j["tool_call_id"] = m.ToolCallId;
                if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    var calls = new JArray();
                    foreach (var c in m.ToolCalls)
                    {
                        calls.Add(new JObject
                        {
                            ["id"] = c.Id,
                            ["type"] = "function",
                            ["function"] = new JObject
                            {
                                ["name"] = c.Name,
                                ["arguments"] = c.ArgumentsJson ?? "{}"
                            }
                        });
                    }
                    j["tool_calls"] = calls;
                    // OpenAI requires "content" to be present-or-null for assistant tool-call messages.
                    if (m.Content == null)
                        j["content"] = null;
                }
                msgArray.Add(j);
            }

            var payload = new JObject
            {
                ["model"] = Model,
                ["messages"] = msgArray
            };

            if (tools != null && tools.Count > 0)
            {
                var toolArray = new JArray();
                foreach (var t in tools)
                {
                    toolArray.Add(new JObject
                    {
                        ["type"] = "function",
                        ["function"] = new JObject
                        {
                            ["name"] = t.Name,
                            ["description"] = t.Description,
                            ["parameters"] = JObject.Parse(t.ParametersSchemaJson)
                        }
                    });
                }
                payload["tools"] = toolArray;
                payload["tool_choice"] = "auto";
            }

            return payload;
        }

        private ChatMessage ParseResponse(string json)
        {
            var root = JObject.Parse(json);
            var message = root["choices"]?[0]?["message"];
            if (message == null)
                throw new Exception("LLM 响应格式异常: " + Truncate(json, 500));

            var result = new ChatMessage
            {
                Role = "assistant",
                Content = message["content"]?.Type == JTokenType.String
                    ? message["content"].ToString()
                    : null
            };

            var toolCalls = message["tool_calls"] as JArray;
            if (toolCalls != null && toolCalls.Count > 0)
            {
                result.ToolCalls = new List<ToolCall>();
                foreach (var tc in toolCalls)
                {
                    result.ToolCalls.Add(new ToolCall
                    {
                        Id = tc["id"]?.ToString(),
                        Name = tc["function"]?["name"]?.ToString(),
                        ArgumentsJson = tc["function"]?["arguments"]?.ToString()
                    });
                }
            }

            return result;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "...");
    }
}
