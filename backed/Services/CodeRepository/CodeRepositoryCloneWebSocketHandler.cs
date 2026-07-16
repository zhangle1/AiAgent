using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Entities.Git;
using AiAgent.Backend.Services.Auth;
using Microsoft.AspNetCore.DataProtection;
using SqlSugar;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.CodeRepository;

public interface ICodeRepositoryCloneWebSocketHandler
{
    Task HandleClientAsync(HttpContext context);
}

/// <summary>
/// Runs a single, authenticated git clone and streams sanitized process output to its websocket client.
/// </summary>
public sealed class CodeRepositoryCloneWebSocketHandler : ICodeRepositoryCloneWebSocketHandler
{
    private readonly ICodeRepositoryManager _manager;
    private readonly ISqlSugarClient _db;
    private readonly IAuthService _authService;
    private readonly IDataProtector _protector;
    private readonly ILogger<CodeRepositoryCloneWebSocketHandler> _logger;

    public CodeRepositoryCloneWebSocketHandler(ICodeRepositoryManager manager, ISqlSugarClient db, IAuthService authService,
        IDataProtectionProvider dataProtectionProvider, ILogger<CodeRepositoryCloneWebSocketHandler> logger)
    {
        _manager = manager;
        _db = db;
        _authService = authService;
        _protector = dataProtectionProvider.CreateProtector("AiAgent.GitAccounts.AccessToken.v1");
        _logger = logger;
    }

    public async Task HandleClientAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var user = await _authService.TryGetCurrentUserAsync(context, context.RequestAborted);
        if (user is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        try
        {
            await SendAsync(socket, new { type = "connected", message = "Clone terminal connected." }, context.RequestAborted);
            var request = await ReceiveRequestAsync(socket, context.RequestAborted);
            await CloneAsync(socket, user, request, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Git clone request failed.");
            await SendAsync(socket, new { type = "completed", success = false, message = ex.Message }, CancellationToken.None);
        }
        finally
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "completed", CancellationToken.None);
        }
    }

    private async Task CloneAsync(WebSocket socket, AuthenticatedUser user, CodeRepositoryCloneRequest request, CancellationToken cancellationToken)
    {
        var repositoryUrl = ValidateRepositoryUrl(request.RepositoryUrl);
        var destinationParent = _manager.Browse(request.DestinationParentPath).Path;
        var directoryName = GetDirectoryName(repositoryUrl);
        var destination = Path.GetFullPath(Path.Combine(destinationParent, directoryName));
        if (!destination.StartsWith(destinationParent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Clone destination must stay inside the selected folder.");
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new InvalidOperationException($"The destination already exists: {destination}");

        var account = _db.Queryable<AiGitAccount>().First(x => x.Id == request.GitAccountId && x.UserId == user.Id && !x.IsDeleted);
        if (account is null) throw new InvalidOperationException("The selected Git account was not found.");
        if (string.IsNullOrWhiteSpace(account.AccessTokenProtected)) throw new InvalidOperationException("The selected Git account does not have an access token.");
        if (!string.Equals(account.Provider, string.Equals(repositoryUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase) ? "github" : "gitee", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select a Git account for the same provider as the repository URL.");

        string token;
        try { token = _protector.Unprotect(account.AccessTokenProtected); }
        catch { throw new InvalidOperationException("The selected Git account token cannot be read. Save the account again."); }

        var askPassPath = Path.Combine(Path.GetTempPath(), $"aiagent-git-askpass-{Guid.NewGuid():N}.cmd");
        await File.WriteAllTextAsync(askPassPath, "@echo off\r\necho %~1 | findstr /b /i \"Username\" >nul && (echo %GIT_CLONE_USERNAME%) || (echo %GIT_CLONE_TOKEN%)\r\n", cancellationToken);
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = destinationParent,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("clone");
            startInfo.ArgumentList.Add("--progress");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(repositoryUrl.ToString());
            startInfo.ArgumentList.Add(destination);
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GIT_ASKPASS"] = askPassPath;
            startInfo.Environment["GIT_CLONE_USERNAME"] = account.Username;
            startInfo.Environment["GIT_CLONE_TOKEN"] = token;

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) throw new InvalidOperationException("Unable to start the git command. Ensure Git is installed on the server.");
            await SendAsync(socket, new { type = "started", destination_path = destination, message = "git clone started" }, cancellationToken);
            var output = PumpAsync(process.StandardOutput, socket, "stdout", cancellationToken);
            var error = PumpAsync(process.StandardError, socket, "stderr", cancellationToken);
            await Task.WhenAll(process.WaitForExitAsync(cancellationToken), output, error);
            var success = process.ExitCode == 0 && Directory.Exists(destination);
            await SendAsync(socket, new { type = "completed", success, exit_code = process.ExitCode, destination_path = destination, message = success ? "Clone completed successfully." : "Git clone failed. Review the terminal output." }, cancellationToken);
        }
        finally
        {
            try { File.Delete(askPassPath); } catch { }
        }
    }

    private static async Task PumpAsync(StreamReader reader, WebSocket socket, string stream, CancellationToken cancellationToken)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            await SendAsync(socket, new { type = "output", stream, line }, cancellationToken);
    }

    private static async Task<CodeRepositoryCloneRequest> ReceiveRequestAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var message = await socket.ReceiveAsync(buffer, cancellationToken);
        if (message.MessageType != WebSocketMessageType.Text) throw new InvalidOperationException("A clone request message is required.");
        return JsonSerializer.Deserialize<CodeRepositoryCloneRequest>(Encoding.UTF8.GetString(buffer, 0, message.Count), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The clone request is invalid.");
    }

    private static Uri ValidateRepositoryUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Use an HTTPS GitHub or Gitee repository URL.");
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) && !string.Equals(uri.Host, "gitee.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only GitHub and Gitee HTTPS repository URLs are supported.");
        return uri;
    }

    private static string GetDirectoryName(Uri url)
    {
        var name = Path.GetFileName(url.AbsolutePath.TrimEnd('/'));
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
            throw new InvalidOperationException("The repository URL does not contain a valid folder name.");
        return name;
    }

    private static Task SendAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
        => socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)), WebSocketMessageType.Text, true, cancellationToken);
}
