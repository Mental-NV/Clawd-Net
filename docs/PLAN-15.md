# PLAN-15: Provider and Platform Expansion v2, Slice 3 — Azure Foundry Provider

## Objective

Add Azure Foundry as a first-class provider in the .NET runtime, enabling users to route model queries through Azure AI Foundry's Anthropic Claude models. This completes the provider coverage for the current expansion milestone.

## Scope

This slice covers:

- **`ProviderKind.Foundry`** — new enum value in the provider kind system
- **`HttpFoundryMessageClient`** — Foundry API client with SSE streaming and buffered fallback (same Anthropic messages API format, different endpoint)
- **Authentication** — support for:
  - `ANTHROPIC_FOUNDRY_API_KEY` env var for API key auth
  - `CLAUDE_CODE_SKIP_FOUNDRY_AUTH` for skipping auth (dev/testing)
- **Endpoint configuration** — `ANTHROPIC_FOUNDRY_BASE_URL` for full URL override, or `ANTHROPIC_FOUNDRY_RESOURCE` for resource-name-based URL construction
- **Model naming** — Foundry uses simple short identifiers (e.g., `claude-sonnet-4-5`); case-preserved deployment IDs
- **Provider definition** — built-in Foundry provider definition with proper env var references
- **Model client factory** — extension to route `ProviderKind.Foundry` to the new client

This slice does **not** cover:

- Azure AD `DefaultAzureCredential` chained auth (deferred — complex Azure.Identity integration)
- Live Azure Foundry integration tests (will use fake-process tests)

## Assumptions and Non-Goals

- Foundry uses the same Anthropic messages API format. The endpoint is `https://{resource}.services.ai.azure.com/anthropic/v1/messages`.
- API key auth is the primary path; Azure AD token provider auth is deferred.
- Model names are simple short identifiers (deployment IDs on Azure side).
- The provider reuses the same `ModelRequest`/`ModelResponse`/`ModelStreamEvent` contracts.
- No TUI changes needed beyond existing provider selection surfaces.

## Likely Change Areas

- `ClawdNet.Core/Models/ProviderKind.cs` — add `Foundry` value
- `ClawdNet.Core/Models/ProviderDefaults.cs` — add Foundry to built-in providers
- `ClawdNet.Runtime/Providers/DefaultModelClientFactory.cs` — add Foundry routing
- `ClawdNet.Runtime/Providers/ProviderCatalog.cs` — add Foundry default env var mapping
- `ClawdNet.Runtime/Foundry/` — new directory for Foundry client
  - `FoundryCredentialResolver.cs` — API key and skip-auth resolution
  - `HttpFoundryMessageClient.cs` — Foundry API client with SSE streaming and buffered fallback
- `ClawdNet.Tests/FoundryProviderTests.cs` — unit tests
- `docs/PARITY.md` — update provider parity status
- `docs/ARCHITECTURE.md` — update provider defaults

## Implementation Plan

### Step 1: Add `ProviderKind.Foundry` enum value

### Step 2: Create Foundry runtime directory and credential resolver

### Step 3: Implement Foundry API client (same Anthropic format, Azure endpoint)

### Step 4: Wire into provider factory and defaults

### Step 5: Add tests

### Step 6: Validation and documentation

## Validation Plan

- Build must pass: `dotnet build ./ClawdNet.slnx`
- Tests must pass: `dotnet test ./ClawdNet.slnx`
- Provider list must include `foundry` when Foundry is enabled
- No regression in Anthropic, OpenAI, Bedrock, or Vertex AI provider behavior

## Rollback / Risk Notes

- **Risk**: Foundry is the simplest provider (just Anthropic API on Azure endpoint), so minimal risk.
- **Rollback**: Additive enum change, safe to remove if needed.

## Definition of Done

- [x] `ProviderKind.Foundry` enum value added
- [x] Foundry credential resolver implemented and tested
- [x] `HttpFoundryMessageClient` implements `IModelClient` with streaming and buffered fallback
- [x] Foundry provider definition included in built-in defaults
- [x] `DefaultModelClientFactory` routes Foundry to the new client
- [x] Unit tests pass
- [x] `dotnet build ./ClawdNet.slnx` passes
- [x] `dotnet test ./ClawdNet.slnx` passes (182 tests, 0 failures)
- [x] `provider list` includes foundry
- [x] `docs/PARITY.md` updated
- [x] `docs/ARCHITECTURE.md` updated
- [x] Changes committed on current branch

## What Changed

### New files
- `ClawdNet.Runtime/Foundry/FoundryCredentialResolver.cs` — API key and skip-auth resolution, resource name and custom base URL config
- `ClawdNet.Runtime/Foundry/HttpFoundryMessageClient.cs` — Foundry API client with SSE streaming and buffered fallback, same Anthropic messages format
- `ClawdNet.Tests/FoundryProviderTests.cs` — Unit tests for credential resolver and HTTP client

### Modified files
- `ClawdNet.Core/Models/ProviderKind.cs` — added `Foundry` enum value
- `ClawdNet.Core/Models/ProviderDefaults.cs` — added Foundry to built-in providers
- `ClawdNet.Runtime/Providers/DefaultModelClientFactory.cs` — added Foundry case to factory switch
- `ClawdNet.Runtime/Providers/ProviderCatalog.cs` — added Foundry default env var mapping
- `ClawdNet.Tests/ProviderCatalogTests.cs` — added Foundry to built-in provider assertion
- `docs/PARITY.md` — updated provider selection row, added Foundry env vars
- `docs/ARCHITECTURE.md` — updated provider defaults and explicit defaults sections
- `docs/PLAN.md` — marked Provider and Platform Expansion v2 as complete

## Validation Results

- `dotnet build ./ClawdNet.slnx` — **passed** (0 errors)
- `dotnet test ./ClawdNet.slnx` — **passed** (182 tests, 0 failures, 30.1s)

## Remaining Follow-ups

- Azure AD `DefaultAzureCredential` chained auth (deferred)
- Live Azure Foundry integration tests (currently using fake-process tests only)
