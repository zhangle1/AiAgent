using AiAgent.Backend.Services.Knowledge.Core;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using System.Text.Json;

namespace AiAgent.Backend.Tests;

public sealed class KnowledgeCompilerTests
{
    private const string Source = "Original evidence states that the project belongs to the company.";
    private static string Page(string quote = Source, int part = 1, string title = "Company") =>
        JsonSerializer.Serialize(new { pages = new[] { new { title, markdown = "A documented company project.", evidence = new[] { new { part, quote } } } } });
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
    public async Task AnalysisPrecedesGenerationAndTraceExcludesSource()
    {
        var model = new Model("Identified company and project; ownership requires review.", Page());
        var progress = new List<CompilerStep>();
        var result = await new KnowledgeCompiler().CompileAsync(Source, model,
            progress: step => { progress.Add(step); return Task.CompletedTask; }, wikiContext: "Existing wiki topic");
        Assert.Equal(Source, Assert.Single(Assert.Single(result.Pages).Evidence).Quote);
        Assert.Equal("fake-model", result.Model);
        Assert.Equal(2, model.Prompts.Count);
        Assert.Contains("Stage 1/2: ANALYSIS", model.Prompts[0]);
        Assert.Contains("Identified company and project", model.Prompts[1]);
        Assert.All(model.Prompts, prompt => Assert.Contains("Existing wiki topic", prompt));
        Assert.Equal(new[] { "analyze_source", "generate_pages" }, result.Steps.Select(x => x.Action));
        Assert.Contains(progress, step => step.Action == "generate_pages" && step.Result == "started" && step.CoveredParts == 0);
        Assert.DoesNotContain(Source, JsonSerializer.Serialize(result.Steps));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"pages\":[]}")]
    [InlineData("{\"pages\":[null]}")]
    [InlineData("{\"action\":\"run_command\"}")]
    public async Task InvalidGenerationGetsBoundedCorrectionWithSourceAndAnalysis(string invalid)
    {
        var model = new Model("Analysis context", invalid, Page());
        var result = await new KnowledgeCompiler().CompileAsync(Source, model);
        Assert.StartsWith("Validation rejected", result.Steps[1].Result);
        Assert.Contains(Source, model.Prompts[2]);
        Assert.Contains("Analysis context", model.Prompts[2]);
        Assert.Single(result.Pages);
    }

    [Fact]
    public async Task FabricatedEvidenceStopsAfterThreeGenerationAttempts()
    {
        var model = new Model("Analysis", Page("This quotation is entirely fabricated."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KnowledgeCompiler().CompileAsync(Source, model));
        Assert.Equal(4, model.Prompts.Count);
    }

    [Fact]
    public async Task AllPartsAreAnalyzedBeforeGenerationAndMustHaveEvidence()
    {
        var model = new Model("Analysis one", "Analysis two", Page(new string('a', 12)),
            Page(Source, 1, "Second"), Page(Source, 2, "Second"));
        var result = await new KnowledgeCompiler().CompileAsync(new string('a', KnowledgeCompiler.PartSize) + Source, model);
        Assert.All(model.Prompts.Take(2), prompt => Assert.Contains("Stage 1/2", prompt));
        Assert.StartsWith("Validation rejected", result.Steps[3].Result);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(2, result.Steps.Last().CoveredParts);
    }

    [Fact]
    public async Task DuplicateTitleDoesNotOverwriteAcceptedDraft()
    {
        var model = new Model("Analysis one", "Analysis two", Page(new string('a', 12)),
            Page(Source, 2), Page(Source, 2, "Second"));
        var result = await new KnowledgeCompiler().CompileAsync(new string('a', KnowledgeCompiler.PartSize) + Source, model);
        Assert.Equal(2, result.Pages.Count);
        Assert.Contains("Duplicate title", result.Steps[3].Result);
    }

    [Fact]
    public async Task MaximumSourceCompletesBothStagesWithinThirtyTwoCalls()
    {
        var replies = Enumerable.Repeat("Analysis", 16).Concat(Enumerable.Range(1, 16)
            .Select(part => Page(new string('a', 12), part, $"Topic {part}"))).ToArray();
        var model = new Model(replies);
        var result = await new KnowledgeCompiler().CompileAsync(new string('a', KnowledgeCompiler.MaxSourceCharacters), model, maxSteps: 32);
        Assert.Equal(32, model.Prompts.Count);
        Assert.Equal(16, result.Pages.Count);
        Assert.Equal(16, result.Steps.Last().CoveredParts);
    }

    [Fact]
    public async Task InvalidBatchDoesNotRetainItsOtherwiseValidPages()
    {
        var batch = JsonSerializer.Serialize(new { pages = new[] {
            new KnowledgePage("Company", "Valid draft", [new Evidence(1, Source)]),
            new KnowledgePage("Bad", "Invalid draft", [new Evidence(1, "Fabricated quotation")]) } });
        var model = new Model("Analysis", batch, Page());
        var result = await new KnowledgeCompiler().CompileAsync(Source, model);
        Assert.Single(result.Pages);
        Assert.Equal("Company", result.Pages[0].Title);
        Assert.Equal(3, model.Prompts.Count);
    }

    [Fact]
    public async Task InvalidAnalysisDoesNotStartGeneration()
    {
        var model = new Model("");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KnowledgeCompiler().CompileAsync(Source, model));
        Assert.Single(model.Prompts);
    }

    [Fact]
    public async Task OversizedSourceInsufficientBudgetAndCancellationNeverCallModel()
    {
        var model = new Model("{}");
        await Assert.ThrowsAsync<ArgumentException>(() => new KnowledgeCompiler().CompileAsync(new string('a', KnowledgeCompiler.MaxSourceCharacters + 1), model));
        await Assert.ThrowsAsync<ArgumentException>(() => new KnowledgeCompiler().CompileAsync(new string('a', KnowledgeCompiler.PartSize * 5), model, maxSteps: 8));
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

    [Fact]
    public async Task ChainDiagnosticsCompilesSearchesAndReturnsValidatedCitationWithoutStorage()
    {
        var page = JsonSerializer.Serialize(new { pages = new[] { new {
            title = "星河项目发布规范",
            markdown = "星河项目每周三晚发布；紧急修复前必须评审回滚方案。",
            evidence = new[] { new { part = 1, quote = KnowledgeChainDiagnosticsService.SampleSource } }
        } } });
        var model = new Model(
            "识别出发布时间、负责人和紧急修复前置条件。",
            page,
            "{\"action\":\"read\",\"id\":\"check-1\",\"part\":1}",
            "{\"action\":\"cite\",\"id\":\"check-1\",\"quote\":\"紧急修复前必须评审回滚方案。\"}",
            "{\"action\":\"finish\"}");

        var result = await KnowledgeChainDiagnosticsService.ExecuteAsync(new(), model, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "model", "analysis", "generation", "evidence", "retrieval", "citation" }, result.Steps.Select(step => step.Key));
        Assert.Contains("回滚方案", result.Steps.Last().Detail);
        Assert.Equal(5, model.Prompts.Count);
    }
}
