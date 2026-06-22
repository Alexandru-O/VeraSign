# Contributing to VeraSign

Thanks for taking a look. VeraSign started as a master's dissertation prototype
for **pixel-bound qualified e-signatures on the EU Digital Identity Wallet**.
Contributions that push it toward production-grade standards compliance are very
welcome.

## Quick start

```powershell
.\start-all.ps1 -Publish -Open   # API + Web + Mock QTSP, browser opens on /welcome
```

or, cross-platform:

```bash
docker compose up --build        # MSSQL + Mock QTSP + API + Web on http://localhost:7165
```

Demo login: `admin@verasign.demo` / `Demo!2025`.

## Before opening a PR

- **Build:** `dotnet build` (all non-MAUI projects).
- **Test:** `dotnet test` — keep the suite green; add tests for new behavior.
  Existing locks live on SD-JWT key binding, the EUTL parser, and the WYSIWYS
  page manifest.
- **Style:** match the surrounding code. This repo favors vertical-slice
  features (Minimal API endpoint + MediatR handler + EF Core) — follow the
  existing slice layout rather than introducing new layers.
- **Surgical changes:** touch only what the change needs. Don't reformat or
  refactor adjacent code in the same PR.

## Good first issues

- New-language i18n strings (currently RO + EN).
- Additional EU Trust List TSP entries in the curated snapshot.
- Wallet retargeting without rebuild (`wallet.config.json`).

## Reporting bugs

Open an issue with repro steps and, for the signing pipeline, the
`SigningRequest.FailedAtStage` value shown on the dashboard node.

Security issues: see [SECURITY.md](SECURITY.md) — do not file them publicly.
