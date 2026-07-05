# Frequently asked questions

## Should I use this in production?

**In a controlled issuer/verifier system — yes, with eyes open.** From
`1.0.0` the library is production-quality and stable under SemVer, and
it is appropriate where the same team owns both token issuing and token
validation. The load-bearing caveat: the underlying cryptographic
construction (in `PostQuantum.Jwt`) has **not** been independently
audited, and at 1.0 that is a **permanent, documented limitation** — no
audit is scheduled, and waiting for one is no longer the plan (see
[`SECURITY.md`](../SECURITY.md)). If your deployment needs
standards-interoperable tokens, anonymous relying parties, or a
third-party audit sign-off, use standard `JwtBearer` instead. Walk
[`PRODUCTION-CHECKLIST.md`](PRODUCTION-CHECKLIST.md) before shipping.

See [`API-STABILITY.md`](API-STABILITY.md) for the SemVer commitment and
how the `1.0` gates were resolved.

## Is this faster or slower than `Microsoft.AspNetCore.Authentication.JwtBearer`?

**Slower per token.** `ML-DSA-65` signature verification takes
milliseconds (vs microseconds for HMAC/EdDSA/ECDSA). For typical
APIs — where the bottleneck is the business logic, not the auth
header — this is invisible. For very-high-throughput services where
every microsecond matters, you'll feel it.

See [`PERFORMANCE.md`](PERFORMANCE.md) for measurement guidance.

## How big are the tokens really?

- Signed-only: **~4.5 KB** after base64url encoding. The ML-DSA-65
  signature is 3,309 bytes (vs 32 for HMAC, ~64 for EdDSA).
- Signed-then-encrypted: **~6.5 KB** — adds a 1.1 KB X-Wing
  ciphertext + AES-GCM nonce/tag.

This matters for:

- **Cookies.** A 4.5 KB token won't fit comfortably in a single
  cookie (4 KB hard cap on most browsers). Use `Authorization: Bearer`
  headers.
- **Query strings.** Some load balancers and WAFs limit URL length to
  ~4 KB. SignalR `?access_token=…` usually fits, but verify.
- **Logging / observability.** Don't log full tokens — they
  inflate index sizes for no benefit. The library never logs token
  content; the engine doesn't either.

## Does this interop with Auth0, IdentityServer, Microsoft Entra, etc.?

**No.** Those identity providers issue tokens with IANA-registered
JOSE algorithms (RS256, ES256, etc.). The `ML-DSA-65` and `X-Wing`
algorithm identifiers this library uses are **not IANA-registered**
yet — your tokens won't validate against generic JWT consumers, and
the standard providers won't issue them.

What you **can** do today:

