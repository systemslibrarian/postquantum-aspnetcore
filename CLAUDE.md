# CLAUDE.md — PostQuantum.AspNetCore

Conventions and guardrails for working in this repository. Read before making
changes.

## What this is

The **high-level ASP.NET Core integration** for
[`PostQuantum.Jwt`](https://github.com/systemslibrarian/postquantum-jwt) —
`AddPostQuantumJwtBearer(…)` plus a small set of supporting types so hybrid
post-quantum JWTs slot into the standard ASP.NET Core auth pipeline.

**What this package is NOT.** It is not a cryptography library. It is not
a competitor to BouncyCastle, liboqs, or `System.Security.Cryptography`.
No implementation of ML-DSA, ML-KEM, X25519, AES-GCM, or SHA-3 lives in
this repo. Every cryptographic operation goes through the engine library
(`PostQuantum.Jwt`), which in turn uses the FIPS-validated .NET 10 BCL
post-quantum primitives plus BouncyCastle (for the X25519 piece the BCL
doesn't ship). If a change here looks like it's reaching for key
material, signatures, KEM operations, or block-cipher modes, that's a
sign the work belongs in `PostQuantum.Jwt`, not here.

**What this package IS.** The application-layer wiring: extension
methods, an `AuthenticationHandler`, options + four event hooks
(`OnMessageReceived` / `OnTokenValidated` / `OnAuthenticationFailed` /
`OnChallenge`), a JWKS-equivalent key ring, a hosted-service warmup,
metrics + tracing via `System.Diagnostics`. Everything you'd otherwise
hand-write between your ASP.NET Core app and the engine library.

## Engineering discipline

- **Honesty over polish.** If something is incomplete, unproven, or risky,
  say so — in code comments, `SECURITY.md`, and `KNOWN-GAPS.md`. Never
  overstate what the library provides.
- **Fail-closed, always.** Every validation failure becomes
  `AuthenticateResult.Fail`. No "best-effort" success ticket, no fallback to
  an alternate algorithm, no degraded mode.
- **Don't reinvent the engine.** If you're tempted to copy a piece of
  `PqJwtValidator` here for "convenience", stop. Add the missing abstraction
  upstream instead.
- **Native first, then `Microsoft.AspNetCore.App`, then the engine.** No new
  third-party runtime dependencies without a written justification in
  `SECURITY.md`.
- **Keep the surface small.** No speculative options, no kitchen-sink
  events. Add only what a real consumer is asking for.

## Code conventions

- **Target:** `net10.0` only. Tracked in `KNOWN-GAPS.md` — multi-targeting
  is contingent on the engine library doing the same.
- **Nullable** and **implicit usings** are on. `LangVersion` is `latest`.
- **Warnings:** compiler warnings are errors (`TreatWarningsAsErrors`),
  analyzer (`CAxxxx`) suggestions stay warnings
  (`CodeAnalysisTreatWarningsAsErrors=false`). Don't suppress an analyzer
  without a comment explaining why.
- **Public API is documented.** XML doc comments on every public member
  (`GenerateDocumentationFile` is on).
- **AOT/trim-safe.** `IsAotCompatible=true`, `IsTrimmable=true`. The HTTP
  key-ring JSON path uses a source-generated `JsonSerializerContext`. Don't
  introduce reflection-based serialization here.
- **Deterministic builds** are enabled repo-wide; don't add nondeterminism.
- **Naming:** `PostQuantumJwtBearer*` for the public ASP.NET Core surface,
  matching the `AddPostQuantumJwtBearer` extension method;
  `IPostQuantumJwtKeyRing` / `HttpPostQuantumJwtKeyRing` for the JWKS
  analogue. Internal helpers stay internal.
- **Logging:** source-generated `LoggerMessage` methods only — satisfies
  CA1848 and stays AOT-friendly.

## Layout

```
src/PostQuantum.AspNetCore/        library
  PostQuantumJwtBearer*.cs           handler / options / extensions / defaults
  IPostQuantumJwtKeyRing.cs          JWKS-equivalent abstraction
  HttpPostQuantumJwtKeyRing.cs       HTTP-backed implementation (AOT-safe JSON)
  Logging.cs                         source-generated logger messages
samples/PostQuantum.AspNetCore.Demo/ runnable end-to-end demo
tests/                               reserved for the upcoming TestServer suite
docs/                                long-form documentation slot
```

## Build & run

```bash
dotnet build
dotnet run --project samples/PostQuantum.AspNetCore.Demo
```

The demo binds to `http://localhost:5000` and exposes:

- `GET  /` — landing page
- `POST /dev/token` — mints a 15-minute demo token (do **not** ship this
  endpoint as written; it's for the demo)
- `GET  /me` — protected; echoes the validated `sub` / `role` / `iss` / `aud`

## Relationship to `PostQuantum.Jwt.AspNetCore`

The engine repository ships a sibling package, `PostQuantum.Jwt.AspNetCore`,
that exposes the same shape under `Pq*`-prefixed names (`AddPqJwtBearer`,
`PqJwtBearerHandler`, …). `PostQuantum.AspNetCore` is the renamed, repackaged
**successor** — same engine, same shape, cleaner naming, its own release
cadence. Until both are at 1.0, expect a transition window where both exist.
Don't cross-reference types between the two packages.

## Faith statement

This project is built in gratitude to God. Documentation ends with:

> *To God be the glory — 1 Corinthians 10:31.*

Keep that footer on the README and the security docs.
