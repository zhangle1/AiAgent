using System.Text.Json;
using AiAgent.Backend.Dtos.Chat;
using AiAgent.Backend.Dtos.WorkCanvas;
using AiAgent.Backend.Entities.Chat;
using AiAgent.Backend.Entities.CodeRepository;
using AiAgent.Backend.Entities.WorkCanvas;
using AiAgent.Backend.Services.Auth;
using SqlSugar;

namespace AiAgent.Backend.Services.WorkCanvas;

public interface IWorkCanvasService
{
    List<WorkCanvasSummaryDto> List(AuthenticatedUser user);
    WorkCanvasSnapshotDto Create(AuthenticatedUser user, CreateWorkCanvasRequest request);
    WorkCanvasSnapshotDto? Get(AuthenticatedUser user, string canvasId);
    bool Update(AuthenticatedUser user, string canvasId, UpdateWorkCanvasRequest request);
    WorkCanvasNodeDto? AddNode(AuthenticatedUser user, string canvasId, AddWorkCanvasNodeRequest request);
    bool RemoveNode(AuthenticatedUser user, string canvasId, string nodeId);
    int? UpdateLayout(AuthenticatedUser user, string canvasId, UpdateWorkCanvasLayoutRequest request);
    WorkCanvasEdgeDto? AddLink(AuthenticatedUser user, string canvasId, CreateDeliveryLinkRequest request);
    bool RemoveLink(AuthenticatedUser user, string canvasId, string linkId);
    CanvasDeliveryDto? SendDelivery(AuthenticatedUser user, CreateCanvasDeliveryRequest request);
    List<CanvasDeliveryDto>? Inbox(AuthenticatedUser user, string sessionId);
    bool DecideDelivery(AuthenticatedUser user, string deliveryId, DecideCanvasDeliveryRequest request);
}

public sealed class WorkCanvasService : IWorkCanvasService
{
    private readonly ISqlSugarClient _db;
    public WorkCanvasService(ISqlSugarClient db) => _db = db;

    public List<WorkCanvasSummaryDto> List(AuthenticatedUser user)
    {
        var canvases = _db.Queryable<AiWorkCanvas>().Where(x => x.UserId == user.Id && (x.IsArchived == false || x.IsArchived == null)).OrderByDescending(x => x.UpdatedAt).ToList();
        var ids = canvases.Select(x => x.Id).ToList();
        var counts = ids.Count == 0 ? new Dictionary<string, int>() : _db.Queryable<AiWorkCanvasNode>().Where(x => ids.Contains(x.CanvasId)).GroupBy(x => x.CanvasId).Select(x => new { CanvasId = x.CanvasId, Count = SqlFunc.AggregateCount(x.Id) }).ToList().ToDictionary(x => x.CanvasId, x => x.Count);
        return canvases.Select(x => Summary(x, counts.GetValueOrDefault(x.Id))).ToList();
    }

    public WorkCanvasSnapshotDto Create(AuthenticatedUser user, CreateWorkCanvasRequest request)
    {
        var name = string.IsNullOrWhiteSpace(request.Name) ? "新建工作画布" : request.Name.Trim();
        var canvas = new AiWorkCanvas { UserId = user.Id, Name = name[..Math.Min(160, name.Length)], ScopeProjectId = request.ScopeProjectId, Version = 1, IsArchived = false };
        _db.Insertable(canvas).ExecuteCommand();
        return Snapshot(canvas, [], []);
    }

    public WorkCanvasSnapshotDto? Get(AuthenticatedUser user, string canvasId)
    {
        var canvas = Owned(user, canvasId);
        if (canvas == null) return null;
        var nodes = _db.Queryable<AiWorkCanvasNode>().Where(x => x.CanvasId == canvas.Id).ToList();
        var edges = _db.Queryable<AiWorkCanvasEdge>().Where(x => x.CanvasId == canvas.Id).ToList();
        return Snapshot(canvas, nodes, edges);
    }

