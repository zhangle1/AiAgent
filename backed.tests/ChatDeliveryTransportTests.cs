using System.IO;
using System.Reflection;
using System.Text.Json;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Memory;
using AiAgent.Backend.Services.Usage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed partial class ChatCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeliveryAdmissionIsSharedBySseAndWebsocketAndPersistsInExecutionOrder(bool websocket)
    {
        db.CodeFirst.InitTables(typeof(AiUser), typeof(AiChatDelivery));
        db.Insertable(new AiUser { Id = user.Id, Username = user.Username }).ExecuteCommand();
        var calls = new List<string>();
        T Stub<T>() where T : class
        {
            var value = DispatchProxy.Create<T, Collaborator>();
            ((Collaborator)(object)value).InvokeMethod = (method, args) => {
                if (method.Name == "TryGetCurrentUserAsync") return Task.FromResult<AuthenticatedUser?>(user);
                if (method.Name is "RecordUserMessageAsync" or "RecordAssistantMessageAsync") calls.Add(method.Name + ":" + ((ChatCompleteRequest)args![1]!).Message);
                if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
                if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                    return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(method.ReturnType.GenericTypeArguments[0]).Invoke(null, [null]);
                return null;
            };
            return value;
        }
        var sessions = Stub<IChatSessionService>(); var deliveries = new ChatDeliveryService(db, sessions);
        using var services = new ServiceCollection().AddSingleton(store).AddSingleton(deliveries).BuildServiceProvider();
        var engine = new DeliveryEngine();
        async Task<string> Run(string command)
        {
            var request = Request(command); request.Message = command; request.DeliveryIntent = "queue";
            var context = new DefaultHttpContext { RequestServices = services }; context.Response.Body = new MemoryStream();
            if (websocket)
            {
                using var socket = new FixtureSocket(JsonSerializer.Serialize(request)); context.Features.Set<IHttpWebSocketFeature>(new SocketFeature(socket));
                var handler = new ChatWebSocketHandler(engine, Stub<IAuthService>(), sessions, Stub<IChatImageAttachmentService>(), Stub<IChatFileAttachmentService>(), Stub<IUsageStatisticsService>(), Stub<IMemoryService>(),
                    Stub<IProjectReferenceContextService>(), Stub<IMarkdownDocumentReferenceContextService>(), Stub<IProjectAgentMarkdownIndexContextService>(), Stub<ICodexModelPolicyService>(), Stub<IImageOcrPolicyService>(), Stub<IChatDebugTraceStore>(), NullLogger<ChatWebSocketHandler>.Instance);
                await handler.HandleClientAsync(context); return string.Join("\n", socket.Sent);
            }
            var app = new ChatAppService(new HttpContextAccessor { HttpContext = context }, engine, Stub<IAuthService>(), sessions, Stub<IChatImageAttachmentService>(), Stub<IChatFileAttachmentService>(), Stub<IChatUploadLibraryService>(), Stub<IUsageStatisticsService>(), Stub<IMemoryService>(),
                Stub<IProjectReferenceContextService>(), Stub<IMarkdownDocumentReferenceContextService>(), Stub<IProjectAgentMarkdownIndexContextService>(), Stub<ICodexChatService>(), Stub<ICodexModelPolicyService>(), Stub<IImageOcrPolicyService>(), Stub<IChatDebugTraceStore>());
            await app.CompleteStream(request, default); context.Response.Body.Position = 0; return await new StreamReader(context.Response.Body).ReadToEndAsync();
        }
        var first = Run("A"); await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = Run("B"); var duplicate = Run("B");
        Assert.False(second.IsCompleted); Assert.Equal(new[] { "A" }, engine.Calls);
        engine.Release.TrySetResult();
        var outputs = await Task.WhenAll(first, second, duplicate).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "A", "B" }, engine.Calls);
        Assert.Equal(new[] { "RecordUserMessageAsync:A", "RecordAssistantMessageAsync:A", "RecordUserMessageAsync:B", "RecordAssistantMessageAsync:B" }, calls);
        Assert.Contains("delivery_queued", outputs[1]); Assert.Contains("completed", outputs[1]); Assert.Contains("answer B", outputs[2]);
        Assert.Empty(await deliveries.ListAsync(user, "session", default));
    }

    private sealed class DeliveryEngine : IChatOrchestrator
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Calls { get; } = [];
        public Task<ChatCompleteResponse> CompleteAsync(ChatCompleteRequest request, CancellationToken ct) => throw new NotSupportedException();
        public async Task<ChatCompleteResponse> CompleteStreamingAsync(ChatCompleteRequest request, AgentStreamEventHandler? emit, CancellationToken ct)
        {
            Calls.Add(request.Message);
            if (request.Message == "A") { Started.TrySetResult(); await Release.Task.WaitAsync(ct); }
            if (emit is not null) await emit(new() { Type = "content", Content = "answer " + request.Message }, ct);
            return new() { Content = "answer " + request.Message };
        }
    }
}
