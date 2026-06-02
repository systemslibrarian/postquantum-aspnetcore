# Changelog

All notable changes to `PostQuantum.AspNetCore` are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
once it reaches `1.0.0`. Preview releases (`0.x`) may break the API between
versions.

## [Unreleased]

_No changes yet._

## [1.0.0-preview.3] — 2026-06-01

A **suite-reconciliation + integration-hardening** release. The PostQuantum.*
ecosystem reconciled its versions; this is `PostQuantum.AspNetCore`'s
assigned slot. No crypto changes (there is none in this repo), no API
breaks, no behaviour changes on the happy path. Hardening additions are
deployment-shape mitigations that an adversarial review surfaced.

### Changed

- **`PostQuantum.Jwt` engine dependency bumped to `1.0.0-preview.1`** to
  match the suite target. The engine's own 1.0-preview line maintains honest
  preview status (no independent audit, IANA still has not registered the
  `ML-DSA-65` identifier). This package does not advertise more maturity
  than what it depends on.

### Added

- **`SECURITY.md` — "Trust root: the HTTP key directory" section.** Makes
  explicit what the model already implied: the HTTPS key-directory endpoint
  is the root of trust for token validation, the library has no insecure
  fallback by design, and operators SHOULD configure certificate pinning or
  a hardened `HttpClient`. The library does not pin certificates for you.
- **`AddPostQuantumJwtKeyRing(..., configureHttpClient: …)` overload.** A
  new optional `Action<IHttpClientBuilder>` on the HTTP DI helpers so wiring
  a pinned `HttpMessageHandler` is the obvious path, not a fork. The default
  null parameter preserves the existing call sites bit-for-bit.
- **1 MB cap on the key-directory response.** The DI helper sets
  `MaxResponseContentBufferSize = 1 MB` on the typed `HttpClient`; a hostile
  or compromised endpoint trying to drive memory pressure is rejected. The
  manual `HttpPostQuantumJwtKeyRing` constructor gains an optional
  `maxResponseBytes` parameter (default `null` = honour caller's
  `HttpClient` settings, no surprise behavior change).
- **Unknown-`kid` flood mitigation.** `Resolve` now consults a bounded
  short-TTL (10s) negative cache *before* the sync-over-async refresh
  bridge. Under random-`kid` flooding this short-circuits to `null`
  immediately, without entering `_refreshLock` or burning a thread-pool
  thread on the bridge. The negative-cache TTL is intentionally equal to
  the existing forced-refresh throttle window, so the bound on wrong
  rejection is unchanged: a `kid` which becomes valid after a previous
  refresh-miss is wrongly rejected for at most 10 seconds, matching the
  pre-existing throttle window. A `refresh-hit` clears any stale negative
  entry, and the cache is bounded at 1024 entries.

### Tests

- **`HttpPostQuantumJwtKeyRingTests`** — four new tests covering: concurrent
  Resolve during atomic-snapshot Refresh, unknown-`kid` flood short-
  circuiting via the negative cache, negative-cache entries ageing out so a
  newly-valid `kid` becomes resolvable, and the 1 MB response cap rejecting
  oversize bodies.
- **`RedisReplayCacheTests`** — concurrent `TryRegister` against the same
  `jti`: exactly one caller wins.

### Not changed

- API surface — `1.0.0-preview.2` callers compile and behave identically.
- Fail-closed contract — same exception filter (`is not (OOM or
  StackOverflow)`), same `AuthenticateResult.Fail` on every validation
  failure, same atomic-snapshot key-ring swap.
- Single-suite policy — non-`ML-DSA-65` entries are still silently dropped
  at the key-ring boundary.
- Audit status — the underlying cryptographic construction has not been
  independently audited. See `KNOWN-GAPS.md`.

## [1.0.0-preview.2] — 2026-05-31

A **bug-fix preview** on the `1.0.0-preview.1` API surface. No behavior
changes for consumers who don't touch the event hooks; the three fixes
below close gaps a real integrator would hit the moment they tried to
use `Events` the way the stock `AddJwtBearer` lets you.

### Fixed

- **`OnMessageReceived` could not short-circuit.** The standard
  `Microsoft.AspNetCore.Authentication.JwtBearer.MessageReceivedContext`
  exposes a settable `Result` so the event can return
  `AuthenticateResult.NoResult()` or `AuthenticateResult.Fail(...)`
  directly. Ours did not — once the event ran, the handler always
  proceeded into the `Authorization` header lookup. Added the missing
  `Result` property; the handler now honors it.
