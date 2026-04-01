using ClawdNet.Core.Abstractions;
using ClawdNet.Core.Models;

namespace ClawdNet.Runtime.Editing;

public sealed class EditPreviewService : IEditPreviewService
{
    public Task<EditPreview> PreviewAsync(EditBatch batch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var planning = EditBatchPlanner.Plan(batch);
        if (!planning.Success)
        {
            return Task.FromResult(new EditPreview(
                false,
                batch,
                0,
                "Edit batch is invalid.",
                string.Empty,
                planning.Error));
        }

        var summary = $"Edit batch touches {planning.Files.Count} file(s): {string.Join(", ", planning.Files.Select(file => Path.GetFileName(file.Path)))}";
        var diff = EditDiffFormatter.Format(planning.Files);
        return Task.FromResult(new EditPreview(true, batch, planning.Files.Count, summary, diff));
    }
}
