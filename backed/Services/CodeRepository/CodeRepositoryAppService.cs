using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Services.Git;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>
    /// Creates the code repository API service.
    /// </summary>
    public CodeRepositoryAppService(ICodeRepositoryManager manager, ICodeRepositoryIndexService indexService, ICodeRepositoryIndexProgressStore progressStore, ICodeRepositoryGitService git)
    {
        _manager = manager;
        _indexService = indexService;
        _progressStore = progressStore;
        _git = git;
    }

    [HttpGet("list")]
    public List<CodeRepositoryDto> List()
    {
        return _manager.List();
    }

    [HttpGet("projects")]
    public List<CodeProjectDto> ListProjects() => _manager.ListProjects();

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

    [HttpGet("browse/files")]
    public CodeRepositoryDirectoryBrowserDto BrowseFiles([FromQuery(Name = "root_path")] string rootPath, [FromQuery] string? path, [FromQuery] string kind)
    {
        return _manager.BrowseFiles(rootPath, path, kind);
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

    [HttpPost("{name}/git/pull")]
    public Task<GitOperationResult> GitPull([FromRoute] string name, CancellationToken cancellationToken) => _git.PullAsync(name, cancellationToken);

    [HttpPost("{name}/git/push")]
    public Task<GitOperationResult> GitPush([FromRoute] string name, [FromBody] CodeRepositoryGitPushRequest request, CancellationToken cancellationToken) => _git.CommitAndPushAsync(name, request.Message, cancellationToken);

    [HttpDelete("{name}")]
    public object Delete([FromRoute] string name)
    {
        _manager.Delete(name);
        return new { ok = true };
    }
}
