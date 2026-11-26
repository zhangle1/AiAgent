using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.Chat;

public interface IAgentProviderDriver
{
    string Kind { get; }
    IAgentProviderInstance Create(string instanceId);
}

public interface IAgentProviderInstance
{
    string Id { get; }
    Task<AgentProviderEnvironmentDto> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<ChatCompleteResponse> ExecuteAsync(ChatCompleteRequest request, AgentStreamEventHandler? onEvent, CancellationToken cancellationToken);
}

public sealed class AgentProviderRegistry(IEnumerable<IAgentProviderDriver> drivers)
{
    // The legacy agent name selects the one server-configured default for that driver.
    private readonly Dictionary<string, IAgentProviderDriver> _drivers = drivers.ToDictionary(driver => driver.Kind, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IAgentProviderInstance> _defaults = new(StringComparer.OrdinalIgnoreCase);

    public IAgentProviderInstance? ResolveDefault(string kind)
    {
        if (!_drivers.TryGetValue(kind, out var driver)) return null;
        lock (_defaults)
        {
            if (_defaults.TryGetValue(kind, out var instance)) return instance;
            try { instance = driver.Create(driver.Kind); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new InvalidOperationException("Agent Provider 实例创建失败，请检查服务端配置。", error);
            }
            _defaults.Add(kind, instance);
            return instance;
        }
    }

    internal async Task<AgentProviderEnvironmentDto> GetCodexSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (ResolveDefault(CodexAgentProviderDriver.KindId) is { } instance)
                return await instance.GetSnapshotAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Only the public availability reason belongs in the environment response.
        }
        return new AgentProviderEnvironmentDto { Id = CodexAgentProviderDriver.KindId, Name = "Codex", Command = "codex",
            Protocol = "app-server JSONL", Message = "Codex 实例状态读取失败，请检查服务端配置。" };
    }
}
