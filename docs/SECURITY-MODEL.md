# Security model

What this library protects, what it doesn't, and how to wire the
pieces together so the security claim holds end-to-end. **Read this
before you depend on the library for anything that matters.**

This document is the security contract — paired with
[`../SECURITY.md`](../SECURITY.md) (the formal threat model + disclosure
policy) and [`../KNOWN-GAPS.md`](../KNOWN-GAPS.md) (the running list of
what's unverified).

## TL;DR

`PostQuantum.AspNetCore` is the **application-layer wiring** that turns
`PostQuantum.Jwt`'s post-quantum hybrid tokens into ASP.NET Core
authentication. It validates signed JWTs with ML-DSA-65, exposes the
result as a `ClaimsPrincipal`, and **fails closed** on any error. It
does not store keys, mint tokens at request time, terminate TLS, or
prevent denial-of-service.

If a token is well-formed, signature-correct, within its lifetime,
issued by the configured issuer, audienced to the configured audience,
and (when a replay cache is configured) not previously seen — it
authenticates. **Every other case is a `401`.** There is no degraded
path, no "best-effort" success, no algorithm fallback.

## What this library protects

### ✅ Token integrity and authenticity

Every accepted token must carry a valid ML-DSA-65 signature over its
header + payload, produced by a private key whose public counterpart
the validator has been configured with (statically, via
`SignatureVerificationKey`, or dynamically by `kid`, via the key
ring). Tokens with no signature, the wrong signature, or a tampered
header/payload are rejected with `PqJwtValidationException` → `401`.

### ✅ Issuer and audience binding

When `ValidIssuer` and `ValidAudience` are configured (and they
**always should be**), tokens whose `iss` or `aud` claim doesn't
exact-match are rejected. This prevents a token minted for a different
service or by a different issuer from being accepted.

### ✅ Lifetime enforcement

`exp` is required by default (`RequireExpiration = true`). Tokens
past their `exp` (minus `ClockSkew`, default 60s) are rejected.
Tokens with a `nbf` in the future are also rejected.

### ✅ Algorithm pinning

The validator accepts exactly **one** signature suite: `ML-DSA-65`.
It does not trust the token's `alg` header to choose a code path —
it checks for an exact-match equality and rejects anything else.
`alg: none`, RS/HS confusion, and downgrade attacks are
**unreachable by construction**.

### ✅ Fail-closed exception handling

The handler catches every non-fatal exception out of
`PqJwtValidator.Validate` and converts it to
`AuthenticateResult.Fail` (→ `401`). Engine-level leaks
(`FormatException` from bad Base64, `CryptographicException`,
`JsonException`) used to escape as `500`s and now don't.
`OutOfMemoryException` and `StackOverflowException` are deliberately
**not** caught — those are environmental and should crash the host so
an operator notices.

### ✅ Bearer-prefix normalization

`Authorization: Bearer …` is matched case-insensitively (RFC 6750).
`bearer`, `BEARER`, and `Bearer` all work, but empty or
whitespace-only tokens are rejected as "no result" (not "fail") so
other auth schemes on the same request still get a turn.

### ✅ RFC-compliant challenge responses

The `WWW-Authenticate` header issued on 401 is a well-formed RFC 7235
quoted-string — realm values containing `"` or `\` are
backslash-escaped. The `error="invalid_token"` parameter (RFC 6750)
is emitted only when a token was actually supplied and rejected, not
on a bare missing-header case.

### ✅ Replay protection (when configured)

When `PqJwtValidationParameters.ReplayCache` is configured — usually
via the companion package
[`PostQuantum.AspNetCore.RedisReplayCache`](../src/PostQuantum.AspNetCore.RedisReplayCache)
— the validator atomically records each token's `jti` on first use
and rejects subsequent presentations. **A token captured in transit
cannot be replayed once it's been used.** TTL = remaining token
lifetime, so the cache cleans itself up.

> **This is opt-in.** With no `ReplayCache` configured, `jti` is
> carried by the token but **not enforced** — a stolen token is
> reusable until it expires. See "Replay protection requirements"
> below.

## What this library does NOT protect

These are the operator's responsibility. The library cannot enforce
them for you.

### ❌ Key generation, storage, and distribution

Your issuer service holds the ML-DSA-65 **signing key**. This library
is on the verifier side and only sees the **verification key**
(public half). Where you store the signing key (HSM, KMS, secrets
manager, an `appsettings.json` checked into git — please don't) and
how you publish the verification key (JWKS-equivalent endpoint,
embedded constant, configuration) are entirely your call.

If the signing key leaks, tokens are forgeable. The library cannot
help.

### ❌ Token confidentiality on the wire

A bearer token is whoever holds it. Use HTTPS for every endpoint
that accepts a token. The library does not enforce TLS — that's
ASP.NET Core / your reverse proxy / your hosting environment.

### ❌ Transport-level integrity beyond the token

If your reverse proxy strips the `Authorization` header, the library
sees nothing. If it spoofs `X-Forwarded-For` or `X-Forwarded-Proto`
without your `ForwardedHeaders` middleware configured to trust it,
the library has no visibility. Configure forwarded-header trust
correctly **before** authentication runs in the pipeline.

### ❌ Authorization policy

The library produces a `ClaimsPrincipal` from validated claims. What
those claims mean — what roles unlock what endpoints, what tenants
own what resources — is your authorization layer's job. Use
`[Authorize(Roles = ...)]`, policies, or custom
`IAuthorizationHandler` implementations to decide.

### ❌ Denial of service

A flood of requests, each carrying a (correctly-signed) token, costs
~ms per request to validate (ML-DSA-65 verify). Rate-limiting,
load-shedding, and DDoS protection are upstream concerns.

For the specific case of an attacker flooding unknown `kid` values
to amplify outbound fetches to your key endpoint, the HTTP key ring
**does** include throttling: a forced refresh fires at most once
every 10 seconds. Beyond that, your DDoS layer.

### ❌ Token revocation beyond expiration

Without revocation, a token issued at 12:00 with a 60-minute lifetime
is valid until 13:00 even if you discover at 12:15 that the user's
account was compromised. Mitigations the library does not implement:

- **Short token lifetimes** + **refresh tokens** — issue short-lived
  access tokens (5–15 min) and require a refresh-token round-trip to
  get a new one. The refresh endpoint can revoke.
- **Revocation list** — maintain a denylist of revoked `jti` values
  in a fast store; check it during validation. Implement
  `IPqJwtReplayCache` (or wrap one) to gate on revocation as well as
  replay.

### ❌ Side-channel resistance beyond the primitives

The library performs no constant-time operations of its own. It
relies on the constant-time properties of the BCL post-quantum
primitives and BouncyCastle X25519 used by `PostQuantum.Jwt`. If
your threat model includes timing-attack resistance against the
auth layer specifically, deploy a constant-time-aware reverse proxy
or measure carefully.

### ❌ Side-channel resistance of your own code

`OnTokenValidated` hooks that hit a database with the validated
`sub` claim can leak account-existence via timing. Defending that
is your business logic's job.

## Replay protection requirements

This is the most-asked-about gap. **The library does NOT enforce
replay protection by default.** It's opt-in by design — single-process
apps don't need a distributed cache, and forcing one would be
over-prescriptive.

### Deployment-shape matrix

| Your deployment             | Replay-protection recommendation                                                            |
|-----------------------------|---------------------------------------------------------------------------------------------|
| Single instance, dev/staging | `InMemoryReplayCache` from `PostQuantum.Jwt`. Single-process, no setup.                    |
| Multi-instance production   | **`PostQuantum.AspNetCore.RedisReplayCache`** companion package. Coordinates across the fleet via Redis SET NX. |
| Custom data store (DB, Memcached, DynamoDB) | Implement `IPqJwtReplayCache` against your store. The contract is one method: atomic `TryRegister(jti, expiresAt) → bool`. |
| No replay protection (deliberate) | Acceptable only if every token is single-use by your business logic anyway (e.g. signed for an action that can only happen once). Document this decision. |

### Wireup (Redis)

```csharp
using PostQuantum.AspNetCore.RedisReplayCache;

builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            SignatureVerificationKey = verificationKey,
            ValidIssuer   = "https://issuer.example",
            ValidAudience = "https://api.example",
        };
    });

