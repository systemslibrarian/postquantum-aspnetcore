# Changelog

All notable changes to `PostQuantum.AspNetCore` are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
once it reaches `1.0.0`. Preview releases (`0.x`) may break the API between
versions.

## [Unreleased]

_No changes yet._

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

[Unreleased]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.1.0-preview.1...HEAD
[0.1.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/releases/tag/v0.1.0-preview.1
