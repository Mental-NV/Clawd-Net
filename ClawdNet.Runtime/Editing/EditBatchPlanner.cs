using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Editing;

internal static class EditBatchPlanner
{
    public static PlanningResult Plan(EditBatch batch)
    {
        if (batch.Edits.Count == 0)
        {
            return PlanningResult.Failure("Edit batch must include at least one file edit.");
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prepared = new List<PreparedFileEdit>();
        foreach (var edit in batch.Edits)
        {
            if (string.IsNullOrWhiteSpace(edit.Path))
            {
                return PlanningResult.Failure("Each file edit requires a path.");
            }

            if (!seenPaths.Add(edit.Path))
            {
                return PlanningResult.Failure($"Duplicate edit path '{edit.Path}' is not allowed in one batch.");
            }

            switch (edit.Operation)
            {
                case EditOperation.Patch:
                {
                    if (!File.Exists(edit.Path))
                    {
                        return PlanningResult.Failure($"Patch target '{edit.Path}' does not exist.");
                    }

                    if (edit.Hunks is null || edit.Hunks.Count == 0)
                    {
                        return PlanningResult.Failure($"Patch edit for '{edit.Path}' requires at least one hunk.");
                    }

                    var originalContent = File.ReadAllText(edit.Path);
                    var updatedContent = originalContent;
                    foreach (var hunk in edit.Hunks)
                    {
                        if (string.IsNullOrEmpty(hunk.OldText))
                        {
                            return PlanningResult.Failure($"Patch hunk for '{edit.Path}' requires non-empty oldText.");
                        }

                        var matchIndex = updatedContent.IndexOf(hunk.OldText, StringComparison.Ordinal);
                        if (matchIndex < 0)
                        {
                            return PlanningResult.Failure($"Patch hunk for '{edit.Path}' did not match the current file contents.");
                        }

                        var duplicateMatchIndex = updatedContent.IndexOf(
                            hunk.OldText,
                            matchIndex + hunk.OldText.Length,
                            StringComparison.Ordinal);
                        if (duplicateMatchIndex >= 0)
                        {
                            return PlanningResult.Failure($"Patch hunk for '{edit.Path}' matched multiple locations. Hunks must be unambiguous.");
                        }

                        updatedContent = string.Concat(
                            updatedContent.AsSpan(0, matchIndex),
                            hunk.NewText,
                            updatedContent.AsSpan(matchIndex + hunk.OldText.Length));
                    }

                    prepared.Add(new PreparedFileEdit(edit.Path, edit.Operation, originalContent, updatedContent));
                    break;
                }
                case EditOperation.Create:
                {
                    if (File.Exists(edit.Path))
                    {
                        return PlanningResult.Failure($"Create target '{edit.Path}' already exists.");
                    }

                    if (edit.Content is null)
                    {
                        return PlanningResult.Failure($"Create edit for '{edit.Path}' requires content.");
                    }

                    prepared.Add(new PreparedFileEdit(edit.Path, edit.Operation, null, edit.Content));
                    break;
                }
                case EditOperation.Delete:
                {
                    if (!File.Exists(edit.Path))
                    {
                        return PlanningResult.Failure($"Delete target '{edit.Path}' does not exist.");
                    }

                    prepared.Add(new PreparedFileEdit(edit.Path, edit.Operation, File.ReadAllText(edit.Path), null));
                    break;
                }
                default:
                    return PlanningResult.Failure($"Unsupported edit operation '{edit.Operation}'.");
            }
        }

        return PlanningResult.FromFiles(prepared);
    }
}

internal sealed record PreparedFileEdit(
    string Path,
    EditOperation Operation,
    string? OriginalContent,
    string? UpdatedContent);

internal sealed record PlanningResult(
    bool Success,
    IReadOnlyList<PreparedFileEdit> Files,
    string? Error)
{
    public static PlanningResult Failure(string error) => new(false, [], error);

    public static PlanningResult FromFiles(IReadOnlyList<PreparedFileEdit> files) => new(true, files, null);
}
