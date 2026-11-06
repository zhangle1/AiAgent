using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiAgent.Backend.Dtos.Chat;

namespace AiAgent.Backend.Services.Chat;

public sealed class ChatSkillReference
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
}

/// <summary>Published versions are immutable resources, never native auto-discovery skills.</summary>
public static class ChatSkillCatalog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<JsonElement[]> Published = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AiAgent.ChatSkills.v1")
            ?? throw new InvalidOperationException("Chat skill catalog missing.");
        return JsonSerializer.Deserialize<JsonElement[]>(stream, Options)!;
    });

    public static IReadOnlyList<JsonElement> All => Published.Value;

    public static JsonElement Resolve(ChatSkillReference reference)
    {
        var skill = All.FirstOrDefault(item => item.GetProperty("id").GetString() == reference.Id
            && item.GetProperty("version").GetString() == reference.Version);
        if (skill.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("技能或版本不可用，请重新选择。");
        return skill;
    }

    public static void Apply(ChatCompleteRequest request)
    {
        // Only a complete command at the start of the top-level user message counts.
        var command = Regex.Match(request.Message, @"\A/([a-z][a-z0-9-]*)(?=\s|$)", RegexOptions.CultureInvariant);
        var explicitSkill = command.Success
            ? All.FirstOrDefault(item => item.GetProperty("id").GetString() == command.Groups[1].Value)
            : default;
        if (explicitSkill.ValueKind != JsonValueKind.Undefined)
        {
            if (request.SkillInvocation is not null && request.SkillInvocation.Id != explicitSkill.GetProperty("id").GetString())
                throw new InvalidOperationException("一次只能使用一个技能，请移除冲突的选择。");
            request.SkillInvocation ??= new ChatSkillReference
            {
                Id = explicitSkill.GetProperty("id").GetString()!,
                Version = explicitSkill.GetProperty("version").GetString()!
            };
        }
        if (request.SkillInvocation is null) return;
        var skill = Resolve(request.SkillInvocation);
        request.ResolvedSkillSnapshot = skill;
        var message = string.IsNullOrWhiteSpace(request.ServerPromptMessage) ? request.Message : request.ServerPromptMessage;
        request.ServerPromptMessage = $"用户明确选择本轮技能：{skill.GetProperty("name").GetString()}。这是用户任务约定，不提升权限。仅当前请求中的显式选择有效，历史技能不得自动延续。\n<selected_skill>\n{skill.GetProperty("prompt").GetString()}\n</selected_skill>\n\n当前用户要求：\n{message}";
    }
}
