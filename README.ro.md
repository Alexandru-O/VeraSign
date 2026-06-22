# MasterSTI / VeraSign

Prototip de dizertație pentru **semnătură electronică calificată la distanță**
construit pe **EU Digital Identity Wallet (EUDIW)** + **Cloud Signature
Consortium API v2**, cu profil PAdES baseline complet **B-B / B-T / B-LT / B-LTA**,
validare EU Trust List live și manifest de integritate per pagină
(anti-Shadow-Attack). Marca UI: **VeraSign**; namespace / soluție: `MasterSTI.*`.

## Postură conformitate — ce depășește industria

| Capability | Standard / referință | Postură VeraSign |
|---|---|---|
| KB-JWT key binding | RFC 7800 / SD-JWT VC §4.3 | Strict `cnf.jwk` verificat; fallback la cheia issuer-ului **gated** prin `Eudiw:AllowIssuerKeyKbFallback` (default `false`) |
| KB-JWT replay window | ARF §6.5.3 — ≤60 s recomandat | `Eudiw:KbJwtIatSkewSeconds` = 120 s (era 5 min înainte) |
| `sd_hash` compare | SD-JWT §4 | Fixed-time prin `CryptographicOperations.FixedTimeEquals` |
| `alg=none` | RFC 7519 §6.1 | Refuzat în producție; `AllowUnsignedJwt` doar în teste |
| GDPR data-minimisation | Art. 5(1)(c) + SD-JWT VC §6.2 | Verifier rejects orice disclosure în afara allowlist-ului (`family_name`, `given_name`, `email`) |
| PAdES baseline | ETSI EN 319 142-1 §5.2-§5.6 | Toate 4 nivelurile: B-B / B-T / B-LT / **B-LTA** (archive TS) |
| EU Trust List | ETSI TS 119 612 / LOTL | `TrustListProvider` ingest curated 15-TSP subset; matching Issuer DN pe `/verify` |
| Page-content integrity | (nu există standard) | **Wysiwys v1** — SHA-256 per pagină capturat la `embed`, re-verificat la `validate`; detectează Shadow Attacks (Mainka et al., USENIX 2020) |
| Wallet WSCD | ARF §6.6.4 | StrongBox EC P-256 cu r\|\|s raw JOSE; nu DER |
| TSA probe | RFC 3161 | Round-trip real către `timestamp.digicert.com` la 60 s, 7-day sparkline |
| Real-time pipeline | (proprietary) | SignalR push `dashboard-changed` + `FailedAtStage` per stage |
| Audit log | ETSI EN 319 401 §7.10 | Append-only `AuditEvents`; cross-doc viewer în Settings |
| Accessibility | EU Web Accessibility Directive | WCAG 2.2 AA semantic Dashboard (`<main>`, skip-link, focus-visible, reduced-motion, ARIA) |

## Ce demonstrează

- **OpenID4VP + SD-JWT VC** cu cheie de legare criptografică (`cnf.jwk` →
  KB-JWT semnat în StrongBox EC P-256). RFC 7515 / 7517 / 7519 / 7638 / 7800.
- **CSC API v2 remote QES** — cheia privată calificată **nu** părăsește
  HSM-ul QTSP-ului. PKCE pe `oauth2/authorize`, zeroize SAD imediat.
- **PAdES deferred signing** prin iText7 v9 — placeholder 32 KB, hash extern,
  embed CMS, LTV (OCSP + CRL) → B-LT, archive timestamp → **B-LTA** când
  `TsaUrl` configurat.
- **EU Trust List validation** — Issuer DN matched contra snapshot curat
  (15 QTSP-uri RO/DE/IT/NO/CH/PL/ES/FR/AT). Source `ec.europa.eu/tools/lotl/eu-lotl.xml`
  păstrat în snapshot pentru auditability.
- **Page-content manifest (Wysiwys v1)** — SHA-256 per pagină capturat la
  embed-time din PDF preparat, re-hash și comparare la validare. Per-page
  badge grid pe `/signed/{id}/validate` arată ce pagină a fost atinsă.
- **Mixed-level workflow** — același document poate cere QES de la un
  semnatar și AdES de la altul; per-recipient dropdown pe pagina Recipients.
- **Wallet fat** MAUI Android — key în `AndroidKeyStore` / StrongBox, KB-JWT
  ES256 cu r\|\|s raw (NU DER).
- **i18n RO + EN toggle** în topbar (sidebar + topbar + new-request).

## Tech stack

- **.NET 10** Vertical Slice Architecture — Minimal APIs + MediatR + EF Core
- **Blazor Server** (InteractiveServer, SignalR circuit) → API prin
  `HttpClient` numit + `ApiBearerHandler` JWT
