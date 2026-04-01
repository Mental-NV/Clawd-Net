using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakePtyManager : IPtyManager
{
    public PtySessionState? CurrentState { get; private set; }

    public event Action<PtySessionState?>? SessionChanged;

    public List<(string Command, string? WorkingDirectory)> Starts { get; } = [];

    public List<string> Writes { get; } = [];

    public int CloseCount { get; private set; }

    public Func<string, string?, PtySessionState>? StartHandler { get; set; }

    public Func<string, PtySessionState>? WriteHandler { get; set; }

    public Task<PtySessionState> StartAsync(string command, string? workingDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Starts.Add((command, workingDirectory));
        CurrentState = StartHandler?.Invoke(command, workingDirectory)
            ?? NewState(command, workingDirectory ?? Environment.CurrentDirectory, string.Empty, true, null, false);
        SessionChanged?.Invoke(CurrentState);
        return Task.FromResult(CurrentState);
    }

    public Task<PtySessionState> WriteAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Writes.Add(text);
        CurrentState = WriteHandler?.Invoke(text)
            ?? (CurrentState is null
                ? NewState("cat", Environment.CurrentDirectory, text, true, null, false)
                : CurrentState with
                {
                    RecentOutput = CurrentState.RecentOutput + text,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
        SessionChanged?.Invoke(CurrentState);
        return Task.FromResult(CurrentState);
    }

    public Task<PtySessionState?> CloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloseCount++;
        if (CurrentState is not null)
        {
            CurrentState = CurrentState with
            {
                IsRunning = false,
                ExitCode = 0,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        SessionChanged?.Invoke(CurrentState);
        return Task.FromResult(CurrentState);
    }

    public Task<PtySessionState?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CurrentState);
    }

    public void Publish(PtySessionState? state)
    {
        CurrentState = state;
        SessionChanged?.Invoke(CurrentState);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static PtySessionState NewState(
        string command,
        string workingDirectory,
        string recentOutput,
        bool isRunning,
        int? exitCode,
        bool isOutputClipped)
    {
        var now = DateTimeOffset.UtcNow;
        return new PtySessionState(
            Guid.NewGuid().ToString("N"),
            command,
            workingDirectory,
            now,
            now,
            isRunning,
            exitCode,
            recentOutput,
            isOutputClipped);
    }
}
