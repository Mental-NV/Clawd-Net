using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Commands;
using ClawdNet.Core.Models;
using ClawdNet.Core.Services;
using ClawdNet.Runtime.Anthropic;
using ClawdNet.Runtime.FeatureGates;
using ClawdNet.Runtime.Protocols;
using ClawdNet.Runtime.Processes;
using ClawdNet.Runtime.Sessions;
using ClawdNet.Runtime.Tools;
using ClawdNet.Terminal.Rendering;

namespace ClawdNet.App;

public sealed class AppHost
{
    private readonly CommandDispatcher _dispatcher;
    private readonly CommandContext _context;

    public AppHost(
        string version,
        string dataRoot,
        IAnthropicMessageClient? anthropicMessageClient = null,
        IProcessRunner? processRunner = null)
    {
        IFeatureGate featureGate = new DictionaryFeatureGate();
        processRunner ??= new SystemProcessRunner();
        IToolRegistry toolRegistry = new ToolRegistry(
        [
            new EchoTool(),
            new FileReadTool(),
            new ShellTool(processRunner)
        ]);
        IToolExecutor toolExecutor = new ToolExecutor(toolRegistry);
        IConversationStore conversationStore = new JsonSessionStore(dataRoot);
        anthropicMessageClient ??= new HttpAnthropicMessageClient(new HttpClient());
        IQueryEngine queryEngine = new QueryEngine(conversationStore, anthropicMessageClient, toolRegistry, toolExecutor);
        ITranscriptRenderer transcriptRenderer = new ConsoleTranscriptRenderer();

        _context = new CommandContext(featureGate, toolExecutor, conversationStore, queryEngine, transcriptRenderer, version);
        _dispatcher = new CommandDispatcher(
        [
            new AskCommandHandler(),
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
