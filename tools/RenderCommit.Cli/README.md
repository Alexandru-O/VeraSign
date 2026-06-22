# `tools/RenderCommit.Cli/`

Headless reference renderer for the
[Pixel-Bound QES](../../docs/adr/0008-pixel-bound-qes.md) determinism spike.

The CLI is the bit-identity authority for v1 per the ADR's
"Cross-toolchain caveat" -- the MAUI wallet UI displays bitmaps for the
user, but the value committed inside the PAdES signature dictionary is the
root this CLI computes. Same binary, same fixed DPI, same Merkle
personalisation as the future `RenderCommitmentService`.

## Subcommands

```
render-commit render <pdfPath> [--locale ro-RO] [--pdfium-root tools/pdfium-v1]
    Emits JSON: { profile, algo, dpi, pageCount, locale, root, leaves[],
                  pdfiumBinarySha256 }.

render-commit generate-fixtures <outDir>
    Writes 01-romanian-diacritics.pdf, 02-ocg-hidden-amount.pdf,
    03-transparent-overlay.pdf, 04a-lta-base.pdf, 04b-lta-refreshed.pdf.
```

## Build

```bash
dotnet build tools/RenderCommit.Cli/RenderCommit.Cli.csproj
```

The CLI is **not** part of `MasterSTI.slnx` -- it is a sidecar tool.
Building the solution does not pull it in; CI gates that need it must build
it explicitly.

## Run (Linux, recommended)

The spike is Linux-only per ADR-0008 §"Cross-toolchain caveat". On Windows
the CLI builds and `generate-fixtures` works (iText is cross-platform), but
`render` fails until a `win-x64` slot is added to
`tools/pdfium-v1/` and its sha256 pinned -- which is out of scope for the
v1 spike.

```bash
docker build -t mastersti/pixelcommit-cli -f tools/RenderCommit.Cli/Dockerfile .
docker run --rm mastersti/pixelcommit-cli /usr/local/bin/spike-harness.sh
```

## Files

| File | Role |
|---|---|
| `Program.cs` | Subcommand dispatcher, JSON output. |
| `RenderCommitment.cs` | Per-page render -> leaf hash -> Merkle root. |
| `MerkleRoot.cs` | RFC 6962-personalised tree (`0x00` leaf, `0x01` node, odd-leaf duplicates last). |
| `RenderProfiles.cs` | `(profile, sha256)` registry + assertion. Bootstrap mode prints observed sha256 and fails until pinned. |
| `PdfiumLoader.cs` | `NativeLibrary.SetDllImportResolver` for the pinned binary. |
| `Fixtures/FixtureAuthor.cs` | Three PoC PDFs probing diacritics / OCG / transparency. |
| `Fixtures/LtvRefreshFixture.cs` | Base + refreshed pair for the LTV-refresh side-test. |
| `Dockerfile` | Spike image (.NET runtime + pinned libpdfium + Noto fonts + jq). |
| `spike-harness.sh` | 5-run paired determinism check + LTV side-test. |
