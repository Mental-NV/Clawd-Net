# PLAN-11: Plugin Platform v4, Slice 1 — Plugin Lifecycle Management

## Objective

Add essential plugin lifecycle management commands: install, uninstall, enable, disable, and unload. This closes the biggest usability gap between the legacy TypeScript CLI and the .NET plugin surface.

## Scope

This slice covers:

- **Plugin install**: Copy a plugin directory from a source path to `<AppData>/ClawdNet/plugins/<plugin-id>/`
- **Plugin uninstall**: Remove a plugin directory from the plugins folder
- **Plugin enable/disable**: Toggle the `enabled` flag in `plugin.json` without manual file editing
- **Plugin unload**: API method to remove a plugin from the running catalog without full reload
- **CLI commands**: `plugin install <path>`, `plugin uninstall <name>`, `plugin enable <name>`, `plugin disable <name>`, `plugin unload <name>`

This slice does **not** attempt:

- Marketplace or registry integration (downloading plugins from a remote source)
- Plugin dependency resolution
- Plugin signature verification
- In-process plugin loading
- Plugin configuration API
- Additional hook kinds

## Assumptions and Non-Goals

- Install copies local files only; no remote download in this slice.
- Uninstall immediately removes the directory; running plugin processes are not gracefully terminated.
- Enable/disable modifies `plugin.json` in place and triggers a reload.
- Unload removes the plugin from the catalog and unregisters its tools/commands.
- All operations are filesystem-based and synchronous.

## Likely Change Areas

- `ClawdNet.Core/Abstractions/IPluginCatalog.cs` — add InstallAsync, UninstallAsync, EnableAsync, DisableAsync, UnloadAsync
- `ClawdNet.Runtime/Plugins/PluginCatalog.cs` — implement lifecycle methods
- `ClawdNet.Core/Commands/PluginCommandHandler.cs` — add CLI handlers for new commands
- `ClawdNet.Tests/PluginCatalogTests.cs` — add tests for new lifecycle methods
- `README.md` — document new plugin commands

## Implementation Plan

### Step 1: Add lifecycle methods to IPluginCatalog interface

1. `Task<PluginDefinition> InstallAsync(string sourcePath, CancellationToken ct)`
2. `Task UninstallAsync(string pluginName, CancellationToken ct)`
3. `Task<PluginDefinition> EnableAsync(string pluginName, CancellationToken ct)`
4. `Task<PluginDefinition> DisableAsync(string pluginName, CancellationToken ct)`
5. `Task UnloadAsync(string pluginName, CancellationToken ct)`

### Step 2: Implement lifecycle methods in PluginCatalog

1. **InstallAsync**: Validate source has `plugin.json`, copy directory to `<dataRoot>/plugins/<name>/`, reload catalog
2. **UninstallAsync**: Verify plugin exists, delete directory, reload catalog
3. **EnableAsync**: Read `plugin.json`, set `enabled: true`, write back, reload catalog
4. **DisableAsync**: Read `plugin.json`, set `enabled: false`, write back, reload catalog
5. **UnloadAsync**: Remove plugin from internal catalog, unregister tools/commands from runtime

### Step 3: Add CLI command handlers

1. Add handlers to `PluginCommandHandler` for each new subcommand
2. Wire up argument parsing for `plugin install <path>`, etc.
3. Return appropriate exit codes (0=success, 1=failure, 3=not found)

### Step 4: Add tests

1. Install from valid source path
2. Install from invalid path (no plugin.json)
3. Uninstall existing plugin
4. Uninstall non-existent plugin (exit code 3)
5. Enable/disable toggling
6. Unload and verify tools unregistered

### Step 5: Validation and documentation

1. Run `dotnet build ./ClawdNet.slnx`
2. Run `dotnet test ./ClawdNet.slnx`
3. Smoke test: `plugin install`, `plugin list`, `plugin disable`, `plugin enable`, `plugin uninstall`
4. Update README.md with new commands

## Validation Plan

### Build Validation

```bash
dotnet build ./ClawdNet.slnx
```

### Test Validation

```bash
dotnet test ./ClawdNet.slnx
```

### Smoke Tests

1. Create a test plugin directory with `plugin.json`
2. `clawdnet plugin install /path/to/test-plugin`
3. `clawdnet plugin list` — verify plugin appears
4. `clawdnet plugin disable test-plugin`
5. `clawdnet plugin list` — verify plugin shows as disabled
6. `clawdnet plugin enable test-plugin`
7. `clawdnet plugin uninstall test-plugin`
8. `clawdnet plugin list` — verify plugin removed

## Definition of Done

- [x] Install/uninstall/enable/disable/unload methods implemented
- [x] CLI commands wired up and functional
- [x] All tests pass
- [x] Documentation updated (README.md)
