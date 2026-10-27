using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using AiAgent.Backend.Services.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using Xunit.Abstractions;

namespace AiAgent.Backend.Tests;

[CollectionDefinition("Swagger metadata", DisableParallelization = true)]
public sealed class SwaggerMetadataCollection;

[Collection("Swagger metadata")]
public sealed class SwaggerDocumentTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Settings")]
    [InlineData("v1")]
    public async Task DynamicApiSwaggerDocumentsCanBeGenerated(string group)
    {
        var root = Path.Combine(Path.GetTempPath(), "aiagent-swagger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [], ContentRootPath = root, EnvironmentName = "Testing",
                ApplicationName = typeof(SettingsAppService).Assembly.FullName
            });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection();
            builder.Inject();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllers().AddApplicationPart(typeof(SettingsAppService).Assembly).AddDynamicApiControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("Settings", new OpenApiInfo { Title = "Settings", Version = "Settings" });
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "Backend", Version = "v1" });
            });
            await using var app = builder.Build();
            app.UseInject();
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.MapControllers();
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()), Timeout = TimeSpan.FromSeconds(15) };
            using var response = await client.GetAsync($"/swagger/{group}/swagger.json");
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger(group);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Swagger {group}: HTTP {(int)response.StatusCode}\n{content}");
            using var document = JsonDocument.Parse(content);
            var paths = document.RootElement.GetProperty("paths");
            Assert.NotEmpty(paths.EnumerateObject());
            var properties = paths.GetProperty("/api/v1/admin/client-releases/upload").GetProperty("post")
                .GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data")
                .GetProperty("schema").GetProperty("properties");
            Assert.Equal("string", properties.GetProperty("file").GetProperty("type").GetString());
            Assert.Equal("binary", properties.GetProperty("file").GetProperty("format").GetString());
            foreach (var field in new[] { "version", "platform", "architecture", "notes" })
                Assert.True(properties.TryGetProperty(field, out _), $"Missing multipart field: {field}");
            output.WriteLine($"{group}: HTTP 200; {paths.EnumerateObject().Count()} API paths; binary file and all multipart metadata fields present.");
            await app.StopAsync();
        }
        finally { Directory.Delete(root, true); }
    }
}
