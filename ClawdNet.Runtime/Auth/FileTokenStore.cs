using System.Text.Json;
using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Auth;

/// <summary>
/// File-based token store that persists OAuth tokens as JSON with restricted file permissions.
/// Storage location: &lt;root&gt;/.credentials.json
/// </summary>
public sealed class FileTokenStore : ITokenStore, IAsyncDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public FileTokenStore(string dataRoot)
    {
        _filePath = Path.Combine(dataRoot, ".credentials.json");
        Directory.CreateDirectory(dataRoot);
    }

    public async Task SaveTokensAsync(string key, OAuthTokens tokens, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileTokenStore));

        await _lock.WaitAsync(CancellationToken.None);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileTokenStore));

            var store = await LoadStoreAsync();
            store[key] = tokens;
            await SaveStoreAsync(store);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<OAuthTokens?> LoadTokensAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileTokenStore));

        await _lock.WaitAsync(CancellationToken.None);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileTokenStore));

            var store = await LoadStoreAsync();
            return store.TryGetValue(key, out var tokens) ? tokens : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteTokensAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileTokenStore));

        await _lock.WaitAsync(CancellationToken.None);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileTokenStore));

            var store = await LoadStoreAsync();
            if (store.Remove(key))
            {
                await SaveStoreAsync(store);
                return true;
            }
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileTokenStore));

        await _lock.WaitAsync(CancellationToken.None);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileTokenStore));

            var store = await LoadStoreAsync();
            return store.Keys.ToList().AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, OAuthTokens>> LoadStoreAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, OAuthTokens>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            var store = JsonSerializer.Deserialize<Dictionary<string, OAuthTokens>>(json);
            return store ?? new Dictionary<string, OAuthTokens>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, OAuthTokens>();
        }
    }

    private async Task SaveStoreAsync(Dictionary<string, OAuthTokens> store)
    {
        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        // Write to temp file first, then move atomically
        var tempPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);

        // Set restricted permissions (owner read/write only)
        try
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(tempPath, FileAttributes.Normal);
            }
            else
            {
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Best-effort permissions
        }

        // Atomic move
        File.Move(tempPath, _filePath, overwrite: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
        await Task.CompletedTask;
    }
}
