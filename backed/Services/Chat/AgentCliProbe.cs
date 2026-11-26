using System.Diagnostics;

namespace AiAgent.Backend.Services.Chat;

internal static class AgentCliProbe
{
    internal static async Task<(bool Installed, string Command, string? Version)> ProbeAsync(IEnumerable<string> candidates, CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new ProcessStartInfo { FileName = candidate, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                info.ArgumentList.Add("--version");
                using var process = Process.Start(info);
                if (process is null) continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, errors);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    throw;
                }
                if (process.ExitCode != 0) continue;
                var version = (await output).Trim();
                if (version.Length == 0) version = (await errors).Trim();
                return (true, candidate, version.Length == 0 ? null : version[..Math.Min(version.Length, 180)]);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
        }
        return (false, candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty, null);
    }
}
