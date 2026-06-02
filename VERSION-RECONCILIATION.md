# Version Reconciliation — 1.0.0-preview.3

## This package's assigned version

`PostQuantum.AspNetCore` = **`1.0.0-preview.3`**

The companion package `PostQuantum.AspNetCore.RedisReplayCache` ships in
lockstep at the same version, as it has at every previous release in
this repository.

## Dependency constraints updated

| Package | Was | Now |
|---|---|---|
| `PostQuantum.Jwt` | `0.3.0-preview.1` | `1.0.0-preview.1` |

Constraints **not changed** because this repository does not reference them:

- `PostQuantum.Cryptography` (suite target `1.0.0-rc.1`)
- `PostQuantum.FileEncryption` (suite target `1.0.0-rc.1`)
- `PostQuantum.SecureChannel` (suite target `0.3.0-preview.1`)

## Maturity discipline

This package ships at `1.0.0-preview.3`. Its only PostQuantum.* dependency
is `PostQuantum.Jwt` at `1.0.0-preview.1` — both on the 1.0 preview line,
both honestly labelled preview. **This package does not advertise more
maturity than anything it depends on.** The `preview.N` suffix plus
`KNOWN-GAPS.md` plus `SECURITY.md` carry the maturity caveat; the leading
`1.0` is the suite's coordinated signal that the API surface is what it
expects to ship at 1.0 proper, not a claim that the cryptographic
construction has been externally audited.

The "preview / not independently audited" language in the README and
`SECURITY.md` is preserved verbatim; only the version number changed.

---

*To God be the glory — 1 Corinthians 10:31.*
