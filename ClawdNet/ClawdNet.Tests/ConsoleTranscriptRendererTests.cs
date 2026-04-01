using ClawdNet.Core.Models;
using ClawdNet.Terminal.Rendering;

namespace ClawdNet.Tests;

public sealed class ConsoleTranscriptRendererTests
{
    [Fact]
    public void Render_formats_timestamp_role_and_content()
    {
        var renderer = new ConsoleTranscriptRenderer();
        var transcript = new[]
        {
            new ConversationMessage("system", "ready", new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero))
        };

        var rendered = renderer.Render(transcript);

        Assert.Equal("[2026-04-01T12:00:00.0000000+00:00] system: ready", rendered);
    }
}
