# `tools/pdfium-v1/linux-x64/`

Pinned PDFium native binary for the `PdfiumPinned-v1` render profile.
Loaded at runtime by [`PdfiumLoader`](../../RenderCommit.Cli/PdfiumLoader.cs)
via `NativeLibrary.SetDllImportResolver` and asserted against the sha256 in
[`RenderProfiles`](../../RenderCommit.Cli/RenderProfiles.cs) per
[ADR-0008](../../../docs/adr/0008-pixel-bound-qes.md).

## What lives here

A single file: `libpdfium.so` (6 007 480 bytes, sha256
`8f67fac92554e4a6ab57f7d4f6a3d6974b1646373e0d314d90694738941c040c`),
extracted from bblanchon/pdfium-binaries `chromium/7678` release asset
`pdfium-linux-x64.tgz`. Full provenance and bump checklist live in
[`docs/render-profiles.md`](../../../docs/render-profiles.md).

## Reproduce

```bash
TAG="chromium%2F7678"
BASE="https://github.com/bblanchon/pdfium-binaries/releases/download/${TAG}"

curl -fsSL -o pdfium-linux-x64.tgz   "${BASE}/pdfium-linux-x64.tgz"
curl -fsSL -o pdfium-attestation.json "${BASE}/pdfium-attestation.json"

# SLSA-attested sha256 for pdfium-linux-x64.tgz must equal:
#   80ff74fda755237de1df2feda6972aafbd82828be23836093c5708063c815af8
sha256sum pdfium-linux-x64.tgz

tar -xzf pdfium-linux-x64.tgz lib/libpdfium.so
mv lib/libpdfium.so .; rmdir lib
sha256sum libpdfium.so   # must equal the value pinned in RenderProfiles.cs
rm pdfium-linux-x64.tgz pdfium-attestation.json
```

## Out of scope for v1

- `win-x64/pdfium.dll` and `android-x86_64/libpdfium.so` slots remain
  empty until separate spikes validate them — see ADR-0008
  §"Cross-toolchain caveat".
