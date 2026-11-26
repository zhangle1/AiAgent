using System.Text.RegularExpressions;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.Chat;

public sealed class CodexAgentProviderDriver(IConfiguration configuration, Func<ICodexChatService> execution) : IAgentProviderDriver
{
    internal const string KindId = "codex";
    public string Kind => KindId;
    public IAgentProviderInstance Create(string instanceId) => new Instance(instanceId, configuration, execution);

    internal static string ResolveCommand(IConfiguration configuration)
    {
        var configured = configuration["Codex:Command"] ?? Environment.GetEnvironmentVariable("AIAGENT_CODEX_COMMAND");
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

        // npm on Windows exposes a .cmd launcher. Prefer the known npm locations so a
        // long-running IDE/backend process does not accidentally resolve an App Store alias.
        var npmCommand = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "codex.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd")
        }.FirstOrDefault(File.Exists);
        return npmCommand ?? "codex";
    }

    private sealed class Instance(string id, IConfiguration configuration, Func<ICodexChatService> execution) : IAgentProviderInstance
    {
        public string Id => id;

        public async Task<AgentProviderEnvironmentDto> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            var probe = await AgentCliProbe.ProbeAsync([ResolveCommand(configuration)], cancellationToken);
            var version = Regex.Match(probe.Version ?? "", @"^(?:codex(?:-cli)?\s+)?(\d+\.\d+\.\d+(?:[-+][\w.-]+)?)$", RegexOptions.IgnoreCase);
            return new AgentProviderEnvironmentDto
            {
                Id = Id, Name = "Codex", Command = "codex", Protocol = "app-server JSONL",
                Installed = probe.Installed, ChatSupported = probe.Installed,
                Version = version.Success ? version.Groups[1].Value : null,
                Message = probe.Installed ? "已检测到 Codex CLI。" : "无法运行配置的 Codex CLI，请检查安装与启动命令。"
            };
        }

        public Task<ChatCompleteResponse> ExecuteAsync(ChatCompleteRequest request, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken) =>
            execution().CompleteAsync(request, onEvent, cancellationToken);
    }
}
