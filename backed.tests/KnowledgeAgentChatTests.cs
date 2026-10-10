using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AiAgent.Backend.Models.Settings;
using AiAgent.Backend.Services.Settings;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Chat.Llm;
using AiAgent.Backend.Services.Knowledge.KnowAgent;

namespace AiAgent.Backend.Tests;

public sealed class KnowledgeAgentChatTests
{
    [Fact]
    public async Task ConfiguredApiSelectionUsesNativeWireToolsAndNeverFallsBackForUnknownModel()
    {
        var catalog = new Catalog(); var handler = new WireHandler();
        var llm = new LlmChatClient(catalog, new Factory(handler));
        var agent = new KnowledgeAgentService(catalog, llm, null!, null!, null!);
        Assert.Throws<ArgumentException>(() => agent.RequireModel("unknown"));
        Assert.True(agent.RequireModel("two").NativeTools);
        var events = new List<AgentStreamEvent>();
        await new KnowledgeAgentChatEngine(llm, new Tools()).RunAsync(new() { Uri = "viking://resources/", ModelId = "two", Message = "ls" },
            (item, _) => { events.Add(item); return Task.CompletedTask; }, default);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("model-two", request.GetProperty("model").GetString()));
        Assert.Equal("ls", handler.Requests[0].GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        var messages = handler.Requests[1].GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal("call-wire", messages.Single(message => message.GetProperty("role").GetString() == "tool").GetProperty("tool_call_id").GetString());
        Assert.Equal("wire answer", string.Concat(events.Where(item => item.Type == "content").Select(item => item.Content)));
        Assert.Equal("done", events.Last().Type);
    }