- **`OnTokenValidated` could not reject a valid token.** Same pattern
  on `PostQuantumJwtBearerTokenValidatedContext`. The use case is
  custom authorization that runs after cryptographic validation
  (e.g. "the signature is valid but this `sub` is on a deny list").
  Without a settable `Result`, the only way to reject was to throw,
  which routed through `OnAuthenticationFailed` and lost the
  intended `Fail(reason)`. Now an event can write
  `ctx.Result = AuthenticateResult.Fail(...)` and the handler
  returns that result in place of the success ticket.
- **Challenge was overwriting `WWW-Authenticate` instead of appending.**
  The previous code assigned `Response.Headers.WWWAuthenticate = header`,
  which clobbers any challenge other middleware (e.g. a different
  authentication scheme) had already added. Switched to
  `Response.Headers.Append(...)` so multiple challenges coexist —
  matches the behavior of the stock `JwtBearerHandler`.

### Tests

- Added `OnMessageReceivedTests.ResultNoResult_ShortCircuitsAuthorizationHeader`.
- Added `PostQuantumJwtBearerHandlerTests.OnTokenValidated_ResultFail_ReplacesSuccessTicket`.
- Added `PostQuantumJwtBearerHandlerTests.Challenge_PreservesExistingWwwAuthenticateHeaders`.

Suite total: **69 tests, 0 failures, 0 skips** on PQ-capable hosts.

## [1.0.0-preview.1] — 2026-05-31

**First release-candidate-preview cut.** Same API surface as
`0.9.1-preview.1`; the version bump signals that we believe the
public API is what we expect to ship as `1.0.0`. The `-preview.1`
suffix maintains honest preview status — the cryptographic
construction in the underlying engine library has not yet been
independently audited, and IANA has not registered the `ML-DSA-65`
identifier the library uses. Until those gates are closed,
`1.0.0` proper is not appropriate; this preview line is the
runway.

### Changed

- **Version bump only.** No source changes vs `0.9.1-preview.1`.
- **csproj metadata refreshed** for 1.0-preview semantics —
  `<Description>` tightened, `<PackageReleaseNotes>` rewritten
  to summarise the full feature set rather than a single release's
  diff, in case this is a consumer's first encounter with the
  package on nuget.org.

### What's in 1.0.0-preview.1 (cumulative through 0.9.x)

- **`AddPostQuantumJwtBearer()`** one-line wireup that slots into the
  standard `AuthenticationBuilder`. `[Authorize]`, policies, role
  checks, and middleware all work unchanged.
- **Fail-closed validation handler** with defense-in-depth catch of
  every non-fatal exception out of `Validate()`. Engine-level
  parser leaks no longer surface as 500s.
- **JWKS-equivalent key rotation** via `IPostQuantumJwtKeyRing` +
  `HttpPostQuantumJwtKeyRing`. Atomic snapshot swap on refresh.
  Unknown-`kid` throttling. Hosted-service startup warmup
  (`AddPostQuantumJwtKeyRingWarmup`).
- **Four event hooks** — `OnMessageReceived` (alternate token
  transports including SignalR `?access_token=`), `OnTokenValidated`,
  `OnAuthenticationFailed`, `OnChallenge`.
- **Distributed replay protection** — `PostQuantum.AspNetCore.RedisReplayCache`
  companion package; one-line `AddPostQuantumJwtRedisReplayCache(...)`.
- **First-class observability** — `System.Diagnostics.Metrics`
  (`postquantum.jwt.auth.success/failure/latency`,
  `postquantum.jwt.keyring.resolve/fetch.latency`) +
  `ActivitySource` (`PostQuantum.AspNetCore`) for OpenTelemetry.
- **AOT-compatible** — `IsAotCompatible=true`, verified end-to-end
  in CI on Linux, Windows, and macOS.
- **Documentation** — SECURITY-MODEL, GETTING-STARTED, RECIPES
  (13 scenarios), FAQ, PRODUCTION-CHECKLIST, MIGRATION (from both
  `AddJwtBearer` and `PostQuantum.Jwt.AspNetCore`), DIAGNOSTICS,
  PERFORMANCE, API-STABILITY, MUTATION-TESTING, audit trail under
  `docs/audits/`.
- **Samples** — minimal API, SignalR (in-page browser client),
  MVC with controllers + role/policy authorization.
