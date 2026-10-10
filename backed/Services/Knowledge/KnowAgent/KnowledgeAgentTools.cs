using System.Text.Json;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Services.Chat.Agentic;

namespace AiAgent.Backend.Services.Knowledge.KnowAgent;

public interface IKnowledgeAgentTools
{
    Task<string> ResolveScopeAsync(string uri, CancellationToken ct);
    Task<string> ExecuteAsync(string scope, KnowledgeAgentCommandRequest request, CancellationToken ct);
}

/// <summary>Only resource URIs visible in the selected subtree are executable; no shell or local paths.</summary>
public sealed class KnowledgeAgentTools(KnowledgeResourceService resources) : IKnowledgeAgentTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly IReadOnlyList<ToolDefinition> Definitions = new[] { "ls", "read", "search", "status" }.Select(name => new ToolDefinition {
        Name = name,
        Description = name switch { "ls" => "List actual direct children of a resource directory with pagination.", "read" => "Read a page of parsed file text or a generated overview; cite its URI and offset.", "search" => "Search names and readable text in the selected subtree; limited scan is reported.", _ => "Read the latest parsing and semantic task for a source file." },
        Parameters = [new() { Name = "target_uri", Description = "Visible Viking URI within the current scope; omit to use the current scope.", Required = false },
            new() { Name = "query", Description = "Search text, required for search.", Required = false },
            new() { Name = "offset", Type = "integer", Description = "Starting node or character offset.", Required = false },
            new() { Name = "limit", Type = "integer", Description = "Page size: ls up to 200 nodes, read up to 6000 characters.", Required = false }]
    }).ToArray();

    public async Task<string> ResolveScopeAsync(string uri, CancellationToken ct) => (await resources.ReadAsync(uri, ct)).Node.Uri;

    public async Task<string> ExecuteAsync(string scope, KnowledgeAgentCommandRequest request, CancellationToken ct)
    {
        var tree = resources.Tree();
        var root = tree.FirstOrDefault(x => x.Uri == scope) ?? throw new KeyNotFoundException("当前目录已删除，请重新选择。");
        var target = string.IsNullOrWhiteSpace(request.TargetUri) || request.TargetUri == request.Uri ? root.Uri : request.TargetUri;
        var node = tree.FirstOrDefault(x => x.Uri == target) ?? throw new KeyNotFoundException("资源不存在或无权访问。");
        if (node.Uri != root.Uri && (root.Kind != "directory" || !node.Uri.StartsWith(root.Uri, StringComparison.Ordinal)))
            throw new UnauthorizedAccessException("工具只能访问本轮选择的目录范围。");
        var offset = Math.Clamp(request.Offset, 0, 2_000_000);
        switch (request.Command)
        {
            case "ls":
                if (node.Kind != "directory") throw new ArgumentException("ls 需要目录 URI。");
                var children = tree.Where(x => x.ParentUri == node.Uri).ToList();
                var page = children.Skip(offset).Take(Math.Clamp(request.Limit, 1, 200)).ToList();
                return JsonSerializer.Serialize(new { uri = node.Uri, nodes = page, total = children.Count,
                    next_offset = offset + page.Count < children.Count ? (int?)(offset + page.Count) : null }, Json);
            case "read":
                var data = await resources.ReadAsync(node.Uri, ct);
                var text = data.ParsedContent ?? data.SourceText ?? data.SemanticContent ?? data.OverviewContent ?? data.AbstractContent ?? "";
                offset = Math.Min(offset, text.Length);
                // Avoid splitting a Unicode surrogate pair at either page boundary.
                if (offset > 0 && offset < text.Length && char.IsLowSurrogate(text[offset])) offset--;
                var end = Math.Min(text.Length, offset + Math.Clamp(request.Limit, 1, 6000));
                if (end < text.Length && end > offset && char.IsHighSurrogate(text[end - 1])) end--;
                if (end == offset && offset < text.Length) end = Math.Min(text.Length, offset + 2);
                return JsonSerializer.Serialize(new { uri = node.Uri, content = text[offset..end], offset, total_characters = text.Length,
                    next_offset = end < text.Length ? (int?)end : null, parsed = !string.IsNullOrEmpty(data.ParsedContent) }, Json);
            case "status":
                if (node.Kind != "file" || System.Text.RegularExpressions.Regex.IsMatch(node.Uri, @"^viking://user/[^/]+/(summaries|wiki)/"))
                    throw new ArgumentException("status 需要原始资源文件 URI。");
                var task = resources.Processing().FirstOrDefault(x => x.Node.Uri == node.Uri);
                return JsonSerializer.Serialize(new { uri = node.Uri, processing = task }, Json);
            case "search":
                var query = request.Query?.Trim() ?? "";
                if (query.Length is 0 or > 200) throw new ArgumentException("搜索词须为 1–200 个字符。");
                var candidates = tree.Where(x => x.Kind == "file" && (node.Kind == "file" ? x.Uri == node.Uri : x.Uri.StartsWith(node.Uri, StringComparison.Ordinal))).ToList();
                var hits = new List<object>();
                foreach (var file in candidates.Skip(offset).Take(40))
                {
                    ct.ThrowIfCancellationRequested();
                    var read = await resources.ReadAsync(file.Uri, ct);
                    var body = read.ParsedContent ?? read.SourceText ?? read.SemanticContent ?? "";
                    var index = body.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (index < 0 && !file.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                    var from = Math.Max(0, index - 100);
                    hits.Add(new { uri = file.Uri, name = file.Name, offset = from, excerpt = body.Substring(from, Math.Min(360, body.Length - from)) });
                }
                return JsonSerializer.Serialize(new { query, hits, total_files = candidates.Count,
                    next_offset = offset + 40 < candidates.Count ? (int?)(offset + 40) : null,
                    scan_limited = offset > 0 || candidates.Count > 40 }, Json);
            default: throw new ArgumentException("可用命令为 ls、read、search、status；编译请使用配置弹窗。");
        }
    }
}
