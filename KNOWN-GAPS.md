# Known Gaps

A transparent, running list of what `PostQuantum.AspNetCore` does **not** yet
do, what is unverified, and where the sharp edges are. Honesty over polish:
if something is incomplete, it is listed here rather than glossed over. This
file is part of the contract with anyone evaluating the package.

Last reviewed for: `0.1.0-preview.1`.

## Inherited from `PostQuantum.Jwt`

Every gap in the engine library applies here too. The most important ones:

- **No external audit** of the cryptographic construction.
- **X-Wing encapsulation is not KAT-validated** (the BCL `MLKem.Encapsulate`
  exposes no derandomized entry point); decapsulation + the SHA3-256
  combiner are validated against the official IETF vectors.
- **Non-standard JOSE identifiers** (`ML-DSA-65`, `X-Wing`) — tokens will not
  validate in generic JWT tooling.
- **Single algorithm suite by design** — no agility.

See the engine's [`KNOWN-GAPS.md`](https://github.com/systemslibrarian/postquantum-jwt/blob/main/KNOWN-GAPS.md)
for the full list.

## Specific to this package

### Tests

- **No test project yet.** `0.1.0-preview.1` ships with the `tests/` folder
  reserved but empty. The first follow-up release will land a `TestServer`-based
  suite that locks the fail-closed contract at the HTTP boundary: tampered
  token → 401, missing header → no result, wrong scheme → no result, valid
  token → 200 with the expected `ClaimsPrincipal`. Until then, the contract
  rests entirely on the engine's 68 tests plus manual exercise via the demo
  sample.

### Target framework

- **`net10.0` only.** Multi-targeting `net8.0;net9.0;net10.0` was scoped but
  not shipped: the underlying `PostQuantum.Jwt` engine relies on the BCL
  `MLDsa` / `MLKem` types introduced in .NET 10, so any older TFM would
  require a separate crypto backend. Tracked for a future release once the
  engine grows a polyfill path or `Microsoft.Cryptography.PostQuantum`
  ships out-of-band on the older runtimes.

### Key ring

- **HTTP key ring fetches synchronously inside `Resolve`.** When a `kid`
  miss triggers a refresh, the lookup is currently synchronous
  (`.GetAwaiter().GetResult()`) because `SignatureKeyResolver` is a
  synchronous delegate in the engine. This is acceptable for a cached
  hot path but blocks a thread on cold lookups. An async resolver hook on
  the engine side would let us drop the sync-over-async; tracked upstream.
- **No ETag / `Cache-Control` awareness.** The HTTP key ring re-fetches on
  a fixed `refreshInterval` (default 5 minutes) and on unknown-`kid` misses.
  It does not honour HTTP caching headers. For a slow-changing directory
  this is fine; for a high-churn one, pick the interval accordingly.
- **No certificate / hostname pinning.** Trust the supplied `HttpClient`'s
  TLS configuration. Configure pinning at the `HttpClientHandler` level if
  your threat model needs it.
- **No key-rotation rollover window.** When a key is removed from the
  upstream directory, the local cache only drops it on the next successful
  full refresh; tokens signed with that key keep validating against the
  cached entry until then. For most operational rotation schemes this is the
  desired behaviour; if you need hard cutover, restart the host or clear the
  cache explicitly.

### Handler

- **No `JwtBearerEvents`-equivalent.** The standard handler exposes a rich
  `OnAuthenticationFailed`, `OnTokenValidated`, `OnChallenge`, etc. event
  surface. This handler does not — yet. If you need to enrich the
  `ClaimsPrincipal` or react to failures, do it in middleware or a derived
  handler in the meantime.
- **No automatic forwarded-header trust.** The handler reads the
  `Authorization` header directly from `HttpContext.Request`. If your
  deployment terminates TLS upstream and rewrites headers, ensure
  `Microsoft.AspNetCore.HttpOverrides` is configured before authentication
  in the pipeline.
- **No bearer-token retrieval hook.** Token always comes from the
  `Authorization` header with a literal `Bearer ` prefix. No support for
  query-string or cookie-borne tokens — by design (cookies are a poor
  carrier for ~4.5 KB post-quantum tokens), but worth knowing.

### Packaging / CI

- **No CI workflow yet.** `0.1.0-preview.1` ships from a local build. A
  GitHub Actions workflow that mirrors `PostQuantum.Jwt`'s `pack` +
  `publish` split, with a `nuget-publish` environment gate and
  build-provenance attestations, is the next packaging milestone.
- **No CycloneDX SBOM inside the `.nupkg` yet.** The engine ships one; this
  package will follow as soon as the CI lands.
- **No API baseline check.** `EnablePackageValidation` is on, but no
  `PackageValidationBaselineVersion` is wired in — that's a v0.2 follow-up
  once `0.1.0-preview.1` is on nuget.org and there's a baseline to validate
  against.

---

If you hit a gap not listed here, that itself is a gap — please open an
issue so it can be recorded honestly.

---

*To God be the glory — 1 Corinthians 10:31.*
