using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IReviewableEditTool : ITool
{
    Task<EditPreview> PreviewAsync(ToolExecutionRequest request, CancellationToken cancellationToken);

    Task<EditApplyResult> ApplyAsync(ToolExecutionRequest request, CancellationToken cancellationToken);
}
