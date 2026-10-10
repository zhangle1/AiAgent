using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Llm;
using AiAgent.Backend.Services.Settings;

namespace AiAgent.Backend.Services.Knowledge.KnowAgent;

public sealed class KnowledgeAgentService(IModelCatalogService catalog, ILlmChatClient llm,
    ICodexModelPolicyService codexPolicy, KnowledgeCompilerSettings settings, KnowledgeResourceService resources)
{
    public List<KnowledgeAgentModelDto> Models()
    {
        // Project only safe metadata; never return endpoints, headers or credentials to the chat.
        var service = catalog.Load(redactSecrets: true).Services.Llm;
        return service.Profiles.SelectMany(profile => profile.Models.Where(model => !string.IsNullOrWhiteSpace(model.Model)).Select(model => {
            var capability = llm.GetCapabilities(model.Id);
            return new KnowledgeAgentModelDto { Id = model.Id, Name = string.IsNullOrWhiteSpace(model.Name) ? model.Model : model.Name,
                Profile = profile.Name, ContextWindow = capability.ContextWindowTokens, NativeTools = capability.SupportsNativeToolCalling,
                IsDefault = model.Id == service.ActiveModelId };
        })).ToList();
    }

    public object Options() => new { llm_models = Models(), cli_policy = codexPolicy.GetPolicy(), compiler = settings.Get() };

    public KnowledgeAgentModelDto RequireModel(string id) => Models().FirstOrDefault(x => x.Id == id)
        ?? throw new ArgumentException("请选择已配置的 LLM API 模型；未知模型不会回退到默认模型。");

    public KnowledgeResourceImportDto Compile(KnowledgeAgentCompileRequest request)
    {
        if (request.Uris.Count is 0 or > 32) throw new ArgumentException("每次请选择 1–32 个原始资源文件。");
        var configuration = request.Configuration;
        KnowledgeCompilerSettings.Validate(configuration);
        if (configuration.Generator == "llm_api") RequireModel(configuration.ModelId ?? "");
        else
        {
            var resolved = codexPolicy.ResolveModel(configuration.ModelId, configuration.ReasoningEffort);
            configuration.ModelId = resolved.Id;
            configuration.ReasoningEffort = resolved.ReasoningEffort;
        }
        var tree = resources.Tree();
        var files = request.Uris.Distinct(StringComparer.Ordinal).Select(uri => tree.FirstOrDefault(x => x.Uri == uri && x.Kind == "file")
            ?? throw new ArgumentException("请选择当前可见的原始文件。")).ToList();
        if (files.Any(x => x.Uri.StartsWith("viking://user/", StringComparison.Ordinal) &&
            (x.Uri.Contains("/summaries/", StringComparison.Ordinal) || x.Uri.Contains("/wiki/", StringComparison.Ordinal))))
            throw new ArgumentException("生成产物不能作为原始资料重新编译。");
        var result = new KnowledgeResourceImportDto();
        foreach (var file in files)
        {
            try { result.Tasks.Add(resources.Compile(file.Uri, configuration)); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            { result.Warnings.Add($"{file.Name}：{ex.Message}"); }
        }
        return result;
    }
}
