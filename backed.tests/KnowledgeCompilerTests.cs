using AiAgent.Knowledge.Core;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using System.Text.Json;

namespace AiAgent.Backend.Tests;

public sealed class KnowledgeCompilerTests
{
    private const string Source = "Original evidence states that the project belongs to the company.";
    private static string Page(string quote = Source, int part = 1, string title = "Company") =>
        JsonSerializer.Serialize(new { action = "propose_page", title, markdown = "A documented company project.", evidence = new[] { new { part, quote } } });
    private sealed class Model(params string[] replies) : IKnowledgeModel
    {
        public List<string> Prompts { get; } = [];
        public Task<CompilerReply> CompleteAsync(string prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return Task.FromResult(new CompilerReply(replies[Math.Min(Prompts.Count - 1, replies.Length - 1)], "test", "fake-model"));
        }
    }

    [Fact]
    public async Task ProducesTraceableDraftWithoutSourceInTrace()
    {
        var model = new Model("{\"action\":\"read_source\",\"part\":1}", Page(), "{\"action\":\"finish\"}");
        var progress = new List<CompilerStep>();
        var result = await new KnowledgeCompiler().CompileAsync(Source, model, progress: step => { progress.Add(step); return Task.CompletedTask; });
        Assert.Equal(Source, Assert.Single(Assert.Single(result.Pages).Evidence).Quote);
        Assert.Equal("fake-model", result.Model);
        Assert.Equal(3, progress.Count);
        Assert.DoesNotContain(Source, JsonSerializer.Serialize(result.Steps));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"action\":\"read_source\",\"part\":0}")]
    [InlineData("{\"action\":\"finish\"}")]
    [InlineData("{\"action\":\"run_command\"}")]
    public async Task InvalidOperationsAreRejectedAndRecoverable(string invalid)
    {
        var model = new Model(invalid, "{\"action\":\"read_source\",\"part\":1}", Page(), "{\"action\":\"finish\"}");
        var result = await new KnowledgeCompiler().CompileAsync(Source, model);
        Assert.StartsWith("Validation rejected", result.Steps[0].Result);
        Assert.Single(result.Pages);
    }

    [Fact]
    public async Task FabricatedEvidenceAndPrematureFinishCannotCommit()
    {
        var model = new Model("{\"action\":\"read_source\",\"part\":1}", Page("This quotation is entirely fabricated."), "{\"action\":\"finish\"}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KnowledgeCompiler().CompileAsync(Source, model, maxSteps: 8));
        Assert.Contains("Validation rejected", model.Prompts[2]);
    }

    [Fact]
    public async Task EveryPartMustBeReadAndHaveEvidence()
    {
        var model = new Model("{\"action\":\"read_source\",\"part\":1}", Page(new string('a', 12)),
            "{\"action\":\"read_source\",\"part\":2}", "{\"action\":\"finish\"}", Page(Source, 2, "Second"), "{\"action\":\"finish\"}");
        var result = await new KnowledgeCompiler().CompileAsync(new string('a', KnowledgeCompiler.PartSize) + Source, model);
        Assert.StartsWith("Validation rejected", result.Steps[3].Result);
        Assert.Equal(2, result.Pages.Count);
    }

    [Fact]
    public async Task DuplicatePageDoesNotOverwriteAcceptedDraft()
    {
        var model = new Model("{\"action\":\"read_source\",\"part\":1}", Page(), Page(), "{\"action\":\"finish\"}");
        var result = await new KnowledgeCompiler().CompileAsync(Source, model);
        Assert.Single(result.Pages);
        Assert.Contains("Duplicate title", result.Steps[2].Result);
    }

    [Fact]
    public async Task OversizedSourceAndCancellationNeverCallModel()
    {
        var model = new Model("{}");
        await Assert.ThrowsAsync<ArgumentException>(() => new KnowledgeCompiler().CompileAsync(new string('a', KnowledgeCompiler.MaxSourceCharacters + 1), model));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new KnowledgeCompiler().CompileAsync(Source, model, cancellationToken: new CancellationToken(true)));
        Assert.Empty(model.Prompts);
    }

    [Fact]
    public void NewArtifactExposesIndividualPagesAndLegacyArtifactRemainsVisible()
    {
        var artifact = new AiKnowledgeArtifact { Id = 42, DocumentId = 7, Content = "Legacy markdown", ReviewStatus = "draft" };
        Assert.Equal("Legacy markdown", Assert.Single(KnowledgeWorkspaceService.ExpandPages(artifact, "source.txt")).Content);
        artifact.EvidenceJson = JsonSerializer.Serialize(new { pages = new[] {
            new KnowledgePage("First", "Body one", [new Evidence(1, Source)]),
            new KnowledgePage("Second", "Body two", [new Evidence(2, Source)]) } });
        var pages = KnowledgeWorkspaceService.ExpandPages(artifact, "source.txt");
        Assert.Equal(2, pages.Count);
        Assert.Equal("Second", pages[1].Title);
        Assert.Equal(1, pages[1].PageIndex);
        Assert.Equal(7, pages[1].DocumentId);
        Assert.Contains(Source, pages[1].Content);
    }

    [Fact]
    public void InvalidCompilerSettingsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => KnowledgeCompilerSettings.Validate(new() { Generator = "shell" }));
        Assert.Throws<ArgumentException>(() => KnowledgeCompilerSettings.Validate(new() { MaxSteps = 0 }));
        Assert.Throws<ArgumentException>(() => KnowledgeCompilerSettings.Validate(new() { TimeoutMinutes = 0 }));
    }
}
