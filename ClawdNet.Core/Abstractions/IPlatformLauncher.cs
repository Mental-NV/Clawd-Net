using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IPlatformLauncher
{
    Task<PlatformLaunchResult> OpenPathAsync(PlatformOpenRequest request, CancellationToken cancellationToken);

    Task<PlatformLaunchResult> OpenUrlAsync(string url, CancellationToken cancellationToken);
}
