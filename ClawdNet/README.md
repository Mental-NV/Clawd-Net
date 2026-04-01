# ClawdNet

`ClawdNet` is the .NET 10 replatforming workspace for the Bun-first TypeScript
CLI that lives in the parent repository.

This initial implementation provides:

- a production-shaped solution layout
- a small command pipeline for `--version`, `session new`, and `session list`
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
dotnet run --project ClawdNet/ClawdNet.App -- session new "First Slice"
dotnet run --project ClawdNet/ClawdNet.App -- session list
dotnet run --project ClawdNet/ClawdNet.App -- tool echo "hello world"
```

Run from inside the `ClawdNet/` workspace:

```bash
dotnet build ../ClawdNet.slnx
dotnet test ../ClawdNet.slnx
dotnet run --project ClawdNet.App -- --version
```

## Current Supported CLI Surface

- `--version`
- `session new [title]`
- `session list`
- `tool echo <text>`
