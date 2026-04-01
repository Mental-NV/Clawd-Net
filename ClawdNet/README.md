# ClawdNet

`ClawdNet` is the .NET 10 replatforming workspace for the Bun-first TypeScript
CLI that lives in the parent repository.

This initial implementation provides:

- a production-shaped solution layout
- a headless `ask` workflow for non-interactive conversations
- typed tool/runtime abstractions
- JSON-backed session persistence
- a console transcript renderer
- characterization-style tests for core behaviors

It is intentionally the first migration slice rather than full feature parity.

## Commands

From the repository root:

```bash
dotnet build ClawdNet.slnx
dotnet test ClawdNet.slnx
```

Run the app directly:

```bash
dotnet run --project ClawdNet/ClawdNet.App -- --version
dotnet run --project ClawdNet/ClawdNet.App
dotnet run --project ClawdNet/ClawdNet.App -- --session <session-id>
dotnet run --project ClawdNet/ClawdNet.App -- --model claude-sonnet-4-5
dotnet run --project ClawdNet/ClawdNet.App -- --permission-mode accept-edits
dotnet run --project ClawdNet/ClawdNet.App -- ask "Explain this project"
dotnet run --project ClawdNet/ClawdNet.App -- ask --permission-mode bypass-permissions "Inspect this repo"
dotnet run --project ClawdNet/ClawdNet.App -- ask --json "Summarize the current milestone"
dotnet run --project ClawdNet/ClawdNet.App -- ask --session <session-id> "Continue"
dotnet run --project ClawdNet/ClawdNet.App -- session new "First Slice"
dotnet run --project ClawdNet/ClawdNet.App -- session list
dotnet run --project ClawdNet/ClawdNet.App -- plugin list
dotnet run --project ClawdNet/ClawdNet.App -- plugin reload
dotnet run --project ClawdNet/ClawdNet.App -- mcp list
dotnet run --project ClawdNet/ClawdNet.App -- mcp ping <server-name>
dotnet run --project ClawdNet/ClawdNet.App -- mcp tools
dotnet run --project ClawdNet/ClawdNet.App -- mcp tools <server-name>
dotnet run --project ClawdNet/ClawdNet.App -- lsp list
dotnet run --project ClawdNet/ClawdNet.App -- lsp ping <server-name>
dotnet run --project ClawdNet/ClawdNet.App -- lsp diagnostics <path>
dotnet run --project ClawdNet/ClawdNet.App -- tool echo "hello world"
```

Set your Anthropic API key before using `ask`:

```bash
export ANTHROPIC_API_KEY=your_key_here
```

Run from inside the `ClawdNet/` workspace:

```bash
dotnet build ../ClawdNet.slnx
dotnet test ../ClawdNet.slnx
dotnet run --project ClawdNet.App -- --version
```

## Current Supported CLI Surface

- `clawdnet` interactive mode
- `clawdnet --session <id>`
- `clawdnet --model <name>`
- `clawdnet --permission-mode <mode>`
- `--version`
- `ask <prompt>`
- `ask --session <id> <prompt>`
- `ask --model <name> <prompt>`
- `ask --permission-mode <mode> <prompt>`
- `ask --json <prompt>`
- `session new [title]`
- `session list`
- `plugin list`
- `plugin reload`
- `mcp list`
- `mcp ping <server>`
- `mcp tools [server]`
- `lsp list`
- `lsp ping <server>`
- `lsp diagnostics <path>`
- `tool echo <text>`

## MCP Configuration

Configure stdio MCP servers in:

```text
<LocalApplicationData>/ClawdNet/config/mcp.json
```

Example:

```json
{
  "servers": [
    {
      "name": "demo",
      "command": "python3",
      "arguments": ["/absolute/path/to/server.py"],
      "enabled": true,
      "toolsReadOnly": true,
      "environment": {
        "DEMO_FLAG": "1"
      }
    }
  ]
}
```

Configured server tools are exposed to the model with names like `mcp.demo.echo`.

## LSP Configuration

Configure stdio language servers in:

```text
<LocalApplicationData>/ClawdNet/config/lsp.json
```

Example:

```json
{
  "servers": [
    {
      "name": "csharp",
      "command": "python3",
      "arguments": ["/absolute/path/to/fake-or-real-lsp-server.py"],
      "fileExtensions": [".cs", ".csx"],
      "languageId": "csharp",
      "enabled": true,
      "environment": {
        "DOTNET_ENVIRONMENT": "Development"
      }
    }
  ]
}
```

Built-in LSP tools are exposed to the model as `lsp_definition`, `lsp_references`, `lsp_hover`, and `lsp_diagnostics`.

## Plugin Configuration

Discover local plugins in:

```text
<LocalApplicationData>/ClawdNet/plugins/<plugin-id>/plugin.json
```

Example:

```json
{
  "name": "demo",
  "version": "1.0.0",
  "enabled": true,
  "mcpServers": [
    {
      "name": "echo",
      "command": "python3",
      "arguments": ["/absolute/path/to/mcp_server.py"],
      "toolsReadOnly": true
    }
  ],
  "lspServers": [
    {
      "name": "csharp",
      "command": "python3",
      "arguments": ["/absolute/path/to/lsp_server.py"],
      "fileExtensions": [".cs"],
      "languageId": "csharp"
    }
  ]
}
```

Plugin-provided server names are scoped automatically, so the example above contributes `demo.echo` and `demo.csharp`.
