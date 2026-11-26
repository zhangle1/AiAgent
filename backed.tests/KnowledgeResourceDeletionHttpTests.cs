using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Knowledge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace AiAgent.Backend.Tests;

[Collection("Swagger metadata")]
public sealed class KnowledgeResourceDeletionHttpTests
{
    [Fact]
    public async Task AvailablePdfCanBeRemovedThroughResourceDeleteRouteWithoutDeletingSourceBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "knowledge-delete-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = root, EnvironmentName = "Testing", ApplicationName = typeof(KnowledgeResourceAppService).Assembly.FullName });
            builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = root });
            builder.Inject(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllers().AddApplicationPart(typeof(KnowledgeResourceAppService).Assembly).AddDynamicApiControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options => {
                options.SwaggerDoc("Settings", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Settings", Version = "Settings" });
                options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Backend", Version = "v1" });
            });
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator, Microsoft.AspNetCore.Mvc.Controllers.ServiceBasedControllerActivator>();
            using var db = new SqlSugarScope(new ConnectionConfig {
                DbType = DbType.Sqlite, ConnectionString = $"Data Source={Path.Combine(root, "fixture.db")};Pooling=False", IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
                ConfigureExternalServices = new() { EntityService = (_, column) => { if (column.DataType == "nvarchar(max)") column.DataType = "text"; if (column.IsIdentity) column.DataType = "INTEGER"; } }
            });
            db.CodeFirst.InitTables(typeof(AiKnowledgeBase), typeof(AiKnowledgeDocument), typeof(AiKnowledgeJob));
            var paths = new KnowledgePathService(builder.Configuration, NullLogger<KnowledgePathService>.Instance);
            var kb = new AiKnowledgeBase { Name = "resource-store", DisplayName = "fixture" }; kb.Id = db.Insertable(kb).ExecuteReturnBigIdentity();
            var raw = paths.GetRawPath(kb.Name); Directory.CreateDirectory(raw);
            var source = Path.Combine(raw, "Factory Physics.pdf"); var bytes = "%PDF-1.7\nsynthetic deletion fixture"u8.ToArray(); File.WriteAllBytes(source, bytes);
            const string uri = "viking://resources/Factory%20Physics.pdf";
            var document = new AiKnowledgeDocument { KnowledgeBaseId = kb.Id, OriginalFileName = "Factory Physics.pdf", Extension = ".pdf", ResourceUri = uri, StoragePath = source, FileHash = paths.ComputeFileHash(source), Status = "error" };
            document.Id = db.Insertable(document).ExecuteReturnBigIdentity();
            var auth = DispatchProxy.Create<IAuthService, FixtureAuth>();
            var fixtureAuth = (FixtureAuth)(object)auth;
            builder.Services.AddSingleton<ISqlSugarClient>(db);
            builder.Services.AddSingleton<KnowledgeResourceAppService>(provider => {
                var accessor = provider.GetRequiredService<IHttpContextAccessor>();
                var resources = new KnowledgeResourceService(db, paths, null!, null!, null!, null!, null!, NullLogger<KnowledgeBaseManager>.Instance, accessor, null!);
                return new(resources, null!, auth, accessor);
            });
            await using var app = builder.Build(); app.UseInject(); app.MapControllers(); await app.StartAsync();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()), Timeout = TimeSpan.FromSeconds(15) };
            var endpoint = $"/api/v1/knowledge-resources/file?uri={Uri.EscapeDataString(uri)}&document_id={document.Id}";
            fixtureAuth.User = null;
            using (var denied = await client.DeleteAsync(endpoint)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            fixtureAuth.User = new("fixture", "fixture");
            using (var denied = await client.DeleteAsync(endpoint)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            fixtureAuth.User = new("fixture", "fixture", "admin");
            var job = new AiKnowledgeJob { KnowledgeBaseId = kb.Id, DocumentId = document.Id, Status = "queued" };
            job.Id = db.Insertable(job).ExecuteReturnBigIdentity();
            using (var busy = await client.DeleteAsync(endpoint)) Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
            Assert.False(db.Queryable<AiKnowledgeDocument>().InSingle(document.Id).IsDeleted);
            db.Updateable<AiKnowledgeJob>().SetColumns(j => j.Status == "error").Where(j => j.Id == job.Id).ExecuteCommand();
            using var response = await client.DeleteAsync(endpoint);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"DELETE returned HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            var deleted = db.Queryable<AiKnowledgeDocument>().InSingle(document.Id);
            Assert.True(deleted.IsDeleted); Assert.NotNull(deleted.DeletedAt);
            Assert.Equal(bytes, File.ReadAllBytes(source));
            using (var missing = await client.DeleteAsync(endpoint)) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            await app.StopAsync();
        }
        finally
        {
            var full = Path.GetFullPath(root);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("knowledge-delete-http-", StringComparison.Ordinal)) Directory.Delete(full, true);
        }
    }

    public class FixtureAuth : DispatchProxy
    {
        public AuthenticatedUser? User { get; set; } = new("fixture", "fixture", "admin");
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "TryGetCurrentUserAsync"
            ? Task.FromResult(User) : throw new NotSupportedException();
    }
}
