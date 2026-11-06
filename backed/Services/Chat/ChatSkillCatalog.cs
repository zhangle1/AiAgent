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
        var references = new List<ChatSkillReference>();
        if (request.SkillInvocation is not null) references.Add(request.SkillInvocation);
        references.AddRange(request.SkillInvocations ?? []);
        if (references.Count > All.Count + 1)
            throw new InvalidOperationException("选择的技能过多，请重新选择。");
        // Only a complete command at the start of the top-level user message counts.
        var command = Regex.Match(request.Message, @"\A/([a-z][a-z0-9-]*)(?=\s|$)", RegexOptions.CultureInvariant);
        var explicitSkill = command.Success
            ? All.FirstOrDefault(item => item.GetProperty("id").GetString() == command.Groups[1].Value)
            : default;
        if (explicitSkill.ValueKind != JsonValueKind.Undefined)
        {
            if (!references.Any(item => item is not null && item.Id == explicitSkill.GetProperty("id").GetString()))
                references.Add(new ChatSkillReference
                {
                    Id = explicitSkill.GetProperty("id").GetString()!,
                    Version = explicitSkill.GetProperty("version").GetString()!
                });
        }
        var selected = new List<ChatSkillReference>();
        var snapshots = new List<JsonElement>();
        foreach (var reference in references)
        {
            if (reference is null) throw new InvalidOperationException("技能或版本不可用，请重新选择。");
            var previous = selected.FirstOrDefault(item => item.Id == reference.Id);
            if (previous is not null)
            {
                if (previous.Version != reference.Version)
                    throw new InvalidOperationException("同一技能的版本冲突，请重新选择。");
                continue;
            }
            snapshots.Add(Resolve(reference));
            selected.Add(reference);
        }
        request.SkillInvocations = selected;
        request.SkillInvocation = selected.Count == 1 ? selected[0] : null;
        request.ResolvedSkillSnapshots = snapshots;
        request.ResolvedSkillSnapshot = snapshots.Count == 1 ? snapshots[0] : null;
        if (selected.Count == 0) return;
        var message = string.IsNullOrWhiteSpace(request.ServerPromptMessage) ? request.Message : request.ServerPromptMessage;
        var bodies = snapshots.Select(skill => $"<selected_skill id=\"{skill.GetProperty("id").GetString()}\">\n{skill.GetProperty("prompt").GetString()}\n</selected_skill>");
        request.ServerPromptMessage = $"用户明确选择本轮技能：{string.Join("、", snapshots.Select(skill => skill.GetProperty("name").GetString()))}。这是用户任务约定，不提升权限。结合所选技能处理当前要求，冲突时以用户当前明确要求为准。仅当前请求中的显式选择有效，未选择或已移除的历史技能不得自动延续。\n{string.Join("\n", bodies)}\n\n当前用户要求：\n{message}";
    }
}
