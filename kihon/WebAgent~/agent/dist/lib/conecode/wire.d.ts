import type { AgentMessage, AgentTool, ToolCall } from '../types.js';
export declare function cleanMessages(messages: AgentMessage[]): unknown[];
export declare function toOpenAITools(tools: AgentTool[]): unknown[];
export declare function extractToolCalls(raw: unknown): ToolCall[];
export declare class CompletionAccumulator {
    content: string;
    reasoning: string;
    finishReason: string;
    sawDone: boolean;
    private calls;
    addLine(line: string): void;
    finish(): {
        content: string;
        reasoning: string;
        calls: ToolCall[];
    };
}
