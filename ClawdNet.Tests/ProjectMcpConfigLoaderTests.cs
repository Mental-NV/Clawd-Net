using ClawdNet.Core.Services;

namespace ClawdNet.Tests;

public sealed class ProjectMcpConfigLoaderTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "clawdnet-tests", Guid.NewGuid().ToString("N"));
    private readonly ProjectMcpConfigLoader _loader = new();

    public ProjectMcpConfigLoaderTests()
    {
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public void LoadFromProjectTree_returns_empty_when_no_mcp_files()
    {
        var result = _loader.LoadFromProjectTree(_testDir);
        Assert.Empty(result);
    }

    [Fact]
    public void LoadFromProjectTree_loads_mcp_file_from_cwd()
    {
        var mcpContent = """
        {
            "servers": [
                {
                    "name": "demo",
                    "command": "python3",
                    "arguments": ["/path/to/server.py"],
                    "enabled": true,
                    "toolsReadOnly": true
                }
            ]
        }
        """;
        File.WriteAllText(Path.Combine(_testDir, ".mcp.json"), mcpContent);

        var result = _loader.LoadFromProjectTree(_testDir);

        Assert.Single(result);
        var server = result[0];
        Assert.Equal("demo", server.Name);
        Assert.Equal("python3", server.Command);
        Assert.Single(server.Arguments);
        Assert.Equal("/path/to/server.py", server.Arguments[0]);
        Assert.True(server.Enabled);
        Assert.True(server.ToolsReadOnly);
    }

    [Fact]
    public void LoadFromProjectTree_walks_up_parent_directories()
    {
        // Create nested structure
        var childDir = Path.Combine(_testDir, "sub", "deep");
        Directory.CreateDirectory(childDir);

        // .mcp.json in parent
        var parentMcp = """
        {
            "servers": [
                {"name": "parent-server", "command": "echo", "arguments": []}
            ]
        }
        """;
        File.WriteAllText(Path.Combine(_testDir, ".mcp.json"), parentMcp);

        // .mcp.json in child
        var childMcp = """
        {
            "servers": [
                {"name": "child-server", "command": "cat", "arguments": []}
            ]
        }
        """;
        File.WriteAllText(Path.Combine(childDir, ".mcp.json"), childMcp);

        var result = _loader.LoadFromProjectTree(childDir);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Name == "parent-server");
        Assert.Contains(result, s => s.Name == "child-server");
    }

    [Fact]
    public void LoadFromProjectTree_child_wins_on_name_conflict()
    {
        var childDir = Path.Combine(_testDir, "sub");
        Directory.CreateDirectory(childDir);

        // Same-named server in both
        var parentMcp = """
        {
            "servers": [
                {"name": "shared", "command": "echo", "arguments": ["parent"]}
            ]
        }
        """;
        File.WriteAllText(Path.Combine(_testDir, ".mcp.json"), parentMcp);

        var childMcp = """
        {
            "servers": [
                {"name": "shared", "command": "cat", "arguments": ["child"]}
            ]
        }
        """;
        File.WriteAllText(Path.Combine(childDir, ".mcp.json"), childMcp);

        var result = _loader.LoadFromProjectTree(childDir);

        Assert.Single(result);
        Assert.Equal("cat", result[0].Command); // Child wins
    }

    [Fact]
    public void LoadFromFile_handles_disabled_servers()
    {
        var mcpContent = """
        {
            "servers": [
                {"name": "disabled-server", "command": "echo", "arguments": [], "enabled": false},
                {"name": "enabled-server", "command": "cat", "arguments": []}
            ]
        }
        """;
        var path = Path.Combine(_testDir, ".mcp.json");
        File.WriteAllText(path, mcpContent);

        var result = _loader.LoadFromFile(path);

        Assert.Equal(2, result.Count);
        Assert.False(result[0].Enabled);
        Assert.True(result[1].Enabled);
    }

    [Fact]
    public void LoadFromFile_parses_environment_variables()
    {
        var mcpContent = """
        {
            "servers": [
                {
                    "name": "env-server",
                    "command": "python3",
                    "arguments": [],
                    "environment": {
                        "DEMO_VAR": "test-value",
                        "ANOTHER_VAR": "123"
                    }
                }
            ]
        }
        """;
        var path = Path.Combine(_testDir, ".mcp.json");
        File.WriteAllText(path, mcpContent);

        var result = _loader.LoadFromFile(path);

        Assert.Single(result);
        Assert.Equal("test-value", result[0].Environment!["DEMO_VAR"]);
        Assert.Equal("123", result[0].Environment!["ANOTHER_VAR"]);
    }

    [Fact]
    public void LoadFromFile_handles_invalid_json_gracefully()
    {
        var path = Path.Combine(_testDir, ".mcp.json");
        File.WriteAllText(path, "{invalid json}");

        var result = _loader.LoadFromFile(path);

        Assert.Empty(result);
    }

    [Fact]
    public void LoadFromFile_returns_empty_for_missing_file()
    {
        var result = _loader.LoadFromFile(Path.Combine(_testDir, "nonexistent.json"));
        Assert.Empty(result);
    }

    [Fact]
    public void LoadFromFile_skips_servers_without_name_or_command()
    {
        var mcpContent = """
        {
            "servers": [
                {"command": "echo"},
                {"name": "valid-server", "command": "cat"},
                {"name": "no-command"}
            ]
        }
        """;
        var path = Path.Combine(_testDir, ".mcp.json");
        File.WriteAllText(path, mcpContent);

        var result = _loader.LoadFromFile(path);

        Assert.Single(result);
        Assert.Equal("valid-server", result[0].Name);
    }
}
