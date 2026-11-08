using System.Collections.Generic;
using Lmbox.AIAgent.LLM;

namespace Lmbox.AIAgent.Tools
{
    /// <summary>Registry of all tools available to the agent.</summary>
    public class ToolRegistry
    {
        private readonly List<AgentTool> _tools = new List<AgentTool>();
        private readonly Dictionary<string, AgentTool> _byName = new Dictionary<string, AgentTool>();

        public void Register(AgentTool tool)
        {
            _tools.Add(tool);
            _byName[tool.Name] = tool;
        }

        public AgentTool Find(string name) =>
            name != null && _byName.TryGetValue(name, out var t) ? t : null;

        public IReadOnlyList<AgentTool> All => _tools;

        public List<ToolSpec> BuildSpecs()
        {
            var specs = new List<ToolSpec>(_tools.Count);
            foreach (var t in _tools)
            {
                specs.Add(new ToolSpec
                {
                    Name = t.Name,
                    Description = t.Description,
                    ParametersSchemaJson = t.ParametersSchemaJson
                });
            }
            return specs;
        }

        /// <summary>Creates a registry pre-loaded with the built-in editor tools.</summary>
        public static ToolRegistry CreateDefault()
        {
            var r = new ToolRegistry();
            r.Register(new CliTool());
            r.Register(new GetSceneHierarchyTool());
            r.Register(new CreateGameObjectTool());
            r.Register(new ReadConsoleLogsTool());
            r.Register(new WriteScriptTool());
            r.Register(new ListAssetsTool());
            r.Register(new PlayModeControlTool());
            r.Register(new ExecuteMenuItemTool());
            return r;
        }
    }
}
