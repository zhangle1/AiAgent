using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Knowledge.KnowAgent;

public static class ContextBudget
{
    // UTF-8 byte count is a conservative upper estimate, not a provider tokenizer.
    // Unknown model windows use this application ceiling; output and instructions are reserved.
    public const int WindowTokens = 65536;
    public const int OutputTokens = 8192;
    public const int ReserveTokens = 4096;
    public static int Estimate(string text) => Encoding.UTF8.GetByteCount(text);
    public static string CallsLabel(int count, int limit) => limit > 0 ? $"{count}/{limit}" : $"{count}（自动，不限次数）";
    public static int EstimateJsonData(string text) => Estimate(JsonSerializer.Serialize(text,
        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

    public static IEnumerable<string> SplitJsonData(string text, int budget)
    {
        foreach (var part in Split(text, budget))
            foreach (var value in Fit(part)) yield return value;
        IEnumerable<string> Fit(string part)
        {
            if (EstimateJsonData(part) <= budget) { yield return part; yield break; }
            var middle = part.Length / 2;
            if (middle > 0 && char.IsLowSurrogate(part[middle])) middle--;
            if (middle == 0) throw new InvalidOperationException("上下文窗口不足以容纳一个完整字符。");
            foreach (var value in Fit(part[..middle])) yield return value;
            foreach (var value in Fit(part[middle..])) yield return value;
        }
    }
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