- **66 tests**, zero skips on PQ-capable hosts. CI lanes: build+test
  on Ubuntu and Windows, linux-pq-required with OpenSSL 3.5+ via
  conda-forge, multi-platform AOT publish, code coverage with a 75%
  floor (project sits at ~84.6%), version-sync gate, format-verify
  gate, pack-verify, release workflow with `nuget-publish` env gate
  + SHA256SUMS + build-provenance attestations + optional author
  signing.

## [0.9.1-preview.1] — 2026-05-31

A **follow-up polish** release on the 0.9 trust-and-adoption pass.
Audit found one stale claim in the README and two opportunities to
surface critical security guidance earlier in the consumer's read.

### Fixed

- **README Security posture had a stale claim.** It said "the bearer
  prefix is matched ordinally — no case-insensitive surprises," but
  that was changed to case-insensitive matching (RFC 6750) all the way
  back in `0.3.0-preview.1`. Corrected to reflect actual behaviour and
  expanded to call out the new fail-closed exception catch + RFC 7235
  realm escaping.

### Changed

- **`docs/SECURITY-MODEL.md` surfaced at the top of the README's
  Security posture section** with an explicit "read that before
  depending on this for anything that matters" callout. Previously it
  was only reachable via the "in a hurry?" jump-table; now the section
  itself opens with the link.
- **Production-readiness pointer added right after the 60-second
  tour.** A ⚠️ callout shows the one-line Redis replay-cache wireup
  so consumers see the production path immediately after the
  happy-path demo, not buried in a later section.
- **Replay-defence bullet in Security posture sharpened** — explicit
  "**without a configured `IPqJwtReplayCache`, captured tokens are
  reusable until they expire**" warning, with a link to the headline
  Redis section.

### Tests

- **66/66 tests pass on PQ-capable hosts.** Documentation-and-warning
  improvements only — no code changes.

## [0.9.0-preview.1] — 2026-05-31

A **trust-and-adoption polish** release. The library code is mature
enough that the next biggest improvement is friction reduction at
the consumer's first touch and clarity about the security contract.

### Added

- **`docs/SECURITY-MODEL.md`** — comprehensive doc explaining what
  the library protects (token integrity / authenticity, issuer +
  audience binding, lifetime, algorithm pinning, fail-closed
  exception handling, bearer-prefix normalisation, RFC-compliant
  challenge responses, opt-in replay protection), what it does NOT
  protect (key management, transport, authorization policy, DoS,
  token revocation beyond expiration, side-channel resistance),
  the replay-protection deployment-shape matrix, the key-rotation
  cadence, the full fail-closed contract enumerated, and a
  comparison vs standard `JwtBearer`.
- **"Migrating from `AddJwtBearer`" section in the README** — a
  side-by-side diff showing exactly what changes at the call site
  (most apps need only that one block; `[Authorize]`, policies,
  claims, and middleware all work unchanged downstream). Includes
  a "Run both during migration" pattern for gradual rollout.
- **"Highlights" section in the README** — at-a-glance bullet list
  of the seven things a consumer needs to know in 15 seconds:
  one-line wireup, fail-closed, distributed replay, JWKS-equivalent
  rotation, event hooks, observability, AOT, honest preview status.
- **"About this library" section in the README** — human + AI
  transparency statement crediting the collaboration model.

### Changed

- **Redis replay protection elevated to a headline capability.**
  The README's `Distributed replay protection with Redis` section
  is now marked `⭐ recommended for production`, with explicit
  guidance that **without a configured replay cache, `jti` is
  carried but never enforced — a captured token is reusable until
  it expires**. The "in a hurry?" jump-table surfaces the security
  model + Redis path at the top of the readme.
- **README lede tightened** for adoption focus — opens with the
  one-line wireup and the unchanged-downstream promise (`[Authorize]`,
  policies, claims, middleware all keep working) so consumers see
  the value in the first paragraph.
- **`KNOWN-GAPS.md` audited and synced with the code.** Removed
  stale entries that misrepresented current capabilities:
  the "No bearer-token retrieval hook" gap (closed by
  `OnMessageReceived` in v0.4), the "No `OnMessageReceived` event"
  gap (same), and the "No coverage-guided fuzzing" gap (closed by
  the SharpFuzz harness in v0.7). Existing accurate gaps preserved.
- **NuGet `<Description>` and `<PackageReleaseNotes>` polished** —
  tighter, adoption-focused, lead with the "same shape as
  AddJwtBearer" pitch, name the companion package, surface
  replay protection.

