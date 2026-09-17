import type { Context } from '@deepseek-ai/cordis';
import { TypertRemoteService } from '@deepseek-ai/dsh-typert-protocol';
import type { Config } from './index.js';
export interface AiAgentRemoteStatus {
    configured: boolean;
    baseUrl: string;
    models: Array<{
        id: string;
        name: string;
    }>;
    codexAvailable: boolean;
}
export interface AiAgentCodexTest {
    modelId: string;
    answer: string;
}
export declare class AiAgentRemoteController extends TypertRemoteService {
    private readonly config;
    constructor(ctx: Context, config: Config);
    status(): Promise<AiAgentRemoteStatus>;
    login(baseUrl: string, username: string, password: string): Promise<AiAgentRemoteStatus>;
    logout(): Promise<void>;
    testCodex(): Promise<AiAgentCodexTest>;
}