    [Fact]
    public async Task ProviderTransportEofWithoutCompletionMarkerNeverReportsDone()
    {
        var handler = new WireHandler { Truncate = true }; var events = new List<AgentStreamEvent>();
        var llm = new LlmChatClient(new Catalog(), new Factory(handler));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KnowledgeAgentChatEngine(llm, new Tools()).RunAsync(new() { Uri = "viking://resources/", ModelId = "two", Message = "问答" },
            (item, _) => { events.Add(item); return Task.CompletedTask; }, default));
        Assert.Contains(events, item => item.Type == "content" && item.Content == "partial");
        Assert.DoesNotContain(events, item => item.Type == "done");
    }

    [Fact]
    public async Task NativeToolFragmentsAreExecutedAndPairedBeforeStreamingAnswer()
    {
        var model = new Model(); var tools = new Tools(); var events = new List<AgentStreamEvent>();
        await new KnowledgeAgentChatEngine(model, tools).RunAsync(new() { Uri = "viking://resources/docs/", ModelId = "fixture", Message = "列出资料" },
            (item, _) => { events.Add(item); return Task.CompletedTask; }, default);
        Assert.Equal("ls", Assert.Single(tools.Commands).Command);
        Assert.Equal("viking://resources/docs/", Assert.Single(tools.Scopes));
        var second = model.Inputs[1];
        var call = Assert.Single(second.Single(item => item.Role == "assistant").ToolCalls);
        Assert.Equal("call-ls", call.Id);
        Assert.Equal(call.Id, second.Single(item => item.Role == "tool").ToolCallId);
        Assert.Equal("真实目录", string.Concat(events.Where(item => item.Type == "content").Select(item => item.Content)));
        Assert.Contains(events, item => item.Type == "tool_result" && Equals(item.Metadata["success"], true));
        Assert.Contains(events, item => item.Type == "context" && (int)item.Metadata["estimated_input_tokens"]! < (int)item.Metadata["input_limit"]!);
        Assert.Equal("done", events.Last().Type);
    }

    [Fact]
    public async Task OversizedHistoryIsCompressedAndShowsMeasuredRatio()
    {
        var model = new Model { Native = false, Window = 16000 }; var events = new List<AgentStreamEvent>();
        await new KnowledgeAgentChatEngine(model, new Tools()).RunAsync(new() { Uri = "viking://resources/docs/", ModelId = "fixture", Message = "继续分析",
            History = [new() { Role = "assistant", Content = new string('a', 22000) }] }, (item, _) => { events.Add(item); return Task.CompletedTask; }, default);
        Assert.True(model.Compressions > 0);
        var compacted = Assert.Single(events, item => item.Type == "context_compacted");
        Assert.True((double)compacted.Metadata["compression_ratio"]! > 0);
        Assert.Equal("done", events.Last().Type);
    }

    [Fact]
    public async Task InvalidToolIsRejectedAndCancellationNeverReportsDone()
    {
        var model = new Model { ToolName = "shell" }; var tools = new Tools();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KnowledgeAgentChatEngine(model, tools).RunAsync(new() { Message = "execute", ModelId = "fixture" }, (_, _) => Task.CompletedTask, default));
        Assert.Empty(tools.Commands);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new KnowledgeAgentChatEngine(new Model(), new Tools()).RunAsync(new() { Message = "read", ModelId = "fixture" }, (_, _) => Task.CompletedTask, cancelled.Token));
    }

    private sealed class Tools : IKnowledgeAgentTools
    {
        public List<KnowledgeAgentCommandRequest> Commands { get; } = [];
        public List<string> Scopes { get; } = [];
        public Task<string> ResolveScopeAsync(string uri, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(uri); }
        public Task<string> ExecuteAsync(string scope, KnowledgeAgentCommandRequest request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Commands.Add(request); Scopes.Add(scope); return Task.FromResult("{\"nodes\":[{\"uri\":\"viking://resources/docs/a.md\"}],\"total\":1}"); }
    }
    private sealed class Model : ILlmChatClient
    {
        public bool Native { get; set; } = true;
        public int Window { get; set; } = 64000;
        public string ToolName { get; set; } = "ls";
        public int Compressions { get; private set; }
        public List<List<LlmMessage>> Inputs { get; } = [];
        public LlmModelCapabilities GetCapabilities(string? modelId) => new(modelId, "fixture", Window, 1600, Native);
        public Task<LlmChatResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, string? modelId, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Compressions++; return Task.FromResult(new LlmChatResult { Text = "资料摘要；证据需重新读取。" }); }
        public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(IReadOnlyList<LlmMessage> messages, string? modelId, [EnumeratorCancellation] CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); await Task.CompletedTask; yield return new() { Content = "真实目录", FinishReason = "stop" }; }
        public async IAsyncEnumerable<LlmStreamChunk> StreamWithToolsAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<ToolDefinition> definitions, string? modelId, [EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Inputs.Add(messages.ToList()); await Task.CompletedTask;
            if (Inputs.Count == 1)
            {
                yield return new() { ToolCallDeltas = [new() { Index = 0, Id = "call-ls", Name = ToolName, ArgumentsDelta = "{\"offset\":" }] };
                yield return new() { ToolCallDeltas = [new() { Index = 0, ArgumentsDelta = "0}" }], FinishReason = "tool_calls" }; yield break;
            }
            yield return new() { Content = "真实" }; yield return new() { Content = "目录", FinishReason = "stop" };
        }
    }

    private sealed class Catalog : IModelCatalogService
    {
        public ModelCatalog Load(bool redactSecrets = false) => new() { Services = new() { Llm = new() {
            ActiveProfileId = "p-one", ActiveModelId = "one", Profiles = [
                new() { Id = "p-one", Name = "One", BaseUrl = "https://one.example.invalid/v1", Binding = "openai", ApiKey = "synthetic-test-only", Models = [new() { Id = "one", Model = "model-one", ContextWindow = "64000", SupportsNativeToolCalling = true }] },
                new() { Id = "p-two", Name = "Two", BaseUrl = "https://two.example.invalid/v1", Binding = "openai", ApiKey = "synthetic-test-only", Models = [new() { Id = "two", Model = "model-two", ContextWindow = "64000", SupportsNativeToolCalling = true }] }
            ] } } };
        public ModelCatalog Save(ModelCatalog catalog) => throw new NotSupportedException();
        public ApplyResult Apply(ModelCatalog? catalog = null) => throw new NotSupportedException();
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private sealed class WireHandler : HttpMessageHandler
    {
        public bool Truncate { get; set; }
        public List<JsonElement> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("two.example.invalid", request.RequestUri!.Host);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Requests.Add(body.RootElement.Clone());
            var response = Truncate ? "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n" : Requests.Count == 1 ? "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call-wire\",\"function\":{\"name\":\"ls\",\"arguments\":\"{}\"}}]}}]}\n\ndata: [DONE]\n\n"
                : "data: {\"choices\":[{\"delta\":{\"content\":\"wire answer\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "text/event-stream") };
        }
    }
}
