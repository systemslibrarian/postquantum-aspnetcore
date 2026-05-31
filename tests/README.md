# tests/

Reserved for the upcoming `PostQuantum.AspNetCore.Tests` project.

`0.1.0-preview.1` ships without an integration test suite. The first
follow-up release will land a `Microsoft.AspNetCore.Mvc.Testing`-based suite
that locks the fail-closed contract at the HTTP boundary:

- valid token → `200 OK` with the expected `ClaimsPrincipal`
- tampered signature → `401 Unauthorized`
- expired token → `401 Unauthorized`
- wrong audience / wrong issuer → `401 Unauthorized`
- missing `Authorization` header → `401 Unauthorized` (challenge)
- non-`Bearer` scheme → falls through to other schemes (`NoResult`)

Until then, the contract rests on the engine library's 68 tests plus manual
exercise via the demo sample.

See `KNOWN-GAPS.md` at the repo root for the full set of v0.1 limitations.
