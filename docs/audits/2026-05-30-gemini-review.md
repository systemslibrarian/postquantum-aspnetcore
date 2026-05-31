# PostQuantum.AspNetCore 10/10 Review & Fixes

Based on an initial review of the codebase to make it robust, secure, and ready for millions of users, the following high-priority issues were identified and fixed:

## 1. Key Ring Eviction and Atomic Cache Swaps (Fixed)
**Issue**: Stale verification keys were never evicted after a refresh. A removed key could remain valid for the lifetime of the process.
**Fix**: `HttpPostQuantumJwtKeyRing` now builds the refreshed key set into a new dictionary and swaps it in atomically. Kids that disappear from the upstream directory are correctly removed from the active cache upon refresh.

## 2. Rate-Limiting Unknown Key Fetches (Fixed)
**Issue**: Unknown `kid` values forced a remote fetch on every miss, enabling an amplification attack where an attacker could flood the API with random `kid` values to overload the key endpoint.
**Fix**: Added local throttling in `HttpPostQuantumJwtKeyRing`. A forced refresh triggered by an unknown `kid` is now rate-limited to avoid hammering the JWKS endpoint on a cache miss.

## 3. Case-Insensitive Bearer Auth (Fixed)
**Issue**: The bearer scheme check was case-sensitive (`StringComparison.Ordinal`), rejecting valid HTTP headers like `bearer` or `BEARER`.
**Fix**: The header validation in `PostQuantumJwtBearerHandler.cs` has been updated to use `StringComparison.OrdinalIgnoreCase`, improving interoperability with different clients and proxies. We also added a corresponding test case for `bearer` (lowercase).

## 4. Unhandled Exceptions in User Event Hooks (Fixed)
**Issue**: Exceptions thrown by user-configured `OnTokenValidated` hook would escape as unhandled 500 server errors instead of clean authentication failures, bypassing `OnAuthenticationFailed`.
**Fix**: The `OnTokenValidated` execution block is now wrapped in a try/catch in the handler. Any exceptions thrown by user land are safely routed through `OnAuthenticationFailed`, logging appropriately and failing clean.

## 5. Consistent Challenge Header (Fixed)
**Issue**: The `invalid_token` parameter was added to the `WWW-Authenticate` challenge unconditionally, even for missing headers. 
**Fix**: The 401 challenge generation logic now checks `await Context.AuthenticateAsync(...)` failure state so that `error="invalid_token"` is only included when an actual token was supplied and subsequently failed validation. 

---
**Status**: `dotnet test` passes with full coverage for the newly added edge cases, and the handler is significantly more resilient for massive scale and production deployment.