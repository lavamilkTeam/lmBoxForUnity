// Development host: existing frontend sources → Vite → bundled host or Unity's public proxy.
import { createRequire } from 'node:module'
import { randomBytes } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createWebAgentServer } from './server.mjs'
import { createTransport } from './lib/transport.mjs'

const root = fileURLToPath(new URL('../', import.meta.url))
const source = resolve(process.env.LMBOX_FRONTEND_SOURCE || resolve(root, '../../../lmbox'))
const requireFrontend = createRequire(resolve(source, 'package.json'))
const { createServer } = await import(pathToFileURL(requireFrontend.resolve('vite')).href)
const port = Number(process.env.LMBOX_DEV_PORT || 5173)
const origin = `http://127.0.0.1:${port}`
let backend, vite, authorization
let target = process.env.LMBOX_DEV_BACKEND_URL

async function close() {
  await vite?.close()
  if (backend) {
    backend.closeAllConnections()
    await new Promise(resolve => backend.close(resolve))
  }
}

try {
  if (target) {
    const url = new URL(target)
    if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1' || url.username || url.password || url.pathname !== '/' || url.search || url.hash)
      throw new Error('LMBOX_DEV_BACKEND_URL must be the Unity loopback public URL')
    target = url.origin
  } else {
    const project = resolve(root, '../../AIAgentDemo')
    let settings = {}
    try { settings = JSON.parse(await readFile(resolve(project, 'ProjectSettings/LmboxAIAgentSettings.json'), 'utf8')) }
    catch (error) { if (error.code !== 'ENOENT') throw error }
    const base = (settings.BaseUrl || 'https://api.openai.com').replace(/\/+$/, '')
    process.env.LMBOX_AGENT_BASE_URL ??= base.endsWith('/v1') ? base : `${base}/v1`
    process.env.LMBOX_AGENT_MODEL ??= settings.Model || 'gpt-4o-mini'
    process.env.LMBOX_AGENT_API_KEY ??= settings.ApiKey || ''
    const password = randomBytes(24).toString('hex')
    authorization = `Basic ${Buffer.from(`dev:${password}`).toString('base64')}`
    const transport = createTransport({
      binary: resolve(root, `bin/ai${process.platform === 'win32' ? '.exe' : ''}`),
      worker: resolve(root, 'agent/dist/worker.js'),
      directory: resolve(process.env.LMBOX_WEB_DATA || resolve(project, 'Library/AIAgentWeb/data')),
    })
    // Host and Origin remain the public Vite origin; credentials stay in this process.
    backend = createWebAgentServer({ origin, username: 'dev', password, site: resolve(root, 'site'), transport })
    await new Promise((resolve, reject) => {
      backend.once('error', reject)
      backend.listen(0, '127.0.0.1', resolve)
    })
    target = `http://127.0.0.1:${backend.address().port}`
  }
  vite = await createServer({
    root: source,
    configFile: resolve(source, 'vite.web-agent.config.ts'),
    envDir: root,
    cacheDir: resolve(root, 'node_modules/.vite-frontend'),
    plugins: [{
      name: 'unity-agent-dev-entry',
      configureServer(server) {
        server.middlewares.use((req, res, next) => {
          if (req.url?.split('?')[0] === '/api/ai' &&
              (req.headers.origin !== origin || req.headers['sec-fetch-site'] === 'cross-site')) {
            res.writeHead(403); res.end('Invalid origin'); return
          }
          if (req.url === '/') req.url = '/agent-web.html'
          next()
        })
      },
    }],
    server: {
      host: '127.0.0.1', port, strictPort: true,
      proxy: { '^/api/ai(?:\\?|$)': { target, changeOrigin: false, headers: authorization ? { Authorization: authorization } : {} } },
    },
  })
  await vite.listen()
  console.log(`Frontend dev: ${origin}/`)
  console.log(`Source: ${source}/frontend`)
  console.log(backend ? 'Backend: bundled Agent (standalone; no Unity editor tools)' : `Backend: Unity ${target}`)
  for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => { void close() })
} catch (error) {
  await close()
  throw error
}
