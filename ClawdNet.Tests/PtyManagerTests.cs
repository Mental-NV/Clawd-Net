using ClawdNet.Runtime.Processes;

namespace ClawdNet.Tests;

public sealed class PtyManagerTests : IDisposable
{
    private readonly PtyManager _ptyManager = new();

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

    private async Task DisposeAsync()
    {
        await _ptyManager.DisposeAsync();
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
