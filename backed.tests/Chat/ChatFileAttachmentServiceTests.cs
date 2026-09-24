using System.IO;
using System.Text;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Parsing;
using AiAgent.Backend.Services.Rag;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgent.Backend.Tests.Chat;

public sealed class ChatFileAttachmentServiceTests
{
    [Theory]
    [InlineData("legacy.xls")]
    [InlineData("legacy.doc")]
    public async Task LegacyExcelAndWordAttachmentsAreAcceptedForServerSideExtraction(string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiagent-chat-files-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ChatAttachments:RootPath"] = root
            }).Build();
            var service = new ChatFileAttachmentService(configuration, new UnusedDocumentParser(), NullLogger<ChatFileAttachmentService>.Instance);
            var oleHeader = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0, 0, 0, 0, 0 };
            await using var stream = new MemoryStream(oleHeader);
            var formFile = new FormFile(stream, 0, stream.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/vnd.ms-office"
            };

            var uploaded = await service.SaveAsync(new AuthenticatedUser("user-1", "tester"), formFile, default);

            Assert.Equal("ready", uploaded.ExtractionStatus);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("page.html", "text/html")]
    [InlineData("page.htm", "text/html")]
    public async Task HtmlAttachmentUploadsAndExtractsVisibleText(string fileName, string contentType)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiagent-chat-files-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ChatAttachments:RootPath"] = root
            }).Build();
            var service = new ChatFileAttachmentService(configuration, new UnusedDocumentParser(), NullLogger<ChatFileAttachmentService>.Instance);
            const string html = "<!doctype html><html><head><title>PMC analysis</title><style>.secret{display:none}</style><script>alert('hidden')</script></head><body><h1>看板分析</h1><p>Issue &amp; resolution</p></body></html>";
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
            var formFile = new FormFile(stream, 0, stream.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };

            var uploaded = await service.SaveAsync(new AuthenticatedUser("user-1", "tester"), formFile, default);
            var preview = await service.ExtractPreviewAsync(new AuthenticatedUser("user-1", "tester"), null, uploaded.Id, default);

            Assert.Equal("ready", uploaded.ExtractionStatus);
            Assert.Equal("text/html", uploaded.ContentType);
            Assert.Contains("PMC analysis", preview.Content);
            Assert.Contains("看板分析", preview.Content);
            Assert.Contains("Issue & resolution", preview.Content);
            Assert.DoesNotContain("alert", preview.Content);
            Assert.DoesNotContain("display:none", preview.Content);
            Assert.DoesNotContain("<h1>", preview.Content);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class UnusedDocumentParser : IDocumentParsingService
    {
        public Task<RagOperationResult> PreflightAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DocumentParseResult> ParsePdfAsync(DocumentParseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
