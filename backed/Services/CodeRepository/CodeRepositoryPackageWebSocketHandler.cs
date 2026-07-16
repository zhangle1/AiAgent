using AiAgent.Backend.Dtos.CodeRepository;
using AiAgent.Backend.Services.Auth;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AiAgent.Backend.Services.CodeRepository;

public interface ICodeRepositoryPackageWebSocketHandler
{
    Task HandleClientAsync(HttpContext context);
}

/// <summary>
/// Builds a selected .NET or React repository using only repository-owned saved settings.
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
        var isNpmProject = repository.Languages.Contains("TypeScript/JavaScript", StringComparer.OrdinalIgnoreCase) || repository.Languages.Contains("React", StringComparer.OrdinalIgnoreCase);
        var npmArguments = isNpmProject ? ParseNpmBuildCommand(repository.PublishCommand) : new List<string>();
        if (!isNpmProject && !repository.BuildSystems.Contains("dotnet", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only detected .NET or front-end repositories can be packaged from this panel.");

        var target = isNpmProject ? ResolveNpmPackageTarget(repository) : repository.PublishTarget;
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("Select and save a solution or project file before packaging.");
        if (!isNpmProject && !repository.SolutionFiles.Contains(target, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The saved publish target is not one of this repository's selected solution files.");

        var rootPath = Path.GetFullPath(repository.RootPath);
        var targetPath = Path.GetFullPath(Path.Combine(rootPath, target.Replace('/', Path.DirectorySeparatorChar)));
        var outputPath = Path.GetFullPath(Path.Combine(rootPath, repository.PublishOutputPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathWithin(rootPath, targetPath) || !File.Exists(targetPath))
            throw new FileNotFoundException("The saved publish target no longer exists.");
        if (!IsPathWithin(rootPath, outputPath))
            throw new InvalidOperationException("The saved publish output path is outside the repository.");

        var startInfo = new ProcessStartInfo(isNpmProject ? ResolveNpmExecutable() : "dotnet")
        {
            WorkingDirectory = isNpmProject ? Path.GetDirectoryName(targetPath)! : rootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            CreateNoWindow = true
        };
        if (isNpmProject)
        {
            foreach (var argument in npmArguments) startInfo.ArgumentList.Add(argument);
        }
        else
        {
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
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException(isNpmProject ? "Unable to start npm. Ensure Node.js and npm are installed on the server." : "Unable to start dotnet. Ensure the .NET SDK is installed on the server.");
        await SendAsync(socket, new { type = "started", target_path = target, output_path = outputPath, message = isNpmProject ? $"npm {string.Join(' ', npmArguments)} started" : "dotnet publish started" }, cancellationToken);
        var output = PumpAsync(process.StandardOutput, socket, "stdout", cancellationToken);
        var error = PumpAsync(process.StandardError, socket, "stderr", cancellationToken);
        await Task.WhenAll(process.WaitForExitAsync(cancellationToken), output, error);
        var success = process.ExitCode == 0 && Directory.Exists(outputPath);
        string? archiveName = null;
        if (success)
        {
            archiveName = CreatePackageArchive(repository.Name, outputPath);
        }
        await SendAsync(socket, new { type = "completed", success, exit_code = process.ExitCode, target_path = target, output_path = outputPath, archive_name = archiveName, message = success ? "Package completed." : "Package failed. Review the terminal output." }, cancellationToken);
    }

    private static string ResolveNpmPackageTarget(CodeRepositoryDto repository)
    {
        if (!string.IsNullOrWhiteSpace(repository.PublishTarget)
            && repository.ConfigurationFiles.Contains(repository.PublishTarget, StringComparer.OrdinalIgnoreCase)
            && Path.GetFileName(repository.PublishTarget).Equals("package.json", StringComparison.OrdinalIgnoreCase))
            return repository.PublishTarget;

        var packageFiles = repository.ConfigurationFiles
            .Where(path => Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return packageFiles.Count == 1
            ? packageFiles[0]
            : throw new InvalidOperationException("Select and save exactly one package.json before packaging a front-end repository.");
    }

    private static List<string> ParseNpmBuildCommand(string? command)
    {
        var parts = (string.IsNullOrWhiteSpace(command) ? "npm run build" : command)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || !parts[0].Equals("npm", StringComparison.OrdinalIgnoreCase) || !parts[1].Equals("run", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Front-end build command must use the format: npm run <script>.");
        if (parts.Length > 20 || parts.Any(part => part.Length > 128 || part.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or ':' or '.' or '/' or '='))))
            throw new InvalidOperationException("Front-end build command contains unsupported characters.");
        return parts.Skip(1).ToList();
    }

    private static string ResolveNpmExecutable()
    {
        if (!OperatingSystem.IsWindows()) return "npm";

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pathEntry in pathEntries)
        {
            var directory = pathEntry.Trim('"');
            if (!Path.IsPathFullyQualified(directory)) continue;
            var npmPath = Path.Combine(directory, "npm.cmd");
            if (File.Exists(npmPath)) return npmPath;
        }

        throw new InvalidOperationException("Unable to locate the system npm.cmd. Ensure Node.js is installed and its directory is included in the server PATH.");
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

    private static string CreatePackageArchive(string repositoryName, string outputPath)
    {
        var archiveDirectory = Path.Combine(AppContext.BaseDirectory, "App_Data", "code-packages", repositoryName);
        Directory.CreateDirectory(archiveDirectory);
        var archiveName = $"{repositoryName}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.zip";
        var archivePath = Path.Combine(archiveDirectory, archiveName);
        ZipFile.CreateFromDirectory(outputPath, archivePath, CompressionLevel.Fastest, false);
        return archiveName;
    }

    private static Task SendAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
        => socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)), WebSocketMessageType.Text, true, cancellationToken);
}
