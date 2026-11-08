import { AgentError } from './index.js';
import { randomUUID } from 'node:crypto';
const LIMIT = 2 * 1024 * 1024;
/** Host-only connection. Credentials never enter model messages or browser responses. */
export async function connectUnityTools(env, signal) {
    const address = env.LMBOX_UNITY_BRIDGE_URL;
    const token = env.LMBOX_UNITY_BRIDGE_TOKEN;
    if (!address && !token)
        return undefined;
    let origin;
    try {
        origin = new URL(address ?? '');
        if (origin.protocol !== 'http:' || origin.hostname !== '127.0.0.1' || !origin.port
            || origin.pathname !== '/' || origin.search || origin.hash || origin.username || origin.password
            || !token || !/^[a-f0-9]{64}$/.test(token))
            throw new Error();
    }
    catch {
        throw new AgentError('invalid_configuration', 'Invalid Unity host connection.');
    }
    async function request(path, body, signal) {
        if (signal.aborted)
            throw new AgentError('cancelled', 'Agent job cancelled.');
        const controller = new AbortController();
        const id = body === undefined ? undefined : randomUUID();
        const cancel = () => {
            controller.abort();
            // Best effort cancellation, never retry the mutation itself.
            if (id)
                void fetch(new URL(`execute/${id}`, origin), {
                    method: 'DELETE', redirect: 'error', signal: AbortSignal.timeout(2000),
                    headers: { Authorization: `Bearer ${token}` },
                }).then(response => response.body?.cancel()).catch(() => { });
        };
        signal.addEventListener('abort', cancel, { once: true });
        const timer = setTimeout(cancel, body === undefined ? 10000 : 120000);
        try {
            const encoded = body === undefined ? undefined : JSON.stringify({ ...body, id });
            if (encoded && Buffer.byteLength(encoded) > LIMIT)
                throw new AgentError('unity_input_limit', 'Unity tool input is too large.');
            const response = await fetch(new URL(path, origin), {
                method: encoded ? 'POST' : 'GET', redirect: 'error', signal: controller.signal,
                headers: { Authorization: `Bearer ${token}`, ...(encoded ? { 'Content-Type': 'application/json' } : {}) },
                body: encoded,
            });
            if (!response.ok) {
                await response.body?.cancel();
                throw new AgentError('unity_unavailable', `Unity host returned HTTP ${response.status}. Do not automatically repeat edits.`);
            }
            const reader = response.body?.getReader();
            if (!reader)
                throw new Error();
            const chunks = [];
            let size = 0;
            try {
                while (true) {
                    const part = await reader.read();
                    if (part.done)
                        break;
                    size += part.value.byteLength;
                    if (size > LIMIT)
                        throw new AgentError('unity_output_limit', 'Unity host response is too large.');
                    chunks.push(part.value);
                }
            }
            finally {
                await reader.cancel().catch(() => { });
                reader.releaseLock();
            }
            return JSON.parse(Buffer.concat(chunks).toString('utf8'));
        }
        catch (error) {
            if (signal.aborted)
                throw new AgentError('cancelled', 'Agent job cancelled.');
            if (error instanceof AgentError)
                throw error;
            throw new AgentError('unity_unavailable', 'Unity host disconnected or timed out. An edit may already have completed; inspect before repeating it.');
        }
        finally {
            clearTimeout(timer);
            signal.removeEventListener('abort', cancel);
        }
    }
    const manifest = await request('tools', undefined, signal);
    if (!manifest || manifest.protocolVersion !== 1 || !Array.isArray(manifest.tools) || manifest.tools.length > 64
        || manifest.tools.some((tool) => !tool || !/^unity_[a-z0-9_]+$/.test(tool.name)
            || typeof tool.description !== 'string' || !tool.parameters || tool.parameters.type !== 'object')
        || new Set(manifest.tools.map((tool) => tool.name)).size !== manifest.tools.length) {
        throw new AgentError('unity_protocol', 'Invalid Unity tool catalog.');
    }
    const tools = manifest.tools.map((tool) => ({ name: tool.name, description: tool.description, parameters: tool.parameters }));
    return {
        tools,
        instructions: '\nUnity editor tools are additionally available through the trusted local host. '
            + 'Use unity_* tools for the actual Unity project, scene, assets, scripts and Console; ordinary file tools only access isolated job files. '
            + 'Inspect the scene before editing. Script writes and menu commands require confirmation inside Unity. '
            + 'Compilation or play mode can reload the editor and interrupt this task. Do not claim compilation succeeded from a write response; check logs once the editor is ready. '
            + 'Never automatically repeat a mutation after a disconnect or timeout.',
        async execute(name, args, signal) {
            if (!tools.some(tool => tool.name === name) || !args || typeof args !== 'object' || Array.isArray(args)) {
                return { ok: false, error: { code: 'invalid_tool_input', message: 'Unknown Unity tool or invalid arguments.' } };
            }
            const reply = await request('execute', { name, arguments: args }, signal);
            if (!reply || !(reply.ok === true && 'result' in reply || reply.ok === false
                && typeof reply.error?.code === 'string' && typeof reply.error?.message === 'string')) {
                throw new AgentError('unity_protocol', 'Invalid Unity tool reply.');
            }
            return reply;
        },
    };
}
