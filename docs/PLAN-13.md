# PLAN-13: Provider and Platform Expansion v2, Slice 1 — AWS Bedrock Provider

## Objective

Add AWS Bedrock as a first-class provider in the .NET runtime, enabling users to route model queries through AWS Bedrock's Anthropic Claude models. This expands provider coverage beyond the current Anthropic first-party and OpenAI-compatible clients.

## Scope

This slice covers:

- **`ProviderKind.Bedrock`** — new enum value in the provider kind system
- **`HttpBedrockMessageClient`** — AWS Bedrock Converse API client with SSE streaming and buffered fallback
- **AWS authentication** — support for:
  - AWS access key + secret key + optional session token (standard AWS credentials)
  - `AWS_BEARER_TOKEN_BEDROCK` environment variable for bearer token auth
  - `CLAUDE_CODE_SKIP_BEDROCK_AUTH` for skipping auth (dev/testing)
- **Region configuration** — `AWS_REGION` / `AWS_DEFAULT_REGION` env vars, default `us-east-1`
- **Custom endpoint** — `ANTHROPIC_BEDROCK_BASE_URL` env var override
- **ARN and inference profile handling** — proper model ID resolution including cross-region inference profiles (`us.`, `eu.`, `apac.`, `global.` prefixes)
- **Provider definition** — built-in Bedrock provider definition with proper env var references
- **Model client factory** — extension to route `ProviderKind.Bedrock` to the new client

This slice does **not** cover:

- Google Vertex AI provider (separate slice)
- Azure Foundry provider (separate slice)
- Bedrock-specific token estimation API (deferred)
- Inference profile discovery via `ListInferenceProfilesCommand` (deferred)
- Bedrock provider UI/UX in TUI beyond what existing provider UI supports

## Assumptions and Non-Goals

- The Bedrock Converse API is used (not the legacy InvokeModel API), as it aligns with the Anthropic messages API shape.
- AWS SigV4 signing is required for standard credential auth. We will implement this using the AWS SDK for .NET (`AWSSDK.BedrockRuntime`) or manual signing.
- Bearer token auth is a simpler alternative that some users may prefer.
- Model names map to Bedrock model IDs (e.g., `claude-sonnet-4-5-20250514` -> `anthropic.claude-sonnet-4-5-20250514-v1:0` or ARN format).
- The provider reuses the same `ModelRequest`/`ModelResponse`/`ModelStreamEvent` contracts as other providers.
- No UI changes are needed beyond what the existing provider selection surfaces already support.

## Likely Change Areas

- `ClawdNet.Core/Models/ProviderKind.cs` — add `Bedrock` value
- `ClawdNet.Core/Models/ProviderDefinition.cs` — update defaults if needed
- `ClawdNet.Runtime/Providers/DefaultModelClientFactory.cs` — add Bedrock routing
- `ClawdNet.Runtime/Bedrock/` — new directory for Bedrock client
  - `HttpBedrockMessageClient.cs` — main client implementation
  - `BedrockAuthHandler.cs` — credential resolution and request signing
  - `BedrockConverseApi.cs` — request/response mapping for Converse API
- `ClawdNet.Runtime/Providers/BedrockProviderDefaults.cs` — built-in Bedrock provider definition
- `ClawdNet.Tests/` — add Bedrock provider tests
- `docs/PARITY.md` — update provider parity status
- `docs/ARCHITECTURE.md` — update provider defaults if needed

## Implementation Plan

### Step 1: Add `ProviderKind.Bedrock` enum value

1. Add `Bedrock` to `ProviderKind` enum in `ClawdNet.Core/Models/ProviderKind.cs`

### Step 2: Create Bedrock runtime directory and auth handler

