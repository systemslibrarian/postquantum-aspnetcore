# Known Gaps

A transparent, running list of what `PostQuantum.AspNetCore` does **not** yet
do, what is unverified, and where the sharp edges are. Honesty over polish:
if something is incomplete, it is listed here rather than glossed over. This
file is part of the contract with anyone evaluating the package.

Last reviewed for: `1.0.0-preview.3`.

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

### Target framework

- **`net10.0` only.** The underlying `PostQuantum.Jwt` engine relies on the
  BCL `MLDsa` / `MLKem` types introduced in .NET 10, so any older TFM would
  require a separate cryptographic backend. Tracked for a future release
  once the engine grows a polyfill path or
  `Microsoft.Cryptography.PostQuantum` ships out-of-band on the older
  runtimes.

### Key ring

- **HTTP key ring's `Resolve` is sync-over-async on cold-miss.** Native
  `ResolveAsync` exists (and is the right path for warm-up via
  `PreloadAsync`), but the engine's `SignatureKeyResolver` is still a
  synchronous delegate, so a cold cache miss inside an auth request
  blocks a thread via `.GetAwaiter().GetResult()`. As of `preview.3` the
  hot path is protected against unknown-`kid` flooding by a bounded
  short-TTL (10s) negative cache that short-circuits *before* the
  bridge — see [Trust root in SECURITY.md](SECURITY.md#trust-root-the-http-key-directory)
  for the bound. Fully removing the bridge is still blocked on the
  engine exposing an async `SignatureKeyResolver`; in the meantime,
  **warm the cache at startup** with `AddPostQuantumJwtKeyRingWarmup(...)`
  to keep the hot path off the bridge entirely.
- **No ETag / `Cache-Control` awareness.** The HTTP key ring re-fetches on
  a fixed `refreshInterval` (default 5 minutes) and on unknown-`kid` misses.
  It does not honour HTTP caching headers. For a slow-changing directory
  this is fine; for a high-churn one, pick the interval accordingly.
- **No built-in certificate / hostname pinning.** The HTTP key directory
  is the **root of trust for token validation** (see
  [SECURITY.md](SECURITY.md#trust-root-the-http-key-directory)). The
  library does not pin certificates for you; operators SHOULD configure
  pinning or a hardened `HttpMessageHandler` on the typed `HttpClient`.
  `AddPostQuantumJwtKeyRing(..., configureHttpClient: …)` exposes the
  obvious DI hook for that.
- **Key-directory response is bounded at 1 MB by default.** Applied via
  `HttpClient.MaxResponseContentBufferSize` when you use the DI helper.
  A real directory is tens of keys × a few hundred bytes; 1 MB is
  ~3 orders of magnitude of headroom. If your directory is genuinely
  larger, override via the `configureHttpClient` hook or the
  `maxResponseBytes` constructor parameter.
- **No key-rotation rollover window.** When a key is removed from the
  upstream directory, the local cache only drops it on the next successful
  full refresh; tokens signed with that key keep validating against the
  cached entry until then. For most operational rotation schemes this is
  the desired behaviour; if you need hard cutover, restart the host or
  clear the cache explicitly.

### Handler

- **No automatic forwarded-header trust.** The handler reads the
  `Authorization` header directly from `HttpContext.Request`. If your
  deployment terminates TLS upstream and rewrites headers, ensure
  `Microsoft.AspNetCore.HttpOverrides` is configured before authentication
  in the pipeline.
- **No `OnForbidden` event.** The current event surface is the four
  hooks consumers actually reach for (`OnMessageReceived`,
  `OnTokenValidated`, `OnAuthenticationFailed`, `OnChallenge`). The
  remaining `JwtBearerEvents`-style `OnForbidden` is not implemented
  because no consumer has asked yet; if you need it, please open an
  issue with the scenario.

### Testing depth

- **Mutation testing baseline is incomplete.** `stryker-config.json`
  is checked in and `dotnet stryker` runs, but the run could not
  produce a useful mutation score on this project shape (likely an
  xunit-v3 + NSubstitute test-discovery limitation in Stryker.NET).
  Configuration is preserved for a future Stryker release that handles
  this shape; the in-process fuzz tests catch the equivalents *they* can
  catch (and have — twice in v0.5). See
  [`docs/MUTATION-TESTING.md`](docs/MUTATION-TESTING.md) for the full
  record.

### Packaging / CI

- **Packages are not author-signed by default.** The release workflow has
  an optional author-signing hook: if a `NUGET_SIGNING_CERT` secret is
  present on the `nuget-publish` GitHub Environment, packages are signed
  with `dotnet nuget sign` and a DigiCert timestamp before push. Until a
  certificate is procured and that secret is populated, packages rely on
  nuget.org's repository signature alone. Every release also emits
  GitHub build-provenance attestations for the `.nupkg` and the SBOM —
  verify with
  `gh attestation verify <file> --repo systemslibrarian/postquantum-aspnetcore`.
- **API baseline is opt-in.** `EnablePackageValidation` is on; the
  baseline comparison against `0.1.0-preview.1` is gated behind
  `-p:EnableBaselineValidation=true` until that version lands on
  nuget.org and the baseline package is resolvable.

---

If you hit a gap not listed here, that itself is a gap — please open an
issue so it can be recorded honestly.

---

*To God be the glory — 1 Corinthians 10:31.*
