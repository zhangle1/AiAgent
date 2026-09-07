using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.AgentRuntime;

/// <summary>One immutable tool visibility snapshot used both for model schema and execution authorization.</summary>
public sealed record NativeToolPlan(IReadOnlyList<ToolDefinition> Definitions, string SnapshotId)
{
    public bool Contains(string toolName) => Definitions.Any(definition => string.Equals(definition.Name, toolName, StringComparison.OrdinalIgnoreCase));
}

public sealed record NativeToolExecution(ToolCall Call, ToolResult Result, IReadOnlyList<KnowledgeCitationDto> Citations);

public interface INativeToolRouter
{
    NativeToolPlan BuildPlan(AgentContext context);
    Task<NativeToolExecution> ExecuteAsync(AgentContext context, NativeToolPlan plan, ToolCall call, CancellationToken cancellationToken);
}

/// <summary>
/// Transitional router around the existing domain dispatcher. It makes the V2
/// execution permission explicit before the legacy tools are migrated one by one.
/// </summary>
public sealed class NativeToolRouter : INativeToolRouter
{
    private const int SafeReadToolMaxAttempts = 2;
    private readonly IToolDispatcher _legacyDispatcher;
    private readonly TimeSpan _timeout;

    public NativeToolRouter(IToolDispatcher legacyDispatcher) : this(legacyDispatcher, TimeSpan.FromSeconds(90)) { }

    public NativeToolRouter(IToolDispatcher legacyDispatcher, IConfiguration configuration)
        : this(legacyDispatcher, TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("AgentRuntime:NativeV2ToolTimeoutSeconds", 90), 5, 900))) { }

    internal NativeToolRouter(IToolDispatcher legacyDispatcher, TimeSpan timeout)
    {
        _legacyDispatcher = legacyDispatcher;
        _timeout = timeout;
    }

    public NativeToolPlan BuildPlan(AgentContext context)
    {
        var definitions = _legacyDispatcher.GetDefinitions(context).ToArray();
        return new NativeToolPlan(definitions, RuntimeStepContext.CreateToolSnapshotId(definitions));
    }

    public async Task<NativeToolExecution> ExecuteAsync(AgentContext context, NativeToolPlan plan, ToolCall call, CancellationToken cancellationToken)
    {
        if (!plan.Contains(call.Name))
        {
            var denied = ToolResult.Failed($"Tool '{call.Name}' is not available in this turn.");
            return new NativeToolExecution(call, denied, []);
        }

        for (var attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                var outcome = await _legacyDispatcher.DispatchAsync(context, [call], timeout.Token);
                var result = outcome.Results.FirstOrDefault() ?? ToolResult.Failed("The tool dispatcher produced no result.");
                return new NativeToolExecution(call, result, outcome.Citations);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!CanRetry(call, attempt))
            {
                return new NativeToolExecution(call, ToolResult.Failed($"Tool '{call.Name}' timed out."), []);
            }
            catch (OperationCanceledException) when (CanRetry(call, attempt))
            {
                // Own timeout on a declared read-only operation: retry once.
            }
            catch (Exception exception) when (IsTransient(exception) && CanRetry(call, attempt))
            {
                // The next attempt receives a fresh linked timeout. This is only
                // allowed for declared read-only tools, never for a write.
            }
        }
    }

    private static bool CanRetry(ToolCall call, int attempt) => NativeToolExecutionPolicy.IsSafeReadOnly(call) && attempt < SafeReadToolMaxAttempts;
    private static bool IsTransient(Exception exception) => exception is HttpRequestException or TimeoutException;
}
