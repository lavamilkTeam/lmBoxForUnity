using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Lmbox.AIAgent.Commands;

namespace Lmbox.AIAgent.Tools
{
    public sealed class CliTool : AgentTool
    {
        private static readonly LmuCommands Commands = new LmuCommands(ConsoleLogCapture.DumpRecent, TerrainPainter.Fill);
        public override string Name => "cli";
        public override string Description => "Execute validated lmu3d editor commands.";
        public override string ParametersSchemaJson => @"{""type"":""object"",""properties"":{""request"":{""type"":""string""}},""required"":[""request""]}";
        public override Task<string> ExecuteAsync(JObject args) => ExecuteAsync(args, null);
        public Task<string> ExecuteAsync(JObject args, System.Func<bool> cancelled) => Task.FromResult(Commands.Execute(JObject.Parse((string)args["request"]), cancelled).ToString(Formatting.None));
    }
}
