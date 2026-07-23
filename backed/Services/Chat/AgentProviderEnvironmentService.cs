using AiAgent.Backend.Dtos.Chat;
using System.Diagnostics;

namespace AiAgent.Backend.Services.Chat;

public interface IAgentProviderEnvironmentService
{
    Task<List<AgentProviderEnvironmentDto>> GetEnvironmentsAsync(CancellationToken cancellationToken);
}

/// <summary>Probes only configured or well-known local CLI launchers; no user text is executed.</summary>
public sealed class AgentProviderEnvironmentService : IAgentProviderEnvironmentService
{
    private readonly IConfiguration _configuration;

    public AgentProviderEnvironmentService(IConfiguration configuration) => _configuration = configuration;

    public async Task<List<AgentProviderEnvironmentDto>> GetEnvironmentsAsync(CancellationToken cancellationToken)
    {
        var codex = await ProbeAsync(CodexCandidates(), cancellationToken);
        var codeBuddy = await ProbeAsync(CodeBuddyCandidates(), cancellationToken);
        return
        [
            new AgentProviderEnvironmentDto
            {
                Id = "codex", Name = "Codex", Command = codex.Command, Installed = codex.Installed, Version = codex.Version,
                Protocol = "app-server JSONL", ChatSupported = codex.Installed,
                Message = codex.Installed ? "已检测到 app-server CLI，可在聊天中接管项目。" : "未检测到 Codex CLI；请完成安装和登录后刷新。"
            },
            new AgentProviderEnvironmentDto
            {
                Id = "codebuddy", Name = "CodeBuddy Code", Command = codeBuddy.Command, Installed = codeBuddy.Installed, Version = codeBuddy.Version,
                Protocol = "交互式 CLI（待适配）", ChatSupported = false,
                Message = codeBuddy.Installed ? "已检测到 CodeBuddy CLI，但官方公开文档未提供与 Codex app-server 相同的 JSONL 协议，暂不能用于聊天接管。" : "未检测到 CodeBuddy CLI；安装命令：npm install -g @tencent-ai/codebuddy-code"
            }
        ];
    }

    private IEnumerable<string> CodexCandidates()
    {
        yield return _configuration["Codex:Command"] ?? Environment.GetEnvironmentVariable("AIAGENT_CODEX_COMMAND") ?? string.Empty;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "codex.cmd");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd");
        yield return "codex";
    }

    private IEnumerable<string> CodeBuddyCandidates()
    {
        yield return _configuration["CodeBuddy:Command"] ?? Environment.GetEnvironmentVariable("AIAGENT_CODEBUDDY_COMMAND") ?? string.Empty;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "codebuddy.cmd");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codebuddy.cmd");
        yield return "codebuddy";
    }

    private static async Task<(bool Installed, string Command, string? Version)> ProbeAsync(IEnumerable<string> candidates, CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new ProcessStartInfo { FileName = candidate, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                info.ArgumentList.Add("--version");
                using var process = Process.Start(info);
                if (process is null) continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var version = (await output).Trim();
                if (version.Length == 0) version = (await errors).Trim();
                return (true, candidate, version.Length == 0 ? null : version[..Math.Min(version.Length, 180)]);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
        }
        return (false, candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty, null);
    }
}
