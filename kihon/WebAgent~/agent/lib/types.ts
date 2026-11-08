export interface ToolCall { id: string; name: string; arguments: Record<string, unknown>; rawArguments: string }
export interface AgentMessage {
  role: 'system' | 'user' | 'assistant' | 'tool'
  content: string
  tool_calls?: ToolCall[]
  tool_call_id?: string
  reasoning_content?: string
}
export interface AgentTool {
  name: string
  description: string
  parameters: Record<string, unknown>
}
export interface AgentTurn { content: string; calls: ToolCall[]; reasoning?: string }
export interface AgentProvider {
  readonly model: string
  inspect(signal: AbortSignal): Promise<void>
  complete(messages: AgentMessage[], tools: AgentTool[], signal: AbortSignal): Promise<AgentTurn>
}
export interface AgentRequest {
  schemaVersion: number
  projectId: string
  requestId: string
  inputRevision: number
  capability: string
  operation: string
  payload: Record<string, unknown>
}
export type ToolReply = { ok: true; result: unknown } | { ok: false; error: { code: string; message: string } }
export interface AgentDependencies {
  provider: AgentProvider
  /** Trusted host capabilities, never taken from a conversation or request payload. */
  hostTools?: {
    tools: AgentTool[]
    instructions: string
    execute(name: string, args: Record<string, unknown>, signal: AbortSignal): Promise<ToolReply>
  }
  executeTool(name: string, args: Record<string, unknown>, signal: AbortSignal): Promise<ToolReply>
  progress(message: string): void
  signal: AbortSignal
}
export type AgentResult = { text: string; artifacts: [] } | { configured: true; model: string; tools: string[] }
