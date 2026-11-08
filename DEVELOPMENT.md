# Unity Web Agent development

The repository root is the `com.lmbox.ai-agent` Unity package and contains the development sources and bundled runtime. Unity projects are kept outside this repository. The local sibling project `../AIAgentDemo/` references this package directly through its Package Manager manifest; rebuilding the Agent updates the runtime used by that project without a second package copy.

The development baseline is Unity 2022.3 LTS. The local demo uses Unity 2022.3.22f1, matching its `ProjectSettings/ProjectVersion.txt`. A fresh clone does not include the demo: use an external Unity project and add this repository's `package.json` through Package Manager. The smoke runner selects the installed editor using that project's version file by default; `UNITY_EDITOR` can override the executable path.

The `Window → AI Agent (Web)` menu starts the service and opens `DockedWebAgentWindow` next to Inspector on macOS. The Web page occupies the tab content directly. Settings and diagnostics are separate `Window → AI Agent Settings` and `Window → AI Agent Logs` entries. Other editor platforms retain the floating unity-webview/browser fallback.

## Execution boundaries

The C# `lmu3d` CLI and implemented command groups are documented in [the CLI guide](WebAgent~/LMU3D-CLI.md). It supports short commands and JSON recipes for project, scene, object, component, terrain, lighting, material, camera, asset and debugging operations. VRChat is excluded.

The existing Web UI sends requests to the Node HTTP host. Rust retains task identity, status, isolated job files and the original five tools. The TypeScript worker owns model calls and the Agent loop. `agent/index.ts` is the core's public entry point; `agent/unity.ts` is the host adapter's entry point. `agent/lib/` stays private. Tests use public entry points or the real HTTP/process protocol.

The independent `UnityBridgeHost` starts the loopback HTTP service when the editor loads the package, without requiring the Web UI. `WebAgentServer` injects this connection into the worker. The worker exposes a single `unity_cli` tool and invokes the bundled C# CLI; the legacy TS tool adapter remains available for older hosts. `UnityToolServer` publishes `ToolRegistry` schemas with a `unity_` prefix and executes them serially from `EditorApplication.update`. No Web task goes through the IMGUI `AgentController`. Credentials are generated per server session and supplied only in worker environment variables; they are not part of the browser or model protocol. Cloud transport variables are cleared for the Unity-hosted process.

Legacy Unity tools (kept for compatibility; the Web AI now uses `unity_cli`): `unity_get_scene_hierarchy`, `unity_create_gameobject`, `unity_read_console_logs`, `unity_write_script`, `unity_list_assets`, `unity_play_mode_control`, `unity_execute_menu_item`. Ordinary `read_file` / `write_file` operate on isolated job files; Unity script writes use `unity_write_script`.

## Build and verification

