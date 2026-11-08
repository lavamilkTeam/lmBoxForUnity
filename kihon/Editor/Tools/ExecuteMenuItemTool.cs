// execute_menu_item：经确认后按完整菜单路径执行 Unity 编辑器菜单命令。
using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Lmbox.AIAgent.Tools
{
    /// <summary>Executes an arbitrary editor menu item. Powerful but risky — requires confirmation.</summary>
    public class ExecuteMenuItemTool : AgentTool
    {
        public override string Name => "execute_menu_item";
        public override string Description =>
            "执行 Unity 编辑器菜单命令，如 \"Assets/Refresh\" 或 \"File/Save\"。仅在明确知道菜单路径时使用。";

        public override bool RequiresConfirmation => true;

        public override string ParametersSchemaJson => @"{
            ""type"": ""object"",
            ""properties"": {
                ""menuPath"": { ""type"": ""string"", ""description"": ""完整菜单路径，如 'File/Save Project'"" }
            },
            ""required"": [""menuPath""]
        }";

        public override Task<string> ExecuteAsync(JObject args)
        {
            var menuPath = args?["menuPath"]?.ToString();
            if (string.IsNullOrEmpty(menuPath))
                throw new ArgumentException("缺少参数 menuPath");

            bool ok = EditorApplication.ExecuteMenuItem(menuPath);
            return Task.FromResult(ok ? $"已执行菜单: {menuPath}" : $"菜单不存在或执行失败: {menuPath}");
        }
    }
}
