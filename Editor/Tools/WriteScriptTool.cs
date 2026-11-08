// write_script：经确认后在 Assets 目录内创建或覆盖脚本等文本资源，并触发 Unity 导入。
using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Lmbox.AIAgent.Tools
{
    /// <summary>Writes (creates or overwrites) a text asset such as a C# script. Requires confirmation.</summary>
    public class WriteScriptTool : AgentTool
    {
        public override string Name => "write_script";
        public override string Description =>
            "创建或覆盖一个文本资源（通常是 .cs 脚本）。路径必须在 Assets/ 下。写入后 Unity 会自动编译，编译错误可用 read_console_logs 查看。此操作会覆盖同名文件。";

        public override bool RequiresConfirmation => true;

        public override string ParametersSchemaJson => @"{
            ""type"": ""object"",
            ""properties"": {
                ""path"": { ""type"": ""string"", ""description"": ""资源路径，必须以 Assets/ 开头，如 Assets/Scripts/Player.cs"" },
                ""content"": { ""type"": ""string"", ""description"": ""完整文件内容"" }
            },
            ""required"": [""path"", ""content""]
        }";

        public override async Task<string> ExecuteAsync(JObject args)
        {
            var path = args?["path"]?.ToString();
            var content = args?["content"]?.ToString();
            if (string.IsNullOrEmpty(path) || content == null)
                throw new ArgumentException("缺少参数 path 或 content");
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("path 必须在 Assets/ 目录下");

            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("path 必须在 Assets/ 目录下，不能通过 .. 越界");
            // Reject linked ancestors/files as well as lexical traversal before writing.
            for (var current = fullPath; current != null; current = Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new ArgumentException("path 不能包含符号链接");
                }
                catch (FileNotFoundException) { /* A new asset is allowed. */ }
                catch (DirectoryNotFoundException) { /* New directories are allowed. */ }
                if (current == assetsRoot) break;
            }
            path = "Assets/" + fullPath.Substring(assetsRoot.Length + 1).Replace('\\', '/');

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            bool existed = File.Exists(path);
            await File.WriteAllTextAsync(path, content);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            return (existed ? "已覆盖: " : "已创建: ") + path +
                   "（Unity 正在编译，如有错误请调用 read_console_logs 查看）";
        }
    }
}
