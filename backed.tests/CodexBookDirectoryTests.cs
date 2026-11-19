using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Knowledge;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CodexReadsOnlySelectedBookChaptersFromTurnInput(bool steer)
    {
        using var db = Database();
        using var files = new SemanticFiles();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory), typeof(AiUser), typeof(AiUserSession));
        var user = new AuthenticatedUser("reader", "reader");
        var token = Guid.NewGuid().ToString("N");
        db.Insertable(new AiUser { Id = user.Id, Username = user.Username }).ExecuteCommand();
        db.Insertable(new AiUserSession { UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), ExpiresAt = DateTime.UtcNow.AddMinutes(10) }).ExecuteCommand();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Request.Headers.Authorization = "Bearer " + token;
        var kb = CreateBase(db);
        var doc = files.Document(kb, "viking://resources/books/manual.md");
        var marker = "CHAPTER-" + Guid.NewGuid().ToString("N");
        var sibling = "SIBLING-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(doc.StoragePath, "# Production\n\n## Limits\n\n" + marker + new string('a', 1800) + "\n\n# Finance\n\n## Cost\n\n" + sibling + new string('b', 1800));
        doc.FileHash = files.Paths.ComputeFileHash(doc.StoragePath);
        doc.Id = db.Insertable(doc).ExecuteReturnBigIdentity();
        var settings = new KnowledgeCompilerSettings(db);
        var ingestion = new KnowledgeIngestionService(db, files.Paths, null!, new Llm(), new Codex(), settings);
        await ingestion.ParseResourceAsync(kb, doc, new() { Parsing = new() { MaxSectionChars = 800, MaxSectionTokens = 400, MinSectionTokens = 10 } }, default);
        var resources = new KnowledgeResourceService(db, files.Paths, ingestion, null!, settings, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, accessor, null!);
        var all = resources.ResolveBookChapters(user, new() { KnowledgeScopeUris = [doc.ResourceUri!] }, default);
        var chosen = all.Single(chapter => File.ReadAllText(chapter.Source).Contains(marker));
        var node = resources.Tree().Single(item => item.Kind == "file" && item.SourceUri == doc.ResourceUri && !item.IsNavigation && item.Name == Path.GetFileName(chosen.RelativePath));
        var scope = node.ParentUri!;
        using var fixture = new ChatGoalTests.Fixture(steer ? "book-hang" : "book");
        var codeMarker = "CODE-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(fixture.Root, "Capacity.cs"), "// " + codeMarker);
        using var service = fixture.Service();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        ChatCompleteRequest Request()
        {
            var request = fixture.Request();
            request.KnowledgeScopeUris = [scope];
            request.BookChapters = resources.ResolveBookChapters(user, request, timeout.Token);
            Assert.Contains(chosen, request.BookChapters);
            Assert.DoesNotContain(request.BookChapters, c => File.ReadAllText(c.Source).Contains(sibling));
            return request;
        }
        var first = Request();
        first.TurnControl = new();
        var running = service.CompleteAsync(first, null, timeout.Token);
        var submitted = first;
        if (steer)
        {
            while (!first.TurnControl.Ready) await Task.Delay(10, timeout.Token);
            submitted = Request();
            await first.TurnControl.SteerAsync(submitted, timeout.Token);
        }
        var result = await running.WaitAsync(timeout.Token);
        if (steer) Assert.Contains("read-after-ack:", result.Content);
        Assert.Contains(marker, result.Content);
        Assert.Contains(codeMarker, result.Content);
        if (steer) Assert.Contains(marker, result.Content.Split("read-after-ack:").Last());
        Assert.DoesNotContain(sibling, result.Content);
        var rpc = fixture.Calls().Single(call => call.GetProperty("method").GetString() == (steer ? "turn/steer" : "turn/start"));
        var prompt = rpc.GetProperty("params").GetProperty("input")[0].GetProperty("text").GetString()!;
        Assert.DoesNotContain(marker, prompt);
        Assert.DoesNotContain(codeMarker, prompt);
        Assert.DoesNotContain(chosen.Source, prompt);
        Assert.DoesNotContain("BookDirectory", JsonSerializer.Serialize(submitted), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(submitted.BookDirectory));
        Assert.False(Directory.Exists(first.BookDirectory));
        var unpublished = files.Document(kb, "viking://resources/drafts/unpublished.md");
        db.Insertable(unpublished).ExecuteCommand();
        Assert.Throws<InvalidOperationException>(() => resources.ResolveBookChapters(user, new() { KnowledgeScopeUris = [scope, unpublished.ResourceUri!] }, default));
        Assert.Throws<UnauthorizedAccessException>(() => resources.ResolveBookChapters(new("other", "other"), submitted, default));
        Assert.Throws<InvalidOperationException>(() => resources.ResolveBookChapters(user, new() { KnowledgeScopeUris = ["viking://user/other/private/"] }, default));
        File.AppendAllText(chosen.Source, "tampered");
        Assert.Throws<InvalidOperationException>(() => resources.ResolveBookChapters(user, submitted, default));
    }
}
