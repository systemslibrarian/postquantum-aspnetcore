# Security Policy

PostQuantum.AspNetCore is a **production-quality library** (`1.0.0`, stable)
for **controlled issuer/verifier systems** — environments where the same team
owns both token issuing and token validation. "Production-quality" describes
the hardened, fail-closed integration (strict validation, replay and
key-rotation support, no insecure fallback), **not** an audit sign-off: the
underlying cryptographic construction has **not** been independently audited,
and this is **not** a drop-in replacement for OAuth/OIDC/JWT middleware. The
public API is stable under SemVer from `1.0.0` onward.

**The lack of an independent cryptographic audit is a permanent, documented
limitation, not a temporary gate.** Through the preview series an external
audit was framed as the blocker to `1.0`; at `1.0.0` that gate was removed
deliberately — matching the engine library's own `1.0.0` — because an
unfunded project is unlikely to secure a formal review, and perpetual
`preview` served no one. No third party has reviewed the design or
implementation, and none is scheduled. Adopt this only where you control
both issuer and verifier and accept that risk with eyes open. This document
states the security model honestly so you can make an informed decision
before relying on it.

## Supported versions

| Version             | Supported                    |
|---------------------|------------------------------|
| `1.0.x`             | ✅ (current stable line)     |
| `1.0.0-preview.*`   | ❌ (superseded by `1.0.0`)   |
| `0.x.y-preview.z`   | ❌ (superseded)              |

Only the current stable line receives fixes.

## Reporting a vulnerability

Please report security issues **privately** — do not open a public issue for
an exploitable flaw.

- Use GitHub's **"Report a vulnerability"** (Security → Advisories) on the
  repository, **or**
- email the maintainer listed on the GitHub profile.

Please include a description, affected version, and a reproduction if
possible. We aim to acknowledge within **5 business days**. As an unfunded
project, timelines are best-effort and stated honestly rather than promised.

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

## Trust root: the HTTP key directory

When you wire `AddPostQuantumJwtKeyRing(uri)` (or construct
`HttpPostQuantumJwtKeyRing` directly), **the HTTPS endpoint you point at
becomes the root of trust for token validation.** A successful
man-in-the-middle on that endpoint can substitute attacker-controlled
verification keys and forge any token. The library has **no insecure
fallback by design**: if the fetch fails closed, validation fails closed.

This package does not pin certificates for you. **Operators should
configure certificate pinning, a hardened `HttpClient`, or both** on the
typed client the key ring uses. The recommended pattern is the
`configureHttpClient` DI hook:

```csharp
services.AddPostQuantumJwtKeyRing(
    endpoint: new Uri("https://keys.example.com/pq/keys"),
    configureHttpClient: builder => builder
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = MyPinningCallback,
            },
        }));
```

Defense-in-depth measures the library does apply by default when you use
the DI helper:

- **Response-size cap.** `HttpClient.MaxResponseContentBufferSize` is set
  to **1 MB**. A real key directory is tens of keys × a few hundred
  bytes; the cap rejects bodies designed to drive memory pressure. The
  cap can be overridden via `configureHttpClient` if you have a genuinely
  larger directory.
- **Single-suite enforcement on the wire.** Entries whose `alg` is
  anything other than `ML-DSA-65` are silently dropped at the key-ring
  boundary — a hostile directory cannot trick a consumer into validating
  an `RS256` token by serving a key with that `alg`.
- **Atomic-snapshot key rotation.** Refreshes never produce a torn
  intermediate state; readers see either the pre- or post-refresh
  snapshot, never both.
- **Bounded short-TTL negative cache.** A `kid` confirmed missing by a
  recent refresh is short-circuited to `null` for up to 10 seconds
  *before* the sync-over-async refresh bridge runs. This deflects
  random-`kid` flooding from amplifying into thread-pool pressure.
  The 10-second TTL is intentionally equal to the existing forced-
  refresh throttle window, so a `kid` which becomes valid after a
  refresh-miss is wrongly rejected for **at most 10 seconds**, identical
  to the pre-existing bound.

See also `KNOWN-GAPS.md` for what the library does *not* do here.

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

This is cryptographic-adjacent software written in the open. The
cryptography itself is delegated to `PostQuantum.Jwt` (stable `1.0.0`),
which has **not** been externally audited and has no review scheduled — at
`1.0.0` that is a permanent, documented limitation rather than a pending
gate; see its
[`KNOWN-GAPS.md`](https://github.com/systemslibrarian/postquantum-jwt/blob/main/KNOWN-GAPS.md).
Known limitations specific to this package are tracked transparently in
[`KNOWN-GAPS.md`](KNOWN-GAPS.md). Treat the absence of an independent audit
as the load-bearing caveat: both packages are appropriate for controlled
issuer/verifier systems whose operators accept that documented risk — not
for anonymous-relying-party or standards-interop deployments.

---

*To God be the glory — 1 Corinthians 10:31.*
