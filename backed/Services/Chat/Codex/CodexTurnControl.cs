using System.Text.Json;
using AiAgent.Backend.Dtos.Chat;

namespace AiAgent.Backend.Services.Chat;

public sealed class CodexConversationContext
{
    public required Func<string, string?> LoadThread { get; init; }
    public required Action<string, string> SaveThread { get; init; }
    internal static string SelectionKey(ChatCompleteRequest request) => JsonSerializer.Serialize(new {
        request.CodeProjectId, request.KnowledgeBaseName, request.KnowledgeBaseNames, request.KnowledgeScopeUri, request.KnowledgeScopeUris,
        request.CodeRepositoryNames, request.ProjectReferences, request.MarkdownDocumentReferences, request.DashboardApplicationId,
        request.DashboardFilePath, request.DashboardWorkspaceRevision, request.Mode, request.SkillInvocation, request.SkillInvocations
    });
}

/// <summary>One live, server-owned native turn. Never accepts thread or turn IDs from a browser.</summary>
public sealed class CodexTurnControl
{
    private readonly object _gate = new();
    private Func<ChatCompleteRequest, CancellationToken, Task>? _send;
    private Func<bool>? _ready;

    public bool Ready { get { lock (_gate) return _send is not null && _ready?.Invoke() == true; } }

    public IDisposable Bind(Func<bool> ready, Func<ChatCompleteRequest, CancellationToken, Task> send)
    {
        lock (_gate) { _ready = ready; _send = send; }
        return new Binding(this, send);
    }

    public Task SteerAsync(ChatCompleteRequest request, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_send is null || _ready?.Invoke() != true) throw new InvalidOperationException("当前原生轮次尚不可追加，请改用排队继续。");
            return _send(request, ct);
        }
    }

    private sealed class Binding(CodexTurnControl owner, Func<ChatCompleteRequest, CancellationToken, Task> send) : IDisposable
    {
        public void Dispose() { lock (owner._gate) if (ReferenceEquals(owner._send, send)) { owner._send = null; owner._ready = null; } }
    }
}
