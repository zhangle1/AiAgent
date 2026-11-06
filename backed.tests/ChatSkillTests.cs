using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Reflection;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

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
    public void DirectCommandRequiresExactBoundaryAndRejectsInvalidVersion()
    {
        var request = new ChatCompleteRequest { Message = "/grill-me 我想改知识库" };
        ChatSkillCatalog.Apply(request);
        Assert.Equal("grill-me", request.SkillInvocation!.Id);
        Assert.Contains("每次只追问", request.ServerPromptMessage);
        request.SkillInvocation = new ChatSkillReference { Id = "teach", Version = "invalid" };
        Assert.Throws<InvalidOperationException>(() => ChatSkillCatalog.Apply(request));
    }

    [Fact]
    public void MultipleSkillsAreResolvedInOrderWithoutLoadingUnselectedBodies()
    {
        var request = new ChatCompleteRequest { Message = "检查这个方案", SkillInvocations = [Reference("grill-me"), Reference("to-spec")] };
        ChatSkillCatalog.Apply(request);
        Assert.Equal(new[] { "grill-me", "to-spec" }, request.SkillInvocations.Select(item => item.Id));
        Assert.Equal(2, request.ResolvedSkillSnapshots.Count);
        Assert.Null(request.SkillInvocation);
        Assert.Null(request.ResolvedSkillSnapshot);
        Assert.Equal("检查这个方案", request.Message);
        foreach (var snapshot in request.ResolvedSkillSnapshots) Assert.Contains(snapshot.GetProperty("prompt").GetString()!, request.ServerPromptMessage);
        Assert.DoesNotContain(ChatSkillCatalog.Resolve(Reference("teach")).GetProperty("prompt").GetString()!, request.ServerPromptMessage);
        Assert.True(request.ServerPromptMessage.IndexOf("id=\"grill-me\"", StringComparison.Ordinal) < request.ServerPromptMessage.IndexOf("id=\"to-spec\"", StringComparison.Ordinal));
        var retry = JsonSerializer.Deserialize<ChatCompleteRequest>(JsonSerializer.Serialize(request))!;
        ChatSkillCatalog.Apply(retry);
        Assert.Equal(request.ServerPromptMessage, retry.ServerPromptMessage);
        Assert.Equal(request.ResolvedSkillSnapshots.Select(item => item.GetRawText()), retry.ResolvedSkillSnapshots.Select(item => item.GetRawText()));
    }

    [Fact]
    public void TypedCommandCanAddToSelectionsAndLegacySelectionIsDeduplicated()
    {
        var request = new ChatCompleteRequest { Message = "/teach 分析需求", SkillInvocation = Reference("grill-me"), SkillInvocations = [Reference("grill-me"), Reference("to-spec")] };
        ChatSkillCatalog.Apply(request);
        Assert.Equal(new[] { "grill-me", "to-spec", "teach" }, request.SkillInvocations.Select(item => item.Id));
        Assert.Equal(3, request.ResolvedSkillSnapshots.Count);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("unknown")]
    [InlineData("conflict")]
    [InlineData("null")]
    public void InvalidItemRejectsTheWholeSelection(string reason)
    {
        var bad = reason switch {
            "unknown" => new ChatSkillReference { Id = "not-a-skill", Version = "fixture" },
            "conflict" => new ChatSkillReference { Id = "grill-me", Version = "changed" },
            "null" => null!,
            _ => new ChatSkillReference { Id = "teach", Version = "tampered" }
        };
        var request = new ChatCompleteRequest { Message = "hello", SkillInvocations = [Reference("grill-me"), bad] };
        Assert.Throws<InvalidOperationException>(() => ChatSkillCatalog.Apply(request));
        Assert.Empty(request.ResolvedSkillSnapshots);
        Assert.Null(request.ResolvedSkillSnapshot);
        Assert.Equal("", request.ServerPromptMessage);
    }

    private static ChatSkillReference Reference(string id)
        => new() { Id = id, Version = ChatSkillCatalog.All.Single(item => item.GetProperty("id").GetString() == id).GetProperty("version").GetString()! };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionPersistsSnapshotsAndClosesBothLegacyAndMultipleSkills(bool legacy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aiagent-chat-skills-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new SqlSugarScope(new ConnectionConfig {
                DbType = DbType.Sqlite, ConnectionString = $"Data Source={path};Pooling=False", IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
                ConfigureExternalServices = new() { EntityService = (_, column) => {
                    if (column.DataType == "nvarchar(max)") column.DataType = "text";
                    if (column.IsIdentity) column.DataType = "INTEGER";
                } }
            });
            db.CodeFirst.InitTables(typeof(AiChatSession), typeof(AiChatMessage));
            var service = new ChatSessionService(db, null!, DispatchProxy.Create<IChatFileAttachmentService, EmptySkillCollaborator>(), null!,
                DispatchProxy.Create<IMemoryService, EmptySkillCollaborator>(), null!, NullLogger<ChatSessionService>.Instance);
            var user = new AuthenticatedUser("fixture", "fixture");
            var request = new ChatCompleteRequest { Message = "保留用户原文", SkillInvocations = legacy ? [Reference("grill-me")] : [Reference("grill-me"), Reference("to-spec")] };
            ChatSkillCatalog.Apply(request);
            await service.RecordUserMessageAsync(user, request, default);
            var stored = db.Queryable<AiChatMessage>().Single();
            Assert.Equal(request.Message, stored.Content);
            using var metadata = JsonDocument.Parse(stored.MetadataJson!);
            Assert.Equal(legacy ? 1 : 2, metadata.RootElement.GetProperty("skill_invocations").GetArrayLength());
            Assert.Equal(request.ResolvedSkillSnapshots.Select(item => JsonSerializer.Serialize(item)), metadata.RootElement.GetProperty("skill_snapshots").EnumerateArray().Select(item => JsonSerializer.Serialize(item)));
            if (legacy)
            {
                stored.MetadataJson = JsonSerializer.Serialize(new { skill_invocation = request.SkillInvocation, skill_snapshot = request.ResolvedSkillSnapshot });
                db.Updateable(stored).ExecuteCommand();
            }
            var ordinary = new ChatCompleteRequest { SessionId = request.SessionId, Message = "普通聊天" };
            ChatSkillCatalog.Apply(ordinary);
            await service.RecordUserMessageAsync(user, ordinary, default);
            Assert.Contains("此前选择的技能流程已结束", ordinary.ServerPromptMessage);
            Assert.Contains("普通聊天", ordinary.ServerPromptMessage);
            Assert.DoesNotContain(request.ResolvedSkillSnapshots[0].GetProperty("prompt").GetString()!, ordinary.ServerPromptMessage);
            var separateSession = new ChatCompleteRequest { Message = "独立会话" };
            ChatSkillCatalog.Apply(separateSession);
            await service.RecordUserMessageAsync(user, separateSession, default);
            Assert.Equal("", separateSession.ServerPromptMessage);
        }
        finally { File.Delete(path); }
    }

    public class EmptySkillCollaborator : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name switch {
                nameof(IChatFileAttachmentService.PersistExtractionFilesAsync) => Task.FromResult(new Dictionary<string, string>()),
                nameof(IMemoryService.RecordObservationAsync) => Task.CompletedTask,
                _ => throw new NotSupportedException(method?.Name)
            };
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
