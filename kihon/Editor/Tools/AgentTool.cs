using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Lmbox.AIAgent.Tools
{
    /// <summary>
    /// Base class for a tool the agent can invoke.
    /// Implementations run on the Unity main thread and may call any UnityEditor API.
    /// </summary>
    public abstract class AgentTool
    {
        /// <summary>Function name exposed to the model (snake_case recommended).</summary>
        public abstract string Name { get; }

        /// <summary>Natural-language description shown to the model; write it like prompt engineering.</summary>
        public abstract string Description { get; }

        /// <summary>JSON Schema (raw JSON text) for the arguments object. Use an empty object schema if none.</summary>
        public abstract string ParametersSchemaJson { get; }

        /// <summary>
        /// If true, the agent loop asks the user for confirmation before executing.
        /// Set this for anything destructive or hard to undo (delete, overwrite, menu commands).
        /// </summary>
        public virtual bool RequiresConfirmation => false;

        /// <summary>
        /// Executes the tool. Return a compact text result for the model
        /// (it goes straight into the conversation context — keep it short).
        /// Throw on failure; the loop converts exceptions into an "error:" result.
        /// </summary>
        public abstract Task<string> ExecuteAsync(JObject args);
    }
}
