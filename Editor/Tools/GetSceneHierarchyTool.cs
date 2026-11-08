// get_scene_hierarchy：读取当前激活场景的物体层级、激活状态和组件列表。
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lmbox.AIAgent.Tools
{
    /// <summary>Reads the active scene hierarchy as an indented text tree.</summary>
    public class GetSceneHierarchyTool : AgentTool
    {
        public override string Name => "get_scene_hierarchy";
        public override string Description =>
            "获取当前激活场景的 GameObject 层级树，包含每个节点的激活状态和组件列表。在修改场景前先调用它以了解现状。";

        public override string ParametersSchemaJson => @"{
            ""type"": ""object"",
            ""properties"": {
                ""maxDepth"": { ""type"": ""integer"", ""description"": ""最大遍历深度，默认 4"" }
            }
        }";

        public override Task<string> ExecuteAsync(JObject args)
        {
            int maxDepth = args?["maxDepth"]?.Value<int?>() ?? 4;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return Task.FromResult("error: 没有激活的场景");

            var sb = new StringBuilder();
            sb.AppendLine($"场景: {scene.name} (路径: {scene.path})");
            foreach (var root in scene.GetRootGameObjects())
                AppendNode(root.transform, 0, maxDepth, sb);

            var text = sb.ToString();
            if (text.Length > 6000)
                text = text.Substring(0, 6000) + "\n...(已截断，请用更小的 maxDepth 分段查看)";
            return Task.FromResult(text);
        }

        private static void AppendNode(Transform t, int depth, int maxDepth, StringBuilder sb)
        {
            if (depth > maxDepth) return;
            var components = t.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform))
                .Select(c => c.GetType().Name);
            var compText = string.Join(",", components);
            sb.Append(' ', depth * 2)
              .Append(t.gameObject.activeSelf ? "● " : "○ ")
              .Append(t.name);
            if (compText.Length > 0)
                sb.Append("  [").Append(compText).Append(']');
            sb.AppendLine();

            foreach (Transform child in t)
                AppendNode(child, depth + 1, maxDepth, sb);
        }
    }
}
