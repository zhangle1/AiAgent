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
    public void GraphRejectsCyclesUnknownPluginsAndDanglingReferences()
    {
        var nodes = new List<OntologyNode> { new("a", "source", "MES"), new("b", "output", "Chat") };
        OntologyStore.Validate(new(nodes, [new("a", "b")]));
        Assert.Throws<ArgumentException>(() => OntologyStore.Validate(new(nodes, [new("a", "b"), new("b", "a")])));
        Assert.Throws<ArgumentException>(() => OntologyStore.Validate(new(nodes, [new("a", "missing")])));
        Assert.Throws<ArgumentException>(() => OntologyStore.Validate(new([new("x", "shell", "unsafe")], [])));
        Assert.Throws<ArgumentException>(() => OntologyStore.Validate(new(nodes, [new("a", "b"), new("a", "b")])));
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
