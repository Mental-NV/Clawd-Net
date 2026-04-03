namespace ClawdNet.Core.Models;

public sealed record EditBatch(
    IReadOnlyList<FileEdit> Edits);
