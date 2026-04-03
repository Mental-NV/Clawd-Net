using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakePlatformLauncher : IPlatformLauncher
{
    public List<PlatformOpenRequest> OpenPathRequests { get; } = [];

    public List<string> OpenUrlRequests { get; } = [];

    public Func<PlatformOpenRequest, PlatformLaunchResult> OpenPathHandler { get; set; }
        = request => new PlatformLaunchResult(true, $"Opened {request.Path}.");

    public Func<string, PlatformLaunchResult> OpenUrlHandler { get; set; }
        = url => new PlatformLaunchResult(true, $"Opened {url}.");

    public Task<PlatformLaunchResult> OpenPathAsync(PlatformOpenRequest request, CancellationToken cancellationToken)
    {
        OpenPathRequests.Add(request);
        return Task.FromResult(OpenPathHandler(request));
    }

    public Task<PlatformLaunchResult> OpenUrlAsync(string url, CancellationToken cancellationToken)
    {
        OpenUrlRequests.Add(url);
        return Task.FromResult(OpenUrlHandler(url));
    }
}
