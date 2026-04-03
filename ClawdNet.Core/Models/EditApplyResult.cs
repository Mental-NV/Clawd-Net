namespace ClawdNet.Core.Models;

public sealed record EditApplyResult(
    bool Success,
    EditBatch Batch,
    int AppliedFileCount,
    string Summary,
    string Diff,
    string? Error = null);
