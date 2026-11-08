import test from 'node:test'
import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { once } from 'node:events'
import { connectUnityTools } from '../agent/dist/unity.js'
import { executeAgent } from '../agent/dist/index.js'

const token = 'a'.repeat(64)
const tool = { name: 'unity_create_gameobject', description: 'Create an object', parameters: { type: 'object', properties: { name: { type: 'string' }, position: { type: 'object' } }, required: ['name'] } }
async function host(t, handler) {
  const server = createServer(handler)
  server.listen(0, '127.0.0.1')
  await once(server, 'listening')
  t.after(() => { server.closeAllConnections(); return new Promise(resolve => server.close(resolve)) })
  return { LMBOX_UNITY_BRIDGE_URL: `http://127.0.0.1:${server.address().port}/`, LMBOX_UNITY_BRIDGE_TOKEN: token }
}
const json = (res, value) => { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(value)) }
const request = { schemaVersion: 1, projectId: 'test', requestId: 'test', inputRevision: 1, capability: 'agent', operation: 'start', payload: { prompt: 'Create a cube', maxSteps: 3 } }

test('Unity tool schema, arguments and real reply roundtrip through the TS Agent', async t => {
  const calls = []
  const env = await host(t, async (req, res) => {
    assert.equal(req.headers.authorization, `Bearer ${token}`)
    if (req.url === '/tools') return json(res, { protocolVersion: 1, tools: [tool] })
    let body = ''; for await (const chunk of req) body += chunk
    calls.push(JSON.parse(body))
    json(res, { ok: true, result: 'Created Cube in Unity' })
  })
  const signal = new AbortController().signal
  const hostTools = await connectUnityTools(env, signal)
  let step = 0
  const result = await executeAgent(request, {
    signal, hostTools, progress() {}, executeTool() { throw new Error('Unity tools must not reach Rust') },
    provider: { model: 'fixture', async inspect() {}, async complete(messages, tools) {
      assert.deepEqual(tools.find(t => t.name === tool.name), tool)
      assert.ok(!JSON.stringify(messages).includes(token))
      if (step++ === 0) return { content: '', calls: [{ id: 'create', name: tool.name, arguments: { name: 'Cube', position: { x: 1, y: 2, z: 3 } }, rawArguments: '{}' }] }
      assert.deepEqual(JSON.parse(messages.at(-1).content), { ok: true, result: 'Created Cube in Unity' })
      return { content: 'Created.', calls: [] }
    } },
  })
  assert.equal(result.text, 'Created.')
  assert.equal(calls.length, 1)
  assert.equal(calls[0].name, tool.name)
  assert.equal(calls[0].arguments.position.y, 2)
  assert.match(calls[0].id, /^[a-f0-9-]{36}$/)
})

test('without Unity configuration, existing tools still work and request payload cannot inject tools', async () => {
  const signal = new AbortController().signal
  assert.equal(await connectUnityTools({}, signal), undefined)
  let step = 0, nativeCalls = 0
  const result = await executeAgent({ ...request, payload: { ...request.payload, hostTools: [tool] } }, {
    signal, progress() {}, async executeTool(name) { nativeCalls++; assert.equal(name, 'calculate'); return { ok: true, result: 42 } },
    provider: { model: 'fixture', async inspect() {}, async complete(messages, tools) {
      assert.equal(tools.length, 5)
      if (step++ === 0) return { content: '', calls: [{ id: 'math', name: 'calculate', arguments: { expression: '6*7' }, rawArguments: '{}' }] }
      assert.equal(JSON.parse(messages.at(-1).content).result, 42)
      return { content: '42', calls: [] }
    } },
  })
  assert.equal(result.text, '42'); assert.equal(nativeCalls, 1)
})

test('unknown Unity calls never reach the bridge and denial remains a failure reply', async t => {
  let executions = 0
  const env = await host(t, (req, res) => {
    if (req.url === '/tools') return json(res, { protocolVersion: 1, tools: [tool] })
    executions++; json(res, { ok: false, error: { code: 'user_denied', message: 'Denied' } })
  })
  const signal = new AbortController().signal
  const adapter = await connectUnityTools(env, signal)
  assert.equal((await adapter.execute('unity_unknown', {}, signal)).ok, false)
  assert.equal(executions, 0)
  assert.equal((await adapter.execute(tool.name, { name: 'Cube' }, signal)).error.code, 'user_denied')
  assert.equal(executions, 1)
})

test('only fully configured loopback hosts are accepted', async () => {
  const signal = new AbortController().signal
  for (const address of ['http://example.com:1234/', 'http://127.0.0.1:1234/tools', 'https://127.0.0.1:1234/', 'http://user@127.0.0.1:1234/']) {
    await assert.rejects(connectUnityTools({ LMBOX_UNITY_BRIDGE_URL: address, LMBOX_UNITY_BRIDGE_TOKEN: token }, signal), { code: 'invalid_configuration' })
  }
  await assert.rejects(connectUnityTools({ LMBOX_UNITY_BRIDGE_TOKEN: token }, signal), { code: 'invalid_configuration' })
})

test('cancellation aborts the call and sends a matching cancellation ID without replaying edits', async t => {
  let postId, cancelId, posts = 0
  let received, cancelled
  const ready = new Promise(resolve => { received = resolve })
  const cancellation = new Promise(resolve => { cancelled = resolve })
  const env = await host(t, async (req, res) => {
    if (req.url === '/tools') return json(res, { protocolVersion: 1, tools: [tool] })
    if (req.method === 'DELETE') { cancelId = req.url.split('/').at(-1); json(res, { ok: true, result: 'cancelled' }); cancelled(); return }
    posts++
    let body = ''; for await (const chunk of req) body += chunk
    postId = JSON.parse(body).id; received()
  })
  const controller = new AbortController()
  const adapter = await connectUnityTools(env, controller.signal)
  const call = adapter.execute(tool.name, { name: 'Cube' }, controller.signal)
  const rejected = assert.rejects(call, { code: 'cancelled' })
  await ready; controller.abort(); await rejected
  await cancellation
  assert.equal(cancelId, postId); assert.equal(posts, 1)
})

test('authentication errors and malformed catalogs fail closed', async t => {
  const env = await host(t, (req, res) => { res.writeHead(401); res.end('private details') })
  await assert.rejects(connectUnityTools(env, new AbortController().signal), error => error.code === 'unity_unavailable' && !error.message.includes('private details'))
  const malformed = await host(t, (req, res) => json(res, { protocolVersion: 1, tools: [tool, tool] }))
  await assert.rejects(connectUnityTools(malformed, new AbortController().signal), { code: 'unity_protocol' })
})
