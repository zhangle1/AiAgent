window.__ModuleLoader__.load({
	id: "@aiagent/deepseek-plugin-remote",
	factory: (require) => {
		var module = { exports: {} };
		var exports = module.exports;
		Object.defineProperty(exports, Symbol.toStringTag, { value: "Module" });
		let react = require("react");
		let react_jsx_runtime = require("react/jsx-runtime");
		//#region src/remote-client.ts
		function invalid(field) {
			throw new TypeError(`aiagentRemote: invalid ${field} returned by the Host`);
		}
		const string = { parse(value) {
			if (typeof value !== "string") invalid("string");
			return value;
		} };
		const status = { parse(value) {
			if (value === null || typeof value !== "object") invalid("status");
			const candidate = value;
			if (typeof candidate.configured !== "boolean" || typeof candidate.baseUrl !== "string" || typeof candidate.codexAvailable !== "boolean" || !Array.isArray(candidate.models)) invalid("status");
			const models = candidate.models.map((model) => {
				if (model === null || typeof model !== "object") invalid("model");
				const item = model;
				if (typeof item.id !== "string" || typeof item.name !== "string") invalid("model");
				return {
					id: item.id,
					name: item.name
				};
			});
			return {
				configured: candidate.configured,
				baseUrl: candidate.baseUrl,
				models,
				codexAvailable: candidate.codexAvailable
			};
		} };
		const codexTest = { parse(value) {
			if (value === null || typeof value !== "object") invalid("Codex test result");
			const candidate = value;
			if (typeof candidate.modelId !== "string" || typeof candidate.answer !== "string") invalid("Codex test result");
			return {
				modelId: candidate.modelId,
				answer: candidate.answer
			};
		} };
		const empty = { parse(value) {
			if (value !== void 0) invalid("logout result");
		} };
		const packageName = "@aiagent/deepseek-plugin-remote";
		/** Browser descriptors for the Host's source-mode Typert service. */
		const contribution = {
			package: packageName,
			descriptors: [
				{
					id: `${packageName}#aiagentRemote/status`,
					service: "aiagentRemoteController",
					namespace: "aiagentRemote",
					method: "status",
					invocation: { kind: "direct" },
					parameters: [],
					result: {
						mode: "strict",
						typeSymbol: `${packageName}#AiAgentRemoteStatus`,
						schema: status
					},
					sourceLocation: {
						file: "src/remote.ts",
						line: 15,
						column: 3
					}
				},
				{
					id: `${packageName}#aiagentRemote/login`,
					service: "aiagentRemoteController",
					namespace: "aiagentRemote",
					method: "login",
					invocation: { kind: "direct" },
					parameters: [
						"baseUrl",
						"username",
						"password"
					].map((name) => ({
						name,
						wire: name,
						source: "json",
						codec: {
							mode: "strict",
							typeSymbol: `${packageName}#aiagentRemote/login:${name}`,
							schema: string
						}
					})),
					result: {
						mode: "strict",
						typeSymbol: `${packageName}#AiAgentRemoteStatus`,
						schema: status
					},
					sourceLocation: {
						file: "src/remote.ts",
						line: 26,
						column: 3
					}
				},
				{
					id: `${packageName}#aiagentRemote/logout`,
					service: "aiagentRemoteController",
					namespace: "aiagentRemote",
					method: "logout",
					invocation: { kind: "direct" },
					parameters: [],
					result: {
						mode: "strict",
						typeSymbol: `${packageName}#aiagentRemote/logout:result`,
						schema: empty
					},
					sourceLocation: {
						file: "src/remote.ts",
						line: 50,
						column: 3
					}
				},
				{
					id: `${packageName}#aiagentRemote/testCodex`,
					service: "aiagentRemoteController",
					namespace: "aiagentRemote",
					method: "testCodex",
					invocation: { kind: "direct" },
					parameters: [],
					result: {
						mode: "strict",
						typeSymbol: `${packageName}#AiAgentCodexTest`,
						schema: codexTest
					},
					sourceLocation: {
						file: "src/remote.ts",
						line: 78,
						column: 3
					}
				}
			]
		};
		//#endregion
		//#region src/client.tsx
		const inject = ["slots", "remote"];
		function unwrap(result) {
			if (!result.ok) throw new Error(result.error.message);
			return result.value;
		}
		async function apply(ctx) {
			const client = ctx;
			await client.remote.$mount(contribution);
			ctx.inject(["remote.aiagentRemote"], (remoteCtx) => {
				const mountedRemote = remoteCtx.remote.aiagentRemote;
				if (mountedRemote === void 0) throw new Error("AiAgent Remote API did not mount.");
				const api = mountedRemote;
				function AiAgentSettings() {
					const [status, setStatus] = (0, react.useState)();
					const [baseUrl, setBaseUrl] = (0, react.useState)("http://127.0.0.1:5000");
					const [username, setUsername] = (0, react.useState)("");
					const [password, setPassword] = (0, react.useState)("");
					const [error, setError] = (0, react.useState)("");
					const [busy, setBusy] = (0, react.useState)(false);
					const [codexTest, setCodexTest] = (0, react.useState)();
					(0, react.useEffect)(() => {
						api.status().then(unwrap).then((value) => {
							setStatus(value);
							setBaseUrl(value.baseUrl);
						}, () => {});
					}, []);
					const login = async () => {
						setBusy(true);
						setError("");
						try {
							setStatus(unwrap(await api.login(baseUrl, username, password)));
							setPassword("");
							setCodexTest(void 0);
						} catch (cause) {
							setError(cause instanceof Error ? cause.message : "登录失败。");
						} finally {
							setBusy(false);
						}
					};
					const testCodex = async () => {
						setBusy(true);
						setError("");
						setCodexTest(void 0);
						try {
							setCodexTest(unwrap(await api.testCodex()));
						} catch (cause) {
							setError(cause instanceof Error ? cause.message : "服务器 Codex CLI 测试失败。");
						} finally {
							setBusy(false);
						}
					};
					const logout = async () => {
						setBusy(true);
						setError("");
						try {
							unwrap(await api.logout());
							setStatus({
								configured: false,
								baseUrl,
								models: [],
								codexAvailable: false
							});
							setCodexTest(void 0);
						} catch (cause) {
							setError(cause instanceof Error ? cause.message : "退出失败。");
						} finally {
							setBusy(false);
						}
					};
					return /* @__PURE__ */ (0, react_jsx_runtime.jsxs)("section", {
						style: {
							padding: 16,
							maxWidth: 520
						},
						children: [
							/* @__PURE__ */ (0, react_jsx_runtime.jsx)("h2", { children: "AiAgent Remote" }),
							/* @__PURE__ */ (0, react_jsx_runtime.jsx)("p", { children: "登录信息仅用于换取短期令牌；密码不会写入 DSH 配置文件。" }),
							/* @__PURE__ */ (0, react_jsx_runtime.jsxs)("label", {
								style: {
									display: "block",
									marginTop: 12
								},
								children: ["AiAgent 地址", /* @__PURE__ */ (0, react_jsx_runtime.jsx)("input", {
									value: baseUrl,
									onChange: (event) => setBaseUrl(event.target.value),
									disabled: busy,
									style: {
										display: "block",
										width: "100%"
									}
								})]
							}),
							/* @__PURE__ */ (0, react_jsx_runtime.jsxs)("label", {
								style: {
									display: "block",
									marginTop: 12
								},
								children: ["用户名", /* @__PURE__ */ (0, react_jsx_runtime.jsx)("input", {
									value: username,
									onChange: (event) => setUsername(event.target.value),
									disabled: busy,
									style: {
										display: "block",
										width: "100%"
									}
								})]
							}),
							/* @__PURE__ */ (0, react_jsx_runtime.jsxs)("label", {
								style: {
									display: "block",
									marginTop: 12
								},
								children: ["密码", /* @__PURE__ */ (0, react_jsx_runtime.jsx)("input", {
									type: "password",
									value: password,
									onChange: (event) => setPassword(event.target.value),
									disabled: busy,
									style: {
										display: "block",
										width: "100%"
									}
								})]
							}),
							/* @__PURE__ */ (0, react_jsx_runtime.jsx)("p", {
								role: "status",
								children: status?.configured ? `已登录；可用模型：${status.models.map((model) => model.name).join("、") || "正在加载"}` : "尚未登录"
							}),
							/* @__PURE__ */ (0, react_jsx_runtime.jsxs)("section", {
								style: {
									borderTop: "1px solid #666",
									marginTop: 20,
									paddingTop: 16
								},
								children: [
									/* @__PURE__ */ (0, react_jsx_runtime.jsx)("h3", { children: "服务器 Codex CLI" }),
									/* @__PURE__ */ (0, react_jsx_runtime.jsx)("p", { children: status?.configured ? status.codexAvailable ? "已检测到服务器 Codex CLI；可供 aiagent_codex_delegate 工具委托。" : "服务器未检测到可用的 Codex CLI。" : "登录后可检测服务器 Codex CLI。" }),
									/* @__PURE__ */ (0, react_jsx_runtime.jsx)("button", {
										type: "button",
										onClick: () => {
											testCodex();
										},
										disabled: busy || status?.codexAvailable !== true,
										children: "测试服务器 Codex CLI"
									}),
									codexTest ? /* @__PURE__ */ (0, react_jsx_runtime.jsxs)("p", {
										role: "status",
										children: [
											"测试成功（",
											codexTest.modelId,
											"）：",
											codexTest.answer
										]
									}) : null,
									/* @__PURE__ */ (0, react_jsx_runtime.jsx)("p", { children: "测试不会传送本机文件，服务器 Codex 以只读方式运行。" })
								]
							}),
							error ? /* @__PURE__ */ (0, react_jsx_runtime.jsx)("p", {
								role: "alert",
								children: error
							}) : null,
							/* @__PURE__ */ (0, react_jsx_runtime.jsx)("button", {
								type: "button",
								onClick: () => {
									login();
								},
								disabled: busy,
								children: "登录并同步模型"
							}),
							status?.configured ? /* @__PURE__ */ (0, react_jsx_runtime.jsx)("button", {
								type: "button",
								onClick: () => {
									logout();
								},
								disabled: busy,
								style: { marginLeft: 8 },
								children: "退出"
							}) : null
						]
					});
				}
				client.slots.inject("settings.plugins.tab", () => client.slots.register({
					name: "settings.plugins.tab",
					id: "aiagent-remote",
					order: 10,
					label: () => "AiAgent Remote"
				}, AiAgentSettings));
			});
		}
		//#endregion
		exports.apply = apply;
		exports.inject = inject;
		return module.exports;
	}
});

//# sourceMappingURL=client.js.map