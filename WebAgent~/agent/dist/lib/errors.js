export class AgentError extends Error {
    code;
    constructor(code, message) {
        super(message);
        this.code = code;
        this.name = 'AgentError';
    }
}
export function checkCancelled(signal) {
    if (signal.aborted)
        throw new AgentError('cancelled', 'Agent job cancelled.');
}
export function safeError(error) {
    return error instanceof AgentError ? { code: error.code, message: error.message }
        : { code: 'worker_error', message: 'Agent worker failed.' };
}
