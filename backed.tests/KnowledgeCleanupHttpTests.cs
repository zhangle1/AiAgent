using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Knowledge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi.Models;
using SqlSugar;

namespace AiAgent.Backend.Tests;

[Collection("Swagger metadata")]
public sealed class KnowledgeCleanupHttpTests
{
    [Fact]
    public async Task DynamicApiCleanupSupportsTheExactPreviewAndPostUrlsUsedByTheBrowser()
    {
        var root = Path.Combine(Path.GetTempPath(), "aiagent-cleanup-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
                Args = [], ContentRootPath = root, EnvironmentName = "Testing",
                ApplicationName = typeof(KnowledgeResourceCleanupAppService).Assembly.FullName
            });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["KnowledgeStorage:RootPath"] = Path.Combine(root, "current") });
            builder.Inject();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllers().AddApplicationPart(typeof(KnowledgeResourceCleanupAppService).Assembly).AddDynamicApiControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options => {
                options.SwaggerDoc("Settings", new OpenApiInfo { Title = "Settings", Version = "Settings" });
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "Backend", Version = "v1" });
            });
            builder.Services.AddHttpContextAccessor();
            using var db = new SqlSugarScope(new ConnectionConfig {
                DbType = DbType.Sqlite, ConnectionString = $"Data Source={Path.Combine(root, "fixture.db")};Pooling=False",
                IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
                ConfigureExternalServices = new() { EntityService = (_, column) => {
                    if (column.DataType == "nvarchar(max)") column.DataType = "text";
                    if (column.IsIdentity) column.DataType = "INTEGER";
                } }
            });
            db.CodeFirst.InitTables(typeof(AiKnowledgeBase), typeof(AiKnowledgeDocument), typeof(AiKnowledgeJob));
            var kb = new AiKnowledgeBase { Name = "resource-store", DisplayName = "fixture" };
            kb.Id = db.Insertable(kb).ExecuteReturnBigIdentity();
            var document = new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, OriginalFileName = "legacy.md", Extension = ".md",
                ResourceUri = "viking://user/fixture/sessions/legacy.md", StoragePath = Path.Combine(root, "previous", "legacy.md") };
            document.Id = db.Insertable(document).ExecuteReturnBigIdentity();
            var auth = DispatchProxy.Create<IAuthService, KnowledgeWorkspaceTests.TeamAuth>();
            var paths = new KnowledgePathService(builder.Configuration, NullLogger<KnowledgePathService>.Instance);
            builder.Services.AddSingleton<ISqlSugarClient>(db);
            builder.Services.AddSingleton<IKnowledgePathService>(paths);
            builder.Services.AddSingleton<IAuthService>(auth);
            builder.Services.AddSingleton<KnowledgeResourceCleanupService>();

            await using var app = builder.Build();
            app.UseDeveloperExceptionPage();
            app.UseInject();
            app.MapControllers();
            await app.StartAsync();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),
                Timeout = TimeSpan.FromSeconds(15)
            };
            using var previewResponse = await client.GetAsync("/api/v1/knowledge-resources/cleanup/preview");
            var previewBody = await previewResponse.Content.ReadAsStringAsync();
            Assert.True(previewResponse.StatusCode == HttpStatusCode.OK, $"Preview: HTTP {(int)previewResponse.StatusCode}: {previewBody}");
            using var preview = JsonDocument.Parse(previewBody);
            Assert.Equal(document.Id, preview.RootElement.GetProperty("items")[0].GetProperty("document_id").GetInt64());
            Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(document.Id).IsDeleted);

            var payload = JsonSerializer.Serialize(new { document_ids = new[] { document.Id }, snapshot_sha256 = preview.RootElement.GetProperty("snapshot_sha256").GetString() });
            using var response = await client.PostAsync("/api/v1/knowledge-resources/cleanup", new StringContent(payload, Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            var routes = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
                .Where(action => action.DisplayName?.Contains(nameof(KnowledgeResourceCleanupAppService), StringComparison.Ordinal) == true)
                .Select(action => action.AttributeRouteInfo?.Template);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Cleanup: HTTP {(int)response.StatusCode}: {body}\nRegistered cleanup routes: {string.Join(", ", routes)}");
            using var result = JsonDocument.Parse(body);
            Assert.Equal(1, result.RootElement.GetProperty("removed_count").GetInt32());
            Assert.True(db.Queryable<AiKnowledgeDocument>().InSingle(document.Id).IsDeleted);
            await app.StopAsync();
        }
        finally
        {
            var full = Path.GetFullPath(root);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(full).StartsWith("aiagent-cleanup-http-", StringComparison.Ordinal)) Directory.Delete(full, true);
        }
    }
}
