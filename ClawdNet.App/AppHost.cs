using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Commands;
using ClawdNet.Core.Models;
using ClawdNet.Core.Services;
using ClawdNet.Core.Tools;
using ClawdNet.Runtime.Anthropic;
using ClawdNet.Runtime.Editing;
using ClawdNet.Runtime.FeatureGates;
using ClawdNet.Runtime.Permissions;
using ClawdNet.Runtime.Platform;
using ClawdNet.Runtime.Plugins;
using ClawdNet.Runtime.Protocols;
using ClawdNet.Runtime.Processes;
using ClawdNet.Runtime.Providers;
using ClawdNet.Runtime.Sessions;
using ClawdNet.Runtime.Storage;
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
    private readonly IReadOnlyList<ICommandHandler> _handlers;
    private readonly IReplHost _replHost;
    private readonly ITuiHost _tuiHost;
    private readonly IToolRegistry _toolRegistry;
    private readonly ITaskStore _taskStore;
    private readonly ITaskManager _taskManager;
    private readonly IProviderCatalog _providerCatalog;
    private readonly IPluginCatalog _pluginCatalog;
    private readonly IPluginRuntime _pluginRuntime;
    private readonly IPlatformLauncher _platformLauncher;
    private readonly IMcpClient _mcpClient;
    private readonly ILspClient _lspClient;
    private readonly IPtyManager _ptyManager;
    private readonly SemaphoreSlim _pluginInitializationLock = new(1, 1);
    private readonly SemaphoreSlim _taskInitializationLock = new(1, 1);
    private readonly SemaphoreSlim _mcpInitializationLock = new(1, 1);
    private readonly SemaphoreSlim _lspInitializationLock = new(1, 1);
    private readonly IFeatureGate _featureGate;
    private readonly LegacySettingsLoader _legacySettingsLoader;
    private readonly MemoryFileLoader _memoryFileLoader;
    private readonly ProjectMcpConfigLoader _projectMcpConfigLoader;
    private bool _pluginsInitialized;
    private bool _tasksInitialized;
    private bool _mcpInitialized;
    private bool _lspInitialized;

    public AppHost(
        string version,
        string dataRoot,
        string? legacyConfigDir = null,
        IAnthropicMessageClient? anthropicMessageClient = null,
        IProviderCatalog? providerCatalog = null,
        IModelClientFactory? modelClientFactory = null,
        IProcessRunner? processRunner = null,
        IFeatureGate? featureGate = null,
        IPluginCatalog? pluginCatalog = null,
        IPluginRuntime? pluginRuntime = null,
        IPlatformLauncher? platformLauncher = null,
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
        _legacySettingsLoader = new LegacySettingsLoader();
        _memoryFileLoader = new MemoryFileLoader();
        _projectMcpConfigLoader = new ProjectMcpConfigLoader();
        processRunner ??= new SystemProcessRunner();
        var builtInCommands = new[]
        {
            "ask",
            "provider",
            "platform",
            "task",
            "plugin",
            "lsp",
            "mcp",
            "session",
            "tool",
            "auth",
            "version",
            "--version",
            "-v",
            "-V"
        };
        var builtInToolNames = new[]
        {
            "echo",
            "file_read",
            "glob",
            "grep",
            "shell",
            "pty_start",
            "pty_focus",
            "pty_list",
            "pty_write",
            "pty_read",
            "pty_close",
            "open_path",
            "open_url",
            "apply_patch",
            "file_write",
            "task_start",
            "task_status",
            "task_list",
            "task_inspect",
            "task_cancel",
            "lsp_definition",
            "lsp_references",
            "lsp_hover",
            "lsp_diagnostics"
        };
        _providerCatalog = providerCatalog ?? new ProviderCatalog(dataRoot);
        _pluginCatalog = pluginCatalog ?? new PluginCatalog(dataRoot, builtInCommands, builtInToolNames);
        _pluginRuntime = pluginRuntime ?? new PluginRuntime(_pluginCatalog, processRunner, builtInCommands);
        _mcpClient = mcpClient ?? new StdioMcpClient(dataRoot, _pluginCatalog);
        _lspClient = lspClient ?? new StdioLspClient(dataRoot, _pluginCatalog);
        var transcriptStore = new PtyTranscriptStore(dataRoot);
        _ptyManager = ptyManager ?? new PtyManager(transcriptStore);
        _platformLauncher = platformLauncher ?? new DefaultPlatformLauncher(processRunner, new PlatformConfigurationLoader(dataRoot));
        IEditPreviewService editPreviewService = new EditPreviewService();
        IEditApplier editApplier = new EditApplier(_lspClient);
        IConversationStore conversationStore = new JsonSessionStore(dataRoot);
        _taskStore = taskStore ?? new JsonTaskStore(dataRoot);
        if (modelClientFactory is null)
        {
            var factory = new DefaultModelClientFactory();
            if (anthropicMessageClient is not null)
            {
                factory.RegisterOverride(ProviderDefaults.DefaultProviderName, anthropicMessageClient);
            }

            modelClientFactory = factory;
        }

        _toolRegistry = new ToolRegistry(
        [
            new EchoTool(),
            new FileReadTool(),
            new GlobTool(),
            new GrepTool(),
            new ShellTool(processRunner),
            new PtyStartTool(_ptyManager),
            new PtyFocusTool(_ptyManager),
            new PtyListTool(_ptyManager),
            new PtyWriteTool(_ptyManager),
            new PtyReadTool(_ptyManager),
            new PtyCloseTool(_ptyManager),
            new OpenPathTool(_platformLauncher),
            new OpenUrlTool(_platformLauncher),
            new ApplyPatchTool(editPreviewService, editApplier)
        ]);
        IToolExecutor toolExecutor = new ToolExecutor(_toolRegistry);
        IPermissionService permissionService = new DefaultPermissionService();
        _toolRegistry.Register(new FileWriteTool(_lspClient));
        IQueryEngine queryEngine = new QueryEngine(conversationStore, _providerCatalog, modelClientFactory, _toolRegistry, toolExecutor, permissionService, _pluginRuntime);
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
        _replHost = replHost ?? new ReplHost(terminalSession, conversationStore, queryEngine, transcriptRenderer, _ptyManager, _taskManager, _providerCatalog, _platformLauncher, _toolRegistry);
        _tuiHost = tuiHost ?? new TuiHost(terminalSession, conversationStore, queryEngine, tuiRenderer, _ptyManager, _taskManager, _providerCatalog, _platformLauncher, _toolRegistry);

        _context = new CommandContext(_featureGate, _toolRegistry, toolExecutor, conversationStore, _taskStore, _taskManager, queryEngine, _providerCatalog, _mcpClient, _lspClient, _pluginCatalog, _pluginRuntime, _platformLauncher, permissionService, transcriptRenderer, version, _legacySettingsLoader, _memoryFileLoader, _projectMcpConfigLoader);

        _handlers =
        [
            new AskCommandHandler(),
            new ProviderCommandHandler(),
            new PlatformCommandHandler(),
            new TaskCommandHandler(),
            new PluginCommandHandler(),
            new LspCommandHandler(),
            new McpCommandHandler(),
            new SessionCommandHandler(),
            new ToolCommandHandler(),
            new VersionCommandHandler(),
            new AuthCommandHandler(_providerCatalog)
        ];
        var helpHandler = new HelpCommandHandler(_handlers);
        var allHandlers = new ICommandHandler[] { helpHandler }.Concat(_handlers);
        _dispatcher = new CommandDispatcher(allHandlers);

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

        // Handle -p/--print mode: route to ask with the prompt
        if (TryParsePrintMode(args, out var printPrompt) && printPrompt is not null)
        {
            return await ExecuteAskAsync(printPrompt, args, cancellationToken);
        }

        // Handle root positional prompt: single non-flag argument treated as ask prompt
        if (TryParseRootPositionalPrompt(args, out var rootPrompt) && rootPrompt is not null)
        {
            return await ExecuteAskAsync(rootPrompt, args, cancellationToken);
        }

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
        string? provider = null;
        string? model = null;
        string? initialPrompt = null;
        var permissionMode = PermissionMode.Default;
        var continueFlag = false;
        string? resumeQuery = null;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--session" when index + 1 < args.Count:
                    sessionId = args[++index];
                    break;
                case "--provider" when index + 1 < args.Count:
                    provider = args[++index];
                    break;
                case "--model" when index + 1 < args.Count:
                    model = args[++index];
                    break;
                case "--permission-mode" when index + 1 < args.Count:
                    permissionMode = ParsePermissionMode(args[++index]);
                    break;
                case "-c" or "--continue":
                    continueFlag = true;
                    break;
                case "-r" when index + 1 < args.Count:
                case "--resume" when index + 1 < args.Count:
                    resumeQuery = args[++index];
                    break;
                case "-r" or "--resume":
                    // -r/--resume without value: signal resume mode with no specific query
                    resumeQuery = string.Empty;
                    break;
                default:
                    // Treat a single non-flag argument as a positional prompt
                    if (args.Count == 1 && !args[0].StartsWith("-", StringComparison.Ordinal))
                    {
                        initialPrompt = args[0];
                        options = new ReplLaunchOptions(sessionId, model, permissionMode, provider, initialPrompt, continueFlag, resumeQuery);
                        return true;
                    }
                    options = new ReplLaunchOptions();
                    return false;
            }
        }

        options = new ReplLaunchOptions(sessionId, model, permissionMode, provider, initialPrompt, continueFlag, resumeQuery);
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
            _toolRegistry.UnregisterWhere(tool => tool.Name.StartsWith("plugin.", StringComparison.OrdinalIgnoreCase));
            _toolRegistry.RegisterRange(
                _pluginCatalog.Plugins
                    .Where(plugin => plugin.Enabled && plugin.IsValid)
                    .SelectMany(plugin => plugin.Tools.Where(tool => tool.Enabled).Select(tool => new PluginToolProxy(_pluginRuntime, plugin, tool))));
            _pluginsInitialized = true;
        }
        finally
        {
            _pluginInitializationLock.Release();
        }
    }

    private static bool TryParsePrintMode(IReadOnlyList<string> args, out string? prompt)
    {
        prompt = null;
        var printIndex = -1;

        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] is "-p" or "--print")
            {
                printIndex = i;
                break;
            }
        }

        if (printIndex < 0)
        {
            return false;
        }

        // Collect everything after -p/--print as the prompt
        if (printIndex + 1 < args.Count)
        {
            prompt = string.Join(' ', args.Skip(printIndex + 1)).Trim();
        }

        return !string.IsNullOrWhiteSpace(prompt);
    }

    private static bool TryParseRootPositionalPrompt(IReadOnlyList<string> args, out string? prompt)
    {
        prompt = null;

        // A single non-flag argument is treated as a positional prompt
        if (args.Count == 1 && !args[0].StartsWith("-", StringComparison.Ordinal))
        {
            prompt = args[0];
            return true;
        }

        return false;
    }

    private async Task<CommandExecutionResult> ExecuteAskAsync(
        string prompt,
        IReadOnlyList<string> originalArgs,
        CancellationToken cancellationToken)
    {
        try
        {
            var askHandler = _handlers.OfType<AskCommandHandler>().First();
            // Build ask args: original args minus -p/--print, plus the prompt
            var askArgs = originalArgs
                .Where(arg => arg is not "-p" and not "--print")
                .Append(prompt)
                .ToArray();
            return await askHandler.ExecuteAsync(
                _context,
                new CommandRequest(askArgs),
                cancellationToken);
        }
        catch (Exception ex)
        {
            return CommandExecutionResult.Failure(ex.Message);
        }
    }
}
