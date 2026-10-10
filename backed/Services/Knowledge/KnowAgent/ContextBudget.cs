using System.Text;

namespace AiAgent.Backend.Services.Knowledge.KnowAgent;

public static class ContextBudget
{
    // UTF-8 byte count is a conservative upper estimate, not a provider tokenizer.
    // Unknown model windows use this application ceiling; output and instructions are reserved.
    public const int WindowTokens = 65536;
    public const int OutputTokens = 8192;
    public const int ReserveTokens = 4096;
    public static int Estimate(string text) => Encoding.UTF8.GetByteCount(text);
    public static void Validate(string prompt, int window = WindowTokens, int output = OutputTokens)
    {
        if (Estimate(prompt) + output + ReserveTokens > window)
            throw new InvalidOperationException("KnowAgent 输入超过上下文预算；未截断原文，请拆分资料后重试。");
    }

    public static IEnumerable<string> Split(string text, int budget)
    {
        if (budget < 4) throw new ArgumentOutOfRangeException(nameof(budget));
        var start = 0;
        var bytes = 0;
        for (var index = 0; index < text.Length;)
        {
            var length = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
            var size = Encoding.UTF8.GetByteCount(text.AsSpan(index, length));
            if (bytes + size > budget && index > start)
            {
                yield return text[start..index];
                start = index;
                bytes = 0;
            }
            bytes += size;
            index += length;
        }
        if (start < text.Length) yield return text[start..];
    }
}
