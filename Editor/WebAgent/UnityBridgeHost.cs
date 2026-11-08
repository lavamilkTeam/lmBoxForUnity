using System;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Lmbox.AIAgent.WebAgent
{
    // Editor-owned service, independent of Web UI and model credentials.
    [InitializeOnLoad]
    internal static class UnityBridgeHost
    {
        private static UnityToolServer _server;
        internal static UnityToolServer Server { get { if (_server == null) Start(); return _server; } }
        internal static string DirectoryPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "LmBoxCli");
        [DllImport("libc", SetLastError = true)] private static extern int chmod(string path, int mode);
        static UnityBridgeHost()
        {
            EditorApplication.delayCall += () => { try { Start(); } catch (Exception e) { Debug.LogError("lmu3d: " + e.Message); } };
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }
        internal static void SecureDirectory(string path)
        {
            for (string p = path; p != null; p = Path.GetDirectoryName(p))
            {
                try { if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("CLI data path cannot traverse a symbolic link"); } catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
                if (p == Path.GetDirectoryName(Application.dataPath)) break;
            }
            Directory.CreateDirectory(path);
            if (Application.platform != RuntimePlatform.WindowsEditor && chmod(path, 448) != 0) throw new IOException("Cannot restrict CLI directory permissions");
        }
        private static void Start()
        {
            if (_server != null) return; SecureDirectory(DirectoryPath);
            var server = new UnityToolServer();
            try
            {
                string descriptor = Path.Combine(DirectoryPath, "connection.json");
                if (File.Exists(descriptor) && (File.GetAttributes(descriptor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Invalid connection descriptor");
                File.WriteAllText(descriptor, new JObject { ["version"] = 1, ["project"] = Path.GetDirectoryName(Application.dataPath), ["url"] = server.Url, ["token"] = server.Token, ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id }.ToString(Formatting.None));
                if (Application.platform != RuntimePlatform.WindowsEditor && chmod(descriptor, 384) != 0) throw new IOException("Cannot restrict CLI connection permissions");
                _server = server;
            }
            catch { server.Dispose(); throw; }
        }
        private static void Stop()
        {
            if (_server == null) return; _server.Dispose(); _server = null;
            try { File.Delete(Path.Combine(DirectoryPath, "connection.json")); } catch (IOException) { }
        }
    }
}
