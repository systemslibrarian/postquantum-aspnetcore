---
name: Feature request
about: Propose a new public API or capability
title: ''
labels: enhancement
assignees: ''
---

## What you'd like to do

<!-- The user-facing scenario, not the implementation. -->

## Why the current API doesn't get you there

<!-- If you've already tried something, mention what and why it fell short. -->

## Sketch of the API (optional)

```csharp
// Roughly what would the call-site look like?
```

## Constraints to keep in mind

- This library is deliberately single-suite (see
  [docs/adr/0001-no-algorithm-agility.md](../../docs/adr/0001-no-algorithm-agility.md)).
  Proposals that hinge on algorithm agility will be redirected.
- Public API additions need XML docs, a happy-path test, and a CHANGELOG
  entry. If that scope feels heavy, an issue describing the *need* (and
  letting the maintainer propose the shape) is better than a PR.
