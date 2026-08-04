using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Entities.Settings;
using AiAgent.Backend.Services.Auth;
using SqlSugar;
using System.Text.Json;

namespace AiAgent.Backend.Services.Chat;

public interface IImageOcrPolicyService
{
    ImageOcrPolicyDto GetPolicy();
    ImageOcrPolicyDto UpdatePolicy(AuthenticatedUser administrator, ImageOcrPolicyUpdateRequest request);
}

/// <summary>Stores the administrator-controlled, opt-in image OCR limits.</summary>
public sealed class ImageOcrPolicyService : IImageOcrPolicyService
{
    private const string SettingKey = "image_ocr_policy";
    private readonly ISqlSugarClient _db;

    public ImageOcrPolicyService(ISqlSugarClient db) => _db = db;

    public ImageOcrPolicyDto GetPolicy() => LoadPolicySafely();

    public ImageOcrPolicyDto UpdatePolicy(AuthenticatedUser administrator, ImageOcrPolicyUpdateRequest request)
    {
        if (!administrator.IsAdministrator) throw new UnauthorizedAccessException("Administrator access is required.");
        if (request is null) throw new ArgumentNullException(nameof(request));
        var current = LoadPolicySafely();
        var policy = new ImageOcrPolicyDto
        {
            Enabled = request.Enabled ?? current.Enabled,
            NativeImageInputEnabled = request.NativeImageInputEnabled ?? current.NativeImageInputEnabled,
            AutoProcessImages = request.AutoProcessImages ?? current.AutoProcessImages,
            Language = NormalizeLanguage(request.Language ?? current.Language),
            MaxImageBytes = Math.Clamp(request.MaxImageBytes ?? current.MaxImageBytes, 1024 * 1024, 50L * 1024 * 1024),
            MaxPromptCharacters = Math.Clamp(request.MaxPromptCharacters ?? current.MaxPromptCharacters, 256, 12000),
            TimeoutSeconds = Math.Clamp(request.TimeoutSeconds ?? current.TimeoutSeconds, 10, 180)
        };
        var latestVersion = _db.Queryable<AiSettingSnapshot>()
            .Where(item => item.SettingKey == SettingKey)
            .OrderByDescending(item => item.VersionNo)
            .Select(item => item.VersionNo)
            .First();
        _db.Insertable(new AiSettingSnapshot
        {
            SettingKey = SettingKey,
            PayloadJson = JsonSerializer.Serialize(policy),
            VersionNo = latestVersion + 1,
            AppliedAt = DateTime.UtcNow,
            AppliedBy = administrator.Username,
            Remark = "Image OCR policy updated"
        }).ExecuteCommand();
        return policy;
    }

    private ImageOcrPolicyDto LoadPolicySafely()
    {
        try
        {
            var row = _db.Queryable<AiSettingSnapshot>()
                .Where(item => item.SettingKey == SettingKey)
                .OrderByDescending(item => item.AppliedAt)
                .OrderByDescending(item => item.Id)
                .First();
            if (row == null || string.IsNullOrWhiteSpace(row.PayloadJson)) return new ImageOcrPolicyDto();
            var raw = JsonSerializer.Deserialize<ImageOcrPolicyDto>(row.PayloadJson);
            return raw == null
                ? new ImageOcrPolicyDto()
                : new ImageOcrPolicyDto
                {
                    Enabled = raw.Enabled,
                    NativeImageInputEnabled = raw.NativeImageInputEnabled,
                    AutoProcessImages = raw.AutoProcessImages,
                    Language = NormalizeLanguage(raw.Language),
                    MaxImageBytes = Math.Clamp(raw.MaxImageBytes, 1024 * 1024, 50L * 1024 * 1024),
                    MaxPromptCharacters = Math.Clamp(raw.MaxPromptCharacters, 256, 12000),
                    TimeoutSeconds = Math.Clamp(raw.TimeoutSeconds, 10, 180)
                };
        }
        catch (JsonException)
        {
            return new ImageOcrPolicyDto();
        }
        catch
        {
            return new ImageOcrPolicyDto();
        }
    }

    private static string NormalizeLanguage(string? language)
    {
        var normalized = language?.Trim().ToLowerInvariant();
        return normalized is "ch" or "en" or "japan" or "korean" ? normalized : "ch";
    }
}