- **MSSQL** 2022 (Docker) sau LocalDB
- **.NET MAUI 11** Android (iOS one-line) — fat wallet cu `IDeviceKeyService`
- **iText7 v9** PAdES + LTV + B-LTA archive timestamp; **BouncyCastle** ECDSA/RSA
- **xUnit + NSubstitute** — **71 teste**, lock-in pe SD-JWT key binding,
  EUTL parser, Wysiwys page manifest

## Quickstart

### Demo full-stack via PowerShell

```powershell
.\start-all.ps1 -Publish -Open
```

Cele trei servicii (API, Web, Mock QTSP) pornesc în ferestre `cmd` separate
ca exe-uri publish. Browserul se deschide pe `/welcome`.

Pentru Android emulator (10.0.2.2):

```powershell
.\start-all.ps1 -Public https://10.0.2.2:7001
```

#### Dispozitiv Android real pe Wi-Fi-ul laptopului

Pentru a rula APK-ul pe un telefon fizic conectat la aceeași rețea Wi-Fi
ca laptopul (in loc de emulator), folosește IP-ul LAN al laptopului in
ambele părți:

1. **Află IP-ul LAN al laptopului** (ex. `192.168.1.42`):

   ```powershell
   (Get-NetIPAddress -AddressFamily IPv4 -InterfaceAlias 'Wi-Fi').IPAddress
   ```

2. **Pornește stack-ul cu binding pe `0.0.0.0`** și setează `PublicBaseUrl`
   la IP-ul LAN — flag-ul `-Public` face exact asta (bind 0.0.0.0 + setează
   `Eudiw__PublicBaseUrl`, care e citit de `OpenId4VpService.CreateAuthorizationRequest`
   ca să reescrie QR-urile OID4VP cu URL-uri reachable de pe telefon):

   ```powershell
   .\start-all.ps1 -Public https://192.168.1.42:7001
   ```

3. **Retargetează wallet-ul către același IP LAN.** Defaultele
   compile-time din `mobile/MasterSTI.Wallet/Services/WalletConfig.cs`
   indică spre `10.0.2.2` (loopback emulator). Pentru un device real:

   - **Astăzi** (rebuild necesar): editează `WalletConfig.cs` și schimbă
     `ApiBaseUrl` / `QtspBaseUrl` la IP-ul LAN, apoi rebuild APK:

     ```powershell
     dotnet build mobile/MasterSTI.Wallet/MasterSTI.Wallet.csproj -f net10.0-android -t:Run
     ```

   - **După issue #15** (no-rebuild retargeting): editează
     `mobile/MasterSTI.Wallet/Resources/Raw/wallet.config.json` cu
     `{ "apiBaseUrl": "https://192.168.1.42:7001", "qtspBaseUrl": "https://192.168.1.42:7111" }`
     și restartează APK-ul — fără rebuild.

4. **Firewall Windows.** Permite inbound TCP `7001` (API), `7165` (Web)
   și `7111` (Mock QTSP) pe profilul **Private** (Wi-Fi domestic). Comandă
   PowerShell elevată one-shot:

   ```powershell
   New-NetFirewallRule -DisplayName 'VeraSign demo (LAN)' -Direction Inbound `
     -Protocol TCP -LocalPort 7001,7165,7111 -Action Allow -Profile Private
   ```

5. **Cert dev HTTPS.** Telefonul va respinge cert-ul self-signed al
   Kestrel-ului. APK-ul de DEBUG bypasează validarea (vezi
   `MauiProgram.cs` — `DangerousAcceptAnyServerCertificateValidator`),
   deci flow-ul OID4VP merge end-to-end. Pentru un demo "curat", instalează
   cert-ul dev pe telefon sau termină HTTPS la un reverse proxy (în afara
   scope-ului dizertației).

Activare archive timestamp (B-LTA) la demo:

```powershell
$env:TsaUrl = "http://timestamp.digicert.com"; .\start-all.ps1 -Publish -Open
```

### Demo via Docker

```bash
docker compose up --build
```

MSSQL + Mock QTSP + API + Web pe plain HTTP. Browser → `http://localhost:7165`.

### Credențiale demo

| Câmp | Valoare |
|---|---|
| Email | `admin@verasign.demo` |
| Parolă | `Demo!2025` |

Alternativ: login fără parolă prin EU Wallet — modal QR + simulator deep-link.

## Pipeline semnare — 5 etape

