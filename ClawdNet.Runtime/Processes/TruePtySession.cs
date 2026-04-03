using System.Threading.Channels;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;
using Porta.Pty;

namespace ClawdNet.Runtime.Processes;

/// <summary>
/// PTY session using a true pseudo-terminal device via Porta.Pty.
/// Supports interactive programs that require a real TTY (vim, top, ssh, etc.).
/// Falls back to pipe-based mode if PTY allocation fails.
/// </summary>
public sealed class TruePtySession : IPtySession
{
    private const int MaxOutputChars = 4096;
    private const int DefaultTranscriptTailCount = 100;
    private const int DefaultCols = 120;
    private const int DefaultRows = 30;

    private static readonly string[] s_shellCandidates =
    {
        "/bin/zsh",
        "/bin/bash",
        "/bin/sh",
        "/usr/bin/zsh",
        "/usr/bin/bash",
        "/usr/bin/sh"
    };

    private readonly IPtyConnection _connection;
    private readonly Channel<PtyOutputChunk> _outputChannel = Channel.CreateUnbounded<PtyOutputChunk>();
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly IPtyTranscriptStore _transcriptStore;
    private readonly string _sessionId;
    private readonly string _command;
    private readonly string _workingDirectory;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly TimeSpan? _timeout;
    private readonly bool _isBackground;
    private readonly int _cols;
    private readonly int _rows;
    private string _recentOutput = string.Empty;
    private bool _isOutputClipped;
    private bool _disposeRequested;
    private int? _exitCode;
    private DateTimeOffset _updatedAtUtc;
    private DateTimeOffset? _completedAtUtc;
    private int _transcriptSequenceNumber;
    private int _outputLineCount;
    private CancellationTokenSource? _timeoutCancellation;
    private readonly Task _readTask;

    private TruePtySession(
        IPtyConnection connection,
        string sessionId,
        string command,
        string workingDirectory,
        IPtyTranscriptStore transcriptStore,
        TimeSpan? timeout,
        bool isBackground,
        int cols,
        int rows)
    {
        _connection = connection;
        _sessionId = sessionId;
        _command = command;
        _workingDirectory = workingDirectory;
        _startedAtUtc = DateTimeOffset.UtcNow;
        _updatedAtUtc = _startedAtUtc;
        _transcriptStore = transcriptStore;
        _timeout = timeout;
        _isBackground = isBackground;
        _cols = cols;
        _rows = rows;

        _connection.ProcessExited += OnProcessExited;

        _readTask = MonitorReaderAsync(CancellationToken.None);
    }

    public event Action<PtySessionState>? StateChanged;

    public PtySessionState Snapshot => new(
        _sessionId,
        _command,
        _workingDirectory,
        _startedAtUtc,
        _updatedAtUtc,
        IsProcessRunning(),
        _exitCode,
        _recentOutput,
        _isOutputClipped,
        _timeout,
        _isBackground,
        _completedAtUtc,
        Volatile.Read(ref _outputLineCount));

    private bool IsProcessRunning()
    {
        // Porta.Pty doesn't expose HasExited directly, so we track it via exit code
        return !_exitCode.HasValue && !_disposeRequested;
    }

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