### Tests

- **66 tests, zero skips on PQ-capable hosts.** No new tests this
  release — the changes are documentation + packaging.

## [0.8.0-preview.1] — 2026-05-31

## [0.8.0-preview.1] — 2026-05-31

A **documentation-and-examples** release. The library code is mature
enough that the next biggest improvement isn't more code — it's
making sure a downloader can go from "I just saw this on nuget.org"
to "in production" without writing a single line of glue.

### Added

- **`docs/GETTING-STARTED.md`** — the canonical "first 10 minutes"
  walkthrough. Prereqs check, `dotnet new webapi`, install,
  scaffold a working PQ-protected API, exercise it with `curl`.
- **`docs/RECIPES.md`** — 13 copy-paste-able scenarios covering the
  questions consumers actually ask: token minting, static-key
  validation, JWKS-equivalent key rotation, hosted-service warmup,
  Redis replay protection, role + policy authorization,
  multi-scheme coexistence with `AddJwtBearer`, OpenTelemetry
  (metrics + traces), SignalR `?access_token=`, multi-tenant
  validation, health checks, Swagger/OpenAPI integration,
  Docker/Kubernetes notes.
- **`docs/FAQ.md`** — 17 pre-loaded answers covering "should I use
  this in production?", "how big are tokens really?", "does this
  work with Auth0/IdentityServer?", "why net10.0 only?", and the
  rest.
- **`samples/PostQuantum.AspNetCore.Mvc.Demo`** — controller-based
  ASP.NET Core MVC sample with `[Authorize]`,
  `[Authorize(Roles = "admin")]`, and
  `[Authorize(Policy = "AcmeTenant")]` against PQ tokens. In-page
  browser harness mints tokens and exercises each endpoint.

### Changed

- **README opens with an "in a hurry?" callout** linking the new
  getting-started, recipes, FAQ, production checklist, and migration
  docs. The very first thing a downloader sees points them at the
  right doc for their context.
- **README adds inline sections** for the three things consumers ask
  about first: Redis replay protection (companion package wireup),
  OpenTelemetry metrics + traces (one-liner subscription), issuing
  tokens (server-side `PqJwtBuilder` snippet).
- **`docs/README.md` index** restructured into Start-Here /
  Migrating / Operations / ADRs / Audits sections so the doc landing
  page is itself a useful map.

## [0.7.0-preview.1] — 2026-05-31

A **companion + depth** release. v0.6 was 56-tests-green; v0.7 adds the
last residual items from the v0.5 audit-tier list: a Redis-backed
`IPqJwtReplayCache` reference, a coverage-guided fuzz harness, and a
cross-repo engine-side wrap of parser-level exceptions.

### Added

- **`PostQuantum.AspNetCore.RedisReplayCache`** — new sibling NuGet
  package implementing `IPqJwtReplayCache` against
  StackExchange.Redis. Uses `SET key 1 NX PX {ttl}` for atomic
  single-use-`jti` enforcement across a Redis cluster. TTL = remaining
  token lifetime (capped at 30 days for adversarial-clock cases).
  `AddPostQuantumJwtRedisReplayCache(connectionString)` DI helper
  registers the cache and weaves it into the named scheme's
  `PqJwtValidationParameters.ReplayCache` via `PostConfigure`. 10 new
  tests against an NSubstitute IDatabase stub.
- **`tests/PostQuantum.AspNetCore.Fuzz`** — coverage-guided fuzz
  harness via SharpFuzz + libfuzzer-dotnet. Targets
  `PqJwtValidator.Validate` (the most interesting parser in the
  stack). Includes seed corpus, README documenting the local-run
  workflow. Complements the in-process structured fuzz tests in
  `FuzzTests` (which run in CI on every push); the SharpFuzz harness
  is for deeper soaks.
- **`docs/MUTATION-TESTING.md`** — honest record of the Stryker.NET
  configuration, the runs we tried, and why the baseline still doesn't
  produce a usable mutation score on this project shape (likely
  xunit-v3 + NSubstitute test-discovery limitation). Configuration
  preserved for a future retry; in the meantime the in-process fuzz
  catches the equivalents *it* can catch (and has — twice).
- **12 new tests.** **66 total, zero skips on PQ-capable hosts.**

### Changed

