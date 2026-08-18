using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Chat.Llm;
using AiAgent.Backend.Services.Chat.Planning;
using AiAgent.Backend.Services.Chat.Prompting;
using AiAgent.Backend.Services.Chat.Retrieval;
using AiAgent.Backend.Services.CodeRepository;
using AiAgent.Backend.Services.DashboardApp;
using AiAgent.Backend.Services.Git;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.Parsing;
using AiAgent.Backend.Services.PythonWorkers;
using AiAgent.Backend.Services.Rag;
using AiAgent.Backend.Services.Settings;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Usage;
using AiAgent.Backend.Services.Memory;
using AiAgent.Backend.Services.PromptTemplate;
using AiAgent.Backend.Services.Push;
using AiAgent.Backend.Services.WorkCanvas;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;
using SqlSugar;

var builder = WebApplication.CreateBuilder(args);
// Kept outside source control for local deployment settings and credentials.
builder.Configuration.AddJsonFile("appsettings.dev.json", optional: true, reloadOnChange: true);
builder.Inject();
var maxUploadBodyBytes = builder.Configuration.GetValue<long?>("Upload:MaxRequestBodySizeBytes")
    ?? 200L * 1024 * 1024;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadBodyBytes;
});

builder.Services.AddControllers().AddDynamicApiControllers();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBodyBytes;
});
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("CodeRuntimePreview").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseProxy = false
});
builder.Services.AddHttpClient("DingTalkWebhook", client => client.Timeout = TimeSpan.FromSeconds(10)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseProxy = false
});
builder.Services.AddHttpClient("DingTalkStreamGateway", client => client.Timeout = TimeSpan.FromSeconds(15)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseProxy = false
});
builder.Services.AddHttpClient("DingTalkSessionWebhook", client => client.Timeout = TimeSpan.FromSeconds(15)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseProxy = false
});
builder.Services.AddDataProtection();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("Settings", new OpenApiInfo
    {
        Title = "AiAgent Settings API",
        Version = "Settings",
        Description = "Settings and model configuration API for AiAgent."
    });
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AiAgent Backend API",
        Version = "v1",
        Description = "Settings and model configuration API for AiAgent."
    });
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("AiAgentCors", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        if (origins.Length == 0)
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            return;
        }

        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

