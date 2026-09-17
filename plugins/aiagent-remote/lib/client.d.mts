export function normalizeAiAgentBaseUrl(value: any): string;
export class AiAgentClient {
    static login(baseUrl: any, username: any, password: any, fetchImpl?: typeof fetch): Promise<AiAgentClient>;
    constructor(baseUrl: any, token: any, fetchImpl?: typeof fetch);
    baseUrl: string;
    token: any;
    fetch: typeof fetch;
    capabilities(signal: any): Promise<any>;
    delegateCodex(request: any, signal: any): Promise<any>;
    #private;
}
