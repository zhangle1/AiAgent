using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Tests;

public sealed class KnowledgeScopeContextTests
{
    [Fact]
    public void Empty_scope_keeps_legacy_context()
    {
        var context = AgentContext.FromRequest(new ChatCompleteRequest { Message = "检查代码", KnowledgeScopeUri = "  " });

        Assert.Null(context.KnowledgeScopeUri);
    }

    [Fact]
    public void Scope_uri_is_trimmed_and_preserved_for_non_codex_context()
    {
        var context = AgentContext.FromRequest(new ChatCompleteRequest
        {
            Message = "按资料检查代码",
            KnowledgeScopeUri = "  viking://resources/books/refactoring/  "
        });

        Assert.Equal("viking://resources/books/refactoring/", context.KnowledgeScopeUri);
    }
}
