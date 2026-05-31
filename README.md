# PostQuantum.AspNetCore

[![NuGet](https://img.shields.io/nuget/vpre/PostQuantum.AspNetCore?label=nuget&color=blue)](https://www.nuget.org/packages/PostQuantum.AspNetCore)
[![CI](https://github.com/systemslibrarian/postquantum-aspnetcore/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/systemslibrarian/postquantum-aspnetcore/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

**The high-level ASP.NET Core integration for post-quantum JWT authentication.**
One line of configuration — `AddPostQuantumJwtBearer(…)` — and hybrid
ML-DSA-65 + X-Wing tokens slot into the standard authentication pipeline
exactly the way `AddJwtBearer` always has. Built on
[`PostQuantum.Jwt`](https://github.com/systemslibrarian/postquantum-jwt) and
the native .NET 10 BCL post-quantum primitives. Fail-closed by construction,
small surface, honest about its limits.

> **What this package *is*: a thin, opinionated **application layer** for
> ASP.NET Core authentication.** Extension methods, an
> `AuthenticationHandler`, event hooks, a JWKS-equivalent key ring, a
> hosted-service warmup, and metrics + tracing — all the wiring you'd
> otherwise write yourself to make post-quantum JWTs feel native to
> `AddAuthentication`.
>
> **What this package is *not*: a cryptography library.** No
> implementation of ML-DSA, ML-KEM, X25519, AES-GCM, or SHA-3 lives in
> here. We don't compete with [BouncyCastle](https://www.bouncycastle.org/csharp/),
> liboqs, or `System.Security.Cryptography`. The actual signing,
> verification, key encapsulation, and content encryption all happen
> inside [`PostQuantum.Jwt`](https://github.com/systemslibrarian/postquantum-jwt),
> which in turn uses the FIPS-validated .NET 10 BCL post-quantum
> primitives (with BouncyCastle for the one piece the BCL doesn't ship:
> X25519). Think of `PostQuantum.AspNetCore` as **the** `AddJwtBearer`
> **equivalent** that knows the right things about ML-DSA-65 — not a
> reinvention of the crypto stack underneath.

> **Status — `0.6.0-preview.1`.** Preview software. Not for production use.
> The API may change before 1.0, and the underlying cryptographic construction
> has not been independently audited. Read [`KNOWN-GAPS.md`](KNOWN-GAPS.md)
> before depending on this for anything that matters.

---

## Where does this fit in the stack?

```
┌──────────────────────────────────────────────────────────────────────┐
│  Your ASP.NET Core app                                               │
│  builder.Services.AddAuthentication().AddPostQuantumJwtBearer(...)   │
├──────────────────────────────────────────────────────────────────────┤
│  PostQuantum.AspNetCore                                  (this lib)  │
│  · AuthenticationHandler + options + 4 event hooks                   │
│  · IPostQuantumJwtKeyRing (JWKS-equivalent)                          │
│  · Hosted-service warmup, metrics, tracing                           │
├──────────────────────────────────────────────────────────────────────┤
│  PostQuantum.Jwt                          (the engine, separate pkg) │
│  · PqJwtBuilder / PqJwtValidator                                     │
│  · X-Wing combiner, JWE wire format, replay cache                    │
├──────────────────────────────────────────────────────────────────────┤
│  Crypto primitives                                  (not this lib)   │
│  · System.Security.Cryptography.MLDsa / MLKem  (.NET 10 BCL)         │
│  · BouncyCastle.Cryptography                   (X25519 only)         │
└──────────────────────────────────────────────────────────────────────┘
```

This library sits at the **top** of that stack — the application
integration layer. It does **no cryptography of its own**. If you're
looking for raw ML-DSA, ML-KEM, X25519, or AES-GCM, those live in the
.NET BCL and BouncyCastle and we are happy customers, not competitors.

## Table of contents

- [Where does this fit in the stack?](#where-does-this-fit-in-the-stack)
- [Why](#why)
- [Install](#install)
- [60-second tour](#60-second-tour)
- [Usage](#usage)
  - [Sign and validate](#sign-and-validate)
  - [Events: enrich, observe, customize the challenge](#events-enrich-observe-customize-the-challenge)
  - [Key rotation across services](#key-rotation-across-services)
  - [Custom scheme name](#custom-scheme-name)
- [Public API at a glance](#public-api-at-a-glance)
- [Defaults and what they mean](#defaults-and-what-they-mean)
- [Compared to `Microsoft.AspNetCore.Authentication.JwtBearer`](#compared-to-microsoftaspnetcoreauthenticationjwtbearer)
- [Migrating from `PostQuantum.Jwt.AspNetCore`](#migrating-from-postquantumjwtaspnetcore)
- [Security posture](#security-posture)
- [Compatibility](#compatibility)
- [Building from source](#building-from-source)
- [Contributing](#contributing)
- [License](#license)

---

## Why

**Why a separate package, when you could just call `PostQuantum.Jwt`
yourself from your ASP.NET Core app?** Because authentication wiring is
where the bugs live. Token retrieval from `Authorization` (or `?access_token=`
for SignalR), case-insensitive `Bearer` prefix matching, the
`WWW-Authenticate` challenge response with RFC-compliant realm escaping,
event hooks for principal enrichment, key-ring rotation, hosted-service
cache warmup, fail-closed handling of every exception path, metrics for
ops dashboards, distributed-tracing spans — `Microsoft.AspNetCore.Authentication.JwtBearer`
does all of that for the *classical* algorithms. This library does it
for `ML-DSA-65`. **You shouldn't have to write the wiring yourself.**

**Why post-quantum at all?**
A cryptographically relevant quantum computer would break the elliptic-curve
math behind every JWT signature in production today (EdDSA, ECDSA, RSA). Pure
post-quantum schemes are new and comparatively under-attacked. **Hybrid** hedges
both at once:

- **Signatures — ML-DSA-65** (FIPS 204). NIST-standardised lattice signature,
  security category 3.
- **Key agreement — X-Wing.** The IETF hybrid KEM combining **X25519** with
  **ML-KEM-768** (FIPS 203), bound together by a SHA3-256 combiner. An
  attacker must break *both* to recover the key.

`Microsoft.AspNetCore.Authentication.JwtBearer` is the right choice for the
vast majority of JWT work today — it speaks the entire IANA JOSE algorithm
catalogue and has been hardened over a decade of production use. But
`Microsoft.IdentityModel` does **not** understand `ML-DSA-65`, and shimming a
post-quantum algorithm into a token validator that wasn't designed for it is
the wrong shape of problem. `PostQuantum.AspNetCore` bypasses that path
entirely: a fail-closed `AuthenticationHandler` that delegates to
[`PqJwtValidator`](https://github.com/systemslibrarian/postquantum-jwt) and
nothing else.

---

## Install

```bash
dotnet add package PostQuantum.AspNetCore --version 0.6.0-preview.1
```

Or in a `.csproj`:

```xml
<PackageReference Include="PostQuantum.AspNetCore" Version="0.6.0-preview.1" />
```

**Runtime requirement:** the native ML-KEM / ML-DSA primitives need an
OpenSSL build that exposes them — **OpenSSL 3.5 or later** on Linux, or a
recent Windows. Where they are unavailable, the underlying `PostQuantum.Jwt`
engine fails closed with a clear error rather than silently falling back to
weaker crypto.

---

## 60-second tour

```csharp
using PostQuantum.AspNetCore;
using PostQuantum.Jwt;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

using var verificationKey = MLDsa.ImportMLDsaPublicKey(
    MLDsaAlgorithm.MLDsa65,
    Convert.FromBase64String(builder.Configuration["Auth:VerificationKey"]!));

builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            SignatureVerificationKey = verificationKey,
            ValidIssuer   = builder.Configuration["Auth:Issuer"],
            ValidAudience = builder.Configuration["Auth:Audience"],
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/me", (HttpContext ctx) => new
{
    sub  = ctx.User.FindFirst("sub")?.Value,
    role = ctx.User.FindFirst("role")?.Value,
}).RequireAuthorization();

app.Run();
```

That's the whole integration. The handler is fail-closed by construction
(tampered, expired, or wrong-issuer tokens produce `AuthenticateResult.Fail`),
`RequireAuthorization()` returns 401 to unauthenticated callers, and standard
`[Authorize(Roles = "...")]` attributes work against the `"role"` claim.

A runnable end-to-end version of this — issuer endpoint, protected endpoint,
ephemeral key pair — lives in [`samples/PostQuantum.AspNetCore.Demo`](samples/PostQuantum.AspNetCore.Demo).

```bash
dotnet run --project samples/PostQuantum.AspNetCore.Demo
# in another shell
TOKEN=$(curl -s -X POST http://localhost:5000/dev/token | jq -r .token)
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/me
```

A second sample — [`samples/PostQuantum.AspNetCore.SignalR.Demo`](samples/PostQuantum.AspNetCore.SignalR.Demo)
— exercises the `OnMessageReceived` event end-to-end against a real
SignalR hub with the canonical `?access_token=` connection pattern,
plus an in-page browser client so the whole loop runs in one process:

```bash
dotnet run --project samples/PostQuantum.AspNetCore.SignalR.Demo
# browse to http://localhost:5050/
```

---

## Usage

### Sign and validate

The handler validates whatever `PqJwtValidator` accepts — single-key,
issuer-and-audience pinned, with optional replay defence:

```csharp
builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            SignatureVerificationKey = verificationKey,
            ValidIssuer   = "https://issuer.example",
            ValidAudience = "https://api.example",
            // Single-process replay defence. Swap to a Redis-backed
            // IPqJwtReplayCache for a horizontally scaled deployment.
            ReplayCache = new InMemoryReplayCache(),
        };
    });
```

Token minting lives in `PostQuantum.Jwt` itself — `PqJwtBuilder` — and is not
duplicated here. This package is the *receiving* half.

### Events: enrich, observe, customize the challenge

`PostQuantumJwtBearerEvents` mirrors the shape of `JwtBearerEvents` —
four async hooks for the moments that matter:

```csharp
.AddPostQuantumJwtBearer(options =>
{
    options.ValidationParameters = new PqJwtValidationParameters { /* ... */ };

    // Substitute a token from a non-Authorization-header source.
    // SignalR's ?access_token= is the canonical use case.
    options.Events.OnMessageReceived = ctx =>
    {
        if (ctx.HttpContext.Request.Path.StartsWithSegments("/hub"))
        {
            var query = ctx.HttpContext.Request.Query["access_token"].ToString();
            if (!string.IsNullOrEmpty(query))
            {
                ctx.Token = query;
            }
        }

        return Task.CompletedTask;
    };

    // Enrich the principal after a token has been successfully validated.
    options.Events.OnTokenValidated = ctx =>
    {
        var identity = (System.Security.Claims.ClaimsIdentity)ctx.Principal.Identity!;
        identity.AddClaim(new("tenant", ResolveTenant(ctx.HttpContext)));
        return Task.CompletedTask;
    };

    // Observe (or, rarely, override) the failure outcome.
    options.Events.OnAuthenticationFailed = ctx =>
    {
        // ctx.Exception is the PqJwtValidationException.
        // Setting ctx.Result downgrades the default Fail() — usually you
        // just log and let the fail-closed default stand.
        return Task.CompletedTask;
    };

    // Customise the 401 challenge response.
    options.Events.OnChallenge = ctx =>
    {
        if (ctx.HttpContext.Request.Path.StartsWithSegments("/api"))
        {
            ctx.HttpContext.Response.Headers["X-PQ-Auth"] = "required";
        }

        // ctx.Handled = true; suppresses the default WWW-Authenticate header.
        return Task.CompletedTask;
    };
});
```

Hook delegates default to no-ops, so leaving `Events` alone gives you
the same behaviour as not having the hooks at all.

### Key rotation across services

Use `AddPostQuantumJwtKeyRing(uri)` to fetch verification keys from a
trusted HTTPS endpoint (the post-quantum analogue of JWKS). The validator
picks the right key for each incoming token from its `kid` header:

```csharp
builder.Services
    .AddAuthentication(PostQuantumJwtBearerDefaults.AuthenticationScheme)
    .AddPostQuantumJwtBearer(options =>
    {
        options.ValidationParameters = new PqJwtValidationParameters
        {
            ValidIssuer   = builder.Configuration["Auth:Issuer"],
            ValidAudience = builder.Configuration["Auth:Audience"],
            // No key here — the ring supplies it.
        };
    });

// Registers HttpPostQuantumJwtKeyRing as a typed HTTP client and
// post-configures it onto the named options. No BuildServiceProvider()
// dance.
builder.Services.AddPostQuantumJwtKeyRing(
    new Uri(builder.Configuration["Auth:KeysEndpoint"]!));
```

For a non-HTTP key source (database, KMS, file), supply your own
`IPostQuantumJwtKeyRing` implementation and register it generically:

```csharp
builder.Services.AddPostQuantumJwtKeyRing<MyDatabaseKeyRing>();
```

**Warm the cache at startup.** A cold cache means the first
authentication request pays a network round trip while every other
request waits. Register the hosted-service warmup helper to preload at
host start (and optionally on a periodic timer so removed keys drop
out without waiting for an unknown-`kid` miss):

```csharp
builder.Services.AddPostQuantumJwtKeyRing(
    new Uri(builder.Configuration["Auth:KeysEndpoint"]!));

builder.Services.AddPostQuantumJwtKeyRingWarmup(options =>
{
    options.FailFastOnStartup = true;                // default
    options.RefreshInterval   = TimeSpan.FromMinutes(15);
});
```

`FailFastOnStartup` (default `true`) makes a startup-time fetch failure
abort the host — strict, but matches the engine library's fail-closed
ethos. Set it to `false` for best-effort warmup that logs and lets the
host come up; the first cache miss will then drive a refresh as usual.

The expected key-directory document is JSON:

```json
{ "keys": [ { "kid": "2026-q2", "alg": "ML-DSA-65", "key": "<base64>" } ] }
```

Entries with any other `alg` are ignored — the single-suite policy holds
across services.

### Custom scheme name

If you already have a `JwtBearer` scheme on the same app (e.g. for a slow
migration), register the post-quantum scheme under its own name and route
specific endpoints to it:

```csharp
builder.Services
    .AddAuthentication()
    .AddJwtBearer("Classical", o => { /* legacy config */ })
    .AddPostQuantumJwtBearer("PostQuantum", o =>
    {
        o.ValidationParameters = new PqJwtValidationParameters { /* ... */ };
    });
```

```csharp
[Authorize(AuthenticationSchemes = "PostQuantum")]
public class ProtectedController : ControllerBase { /* ... */ }
```

> **Don't `AddJwtBearer` *alongside* this on the default scheme.** The
> standard handler will try to parse the token's `alg` and fail. Either use
> `AddPostQuantumJwtBearer` as your only bearer auth, or restrict each scheme
> to specific routes with `[Authorize(AuthenticationSchemes = ...)]`.

---

## Public API at a glance

| Type                                     | Purpose                                                                |
|------------------------------------------|------------------------------------------------------------------------|
| `PostQuantumJwtBearerExtensions`         | `AddPostQuantumJwtBearer(...)` extension methods on `AuthenticationBuilder`. |
| `PostQuantumJwtBearerHandler`            | Fail-closed `AuthenticationHandler` that delegates to `PqJwtValidator`. |
| `PostQuantumJwtBearerOptions`            | Strongly-typed configuration: validation parameters, claim mapping, challenge details. |
| `PostQuantumJwtBearerDefaults`           | Scheme name and `Bearer` constant.                                     |
| `PostQuantumJwtBearerEvents`             | `OnMessageReceived` / `OnTokenValidated` / `OnAuthenticationFailed` / `OnChallenge` async hooks. |
| `IPostQuantumJwtKeyRing`                 | JWKS-equivalent abstraction for `kid → MLDsa` resolution (sync + async). |
| `HttpPostQuantumJwtKeyRing`              | HTTP-backed key ring with refresh, in-memory cache, atomic snapshot swap, AOT-safe JSON. |
| `PostQuantumJwtKeyRingExtensions`        | `AddPostQuantumJwtKeyRing(...)` DI helpers (HTTP and generic).         |
| `PostQuantumJwtKeyRingWarmupExtensions`  | `AddPostQuantumJwtKeyRingWarmup(...)` — hosted-service preload + periodic refresh. |
| `PostQuantumJwtKeyDirectory` / `…KeyEntry` | DTOs for the key-directory wire format.                              |

---

## Defaults and what they mean

| Setting                          | Default                                              | Why                                                                                  |
|----------------------------------|------------------------------------------------------|--------------------------------------------------------------------------------------|
| Scheme name                      | `"PostQuantumJwtBearer"`                             | Distinct from the standard `"Bearer"` scheme so the two can coexist during migration. |
| `NameClaimType`                  | `"sub"`                                              | Standard JWT subject claim. The default `JwtBearer` value (`"unique_name"`) is less portable. |
| `RoleClaimType`                  | `"role"`                                             | Matches common ML-DSA-issued tokens; works with `[Authorize(Roles = ...)]` out of the box. |
| `IncludeErrorDetailsInChallenge` | `true`                                               | The 401 `WWW-Authenticate` header carries `error="invalid_token"`. Set to `false` if you'd rather not signal why. |
| `TimeProvider`                   | `TimeProvider.System` (inherited from `AuthenticationSchemeOptions`) | Override with `TimeProvider.Fake` for deterministic tests. |

---

## Compared to `Microsoft.AspNetCore.Authentication.JwtBearer`

`Microsoft.AspNetCore.Authentication.JwtBearer` is the right choice for any
JWT work that needs to interoperate with OAuth/OIDC, JWKS, the IANA JOSE
algorithm registry, or any third-party token issuer. **Use it unless you have
a specific reason not to.**

`PostQuantum.AspNetCore` is a focused, deliberately *non-interoperable* tool
for one problem: hybrid post-quantum JWT authentication.

| Concern                | `Microsoft.AspNetCore.Authentication.JwtBearer` | `PostQuantum.AspNetCore`                                  |
|------------------------|-------------------------------------------------|-----------------------------------------------------------|
| **Algorithms**         | RS/PS/ES/EdDSA/HS — the full IANA catalogue.    | One suite only: ML-DSA-65 for signatures; X-Wing + AES-256-GCM for encryption. |
| **Quantum resistance** | None of the standard algorithms are quantum-resistant. | Hybrid: classical *and* post-quantum, both must fall.    |
| **Algorithm agility**  | Yes — and historically the source of `alg: none`, RS/HS confusion, and downgrade attacks. | **No, by design.** The validator does not trust the token's `alg` to pick a path; it accepts exactly one. |
| **Standards interop**  | Fully IANA-registered identifiers; tokens validate in every JWT library. | Identifiers (`ML-DSA-65`, `X-Wing`) are not IANA-registered. Tokens **will not** validate in generic JWT tooling. |
| **JWKS**               | First-class.                                    | `IPostQuantumJwtKeyRing` + HTTP-backed implementation — JWKS-equivalent over a deliberately trivial wire format. |
| **External audit**     | Yes — widely deployed and reviewed.             | **No.** Preview, not audited.                              |
| **Dependencies**       | `Microsoft.IdentityModel.*` family.             | `PostQuantum.Jwt` + the `Microsoft.AspNetCore.App` framework reference. |
| **Target framework**   | net8 / net9 / net10.                            | `net10.0` only (matches the engine).                       |

**Use `Microsoft.AspNetCore.Authentication.JwtBearer` if** you need OAuth/OIDC
interop, JWKS, multi-algorithm agility, or any standards-conformant JWT.

**Use `PostQuantum.AspNetCore` if** you specifically want hybrid post-quantum
tokens *now*, you control both the issuer and the verifier, and you accept
that your tokens won't validate in any other ecosystem until IANA registers
these identifiers and standard libraries catch up.

### Not to be confused with…

| Package                                      | What it is                                                                 | Why it isn't this  |
|----------------------------------------------|----------------------------------------------------------------------------|--------------------|
| **`BouncyCastle.Cryptography`**              | A full-stack C# cryptography toolkit — block ciphers, public-key crypto, X.509, TLS, PKCS, OpenPGP, post-quantum primitives, and more. | A primitive library — no JWT support, no ASP.NET Core integration. `PostQuantum.Jwt` uses it for X25519 only; this package never touches it directly. |
| **`liboqs` / `liboqs-dotnet`**               | Open-source post-quantum cryptography primitives (KEMs, signatures) maintained by the Open Quantum Safe project. | A primitive library. Different choice from the BCL's `MLDsa`/`MLKem`; the engine library has chosen the BCL path. |
| **`System.Security.Cryptography`** (BCL)     | The .NET 10 base class library — including FIPS-validated `MLDsa`, `MLKem`, `AesGcm`, etc. | The actual implementation under everything else in the diagram above. `PostQuantum.AspNetCore` does not reimplement any BCL primitive. |
| **`PostQuantum.Jwt`**                        | The engine: `PqJwtBuilder` to mint hybrid signed / signed-then-encrypted tokens; `PqJwtValidator` to verify them; the X-Wing combiner; the JWE wire format. | The library *under* `PostQuantum.AspNetCore`. If you're not using ASP.NET Core, use this directly. |
| **`Microsoft.AspNetCore.Authentication.JwtBearer`** | Microsoft's standard JWT bearer handler. Supports every IANA JOSE algorithm. | The right choice for **every JWT scenario except post-quantum**. This package is the post-quantum sibling, not a replacement. |

---

## Migrating from `PostQuantum.Jwt.AspNetCore`

`PostQuantum.AspNetCore` is the renamed, repackaged successor to the
`PostQuantum.Jwt.AspNetCore` companion that ships from the engine
repository. **Same engine, same shape, cleaner naming, its own release
cadence.** The mapping is mechanical (`AddPqJwtBearer` →
`AddPostQuantumJwtBearer`, `PqJwtBearer*` → `PostQuantumJwtBearer*`,
`IPqJwtKeyRing` → `IPostQuantumJwtKeyRing`, …), and tokens minted by
either package validate in the other.

See [`docs/MIGRATION.md`](docs/MIGRATION.md) for the full diff-style guide.

---

## Security posture

The short version, honestly.

**What you get**

- **Fail-closed validation.** Bad signature, tampered ciphertext, expired or
  not-yet-valid token, wrong issuer/audience, missing `exp`, missing `alg`,
  or an `alg` we don't expect — every one of those throws inside
  `PqJwtValidator`, and the handler turns it into `AuthenticateResult.Fail`.
  There is no `alg: none`, no unsigned path, and no silent downgrade.
- **Native post-quantum primitives.** ML-DSA-65 and ML-KEM-768 are the
  FIPS-validated .NET 10 BCL implementations, not a re-implementation.
- **Hybrid by construction (for encrypted tokens).** Confidentiality stays
  secure unless *both* X25519 and ML-KEM-768 fall.
- **Strict, small-surface defaults.** Expiration is required, clock skew is a
  modest 60 seconds, only the exact post-quantum algorithms are accepted, and
  the bearer prefix is matched ordinally — no case-insensitive surprises.

**What you must know**

- **Not audited.** No third party has reviewed the design or implementation.
- **Non-standard identifiers.** `alg`/`enc` values (`ML-DSA-65`, `X-Wing`)
  are not IANA-registered. Tokens are intentionally not interoperable with
  generic JWT tooling.
- **Preview.** Treat the API and wire format as unstable until 1.0.
- **Replay defence is opt-in and only as strong as the cache you provide.**
  `InMemoryReplayCache` is single-process; back the
  `IPqJwtReplayCache` hook with a distributed store for clusters.

Full detail in [`SECURITY.md`](SECURITY.md) and [`KNOWN-GAPS.md`](KNOWN-GAPS.md).

---

## Compatibility

| Surface          | Supported                                                                                  |
|------------------|--------------------------------------------------------------------------------------------|
| Target framework | `net10.0`                                                                                  |
| ASP.NET Core     | 10.x via `<FrameworkReference Include="Microsoft.AspNetCore.App" />`                       |
| Languages        | C# 13                                                                                      |
| Operating system | Windows, Linux, macOS — wherever .NET 10 runs with an ML-KEM / ML-DSA-capable OpenSSL. On Linux that means **OpenSSL 3.5 or later**. |
| AOT / trimming   | `IsAotCompatible=true`, `IsTrimmable=true`. The HTTP key-ring JSON path is source-generated. |

---

## Building from source

```bash
dotnet build         # zero warnings (compiler warnings are errors)
dotnet test          # 31 tests, zero skips on PQ-capable hosts
dotnet format        # apply the .editorconfig style
dotnet run --project samples/PostQuantum.AspNetCore.Demo
```

The test suite is `Microsoft.AspNetCore.Mvc.Testing`-backed and exercises
the fail-closed contract end-to-end: valid token → `200 OK` with the
expected `ClaimsPrincipal`, every tampered/expired/wrong-issuer/wrong-audience
case → `401 Unauthorized`, plus assertions on each of the three event hooks.
**Tests that exercise the native ML-DSA primitives skip themselves
with a stated reason** (`PqcFactAttribute`) on hosts where the BCL
primitives aren't available; both CI lanes (Windows native + Linux with
OpenSSL 3.5+ via conda-forge) fail the run if any test reports skipped.

---

## Contributing

Issues and pull requests are welcome. Before opening a PR:

1. Run `dotnet build` and `dotnet test` — both must be green, with **zero
   warnings** (the build treats compiler warnings as errors).
2. Keep the discipline in [`CLAUDE.md`](CLAUDE.md): honesty over polish,
   fail-closed always, no rolled-your-own crypto, native BCL first.
3. Security-sensitive changes should land alongside a test that locks in the
   fail-closed behaviour.

**Reporting a vulnerability:** please **do not** open a public issue. Use
GitHub's *Report a vulnerability* button on the repository, or follow the
process in [`SECURITY.md`](SECURITY.md).

---

## License

[MIT](LICENSE).

---

*To God be the glory — 1 Corinthians 10:31.*
