using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Entities.Knowledge;

namespace AiAgent.Backend.Services.Knowledge;

/// <summary>Knowledge execution only; queue lifecycle and persistence belong to TaskQueue.</summary>
internal static class KnowledgeCompilationHandler
{
    public static async Task ExecuteAsync(IKnowledgeIngestionService ingestion, KnowledgeResourceSemanticService? semantic,
        AiKnowledgeBase kb, AiKnowledgeDocument document, KnowledgeCompilerSettingsDto config, bool parseOnly,
        Action<string, int, string> progress, CancellationToken ct)
    {
        progress("validating", 1, "正在校验知识整理设置");
        KnowledgeCompilerSettings.Validate(config);
        var request = new KnowledgeProcessRequest {
            Generator = config.Generator, ModelId = config.ModelId, VlmModelId = config.VlmModelId, ReasoningEffort = config.ReasoningEffort
        };
        if (!parseOnly)
        {
            progress("compiling", 1, "正在提炼知识");
            await ingestion.ProcessAsync(kb, document, request, ct, config, (value, message) => progress("compiling", value, message));
            return;
        }
        progress("parsing", 1, "正在解析正文 L2");
        await ingestion.ParseResourceAsync(kb, document, request, ct, (value, message) => progress("parsing", value * 35 / 100, message));
        progress("semantic", 35, "正文 L2 已保存，等待生成语义 L0/L1");
        if (semantic is null) throw new InvalidOperationException("语义生成服务未配置；正文已保存，请修复服务配置后重新解析。");
        await semantic.GenerateAsync(kb, document, config, (value, message) => progress("semantic", value, message), ct);
    }
}