builder.Services.AddPostQuantumJwtRedisReplayCache(
    connectionString: builder.Configuration["Redis:ConnectionString"]!);
```

That's it. Every token's `jti` is atomically recorded on first use;
replays return false from the cache → validator throws → handler
returns 401.

## Key rotation

The library supports two patterns:

### Pattern A — static verification key

Configure `SignatureVerificationKey` once at startup. Rotation = redeploy
with a new key. Simple, suits single-issuer single-key deployments.

### Pattern B — JWKS-equivalent key ring

Configure `AddPostQuantumJwtKeyRing(uri)` or a custom
`IPostQuantumJwtKeyRing`. Tokens carry a `kid` header; the ring
resolves the verification key per-token. Rotation = the issuer
publishes a new key on the directory endpoint, then starts using its
`kid` on new tokens; verifiers pick up the new key on their refresh
interval or on first unknown-`kid` miss.

**Recommended cadence:**

1. Issue with `kid=A` while the directory advertises `{A, B}`.
2. Switch issuing to `kid=B` while the directory still advertises
   `{A, B}` — old tokens still validate.
3. After the longest outstanding token's lifetime elapses, remove
   `A` from the directory.
4. Verifiers' caches evict `A` on next successful refresh.

The eviction is atomic — readers see either the old snapshot or the
new one, never a torn intermediate. In-flight validations against
`A`'s `MLDsa` instance complete cleanly because the library does not
eagerly dispose retired keys; GC releases them when no validator
holds them.

### Warm the cache at startup

```csharp
builder.Services.AddPostQuantumJwtKeyRing(keysUri);
builder.Services.AddPostQuantumJwtKeyRingWarmup(options =>
{
    options.FailFastOnStartup = true;            // host won't start if endpoint is down
    options.RefreshInterval   = TimeSpan.FromMinutes(15);
});
```

`FailFastOnStartup = true` (default) means an unreachable key
endpoint aborts host startup — strict, but it surfaces operational
problems before user traffic hits.

## Fail-closed contract

Every validation failure produces `401`. The library has **no**
success path that doesn't first pass:

```
present(token) ∧ scheme = Bearer ∧ token ≠ ""                       (header normalization)
∧ token splits into 3 (signed) or 5 (encrypted) segments            (structure)
∧ header.alg = "ML-DSA-65"                                           (algorithm pin)
∧ signature verifies against the resolved verification key           (authenticity)
∧ payload parses as JSON object                                      (well-formed)
∧ exp present ∧ exp + ClockSkew > now                                (not expired)
∧ nbf absent ∨ nbf - ClockSkew ≤ now                                 (not premature)
∧ ValidIssuer absent ∨ iss = ValidIssuer                             (issuer pinned)
∧ ValidAudience absent ∨ aud = ValidAudience                         (audience pinned)
∧ ReplayCache absent ∨ TryRegister(jti, exp) = true                  (not a replay)
```

Failure at any step → `PqJwtValidationException` → `AuthenticateResult.Fail`
→ `401 Unauthorized`. The handler also defends-in-depth against any
non-fatal exception out of `Validate()` — engine-level parser leaks
no longer surface as `500`s.

## Why this is different from standard `JwtBearer`

`Microsoft.AspNetCore.Authentication.JwtBearer` is the right choice
for the vast majority of JWT work today. We don't replace it; we
solve a different problem.

| Concern               | Standard `JwtBearer`                                              | `PostQuantum.AspNetCore`                                       |
|-----------------------|-------------------------------------------------------------------|----------------------------------------------------------------|
| Algorithms accepted   | Entire IANA catalogue (RS/PS/ES/EdDSA/HS).                        | Exactly **one suite**: ML-DSA-65.                              |
| Quantum resistance    | None of the standard algorithms are quantum-resistant.            | Hybrid: classical *and* post-quantum, both must fall.          |
| Algorithm agility     | Yes — historically the source of `alg: none`, RS/HS confusion.    | **No, by design.** Token's `alg` doesn't pick a code path.     |
| Standards interop     | Full IANA-registered identifiers.                                 | Identifiers are not IANA-registered yet — non-interoperable.   |
| OAuth/OIDC integration | First-class.                                                     | None. You issue your own PQ tokens.                            |
| External audit        | Yes — decade-hardened, widely deployed.                           | **No.** Preview, not audited.                                  |
| Production readiness  | Yes.                                                              | **Preview only.**                                              |

**Use standard `JwtBearer`** for every existing JWT scenario where
classical algorithms are acceptable.

**Use `PostQuantum.AspNetCore`** specifically when you control both
the issuer and the verifier, you want post-quantum tokens *now*, and
you accept that this is preview software.

The two schemes coexist cleanly — register both, route specific
endpoints to specific schemes. See
[`RECIPES.md` § 7](RECIPES.md#7-coexist-with-the-standard-jwtbearer-scheme-during-migration).

---

If a question about the security model isn't answered here, please
open an issue. Honest answers are part of the contract.

---

*To God be the glory — 1 Corinthians 10:31.*
