# Mutation testing

[Stryker.NET](https://stryker-mutator.io/) is configured but does not
yet produce a usable mutation score on this codebase. This document
records what we tried, what's blocking, and how a future run should
look.

## Current state

- **Configuration**: [`stryker-config.json`](../stryker-config.json) at
  the repo root targets `src/PostQuantum.AspNetCore/PostQuantum.AspNetCore.csproj`
  against `tests/PostQuantum.AspNetCore.Tests/PostQuantum.AspNetCore.Tests.csproj`.
- **Tool version tested**: `dotnet-stryker` 4.14.2.
- **Result**: 377 mutants created. **0 killed, 0 timeout, 377 survived**.
  Stryker reports "Stryker was unable to calculate a mutation score."

The pattern is consistent with the test runner not actually executing
the test suite against each mutant — when no test runs, every mutant
trivially survives. The same configuration runs (passes) green on
simpler test projects we've sanity-checked separately.

## Suspected root cause

The test project uses `xunit.v3` (2.0.2) plus a mix of integrations
(NSubstitute, FsCheck was tried and dropped, source-generated test
context, MeterListener subscriptions, `Microsoft.AspNetCore.Mvc.Testing`).
At least one of these is not yet supported by Stryker.NET's test-runner
discovery. We tried:

- `--coverage-analysis off` (in `stryker-config.json`) — no change.
- Loosening the `mutate` filter (was excluding too much; relaxed to
  only exclude `AssemblyInfo.cs` and `Logging.cs`).
- Trimming the `ignore-methods` filter (the original glob `*Logging*`
  was matching too greedily).

None of these moved the needle from 0 killed.

## What we did instead

In place of a mutation score, the test suite already covers the
correctness contract via:

- **Adversarial / fuzz coverage** — `FuzzTests` runs 2000 random
  byte sequences against the full pipeline per case (4 cases).
  Caught two real fail-open bugs (`PqJwtException` leak,
  `FormatException` leak) that survived all other tests.
- **Property-flavoured tests on the small helpers** —
  `HeaderEncodingProperties` runs 1000 deterministic-seeded
  iterations per property.
- **End-to-end integration tests** — `WarmupIntegrationTests` runs
  the full DI graph + a real `Host`.
- **Diagnostic-contract tests** — `DiagnosticsTests` exercises the
  Metrics + ActivitySource surface against real listeners.
- **66 tests total**, zero skips on PQ-capable hosts.

Mutation testing would catch a different class of issue (tests that
pass but don't actually verify what they claim to). The fuzz tests
caught the equivalents *they* could catch — fail-open bugs that survived
specific-case tests but couldn't survive 2000 random inputs.

## How a future run should look

When Stryker.NET adds full xunit-v3 + NSubstitute support (or when
we work around it with a smaller adapter project):

```bash
dotnet tool install --global dotnet-stryker
dotnet stryker
```

Target: mutation score **≥ 70%** on the library project. The current
threshold in `stryker-config.json` is `{ high: 80, low: 70, break: 60 }`
— `break: 60` means a future CI integration would fail builds whose
score falls below 60%.

Tracked in `KNOWN-GAPS.md`.

---

*To God be the glory — 1 Corinthians 10:31.*
