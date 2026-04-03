using System.Collections.Concurrent;
using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Storage;

/// <summary>
/// JSONL-backed implementation of IPtyTranscriptStore that persists PTY output to disk.
/// Each chunk is written as a single JSON line for efficient append and replay.
/// </summary>
public sealed class PtyTranscriptStore : IPtyTranscriptStore, IAsyncDisposable
{
    // Default maximum chunks per session to avoid unbounded disk growth
    private const int DefaultMaxChunksPerSession = 1000;

    private readonly string _rootDirectory;
    private readonly int _maxChunksPerSession;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private bool _disposed;

    public PtyTranscriptStore(string rootDirectory, int maxChunksPerSession = DefaultMaxChunksPerSession)
    {
        _rootDirectory = Path.Combine(rootDirectory, "pty-transcripts");
        _maxChunksPerSession = maxChunksPerSession;
        Directory.CreateDirectory(_rootDirectory);
    }

    private string GetTranscriptPath(string sessionId)
    {
        // Sanitize session ID to prevent path traversal
        var safeId = sessionId.Replace(Path.DirectorySeparatorChar, '_')
                              .Replace(Path.AltDirectorySeparatorChar, '_');
        return Path.Combine(_rootDirectory, $"{safeId}.jsonl");
    }

    private SemaphoreSlim GetSessionLock(string sessionId)
    {
        return _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
    }

    public async Task AppendAsync(string sessionId, PtyTranscriptChunk chunk, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        var sessionLock = GetSessionLock(sessionId);
        await sessionLock.WaitAsync(CancellationToken.None); // Don't cancel file I/O

        try
        {
            if (_disposed) return;

            var filePath = GetTranscriptPath(sessionId);
            var json = JsonSerializer.Serialize(chunk);

            // Append to file (create if not exists)
            await using var stream = new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            await using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            // Log and suppress - transcript writes should not crash PTY sessions
            // In production, this would go to the app logger
            System.Diagnostics.Debug.WriteLine($"Failed to append PTY transcript chunk: {ex.Message}");
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task<IReadOnlyList<PtyTranscriptChunk>> ReadAsync(string sessionId, int? tailCount = null, CancellationToken cancellationToken = default)
    {
        var sessionLock = GetSessionLock(sessionId);
        await sessionLock.WaitAsync(CancellationToken.None);

        try
        {
            var filePath = GetTranscriptPath(sessionId);
            if (!File.Exists(filePath))
            {
                return Array.Empty<PtyTranscriptChunk>();
            }

            var chunks = new List<PtyTranscriptChunk>();

            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var chunk = JsonSerializer.Deserialize<PtyTranscriptChunk>(line);
                    if (chunk != null)
                    {
                        chunks.Add(chunk);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            // Return tail if requested
            if (tailCount.HasValue && chunks.Count > tailCount.Value)
            {
                return chunks.TakeLast(tailCount.Value).ToList().AsReadOnly();
            }

            return chunks.AsReadOnly();
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var sessionLock = GetSessionLock(sessionId);
        await sessionLock.WaitAsync(CancellationToken.None);

        try
        {
            var filePath = GetTranscriptPath(sessionId);
            return File.Exists(filePath);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var sessionLock = GetSessionLock(sessionId);
        await sessionLock.WaitAsync(CancellationToken.None);

        try
        {
            var filePath = GetTranscriptPath(sessionId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to delete PTY transcript: {ex.Message}");
        }
        finally
        {
            sessionLock.Release();
            _sessionLocks.TryRemove(sessionId, out var lockToDispose);
            lockToDispose?.Dispose();
        }
    }

    public async Task<IReadOnlyList<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var sessionIds = new List<string>();
        var files = Directory.GetFiles(_rootDirectory, "*.jsonl");

        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            sessionIds.Add(fileName);
        }

        return sessionIds.AsReadOnly();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _sessionLocks)
        {
            kvp.Value.Dispose();
        }
        _sessionLocks.Clear();

        await Task.CompletedTask;
    }
}
