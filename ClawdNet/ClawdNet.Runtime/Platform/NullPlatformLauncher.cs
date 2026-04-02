using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Platform;

public sealed class NullPlatformLauncher : IPlatformLauncher
{
    public Task<PlatformLaunchResult> OpenPathAsync(PlatformOpenRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new PlatformLaunchResult(false, string.Empty, "Platform launcher is unavailable."));

    public Task<PlatformLaunchResult> OpenUrlAsync(string url, CancellationToken cancellationToken)
        => Task.FromResult(new PlatformLaunchResult(false, string.Empty, "Platform launcher is unavailable."));
}