    public bool Update(AuthenticatedUser user, string canvasId, UpdateWorkCanvasRequest request)
    {
        var canvas = Owned(user, canvasId);
        if (canvas == null) return false;
        if (request.Name != null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0) return false;
            canvas.Name = name[..Math.Min(160, name.Length)];
        }
        if (request.IsArchived.HasValue) canvas.IsArchived = request.IsArchived;
        canvas.Version = (canvas.Version ?? 1) + 1;
        canvas.UpdatedAt = DateTime.UtcNow;
        _db.Updateable(canvas).UpdateColumns(x => new { x.Name, x.IsArchived, x.Version, x.UpdatedAt }).ExecuteCommand();
        return true;
    }

    public WorkCanvasNodeDto? AddNode(AuthenticatedUser user, string canvasId, AddWorkCanvasNodeRequest request)
    {
        var canvas = Owned(user, canvasId);
        var session = _db.Queryable<AiChatSession>().First(x => x.Id == request.SessionId && x.UserId == user.Id && !x.IsDeleted && !x.IsArchived);
        if (canvas == null || session == null) return null;
        var existing = _db.Queryable<AiWorkCanvasNode>().First(x => x.CanvasId == canvas.Id && x.ChatSessionId == session.Id);
        if (existing != null) return Node(existing, SessionSummary(session));
        var node = new AiWorkCanvasNode { CanvasId = canvas.Id, ChatSessionId = session.Id, PositionX = request.PositionX, PositionY = request.PositionY };
        _db.Insertable(node).ExecuteCommand();
        Touch(canvas);
        return Node(node, SessionSummary(session));
    }

    public bool RemoveNode(AuthenticatedUser user, string canvasId, string nodeId)
    {
        var canvas = Owned(user, canvasId);
        if (canvas == null || !_db.Queryable<AiWorkCanvasNode>().Any(x => x.Id == nodeId && x.CanvasId == canvas.Id)) return false;
        _db.Ado.UseTran(() =>
        {
            _db.Deleteable<AiWorkCanvasEdge>().Where(x => x.CanvasId == canvas.Id && (x.SourceNodeId == nodeId || x.TargetNodeId == nodeId)).ExecuteCommand();
            _db.Deleteable<AiWorkCanvasNode>().Where(x => x.Id == nodeId && x.CanvasId == canvas.Id).ExecuteCommand();
            Touch(canvas);
        });
        return true;
    }

    public int? UpdateLayout(AuthenticatedUser user, string canvasId, UpdateWorkCanvasLayoutRequest request)
    {
        var canvas = Owned(user, canvasId);
        if (canvas == null || (canvas.Version ?? 1) != request.ExpectedVersion) return null;
        var nodeIds = request.Nodes.Select(x => x.Id).Distinct().ToList();
        var ownedNodes = nodeIds.Count == 0 ? new List<AiWorkCanvasNode>() : _db.Queryable<AiWorkCanvasNode>().Where(x => x.CanvasId == canvas.Id && nodeIds.Contains(x.Id)).ToList();
        if (ownedNodes.Count != nodeIds.Count) return null;
        _db.Ado.UseTran(() =>
        {
            foreach (var position in request.Nodes)
            {
                var node = ownedNodes.First(x => x.Id == position.Id);
                node.PositionX = position.PositionX;
                node.PositionY = position.PositionY;
                node.UpdatedAt = DateTime.UtcNow;
                _db.Updateable(node).UpdateColumns(x => new { x.PositionX, x.PositionY, x.UpdatedAt }).ExecuteCommand();
            }
            canvas.ViewportJson = request.Viewport == null ? canvas.ViewportJson : JsonSerializer.Serialize(request.Viewport);
            canvas.Version = (canvas.Version ?? 1) + 1;
            canvas.UpdatedAt = DateTime.UtcNow;
            _db.Updateable(canvas).UpdateColumns(x => new { x.ViewportJson, x.Version, x.UpdatedAt }).ExecuteCommand();
        });
        return canvas.Version;
    }

    public WorkCanvasEdgeDto? AddLink(AuthenticatedUser user, string canvasId, CreateDeliveryLinkRequest request)
    {
        var canvas = Owned(user, canvasId);
        if (canvas == null || request.SourceNodeId == request.TargetNodeId) return null;
        var nodes = _db.Queryable<AiWorkCanvasNode>().Where(x => x.CanvasId == canvas.Id && (x.Id == request.SourceNodeId || x.Id == request.TargetNodeId)).ToList();
        if (nodes.Count != 2 || nodes.Any(x => string.IsNullOrWhiteSpace(x.ChatSessionId))) return null;
        var existing = _db.Queryable<AiWorkCanvasEdge>().First(x => x.CanvasId == canvas.Id && x.SourceNodeId == request.SourceNodeId && x.TargetNodeId == request.TargetNodeId);
        if (existing != null) return existing.RelationType == "delivery" ? Edge(existing, 0) : null;
        var edge = new AiWorkCanvasEdge { CanvasId = canvas.Id, SourceNodeId = request.SourceNodeId, TargetNodeId = request.TargetNodeId, RelationType = "delivery", Label = Normalize(request.Label, 160) ?? "可投递" };
        _db.Insertable(edge).ExecuteCommand();
        Touch(canvas);
        return Edge(edge, 0);
    }

    public bool RemoveLink(AuthenticatedUser user, string canvasId, string linkId)
    {
        var canvas = Owned(user, canvasId);
        if (canvas == null) return false;
        var removed = _db.Deleteable<AiWorkCanvasEdge>().Where(x => x.Id == linkId && x.CanvasId == canvas.Id).ExecuteCommand() > 0;
        if (removed) Touch(canvas);
        return removed;
    }

    public CanvasDeliveryDto? SendDelivery(AuthenticatedUser user, CreateCanvasDeliveryRequest request)
    {
        var edge = _db.Queryable<AiWorkCanvasEdge, AiWorkCanvas>((edge, canvas) => edge.CanvasId == canvas.Id).Where((edge, canvas) => edge.Id == request.LinkId && canvas.UserId == user.Id && (canvas.IsArchived == false || canvas.IsArchived == null)).Select((edge, canvas) => edge).First();
        if (edge == null || edge.RelationType != "delivery") return null;
        var nodes = _db.Queryable<AiWorkCanvasNode>().Where(x => x.CanvasId == edge.CanvasId && (x.Id == edge.SourceNodeId || x.Id == edge.TargetNodeId)).ToList();
        var source = nodes.FirstOrDefault(x => x.Id == edge.SourceNodeId)?.ChatSessionId;
        var target = nodes.FirstOrDefault(x => x.Id == edge.TargetNodeId)?.ChatSessionId;
        var content = request.Content?.Trim() ?? string.Empty;
        if (source == null || target == null || content.Length == 0 || content.Length > 12000) return null;
        if (_db.Queryable<AiChatSession>().Count(x => (x.Id == source || x.Id == target) && x.UserId == user.Id && !x.IsDeleted) != 2) return null;
        var delivery = new AiCanvasDelivery { LinkId = edge.Id, SourceSessionId = source, TargetSessionId = target, UserId = user.Id, SenderNote = Normalize(request.SenderNote, 500), SelectedContentJson = JsonSerializer.Serialize(new { content }), RunSuggested = request.RunSuggested, Status = "sent" };
        _db.Insertable(delivery).ExecuteCommand();
        return Delivery(delivery);
    }

    public List<CanvasDeliveryDto>? Inbox(AuthenticatedUser user, string sessionId)
    {
        if (!_db.Queryable<AiChatSession>().Any(x => x.Id == sessionId && x.UserId == user.Id && !x.IsDeleted)) return null;
        return _db.Queryable<AiCanvasDelivery>().Where(x => x.UserId == user.Id && x.TargetSessionId == sessionId && (x.Status == "sent" || x.Status == "reviewing")).OrderByDescending(x => x.CreatedAt).ToList().Select(Delivery).ToList();
    }

    public bool DecideDelivery(AuthenticatedUser user, string deliveryId, DecideCanvasDeliveryRequest request)
    {
        var decision = request.Decision?.Trim().ToLowerInvariant() ?? string.Empty;
        if (decision is not ("accepted" or "ignored")) return false;
        var delivery = _db.Queryable<AiCanvasDelivery>().First(x => x.Id == deliveryId && x.UserId == user.Id && (x.Status == "sent" || x.Status == "reviewing"));
        if (delivery == null) return false;
        delivery.Status = decision; delivery.Decision = decision; delivery.DecidedAt = DateTime.UtcNow;
        return _db.Updateable(delivery).UpdateColumns(x => new { x.Status, x.Decision, x.DecidedAt }).ExecuteCommand() > 0;
    }

    private AiWorkCanvas? Owned(AuthenticatedUser user, string id) => _db.Queryable<AiWorkCanvas>().First(x => x.Id == id && x.UserId == user.Id && (x.IsArchived == false || x.IsArchived == null));
    private void Touch(AiWorkCanvas canvas) { canvas.Version = (canvas.Version ?? 1) + 1; canvas.UpdatedAt = DateTime.UtcNow; _db.Updateable(canvas).UpdateColumns(x => new { x.Version, x.UpdatedAt }).ExecuteCommand(); }
    private WorkCanvasSnapshotDto Snapshot(AiWorkCanvas canvas, List<AiWorkCanvasNode> nodes, List<AiWorkCanvasEdge> edges)
    {
        var sessionIds = nodes.Where(x => x.ChatSessionId != null).Select(x => x.ChatSessionId!).Distinct().ToList();
        var sessions = sessionIds.Count == 0 ? new Dictionary<string, ChatSessionSummaryDto>() : _db.Queryable<AiChatSession>().Where(x => sessionIds.Contains(x.Id) && x.UserId == canvas.UserId && !x.IsDeleted).ToList().ToDictionary(x => x.Id, SessionSummary);
        var visibleNodes = nodes.Where(x => x.ChatSessionId != null && sessions.ContainsKey(x.ChatSessionId)).ToList();
        var visibleNodeIds = visibleNodes.Select(x => x.Id).ToHashSet();
        var edgeIds = edges.Select(x => x.Id).ToList();
        var pending = edgeIds.Count == 0 ? new Dictionary<string, int>() : _db.Queryable<AiCanvasDelivery>().Where(x => x.LinkId != null && edgeIds.Contains(x.LinkId) && (x.Status == "sent" || x.Status == "reviewing")).GroupBy(x => x.LinkId).Select(x => new { LinkId = x.LinkId, Count = SqlFunc.AggregateCount(x.Id) }).ToList().Where(x => x.LinkId != null).ToDictionary(x => x.LinkId!, x => x.Count);
        return new WorkCanvasSnapshotDto { Id = canvas.Id, Name = canvas.Name, ScopeProjectId = canvas.ScopeProjectId, NodeCount = visibleNodes.Count, Version = canvas.Version ?? 1, UpdatedAt = canvas.UpdatedAt, Viewport = Deserialize(canvas.ViewportJson), Nodes = visibleNodes.Select(x => Node(x, sessions.GetValueOrDefault(x.ChatSessionId!))).ToList(), Edges = edges.Where(x => visibleNodeIds.Contains(x.SourceNodeId) && visibleNodeIds.Contains(x.TargetNodeId)).Select(x => Edge(x, pending.GetValueOrDefault(x.Id))).ToList() };
    }
    private ChatSessionSummaryDto SessionSummary(AiChatSession session)
    {
        var last = _db.Queryable<AiChatMessage>().Where(x => x.SessionId == session.Id).OrderByDescending(x => x.Id).First();
        var count = _db.Queryable<AiChatMessage>().Count(x => x.SessionId == session.Id);
        var projectName = session.CodeProjectId.HasValue ? _db.Queryable<AiCodeProject>().Where(x => x.Id == session.CodeProjectId.Value && !x.IsDeleted).Select(x => x.DisplayName).First() : null;
        return new ChatSessionSummaryDto { Id = session.Id, Title = session.Title, CreatedAt = session.CreatedAt, UpdatedAt = session.UpdatedAt, MessageCount = count, LastMessage = last?.Content ?? string.Empty, ProjectId = session.CodeProjectId, ProjectName = projectName, SortOrder = session.SortOrder ?? 0, Priority = session.Priority ?? "normal", IsPinned = session.IsPinned ?? false };
    }
    private static WorkCanvasSummaryDto Summary(AiWorkCanvas x, int count) => new() { Id = x.Id, Name = x.Name, ScopeProjectId = x.ScopeProjectId, NodeCount = count, Version = x.Version ?? 1, UpdatedAt = x.UpdatedAt };
    private static WorkCanvasNodeDto Node(AiWorkCanvasNode x, ChatSessionSummaryDto? session) => new() { Id = x.Id, NodeType = x.NodeType, SessionId = x.ChatSessionId, PositionX = x.PositionX, PositionY = x.PositionY, Session = session };
    private static WorkCanvasEdgeDto Edge(AiWorkCanvasEdge x, int pending) => new() { Id = x.Id, SourceNodeId = x.SourceNodeId, TargetNodeId = x.TargetNodeId, RelationType = x.RelationType, Label = x.Label, PendingCount = pending };
    private CanvasDeliveryDto Delivery(AiCanvasDelivery x)
    {
        var title = x.SourceSessionId == null ? string.Empty : _db.Queryable<AiChatSession>().Where(s => s.Id == x.SourceSessionId).Select(s => s.Title).First() ?? string.Empty;
        var content = string.Empty;
        try { content = JsonDocument.Parse(x.SelectedContentJson ?? "{}").RootElement.GetProperty("content").GetString() ?? string.Empty; } catch { }
        return new CanvasDeliveryDto { Id = x.Id, LinkId = x.LinkId, SourceSessionId = x.SourceSessionId, SourceSessionTitle = title, TargetSessionId = x.TargetSessionId, SenderNote = x.SenderNote, Content = content, RunSuggested = x.RunSuggested == true, Status = x.Status ?? string.Empty, CreatedAt = x.CreatedAt };
    }
    private static string? Normalize(string? value, int max) { var result = value?.Trim(); return string.IsNullOrEmpty(result) ? null : result[..Math.Min(max, result.Length)]; }
    private static object? Deserialize(string? json) { if (string.IsNullOrWhiteSpace(json)) return null; try { return JsonSerializer.Deserialize<object>(json); } catch { return null; } }
}
