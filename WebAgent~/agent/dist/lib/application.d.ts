import type { AgentDependencies, AgentRequest, AgentResult } from './types.js';
export declare function executeAgent(request: AgentRequest, deps: AgentDependencies, context?: unknown): Promise<AgentResult>;
