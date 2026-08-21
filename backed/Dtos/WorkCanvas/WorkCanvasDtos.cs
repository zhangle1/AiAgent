using System.Text.Json.Serialization;
using AiAgent.Backend.Dtos.Chat;

namespace AiAgent.Backend.Dtos.WorkCanvas;

public class WorkCanvasSummaryDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("scope_project_id")] public long? ScopeProjectId { get; set; }
    [JsonPropertyName("node_count")] public int NodeCount { get; set; }
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("updated_at")] public DateTime UpdatedAt { get; set; }
}

public sealed class WorkCanvasSnapshotDto : WorkCanvasSummaryDto
{
    [JsonPropertyName("viewport")] public object? Viewport { get; set; }
    [JsonPropertyName("nodes")] public List<WorkCanvasNodeDto> Nodes { get; set; } = [];
    [JsonPropertyName("edges")] public List<WorkCanvasEdgeDto> Edges { get; set; } = [];
}

public sealed class WorkCanvasNodeDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("node_type")] public string NodeType { get; set; } = "session";
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    [JsonPropertyName("position_x")] public decimal PositionX { get; set; }
    [JsonPropertyName("position_y")] public decimal PositionY { get; set; }
    [JsonPropertyName("session")] public ChatSessionSummaryDto? Session { get; set; }
}

public sealed class WorkCanvasEdgeDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("source_node_id")] public string SourceNodeId { get; set; } = string.Empty;
    [JsonPropertyName("target_node_id")] public string TargetNodeId { get; set; } = string.Empty;
    [JsonPropertyName("relation_type")] public string RelationType { get; set; } = string.Empty;
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("pending_count")] public int PendingCount { get; set; }
}

public sealed class CreateDeliveryLinkRequest
{
    [JsonPropertyName("source_node_id")] public string SourceNodeId { get; set; } = string.Empty;
    [JsonPropertyName("target_node_id")] public string TargetNodeId { get; set; } = string.Empty;
    [JsonPropertyName("label")] public string? Label { get; set; }
}

public sealed class CreateCanvasDeliveryRequest
{
    [JsonPropertyName("link_id")] public string LinkId { get; set; } = string.Empty;
    [JsonPropertyName("sender_note")] public string? SenderNote { get; set; }
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("run_suggested")] public bool RunSuggested { get; set; }
}

public sealed class CanvasDeliveryDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("link_id")] public string? LinkId { get; set; }
    [JsonPropertyName("source_session_id")] public string? SourceSessionId { get; set; }
    [JsonPropertyName("source_session_title")] public string SourceSessionTitle { get; set; } = string.Empty;
    [JsonPropertyName("target_session_id")] public string? TargetSessionId { get; set; }
    [JsonPropertyName("sender_note")] public string? SenderNote { get; set; }
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("run_suggested")] public bool RunSuggested { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTime? CreatedAt { get; set; }
}

public sealed class DecideCanvasDeliveryRequest
{
    [JsonPropertyName("decision")] public string Decision { get; set; } = string.Empty;
}

public sealed class CreateWorkCanvasRequest { [JsonPropertyName("name")] public string Name { get; set; } = string.Empty; [JsonPropertyName("scope_project_id")] public long? ScopeProjectId { get; set; } }
public sealed class UpdateWorkCanvasRequest { [JsonPropertyName("name")] public string? Name { get; set; } [JsonPropertyName("is_archived")] public bool? IsArchived { get; set; } }
public sealed class AddWorkCanvasNodeRequest { [JsonPropertyName("session_id")] public string SessionId { get; set; } = string.Empty; [JsonPropertyName("position_x")] public decimal PositionX { get; set; } [JsonPropertyName("position_y")] public decimal PositionY { get; set; } }
public sealed class UpdateWorkCanvasLayoutRequest { [JsonPropertyName("expected_version")] public int ExpectedVersion { get; set; } [JsonPropertyName("viewport")] public object? Viewport { get; set; } [JsonPropertyName("nodes")] public List<WorkCanvasNodePositionDto> Nodes { get; set; } = []; }
public sealed class WorkCanvasNodePositionDto { [JsonPropertyName("id")] public string Id { get; set; } = string.Empty; [JsonPropertyName("position_x")] public decimal PositionX { get; set; } [JsonPropertyName("position_y")] public decimal PositionY { get; set; } }