1. **Autentificare EUDIW** — OpenID4VP cu `state` + `nonce` server-generate
2. **Verificare SD-JWT** — RFC 7800 cnf.jwk, KB-JWT ES256, sd_hash canonical,
   allowlist disclosures (`family_name`/`given_name`/`email`)
3. **Consimțământ SAD** — biometric + PIN în wallet, OAuth2 PKCE pe CSC
4. **CSC signHash** — RSA-SHA256 prin QTSP, opțional timestamp TSA RFC 3161
5. **PAdES embed + LTV + archive TS** — placeholder fill, OCSP/CRL embed
   → B-LT; archive TS → **B-LTA** când `TsaUrl` setat. Manifest pagini
   capturat din PDF-ul preparat înainte de embed.

Failure attribution per stage scrisă pe `SigningRequest.FailedAtStage`,
afișată ca badge roșu pe nodul afectat. SignalR `dashboard-changed` push
către grupul `dashboard:org:{orgId}` la fiecare tranziție.

## Validare semnătură — `/verify` + `/signed/{id}/validate`

Verificator surfacează 6 verdicte:

- **Integritate** — PAdES byte-range hash via iText `SignatureUtil`
- **Timestamp** — RFC 3161 prezent + data TSA
- **LTV** — DSS dict + OCSP/CRL embedded
- **Nivel PAdES** — derivat din PDF content: B-B / B-T / B-LT / B-LTA
- **EU Trust List** — Issuer DN matched cu snapshot curat (15 QTSP-uri); pill
  verde cu numele TSP-ului + țara + data snapshot-ului
- **Integritate pe pagini** — per-page badge grid; pagini divergente
  marcate roșu; hash-urile stored vs re-computed afișate mono

## Securitate

- `SAD` nu este logat, nu este persistat, e zeroized imediat după `SignHashAsync`.
- Fallback authorization policy: orice endpoint cere autentificare; doar
  `POST /api/auth/login` și `/api/eudiw/{request-object,response}` sunt
  `[AllowAnonymous]`.
- Upload: extensie `.pdf` + magic bytes `%PDF-` + ≤ 50 MB + rate limiter 20 r/m.
- JWT signing key vine din user-secrets / env / `publish/api/appsettings.json` —
  sursa `src/**/appsettings.json` rămâne clean.
- KB-JWT verificat strict contra `cnf.jwk` când există; fallback la cheia
  issuer-ului default refuzat — opt-in via `Eudiw:AllowIssuerKeyKbFallback=true`.
- KB-JWT `iat` window 120 s (configurabil prin `Eudiw:KbJwtIatSkewSeconds`).
- Verifier respinge disclosure-uri în afara allowlist-ului `{family_name, given_name, email}` (GDPR data-minimisation).
- `alg=none` refuzat în producție; `AllowUnsignedJwt=true` doar în teste.

## Comenzi utile

```bash
dotnet build                                                # toate proiectele non-MAUI
dotnet test                                                 # 71 teste xUnit
dotnet ef database update --project src/MasterSTI.Api       # aplică migrațiile
dotnet ef migrations add <Name> --project src/MasterSTI.Api # nouă migrație
docker compose down -v && docker compose up --build         # reset DB + rebuild

# MAUI Android
dotnet build mobile/MasterSTI.Wallet/MasterSTI.Wallet.csproj -f net10.0-android -t:Run

# A11y audit (stack must be up):
cd tests/MasterSTI.A11y && npm install && npm run install:browsers && npm test
```

## Limite recunoscute (out-of-scope pentru prototip)

Le numesc explicit ca să nu fie surpriză în viva:

- **QTSP real** — folosim Mock QTSP cu RSA-2048 self-signed. Production
  ar cere HSM EAL4+ și certificat din EU Trusted List.
- **SAD SCAL2 binding** — Mock QTSP returnează SAD hardcoded; production
  ar cere SAD care leagă credențial+hashes+user conform ETSI TS 119 432 §7.3.
- **EU LOTL live walk** — folosim snapshot curat (15 TSP) bundle-uit; production
  ar parsa LOTL XML complet de la `ec.europa.eu/tools/lotl/eu-lotl.xml` + per-country TSL pointers.
- **Wallet recovery** — ARF gap deschis (referință impl EC nu are nici el).
- **Wysiwys v2 raster** — v1 acoperă content-stream; v2 ar adăuga raster hash
  capturat pe client la consimțământ, defensiv contra divergenței font-substitution.
- **JWT HS256 simetric** — production ar folosi RS256/ES256 cu cheie în KeyVault.

## Licență

Cod sursă educațional. Folosit exclusiv în scopul susținerii dizertației.
