namespace ClawdNet.Core.Models;

/// <summary>
/// Effort level for model responses. Controls how much reasoning/processing the model performs.
/// </summary>
public enum EffortLevel
{
    /// <summary>
    /// Default effort level.
    /// </summary>
    Medium,

    /// <summary>
    /// Lower effort - faster responses with less detailed reasoning.
    /// </summary>
    Low,

    /// <summary>
    /// Higher effort - more detailed responses with additional reasoning.
    /// </summary>
    High
}
