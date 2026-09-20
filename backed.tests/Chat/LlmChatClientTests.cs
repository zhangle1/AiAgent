using AiAgent.Backend.Services.Chat.Llm;
using AiAgent.Backend.Models.Settings;

namespace AiAgent.Backend.Tests.Chat;

public sealed class LlmChatClientTests
{
    [Fact]
    public void ResolveLlm_UsesProfileThatOwnsExplicitModel()
    {
        var catalog = new ModelCatalog
        {
            Services = new ModelCatalogServices
            {
                Llm = new CatalogService
                {
                    ActiveProfileId = "openai-profile",
                    ActiveModelId = "openai-model",
                    Profiles =
                    [
                        new CatalogProfile
                        {
                            Id = "openai-profile",
                            Binding = "openai",
                            Models = [new CatalogModel { Id = "openai-model", Model = "gpt-test" }]
                        },
                        new CatalogProfile
                        {
                            Id = "deepseek-profile",
                            Binding = "deepseek",
                            Models = [new CatalogModel { Id = "deepseek-model", Model = "deepseek-chat" }]
                        }
                    ]
                }
            }
        };

        var selection = LlmChatClient.ResolveLlm(catalog, "deepseek-model");

        Assert.Equal("deepseek-profile", selection.Profile.Id);
        Assert.Equal("deepseek-model", selection.Model.Id);
    }

    [Fact]
    public void NormalizeToolMessageSequence_KeepsOnlyCompleteToolCallGroups()
    {
        var completeAssistant = new LlmMessage
        {
            Role = "assistant",
            ToolCalls = [new LlmToolCall { Id = "call-complete", Name = "search", ArgumentsJson = "{}" }]
        };
        var incompleteAssistant = new LlmMessage
        {
            Role = "assistant",
            ToolCalls = [new LlmToolCall { Id = "call-missing", Name = "read", ArgumentsJson = "{}" }]
        };

        var normalized = LlmChatClient.NormalizeToolMessageSequence(
        [
            new LlmMessage { Role = "system", Content = "System" },
            new LlmMessage { Role = "tool", ToolCallId = "orphan", Content = "discard" },
            completeAssistant,
            new LlmMessage { Role = "tool", ToolCallId = "call-complete", Content = "kept" },
            incompleteAssistant,
            new LlmMessage { Role = "tool", ToolCallId = "other", Content = "discard" },
            new LlmMessage { Role = "user", Content = "Continue" }
        ]);

        Assert.Collection(normalized,
            message => Assert.Equal("system", message.Role),
            message => Assert.Same(completeAssistant, message),
            message => Assert.Equal("call-complete", message.ToolCallId),
            message => Assert.Equal("user", message.Role));
    }

    [Fact]
    public void ParseStreamData_ReadsOpenAiCompatibleToolCallDelta()
    {
        const string payload = """
        {"choices":[{"finish_reason":null,"delta":{"tool_calls":[{"index":0,"id":"call-1","type":"function","function":{"name":"search","arguments":"{\"query\":\"hello\"}"}}]}}]}
        """;

        var chunk = LlmChatClient.ParseStreamData(payload);

        Assert.NotNull(chunk);
        var delta = Assert.Single(chunk!.ToolCallDeltas);
        Assert.Equal(0, delta.Index);
        Assert.Equal("call-1", delta.Id);
        Assert.Equal("search", delta.Name);
        Assert.Equal("{\"query\":\"hello\"}", delta.ArgumentsDelta);
    }
}
