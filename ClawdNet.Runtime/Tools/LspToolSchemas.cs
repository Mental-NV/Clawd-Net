using System.Text.Json.Nodes;

namespace ClawdNet.Runtime.Tools;

internal static class LspToolSchemas
{
    public static JsonObject PositionSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = new JsonObject { ["type"] = "string" },
                ["line"] = new JsonObject { ["type"] = "integer" },
                ["character"] = new JsonObject { ["type"] = "integer" }
            },
            ["required"] = new JsonArray("path", "line", "character")
        };
    }

    public static JsonObject PathSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray("path")
        };
    }

    public static (string? Path, int Line, int Character, string? Error) ParsePosition(JsonNode? input)
    {
        var path = input?["path"]?.GetValue<string>();
        var line = input?["line"]?.GetValue<int?>();
        var character = input?["character"]?.GetValue<int?>();

        if (string.IsNullOrWhiteSpace(path) || line is null || character is null)
        {
            return (null, 0, 0, "LSP tool requires 'path', 'line', and 'character'.");
        }

        return (path, line.Value, character.Value, null);
    }
}
