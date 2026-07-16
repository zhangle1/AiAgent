using AiAgent.Backend.Dtos.Knowledge;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.Knowledge;

public interface IKnowledgeProgressHub
{
    Task HandleClientAsync(HttpContext context);

    Task PublishAsync(string kbName, KnowledgeJobDto? job, string eventType = "progress", CancellationToken cancellationToken = default);

    object GetDiagnostics();
}

public sealed class KnowledgeProgressHub : IKnowledgeProgressHub
{
    private sealed class Client
    {
        public string Id { get; init; }

        public string KbName { get; init; }

        public WebSocket Socket { get; init; }

        public SemaphoreSlim SendLock { get; } = new(1, 1);

        public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, Client> _clients = new();
    private readonly ConcurrentQueue<object> _recentEvents = new();
    private readonly ILogger<KnowledgeProgressHub> _logger;

    public KnowledgeProgressHub(ILogger<KnowledgeProgressHub> logger)
    {
        _logger = logger;
    }

    public async Task HandleClientAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var kbName = (context.Request.Query["kbName"].ToString() ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(kbName))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("kbName is required.");
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var client = new Client
        {
            Id = Guid.NewGuid().ToString("N"),
            KbName = kbName,
            Socket = socket
        };
        _clients[client.Id] = client;

        await SendAsync(client, new
        {
            type = "connected",
            kb_name = kbName,
            connected_at = DateTime.UtcNow
        }, context.RequestAborted);

        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogDebug(ex, "Knowledge progress websocket disconnected. ClientId={ClientId}", client.Id);
        }
        finally
        {
            _clients.TryRemove(client.Id, out _);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
            }
        }
    }

    public async Task PublishAsync(string kbName, KnowledgeJobDto? job, string eventType = "progress", CancellationToken cancellationToken = default)
    {
        var normalizedName = kbName.Trim().ToLowerInvariant();
        var payload = new
        {
            type = eventType,
            kb_name = normalizedName,
            job,
            sent_at = DateTime.UtcNow
        };

        _recentEvents.Enqueue(payload);
        while (_recentEvents.Count > 50 && _recentEvents.TryDequeue(out _))
        {
        }

        var sends = _clients.Values
            .Where(x => x.KbName == normalizedName && x.Socket.State == WebSocketState.Open)
            .Select(x => SendAsync(x, payload, cancellationToken));
        await Task.WhenAll(sends);
    }

    public object GetDiagnostics()
    {
        var clients = _clients.Values
            .GroupBy(x => x.KbName)
            .Select(x => new
            {
                kb_name = x.Key,
                connections = x.Count(),
                oldest_connected_at = x.Min(y => y.ConnectedAt)
            })
            .OrderBy(x => x.kb_name)
            .ToList();

        return new
        {
            websocket_path = "/api/v1/knowledge/ws?kbName={kbName}",
            total_connections = _clients.Count,
            clients,
            recent_events = _recentEvents.ToArray()
        };
    }

    private static async Task SendAsync(Client client, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await client.SendLock.WaitAsync(cancellationToken);
        try
        {
            if (client.Socket.State == WebSocketState.Open)
            {
                await client.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        finally
        {
            client.SendLock.Release();
        }
    }
}