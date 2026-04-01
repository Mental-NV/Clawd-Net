using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Sessions;

public sealed class JsonSessionStore : ISessionStore
{
    private readonly string _storePath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonSessionStore(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _storePath = Path.Combine(rootDirectory, "sessions.json");
    }

    public async Task<SessionRecord> CreateAsync(string? title, CancellationToken cancellationToken)
    {
        var sessions = await ReadSessionsAsync(cancellationToken);
        var session = new SessionRecord(
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(title) ? "Untitled session" : title.Trim(),
            DateTimeOffset.UtcNow,
            [
                new TranscriptEntry("system", "ClawdNet session initialized.", DateTimeOffset.UtcNow)
            ]);
        sessions.Add(session);
        await WriteSessionsAsync(sessions, cancellationToken);
        return session;
    }

    public async Task<IReadOnlyList<SessionRecord>> ListAsync(CancellationToken cancellationToken)
    {
        var sessions = await ReadSessionsAsync(cancellationToken);
        return sessions
            .OrderByDescending(session => session.CreatedAtUtc)
            .ToArray();
    }

    private async Task<List<SessionRecord>> ReadSessionsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_storePath);
        var sessions = await JsonSerializer.DeserializeAsync<List<SessionRecord>>(stream, _jsonOptions, cancellationToken);
        return sessions ?? [];
    }

    private async Task WriteSessionsAsync(List<SessionRecord> sessions, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_storePath);
        await JsonSerializer.SerializeAsync(stream, sessions, _jsonOptions, cancellationToken);
    }
}
