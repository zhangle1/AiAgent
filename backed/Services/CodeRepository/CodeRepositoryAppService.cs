using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Services.Git;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Admin;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace AiAgent.Backend.Services.CodeRepository;

/// <summary>
/// HTTP endpoints for local code repository registration and inspection.
/// </summary>
[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/code-repositories")]
public sealed class CodeRepositoryAppService : IDynamicApiController
{
    private readonly ICodeRepositoryManager _manager;
    private readonly ICodeRepositoryIndexService _indexService;
    private readonly ICodeRepositoryIndexProgressStore _progressStore;
    private readonly ICodeRepositoryGitService _git;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;
    private readonly IProjectAccessService _projectAccess;
    private readonly IProjectAutoGitUpdateService _autoGitUpdates;

    /// <summary>
    /// Creates the code repository API service.
    /// </summary>
    public CodeRepositoryAppService(ICodeRepositoryManager manager, ICodeRepositoryIndexService indexService, ICodeRepositoryIndexProgressStore progressStore, ICodeRepositoryGitService git, IHttpContextAccessor httpContextAccessor, IAuthService authService, IProjectAccessService projectAccess, IProjectAutoGitUpdateService autoGitUpdates)
    {
        _manager = manager;
        _indexService = indexService;
        _progressStore = progressStore;
        _git = git;
        _httpContextAccessor = httpContextAccessor;
        _authService = authService;
        _projectAccess = projectAccess;
        _autoGitUpdates = autoGitUpdates;
    }

    [HttpGet("list")]
    public List<CodeRepositoryDto> List()
    {
        return _manager.List();
    }

    [HttpGet("projects")]
    public async Task<List<CodeProjectDto>> ListProjects(CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        var allowedIds = _projectAccess.GetAccessibleProjectIds(user);
        var projects = _manager.ListProjects().Where(project => allowedIds.Contains(project.Id)).ToList();
        if (!user.IsAdministrator)
        {
            foreach (var project in projects)
            {
                project.AutoGitUpdateEnabled = false;
                project.AutoGitUpdateIntervalHours = 0;
                project.AutoGitUpdateLastAttemptedAt = null;
                project.AutoGitUpdateLastSucceededAt = null;
                project.AutoGitUpdateLastResult = null;
            }
        }
        return projects;
    }

    [HttpGet("projects/references")]
    public async Task<List<CodeProjectReferenceDto>> ListProjectReferences([FromQuery] string? query, [FromQuery] long? excludeProjectId, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        var allowedIds = _projectAccess.GetAccessibleProjectIds(user);
        var term = query?.Trim();
        return _manager.ListProjects()
            .Where(project => allowedIds.Contains(project.Id) && project.Id != excludeProjectId)
            .Where(project => string.IsNullOrWhiteSpace(term)
                || project.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || project.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(project.Description) && project.Description.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(project => project.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .Select(project => new CodeProjectReferenceDto { Id = project.Id, DisplayName = project.DisplayName, Description = project.Description })
            .ToList();
    }

    [HttpGet("projects/{projectId:long}/markdown-documents")]
    public async Task<IActionResult> ListMarkdownDocuments([FromRoute] long projectId, [FromQuery] string? query, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        return new OkObjectResult(_manager.ListProjectMarkdownDocuments(projectId, query));
    }

    [HttpGet("projects/{projectId:long}/markdown-documents/content")]
    public async Task<IActionResult> ReadMarkdownDocument([FromRoute] long projectId, [FromQuery(Name = "repository_name")] string repositoryName, [FromQuery] string path, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        try
        {
            return new OkObjectResult(_manager.ReadProjectMarkdownDocument(projectId, repositoryName, path));
        }
        catch (DecoderFallbackException)
        {
            return new BadRequestObjectResult(new { message = "The Markdown document must use UTF-8 or a Unicode byte-order mark." });
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (FileNotFoundException)
        {
            return new NotFoundObjectResult(new { message = "The referenced project document is unavailable." });
        }
        catch (InvalidOperationException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
    }

    [HttpGet("projects/{projectId:long}/markdown-documents/download")]
    public async Task<IActionResult> DownloadMarkdownDocument([FromRoute] long projectId, [FromQuery(Name = "repository_name")] string repositoryName, [FromQuery] string path, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        try
        {
            var document = _manager.DownloadProjectMarkdownDocument(projectId, repositoryName, path);
            return new FileContentResult(document.Content, "text/markdown; charset=utf-8") { FileDownloadName = document.FileName };
        }
        catch (DecoderFallbackException) { return new BadRequestObjectResult(new { message = "Markdown 文件必须使用 UTF-8 或 Unicode BOM 编码。" }); }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (FileNotFoundException) { return new NotFoundObjectResult(new { message = "The referenced project document is unavailable." }); }
        catch (InvalidOperationException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
    }

    [HttpDelete("projects/{projectId:long}/markdown-documents")]
    public async Task<IActionResult> DeleteMarkdownDocument([FromRoute] long projectId, [FromQuery(Name = "repository_name")] string repositoryName, [FromQuery] string path, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        try
        {
            _manager.DeleteProjectMarkdownDocument(projectId, repositoryName, path);
            return new OkObjectResult(new { ok = true });
        }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (DirectoryNotFoundException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (FileNotFoundException) { return new NotFoundObjectResult(new { message = "The referenced project document is unavailable." }); }
        catch (UnauthorizedAccessException) { return new BadRequestObjectResult(new { message = "AiAgent cannot delete the selected server file. Grant the service account Modify permission on its directory." }); }
        catch (IOException) { return new BadRequestObjectResult(new { message = "The selected Markdown file could not be deleted because it is in use or the server filesystem rejected the operation." }); }
        catch (InvalidOperationException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
    }

    [HttpGet("projects/{projectId:long}/markdown-directories")]
    public async Task<IActionResult> ListMarkdownDirectories([FromRoute] long projectId, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        return new OkObjectResult(_manager.ListProjectMarkdownDirectories(projectId));
    }

    [HttpPost("projects/{projectId:long}/markdown-directories")]
    public async Task<IActionResult> CreateMarkdownDirectory([FromRoute] long projectId, [FromBody] CodeProjectMarkdownDirectoryCreateRequest? request, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        try { return new OkObjectResult(_manager.CreateProjectMarkdownDirectory(projectId, request?.RepositoryName ?? string.Empty, request?.ParentPath, request?.Name ?? string.Empty)); }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (DirectoryNotFoundException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (FileNotFoundException ex) { return new NotFoundObjectResult(new { message = ex.Message }); }
        catch (UnauthorizedAccessException) { return new BadRequestObjectResult(new { message = "AiAgent cannot create the selected server directory. Grant the service account Modify permission on that directory." }); }
        catch (InvalidOperationException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
    }

    [HttpPost("projects/{projectId:long}/markdown-documents/upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadProjectMarkdownDocument([FromRoute] long projectId, [FromForm(Name = "repository_name")] string? repositoryName, [FromForm(Name = "directory_path")] string? directoryPath, IFormFile file, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        try
        {
            return new OkObjectResult(await _manager.UploadProjectMarkdownDocumentAsync(projectId, user, repositoryName, directoryPath, file, cancellationToken));
        }
        catch (DecoderFallbackException)
        {
            return new BadRequestObjectResult(new { message = "Markdown 文件必须使用 UTF-8 或 Unicode BOM 编码。" });
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (DirectoryNotFoundException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return new NotFoundObjectResult(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return new BadRequestObjectResult(new { message = "AiAgent cannot write to the selected server directory. Grant the service account Modify permission on that directory." });
        }
        catch (InvalidOperationException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
    }

    [HttpGet("projects/{projectId:long}/agent-markdown-index")]
    public async Task<IActionResult> GetProjectAgentMarkdownIndex([FromRoute] long projectId, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        return new OkObjectResult(_manager.GetProjectAgentMarkdownIndex(projectId));
    }

    [HttpPost("projects/{projectId:long}/agent-markdown-index")]
    public async Task<IActionResult> GenerateProjectAgentMarkdownIndex([FromRoute] long projectId, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        try
        {
            return new OkObjectResult(await _manager.GenerateProjectAgentMarkdownIndexAsync(projectId, user, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
    }

    [HttpGet("projects/{projectId:long}/git/status")]
    public async Task<IActionResult> ProjectGitStatus([FromRoute] long projectId, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        return new OkObjectResult(await _git.ProjectStatusAsync(projectId, cancellationToken));
    }

    [HttpPost("projects/{projectId:long}/git/discard-and-pull")]
    public async Task<IActionResult> ProjectGitDiscardAndPull([FromRoute] long projectId, [FromBody] ProjectGitBatchRequest? request, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        return new OkObjectResult(await _git.ProjectDiscardChangesAndPullAsync(projectId, request?.RepositoryNames, cancellationToken));
    }

    [HttpPost("projects/{projectId:long}/git/commit-and-push")]
    public async Task<IActionResult> ProjectGitCommitAndPush([FromRoute] long projectId, [FromBody] ProjectGitCommitPushRequest? request, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.CanAccess(user, projectId)) return new ForbidResult();
        if (!user.CanCommitCode) return new ForbidResult();
        return new OkObjectResult(await _git.ProjectCommitAndPushAsync(projectId, request?.RepositoryNames, request?.Message, cancellationToken));
    }

    [HttpGet("projects/{projectId:long}/git/auto-update")]
    public async Task<IActionResult> GetProjectAutoGitUpdate([FromRoute] long projectId, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!user.IsAdministrator) return new ForbidResult();
        return new OkObjectResult(_autoGitUpdates.Get(projectId));
    }

    [HttpPut("projects/{projectId:long}/git/auto-update")]
    public async Task<IActionResult> SaveProjectAutoGitUpdate([FromRoute] long projectId, [FromBody] CodeProjectAutoGitUpdateRequest request, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!user.IsAdministrator) return new ForbidResult();
        try { return new OkObjectResult(_autoGitUpdates.Save(projectId, request)); }
        catch (ArgumentException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return new NotFoundObjectResult(new { message = ex.Message }); }
    }

    [HttpPost("projects")]
    public CodeProjectDto CreateProject([FromBody] CodeProjectSaveRequest request) => _manager.CreateProject(request);

    [HttpPut("projects/{projectId:long}")]
    public CodeProjectDto UpdateProject([FromRoute] long projectId, [FromBody] CodeProjectSaveRequest request) => _manager.UpdateProject(projectId, request);

    [HttpDelete("projects/{projectId:long}")]
    public object DeleteProject([FromRoute] long projectId)
    {
        _manager.DeleteProject(projectId);
        return new { ok = true };
    }

    [HttpGet("browse")]
    public CodeRepositoryDirectoryBrowserDto Browse([FromQuery] string? path)
    {
        return _manager.Browse(path);
    }

    [HttpPost("browse/directories")]
    public IActionResult CreateBrowseDirectory([FromBody] CodeRepositoryDirectoryCreateRequest request)
    {
        try
        {
            return new OkObjectResult(_manager.CreateDirectory(request.ParentPath, request.Name));
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (DirectoryNotFoundException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (IOException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return new ObjectResult(new { message = "The server account cannot create a folder in the selected directory." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }

    [HttpGet("browse/files")]
    public CodeRepositoryDirectoryBrowserDto BrowseFiles([FromQuery(Name = "root_path")] string rootPath, [FromQuery] string? path, [FromQuery] string kind)
    {
        return _manager.BrowseFiles(rootPath, path, kind);
    }

    [HttpPost("projects/{projectId:long}/resolve-file-reference")]
    public async Task<IActionResult> ResolveFileReference([FromRoute] long projectId, [FromBody] CodeRepositoryFileReferenceResolveRequest? request, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!_projectAccess.GetAccessibleProjectIds(user).Contains(projectId)) return new ForbidResult();
        try
        {
            var resolved = _manager.ResolveProjectFileReference(projectId, request?.Reference ?? string.Empty);
            return resolved is null
                ? new NotFoundObjectResult(new { message = "The referenced file is not available in this project." })
                : new OkObjectResult(resolved);
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
    }

    [HttpGet("{name}/tree")]
    public object Tree([FromRoute] string name, [FromQuery] string? path) => _indexService.BrowseTree(name, path);

    [HttpGet("{name}/file")]
    public object File([FromRoute] string name, [FromQuery] string path) => _indexService.ReadFile(name, path);

    [HttpGet("{name}/grep")]
    public object Grep([FromRoute] string name, [FromQuery] string query) => _indexService.Grep(name, query);

    [HttpPost("inspect")]
    public CodeRepositoryInspectionDto Inspect([FromBody] CodeRepositoryPathRequest request)
    {
        return _manager.Inspect(request.RootPath);
    }

    [HttpPost("create")]
    public IActionResult Create([FromBody] CodeRepositorySaveRequest request)
    {
        try
        {
            return new OkObjectResult(_manager.Create(request));
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { message = $"挂载代码库失败：{ex.Message}" });
        }
        catch (DirectoryNotFoundException ex)
        {
            return new BadRequestObjectResult(new { message = $"挂载代码库失败：{ex.Message}" });
        }
        catch (InvalidOperationException ex)
        {
            return new BadRequestObjectResult(new { message = $"挂载代码库失败：{ex.Message}" });
        }
        catch (Exception ex)
        {
            return new ObjectResult(new { message = $"挂载代码库时服务器发生异常：{ex.Message}" }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    [HttpPut("{name}")]
    public CodeRepositoryDto Update([FromRoute] string name, [FromBody] CodeRepositorySaveRequest request)
    {
        return _manager.Update(name, request);
    }

    [HttpPost("{name}/index")]
    public object Index([FromRoute] string name)
    {
        _ = Task.Run(async () =>
        {
            try { await _indexService.IndexAsync(name, CancellationToken.None); }
            catch { /* Progress store retains the error for the client. */ }
        });
        return new { ok = true, status = "started" };
    }

    [HttpGet("{name}/index-progress")]
    public CodeRepositoryIndexProgress IndexProgress([FromRoute] string name) => _progressStore.Get(name);

    [HttpGet("{name}/health")]
    public CodeRepositoryHealthDto Health([FromRoute] string name) => _manager.CheckHealth(name);

    [HttpGet("{name}/configured-file")]
    public object ConfiguredFile([FromRoute] string name, [FromQuery] string path) => _manager.ReadConfiguredFile(name, path);

    [HttpPut("{name}/configured-file")]
    public object WriteConfiguredFile([FromRoute] string name, [FromBody] CodeRepositoryFileWriteRequest request) => _manager.WriteConfiguredFile(name, request);

    [HttpGet("{name}/chat-configured-file")]
    public object ChatConfiguredFile([FromRoute] string name, [FromQuery] string path) => _manager.ReadChatConfiguredFile(name, path);

    [HttpPut("{name}/chat-configured-file")]
    public object WriteChatConfiguredFile([FromRoute] string name, [FromBody] CodeRepositoryFileWriteRequest request) => _manager.WriteChatConfiguredFile(name, request);

    [HttpGet("{name}/packages/{archiveName}")]
    public IActionResult DownloadPackage([FromRoute] string name, [FromRoute] string archiveName)
    {
        var archive = _manager.GetPackageArchive(name, archiveName);
        return new PhysicalFileResult(archive.FilePath, "application/zip") { EnableRangeProcessing = true, FileDownloadName = archive.DownloadName };
    }

    [HttpGet("{name}/git/status")]
    public Task<GitWorkspaceStatus> GitStatus([FromRoute] string name, CancellationToken cancellationToken) => _git.StatusAsync(name, cancellationToken);

    [HttpGet("{name}/git/branches")]
    public Task<GitWorkspaceBranches> GitBranches([FromRoute] string name, CancellationToken cancellationToken) => _git.BranchesAsync(name, cancellationToken);

    [HttpGet("{name}/git/diff")]
    public Task<GitWorkspaceDiff> GitDiff([FromRoute] string name, [FromQuery] string? comparison, CancellationToken cancellationToken) => _git.DiffAsync(name, comparison, cancellationToken);

    [HttpPost("{name}/git/checkout")]
    public Task<GitOperationResult> GitCheckout([FromRoute] string name, [FromBody] CodeRepositoryGitCheckoutRequest request, CancellationToken cancellationToken) => _git.CheckoutAsync(name, request.Branch, cancellationToken);

    [HttpPost("{name}/git/discard-and-pull")]
    public Task<GitOperationResult> GitDiscardAndPull([FromRoute] string name, CancellationToken cancellationToken) => _git.DiscardChangesAndPullAsync(name, cancellationToken);

    [HttpPost("{name}/git/pull")]
    public Task<GitOperationResult> GitPull([FromRoute] string name, CancellationToken cancellationToken) => _git.PullAsync(name, cancellationToken);

    [HttpPost("{name}/git/push")]
    public async Task<IActionResult> GitPush([FromRoute] string name, [FromBody] CodeRepositoryGitPushRequest request, CancellationToken cancellationToken)
    {
        var user = await _authService.TryGetCurrentUserAsync(_httpContextAccessor.HttpContext!, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!user.CanCommitCode) return new ForbidResult();
        return new OkObjectResult(await _git.CommitAndPushAsync(name, request.Message, cancellationToken));
    }

    [HttpDelete("{name}")]
    public object Delete([FromRoute] string name)
    {
        _manager.Delete(name);
        return new { ok = true };
    }
}
