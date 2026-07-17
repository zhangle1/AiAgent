using AiAgent.Backend.Dtos.CodeRepository;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace AiAgent.Backend.Services.CodeRepository;

/// <summary>
/// Project-scoped runtime configuration and development process controls.
/// </summary>
[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/code-runtime")]
public sealed class CodeRuntimeAppService : IDynamicApiController
{
    private readonly ICodeRuntimeManager _runtime;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CodeRuntimeAppService(ICodeRuntimeManager runtime, IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor)
    {
        _runtime = runtime;
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("projects/{projectId:long}")]
    public CodeProjectRuntimeDto GetProjectRuntime([FromRoute] long projectId)
    {
        var runtime = _runtime.GetProjectRuntime(projectId);
        AppendRequestHost(runtime.Runs);
        return runtime;
    }

    [HttpPost("projects/{projectId:long}/profiles")]
    public IActionResult CreateProfile([FromRoute] long projectId, [FromBody] CodeRuntimeProfileSaveRequest request)
        => Execute(() => _runtime.SaveProfile(projectId, null, request));

    [HttpPut("projects/{projectId:long}/profiles/{profileId:long}")]
    public IActionResult UpdateProfile([FromRoute] long projectId, [FromRoute] long profileId, [FromBody] CodeRuntimeProfileSaveRequest request)
        => Execute(() => _runtime.SaveProfile(projectId, profileId, request));

    [HttpDelete("projects/{projectId:long}/profiles/{profileId:long}")]
    public IActionResult DeleteProfile([FromRoute] long projectId, [FromRoute] long profileId)
        => Execute(() =>
        {
            _runtime.DeleteProfile(projectId, profileId);
            return new { ok = true };
        });

    [HttpPost("projects/{projectId:long}/start")]
    public async Task<IActionResult> Start([FromRoute] long projectId, [FromBody] CodeRuntimeStartRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var runs = await _runtime.StartAsync(projectId, request, cancellationToken);
            AppendRequestHost(runs);
            return new OkObjectResult(runs);
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return new ConflictObjectResult(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
    }

    [HttpPost("projects/{projectId:long}/runs/{runId}/stop")]
    public IActionResult Stop([FromRoute] long projectId, [FromRoute] string runId)
        => _runtime.Stop(projectId, runId)
            ? new OkObjectResult(new { ok = true })
            : new NotFoundObjectResult(new { message = "Runtime process was not found or is already stopped." });

    [HttpGet("runs/{runId}/logs")]
    public IActionResult Logs([FromRoute] string runId, [FromQuery(Name = "after_sequence")] long? afterSequence)
        => Execute(() => _runtime.GetLogs(runId, afterSequence ?? 0));

    [HttpGet("runs/{runId}/preview/{**path}")]
    public async Task<IActionResult> Preview([FromRoute] string runId, [FromRoute] string? path, CancellationToken cancellationToken)
    {
        try
        {
            var target = _runtime.GetPreviewTarget(runId);
            var context = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("HTTP context is unavailable.");
            var relativePath = string.IsNullOrWhiteSpace(path) ? string.Empty : path.TrimStart('/');
            var targetUri = new Uri($"http://127.0.0.1:{target.Port}/{relativePath}{context.Request.QueryString}");
            using var request = new HttpRequestMessage(HttpMethod.Get, targetUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            using var response = await _httpClientFactory.CreateClient("CodeRuntimePreview").SendAsync(request, cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                var html = System.Text.Encoding.UTF8.GetString(content)
                    .Replace("href=\"/", $"href=\"/api/v1/code-runtime/runs/{runId}/preview/", StringComparison.Ordinal)
                    .Replace("src=\"/", $"src=\"/api/v1/code-runtime/runs/{runId}/preview/", StringComparison.Ordinal);
                content = System.Text.Encoding.UTF8.GetBytes(html);
            }
            context.Response.StatusCode = (int)response.StatusCode;
            return new FileContentResult(content, contentType);
        }
        catch (KeyNotFoundException ex)
        {
            return new NotFoundObjectResult(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return new ConflictObjectResult(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return new ObjectResult(new { message = $"The front-end preview process is not reachable: {ex.Message}" })
            {
                StatusCode = StatusCodes.Status502BadGateway
            };
        }
    }

    private static IActionResult Execute<T>(Func<T> action)
    {
        try
        {
            return new OkObjectResult(action());
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return new NotFoundObjectResult(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return new ConflictObjectResult(new { message = ex.Message });
        }
    }

    private void AppendRequestHost(IEnumerable<CodeRuntimeRunDto> runs)
    {
        var host = _httpContextAccessor.HttpContext?.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(host) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || Uri.CheckHostName(host) == UriHostNameType.Unknown) return;
        var urlHost = host.Contains(':') ? $"[{host}]" : host;
        foreach (var run in runs)
        {
            var url = $"http://{urlHost}:{run.Port}";
            if (!run.AccessUrls.Contains(url, StringComparer.OrdinalIgnoreCase)) run.AccessUrls.Add(url);
        }
    }
}
