using AiAgent.Backend.Dtos.Knowledge;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Llm;
using AiAgent.Backend.Services.Knowledge.Core;

namespace AiAgent.Backend.Services.Knowledge;

public sealed class KnowledgeWikiRetrievalService(KnowledgeWorkspaceService workspace, KnowledgeCompilerSettings settings,
    IKnowledgePathService paths, ILlmChatClient llm, ICodexChatService codex)
{
    public bool Enabled => settings.Get().RetrievalMode == "wiki";

    public async Task<KnowledgeSearchResponse> SearchAsync(string name, string query, int topK, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query) || query.Length > 4000) throw new ArgumentException("Query must contain 1-4000 characters.");
        var config = settings.Get();
        KnowledgeCompilerSettings.Validate(config);
        var pages = workspace.ListPages(name).Where(p => !string.IsNullOrWhiteSpace(p.Content)).ToList();
        if (pages.Count == 0)
        {
            const string message = "尚无知识表示层，请先在原始文件中点击「提炼知识」；无需创建索引。";
            return new() { Query = query, Provider = "wiki", Answer = message, Content = message };
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes));
        // The CLI gets an empty, isolated directory, never the raw knowledge-base directory.
        var runtime = Path.Combine(paths.RootPath, ".wiki-query", Guid.NewGuid().ToString("N"));
        if (config.Generator == "codex") Directory.CreateDirectory(runtime);
        try
        {
            var model = new KnowledgeModelAdapter(llm, codex, new() {
                Generator = config.Generator, ModelId = config.ModelId, ReasoningEffort = config.ReasoningEffort
            }, runtime);
            var hits = await new KnowledgeWikiSearch().SearchAsync(query,
                pages.Select(p => new WikiSearchPage(Key(p), p.Title, p.Content!)).ToList(), model, topK, config.MaxSteps, timeout.Token);
            // Recheck visibility after the model call, so a concurrently deleted source cannot be returned.
            var visible = workspace.ListPages(name).ToDictionary(Key);
            var citations = hits.Where(h => visible.ContainsKey(h.Id)).Select(h => {
                var page = visible[h.Id];
                return new KnowledgeCitationDto { Text = h.Quote, Metadata = new() {
                    ["knowledge_base_name"] = name, ["document_id"] = page.DocumentId,
                    ["artifact_id"] = page.Id, ["page_index"] = page.PageIndex, ["title"] = page.Title,
                    ["file_name"] = page.SourceName, ["review_status"] = page.ReviewStatus,
                    ["source"] = "knowledge_wiki", ["generator"] = config.Generator
                } };
            }).ToList();
            var content = citations.Count == 0 ? "知识表示层中未找到相关证据。" :
                "知识表示层引用（可能包含未审核草稿，请结合原文核实）：\n\n" + string.Join("\n\n", citations.Select((c, i) => $"[{i + 1}] {c.Metadata["title"]}\n{c.Text}"));
            return new() { Query = query, Provider = "wiki-" + config.Generator, Answer = content, Content = content, Citations = citations };
        }
        finally
        {
            // Only remove an empty directory we created; do not recursively delete CLI-generated files.
            if (config.Generator == "codex" && Directory.Exists(runtime) && !Directory.EnumerateFileSystemEntries(runtime).Any())
                Directory.Delete(runtime);
        }
    }

    private static string Key(KnowledgePageDto page) => $"{page.Id}:{page.PageIndex}";
}
