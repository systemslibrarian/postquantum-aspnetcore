# Changelog

All notable changes to `PostQuantum.AspNetCore` are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
once it reaches `1.0.0`. Preview releases (`0.x`) may break the API between
versions.

## [Unreleased]

_No changes yet._

## [0.2.0-preview.1] — 2026-05-30

A **substance-and-confidence** release. v0.1 proved the shape; v0.2 makes
it trustworthy enough to actually wire into something. Most additions are
on the supply-chain and observability fronts; the auth contract itself is
unchanged.

### Added

- **`PostQuantumJwtBearerEvents`** — three async event hooks modelled on
  `JwtBearerEvents`: `OnTokenValidated` (enrich the principal),
  `OnAuthenticationFailed` (observe / rarely override failures),
  `OnChallenge` (customise the 401 response). Default delegates are
  no-ops, so existing configurations behave identically.
- **`IPostQuantumJwtKeyRing.ResolveAsync`** — async key resolution hook
  with a default interface implementation that delegates to `Resolve`.
  `HttpPostQuantumJwtKeyRing` overrides it natively async, leaving
  `Resolve` as a sync-over-async cold-miss bridge until the engine
  exposes an async `SignatureKeyResolver`.
- **`tests/PostQuantum.AspNetCore.Tests`** — `Microsoft.AspNetCore.Mvc.Testing`-
  backed suite that locks the fail-closed contract at the HTTP boundary:
  valid token → `200 OK` with expected `ClaimsPrincipal`; tampered,
  expired, wrong-issuer, wrong-audience → `401 Unauthorized`; missing
  header → challenge; non-`Bearer` scheme → `NoResult`; event hooks fire
  on the right moments. **18 tests, zero skips** on PQ-capable hosts.
- **GitHub Actions: `ci.yml`** — build + test on Ubuntu and Windows.
  Windows is the PQ-required lane; the run fails if any `PqcFact` test
  reports skipped. A separate `linux-pq-required` job installs OpenSSL
  3.5+ via conda-forge and proves the ML-KEM / ML-DSA paths run on
  Linux too. A `pack-verify` job catches packaging regressions before a
  release tag is pushed.
- **GitHub Actions: `release.yml`** — `pack` + `publish` split with a
  `nuget-publish` environment gate, tag-vs-`<Version>` check, top-level
  CycloneDX SBOM, `SHA256SUMS.txt`, build-provenance attestations for
  the `.nupkg` and SBOM, optional `NUGET_SIGNING_CERT` author signing.
- **CycloneDX SBOM packed inside the `.nupkg`** (`/bom.json`) via an
  MSBuild target. Falls back gracefully when the global tool is absent,
  so a fresh dev machine still packs successfully.
- **`PackageValidationBaselineVersion=0.1.0-preview.1`** — wired
  conditionally; opt in via `-p:EnableBaselineValidation=true` once
  `0.1.0-preview.1` is on nuget.org so future versions are checked for
  accidental API breaks.
- **`docs/MIGRATION.md`** — diff-style migration guide from
  `PostQuantum.Jwt.AspNetCore` (the engine repo's companion package).
  Includes the type mapping, scheme-name change, and the new event-hook
  patterns.

### Changed

- **Handler is now natively async.** `HandleAuthenticateAsync` and
  `HandleChallengeAsync` `await` the event hooks instead of returning
  `Task.FromResult(...)`. Behaviour on the no-events default path is
  unchanged.
- **`PostQuantumJwtBearerOptions.Events`** is now the canonical extension
  surface for the handler. The base `AuthenticationSchemeOptions.Events`
  shadow follows the same pattern as `JwtBearerOptions`.

### Known limitations

- See `KNOWN-GAPS.md` — most v0.1 entries are now closed; the residual
  list focuses on the sync-over-async cold-miss path on the key ring
  (blocked on the engine), the missing version-sync script, and
  author-signing (gated on a code-signing certificate).

## [0.1.0-preview.1] — 2026-05-30

Initial preview. The definitive ASP.NET Core integration for post-quantum
JWT authentication — a clean rewrite of the
`PostQuantum.Jwt.AspNetCore` companion under its own package, name, and
release cadence.

### Added

- **`AddPostQuantumJwtBearer(…)`** extension methods on
  `AuthenticationBuilder` — mirrors the shape of `AddJwtBearer` from
  `Microsoft.AspNetCore.Authentication.JwtBearer`, so post-quantum tokens
  slot into the standard auth pipeline.
- **`PostQuantumJwtBearerHandler`** — fail-closed `AuthenticationHandler`
  that delegates to `PqJwtValidator`. Bypasses `Microsoft.IdentityModel`
  entirely.
- **`PostQuantumJwtBearerOptions`** — strongly-typed configuration with
  sensible defaults (`NameClaimType="sub"`, `RoleClaimType="role"`,
  `IncludeErrorDetailsInChallenge=true`).
- **`PostQuantumJwtBearerDefaults`** — well-known scheme name
  (`"PostQuantumJwtBearer"`) and `Bearer` prefix constant.
- **`IPostQuantumJwtKeyRing` + `HttpPostQuantumJwtKeyRing`** — the
  post-quantum analogue of JWKS: fetch a key directory from a trusted HTTPS
  endpoint with configurable refresh, in-memory cache, AOT-safe JSON
  (source-generated `JsonSerializerContext`), and the single-suite
  (`ML-DSA-65` only) policy enforced on the wire.
- **`samples/PostQuantum.AspNetCore.Demo`** — runnable minimal-API sample
  that mints a token and validates it on a protected endpoint.
- **`SECURITY.md`** and **`KNOWN-GAPS.md`** — honest statement of scope,
  threat model, and what is and isn't covered.
- **Packaging:** SourceLink, deterministic builds, symbol packages
  (`.snupkg`), `EnablePackageValidation`, `IsAotCompatible=true`,
  `IsTrimmable=true`.

### Known limitations

- No test project yet — see `KNOWN-GAPS.md`.
- `net10.0` only (pinned to the engine's TFM).
- No CI workflow yet; releases are local for `0.1.0-preview.1`.

---

[Unreleased]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.2.0-preview.1...HEAD
[0.2.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.1.0-preview.1...v0.2.0-preview.1
[0.1.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/releases/tag/v0.1.0-preview.1
