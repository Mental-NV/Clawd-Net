using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Commands;
using ClawdNet.Core.Models;
using ClawdNet.Core.Services;
using ClawdNet.Runtime.FeatureGates;
using ClawdNet.Runtime.Protocols;
using ClawdNet.Runtime.Sessions;
using ClawdNet.Runtime.Tools;
using ClawdNet.Terminal.Rendering;

namespace ClawdNet.App;

public sealed class AppHost
{
    private readonly CommandDispatcher _dispatcher;
    private readonly CommandContext _context;

    public AppHost(string version, string dataRoot)
    {
        IFeatureGate featureGate = new DictionaryFeatureGate();
        IToolRegistry toolRegistry = new ToolRegistry([new EchoTool()]);
        IToolExecutor toolExecutor = new ToolExecutor(toolRegistry);
        ISessionStore sessionStore = new JsonSessionStore(dataRoot);
        ITranscriptRenderer transcriptRenderer = new ConsoleTranscriptRenderer();

        _context = new CommandContext(featureGate, toolExecutor, sessionStore, transcriptRenderer, version);
        _dispatcher = new CommandDispatcher(
        [
            new SessionCommandHandler(),
            new ToolCommandHandler(),
            new VersionCommandHandler()
        ]);

        McpClient = new NullMcpClient();
        LspClient = new NullLspClient();
    }

    public IMcpClient McpClient { get; }

    public ILspClient LspClient { get; }

    public Task<CommandExecutionResult> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        return _dispatcher.DispatchAsync(_context, new CommandRequest(args), cancellationToken);
    }
}
