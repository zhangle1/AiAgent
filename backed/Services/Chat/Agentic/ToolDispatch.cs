using AiAgent.Backend.Services.Chat.Planning;
using AiAgent.Backend.Services.Chat.Retrieval;
using AiAgent.Backend.Services.CodeRepository;
using AiAgent.Backend.Services.DashboardApp;
using AiAgent.Backend.Dtos.DashboardApp;
using AiAgent.Backend.Entities.CodeRepository;
using SqlSugar;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat.Agentic;

/// <summary>
/// 工具执行器，对齐 DeepTutor 的 tool_dispatch.py，负责把 ToolCall 分发给具体工具并聚合结果。
/// </summary>
public interface IToolDispatcher
{
    IReadOnlyList<ToolDefinition> GetDefinitions();

    /// <summary>
    /// 执行一批工具调用。
    /// </summary>
    Task<ToolDispatchOutcome> DispatchAsync(AgentContext context, IReadOnlyList<ToolCall> toolCalls, CancellationToken cancellationToken);
}

/// <summary>
/// 默认工具执行器。
/// </summary>
public sealed class ToolDispatcher : IToolDispatcher
{
    internal static readonly JsonSerializerOptions ToolJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly Dictionary<string, IAgentTool> _tools;

    /// <summary>
    /// 初始化工具执行器，并注册第一批知识库工具。
    /// </summary>
    public ToolDispatcher(IKnowledgeRetrievalService retrievalService, ICodeRepositoryIndexService codeRepositoryIndexService, IDashboardApplicationWorkspace dashboardWorkspace, ISqlSugarClient db)
    {
        var repositoryWorkspace = new RegisteredRepositoryFileWorkspace(db);
        var tools = new IAgentTool[]
        {
            new RagSearchTool(retrievalService),
            new ReadPageRangeTool(retrievalService),
            new CodeRepositoryOverviewTool(codeRepositoryIndexService),
            new CodeSearchTool(codeRepositoryIndexService),
            new FindSymbolTool(codeRepositoryIndexService),
            new DashboardWorkspaceInspectTool(dashboardWorkspace),
            new DashboardWorkspaceSearchTool(dashboardWorkspace),
            new DashboardFileReadTool(dashboardWorkspace, repositoryWorkspace),
            new DashboardPatchTool(dashboardWorkspace),
            new DashboardChangeValidationTool(dashboardWorkspace),
            new DashboardFileWriteTool(dashboardWorkspace, repositoryWorkspace)
        };
        _tools = tools.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ToolDefinition> GetDefinitions()
    {
        return _tools.Values.Select(x => x.GetDefinition()).ToList();
    }

    /// <summary>
    /// 顺序执行工具调用。第一版保持串行，后续可按 DeepTutor 改成并行和子 trace。
    /// </summary>
    public async Task<ToolDispatchOutcome> DispatchAsync(AgentContext context, IReadOnlyList<ToolCall> toolCalls, CancellationToken cancellationToken)
    {
        var outcome = new ToolDispatchOutcome();
        foreach (var call in toolCalls)
        {
            if (!_tools.TryGetValue(call.Name, out var tool))
            {
                outcome.Results.Add(ToolResult.Failed($"未知工具：{call.Name}"));
                continue;
            }

            var result = await tool.ExecuteAsync(context, call.Arguments, cancellationToken);
            outcome.Results.Add(result);
            outcome.Citations.AddRange(result.Citations);
        }

        return outcome;
    }
}

internal sealed class CodeRepositoryOverviewTool : IAgentTool
{
    private readonly ICodeRepositoryIndexService _service;

    public CodeRepositoryOverviewTool(ICodeRepositoryIndexService service) => _service = service;

    public string Name => AgentToolNames.CodeRepositoryOverview;

    public ToolDefinition GetDefinition() => new() { Name = Name, Description = "Read selected repository root structure, README, and manifests without requiring a code index." };

    public Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken) => _service.DescribeAsync(context, cancellationToken);
}

