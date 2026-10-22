using AiAgent.Backend.Services.KunBuddy;
using AiAgent.Backend.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace AiAgent.Backend.Tests;

public sealed partial class KnowledgeWorkspaceTests
{
    private sealed class ReleaseFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "release-test-" + Guid.NewGuid().ToString("N"));
        public ClientReleaseService Service() => new(new SubscriptionEnvironment(Root), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = Root }).Build());
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private static IFormFile Installer(string name = "KunBuddy.exe", byte[]? bytes = null)
    {
        bytes ??= new byte[128]; if (name.EndsWith(".exe")) { bytes[0] = 0x4d; bytes[1] = 0x5a; bytes[0x3c] = 64; bytes[64] = 0x50; bytes[65] = 0x45; }
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);
    }
    private static AuthenticatedUser ReleaseAdmin => new("fixture-admin", "admin", "admin");
    [Fact]
    public async Task ClientReleaseUploadPublishesImmutableHashAndPersistsCatalog()
    {
        using var files = new ReleaseFiles(); var service = files.Service();
        var row = await service.Upload(ReleaseAdmin, Installer(), "0.21.0", "windows", "x64", "fixture notes", default);
        Assert.Equal(row.Id, service.Latest("windows", "x64")!.Id);
        var file = service.Open(row.Id); Assert.Equal(row.Size, new FileInfo(file.Path).Length);
        Assert.Equal(row.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file.Path))).ToLowerInvariant());
        Assert.Single(files.Service().List()); Assert.DoesNotContain(files.Root, row.DownloadPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Upload(ReleaseAdmin, Installer(), "0.21.0", "windows", "x64", "", default));
    }
    [Fact]
    public async Task ClientReleaseSemanticVersionsAndArchitecturesAreIndependentAndWithdrawalStopsDelivery()
    {
        using var files = new ReleaseFiles(); var service = files.Service();
        var small = await service.Upload(ReleaseAdmin, Installer(), "0.9.0", "windows", "x64", null, default);
        var large = await service.Upload(ReleaseAdmin, Installer(), "0.10.0", "windows", "x64", null, default);
        await service.Upload(ReleaseAdmin, Installer(), "1.0.0", "windows", "arm64", null, default);
        Assert.Equal(large.Id, service.Latest("windows", "x64")!.Id); Assert.Null(service.Latest("macos", "x64"));
        await service.Withdraw(ReleaseAdmin, large.Id, default); Assert.Equal(small.Id, service.Latest("windows", "x64")!.Id);
        Assert.Throws<KeyNotFoundException>(() => service.Open(large.Id)); Assert.Equal(large.Id, service.Open(large.Id, true).Release.Id);
        Assert.Throws<KeyNotFoundException>(() => service.Open("../../catalog.json"));
    }
    [Fact]
    public async Task ClientReleaseRejectsNonAdminsInvalidVersionsAndFakeInstallers()
    {
        using var files = new ReleaseFiles(); var service = files.Service();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.Upload(new("fixture-user", "user"), Installer(), "0.1.0", "windows", "x64", null, default));
        foreach (var version in new[] { "../1", "1.2", "01.2.3", "1.2.3-beta" }) Assert.Throws<ArgumentException>(() => ClientReleaseService.ParseVersion(version));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Upload(ReleaseAdmin, new FormFile(new MemoryStream(new byte[128]), 0, 128, "file", "fake.exe"), "0.1.0", "windows", "x64", null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Upload(ReleaseAdmin, Installer("fake.dmg", new byte[512]), "0.1.0", "macos", "arm64", null, default));
        Assert.Empty(service.List());
    }
    [Fact]
    public async Task ClientReleaseAcceptsMacContainersWithoutExecutingOrExtractingAndRejectsZipTraversal()
    {
        using var files = new ReleaseFiles(); var service = files.Service();
        var dmg = new byte[512]; "koly"u8.CopyTo(dmg);
        await service.Upload(ReleaseAdmin, Installer("KunBuddy.dmg", dmg), "0.21.0", "macos", "arm64", null, default);
        static byte[] Zip(bool bad) { using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) { using (var writer = new StreamWriter(zip.CreateEntry("KunBuddy.app/Contents/Info.plist").Open())) writer.Write("fixture"); if (bad) zip.CreateEntry("../bad"); } return stream.ToArray(); }
        await service.Upload(ReleaseAdmin, Installer("KunBuddy.zip", Zip(false)), "0.21.0", "macos", "x64", null, default);
        await Assert.ThrowsAsync<ArgumentException>(() => service.Upload(ReleaseAdmin, Installer("bad.zip", Zip(true)), "0.22.0", "macos", "x64", null, default));
        Assert.Equal(2, service.List().Count);
    }
}
