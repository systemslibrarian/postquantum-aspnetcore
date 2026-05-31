# Changelog

All notable changes to `PostQuantum.AspNetCore` are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
once it reaches `1.0.0`. Preview releases (`0.x`) may break the API between
versions.

## [Unreleased]

_No changes yet._

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

[Unreleased]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.5.0-preview.1...HEAD
[0.5.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.4.0-preview.1...v0.5.0-preview.1
[0.4.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.3.0-preview.1...v0.4.0-preview.1
[0.3.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.2.0-preview.1...v0.3.0-preview.1
[0.2.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/compare/v0.1.0-preview.1...v0.2.0-preview.1
[0.1.0-preview.1]: https://github.com/systemslibrarian/postquantum-aspnetcore/releases/tag/v0.1.0-preview.1
