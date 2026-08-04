using System.Text.Json.Serialization;

namespace AiAgent.Backend.Dtos.Chat;

public sealed class AgentProviderEnvironmentDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("installed")]
    public bool Installed { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;

    [JsonPropertyName("chat_supported")]
    public bool ChatSupported { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public sealed class CodexModelOptionDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    [JsonPropertyName("profile_name")]
    public string? ProfileName { get; set; }

    [JsonPropertyName("supports_reasoning_effort")]
    public bool SupportsReasoningEffort { get; set; } = true;

    [JsonPropertyName("is_builtin")]
    public bool IsBuiltin { get; set; }

    /// <summary>Image input route: native original image, local OCR fallback, or disabled.</summary>
    [JsonPropertyName("image_input")]
    public string ImageInput { get; set; } = "none";
}

public sealed class CodexProfileModelDto
{
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("profile_name")]
    public string ProfileName { get; set; } = string.Empty;

    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("supports_reasoning_effort")]
    public bool SupportsReasoningEffort { get; set; }

    /// <summary>Allows this third-party CLI profile to receive local PaddleOCR text from image attachments.</summary>
    [JsonPropertyName("supports_image_ocr")]
    public bool SupportsImageOcr { get; set; } = true;
}

public sealed class CodexModelPolicyDto
{
    [JsonPropertyName("models")]
    public List<CodexModelOptionDto> Models { get; set; } = [];

    [JsonPropertyName("allowed_model_ids")]
    public List<string> AllowedModelIds { get; set; } = [];

    [JsonPropertyName("default_model_id")]
    public string DefaultModelId { get; set; } = string.Empty;

    [JsonPropertyName("allow_chat_model_override")]
    public bool AllowChatModelOverride { get; set; } = true;

    [JsonPropertyName("allowed_reasoning_efforts")]
    public List<string> AllowedReasoningEfforts { get; set; } = [];

    [JsonPropertyName("default_reasoning_effort")]
    public string DefaultReasoningEffort { get; set; } = "medium";

    [JsonPropertyName("allow_chat_reasoning_effort_override")]
    public bool AllowChatReasoningEffortOverride { get; set; } = true;

    [JsonPropertyName("profile_models")]
    public List<CodexProfileModelDto> ProfileModels { get; set; } = [];
}

public sealed class CodexModelPolicyUpdateRequest
{
    [JsonPropertyName("allowed_model_ids")]
    public List<string>? AllowedModelIds { get; set; }

    [JsonPropertyName("default_model_id")]
    public string? DefaultModelId { get; set; }

    [JsonPropertyName("allow_chat_model_override")]
    public bool? AllowChatModelOverride { get; set; }

    [JsonPropertyName("allowed_reasoning_efforts")]
    public List<string>? AllowedReasoningEfforts { get; set; }

    [JsonPropertyName("default_reasoning_effort")]
    public string? DefaultReasoningEffort { get; set; }

    [JsonPropertyName("allow_chat_reasoning_effort_override")]
    public bool? AllowChatReasoningEffortOverride { get; set; }

    [JsonPropertyName("profile_models")]
    public List<CodexProfileModelDto>? ProfileModels { get; set; }
}

public sealed class ImageOcrPolicyDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Allows native Codex app-server models to receive controlled original image inputs.</summary>
    [JsonPropertyName("native_image_input_enabled")]
    public bool NativeImageInputEnabled { get; set; } = true;

    [JsonPropertyName("auto_process_images")]
    public bool AutoProcessImages { get; set; } = true;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "ch";

    [JsonPropertyName("max_image_bytes")]
    public long MaxImageBytes { get; set; } = 10L * 1024 * 1024;

    [JsonPropertyName("max_prompt_characters")]
    public int MaxPromptCharacters { get; set; } = 12000;

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 45;
}

public sealed class ImageOcrPolicyUpdateRequest
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("native_image_input_enabled")]
    public bool? NativeImageInputEnabled { get; set; }

    [JsonPropertyName("auto_process_images")]
    public bool? AutoProcessImages { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("max_image_bytes")]
    public long? MaxImageBytes { get; set; }

    [JsonPropertyName("max_prompt_characters")]
    public int? MaxPromptCharacters { get; set; }

    [JsonPropertyName("timeout_seconds")]
    public int? TimeoutSeconds { get; set; }
}

public sealed class ImageOcrDiagnosticRequest
{
    [JsonPropertyName("attachment_id")]
    public string? AttachmentId { get; set; }
}

public sealed class ImageOcrDiagnosticDto
{
    [JsonPropertyName("ready")]
    public bool Ready { get; set; }
    [JsonPropertyName("python_configured")]
    public bool PythonConfigured { get; set; }
    [JsonPropertyName("worker_configured")]
    public bool WorkerConfigured { get; set; }
    [JsonPropertyName("paddle_version")]
    public string? PaddleVersion { get; set; }
    [JsonPropertyName("paddleocr_version")]
    public string? PaddleOcrVersion { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    [JsonPropertyName("result")]
    public ChatImageOcrResult? Result { get; set; }
}
