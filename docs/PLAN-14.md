# PLAN-14: Provider and Platform Expansion v2, Slice 2 — Google Vertex AI Provider

## Objective

Add Google Vertex AI as a first-class provider in the .NET runtime, enabling users to route model queries through Google Cloud Vertex AI's Anthropic Claude models. This expands provider coverage beyond Anthropic first-party, OpenAI, and AWS Bedrock.

## Scope

This slice covers:

- **`ProviderKind.VertexAI`** — new enum value in the provider kind system
- **`HttpVertexAIMessageClient`** — Vertex AI Converse API client with SSE streaming and buffered fallback (modeling the same API shape as Bedrock Converse)
- **GCP authentication** — support for:
  - Application Default Credentials (ADC) via `GOOGLE_APPLICATION_CREDENTIALS` env var (service account key file path)
  - `ANTHROPIC_VERTEX_PROJECT_ID` env var for project ID
  - `CLAUDE_CODE_SKIP_VERTEX_AUTH` for skipping auth (dev/testing)
- **Region configuration** — `CLOUD_ML_REGION` env var, default `us-east5`, with per-model region override env vars (`VERTEX_REGION_CLAUDE_*`)
- **Model ID mapping** — Vertex uses `model-name@YYYYMMDD` format; the client resolves short model names to Vertex IDs
- **Provider definition** — built-in Vertex AI provider definition with proper env var references
- **Model client factory** — extension to route `ProviderKind.VertexAI` to the new client

This slice does **not** cover:

- Azure Foundry provider (separate slice, PLAN-15)
- GCP credential refresh via subprocess command (deferred — legacy `gcpAuthRefresh`)
- Live GCP integration tests (will use fake-process tests like Bedrock)
- Vertex-specific token estimation API (deferred)

## Assumptions and Non-Goals

- Vertex AI Anthropic models use the same Converse API shape as Bedrock (both route through `@anthropic-ai/vertex-sdk` in the legacy, which exposes Anthropic-compatible messages API). We will implement HTTP calls to the Vertex AI endpoint directly.
- Vertex AI endpoint format: `https://{region}-aiplatform.googleapis.com/v1/projects/{project}/locations/{region}/publishers/anthropic/models/{modelId}:streamGenerateContent`
- GCP ADC authentication uses service account JSON key files or the `gcloud auth application-default login` flow. For simplicity, we support service account key file authentication via `GOOGLE_APPLICATION_CREDENTIALS`.
- Model name resolution maps short names (e.g., `claude-sonnet-4-5`) to Vertex IDs (e.g., `claude-sonnet-4-5@20250929`).
- The provider reuses the same `ModelRequest`/`ModelResponse`/`ModelStreamEvent` contracts.
- No TUI changes needed beyond existing provider selection surfaces.

## Likely Change Areas

- `ClawdNet.Core/Models/ProviderKind.cs` — add `VertexAI` value
- `ClawdNet.Core/Models/ProviderDefaults.cs` — add Vertex AI to built-in providers
- `ClawdNet.Runtime/Providers/DefaultModelClientFactory.cs` — add VertexAI routing
- `ClawdNet.Runtime/Providers/ProviderCatalog.cs` — add VertexAI default env var mapping
- `ClawdNet.Runtime/VertexAI/` — new directory for Vertex AI client
  - `VertexAICredentialResolver.cs` — GCP credential resolution, project ID, region config
  - `HttpVertexAIMessageClient.cs` — Vertex AI API client with SSE streaming and buffered fallback
  - `VertexAIModelIdResolver.cs` — model name to Vertex ID mapping
- `ClawdNet.Tests/VertexAIProviderTests.cs` — unit tests for credential resolver and HTTP client
- `ClawdNet.Tests/ProviderCatalogTests.cs` — add VertexAI to built-in provider assertion
- `docs/PARITY.md` — update provider parity status
- `docs/ARCHITECTURE.md` — update provider defaults

## Implementation Plan

### Step 1: Add `ProviderKind.VertexAI` enum value

1. Add `VertexAI` to `ProviderKind` enum in `ClawdNet.Core/Models/ProviderKind.cs`

### Step 2: Create Vertex AI runtime directory and credential resolver

1. Create `ClawdNet.Runtime/VertexAI/` directory
2. Create `VertexAICredentialResolver.cs`:
   - Resolve credentials from `GOOGLE_APPLICATION_CREDENTIALS` env var (path to service account key JSON)
   - Resolve project ID from `ANTHROPIC_VERTEX_PROJECT_ID`, `GOOGLE_CLOUD_PROJECT`, or `GCLOUD_PROJECT`
   - Support `CLAUDE_CODE_SKIP_VERTEX_AUTH` for dev mode
   - Resolve region from `CLOUD_ML_REGION`, default `us-east5`
   - Support per-model region overrides via `VERTEX_REGION_CLAUDE_*` env vars

### Step 3: Implement Vertex AI model ID resolver

1. Create `VertexAIModelIdResolver.cs`:
   - Map short model names to Vertex `model@YYYYMMDD` format
   - Support current Claude model IDs:
     - `claude-3-5-haiku` -> `claude-3-5-haiku@20241022`
     - `claude-3-5-sonnet` -> `claude-3-5-sonnet-v2@20241022`
     - `claude-3-7-sonnet` -> `claude-3-7-sonnet@20250219`
     - `claude-haiku-4-5` -> `claude-haiku-4-5@20251001`
     - `claude-sonnet-4` -> `claude-sonnet-4@20250514`
     - `claude-sonnet-4-5` -> `claude-sonnet-4-5@20250929`
     - `claude-opus-4` -> `claude-opus-4@20250514`
     - `claude-opus-4-1` -> `claude-opus-4-1@20250805`
   - Pass through model IDs that already contain `@` (already in Vertex format)

