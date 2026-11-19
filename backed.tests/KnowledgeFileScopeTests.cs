using System.Reflection;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Chat.Retrieval;
using AiAgent.Backend.Services.Knowledge;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public void FileScopeMatchesOneDocumentRatherThanItsPrefixOrSiblings()
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        var kb = CreateBase(db, "resource-store");
        long Add(string uri)
        {
            var doc = new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, ResourceUri = uri,
                OriginalFileName = uri.Split('/').Last(), Extension = ".pdf" };
            return db.Insertable(doc).ExecuteReturnBigIdentity();
        }
        const string book = "viking://resources/books/one.pdf";
        var wanted = Add(book);
        Add(book + "-other.pdf");
        var sibling = Add("viking://resources/books/two.pdf");
        var rootFile = Add("viking://resources/root.pdf");
        var resources = new KnowledgeResourceService(db, null!, null!, null!, null!, null!, null!,
            NullLogger<KnowledgeBaseManager>.Instance, new HttpContextAccessor(), null!);
        var retrieval = new KnowledgeRetrievalService(db, null!, null!, resources);
        var method = typeof(KnowledgeRetrievalService).GetMethod("ResolveScopeDocumentIds", BindingFlags.Instance | BindingFlags.NonPublic)!;
        HashSet<long> Scope(params string[] uris) => (HashSet<long>)method.Invoke(retrieval, [new AgentContext { KnowledgeScopeUris = uris.ToList() }, kb.Name])!;

        Assert.Equal([wanted], Scope(book));
        Assert.Equal(new HashSet<long> { wanted, rootFile }, Scope(book, "viking://resources/root.pdf"));
        Assert.Equal(3, Scope("viking://resources/books/").Count);
        Assert.Equal(3, Scope("viking://resources/books").Count);
        Assert.DoesNotContain(sibling, Scope(book));
        Assert.Empty((HashSet<long>)method.Invoke(retrieval, [new AgentContext { KnowledgeScopeUris = [book] }, "another-base"])!);
        foreach (var denied in new[] { "viking://resources/missing.pdf", "viking://user/someone/private.pdf" })
            Assert.IsType<KeyNotFoundException>(Assert.Throws<TargetInvocationException>(() => Scope(denied)).InnerException);
    }
}
