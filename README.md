# VeraSign

**Pixel-bound qualified e-signatures on the EU Digital Identity Wallet.**

> What you see is what you sign. Every page is hashed at sign-time and
> re-verified at validation — so a document can't silently change after you
> approve it. This kills PDF **Shadow Attacks** (Mainka et al., USENIX 2020),
> which classic PAdES signatures don't catch.

VeraSign is a dissertation prototype that wires the **EU Digital Identity Wallet
(EUDIW)** to **remote Qualified Electronic Signatures (QES)** via the **Cloud
Signature Consortium (CSC) API v2**, with full PAdES baseline
(**B-B / B-T / B-LT / B-LTA**), live EU Trust List validation, and per-page
content integrity (WYSIWYS).

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Tests](https://img.shields.io/badge/tests-71%20xUnit-success)
![PAdES](https://img.shields.io/badge/PAdES-B--B%20%7C%20B--T%20%7C%20B--LT%20%7C%20B--LTA-blue)

🇷🇴 Versiunea în română: [README.ro.md](README.ro.md)

## Demo

End-to-end signing from the Blazor web app:

https://github.com/Alexandru-O/VeraSign/raw/main/docs/media/web-demo.mp4

<sub>More clips below: <a href="#signing-pipeline--5-stages">wallet auth</a> · <a href="#signature-validation----verify--signedidvalidate">validation</a>.</sub>

## Why it's different

VeraSign pairs three things that are rarely seen together in one open codebase:

1. **EUDIW authentication** — OpenID4VP + SD-JWT VC with cryptographic key
   binding (`cnf.jwk` → KB-JWT signed in StrongBox EC P-256).
2. **Remote QES** — the qualified private key never leaves the QTSP's HSM
   (CSC API v2, PKCE, SAD zeroized immediately after use).
3. **Pixel-bound integrity (WYSIWYS v1)** — SHA-256 per page captured at embed,
   re-hashed at validation, surfaced as a per-page badge grid. Detects content
   that a plain PAdES signature would happily cover up.

## Compliance posture — where it goes beyond the baseline

| Capability | Standard / reference | VeraSign posture |
|---|---|---|
| KB-JWT key binding | RFC 7800 / SD-JWT VC §4.3 | Strict `cnf.jwk`; issuer-key fallback **gated** by `Eudiw:AllowIssuerKeyKbFallback` (default `false`) |
| KB-JWT replay window | ARF §6.5.3 (≤60 s recommended) | `Eudiw:KbJwtIatSkewSeconds` = 120 s |
| `sd_hash` compare | SD-JWT §4 | Fixed-time via `CryptographicOperations.FixedTimeEquals` |
| `alg=none` | RFC 7519 §6.1 | Rejected in production; `AllowUnsignedJwt` only in tests |
| GDPR data-minimisation | Art. 5(1)(c) + SD-JWT VC §6.2 | Verifier rejects any disclosure outside the allowlist (`family_name`, `given_name`, `email`) |
| PAdES baseline | ETSI EN 319 142-1 §5.2–§5.6 | All 4 levels: B-B / B-T / B-LT / **B-LTA** (archive TS) |
| EU Trust List | ETSI TS 119 612 / LOTL | `TrustListProvider` ingests a curated 15-TSP subset; Issuer-DN matching on `/verify` |
| Page-content integrity | (no standard exists) | **WYSIWYS v1** — SHA-256 per page; detects Shadow Attacks |
| Wallet WSCD | ARF §6.6.4 | StrongBox EC P-256, raw `r‖s` JOSE (not DER) |
| TSA probe | RFC 3161 | Live round-trip to `timestamp.digicert.com`, 7-day sparkline |
| Real-time pipeline | (proprietary) | SignalR `dashboard-changed` push + `FailedAtStage` per stage |
| Audit log | ETSI EN 319 401 §7.10 | Append-only `AuditEvents`, cross-document viewer |
| Accessibility | EU Web Accessibility Directive | WCAG 2.2 AA semantic dashboard |

## Tech stack

- **.NET 10**, Vertical Slice Architecture — Minimal APIs + MediatR + EF Core
- **Blazor Server** (InteractiveServer / SignalR) → API via named `HttpClient` + `ApiBearerHandler`
- **MSSQL 2022** (Docker) or LocalDB
- **.NET MAUI** Android fat wallet — `IDeviceKeyService`, `AndroidKeyStore` / StrongBox
- **iText7 v9** for PAdES + LTV + B-LTA archive timestamp; **BouncyCastle** ECDSA/RSA
- **xUnit + NSubstitute** — **71 tests** locking SD-JWT key binding, the EUTL parser, and the WYSIWYS manifest

## Quickstart

### Full stack (PowerShell)

```powershell
.\start-all.ps1 -Publish -Open
```

API, Web, and Mock QTSP launch as published exes in separate windows; the
browser opens on `/welcome`.

Enable the B-LTA archive timestamp for the demo:

```powershell
$env:TsaUrl = "http://timestamp.digicert.com"; .\start-all.ps1 -Publish -Open
```

### Full stack (Docker)

```bash
docker compose up --build
```

MSSQL + Mock QTSP + API + Web on plain HTTP → `http://localhost:7165`.

### Demo credentials

| Field | Value |
|---|---|
| Email | `admin@verasign.demo` |
| Password | `Demo!2025` |

Or log in passwordless via the EU Wallet — QR modal + deep-link simulator.

> Running on a real Android device over Wi-Fi (LAN IP, firewall, dev cert)?
> The Romanian README has the full step-by-step: [README.ro.md](README.ro.md).

## Signing pipeline — 5 stages

1. **EUDIW authentication** — OpenID4VP with server-generated `state` + `nonce`
2. **SD-JWT verification** — RFC 7800 `cnf.jwk`, KB-JWT ES256, canonical `sd_hash`, disclosure allowlist
3. **SAD consent** — biometric + PIN in the wallet, OAuth2 PKCE on CSC
4. **CSC signHash** — RSA-SHA256 via QTSP, optional RFC 3161 TSA timestamp
5. **PAdES embed + LTV + archive TS** — placeholder fill, OCSP/CRL embed → B-LT; archive TS → **B-LTA** when `TsaUrl` is set

Per-stage failure attribution is written to `SigningRequest.FailedAtStage` and
shown as a red badge on the affected node; SignalR pushes `dashboard-changed`.

EUDIW wallet authentication + SAD consent (MAUI Android):

https://github.com/Alexandru-O/VeraSign/raw/main/docs/media/wallet-demo.mp4

## Signature validation — `/verify` + `/signed/{id}/validate`

Six verdicts surfaced: **integrity** (PAdES byte-range), **timestamp**
(RFC 3161), **LTV** (DSS + OCSP/CRL), **PAdES level** (B-B → B-LTA), **EU Trust
List** (Issuer-DN matched to the curated 15-TSP snapshot), and **per-page
integrity** (badge grid; divergent pages flagged red, stored vs recomputed
hashes shown).

Verifier overview:

https://github.com/Alexandru-O/VeraSign/raw/main/docs/media/verify-overview.mp4

Full validation report (per-page grid, Trust List pill, PAdES level):

https://github.com/Alexandru-O/VeraSign/raw/main/docs/media/verify-report.mp4

## Recognized limits (out of scope for a prototype)

Named explicitly so there are no surprises:

- **Real QTSP** — uses a Mock QTSP with RSA-2048 self-signed; production needs an EAL4+ HSM and an EU-Trusted-List certificate.
- **SAD SCAL2 binding** — Mock QTSP returns a hardcoded SAD; production needs ETSI TS 119 432 §7.3 binding.
- **EU LOTL live walk** — uses a curated 15-TSP snapshot; production parses the full LOTL XML + per-country TSL pointers.
- **Wallet recovery** — open ARF gap.
- **WYSIWYS v2 raster** — v1 covers the content stream; v2 would add a client-side raster hash at consent time.
- **JWT HS256 symmetric** — production would use RS256/ES256 with a key in a vault.

## Contributing

PRs welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). Security issues: see
[SECURITY.md](SECURITY.md) (please report privately).

## License

[MIT](LICENSE) © 2026 Alex Ologu. Built as a master's dissertation prototype —
**not** for signing legally binding documents (see *Recognized limits*).
