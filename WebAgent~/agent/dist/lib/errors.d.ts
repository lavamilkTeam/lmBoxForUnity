export declare class AgentError extends Error {
    readonly code: string;
    constructor(code: string, message: string);
}
export declare function checkCancelled(signal: AbortSignal): void;
export declare function safeError(error: unknown): {
    code: string;
    message: string;
};
