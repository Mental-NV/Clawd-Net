namespace ClawdNet.Core.Models;

/// <summary>
/// Thinking mode for model responses. Controls whether extended thinking/reasoning is enabled.
/// </summary>
public enum ThinkingMode
{
    /// <summary>
    /// Adaptive - let the model decide whether to use thinking based on the prompt.
    /// </summary>
    Adaptive,

    /// <summary>
    /// Enabled - force extended thinking/reasoning.
    /// </summary>
    Enabled,

    /// <summary>
    /// Disabled - disable extended thinking.
    /// </summary>
    Disabled
}
