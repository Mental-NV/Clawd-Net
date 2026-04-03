using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Plugins;

public sealed class PluginRuntime : IPluginRuntime
{
    private readonly IPluginCatalog _pluginCatalog;
    private readonly IProcessRunner _processRunner;
    private readonly HashSet<string> _reservedCommands;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
    private readonly Dictionary<string, PluginHealthMetrics> _healthMetrics = new(StringComparer.OrdinalIgnoreCase);

    public PluginRuntime(
        IPluginCatalog pluginCatalog,
        IProcessRunner processRunner,
        IEnumerable<string> reservedCommands)
    {
        _pluginCatalog = pluginCatalog;
        _processRunner = processRunner;
        _reservedCommands = new HashSet<string>(reservedCommands, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets health metrics for a specific plugin.
    /// </summary>
    public PluginHealthMetrics GetHealthMetrics(string pluginName)
    {
        return _healthMetrics.GetValueOrDefault(pluginName) ?? new PluginHealthMetrics();
    }

    /// <summary>
    /// Gets health metrics for all loaded plugins.
    /// </summary>
    public IReadOnlyDictionary<string, PluginHealthMetrics> GetAllHealthMetrics()
    {
        return new Dictionary<string, PluginHealthMetrics>(_healthMetrics, StringComparer.OrdinalIgnoreCase);
    }

    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task<PluginCommandResult?> TryExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        await _pluginCatalog.ReloadAsync(cancellationToken);
        if (request.Arguments.Count == 0)
        {
            return null;
        }

        var name = request.Arguments[0];
        if (_reservedCommands.Contains(name))
        {
            return null;
        }

        var plugin = _pluginCatalog.Plugins
            .Where(candidate => candidate.Enabled && candidate.IsValid)
            .FirstOrDefault(candidate => candidate.Commands.Any(command =>
                command.Enabled && string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase)));
        if (plugin is null)
        {
            return null;
        }

        var command = plugin.Commands.First(candidate =>
            candidate.Enabled && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        var invocation = new PluginCommandInvocation(plugin, command, request.Arguments.Skip(1).ToArray(), Environment.CurrentDirectory);
        return await ExecuteCommandAsync(invocation, cancellationToken);
    }

    public async Task<IReadOnlyList<PluginHookResult>> InvokeHooksAsync(PluginHookInvocation invocation, CancellationToken cancellationToken)
    {
        await _pluginCatalog.ReloadAsync(cancellationToken);
        var results = new List<PluginHookResult>();
        foreach (var plugin in _pluginCatalog.Plugins.Where(plugin => plugin.Enabled && plugin.IsValid))
        {
            foreach (var hook in plugin.Hooks.Where(hook => hook.Enabled && hook.Kind == invocation.Kind))
            {
                results.Add(await ExecuteHookAsync(plugin, hook, invocation, cancellationToken));
            }
        }

        return results;
    }

    public async Task<ToolExecutionResult> ExecuteToolAsync(PluginToolInvocation invocation, CancellationToken cancellationToken)
    {
        // Track tool invocation
        TrackToolInvocation(invocation.Plugin.Name);

        var payload = JsonSerializer.Serialize(new
        {
            plugin = new { id = invocation.Plugin.Id, name = invocation.Plugin.Name, path = invocation.Plugin.Path },
            invocation = "tool",
            tool = new
            {
                name = invocation.Tool.Name,
                qualifiedName = invocation.QualifiedToolName,
                category = invocation.Tool.Category.ToString()
            },
            cwd = invocation.WorkingDirectory,
            sessionId = invocation.SessionId,
            taskId = invocation.TaskId,
            rawInput = invocation.RawInput,
            input = invocation.Input
        }, _jsonOptions);
        var result = await _processRunner.RunAsync(
            new ProcessRequest(
                invocation.Tool.Command,
                string.Join(' ', invocation.Tool.Arguments),
                invocation.WorkingDirectory ?? invocation.Plugin.Path,
                invocation.Tool.Environment,
                payload),
            cancellationToken);
        return ParseToolResult(result);
    }

    private async Task<PluginCommandResult> ExecuteCommandAsync(PluginCommandInvocation invocation, CancellationToken cancellationToken)
    {
        // Track command invocation
        TrackCommandInvocation(invocation.Plugin.Name);

        var payload = JsonSerializer.Serialize(new
        {
            plugin = new { id = invocation.Plugin.Id, name = invocation.Plugin.Name, path = invocation.Plugin.Path },
            invocation = "command",
            command = invocation.Command.Name,
            arguments = invocation.Arguments,
            cwd = invocation.WorkingDirectory
        }, _jsonOptions);
        var result = await _processRunner.RunAsync(
            new ProcessRequest(
                invocation.Command.Command,
                string.Join(' ', invocation.Command.Arguments),
                invocation.WorkingDirectory ?? invocation.Plugin.Path,
                invocation.Command.Environment,
                payload),
            cancellationToken);
        return ParseCommandResult(result);
    }

    private async Task<PluginHookResult> ExecuteHookAsync(
        PluginDefinition plugin,
        PluginHookDefinition hook,
        PluginHookInvocation invocation,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            plugin = new { id = plugin.Id, name = plugin.Name, path = plugin.Path },
            invocation = "hook",
            kind = hook.Kind.ToString(),
            sessionId = invocation.SessionId,
            taskId = invocation.TaskId,
            cwd = invocation.WorkingDirectory,
            payload = invocation.Payload
        }, _jsonOptions);
        var result = await _processRunner.RunAsync(
            new ProcessRequest(
                hook.Command,
                string.Join(' ', hook.Arguments),
                invocation.WorkingDirectory ?? plugin.Path,
                hook.Environment,
                payload),
            cancellationToken);
        var hookResult = ParseHookResult(plugin, hook, result);

