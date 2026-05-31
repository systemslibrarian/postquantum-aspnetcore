# Production checklist

Before you put `PostQuantum.AspNetCore` on a path that real users hit,
walk this list. **The library is preview software** — none of this turns
that into "production ready," but skipping any of these items is a
near-guaranteed incident waiting to happen.

## Crypto & key material

- [ ] **You are on a runtime with ML-DSA support.** Windows recent enough
      or Linux with OpenSSL 3.5+. The engine fails closed if not, but
      catching that at deployment is much kinder than catching it at
      first request.
- [ ] **Signing keys live outside source control.** Configuration,
      environment variables, a secret manager — anywhere but the repo.
- [ ] **Verification keys are distributed out-of-band** to every service
      that validates tokens, either statically or via the key ring's
      HTTPS endpoint.
- [ ] **The key-directory endpoint (if used) is HTTPS, not HTTP**, with
      TLS pinning or a private CA where the threat model warrants it.
- [ ] **Key rotation has a written cadence and a rollback plan.** Common
      starts: signing keys rotate every 90 days; verification keys are
      retired one full token lifetime *after* the corresponding signing
      key is retired.

## Replay protection

- [ ] **`PqJwtValidationParameters.ReplayCache` is configured.** With no
      cache, `jti` is carried but not enforced — a captured token is
      reusable until it expires.
- [ ] **Multi-instance deployments back the cache with a shared store**
      (Redis, a database, the cache your stack already uses). The
      bundled `InMemoryReplayCache` is single-process and does not
      survive restarts.

## Issuer & audience

- [ ] **`ValidIssuer` and `ValidAudience` are pinned** to the values
      your issuer mints, not left null. A null issuer/audience means
      "accept any" — that's never what you want in production.
- [ ] **Audience is service-specific.** A token meant for the orders API
      should not validate at the payments API.

## ASP.NET Core wiring

- [ ] **`UseAuthentication()` is registered before `UseAuthorization()`**
      in the pipeline.
- [ ] **`UseHttpsRedirection()` (or equivalent TLS termination) runs
      first**, so the `Authorization` header never crosses an unencrypted
      hop.
- [ ] **If you run behind a reverse proxy, `UseForwardedHeaders()` is
      configured** with explicit `KnownProxies` / `KnownNetworks` so the
      proxy's `X-Forwarded-*` are trusted and downstream's aren't.
- [ ] **No `AddJwtBearer` and `AddPostQuantumJwtBearer` registered on the
      same default scheme.** Either pick one, or restrict each to
      specific endpoints with
      `[Authorize(AuthenticationSchemes = "...")]`.
- [ ] **Misconfiguration fails at startup.** `Validate()` throws when
      no key source is configured — confirm your CI smoke test actually
      starts the host.

## Observability

- [ ] **`Microsoft.Extensions.Logging` is collecting `PostQuantum.AspNetCore`
      log events.** Validation failures log at `Debug` (event ID 1); key
      ring fetch failures log at `Warning` (event IDs 2-4).
- [ ] **Metrics or traces tag the scheme name.** When migrating from
      `JwtBearer`, distinguishing the two by scheme is the first
      diagnostic step.
- [ ] **401 responses are alertable.** A sudden spike in challenges
      after a key rotation is usually the signal that the verifier
      didn't get the new key in time.

## Threats covered (and not)

- [ ] You have read [`SECURITY.md`](../SECURITY.md) and [`KNOWN-GAPS.md`](../KNOWN-GAPS.md)
      end-to-end.
- [ ] You accept that the cryptographic construction has **not been
      independently audited**.
- [ ] You accept that the wire format is **not IANA-registered** and
      tokens will not validate in generic JWT tooling.
- [ ] You understand this is **preview software** and the API or wire
      format may change before `1.0`.

## Supply chain

- [ ] You verify the package's GitHub build-provenance attestation:
      `gh attestation verify <file> --repo systemslibrarian/postquantum-aspnetcore`.
- [ ] You check the bundled CycloneDX SBOM (`/bom.json` inside the
      `.nupkg`) against your dependency-policy tooling.

---

If everything above is checked and your incident response plan covers the
"key endpoint goes down" and "ML-DSA support disappears from the runtime"
failure modes, you're as ready as preview software allows. Until 1.0,
keep a back-out path.

---

*To God be the glory — 1 Corinthians 10:31.*
