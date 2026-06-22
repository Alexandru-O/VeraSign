#!/usr/bin/env bash
# ADR-0008 determinism spike harness.
#
# For each fixture PDF:
#   1. Run render-commit N times back-to-back (default N=5).
#   2. Extract .root from each JSON output via jq.
#   3. Pass if all N roots are bit-identical; fail loud otherwise.
#
# Run inside the spike Docker image (which carries the pinned PDFium binary
# at /app/pdfium-v1/) or on a host that has it at $PDFIUM_ROOT.
#
# Usage:
#   spike-harness.sh                                    # 5 runs per fixture
#   spike-harness.sh --runs 10                          # custom run count
#   spike-harness.sh --fixtures-dir /work/out/fixtures  # override fixtures
#
# Exit codes: 0 all-identical | 1 divergence | 2 setup error

set -euo pipefail

RUNS=5
FIXTURES_DIR=""
PDFIUM_ROOT="${PDFIUM_ROOT:-/app/pdfium-v1}"
RENDER_CMD=(dotnet /app/render-commit.dll)
WORK_DIR=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --runs)          RUNS="$2"; shift 2 ;;
    --fixtures-dir)  FIXTURES_DIR="$2"; shift 2 ;;
    --pdfium-root)   PDFIUM_ROOT="$2"; shift 2 ;;
    --render-cmd)    IFS=' ' read -r -a RENDER_CMD <<< "$2"; shift 2 ;;
    -h|--help)
      sed -n '2,18p' "$0"
      exit 0
      ;;
    *)
      echo "unknown arg: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "${FIXTURES_DIR}" ]]; then
  WORK_DIR="$(mktemp -d)"
  FIXTURES_DIR="${WORK_DIR}/fixtures"
  echo "==> generating fixtures into ${FIXTURES_DIR}"
  "${RENDER_CMD[@]}" generate-fixtures "${FIXTURES_DIR}"
fi

declare -a FIXTURES=(
  "01-romanian-diacritics.pdf"
  "02-ocg-hidden-amount.pdf"
  "03-transparent-overlay.pdf"
)

declare -a LTV_PAIR=(
  "04a-lta-base.pdf"
  "04b-lta-refreshed.pdf"
)

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required (install fonts-noto-core jq inside the image)" >&2
  exit 2
fi

FAIL=0
echo "==> determinism: ${RUNS} runs per fixture against ${PDFIUM_ROOT}"

for fx in "${FIXTURES[@]}"; do
  pdf="${FIXTURES_DIR}/${fx}"
  if [[ ! -f "${pdf}" ]]; then
    echo "  [SKIP] ${fx} -- not found" >&2
    continue
  fi

  first=""
  ok=1
  for ((i = 1; i <= RUNS; i++)); do
    root="$("${RENDER_CMD[@]}" render "${pdf}" --pdfium-root "${PDFIUM_ROOT}" | jq -r .root)"
    if [[ -z "${first}" ]]; then
      first="${root}"
    elif [[ "${root}" != "${first}" ]]; then
      ok=0
      echo "  [DRIFT] ${fx} run ${i}: ${root} (expected ${first})"
    fi
  done

  if [[ ${ok} -eq 1 ]]; then
    echo "  [OK]    ${fx}  ${first}"
  else
    echo "  [FAIL]  ${fx}"
    FAIL=$((FAIL + 1))
  fi
done

# LTV-refresh side-test: base vs refreshed must share the same root.
echo "==> LTV-refresh side-test"
base_pdf="${FIXTURES_DIR}/${LTV_PAIR[0]}"
refreshed_pdf="${FIXTURES_DIR}/${LTV_PAIR[1]}"
if [[ -f "${base_pdf}" && -f "${refreshed_pdf}" ]]; then
  base_root="$("${RENDER_CMD[@]}" render "${base_pdf}" --pdfium-root "${PDFIUM_ROOT}" | jq -r .root)"
  refreshed_root="$("${RENDER_CMD[@]}" render "${refreshed_pdf}" --pdfium-root "${PDFIUM_ROOT}" | jq -r .root)"
  if [[ "${base_root}" == "${refreshed_root}" ]]; then
    echo "  [OK]    LTV refresh preserves render root: ${base_root}"
  else
    echo "  [FAIL]  LTV refresh changed render root"
    echo "          base:      ${base_root}"
    echo "          refreshed: ${refreshed_root}"
    FAIL=$((FAIL + 1))
  fi
else
  echo "  [SKIP]  LTV pair not found" >&2
fi

if [[ -n "${WORK_DIR}" ]]; then
  rm -rf "${WORK_DIR}"
fi

if [[ ${FAIL} -gt 0 ]]; then
  echo "==> SPIKE FAILED (${FAIL} fixture[s] non-deterministic)"
  exit 1
fi

echo "==> SPIKE PASSED"
exit 0
