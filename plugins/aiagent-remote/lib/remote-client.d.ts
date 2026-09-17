export interface AiAgentRemoteStatus {
    readonly configured: boolean;
    readonly baseUrl: string;
    readonly models: readonly {
        readonly id: string;
        readonly name: string;
    }[];
    readonly codexAvailable: boolean;
}
export interface AiAgentCodexTest {
    readonly modelId: string;
    readonly answer: string;
}
/** Browser descriptors for the Host's source-mode Typert service. */
declare const contribution: {
    package: string;
    descriptors: ({
        id: string;
        service: string;
        namespace: string;
        method: string;
        invocation: {
            kind: "direct";
        };
        parameters: {
            name: string;
            wire: string;
            source: "json";
            codec: {
                mode: "strict";
                typeSymbol: string;
                schema: {
                    parse(value: unknown): string;
                };
            };
        }[];
        result: {
            mode: "strict";
            typeSymbol: string;
            schema: {
                parse(value: unknown): AiAgentRemoteStatus;
            };
        };
        sourceLocation: {
            file: string;
            line: number;
            column: number;
        };
    } | {
        id: string;
        service: string;
        namespace: string;
        method: string;
        invocation: {
            kind: "direct";
        };
        parameters: never[];
        result: {
            mode: "strict";
            typeSymbol: string;
            schema: {
                parse(value: unknown): undefined;
            };
        };
        sourceLocation: {
            file: string;
            line: number;
            column: number;
        };
    } | {
        id: string;
        service: string;
        namespace: string;
        method: string;
        invocation: {
            kind: "direct";
        };
        parameters: never[];
        result: {
            mode: "strict";
            typeSymbol: string;
            schema: {
                parse(value: unknown): AiAgentCodexTest;
            };
        };
        sourceLocation: {
            file: string;
            line: number;
            column: number;
        };
    })[];
};
export default contribution;
