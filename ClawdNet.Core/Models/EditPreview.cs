namespace ClawdNet.Core.Models;

public sealed record EditPreview(
    bool Success,
    EditBatch Batch,
    int FileCount,
    string Summary,
    string Diff,
    string? Error = null);
