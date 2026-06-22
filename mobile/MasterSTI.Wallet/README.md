# MasterSTI Wallet — Android emulator networking guide

## How the emulator reaches the Windows host

Inside an Android emulator, `10.0.2.2` routes to the loopback interface of the Windows host.
`WalletConfig` defaults to `https://10.0.2.2:7001` (API) and `https://10.0.2.2:7111` (Mock QTSP).

The services must bind on `0.0.0.0` (not just `localhost`) for this to work.
Use the `-Public` flag of `start-all.ps1` to enable this:

```powershell
.\start-all.ps1 -Public https://10.0.2.2:7001
```

This also sets `Eudiw__PublicBaseUrl=https://10.0.2.2:7001` on the API process, so QR codes
encode `client_id` and `response_uri` with the emulator-resolvable address.

---

## Dev certificate trust on the emulator

The .NET dev HTTPS certificate is self-signed and is not trusted by Android by default.
Choose one of the two options below.

### Option A — Install the dev cert in the emulator (proper TLS)

1. Export the cert as PEM:
   ```powershell
   dotnet dev-certs https -ep devcert.cer --format PEM
   ```

2. Push it to the running emulator and install it:
   ```powershell
   # Push to a writable location on the device
   adb push devcert.cer /sdcard/Download/devcert.cer
   ```

3. On the emulator:
   - Open **Settings** → **Security** (or **Security & Privacy**) → **Encryption & credentials**
     → **Install a certificate** → **CA certificate**.
   - Pick `/sdcard/Download/devcert.cer`.
   - Accept the warning about monitoring. The cert will appear under "User credentials".

4. Remove `devcert.cer` from your machine after installation (it contains only the public key,
   but good hygiene).

### Option B — DEBUG-only bypass (emulator only, never ship)

`MauiProgram.cs` registers the named HttpClient `"wallet"` with:

```csharp
#if DEBUG
handler.ServerCertificateCustomValidationCallback =
    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#endif
```

This bypass is **compiled out of Release builds** (`#if DEBUG`). It is acceptable for running
on an emulator where you control both endpoints. **Remove or audit this before deploying to
a real device or production environment.**

---

## Full demo checklist

### Prerequisites

| Item | Command |
|---|---|
| Android emulator running | Open via Android Studio or `emulator -avd <name>` |
| API + QTSP accepting 0.0.0.0 | `.\start-all.ps1 -Publish -Public https://10.0.2.2:7001` |
| Windows firewall | Allow inbound TCP 7001 and 7111 (or temporarily disable for testing) |

### Step-by-step

1. **Start services** on the Windows host:
   ```powershell
   cd C:\Work\Repos\MasterSTI
   .\start-all.ps1 -Publish -Public https://10.0.2.2:7001
   ```

2. **Deploy the wallet** to the emulator:
   ```powershell
   dotnet build mobile/MasterSTI.Wallet/MasterSTI.Wallet.csproj -f net10.0-android -t:Run
   ```
   Or deploy from Visual Studio / Rider with the Android emulator selected as target.

3. **Enroll** — on the emulator, tap "Generate device key and enroll".
   The wallet generates an EC P-256 key in the Android Keystore, POSTs the public JWK to
   `https://10.0.2.2:7111/eudiw/issue-pid`, and stores the returned SD-JWT in SecureStorage.

4. **Upload a PDF** — in the browser on the host, navigate to `https://localhost:7165/documents/upload`.

5. **Start signing** — go through the signing flow until the wallet-auth page shows a QR code.
   The QR payload will encode `response_uri=https://10.0.2.2:7001/api/eudiw/response`.

6. **Scan the QR** — switch to the emulator wallet; the camera view opens automatically.
   Point it at the QR on the browser.

7. **Approve** — the consent screen shows the verifier identity and requested claims.
   Tap "Approve"; the wallet builds the VP token (KB-JWT signed with the device key) and
   POSTs it to the API.

8. **Continue signing** — back in the browser, the page should advance to the CSC credentials
   step. Complete the signing flow as normal.

---

## Known emulator limitations

- **StrongBox** is not available in emulators; the key is TEE-backed (software keystore).
  `IsHardwareBackedAsync()` returns `false` — expected.
- **Fingerprint** may not be enrolled in a fresh emulator image.
  The wallet skips the biometric prompt gracefully if no authenticator is registered.
  To enroll: emulator Settings → Security → Fingerprint (or use `adb -e emu finger touch 1`
  to simulate a touch in Extended controls → Fingerprint).
- **Camera** in the emulator uses a virtual scene. You can display the QR on a second monitor
  and point the emulator camera at it, or use the "Virtual scene" camera setting and inject
  a custom image.