    public static async Task<TruePtySession> StartAsync(
        string command,
        string? workingDirectory,
        IPtyTranscriptStore transcriptStore,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        bool isBackground = false,
        int? cols = null,
        int? rows = null)
    {
        var cwd = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory!;

        // Try to run the command directly in the PTY
        // Porta.Pty expects App = executable, CommandLine = args array
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var app = parts[0];
        var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

        var options = new PtyOptions
        {
            Cols = cols ?? DefaultCols,
            Rows = rows ?? DefaultRows,
            Cwd = cwd,
            App = app,
            CommandLine = args
        };

        IPtyConnection connection;
        try
        {
            connection = await Porta.Pty.PtyProvider.SpawnAsync(options, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to allocate true PTY device for command '{command}'. Falling back to pipe-based mode.", ex);
        }

        var session = new TruePtySession(
            connection,
            Guid.NewGuid().ToString("N"),
            command,
            cwd,
            transcriptStore,
            timeout,
            isBackground,
            options.Cols,
            options.Rows);

        // Start timeout if configured
        if (timeout.HasValue)
        {
            session._timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = session.MonitorTimeoutAsync(session._timeoutCancellation.Token);
        }

        return session;
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
        if (_exitCode.HasValue)
        {
            throw new InvalidOperationException("No active PTY session is running.");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        await _connection.WriterStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        await _connection.WriterStream.FlushAsync(cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_exitCode.HasValue)
        {
            return;
        }

        try
        {
            // Close the write stream to signal EOF to the process
            _connection.WriterStream.Close();
        }
        catch
        {
            // Ignore close failures; termination fallback handles stubborn processes.
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            // WaitForExit is synchronous with timeout in milliseconds
            var timeoutMs = (int)TimeSpan.FromSeconds(2).TotalMilliseconds;
            await Task.Run(() => _connection.WaitForExit(timeoutMs), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout expired - force kill
            await TerminateAsync(cancellationToken);
        }

        // If still not exited after close/timeout, force kill
        if (!_exitCode.HasValue)
        {
            await TerminateAsync(cancellationToken);
        }
    }

    public Task TerminateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_exitCode.HasValue)
        {
            _connection.Kill();
            // Give the ProcessExited event a moment to fire
            Thread.Sleep(50);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resize the PTY terminal dimensions.
    /// </summary>
    public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connection.Resize(cols, rows);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<PtyTranscriptChunk>> GetTranscriptAsync(int? tailCount = null, CancellationToken cancellationToken = default)
    {
        var count = tailCount ?? DefaultTranscriptTailCount;
        return await _transcriptStore.ReadAsync(_sessionId, count, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposeRequested)
        {
            return;
        }

        _disposeRequested = true;
        _timeoutCancellation?.Cancel();
        await CloseAsync(CancellationToken.None);
        _outputChannel.Writer.TryComplete();
        _sync.Dispose();
    }

    private async Task MonitorReaderAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (!_disposeRequested)
            {
                var read = await _connection.ReaderStream.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                await AppendOutputAsync(new PtyOutputChunk(text, false, DateTimeOffset.UtcNow));
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PTY reader error: {ex.Message}");
        }
    }

    private void OnProcessExited(object? sender, Porta.Pty.PtyExitedEventArgs e)
    {
        _completedAtUtc = DateTimeOffset.UtcNow;
        _timeoutCancellation?.Cancel();

        _sync.Wait();
        try
        {
            _exitCode = e.ExitCode;
            _updatedAtUtc = _completedAtUtc.Value;
        }
        finally
        {
            _sync.Release();
        }

        NotifyStateChanged();
        _outputChannel.Writer.TryComplete();
    }

    private async Task MonitorTimeoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            var timeout = _timeout ?? TimeSpan.Zero;
            await Task.Delay(timeout, cancellationToken);
            if (!_exitCode.HasValue && !_disposeRequested)
            {
                await TerminateAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout was cancelled (session closed normally)
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PTY timeout monitor error: {ex.Message}");
        }
    }

    private async Task AppendOutputAsync(PtyOutputChunk chunk)
    {
        await _sync.WaitAsync();
        try
        {
            _recentOutput += chunk.Text;
            var newlines = chunk.Text.Count(c => c == '\n');
            if (newlines > 0)
            {
                Interlocked.Add(ref _outputLineCount, newlines);
            }

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

        // Write to transcript store (fire-and-forget, non-blocking)
        var transcriptChunk = new PtyTranscriptChunk(
            chunk.Text,
            chunk.IsError,
            Interlocked.Increment(ref _transcriptSequenceNumber),
            chunk.TimestampUtc);

        _ = Task.Run(async () =>
        {
            try
            {
                await _transcriptStore.AppendAsync(_sessionId, transcriptChunk);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write PTY transcript chunk: {ex.Message}");
            }
        });

        await _outputChannel.Writer.WriteAsync(chunk);
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(Snapshot);
    }
}
