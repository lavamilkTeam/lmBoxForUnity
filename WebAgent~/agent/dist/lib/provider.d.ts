import type { AgentProvider } from './types.js';
type Environment = Record<string, string | undefined>;
export declare function createOpenAIProvider(env?: Environment): AgentProvider;
export {};