- **Engine-side wrap (cross-repo).** `PostQuantum.Jwt`'s
  `PqJwtValidator.Validate` now wraps parser-level
  `FormatException`, `JsonException`, and `CryptographicException` as
  `PqJwtValidationException` (engine commit `postquantum-jwt@0b405c7`).
  This is the engine side of the v0.5 fail-closed fix: this package's
  handler still has the broader catch as defense-in-depth, but
  consumers calling `PqJwtValidator.Validate` directly now see the
  documented `PqJwtException` family for every fail-closed path
  instead of three different raw types. Two new tests landed in the
  engine repo's `PqJwtEdgeCaseTests` to lock the contract.

## [0.6.0-preview.1] — 2026-05-31

A **trust-the-build** release. The library was already 54-tests-green
before this — v0.6 is about closing the *I-cannot-tell-from-CI* gaps
left in v0.5. Discovered (and fixed) one more real semantic bug along
the way.

### Fixed (correctness)

- **`HttpPostQuantumJwtKeyRing.PreloadAsync` now propagates fetch
  failures** instead of swallowing them. `Resolve` (the hot path) still
  swallows: a flaky key endpoint should not turn an authenticated
  request into a 500. But the warmup hosted service's `FailFastOnStartup`
  contract said "fail host startup if the key endpoint is unreachable"
  — and it had nothing to fail on, because `PreloadAsync` was sharing
  the same catch-and-log path as `Resolve`. They now diverge:
  `PreloadAsync` rethrows, `Resolve` doesn't. Found by the new
  `WarmupIntegrationTests.WarmupFailFast_AbortsHostStartup_OnUnreachableEndpoint`.

### Added

- **`WarmupIntegrationTests`** — full-pipeline tests that wire
  `AddPostQuantumJwtKeyRing(uri) + AddPostQuantumJwtKeyRingWarmup()`
  against a stub HTTPS endpoint, start a real `Host`, and assert
  (1) warmup actually preloads before the host considers itself
  started, (2) the first authenticated request hits the warm cache
  with **zero** additional fetches, and (3) fail-fast warmup
  aborts host startup on an unreachable endpoint.
- **Multi-platform AOT publish in CI.** The `aot-publish` job now runs
  on Ubuntu (`linux-x64`), Windows (`win-x64`), and macOS (`osx-arm64`).
  AOT linkers differ — clang, link.exe, ld64 — and a library that
  publishes cleanly on one can still break on another. Three OS lanes
  in CI catch the regression at PR time.
- **Code-coverage gate in CI.** `dotnet test --collect:"XPlat Code
  Coverage"` runs alongside the regular test step; `reportgenerator`
  produces a Markdown summary that surfaces in the PR run's step
  output. The build fails if line coverage drops below 75% (project
  currently sits at **84.6%**). Coverage report uploaded as an
  artifact with 14-day retention.
- **2 new tests.** **56 total, zero skips on PQ-capable hosts.**

## [0.5.0-preview.1] — 2026-05-31

A **production-depth** release. v0.4 finished the surface; v0.5 deepens
it: fuzz testing caught two real fail-open bugs, the handler now defends
in depth against any exception out of `Validate()`, and the library
emits proper metrics + tracing for production observability. AOT
publishing is verified end-to-end via a smoke-test project enforced in
CI. Differentiation messaging is sharpened across README, csproj, and
CLAUDE so it's unmissable that this is the ASP.NET Core integration
layer, not a cryptography library.

### Fixed (correctness)

- **Handler now catches the broader `PqJwtException` family** instead
  of only `PqJwtValidationException`. The engine can throw `PqJwtException`
  (parent class) for configuration mismatches like "encrypted token,
  no decryption key configured" — those used to escape the handler and
  surface as 500s, leaking server state. Found by the new
  `FuzzTests.RandomBytesAsTokens_…` test.
- **Defense-in-depth: handler now catches any non-fatal exception out
  of `Validator.Validate()`.** Engine leaks (e.g. `FormatException`
  from bad Base64 inside a token segment, `CryptographicException`
  from a malformed key blob) used to produce 500s. They are now
  treated as fail-closed 401s. `OutOfMemoryException` and
  `StackOverflowException` are explicitly NOT caught — they're
  environmental and should crash the host so an operator notices.

### Added

