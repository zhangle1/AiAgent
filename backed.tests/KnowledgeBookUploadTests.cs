using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Knowledge;
using AiAgent.Backend.Services.TaskQueue;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests;

[Collection("Swagger metadata")]
public sealed class KnowledgeBookUploadTests
{
    [Fact]
    public async Task NestedResourceFolderIsVisibleInTreeAndRawFilesystem()
    {
        using var fixture = new KnowledgeWorkspaceTests();
        await fixture.CheckMultipartBookUpload(nested: true);
    }
    [Fact]
    public async Task MultipartPdfBookIsSavedAndVisibleBeforeParsing()
    {
        // Reuse the isolated fixture, never the application's config/data directory.
        using var fixture = new KnowledgeWorkspaceTests();
        await fixture.CheckMultipartBookUpload();
    }
}

public sealed partial class KnowledgeWorkspaceTests
{
    internal async Task CheckMultipartBookUpload(bool nested = false)
    {
        using var db = Database();
        db.CodeFirst.InitTables(typeof(AiKnowledgeDirectory));
        using var files = new SemanticFiles();
        var settings = new KnowledgeCompilerSettings(db);
        using var worker = new KnowledgeCompilationWorker(db, settings, null!, NullLogger<KnowledgeCompilationWorker>.Instance);
        var resources = new KnowledgeResourceService(db, files.Paths, null!, worker, settings, null!, null!,
            NullLogger<KnowledgeBaseManager>.Instance, new Microsoft.AspNetCore.Http.HttpContextAccessor(), null!);
        Directory.CreateDirectory(files.Paths.RootPath);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            Args = [], ContentRootPath = files.Paths.RootPath, EnvironmentName = "Testing",
            ApplicationName = typeof(KnowledgeResourceAppService).Assembly.FullName
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Inject();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(KnowledgeResourceAppService).Assembly).AddDynamicApiControllers().AddControllersAsServices();
        builder.Services.AddTransient(_ => new KnowledgeResourceAppService(resources, null!, null!, new Microsoft.AspNetCore.Http.HttpContextAccessor()));
        await using var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) {
            BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
        const string name = "Design and Optimization of Manufacturing Systems, Second Edition.pdf";
        var bytes = new byte[(int)Math.Ceiling(16.1 * 1024 * 1024)];
        System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% Synthetic upload fixture\n").CopyTo(bytes, 0);
        using var form = new MultipartFormDataContent();
        var parent = nested ? (await resources.CreateDirectoryAsync(new() { ParentUri = "viking://resources/", Name = "Books" }, default)).Uri : "viking://resources/";
        form.Add(new StringContent(parent), "Uri");
        form.Add(new StringContent("true"), "Process");
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(content, "Files", name);
        using var response = await client.PostAsync("/api/v1/knowledge-resources/upload", form);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {body}");
        using var result = JsonDocument.Parse(body);
        Assert.Equal("imported", result.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        var node = Assert.Single(resources.Tree(), n => n.Kind == "file");
        Assert.Equal(name, node.Name);
        Assert.Equal(parent, node.ParentUri);
        Assert.Equal(bytes.Length, node.Size);
        var document = db.Queryable<AiKnowledgeDocument>().InSingle(node.DocumentId!.Value);
        Assert.Equal(KnowledgeResourceRawStore.PathFor(files.Paths, "resource-store", node.Uri), document.StoragePath);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(document.StoragePath));
        var task = Assert.Single(result.RootElement.GetProperty("tasks").EnumerateArray());
        Assert.Equal(document.Id, task.GetProperty("document_id").GetInt64());
        Assert.Equal("queued", task.GetProperty("status").GetString());
        // The worker is deliberately not started; raw upload must not depend on conversion/model success.
        Assert.Empty(db.Queryable<AiKnowledgeParsedDocument>().ToList());
        await app.StopAsync();
    }
}
