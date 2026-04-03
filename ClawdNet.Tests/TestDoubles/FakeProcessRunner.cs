using ClawdNet.Core.Abstractions;

namespace ClawdNet.Tests.TestDoubles;

public sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessRequest> Requests { get; } = [];

    public Func<ProcessRequest, ProcessResult> Handler { get; set; }
        = request => new ProcessResult(0, $"ran:{request.Arguments}", string.Empty);

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Handler(request));
    }
}
