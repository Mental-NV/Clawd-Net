using ClawdNet.Core.Abstractions;

namespace ClawdNet.Core.Models;

public sealed record CommandContext(
    IFeatureGate FeatureGate,
    IToolRegistry ToolRegistry,
    IToolExecutor ToolExecutor,
    IConversationStore ConversationStore,
    IQueryEngine QueryEngine,
    IMcpClient McpClient,
    ILspClient LspClient,
    IPluginCatalog PluginCatalog,
    IPermissionService PermissionService,
    ITranscriptRenderer TranscriptRenderer,
    string Version);