- **`System.Diagnostics.Metrics` instrumentation.** New static type
  `PostQuantumJwtBearerDiagnostics` exposes the
  `"PostQuantum.AspNetCore"` `Meter` and `ActivitySource`. The handler
  emits `postquantum.jwt.auth.success` (counter, tagged with `scheme`),
  `postquantum.jwt.auth.failure` (counter, tagged with `scheme` +
  `reason`), and `postquantum.jwt.auth.latency` (histogram). The HTTP
  key ring emits `postquantum.jwt.keyring.resolve` (counter, tagged
  with `result`) and `postquantum.jwt.keyring.fetch.latency`
  (histogram). Subscribe via OpenTelemetry / Prometheus exporters /
  Application Insights — the instrumentation name + version is
  versioned and documented.
- **`ActivitySource` distributed-tracing instrumentation.** The handler
  emits a `PostQuantumJwtBearer.Validate` span around every token
  validation, tagged with `scheme`, `result`, and (on failure) the
  exception type. Span status is set to OK / Error appropriately.
- **`tests/PostQuantum.AspNetCore.AotSmokeTest`.** A minimal consuming
  app that exercises every public API entry point and is published
  with `PublishAot=true` in CI. `TreatWarningsAsErrors=true` means an
  AOT-unsafe regression in the library fails the build. The
  `aot-publish` CI job runs on Ubuntu (where clang is available);
  Windows local runs work too with Visual Studio Build Tools
  installed.
- **`benchmarks/PostQuantum.AspNetCore.Benchmarks`.** BenchmarkDotNet
  project for `PqJwtValidator.Validate` throughput + allocations and
  `HttpPostQuantumJwtKeyRing.Resolve` hot-path lookup. Results land in
  `docs/PERFORMANCE.md`.
- **Property-flavoured fuzz on the header helpers** (`HeaderEncodingProperties`,
  1000 deterministic-seeded iterations per case): realm escaping
  round-trip parseability against `AuthenticationHeaderValue.TryParse`,
  no-op preservation on safe inputs, count-balance invariant on
  escaped output, case-insensitive bearer-prefix acceptance,
  non-bearer rejection.
- **In-process structured fuzz** (`FuzzTests`, 2000 iterations per
  case) for the full pipeline. Caught the two fail-open bugs above
  and locks them with regression tests.
- **`HeaderEncoding` internal helper class** extracted from the
  handler with `InternalsVisibleTo("PostQuantum.AspNetCore.Tests")`,
  so the helpers are unit-testable without spinning a full request
  pipeline.
- **`.github/dependabot.yml`** for weekly NuGet + GitHub Actions
  dependency updates, grouped sensibly (AspNetCore family, test
  tooling) so PRs don't fragment.
- **`docs/API-STABILITY.md`** documenting the public-surface stability
  promise during the `0.x` preview series and what blocks `1.0`.
- **`docs/PERFORMANCE.md`** with the benchmark-running instructions,
  what's measured, and what to expect (ML-DSA-65 verify is in
  milliseconds, not microseconds — plan for it).
- **`stryker-config.json`** for mutation testing. The initial run
  couldn't produce a useful score (coverage capture failed on this
  project); configuration is preserved so a future run on a stable
  Stryker release should land a baseline. Tracked in
  `KNOWN-GAPS.md`.
- **9 new tests**: 5 diagnostic-contract tests (per-scheme isolation
  via process-global signal filtering), 3 fuzz tests, plus a property
  block for `HeaderEncoding`. **54 tests total, zero skips on
  PQ-capable hosts.**

### Changed

- **Messaging across README, csproj `<Description>`, NuGet release
  notes, and CLAUDE.md** sharpened to be unmissable: this is the
  **high-level ASP.NET Core integration**, not a cryptography
  library. New "Where does this fit in the stack?" diagram and a
  "Not to be confused with…" comparison table covering
  BouncyCastle, liboqs, `System.Security.Cryptography`,
  `PostQuantum.Jwt`, and `Microsoft.AspNetCore.Authentication.JwtBearer`.
- **`TestServerFactory` now accepts a constructor scheme-name
  parameter** so diagnostic tests can isolate process-global
  `Meter` and `ActivitySource` signals from concurrent test runs.
  Object-initializer-based usage was dropped because init-only
  properties evaluate after the constructor — a subtle but real
  bug surfaced during T1.

## [0.4.0-preview.1] — 2026-05-31

A **surface-completion** release. v0.3 fixed correctness; v0.4 fills in
the last two API gaps a "definitive" ASP.NET Core integration needs
(cold-start warmup, SignalR proof) and formalises the supersession of
the legacy `PostQuantum.Jwt.AspNetCore` companion.

