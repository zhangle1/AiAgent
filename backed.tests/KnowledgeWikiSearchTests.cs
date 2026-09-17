using AiAgent.Backend.Services.Knowledge.Core;
using System.Text.Json;

namespace AiAgent.Backend.Tests;

public sealed class KnowledgeWikiSearchTests
{
    private const string Evidence = "The project uses a reusable knowledge representation.";
    private sealed class Model(params object[] commands) : IKnowledgeModel
    {
        public List<string> Prompts { get; } = [];
        public Task<CompilerReply> CompleteAsync(string prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return Task.FromResult(new CompilerReply(JsonSerializer.Serialize(commands[Math.Min(Prompts.Count - 1, commands.Length - 1)])));
        }
    }

    [Fact]
    public async Task RejectsUnreadAndInventedEvidenceAndReturnsOnlyVerifiedQuote()
    {
        var model = new Model(
            new { action = "cite", id = "1", quote = Evidence },
            new { action = "read", id = "1", part = 1 },
            new { action = "cite", id = "1", quote = "Invented facts are never accepted." },
            new { action = "cite", id = "1", quote = Evidence },
            new { action = "finish" });
        var hits = await new KnowledgeWikiSearch().SearchAsync("project", [new("1", "Project", Evidence)], model, 5, 8, default);
        Assert.Equal(Evidence, Assert.Single(hits).Quote);
        Assert.Contains("Rejected", model.Prompts[1]);
        Assert.Contains("Rejected", model.Prompts[3]);
    }

    [Fact]
    public async Task SearchesBeyondFirstCatalogPageAndReadsLaterParts()
    {
        var pages = Enumerable.Range(1, 80).Select(i => new WikiSearchPage(i.ToString(), "Page " + i, new string('x', 6000) + Evidence)).ToList();
        var model = new Model(new { action = "search", term = "80", offset = 0 },
            new { action = "read", id = "80", part = 2 },
            new { action = "cite", id = "80", quote = Evidence }, new { action = "finish" });
        var hits = await new KnowledgeWikiSearch().SearchAsync("80", pages, model, 1, 8, default);
        Assert.Equal("80", Assert.Single(hits).Id);
        Assert.Contains("Page 80", model.Prompts[1]);
    }

    [Fact]
    public async Task UnknownPathsAndRepeatedInvalidCommandsExhaustBudget()
    {
        var model = new Model(new { action = "read", id = "../../raw/secret", part = 1 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KnowledgeWikiSearch().SearchAsync("project",
            [new("1", "Project", Evidence)], model, 5, 8, default));
        Assert.Equal(8, model.Prompts.Count);
    }

    [Fact]
    public async Task HonorsCancellationBeforeCallingModel()
    {
        var model = new Model(new { action = "finish" });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new KnowledgeWikiSearch().SearchAsync("project",
            [new("1", "Project", Evidence)], model, 5, 8, new CancellationToken(true)));
        Assert.Empty(model.Prompts);
    }
}
