using System.Collections.Concurrent;
using System.Text.Json;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.Chat;

public sealed partial class CodexChatService
{
    private sealed partial class CodexAppServerWorker
    {
        private async Task ReadSteerableTurnAsync(int requestId, string threadId, CodexRunState state, CodexTurnControl control,
            AgentStreamEventHandler? onEvent, CancellationToken ct)
        {
            var responses = new ConcurrentDictionary<int, TaskCompletionSource<JsonElement>>();
            using var writes = new SemaphoreSlim(1, 1);
            using var binding = control.Bind(() => state.ActiveTurn && !string.IsNullOrWhiteSpace(state.ActiveTurnId) && IsUsable,
                async (text, token) => {
                    var turnId = state.ActiveTurnId;
                    if (!state.ActiveTurn || string.IsNullOrWhiteSpace(turnId)) throw new InvalidOperationException("原生轮次已结束，未追加指令。");
                    if (responses.Count >= 16) throw new InvalidOperationException("追加请求过多，请等待确认。");
                    var id = NextRequestId(); var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                    responses[id] = completion;
                    await writes.WaitAsync(token);
                    try
                    {
                        if (!state.ActiveTurn || state.ActiveTurnId != turnId) throw new InvalidOperationException("原生轮次已变化，未追加指令。");
                        await SendAsync(_process, new { id, method = "turn/steer", @params = new { threadId, expectedTurnId = turnId,
                            input = new[] { new { type = "text", text } } } }, token);
                    }
                    catch { responses.TryRemove(id, out _); throw; }
                    finally { writes.Release(); }
                    await completion.Task.WaitAsync(token);
                });
            var accepted = false; var completed = false;
            try
            {
                while (!accepted || !completed)
                {
                    using var message = await ReadMessageAsync(_stdout, _turnIdleTimeout, ct);
                    var root = message.RootElement;
                    if (root.TryGetProperty("id", out var responseId) && responseId.TryGetInt32(out var id) && responses.TryRemove(id, out var pending))
                    {
                        try { if (TryGetResponse(root, id, out var value)) pending.TrySetResult(value.Clone()); }
                        catch (Exception ex) { pending.TrySetException(ex); }
                    }
                    else if (TryGetResponse(root, requestId, out var started))
                    {
                        accepted = true;
                        if (!completed && started.TryGetProperty("turn", out var turn)) { state.ActiveTurnId = ReadString(turn, "id") ?? state.ActiveTurnId; state.ActiveTurn = true; }
                    }
                    else if (await HandleNotificationAsync(root, state, onEvent, ct)) completed = true;
                }
            }
            finally
            {
                state.ActiveTurn = false;
                foreach (var pending in responses.Values) pending.TrySetException(new InvalidOperationException("轮次已结束，追加指令的确认结果未知。"));
            }
        }
    }
}
