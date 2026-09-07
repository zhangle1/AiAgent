using System.Security.Cryptography;
using System.Text;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using SqlSugar;

namespace AiAgent.Backend.Services.AgentRuntime;

public interface INativeToolClaimStore
{
    bool TryClaim(RuntimeTurnContext turn, RuntimeStepContext step, ToolCall call, string invocationFingerprint);
    void Complete(RuntimeTurnContext turn, ToolCall call, string invocationFingerprint, bool succeeded);
}

/// <summary>
/// Records a claim before a tool is dispatched. A process restart may therefore
/// leave a claim in progress, but it can never cause that side effect to replay.
/// </summary>
public sealed class NativeToolClaimStore : INativeToolClaimStore
{
    private readonly ISqlSugarClient _db;

    public NativeToolClaimStore(ISqlSugarClient db) => _db = db;

    public bool TryClaim(RuntimeTurnContext turn, RuntimeStepContext step, ToolCall call, string invocationFingerprint)
    {
        if (string.IsNullOrWhiteSpace(turn.UserId) || string.IsNullOrWhiteSpace(turn.ThreadId) || string.IsNullOrWhiteSpace(turn.TurnId))
            throw new InvalidOperationException("A native tool claim requires an authenticated turn owner.");

        var id = CreateId(turn, invocationFingerprint);
        var now = DateTime.UtcNow;
        try
        {
            _db.Insertable(new AiAgentToolClaim
            {
                Id = id, RunId = turn.RunId, UserId = turn.UserId, SessionId = turn.ThreadId, TurnId = turn.TurnId,
                ToolCallId = call.Id, InvocationFingerprint = invocationFingerprint, ToolName = call.Name,
                StepNumber = step.StepNumber, Status = "executing", CreatedAt = now, UpdatedAt = now
            }).ExecuteCommand();
            return true;
        }
        catch
        {
            // A duplicate primary key is a prior claim for this exact owner,
            // turn, and canonical invocation. Do not treat unrelated storage
            // failures as duplicates.
            if (_db.Queryable<AiAgentToolClaim>().Any(x => x.Id == id && x.UserId == turn.UserId && x.SessionId == turn.ThreadId && x.TurnId == turn.TurnId))
                return false;
            throw;
        }
    }

    public void Complete(RuntimeTurnContext turn, ToolCall call, string invocationFingerprint, bool succeeded)
    {
        var id = CreateId(turn, invocationFingerprint);
        _db.Updateable<AiAgentToolClaim>()
            .SetColumns(x => new AiAgentToolClaim { Status = succeeded ? "completed" : "failed", UpdatedAt = DateTime.UtcNow })
            .Where(x => x.Id == id && x.UserId == turn.UserId && x.SessionId == turn.ThreadId && x.TurnId == turn.TurnId)
            .ExecuteCommand();
    }

    private static string CreateId(RuntimeTurnContext turn, string invocationFingerprint)
    {
        var material = string.Join("\n", turn.UserId, turn.ThreadId, turn.TurnId, invocationFingerprint);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
}
