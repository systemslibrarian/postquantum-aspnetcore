# Contributing

Thanks for taking a look. `PostQuantum.AspNetCore` is preview software
that handles authentication — the bar for changes is correspondingly
high. This document is the short version of "how to land a PR that ships."

## Before you start

- **Read [`CLAUDE.md`](CLAUDE.md).** That's the engineering charter:
  honesty over polish, fail-closed always, no rolled-your-own crypto,
  native BCL first, small surface.
- **Read [`SECURITY.md`](SECURITY.md) and [`KNOWN-GAPS.md`](KNOWN-GAPS.md).**
  They name what's in scope and what isn't. A PR that contradicts either
  needs to update them in the same change.
- **For security-sensitive ideas, file a private advisory first.** GitHub's
  *Report a vulnerability* button is the right channel — please don't
  open a public issue for a credible exploit.

## Local workflow

```bash
dotnet build         # zero warnings, treats compiler warnings as errors
dotnet test          # 31+ tests, zero skips on PQ-capable hosts
dotnet format        # apply the .editorconfig style
```

The CI on every push runs:

1. `dotnet build -c Release` on Ubuntu **and** Windows.
2. `dotnet test -c Release` on both platforms. Windows is the
   PQ-required lane — it fails the build if any `[PqcFact]` test reports
   skipped.
3. A Linux-PQ-required job that installs OpenSSL 3.5+ via conda-forge,
   pins `LD_LIBRARY_PATH` at it, and asserts zero skips.
4. `dotnet pack -c Release` to catch packaging regressions.

If your PR can't pass all four of these locally on the matching
platform, it won't pass CI.

## What changes need

| Change kind | Tests required | Docs required |
|---|---|---|
| Bug fix in handler/key ring | Failing test that becomes passing | Note in CHANGELOG `[Unreleased]` |
| New public API | Tests for the happy path and one fail-closed case | XML doc on every public member; README example; CHANGELOG note |
| Security-sensitive change | Test that locks the fail-closed contract | Update to `SECURITY.md` or `KNOWN-GAPS.md` as appropriate |
| Documentation only | — | — |
| Build / CI infra | — | CHANGELOG note if it changes consumer experience |

## Style

- **`dotnet format` is authoritative.** The `.editorconfig` is the spec;
  PRs that fight it will be rejected by the CI formatter check.
- **XML doc comments on every public member.** `GenerateDocumentationFile`
  is on; missing docs are warnings → errors.
- **No suppressed warnings without a comment explaining why.**
- **No new third-party runtime dependencies** without a written
  justification in `SECURITY.md`.

## Test discipline

- Tests follow xunit's `Method_Scenario_Expected` naming. `CA1707` is
  suppressed in the test project for that reason.
- Tests that require the native ML-DSA primitives use `[PqcFact]`, which
  skips with a reason when the runtime lacks them. **Never let a crypto
  test silently pass when the path didn't actually execute.**
- The integration suite uses `Microsoft.AspNetCore.Mvc.Testing` /
  `TestServer`. Reach for a real pipeline before reaching for a mock.

## Commit hygiene

- Conventional, descriptive subject lines (no leading "fix: ", no all-caps).
  "Ship 0.3.0-preview.1" beats "fix bugs."
- One logical change per commit, but feel free to land a refactor + the
  feature that needed it together if they're tightly coupled.
- The bot-style `Co-Authored-By` line is fine if you used AI assistance;
  leaving it off is also fine.

## Cutting a release

Tag-driven. The release workflow runs on `v*` tags, asserts the tag
matches the `<Version>` in the csproj, builds, packs, attaches a
CycloneDX SBOM + SHA256SUMS + build-provenance attestations, then waits
on the `nuget-publish` environment gate for an approver before pushing
to nuget.org. The version-sync script (`scripts/check-version-sync.sh`)
runs in CI on every push to catch csproj/README/CHANGELOG drift before
a tag is even considered.

---

*To God be the glory — 1 Corinthians 10:31.*
