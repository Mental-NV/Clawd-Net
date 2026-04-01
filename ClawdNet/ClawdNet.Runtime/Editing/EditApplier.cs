using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Editing;

public sealed class EditApplier : IEditApplier
{
    private readonly ILspClient _lspClient;

    public EditApplier(ILspClient lspClient)
    {
        _lspClient = lspClient;
    }

    public async Task<EditApplyResult> ApplyAsync(EditBatch batch, CancellationToken cancellationToken)
    {
        var planning = EditBatchPlanner.Plan(batch);
        if (!planning.Success)
        {
            return new EditApplyResult(false, batch, 0, "Edit batch is invalid.", string.Empty, planning.Error);
        }

        var applied = new List<PreparedFileEdit>();
        var lspWarnings = new List<string>();
        try
        {
            foreach (var file in planning.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (file.Operation)
                {
                    case EditOperation.Create:
                    case EditOperation.Patch:
                    {
                        var directory = Path.GetDirectoryName(file.Path);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        await File.WriteAllTextAsync(file.Path, file.UpdatedContent ?? string.Empty, cancellationToken);
                        applied.Add(file);
                        try
                        {
                            await _lspClient.SyncFileAsync(file.Path, file.UpdatedContent ?? string.Empty, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            lspWarnings.Add($"{Path.GetFileName(file.Path)}: {ex.Message}");
                        }
                        break;
                    }
                    case EditOperation.Delete:
                        File.Delete(file.Path);
                        applied.Add(file);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            await RollbackAsync(applied, cancellationToken);
            return new EditApplyResult(false, batch, 0, "Failed to apply edit batch.", string.Empty, ex.Message);
        }

        var diff = EditDiffFormatter.Format(planning.Files);
        var summary = $"Applied edit batch to {planning.Files.Count} file(s).";
        if (lspWarnings.Count > 0)
        {
            summary = $"{summary} LSP sync warnings: {string.Join("; ", lspWarnings)}";
        }

        return new EditApplyResult(true, batch, planning.Files.Count, summary, diff);
    }

    private static async Task RollbackAsync(IEnumerable<PreparedFileEdit> applied, CancellationToken cancellationToken)
    {
        foreach (var file in applied.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (file.Operation)
            {
                case EditOperation.Create:
                    if (File.Exists(file.Path))
                    {
                        File.Delete(file.Path);
                    }
                    break;
                case EditOperation.Delete:
                case EditOperation.Patch:
                    if (file.OriginalContent is not null)
                    {
                        var directory = Path.GetDirectoryName(file.Path);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        await File.WriteAllTextAsync(file.Path, file.OriginalContent, cancellationToken);
                    }
                    break;
            }
        }
    }
}
