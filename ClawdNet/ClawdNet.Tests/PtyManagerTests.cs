using ClawdNet.Runtime.Processes;

namespace ClawdNet.Tests;

public sealed class PtyManagerTests : IDisposable
{
    private readonly PtyManager _ptyManager = new();

    [Fact]
    public async Task Pty_manager_can_start_write_read_and_close_session()
    {
        var started = await _ptyManager.StartAsync("cat", null, CancellationToken.None);
        await _ptyManager.WriteAsync("hello from pty\n", CancellationToken.None);

        var state = await WaitForOutputAsync("hello from pty");
        var closed = await _ptyManager.CloseAsync(CancellationToken.None);

        Assert.True(started.IsRunning);
        Assert.NotNull(state);
        Assert.Contains("hello from pty", state!.RecentOutput);
        Assert.NotNull(closed);
        Assert.False(closed!.IsRunning);
    }

    [Fact]
    public async Task Pty_manager_allows_only_one_active_session()
    {
        await _ptyManager.StartAsync("cat", null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _ptyManager.StartAsync("cat", null, CancellationToken.None));
    }

    [Fact]
    public async Task Pty_manager_clips_output_to_bounded_buffer()
    {
        await _ptyManager.StartAsync("cat", null, CancellationToken.None);
        var largeText = new string('x', 5000) + "\n";

        await _ptyManager.WriteAsync(largeText, CancellationToken.None);
        var state = await WaitForConditionAsync(state => state?.IsOutputClipped == true);

        Assert.NotNull(state);
        Assert.True(state!.IsOutputClipped);
        Assert.True(state.RecentOutput.Length <= 4096);
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
            var state = await _ptyManager.ReadAsync(CancellationToken.None);
            if (predicate(state))
            {
                return state;
            }

            await Task.Delay(20);
        }

        return await _ptyManager.ReadAsync(CancellationToken.None);
    }
}