        // Track hook invocation
        TrackHookInvocation(plugin.Name, hookResult.Success);

        return hookResult;
    }

    private void TrackToolInvocation(string pluginName)
    {
        var current = _healthMetrics.GetValueOrDefault(pluginName) ?? new PluginHealthMetrics();
        _healthMetrics[pluginName] = current.WithToolInvocation();
    }

    private void TrackCommandInvocation(string pluginName)
    {
        var current = _healthMetrics.GetValueOrDefault(pluginName) ?? new PluginHealthMetrics();
        _healthMetrics[pluginName] = current.WithCommandInvocation();
    }

    private void TrackHookInvocation(string pluginName, bool succeeded)
    {
        var current = _healthMetrics.GetValueOrDefault(pluginName) ?? new PluginHealthMetrics();
        _healthMetrics[pluginName] = current.WithHookInvocation(succeeded);
    }

    private static PluginCommandResult ParseCommandResult(ProcessResult result)
    {
        if (TryParseStructuredResult(result.StdOut, out var stdout, out var stderr, out var exitCode, out _))
        {
            return new PluginCommandResult(
                (exitCode ?? result.ExitCode) == 0,
                stdout ?? string.Empty,
                string.IsNullOrWhiteSpace(stderr) ? result.StdErr : stderr,
                exitCode ?? result.ExitCode);
        }

        return new PluginCommandResult(result.ExitCode == 0, result.StdOut, result.StdErr, result.ExitCode);
    }

    private static PluginHookResult ParseHookResult(PluginDefinition plugin, PluginHookDefinition hook, ProcessResult result)
    {
        if (TryParseStructuredResult(result.StdOut, out _, out var stderr, out var exitCode, out var message))
        {
            var effectiveExitCode = exitCode ?? result.ExitCode;
            var effectiveMessage = string.IsNullOrWhiteSpace(message)
                ? string.IsNullOrWhiteSpace(stderr) ? result.StdOut : stderr!
                : message!;
            return new PluginHookResult(plugin, hook, effectiveExitCode == 0, effectiveMessage, hook.Blocking, effectiveExitCode);
        }

        var fallbackMessage = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return new PluginHookResult(plugin, hook, result.ExitCode == 0, fallbackMessage, hook.Blocking, result.ExitCode);
    }

    private static ToolExecutionResult ParseToolResult(ProcessResult result)
    {
        if (TryParseStructuredToolResult(result.StdOut, out var success, out var output, out var error, out var exitCode))
        {
            var effectiveSuccess = success ?? ((exitCode ?? result.ExitCode) == 0);
            return new ToolExecutionResult(
                effectiveSuccess,
                output ?? string.Empty,
                effectiveSuccess ? null : (string.IsNullOrWhiteSpace(error) ? result.StdErr : error));
        }

        if (result.ExitCode == 0)
        {
            return new ToolExecutionResult(true, result.StdOut);
        }

        var fallbackError = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return new ToolExecutionResult(false, string.Empty, fallbackError);
    }

    private static bool TryParseStructuredResult(
        string raw,
        out string? stdout,
        out string? stderr,
        out int? exitCode,
        out string? message)
    {
        stdout = null;
        stderr = null;
        exitCode = null;
        message = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("stdout", out var stdoutElement))
            {
                stdout = stdoutElement.GetString();
            }

            if (document.RootElement.TryGetProperty("stderr", out var stderrElement))
            {
                stderr = stderrElement.GetString();
            }

            if (document.RootElement.TryGetProperty("exitCode", out var exitCodeElement) && exitCodeElement.TryGetInt32(out var parsedExitCode))
            {
                exitCode = parsedExitCode;
            }

            if (document.RootElement.TryGetProperty("message", out var messageElement))
            {
                message = messageElement.GetString();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseStructuredToolResult(
        string raw,
        out bool? success,
        out string? output,
        out string? error,
        out int? exitCode)
    {
        success = null;
        output = null;
        error = null;
        exitCode = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("success", out var successElement)
                && (successElement.ValueKind is JsonValueKind.True or JsonValueKind.False))
            {
                success = successElement.GetBoolean();
            }

            if (document.RootElement.TryGetProperty("output", out var outputElement))
            {
                output = outputElement.GetString();
            }

            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                error = errorElement.GetString();
            }

            if (document.RootElement.TryGetProperty("exitCode", out var exitCodeElement) && exitCodeElement.TryGetInt32(out var parsedExitCode))
            {
                exitCode = parsedExitCode;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
