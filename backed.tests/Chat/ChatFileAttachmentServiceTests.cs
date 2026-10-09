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
    [InlineData("chat-legacy.xls", "legacy.xls")]
    [InlineData("chat-legacy.xls", "legacy.xlt")]
    [InlineData("chat-modern.xlsx", "modern.XLSX")]
    [InlineData("chat-modern.xlsm", "modern.xlsm")]
    [InlineData("chat-modern.xlsb", "modern.xlsb")]
    [InlineData("chat-modern.xltx", "modern.xltx")]
    [InlineData("chat-modern.xltm", "modern.xltm")]
    public async Task ExcelExtractsAndPersistsWithoutLibreOffice(string fixture, string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiagent-chat-files-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ChatAttachments:RootPath"] = root,
                ["Knowledge:LibreOfficePath"] = Path.Combine(root, "missing-soffice.exe")
            }).Build();
            var service = new ChatFileAttachmentService(configuration, new UnusedDocumentParser(), NullLogger<ChatFileAttachmentService>.Instance);
            var user = new AuthenticatedUser("user-1", "tester");
            await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
            var file = new FormFile(stream, 0, stream.Length, "file", fileName) { Headers = new HeaderDictionary() };
            var uploaded = await service.SaveAsync(user, file, default);
            var attachments = await service.ResolveLocalAttachmentsAsync(user, null, [uploaded.Id], default);
            var context = await service.ExtractContextAsync(attachments, default);
            Assert.Contains("# 库存", context);
            Assert.Contains("# 说明", context);
            Assert.Contains("物料 |  | 数量", context);
            Assert.Contains("测试零件 |  | 12", context);
            Assert.Contains("中文正常", context);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExtractContextAsync(attachments, cancelled.Token));
            var persisted = await service.PersistForSessionAsync(user, "session-1", [uploaded.Id], default);
            var extractions = await service.PersistExtractionFilesAsync(user, "session-1", persisted, default);
            Assert.Single(extractions);
            var preview = await service.ExtractPreviewAsync(user, "session-1", uploaded.Id, default);
            Assert.Contains("测试零件", preview.Content);
            var restarted = new ChatFileAttachmentService(configuration, new UnusedDocumentParser(), NullLogger<ChatFileAttachmentService>.Instance);
            var restored = await restarted.ResolveLocalAttachmentsAsync(user, "session-1", [uploaded.Id], default);
            Assert.Contains("中文正常", await restarted.ExtractContextAsync(restored, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ResolveLocalAttachmentsAsync(new AuthenticatedUser("user-2", "other"), "session-1", [uploaded.Id], default));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

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

    [Fact]
    public async Task RawBiff2WorkbookExtractsWithoutOleContainer()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiagent-chat-files-{Guid.NewGuid():N}");
        try
        {
            var service = CreateService(root);
            var user = new AuthenticatedUser("user-1", "tester");
            await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "chat-biff2.xls"));
            var uploaded = await service.SaveAsync(user, new FormFile(stream, 0, stream.Length, "file", "early.xls"), default);
            var preview = await service.ExtractPreviewAsync(user, null, uploaded.Id, default);
            Assert.Contains("Item |  | Count", preview.Content);
            Assert.Contains("Part |  | 12", preview.Content);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("xls")]
    [InlineData("xlt")]
    [InlineData("xlsx")]
    [InlineData("xlsm")]
    [InlineData("xlsb")]
    [InlineData("xltx")]
    [InlineData("xltm")]
    public async Task RejectsNonExcelBytesAndCleansUpload(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiagent-chat-files-{Guid.NewGuid():N}");
        try
        {
            var service = CreateService(root);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("This is not an Excel workbook."));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(new AuthenticatedUser("user-1", "tester"),
                new FormFile(stream, 0, stream.Length, "file", $"fake.{extension}"), default));
            Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SpreadsheetExtractionHonorsConfiguredLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiagent-chat-files-{Guid.NewGuid():N}");
        try
        {
            var service = CreateService(root, 1000);
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var sheet = workbook.AddWorksheet("Large");
            for (var row = 1; row <= 100; row++) sheet.Cell(row, 1).Value = new string('a', 100);
            await using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;
            var user = new AuthenticatedUser("user-1", "tester");
            var uploaded = await service.SaveAsync(user, new FormFile(stream, 0, stream.Length, "file", "large.xlsx"), default);
            var preview = await service.ExtractPreviewAsync(user, null, uploaded.Id, default);
            Assert.True(preview.Truncated);
            Assert.Contains("truncated by server limit", preview.Content);
            Assert.InRange(preview.Content.Length, 1000, 1100);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static ChatFileAttachmentService CreateService(string root, int limit = 40000)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ChatAttachments:RootPath"] = root,
            ["ChatFileAttachments:MaxCharactersPerFile"] = limit.ToString()
        }).Build();
        return new ChatFileAttachmentService(configuration, new UnusedDocumentParser(), NullLogger<ChatFileAttachmentService>.Instance);
    }

    private sealed class UnusedDocumentParser : IDocumentParsingService
    {
        public Task<RagOperationResult> PreflightAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DocumentParseResult> ParsePdfAsync(DocumentParseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
