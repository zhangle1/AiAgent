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

    [Fact]
    public void Multiple_scope_uris_are_trimmed_deduplicated_and_ordered()
    {
        var context = AgentContext.FromRequest(new ChatCompleteRequest
        {
            Message = "按多组资料检查代码",
            KnowledgeScopeUris = [" viking://resources/books/ ", "viking://resources/books/", "viking://resources/checklists/"]
        });

        Assert.Equal(["viking://resources/books/", "viking://resources/checklists/"], context.KnowledgeScopeUris);
        Assert.Equal("viking://resources/books/", context.KnowledgeScopeUri);
    }
}
