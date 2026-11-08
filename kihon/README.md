# LMBox AI Agent（Unity 编辑器内置 AI Agent 插件）

插件提供两种形态：

- **AI Agent（Web）**：内嵌 lmBox web-agent 完整栈（网页前端 + Node 服务 + TypeScript Agent + Rust CLI），聊天界面是 lmBox 的 agent 前端原样复用。
- **AI Agent（IMGUI）**：纯 C# 的轻量聊天面板，AI 通过工具调用直接操控 Unity 编辑器。

---

## AI Agent (Web) —— 内嵌 lmBox web-agent

菜单 **Window → AI Agent (Web)** 自动启动服务。macOS 编辑器中，Web 聊天页直接显示在 Inspector 区域的 **AI Agent** 标签页内，可随 Unity 布局停靠和调整大小。

- 面板负责启动/停止内嵌的 `node server/server.mjs`（需要本机 Node.js 24+），每次启动使用随机后端端口和随机 Basic 凭据；面板外置的本地代理自动注入认证头，浏览器/内嵌窗口打开即免登录。
- macOS 使用本包的原生 WKWebView 面板，无需依赖 `unity-webview` 来停靠。其他平台保留 `unity-webview` 独立窗口或系统浏览器回退。
- 模型配置（Base URL / API Key / Model）通过 **Window → AI Agent Settings** 打开，改完点「应用并重启」；密钥保存在 `ProjectSettings/LmboxAIAgentSettings.json`。服务日志及启动/停止操作位于 **Window → AI Agent Logs**。
- Web 的 TS Agent 已接入 Unity 工具，使用 `unity_` 前缀调用下表中的 7 个编辑器工具。C# 仅负责主线程执行和确认弹窗，Web 对话不经过 IMGUI 的 Agent 循环。普通 `read_file` / `write_file` 仍操作隔离任务文件；修改 Unity 脚本使用 `unity_write_script`。
- `WebAgent~/` 包含 `site/` 静态前端、`server/` Node 宿主、`agent/` TS 源码及其 `dist/` 构建产物、`bin/ai` Rust CLI。在该目录执行 `npm ci`、`npm run build` 可重建 Agent。Rust CLI 当前为 darwin-arm64 构建；其他平台需替换对应平台的可执行文件。开发与验证方法见 [DEVELOPMENT.md](DEVELOPMENT.md)。
- 脚本编译或播放模式切换可能触发编辑器重载；服务会重新启动并保留本次编辑器会话的 Web 地址，但中断的任务不会自动重放。请先检查场景、文件和 Console，再决定是否继续操作。

平台范围：原生停靠适配层目前仅支持 macOS Editor。切换 Unity 标签页时网页隐藏，返回 AI Agent 标签页时恢复；关闭标签页或脚本重载时释放原生视图。

---

## AI Agent（IMGUI 版）

类似 VS Code 里 Codex/Copilot 的聊天面板，但住在 Unity 编辑器里：你用自然语言下指令，AI 通过**工具调用（function calling）**直接读取和操控你的 Unity 项目。

- ✅ 开发基线为 **Unity 2022.3 LTS**，演示工程使用 **2022.3.22f1**（IMGUI 界面）
- ✅ 兼容任何 **OpenAI 格式**的 `/v1/chat/completions` 端点：OpenAI、DeepSeek、Kimi（Moonshot）、通义千问兼容模式、Ollama、LM Studio 等
- ✅ 纯 C# 实现，无需 Node.js / Python 等额外进程
- ✅ 危险操作（覆盖文件、执行菜单命令）执行前弹窗确认

## 安装

仓库根目录就是 Unity 插件包，包含 `package.json`、`Editor/` 和 `WebAgent~/`。本地演示工程 `AIAgentDemo/` 位于仓库之外，不随插件提交。

1. 将本仓库检出为 `com.lmbox.ai-agent` 文件夹，放到你的 Unity 工程的 `Packages/` 目录下；
   或者在 Package Manager 中选择 **Add package from disk...**，选中仓库根目录的 `package.json`。
2. Unity 会自动拉取依赖 `com.unity.nuget.newtonsoft-json` 并编译。
3. 菜单栏打开 **Window → AI Agent**（IMGUI 版）或 **Window → AI Agent (Web)**（web-agent 版）。
4. 非 macOS 平台的 Web 版如需 `unity-webview` 独立原生窗口，在 manifest 中加入 `"net.gree.unity-webview": "https://github.com/gree/unity-webview.git?path=/dist/package"`（演示工程已配置）。

## 使用（IMGUI 版）

