namespace ClawdNet.Runtime.VertexAI;

/// <summary>
/// Resolves short model names to Vertex AI model IDs (model-name@YYYYMMDD format).
/// </summary>
public static class VertexAIModelIdResolver
{
    private static readonly Dictionary<string, string> ModelIdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "claude-3-5-haiku", "claude-3-5-haiku@20241022" },
        { "claude-3-5-sonnet", "claude-3-5-sonnet-v2@20241022" },
        { "claude-3-5-sonnet-v2", "claude-3-5-sonnet-v2@20241022" },
        { "claude-3-7-sonnet", "claude-3-7-sonnet@20250219" },
        { "claude-haiku-4-5", "claude-haiku-4-5@20251001" },
        { "claude-sonnet-4", "claude-sonnet-4@20250514" },
        { "claude-sonnet-4-5", "claude-sonnet-4-5@20250929" },
        { "claude-opus-4", "claude-opus-4@20250514" },
        { "claude-opus-4-1", "claude-opus-4-1@20250805" },
        { "claude-opus-4-5", "claude-opus-4-5@20251101" },
    };

    /// <summary>
    /// Resolves a model name to a Vertex AI model ID.
    /// If the model name already contains '@', it is assumed to already be in Vertex format and returned as-is.
    /// </summary>
    public static string Resolve(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return model;
        }

        // Already in Vertex format
        if (model.Contains('@', StringComparison.Ordinal))
        {
            return model;
        }

        if (ModelIdMap.TryGetValue(model, out var vertexId))
        {
            return vertexId;
        }

        // Unknown model — return as-is and let the API return a clear error
        return model;
    }
}
