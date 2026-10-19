using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Entities.Settings;
using AiAgent.Backend.Services.Auth;
using AiAgent.Backend.Services.Chat;
using AiAgent.Backend.Services.Chat.Agentic;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace AiAgent.Backend.Tests;

public sealed class CodexProfileImageTests
{
    private static CodexResolvedModel Model(bool native, bool ocr = false) => new(
        new("profile:fixture", "Fixture", "", null, "fixture", false, false,
            SupportsImageOcr: ocr, SupportsImageInput: native), null);

    [Theory]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, false, true, false)]
    [InlineData(false, false, true, true, false)]
    [InlineData(false, true, false, true, true)]
    [InlineData(false, true, true, false, false)]
    public void AttachmentGateUsesSelectedRouteWithoutSilentOcrFallback(bool native, bool ocr, bool globalNative, bool globalOcr, bool allowed)
    {
        var model = Model(native, ocr);
        var policy = new ImageOcrPolicyDto { NativeImageInputEnabled = globalNative, Enabled = globalOcr };
        Assert.Equal(native ? "native" : ocr ? "ocr" : "none", model.ImageInput);
        if (allowed) model.EnsureImageInputEnabled(policy);
        else Assert.Throws<InvalidOperationException>(() => model.EnsureImageInputEnabled(policy));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fixture-session")]
    public void NewAndResumedExecReceiveOriginalImagesAsSeparateArguments(string? session)
    {
        var images = new[] { @"C:\controlled files\第一张.png", @"C:\controlled files\第二张.png" };
        var info = CodexChatService.CreateExecStartInfo("fixture.exe", @"C:\workspace", Model(true), "read-only", session, [images[0], images[0], "", images[1]]);
        var args = info.ArgumentList.ToArray();
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardInput);
        Assert.Equal("fixture", args[Array.IndexOf(args, "--profile") + 1]);
        Assert.Equal(2, args.Count(arg => arg == "--image"));
        var imageArgs = args.Select((arg, i) => (arg, i)).Where(x => x.arg == "--image").Select(x => args[x.i + 1]);
        Assert.Equal(images, imageArgs);
        Assert.Equal("-", args[^1]);
        Assert.Equal(session is not null, args.Contains("resume"));
        if (session is not null) Assert.Equal(session, args[^2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncheckedProfilesNeverReceiveOriginalImages(bool ocr)
    {
        var info = CodexChatService.CreateExecStartInfo("fixture.exe", @"C:\workspace", Model(false, ocr), "read-only", null, [@"C:\controlled\image.png"]);
        Assert.DoesNotContain("--image", info.ArgumentList);
        Assert.DoesNotContain(@"C:\controlled\image.png", info.ArgumentList);
    }

    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 0)]
    public async Task NativeProfilesSkipOcrEvenWhenLegacyOcrIsEnabled(bool native, bool ocrEnabled, int expectedCalls)
    {
        var ocr = new OcrSpy();
        var missingCommand = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Codex:Command"] = missingCommand }).Build();
        using var db = Database();
        db.CodeFirst.InitTables<AiChatSession>();
        using var service = new CodexChatService(db, config, new PolicyStub(Model(native, ocrEnabled)), ocr);
        var request = new ChatCompleteRequest { Message = "fixture", RuntimeUserId = "fixture", SessionId = "fixture", MaintenanceWorkspacePath = System.IO.Path.GetTempPath(), LocalImagePaths = ["fixture.png"] };
        // Fails at the intentionally missing executable; no real model or local configuration is used.
        await Assert.ThrowsAsync<CodexDiagnosticException>(() => service.CompleteAsync(request, null, CancellationToken.None));
        Assert.Equal(expectedCalls, ocr.Calls);
        if (native) Assert.Empty(request.ImageOcrResults);
    }

    [Fact]
    public void NativeCapabilityPersistsAndCanBeDisabledWithoutChangingOtherProfiles()
    {
        using var db = Database();
        var service = new CodexModelPolicyService(db);
        var profiles = new List<CodexProfileModelDto> {
            new() { DisplayName = "Vision", ProfileName = "vision", SupportsImageInput = true, SupportsImageOcr = true },
            new() { DisplayName = "Text", ProfileName = "text", SupportsImageOcr = false }
        };
        var request = new CodexModelPolicyUpdateRequest {
            ProfileModels = profiles, AllowedModelIds = ["profile:vision", "profile:text"], DefaultModelId = "profile:vision"
        };
        var admin = new AuthenticatedUser("fixture", "fixture", "admin");
        Assert.Throws<UnauthorizedAccessException>(() => service.UpdatePolicy(new("user", "user"), request));
        service.UpdatePolicy(admin, request);
        var reloaded = new CodexModelPolicyService(db);
        Assert.True(reloaded.GetPolicy().ProfileModels[0].SupportsImageInput);
        Assert.Equal("native", reloaded.GetPolicy().Models.Single(m => m.Id == "profile:vision").ImageInput);
        Assert.Equal("none", reloaded.GetPolicy().Models.Single(m => m.Id == "profile:text").ImageInput);
        Assert.True(reloaded.ResolveModel("profile:vision", null).SupportsNativeImageInput);
        Assert.All(reloaded.GetPolicy().Models.Where(m => m.IsBuiltin), m => Assert.Equal("native", m.ImageInput));
        Assert.True(service.UpdatePolicy(admin, new() { AllowChatModelOverride = false }).ProfileModels[0].SupportsImageInput);
        profiles[0].SupportsImageInput = false;
        profiles[0].SupportsImageOcr = false;
        service.UpdatePolicy(admin, request);
        Assert.Equal("none", reloaded.GetPolicy().Models.Single(m => m.Id == "profile:vision").ImageInput);
    }

    [Fact]
    public void HistoricalSnapshotsDoNotAutomaticallyEnableNativeVision()
    {
        using var db = Database();
        db.Insertable(new AiSettingSnapshot {
            SettingKey = "codex_model_policy",
            PayloadJson = JsonSerializer.Serialize(new {
                BuiltinModelsVersion = 4, AllowedModelIds = new[] { "profile:legacy" }, DefaultModelId = "profile:legacy",
                ProfileModels = new[] { new { DisplayName = "Legacy", ProfileName = "legacy", SupportsImageOcr = true } }
            })
        }).ExecuteCommand();
        var policy = new CodexModelPolicyService(db).GetPolicy();
        Assert.False(policy.ProfileModels.Single().SupportsImageInput);
        Assert.Equal("ocr", policy.Models.Single(m => m.Id == "profile:legacy").ImageInput);
        var dto = JsonSerializer.Deserialize<CodexProfileModelDto>("{\"display_name\":\"legacy\",\"profile_name\":\"legacy\"}")!;
        Assert.False(dto.SupportsImageInput);
        Assert.Contains("\"supports_image_input\":false", JsonSerializer.Serialize(dto));
    }

    private static SqlSugarScope Database()
    {
        var db = new SqlSugarScope(new ConnectionConfig {
            DbType = DbType.Sqlite, ConnectionString = "Data Source=:memory:", IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
            ConfigureExternalServices = new() { EntityService = (_, column) => {
                if (column.DataType == "nvarchar(max)") column.DataType = "text";
                if (column.IsIdentity) column.DataType = "INTEGER";
            } }
        });
        db.CodeFirst.InitTables<AiSettingSnapshot>();
        return db;
    }

    private sealed class PolicyStub(CodexResolvedModel model) : ICodexModelPolicyService
    {
        public CodexResolvedModel ResolveModel(string? id, string? effort) => model;
        public CodexModelPolicyDto GetPolicy() => throw new NotSupportedException();
        public CodexModelPolicyDto UpdatePolicy(AuthenticatedUser user, CodexModelPolicyUpdateRequest request) => throw new NotSupportedException();
    }

    private sealed class OcrSpy : IImageOcrService
    {
        public int Calls { get; private set; }
        public Task<List<ChatImageOcrResult>> ExtractAsync(IEnumerable<string> paths, AgentStreamEventHandler? onEvent, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new List<ChatImageOcrResult>());
        }
        public Task<ImageOcrDiagnosticDto> DiagnoseAsync(string? image, CancellationToken ct) => throw new NotSupportedException();
    }
}
