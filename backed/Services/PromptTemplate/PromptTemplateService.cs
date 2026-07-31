using AiAgent.Backend.Dtos.PromptTemplate;
using AiAgent.Backend.Entities.Auth;
using AiAgent.Backend.Entities.PromptTemplate;
using AiAgent.Backend.Services.Admin;
using AiAgent.Backend.Services.Auth;
using SqlSugar;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiAgent.Backend.Services.PromptTemplate;

public interface IPromptTemplateService
{
    Task<List<PromptTemplateDto>> ListAsync(AuthenticatedUser user, string? stage, string? keyword, CancellationToken cancellationToken);
    Task<PromptTemplateDto?> GetAsync(AuthenticatedUser user, long id, CancellationToken cancellationToken);
    Task<(PromptTemplateDto? Template, string? Error)> CreateAsync(AuthenticatedUser user, PromptTemplateSaveRequest request, CancellationToken cancellationToken);
    Task<(PromptTemplateDto? Template, string? Error)> UpdateAsync(AuthenticatedUser user, long id, PromptTemplateSaveRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(AuthenticatedUser user, long id, CancellationToken cancellationToken);
    Task<PromptTemplateDto?> SetLikedAsync(AuthenticatedUser user, long id, bool enabled, CancellationToken cancellationToken);
    Task<PromptTemplateDto?> SetFavoritedAsync(AuthenticatedUser user, long id, bool enabled, CancellationToken cancellationToken);
    Task<(PromptTemplateUseResult? Result, string? Error)> UseAsync(AuthenticatedUser user, long id, PromptTemplateUseRequest request, CancellationToken cancellationToken);
}

public sealed class PromptTemplateService : IPromptTemplateService
{
    private static readonly HashSet<string> AllowedStages = ["requirements", "design", "development", "code-understanding", "testing", "delivery"];
    private static readonly HashSet<string> AllowedVisibilities = ["personal", "project", "team"];
    private static readonly HashSet<string> AllowedVariableTypes = ["text", "textarea", "select"];
    private static readonly Regex VariableToken = new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    private readonly ISqlSugarClient _db;
    private readonly IProjectAccessService _projectAccess;

    public PromptTemplateService(ISqlSugarClient db, IProjectAccessService projectAccess)
        => (_db, _projectAccess) = (db, projectAccess);

