using AiAgent.Backend.Dtos.CodeRepository;
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

    /// <summary>
    /// Creates the code repository API service.
    /// </summary>
    public CodeRepositoryAppService(ICodeRepositoryManager manager, ICodeRepositoryIndexService indexService, ICodeRepositoryIndexProgressStore progressStore)
    {
        _manager = manager;
        _indexService = indexService;
        _progressStore = progressStore;
    }

    [HttpGet("list")]
    public List<CodeRepositoryDto> List()
    {
        return _manager.List();
    }

    [HttpGet("browse")]
    public CodeRepositoryDirectoryBrowserDto Browse([FromQuery] string? path)
    {
        return _manager.Browse(path);
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
    public CodeRepositoryDto Create([FromBody] CodeRepositorySaveRequest request)
    {
        return _manager.Create(request);
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

    [HttpDelete("{name}")]
    public object Delete([FromRoute] string name)
    {
        _manager.Delete(name);
        return new { ok = true };
    }
}