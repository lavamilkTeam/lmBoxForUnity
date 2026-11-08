using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lmbox.AIAgent.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Lmbox.AIAgent.WebAgent
{
    // This is a host adapter, not an Agent loop. All editor operations run from Update.
    internal sealed class UnityToolServer : IDisposable
    {
        private const int Limit = 2 * 1024 * 1024;
        private readonly HttpListener _listener = new HttpListener();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly ConcurrentQueue<Work> _queue = new ConcurrentQueue<Work>();
        private readonly ConcurrentDictionary<string, Work> _requests = new ConcurrentDictionary<string, Work>();
        private readonly ConcurrentDictionary<string, DateTime> _cancelled = new ConcurrentDictionary<string, DateTime>();
        private readonly ToolRegistry _registry = ToolRegistry.CreateDefault();
        private readonly JObject _catalog;
        private readonly CliReceipts _receipts = new CliReceipts();
        private bool _busy;
        private int _pending;
        public string Url { get; }
        public string Token { get; } = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        private sealed class Work
        {
            public string Name;
            public JObject Arguments;
            public DateTime Deadline = DateTime.UtcNow.AddSeconds(110);
            public TaskCompletionSource<JObject> Result = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public UnityToolServer()
        {
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            int port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            Url = $"http://127.0.0.1:{port}/";
            _catalog = new JObject
            {
                ["protocolVersion"] = 1,
                ["tools"] = new JArray(_registry.All.Select(tool => new JObject
                {
                    ["name"] = "unity_" + tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JObject.Parse(tool.ParametersSchemaJson),
                    ["requiresConfirmation"] = tool.RequiresConfirmation
                }))
            };
            _listener.Prefixes.Add(Url);
            try { _listener.Start(); }
            catch { _listener.Close(); throw; }
            EditorApplication.update += Update;
            _ = Task.Run(AcceptLoop);
        }

        public void Dispose()
        {
            if (_lifetime.IsCancellationRequested) return;
            _lifetime.Cancel();
            EditorApplication.update -= Update;
            _listener.Close();
            while (_queue.TryDequeue(out var work))
                work.Result.TrySetResult(Error("unity_stopped", "Unity host stopped before execution."));
        }

        private async Task AcceptLoop()
        {
            while (!_lifetime.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { break; }
                _ = Handle(context);
            }
        }

        private async Task Handle(HttpListenerContext context)
        {
            bool counted = false;
            string registeredId = null;
            bool cliReceipt = false;
            try
            {
                var request = context.Request;
                // No browser origins or CORS. Only the trusted worker receives this credential.
                if (request.Headers["Authorization"] != "Bearer " + Token || request.Headers["Origin"] != null)
                {
                    await Send(context, 401, Error("unauthorized", "Unity host authentication required."));
                    return;
                }
                if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/tools")
                {
                    await Send(context, 200, _catalog);
                    return;
                }
                if (request.HttpMethod == "DELETE" && request.Url.AbsolutePath.StartsWith("/execute/", StringComparison.Ordinal))
                {
                    string id = request.Url.AbsolutePath.Substring(9);
                    if (!Guid.TryParseExact(id, "D", out _) || _cancelled.Count >= 256)
                    {
                        await Send(context, 400, Error("invalid_request", "Invalid cancellation request."));
                        return;
                    }
                    _cancelled[id] = DateTime.UtcNow;
                    if (_requests.TryGetValue(id, out var pending))
                        pending.Result.TrySetResult(Error("cancelled", "Unity request cancelled; an operation already in progress cannot be undone."));
                    await Send(context, 200, new JObject { ["ok"] = true, ["result"] = "Cancellation received." });
                    return;
                }
                if (request.HttpMethod != "POST" || request.Url.AbsolutePath != "/execute")
                {
                    await Send(context, 404, Error("not_found", "Unknown Unity host endpoint."));
                    return;
                }
                counted = true;
                if (Interlocked.Increment(ref _pending) > 32)
                {
                    await Send(context, 429, Error("unity_busy", "Unity host queue is full."));
                    return;
                }
                if (request.ContentType == null || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    await Send(context, 415, Error("invalid_request", "JSON required."));
                    return;
                }
                JObject body;
                using (var memory = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        int read;
                        while ((read = await request.InputStream.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) > 0)
                        {
                            if (memory.Length + read > Limit)
                            {
                                await Send(context, 413, Error("input_limit", "Unity tool input is too large."));
                                return;
                            }
                            memory.Write(buffer, 0, read);
                        }
                    }
                    body = JObject.Parse(new UTF8Encoding(false, true).GetString(memory.ToArray()));
                }
                if (body["name"]?.Type != JTokenType.String || !(body["arguments"] is JObject arguments))
                    throw new ArgumentException("Tool name and object arguments are required.");
                var requestId = (string)body["id"];
                if (!Guid.TryParseExact(requestId, "D", out _)) throw new ArgumentException("A request ID is required.");
                if ((string)body["name"] == "unity_cli")
                {
                    var previous = _receipts.Begin(requestId, arguments);
                    if (previous != null) { await Send(context, 200, previous); return; }
                    cliReceipt = true;
                }
                var work = new Work { Name = (string)body["name"], Arguments = arguments };
                if (!_requests.TryAdd(requestId, work)) throw new ArgumentException("Duplicate request ID.");
                registeredId = requestId;
                if (_cancelled.ContainsKey(requestId))
                    work.Result.TrySetResult(Error("cancelled", "Unity request cancelled before execution."));
                _queue.Enqueue(work);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(110));
                    using (timeout.Token.Register(() => work.Result.TrySetResult(Error("unity_expired", "Unity request expired or host stopped. Inspect before repeating edits."))))
                    {
                        var reply = await work.Result.Task;
                        if (cliReceipt) _receipts.Complete(requestId, reply);
                        await Send(context, 200, reply);
                    }
                }
            }
            catch (Exception e) when (e is JsonException || e is ArgumentException)
            {
                await Send(context, 400, Error("invalid_request", "Invalid Unity tool request."));
            }
            catch { /* Disconnection or editor shutdown. Never repeat an operation. */ }
            finally
            {
                if (counted) Interlocked.Decrement(ref _pending);
                if (registeredId != null) _requests.TryRemove(registeredId, out _);
                try { context.Response.Close(); } catch { }
            }
        }

        private async void Update()
        {
            foreach (var entry in _cancelled)
                if (DateTime.UtcNow - entry.Value > TimeSpan.FromMinutes(3)) _cancelled.TryRemove(entry.Key, out _);
            if (_busy || _lifetime.IsCancellationRequested || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!_queue.TryDequeue(out var work)) return;
            if (Expired(work)) return;
            _busy = true;
            try
            {
                var tool = work.Name.StartsWith("unity_", StringComparison.Ordinal) ? _registry.Find(work.Name.Substring(6)) : null;
                if (tool == null)
                {
                    work.Result.TrySetResult(Error("unknown_tool", "Unknown Unity tool."));
                    return;
                }
                Validate(work.Arguments, JObject.Parse(tool.ParametersSchemaJson), "arguments");
                if (tool.RequiresConfirmation)
                {
                    // Headless sessions cannot consent to destructive editor operations.
                    if (Application.isBatchMode || !EditorUtility.DisplayDialog("Web Agent 请求确认",
                        tool.Name + "\n\n" + work.Arguments.ToString(Formatting.Indented), "允许", "拒绝"))
                    {
                        work.Result.TrySetResult(Error("user_denied", "用户拒绝了该操作，未执行。"));
                        return;
                    }
                }
                if (Expired(work)) return;
                var result = tool is CliTool cli
                    ? await cli.ExecuteAsync(work.Arguments, () => work.Result.Task.IsCompleted || _lifetime.IsCancellationRequested || DateTime.UtcNow >= work.Deadline)
                    : await tool.ExecuteAsync(work.Arguments);
                var reply = work.Name == "unity_cli" ? JObject.Parse(result) : result != null && result.StartsWith("error:", StringComparison.Ordinal)
                    ? Error("unity_tool_failed", result)
                    : new JObject { ["ok"] = true, ["result"] = result ?? "" };
                work.Result.TrySetResult(reply);
            }
            catch (Exception e)
            {
                work.Result.TrySetResult(Error("unity_tool_failed", e.Message));
            }
            finally { _busy = false; }
        }

        private bool Expired(Work work)
        {
            if (!work.Result.Task.IsCompleted && !_lifetime.IsCancellationRequested && DateTime.UtcNow < work.Deadline) return false;
            work.Result.TrySetResult(Error("unity_expired", "Unity request expired before execution."));
            return true;
        }

        // Validate the subset used by AgentTool schemas before touching Unity state.
        private static void Validate(JToken value, JObject schema, string path)
        {
            var type = (string)schema["type"];
            bool valid = type == "object" ? value.Type == JTokenType.Object
                : type == "string" ? value.Type == JTokenType.String
                : type == "integer" ? value.Type == JTokenType.Integer
                : type == "number" ? value.Type == JTokenType.Integer || value.Type == JTokenType.Float
                : type == "boolean" && value.Type == JTokenType.Boolean;
            if (!valid) throw new ArgumentException(path + " has an invalid type.");
            if (schema["enum"] is JArray values && !values.Any(item => JToken.DeepEquals(item, value)))
                throw new ArgumentException(path + " is not an allowed value.");
            if (!(value is JObject obj)) return;
            var properties = schema["properties"] as JObject ?? new JObject();
            if (schema["required"] is JArray required)
                foreach (var key in required)
                    if (obj[(string)key] == null) throw new ArgumentException(path + "." + key + " is required.");
            foreach (var property in obj.Properties())
            {
                if (!(properties[property.Name] is JObject child)) throw new ArgumentException("Unknown argument: " + property.Name);
                Validate(property.Value, child, path + "." + property.Name);
            }
        }

        private static JObject Error(string code, string message) => new JObject
        {
            ["ok"] = false, ["error"] = new JObject { ["code"] = code, ["message"] = message }
        };

        private static async Task Send(HttpListenerContext context, int status, JObject body)
        {
            var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            if (bytes.Length > Limit) bytes = Encoding.UTF8.GetBytes(Error("output_limit", "Unity tool result is too large.").ToString(Formatting.None));
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }
    }
}
