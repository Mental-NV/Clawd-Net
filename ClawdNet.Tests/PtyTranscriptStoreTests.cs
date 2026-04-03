using ClawdNet.Core.Models;
using ClawdNet.Runtime.Storage;

namespace ClawdNet.Tests;

public sealed class PtyTranscriptStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"clawdnet-transcript-test-{Guid.NewGuid():N}");
    private readonly PtyTranscriptStore _store;

    public PtyTranscriptStoreTests()
    {
        Directory.CreateDirectory(_tempDir);
        _store = new PtyTranscriptStore(_tempDir);
    }

    [Fact]
    public async Task Append_and_read_transcript_chunks()
    {
        var sessionId = "test-session-1";
        var chunk1 = new PtyTranscriptChunk("hello ", false, 1, DateTimeOffset.UtcNow);
        var chunk2 = new PtyTranscriptChunk("world", false, 2, DateTimeOffset.UtcNow);

        await _store.AppendAsync(sessionId, chunk1);
        await _store.AppendAsync(sessionId, chunk2);

        var chunks = await _store.ReadAsync(sessionId);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("hello ", chunks[0].Text);
        Assert.Equal("world", chunks[1].Text);
        Assert.Equal(1, chunks[0].SequenceNumber);
        Assert.Equal(2, chunks[1].SequenceNumber);
    }

    [Fact]
    public async Task Read_tail_returns_recent_chunks()
    {
        var sessionId = "test-session-tail";
        for (var i = 0; i < 10; i++)
        {
            await _store.AppendAsync(sessionId, new PtyTranscriptChunk($"chunk-{i}", false, i + 1, DateTimeOffset.UtcNow));
        }

        var tail = await _store.ReadAsync(sessionId, tailCount: 3);

        Assert.Equal(3, tail.Count);
        Assert.Equal("chunk-7", tail[0].Text);
        Assert.Equal("chunk-8", tail[1].Text);
        Assert.Equal("chunk-9", tail[2].Text);
    }

    [Fact]
    public async Task Exists_returns_false_for_missing_session()
    {
        var exists = await _store.ExistsAsync("nonexistent-session");
        Assert.False(exists);
    }

    [Fact]
    public async Task Exists_returns_true_after_append()
    {
        var sessionId = "test-session-exists";
        await _store.AppendAsync(sessionId, new PtyTranscriptChunk("data", false, 1, DateTimeOffset.UtcNow));

        var exists = await _store.ExistsAsync(sessionId);
        Assert.True(exists);
    }

    [Fact]
    public async Task Delete_removes_transcript()
    {
        var sessionId = "test-session-delete";
        await _store.AppendAsync(sessionId, new PtyTranscriptChunk("data", false, 1, DateTimeOffset.UtcNow));

        await _store.DeleteAsync(sessionId);

        var exists = await _store.ExistsAsync(sessionId);
        Assert.False(exists);
    }

    [Fact]
    public async Task List_session_ids_returns_all_sessions()
    {
        await _store.AppendAsync("session-a", new PtyTranscriptChunk("data", false, 1, DateTimeOffset.UtcNow));
        await _store.AppendAsync("session-b", new PtyTranscriptChunk("data", false, 1, DateTimeOffset.UtcNow));
        await _store.AppendAsync("session-c", new PtyTranscriptChunk("data", false, 1, DateTimeOffset.UtcNow));

        var ids = await _store.ListSessionIdsAsync();

        Assert.Equal(3, ids.Count);
        Assert.Contains("session-a", ids);
        Assert.Contains("session-b", ids);
        Assert.Contains("session-c", ids);
    }

    [Fact]
    public async Task Read_preserves_error_flag()
    {
        var sessionId = "test-session-error";
        await _store.AppendAsync(sessionId, new PtyTranscriptChunk("error output", true, 1, DateTimeOffset.UtcNow));

        var chunks = await _store.ReadAsync(sessionId);

        Assert.Single(chunks);
        Assert.True(chunks[0].IsError);
    }

    public void Dispose()
    {
        _store.DisposeAsync().GetAwaiter().GetResult();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }
}
