// list_assets：按目录和 AssetDatabase 搜索条件查找项目资源，并限制返回条数。
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Lmbox.AIAgent.Tools
{
    /// <summary>Lists assets under a folder via AssetDatabase search.</summary>
    public class ListAssetsTool : AgentTool
    {
        public override string Name => "list_assets";
        public override string Description =>
            "列出项目中的资源文件。可用 AssetDatabase 搜索语法过滤，如 \"t:Script\"、\"t:Prefab Enemy\"。";

        public override string ParametersSchemaJson => @"{
            ""type"": ""object"",
            ""properties"": {
                ""folder"": { ""type"": ""string"", ""description"": ""搜索目录，默认 Assets"" },
                ""filter"": { ""type"": ""string"", ""description"": ""搜索过滤条件，如 t:Prefab，留空列出全部"" },
                ""limit"": { ""type"": ""integer"", ""description"": ""最多返回条数，默认 50"" }
            }
        }";

        public override Task<string> ExecuteAsync(JObject args)
        {
            var folder = args?["folder"]?.ToString();
            var filter = args?["filter"]?.ToString() ?? "";
            int limit = args?["limit"]?.Value<int?>() ?? 50;

            var folders = string.IsNullOrEmpty(folder) ? null : new[] { folder };
            var guids = AssetDatabase.FindAssets(filter, folders);
            var sb = new StringBuilder();
            sb.AppendLine($"共找到 {guids.Length} 个资源" + (guids.Length > limit ? $"，显示前 {limit} 个:" : ":"));
            foreach (var guid in guids.Take(limit))
                sb.AppendLine(AssetDatabase.GUIDToAssetPath(guid));

            return Task.FromResult(sb.ToString());
        }
    }
}
