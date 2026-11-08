# lmu3d 使用与模块说明

已实现 C# CLI → 本机 HTTP → Unity 2022 编辑器执行；TS Agent 只向模型暴露一个 `unity_cli` 参数数组工具。VRChat 未接入。当前发布验证环境是 macOS ARM64 / Unity 2022.3.22f1，CLI 需要 .NET 10。

## 启动

先用 Unity 打开装有本包的项目。连接服务随编辑器加载，不需要打开聊天窗口或配置模型。源码和启动脚本在 `Cli~/`，发布的 C# 程序在 `WebAgent~/cli/`。

从包根目录运行：

```sh
./Cli~/lmu3d inspect --project "../AIAgentDemo"
```

要在当前终端直接使用 `lmu3d`，可将这个正式开发目录加入当前会话 PATH（不写入全局配置）：

```sh
export PATH="$PWD/Cli~:$PATH"
```

在 Unity 项目目录内运行时自动选择该项目；在其他目录使用 `--project`。这个选项可以放在子命令之前或之后，不会改变后续调用的默认项目。多开项目时不会任意挑选另一个实例。CLI 不自动启动 Unity。

```sh
lmu3d inspect --project "/path/to/project"
lmu3d terrain paint --recipe terrain.json
lmu3d objects place --recipe garden.json
lmu3d lighting bake
lmu3d scene capture --camera Lakeside
```

`help` 的简短目录可离线读取，分组帮助与 schema 从当前 Unity 版本的执行器读取：

```sh
lmu3d help
lmu3d help terrain
lmu3d terrain --help
lmu3d schema terrain.paint
```

## 已接入功能

| 组 | 命令 | 范围 |
| --- | --- | --- |
| 项目 | `inspect`、`status` | 版本、场景、包、编译错误、编辑器状态 |
| 场景 | `scene inspect/new/open/save/capture` | 新建/打开采用 additive；保存显式执行；相机 PNG 截图 |
| 物体 | `objects list/add/place/update/duplicate/remove` | 包含禁用物体；基础形状、Prefab、父节点、变换、复制、Undo 删除 |
| 组件 | `components list/add/set/remove` | 内置 UnityEngine 组件；可编辑的标量、枚举、Vector3、Color 序列化属性 |
| 地形 | `terrain add/height/layers/paint/trees/details` | 创建、平坦/种子噪声高度、地表层、软圆形笔刷、树木实例、均匀草地密度 |
| 灯光 | `lighting add/apply/bake/status/cancel` | 灯光类型、颜色、强度、阴影、Realtime/Mixed/Baked；异步烘焙 |
| 材质 | `materials create/update/apply/layer` | Shader 颜色/数值/贴图属性、Renderer 插槽、TerrainLayer 资产 |
| 相机 | `camera add/update` | 位置、朝向、FOV、裁剪距离、背景色；截图在 `scene capture` |
| 资源 | `assets find/import/prefab` | 资源搜索、Assets 内已有文件/目录的导入或重新导入、保存新 Prefab |
| 调试 | `logs`、`play start/stop/pause/resume` | 近期日志过滤、编译反馈、运行模式控制 |
| 脚本/菜单 | `scripts write`、`editor menu` | 保留 Unity 内确认；无界面测试模式拒绝执行 |
| 请求 | `request status/cancel <id>` | 查询/取消本次编辑器会话发起的烘焙任务 |

以下是常见用法；先 `inspect` / `objects list` 确认场景中的实际名字，名称不唯一时使用返回的 `ref`。

```sh
lmu3d terrain add 1 --name Ground --pos=0,0,0 --size=100,30,100
lmu3d terrain height --target Ground --kind noise --seed 42 --amplitude 10
lmu3d lighting add --name Sun --type Directional --intensity 1.5 --shadows Soft
lmu3d lighting apply --target Sun --color '#fff1d6'
lmu3d camera add --name Lakeside --pos=0,5,-10 --rotation=15,0,0
lmu3d scene capture --camera Lakeside --width 1280 --height 720
lmu3d objects list --limit 20 --cursor 0
lmu3d components list --target Player
lmu3d assets find --type Prefab --limit 20
lmu3d logs --level error --limit 10
lmu3d scene save --path Assets/Scenes/Lakeside.unity
lmu3d lighting bake
lmu3d lighting status
lmu3d lighting cancel
```

## JSON recipe

统一格式是 `version: 1` 与非空 `operations` 数组。每个操作的 `op` 对应一个命令。未知字段、操作、错误类型都会拒绝。具体字段、必填项和限制用 `schema` 查询。

```json
{
  "version": 1,
  "operations": [
    {
      "id": "trees",
      "op": "objects.place",
      "prefab": "Assets/Prefabs/Tree.prefab",
      "name": "Tree",
      "count": 5,
      "position": {"x": 0, "y": 0, "z": 0},
      "step": {"x": 3, "y": 0, "z": 0}
    }
  ]
}
```

```sh
lmu3d objects place --recipe garden.json --dry-run
lmu3d objects place --recipe garden.json
lmu3d apply --recipe scene.json
```

- 动作命令的配方只能包含对应操作，如 `objects place` 只接受 `objects.place`；混合操作用 `apply`。
- 配方与操作参数不能混用，避免命令行参数暗中覆盖 JSON。全局 `--project`、`--dry-run` 可使用。
- recipe 文件路径相对于终端工作目录；配方中的资源路径统一为 Unity 项目相对的 `Assets/...`，不会相对于 JSON 文件位置解析资源。
- 完整配方先校验再执行。校验时引用的对象/资产必须已存在，不支持在同一批中引用前一操作尚未创建的对象；这种流程拆成两次调用。
- 数组顺序决定执行顺序。操作失败即停止，返回已完成操作、失败项和未执行项；单个失败操作也可能已有部分效果，不保证事务回滚。
- `id` 是文件内操作标签，省略时自动生成 `op-1` 等；不能当作对象 ID 或请求去重键。
- `Cli~/examples/` 有地形、花园和灯光配方。运行前按自己的项目调整对象名、Prefab 路径和图层编号。

