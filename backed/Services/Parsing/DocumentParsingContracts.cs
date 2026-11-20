using System.Text.Json.Serialization;

namespace AiAgent.Backend.Services.Parsing;

/// <summary>
/// 文档解析请求。
/// </summary>
public sealed class DocumentParseRequest
{
    [JsonPropertyName("parsing")]
    public MarkdownParsingOptions Parsing { get; set; } = new();

    /// <summary>调用方可使用原文件名，避免受控临时文件名变成章节名称。</summary>
    [JsonPropertyName("source_name")]
    public string? SourceName { get; set; }

    /// <summary>仅提取文本的聊天／项目入口可以跳过临时章节文件。</summary>
    [JsonPropertyName("write_bundle")]
    public bool WriteBundle { get; set; } = true;
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
    /// 兼容旧请求的引擎字段；当前 PDF 固定使用 C# PdfPig / PDFium 布局解析。
    /// </summary>
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "pdfpig-pdfium-layout";

    /// <summary>
    /// 是否导出图片资源。
    /// </summary>
    [JsonPropertyName("write_images")]
    public bool WriteImages { get; set; } = true;

    /// <summary>仅用于显式图片转录；不自动启用模型或扫描 PDF OCR。</summary>
    [JsonPropertyName("vlm_model_id")]
    public string? VlmModelId { get; set; }

    internal string? PdfCheckpointDirectory { get; init; }
    internal Action<PdfParseProgress>? PdfProgress { get; init; }
}

internal sealed record PdfParseProgress(int Page, int Total, int Completed, int Reused, int Failed)
{
    internal const int BatchSize = 20;
    internal int Batch => (Page - 1) / BatchSize + 1;
    internal int TotalBatches => (Total - 1) / BatchSize + 1;
}

/// <summary>
/// 文档解析返回结果。
/// </summary>
public sealed class DocumentParseResult
{
    [JsonPropertyName("layout_path")]
    public string? LayoutPath { get; set; }

    [JsonPropertyName("index_path")]
    public string? IndexPath { get; set; }

    [JsonPropertyName("section_count")]
    public int SectionCount { get; set; }
    [JsonPropertyName("assets")]
    public List<DocumentParseAsset> Assets { get; set; } = [];

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];

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
public sealed class DocumentParseAsset
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "image";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}
