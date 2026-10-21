using AiAgent.Backend.Dtos.Settings;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Settings;
using AiAgent.Backend.Models.Settings;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.KunBuddy;
using AiAgent.Backend.Services.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    [Fact]
    public async Task KunBuddySessionsAreScopedHashedRevocableAndExpire()
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiUser), typeof(AiUserSession));
        var auth = new AuthService(db, new ConfigurationBuilder().Build(), new SubscriptionEnvironment(Path.GetTempPath()));
        var created = await auth.CreateUserAsync("fixture-user", "fixture-password", null, default);
        Assert.NotNull(created.User);
        var (_, token) = await auth.LoginKunBuddyAsync("fixture-user", "fixture-password", default);
        Assert.NotNull(token);
        var session = db.Queryable<AiUserSession>().Single();
        Assert.NotEqual(token, session.TokenHash);
        Assert.Equal("kunbuddy-client", session.Purpose);
        var context = new DefaultHttpContext(); context.Request.Headers.Authorization = "Bearer " + token;
        Assert.NotNull(await auth.TryGetKunBuddyUserAsync(context, default));
        Assert.Null(await auth.TryGetCurrentUserAsync(context, default));
        Assert.Null(await auth.TryGetPluginUserAsync(context, default));
        context.Request.Headers.Remove("Authorization"); context.Request.Headers["x-api-key"] = token;
        Assert.NotNull(await auth.TryGetKunBuddyUserAsync(context, default));
        context.Request.Headers.Remove("x-api-key"); context.Request.Headers["x-goog-api-key"] = token;
        Assert.NotNull(await auth.TryGetKunBuddyUserAsync(context, default));
        context.Request.Headers.Remove("x-goog-api-key"); context.Request.Headers.Authorization = "Bearer " + token;
        await auth.LogoutAsync(context, default);
        Assert.Null(await auth.TryGetKunBuddyUserAsync(context, default));
        (_, token) = await auth.LoginKunBuddyAsync("fixture-user", "fixture-password", default);
        context.Request.Headers.Authorization = "Bearer " + token;
        var expired = DateTime.UtcNow.AddMinutes(-1);
        db.Updateable<AiUserSession>().SetColumns(item => item.ExpiresAt == expired)
            .Where(item => item.UserId == created.User!.Id && item.Purpose == "kunbuddy-client").ExecuteCommand();
        Assert.Null(await auth.TryGetKunBuddyUserAsync(context, default));
    }

    [Fact]
    public void KunBuddyModelMetadataRoundTripsWithoutEnablingNativeTools()
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiModelProfile), typeof(AiModel), typeof(AiModelProvider));
        var service = new ModelCatalogService(db);
        var catalog = service.Load(); var model = catalog.Services.Llm.Profiles[0].Models[0];
        catalog.Services.Llm.Profiles[0].ApiStyle = "responses";
        model.MaxOutputTokens = "65536"; model.MaxOutputTokensSource = "manual"; model.SupportsImages = true; model.SupportsTools = true;
        model.ContextWindowSource = "endpoint";
        service.Save(catalog);
        var restored = service.Load().Services.Llm.Profiles[0].Models[0];
        Assert.Equal("responses", service.Load().Services.Llm.Profiles[0].ApiStyle);
        Assert.Equal("65536", restored.MaxOutputTokens);
        Assert.Equal("manual", restored.MaxOutputTokensSource);
        Assert.Equal("endpoint", restored.ContextWindowSource);
        Assert.True(restored.SupportsImages);
        Assert.True(restored.SupportsTools);
        Assert.NotEqual(true, restored.SupportsNativeToolCalling);
    }

    [Theory]
    [InlineData("openai", "chat/completions", "chat_completions")]
    [InlineData("anthropic", "messages", "anthropic_messages")]
    [InlineData("google", "streamGenerateContent", "google_generative_ai")]
    public async Task KunBuddyGatewayKeepsNativeToolAndImagePayloadsAndReplacesOnlyServerIdentity(string binding, string operation, string expectedStyle)
    {
        var catalog = SubscriptionCatalog(binding);
        string? forwarded = null; string? credential = null; Uri? address = null;
        var factory = new SubscriptionHttpFactory(async request =>
        {
            forwarded = await request.Content!.ReadAsStringAsync(); address = request.RequestUri;
            credential = request.Headers.Authorization?.Parameter
                ?? (request.Headers.TryGetValues("x-api-key", out var anthropic) ? anthropic.Single() : null)
                ?? (request.Headers.TryGetValues("x-goog-api-key", out var google) ? google.Single() : null);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data: {\"choices\":[]}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        });
        var gateway = new KunBuddyGatewayService(catalog, factory);
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var payload = """{"model":"model-1","messages":[{"role":"user","content":[{"type":"image_url","image_url":{"url":"data:image/png;base64,fixture"}}]}],"tools":[{"type":"function","function":{"name":"read","parameters":{"type":"object","properties":{"path":{"type":"string"}}}}}],"stream":true} """;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        await gateway.ForwardAsync(context, "profile-1", operation, binding == "google" ? "model-1" : null, default);
        Assert.Equal("fixture-server-key", credential);
        Assert.Contains("data:image/png;base64,fixture", forwarded);
        Assert.Contains("\"properties\"", forwarded);
        if (binding != "google") Assert.Contains("wire-model", forwarded);
        Assert.Equal(expectedStyle, KunBuddyGatewayService.ApiStyle(binding));
        Assert.Equal(200, context.Response.StatusCode);
        Assert.NotNull(address);
        Assert.DoesNotContain("fixture-server-key", JsonSerializer.Serialize(gateway.Models()));
    }

    [Fact]
    public async Task KunBuddyGatewayRejectsUnknownModelsBeforeProviderAccess()
    {
        var factory = new SubscriptionHttpFactory(_ => throw new InvalidOperationException("Must not reach provider"));
        var gateway = new KunBuddyGatewayService(SubscriptionCatalog("openai"), factory);
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"model\":\"invented\",\"messages\":[]}"));
        await gateway.ForwardAsync(context, "profile-1", "chat/completions", null, default);
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task KunBuddyGatewayPreservesResponsesEventsAndRequestSchema()
    {
        var catalog = SubscriptionCatalog("openai"); catalog.Load().Services.Llm.Profiles[0].ApiStyle = "responses";
        var gateway = new KunBuddyGatewayService(catalog, new SubscriptionHttpFactory(async request =>
        {
            Assert.EndsWith("/responses", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("wire-model", body.RootElement.GetProperty("model").GetString());
            Assert.True(body.RootElement.GetProperty("input").ValueKind == JsonValueKind.Array);
            Assert.Equal("auto", body.RootElement.GetProperty("tool_choice").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("event: response.completed\ndata: {\"type\":\"response.completed\"}\n\n", Encoding.UTF8, "text/event-stream") };
        }));
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"model":"model-1","input":[{"role":"user","content":"fixture"}],"tool_choice":"auto","stream":true}"""));
        await gateway.ForwardAsync(context, "profile-1", "responses", null, default);
        context.Response.Body.Position = 0;
        Assert.Contains("event: response.completed", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task KunBuddyGatewayDoesNotReturnCredentialBearingProviderErrors()
    {
        var gateway = new KunBuddyGatewayService(SubscriptionCatalog("openai"), new SubscriptionHttpFactory(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("fixture-server-key private prompt") })));
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"model\":\"model-1\"}"));
        await gateway.ForwardAsync(context, "profile-1", "chat/completions", null, default);
        context.Response.Body.Position = 0;
        var result = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("fixture-server-key", result);
        Assert.DoesNotContain("private prompt", result);
        Assert.Equal(502, context.Response.StatusCode);
    }

    private static SubscriptionCatalogService SubscriptionCatalog(string binding) => new(new ModelCatalog { Services = new ModelCatalogServices { Llm = new CatalogService { Profiles = [new CatalogProfile { Id = "profile-1", Binding = binding, BaseUrl = "https://fixture.example/v1", ApiKey = "fixture-server-key", Models = [new CatalogModel { Id = "model-1", Model = "wire-model", ContextWindow = "1000000" }] }] } } });

    private sealed class SubscriptionCatalogService(ModelCatalog value) : IModelCatalogService
    {
        public ModelCatalog Load(bool redactSecrets = false) => value;
        public ModelCatalog Save(ModelCatalog catalog) => catalog;
        public ApplyResult Apply(ModelCatalog? catalog = null) => new();
    }

    private sealed class SubscriptionHttpFactory(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new SubscriptionHandler(send));
    }
    private sealed class SubscriptionHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    private sealed class SubscriptionEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "KunBuddyTests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
