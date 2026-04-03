using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Protocols;

public sealed class NullLspClient : ILspClient
{
    public IReadOnlyCollection<LspServerState> Servers => [];

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<LspServerState?> PingAsync(string serverName, CancellationToken cancellationToken)
        => Task.FromResult<LspServerState?>(null);

    public Task SyncFileAsync(string path, string content, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<LspLocation>> GetDefinitionsAsync(string path, int line, int character, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LspLocation>>([]);

    public Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string path, int line, int character, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LspLocation>>([]);

    public Task<string?> GetHoverAsync(string path, int line, int character, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<LspDiagnostic>> GetDiagnosticsAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LspDiagnostic>>([]);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
