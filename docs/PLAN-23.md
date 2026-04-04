# PLAN-23: MCP Parity v2

## Objective

Close the MCP management parity gap by adding server management commands and making the MCP config contract explicit.

**Scope:**
1. Extend `McpServerDefinition` model with `TransportType` (Stdio/Sse/Http), `Url`, `Headers`, and `Scope` fields
2. Add `mcp add` command — add stdio/SSE/HTTP servers with flags for scope, transport, env vars, headers
3. Add `mcp remove` command — remove servers from config
4. Add `mcp get` command — show detailed server info including transport, env, headers
5. Add `mcp add-json` command — add a server from raw JSON string
6. Add config write support to `McpConfigurationLoader` — save/add/remove servers to mcp.json
7. Add `/mcp` slash command to TUI and REPL for enable/disable/reconnect
8. Add `--mcp-config` root flag for inline JSON or file-based MCP config override

**Non-goals (deferred to later milestones):**
- `mcp serve` — acting as an MCP server (separate program)
- `mcp add-from-claude-desktop` — requires platform-specific desktop config reading + interactive UI
- `mcp xaa` — XAA/OIDC IdP integration (feature-gated in legacy)
- `mcp reset-project-choices` — requires project-level `.mcp.json` write support
- Full scope model (local/user/project) — start with user-scope (app-data config) and document the limitation

## Assumptions

1. `McpServerDefinition` model exists in `ClawdNet.Core/Models/`
2. `McpConfigurationLoader` exists in `ClawdNet.Runtime/Protocols/`
3. `McpCommandHandler` exists in `ClawdNet.Core/Commands/`
4. `IMcpClient` has `PingAsync` for health checking
5. MCP config is stored as JSON in `<app-data>/config/mcp.json`
6. The current stdio transport implementation is correct and working
7. SSE/HTTP transport support in the underlying MCP client library needs verification

## Files and Subsystems Likely to Change

| File | Change Type | Reason |
|------|------------|--------|
| `ClawdNet.Core/Models/McpServerDefinition.cs` | Modify | Add TransportType, Url, Headers, Scope fields |
| `ClawdNet.Core/Commands/McpCommandHandler.cs` | Modify | Add add/remove/get/add-json subcommands |
| `ClawdNet.Runtime/Protocols/McpConfigurationLoader.cs` | Modify | Add write support (SaveAsync, AddAsync, RemoveAsync) |
| `ClawdNet.Core/Abstractions/IMcpConfigurationLoader.cs` | Possibly add | Interface for config loader if not already present |
| `ClawdNet.Terminal/Repl/ReplHost.cs` | Modify | Add `/mcp` slash command |
| `ClawdNet.Terminal/Tui/TuiHost.cs` | Modify | Add `/mcp` slash command |
| `ClawdNet.App/AppHost.cs` | Modify | Add `--mcp-config` root flag |
| `ClawdNet.Core/Models/ReplLaunchOptions.cs` | Possibly modify | If `--mcp-config` needs to be passed through |
| `ClawdNet.Tests/` | Add/Modify | Tests for new MCP commands |

## Step-by-Step Implementation Plan

### Step 1: Audit and Extend McpServerDefinition Model

**Goal:** Add transport type, URL, headers, and scope fields.

1. Read current `McpServerDefinition.cs`
2. Add `TransportType` enum (Stdio, Sse, Http)
3. Add `Url` property for SSE/HTTP transports
4. Add `Headers` property (Dictionary<string, string>?)
5. Add `Scope` property (User, Local, Project) — default to User for now
6. Update JSON serialization to handle new fields

### Step 2: Add Write Support to McpConfigurationLoader

**Goal:** Enable adding/removing servers from config files.

1. Add `SaveAsync(McpConfiguration config, CancellationToken)` method
2. Add `AddServerAsync(McpServerDefinition server, CancellationToken)` method
3. Add `RemoveServerAsync(string name, CancellationToken)` method
4. Handle concurrent writes with file locking

### Step 3: Add `mcp get` Command

**Goal:** Show detailed server info.

1. In `McpCommandHandler.cs`, add `get <server>` subcommand
2. Load server from config, show: name, transport, command/url, enabled, args, env, headers
3. Run a health check (ping) and show status

