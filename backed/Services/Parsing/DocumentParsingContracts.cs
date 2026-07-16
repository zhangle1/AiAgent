using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Parsing;

/// <summary>
/// 文档解析请求。
/// </summary>
public sealed class DocumentParseRequest
{
    /// <summary>
    /// 原始文件路径。
    /// </summary>
    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 解析结果输出目录。
    /// </summary>
    [JsonPropertyName("output_dir")]
    public string OutputDir { get; set; } = string.Empty;

    /// <summary>
    /// 解析引擎，例如 pymupdf4llm、pymupdf。
    /// </summary>
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "pymupdf4llm";

    /// <summary>
    /// 是否导出图片资源。
    /// </summary>
    [JsonPropertyName("write_images")]
    public bool WriteImages { get; set; }
}

/// <summary>
/// 文档解析返回结果。
/// </summary>
public sealed class DocumentParseResult
{
    /// <summary>
    /// 解析是否成功。
    /// </summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary>
    /// 实际处理的 provider。
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "document-parser";

    /// <summary>
    /// 操作名称。
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 解析引擎。
    /// </summary>
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = string.Empty;

    /// <summary>
    /// Markdown 输出路径。
    /// </summary>
    [JsonPropertyName("markdown_path")]
    public string? MarkdownPath { get; set; }

    /// <summary>
    /// 纯文本输出路径。
    /// </summary>
    [JsonPropertyName("text_path")]
    public string? TextPath { get; set; }

    /// <summary>
    /// PDF 页数。
    /// </summary>
    [JsonPropertyName("page_count")]
    public int PageCount { get; set; }

    /// <summary>
    /// 错误编码。
    /// </summary>
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 错误信息。
    /// </summary>
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 额外信息。
    /// </summary>
    [JsonPropertyName("details")]
    public Dictionary<string, object?> Details { get; set; } = [];
}