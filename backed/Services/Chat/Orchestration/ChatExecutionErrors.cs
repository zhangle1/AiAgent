using AiAgent.Backend.Dtos.Chat;

namespace AiAgent.Backend.Services.Chat.Orchestration;

internal static class ChatExecutionErrors
{
    internal static string ToClientError(Exception exception, ChatCompleteRequest? request)
    {
        if (exception is ChatDeliveryException) return exception.Message;
        if (exception is CodexDiagnosticException diagnostic) return diagnostic.Message;
        var isCodex = string.Equals(request?.Agent?.Trim(), "codex", StringComparison.OrdinalIgnoreCase);
        if (isCodex && exception.Message.Contains("requires a newer version of Codex", StringComparison.OrdinalIgnoreCase))
        {
            return "当前本机 Codex CLI 版本过旧，无法运行所选 GPT-6 模型。请升级 Codex CLI 后重试。";
        }
        if (isCodex && (exception.Message.StartsWith("Codex stopped after", StringComparison.Ordinal)
            || exception.Message.StartsWith("Codex exceeded", StringComparison.Ordinal)))
        {
            return exception.Message;
        }
        if (!string.Equals(request?.Agent?.Trim(), "deepseek-harness", StringComparison.OrdinalIgnoreCase)) return "Chat request failed.";
        if (exception.Message.StartsWith("DeepSeek Harness requires Dsh:ApiKey", StringComparison.Ordinal)) return "DeepSeek Harness 缺少 Dsh:ApiKey（或后端环境变量 DEEPSEEK_API_KEY）。";
        if (exception is FileNotFoundException) return "DeepSeek Harness 找不到配置文件；请检查 Dsh:ConfigPath。";
        if (exception is DirectoryNotFoundException) return "DeepSeek Harness 工作区或会话目录不存在。";
        if (exception is UnauthorizedAccessException) return "DeepSeek Harness 没有工作区或会话目录的访问权限。";
        if (exception.Message.Contains("closed its JSON-RPC stream", StringComparison.Ordinal)) return "DeepSeek Harness 运行时在初始化期间退出；请检查后端日志中的 DSH 插件配置错误。";
        return "DeepSeek Harness 请求失败；请检查后端日志中的 DSH 错误详情。";
    }
}
