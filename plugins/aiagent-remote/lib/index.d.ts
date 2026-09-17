import type { Context } from '@deepseek-ai/cordis';
import Schema from '@deepseek-ai/schemastery';
export interface Config {
    baseUrl: string;
    baseUrlEnv: string;
    accessTokenEnv: string;
    codexModelEnv: string;
    enableCodex: boolean;
    maxContextChars: number;
}
export declare const Config: Schema<Config>;
export declare const name = "aiagent-remote";
export declare const inject: string[];
export declare function apply(ctx: Context, config: Config): void;
