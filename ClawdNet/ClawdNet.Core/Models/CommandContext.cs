using ClawdNet.Core.Abstractions;

namespace ClawdNet.Core.Models;

public sealed record CommandContext(
    IFeatureGate FeatureGate,
    IToolRegistry ToolRegistry,
    IToolExecutor ToolExecutor,
    IConversationStore ConversationStore,
    ITaskStore TaskStore,
    ITaskManager TaskManager,
    IQueryEngine QueryEngine,
    IProviderCatalog ProviderCatalog,
    IMcpClient McpClient,
    ILspClient LspClient,
    IPluginCatalog PluginCatalog,
    IPluginRuntime PluginRuntime,
    IPlatformLauncher PlatformLauncher,
    IPermissionService PermissionService,
    ITranscriptRenderer TranscriptRenderer,
    string Version);