internal sealed class CodeSearchTool : IAgentTool
{
    private readonly ICodeRepositoryIndexService _service;

    public CodeSearchTool(ICodeRepositoryIndexService service) => _service = service;

    public string Name => AgentToolNames.CodeSearch;

    public ToolDefinition GetDefinition() => new() { Name = Name, Description = "Search selected indexed code repositories for source files and relevant snippets.", Parameters = { new ToolParameter { Name = "query", Type = "string", Description = "Code or error query" }, new ToolParameter { Name = "top_k", Type = "integer", Description = "Maximum source snippets", Required = false } } };

    public Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken) => _service.SearchAsync(context, arguments.TryGetValue("query", out var query) ? query?.ToString() ?? context.UserMessage : context.UserMessage, arguments.TryGetValue("top_k", out var topK) && int.TryParse(topK?.ToString(), out var value) ? value : context.TopK, cancellationToken);
}

internal sealed class FindSymbolTool : IAgentTool
{
    private readonly ICodeRepositoryIndexService _service;

    public FindSymbolTool(ICodeRepositoryIndexService service) => _service = service;

    public string Name => AgentToolNames.FindSymbol;

    public ToolDefinition GetDefinition() => new() { Name = Name, Description = "Find class, method, interface, function, or other symbols in selected indexed code repositories.", Parameters = { new ToolParameter { Name = "symbol", Type = "string", Description = "Symbol name" } } };

    public Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken) => _service.FindSymbolAsync(context, arguments.TryGetValue("symbol", out var symbol) ? symbol?.ToString() ?? context.UserMessage : context.UserMessage, cancellationToken);
}

internal sealed class DashboardWorkspaceInspectTool : IAgentTool
{
    private readonly IDashboardApplicationWorkspace _workspace;
    public DashboardWorkspaceInspectTool(IDashboardApplicationWorkspace workspace) => _workspace = workspace;
    public string Name => AgentToolNames.InspectDashboardWorkspace;
    public ToolDefinition GetDefinition() => new() { Name = Name, Description = "Inspect the active dashboard workspace. Returns the only writable root, framework, entry files, source files, styles, imports, visual targets, and revision. Always call this first for a dashboard change." };
    public async Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.DashboardApplicationId)) return ToolResult.Failed("Select a dashboard workspace before inspection.");
        var snapshot = await _workspace.InspectAsync(context.DashboardApplicationId, cancellationToken);
        context.Metadata["dashboard_workspace_inspected"] = true;
        return new ToolResult { Content = JsonSerializer.Serialize(snapshot, ToolDispatcher.ToolJsonOptions), Metadata = { ["dashboard_application_id"] = context.DashboardApplicationId, ["dashboard_workspace_inspected"] = true } };
    }
}

internal sealed class DashboardWorkspaceSearchTool : IAgentTool
{
    private readonly IDashboardApplicationWorkspace _workspace;
    public DashboardWorkspaceSearchTool(IDashboardApplicationWorkspace workspace) => _workspace = workspace;
    public string Name => AgentToolNames.SearchDashboardCode;
    public ToolDefinition GetDefinition() => new() { Name = Name, Description = "Search source only inside the active dashboard workspace. Use it to locate visual components, ECharts options, labels, data, and styles after workspace inspection.", Parameters = { new ToolParameter { Name = "query", Type = "string", Description = "Text, symbol, or configuration fragment to locate" } } };
    public async Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.DashboardApplicationId)) return ToolResult.Failed("Select a dashboard workspace before searching.");
        if (!context.Metadata.TryGetValue("dashboard_workspace_inspected", out var inspected) || inspected is not true) return ToolResult.Failed("Call inspect_dashboard_workspace before searching.");
        var query = DashboardFileWriteTool.StringValue(arguments, "query");
        if (string.IsNullOrWhiteSpace(query)) return ToolResult.Failed("search_dashboard_code requires query.");
        var result = await _workspace.SearchAsync(context.DashboardApplicationId, query, cancellationToken);
        return new ToolResult { Content = JsonSerializer.Serialize(result, ToolDispatcher.ToolJsonOptions), Metadata = { ["dashboard_application_id"] = context.DashboardApplicationId, ["query"] = query } };
    }
}

