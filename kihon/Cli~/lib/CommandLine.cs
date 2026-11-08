using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lmu3d;

// CLI implementation: no dependency on Unity assemblies or the AI provider.
internal static class CommandLine
{
    private const int Limit = 2 * 1024 * 1024;
    internal static async Task<int> RunAsync(string[] args)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        try
        {
            var (words, options) = Parse(args);
            string? project = Take(options, "project");
            string? recipe = Take(options, "recipe");
            string? dryRunValue = Take(options, "dry-run");
            if (dryRunValue != null && dryRunValue != "true" && dryRunValue != "false") throw new ArgumentException("dry-run must be true or false");
            bool dryRun = dryRunValue == "true";
            string? requestId = Take(options, "request-id");
            if (requestId != null && !Guid.TryParseExact(requestId, "D", out _)) throw new ArgumentException("request-id must be a UUID");
            if (options.Remove("help")) words = words.Count == 0 ? ["help"] : ["help", words[0]];
            if (words.Count == 0) words.Add("help");
            if (words[0] == "help" && words.Count == 1 && project == null && recipe == null)
            {
                Print(new { ok = true, result = new { command = "lmu3d <group> <action> [--recipe file.json] [--project path]", groups = new[] { "inspect", "status", "scene", "objects", "components", "terrain", "lighting", "materials", "camera", "assets", "logs", "play", "scripts", "editor", "request", "schema" }, hint = "lmu3d help terrain; lmu3d schema terrain.paint" } });
                return 0;
            }
            string op;
            var single = new[] { "inspect", "status", "logs", "apply", "schema", "help" };
            if (single.Contains(words[0]))
            {
                op = words[0];
                if (words.Count > 2 || (words.Count == 2 && op != "schema" && op != "help")) throw new ArgumentException("Unexpected positional argument");
                if (words.Count == 2) options[op == "schema" ? "command" : "group"] = words[1];
            }
            else
            {
                if (words.Count < 2 || words.Count > 3) throw new ArgumentException("Expected <group> <action>");
                op = words[0] + "." + words[1];
                if (words.Count == 3)
                {
                    if (op == "terrain.add") options.Add("count", words[2]);
                    else if (op == "request.status" || op == "request.cancel") options.Add("id", words[2]);
                    else throw new ArgumentException("Unexpected positional argument");
                }
            }
            JsonObject request;
            if (recipe != null)
            {
                if (options.Count != 0) throw new ArgumentException("Recipe cannot be combined with operation arguments");
                string file = SafeRecipe(recipe);
                if (new FileInfo(file).Length > Limit) throw new ArgumentException("Recipe exceeds 2 MiB");
                request = JsonNode.Parse(await File.ReadAllTextAsync(file, cancel.Token)) as JsonObject ?? throw new ArgumentException("Recipe must be a JSON object");
                if (request.Any(p => p.Key != "version" && p.Key != "operations")) throw new ArgumentException("Recipe only accepts version and operations");
                if (request["version"]?.GetValue<int>() != 1 || request["operations"] is not JsonArray operations || operations.Count == 0) throw new ArgumentException("Expected version:1 and nonempty operations");
                if (op != "apply" && operations.Any(item => item?["op"]?.GetValue<string>() != op)) throw new ArgumentException("Recipe operations must match " + op);
            }
            else
            {
                if (op == "apply") throw new ArgumentException("apply requires --recipe");
                var operation = new JsonObject { ["op"] = op };
                foreach (var pair in options) operation[pair.Key == "pos" ? "position" : pair.Key] = Value(pair.Key, pair.Value);
                request = new JsonObject { ["version"] = 1, ["operations"] = new JsonArray(operation) };
            }
            request["dryRun"] = dryRun;
            var (url, token) = await Connection(project, cancel.Token);
            string id = requestId ?? Guid.NewGuid().ToString();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var registration = cancel.Token.Register(() => { _ = CancelRequest(url, token, id); });
            string body = JsonSerializer.Serialize(new { id, name = "unity_cli", arguments = new { request = request.ToJsonString() } });
            if (Encoding.UTF8.GetByteCount(body) > Limit) throw new ArgumentException("Request exceeds 2 MiB");
            using var response = await client.PostAsync(new Uri(url, "execute"), new StringContent(body, Encoding.UTF8, "application/json"), cancel.Token);
            using var stream = await response.Content.ReadAsStreamAsync(cancel.Token);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(chunk, cancel.Token)) != 0)
            {
                if (buffer.Length + count > Limit) throw new IOException("Response exceeds 2 MiB");
                buffer.Write(chunk, 0, count);
            }
            var reply = JsonNode.Parse(buffer.ToArray()) as JsonObject ?? throw new IOException("Invalid host reply");
            if (reply["ok"] is not JsonValue) throw new IOException("Invalid host reply");
            reply["requestId"] = id;
            Console.WriteLine(reply.ToJsonString());
            return reply["ok"]!.GetValue<bool>() ? 0 : 4;
        }
        catch (OperationCanceledException) { PrintError("cancelled", "Cancelled or timed out; an edit may have completed. Inspect before retrying."); return 5; }
        catch (Exception e) { PrintError(e is ArgumentException || e is JsonException || e is FormatException || e is OverflowException ? "invalid_arguments" : "unavailable", e.Message); return 2; }
    }
    private static (List<string>, Dictionary<string, string>) Parse(string[] args)
    {
        var words = new List<string>(); var options = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) { words.Add(arg); continue; }
            string[] pair = arg[2..].Split('=', 2); string key = pair[0]; string value;
            if (pair.Length == 2) value = pair[1];
            else if (key is "help" or "dry-run") value = "true";
            else if (++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal)) value = args[i];
            else throw new ArgumentException("Missing value for --" + key);
            if (!options.TryAdd(key, value)) throw new ArgumentException("Repeated option --" + key);
        }
        return (words, options);
    }
    private static JsonNode Value(string key, string value)
    {
        if (key is "pos" or "position" or "rotation" or "scale" or "size" or "step" or "tileSize")
        {
            var xyz = value.Split(','); if (xyz.Length != 3) throw new ArgumentException(key + " expects x,y,z");
            return new JsonObject { ["x"] = Number(xyz[0]), ["y"] = Number(xyz[1]), ["z"] = Number(xyz[2]) };
        }
        if (key is "count" or "limit" or "cursor" or "width" or "height" or "layer" or "depth" or "resolution" or "seed" or "slot" or "density") return JsonValue.Create(int.Parse(value, CultureInfo.InvariantCulture))!;
        if (key is "intensity" or "range" or "fov" or "strength" or "radius" or "elevation" or "amplitude" or "frequency" or "spotAngle" or "near" or "far") return JsonValue.Create(Number(value))!;
        return JsonValue.Create(value)!;
    }
    private static double Number(string value)
    {
        double number = double.Parse(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number)) throw new ArgumentException("Numbers must be finite");
        return number;
    }
    private static string? Take(Dictionary<string, string> options, string key) => options.Remove(key, out var value) ? value : null;
    private static string SafeRecipe(string path)
    {
        string file = Path.GetFullPath(path);
        string? workspace = Environment.GetEnvironmentVariable("LMBOX_CLI_WORKSPACE");
        if (workspace != null)
        {
            string root = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar);
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ArgumentException("Recipe must be inside the AI task workspace");
            for (var current = new FileInfo(file) as FileSystemInfo; current != null; current = Directory.GetParent(current.FullName))
            {
                if (current.LinkTarget != null) throw new ArgumentException("Recipe cannot traverse symbolic links");
                if (current.FullName == root) break;
            }
        }
        return file;
    }
    private static async Task<(Uri, string)> Connection(string? selected, CancellationToken cancel)
    {
        string? bound = Environment.GetEnvironmentVariable("LMBOX_UNITY_PROJECT");
        if (bound != null && selected != null && Path.GetFullPath(selected) != Path.GetFullPath(bound)) throw new ArgumentException("AI is bound to a different Unity project");
        string? address = Environment.GetEnvironmentVariable("LMBOX_UNITY_BRIDGE_URL");
        string? token = Environment.GetEnvironmentVariable("LMBOX_UNITY_BRIDGE_TOKEN");
        if (bound == null || address == null || token == null)
        {
            string? project = selected;
            if (project == null)
                for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir != null; dir = dir.Parent)
                    if (File.Exists(Path.Combine(dir.FullName, "ProjectSettings/ProjectVersion.txt"))) { project = dir.FullName; break; }
            if (project == null) throw new ArgumentException("Use --project to select an open Unity project");
            var descriptor = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetFullPath(project), "Library/LmBoxCli/connection.json"), cancel))!;
            if (descriptor["project"]?.GetValue<string>() != Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar)) throw new ArgumentException("Connection project mismatch");
            address = descriptor["url"]?.GetValue<string>(); token = descriptor["token"]?.GetValue<string>();
        }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Scheme != "http" || url.Host != "127.0.0.1" || url.AbsolutePath != "/" || url.Query != "" || url.Fragment != "" || url.UserInfo != "" || token == null || token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Invalid Unity connection");
        return (url, token);
    }
    private static async Task CancelRequest(Uri url, string token, string id)
    {
        try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) }; client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); using var response = await client.DeleteAsync(new Uri(url, "execute/" + id)); } catch { }
    }
    private static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
    private static void PrintError(string code, string message) => Print(new { ok = false, error = new { code, message } });
}
