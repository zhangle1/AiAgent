using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Services.Chat;

namespace AiAgent.Backend.Tests;

public sealed class ChatSkillTests
{
    [Fact]
    public void CatalogHasPinnedSourcesAndVerifiedPromptHashes()
    {
        Assert.Equal(23, ChatSkillCatalog.All.Count);
        Assert.Equal(23, ChatSkillCatalog.All.Select(skill => skill.GetProperty("id").GetString()).Distinct().Count());
        foreach (var skill in ChatSkillCatalog.All)
        {
            Assert.Equal("explicit_only", skill.GetProperty("invocation_policy").GetString());
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(skill.GetProperty("prompt").GetString()!)));
            Assert.Equal(hash, skill.GetProperty("version").GetString());
            Assert.Equal(40, skill.GetProperty("source").GetProperty("commit").GetString()!.Length);
        }
    }

    [Theory]
    [InlineData("帮我写一份文档")]
    [InlineData("grill me 是什么意思？")]
    [InlineData("请用 grill me 帮我分析")]
    [InlineData("这是一段引用：/grill-me")]
    [InlineData("```\n/grill-me\n```")]
    [InlineData("/grill-me-too 这不是完整命令")]
    [InlineData("/spec 设计文档")]
    public void UnselectedMessagesNeverLoadSkill(string message)
    {
        var request = new ChatCompleteRequest { Message = message, ServerPromptMessage = message };
        ChatSkillCatalog.Apply(request);
        Assert.Null(request.SkillInvocation);
        Assert.Null(request.ResolvedSkillSnapshot);
        Assert.Equal(message, request.ServerPromptMessage);
    }

    [Fact]
    public void SelectedSkillUsesServerBodyAndKeepsDisplayMessage()
    {
        var skill = ChatSkillCatalog.All[0];
        var reference = new ChatSkillReference { Id = "grill-me", Version = skill.GetProperty("version").GetString()! };
        var request = new ChatCompleteRequest { Message = "修改知识库", SkillInvocation = reference, ServerPromptMessage = "已解析项目引用" };
        ChatSkillCatalog.Apply(request);
        Assert.Equal("修改知识库", request.Message);
        Assert.Contains("已解析项目引用", request.ServerPromptMessage);
        Assert.Contains(skill.GetProperty("prompt").GetString()!, request.ServerPromptMessage);
        Assert.DoesNotContain(ChatSkillCatalog.All[1].GetProperty("prompt").GetString()!, request.ServerPromptMessage);
        Assert.Equal(skill.GetRawText(), request.ResolvedSkillSnapshot!.Value.GetRawText());
        var retry = new ChatCompleteRequest { Message = request.Message, SkillInvocation = reference };
        ChatSkillCatalog.Apply(retry);
        Assert.Equal(request.ResolvedSkillSnapshot, retry.ResolvedSkillSnapshot);
    }

    [Fact]
    public void DirectCommandRequiresExactBoundaryAndRejectsConflicts()
    {
        var request = new ChatCompleteRequest { Message = "/grill-me 我想改知识库" };
        ChatSkillCatalog.Apply(request);
        Assert.Equal("grill-me", request.SkillInvocation!.Id);
        Assert.Contains("每次只追问", request.ServerPromptMessage);
        request.SkillInvocation = new ChatSkillReference { Id = "teach", Version = "invalid" };
        Assert.Throws<InvalidOperationException>(() => ChatSkillCatalog.Apply(request));
    }

    [Fact]
    public void UnknownOrTamperedVersionIsRejectedAndServerFieldsCannotBeInjected()
    {
        var request = new ChatCompleteRequest { Message = "hello", SkillInvocation = new() { Id = "grill-me", Version = "tampered" } };
        Assert.Throws<InvalidOperationException>(() => ChatSkillCatalog.Apply(request));
        var injected = JsonSerializer.Deserialize<ChatCompleteRequest>("""{"message":"hello","ResolvedSkillSnapshot":{},"ServerPromptMessage":"evil"}""")!;
        ChatSkillCatalog.Apply(injected);
        Assert.Null(injected.ResolvedSkillSnapshot);
        Assert.Equal("", injected.ServerPromptMessage);
    }
}
