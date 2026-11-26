using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Ontology;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace AiAgent.Backend.Tests;

[Collection("Swagger metadata")]
public sealed class OntologySourceHttpTests
{
    public class Auth : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(IAuthService.TryGetCurrentUserAsync)
            ? Task.FromResult<AuthenticatedUser?>(new("fixture","fixture")) : throw new NotSupportedException();
    }
    private sealed class Access : IProjectAccessService
    {
        public bool CanAccess(AuthenticatedUser user, long projectId) => projectId == 43;
        public List<long> GetAccessibleProjectIds(AuthenticatedUser user) => [43];
    }

    [Fact]
    public async Task BrowserContractCanCreateVerifySaveReadAndRemoveARealSource()
    {
        var root = Directory.CreateTempSubdirectory("ontology-routes-");
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root.FullName,
                ApplicationName = typeof(OntologyAppService).Assembly.FullName, EnvironmentName = "Testing", Args = [] });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection();
            var dbPath = Path.Combine(root.FullName,"sample.db");
            builder.Configuration["Ontology:Projects:43:Sqlite:sample"] = dbPath;
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                db.Open();
                using var create = db.CreateCommand();
                create.CommandText = "CREATE TABLE records(id INTEGER, status TEXT); INSERT INTO records VALUES(7,'ready')";
                create.ExecuteNonQuery();
            }
            builder.Inject();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllers().AddApplicationPart(typeof(OntologyAppService).Assembly).AddDynamicApiControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator, Microsoft.AspNetCore.Mvc.Controllers.ServiceBasedControllerActivator>();
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            builder.Services.AddSingleton<IAuthService>(DispatchProxy.Create<IAuthService,Auth>());
            builder.Services.AddSingleton<IProjectAccessService,Access>();
            builder.Services.AddSingleton<OntologyStore>();
            builder.Services.AddSingleton<OntologySources>();
            builder.Services.AddTransient<OntologyAppService>();
            await using var app = builder.Build();
            app.UseInject(); app.MapControllers(); await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
            using var connectionModel = await client.PostAsJsonAsync("/api/v1/ontology/43/models", new { name = "连接测试" });
            connectionModel.EnsureSuccessStatusCode();
            var connectionId = (await connectionModel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
            var connectionApi = "/api/v1/ontology/43/models/" + connectionId;
            var connectionSource = new { id = Guid.NewGuid().ToString("N"), label = "项目数据库", connection = new { type = "sqlite", database = "sample" } };
            var connectionBody = new { version = 1, source = connectionSource, password = "fixture-only-credential" };
            using var testedConnection = await client.PostAsJsonAsync(connectionApi + "/sources/test", connectionBody);
            testedConnection.EnsureSuccessStatusCode();
            var incomplete = new { version = 1, source = new { connectionSource.id, connectionSource.label, connectionSource.connection, relation = "unfinished" } };
            using var testedPartial = await client.PostAsJsonAsync(connectionApi + "/sources/test", incomplete);
            testedPartial.EnsureSuccessStatusCode();
            using var savePartial = await client.PutAsJsonAsync(connectionApi + "/sources", incomplete);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, savePartial.StatusCode);
            using var unavailable = await client.PostAsJsonAsync(connectionApi + "/sources/test", new { version = 1,
                source = new { connectionSource.id, connectionSource.label, connection = new { type = "sqlite", database = "missing" } } });
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, unavailable.StatusCode);
            using var savedConnection = await client.PutAsJsonAsync(connectionApi + "/sources", connectionBody);
            savedConnection.EnsureSuccessStatusCode();
            using var connectionPreview = await client.PostAsJsonAsync(connectionApi + "/lookup", new LookupRequest(2, [connectionSource.id], "7"));
            connectionPreview.EnsureSuccessStatusCode();
            var connectionText = (await connectionPreview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString();
            var connectionContext = JsonSerializer.Deserialize<JsonElement>(connectionText!);
            var sqlite = connectionContext.GetProperty("sources")[0];
            Assert.Equal("OntologyModel", connectionContext.GetProperty("@type").GetString());
            Assert.Equal("sample", sqlite.GetProperty("databaseAlias").GetString());
            Assert.True(sqlite.TryGetProperty("verifiedAt", out _));
            Assert.False(sqlite.TryGetProperty("result", out _));
            Assert.DoesNotContain("main.records", connectionText);
            Assert.DoesNotContain(dbPath, connectionText);
            Assert.DoesNotContain("fixture-only-credential", connectionText);
            Assert.DoesNotContain("ready", connectionText);

            const string fixturePassword = "fixture-sql-password";
            var sqlServer = new OntologySource(Guid.NewGuid().ToString("N"), "MES", new(
                "sqlserver", "db.example.invalid", 1888, "mes_demo", "reader", TrustServerCertificate: true));
            app.Services.GetRequiredService<OntologyStore>().SaveSource("fixture", 43, connectionId!, 2, sqlServer, fixturePassword, DateTimeOffset.UtcNow);
            using var previewRequest = await client.PostAsJsonAsync(connectionApi + "/lookup", new LookupRequest(3, [sqlServer.Id], null));
            previewRequest.EnsureSuccessStatusCode();
            var previewText = (await previewRequest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString()!;
            var database = JsonSerializer.Deserialize<JsonElement>(previewText).GetProperty("sources")[0];
            Assert.Equal("db.example.invalid", database.GetProperty("host").GetString());
            Assert.Equal(1888, database.GetProperty("port").GetInt32());
            Assert.Equal("mes_demo", database.GetProperty("database").GetString());
            Assert.Equal("reader", database.GetProperty("username").GetString());
            Assert.True(database.GetProperty("trustServerCertificate").GetBoolean());
            Assert.EndsWith($"#source-{sqlServer.Id}-credential", database.GetProperty("credential").GetProperty("@id").GetString());
            Assert.DoesNotContain(fixturePassword, previewText);

            using var sendRequest = await client.PostAsJsonAsync(connectionApi + "/lookup",
                new LookupRequest(3, [sqlServer.Id], null, IncludeDatabasePasswords: true));
            sendRequest.EnsureSuccessStatusCode();
            Assert.True(sendRequest.Headers.CacheControl?.NoStore);
            var sendText = (await sendRequest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString()!;
            var sentDatabase = JsonSerializer.Deserialize<JsonElement>(sendText).GetProperty("sources")[0];
            Assert.Equal(fixturePassword, sentDatabase.GetProperty("credential").GetProperty("password").GetString());
            using var wrongProject = await client.PostAsJsonAsync(connectionApi.Replace("/43/", "/44/") + "/lookup",
                new LookupRequest(3, [sqlServer.Id], null, IncludeDatabasePasswords: true));
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, wrongProject.StatusCode);
            using var outdated = await client.PostAsJsonAsync(connectionApi + "/lookup",
                new LookupRequest(2, [sqlServer.Id], null, IncludeDatabasePasswords: true));
            Assert.Equal(System.Net.HttpStatusCode.Conflict, outdated.StatusCode);
            using var created = await client.PostAsJsonAsync("/api/v1/ontology/43/models", new { name = "Test" });
            created.EnsureSuccessStatusCode();
            var model = await created.Content.ReadFromJsonAsync<JsonElement>();
            var endpoint = "/api/v1/ontology/43/models/" + model.GetProperty("id").GetString();
            var sourceId = Guid.NewGuid().ToString("N");
            var source = new {id=sourceId,label="records",connection=new {type="sqlite",database="sample"},relation="records",keyColumn="id",valueColumn="status"};
            var body = new {version=1,source,password=""};
            using var verify = await client.PostAsJsonAsync(endpoint + "/sources/test",body);
            verify.EnsureSuccessStatusCode();
            using var saved = await client.PutAsJsonAsync(endpoint + "/sources",body);
            saved.EnsureSuccessStatusCode();
            var data = await saved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("id", data.GetProperty("sources")[0].GetProperty("definition").GetProperty("keyColumn").GetString());
            using var read = await client.PostAsJsonAsync(endpoint + "/lookup",new {version=2,sourceIds=new[]{sourceId},key="7"});
            read.EnsureSuccessStatusCode();
            var dataText = (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString()!;
            var row = JsonSerializer.Deserialize<JsonElement>(dataText).GetProperty("sources")[0].GetProperty("result");
            Assert.Equal("ready", row.GetProperty("values").GetProperty("status").GetString());
            using var stale = await client.DeleteAsync(endpoint + "/sources/" + sourceId + "?version=1");
            Assert.Equal(System.Net.HttpStatusCode.Conflict,stale.StatusCode);
            using var removed = await client.DeleteAsync(endpoint + "/sources/" + sourceId + "?version=2");
            removed.EnsureSuccessStatusCode();
            Assert.Equal(0,(await removed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sources").GetArrayLength());
            using var renamed = await client.PutAsJsonAsync(endpoint + "/name", new { version = 3, name = "模型 B" });
            renamed.EnsureSuccessStatusCode();
            var renamedModel = await renamed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(model.GetProperty("id").GetString(), renamedModel.GetProperty("id").GetString());
            Assert.Equal("模型 B", renamedModel.GetProperty("name").GetString());
            using var staleDelete = await client.DeleteAsync(endpoint + "?version=3");
            Assert.Equal(System.Net.HttpStatusCode.Conflict, staleDelete.StatusCode);
            var note = new OntologySource(sourceId, "字段说明", new("note"), "", "", "", Note: "查询键使用记录编号");
            using var savedNote = await client.PutAsJsonAsync(endpoint + "/sources", new SourceRequest(4, note, null));
            savedNote.EnsureSuccessStatusCode();
            foreach (var key in new[] { "", "7" })
            {
                using var preview = await client.PostAsJsonAsync(endpoint + "/lookup", new LookupRequest(5, [sourceId], key));
                preview.EnsureSuccessStatusCode();
                var text = (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString();
                Assert.Contains("查询键使用记录编号", text);
                Assert.DoesNotContain("读取时间", text);
            }
            using var deleted = await client.DeleteAsync(endpoint + "?version=5");
            deleted.EnsureSuccessStatusCode();
            using var missing = await client.GetAsync(endpoint);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
            await app.StopAsync();
        }
        finally { root.Delete(true); }
    }
}
