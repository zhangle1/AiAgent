using AiAgent.Backend.Dtos.Settings;
using AiAgent.Backend.Services.Settings;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public void DiscoveryPrefersEndpointLimitsThenLocalMetadataAndNeverInventsUnknownCapacity()
    {
        using var fixture = new SubscriptionMetadataFixture();
        using var json = JsonDocument.Parse("""{"data":[{"id":"known","context_length":1000000},{"id":"published"},{"id":"unknown"},{"id":"known"}]}""");
        var models = ModelDiscoveryService.ParseModels(json.RootElement, "fixture", fixture.Store);
        Assert.Equal(3, models.Count);
        var endpoint = models.Single(item => item.Model == "known");
        Assert.Equal(1000000, endpoint.ContextWindow); Assert.Equal("endpoint", endpoint.ContextWindowSource);
        var published = models.Single(item => item.Model == "published");
        Assert.Equal(262144, published.ContextWindow); Assert.Equal(65536, published.MaxOutputTokens);
        Assert.True(published.SupportsImages); Assert.Equal("model.json", published.ContextWindowSource);
        Assert.Null(models.Single(item => item.Model == "unknown").ContextWindow);
        using var ambiguous = JsonDocument.Parse("""{"data":[{"id":"same"}]}""");
        Assert.Null(ModelDiscoveryService.ParseModels(ambiguous.RootElement, "custom", fixture.Store).Single().ContextWindow);
    }

    [Fact]
    public void DiscoveryReadsGoogleAndOllamaLists()
    {
        using var fixture = new SubscriptionMetadataFixture();
        using var google = JsonDocument.Parse("""{"models":[{"name":"models/gemini-fixture","inputTokenLimit":1048576,"outputTokenLimit":65536}]}""");
        var model = Assert.Single(ModelDiscoveryService.ParseModels(google.RootElement, "google", fixture.Store));
        Assert.Equal("gemini-fixture", model.Model); Assert.Equal(1048576, model.ContextWindow);
        using var ollama = JsonDocument.Parse("""{"models":[{"name":"local-model:latest"}]}""");
        Assert.Equal("local-model:latest", Assert.Single(ModelDiscoveryService.ParseModels(ollama.RootElement, "ollama", fixture.Store)).Model);
    }

    [Theory]
    [InlineData("file:///private")]
    [InlineData("https://user:secret@example.test")]
    [InlineData("https://example.test/?api_key=secret")]
    [InlineData("https://example.test/#fragment")]
    public void DiscoveryRejectsCredentialBearingOrNonHttpEndpoints(string value)
        => Assert.Throws<ArgumentException>(() => ModelDiscoveryService.ValidateEndpoint(value));

    [Fact]
    public async Task DiscoveryCannotSendAStoredSecretToAChangedEndpoint()
    {
        using var fixture = new SubscriptionMetadataFixture();
        var discovery = new ModelDiscoveryService(new SubscriptionHttpFactory(_ => throw new Exception("No request expected")), SubscriptionCatalog("openai"), fixture.Store);
        await Assert.ThrowsAsync<ArgumentException>(() => discovery.DiscoverAsync(new ModelDiscoveryRequest { ProfileId = "profile-1", BaseUrl = "https://other.example/v1", ApiKey = "********" }, default));
    }

    [Fact]
    public async Task DiscoveryDoesNotUseCatalogRowsAsProofOfKeyAuthorization()
    {
        using var fixture = new SubscriptionMetadataFixture();
        var discovery = new ModelDiscoveryService(new SubscriptionHttpFactory(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("fixture-key must stay private") })), SubscriptionCatalog("openai"), fixture.Store);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => discovery.DiscoverAsync(new ModelDiscoveryRequest { BaseUrl = "https://fixture.example/v1", ApiKey = "fixture-key" }, default));
        Assert.Contains("401", error.Message); Assert.DoesNotContain("fixture-key", error.Message);
    }

    [Fact]
    public void MetadataSeedIsMaterializedAsALocalModelJsonWithoutCredentials()
    {
        var root = Path.Combine(Path.GetTempPath(), "kunbuddy-metadata-" + Guid.NewGuid().ToString("N"));
        try
        {
            var metadata = new ModelMetadataStore(new SubscriptionEnvironment(root));
            metadata.EnsureLoaded();
            var file = Path.Combine(root, "data", "models", "model.json");
            using var parsed = JsonDocument.Parse(File.ReadAllText(file));
            Assert.True(parsed.RootElement.TryGetProperty("deepseek", out _));
            Assert.Equal("data/models/model.json", metadata.CatalogPath);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DiscoveryFollowsGooglePagesWithoutPuttingTheKeyInTheUrl()
    {
        using var fixture = new SubscriptionMetadataFixture();
        var requests = new List<string>();
        var discovery = new ModelDiscoveryService(new SubscriptionHttpFactory(request =>
        {
            requests.Add(request.RequestUri!.ToString());
            Assert.Equal("fixture-key", request.Headers.GetValues("x-goog-api-key").Single());
            var payload = requests.Count == 1 ? """{"models":[{"name":"models/first"}],"nextPageToken":"page-two"}""" : """{"models":[{"name":"models/second"}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        }), SubscriptionCatalog("openai"), fixture.Store);
        var models = await discovery.DiscoverAsync(new ModelDiscoveryRequest { Binding = "google", BaseUrl = "https://fixture.example", ApiKey = "fixture-key" }, default);
        Assert.Equal(2, models.Count); Assert.Contains("pageToken=page-two", requests[1]);
        Assert.DoesNotContain(requests, request => request.Contains("fixture-key"));
    }

    [Fact]
    public void MetadataSeedWriteFailureDoesNotPublishAnUnpersistedMemoryCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "kunbuddy-metadata-retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var obstruction = Path.Combine(root, "data");
        try
        {
            File.WriteAllText(obstruction, "fixture obstruction");
            var store = new ModelMetadataStore(new SubscriptionEnvironment(root));
            Assert.ThrowsAny<IOException>(() => store.EnsureLoaded());
            File.Delete(obstruction);
            store.EnsureLoaded();
            Assert.True(File.Exists(Path.Combine(root, "data", "models", "model.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private sealed class SubscriptionMetadataFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "kunbuddy-discovery-" + Guid.NewGuid().ToString("N"));
        public ModelMetadataStore Store { get; }
        public SubscriptionMetadataFixture()
        {
            var directory = Path.Combine(_root, "data", "models"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "model.json"), """{"fixture":{"models":{"known":{"limit":{"context":32000}},"published":{"name":"Published","limit":{"context":262144,"output":65536},"modalities":{"input":["text","image"]},"tool_call":true},"same":{"limit":{"context":1000000}}}},"other":{"models":{"same":{"limit":{"context":1000}}}}}""");
            Store = new ModelMetadataStore(new SubscriptionEnvironment(_root));
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
