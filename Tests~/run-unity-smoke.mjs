import { createServer } from 'node:http'
import { once } from 'node:events'
import { spawn } from 'node:child_process'
import { mkdtemp, mkdir, copyFile, readFile, writeFile, rm, symlink, lstat, rmdir } from 'node:fs/promises'
import { dirname, resolve, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import assert from 'node:assert/strict'

const here = dirname(fileURLToPath(import.meta.url))
const project = resolve(process.argv[2] || join(here, '../../AIAgentDemo'))
const versionFile = await readFile(join(project, 'ProjectSettings/ProjectVersion.txt'), 'utf8')
const editorVersion = /^m_EditorVersion:\s*(\S+)\s*$/m.exec(versionFile)?.[1]
if (!editorVersion) throw new Error('ProjectVersion.txt does not specify a Unity editor version')
const unity = process.env.UNITY_EDITOR || `/Applications/Unity/Hub/Editor/${editorVersion}/Unity.app/Contents/MacOS/Unity`
await lstat(unity)
const temp = await mkdtemp(join(project, 'Library/lmbox-unity-smoke-'))
const editor = join(project, 'Assets/Editor')
const script = join(editor, 'LMBoxBridgeSmoke.cs')
const link = join(project, 'Assets/LMBoxSmokeLink')
const ready = join(project, 'Library/LMBoxBridgeSmoke.ready.json')
const stop = join(project, 'Library/LMBoxBridgeSmoke.stop')
const reload = join(project, 'Library/LMBoxBridgeSmoke.reload')
let child, exit, fixtureError, turns = 0, ownsAssets = false, createdEditor = false
const checked = new Set()
const model = createServer(async (req, res) => {
  try {
    res.setHeader('Content-Type', 'application/json')
    if (req.url === '/v1/models') { res.end(JSON.stringify({ data: [{ id: 'unity-smoke' }] })); return }
    let body = ''; for await (const chunk of req) body += chunk
    const input = JSON.parse(body)
    turns++
    assert.deepEqual(input.tools.filter(tool => tool.function.name.startsWith('unity_')).map(tool=>tool.function.name), ['unity_cli'])
    assert.ok(!body.includes('LMBOX_UNITY_BRIDGE_TOKEN'))
    const replies = input.messages.filter(message => message.role === 'tool')
    const afterReload = input.messages.some(message => message.role === 'user' && message.content === 'After reload: read scene')
    let message
    if (!replies.length) {
      const calls = afterReload ? [['reload', 'unity_cli', { args: ['objects','list','--name','LMBoxBridgeSmokeCube'] }]] : [
        ['create', 'unity_cli', { args: ['objects','add','--name','LMBoxBridgeSmokeCube','--primitive','Cube','--pos=1,2,3'] }],
        ['hierarchy', 'unity_cli', { args: ['objects','list','--name','LMBoxBridgeSmokeCube'] }],
        ['deny', 'unity_cli', { args: ['inspect','--project','/another/project'] }],
        ['invalid', 'unity_cli', { args: ['objects','add','--name','MustNotExist','--pos=wrong-type,0,0'] }],
        ['rust', 'calculate', { expression: '6*7' }],
      ]
      message = { role: 'assistant', content: null, tool_calls: calls.map(([id, name, args]) => ({ id, type: 'function', function: { name, arguments: JSON.stringify(args) } })) }
    } else {
      assert.equal(replies.length, afterReload ? 1 : 5)
      for (const reply of replies) {
        const data = JSON.parse(reply.content)
        checked.add(reply.tool_call_id)
        if (reply.tool_call_id === 'create') { assert.equal(data.ok, true); assert.match(JSON.stringify(data.result), /LMBoxBridgeSmokeCube/) }
        if (reply.tool_call_id === 'hierarchy') { assert.equal(data.ok, true); assert.match(JSON.stringify(data.result), /LMBoxBridgeSmokeCube/) }
        if (reply.tool_call_id === 'reload') { assert.equal(data.ok, true); assert.match(JSON.stringify(data.result), /LMBoxBridgeSmokeCube/) }
        if (reply.tool_call_id === 'deny') assert.equal(data.error.code, 'invalid_arguments')
        if (reply.tool_call_id === 'invalid') assert.equal(data.ok, false)
        if (reply.tool_call_id === 'rust') { assert.equal(data.ok, true); assert.match(JSON.stringify(data.result), /42/) }
      }
      message = { role: 'assistant', content: 'Unity bridge smoke completed' }
    }
    res.end(JSON.stringify({ choices: [{ message, finish_reason: replies.length ? 'stop' : 'tool_calls' }] }))
  } catch (error) { fixtureError = error; res.statusCode = 500; res.end('{}') }
})
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms))
try {
  for (const path of [script, script + '.meta', link, link + '.meta', ready, stop, reload, join(project, 'Temp/UnityLockfile')]) {
    let exists = false; try { await lstat(path); exists = true } catch (error) { if (error.code !== 'ENOENT') throw error }
    if (exists) throw new Error(`Smoke test refuses to overwrite ${path}`)
  }
  try { await lstat(editor) } catch (error) { if (error.code !== 'ENOENT') throw error; createdEditor = true }
  ownsAssets = true
  await mkdir(editor, { recursive: true })
  await copyFile(join(here, 'UnityBridgeSmoke.cs'), script)
  const linkTarget = join(temp, 'link-target')
  await mkdir(linkTarget)
  // Unity prunes empty imported folders, including empty directory links.
  await writeFile(join(linkTarget, 'sentinel.txt'), 'Unity bridge symlink fixture')
  await symlink(linkTarget, link)
  model.listen(0, '127.0.0.1'); await once(model, 'listening')
  child = spawn(unity, ['-batchmode', '-nographics', '-projectPath', project, '-executeMethod', 'LMBoxBridgeSmoke.Run', '-logFile', join(temp, 'Unity.log')], {
    stdio: ['ignore', 'ignore', 'pipe'], env: { ...process.env, LMBOX_SMOKE_MODEL_URL: `http://127.0.0.1:${model.address().port}/v1` },
  })
  child.stderr.resume()
  exit = once(child, 'exit')
  let origin
  for (let i = 0; i < 600; i++) {
    if (child.exitCode !== null) throw new Error(`Unity exited early (${child.exitCode})`)
    try { origin = JSON.parse(await readFile(ready, 'utf8')).url; break } catch {}
    await sleep(500)
  }
  if (!origin) throw new Error('Unity readiness timeout')
  console.log('Unity compiled and Web host started')
  const initial = { schemaVersion: 1, projectId: `unity-smoke-${Date.now()}`, requestId: 'start', inputRevision: 1, capability: 'agent', operation: 'start', payload: { prompt: 'Exercise Unity and Rust tools', maxSteps: 3, files: [] } }
  async function request(body) {
    const response = await fetch(new URL('api/ai', origin), { method: 'POST', headers: { 'Content-Type': 'application/json', Origin: new URL(origin).origin }, body: JSON.stringify(body), signal: AbortSignal.timeout(60000) })
    assert.equal(response.status, 200)
    const reply = await response.json(); assert.equal(reply.ok, true, JSON.stringify(reply)); return reply.data
  }
  const inspection = await request({ ...initial, requestId: 'inspect', operation: 'inspect', payload: {} })
  assert.deepEqual(inspection.tools.filter(name => name.startsWith('unity_')), ['unity_cli'])
  let job = (await request(initial)).job
  for (let i = 0; i < 300 && job.status === 'running'; i++) {
    await sleep(200)
    job = (await request({ ...initial, requestId: `status-${i}`, operation: 'status', jobId: job.jobId, payload: {} })).job
  }
  if (fixtureError) throw fixtureError
  assert.equal(job.status, 'succeeded', JSON.stringify(job))
  assert.equal(job.result.text, 'Unity bridge smoke completed')
  assert.equal(turns, 2); assert.equal(checked.size, 5)
  await rm(ready)
  await writeFile(reload, 'reload')
  let resumed = false
  for (let i = 0; i < 120; i++) {
    if (child.exitCode !== null) throw new Error('Unity exited during reload')
    try { const next = JSON.parse(await readFile(ready, 'utf8')); assert.equal(next.url, origin); resumed = true; break } catch {}
    await sleep(500)
  }
  assert.equal(resumed, true, 'Web host did not restart after assembly reload')
  const resumedRequest = { ...initial, requestId: 'after-reload', payload: { prompt: 'After reload: read scene', maxSteps: 3, files: [] } }
  job = (await request(resumedRequest)).job
  for (let i = 0; i < 300 && job.status === 'running'; i++) {
    await sleep(200)
    job = (await request({ ...initial, requestId: `reload-status-${i}`, operation: 'status', jobId: job.jobId, payload: {} })).job
  }
  if (fixtureError) throw fixtureError
  assert.equal(job.status, 'succeeded', JSON.stringify(job)); assert.ok(checked.has('reload'))
  console.log('Assembly reload recovered the same Web URL and fresh Unity bridge connection')
  await writeFile(stop, 'done')
  const [code] = await Promise.race([exit, sleep(15000).then(() => { throw new Error('Unity shutdown timeout') })])
  assert.equal(code, 0)
  assert.match(await readFile(join(temp, 'Unity.log'), 'utf8'), /LMBOX_UNITY_SMOKE_PASSED/)
  console.log('PASS: Web HTTP → Rust → TS Agent → C# CLI → C# Unity tools → TS model result; actual scene mutation, schema rejection, bound project enforcement, path traversal and symlink rejection, original Rust tools')
} catch (error) {
  console.error(`Unity log: ${join(temp, 'Unity.log')}`)
  throw error
} finally {
  if (child && child.exitCode === null) { child.kill('SIGTERM'); await Promise.race([exit, sleep(5000)]) }
  model.closeAllConnections(); await new Promise(resolve => model.close(resolve))
  // Remove only this test's temporary assets; retain the log for diagnosis.
  if (ownsAssets) {
    for (const path of [script, script + '.meta', link + '.meta', ready, stop, reload]) await rm(path, { force: true })
    try {
      const info = await lstat(link)
      if (info.isSymbolicLink()) await rm(link)
      else if (info.isDirectory()) {
        for (const name of ['escape.txt', 'escape.txt.meta']) await rm(join(link, name), { force: true })
        await rmdir(link)
      }
    } catch (error) { if (error.code !== 'ENOENT') console.error('Smoke fixture cleanup:', error.message) }
    if (createdEditor) try { await rmdir(editor); await rm(editor + '.meta', { force: true }) } catch {}
  }
}
