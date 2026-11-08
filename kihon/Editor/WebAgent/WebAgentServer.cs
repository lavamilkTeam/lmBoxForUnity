using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Lmbox.AIAgent.Settings;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Lmbox.AIAgent.WebAgent
{
    /// <summary>
    /// Hosts the bundled lmBox web-agent (site + Node HTTP server + TS agent + Rust CLI)
    /// inside the editor: starts `node server/server.mjs` as a child process with
    /// per-session credentials, then exposes it through a local proxy that injects
    /// the Authorization header so embedded webviews and browsers need no login.
    /// </summary>
    [InitializeOnLoad]
    public static class WebAgentServer
    {
        public enum ServerState { Stopped, Starting, Running, Failed }

        public static ServerState Current { get; private set; } = ServerState.Stopped;
        public static string StatusMessage { get; private set; } = "未启动";
        public static string PublicUrl { get; private set; } = "";

        public static event Action OnStateChanged;

        private const int MaxLogLines = 200;
        private static readonly StringBuilder _log = new StringBuilder();
        private static Process _process;
        private static HttpListener _listener;
        private static HttpClient _client;
        private static int _targetPort;
        private static string _authHeader;
        private static int _generation;

        private const string PidKey = "Lmbox.AIAgent.WebAgentPid";
        private const string RestartKey = "Lmbox.AIAgent.WebAgentRestart";
        private const string ProxyPortKey = "Lmbox.AIAgent.WebAgentProxyPort";

        static WebAgentServer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                bool restart = Current == ServerState.Running || Current == ServerState.Starting;
                Stop();
                SessionState.SetBool(RestartKey, restart);
            };
            EditorApplication.quitting += Stop;
            KillOrphaned();
            if (SessionState.GetBool(RestartKey, false))
                EditorApplication.delayCall += StartOrRestart;
        }

        public static string LogText
        {
            get { lock (_log) return _log.ToString(); }
        }

        // ---------- Lifecycle ----------

        public static async void StartOrRestart()
        {
            Stop();
            int generation = _generation;
            SetState(ServerState.Starting, "正在启动…");
            try
            {
                var node = FindNode();
                if (node == null)
                    throw new Exception("未找到 Node.js（需要 24+）。请安装 Node 或设置环境变量 LMBOX_NODE 指向 node 可执行文件。");

                var packagePath = PackagePath();
                if (packagePath == null)
                    throw new Exception("无法定位 com.lmbox.ai-agent 包路径。");
                var root = Path.Combine(packagePath, "WebAgent~");
                if (!File.Exists(Path.Combine(root, "server", "server.mjs")))
                    throw new Exception("WebAgent~ 资源缺失，请重新导入插件。");

                var settings = AgentSettings.Instance;
                var baseUrl = (settings.BaseUrl ?? "").TrimEnd('/');
                if (!baseUrl.EndsWith("/v1", StringComparison.Ordinal))
                    baseUrl += "/v1";

                _targetPort = FreePort();
                var username = "unity";
                var password = Guid.NewGuid().ToString("N"); // 32 位，满足服务端 ≥16 的要求
                var origin = $"http://127.0.0.1:{_targetPort}";
                _authHeader = "Basic " + Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{username}:{password}"));

                var psi = new ProcessStartInfo(node, "server/server.mjs")
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                var env = psi.Environment;
                // Unity always uses its own local worker, even when the parent shell has cloud settings.
                env.Remove("LMBOX_CONTAINER_ENSURE_URL");
                env.Remove("LMBOX_CONTAINER_SERVICE_TOKEN");
                env.Remove("LMBOX_CLOUD_USER_ID");
                var unityTools = UnityBridgeHost.Server;
                env["LMBOX_UNITY_BRIDGE_URL"] = unityTools.Url;
                env["LMBOX_UNITY_BRIDGE_TOKEN"] = unityTools.Token;
                env["LMBOX_UNITY_PROJECT"] = ProjectRoot();
                env["LMBOX_UNITY_CLI_DLL"] = Path.Combine(root,"cli","lmu3d.dll");
                env["HOST"] = "127.0.0.1";
                env["PORT"] = _targetPort.ToString();
                env["LMBOX_WEB_ORIGIN"] = origin;
                env["LMBOX_WEB_USERNAME"] = username;
                env["LMBOX_WEB_PASSWORD"] = password;
                env["LMBOX_WEB_DATA"] = Path.Combine(ProjectRoot(), "Library", "AIAgentWeb", "data");
                env["LMBOX_AGENT_BASE_URL"] = baseUrl;
                env["LMBOX_AGENT_MODEL"] = settings.Model ?? "";
                env["LMBOX_AGENT_API_KEY"] = settings.ApiKey ?? "";

                _process = Process.Start(psi);
                if (_process == null)
                    throw new Exception("Node 进程启动失败。");
                SessionState.SetInt(PidKey, _process.Id);
                _process.OutputDataReceived += (_, e) => AppendLog(e.Data);
                _process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                bool ready = await WaitReadyAsync(origin, TimeSpan.FromSeconds(15), generation);
                if (generation != _generation) return;
                if (!ready)
                    throw new Exception("服务在 15 秒内未就绪，详见日志。");

                StartProxy(origin);
                SetState(ServerState.Running, $"运行中 · {PublicUrl}");
            }
            catch (Exception e)
            {
                if (generation != _generation) return;
                AppendLog("启动失败: " + e.Message);
                StopProxy();
                StopProcess();
                SetState(ServerState.Failed, e.Message);
            }
        }

        public static void Stop()
        {
            _generation++;
            SessionState.SetBool(RestartKey, false);
            StopProxy();
            StopProcess();
            if (Current != ServerState.Stopped)
                SetState(ServerState.Stopped, "未启动");
        }

        private static void StopProcess()
        {
            SessionState.EraseInt(PidKey);
            var p = _process;
            _process = null;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { /* already gone */ }
            try { p.Dispose(); } catch { }
        }

        private static void StopProxy()
        {
            var l = _listener;
            _listener = null;
            if (l != null) try { l.Stop(); l.Close(); } catch { }
            var c = _client;
            _client = null;
            c?.Dispose();
        }

        private static void KillOrphaned()
        {
            var pid = SessionState.GetInt(PidKey, 0);
            if (pid <= 0) return;
            SessionState.EraseInt(PidKey);
            try
            {
                var p = Process.GetProcessById(pid);
                if (p.ProcessName.IndexOf("node", StringComparison.OrdinalIgnoreCase) >= 0)
                    p.Kill();
            }
            catch { /* process already gone */ }
        }

        // ---------- Auth-injecting proxy ----------

        private static void StartProxy(string targetOrigin)
        {
            var port = SessionState.GetInt(ProxyPortKey, 0);
            if (port == 0) port = FreePort();
            PublicUrl = $"http://127.0.0.1:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(PublicUrl);
            _listener.Start();
            SessionState.SetInt(ProxyPortKey, port);
            _client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            _ = ProxyLoop(targetOrigin, _listener, _client, _authHeader);
        }

        private static async Task ProxyLoop(string targetOrigin, HttpListener listener, HttpClient client, string authHeader)
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { break; }
                _ = HandleRequest(ctx, targetOrigin, client, authHeader);
            }
        }

        private static async Task HandleRequest(HttpListenerContext ctx, string targetOrigin, HttpClient client, string authHeader)
        {
            try
            {
                var req = ctx.Request;
                var target = targetOrigin + req.Url.PathAndQuery;
                using (var msg = new HttpRequestMessage(new HttpMethod(req.HttpMethod), target))
                {
                    if (req.HasEntityBody)
                    {
                        var ms = new MemoryStream();
                        await req.InputStream.CopyToAsync(ms);
                        ms.Position = 0;
                        var content = new StreamContent(ms);
                        if (!string.IsNullOrEmpty(req.ContentType))
                            content.Headers.TryAddWithoutValidation("Content-Type", req.ContentType);
                        msg.Content = content;
                    }
                    msg.Headers.TryAddWithoutValidation("Authorization", authHeader);
                    // 服务端校验 Origin 必须等于目标源，代理统一改写
                    msg.Headers.TryAddWithoutValidation("Origin", targetOrigin);
                    if (req.Headers["sec-fetch-site"] != null)
                        msg.Headers.TryAddWithoutValidation("sec-fetch-site", req.Headers["sec-fetch-site"]);

                    using (var resp = await client.SendAsync(msg, HttpCompletionOption.ResponseContentRead))
                    {
                        ctx.Response.StatusCode = (int)resp.StatusCode;
                        if (resp.Content.Headers.ContentType != null)
                            ctx.Response.ContentType = resp.Content.Headers.ContentType.ToString();
                        var body = await resp.Content.ReadAsByteArrayAsync();
                        ctx.Response.ContentLength64 = body.Length;
                        await ctx.Response.OutputStream.WriteAsync(body, 0, body.Length);
                    }
                }
            }
            catch { /* client disconnected or server gone */ }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
            }
        }

        private static async Task<bool> WaitReadyAsync(string origin, TimeSpan timeout, int generation)
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
            {
                http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", _authHeader);
                var deadline = DateTime.UtcNow + timeout;
                while (DateTime.UtcNow < deadline)
                {
                    if (generation != _generation) return false;
                    if (_process == null || _process.HasExited) return false;
                    try
                    {
                        var resp = await http.GetAsync(origin + "/");
                        if ((int)resp.StatusCode == 200) return true;
                    }
                    catch { /* not ready yet */ }
                    await Task.Delay(200);
                }
            }
            return false;
        }

        // ---------- Helpers ----------

        private static void SetState(ServerState state, string message)
        {
            Current = state;
            StatusMessage = message;
            OnStateChanged?.Invoke();
        }

        private static void AppendLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (_log)
            {
                _log.AppendLine(line);
                // 简单截断：超出后保留尾部
                if (_log.Length > 20000)
                    _log.Remove(0, _log.Length - 16000);
            }
        }

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static string ProjectRoot() =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string PackagePath() =>
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WebAgentServer).Assembly)?.resolvedPath;

        private static string FindNode()
        {
            var overridePath = Environment.GetEnvironmentVariable("LMBOX_NODE");
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
                return overridePath;

            // Unity 从 Hub 启动时 PATH 很短，需手动搜索常见安装位置
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new System.Collections.Generic.List<string>
            {
                "/opt/homebrew/bin/node",
                "/usr/local/bin/node",
                "/usr/bin/node",
                Path.Combine(home, ".volta", "bin", "node"),
                Path.Combine(home, ".asdf", "shims", "node"),
            };
            var nvmDir = Path.Combine(home, ".nvm", "versions", "node");
            if (Directory.Exists(nvmDir))
                candidates.AddRange(Directory.GetDirectories(nvmDir)
                    .OrderByDescending(d => d)
                    .Select(d => Path.Combine(d, "bin", "node")));

            foreach (var c in candidates)
                if (File.Exists(c)) return c;

            // 最后尝试 PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(':'))
            {
                var p = Path.Combine(dir, "node");
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }
}
