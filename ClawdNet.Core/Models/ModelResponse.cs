namespace ClawdNet.Core.Models;

public sealed record ModelResponse(
    string Model,
    IReadOnlyList<ModelContentBlock> ContentBlocks,
    string StopReason);
