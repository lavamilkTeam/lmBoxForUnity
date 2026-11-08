using System.Collections.Generic;

namespace Lmbox.AIAgent.LLM
{
    /// <summary>
    /// A single message in the conversation history.
    /// Roles: "system", "user", "assistant", "tool".
    /// </summary>
    public class ChatMessage
    {
        public string Role;
        public string Content;

        /// <summary>Tool calls requested by the assistant (role == "assistant").</summary>
        public List<ToolCall> ToolCalls;

        /// <summary>ID of the tool call this message answers (role == "tool").</summary>
        public string ToolCallId;

        public static ChatMessage User(string content) =>
            new ChatMessage { Role = "user", Content = content };

        public static ChatMessage System(string content) =>
            new ChatMessage { Role = "system", Content = content };

        public static ChatMessage ToolResult(string toolCallId, string content) =>
            new ChatMessage { Role = "tool", ToolCallId = toolCallId, Content = content };
    }

    /// <summary>A function call requested by the model.</summary>
    public class ToolCall
    {
        public string Id;
        public string Name;
        public string ArgumentsJson;
    }

    /// <summary>Schema of a tool exposed to the model (OpenAI function-calling format).</summary>
    public class ToolSpec
    {
        public string Name;
        public string Description;

        /// <summary>JSON Schema (as raw JSON text) describing the tool's arguments object.</summary>
        public string ParametersSchemaJson;
    }
}
