using System.Reflection;
using System.IO;
using AiAgent.Backend.Services.CodeRepository;

public sealed class ProjectPackageDocumentTests
{
    private static object Invoke(string method, string path) => typeof(CodeRepositoryManager)
        .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [path])!;

    [Theory]
    [InlineData("artifacts/aiagent-packages/v1/app.zip")]
    [InlineData("artifacts/aiagent-packages/v2/交付包.ZIP")]
    public void PackageHasDownloadOnlyMetadata(string path)
    {
        Assert.Equal(path, Invoke("NormalizeProjectDocumentPath", path));
        Assert.Equal("archive", Invoke("GetProjectDocumentPreviewKind", path));
        Assert.Equal("application/zip", Invoke("GetProjectDocumentContentType", path));
    }

    [Theory]
    [InlineData("other/app.zip")]
    [InlineData("artifacts/aiagent-packages/../secret.zip")]
    [InlineData("artifacts/aiagent-packages/.git/app.zip")]
    [InlineData("artifacts/aiagent-packages/app.exe")]
    public void InvalidPackagePathsAreRejected(string path)
        => Assert.Throws<TargetInvocationException>(() => Invoke("NormalizeProjectDocumentPath", path));

    [Fact]
    public void DocumentTreeIncludesPackageWithoutExposingCodeOrArbitraryArchives()
    {
        var root = Path.Combine(Path.GetTempPath(), "aiagent-package-test-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "artifacts", "aiagent-packages", "v1");
        Directory.CreateDirectory(output);
        try
        {
            var package = Path.Combine(output, "app.zip");
            using (System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Create)) { }
            File.WriteAllText(Path.Combine(root, "README.md"), "test");
            File.WriteAllText(Path.Combine(root, "source.cs"), "test");
            File.WriteAllText(Path.Combine(root, "other.zip"), "test");
            var files = ((IEnumerable<string>)Invoke("EnumerateProjectDocumentFiles", root)).ToArray();
            Assert.Contains(package, files);
            Assert.Equal(2, files.Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
