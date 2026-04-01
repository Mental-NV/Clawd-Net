using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeLspClient : ILspClient
{
    public IReadOnlyCollection<LspServerState> Servers { get; set; } = [];

    public List<(string Path, string Content)> SyncRequests { get; } = [];

    public Func<string, LspServerState?> PingHandler { get; set; } = _ => null;
    public Func<string, int, int, IReadOnlyList<LspLocation>> DefinitionsHandler { get; set; } = (_, _, _) => [];
    public Func<string, int, int, IReadOnlyList<LspLocation>> ReferencesHandler { get; set; } = (_, _, _) => [];
    public Func<string, int, int, string?> HoverHandler { get; set; } = (_, _, _) => null;
    public Func<string, IReadOnlyList<LspDiagnostic>> DiagnosticsHandler { get; set; } = _ => [];
    public Func<string, string, Exception?> SyncHandler { get; set; } = (_, _) => null;

    public int ReloadCount { get; private set; }

    public Func<FakeLspClient, Task>? ReloadHandler { get; set; }

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        ReloadCount++;
        if (ReloadHandler is not null)
        {
            await ReloadHandler(this);
        }
    }

    public Task<LspServerState?> PingAsync(string serverName, CancellationToken cancellationToken)
        => Task.FromResult(PingHandler(serverName));

    public Task SyncFileAsync(string path, string content, CancellationToken cancellationToken)
    {
        SyncRequests.Add((path, content));
        var error = SyncHandler(path, content);
        if (error is not null)
        {
            throw error;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LspLocation>> GetDefinitionsAsync(string path, int line, int character, CancellationToken cancellationToken)
        => Task.FromResult(DefinitionsHandler(path, line, character));

    public Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string path, int line, int character, CancellationToken cancellationToken)
        => Task.FromResult(ReferencesHandler(path, line, character));

    public Task<string?> GetHoverAsync(string path, int line, int character, CancellationToken cancellationToken)
        => Task.FromResult(HoverHandler(path, line, character));

    public Task<IReadOnlyList<LspDiagnostic>> GetDiagnosticsAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(DiagnosticsHandler(path));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
