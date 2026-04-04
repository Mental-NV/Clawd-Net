using ClawdNet.Core.Models;
using ClawdNet.Runtime.Auth;

namespace ClawdNet.Tests;

public class FileTokenStoreTests
{
    [Fact]
    public async Task SaveAndLoadTokens_RoundTrips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawdnet_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileTokenStore(tempDir);
            var tokens = new OAuthTokens
            {
                AccessToken = "test-access-token",
                RefreshToken = "test-refresh-token",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                Scopes = new List<string> { "user:profile", "user:sessions:claude_code" }
            };

            await store.SaveTokensAsync("test_key", tokens);
            var loaded = await store.LoadTokensAsync("test_key");

            Assert.NotNull(loaded);
            Assert.Equal("test-access-token", loaded!.AccessToken);
            Assert.Equal("test-refresh-token", loaded.RefreshToken);
            Assert.Equal(2, loaded.Scopes.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task LoadTokens_NonExistentKey_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawdnet_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileTokenStore(tempDir);
            var loaded = await store.LoadTokensAsync("nonexistent");
            Assert.Null(loaded);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task DeleteTokens_RemovesKey()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawdnet_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileTokenStore(tempDir);
            var tokens = new OAuthTokens { AccessToken = "tok" };
            await store.SaveTokensAsync("delete_me", tokens);

            var deleted = await store.DeleteTokensAsync("delete_me");
            Assert.True(deleted);

            var loaded = await store.LoadTokensAsync("delete_me");
            Assert.Null(loaded);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task DeleteTokens_NonExistentKey_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawdnet_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileTokenStore(tempDir);
            var deleted = await store.DeleteTokensAsync("nonexistent");
            Assert.False(deleted);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task ListKeys_ReturnsAllKeys()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawdnet_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileTokenStore(tempDir);
            await store.SaveTokensAsync("key1", new OAuthTokens { AccessToken = "a" });
            await store.SaveTokensAsync("key2", new OAuthTokens { AccessToken = "b" });

            var keys = await store.ListKeysAsync();
            Assert.Equal(2, keys.Count);
            Assert.Contains("key1", keys);
            Assert.Contains("key2", keys);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task CredentialsFile_HasRestrictedPermissions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawdnet_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileTokenStore(tempDir);
            await store.SaveTokensAsync("test", new OAuthTokens { AccessToken = "t" });

            var credFile = Path.Combine(tempDir, ".credentials.json");
            Assert.True(File.Exists(credFile));

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(credFile);
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
