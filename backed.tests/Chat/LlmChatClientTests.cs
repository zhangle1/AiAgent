using AiAgent.Backend.Services.Chat.Llm;

namespace AiAgent.Backend.Tests.Chat;

public sealed class LlmChatClientTests
{
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
