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
- `tool echo <text>`
