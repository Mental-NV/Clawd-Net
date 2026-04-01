using ClawdNet.Core.Models;

namespace ClawdNet.Core.Abstractions;

public interface ITranscriptRenderer
{
    string Render(IReadOnlyList<TranscriptEntry> entries);
}
