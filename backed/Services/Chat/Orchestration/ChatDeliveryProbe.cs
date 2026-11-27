using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AiAgent.Backend.Services.Chat.Orchestration;

// One sample per milestone of the original submission. Replays and queue heartbeats
// must not inflate the latency distribution. No text, account or command ID is exported.
internal sealed class ChatDeliveryProbe(long start, ChatDebugTrace? trace)
{
    private static readonly Meter Meter = new("AiAgent.Chat.Orchestration");
    private static readonly Histogram<double> Elapsed = Meter.CreateHistogram<double>("aiagent.chat.delivery.elapsed", "ms");
    private bool firstText;

    internal static ChatDeliveryProbe? Create(long start, bool owner, ChatDebugTrace? trace) =>
        owner && (Elapsed.Enabled || trace != null) ? new(start, trace) : null;

    internal void Observe(string type, bool hasText = false)
    {
        if (!firstText && hasText && type is ("content" or "done"))
        {
            firstText = true;
            Record("first_content");
        }
        var stage = type switch
        {
            "delivery_accepted" => "accepted",
            "delivery_started" => "started",
            "done" => "provider_done",
            "completed" or "cancelled" or "error" or "delivery_held" or "delivery_steered" => type,
            _ => null
        };
        if (stage != null) Record(stage);
    }

    private void Record(string stage)
    {
        Elapsed.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, new KeyValuePair<string, object?>("stage", stage));
        // The existing debug trace is saved during finalization, before terminal delivery.
        if (stage is "accepted" or "started" or "first_content" or "provider_done")
            trace?.Complete("delivery_probe_" + stage);
    }
}
