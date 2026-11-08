import type { AgentDependencies } from './index.js';
type Environment = Record<string, string | undefined>;
/** Host-only connection. Credentials never enter model messages or browser responses. */
export declare function connectUnityTools(env: Environment, signal: AbortSignal): Promise<AgentDependencies['hostTools']>;
export {};
