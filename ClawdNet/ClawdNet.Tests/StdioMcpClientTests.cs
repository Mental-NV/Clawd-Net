using System.Text.Json.Nodes;
using ClawdNet.Runtime.Protocols;

namespace ClawdNet.Tests;

public sealed class StdioMcpClientTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-mcp-client", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Stdio_client_initializes_lists_tools_and_invokes_tool()
    {
        var scriptPath = await WriteServerScriptAsync();
        var configDirectory = Path.Combine(_dataRoot, "config");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(configDirectory, "mcp.json"),
            $$"""
            {
              "servers": [
                {
                  "name": "demo",
                  "command": "python3",
                  "arguments": ["{{scriptPath}}"],
                  "enabled": true,
                  "toolsReadOnly": true
                }
              ]
            }
            """);

        await using var client = new StdioMcpClient(_dataRoot);
        await client.InitializeAsync(CancellationToken.None);

        var state = await client.PingAsync("demo", CancellationToken.None);
        var tools = await client.GetToolsAsync("demo", CancellationToken.None);
        var result = await client.InvokeToolAsync("demo", "echo", new JsonObject { ["text"] = "hello" }, CancellationToken.None);

        Assert.NotNull(state);
        Assert.True(state!.Connected);
        Assert.Single(tools);
        Assert.Equal("echo", tools[0].Name);
        Assert.True(result.Success);
        Assert.Equal("mcp:hello", result.Output);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, true);
        }
    }

    private async Task<string> WriteServerScriptAsync()
    {
        Directory.CreateDirectory(_dataRoot);
        var scriptPath = Path.Combine(_dataRoot, "server.py");
        await File.WriteAllTextAsync(
            scriptPath,
            """
            import json
            import sys

            def read_message():
                headers = {}
                while True:
                    line = sys.stdin.buffer.readline()
                    if not line:
                        return None
                    if line == b"\r\n":
                        break
                    name, value = line.decode("ascii").split(":", 1)
                    headers[name.strip().lower()] = value.strip()
                length = int(headers["content-length"])
                payload = sys.stdin.buffer.read(length)
                return json.loads(payload.decode("utf-8"))

            def write_message(message):
                payload = json.dumps(message).encode("utf-8")
                sys.stdout.buffer.write(f"Content-Length: {len(payload)}\r\n\r\n".encode("ascii"))
                sys.stdout.buffer.write(payload)
                sys.stdout.buffer.flush()

            while True:
                message = read_message()
                if message is None:
                    break

                method = message.get("method")
                msg_id = message.get("id")
                if method == "initialize":
                    write_message({"jsonrpc": "2.0", "id": msg_id, "result": {"protocolVersion": "2024-11-05", "capabilities": {}}})
                elif method == "notifications/initialized":
                    continue
                elif method == "tools/list":
                    write_message({"jsonrpc": "2.0", "id": msg_id, "result": {"tools": [{"name": "echo", "description": "Echo text", "inputSchema": {"type": "object", "properties": {"text": {"type": "string"}}}}]}})
                elif method == "tools/call":
                    text = message.get("params", {}).get("arguments", {}).get("text", "")
                    write_message({"jsonrpc": "2.0", "id": msg_id, "result": {"content": [{"type": "text", "text": f"mcp:{text}"}]}})
                elif method == "shutdown":
                    write_message({"jsonrpc": "2.0", "id": msg_id, "result": {}})
                    break
                else:
                    write_message({"jsonrpc": "2.0", "id": msg_id, "error": {"message": f"unknown method {method}"}})
            """);
        return scriptPath;
    }
}
