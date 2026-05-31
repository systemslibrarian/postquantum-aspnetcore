# Security Policy

PostQuantum.AspNetCore is **preview software** (`0.x.y-preview.z`). It is not
yet suitable for production use and has not been independently audited. This
document states the security model honestly so you can make an informed
decision before relying on it.

## Supported versions

| Version             | Supported          |
|---------------------|--------------------|
| `0.1.0-preview.1`+  | ✅ (latest preview) |
| anything older      | ❌                 |

During the `0.x` series only the most recent preview receives fixes.

## Reporting a vulnerability

Please report security issues **privately** — do not open a public issue for
an exploitable flaw.

- Use GitHub's **"Report a vulnerability"** (Security → Advisories) on the
  repository, **or**
- email the maintainer listed on the GitHub profile.

Please include a description, affected version, and a reproduction if
possible. We aim to acknowledge within **5 business days**. As an unfunded
preview project, timelines are best-effort and stated honestly rather than
promised.

## Threat model

`PostQuantum.AspNetCore` is the *receiving* half of a post-quantum JWT
deployment. It validates tokens that arrive over `Authorization: Bearer …`,
delegates the cryptographic work to
[`PostQuantum.Jwt`](https://github.com/systemslibrarian/postquantum-jwt),
and maps the validated claims onto an ASP.NET Core `ClaimsPrincipal`. The
relevant threats and where they're handled:

**In scope**

- **Forged or tampered tokens.** Any validation failure inside
  `PqJwtValidator` (bad signature, tampered ciphertext, wrong issuer or
  audience, missing or unexpected `alg`, expired, not-yet-valid, replayed
  `jti` when a cache is configured) is turned into
  `AuthenticateResult.Fail(exception)`. No success ticket is constructed on
  any failing path.
- **Algorithm confusion.** The validator does not trust the token's `alg`
  header to choose a code path — it accepts exactly one suite. `alg: none`,
  RS/HS confusion, downgrade attacks: not reachable from this handler.
- **Authorization-header smuggling.** The bearer prefix is matched
  ordinally (`StringComparison.Ordinal`) so casing tricks don't bypass
  validation. Empty or whitespace-only tokens are treated as
  `AuthenticateResult.NoResult()`, not success.
- **Cross-handler bleed.** `AddPostQuantumJwtBearer` registers under a
  distinct scheme name (`"PostQuantumJwtBearer"` by default) so it does not
  silently override an existing `JwtBearer` registration on the default
  scheme. README documents the safe coexistence pattern.

**Out of scope / your responsibility**

- **Key management & storage.** Generating, protecting, rotating, and
  distributing the ML-DSA-65 signing/verification keys is the caller's
  responsibility. This package consumes keys; it does not provision them.
- **Replay protection at the application layer.** `jti` is carried by tokens
  but only enforced when you configure `PqJwtValidationParameters.ReplayCache`
  with a cache that fits your deployment. The library does not pick that
  default for you.
- **Transport security.** TLS termination, certificate management, and
  forwarded-header handling are ASP.NET Core / hosting concerns — not
  handled here.
- **Authorization policy.** This package authenticates; you decide what
  claims mean. `[Authorize(Roles = "...")]` works against the `"role"` claim
  by default, but role semantics are yours to define.
- **Side-channel resistance beyond the underlying primitives.** We rely on
  the constant-time properties of the .NET BCL post-quantum primitives and
  the BouncyCastle X25519 used by `PostQuantum.Jwt`; we add no guarantees of
  our own.
- **Standards interoperability.** Tokens use non-IANA algorithm identifiers
  and are not meant to validate in generic JWT libraries.

## Cryptographic construction

This package adds **no new cryptography** of its own. Every byte of signing,
verification, key agreement, and content encryption goes through
`PostQuantum.Jwt`. The construction summary lives in that repository's
`SECURITY.md`; for completeness:

| Role                  | Algorithm   | Source              |
|-----------------------|-------------|---------------------|
| Signature             | ML-DSA-65   | .NET BCL (`MLDsa`)  |
| KEM (PQ half)         | ML-KEM-768  | .NET BCL (`MLKem`)  |
| KEM (classical half)  | X25519      | BouncyCastle        |
| KEM combiner          | SHA3-256    | BouncyCastle        |
| Content encryption    | AES-256-GCM | .NET BCL (`AesGcm`) |

The full X-Wing combiner definition, including the six-byte label and the
binding of the JWE protected header as AES-GCM AAD, is in
[`postquantum-jwt/SECURITY.md`](https://github.com/systemslibrarian/postquantum-jwt/blob/main/SECURITY.md).

## Dependency rationale

This package depends on:

- **`PostQuantum.Jwt`** — the engine. All cryptographic work.
- **`Microsoft.AspNetCore.App` (framework reference)** — the ASP.NET Core
  authentication abstractions (`AuthenticationHandler`, `AuthenticationBuilder`,
  `AuthenticationSchemeOptions`).
- **`Microsoft.SourceLink.GitHub`** (build-time only) — for symbol package
  source links.

No other runtime dependencies. The HTTP key ring uses
`System.Net.Http.Json` with a source-generated `JsonSerializerContext`,
keeping the JSON path AOT- and trim-safe and avoiding the reflection-based
serializer entirely.

## Honesty statement

This is preview cryptographic-adjacent software written in the open. The
cryptography itself is delegated to `PostQuantum.Jwt`, which is also preview
and not externally audited — see its
[`KNOWN-GAPS.md`](https://github.com/systemslibrarian/postquantum-jwt/blob/main/KNOWN-GAPS.md).
Known limitations specific to this package are tracked transparently in
[`KNOWN-GAPS.md`](KNOWN-GAPS.md). Until a 1.0 release and an external review,
treat both packages as suitable for experimentation only.

---

*To God be the glory — 1 Corinthians 10:31.*
