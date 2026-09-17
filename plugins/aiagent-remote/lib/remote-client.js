function invalid(field) {
    throw new TypeError(`aiagentRemote: invalid ${field} returned by the Host`);
}
const string = {
    parse(value) {
        if (typeof value !== 'string')
            invalid('string');
        return value;
    },
};
const status = {
    parse(value) {
        if (value === null || typeof value !== 'object')
            invalid('status');
        const candidate = value;
        if (typeof candidate.configured !== 'boolean' || typeof candidate.baseUrl !== 'string'
            || typeof candidate.codexAvailable !== 'boolean' || !Array.isArray(candidate.models)) {
            invalid('status');
        }
        const models = candidate.models.map((model) => {
            if (model === null || typeof model !== 'object')
                invalid('model');
            const item = model;
            if (typeof item.id !== 'string' || typeof item.name !== 'string')
                invalid('model');
            return { id: item.id, name: item.name };
        });
        return { configured: candidate.configured, baseUrl: candidate.baseUrl, models, codexAvailable: candidate.codexAvailable };
    },
};
const codexTest = {
    parse(value) {
        if (value === null || typeof value !== 'object')
            invalid('Codex test result');
        const candidate = value;
        if (typeof candidate.modelId !== 'string' || typeof candidate.answer !== 'string')
            invalid('Codex test result');
        return { modelId: candidate.modelId, answer: candidate.answer };
    },
};
const empty = { parse(value) { if (value !== undefined)
        invalid('logout result'); return undefined; } };
const packageName = '@aiagent/deepseek-plugin-remote';
/** Browser descriptors for the Host's source-mode Typert service. */
const contribution = {
    package: packageName,
    descriptors: [
        {
            id: `${packageName}#aiagentRemote/status`, service: 'aiagentRemoteController', namespace: 'aiagentRemote', method: 'status',
            invocation: { kind: 'direct' }, parameters: [],
            result: { mode: 'strict', typeSymbol: `${packageName}#AiAgentRemoteStatus`, schema: status },
            sourceLocation: { file: 'src/remote.ts', line: 15, column: 3 },
        },
        {
            id: `${packageName}#aiagentRemote/login`, service: 'aiagentRemoteController', namespace: 'aiagentRemote', method: 'login',
            invocation: { kind: 'direct' },
            parameters: ['baseUrl', 'username', 'password'].map((name) => ({
                name, wire: name, source: 'json',
                codec: { mode: 'strict', typeSymbol: `${packageName}#aiagentRemote/login:${name}`, schema: string },
            })),
            result: { mode: 'strict', typeSymbol: `${packageName}#AiAgentRemoteStatus`, schema: status },
            sourceLocation: { file: 'src/remote.ts', line: 26, column: 3 },
        },
        {
            id: `${packageName}#aiagentRemote/logout`, service: 'aiagentRemoteController', namespace: 'aiagentRemote', method: 'logout',
            invocation: { kind: 'direct' }, parameters: [],
            result: { mode: 'strict', typeSymbol: `${packageName}#aiagentRemote/logout:result`, schema: empty },
            sourceLocation: { file: 'src/remote.ts', line: 50, column: 3 },
        },
        {
            id: `${packageName}#aiagentRemote/testCodex`, service: 'aiagentRemoteController', namespace: 'aiagentRemote', method: 'testCodex',
            invocation: { kind: 'direct' }, parameters: [],
            result: { mode: 'strict', typeSymbol: `${packageName}#AiAgentCodexTest`, schema: codexTest },
            sourceLocation: { file: 'src/remote.ts', line: 78, column: 3 },
        },
    ],
};
export default contribution;
