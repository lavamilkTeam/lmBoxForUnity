using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Lmbox.AIAgent.Settings
{
    /// <summary>
    /// Persisted plugin settings, stored as JSON under ProjectSettings/
    /// so the API key stays out of Assets/ and (typically) out of version control.
    /// </summary>
    [Serializable]
    public class AgentSettings
    {
        public string BaseUrl = "https://api.openai.com";
        public string ApiKey = "";
        public string Model = "gpt-4o-mini";
        public int MaxToolIterations = 12;

        [TextArea(4, 12)]
        public string SystemPrompt =
            "你是运行在 Unity 编辑器内的 AI 开发助手。你可以通过工具读取场景层级、创建 GameObject、" +
            "读写脚本、查看 Console 日志、控制播放模式。\n" +
            "工作原则：\n" +
            "1. 修改场景前先调用 get_scene_hierarchy 了解现状。\n" +
            "2. 写脚本后立即用 read_console_logs 检查编译错误并修复。\n" +
            "3. 工具结果会自动回到你的上下文，基于真实结果作答，不要臆测。\n" +
            "4. 用中文回答，保持简洁。";

        private const string FilePath = "ProjectSettings/LmboxAIAgentSettings.json";
        private static AgentSettings _instance;

        public static AgentSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        private static AgentSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var s = JsonConvert.DeserializeObject<AgentSettings>(json);
                    if (s != null)
                        return s;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AIAgent] 设置读取失败，使用默认设置: " + e.Message);
            }
            return new AgentSettings();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogError("[AIAgent] 设置保存失败: " + e.Message);
            }
        }
    }
}
