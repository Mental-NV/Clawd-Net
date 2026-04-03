using ClawdNet.Runtime.Processes;
using ClawdNet.Runtime.Storage;

namespace ClawdNet.Tests;

public sealed class PtyManagerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"clawdnet-test-{Guid.NewGuid():N}");
    private readonly PtyTranscriptStore _transcriptStore;
    private readonly PtyManager _ptyManager;

    public PtyManagerTests()
    {
        Directory.CreateDirectory(_tempDir);
        _transcriptStore = new PtyTranscriptStore(_tempDir);
        _ptyManager = new PtyManager(_transcriptStore);
    }

    [Fact]
    public async Task Pty_manager_can_start_write_read_and_close_session()
    {
        var started = await _ptyManager.StartAsync("cat", null, CancellationToken.None);
        await _ptyManager.WriteAsync("hello from pty\n", null, CancellationToken.None);

        var state = await WaitForOutputAsync("hello from pty");
        var closed = await _ptyManager.CloseAsync(null, CancellationToken.None);

        Assert.True(started.IsRunning);
        Assert.NotNull(state);
        Assert.Contains("hello from pty", state!.RecentOutput);
        Assert.NotNull(closed);
        Assert.False(closed!.IsRunning);
    }

    [Fact]
    public async Task Pty_manager_supports_multiple_sessions_and_focus_switching()
    {
        var first = await _ptyManager.StartAsync("cat", null, CancellationToken.None);
        var second = await _ptyManager.StartAsync("python3", null, CancellationToken.None);

        var sessions = await _ptyManager.ListAsync(CancellationToken.None);

        Assert.Equal(2, sessions.Count);
        Assert.Equal(second.SessionId, _ptyManager.State.CurrentSessionId);
        var focused = await _ptyManager.FocusAsync(first.SessionId, CancellationToken.None);
        Assert.Equal(first.SessionId, focused.SessionId);
        Assert.Equal(first.SessionId, _ptyManager.State.CurrentSessionId);
    }

    [Fact]
    public async Task Pty_manager_clips_output_to_bounded_buffer()
    {
        await _ptyManager.StartAsync("cat", null, CancellationToken.None);
        var largeText = new string('x', 5000) + "\n";

        await _ptyManager.WriteAsync(largeText, null, CancellationToken.None);
        var state = await WaitForConditionAsync(state => state?.IsOutputClipped == true);

        Assert.NotNull(state);
        Assert.True(state!.IsOutputClipped);
        Assert.True(state.RecentOutput.Length <= 4096);
    }

    [Fact]
    public async Task Closing_current_session_promotes_another_running_session()
    {
        var first = await _ptyManager.StartAsync("cat", null, CancellationToken.None);
        var second = await _ptyManager.StartAsync("python3", null, CancellationToken.None);

        await _ptyManager.CloseAsync(second.SessionId, CancellationToken.None);

        Assert.Equal(first.SessionId, _ptyManager.State.CurrentSessionId);
    }

    [Fact]
    public async Task Prune_exited_removes_stopped_sessions()
    {
        var first = await _ptyManager.StartAsync("cat", null, CancellationToken.None);
        var second = await _ptyManager.StartAsync("python3", null, CancellationToken.None);
        await _ptyManager.CloseAsync(first.SessionId, CancellationToken.None);

        var removed = await _ptyManager.PruneExitedAsync(CancellationToken.None);
        var sessions = await _ptyManager.ListAsync(CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.DoesNotContain(sessions, session => session.SessionId == first.SessionId);
        Assert.Contains(sessions, session => session.SessionId == second.SessionId);
    }

    [Fact]
    public async Task Pty_session_writes_to_transcript_store()
    {
        var started = await _ptyManager.StartAsync("cat", null, CancellationToken.None);
        await _ptyManager.WriteAsync("transcript test\n", null, CancellationToken.None);

        // Wait for output to arrive
        await WaitForOutputAsync("transcript test");

        // Small delay for transcript write (fire-and-forget)
        await Task.Delay(100);

        var transcript = await _ptyManager.GetTranscriptAsync(started.SessionId);

        Assert.NotEmpty(transcript);
        Assert.Contains("transcript test", string.Join("", transcript.Select(c => c.Text)));
    }

    private async Task DisposeAsync()
    {
        await _ptyManager.DisposeAsync();
        await _transcriptStore.DisposeAsync();
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

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    private async Task<Core.Models.PtySessionState?> WaitForOutputAsync(string expected)
    {
        return await WaitForConditionAsync(state => state?.RecentOutput.Contains(expected, StringComparison.Ordinal) == true);
    }

    private async Task<Core.Models.PtySessionState?> WaitForConditionAsync(Func<Core.Models.PtySessionState?, bool> predicate)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var state = await _ptyManager.ReadAsync(null, CancellationToken.None);
            if (predicate(state))
            {
                return state;
            }

            await Task.Delay(20);
        }

        return await _ptyManager.ReadAsync(null, CancellationToken.None);
    }
}
