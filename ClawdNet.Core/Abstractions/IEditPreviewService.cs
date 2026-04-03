using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IEditPreviewService
{
    Task<EditPreview> PreviewAsync(EditBatch batch, CancellationToken cancellationToken);
}
