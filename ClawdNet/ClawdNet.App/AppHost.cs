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
using ClawdNet.Runtime.Tasks;
using ClawdNet.Runtime.Tools;
using ClawdNet.Terminal.Abstractions;
using ClawdNet.Terminal.Console;
using ClawdNet.Terminal.Repl;
using ClawdNet.Terminal.Rendering;
using ClawdNet.Terminal.Tui;

namespace ClawdNet.App;

public sealed class AppHost : IAsyncDisposable
{
    private readonly CommandDispatcher _dispatcher;
    private readonly CommandContext _context;
    private readonly IReplHost _replHost;
    private readonly ITuiHost _tuiHost;
    private readonly IToolRegistry _toolRegistry;
    private readonly ITaskStore _taskStore;
    private readonly ITaskManager _taskManager;
    private readonly IPluginCatalog _pluginCatalog;
    private readonly IPluginRuntime _pluginRuntime;
    private readonly IMcpClient _mcpClient;
    private readonly ILspClient _lspClient;
    private readonly IPtyManager _ptyManager;
    private readonly SemaphoreSlim _pluginInitializationLock = new(1, 1);
    private readonly SemaphoreSlim _taskInitializationLock = new(1, 1);
    private readonly SemaphoreSlim _mcpInitializationLock = new(1, 1);
    private readonly SemaphoreSlim _lspInitializationLock = new(1, 1);
    private readonly IFeatureGate _featureGate;
    private bool _pluginsInitialized;
    private bool _tasksInitialized;
    private bool _mcpInitialized;
    private bool _lspInitialized;

    public AppHost(
        string version,
        string dataRoot,
        IAnthropicMessageClient? anthropicMessageClient = null,
        IProcessRunner? processRunner = null,
        IFeatureGate? featureGate = null,
        IPluginCatalog? pluginCatalog = null,
        IPluginRuntime? pluginRuntime = null,
        IMcpClient? mcpClient = null,
        ILspClient? lspClient = null,
        IPtyManager? ptyManager = null,
        ITaskStore? taskStore = null,
        ITaskManager? taskManager = null,
        IReplHost? replHost = null,
        ITuiHost? tuiHost = null,
        ITerminalSession? terminalSession = null)
    {
        _featureGate = featureGate ?? new DictionaryFeatureGate();
        processRunner ??= new SystemProcessRunner();
        var builtInCommands = new[]
        {
            "ask",
            "task",
            "plugin",
            "lsp",
            "mcp",
            "session",
            "tool",
            "version",
            "--version",
            "-v",
            "-V"
        };
        _pluginCatalog = pluginCatalog ?? new PluginCatalog(dataRoot, builtInCommands);
        _pluginRuntime = pluginRuntime ?? new PluginRuntime(_pluginCatalog, processRunner, builtInCommands);
        _mcpClient = mcpClient ?? new StdioMcpClient(dataRoot, _pluginCatalog);
        _lspClient = lspClient ?? new StdioLspClient(dataRoot, _pluginCatalog);
        _ptyManager = ptyManager ?? new PtyManager();
        IEditPreviewService editPreviewService = new EditPreviewService();
        IEditApplier editApplier = new EditApplier(_lspClient);
        IConversationStore conversationStore = new JsonSessionStore(dataRoot);
        _taskStore = taskStore ?? new JsonTaskStore(dataRoot);
        _toolRegistry = new ToolRegistry(
        [
            new EchoTool(),
            new FileReadTool(),
            new GlobTool(),
            new GrepTool(),
            new ShellTool(processRunner),
            new PtyStartTool(_ptyManager),
            new PtyWriteTool(_ptyManager),
            new PtyReadTool(_ptyManager),
            new PtyCloseTool(_ptyManager),
            new ApplyPatchTool(editPreviewService, editApplier)
        ]);
        IToolExecutor toolExecutor = new ToolExecutor(_toolRegistry);
        anthropicMessageClient ??= new HttpAnthropicMessageClient(new HttpClient());
        IPermissionService permissionService = new DefaultPermissionService();
        _toolRegistry.Register(new FileWriteTool(_lspClient));
        IQueryEngine queryEngine = new QueryEngine(conversationStore, anthropicMessageClient, _toolRegistry, toolExecutor, permissionService, _pluginRuntime);
        _taskManager = taskManager ?? new TaskManager(_taskStore, conversationStore, queryEngine, _pluginRuntime);
        _toolRegistry.RegisterRange(
        [
            new TaskStartTool(_taskManager),
            new TaskStatusTool(_taskManager),
            new TaskListTool(_taskManager),
            new TaskInspectTool(_taskManager),
            new TaskCancelTool(_taskManager)
        ]);
        ITranscriptRenderer transcriptRenderer = new ConsoleTranscriptRenderer();
        ITuiRenderer tuiRenderer = new ConsoleTuiRenderer(transcriptRenderer);
        terminalSession ??= new ConsoleTerminalSession();
        _replHost = replHost ?? new ReplHost(terminalSession, conversationStore, queryEngine, transcriptRenderer, _ptyManager, _taskManager);
        _tuiHost = tuiHost ?? new TuiHost(terminalSession, conversationStore, queryEngine, tuiRenderer, _ptyManager, _taskManager);

        _context = new CommandContext(_featureGate, _toolRegistry, toolExecutor, conversationStore, _taskStore, _taskManager, queryEngine, _mcpClient, _lspClient, _pluginCatalog, _pluginRuntime, permissionService, transcriptRenderer, version);
        _dispatcher = new CommandDispatcher(
        [
            new AskCommandHandler(),
            new TaskCommandHandler(),
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
        await EnsureTasksInitializedAsync(cancellationToken);
        await EnsureMcpInitializedAsync(cancellationToken);
        await EnsureLspInitializedAsync(cancellationToken);
        if (ShouldLaunchInteractive(args))
        {
            var options = ParseReplLaunchOptions(args);
            return _featureGate.IsEnabled("legacy-repl")
                ? await _replHost.RunAsync(options, cancellationToken)
                : await _tuiHost.RunAsync(options, cancellationToken);
        }

        return await _dispatcher.DispatchAsync(_context, new CommandRequest(args), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _taskManager.DisposeAsync();
        await _mcpClient.DisposeAsync();
        await _lspClient.DisposeAsync();
        await _ptyManager.DisposeAsync();
        _pluginInitializationLock.Dispose();
        _taskInitializationLock.Dispose();
        _mcpInitializationLock.Dispose();
        _lspInitializationLock.Dispose();
    }

    private async Task EnsureTasksInitializedAsync(CancellationToken cancellationToken)
    {
        if (_tasksInitialized)
        {
            return;
        }

        await _taskInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_tasksInitialized)
            {
                return;
            }

            await _taskManager.InitializeAsync(cancellationToken);
            _tasksInitialized = true;
        }
        finally
        {
            _taskInitializationLock.Release();
        }
    }

    private static bool ShouldLaunchInteractive(IReadOnlyList<string> args)
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
            await _pluginRuntime.ReloadAsync(cancellationToken);
            _pluginsInitialized = true;
        }
        finally
        {
            _pluginInitializationLock.Release();
        }
    }
}