1. 在窗口顶部 **Settings** 中填入：
   - **Base URL**：如 `https://api.deepseek.com`、`https://api.moonshot.cn`、`http://localhost:11434`（Ollama）
   - **API Key**：对应供应商的密钥（保存在 `ProjectSettings/LmboxAIAgentSettings.json`，不会进 Assets）
   - **Model**：如 `deepseek-chat`、`kimi-k2`、`gpt-4o-mini`
2. 在底部输入框下指令，例如：
   - 「看看当前场景结构」
   - 「创建一个空物体叫 GameManager，然后写一个同名单例脚本挂上去」（挂组件工具见下文扩展）
   - 「看一下 Console 有没有报错，帮我修掉」
   - 「进入播放模式 5 秒后停止」（步进控制）

## 架构

```
UI/AIAgentWindow.cs          IMGUI 聊天窗口（Window > AI Agent，macOS 风格圆角气泡）
UI/DockHelper.cs             窗口自动停靠到 Inspector 旁
WebAgent/WebAgentWindow.cs   Web 直接打开入口；独立设置与日志窗口
WebAgent/WebAgentServer.cs   Node 进程宿主 + 认证注入代理 + 日志
WebAgent/WebViewBridge.cs    unity-webview 反射桥（可选依赖，缺包也能编译）
WebAgent~/                   lmBox web-agent 发布包原样副本（site/server/agent/bin）
Agent/AgentController.cs     Agent 循环：历史管理 + 工具调用往返 + 确认回调
LLM/OpenAIChatClient.cs      OpenAI 兼容协议客户端（非流式，function calling）
LLM/ChatTypes.cs             消息 / 工具调用 / 工具定义模型
Tools/AgentTool.cs           工具基类（名称、描述、JSON Schema、是否需确认）
Tools/ToolRegistry.cs        工具注册表
Tools/GetSceneHierarchyTool.cs 读取场景层级和组件
Tools/CreateGameObjectTool.cs 创建物体和基础几何体
Tools/ReadConsoleLogsTool.cs  读取近期日志
Tools/WriteScriptTool.cs      写入脚本等文本资源
Tools/ListAssetsTool.cs       搜索项目资源
Tools/PlayModeControlTool.cs  控制播放、暂停和单步
Tools/ExecuteMenuItemTool.cs  执行编辑器菜单命令
Tools/ConsoleLogCapture.cs   Console 日志环形缓冲（供 AI 读取）
Settings/AgentSettings.cs    设置持久化（ProjectSettings/*.json）
```

### 内置工具

| 工具 | 作用 | 需确认 |
|---|---|---|
| `get_scene_hierarchy` | 读取当前场景层级树（含组件列表） | 否 |
| `create_gameobject` | 创建 GameObject / 基础几何体（可撤销） | 否 |
| `read_console_logs` | 读取 Console 日志 / 编译错误 | 否 |
| `write_script` | 创建或覆盖 Assets/ 下的脚本等文本资源 | ✅ |
| `list_assets` | 按 AssetDatabase 语法搜索项目资源 | 否 |
| `play_mode_control` | 播放 / 暂停 / 停止 / 单步 | 否 |
| `execute_menu_item` | 执行任意编辑器菜单命令 | ✅ |

### 新增一个工具

继承 `AgentTool`，在 `ToolRegistry.CreateDefault()` 里注册即可：

```csharp
public class MyTool : AgentTool
{
    public override string Name => "my_tool";
    public override string Description => "给模型看的说明，写清楚何时用、参数含义";
    public override string ParametersSchemaJson => @"{ ""type"": ""object"", ""properties"": {} }";
    public override bool RequiresConfirmation => true; // 破坏性操作务必开启
    public override Task<string> ExecuteAsync(JObject args)
    {
        // 直接调用 UnityEditor API，返回简短文本结果给模型
        return Task.FromResult("done");
    }
}
```

## 路线图（下一步可做）

- SSE 流式输出（逐字显示回复）
- `add_component` / `set_property` / `delete_gameobject` 等更多场景工具
- 会话持久化与多会话
- Anthropic Messages API 后端（当前为 OpenAI 兼容协议）
- MCP Server 模式：把工具集反向暴露给外部客户端（Cursor / Claude Code）
- 工具结果的 diff 预览确认（类似 Codex 的变更审批）

## 注意

- 插件仅在编辑器环境编译（`Editor` asmdef），不会进入构建包。
- `write_script` 后 Unity 会触发编译，编译错误通过 `read_console_logs` 形成「写码 → 验证 → 修复」闭环。
- 工具结果直接进入模型上下文，内置实现已做长度截断；自定义工具也请保持返回精简。
