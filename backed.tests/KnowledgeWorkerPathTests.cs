using System;
using System.Collections.Generic;
using System.IO;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.PythonWorkers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

public sealed class KnowledgeWorkerPathTests
{
    [Fact]
    public void ExternalKnowledgeStoreIsAllowedWithoutOpeningItsSiblings()
    {
        var root = Path.Combine(Path.GetTempPath(), "knowledge-worker-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["KnowledgeStorage:RootPath"] = root,
            ["PythonWorkers:AllowedRoots:0"] = Path.Combine(root, "legacy-data")
        }).Build();
        var paths = new KnowledgePathService(config, NullLogger<KnowledgePathService>.Instance);
        var host = new PythonWorkerHost(config, NullLogger<PythonWorkerHost>.Instance);
        host.EnsureAllowedPath(Path.Combine(paths.RootPath, "resource-store", "raw", "book.pdf"));
        host.EnsureAllowedPath(Path.Combine(paths.RootPath, "resource-store", "parsed", "1", ".staging-test"));
        Assert.Throws<UnauthorizedAccessException>(() => host.EnsureAllowedPath(Path.Combine(root, "private", "book.pdf")));
        Assert.Throws<UnauthorizedAccessException>(() => host.EnsureAllowedPath(paths.RootPath + "-other"));
        Assert.NotEqual(KnowledgeResourceRawStore.PathFor(paths, "resource-store", "viking://resources/projects/book.pdf"),
            KnowledgeResourceRawStore.PathFor(paths, "resource-store", "viking://projects/book.pdf"));
    }
}
