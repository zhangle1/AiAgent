using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Llm;
using AiAgent.Backend.Services.Knowledge.Core;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Runs the compiler and model-retrieval path entirely in memory.</summary>
public sealed class KnowledgeChainDiagnosticsService(
    IKnowledgePathService paths,
    ILlmChatClient llm,
    ICodexChatService codex)
{
    internal const string SampleSource = "星河项目的发布窗口为每周三 20:00 至 22:00，发布负责人是平台工程组。紧急修复必须先完成回滚方案评审。";
    internal const string SampleQuery = "星河项目何时发布，紧急修复需要什么前置条件？";

    public async Task<KnowledgeChainCheckResultDto> CheckAsync(KnowledgeCompilerSettingsDto config, CancellationToken cancellationToken)
    {
        KnowledgeCompilerSettings.Validate(config);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes));
        var runtime = Path.Combine(paths.RootPath, ".chain-check", Guid.NewGuid().ToString("N"));
        if (config.Generator == "codex") Directory.CreateDirectory(runtime);
        try
        {
            var model = new KnowledgeModelAdapter(llm, codex, new KnowledgeProcessRequest
            {
                Generator = config.Generator,
                ModelId = config.ModelId,
                ReasoningEffort = config.ReasoningEffort
            }, runtime);
            try
            {
                return await ExecuteAsync(config, model, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Failed(config, "模型调用超时", "在设定时限内没有完成模型响应；请检查模型服务，或提高超时分钟数。");
            }
            catch (InvalidOperationException ex)
            {
                return Failed(config, "模型连接失败", DescribeModelFailure(config, ex));
            }
        }
        finally
        {
            if (config.Generator == "codex" && Directory.Exists(runtime) && !Directory.EnumerateFileSystemEntries(runtime).Any())
                Directory.Delete(runtime);
        }
    }

    internal static async Task<KnowledgeChainCheckResultDto> ExecuteAsync(
        KnowledgeCompilerSettingsDto config,
        IKnowledgeModel model,
        CancellationToken cancellationToken)
    {
        var result = new KnowledgeChainCheckResultDto();
        var compilation = await new KnowledgeCompiler().CompileAsync(
            SampleSource, model, config.MaxSteps, cancellationToken: cancellationToken);
        result.Provider = compilation.Provider;
        result.Model = compilation.Model;
        result.Steps.Add(Step("model", "模型连接", $"已连接 {compilation.Provider ?? config.Generator} / {compilation.Model ?? config.ModelId ?? "默认模型"}"));
        result.Steps.Add(Step("analysis", "内容分析", "内置测试文本分析成功"));
        result.Steps.Add(Step("generation", "知识页生成", $"生成 {compilation.Pages.Count} 个内存知识页"));
        result.Steps.Add(Step("evidence", "证据校验", $"校验通过 {compilation.Pages.Sum(page => page.Evidence.Count)} 条原文证据"));

        var pages = compilation.Pages.Select((page, index) =>
            new WikiSearchPage($"check-{index + 1}", page.Title, page.Markdown + "\n\n原文证据：\n" + string.Join("\n", page.Evidence.Select(e => e.Quote)))).ToList();
        var hits = await new KnowledgeWikiSearch().SearchAsync(SampleQuery, pages, model, 3, config.MaxSteps, cancellationToken);
        result.Steps.Add(Step("retrieval", "模型检索", $"检索完成，命中 {hits.Count} 个知识页"));
        if (hits.Count == 0) throw new InvalidOperationException("模拟检索没有返回可核对引用。");
        result.Steps.Add(Step("citation", "返回引用", $"引用校验成功：{hits[0].Quote[..Math.Min(hits[0].Quote.Length, 80)]}"));
        result.Ok = true;
        return result;
    }

    private static KnowledgeChainCheckStepDto Step(string key, string label, string detail) => new()
    {
        Key = key,
        Label = label,
        Detail = detail
    };

    private static KnowledgeChainCheckResultDto Failed(KnowledgeCompilerSettingsDto config, string label, string detail) => new()
    {
        Ok = false,
        Provider = config.Generator,
        Model = config.ModelId,
        Steps = [new KnowledgeChainCheckStepDto { Key = "model", Label = label, Status = "error", Detail = detail }]
    };

    private static string DescribeModelFailure(KnowledgeCompilerSettingsDto config, InvalidOperationException exception)
    {
        if (exception.Message.Contains("LLM API key is missing", StringComparison.OrdinalIgnoreCase))
        {
            return "LLM API 未配置可用密钥。请到“模型服务 → LLM”保存 API Key，或将执行方式切换为“本地 Codex CLI”。";
        }

        if (exception.Message.Contains("empty response", StringComparison.OrdinalIgnoreCase))
        {
            return "所选模型没有返回可用于提炼的正文。请确认模型支持 OpenAI 兼容的流式 chat/completions，并尝试选择其他模型或“本地 Codex CLI”。";
        }

        return config.Generator == "codex"
            ? "Codex CLI 调用失败。请确认后端机器已完成登录，并在聊天中验证该 Codex 模型可用。"
            : "LLM API 调用失败。请在“模型服务 → LLM”检查该模型所属配置档、地址和密钥。";
    }
}
