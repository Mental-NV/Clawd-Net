using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Commands;
using ClawdNet.Core.Models;
using ClawdNet.Core.Services;
using ClawdNet.Core.Tools;
using ClawdNet.Runtime.Anthropic;
using ClawdNet.Runtime.Editing;
using ClawdNet.Runtime.FeatureGates;
using ClawdNet.Runtime.Permissions;
using ClawdNet.Runtime.Plugins;
using ClawdNet.Runtime.Protocols;
using ClawdNet.Runtime.Processes;
using ClawdNet.Runtime.Sessions;
using ClawdNet.Runtime.Tools;
using ClawdNet.Terminal.Abstractions;
using ClawdNet.Terminal.Console;
using ClawdNet.Terminal.Repl;
using ClawdNet.Terminal.Rendering;

namespace ClawdNet.App;

public sealed class AppHost : IAsyncDisposable
{
    private readonly CommandDispatcher _dispatcher;
    private readonly CommandContext _context;
    private readonly IReplHost _replHost;
    private readonly IToolRegistry _toolRegistry;
    private readonly IPluginCatalog _pluginCatalog;
    private readonly IMcpClient _mcpClient;
    private readonly ILspClient _lspClient;
    private readonly SemaphoreSlim _pluginInitializationLock = new(1, 1);
    private readonly SemaphoreSlim _mcpInitializationLock = new(1, 1);
    private readonly SemaphoreSlim _lspInitializationLock = new(1, 1);
    private bool _pluginsInitialized;
    private bool _mcpInitialized;
    private bool _lspInitialized;

    public AppHost(
        string version,
        string dataRoot,
        IAnthropicMessageClient? anthropicMessageClient = null,
        IProcessRunner? processRunner = null,
        IPluginCatalog? pluginCatalog = null,
        IMcpClient? mcpClient = null,
        ILspClient? lspClient = null,
        IReplHost? replHost = null,
        ITerminalSession? terminalSession = null)
    {
        IFeatureGate featureGate = new DictionaryFeatureGate();
        processRunner ??= new SystemProcessRunner();
        _pluginCatalog = pluginCatalog ?? new PluginCatalog(dataRoot);
        _mcpClient = mcpClient ?? new StdioMcpClient(dataRoot, _pluginCatalog);
        _lspClient = lspClient ?? new StdioLspClient(dataRoot, _pluginCatalog);
        IEditPreviewService editPreviewService = new EditPreviewService();
        IEditApplier editApplier = new EditApplier(_lspClient);
        _toolRegistry = new ToolRegistry(
        [
            new EchoTool(),
            new FileReadTool(),
            new GlobTool(),
            new GrepTool(),
            new ShellTool(processRunner),
            new ApplyPatchTool(editPreviewService, editApplier)
        ]);
        IToolExecutor toolExecutor = new ToolExecutor(_toolRegistry);
        IConversationStore conversationStore = new JsonSessionStore(dataRoot);
        anthropicMessageClient ??= new HttpAnthropicMessageClient(new HttpClient());
        IPermissionService permissionService = new DefaultPermissionService();
        _toolRegistry.Register(new FileWriteTool(_lspClient));
        IQueryEngine queryEngine = new QueryEngine(conversationStore, anthropicMessageClient, _toolRegistry, toolExecutor, permissionService);
        ITranscriptRenderer transcriptRenderer = new ConsoleTranscriptRenderer();
        terminalSession ??= new ConsoleTerminalSession();
        _replHost = replHost ?? new ReplHost(terminalSession, conversationStore, queryEngine, transcriptRenderer);

        _context = new CommandContext(featureGate, _toolRegistry, toolExecutor, conversationStore, queryEngine, _mcpClient, _lspClient, _pluginCatalog, permissionService, transcriptRenderer, version);
        _dispatcher = new CommandDispatcher(
        [
            new AskCommandHandler(),
            new PluginCommandHandler(),
            new LspCommandHandler(),
            new McpCommandHandler(),
            new SessionCommandHandler(),
            new ToolCommandHandler(),
            new VersionCommandHandler()
        ]);

        McpClient = _mcpClient;
        LspClient = _lspClient;
    }

