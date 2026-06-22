# RenderDivergence fixture corpus

Three hand-authored PoC PDFs that exercise the shadow-attack surface area named
in ADR-0008 §"Test fixtures". Each PDF ships with a sibling `.expected.json`
naming the attack class, expected disputed pages, and a documented mutation
recipe a reviewer can replay to drive a Disputed verifier output.

| Fixture | Attack class | Mladenov et al. NDSS 2021 ref |
|---|---|---|
| `01-romanian-diacritics.pdf` | font-fallback divergence | §3.2 |
| `02-ocg-hidden-amount.pdf` | OCG-layer toggle | §3.1 (`hide`) |
| `03-transparent-overlay.pdf` | annotation-visibility tamper | §3.3 (`replace`) |

The 04a/04b LTV-refresh pair lives in `tools/RenderCommit.Cli/Fixtures/` and is
emitted into the same output directory by `generate-fixtures` — it covers a
different side-test (does the per-page commitment survive a legitimate archive-
timestamp refresh) and is not part of the divergence corpus.

## Regenerating

```bash
dotnet run --project tools/RenderCommit.Cli -- generate-fixtures tests/Fixtures/RenderDivergence
```

The fixture author is `tools/RenderCommit.Cli/Fixtures/FixtureAuthor.cs`. iText
PDF output is **not** byte-deterministic across runs (creation date + document
`/ID`), so the committed binaries here are the source of truth for any test
asserting a specific R. Regenerate only when the fixture content itself
changes; do not regenerate to "refresh" the corpus.

## Running the disputes

The committed Verified-vs-Disputed proof for these fixtures runs against the
linux-x64 PdfiumPinned-v1 binary in docker compose:

```bash
docker compose up -d
bash tools/step4-smoke.sh        # plain + pixel-bound + disputed variants
```

`step4-smoke.sh` exercises the end-to-end /api/signing/prepare ->
/api/signing/{id}/sign -> /api/signed-documents/{id}/validate path. The
fixtures here are the dissertation eval-chapter corpus, not unit-test inputs —
unit tests over the verifier seam live in
`tests/MasterSTI.UnitTests/Rendering/RenderVerificationBuilderTests.cs` and
`RenderDivergenceFixtureTests.cs`.
