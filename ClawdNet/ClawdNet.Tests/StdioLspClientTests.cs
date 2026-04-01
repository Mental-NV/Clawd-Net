using System.Text.Json.Nodes;
using ClawdNet.Runtime.Protocols;

namespace ClawdNet.Tests;

public sealed class StdioLspClientTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "clawdnet-lsp-client", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Stdio_client_initializes_serves_queries_and_tracks_diagnostics()
    {
        var scriptPath = await WriteServerScriptAsync();
        var configDirectory = Path.Combine(_dataRoot, "config");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(configDirectory, "lsp.json"),
            $$"""
            {
              "servers": [
                {
                  "name": "csharp",
                  "command": "python3",
                  "arguments": ["{{scriptPath}}"],
                  "fileExtensions": [".cs"],
                  "languageId": "csharp",
                  "enabled": true
                }
              ]
            }
            """);

        var filePath = Path.Combine(_dataRoot, "sample.cs");
        await File.WriteAllTextAsync(filePath, "class A {}");

        await using var client = new StdioLspClient(_dataRoot);
        await client.InitializeAsync(CancellationToken.None);
        await client.SyncFileAsync(filePath, "class Broken {}", CancellationToken.None);

        var state = await client.PingAsync("csharp", CancellationToken.None);
        var definitions = await client.GetDefinitionsAsync(filePath, 1, 2, CancellationToken.None);
        var references = await client.GetReferencesAsync(filePath, 1, 2, CancellationToken.None);
        var hover = await client.GetHoverAsync(filePath, 1, 2, CancellationToken.None);
        var diagnostics = await client.GetDiagnosticsAsync(filePath, CancellationToken.None);

        Assert.NotNull(state);
        Assert.True(state!.Connected);
        Assert.Single(definitions);
        Assert.Single(references);
        Assert.Equal("hover text", hover);
        Assert.Single(diagnostics);
        Assert.Equal("Broken type", diagnostics[0].Message);
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

            OPEN_DOCS = {}

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

            def publish(uri, text):
                diagnostics = []
                if "Broken" in text:
                    diagnostics.append({
                        "range": {"start": {"line": 0, "character": 6}, "end": {"line": 0, "character": 12}},
                        "severity": 1,
                        "message": "Broken type"
                    })
                write_message({"jsonrpc": "2.0", "method": "textDocument/publishDiagnostics", "params": {"uri": uri, "diagnostics": diagnostics}})

            while True:
                message = read_message()
                if message is None:
                    break

                method = message.get("method")
                msg_id = message.get("id")
                params = message.get("params", {})
                if method == "initialize":
                    write_message({"jsonrpc": "2.0", "id": msg_id, "result": {"capabilities": {}}})
                elif method == "initialized":
                    continue
                elif method == "workspace/symbol":
                    write_message({"jsonrpc": "2.0", "id": msg_id, "result": []})
                elif method == "textDocument/didOpen":
                    text_document = params.get("textDocument", {})
                    OPEN_DOCS[text_document.get("uri")] = text_document.get("text", "")
                elif method == "textDocument/didChange":
                    text_document = params.get("textDocument", {})
                    changes = params.get("contentChanges", [])
                    if changes:
                        OPEN_DOCS[text_document.get("uri")] = changes[-1].get("text", "")
                elif method == "textDocument/didSave":
                    text_document = params.get("textDocument", {})
                    uri = text_document.get("uri")
                    text = params.get("text", OPEN_DOCS.get(uri, ""))
                    publish(uri, text)
                elif method == "textDocument/definition":
                    uri = params.get("textDocument", {}).get("uri")
                    write_message({"jsonrpc": "2.0", "id": msg_id, "result": [{"uri": uri, "range": {"start": {"line": 0, "character": 1}, "end": {"line": 0, "character": 2}}}]})
                elif method == "textDocument/references":
                    uri = params.get("textDocument", {}).get("uri")
                    write_message({"jsonrpc": "2.0", "id": msg_id, "result": [{"uri": uri, "range": {"start": {"line": 1, "character": 3}, "end": {"line": 1, "character": 4}}}]})
                elif method == "textDocument/hover":
                    write_message({"jsonrpc": "2.0", "id": msg_id, "result": {"contents": "hover text"}})
                elif method == "shutdown":
                    write_message({"jsonrpc": "2.0", "id": msg_id, "result": {}})
                    break
                elif method == "exit":
                    break
                else:
                    write_message({"jsonrpc": "2.0", "id": msg_id, "error": {"message": f"unknown method {method}"}})
            """);
        return scriptPath;
    }
}