### Added

- **`AddPostQuantumJwtKeyRingWarmup(...)` hosted-service helper.**
  Registers an `IHostedService` that calls
  `IPostQuantumJwtKeyRing.PreloadAsync` on host startup and (optionally)
  on a periodic timer. By default, warmup is **fail-fast** — a key
  endpoint that's unreachable at startup aborts host start; set
  `FailFastOnStartup=false` for best-effort warmup that logs and
  continues. Closes the cold-start gap: the first validation request no
  longer pays a network round trip, and a removed `kid` is evicted on
  the next periodic tick rather than waiting for an unknown-`kid` miss.
- **`PreloadAsync` lifted onto `IPostQuantumJwtKeyRing`** with a no-op
  default implementation. `HttpPostQuantumJwtKeyRing` already had the
  method; the lift is what lets the warmup helper work generically
  against any ring (database-backed, KMS-backed, etc.).
- **`samples/PostQuantum.AspNetCore.SignalR.Demo`.** A complete one-process
  sample: token-minting endpoint, SignalR hub, in-page browser client.
  Proves the `OnMessageReceived` event end-to-end with the canonical
  SignalR `?access_token=` query-string pattern — not just in a unit
  test.
- **5 new tests** for warmup behaviour: initial preload fires once,
  fail-fast propagates exceptions, best-effort swallows them, periodic
  refresh fires on the timer (with a small FakeTimeProvider stub), the
  DI helper registers the hosted service correctly. **40 tests total,
  zero skips on PQ-capable hosts.**

### Changed

