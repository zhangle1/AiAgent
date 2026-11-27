using System.Text;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.Chat.Orchestration;

// One collector per turn. Forward each event as it arrives, retain only the fields needed to save the answer.
internal sealed class ChatProviderOutput(ChatDebugTrace? trace, AgentStreamEventHandler emit, bool batchText = true)
{
    private readonly ChatTextBatch? batch = batchText ? new(emit) : null;
    private readonly StringBuilder content = new();
    private readonly StringBuilder thinking = new();
    internal string? Content => content.Length > 0 ? content.ToString() : null;
    internal string Thinking => thinking.ToString();
    internal object? Citations { get; private set; }
    internal string? ModelId { get; private set; }
    internal string? Model { get; private set; }
    internal bool ProviderStarted { get; private set; }

    internal async Task ReceiveAsync(AgentStreamEvent item, CancellationToken token)
    {
        if (item.Type != "content") await FlushAsync();
        if (item.Type == "provider_request_started")
        {
            if (!ProviderStarted)
            {
                ProviderStarted = true;
                if (trace is not null) await emit(trace.Start("provider_request_started"), token);
            }
            return;
        }
        if (item.Type == "debug_trace") trace?.RecordProviderDiagnostic(item.DebugTrace);
        else if (ProviderStarted && trace?.FirstStreamEvent() is { } first) await emit(first, token);

        switch (item.Type)
        {
            case "content": content.Append(item.Content); break;
            case "thinking": thinking.Append(item.Content); break;
            case "done" when item.Metadata.TryGetValue("agent", out var agent) && Equals(agent, "codex") && !string.IsNullOrEmpty(item.Content):
                content.Clear().Append(item.Content);
                break;
            case "sources": Citations = item.Citations; break;
        }
        ModelId ??= item.ModelId;
        Model ??= item.Model;
        if (batch != null && item.Type == "content") await batch.SendContentAsync(item, token);
        else await emit(item, token);
    }

    internal Task FlushAsync() => batch?.FlushAsync() ?? Task.CompletedTask;

    internal async Task CompleteProviderAsync(CancellationToken token)
    {
        await FlushAsync();
        if (trace is not null) await emit(trace.CompleteProvider(), token);
        ProviderStarted = false;
    }
}
