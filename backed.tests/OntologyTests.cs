using System.IO;
using AiAgent.Backend.Services.Ontology;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace AiAgent.Backend.Tests;

public sealed class OntologyTests
{
    [Fact]
    public void OracleUsesRegisteredServiceAndBoundKeyForOrdinaryIdentifiers()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["Ontology:Projects:7:Targets:0"] = "oracle/db.example.invalid:2484/app.example";
        var store = new OntologyStore(new TestEnvironment(Path.GetTempPath()), new EphemeralDataProtectionProvider());
        var sources = new OntologySources(store, config);
        var connection = new OntologyConnection("oracle", "db.example.invalid", 2484, "app.example", "reader");
        using var target = sources.OpenTarget(7, connection, "fixture");
        Assert.IsType<Oracle.ManagedDataAccess.Client.OracleConnection>(target);
        Assert.StartsWith("tcps://db.example.invalid:2484/app.example?", target.DataSource);
        Assert.Throws<ArgumentException>(() => sources.OpenTarget(8, connection, "fixture"));
        var source = new OntologySource(Guid.NewGuid().ToString("N"), "Oracle", connection, "app.records", "id", "status");
        var statements = OntologySources.SelectStatements(source);
        Assert.Equal("""SELECT "ID", "STATUS" FROM "APP"."RECORDS" WHERE 1=0""", statements.Metadata);
        Assert.Equal("""SELECT "ID", "STATUS" FROM "APP"."RECORDS" WHERE "ID" = :lookup_key AND ROWNUM <= 2""", statements.Lookup);
    }

    [Fact]
    public async Task VerifiedSourceReadsOnlySelectedFieldsAndRejectsStaleVersion()
    {
        var root = Directory.CreateTempSubdirectory("ontology-sql-");
        try
        {
            var file = Path.Combine(root.FullName, "sample.sqlite");
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=false"))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "CREATE TABLE records(id TEXT, status TEXT, hidden TEXT); INSERT INTO records VALUES('A1','open','private-value')";
                command.ExecuteNonQuery();
            }
            var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
            config["Ontology:Projects:7:Sqlite:sample"] = file;
            var store = new OntologyStore(new TestEnvironment(root.FullName), new EphemeralDataProtectionProvider());
            var sources = new OntologySources(store, config);
            var id = JsonSerializer.SerializeToElement(store.Create("alice", 7, "通用模型")).GetProperty("id").GetString()!;
            var source = new OntologySource(Guid.NewGuid().ToString("N"), "测试库", new("sqlite", Database: "sample"), "records", "id", "status");
            var request = new SourceRequest(1, source, null);
            await sources.Verify("alice", 7, id, request, false, default);
            Assert.Empty(store.Get("alice", 7, id).Sources);
            await sources.Verify("alice", 7, id, request, true, default);
            var result = JsonSerializer.SerializeToElement(await sources.Lookup("alice", 7, id, new(2, [source.Id], "A1"), default)).GetProperty("text").GetString()!;
            Assert.Contains("open", result);
            Assert.DoesNotContain("private-value", result);
            Assert.DoesNotContain(file, result);
            await Assert.ThrowsAsync<InvalidOperationException>(() => sources.Verify("alice", 7, id, request, true, default));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => sources.Lookup("bob", 7, id, new(2, [source.Id], "A1"), default));
        }
        finally { root.Delete(true); }
    }
    [Fact]
    public void GraphRejectsCyclesUnknownPluginsAndDanglingReferences()
    {
        var nodes = new List<OntologyNode> { new("a", "source", "MES"), new("b", "output", "Chat") };
        OntologyStore.Validate(new(nodes, [new("a", "b")]));
        Assert.Throws<ArgumentException>(() => OntologyStore.Validate(new(nodes, [new("a", "b"), new("b", "a")])));
        Assert.Throws<ArgumentException>(() => OntologyStore.Validate(new(nodes, [new("a", "missing")])));
        Assert.Throws<ArgumentException>(() => OntologyStore.Validate(new([new("x", "shell", "unsafe")], [])));
        Assert.Throws<ArgumentException>(() => OntologyStore.Validate(new(nodes, [new("a", "b"), new("a", "b")])));
    }

    private sealed class HttpFixture : System.Net.Http.HttpMessageHandler, System.Net.Http.IHttpClientFactory
    {
        public System.Net.Http.HttpClient CreateClient(string name) => new(this, false);
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://api.example.invalid/records/A1", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer demo", request.Headers.Authorization?.ToString());
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new System.Net.Http.StringContent("""{"id":"A1","status":"open","other":"hidden"}""")
            });
        }
    }

    [Fact]
    public async Task RegisteredHttpComponentFiltersResultAndReusesItsCredential()
    {
        var root = Directory.CreateTempSubdirectory("ontology-api-");
        try
        {
            using var http = new HttpFixture();
            var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
            config["Ontology:Projects:7:Http:records"] = "https://api.example.invalid/records/{key}";
            var store = new OntologyStore(new TestEnvironment(root.FullName), new EphemeralDataProtectionProvider());
            var sources = new OntologySources(store, config, http);
            var id = JsonSerializer.SerializeToElement(store.Create("alice", 7, "接口模型")).GetProperty("id").GetString()!;
            var source = new OntologySource(Guid.NewGuid().ToString("N"), "读接口", new("http"), "records", "id", "status", "A1");
            await sources.Verify("alice", 7, id, new(1,source,"demo"), true, default);
            var result = JsonSerializer.SerializeToElement(await sources.Lookup("alice",7,id,new(2,[source.Id],"A1"),default)).GetProperty("text").GetString()!;
            Assert.Contains("open", result);
            Assert.DoesNotContain("hidden", result);
            Assert.DoesNotContain("Bearer", result);
            await Assert.ThrowsAsync<ArgumentException>(() => sources.Verify("alice",7,id,new(2,source with {Relation="other"},null),true,default));
        }
        finally { root.Delete(true); }
    }
    [Fact]
    public void StoreIsIsolatedVersionedAndNeverReturnsCredentialsInContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "ontology-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new OntologyStore(new TestEnvironment(root), new EphemeralDataProtectionProvider());
            var created = JsonSerializer.SerializeToElement(store.Create("alice", 1, "Synthetic MES"));
            var id = created.GetProperty("id").GetString()!;
            var connection = new OntologyConnection("mysql", "synthetic-host", 3306, "synthetic-db", "synthetic-user");
            var saved = JsonSerializer.SerializeToElement(store.Update("alice", 1, id, 1, null, connection, "synthetic-secret"));
            Assert.True(saved.GetProperty("has_password").GetBoolean());
            Assert.DoesNotContain("synthetic-secret", saved.ToString());
            Assert.Throws<InvalidOperationException>(() => store.Update("alice", 1, id, 1, new([], []), null, null));
            Assert.Throws<KeyNotFoundException>(() => store.Get("bob", 1, id));
            Assert.Throws<KeyNotFoundException>(() => store.Get("alice", 2, id));
            Assert.Throws<ArgumentException>(() => store.Get("alice", 1, "../outside"));
            Assert.Throws<ArgumentException>(() => store.Update("alice", 1, id, 2, new([new("bad", "shell", "unsafe")], []), connection with { Host = "changed" }, "replacement"));
            Assert.Equal(2, store.Get("alice", 1, id).Version);
            Assert.Equal("synthetic-host", store.Get("alice", 1, id).Connection.Host);
            store.Update("alice", 1, id, 2, null, connection, "");
            Assert.NotNull(store.Get("alice", 1, id).ProtectedPassword);
            var context = JsonSerializer.Serialize(store.Context("alice", 1, id));
            foreach (var secret in new[] { "synthetic-host", "synthetic-user", "synthetic-secret" }) Assert.DoesNotContain(secret, context);
            var file = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Single();
            Assert.DoesNotContain("synthetic-secret", File.ReadAllText(file));
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "OntologyTest";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