### Step 4: Implement Vertex AI API client

1. Create `HttpVertexAIMessageClient.cs`:
   - Implement `IModelClient` interface
   - Build Vertex AI `streamGenerateContent` request from `ModelRequest`
   - Handle GCP ADC authentication (service account key file JWT + token exchange)
   - Implement SSE streaming via `StreamAsync`
   - Implement buffered fallback via `SendAsync`
   - Map Vertex AI response to `ModelResponse` and `ModelStreamEvent`
   - Build endpoint URL: `https://{region}-aiplatform.googleapis.com/v1/projects/{project}/locations/{region}/publishers/anthropic/models/{modelId}:rawPredict`

### Step 5: Wire into provider factory and defaults

1. Update `DefaultModelClientFactory` to handle `ProviderKind.VertexAI`
2. Update `ProviderCatalog.ParseProvider` for VertexAI env var default
3. Add Vertex AI to `ProviderDefaults.GetBuiltInProviders()`

### Step 6: Add tests

1. Unit tests for credential resolver
2. Unit tests for model ID resolver
3. Unit tests for request/response mapping
4. Provider catalog loading test with VertexAI provider

### Step 7: Validation and documentation

1. Run `dotnet build ./ClawdNet.slnx`
2. Run `dotnet test ./ClawdNet.slnx`
3. Smoke test: `dotnet run --project ./ClawdNet.App -- provider list` shows vertex
4. Update `docs/PARITY.md` — mark Vertex AI provider as implemented
5. Update `docs/ARCHITECTURE.md` — add Vertex AI to provider defaults

## Validation Plan

- Build must pass: `dotnet build ./ClawdNet.slnx`
- Tests must pass: `dotnet test ./ClawdNet.slnx`
- Provider list must include `vertex` when Vertex AI is enabled
- `ask --provider vertex` must route to the Vertex AI client
- No regression in Anthropic, OpenAI, or Bedrock provider behavior

## Rollback / Risk Notes

- **Risk**: GCP ADC authentication complexity. Mitigation: support service account key file auth (simplest path) and skip-auth flag for testing. Full ADC chain (gcloud, GCE metadata) is deferred.
- **Risk**: Vertex AI endpoint and request format differs from Bedrock. Mitigation: model after the Bedrock client but target Vertex AI's `rawPredict` endpoint which uses Anthropic-compatible message format.
- **Risk**: Model ID format variance. Mitigation: explicit mapping table with clear error for unrecognized models.
- **Rollback**: The new `ProviderKind.VertexAI` enum value is additive. Removing it would require reverting the enum change and any code that references it, but would not break existing providers.

## Definition of Done

- [x] `ProviderKind.VertexAI` enum value added
- [x] Vertex AI credential resolver implemented and tested
- [x] Vertex AI model ID resolver implemented and tested
- [x] `HttpVertexAIMessageClient` implements `IModelClient` with streaming and buffered fallback
- [x] Vertex AI provider definition included in built-in defaults
- [x] `DefaultModelClientFactory` routes VertexAI to the new client
- [x] Unit tests pass for credential resolution, model ID resolution, and request mapping
- [x] `dotnet build ./ClawdNet.slnx` passes
- [x] `dotnet test ./ClawdNet.slnx` passes (170 tests, 0 failures)
- [x] `provider list` includes vertex
- [x] `docs/PARITY.md` updated with Vertex AI provider status
- [x] `docs/ARCHITECTURE.md` updated with Vertex AI provider defaults
- [x] Changes committed on current branch

## What Changed

### New files
- `ClawdNet.Runtime/VertexAI/VertexAICredentialResolver.cs` — GCP credential resolution, project ID, region config, service account key loading
- `ClawdNet.Runtime/VertexAI/VertexAIModelIdResolver.cs` — model name to Vertex ID mapping (short names to `model@YYYYMMDD` format)
- `ClawdNet.Runtime/VertexAI/HttpVertexAIMessageClient.cs` — Vertex AI API client with SSE streaming and buffered fallback, GCP JWT auth
- `ClawdNet.Tests/VertexAIProviderTests.cs` — Unit tests for credential resolver, model ID resolver, and HTTP client

### Modified files
- `ClawdNet.Core/Models/ProviderKind.cs` — added `VertexAI` enum value
- `ClawdNet.Core/Models/ProviderDefaults.cs` — added Vertex AI to built-in providers
- `ClawdNet.Runtime/Providers/DefaultModelClientFactory.cs` — added VertexAI case to factory switch
- `ClawdNet.Runtime/Providers/ProviderCatalog.cs` — added VertexAI default env var mapping
- `ClawdNet.Tests/ProviderCatalogTests.cs` — added VertexAI to built-in provider assertion
- `docs/PARITY.md` — updated provider selection row, added Vertex AI env vars
- `docs/ARCHITECTURE.md` — updated provider defaults and explicit defaults sections
- `docs/PLAN.md` — updated active milestone with slice references

## Validation Results

- `dotnet build ./ClawdNet.slnx` — **passed** (0 errors)
- `dotnet test ./ClawdNet.slnx` — **passed** (170 tests, 0 failures, 30.0s)

## Remaining Follow-ups

- Azure Foundry provider (PLAN-15, next slice)
- GCP credential refresh via subprocess command (deferred — legacy `gcpAuthRefresh`)
- Live GCP credential integration tests (currently using fake-process tests only)
- Vertex-specific token estimation API (deferred)
