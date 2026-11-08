// read_console_logs：读取捕获到的近期 Unity 日志，支持条数和日志类型筛选。
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Lmbox.AIAgent.Tools
{
    /// <summary>Reads recent Unity Console entries captured by ConsoleLogCapture.</summary>
    public class ReadConsoleLogsTool : AgentTool
    {
        public override string Name => "read_console_logs";
        public override string Description =>
            "读取 Unity Console 最近的日志（含编译错误和运行时异常）。排查问题时优先使用。";

        public override string ParametersSchemaJson => @"{
            ""type"": ""object"",
            ""properties"": {
                ""count"": { ""type"": ""integer"", ""description"": ""返回条数，默认 20"" },
                ""type"": { ""type"": ""string"", ""enum"": [""Log"",""Warning"",""Error"",""Exception""], ""description"": ""按类型过滤，留空返回全部"" }
            }
        }";

        public override Task<string> ExecuteAsync(JObject args)
        {
            int count = args?["count"]?.Value<int?>() ?? 20;
            var type = args?["type"]?.ToString();
            return Task.FromResult(ConsoleLogCapture.DumpRecent(count, type));
        }
    }
}