    public Task<List<PromptTemplateDto>> ListAsync(AuthenticatedUser user, string? stage, string? keyword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSeeded();
        var normalizedStage = NormalizeStage(stage, allowEmpty: true);
        var query = _db.Queryable<AiPromptTemplate>().Where(item => !item.IsDeleted);
        if (!string.IsNullOrEmpty(normalizedStage)) query = query.Where(item => item.Stage == normalizedStage);
        var rows = query.OrderByDescending(item => item.UseCount).OrderByDescending(item => item.UpdatedAt).ToList();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var needle = keyword.Trim();
            rows = rows.Where(item => item.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || DeserializeTags(item.TagsJson).Any(tag => tag.Contains(needle, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        return Task.FromResult(MapRows(user, rows.Where(item => CanRead(user, item)).ToList()));
    }

    public Task<PromptTemplateDto?> GetAsync(AuthenticatedUser user, long id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSeeded();
        var row = _db.Queryable<AiPromptTemplate>().First(item => item.Id == id && !item.IsDeleted);
        return Task.FromResult(row is not null && CanRead(user, row) ? MapRows(user, [row]).FirstOrDefault() : null);
    }

    public Task<(PromptTemplateDto? Template, string? Error)> CreateAsync(AuthenticatedUser user, PromptTemplateSaveRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (template, error) = BuildTemplate(user, request, null);
        if (error is not null) return Task.FromResult<(PromptTemplateDto?, string?)>((null, error));
        template!.Id = _db.Insertable(template).ExecuteReturnIdentity();
        return Task.FromResult<(PromptTemplateDto?, string?)>((MapRows(user, [template!]).Single(), null));
    }

    public Task<(PromptTemplateDto? Template, string? Error)> UpdateAsync(AuthenticatedUser user, long id, PromptTemplateSaveRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _db.Queryable<AiPromptTemplate>().First(item => item.Id == id && !item.IsDeleted);
        if (current is null || !CanEdit(user, current)) return Task.FromResult<(PromptTemplateDto?, string?)>((null, "模板不存在或你没有编辑权限。"));
        var (template, error) = BuildTemplate(user, request, current);
        if (error is not null) return Task.FromResult<(PromptTemplateDto?, string?)>((null, error));
        _db.Updateable(template!).UpdateColumns(item => new { item.Name, item.Description, item.Stage, item.TagsJson, item.Body, item.VariablesJson, item.CodeProjectId, item.Visibility, item.UpdatedAt }).ExecuteCommand();
        return Task.FromResult<(PromptTemplateDto?, string?)>((MapRows(user, [template!]).Single(), null));
    }

    public Task<bool> DeleteAsync(AuthenticatedUser user, long id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = _db.Queryable<AiPromptTemplate>().First(item => item.Id == id && !item.IsDeleted);
        if (row is null || !CanEdit(user, row)) return Task.FromResult(false);
        var updated = _db.Updateable<AiPromptTemplate>().SetColumns(item => item.IsDeleted == true).SetColumns(item => item.UpdatedAt == DateTime.UtcNow).Where(item => item.Id == id).ExecuteCommand();
        return Task.FromResult(updated > 0);
    }

    public Task<PromptTemplateDto?> SetLikedAsync(AuthenticatedUser user, long id, bool enabled, CancellationToken cancellationToken)
        => SetUserStateAsync(user, id, enabled, updateLike: true, cancellationToken);

    public Task<PromptTemplateDto?> SetFavoritedAsync(AuthenticatedUser user, long id, bool enabled, CancellationToken cancellationToken)
        => SetUserStateAsync(user, id, enabled, updateLike: false, cancellationToken);

    public Task<(PromptTemplateUseResult? Result, string? Error)> UseAsync(AuthenticatedUser user, long id, PromptTemplateUseRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = _db.Queryable<AiPromptTemplate>().First(item => item.Id == id && !item.IsDeleted);
        if (row is null || !CanRead(user, row)) return Task.FromResult<(PromptTemplateUseResult?, string?)>((null, "模板不存在或你没有使用权限。"));

        if (row.CodeProjectId.HasValue && request.ProjectId.HasValue && request.ProjectId.Value != row.CodeProjectId.Value)
            return Task.FromResult<(PromptTemplateUseResult?, string?)>((null, "该模板已绑定项目，不能切换到其他项目使用。"));
        var projectId = request.ProjectId ?? row.CodeProjectId;
        if (projectId.HasValue && !_projectAccess.CanAccess(user, projectId.Value)) return Task.FromResult<(PromptTemplateUseResult?, string?)>((null, "你没有所选项目的访问权限。"));
        var variables = request.Variables ?? [];
        var definitions = DeserializeVariables(row.VariablesJson);
        foreach (var key in variables.Keys)
            if (!definitions.Any(item => item.Key == key)) return Task.FromResult<(PromptTemplateUseResult?, string?)>((null, $"变量“{key}”不在模板定义中。"));

        foreach (var definition in definitions)
        {
            var value = variables.GetValueOrDefault(definition.Key) ?? definition.DefaultValue ?? string.Empty;
            if (value.Length > 4000) return Task.FromResult<(PromptTemplateUseResult?, string?)>((null, $"变量“{definition.Label}”过长。"));
            if (definition.Required && string.IsNullOrWhiteSpace(value)) return Task.FromResult<(PromptTemplateUseResult?, string?)>((null, $"请填写必填变量“{definition.Label}”。"));
        }

        var rendered = VariableToken.Replace(row.Body, match =>
        {
            var key = match.Groups[1].Value;
            var definition = definitions.FirstOrDefault(item => item.Key == key);
            return variables.GetValueOrDefault(key) ?? definition?.DefaultValue ?? string.Empty;
        });
        row.UseCount++;
        _db.Updateable(row).UpdateColumns(item => item.UseCount).ExecuteCommand();
        var dto = MapRows(user, [row]).Single();
        return Task.FromResult<(PromptTemplateUseResult?, string?)>((new PromptTemplateUseResult { Template = dto, ProjectId = projectId, RenderedContent = rendered }, null));
    }

    private Task<PromptTemplateDto?> SetUserStateAsync(AuthenticatedUser user, long id, bool enabled, bool updateLike, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = _db.Queryable<AiPromptTemplate>().First(item => item.Id == id && !item.IsDeleted);
        if (row is null || !CanRead(user, row)) return Task.FromResult<PromptTemplateDto?>(null);
        var state = _db.Queryable<AiPromptTemplateUserState>().First(item => item.UserId == user.Id && item.TemplateId == id);
        var isNew = state is null;
        state ??= new AiPromptTemplateUserState { UserId = user.Id, TemplateId = id };
        var before = state.IsLiked;
        if (updateLike) state.IsLiked = enabled;
        else state.IsFavorited = enabled;
        state.UpdatedAt = DateTime.UtcNow;
        if (isNew) _db.Insertable(state).ExecuteCommand();
        else _db.Updateable(state).UpdateColumns(item => new { item.IsLiked, item.IsFavorited, item.UpdatedAt }).ExecuteCommand();
        if (updateLike && before != enabled)
        {
            row.LikeCount = Math.Max(0, row.LikeCount + (enabled ? 1 : -1));
            _db.Updateable(row).UpdateColumns(item => item.LikeCount).ExecuteCommand();
        }
        return Task.FromResult<PromptTemplateDto?>(MapRows(user, [row]).Single());
    }

    private (AiPromptTemplate? Template, string? Error) BuildTemplate(AuthenticatedUser user, PromptTemplateSaveRequest request, AiPromptTemplate? existing)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        var body = request.Body?.Trim() ?? string.Empty;
        var stage = NormalizeStage(request.Stage, allowEmpty: false);
        var visibility = NormalizeVisibility(request.Visibility);
        var tags = (request.Tags ?? []).Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        var variables = NormalizeVariables(request.Variables ?? []);
        if (name.Length is < 2 or > 120) return (null, "模板名称长度应为 2 到 120 个字符。");
        if (description.Length is < 4 or > 320) return (null, "模板摘要长度应为 4 到 320 个字符。");
        if (body.Length is < 8 or > 50000) return (null, "模板正文长度应为 8 到 50000 个字符。");
        if (stage is null) return (null, "研发阶段无效。");
        if (visibility is null) return (null, "可见范围无效。");
        if (request.ProjectId.HasValue && !_projectAccess.CanAccess(user, request.ProjectId.Value)) return (null, "你没有所选项目的访问权限。");
        if (visibility == "project" && !request.ProjectId.HasValue) return (null, "项目模板必须选择项目。");
        if (variables.Error is not null) return (null, variables.Error);
        var bodyKeys = VariableToken.Matches(body).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        var configuredKeys = variables.Items.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var missing = bodyKeys.FirstOrDefault(key => !configuredKeys.Contains(key));
        if (missing is not null) return (null, $"正文变量 ${{{missing}}} 尚未配置为属性。");
        if (tags.Any(tag => tag.Length > 32)) return (null, "标签不能超过 32 个字符。");

        var template = existing ?? new AiPromptTemplate { CreatedBy = user.Id, CreatedAt = DateTime.UtcNow };
        template.Name = name;
        template.Description = description;
        template.Stage = stage;
        template.TagsJson = JsonSerializer.Serialize(tags);
        template.Body = body;
        template.VariablesJson = JsonSerializer.Serialize(variables.Items);
        template.CodeProjectId = request.ProjectId;
        template.Visibility = visibility;
        template.UpdatedAt = DateTime.UtcNow;
        return (template, null);
    }

    private List<PromptTemplateDto> MapRows(AuthenticatedUser user, List<AiPromptTemplate> rows)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(item => item.Id).ToList();
        var states = _db.Queryable<AiPromptTemplateUserState>().Where(item => item.UserId == user.Id && ids.Contains(item.TemplateId)).ToList().ToDictionary(item => item.TemplateId);
        var userIds = rows.Select(item => item.CreatedBy).Where(item => item != "system").Distinct().ToList();
        var authors = userIds.Count == 0 ? new Dictionary<string, string>() : _db.Queryable<AiUser>().Where(item => userIds.Contains(item.Id)).ToList().ToDictionary(item => item.Id, item => string.IsNullOrWhiteSpace(item.Alias) ? item.Username : item.Alias!);
        return rows.Select(item =>
        {
            states.TryGetValue(item.Id, out var state);
            return new PromptTemplateDto
            {
                Id = item.Id,
                Name = item.Name,
                Description = item.Description,
                Stage = item.Stage,
                Tags = DeserializeTags(item.TagsJson),
                Body = item.Body,
                Variables = DeserializeVariables(item.VariablesJson),
                ProjectId = item.CodeProjectId,
                Visibility = item.Visibility,
                AuthorName = item.CreatedBy == "system" ? "AiAgent 官方" : authors.GetValueOrDefault(item.CreatedBy, "项目成员"),
                CreatedByMe = item.CreatedBy == user.Id,
                LikedByMe = state?.IsLiked ?? false,
                FavoritedByMe = state?.IsFavorited ?? false,
                LikeCount = item.LikeCount,
                UseCount = item.UseCount,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
            };
        }).ToList();
    }

    private bool CanRead(AuthenticatedUser user, AiPromptTemplate item)
    {
        if (item.Visibility == "personal" && item.CreatedBy != user.Id) return false;
        return !item.CodeProjectId.HasValue || _projectAccess.CanAccess(user, item.CodeProjectId.Value);
    }

    private static bool CanEdit(AuthenticatedUser user, AiPromptTemplate item) => user.IsAdministrator || item.CreatedBy == user.Id;
    private static string? NormalizeStage(string? value, bool allowEmpty) => string.IsNullOrWhiteSpace(value) ? (allowEmpty ? string.Empty : null) : AllowedStages.Contains(value.Trim().ToLowerInvariant()) ? value.Trim().ToLowerInvariant() : null;
    private static string? NormalizeVisibility(string? value) => string.IsNullOrWhiteSpace(value) ? "personal" : AllowedVisibilities.Contains(value.Trim().ToLowerInvariant()) ? value.Trim().ToLowerInvariant() : null;

    private static (List<PromptTemplateVariableDto> Items, string? Error) NormalizeVariables(List<PromptTemplateVariableDto> items)
    {
        if (items.Count > 20) return ([], "变量数量不能超过 20 个。");
        var normalized = new List<PromptTemplateVariableDto>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in items)
        {
            var key = source.Key?.Trim() ?? string.Empty;
            if (!Regex.IsMatch(key, "^[A-Za-z_][A-Za-z0-9_]*$")) return ([], "变量名只能使用字母、数字和下划线，且不能以数字开头。");
            if (!keys.Add(key)) return ([], $"变量“{key}”重复。" );
            var type = string.IsNullOrWhiteSpace(source.Type) ? "text" : source.Type.Trim().ToLowerInvariant();
            if (!AllowedVariableTypes.Contains(type)) return ([], "变量类型无效。" );
            var label = string.IsNullOrWhiteSpace(source.Label) ? key : source.Label.Trim();
            if (label.Length > 64 || (source.DefaultValue?.Length ?? 0) > 4000 || (source.Description?.Length ?? 0) > 240) return ([], "变量属性长度超出限制。" );
            var options = (source.Options ?? []).Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).Take(30).ToList();
            if (type == "select" && options.Count == 0) return ([], $"选择型变量“{label}”至少需要一个选项。" );
            normalized.Add(new PromptTemplateVariableDto { Key = key, Label = label, Type = type, Required = source.Required, DefaultValue = source.DefaultValue?.Trim(), Description = source.Description?.Trim(), Options = options });
        }
        return (normalized, null);
    }

