# Security Policy

VeraSign is a dissertation prototype for remote qualified electronic signatures
(QES) on the EU Digital Identity Wallet. It is **not** production-hardened — see
the "Recognized limits" section of the README (Mock QTSP, hardcoded SAD, curated
Trust List snapshot). Do not use it to sign legally binding documents.

## Reporting a vulnerability

Found a security issue? Please **do not** open a public issue.

- Use GitHub's **private vulnerability reporting** (Security tab → "Report a
  vulnerability"), or
- email the maintainer at the address on the GitHub profile.

Include reproduction steps and the affected component (API, Web, Wallet, or the
WYSIWYS / SD-JWT verification path). Expect an acknowledgement within a few days.

## Scope of interest

This project deliberately exercises high-assurance flows; reports touching these
are especially welcome:

- SD-JWT VC / KB-JWT verification (RFC 7515/7517/7519/7638/7800)
- OpenID4VP `state` / `nonce` / replay handling
- PAdES byte-range and LTV/DSS validation
- WYSIWYS per-page hash bypass (Shadow Attack variants)
- Disclosure allowlist / GDPR data-minimisation enforcement
