using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ILspClient : IAsyncDisposable
{
    IReadOnlyCollection<LspServerState> Servers { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task ReloadAsync(CancellationToken cancellationToken);

    Task<LspServerState?> PingAsync(string serverName, CancellationToken cancellationToken);

    Task SyncFileAsync(string path, string content, CancellationToken cancellationToken);

    Task<IReadOnlyList<LspLocation>> GetDefinitionsAsync(string path, int line, int character, CancellationToken cancellationToken);

    Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string path, int line, int character, CancellationToken cancellationToken);

    Task<string?> GetHoverAsync(string path, int line, int character, CancellationToken cancellationToken);

    Task<IReadOnlyList<LspDiagnostic>> GetDiagnosticsAsync(string path, CancellationToken cancellationToken);
}
