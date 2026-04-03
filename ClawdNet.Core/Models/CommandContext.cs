using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Services;

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
    string Version,
    LegacySettingsLoader? LegacySettingsLoader = null,
    MemoryFileLoader? MemoryFileLoader = null,
    ProjectMcpConfigLoader? ProjectMcpConfigLoader = null);
