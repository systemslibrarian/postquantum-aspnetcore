# API stability

`PostQuantum.AspNetCore` is **stable** (`1.0.0`): the public surface is
frozen under SemVer, and the "1.0 commitment" section below is now in
force, not a promise about the future. This document spells out exactly
what that covers and what it doesn't.

## The stable surface

Every release ships clean — zero compiler warnings, all tests green,
format-clean, SBOM and provenance attestation attached — and from
`1.0.0` an accidental public-API break fails the pack itself
(`PackageValidationBaselineVersion` compares against the last published
version). The anchors of the surface:

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
list above and you need the strongest commitment, pin to an **exact
version**:

```xml
<PackageReference Include="PostQuantum.AspNetCore"
                  Version="[1.0.0]" />
```

If you can tolerate additive changes (the normal case), pin to the
major line:

```xml
<PackageReference Include="PostQuantum.AspNetCore"
                  Version="1.*" />
```

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

## The `1.0` commitment (in force from `1.0.0`)

At `1.0.0` the public surface froze. From that point on:

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

## How the `1.0` gates were resolved

Earlier revisions of this document listed four blockers to `1.0`. For
the record — because honesty about *how* a gate was cleared matters as
much as clearing it — here is what actually happened to each:

1. **External cryptographic audit of `PostQuantum.Jwt` (the engine)** —
   **deliberately waived, not satisfied.** The engine shipped its own
   `1.0.0` (2026-06-30) reframing the missing audit as a **permanent,
   documented limitation**: an unfunded project is unlikely to secure a
   formal review, and perpetual preview served no one. This package
   inherits that decision verbatim. No third party has reviewed either
   codebase, and none is scheduled — adopt only in controlled
   issuer/verifier systems where you accept that documented risk.
2. **IANA registration of `ML-DSA-65` / `X-Wing`** — resolved via the
   alternative the gate itself offered: **written acceptance that the
   library is non-interoperable by design.** That acceptance is stated
   in the README, `SECURITY.md`, and the production checklist. If IANA
   registration lands upstream someday, adopting the registered
   identifiers would be a MAJOR (wire-format) change.
3. **A production deployment at non-trivial scale** — **waived.** The
   preview line produced no such deployment, and holding a completed
   API hostage to a chicken-and-egg gate (nobody deploys previews of
   auth libraries; the library can't leave preview without a
   deployment) helped no one. The offer stands: if you want to be that
   deployment, get in touch — the maintainer will walk through it with
   you.
4. **At least one external consumer migration validating
   `docs/MIGRATION.md`** — **partially satisfied, honestly labelled.**
   The migration path was exercised end-to-end by the maintainer's own
   template migration in the engine repository (scaffold → build →
   issue → validate → tamper-reject against the published packages),
   not yet by an independent external consumer.

The version number is a SemVer commitment about the API surface. It is
not, and was never going to be, a substitute for the audit.

---

*To God be the glory — 1 Corinthians 10:31.*
