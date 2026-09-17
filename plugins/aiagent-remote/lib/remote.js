var __runInitializers = (this && this.__runInitializers) || function (thisArg, initializers, value) {
    var useValue = arguments.length > 2;
    for (var i = 0; i < initializers.length; i++) {
        value = useValue ? initializers[i].call(thisArg, value) : initializers[i].call(thisArg);
    }
    return useValue ? value : void 0;
};
var __esDecorate = (this && this.__esDecorate) || function (ctor, descriptorIn, decorators, contextIn, initializers, extraInitializers) {
    function accept(f) { if (f !== void 0 && typeof f !== "function") throw new TypeError("Function expected"); return f; }
    var kind = contextIn.kind, key = kind === "getter" ? "get" : kind === "setter" ? "set" : "value";
    var target = !descriptorIn && ctor ? contextIn["static"] ? ctor : ctor.prototype : null;
    var descriptor = descriptorIn || (target ? Object.getOwnPropertyDescriptor(target, contextIn.name) : {});
    var _, done = false;
    for (var i = decorators.length - 1; i >= 0; i--) {
        var context = {};
        for (var p in contextIn) context[p] = p === "access" ? {} : contextIn[p];
        for (var p in contextIn.access) context.access[p] = contextIn.access[p];
        context.addInitializer = function (f) { if (done) throw new TypeError("Cannot add initializers after decoration has completed"); extraInitializers.push(accept(f || null)); };
        var result = (0, decorators[i])(kind === "accessor" ? { get: descriptor.get, set: descriptor.set } : descriptor[key], context);
        if (kind === "accessor") {
            if (result === void 0) continue;
            if (result === null || typeof result !== "object") throw new TypeError("Object expected");
            if (_ = accept(result.get)) descriptor.get = _;
            if (_ = accept(result.set)) descriptor.set = _;
            if (_ = accept(result.init)) initializers.unshift(_);
        }
        else if (_ = accept(result)) {
            if (kind === "field") initializers.unshift(_);
            else descriptor[key] = _;
        }
    }
    if (target) Object.defineProperty(target, contextIn.name, descriptor);
    done = true;
};
import { credentialRef } from '@deepseek-ai/dsh-credentials';
import { Remote, RemoteError, TypertRemoteService } from '@deepseek-ai/dsh-typert-protocol';
import { AiAgentClient, normalizeAiAgentBaseUrl } from './client.mjs';
function contextWindowOf(value) {
    const parsed = typeof value === 'number' ? value : Number(value);
    return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : 128000;
}
function codexAvailableOf(capabilities) {
    if (capabilities === null || typeof capabilities !== 'object')
        return false;
    const codex = capabilities.codex;
    return codex !== null && typeof codex === 'object' && codex.available === true;
}
function statusOf(configured, baseUrl, capabilities) {
    const rawModels = capabilities !== null && typeof capabilities === 'object'
        && Array.isArray(capabilities.llm_models)
        ? capabilities.llm_models
        : [];
    return {
        configured, baseUrl, codexAvailable: codexAvailableOf(capabilities),
        models: rawModels.map(model => ({ id: model.id, name: model.name ?? model.id })),
    };
}
let AiAgentRemoteController = (() => {
    let _classSuper = TypertRemoteService;
    let _instanceExtraInitializers = [];
    let _status_decorators;
    let _login_decorators;
    let _logout_decorators;
    let _testCodex_decorators;
    return class AiAgentRemoteController extends _classSuper {
        static {
            const _metadata = typeof Symbol === "function" && Symbol.metadata ? Object.create(_classSuper[Symbol.metadata] ?? null) : void 0;
            _status_decorators = [Remote];
            _login_decorators = [Remote];
            _logout_decorators = [Remote];
            _testCodex_decorators = [Remote];
            __esDecorate(this, null, _status_decorators, { kind: "method", name: "status", static: false, private: false, access: { has: obj => "status" in obj, get: obj => obj.status }, metadata: _metadata }, null, _instanceExtraInitializers);
            __esDecorate(this, null, _login_decorators, { kind: "method", name: "login", static: false, private: false, access: { has: obj => "login" in obj, get: obj => obj.login }, metadata: _metadata }, null, _instanceExtraInitializers);
            __esDecorate(this, null, _logout_decorators, { kind: "method", name: "logout", static: false, private: false, access: { has: obj => "logout" in obj, get: obj => obj.logout }, metadata: _metadata }, null, _instanceExtraInitializers);
            __esDecorate(this, null, _testCodex_decorators, { kind: "method", name: "testCodex", static: false, private: false, access: { has: obj => "testCodex" in obj, get: obj => obj.testCodex }, metadata: _metadata }, null, _instanceExtraInitializers);
            if (_metadata) Object.defineProperty(this, Symbol.metadata, { enumerable: true, configurable: true, writable: true, value: _metadata });
        }
        config = __runInitializers(this, _instanceExtraInitializers);
        constructor(ctx, config) {
            super(ctx, 'aiagentRemoteController', { namespace: 'aiagentRemote' });
            this.config = config;
        }
        async status() {
            const credentials = this.ctx.credentials;
            const [tokenInfo, token, baseUrl] = await Promise.all([
                credentials.describe(credentialRef(this.config.accessTokenEnv)),
                credentials.resolve(credentialRef(this.config.accessTokenEnv)),
                credentials.resolve(credentialRef(this.config.baseUrlEnv)),
            ]);
            const resolvedBaseUrl = baseUrl?.value ?? this.config.baseUrl;
            if (!tokenInfo.configured || token === undefined)
                return statusOf(false, resolvedBaseUrl);
            try {
                return statusOf(true, resolvedBaseUrl, await new AiAgentClient(resolvedBaseUrl, token.value).capabilities());
            }
            catch {
                return statusOf(true, resolvedBaseUrl);
            }
        }
        async login(baseUrl, username, password) {
            if (username.trim().length === 0 || password.length === 0) {
                throw new RemoteError('gateway/bad-request', 'AiAgent username and password are required.', {});
            }
            const normalized = normalizeAiAgentBaseUrl(baseUrl);
            try {
                const client = await AiAgentClient.login(normalized, username.trim(), password);
                const capabilities = await client.capabilities();
                const models = Array.isArray(capabilities.llm_models) ? capabilities.llm_models : [];
                await this.ctx.credentials.set(credentialRef(this.config.accessTokenEnv), client.token);
                await this.ctx.credentials.set(credentialRef(this.config.baseUrlEnv), normalized);
                const settings = this.ctx.get('settings');
                if (settings !== undefined) {
                    await settings.update('llm-deepseek', {
                        baseURL: `${normalized}/api/v1/deepseek-plugin`,
                        models: models.map((model) => ({
                            id: model.id, name: model.name ?? model.id, contextWindow: contextWindowOf(model.context_window), maxTokens: 8192,
                        })),
                    });
                }
                return statusOf(true, normalized, capabilities);
            }
            catch (error) {
                throw new RemoteError('gateway/internal', error instanceof Error ? error.message : 'AiAgent login failed.', {});
            }
        }
        async logout() {
            await this.ctx.credentials.unset(credentialRef(this.config.accessTokenEnv));
        }
        async testCodex() {
            const token = await this.ctx.credentials.resolve(credentialRef(this.config.accessTokenEnv));
            const baseUrl = await this.ctx.credentials.resolve(credentialRef(this.config.baseUrlEnv));
            if (token === undefined)
                throw new RemoteError('gateway/bad-request', 'Sign in before testing the server Codex CLI.', {});
            const client = new AiAgentClient(baseUrl?.value ?? this.config.baseUrl, token.value);
            if (!codexAvailableOf(await client.capabilities())) {
                throw new RemoteError('gateway/bad-request', 'The AiAgent server has no available Codex CLI.', {});
            }
            const result = await client.delegateCodex({
                prompt: 'Reply with one short sentence confirming that the server Codex CLI is available. Do not access files.',
            });
            return {
                modelId: typeof result.model_id === 'string' ? result.model_id : typeof result.model === 'string' ? result.model : 'server default',
                answer: typeof result.answer === 'string' ? result.answer.slice(0, 500) : 'Server Codex CLI returned a response.',
            };
        }
    };
})();
export { AiAgentRemoteController };
