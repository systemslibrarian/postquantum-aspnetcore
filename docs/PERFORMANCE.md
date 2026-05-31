# Performance

How to measure, what to expect, and where the cost lives.

## Running the benchmarks

The benchmark suite lives at
[`benchmarks/PostQuantum.AspNetCore.Benchmarks`](../benchmarks/PostQuantum.AspNetCore.Benchmarks)
and uses [BenchmarkDotNet](https://benchmarkdotnet.org/).

```bash
# All benchmarks
dotnet run -c Release --project benchmarks/PostQuantum.AspNetCore.Benchmarks

# A specific class or method, by filter
dotnet run -c Release --project benchmarks/PostQuantum.AspNetCore.Benchmarks -- --filter '*Validate*'
dotnet run -c Release --project benchmarks/PostQuantum.AspNetCore.Benchmarks -- --filter '*KeyRing*'
```

Output is in `BenchmarkDotNet.Artifacts/results/`. Both Markdown
(`*-report-github.md`) and CSV reports are produced.

## What's measured

| Class                          | Question answered                                                  |
|--------------------------------|--------------------------------------------------------------------|
| `TokenValidationBenchmarks`    | What's the per-request cost of `PqJwtValidator.Validate` on a signed token? This is the dominant cost inside `PostQuantumJwtBearerHandler`. |
| `KeyRingBenchmarks`            | What's the cost of a hot-path key-ring resolve (cache hit)?         |

Both use `[MemoryDiagnoser]`, so allocation columns appear alongside
timing.

## What to expect

The numbers are environment-dependent; we don't bake them into this
document because they go stale fast and the *shape* matters more than
the absolute values:

- **Token validation is dominated by ML-DSA-65 signature verification.**
  Plan for it to be measured in **milliseconds**, not microseconds —
  that's the cost of post-quantum security versus classical (EdDSA /
  ECDSA at microseconds). If you can amortise validation across many
  requests in a session (e.g. by caching the validated principal
  yourself), do so.
- **Key-ring cache hits are essentially free** — one volatile read of
  the snapshot reference, one `Dictionary.TryGetValue`. Hot-path
  lookup time should be on the order of tens of nanoseconds with zero
  allocations.
- **Token size is the headline cost**, not validation time, for most
  deployments. ~4.5 KB signed, ~6.5 KB encrypted (see the README's
  *Operational tradeoffs* section in the engine repo for the full
  breakdown).

## CI hookup

The benchmark project is excluded from the default CI lanes
(`build-test`, `linux-pq-required`, `pack-verify`) because benchmark
runs are too slow and too environment-sensitive to gate PRs on. A
future workflow will run benchmarks on a self-hosted runner or a
labelled PR opt-in, comparing against a checked-in baseline so
regressions surface.

Until that lands, run benchmarks locally before merging
performance-sensitive changes and paste the relevant comparison into
the PR.

## What's NOT measured here

- **Engine-internal hot paths.** The X-Wing combiner, ML-KEM
  encapsulation, etc. live in `postquantum-jwt`; that repo is the right
  place for benchmarks on those.
- **End-to-end HTTP request throughput.** The Validate cost dominates,
  but the ASP.NET Core pipeline overhead (model binding, routing,
  authorization policy evaluation) is not captured here. Use
  `dotnet-counters` or `BenchmarkDotNet`-with-the-`TestServer` for
  that — but for most deployments, *validation* is the costly leg.
- **Cold-start key-ring fetches.** Network-bound; depends on your
  key-directory hosting. The
  `AddPostQuantumJwtKeyRingWarmup(...)` hosted service pulls the
  network cost into startup, off the hot path.

---

*To God be the glory — 1 Corinthians 10:31.*