builder.Services.AddSingleton<IModelCatalogService, ModelCatalogService>();
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<IProjectAccessService, ProjectAccessService>();
builder.Services.AddSingleton<IMemoryService, MemoryService>();
builder.Services.AddSingleton<IMemoryCandidateService, MemoryCandidateService>();
builder.Services.AddHostedService<MemoryCandidateHostedService>();
builder.Services.AddSingleton<IAdminService, AdminService>();
builder.Services.AddSingleton<IChatSessionService, ChatSessionService>();
builder.Services.AddSingleton<IWorkCanvasService, WorkCanvasService>();
builder.Services.AddSingleton<IChatDebugTraceStore, ChatDebugTraceStore>();
builder.Services.AddSingleton<IPromptTemplateService, PromptTemplateService>();
builder.Services.AddSingleton<IModelProviderOptionsService, ModelProviderOptionsService>();
builder.Services.AddSingleton<IModelSchemaInitializer, ModelSchemaInitializer>();
builder.Services.AddSingleton<IKnowledgePathService, KnowledgePathService>();
builder.Services.AddSingleton<IKnowledgeProviderConfigService, KnowledgeProviderConfigService>();
builder.Services.AddSingleton<IKnowledgeBaseManager, KnowledgeBaseManager>();
builder.Services.AddSingleton<IKnowledgeProgressHub, KnowledgeProgressHub>();
builder.Services.AddSingleton<IKnowledgeIndexMaterializer, KnowledgeIndexMaterializer>();
builder.Services.AddSingleton<IKnowledgeTaskRunner, KnowledgeTaskRunner>();
builder.Services.AddSingleton<IPythonWorkerHost, PythonWorkerHost>();
builder.Services.AddSingleton<IDocumentParsingService, DocumentParsingService>();
builder.Services.AddSingleton<IRagPipelineFactory, RagPipelineFactory>();
builder.Services.AddSingleton<IRagService, RagService>();
builder.Services.AddSingleton<LlamaIndexPipeline>();
builder.Services.AddSingleton<IKnowledgeQueryPlanner, KnowledgeQueryPlanner>();
builder.Services.AddSingleton<IKnowledgeRetrievalService, KnowledgeRetrievalService>();
builder.Services.AddSingleton<ICodeRepositoryIndexService, CodeRepositoryIndexService>();
builder.Services.AddSingleton<ICodeRepositoryIndexProgressStore, CodeRepositoryIndexProgressStore>();
builder.Services.AddSingleton<IToolDispatcher, ToolDispatcher>();
builder.Services.AddSingleton<IChatPromptBuilder, ChatPromptBuilder>();
builder.Services.AddSingleton<ILlmChatClient, LlmChatClient>();
builder.Services.AddSingleton<ILabeledStepRunner, LabeledStepRunner>();
builder.Services.AddSingleton<IAgentLoop, AgentLoop>();
builder.Services.AddSingleton<IAgentProviderEnvironmentService, AgentProviderEnvironmentService>();
builder.Services.AddSingleton<ICodexModelPolicyService, CodexModelPolicyService>();
builder.Services.AddSingleton<IImageOcrPolicyService, ImageOcrPolicyService>();
builder.Services.AddSingleton<IChatImageAttachmentService, ChatImageAttachmentService>();
builder.Services.AddSingleton<IChatFileAttachmentService, ChatFileAttachmentService>();
builder.Services.AddSingleton<IChatUploadLibraryService, ChatUploadLibraryService>();
builder.Services.AddSingleton<IProjectReferenceContextService, ProjectReferenceContextService>();
builder.Services.AddSingleton<IMarkdownDocumentReferenceContextService, MarkdownDocumentReferenceContextService>();
builder.Services.AddSingleton<IProjectAgentMarkdownIndexContextService, ProjectAgentMarkdownIndexContextService>();
builder.Services.AddSingleton<IImageOcrService, ImageOcrService>();
builder.Services.AddSingleton<ICodexChatService, CodexChatService>();
builder.Services.AddSingleton<IDshChatService, DshChatService>();
builder.Services.AddSingleton<IChatOrchestrator, ChatOrchestrator>();
builder.Services.AddSingleton<IUsageStatisticsService, UsageStatisticsService>();
builder.Services.AddSingleton<ChatWebSocketHandler>();
builder.Services.AddSingleton<ICodeRepositoryManager, CodeRepositoryManager>();
builder.Services.AddSingleton<ICodeRuntimeManager, CodeRuntimeManager>();
builder.Services.AddSingleton<IGitWorkspaceService, GitWorkspaceService>();
builder.Services.AddSingleton<ICodeRepositoryGitService, CodeRepositoryGitService>();
builder.Services.AddSingleton<IProjectAutoGitUpdateService, ProjectAutoGitUpdateService>();
builder.Services.AddHostedService<ProjectAutoGitUpdateHostedService>();
builder.Services.AddSingleton<IProjectPushService, ProjectPushService>();
builder.Services.AddHostedService<ProjectPushHostedService>();
builder.Services.AddSingleton<IDingTalkGroupAgentService, DingTalkGroupAgentService>();
builder.Services.AddHostedService<DingTalkStreamHostedService>();
builder.Services.AddSingleton<IDashboardApplicationWorkspace, DashboardApplicationWorkspace>();
builder.Services.AddSingleton<IDashboardRuntimeService, DashboardRuntimeService>();
builder.Services.AddSingleton<IDashboardGitService, DashboardGitService>();
builder.Services.AddSingleton<ICodeRepositoryCloneWebSocketHandler, CodeRepositoryCloneWebSocketHandler>();
builder.Services.AddSingleton<ICodeRepositoryPackageWebSocketHandler, CodeRepositoryPackageWebSocketHandler>();
builder.Services.AddSingleton<ISqlSugarClient>(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    
    return new SqlSugarScope(new ConnectionConfig
    {
        ConnectionString = connectionString,
        DbType = DbType.SqlServer,
        IsAutoCloseConnection = true,
        InitKeyType = InitKeyType.Attribute
    });
});

var app = builder.Build();

if (builder.Configuration.GetValue("Database:CodeFirst", true))
{
    try
    {
        app.Services.GetRequiredService<IModelSchemaInitializer>().Initialize();
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Model settings CodeFirst initialization failed. The application will not start with an incomplete database schema.");
        throw;
    }
}
else
{
    app.Logger.LogWarning("Database:CodeFirst is disabled. Pending database schema migrations, including project automatic Git update columns, will not be applied.");
}

try
{
    await app.Services.GetRequiredService<IAuthService>().EnsureInitialAdministratorAsync(CancellationToken.None);
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Administrator initialization failed. Startup is aborted.");
    throw;
}

app.UseInject();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/Settings/swagger.json", "AiAgent Settings API");
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "AiAgent Backend API v1");
    options.RoutePrefix = "swagger";
});

app.UseCors("AiAgentCors");
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1") && !context.Request.Path.StartsWithSegments("/api/v1/auth"))
    {
        var auth = context.RequestServices.GetRequiredService<IAuthService>();
        if (await auth.TryGetCurrentUserAsync(context, context.RequestAborted) == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "请先登录。" }, context.RequestAborted);
            return;
        }
    }
    await next();
});
app.UseWebSockets();
app.Map("/api/v1/knowledge/ws", async context =>
{
    var hub = context.RequestServices.GetRequiredService<IKnowledgeProgressHub>();
    await hub.HandleClientAsync(context);
});
app.Map("/api/v1/chat/ws", async context =>
{
    var handler = context.RequestServices.GetRequiredService<ChatWebSocketHandler>();
    await handler.HandleClientAsync(context);
});
app.Map("/api/v1/code-repositories/clone/ws", async context =>
{
    var handler = context.RequestServices.GetRequiredService<ICodeRepositoryCloneWebSocketHandler>();
    await handler.HandleClientAsync(context);
});
app.Map("/api/v1/code-repositories/package/ws", async context =>
{
    var handler = context.RequestServices.GetRequiredService<ICodeRepositoryPackageWebSocketHandler>();
    await handler.HandleClientAsync(context);
});
app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapControllers();
app.Run();
