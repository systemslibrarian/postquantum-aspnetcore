# API stability

`PostQuantum.AspNetCore` is **preview software** (`0.x.y-preview.z`).
This document spells out exactly what that promises and what it doesn't,
so you can decide whether to depend on a specific surface before `1.0`.

## During the `0.x` preview

**The public API may change between any two `0.x` releases.** Renames,
removed members, changed signatures, and altered defaults are all in
scope. We try to minimise churn — most v0.x → v0.x bumps are additive —
but the only stability guarantee during preview is that *each release
ships clean*: zero compiler warnings, all tests green, format-clean,
SBOM and provenance attestation attached.

What "minimising churn" means in practice:

- **`Add…` extension methods (`AddPostQuantumJwtBearer`, `AddPostQuantumJwtKeyRing*`)
  are stable in shape.** A new overload may appear; existing ones won't
  silently change behaviour.
- **`PostQuantumJwtBearerDefaults.AuthenticationScheme` and `BearerPrefix`
  string values are stable.** Changing either would break every
  `[Authorize(AuthenticationSchemes = …)]` attribute in a downstream app.
- **`PqJwtValidationParameters` (from the engine library) is the
  authoritative validator contract.** This package never copies its
  fields; if the engine adds a new validation rule, it propagates
  through automatically.
- **The HTTP key-directory wire format is stable.** Adding new fields
  to entries is non-breaking; renaming or removing existing fields
  (`kid`, `alg`, `key`) would be.
- **Event-hook shapes can grow new properties on context objects**
  without breaking existing subscribers. New events may be added.
  Removing or renaming an existing event is a breaking change.

What's *not* guaranteed:

- Internal type names, namespaces under `PostQuantum.AspNetCore.Internal`,
  source-generated logger message text, log event IDs above a stable
  range (the current Event IDs 1-8 are stable; 9+ are not yet).
- The exact wording of exception messages.
- The exact `WWW-Authenticate` parameter ordering (the header is
  parseable per RFC 7235 either way).

## How to depend

If your consuming code touches anything outside the "stable in shape"
list above and you need a stronger commitment, pin to an **exact
version** rather than a range:

```xml
<PackageReference Include="PostQuantum.AspNetCore"
                  Version="[0.4.0-preview.1]" />
```

If you can tolerate additive changes, pin to a minor range:

```xml
<PackageReference Include="PostQuantum.AspNetCore"
                  Version="0.4.*-*" />
```

(The wildcard `*-*` is necessary to match preview suffixes.)

## What counts as a breaking change

We follow the [.NET runtime team's definition](https://learn.microsoft.com/en-us/dotnet/core/compatibility/library-change-rules)
of public-API breaks. Specifically:

- Removing or renaming any `public` or `protected` member.
- Changing the signature, accessibility, or return type of any
  `public`/`protected` member.
- Adding a new abstract member to a public unsealed class.
- Tightening the `[Required]` set of a public type's members.
- Changing the runtime behaviour of an existing API in a way a correct
  consumer would notice (e.g. switching a documented exception type).

**Adding** new public types, new overloads, new optional parameters,
new event hooks, new options-class properties with defaults — not
breaking. Source-generated logger messages and log levels — not
breaking.

## The `1.0` commitment

When `PostQuantum.AspNetCore` reaches `1.0.0`, the public surface
freezes. From that point on:

- Patch (`1.0.x`) releases ship **only** bug fixes and security
  updates. No new APIs, no new defaults, no behavioural changes
  beyond making bugs match their docs.
- Minor (`1.x.0`) releases may add new APIs (additive only) and adjust
  defaults *only* where the existing default is harmful (e.g. a
  security weakening). Adjustments are called out in CHANGELOG with
  an explicit migration note.
- Major (`2.0.0`) releases may break the public surface, with a
  documented migration guide for each break.

We also commit, at 1.0, to maintaining the previous major version for
**at least 12 months** with security updates after a new major ships,
so consumers have time to migrate.

## What blocks `1.0`

In dependency order:

1. **External cryptographic audit of `PostQuantum.Jwt` (the engine).**
   The wire format and the validator are upstream. Until an auditor has
   reviewed them, neither this package nor any consumer of it should
   be in production.
2. **IANA registration of the `ML-DSA-65` / `X-Wing` algorithm
   identifiers** — or, alternatively, a written acceptance that the
   library is non-interoperable by design and that's fine.
3. **A production deployment running at non-trivial scale** with
   metrics and tracing wired up, surfacing real-world failure modes
   we haven't seen in unit tests yet.
4. **At least one external consumer migration** validating that
   `docs/MIGRATION.md` is correct end-to-end.

If you're evaluating this library and you'd like to be the production
deployment in point 3, please get in touch — the maintainer is interested
in walking through it carefully.

---

*To God be the glory — 1 Corinthians 10:31.*
