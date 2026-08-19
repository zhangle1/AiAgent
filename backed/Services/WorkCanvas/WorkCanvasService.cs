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

    private AiWorkCanvas? Owned(AuthenticatedUser user, string id) => _db.Queryable<AiWorkCanvas>().First(x => x.Id == id && x.UserId == user.Id && (x.IsArchived == false || x.IsArchived == null));
    private void Touch(AiWorkCanvas canvas) { canvas.Version = (canvas.Version ?? 1) + 1; canvas.UpdatedAt = DateTime.UtcNow; _db.Updateable(canvas).UpdateColumns(x => new { x.Version, x.UpdatedAt }).ExecuteCommand(); }
    private WorkCanvasSnapshotDto Snapshot(AiWorkCanvas canvas, List<AiWorkCanvasNode> nodes, List<AiWorkCanvasEdge> edges)
    {
        var sessionIds = nodes.Where(x => x.ChatSessionId != null).Select(x => x.ChatSessionId!).Distinct().ToList();
        var sessions = sessionIds.Count == 0 ? new Dictionary<string, ChatSessionSummaryDto>() : _db.Queryable<AiChatSession>().Where(x => sessionIds.Contains(x.Id) && x.UserId == canvas.UserId && !x.IsDeleted).ToList().ToDictionary(x => x.Id, SessionSummary);
        var visibleNodes = nodes.Where(x => x.ChatSessionId != null && sessions.ContainsKey(x.ChatSessionId)).ToList();
        var visibleNodeIds = visibleNodes.Select(x => x.Id).ToHashSet();
        return new WorkCanvasSnapshotDto { Id = canvas.Id, Name = canvas.Name, ScopeProjectId = canvas.ScopeProjectId, NodeCount = visibleNodes.Count, Version = canvas.Version ?? 1, UpdatedAt = canvas.UpdatedAt, Viewport = Deserialize(canvas.ViewportJson), Nodes = visibleNodes.Select(x => Node(x, sessions.GetValueOrDefault(x.ChatSessionId!))).ToList(), Edges = edges.Where(x => visibleNodeIds.Contains(x.SourceNodeId) && visibleNodeIds.Contains(x.TargetNodeId)).Select(x => new WorkCanvasEdgeDto { Id = x.Id, SourceNodeId = x.SourceNodeId, TargetNodeId = x.TargetNodeId, RelationType = x.RelationType, Label = x.Label }).ToList() };
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
    private static object? Deserialize(string? json) { if (string.IsNullOrWhiteSpace(json)) return null; try { return JsonSerializer.Deserialize<object>(json); } catch { return null; } }
}