internal sealed class DashboardPatchTool : IAgentTool
{
    private readonly IDashboardApplicationWorkspace _workspace;
    public DashboardPatchTool(IDashboardApplicationWorkspace workspace) => _workspace = workspace;
    public string Name => AgentToolNames.ApplyDashboardPatch;
    public ToolDefinition GetDefinition() => new()
    {
        Name = Name,
        Description = "Update one previously read existing dashboard file. Prefer an exact minimal replacement; for one visual request requiring several coordinated edits, send the complete replacement content. The server always verifies SHA-256.",
        Parameters =
        {
            new ToolParameter { Name = "path", Type = "string", Description = "Previously read workspace-relative file path" },
            new ToolParameter { Name = "expected_sha256", Type = "string", Description = "SHA-256 returned by read_dashboard_file" },
            new ToolParameter { Name = "search", Type = "string", Description = "Exact existing source fragment; must occur once. Omit only when content is supplied.", Required = false },
            new ToolParameter { Name = "replace", Type = "string", Description = "Replacement source fragment", Required = false },
            new ToolParameter { Name = "content", Type = "string", Description = "Complete replacement of the same previously read file; use for coordinated multi-location edits only.", Required = false }
        }
    };
    public async Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.DashboardApplicationId)) return ToolResult.Failed("Select a dashboard workspace before patching.");
        var path = DashboardFileWriteTool.StringValue(arguments, "path");
        if (string.IsNullOrWhiteSpace(path)) return ToolResult.Failed("apply_dashboard_patch requires path.");
        if (!DashboardFileReadTool.ReadPaths(context).Contains(path.Replace('\\', '/'))) return ToolResult.Failed("Read the exact existing file with read_dashboard_file before applying a patch.");
        var result = await _workspace.ApplyPatchAsync(context.DashboardApplicationId, new DashboardFilePatchRequest
        {
            Path = path,
            ExpectedSha256 = DashboardFileWriteTool.StringValue(arguments, "expected_sha256") ?? string.Empty,
            Search = DashboardFileWriteTool.StringValue(arguments, "search") ?? string.Empty,
            Replace = DashboardFileWriteTool.StringValue(arguments, "replace") ?? string.Empty,
            Content = DashboardFileWriteTool.StringValue(arguments, "content")
        }, cancellationToken);
        return new ToolResult { Content = $"dashboard_change_applied:{path}\n{JsonSerializer.Serialize(result, ToolDispatcher.ToolJsonOptions)}", Metadata = { ["dashboard_application_id"] = context.DashboardApplicationId, ["path"] = path, ["dashboard_change_applied"] = true } };
    }
}

internal sealed class DashboardChangeValidationTool : IAgentTool
{
    private readonly IDashboardApplicationWorkspace _workspace;
    public DashboardChangeValidationTool(IDashboardApplicationWorkspace workspace) => _workspace = workspace;
    public string Name => AgentToolNames.ValidateDashboardChange;
    public ToolDefinition GetDefinition() => new() { Name = Name, Description = "Statically validate an applied dashboard change: target file is a known source/style, local imports resolve, and an optional expected fragment exists.", Parameters = { new ToolParameter { Name = "path", Type = "string", Description = "Changed workspace-relative file path" }, new ToolParameter { Name = "expected_contains", Type = "string", Description = "Required changed fragment, for example a bar series", Required = false } } };
    public async Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.DashboardApplicationId)) return ToolResult.Failed("Select a dashboard workspace before validation.");
        var path = DashboardFileWriteTool.StringValue(arguments, "path");
        if (string.IsNullOrWhiteSpace(path)) return ToolResult.Failed("validate_dashboard_change requires path.");
        var result = await _workspace.ValidateChangeAsync(context.DashboardApplicationId, new DashboardChangeValidationRequest { Path = path, ExpectedContains = DashboardFileWriteTool.StringValue(arguments, "expected_contains") }, cancellationToken);
        return new ToolResult { Content = $"dashboard_change_validated:{path}\n{JsonSerializer.Serialize(result, ToolDispatcher.ToolJsonOptions)}", Metadata = { ["dashboard_application_id"] = context.DashboardApplicationId, ["path"] = path } };
    }
}

