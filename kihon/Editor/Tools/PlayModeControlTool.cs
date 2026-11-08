// play_mode_control：控制 Unity 编辑器开始播放、暂停、恢复、停止和单步运行。
using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Lmbox.AIAgent.Tools
{
    /// <summary>Controls editor play mode.</summary>
    public class PlayModeControlTool : AgentTool
    {
        public override string Name => "play_mode_control";
        public override string Description =>
            "控制 Unity 编辑器播放模式：play 进入/继续，pause 暂停，stop 停止，step 单步推进一帧。";

        public override string ParametersSchemaJson => @"{
            ""type"": ""object"",
            ""properties"": {
                ""action"": { ""type"": ""string"", ""enum"": [""play"",""pause"",""unpause"",""stop"",""step""] }
            },
            ""required"": [""action""]
        }";

        public override Task<string> ExecuteAsync(JObject args)
        {
            var action = args?["action"]?.ToString()?.ToLowerInvariant();
            switch (action)
            {
                case "play":
                    EditorApplication.isPaused = false;
                    EditorApplication.isPlaying = true;
                    break;
                case "pause":
                    EditorApplication.isPaused = true;
                    break;
                case "unpause":
                    EditorApplication.isPaused = false;
                    break;
                case "stop":
                    EditorApplication.isPlaying = false;
                    break;
                case "step":
                    EditorApplication.Step();
                    break;
                default:
                    throw new ArgumentException($"未知 action: {action}");
            }
            return Task.FromResult($"已执行 {action}。当前: isPlaying={EditorApplication.isPlaying}, isPaused={EditorApplication.isPaused}");
        }
    }
}
