# ADR 0001 — No algorithm agility

**Status:** Accepted (locked in for the `0.x` series).
**Date:** 2026-05-30.
**Deciders:** Paul Clark.

## Context

`Microsoft.AspNetCore.Authentication.JwtBearer` accepts a wide catalogue of
JWT algorithms (RS256/384/512, PS256/384/512, ES256/384/512, EdDSA,
HS256/384/512) and lets the token's `alg` header steer the validator to
the right code path. That design is the source of every named JWT bug in
the last decade:

- `alg: none` — token claims accepted with no signature.
- HS/RS confusion — an asymmetric public key parsed as a symmetric HMAC
  secret.
- Downgrade attacks — coercing a verifier to a weaker algorithm than the
  issuer intended.
- Algorithm-substitution mismatches when a key is reused across multiple
  algorithm families.

A library whose entire reason for existing is to make *quantum-resistant*
authentication trustworthy cannot inherit that surface.

## Decision

`PostQuantum.AspNetCore` (and the underlying `PostQuantum.Jwt` engine)
accept **exactly one signature suite**: `ML-DSA-65` (FIPS 204), with
optional `X-Wing` (X25519 + ML-KEM-768, FIPS 203) sign-then-encrypt
using AES-256-GCM for content encryption.

There is no consumer-facing knob to enable a second algorithm, fall back
to a classical one, or "try both." The token's `alg` header is not
trusted to choose a code path — it is checked for an exact-match equality
against the one supported value, and any other value (or an absent value)
is a fail-closed validation error.

The HTTP key ring (`HttpPostQuantumJwtKeyRing`) enforces the same policy
on the wire: entries whose `alg` is anything other than `"ML-DSA-65"` are
silently dropped during refresh. A key directory cannot smuggle a
different algorithm into validation by listing it under a known `kid`.

## Consequences

**What we lose**

- **Interoperability with the IANA JOSE algorithm registry.** Tokens
  minted by this library will not validate in generic JWT tooling. This
  is the same trade-off the engine library makes, and is documented as
  the headline limitation in `SECURITY.md`.
- **The ability to negotiate to a faster algorithm** when post-quantum
  cost (token size ~4.5 KB, signing throughput) is operationally painful.
  Consumers who need that flexibility should use
  `Microsoft.AspNetCore.Authentication.JwtBearer` until the IANA
  identifiers are registered and a true hybrid-by-policy story emerges.
- **The classical migration path** of "run two algorithms during a
  transition window." There is no transition window here — the only
  choice is between this scheme and nothing.

**What we gain**

- **No algorithm-confusion bugs by construction.** The vulnerability
  class that produced `alg: none`, HS/RS confusion, and a decade of CVEs
  in standard JWT libraries is *unreachable* in this codebase. There is
  no second code path to confuse.
- **A token format that fails closed under attacker control of the
  header.** Tampering with `alg` does not steer validation — it fails it.
- **A `Validate()` and key-ring policy that can be reasoned about end to
  end.** The single fact "every accepted token is ML-DSA-65" propagates
  through the handler, the validation parameters, and the JWKS-equivalent
  wire format without exception.
- **A smaller test surface.** The fail-closed suite enumerates exactly
  one happy path; everything else is a fail-closed case.

## Reconsidering

This ADR locks the decision for the `0.x` preview series. The library
will revisit algorithm agility when **both** of the following are true:

1. The IANA JOSE registry has assigned identifiers for the post-quantum
   algorithms we use (or any successor we'd want to adopt), so that
   standard tokens can describe them unambiguously.
2. There is a concrete consumer need — not a hypothetical one — that
   requires accepting a second algorithm. Common candidates: an explicit
   policy-based hybrid scheme (e.g., "require *both* a classical and a
   post-quantum signature on every token"), or a clean migration story
   to a higher security category (ML-DSA-87) when threat-model evidence
   demands it.

Even when we revisit, "agility" will mean *adding* a second algorithm
under a separately-named scheme/policy — not adding a runtime knob that
lets the token header pick between them. The token-driven-`alg` pattern
is closed for life.

---

*To God be the glory — 1 Corinthians 10:31.*