internal sealed class DashboardFileReadTool : IAgentTool
{
    private readonly IDashboardApplicationWorkspace _workspace;
    private readonly RegisteredRepositoryFileWorkspace _repositoryWorkspace;

    public DashboardFileReadTool(IDashboardApplicationWorkspace workspace, RegisteredRepositoryFileWorkspace repositoryWorkspace) { _workspace = workspace; _repositoryWorkspace = repositoryWorkspace; }

    public string Name => AgentToolNames.ReadDashboardFile;

    public ToolDefinition GetDefinition() => new()
    {
        Name = Name,
        Description = "Read one text file from the active dashboard workspace or an explicitly selected registered code repository. Use it before modifying an existing file.",
        Parameters = { new ToolParameter { Name = "path", Type = "string", Description = "Workspace-relative source path" }, new ToolParameter { Name = "repository_name", Type = "string", Description = "Selected registered repository name; omit for the active dashboard workspace", Required = false } }
    };

    public async Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var path = DashboardFileWriteTool.StringValue(arguments, "path");
        if (string.IsNullOrWhiteSpace(path)) return ToolResult.Failed("read_dashboard_file requires path.");
        var repositoryName = DashboardFileWriteTool.StringValue(arguments, "repository_name");
        if (!string.IsNullOrWhiteSpace(context.DashboardApplicationId) && !string.IsNullOrWhiteSpace(repositoryName)) return ToolResult.Failed("Dashboard chat is restricted to the current workspace; repository_name is not allowed.");
        if (!string.IsNullOrWhiteSpace(context.DashboardApplicationId) && (!context.Metadata.TryGetValue("dashboard_workspace_inspected", out var inspected) || inspected is not true)) return ToolResult.Failed("Call inspect_dashboard_workspace before reading dashboard files.");
        object file;
        if (!string.IsNullOrWhiteSpace(repositoryName)) file = await _repositoryWorkspace.ReadAsync(context, repositoryName, path, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(context.DashboardApplicationId)) file = await _workspace.ReadFileAsync(context.DashboardApplicationId, path, cancellationToken);
        else return ToolResult.Failed("Select a dashboard workspace or registered code repository before reading files.");
        if (!string.IsNullOrWhiteSpace(context.DashboardApplicationId)) ReadPaths(context).Add(path.Replace('\\', '/'));
        return new ToolResult
        {
            Content = JsonSerializer.Serialize(file, ToolDispatcher.ToolJsonOptions),
            Metadata = { ["dashboard_application_id"] = context.DashboardApplicationId, ["repository_name"] = repositoryName, ["path"] = path }
        };
    }

    internal static HashSet<string> ReadPaths(AgentContext context)
    {
        const string key = "dashboard_read_paths";
        if (context.Metadata.TryGetValue(key, out var value) && value is HashSet<string> paths) return paths;
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        context.Metadata[key] = created;
        return created;
    }
}

internal sealed class DashboardFileWriteTool : IAgentTool
{
    private readonly IDashboardApplicationWorkspace _workspace;
    private readonly RegisteredRepositoryFileWorkspace _repositoryWorkspace;

    public DashboardFileWriteTool(IDashboardApplicationWorkspace workspace, RegisteredRepositoryFileWorkspace repositoryWorkspace) { _workspace = workspace; _repositoryWorkspace = repositoryWorkspace; }

    public string Name => AgentToolNames.WriteDashboardFile;

