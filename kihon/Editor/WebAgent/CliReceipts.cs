using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lmbox.AIAgent.WebAgent
{
    // Durable deduplication: never replay a modification with an uncertain outcome.
    internal sealed class CliReceipts
    {
        private readonly string _directory;
        private readonly object _gate = new object();
        internal CliReceipts() { _directory = Path.Combine(UnityBridgeHost.DirectoryPath, "receipts"); UnityBridgeHost.SecureDirectory(_directory); }
        internal JObject Begin(string id, JObject arguments)
        {
            lock (_gate)
            {
                string path = Path.Combine(_directory, id + ".json"); string hash = Hash(arguments.ToString(Formatting.None));
                if (File.Exists(path))
                {
                    var receipt = JObject.Parse(File.ReadAllText(path));
                    if ((string)receipt["hash"] != hash) throw new ArgumentException("request-id already used with different arguments");
                    return receipt["reply"] as JObject ?? new JObject { ["ok"] = false, ["error"] = new JObject { ["code"] = "outcome_unknown", ["message"] = "Request already accepted; outcome is pending or unknown after reload. Inspect the scene before using a new request ID." } };
                }
                // Exclusive create persists intent before any Unity mutation.
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) using (var writer = new StreamWriter(stream)) writer.Write(new JObject { ["hash"] = hash }.ToString(Formatting.None));
                return null;
            }
        }
        internal void Complete(string id, JObject reply)
        {
            lock (_gate)
            {
                string path = Path.Combine(_directory, id + ".json"); var receipt = JObject.Parse(File.ReadAllText(path)); receipt["reply"] = reply.DeepClone();
                // A crash during writing leaves an unreadable receipt, which fails closed instead of replaying.
                File.WriteAllText(path, receipt.ToString(Formatting.None));
            }
        }
        private static string Hash(string input) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(input))); }
    }
}
