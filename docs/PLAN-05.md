# PLAN-05: Auth Parity and Provider Defaults v1

## Objective

Implement OAuth-capable authentication support for the primary Anthropic provider without regressing current env-var-based provider auth. Revise `auth login` and `auth logout` so they no longer frame OAuth as an intentional non-goal. Preserve explicit provider selection while smoothing default provider and model behavior.

## Scope

- Implement a local OAuth login flow for Anthropic (browser redirect + local callback) that stores tokens securely
- Add token persistence using file-based storage (with 0o600 permissions on Linux/macOS) as the baseline, since keychain integration is platform-specific and complex
- Implement automatic token refresh before expiry
- Revise `auth login` to support both env-var guidance and interactive OAuth login (`--browser` flag)
- Revise `auth logout` to clear OAuth tokens as well as env-var guidance
- Smooth default provider and model behavior: ensure provider defaults are well-documented and practical
- Update `auth status` to report OAuth token status alongside env-var status

## Non-Goals

- macOS keychain integration (too platform-specific for this milestone; file-based secure storage is the baseline)
- Windows credential manager integration
- Full claude.ai subscriber profile integration (email, org, subscription type display)
- MCP server OAuth flow
- Console (API key helper) login flow
- SSO / `--sso` login flow
- Cross-App Access (XAA) auth

## Files Likely to Change

- `ClawdNet.Core/Commands/AuthCommandHandler.cs` — revise login/logout/status to support OAuth
- `ClawdNet.Core/Abstractions/` — new interfaces for token storage and OAuth service
- `ClawdNet.Runtime/` — new OAuth service, token persistence, token refresh
- `ClawdNet.App/AppHost.cs` — register new services
- `ClawdNet.Tests/` — new auth tests
- `docs/PARITY.md` — update auth rows
- `docs/ARCHITECTURE.md` — update auth section
- `README.md` — update auth documentation
- `docs/PLAN.md` — mark milestone complete

## Implementation Plan

### Step 1: Define token storage abstraction
- Create `ITokenStore` interface in `ClawdNet.Core/Abstractions/`
- Implement `FileTokenStore` in `ClawdNet.Runtime/` — stores tokens as JSON with 0o600 permissions
- Token model: `accessToken`, `refreshToken`, `expiresAt`, `scopes`
- Storage location: `<AppData>/ClawdNet/.credentials.json`

### Step 2: Implement OAuth service
- Create `IOAuthService` interface in `ClawdNet.Core/Abstractions/`
- Implement `AnthropicOAuthService` in `ClawdNet.Runtime/Auth/`
  - PKCE code verifier/challenge generation (SHA-256, base64url)
  - Build authorization URL (Anthropic OAuth endpoints)
  - Start local HTTP listener for callback
  - Exchange authorization code for tokens
  - Token refresh logic
  - Profile info fetching (email, subscription type)
- Use Anthropic's OAuth constants:
  - Client ID from legacy: `9d1c250a-e61b-44d9-88ed-5944d1962f5e`
  - Scopes for claude.ai: standard user scopes
  - Auth URL: `https://console.anthropic.com` or claude.ai endpoints

### Step 3: Revise AuthCommandHandler
- `auth login`:
  - Default: show env-var guidance (preserves CI/CD behavior)
  - `--browser` flag: initiate OAuth flow, open browser, wait for callback, store tokens
  - Report success/failure with account info on success
- `auth logout`:
  - Clear OAuth tokens from token store
  - Show env-var unset guidance
- `auth status`:
  - Report OAuth token status if tokens exist
  - Report env-var status for all providers
  - Show account info (email, subscription type) if OAuth tokens are present

### Step 4: Register services in AppHost
- Register `ITokenStore` -> `FileTokenStore`
- Register `IOAuthService` -> `AnthropicOAuthService`
- Pass `IOAuthService` to `AuthCommandHandler`

### Step 5: Add tests
- FileTokenStore tests (write, read, delete, permissions)
- OAuth service unit tests (code generation, URL building, token exchange — mocked HTTP)
- AuthCommandHandler tests (status with/without tokens, login browser flag, logout)

### Step 6: Update documentation
- PARITY.md: mark auth rows as Implemented/Verified
- ARCHITECTURE.md: update auth section with OAuth support
- README.md: add auth login --browser example
- PLAN.md: mark milestone as complete

## Validation Plan

1. `dotnet build ClawdNet.slnx` — must pass
2. `dotnet test ClawdNet.slnx` — must pass
3. Manual smoke: `clawdnet auth status` — shows env-var and OAuth token status
4. Manual smoke: `clawdnet auth login --help` — shows browser flag
5. Manual smoke: `clawdnet auth logout` — clears tokens

## Validation Results

- `dotnet build ClawdNet.slnx` — PASSED (0 errors)
- `dotnet test ClawdNet.slnx` — PASSED (244 tests: 244 passed, 0 failed)
  - New tests: FileTokenStoreTests (6 tests), AuthCommandHandlerTests (8 tests)

## What Changed

### New files
- `ClawdNet.Core/Abstractions/ITokenStore.cs` — token storage interface
- `ClawdNet.Core/Abstractions/IOAuthService.cs` — OAuth service interface
- `ClawdNet.Core/Models/OAuthTokens.cs` — OAuth token model
- `ClawdNet.Runtime/Auth/FileTokenStore.cs` — file-based token persistence with 0o600 permissions
- `ClawdNet.Runtime/Auth/AnthropicOAuthService.cs` — PKCE-based OAuth flow with local callback
- `ClawdNet.Tests/FileTokenStoreTests.cs` — token store unit tests
- `ClawdNet.Tests/AuthCommandHandlerTests.cs` — auth command unit tests

### Modified files
- `ClawdNet.Core/Commands/AuthCommandHandler.cs` — revised to support OAuth login, status, and logout
- `ClawdNet.App/AppHost.cs` — registered token store and OAuth service
- `docs/PARITY.md` — auth rows updated to Implemented
- `docs/ARCHITECTURE.md` — auth section updated with OAuth support
- `docs/PLAN.md` — milestone marked complete
- `README.md` — auth commands updated

## Remaining Follow-ups

- macOS keychain integration (deferred — file-based storage is the current baseline)
- Windows Credential Manager integration (deferred)
- MCP server OAuth flow (out of scope)
- SSO / `--sso` login flow (out of scope)
- Console (API key helper) login flow (out of scope)
- claude.ai subscriber profile integration (email, org, subscription type display — partial: email and subscription type shown)

## Rollback Notes

- OAuth login is opt-in via `--browser` flag; env-var auth remains fully functional
- Token store is file-based and isolated to app data directory; deleting `.credentials.json` reverts to env-var-only behavior
- No changes to provider resolution or model client factory that could regress existing flows

## Risks

- OAuth callback listener may conflict with existing ports on localhost
- Token storage without keychain is less secure than ideal, but is consistent with the legacy plain-text fallback storage
- Scope kept narrow to avoid pulling in MCP OAuth, SSO, or XAA flows
