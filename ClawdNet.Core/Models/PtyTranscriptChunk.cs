namespace ClawdNet.Core.Models;

/// <summary>
/// Represents a single output chunk from a PTY session that can be persisted to a transcript.
/// </summary>
public sealed record PtyTranscriptChunk(
    string Text,
    bool IsError,
    int SequenceNumber,
    DateTimeOffset TimestampUtc);
