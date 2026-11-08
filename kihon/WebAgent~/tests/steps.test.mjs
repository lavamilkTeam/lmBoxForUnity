import test from 'node:test'
import assert from 'node:assert/strict'
import { executeAgent } from '../agent/dist/index.js'

const request = maxSteps => ({ schemaVersion: 1, capability: 'agent', operation: 'start', payload: { prompt: 'Long task', ...(maxSteps === undefined ? {} : { maxSteps }) } })
for (const maxSteps of [undefined, 0]) {
  test(`unlimited budget ${maxSteps} completes beyond 32 model calls`, async () => {
    let steps = 0, tools = 0
    const result = await executeAgent(request(maxSteps), {
      signal: new AbortController().signal, progress() {},
      provider: { model: 'fixture', async complete() {
        return ++steps <= 40 ? { content: '', calls: [{ id: `call-${steps}`, name: 'calculate', arguments: { expression: '6*7' } }] } : { content: 'Done', calls: [] }
      } },
      async executeTool() { tools++; return { ok: true, result: 42 } },
    })
    assert.equal(result.text, 'Done'); assert.equal(steps, 41); assert.equal(tools, 40)
  })
}
test('unlimited runs still honour cancellation', async () => {
  const controller = new AbortController()
  let steps = 0
  await assert.rejects(executeAgent(request(0), {
    signal: controller.signal, progress() {},
    provider: { model: 'fixture', async complete() { return { content: '', calls: [{ id: `call-${++steps}`, name: 'list_files', arguments: {} }] } } },
    async executeTool() { if (steps === 35) controller.abort(); return { ok: true, result: [] } },
  }), { code: 'cancelled' })
  assert.equal(steps, 35)
})
