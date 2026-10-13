using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.Knowledge.Core;
using AiAgent.Backend.Services.Knowledge.KnowAgent;
using AiAgent.Backend.Services.Chat.Llm;
using AiAgent.Backend.Models.Settings;
using AiAgent.Backend.Services.Settings;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Tests;

public sealed class KnowledgeAutomaticBudgetTests
{
    private sealed class Model(int window = 1000000, string? output = null) : IKnowledgeModel
    {
        public int ContextWindowTokens => window;
        public int OutputTokens => 8192;
        public List<string> Prompts { get; } = [];
        public Task<CompilerReply> CompleteAsync(string prompt, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Prompts.Add(prompt); return Task.FromResult(new CompilerReply(output ?? "# 概览\n\n完整资料摘要。", "fixture", "fixture")); }
    }

    [Theory]
    [InlineData(6000)]
    [InlineData(14000)]
    public async Task ValidChineseOutputIsNotRejectedByUtf8ByteCountAsIfItWereProviderTokens(int characters)
    {
        var output = "# 知识摘要\n\n" + new string('知', characters);
        var model = new Model(output: output);
        Assert.Equal(output, await new KnowAgent(model, 8).Semantics().SummarizeAsync("original source", default));
    }

    [Fact]
    public async Task MillionContextReadsLargeSourceInOneCallWithoutLegacySourceOrChunkCaps()
    {
        var model = new Model();
        var source = new string('a', 300000) + "END_OF_SOURCE";
        await new KnowAgent(model, 0).Semantics().SummarizeAsync(source, default);
        Assert.Single(model.Prompts); Assert.Contains("END_OF_SOURCE", model.Prompts[0]);
    }

    [Fact]
    public async Task AutomaticCallsCanExceedLegacy48AndExplicitBudgetStillStops()
    {
        var model = new Model();
        await new KnowledgeSemanticGenerator(model, 0, 512).SummarizeAsync(new string('a', 30000), default);
        Assert.True(model.Prompts.Count > 48);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KnowledgeSemanticGenerator(new Model(), 1, 512).SummarizeAsync(new string('a', 2000), default));
        var settings = new KnowledgeCompilerSettingsDto(); Assert.Equal(0, settings.MaxSteps); KnowledgeCompilerSettings.Validate(settings);
        KnowledgeCompilerSettings.Validate(new() { MaxSteps = 200 });
    }

    [Fact]
    public async Task JsonExpansionIsSplitAutomaticallyBeforeExceedingModelContext()
    {
        var model = new Model(65536);
        await new KnowAgent(model, 0).Semantics().SummarizeAsync(string.Concat(Enumerable.Repeat("\"\\\n\0", 20000)), default);
        Assert.True(model.Prompts.Count > 1);
        Assert.All(model.Prompts, prompt => ContextBudget.Validate(prompt, model.ContextWindowTokens, model.OutputTokens));
    }

    [Fact]
    public async Task AutomaticOutputUsesProviderDefaultAndStillRejectsTruncatedProviderResponses()
    {
        var handler = new CompletionHandler();
        var llm = new LlmChatClient(new BudgetCatalog(), new BudgetFactory(handler));
        var adapter = new KnowledgeModelAdapter(llm, null!, new() { Generator = "llm_api", ModelId = "large" }, "unused");
        await adapter.CompleteAsync("source", default);
        Assert.False(handler.Request!.Value.TryGetProperty("max_tokens", out _));
        Assert.True(adapter.ContextWindowTokens > 900000);
        handler.Finish = "length";
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.CompleteAsync("source", default));
        Assert.Contains("output token limit", failure.Message);
    }

    private sealed class BudgetCatalog : IModelCatalogService
    {
        public ModelCatalog Load(bool redactSecrets = false) => new() { Services = new() { Llm = new() {
            ActiveModelId = "large", Profiles = [new() { Id = "fixture", Binding = "openai", BaseUrl = "https://fixture.invalid/v1", ApiKey = "synthetic-test-only", Models = [new() { Id = "large", Model = "fixture-model", ContextWindow = "1000000" }] }]
        } } };
        public ModelCatalog Save(ModelCatalog catalog) => throw new NotSupportedException();
        public ApplyResult Apply(ModelCatalog? catalog = null) => throw new NotSupportedException();
    }
    private sealed class BudgetFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class CompletionHandler : HttpMessageHandler
    {
        public JsonElement? Request { get; private set; }
        public string Finish { get; set; } = "stop";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Request = json.RootElement.Clone();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = Finish, message = new { content = "# 概览\n\n完整返回。" } } } }), Encoding.UTF8, "application/json") };
        }
    }
}
