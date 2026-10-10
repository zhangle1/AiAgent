using System.IO;
using System.Text.Json;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using Microsoft.Extensions.Configuration;

namespace AiAgent.Backend.Tests.Chat;

public sealed class ChatUploadLibraryServiceTests
{
    [Theory]
    [InlineData("user")]
    [InlineData("admin")]
    public async Task PersonalLibraryUsesExplicitOwnerForListAndDownload(string role)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiagent-library-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ChatAttachments:RootPath"] = root }).Build();
            var service = new ChatUploadLibraryService(configuration);
            foreach (var owner in new[] { "alice", "bob" })
            {
                var directory = Path.Combine(root, "files", owner, "session");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "report.txt"), owner);
                File.WriteAllText(Path.Combine(directory, $"{owner}.json"), JsonSerializer.Serialize(new { UserId = owner, SessionId = "session", StoredFileName = "report.txt", FileName = "report.txt" }));
            }
            var user = new AuthenticatedUser("alice", "alice", role);
            var items = await service.ListAsync(user, user.Id, null, null, null, 200, default);
            Assert.Equal("alice", Assert.Single(items).UploaderId);
            Assert.Null(await service.OpenAsync(user, user.Id, "bob", default));
            var own = await service.OpenAsync(user, user.Id, "alice", default);
            Assert.NotNull(own);
            Assert.Equal("alice", File.ReadAllText(own.Path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
