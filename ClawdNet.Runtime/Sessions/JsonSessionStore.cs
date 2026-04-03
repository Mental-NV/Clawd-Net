using System.Text.Json;
using ClawdNet.Core.Exceptions;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Sessions;

public sealed class JsonSessionStore : IConversationStore
{
    private readonly string _storePath;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonSessionStore(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _storePath = Path.Combine(rootDirectory, "sessions.json");
    }

    public async Task<ConversationSession> CreateAsync(string? title, string model, CancellationToken cancellationToken, string? provider = null)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var sessions = await ReadSessionsAsync(cancellationToken);
            var timestamp = DateTimeOffset.UtcNow;
            var session = new ConversationSession(
                Guid.NewGuid().ToString("N"),
                string.IsNullOrWhiteSpace(title) ? "Untitled session" : title.Trim(),
                model,
                timestamp,
                timestamp,
                [
                    new ConversationMessage("system", "ClawdNet session initialized.", timestamp)
                ],
                provider);
            sessions.Add(session);
            await WriteSessionsAsync(sessions, cancellationToken);
            return session;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<ConversationSession?> GetAsync(string sessionId, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var sessions = await ReadSessionsAsync(cancellationToken);
            return sessions.FirstOrDefault(session => string.Equals(session.Id, sessionId, StringComparison.Ordinal));
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<IReadOnlyList<ConversationSession>> ListAsync(CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var sessions = await ReadSessionsAsync(cancellationToken);
            return sessions
                .OrderByDescending(session => session.UpdatedAtUtc)
                .ToArray();
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task SaveAsync(ConversationSession session, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var sessions = await ReadSessionsAsync(cancellationToken);
            var index = sessions.FindIndex(existing => string.Equals(existing.Id, session.Id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new ConversationStoreException($"Session '{session.Id}' was not found.");
            }

            sessions[index] = session;
            await WriteSessionsAsync(sessions, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<ConversationSession?> GetMostRecentAsync(CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var sessions = await ReadSessionsAsync(cancellationToken);
            return sessions
                .OrderByDescending(session => session.UpdatedAtUtc)
                .FirstOrDefault();
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<IReadOnlyList<ConversationSession>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await ListAsync(cancellationToken);
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            var sessions = await ReadSessionsAsync(cancellationToken);
            var normalizedQuery = query.Trim().ToLowerInvariant();

            // Exact ID match takes priority
            var exactIdMatch = sessions.FirstOrDefault(s =>
                string.Equals(s.Id, query, StringComparison.OrdinalIgnoreCase));
            if (exactIdMatch is not null)
            {
                return [exactIdMatch];
            }

            // Prefix match on ID
            var prefixMatches = sessions
                .Where(s => s.Id.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.UpdatedAtUtc)
                .ToList();
            if (prefixMatches.Count > 0)
            {
                return prefixMatches;
            }

            // Substring match on title
            var titleMatches = sessions
                .Where(s => s.Title.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(s => s.UpdatedAtUtc)
                .ToList();
            return titleMatches;
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<List<ConversationSession>> ReadSessionsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_storePath);
        var sessions = await JsonSerializer.DeserializeAsync<List<ConversationSession>>(stream, _jsonOptions, cancellationToken);
        return sessions?.Select(Normalize).ToList() ?? [];
    }

    private async Task WriteSessionsAsync(List<ConversationSession> sessions, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_storePath);
        await JsonSerializer.SerializeAsync(stream, sessions, _jsonOptions, cancellationToken);
    }

    private static ConversationSession Normalize(ConversationSession session)
    {
        var createdAt = session.CreatedAtUtc == default ? DateTimeOffset.UtcNow : session.CreatedAtUtc;
        var updatedAt = session.UpdatedAtUtc == default ? createdAt : session.UpdatedAtUtc;
        var model = string.IsNullOrWhiteSpace(session.Model) ? "claude-sonnet-4-5" : session.Model;
        var provider = string.IsNullOrWhiteSpace(session.Provider) ? "anthropic" : session.Provider;

        return session with
        {
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = updatedAt,
            Model = model,
            Provider = provider
        };
    }
}
