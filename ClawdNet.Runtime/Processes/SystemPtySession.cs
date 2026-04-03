using System.Diagnostics;
using System.Threading.Channels;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Processes;

public sealed class SystemPtySession : IPtySession
{
    private const int MaxOutputChars = 4096;

    private static readonly string[] s_shellCandidates =
    {
        "/bin/zsh",
        "/bin/bash",
        "/bin/sh",
        "/usr/bin/zsh",
        "/usr/bin/bash",
        "/usr/bin/sh"
    };

    private readonly Process _process;
    private readonly Channel<PtyOutputChunk> _outputChannel = Channel.CreateUnbounded<PtyOutputChunk>();
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly string _sessionId;
    private readonly string _command;
    private readonly string _workingDirectory;
    private readonly DateTimeOffset _startedAtUtc;
    private string _recentOutput = string.Empty;
    private bool _isOutputClipped;
    private bool _disposeRequested;
    private int? _exitCode;
    private DateTimeOffset _updatedAtUtc;

    private SystemPtySession(Process process, string sessionId, string command, string workingDirectory)
    {
        _process = process;
        _sessionId = sessionId;
        _command = command;
        _workingDirectory = workingDirectory;
        _startedAtUtc = DateTimeOffset.UtcNow;
        _updatedAtUtc = _startedAtUtc;
    }

    public event Action<PtySessionState>? StateChanged;

    public PtySessionState Snapshot => new(
        _sessionId,
        _command,
        _workingDirectory,
        _startedAtUtc,
        _updatedAtUtc,
        !_process.HasExited,
        _exitCode,
        _recentOutput,
        _isOutputClipped);

    private static string ResolveShell()
    {
        foreach (var candidate in s_shellCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "No suitable shell found for PTY. Expected one of: " + string.Join(", ", s_shellCandidates));
    }

    public static Task<SystemPtySession> StartAsync(string command, string? workingDirectory, CancellationToken cancellationToken)
    {
        var cwd = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory!;
        var shell = ResolveShell();
        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = $"-lc \"exec {command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start PTY command '{command}'.");
        }

        var session = new SystemPtySession(process, Guid.NewGuid().ToString("N"), command, cwd);
        session.BeginMonitoring(cancellationToken);
        return Task.FromResult(session);
    }

    public async IAsyncEnumerable<PtyOutputChunk> GetOutputAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await _outputChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_outputChannel.Reader.TryRead(out var chunk))
            {
                yield return chunk;
            }
        }
    }

    public async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException("No active PTY session is running.");
        }

        await _process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken);
        await _process.StandardInput.FlushAsync(cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_process.HasExited)
        {
            return;
        }

        try
        {
            _process.StandardInput.Close();
        }
        catch
        {
            // Ignore close failures; termination fallback below handles stubborn processes.
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await _process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await TerminateAsync(cancellationToken);
        }
    }

    public Task TerminateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposeRequested)
        {
            return;
        }

        _disposeRequested = true;
        await CloseAsync(CancellationToken.None);
        _outputChannel.Writer.TryComplete();
        _process.Dispose();
        _sync.Dispose();
    }

    private void BeginMonitoring(CancellationToken cancellationToken)
    {
        _ = MonitorReaderAsync(_process.StandardOutput, isError: false, cancellationToken);
        _ = MonitorReaderAsync(_process.StandardError, isError: true, cancellationToken);
        _ = MonitorExitAsync();
    }

    private async Task MonitorReaderAsync(StreamReader reader, bool isError, CancellationToken cancellationToken)
    {
        var buffer = new char[256];
        while (!_disposeRequested)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            var text = new string(buffer, 0, read);
            await AppendOutputAsync(new PtyOutputChunk(text, isError, DateTimeOffset.UtcNow));
        }
    }

    private async Task MonitorExitAsync()
    {
        await _process.WaitForExitAsync();
        await _sync.WaitAsync();
        try
        {
            _exitCode = _process.ExitCode;
            _updatedAtUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _sync.Release();
        }

        NotifyStateChanged();
        _outputChannel.Writer.TryComplete();
    }

    private async Task AppendOutputAsync(PtyOutputChunk chunk)
    {
        await _sync.WaitAsync();
        try
        {
            _recentOutput += chunk.Text;
            if (_recentOutput.Length > MaxOutputChars)
            {
                _recentOutput = _recentOutput[^MaxOutputChars..];
                _isOutputClipped = true;
            }

            _updatedAtUtc = chunk.TimestampUtc;
        }
        finally
        {
            _sync.Release();
        }

        await _outputChannel.Writer.WriteAsync(chunk);
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(Snapshot);
    }
}