    public ToolDefinition GetDefinition() => new()
    {
        Name = Name,
        Description = "Write one complete text file to the active dashboard workspace or an explicitly selected registered code repository. The write is path-restricted and atomic.",
        Parameters =
        {
            new ToolParameter { Name = "path", Type = "string", Description = "Workspace-relative target path" },
            new ToolParameter { Name = "content", Type = "string", Description = "Complete replacement file content" },
            new ToolParameter { Name = "repository_name", Type = "string", Description = "Selected registered repository name; omit for the active dashboard workspace", Required = false }
        }
    };

    public async Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var path = StringValue(arguments, "path");
        var content = StringValue(arguments, "content");
        if (string.IsNullOrWhiteSpace(path) || content == null) return ToolResult.Failed("write_dashboard_file requires path and content.");
        var repositoryName = StringValue(arguments, "repository_name");
        if (!string.IsNullOrWhiteSpace(context.DashboardApplicationId) && !string.IsNullOrWhiteSpace(repositoryName)) return ToolResult.Failed("Dashboard chat is restricted to the current workspace; repository_name is not allowed.");
        if (!string.IsNullOrWhiteSpace(context.DashboardApplicationId)) return ToolResult.Failed("Dashboard changes must use inspect_dashboard_workspace, read_dashboard_file, apply_dashboard_patch, and validate_dashboard_change. Direct complete-file writes are disabled.");
        if (!string.IsNullOrWhiteSpace(repositoryName)) await _repositoryWorkspace.WriteAsync(context, repositoryName, path, content, cancellationToken);
        else return ToolResult.Failed("Select a dashboard workspace or registered code repository before writing files.");
        return new ToolResult { Content = $"dashboard_file_written:{path}", Metadata = { ["dashboard_application_id"] = context.DashboardApplicationId, ["repository_name"] = repositoryName, ["path"] = path } };
    }

    internal static string? StringValue(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value == null) return null;
        return value is JsonElement element && element.ValueKind == JsonValueKind.String ? element.GetString() : value.ToString();
    }
}

/// <summary>
/// 语义检索工具，封装当前 IRagService。
/// </summary>
internal sealed class RegisteredRepositoryFileWorkspace
{
    private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".html", ".css", ".js", ".jsx", ".ts", ".tsx", ".json", ".md", ".yml", ".yaml", ".cs", ".csproj" };
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", "bin", "obj", "dist", "build", ".next" };
    private readonly ISqlSugarClient _db;

    public RegisteredRepositoryFileWorkspace(ISqlSugarClient db) => _db = db;

    public async Task<object> ReadAsync(AgentContext context, string repositoryName, string relativePath, CancellationToken cancellationToken)
    {
        var repository = ResolveRepository(context, repositoryName);
        var filePath = ResolveFilePath(repository.RootPath, relativePath, false);
        var info = new FileInfo(filePath);
        if (info.Length > 1024 * 1024 || !EditableExtensions.Contains(info.Extension)) throw new InvalidOperationException("Only supported text files up to 1 MB can be opened.");
        var content = await File.ReadAllTextAsync(filePath, DetectEncoding(filePath), cancellationToken);
        return new { repository_name = repository.Name, path = Path.GetRelativePath(repository.RootPath, filePath), extension = info.Extension, content, line_count = content.Count(x => x == '\n') + 1, updated_at = info.LastWriteTimeUtc };
    }

    public async Task WriteAsync(AgentContext context, string repositoryName, string relativePath, string content, CancellationToken cancellationToken)
    {
        var repository = ResolveRepository(context, repositoryName);
        var filePath = ResolveFilePath(repository.RootPath, relativePath, true);
        if (!EditableExtensions.Contains(Path.GetExtension(filePath))) throw new InvalidOperationException("This file type cannot be edited in the selected repository.");
        if (Encoding.UTF8.GetByteCount(content) > 1024 * 1024) throw new InvalidOperationException("Edited file content must not exceed 1 MB.");
        var encoding = File.Exists(filePath) ? DetectEncoding(filePath) : new UTF8Encoding(false);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temporary = filePath + ".aiagent.tmp";
        await File.WriteAllTextAsync(temporary, content, encoding, cancellationToken);
        File.Move(temporary, filePath, true);
    }

    private AiCodeRepository ResolveRepository(AgentContext context, string repositoryName)
    {
        if (!context.CodeRepositoryNames.Contains(repositoryName, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("The repository was not selected for this chat request.");
        return _db.Queryable<AiCodeRepository>().First(x => x.Name == repositoryName && !x.IsDeleted)
            ?? throw new FileNotFoundException("The selected code repository was not found.");
    }

    private static string ResolveFilePath(string repositoryRoot, string relativePath, bool allowNewFile)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A repository-relative file path is required.");
        var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Path is outside the selected code repository.");
        if (relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\').Any(IgnoredDirectories.Contains)) throw new InvalidOperationException("Files in generated and Git metadata directories cannot be changed.");
        if (!allowNewFile && !File.Exists(fullPath)) throw new FileNotFoundException("Repository file does not exist.");
        return fullPath;
    }

    private static Encoding DetectEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path).Take(3).ToArray();
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;
        return bytes.Length == 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? new UTF8Encoding(true) : new UTF8Encoding(false, true);
    }
}

