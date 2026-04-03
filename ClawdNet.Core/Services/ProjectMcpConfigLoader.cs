using System.Text.Json;

namespace ClawdNet.Core.Services;

/// <summary>
/// Represents an MCP server definition from a .mcp.json config file.
/// </summary>
public record McpServerDefinition(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    bool Enabled = true,
    bool ToolsReadOnly = true,
    IReadOnlyDictionary<string, string>? Environment = null);

/// <summary>
/// Loads MCP server definitions from project-level .mcp.json files.
/// Walks up from the current directory to find all .mcp.json files (like legacy CLI).
/// </summary>
public class ProjectMcpConfigLoader
{
    /// <summary>
    /// Loads MCP servers from .mcp.json files starting at cwd and walking up parent directories.
    /// Returns servers in order from closest to furthest (closest wins on name conflicts).
    /// </summary>
    public IReadOnlyList<McpServerDefinition> LoadFromProjectTree(string? cwd = null)
    {
        cwd ??= Environment.CurrentDirectory;
        var allServers = new List<McpServerDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Walk up from cwd to root, collecting .mcp.json files
        var current = cwd;
        var mcpFiles = new List<string>();

        while (!string.IsNullOrEmpty(current))
        {
            var mcpPath = Path.Combine(current, ".mcp.json");
            if (File.Exists(mcpPath))
            {
                mcpFiles.Add(mcpPath);
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current)
            {
                break;
            }
            current = parent;
        }

        // Process from closest to furthest (closest wins via seen set)
        foreach (var mcpFile in mcpFiles)
        {
            var servers = LoadFromFile(mcpFile);
            foreach (var server in servers)
            {
                if (seen.Add(server.Name))
                {
                    allServers.Add(server);
                }
            }
        }

        return allServers;
    }

    /// <summary>
    /// Loads MCP servers from a single .mcp.json file.
    /// </summary>
    public IReadOnlyList<McpServerDefinition> LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<McpServerDefinition>();
        }

        try
        {
            var content = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("servers", out var serversElement))
            {
                return Array.Empty<McpServerDefinition>();
            }

            var servers = new List<McpServerDefinition>();
            foreach (var serverElement in serversElement.EnumerateArray())
            {
                var server = ParseServerDefinition(serverElement);
                if (server is not null)
                {
                    servers.Add(server);
                }
            }

            return servers;
        }
        catch (JsonException)
        {
            return Array.Empty<McpServerDefinition>();
        }
        catch (IOException)
        {
            return Array.Empty<McpServerDefinition>();
        }
    }

    private static McpServerDefinition? ParseServerDefinition(JsonElement element)
    {
        if (!element.TryGetProperty("name", out var nameElement))
        {
            return null;
        }

        if (!element.TryGetProperty("command", out var commandElement))
        {
            return null;
        }

        var name = nameElement.GetString();
        var command = commandElement.GetString();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var args = new List<string>();
        if (element.TryGetProperty("arguments", out var argsElement) && argsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var arg in argsElement.EnumerateArray())
            {
                if (arg.ValueKind == JsonValueKind.String)
                {
                    args.Add(arg.GetString()!);
                }
            }
        }

        var enabled = true;
        if (element.TryGetProperty("enabled", out var enabledElement))
        {
            enabled = enabledElement.GetBoolean();
        }

        var toolsReadOnly = true;
        if (element.TryGetProperty("toolsReadOnly", out var toolsReadOnlyElement))
        {
            toolsReadOnly = toolsReadOnlyElement.GetBoolean();
        }

        var env = new Dictionary<string, string>();
        if (element.TryGetProperty("environment", out var envElement) && envElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in envElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    env[prop.Name] = prop.Value.GetString()!;
                }
            }
        }

        return new McpServerDefinition(
            name,
            command,
            args,
            enabled,
            toolsReadOnly,
            env);
    }
}
