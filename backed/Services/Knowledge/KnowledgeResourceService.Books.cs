using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Parsing;

namespace AiAgent.Backend.Services.Knowledge;

public sealed partial class KnowledgeResourceService
{
    internal List<CodexBookChapter> ResolveBookChapters(AuthenticatedUser user, ChatCompleteRequest request, CancellationToken ct)
    {
        using var db = database.CopyNew();
        if (KnowledgeUserScope.ResolveUser(db, httpContextAccessor)?.Id != user.Id)
            throw new UnauthorizedAccessException("知识目录访问身份已失效。");
        var tree = Tree();
        var selected = SelectBookFiles(tree, request.KnowledgeScopeUris ?? [], request.KnowledgeScopeUri);
        var store = new KnowledgeParsedDocumentStore(paths);
        var chapters = new List<CodexBookChapter>();
        foreach (var book in selected.GroupBy(node => node.DocumentId!.Value))
        {
            ct.ThrowIfCancellationRequested();
            var (kb, doc) = Resolve(book.First());
            var parsed = db.Queryable<AiKnowledgeParsedDocument>().Where(row => row.DocumentId == doc.Id && row.KnowledgeBaseId == kb.Id)
                .OrderByDescending(row => row.Id).First();
            if (parsed == null || parsed.SourceHash != doc.FileHash || book.Any(node => node.ParsedDocumentId != parsed.Id))
                throw new InvalidOperationException("书籍解析版本已变化，请刷新后重试。");
            var manifest = store.ReadManifest(kb, parsed) ?? throw new InvalidOperationException("书籍章节清单不可用。");
            var root = ParsedRootUri(DocumentUri(kb, doc), doc);
            var allowed = book.Select(node => node.Uri).ToHashSet(StringComparer.Ordinal);
            var label = $"{doc.Id}-{MarkdownDocumentPlanner.SafeName(Path.GetFileNameWithoutExtension(doc.OriginalFileName))}";
            bool Selected(string path) => allowed.Contains(root + string.Join('/', path.Split('/').Select(Uri.EscapeDataString)));
            foreach (var entry in manifest.Files.Where(entry => Selected(entry.Path)))
            {
                ct.ThrowIfCancellationRequested();
                chapters.Add(new(label, entry.Path, store.SectionPath(kb, parsed, entry), entry.Hash));
            }
            foreach (var index in (manifest.Navigation ?? []).Where(index => Selected(index.Path)))
                chapters.Add(new(label, index.Path, store.NavigationPath(kb, parsed, index), index.Hash));
        }
        if (chapters.Count != selected.Count) throw new InvalidOperationException("书籍目录已变化，请刷新后重试。");
        return chapters;
    }

    private static List<KnowledgeResourceNodeDto> SelectBookFiles(List<KnowledgeResourceNodeDto> tree, IEnumerable<string> scopes, string? legacy)
    {
        var uris = scopes.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (uris.Count == 0 && !string.IsNullOrWhiteSpace(legacy)) uris.Add(legacy);
        var selected = new Dictionary<string, KnowledgeResourceNodeDto>(StringComparer.Ordinal);
        foreach (var value in uris.Distinct(StringComparer.Ordinal))
        {
            var uri = NormalizeUri(value, value.EndsWith('/'));
            var scope = tree.FirstOrDefault(node => node.Uri == uri || node.Kind == "directory" && node.Uri == uri + "/");
            if (scope == null || Roots.Contains(scope.Uri)) throw new InvalidOperationException("所选知识目录不可访问。");
            var matches = tree.Where(node => node.DocumentId.HasValue && node.ParsedDocumentId.HasValue &&
                node.Kind == "file" && node.Extension == ".md" && IsParsedUri(node.Uri) &&
                (node.Uri == scope.Uri || node.SourceUri == scope.Uri ||
                 scope.Kind == "directory" && (node.Uri.StartsWith(scope.Uri, StringComparison.Ordinal) ||
                 node.SourceUri?.StartsWith(scope.Uri, StringComparison.Ordinal) == true))).ToList();
            if (matches.Count == 0) throw new InvalidOperationException($"所选范围「{scope.Name}」没有可读的解析章节，请先解析原文。");
            foreach (var node in matches) selected[node.Uri] = node;
        }
        return selected.Values.ToList();
    }
}
