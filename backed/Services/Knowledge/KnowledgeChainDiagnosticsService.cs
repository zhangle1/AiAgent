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
            return await ExecuteAsync(config, model, timeout.Token);
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
}
