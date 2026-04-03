using System.Text;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Platform;

public sealed class DefaultPlatformLauncher : IPlatformLauncher
{
    private readonly IProcessRunner _processRunner;
    private readonly PlatformConfigurationLoader _configurationLoader;

    public DefaultPlatformLauncher(IProcessRunner processRunner, PlatformConfigurationLoader configurationLoader)
    {
        _processRunner = processRunner;
        _configurationLoader = configurationLoader;
    }

    public async Task<PlatformLaunchResult> OpenPathAsync(PlatformOpenRequest request, CancellationToken cancellationToken)
    {
        var configuration = await _configurationLoader.LoadAsync(cancellationToken);
        var fullPath = Path.IsPathRooted(request.Path)
            ? request.Path
            : Path.GetFullPath(request.Path, request.WorkingDirectory ?? Environment.CurrentDirectory);
        var location = BuildLocation(fullPath, request.Line, request.Column);
        var attempts = BuildOpenPathAttempts(configuration, fullPath, location, request.Reveal);
        return await ExecuteAttemptsAsync(attempts, $"Opened {fullPath}.", cancellationToken);
    }

    public async Task<PlatformLaunchResult> OpenUrlAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return new PlatformLaunchResult(false, string.Empty, $"Invalid URL '{url}'.");
        }

        var configuration = await _configurationLoader.LoadAsync(cancellationToken);
        var attempts = BuildOpenUrlAttempts(configuration, url);
        return await ExecuteAttemptsAsync(attempts, $"Opened {url}.", cancellationToken);
    }

    private async Task<PlatformLaunchResult> ExecuteAttemptsAsync(
        IReadOnlyList<ProcessRequest> attempts,
        string successMessage,
        CancellationToken cancellationToken)
    {
        string? lastError = null;
        foreach (var attempt in attempts)
        {
            try
            {
                var result = await _processRunner.RunAsync(attempt, cancellationToken);
                if (result.ExitCode == 0)
                {
                    return new PlatformLaunchResult(true, successMessage);
                }

                lastError = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        return new PlatformLaunchResult(false, string.Empty, lastError ?? "No launch strategy succeeded.");
    }

    private static IReadOnlyList<ProcessRequest> BuildOpenPathAttempts(
        PlatformConfiguration configuration,
        string fullPath,
        string location,
        bool reveal)
    {
        var attempts = new List<ProcessRequest>();

        if (reveal && !string.IsNullOrWhiteSpace(configuration.RevealCommand))
        {
            attempts.Add(new ProcessRequest(
                configuration.RevealCommand!,
                BuildArguments([.. configuration.RevealArguments ?? [], fullPath])));
        }

        if (!string.IsNullOrWhiteSpace(configuration.EditorCommand))
        {
            attempts.Add(new ProcessRequest(
                configuration.EditorCommand!,
                BuildArguments([.. configuration.EditorArguments ?? [], location])));
        }

        AddEnvironmentCommandAttempt(attempts, Environment.GetEnvironmentVariable("VISUAL"), location);
        AddEnvironmentCommandAttempt(attempts, Environment.GetEnvironmentVariable("EDITOR"), location);

        attempts.Add(new ProcessRequest("code", BuildArguments(["-g", location])));
        attempts.Add(BuildDefaultOpenRequest(fullPath));
        return Deduplicate(attempts);
    }

    private static IReadOnlyList<ProcessRequest> BuildOpenUrlAttempts(PlatformConfiguration configuration, string url)
    {
        var attempts = new List<ProcessRequest>();
        if (!string.IsNullOrWhiteSpace(configuration.BrowserCommand))
        {
            attempts.Add(new ProcessRequest(
                configuration.BrowserCommand!,
                BuildArguments([.. configuration.BrowserArguments ?? [], url])));
        }

        attempts.Add(BuildDefaultOpenRequest(url));
        return Deduplicate(attempts);
    }

    private static ProcessRequest BuildDefaultOpenRequest(string target)
    {
        if (OperatingSystem.IsMacOS())
        {
            return new ProcessRequest("open", BuildArguments([target]));
        }

        if (OperatingSystem.IsWindows())
        {
            return new ProcessRequest("cmd", $"/c start \"\" {Quote(target)}");
        }

        return new ProcessRequest("xdg-open", BuildArguments([target]));
    }

    private static void AddEnvironmentCommandAttempt(List<ProcessRequest> attempts, string? commandLine, string target)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return;
        }

        var tokens = SplitCommandLine(commandLine);
        if (tokens.Count == 0)
        {
            return;
        }

        attempts.Add(new ProcessRequest(tokens[0], BuildArguments([.. tokens.Skip(1), target])));
    }

    private static IReadOnlyList<ProcessRequest> Deduplicate(IEnumerable<ProcessRequest> attempts)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = new List<ProcessRequest>();
        foreach (var attempt in attempts)
        {
            var key = $"{attempt.FileName}\u001f{attempt.Arguments}";
            if (seen.Add(key))
            {
                distinct.Add(attempt);
            }
        }

        return distinct;
    }

    private static string BuildLocation(string path, int? line, int? column)
    {
        if (line is null || line <= 0)
        {
            return path;
        }

        return column is null || column <= 0
            ? $"{path}:{line.Value}"
            : $"{path}:{line.Value}:{column.Value}";
    }

    private static string BuildArguments(IEnumerable<string> args)
        => string.Join(' ', args.Select(Quote));

    private static string Quote(string value)
        => value.Contains(' ', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

    private static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    tokens.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(ch);
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }
}
