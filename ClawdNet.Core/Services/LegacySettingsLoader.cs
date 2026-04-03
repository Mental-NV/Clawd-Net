using System.Text.Json;

namespace ClawdNet.Core.Services;

/// <summary>
/// Loads and merges legacy TypeScript CLI settings files.
/// Settings come from multiple sources with later sources overriding earlier ones:
/// 1. User settings (~/.claude/settings.json)
/// 2. Project settings (.claude/settings.json)
/// 3. Local settings (.claude/settings.local.json)
/// </summary>
public class LegacySettingsLoader
{
    private readonly Dictionary<string, JsonElement> _cache = new();

    /// <summary>
    /// Loads and merges settings from all legacy sources.
    /// Returns an empty dictionary if no legacy settings files exist.
    /// </summary>
    public Dictionary<string, object?> LoadMergedSettings(string? cwd = null)
    {
        cwd ??= Environment.CurrentDirectory;
        var merged = new Dictionary<string, object?>();

        // Load in priority order (later overrides earlier)
        var sources = new List<string>
        {
            LegacyConfigPaths.GetUserSettingsPath(),
            LegacyConfigPaths.GetProjectSettingsPath(cwd),
            LegacyConfigPaths.GetLocalSettingsPath(cwd)
        };

        foreach (var sourcePath in sources)
        {
            var settings = LoadSettingsFile(sourcePath);
            if (settings is not null)
            {
                MergeInto(merged, settings);
            }
        }

        return merged;
    }

    /// <summary>
    /// Loads settings from an additional directory (for --add-dir support).
    /// Loads both settings.json and settings.local.json from the directory's .claude/ subdirectory.
    /// </summary>
    public Dictionary<string, object?> LoadSettingsFromDirectory(string directory)
    {
        var result = new Dictionary<string, object?>();

        var settingsPath = Path.Combine(directory, ".claude", "settings.json");
        var settings = LoadSettingsFile(settingsPath);
        if (settings is not null)
        {
            MergeInto(result, settings);
        }

        var localPath = Path.Combine(directory, ".claude", "settings.local.json");
        var localSettings = LoadSettingsFile(localPath);
        if (localSettings is not null)
        {
            MergeInto(result, localSettings);
        }

        return result;
    }

    /// <summary>
    /// Loads a single settings file and returns its contents as a dictionary.
    /// Returns null if the file doesn't exist or is invalid.
    /// </summary>
    private Dictionary<string, object?>? LoadSettingsFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // Check cache
        if (_cache.TryGetValue(path, out var cached))
        {
            return ConvertToDictionary(cached);
        }

        try
        {
            var content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content))
            {
                _cache[path] = JsonDocument.Parse("{}").RootElement;
                return new Dictionary<string, object?>();
            }

            using var doc = JsonDocument.Parse(content);
            var element = doc.RootElement.Clone();
            _cache[path] = element;
            return ConvertToDictionary(element);
        }
        catch (JsonException)
        {
            // Invalid JSON - skip silently
            return null;
        }
        catch (IOException)
        {
            // File read error - skip silently
            return null;
        }
    }

    /// <summary>
    /// Merges source settings into target. Source values override target values.
    /// Arrays are concatenated and deduplicated.
    /// </summary>
    private static void MergeInto(Dictionary<string, object?> target, Dictionary<string, object?> source)
    {
        foreach (var kvp in source)
        {
            if (target.TryGetValue(kvp.Key, out var existing))
            {
                // Merge arrays, replace scalars
                if (existing is List<object?> existingList && kvp.Value is List<object?> sourceList)
                {
                    // Concatenate and deduplicate
                    var merged = new List<object?>(existingList);
                    foreach (var item in sourceList)
                    {
                        if (!merged.Contains(item))
                        {
                            merged.Add(item);
                        }
                    }
                    target[kvp.Key] = merged;
                }
                else
                {
                    // For non-array values, source wins
                    // (This matches legacy: later source wins)
                    target[kvp.Key] = kvp.Value;
                }
            }
            else
            {
                target[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Converts a JsonElement to a dictionary of primitive values.
    /// Only handles top-level properties (no nested object traversal).
    /// </summary>
    private static Dictionary<string, object?> ConvertToDictionary(JsonElement element)
    {
        var result = new Dictionary<string, object?>();

        if (element.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ConvertJsonValue(property.Value);
        }

        return result;
    }

    private static object? ConvertJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
            JsonValueKind.Object => element.ToString(), // Store nested objects as raw JSON string
            _ => null
        };
    }
}
