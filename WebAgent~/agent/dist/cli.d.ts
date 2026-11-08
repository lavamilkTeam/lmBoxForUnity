import type { AgentDependencies } from './index.js';
type Environment = Record<string, string | undefined>;
/** AI owns only an argv tool. Parsing and Unity communication are implemented by C# lmu3d. */
export declare function connectUnityCli(env: Environment, signal: AbortSignal, workspace: string): Promise<AgentDependencies['hostTools']>;
export {};
