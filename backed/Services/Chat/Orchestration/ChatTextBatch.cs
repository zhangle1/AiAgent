using System.Text;
using System.Threading.Channels;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.Chat.Orchestration;

// A turn has one producer. Deliver the first fragment directly, then combine only
// compatible text already available while the previous database write was in flight.
internal sealed class ChatTextBatch(AgentStreamEventHandler emit)
{
    private const int MaxChars = 16 * 1024;
    private AgentStreamEvent? first;
    private Channel<string>? queue;
    private Task? writer;

    internal async Task SendContentAsync(AgentStreamEvent item, CancellationToken token)
    {
        if (item.Citations != null || item.DebugTrace != null || item.Content.Length >= MaxChars)
        {
            await FlushAsync();
            await emit(item, token);
            return;
        }
        if (first == null || !Compatible(first, item))
        {
            await FlushAsync();
            await emit(item, token);
            first = item;
            return;
        }
        if (queue == null)
        {
            queue = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { SingleReader = true, SingleWriter = true });
            writer = DrainAsync(queue, first, token);
        }
        await queue.Writer.WriteAsync(item.Content, token);
    }

    internal async Task FlushAsync()
    {
        first = null;
        if (queue == null) return;
        queue.Writer.TryComplete();
        var pending = writer!;
        queue = null;
        writer = null;
        await pending;
    }

    private static bool Compatible(AgentStreamEvent left, AgentStreamEvent right) =>
        left.Model == right.Model && left.ModelId == right.ModelId && left.Label == right.Label &&
        left.KnowledgeBaseName == right.KnowledgeBaseName && left.Metadata.Count == right.Metadata.Count &&
        left.Metadata.All(pair => right.Metadata.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value));

    private async Task DrainAsync(Channel<string> channel, AgentStreamEvent template, CancellationToken token)
    {
        try
        {
            await foreach (var part in channel.Reader.ReadAllAsync(token))
            {
                var text = new StringBuilder(part);
                while (channel.Reader.TryPeek(out var next) && next.Length <= MaxChars - text.Length &&
                    channel.Reader.TryRead(out next)) text.Append(next);
                await emit(new AgentStreamEvent { Type = "content", Content = text.ToString(), ModelId = template.ModelId,
                    Model = template.Model, Label = template.Label, Metadata = template.Metadata, KnowledgeBaseName = template.KnowledgeBaseName }, token);
            }
        }
        catch (Exception error)
        {
            channel.Writer.TryComplete(error);
            throw;
        }
    }
}
