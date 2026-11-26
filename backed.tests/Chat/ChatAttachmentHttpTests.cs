using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Parsing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi.Models;

namespace AiAgent.Backend.Tests.Chat;

[Collection("Swagger metadata")]
public sealed class ChatAttachmentHttpTests
{
    [Fact]
    public async Task MultipartUploadsAcceptDocumentsAndImagesAndReturnJsonForMissingBytes()
    {
        var root = Directory.CreateTempSubdirectory("aiagent-attachment-http-");
        try
        {
            var assembly = typeof(ChatAppService).Assembly;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [], ContentRootPath = root.FullName, EnvironmentName = "Testing", ApplicationName = assembly.FullName
            });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ChatAttachments:RootPath"] = root.FullName });
            builder.Inject();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllers().AddApplicationPart(assembly).AddDynamicApiControllers().AddControllersAsServices();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("Settings", new OpenApiInfo { Title = "Settings", Version = "Settings" });
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "Backend", Version = "v1" });
            });
            builder.Services.AddHttpContextAccessor();
            var auth = DispatchProxy.Create<IAuthService, KnowledgeWorkspaceTests.TeamAuth>();
            var images = new ChatImageAttachmentService(builder.Configuration, NullLogger<ChatImageAttachmentService>.Instance);
            var parser = new DocumentParsingService(null!, NullLogger<DocumentParsingService>.Instance);
            var files = new ChatFileAttachmentService(builder.Configuration, parser, NullLogger<ChatFileAttachmentService>.Instance);
            builder.Services.AddSingleton(services => new ChatAppService(
                services.GetRequiredService<IHttpContextAccessor>(), auth, null!, images, files, null!, null!));

            await using var app = builder.Build();
            app.UseDeveloperExceptionPage();
            app.UseInject();
            app.MapControllers();
            await app.StartAsync();
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),
                Timeout = TimeSpan.FromSeconds(15)
            };
            foreach (var (name, type, bytes, expectedText) in new[]
            {
                ("日志.txt", "text/plain", Encoding.UTF8.GetBytes("中文文本"), "中文文本"),
                ("说明.md", "text/markdown", Encoding.UTF8.GetBytes("# 中文标题"), "# 中文标题"),
                ("表单.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", WordFixture(), "中文 Word"),
                ("表格.csv", "text/csv", Encoding.UTF8.GetBytes("名称,数量\n测试零件,12"), "| 测试零件 | 12 |"),
                ("页面.html", "text/html", Encoding.UTF8.GetBytes("<p>中文网页</p><script>hidden()</script>"), "中文网页")
            })
            {
                using var response = await UploadAsync(http, "files", name, type, bytes);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var attachment = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(name, attachment.RootElement.GetProperty("file_name").GetString());
                Assert.Equal(bytes.Length, attachment.RootElement.GetProperty("size_bytes").GetInt64());

                var id = attachment.RootElement.GetProperty("id").GetString();
                using var extraction = JsonDocument.Parse(await http.GetStringAsync($"/api/v1/chat/attachments/files/{id}/extraction"));
                Assert.Contains(expectedText, extraction.RootElement.GetProperty("content").GetString());
            }

            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aHfcAAAAASUVORK5CYII=");
            using var image = await UploadAsync(http, "images", "截图.png", "image/png", png);
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            using var uploadedImage = JsonDocument.Parse(await image.Content.ReadAsStringAsync());
            var imageId = uploadedImage.RootElement.GetProperty("id").GetString();
            Assert.Equal(png, await http.GetByteArrayAsync($"/api/v1/chat/attachments/{imageId}/preview"));

            using var invalidText = await UploadAsync(http, "files", "invalid.txt", "text/plain", [0xff, 0xff, 0xff]);
            Assert.Equal(HttpStatusCode.BadRequest, invalidText.StatusCode);
            using var invalidTextBody = JsonDocument.Parse(await invalidText.Content.ReadAsStringAsync());
            Assert.Contains("UTF-8", invalidTextBody.RootElement.GetProperty("message").GetString());
            using var invalidWord = await UploadAsync(http, "files", "invalid.docx", "application/octet-stream", [1, 2, 3]);
            Assert.Equal(HttpStatusCode.BadRequest, invalidWord.StatusCode);
            using var invalidImage = await UploadAsync(http, "images", "invalid.png", "image/png", [1, 2, 3]);
            Assert.Equal(HttpStatusCode.BadRequest, invalidImage.StatusCode);

            foreach (var (endpoint, name, contentType) in new[] { ("files", "empty.txt", "text/plain"), ("images", "empty.png", "image/png") })
            {
                using var empty = await UploadAsync(http, endpoint, name, contentType, []);
                Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
                using var missing = await UploadAsync(http, endpoint, name, contentType, [1], "other");
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
                foreach (var response in new[] { empty, missing })
                {
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Assert.Contains("empty", body.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
                }
            }
            await app.StopAsync();
        }
        finally { root.Delete(recursive: true); }
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string endpoint, string name, string type, byte[] bytes, string field = "file")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, field, name);
        return await client.PostAsync($"/api/v1/chat/attachments/{endpoint}", form);
    }

    internal static byte[] WordFixture()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var contentTypes = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open()))
                contentTypes.Write("""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""");
            using (var relationships = new StreamWriter(zip.CreateEntry("_rels/.rels").Open()))
                relationships.Write("""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""");
            using var document = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            document.Write("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>中文 Word</w:t></w:r></w:p></w:body></w:document>""");
        }
        return bytes.ToArray();
    }
}
