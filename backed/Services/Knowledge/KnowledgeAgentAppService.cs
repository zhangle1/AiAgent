using System.Text.Json;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat.Agentic;
using AiAgent.Backend.Services.Knowledge.KnowAgent;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.Backend.Services.Knowledge;

[DynamicApiController]
[ApiDescriptionSettings("v1", KeepName = true)]
[Route("api/v1/knowledge-agent")]
public sealed class KnowledgeAgentAppService(KnowledgeAgentService agent, KnowledgeAgentChatEngine engine,
    IKnowledgeAgentTools tools, IHttpContextAccessor http, IAuthService auth) : IDynamicApiController
{
    [HttpGet("options")]
    public object Options() => agent.Options();

    [HttpPost("command")]
    public async Task<IActionResult> Command([FromBody] KnowledgeAgentCommandRequest request, CancellationToken ct)
    {
        await RequireUser(ct);
        var scope = await tools.ResolveScopeAsync(request.Uri, ct);
        var result = await tools.ExecuteAsync(scope, request, ct);
        return new ContentResult { Content = result, ContentType = "application/json; charset=utf-8" };
    }

    [HttpPost("compile")]
    public async Task<KnowledgeResourceImportDto> Compile([FromBody] KnowledgeAgentCompileRequest request, CancellationToken ct)
    { await RequireUser(ct); return agent.Compile(request); }

    [HttpPost("stream")]
    public async Task Stream([FromBody] KnowledgeAgentChatRequest request, CancellationToken ct)
    {
        await RequireUser(ct);
        agent.RequireModel(request.ModelId);
        var response = http.HttpContext!.Response;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        async Task Write(AgentStreamEvent data, CancellationToken token)
        {
            await response.WriteAsync("data: " + JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n\n", token);
            await response.Body.FlushAsync(token);
        }
        try { await engine.RunAsync(request, Write, timeout.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* Browser cancellation ends the stream. */ }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var message = ex is OperationCanceledException ? "知识 Agent 超时，请缩小范围后重试。" : ex.Message;
            await Write(new() { Type = "error", Content = message.Length > 800 ? message[..800] : message }, ct);
        }
    }

    private async Task RequireUser(CancellationToken ct)
    {
        if (http.HttpContext is null || await auth.TryGetCurrentUserAsync(http.HttpContext, ct) is null) throw new UnauthorizedAccessException();
    }
}
