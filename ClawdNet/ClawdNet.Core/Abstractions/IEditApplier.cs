using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface IEditApplier
{
    Task<EditApplyResult> ApplyAsync(EditBatch batch, CancellationToken cancellationToken);
}
