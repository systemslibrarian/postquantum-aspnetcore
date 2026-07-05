# Version Reconciliation — 1.0.0

## This package's assigned version

`PostQuantum.AspNetCore` = **`1.0.0`** (first stable release)

The companion package `PostQuantum.AspNetCore.RedisReplayCache` ships in
lockstep at the same version, as it has at every previous release in
this repository. The lockstep is now enforced mechanically: the release
workflow's version step fails if the two csproj versions drift.

## Dependency constraints updated

| Package | Was | Now |
|---|---|---|
| `PostQuantum.Jwt` | `1.0.0-preview.1` | `1.0.0` (stable, released 2026-06-30) |

Constraints **not changed** because this repository does not reference them:

- `PostQuantum.Cryptography` (suite target `1.0.0-rc.1`)
- `PostQuantum.FileEncryption` (suite target `1.0.0-rc.1`)
- `PostQuantum.SecureChannel` (suite target `0.3.0-preview.1`)

## Maturity discipline

This package ships at `1.0.0`. Its only PostQuantum.* dependency is
`PostQuantum.Jwt` at `1.0.0` — both stable, both under SemVer. **This
package does not advertise more maturity than anything it depends on**,
and that rule is what made this release possible: the engine GA'd first
(2026-06-30), so the integration layer may follow.

Stable does **not** mean audited. The engine's `1.0.0` reframed the
missing independent audit from a release gate to a **permanent,
documented limitation**, and this package inherits that framing verbatim
in `README.md`, `SECURITY.md`, and `KNOWN-GAPS.md`. The `1.0.0` version
number is a SemVer commitment about the API surface, not a claim that
the cryptographic construction has been externally reviewed.

## Related retirement

`PostQuantum.Jwt.AspNetCore` — the engine repository's original ASP.NET
Core companion and this package's predecessor — was deprecated, unlisted,
and frozen at its own `1.0.0` on 2026-07-05. Its repository enforces the
freeze (`IsPackable=false`, no pack/push steps, `[Obsolete]` entry point).
This package is the sole go-forward ASP.NET Core integration.

---

*To God be the glory — 1 Corinthians 10:31.*