    private static List<string> DeserializeTags(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private static List<PromptTemplateVariableDto> DeserializeVariables(string json)
    {
        try { return JsonSerializer.Deserialize<List<PromptTemplateVariableDto>>(json) ?? []; }
        catch { return []; }
    }

    private void EnsureSeeded()
    {
        if (_db.Queryable<AiPromptTemplate>().Any(item => !item.IsDeleted)) return;
        var now = DateTime.UtcNow;
        var seeds = new[]
        {
            Seed("需求评审与验收标准补全", "将模糊需求拆为影响矩阵和 Given/When/Then 验收标准。", "requirements", ["需求评审", "BDD", "验收"], "你是跨职能研发评审主持人。评审需求：${requirement}，业务目标：${business_goal}。\n\n输出范围、澄清问题、影响矩阵、Given/When/Then 验收标准、风险与假设。", [Variable("requirement", "需求描述", "textarea", true), Variable("business_goal", "业务目标", "text", true)]),
            Seed("技术方案与 ADR 评审", "围绕约束、取舍、迁移与回滚形成可评审的架构决策记录。", "design", ["架构", "ADR", "接口设计"], "你是企业应用架构师。评估待决策问题：${decision}。约束：${constraints}。\n\n按 ADR 输出背景、备选方案、决策矩阵、推荐方案、接口数据契约、迁移和回滚。", [Variable("decision", "待决策问题", "textarea", true), Variable("constraints", "约束条件", "textarea", false)]),
            Seed("最小可交付实现计划", "以文件级改动、数据/API 影响和测试清单拆解功能实现。", "development", ["开发", "实现计划", "影响分析"], "你是项目的资深开发者。为模块 ${target_module} 实现：${feature}。\n\n输出实现边界、文件级变更、接口数据影响、实施顺序、回滚和测试建议。", [Variable("target_module", "目标模块", "text", true), Variable("feature", "功能与验收标准", "textarea", true)]),
            Seed("功能调用链与 Mermaid 时序图", "从代码入口梳理调用链、异常分支、事务边界并输出时序图。", "code-understanding", ["代码理解", "Mermaid", "调用链"], "你是代码考古与架构分析助手。分析入口：${entry}，粒度：${granularity}。\n\n输出功能摘要、文件符号调用链、Mermaid sequenceDiagram、事务边界、异常和未知项。", [Variable("entry", "功能入口", "text", true), new PromptTemplateVariableDto { Key = "granularity", Label = "分析粒度", Type = "select", Required = true, DefaultValue = "service", Options = ["interface", "service", "cross-service"] }]),
            Seed("风险导向测试与日志补强", "补齐主流程、异常、并发和回归测试，并建议安全的日志与指标。", "testing", ["测试", "日志", "可观测性"], "你是测试开发与可观测性工程师。针对变更：${change}，故障线索：${incident}。\n\n输出测试矩阵、日志指标、测试数据、回归范围与缺失证据。不得记录敏感原文。", [Variable("change", "功能或变更", "textarea", true), Variable("incident", "故障现象", "textarea", false)]),
            Seed("交付验收包与放行建议", "将需求、变更、CI、测试与部署证据整理为可追溯的放行结论。", "delivery", ["交付", "验收", "发布"], "你是交付验收负责人。为版本 ${release} 汇总需求、PR、CI、测试和部署记录。\n\n输出追溯矩阵、质量门禁、发布检查单、回滚条件、遗留风险与放行建议。", [Variable("release", "版本或交付项", "text", true)])
        };
        foreach (var item in seeds)
        {
            item.CreatedAt = now;
            item.UpdatedAt = now;
        }
        _db.Insertable(seeds).ExecuteCommand();
    }

    private static AiPromptTemplate Seed(string name, string description, string stage, List<string> tags, string body, List<PromptTemplateVariableDto> variables)
        => new() { Name = name, Description = description, Stage = stage, TagsJson = JsonSerializer.Serialize(tags), Body = body, VariablesJson = JsonSerializer.Serialize(variables), Visibility = "team", CreatedBy = "system" };

    private static PromptTemplateVariableDto Variable(string key, string label, string type, bool required)
        => new() { Key = key, Label = label, Type = type, Required = required };
}