- **Run a coexistence pattern.** Register both `AddJwtBearer` (for
  classical tokens from Auth0/etc.) and `AddPostQuantumJwtBearer`
  (for PQ tokens from your own issuer service), and route specific
  endpoints to specific schemes. See
  [`RECIPES.md` § 7](RECIPES.md#7-coexist-with-the-standard-jwtbearer-scheme-during-migration).
- **Issue your own PQ tokens** using `PqJwtBuilder` from
  `PostQuantum.Jwt`. Treat your service as the PQ issuer; the rest
  of your stack stays on classical.

## Why net10.0 only?

The engine library uses the .NET 10 BCL's `System.Security.Cryptography.MLDsa`
and `MLKem` types. Those don't exist in net8 / net9. Multi-targeting
this package without those types would mean shipping a different (and
less-reviewed) cryptographic backend on older runtimes — which
defeats the point.

Tracked in [`KNOWN-GAPS.md`](../KNOWN-GAPS.md#target-framework).

## What if the engine library is wrong?

`PostQuantum.AspNetCore` is the wrapper; `PostQuantum.Jwt` is the
engine. A bug in the engine's cryptographic construction would
propagate through this package. That's why:

- We bound the security claim to **what the engine provides** — see
  [`SECURITY.md`](../SECURITY.md).
- The engine's own security posture lives at
  [`postquantum-jwt/SECURITY.md`](https://github.com/systemslibrarian/postquantum-jwt/blob/main/SECURITY.md).
- Both packages are explicit that an external cryptographic audit is
  required before `1.0`.

If you find what you suspect is a cryptographic flaw, please report
privately via the engine repo's security advisory channel.

## How do I migrate from `PostQuantum.Jwt.AspNetCore`?

See [`MIGRATION.md`](MIGRATION.md). Short version: the rename is
mechanical (`AddPqJwtBearer` → `AddPostQuantumJwtBearer`, `PqJwtBearer*`
→ `PostQuantumJwtBearer*`), and tokens minted under either package
validate in the other.

## Why is the package so small? Where's all the crypto?

**On purpose.** The crypto is in `PostQuantum.Jwt` and the .NET BCL.
This package is the ASP.NET Core integration layer — extension
methods, an `AuthenticationHandler`, options, event hooks, a key
ring, metrics. ~1,500 lines of C# total. The thinness is a feature:
the security surface to review is small, and the same engine powers
every consumer.

See the "[Where does this fit in the stack?](../README.md#where-does-this-fit-in-the-stack)"
diagram in the README.

## Does this work with AOT publishing?

**Yes.** `IsAotCompatible=true` is declared, and we verify it
end-to-end in CI on Linux + Windows + macOS by publishing a
consuming app with `PublishAot=true` + `TreatWarningsAsErrors=true`.

A regression in the library that breaks AOT would fail the
`aot-publish` CI job before it merged.

## How is replay protection actually enforced?

The handler validates the token's `jti` against a configured
`IPqJwtReplayCache`. With no cache configured, the `jti` claim is
carried but not enforced — replays are accepted.

- **Single instance:** use the `InMemoryReplayCache` bundled with
  `PostQuantum.Jwt`. Replays are caught within one process.
- **Multi-instance:** install the `PostQuantum.AspNetCore.RedisReplayCache`
  companion package. See [`RECIPES.md` § 5](RECIPES.md#5-distributed-replay-protection-with-redis).

## What metrics and traces does this emit?

A `Meter` and an `ActivitySource` under the
`"PostQuantum.AspNetCore"` instrumentation name. Subscribe via
OpenTelemetry — see [`RECIPES.md` § 8](RECIPES.md#8-opentelemetry-metrics-and-distributed-tracing)
for a wire-up example. The signal contract is locked by tests, so
dashboards won't break across releases.

## Is the `WWW-Authenticate` header standards-compliant?

Yes. The realm parameter is RFC 7235 quoted-string escaped (any
embedded `"` or `\` is backslash-escaped), and the `error=invalid_token`
parameter is added per RFC 6750 only when a token was actually
supplied and rejected.

## Can I customize the 401 response body?

Yes — subscribe to `Events.OnChallenge` and write your own response.
Set `ctx.Handled = true` to suppress the default
`WWW-Authenticate` header. Example in
[`RECIPES.md` § 6](RECIPES.md#6-role-based-and-policy-based-authorization).

## What's the difference between `Resolve` and `PreloadAsync` on the key ring?

- **`Resolve(kid)`** — the hot path. Cache-hit → instant. Cache-miss
  → triggers a refresh fetch. **Swallows** fetch failures: a flaky
  key endpoint should not produce 500s to your users. The token
  fails closed with a 401 because the resolver returned `null`.
- **`PreloadAsync()`** — the warm-up path. **Propagates** fetch
  failures so an operator at startup time knows the endpoint is
  unreachable. Used by `AddPostQuantumJwtKeyRingWarmup` and by
  health checks.

The asymmetry is deliberate: hot path stays available, cold path
fails loudly.

## How do I rotate keys without downtime?

1. Issuer mints with a new `kid` (e.g. `signing-key-2026-q3`).
2. Issuer publishes the new public key on its JWKS-equivalent
   endpoint alongside the old one.
3. Verifiers' key rings refresh on their interval (or on first
   unknown-`kid` miss) and pick up the new key.
4. Issuer stops minting with the old `kid` once the longest
   outstanding token's lifetime has elapsed.
5. Old `kid` removed from the endpoint at next refresh; cache evicts
   on subsequent refresh.

The `PostConfigure` pattern means the in-memory snapshot updates
atomically — no torn state.

## What happens if the key endpoint is down?

- **At startup with warmup enabled and `FailFastOnStartup=true`:**
  host startup aborts. The operator sees the failure immediately.
- **At startup with `FailFastOnStartup=false`:** host starts; the
  first request to need a key triggers a refresh, which fails and is
  logged. The validator's `SignatureKeyResolver` returns `null`, the
  token fails closed, the request gets a 401.
- **At runtime:** the cache continues serving known keys.
  Unknown-`kid` requests fail closed. The refresh throttle (10 s)
  prevents fan-out attacks against your key endpoint.

## Can I use multiple algorithms?

**No, by design.** The library accepts exactly one signature suite:
`ML-DSA-65`. See
[`docs/adr/0001-no-algorithm-agility.md`](adr/0001-no-algorithm-agility.md)
for the full rationale and the "when would we reconsider" gate.

## Where do I file a bug?

[GitHub issues](https://github.com/systemslibrarian/postquantum-aspnetcore/issues/new/choose).
Pick the right template (bug / feature / security).

**For security-sensitive bugs**, use GitHub's private advisory
channel — not a public issue. The library `SECURITY.md` has the
process.

## How can I contribute?

See [`CONTRIBUTING.md`](../CONTRIBUTING.md). The bar is honest, but
not high: zero build warnings, tests for new behaviour, and a
CHANGELOG entry. PRs welcome.

---

If your question isn't here, please open an issue or a discussion —
the FAQ grows from real consumer questions.

---

*To God be the glory — 1 Corinthians 10:31.*
