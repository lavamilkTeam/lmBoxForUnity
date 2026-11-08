using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using Lmbox.AIAgent.Commands.Internal;

namespace Lmbox.AIAgent.Commands
{
    // Public editor command seam. HTTP and tests call this; implementation stays in Internal.
    public sealed class LmuCommands
    {
        private readonly Dictionary<string, CommandSpec> Catalog;
        public LmuCommands(Func<int, string, string> readLogs, Action<UnityEngine.Terrain, int> fillTerrain)
        {
            Catalog = BuildCatalog(readLogs, fillTerrain);
        }
        private static Dictionary<string, CommandSpec> BuildCatalog(Func<int, string, string> readLogs, Action<UnityEngine.Terrain, int> fillTerrain)
        {
            var specs = new Dictionary<string, CommandSpec>(StringComparer.Ordinal);
            SceneCommands.Register(specs, readLogs);
            ObjectCommands.Register(specs);
            TerrainCommands.Register(specs, fillTerrain);
            RenderCommands.Register(specs);
            AssetCommands.Register(specs);
            return specs;
        }
        public JObject Execute(JObject request, Func<bool> cancelled = null)
        {
            CommandInput.Keys(request, "version", "operations", "dryRun");
            if (request.Value<int?>("version") != 1 || !(request["operations"] is JArray ops) || ops.Count == 0 || ops.Count > 64)
                throw new ArgumentException("Expected version 1 and 1..64 operations");
            bool dryRun = CommandInput.Boolean(request, "dryRun", false);
            var prepared = new List<(string id, string op, Func<JObject> run)>();
            var ids = new HashSet<string>();
            foreach (var token in ops)
            {
                if (!(token is JObject input))
                    throw new ArgumentException("Each operation must be an object");
                string op = CommandInput.Text(input, "op");
                string id = CommandInput.Text(input, "id", "op-" + (prepared.Count + 1));
                if (!ids.Add(id))
                    throw new ArgumentException("Duplicate operation id: " + id);
                if (op == "help" || op == "schema")
                {
                    CommandInput.Keys(input, "op", "id", op == "help" ? "group" : "command");
                    string filter = CommandInput.Text(input, op == "help" ? "group" : "command", "");
                    if (op == "schema" && !Catalog.ContainsKey(filter))
                        throw new ArgumentException("Unknown command: " + filter);
                    prepared.Add((id, op, () => op == "schema" ? Catalog[filter].Schema() : new JObject { ["commands"] = new JArray(Catalog.Values.Where(s => filter == "" || s.Name.StartsWith(filter + ".", StringComparison.Ordinal) || s.Name == filter).Select(s => new JObject { ["op"] = s.Name, ["description"] = s.Description })) }));
                    continue;
                }
                if (!Catalog.TryGetValue(op, out var spec))
                    throw new ArgumentException("Unsupported command: " + op + ". Use help or schema.");
                spec.Validate(input);
                prepared.Add((id, op, spec.Prepare(input)));
            }
            if (dryRun)
                return new JObject { ["ok"] = true, ["result"] = new JObject { ["status"] = "validated", ["operations"] = new JArray(prepared.Select(p => new JObject { ["id"] = p.id, ["op"] = p.op })) } };
            if (EditorApplication.isPlaying && prepared.Any(p => !new[] { "inspect", "status", "logs", "objects.list", "components.list", "scene.inspect", "assets.find", "lighting.status", "request.status", "help", "schema" }.Contains(p.op) && !p.op.StartsWith("play.", StringComparison.Ordinal)))
                throw new ArgumentException("Stop Play Mode before editing the scene");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("lmu3d");
            var results = new JArray();
            try
            {
                foreach (var p in prepared)
                {
                    if (cancelled != null && cancelled())
                        return new JObject
                        {
                            ["ok"] = false,
                            ["error"] = new JObject { ["code"] = "cancelled", ["message"] = "Remaining operations cancelled; completed edits were not rolled back." },
                            ["result"] = new JObject { ["completed"] = results, ["remaining"] = new JArray(prepared.Skip(results.Count).Select(x => x.id)) }
                        };
                    try
                    {
                        results.Add(new JObject { ["id"] = p.id, ["op"] = p.op, ["result"] = p.run() });
                    }
                    catch (Exception e)
                    {
                        return new JObject { ["ok"] = false, ["error"] = new JObject { ["code"] = results.Count == 0 ? "operation_failed" : "partial_failure", ["message"] = e.Message }, ["result"] = new JObject { ["status"] = "failed", ["completed"] = results, ["failed"] = p.id, ["remaining"] = new JArray(prepared.Skip(results.Count + 1).Select(x => x.id)), ["note"] = "The failed operation may have partial effects; inspect before retrying." } };
                    }
                }
                return new JObject { ["ok"] = true, ["result"] = new JObject { ["status"] = "completed", ["results"] = results } };
            }
            finally { Undo.CollapseUndoOperations(group); }
        }
    }
}
