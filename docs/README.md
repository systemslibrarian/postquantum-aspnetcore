# docs/

## Start here

- [**GETTING-STARTED.md**](GETTING-STARTED.md) — Zero to working PQ
  API in 10 minutes. The canonical "first hour" walkthrough.
- [**RECIPES.md**](RECIPES.md) — Copy-paste-able solutions to 13
  common scenarios: Redis replay protection, OpenTelemetry,
  multi-scheme, SignalR, multi-tenant, Swagger, Docker/K8s.
- [**FAQ.md**](FAQ.md) — Should I use this in production? How big are
  tokens? Does this work with Auth0? — plus 15+ more.

## Migrating

- [**MIGRATION.md**](MIGRATION.md) — Diff-style guide from
  `PostQuantum.Jwt.AspNetCore` (the engine repo's companion).

## Operations & security

- [**PRODUCTION-CHECKLIST.md**](PRODUCTION-CHECKLIST.md) — What to
  verify before user traffic hits.
- [**DIAGNOSTICS.md**](DIAGNOSTICS.md) — "Why is my token being
  rejected?" — top-to-bottom debugging guide.
- [**PERFORMANCE.md**](PERFORMANCE.md) — Benchmark methodology and
  what to expect (post-quantum is measured in milliseconds, not
  microseconds).
- [**API-STABILITY.md**](API-STABILITY.md) — Public-surface stability
  promise during preview and the `1.0` commitment.
- [**MUTATION-TESTING.md**](MUTATION-TESTING.md) — Stryker.NET
  configuration and honest record of why the baseline doesn't yet
  produce a usable score.

## ADRs (architecture decision records)

- [**adr/0001-no-algorithm-agility.md**](adr/0001-no-algorithm-agility.md) —
  The deliberate single-suite decision for the `0.x` series.

## Audits

- [**audits/**](audits/) — Independent reviews of the codebase,
  preserved as a transparency trail.

## Where the high-traffic references live

- [`../README.md`](../README.md) — Overview, usage, defaults, API
  surface.
- [`../SECURITY.md`](../SECURITY.md) — Threat model and security
  posture.
- [`../KNOWN-GAPS.md`](../KNOWN-GAPS.md) — Honest list of current
  limitations.
- [`../CLAUDE.md`](../CLAUDE.md) — Engineering conventions.
- [`../CHANGELOG.md`](../CHANGELOG.md) — Release notes.
- [`../CONTRIBUTING.md`](../CONTRIBUTING.md) — How to land a PR.
