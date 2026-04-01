using ClawdNet.Core.Abstractions;

namespace ClawdNet.Core.Models;

public sealed record CommandContext(
    IFeatureGate FeatureGate,
    IToolExecutor ToolExecutor,
    IConversationStore ConversationStore,
    IQueryEngine QueryEngine,
    IMcpClient McpClient,
    IPermissionService PermissionService,
    ITranscriptRenderer TranscriptRenderer,
    string Version);
