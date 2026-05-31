# Review Results

## Findings

1. Stale verification keys are never evicted after refresh, so a removed key can remain valid for the lifetime of the process.

   The refresh path only adds or overwrites entries in [_cache[entry.Kid] = key](src/PostQuantum.AspNetCore/HttpPostQuantumJwtKeyRing.cs#L166); it never removes kids that disappeared from the upstream directory. That directly conflicts with the documented behavior in [KNOWN-GAPS.md](KNOWN-GAPS.md#L53), which says removal takes effect on the next successful full refresh. For a package that may sit on internet-facing APIs, this is the highest-priority fix: build the refreshed key set off to the side, swap it in atomically, and add a regression test for key removal.

   Relevant code: [refresh on miss](src/PostQuantum.AspNetCore/HttpPostQuantumJwtKeyRing.cs#L97), [cache write](src/PostQuantum.AspNetCore/HttpPostQuantumJwtKeyRing.cs#L166), [documented expectation](KNOWN-GAPS.md#L53).

2. The bearer scheme check is case-sensitive, which is not HTTP-compatible and will reject valid Authorization headers from some clients and proxies.

   The handler requires an exact Bearer prefix via [StringComparison.Ordinal](src/PostQuantum.AspNetCore/PostQuantumJwtBearerHandler.cs#L79), and the security doc explicitly calls that out in [SECURITY.md](SECURITY.md#L51). HTTP auth schemes are case-insensitive, so `bearer`, `BEARER`, and `Bearer` should all work. At scale this becomes an avoidable interoperability outage. Suggestion: accept the scheme case-insensitively while still preserving fail-closed behavior for malformed headers.

   Relevant tests currently only cover exact Bearer and a non-bearer scheme in [tests/PostQuantum.AspNetCore.Tests/PostQuantumJwtBearerHandlerTests.cs](tests/PostQuantum.AspNetCore.Tests/PostQuantumJwtBearerHandlerTests.cs#L21) and [tests/PostQuantum.AspNetCore.Tests/PostQuantumJwtBearerHandlerTests.cs](tests/PostQuantum.AspNetCore.Tests/PostQuantumJwtBearerHandlerTests.cs#L45).

3. Unknown `kid` values force a remote fetch on every miss, which creates an easy outbound amplification path.

   On a cache miss, the key ring always does [RefreshAsync(force: true)](src/PostQuantum.AspNetCore/HttpPostQuantumJwtKeyRing.cs#L97), bypassing the normal refresh interval. An attacker can send many tokens with random `kid` values and turn your API tier into a steady stream of fetches against the key endpoint. That is both a scalability risk and a dependency-amplification risk. Suggestion: add bounded negative caching or rate-limited refresh-on-miss behavior, and test repeated unknown kids under the same interval window.

   Current tests cover a single unknown-kid refresh only in [tests/PostQuantum.AspNetCore.Tests/HttpPostQuantumJwtKeyRingTests.cs](tests/PostQuantum.AspNetCore.Tests/HttpPostQuantumJwtKeyRingTests.cs#L40).

4. Exceptions thrown by `OnTokenValidated` will escape the handler as server errors instead of becoming authentication failures.

   The validated event is awaited directly in [src/PostQuantum.AspNetCore/PostQuantumJwtBearerHandler.cs](src/PostQuantum.AspNetCore/PostQuantumJwtBearerHandler.cs#L121) with no surrounding failure path, while only `PqJwtValidationException` is handled earlier in the method. In practice, one bad event hook can turn an auth failure into a 500 and bypass `OnAuthenticationFailed` entirely. Suggestion: wrap post-validation event execution, route exceptions through the authentication-failed event/context, and add a test for an event handler that throws. The current suite only verifies the happy-path enrichment hook in [tests/PostQuantum.AspNetCore.Tests/PostQuantumJwtBearerHandlerTests.cs](tests/PostQuantum.AspNetCore.Tests/PostQuantumJwtBearerHandlerTests.cs#L170).

## Open Questions

- The option docs say the challenge should include `invalid_token` only when a token was supplied and failed validation in [src/PostQuantum.AspNetCore/PostQuantumJwtBearerOptions.cs](src/PostQuantum.AspNetCore/PostQuantumJwtBearerOptions.cs#L53), but the implementation adds it unconditionally in [src/PostQuantum.AspNetCore/PostQuantumJwtBearerHandler.cs](src/PostQuantum.AspNetCore/PostQuantumJwtBearerHandler.cs#L141), and the test suite expects that behavior for a missing header in [tests/PostQuantum.AspNetCore.Tests/PostQuantumJwtBearerHandlerTests.cs](tests/PostQuantum.AspNetCore.Tests/PostQuantumJwtBearerHandlerTests.cs#L136). That should be made consistent one way or the other.
- If this is intended for very large deployments, it needs explicit tests for key rotation removal, lowercase bearer, repeated unknown-`kid` pressure, and throwing event hooks before calling the surface production-ready.

## Validation

- `dotnet test` passed: 18 tests, 0 failures.
