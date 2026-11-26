using AiAgent.Backend.Dtos.Chat;

namespace AiAgent.Backend.Services.Chat;

public interface IAgentProviderEnvironmentService
{
    Task<List<AgentProviderEnvironmentDto>> GetEnvironmentsAsync(CancellationToken cancellationToken);
}

/// <summary>Probes only configured or well-known local CLI launchers; no user text is executed.</summary>
public sealed class AgentProviderEnvironmentService : IAgentProviderEnvironmentService
{
    private readonly IConfiguration _configuration;

    private readonly AgentProviderRegistry _providers;

    public AgentProviderEnvironmentService(IConfiguration configuration, AgentProviderRegistry providers)
    {
        _configuration = configuration;
        _providers = providers;
    }

    public async Task<List<AgentProviderEnvironmentDto>> GetEnvironmentsAsync(CancellationToken cancellationToken)
    {
        var codex = await _providers.GetCodexSnapshotAsync(cancellationToken);
        var dsh = await AgentCliProbe.ProbeAsync(DshCandidates(), cancellationToken);
        var codeBuddy = await AgentCliProbe.ProbeAsync(CodeBuddyCandidates(), cancellationToken);
        var dshConfigured = IsDshConfigured();
        return
        [
            codex,
            new AgentProviderEnvironmentDto
            {
                Id = "deepseek-harness", Name = "DeepSeek Harness", Command = dsh.Command, Installed = dsh.Installed, Version = dsh.Version,
                Protocol = "SDK JSON-RPC stdio", ChatSupported = dsh.Installed && dshConfigured,
                Message = !dsh.Installed ? "DeepSeek Harness SDK JSON-RPC command was not found." : !dshConfigured ? "DeepSeek Harness is installed but the server-side Dsh configuration is incomplete." : "DeepSeek Harness SDK JSON-RPC runtime is ready for registered code projects."
            },
            new AgentProviderEnvironmentDto
            {
                Id = "codebuddy", Name = "CodeBuddy Code", Command = codeBuddy.Command, Installed = codeBuddy.Installed, Version = codeBuddy.Version,
                Protocol = "交互式 CLI（待适配）", ChatSupported = false,
                Message = codeBuddy.Installed ? "已检测到 CodeBuddy CLI，但官方公开文档未提供与 Codex app-server 相同的 JSONL 协议，暂不能用于聊天接管。" : "未检测到 CodeBuddy CLI；安装命令：npm install -g @tencent-ai/codebuddy-code"
            }
        ];
    }

    private IEnumerable<string> CodeBuddyCandidates()
    {
        yield return _configuration["CodeBuddy:Command"] ?? Environment.GetEnvironmentVariable("AIAGENT_CODEBUDDY_COMMAND") ?? string.Empty;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "codebuddy.cmd");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codebuddy.cmd");
        yield return "codebuddy";
    }

    private IEnumerable<string> DshCandidates()
    {
        return DshCommandLocator.Candidates(_configuration["Dsh:Command"] ?? Environment.GetEnvironmentVariable("AIAGENT_DSH_COMMAND"));
    }

    private bool IsDshConfigured()
    {
        if (!bool.TryParse(_configuration["Dsh:Enabled"], out var enabled) || !enabled) return false;
        var configPath = _configuration["Dsh:ConfigPath"] ?? Environment.GetEnvironmentVariable("AIAGENT_DSH_CONFIG_PATH");
        try
        {
            return !string.IsNullOrWhiteSpace(configPath)
                   && File.Exists(Path.GetFullPath(configPath))
                   && !string.IsNullOrWhiteSpace(_configuration["Dsh:SessionRoot"]);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
