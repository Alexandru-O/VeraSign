# MasterSTI.A11y — automated WCAG 2.2 AA audit

Headless Chromium + axe-core scan of every key VeraSign page. Fails on any
finding with `impact: critical` or `serious`. Moderate / minor issues are
logged but non-blocking (Blazor scaffolding produces a lot of false noise
otherwise).

## Quick run (local)

```powershell
# 1. Bring the stack up (any of these works):
docker compose up -d --build
# or:
.\start-all.ps1 -Publish -Open

# 2. Install the harness once:
cd tests\MasterSTI.A11y
npm install
npm run install:browsers

# 3. Scan:
npm test
```

Override the base URL via `WEB_BASE_URL` if Web is bound elsewhere:

```powershell
$env:WEB_BASE_URL = "https://localhost:7165"
npm test
```

## Routes covered

| URL | Auth |
|---|---|
| `/welcome` (Landing) | anonymous |
| `/login`             | anonymous |
| `/dashboard`         | authenticated |
| `/documents`         | authenticated |
| `/verify`            | authenticated |

The authenticated flow seeds session via the demo credentials baked into
`DbInitializer` (`admin@verasign.demo` / `Demo!2025`).

## CI

`.github/workflows/a11y.yml` spins docker compose, waits for the Web
healthcheck, then runs `npm test`. Failure uploads the JSON report
(`a11y-report.json`) + Playwright traces as workflow artifacts.

## Adding a new route

1. Append a `test('...', async ({ page }) => { ... })` block in
   `tests/a11y.spec.ts`.
2. Reuse `expectAxeClean(page, '/route')` so the blocking-violations
   contract stays uniform.