internal sealed class RagSearchTool : IAgentTool
{
    private readonly IKnowledgeRetrievalService _retrievalService;

    public RagSearchTool(IKnowledgeRetrievalService retrievalService)
    {
        _retrievalService = retrievalService;
    }

    public string Name => AgentToolNames.RagSearch;

    public ToolDefinition GetDefinition()
    {
        return new ToolDefinition
        {
            Name = Name,
            Description = "从当前知识库中执行向量/混合检索，适合普通语义问答。",
            Parameters =
            {
                new ToolParameter { Name = "query", Type = "string", Description = "检索问题" },
                new ToolParameter { Name = "top_k", Type = "integer", Description = "返回片段数量", Required = false }
            }
        };
    }

    public Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var query = ValueAsString(arguments, "query") ?? context.UserMessage;
        var topK = ValueAsInt(arguments, "top_k") ?? context.TopK;
        return _retrievalService.SearchAsync(context, query, topK, cancellationToken);
    }

    private static string? ValueAsString(Dictionary<string, object?> arguments, string key)
    {
        return arguments.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static int? ValueAsInt(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (value is long longValue)
        {
            return (int)longValue;
        }

        if (value is double doubleValue)
        {
            return (int)doubleValue;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}

/// <summary>
/// 页码范围读取工具，解决“前50页”“第10到30页”等结构化问题。
/// </summary>
internal sealed class ReadPageRangeTool : IAgentTool
{
    private readonly IKnowledgeRetrievalService _retrievalService;

    public ReadPageRangeTool(IKnowledgeRetrievalService retrievalService)
    {
        _retrievalService = retrievalService;
    }

    public string Name => AgentToolNames.ReadPageRange;

    public ToolDefinition GetDefinition()
    {
        return new ToolDefinition
        {
            Name = Name,
            Description = "按 PDF 或文档页码范围读取结构化 chunk，适合总结前 N 页或指定页范围。",
            Parameters =
            {
                new ToolParameter { Name = "page_start", Type = "integer", Description = "起始页码" },
                new ToolParameter { Name = "page_end", Type = "integer", Description = "结束页码" }
            }
        };
    }

    public Task<ToolResult> ExecuteAsync(AgentContext context, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var pageStart = ValueAsInt(arguments, "page_start") ?? 1;
        var pageEnd = ValueAsInt(arguments, "page_end") ?? pageStart;
        return _retrievalService.ReadPageRangeAsync(context, pageStart, pageEnd, cancellationToken);
    }

    private static int? ValueAsInt(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (value is long longValue)
        {
            return (int)longValue;
        }

        if (value is double doubleValue)
        {
            return (int)doubleValue;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}