1. Create `ClawdNet.Runtime/Bedrock/` directory
2. Create `BedrockCredentialResolver.cs`:
   - Resolve credentials from env vars: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`
   - Support `AWS_BEARER_TOKEN_BEDROCK` for bearer auth
   - Support `CLAUDE_CODE_SKIP_BEDROCK_AUTH` for dev mode
   - Resolve region from `AWS_REGION` / `AWS_DEFAULT_REGION`, default `us-east-1`
   - Resolve custom endpoint from `ANTHROPIC_BEDROCK_BASE_URL`

### Step 3: Implement Bedrock Converse API client

1. Create `HttpBedrockMessageClient.cs`:
   - Implement `IModelClient` interface
   - Build Converse API request body from `ModelRequest`
   - Handle AWS SigV4 signing (or bearer token auth)
   - Implement SSE streaming via `StreamAsync`
   - Implement buffered fallback via `SendAsync`
   - Map Bedrock Converse response to `ModelResponse` and `ModelStreamEvent`
   - Handle ARN-format model IDs and cross-region inference profiles

### Step 4: Wire into provider factory and defaults

1. Update `DefaultModelClientFactory` to handle `ProviderKind.Bedrock`
2. Create `BedrockProviderDefaults.cs` with built-in Bedrock provider definition:
   - Name: `"bedrock"`
   - Enabled env var: `"CLAUDE_CODE_USE_BEDROCK"`
   - API key env var references for AWS credentials
   - Region and endpoint configuration notes

### Step 5: Add tests

1. Unit tests for credential resolver
2. Unit tests for request/response mapping
3. Integration test skeleton (fake-process or handler-level, no live AWS calls required)
4. Provider catalog loading test with Bedrock provider

### Step 6: Validation and documentation

1. Run `dotnet build ./ClawdNet.slnx`
2. Run `dotnet test ./ClawdNet.slnx`
3. Smoke test: `dotnet run --project ./ClawdNet.App -- provider list` shows bedrock
4. Smoke test: `ask --provider bedrock --model <model> "hello"` (requires AWS credentials)
5. Update `docs/PARITY.md` — mark Bedrock provider as implemented
6. Update `docs/ARCHITECTURE.md` — add Bedrock to provider defaults if needed

## Validation Plan

- Build must pass: `dotnet build ./ClawdNet.slnx`
- Tests must pass: `dotnet test ./ClawdNet.slnx`
- Provider list must include `bedrock` when Bedrock is enabled
- `ask --provider bedrock` must route to the Bedrock client
- No regression in Anthropic or OpenAI provider behavior

## Rollback / Risk Notes

- **Risk**: AWS SigV4 signing complexity. Mitigation: use `AWSSDK.BedrockRuntime` NuGet package for signing, or implement minimal SigV4 signing for the Converse API endpoint.
- **Risk**: Bedrock model ID format varies by region and inference profile. Mitigation: document expected model ID formats and fail clearly with actionable errors when model IDs are unrecognized.
- **Risk**: AWS credential resolution may fail in environments with complex IAM setups. Mitigation: support bearer token auth and skip-auth flag for simpler cases; log clear error messages.
- **Rollback**: The new `ProviderKind.Bedrock` enum value is additive. Removing it would require reverting the enum change and any code that references it, but would not break existing providers.

## Definition of Done

- [x] `ProviderKind.Bedrock` enum value added
- [x] Bedrock credential resolver implemented and tested
- [x] `HttpBedrockMessageClient` implements `IModelClient` with streaming and buffered fallback
- [x] Bedrock provider definition included in built-in defaults
- [x] `DefaultModelClientFactory` routes Bedrock to the new client
- [x] Unit tests pass for credential resolution and request mapping
- [x] `dotnet build ./ClawdNet.slnx` passes
- [x] `dotnet test ./ClawdNet.slnx` passes (151 tests, 0 failures)
- [x] `provider list` includes bedrock
- [x] `docs/PARITY.md` updated with Bedrock provider status
- [x] `docs/ARCHITECTURE.md` updated with Bedrock provider defaults
- [x] Changes committed on current branch

## What Changed

### New files
- `ClawdNet.Runtime/Bedrock/BedrockCredentialResolver.cs` — AWS credential resolution, region config, SigV4 signing, ARN endpoint handling
- `ClawdNet.Runtime/Bedrock/HttpBedrockMessageClient.cs` — Bedrock Converse API client with SSE streaming and buffered fallback
- `ClawdNet.Tests/BedrockProviderTests.cs` — Unit tests for credential resolver and HTTP client

### Modified files
- `ClawdNet.Core/Models/ProviderKind.cs` — added `Bedrock` enum value
- `ClawdNet.Core/Models/ProviderDefaults.cs` — added Bedrock to built-in providers
- `ClawdNet.Runtime/Providers/DefaultModelClientFactory.cs` — added Bedrock case to factory switch
- `ClawdNet.Runtime/Providers/ProviderCatalog.cs` — added Bedrock default env var mapping
- `ClawdNet.Tests/ProviderCatalogTests.cs` — added Bedrock to built-in provider assertion
- `docs/PARITY.md` — updated provider selection row, added Bedrock env vars
- `docs/ARCHITECTURE.md` — updated provider defaults and explicit defaults sections
- `docs/PLAN.md` — updated active milestone with slice references

## Validation Results

- `dotnet build ./ClawdNet.slnx` — **passed** (0 errors)
- `dotnet test ./ClawdNet.slnx` — **passed** (151 tests, 0 failures, 30.1s)

## Remaining Follow-ups

- Google Vertex AI provider (PLAN-14, next slice)
- Azure Foundry provider (PLAN-15, next slice)
- Bedrock-specific token estimation API (deferred)
- Inference profile discovery via `ListInferenceProfilesCommand` (deferred)
- Live AWS credential integration tests (currently using fake-process tests only)