### Step 4: Add `mcp add` Command

**Goal:** Add MCP servers from CLI.

1. In `McpCommandHandler.cs`, add `add <name> <commandOrUrl> [args...]` subcommand
2. Support flags:
   - `-t/--transport <stdio|sse|http>` (default: stdio)
   - `-e/--env <KEY=VALUE>` (repeatable)
   - `-H/--header <KEY:VALUE>` (repeatable, for SSE/HTTP)
   - `--read-only-tools` (tool permission default)
3. Create `McpServerDefinition` from arguments and save to config

### Step 5: Add `mcp remove` Command

**Goal:** Remove MCP servers from CLI.

1. In `McpCommandHandler.cs`, add `remove <name>` subcommand
2. Load config, remove server by name, save config
3. Output success/error message

### Step 6: Add `mcp add-json` Command

**Goal:** Add MCP server from raw JSON.

1. In `McpCommandHandler.cs`, add `add-json <name> <json>` subcommand
2. Parse JSON into `McpServerDefinition`
3. Validate required fields
4. Save to config

### Step 7: Add `--mcp-config` Root Flag

**Goal:** Allow MCP config override at launch.

1. Add `McpConfig` string property to root flag parsing in `AppHost.cs`
2. If file path: load MCP config from file
3. If JSON string: parse and use inline
4. Merge with or override app-data config

### Step 8: Add `/mcp` Slash Command to TUI and REPL

**Goal:** Interactive MCP management.

1. Add `/mcp` to both TUI and REPL slash command handlers
2. Subcommands:
   - `/mcp list` — show server status
   - `/mcp enable [server]` — enable server(s)
   - `/mcp disable [server]` — disable server(s)
   - `/mcp reconnect <server>` — reconnect to server
3. Show result in activity feed

### Step 9: Update Help Text

**Goal:** Document new MCP commands.

1. Update `mcp --help` to include add/remove/get/add-json
2. Update TUI/REPL help to include `/mcp`

### Step 10: Tests

**Goal:** Verify new MCP commands.

1. Unit tests for add/remove/get/add-json command handlers
2. Unit tests for config loader write support
3. Manual smoke tests

## Validation Plan

### Automated
```bash
dotnet build ./ClawdNet.slnx
dotnet test ./ClawdNet.slnx
```

### Manual smoke tests
```bash
# Test mcp get
dotnet run --project ClawdNet.App -- mcp list
dotnet run --project ClawdNet.App -- mcp get <server-name>

# Test mcp add (stdio)
dotnet run --project ClawdNet.App -- mcp add demo python3 /path/to/server.py -e DEMO_FLAG=1

# Test mcp add-json
dotnet run --project ClawdNet.App -- mcp add-json test '{"command":"echo","arguments":["hello"]}'

# Test mcp remove
dotnet run --project ClawdNet.App -- mcp remove test

# Test --mcp-config
dotnet run --project ClawdNet.App -- --mcp-config '{"servers":[{"name":"test","command":"echo","arguments":["hello"],"enabled":true}]}'
```

## Rollback / Risk Notes

- **Risk:** Config file corruption on concurrent writes. **Mitigation:** Use file locking in config loader
- **Risk:** SSE/HTTP transport not supported by underlying MCP client. **Mitigation:** Start with stdio-only; add SSE/HTTP incrementally
- **Risk:** Breaking existing mcp.json format. **Mitigation:** New fields are optional with defaults; backward compatible
- **Rollback:** All changes are additive; no existing behavior is modified. Safe to revert if issues arise.

## Exit Criteria

- [x] `mcp get <server>` shows detailed server info with health check
- [x] `mcp add <name> <cmd> [args...]` adds stdio servers with env vars
- [x] `mcp remove <name>` removes servers from config
- [x] `mcp add-json <name> <json>` adds servers from JSON
- [x] Config loader supports write operations
- [x] `dotnet build` passes
- [x] `dotnet test` passes (260 tests, 0 failed)
- [ ] `--mcp-config` root flag (deferred — requires root flag parsing changes)
- [ ] `/mcp` slash command in TUI and REPL (deferred — lower priority than CLI commands)
- [x] `PARITY.md` MCP rows updated
- [ ] `PLAN.md` milestone status updated
