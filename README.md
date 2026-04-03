# ClawdNet

`ClawdNet` is the .NET 10 replatforming workspace for the Bun-first TypeScript
CLI preserved in `Original/`.

The current implementation already includes the shared runtime foundation, the
headless CLI surface, the fallback REPL, and the full-screen TUI. It is not at
full parity with the legacy app yet, but most of the core execution model is
now in place.

## Commands

From the repository root:

```bash
dotnet build ClawdNet.slnx
dotnet test ClawdNet.slnx
```

Run the app directly:

```bash
dotnet run --project ClawdNet.App -- --version
dotnet run --project ClawdNet.App -- --help
dotnet run --project ClawdNet.App
dotnet run --project ClawdNet.App -- --provider openai --model gpt-4o-mini
dotnet run --project ClawdNet.App -- --session <session-id>
dotnet run --project ClawdNet.App -- --model claude-sonnet-4-5
dotnet run --project ClawdNet.App -- --permission-mode accept-edits
dotnet run --project ClawdNet.App -- "Summarize this project"
dotnet run --project ClawdNet.App -- -p "What is 2+2?"
dotnet run --project ClawdNet.App -- ask "Explain this project"
dotnet run --project ClawdNet.App -- ask --provider openai --model gpt-4o-mini "Explain this project"
dotnet run --project ClawdNet.App -- ask --permission-mode bypass-permissions "Inspect this repo"
dotnet run --project ClawdNet.App -- ask --json "Summarize the current milestone"
dotnet run --project ClawdNet.App -- ask --session <session-id> "Continue"
dotnet run --project ClawdNet.App -- provider list
dotnet run --project ClawdNet.App -- provider show anthropic
dotnet run --project ClawdNet.App -- platform open /absolute/path/to/file.cs --line 12 --column 4
dotnet run --project ClawdNet.App -- platform browse https://example.com
dotnet run --project ClawdNet.App -- session new "First Slice"
dotnet run --project ClawdNet.App -- session list
dotnet run --project ClawdNet.App -- task list
dotnet run --project ClawdNet.App -- task show <task-id>
dotnet run --project ClawdNet.App -- task cancel <task-id>
dotnet run --project ClawdNet.App -- plugin list
dotnet run --project ClawdNet.App -- plugin show demo
dotnet run --project ClawdNet.App -- plugin reload
dotnet run --project ClawdNet.App -- mcp list
dotnet run --project ClawdNet.App -- mcp ping <server-name>
dotnet run --project ClawdNet.App -- mcp tools
dotnet run --project ClawdNet.App -- mcp tools <server-name>
dotnet run --project ClawdNet.App -- lsp list
dotnet run --project ClawdNet.App -- lsp ping <server-name>
dotnet run --project ClawdNet.App -- lsp diagnostics <path>
dotnet run --project ClawdNet.App -- tool echo "hello world"
```

Set your provider API keys before using `ask`:

```bash
export ANTHROPIC_API_KEY=your_key_here
export OPENAI_API_KEY=your_key_here
```

## Current Supported CLI Surface

- `clawdnet` interactive mode
  - full-screen TUI by default
- `clawdnet --help`, `-h`
- `clawdnet <prompt>` (root positional prompt shorthand)
- `clawdnet -p <prompt>`, `clawdnet --print <prompt>` (headless print mode)
- `clawdnet --session <id>`
- `clawdnet --provider <name>`
- `clawdnet --model <name>`
- `clawdnet --permission-mode <mode>`
- `--version`
- `ask <prompt>`
- `ask --provider <name> <prompt>`
- `ask --session <id> <prompt>`
- `ask --model <name> <prompt>`
- `ask --permission-mode <mode> <prompt>`
- `ask --json <prompt>`
- `ask --output-format text <prompt>`
- `ask --output-format json <prompt>`
- `ask --output-format stream-json <prompt>`
- `ask --input-format stream-json --output-format stream-json`
- `provider list`
- `provider show <name>`
- `platform open <path> [--line N] [--column N]`
- `platform browse <url>`
- `session new [title]`
- `session list`
- `task list`
- `task show <id>`
- `task cancel <id>`
- `plugin list`
- `plugin show <name>`
- `plugin reload`
- `mcp list`
- `mcp ping <server>`
- `mcp tools [server]`
- `lsp list`
- `lsp ping <server>`
- `lsp diagnostics <path>`
- `tool echo <text>`

Common interactive commands:

- `/help`
- `/session`
- `/provider`
- `/provider <name> [model]`
- `/tasks`
- `/pty`
- `/open <path> [line] [column]`
- `/browse <url>`
- `/clear`
- `/bottom`
- `/exit`
- `exit`
- `quit`

TUI-only extended commands:

- `/tasks <id>`
- `/pty <id>`
- `/pty close <id>`
- `/pty close-exited`
- `/activity`

## Provider Configuration

Configure model providers in:

```text
<LocalApplicationData>/ClawdNet/config/providers.json
```

Example:

```json
{
  "defaultProvider": "anthropic",
  "providers": [
    {
      "name": "anthropic",
      "kind": "Anthropic",
      "enabled": true,
      "apiKeyEnv": "ANTHROPIC_API_KEY",
      "defaultModel": "claude-sonnet-4-5"
    },
    {
      "name": "openai",
      "kind": "OpenAI",
      "enabled": true,
      "apiKeyEnv": "OPENAI_API_KEY",
      "defaultModel": "gpt-4o-mini"
    }
  ]
}
```

If `providers.json` is missing, `ClawdNet` seeds built-in `anthropic` and `openai` providers automatically.

## Platform Configuration

Configure lightweight editor and browser launch preferences in:

```text
<LocalApplicationData>/ClawdNet/config/platform.json
```

Example:

```json
{
  "editorCommand": "code",
  "editorArguments": ["-g"],
  "browserCommand": "open"
}
```

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