    public IMcpClient McpClient { get; }

    public ILspClient LspClient { get; }

    public async Task<CommandExecutionResult> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        await EnsurePluginsInitializedAsync(cancellationToken);
        await EnsureMcpInitializedAsync(cancellationToken);
        await EnsureLspInitializedAsync(cancellationToken);
        if (ShouldLaunchRepl(args))
        {
            return await _replHost.RunAsync(ParseReplLaunchOptions(args), cancellationToken);
        }

        return await _dispatcher.DispatchAsync(_context, new CommandRequest(args), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _mcpClient.DisposeAsync();
        await _lspClient.DisposeAsync();
        _pluginInitializationLock.Dispose();
        _mcpInitializationLock.Dispose();
        _lspInitializationLock.Dispose();
    }

    private static bool ShouldLaunchRepl(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return true;
        }

        return TryParseReplLaunchOptions(args, out _);
    }

    private static ReplLaunchOptions ParseReplLaunchOptions(IReadOnlyList<string> args)
    {
        if (!TryParseReplLaunchOptions(args, out var options))
        {
            throw new InvalidOperationException("Invalid REPL launch arguments.");
        }

        return options;
    }

    private static bool TryParseReplLaunchOptions(IReadOnlyList<string> args, out ReplLaunchOptions options)
    {
        string? sessionId = null;
        string? model = null;
        var permissionMode = PermissionMode.Default;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--session" when index + 1 < args.Count:
                    sessionId = args[++index];
                    break;
                case "--model" when index + 1 < args.Count:
                    model = args[++index];
                    break;
                case "--permission-mode" when index + 1 < args.Count:
                    permissionMode = ParsePermissionMode(args[++index]);
                    break;
                default:
                    options = new ReplLaunchOptions();
                    return false;
            }
        }

        options = new ReplLaunchOptions(sessionId, model, permissionMode);
        return true;
    }

    private static PermissionMode ParsePermissionMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "default" => PermissionMode.Default,
            "acceptedits" => PermissionMode.AcceptEdits,
            "accept-edits" => PermissionMode.AcceptEdits,
            "bypasspermissions" => PermissionMode.BypassPermissions,
            "bypass-permissions" => PermissionMode.BypassPermissions,
            "bypass" => PermissionMode.BypassPermissions,
            _ => throw new InvalidOperationException($"Unknown permission mode '{value}'.")
        };
    }

    private async Task EnsureMcpInitializedAsync(CancellationToken cancellationToken)
    {
        if (_mcpInitialized)
        {
            return;
        }

        await _mcpInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_mcpInitialized)
            {
                return;
            }

            await _mcpClient.InitializeAsync(cancellationToken);
            var tools = await _mcpClient.GetToolsAsync(null, cancellationToken);
            _toolRegistry.RegisterRange(tools.Select(tool => new McpToolProxy(_mcpClient, tool)));
            _mcpInitialized = true;
        }
        finally
        {
            _mcpInitializationLock.Release();
        }
    }

    private async Task EnsureLspInitializedAsync(CancellationToken cancellationToken)
    {
        if (_lspInitialized)
        {
            return;
        }

        await _lspInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_lspInitialized)
            {
                return;
            }

            await _lspClient.InitializeAsync(cancellationToken);
            _toolRegistry.RegisterRange(
            [
                new LspDefinitionTool(_lspClient),
                new LspReferencesTool(_lspClient),
                new LspHoverTool(_lspClient),
                new LspDiagnosticsTool(_lspClient)
            ]);
            _lspInitialized = true;
        }
        finally
        {
            _lspInitializationLock.Release();
        }
    }

    private async Task EnsurePluginsInitializedAsync(CancellationToken cancellationToken)
    {
        if (_pluginsInitialized)
        {
            return;
        }

        await _pluginInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_pluginsInitialized)
            {
                return;
            }

            await _pluginCatalog.ReloadAsync(cancellationToken);
            _pluginsInitialized = true;
        }
        finally
        {
            _pluginInitializationLock.Release();
        }
    }
}