For frontend hot reload, run `npm run dev` from `WebAgent~/` and open `http://127.0.0.1:5173/`. This reuses the existing sibling `lmbox` checkout's frontend sources, Vite configuration and installed dependencies; it creates no project copy. Set `LMBOX_FRONTEND_SOURCE` to use another source checkout. Vite's cache stays in this package's ignored `node_modules/`. The development host uses [Vite's public JavaScript API](https://vite.dev/guide/api-javascript).

By default the dev host starts the bundled Agent backend, reads the local sibling demo's model settings when present, and uses `../AIAgentDemo/Library/AIAgentWeb/data` relative to the repository root. Set `LMBOX_WEB_DATA` to use another data directory. This standalone mode has no Unity editor tools. To use the running editor backend instead, set `LMBOX_DEV_BACKEND_URL` to its public loopback URL before `npm run dev`. `LMBOX_DEV_PORT` changes the frontend port. Stop with Ctrl+C. Model credentials remain server-side.

There is currently no chat database: frontend chat lists and turns live in Pinia memory. Reloading the page loses that history. The Rust host retains bounded job/conversation state in memory and temporary `ai-*` workspaces; `data/requests/<projectId>/<requestId>` contains empty durable deduplication markers, not messages. These files cannot reconstruct chat history.

From `WebAgent~/`, run:

```sh
npm ci
npm run check
```

The build emits `agent/dist/` from the included TypeScript source. Development dependencies are not needed at runtime. The bundled static frontend includes the responsive history drawer, bottom composer and conversation settings panel. Frontend and Rust source development still uses the sibling `lmbox` checkout described above; this repository ships their built runtime assets. The bundled Rust executable currently targets macOS ARM64; Node.js 24+ is required by the Web package.

Agent requests default to unlimited steps (`maxSteps: 0`). A final response without tool calls ends the run; manual cancellation, request timeouts and the host job deadline remain active. Explicit positive limits from 1 to 32 are supported. Unlimited steps do not guarantee that a model will avoid repetitive tool calls.

With the external test project closed, run from the repository root:

```sh
node Tests~/run-unity-smoke.mjs /absolute/path/to/UnityProject
```

Set `UNITY_EDITOR` to the Unity executable when it is installed elsewhere. The first argument selects a project with this package installed; when omitted, the runner uses the local sibling `../AIAgentDemo/`. The smoke runner temporarily installs its editor harness, uses a local model fixture, exercises the actual HTTP → Rust → TS → Unity chain, checks scene creation, tool replies and reconnection after an assembly reload, and removes its temporary assets. It does not call an external model or change saved model settings. Its Unity log is retained in the system temporary directory.

`npm run lint:boundaries` checks the C# CLI/editor command/host and TS Agent boundaries. Commands expose only `LmuCommands`; its implementation remains internal. The CLI requires .NET 10 and is built in place from `Cli~/Lmu3d.csproj`; override the runtime executable with `LMBOX_DOTNET` if needed.

## Local bridge protocol and lifecycle

The connection descriptor is stored in the selected project's `Library/LmBoxCli/connection.json` (directory 0700/file 0600 on macOS). All endpoints require the session bearer credential and reject browser Origin headers. `GET /tools` returns protocol version 1 and the tool schemas. `POST /execute` accepts `{id, name, arguments}`; the ID is a UUID and the reply is `{ok:true,result}` or `{ok:false,error:{code,message}}`. `DELETE /execute/{id}` cancels queued work. Cancellation cannot undo an operation already performed.

The bridge validates argument types, required properties and enum values. Script writes reject paths outside Assets and linked paths. Script writes and menu operations retain Unity confirmation dialogs; batch mode refuses them. Requests expire after 110 seconds, with a 120-second client timeout; an expired/cancelled request is checked again after confirmation before execution. Tool calls are not automatically retried.

Assembly reload stops pending work and restarts the independent Unity bridge with fresh credentials. CLI request receipts remain in `Library/LmBoxCli/receipts/` and prevent replay when the outcome is unknown. The browser-facing port is retained during the editor session. An interrupted task is not automatically resumed: inspect the scene/files before repeating an edit. A successful script write does not prove compilation succeeded; check Unity's Console after compilation. Floating-webview interaction and interactive approval dialogs are not covered by the headless smoke test.

## macOS docked Web view

`DockedWebAgentWindow` owns the tab and its visibility/lifecycle. It uses Unity's public `GetWindow` docking overload with the Inspector type obtained from editor metadata. `DockedWebView` exposes only create, rectangle update, hide and dispose to C#. The C ABI in `Native~/WebPanel/WebPanel.m` owns AppKit/WebKit implementation details. It attaches a WKWebView as a subview of the Unity NSWindow containing the tab's screen rectangle, rather than creating a separate native window. GUI screen coordinates are converted from Unity's top-left points to Cocoa coordinates. Switching tabs hides the subview; closing or reloading disposes it.

The native bundle is Editor/macOS-only and contains arm64 and x86_64 slices. Rebuild in place on macOS with installed Apple Command Line Tools:

```sh
sh Native~/WebPanel/build.sh
```

Build output stays in `Editor/Plugins/macOS/LmboxWebPanel.bundle`. This display adapter does not change the TS Agent or Unity tool protocol. It only loads the host's loopback HTTP origin; clicked external HTTP(S) links open in the system browser. Source references: [Unity docking API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/EditorWindow.GetWindow.html), [Apple WKWebView](https://developer.apple.com/documentation/webkit/wkwebview).
