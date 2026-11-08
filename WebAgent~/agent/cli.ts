import { execFile } from 'node:child_process'
import { access, realpath } from 'node:fs/promises'
import { constants } from 'node:fs'
import { homedir } from 'node:os'
import { resolve, isAbsolute } from 'node:path'
import { randomUUID } from 'node:crypto'
import { promisify } from 'node:util'
import { AgentError } from './index.js'
import type { AgentDependencies, ToolReply } from './index.js'

const execute = promisify(execFile)
type Environment = Record<string, string | undefined>

/** AI owns only an argv tool. Parsing and Unity communication are implemented by C# lmu3d. */
export async function connectUnityCli(env: Environment, signal: AbortSignal, workspace: string): Promise<AgentDependencies['hostTools']> {
  if (!env.LMBOX_UNITY_BRIDGE_URL && !env.LMBOX_UNITY_BRIDGE_TOKEN) return undefined
  const origin = new URL(env.LMBOX_UNITY_BRIDGE_URL ?? '')
  const token = env.LMBOX_UNITY_BRIDGE_TOKEN
  const dll = env.LMBOX_UNITY_CLI_DLL
  if (origin.protocol !== 'http:' || origin.hostname !== '127.0.0.1' || !origin.port || origin.pathname !== '/' || origin.search || origin.hash || origin.username || origin.password
    || !token || !/^[a-f0-9]{64}$/.test(token) || !dll || !isAbsolute(dll) || !env.LMBOX_UNITY_PROJECT || !isAbsolute(workspace)) {
    throw new AgentError('invalid_configuration', 'Invalid lmu3d host connection.')
  }
  if (signal.aborted) throw new AgentError('cancelled', 'Agent job cancelled.')
  await access(dll)
  const directory = await realpath(workspace)
  let dotnet: string | undefined
  for (const candidate of [env.LMBOX_DOTNET, resolve(homedir(), '.dotnet/dotnet'), '/usr/local/share/dotnet/dotnet', '/opt/homebrew/bin/dotnet']) {
    if (!candidate) continue
    try { await access(candidate, constants.X_OK); dotnet = candidate; break } catch { /* next installed runtime */ }
  }
  if (!dotnet) throw new AgentError('invalid_configuration', 'lmu3d requires .NET 10. Set LMBOX_DOTNET to the dotnet executable.')
  return {
    tools: [{ name: 'unity_cli', description: 'Run lmu3d in the bound Unity project. Start with help, inspect, or schema <command>. Use --recipe for workspace JSON files.', parameters: { type: 'object', properties: { args: { type: 'array', items: { type: 'string' } } }, required: ['args'], additionalProperties: false } }],
    instructions: '\nUse unity_cli for Unity editing. args is an argument array, never a shell command. Examples: ["inspect"], ["terrain","paint","--recipe","terrain.json"]. '
      + 'Groups: scene, objects, components, terrain, lighting, materials, camera, assets, logs, play, scripts, editor. Scripts and editor menu commands require confirmation in Unity. Ask help <group> then schema <group.action> only as needed. '
      + 'Write recipe files with the file tool in the task workspace. Recipe format: {"version":1,"operations":[{"op":"objects.add","name":"Cube","primitive":"Cube"}]}. '
      + 'Inspect before edits. Commands return compact JSON; baking has its own status/cancel commands. Never replay a mutation after a timeout or partial failure; inspect first. Scene edits are not saved until scene save. VRChat is unsupported.',
    async execute(name, args, toolSignal): Promise<ToolReply> {
      if (name !== 'unity_cli' || Object.keys(args).some(key => key !== 'args') || !Array.isArray(args.args) || args.args.length > 64
        || args.args.some(value => typeof value !== 'string' || value.length > 4096 || value.includes('\0')))
        return { ok: false, error: { code: 'invalid_tool_input', message: 'Expected args: string[] (up to 64 arguments).' } }
      const argv = args.args as string[]
      if (argv.some(value => value === '--request-id' || value.startsWith('--request-id=')))
        return { ok: false, error: { code: 'invalid_tool_input', message: 'Request IDs are assigned by the host.' } }
      if (toolSignal.aborted) return { ok: false, error: { code: 'cancelled', message: 'Agent job cancelled.' } }
      const id = randomUUID()
      const cancel = () => { void fetch(new URL('execute/' + id, origin), { method: 'DELETE', headers: { Authorization: `Bearer ${token}` }, signal: AbortSignal.timeout(2000), redirect: 'error' }).then(r => r.body?.cancel()).catch(() => {}) }
      toolSignal.addEventListener('abort', cancel, { once: true })
      try {
        let stdout: string
        try {
          const result = await execute(dotnet!, [dll, ...argv, '--request-id', id], { cwd: directory, env: { ...env, LMBOX_CLI_WORKSPACE: directory, DOTNET_NOLOGO: '1' }, signal: toolSignal, timeout: 125000, maxBuffer: 2 * 1024 * 1024, encoding: 'utf8' })
          stdout = result.stdout
        } catch (error) {
          if (toolSignal.aborted) { cancel(); return { ok: false, error: { code: 'cancelled', message: 'Cancelled; completed edits are not rolled back.' } } }
          const processError = error as { stdout?: string; killed?: boolean }
          if (processError.killed || !processError.stdout) { cancel(); return { ok: false, error: { code: 'unity_unavailable', message: 'lmu3d disconnected or timed out. Inspect before repeating edits.' } } }
          stdout = processError.stdout
        }
        const reply = JSON.parse(stdout)
        if (!(reply?.ok === true && 'result' in reply || reply?.ok === false && typeof reply.error?.code === 'string' && typeof reply.error?.message === 'string'))
          throw new Error('Invalid CLI reply')
        return reply as ToolReply
      } catch { return { ok: false, error: { code: 'unity_protocol', message: 'lmu3d returned an invalid result. Inspect before repeating edits.' } } }
      finally { toolSignal.removeEventListener('abort', cancel) }
    },
  }
}