组件修改例子（先用 `components list` 查到实际属性路径）：

```json
{
  "version": 1,
  "operations": [
    {
      "op": "components.set",
      "target": "Player",
      "type": "BoxCollider",
      "properties": {"m_IsTrigger": true}
    }
  ]
}
```

## 结果、保存和任务

stdout 始终输出一个 JSON 对象，包含本次传输 `requestId`：成功包含 `ok: true` 和 `result`，失败包含 `ok: false` 与 `error.code/message`；批量部分失败同时返回执行进度。列表最多返回 200 项，支持 `limit/cursor`，并返回 `truncated/nextCursor`。对象引用绑定编辑器会话，脚本重载后重新查询。

场景修改不会自动保存，使用 `scene save`。地形创建会单独生成 `Assets/LmBoxGenerated/Terrains/*.asset`；材质和 Prefab 创建使用明确的 Assets 路径。新资源和截图拒绝覆盖已有文件。截图默认保存在项目 `Library/LmBoxCli/artifacts/`，结果返回文件路径，不把图片编码塞进模型上下文。

烘焙先要求已加载场景保存；启动返回烘焙 request ID，`lighting status` 或 `request status <id>` 查看状态，`lighting cancel` / `request cancel <id>` 取消。提交成功不等于烘焙完成，最终可能为 completed、failed、cancelled 或 unknown。编辑器重载后旧任务身份失效；可用 `lighting status` 查看 Unity 是否仍有外部任务运行。

一般命令有唯一传输 request ID。终端可以显式指定 `--request-id <UUID>`，用于对相同输入去重：同一 ID 与相同输入返回原收据，输入不同则拒绝。AI 宿主自动分配 ID。收据在 `Library/LmBoxCli/receipts/`；重载/中断后未完成的收据返回结果未知，不重新执行。取消会在操作之间停止剩余批次；正在执行的单个同步操作可能完成。超时或取消不意味着修改已回滚，不要换新 ID 盲目重试。

退出码：0 成功，2 参数或连接/运行异常，4 Unity 返回失败，5 取消或超时。失败时优先读取 JSON 中的具体错误码。

## 当前明确限制

- 截图验证的是 Built-in Render Pipeline；URP/HDRP 会明确报告不支持，需要各自的渲染适配器。
- 创建地形最高高度分辨率 1025、单次最多 16 块；绘制支持权重分辨率不超过 1024、图层不超过 16。对象批量放置最多 100 个。
- 树木使用已有 Prefab 和归一化位置；草地目前为整块地形的均匀密度，不支持笔刷遮罩。地形高度支持平坦/种子噪声，未包含高度图导入。
- 组件接口只处理内置 UnityEngine 组件和明确支持的属性类型，不运行任意 C#；脚本写入通过 `scripts write --recipe ...` 接入，仍要求 Unity 内确认；无界面模式拒绝。
- `assets import` 导入已位于 Assets 的资源，不复制任意外部文件。
- `scene open/new` 不丢弃当前场景；默认 additive。Play Mode 中拒绝编辑命令，先 `play stop`。
- CLI 批量是顺序同步执行；烘焙独立异步。一般请求约 110 秒超时，耗时大批次应拆分。

## 模块边界

- `Cli~/Program.cs` 是可执行入口，`Cli~/lib/` 负责参数、recipe、连接、进程 I/O；只依赖 .NET 标准库，不引用 Unity 程序集或 TS Agent。
- `Editor/Commands/LmuCommands.cs` 是 Unity 命令模块公开入口；`Editor/Commands/lib/` 私有。Unity 主线程执行各组操作，构造函数注入已有日志和地形填充能力，避免反向依赖 Tools/Host。
- `Editor/Tools/CliTool.cs` 适配现有工具注册器；`UnityToolServer` 继续承担本机鉴权、排队和取消，`UnityBridgeHost` 提供独立于 Web 的连接生命周期，`CliReceipts` 管理重复请求。
- `WebAgent~/agent/cli.ts` 是 TS 宿主入口，只以固定程序和参数数组调用 C# CLI，不经过 shell。AI recipe 限定在任务工作区内，拒绝路径越界和符号链接；AI 不能切换绑定项目。凭证由宿主注入，不进入模型提示或 JSON 输出。
- C# / TS 边界由 `Tests~/check-boundaries.mjs` 检查，测试通过公开命令或真实 HTTP 接口执行。

## 构建和验证

```sh
npm run check --prefix WebAgent~
python3 Tests~/compile-editor.py
# 外部现有 Demo 必须关闭，测试会清理自己创建的资源，不复制项目。
node Tests~/run-cli-integration.mjs ../AIAgentDemo
node Tests~/run-unity-smoke.mjs ../AIAgentDemo
```

`npm run check` 发布 C# 程序到现有 `WebAgent~/cli/`，编译 TS 并验证 CLI 适配、项目绑定、recipe、取消及原有 Agent 行为。Unity 集成测试验证实际地形、物体、组件、灯光、材质、Prefab、截图和烘焙生命周期；对话测试使用本地模型 fixture 验证完整 Web → Rust → TS → C# CLI → Unity 调用链，不调用云模型。
