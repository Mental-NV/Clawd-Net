using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ISessionStore
{
    Task<SessionRecord> CreateAsync(string? title, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionRecord>> ListAsync(CancellationToken cancellationToken);
}
