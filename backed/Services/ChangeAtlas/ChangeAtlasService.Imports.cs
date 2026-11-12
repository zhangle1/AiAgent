using System.Text.RegularExpressions;
using AiAgent.Backend.Dtos.ChangeAtlas;
using AiAgent.Backend.Services.Auth;

namespace AiAgent.Backend.Services.ChangeAtlas;

public sealed partial class ChangeAtlasService
{
    // Deliberately limited lexical adapter. No package execution, alias guessing or call inference.
    public static List<(string Specifier, int Line, string Text)> ImportStatements(string source)
    {
        var result = new List<(string, int, string)>();
        var block = false; var lineNumber = 0;
        foreach (var raw in source.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (block) { if (line.Contains("*/")) block = false; continue; }
            if (line.StartsWith("/*")) { block = !line.Contains("*/"); continue; }
            if (line.StartsWith("//")) continue;
            var match = Regex.Match(line, "^(?:import|export)\\s+(?:(?:[^;\\\"']+?)\\s+from\\s+)?[\\\"'](?<path>\\.[^\\\"']+)[\\\"']", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (match.Success) result.Add((match.Groups["path"].Value, lineNumber, line[..Math.Min(line.Length, 300)]));
        }
        return result;
    }
    public static string? ResolveImport(string sourcePath, string specifier, ISet<string> paths)
    {
        var parts = sourcePath.Split('/').SkipLast(1).ToList();
        foreach (var part in specifier.Split('/'))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
            else { if (part.Length == 0 || part.Contains('\\')) return null; parts.Add(part); }
        }
        var stem = string.Join('/', parts);
        var candidates = new[] { stem, stem + ".ts", stem + ".tsx", stem + ".js", stem + ".jsx", stem + "/index.ts", stem + "/index.tsx", stem + "/index.js", stem + "/index.jsx" }.Where(paths.Contains).Distinct().ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }
    public async Task<AtlasImportGraph> ImportsAsync(AuthenticatedUser user, long projectId, string snapshotId, long repositoryId, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        var snapshot = await GetAsync(user, projectId, snapshotId, ct);
        var repository = snapshot.Repositories.SingleOrDefault(x => x.Id == repositoryId) ?? throw new ArgumentException("请选择快照中的单个代码库。");
        var files = snapshot.Files.Where(x => x.RepositoryId == repositoryId && x.AtHead && !Sensitive(x.Path)).ToList();
        var byPath = files.ToDictionary(x => x.Path, StringComparer.Ordinal);
        var paths = byPath.Keys.ToHashSet(StringComparer.Ordinal);
        var supported = files.Where(x => new[] { ".ts", ".tsx", ".js", ".jsx" }.Contains(Path.GetExtension(x.Path)) && !Regex.IsMatch(x.Path, @"(?:^|/)(?:node_modules|dist|build|vendor|\.next)(?:/|$)") && !x.Path.EndsWith(".d.ts", StringComparison.Ordinal)).ToList();
        var edges = new List<AtlasImportEdge>(); var scanned = 0; var unresolved = 0; var chars = 0;
        foreach (var file in supported.Take(300))
        {
            ct.ThrowIfCancellationRequested();
            var source = await git.SourceAsync(store.Cache(user.Id, repositoryId), repository.HeadSha, file.Path, ct);
            if (source == null) continue;
            chars += source.Length;
            if (chars > 8_000_000 || edges.Count >= 3000) break;
            scanned++;
            foreach (var statement in ImportStatements(source))
            {
                if (edges.Count >= 3000) break;
                var target = ResolveImport(file.Path, statement.Specifier, paths);
                if (target == null) { unresolved++; continue; }
                edges.Add(new(ChangeAtlasGitReader.Hash(file.Id + ":" + statement.Line + ":" + target), file.Id, byPath[target].Id, statement.Line, statement.Text));
            }
        }
        await GetAsync(user, projectId, snapshotId, ct); // Revalidate before publication.
        return new(snapshot.Id, repository.HeadSha, files, edges, scanned, files.Count - scanned, unresolved,
            ["词法导入候选 v1：仅识别单行相对 import/export；不是 AST、编译器或运行验证。", "不覆盖 C#、路径别名、动态导入、require、多行语句及调用关系；注释和模板文本仍可能产生误判。", "最多扫描 300 个 JS/TS 文件、800 万字符、3000 条关系；仅覆盖快照已载入文件。"]);
    }
}
