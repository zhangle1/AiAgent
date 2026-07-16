using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Services.Auth;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.CodeRepository;

public interface ICodeRepositoryPackageWebSocketHandler
{
    Task HandleClientAsync(HttpContext context);
}

/// <summary>
/// Publishes a selected .NET solution or project using only repository-owned saved settings.
/// Output is streamed to the browser so the operator can observe the server process.
/// </summary>
public sealed class CodeRepositoryPackageWebSocketHandler : ICodeRepositoryPackageWebSocketHandler
{
    private readonly ICodeRepositoryManager _manager;
    private readonly IAuthService _authService;
    private readonly ILogger<CodeRepositoryPackageWebSocketHandler> _logger;

    public CodeRepositoryPackageWebSocketHandler(ICodeRepositoryManager manager, IAuthService authService, ILogger<CodeRepositoryPackageWebSocketHandler> logger)
    {
        _manager = manager;
        _authService = authService;
        _logger = logger;
    }

    public async Task HandleClientAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (await _authService.TryGetCurrentUserAsync(context, context.RequestAborted) is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        try
        {
            await SendAsync(socket, new { type = "connected", message = "Package terminal connected." }, context.RequestAborted);
            var request = await ReceiveRequestAsync(socket, context.RequestAborted);
            await PackageAsync(socket, request, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Code repository package request failed.");
            await SendAsync(socket, new { type = "completed", success = false, message = ex.Message }, CancellationToken.None);
        }
        finally
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "completed", CancellationToken.None);
        }
    }

    private async Task PackageAsync(WebSocket socket, CodeRepositoryPackageRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RepositoryName))
            throw new InvalidOperationException("Select a repository before packaging.");

        var repository = _manager.Get(request.RepositoryName);
        if (!repository.BuildSystems.Contains("dotnet", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only detected .NET repositories can be packaged from this panel.");
        if (string.IsNullOrWhiteSpace(repository.PublishTarget))
            throw new InvalidOperationException("Select and save a solution or project file before packaging.");
        if (!repository.SolutionFiles.Contains(repository.PublishTarget, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The saved publish target is not one of this repository's selected solution files.");

        var rootPath = Path.GetFullPath(repository.RootPath);
        var targetPath = Path.GetFullPath(Path.Combine(rootPath, repository.PublishTarget.Replace('/', Path.DirectorySeparatorChar)));
        var outputPath = Path.GetFullPath(Path.Combine(rootPath, repository.PublishOutputPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathWithin(rootPath, targetPath) || !File.Exists(targetPath))
            throw new FileNotFoundException("The saved publish target no longer exists.");
        if (!IsPathWithin(rootPath, outputPath))
            throw new InvalidOperationException("The saved publish output path is outside the repository.");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = rootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(targetPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(repository.PublishConfiguration);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        if (!string.IsNullOrWhiteSpace(repository.PublishRuntime))
        {
            startInfo.ArgumentList.Add("--runtime");
            startInfo.ArgumentList.Add(repository.PublishRuntime);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Unable to start dotnet. Ensure the .NET SDK is installed on the server.");
        await SendAsync(socket, new { type = "started", target_path = repository.PublishTarget, output_path = outputPath, message = "dotnet publish started" }, cancellationToken);
        var output = PumpAsync(process.StandardOutput, socket, "stdout", cancellationToken);
        var error = PumpAsync(process.StandardError, socket, "stderr", cancellationToken);
        await Task.WhenAll(process.WaitForExitAsync(cancellationToken), output, error);
        var success = process.ExitCode == 0 && Directory.Exists(outputPath);
        await SendAsync(socket, new { type = "completed", success, exit_code = process.ExitCode, target_path = repository.PublishTarget, output_path = outputPath, message = success ? "Package completed." : "Package failed. Review the terminal output." }, cancellationToken);
    }

    private static async Task<CodeRepositoryPackageRequest> ReceiveRequestAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var message = await socket.ReceiveAsync(buffer, cancellationToken);
        if (message.MessageType != WebSocketMessageType.Text)
            throw new InvalidOperationException("A package request message is required.");
        return JsonSerializer.Deserialize<CodeRepositoryPackageRequest>(Encoding.UTF8.GetString(buffer, 0, message.Count), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The package request is invalid.");
    }

    private static async Task PumpAsync(StreamReader reader, WebSocket socket, string stream, CancellationToken cancellationToken)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            await SendAsync(socket, new { type = "output", stream, line }, cancellationToken);
    }

    private static bool IsPathWithin(string parentPath, string childPath)
    {
        var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var child = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static Task SendAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
        => socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)), WebSocketMessageType.Text, true, cancellationToken);
}
