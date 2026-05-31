# Audits

Independent reviews of the codebase, preserved as a trail. Honesty over
polish: when reviewers find issues, the findings live in the repo, the
fixes land in the next release, and the audit document stays.

## Index

- **[2026-05-30 — ChatGPT review](2026-05-30-chatgpt-review.md).**
  Independent audit against `0.2.0-preview.1`. Identified five issues:
  stale verification keys never evicted, case-sensitive `Bearer` match,
  unknown-`kid` amplification path, `OnTokenValidated` exceptions
  escaping as 500s, and inconsistent `invalid_token` challenge header.
  Also flagged three test gaps (key removal eviction, repeated-unknown-`kid`
  throttling, throwing event hook). **All five issues and all three
  test gaps closed in `0.3.0-preview.1`.**

- **[2026-05-30 — Gemini review](2026-05-30-gemini-review.md).**
  Parallel review of the same baseline. Self-reported five fixes
  applied: atomic-snapshot cache swap with kid eviction, rate-limited
  unknown-`kid` refresh, case-insensitive `Bearer`, `OnTokenValidated`
  try/catch routing through `OnAuthenticationFailed`, conditional
  `invalid_token` challenge parameter. **All five fixes merged into
  `0.3.0-preview.1`.**

The fix trail for both reviews is in
[`CHANGELOG.md`](../../CHANGELOG.md)'s `0.3.0-preview.1` section,
matched 1:1 to the findings above.

## How to file an audit

If you've reviewed this codebase — even informally — and want your
findings preserved here, the format is:

- File name: `YYYY-MM-DD-<your-name-or-tool>-review.md`.
- Date the review.
- For each finding: what you saw, where (file:line), why it matters,
  and a suggested fix shape. Tests you'd like to see added are
  especially welcome.
- Open a PR adding the file and a short entry in this index.

Audits are landing-pad documents, not living docs — once the findings
are closed in a release, the audit document is frozen in time. If new
findings appear in the same area later, file a fresh audit; don't edit
the historical one.
