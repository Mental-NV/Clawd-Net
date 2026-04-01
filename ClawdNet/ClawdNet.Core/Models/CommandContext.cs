using ClawdNet.Core.Abstractions;

namespace ClawdNet.Core.Models;

public sealed record CommandContext(
    IFeatureGate FeatureGate,
    IToolExecutor ToolExecutor,
    ISessionStore SessionStore,
    ITranscriptRenderer TranscriptRenderer,
    string Version);