- **Engine repo's `PostQuantum.Jwt.AspNetCore` is now formally
  superseded** by this package. The engine companion's `<Description>`,
  `<PackageReleaseNotes>`, README, and CHANGELOG were updated in a
  parallel commit on that repo (postquantum-jwt#1a3d6a2). Tokens minted
  under either package validate in the other — same engine.

## [0.3.0-preview.1] — 2026-05-30

A **10/10 audit pass** release. A self-imposed code review turned up four
real correctness bugs in `0.2.0-preview.1` plus several "this is the
ASP.NET Core integration; it needs to *be* the standard" gaps. All of
them are closed here; the auth contract is unchanged.

### Fixed (correctness)

- **`HttpPostQuantumJwtKeyRing` no longer disposes `MLDsa` instances
  under in-flight validations.** Previously, a `kid` rotation that
  replaced an entry called `old.Dispose()` while the engine's
  `SignatureKeyResolver` could still be holding the reference — a race
  that surfaced as `ObjectDisposedException` mid-request during key
  rotation. The cache is now an immutable snapshot that gets atomically
  swapped (volatile reference), and previous-generation `MLDsa`
  instances are released by GC once no validator holds them.
- **Caller cancellation now propagates from `ResolveAsync`.** The catch
  filter in `RefreshAsync` was lumping `TaskCanceledException` in with
  network/parse failures and log-and-swallowing it — including genuine
  caller cancellation. A targeted
  `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`
  now rethrows.
- **`WWW-Authenticate` realm is now RFC 7235-compliant quoted-string
  escaped.** Previously a realm containing `"` or `\` produced a
  malformed header. Both characters are now backslash-escaped before
  interpolation.
- **`Bearer` prefix matching is case-insensitive (RFC 6750).** The
  ordinal compare in `HandleAuthenticateAsync` was a footgun for clients
  that send `bearer` (lowercase) — perfectly valid per the RFC. Match is
  now `OrdinalIgnoreCase`.

### Added

- **`PostQuantumJwtBearerEvents.OnMessageReceived`** — runs before the
  `Authorization` header is read. Set
  `PostQuantumJwtBearerMessageReceivedContext.Token` to substitute a
  token from a non-standard carrier: SignalR `?access_token=`, a signed
  cookie, a custom header. Unblocks SignalR and any non-Authorization-header
  transport.
- **`PostQuantumJwtBearerOptions.KeyRing`** — a first-class
  `IPostQuantumJwtKeyRing` slot. The handler weaves the ring's
  `Resolve` method onto `SignatureKeyResolver` at validator construction,
  giving users a way to wire JWKS-equivalent key resolution without
  cloning `PqJwtValidationParameters` by hand.
- **`AddPostQuantumJwtKeyRing(...)` DI helpers** — both an HTTP overload
  (registers `HttpPostQuantumJwtKeyRing` as a typed client) and a
  generic overload for user-supplied implementations. Replaces the
  README's previous `BuildServiceProvider()` anti-pattern with a
  `PostConfigure<TOptions, TDep>` pattern that resolves the ring from
  the real service provider.
- **`PostQuantumJwtBearerOptions.Validate()` override** — the framework
  calls it during named-options materialisation. Throws when no key
  source is configured, so misconfiguration fails at startup rather
  than on the first request.
- **`docs/adr/0001-no-algorithm-agility.md`** — ADR locking in the
  single-suite decision for the `0.x` series, with an explicit "when
  would we reconsider" gate.
- **`docs/PRODUCTION-CHECKLIST.md`** — pre-deployment checklist covering
  crypto/key material, replay protection, ASP.NET Core wiring,
  observability, and supply-chain verification.
- **`docs/DIAGNOSTICS.md`** — top-to-bottom debugging guide for "why is
  my token failing validation?" with the log-event ID table and the
  "isolate against the engine" reproduction recipe.
- **`CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `.github/CODEOWNERS`,
  PR/issue templates** — repo hygiene matching what consumers expect
  from a definitive package.
- **`.editorconfig`** — codifies the formatting baseline.
- **`scripts/check-version-sync.sh`** — asserts csproj/README/CHANGELOG
  agree on the version. Wired into CI on every push.
- **`dotnet format --verify-no-changes` CI job** — formatting drift
  fails the build.
- **11 new tests** (handler-level + integration): kid resolution via
  the key ring (success and unknown-kid fail-closed), kid rotation
  doesn't `ObjectDisposedException`, `Validate()` throws at startup with
  no key source, `OnMessageReceived` substitutes a token from a query
  string, realm with embedded `"`/`\` produces a parseable header,
  cancellation propagates through `ResolveAsync`, kid removal eviction
  (via both `PreloadAsync` and unknown-kid-driven refresh), repeated
  unknown-kid throttling, and a throwing `OnTokenValidated` handler
  routes through `OnAuthenticationFailed` (not as a 500). **35 tests
  total, zero skips on PQ-capable hosts.**
- **`docs/audits/`** — preserved trail of independent reviews against
  `0.2.0-preview.1` from ChatGPT (found the five correctness issues
  above plus three test gaps) and Gemini (applied five of the fixes
  in parallel). Both reviews are referenced from the findings table in
  `docs/audits/README.md`.

### Changed

- **`HttpPostQuantumJwtKeyRing` cache is now an immutable-snapshot
  atomic swap.** Each refresh builds a fresh `Dictionary<string, MLDsa>`
  and assigns it to the volatile `_cache` field. Readers see either the
  pre- or post-refresh snapshot — never a torn intermediate, and never
  a disposed key.
- **Unknown-`kid` refresh is now throttled** (10-second floor between
  forced refreshes from cache misses) to prevent a flood of unknown
  `kid` requests from amplifying into a directory-fetch storm.
- **`OnTokenValidated` is wrapped in try/catch** — a throwing
  enrichment callback flows through `OnAuthenticationFailed` and ends
  with a fail-closed 401, never a 500.

### Known limitations

- See `KNOWN-GAPS.md` — the residual list is now down to the
  sync-over-async cold-miss bridge on the key ring (blocked on the
  engine exposing an async `SignatureKeyResolver`) and the author code
  signing gate (waiting on a code-signing certificate). The test gap
  and the version-sync gap from v0.2 are closed.

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

[Unreleased]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v1.0.0-preview.3...HEAD
[1.0.0-preview.3]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v1.0.0-preview.2...v1.0.0-preview.3
[1.0.0-preview.2]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v1.0.0-preview.1...v1.0.0-preview.2
[1.0.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.9.1-preview.1...v1.0.0-preview.1
[0.9.1-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.9.0-preview.1...v0.9.1-preview.1
[0.9.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.8.0-preview.1...v0.9.0-preview.1
[0.8.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.7.0-preview.1...v0.8.0-preview.1
[0.7.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.6.0-preview.1...v0.7.0-preview.1
[0.6.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.5.0-preview.1...v0.6.0-preview.1
[0.5.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.4.0-preview.1...v0.5.0-preview.1
[0.4.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.3.0-preview.1...v0.4.0-preview.1
[0.3.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.2.0-preview.1...v0.3.0-preview.1
[0.2.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.1.0-preview.1...v0.2.0-preview.1
[0.1.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/releases/tag/v0.1.0-preview.1
