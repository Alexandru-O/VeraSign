#!/usr/bin/env bash
# ADR-0008 step 4 end-to-end smoke. Runs against the compose stack
# (mssql + qtsp + api on http://localhost:7001).
#
# Flow per signed PDF:
#   login -> upload -> set recipient -> send -> render-commit -> prepare(commit)
#   -> sign(auto-embeds) -> validate -> print RenderVerification.Status
#
# Variants:
#   plain                 -> NotPresent
#   pixel-bound           -> Verified
#   pixel-bound + mutate  -> Disputed
set -euo pipefail

API=http://localhost:7001
PDF=src/MasterSTI.Api/storage/templates/nda-confidentialitate.pdf
TOKEN=$(curl -sS -X POST "$API/api/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"email":"admin@verasign.demo","password":"Demo!2025"}' \
    | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
AUTH="Authorization: Bearer $TOKEN"
JSON="Content-Type: application/json"

upload() {
    curl -sS -X POST "$API/api/documents/upload" -H "$AUTH" \
        -F "file=@$PDF;type=application/pdf" \
        | python -c "import sys,json;print(json.load(sys.stdin)['documentId'])"
}

set_recipient() {
    local docId=$1
    curl -sS -X POST "$API/api/documents/$docId/recipients" -H "$AUTH" -H "$JSON" \
        -d '{"recipients":[{"id":null,"email":"admin@verasign.demo","name":"VeraSign Demo Admin","order":1,"level":"QES"}]}' \
        | python -c "import sys,json;print(json.load(sys.stdin)[0]['id'])"
}

set_fields() {
    local docId=$1; local recipientId=$2
    # Minimum field set so /send accepts the doc. Page 1, top-left widget.
    curl -sS -X PATCH "$API/api/documents/$docId/fields" -H "$AUTH" -H "$JSON" \
        -d "{\"fields\":[{\"id\":\"00000000-0000-0000-0000-000000000000\",\"type\":\"Signature\",\"page\":1,\"x\":0.05,\"y\":0.05,\"width\":0.25,\"height\":0.06,\"recipientId\":\"$recipientId\",\"recipientOrder\":1}]}" >/dev/null
}

send_doc() {
    local docId=$1
    curl -sS -X POST "$API/api/documents/$docId/send" -H "$AUTH" -H "$JSON" -d '{}' >/dev/null
}

render_commit() {
    local docId=$1
    curl -sS -X POST "$API/api/documents/$docId/render-commitment" -H "$AUTH" -H "$JSON" \
        -d '{"locale":"ro-RO"}'
}

prepare_plain() {
    local docId=$1; local recipientId=$2
    curl -sS -X POST "$API/api/signing/prepare" -H "$AUTH" -H "$JSON" \
        -d "{\"documentId\":\"$docId\",\"recipientId\":\"$recipientId\",\"requestedBy\":\"smoke\"}" \
        | python -c "import sys,json;print(json.load(sys.stdin)['signingRequestId'])"
}

prepare_with_commit() {
    local docId=$1; local recipientId=$2; local commitJson=$3
    local body=$(python -c "
import sys,json
c=json.loads('$commitJson')
out={'documentId':'$docId','recipientId':'$recipientId','requestedBy':'smoke',
     'renderRootHex':c['rootHex'],'renderAlgo':c['algo'],'renderDpi':c['dpi'],
     'renderPageCount':c['pageCount'],'renderLocale':c['locale'],'renderProfile':c['profile']}
print(json.dumps(out))")
    curl -sS -X POST "$API/api/signing/prepare" -H "$AUTH" -H "$JSON" -d "$body" \
        | python -c "import sys,json;print(json.load(sys.stdin)['signingRequestId'])"
}

sign() {
    local sigReqId=$1
    curl -sS -X POST "$API/api/signing/$sigReqId/sign" -H "$AUTH" -H "$JSON" \
        -d '{"pin":"123456","factor":"pin"}'
}

validate() {
    local signedDocId=$1
    curl -sS "$API/api/signed-documents/$signedDocId/validate" -H "$AUTH"
}

run_plain() {
    echo "=== Variant A: plain sign (no commitment) -> NotPresent ==="
    local docId=$(upload)
    local recId=$(set_recipient "$docId")
    set_fields "$docId" "$recId"
    send_doc "$docId"
    local sigReq=$(prepare_plain "$docId" "$recId")
    local signResp=$(sign "$sigReq")
    local signedId=$(echo "$signResp" | python -c "import sys,json;print(json.load(sys.stdin)['signedDocumentId'])")
    echo "signedDocumentId=$signedId"
    validate "$signedId" \
        | python -c "import sys,json;d=json.load(sys.stdin);rv=d.get('renderVerification');print('renderVerification:',json.dumps(rv,indent=2))"
}

run_pixel_bound() {
    echo "=== Variant B: pixel-bound sign -> Verified ==="
    local docId=$(upload)
    local recId=$(set_recipient "$docId")
    set_fields "$docId" "$recId"
    send_doc "$docId"
    local commit=$(render_commit "$docId")
    echo "commit=$commit"
    local sigReq=$(prepare_with_commit "$docId" "$recId" "$commit")
    local signResp=$(sign "$sigReq")
    local signedId=$(echo "$signResp" | python -c "import sys,json;print(json.load(sys.stdin)['signedDocumentId'])")
    echo "signedDocumentId=$signedId"
    validate "$signedId" \
        | python -c "import sys,json;d=json.load(sys.stdin);rv=d.get('renderVerification');print('renderVerification:',json.dumps(rv,indent=2))"
    echo "$signedId"
}

run_disputed() {
    echo "=== Variant C: pixel-bound sign with WRONG root -> Disputed ==="
    local docId=$(upload)
    local recId=$(set_recipient "$docId")
    set_fields "$docId" "$recId"
    send_doc "$docId"
    # Synthesise a syntactically-valid commitment whose root is deliberately
    # wrong. Server stamps it into the AcroForm dict as-is (no recompute at
    # prepare time — that would defeat the whole point of having the wallet
    # be the bitmap-identity authority). At /validate, the reference
    # renderer recomputes the real root from the signed PDF's bytes and
    # surfaces the divergence.
    local fakeCommit='{"profile":"PdfiumPinned-v1","algo":"SHA-256","dpi":150,"pageCount":2,"locale":"ro-RO","rootHex":"deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef"}'
    local sigReq=$(prepare_with_commit "$docId" "$recId" "$fakeCommit")
    local signResp=$(sign "$sigReq")
    local signedId=$(echo "$signResp" | python -c "import sys,json;print(json.load(sys.stdin)['signedDocumentId'])")
    echo "signedDocumentId=$signedId"
    validate "$signedId" \
        | python -c "import sys,json;d=json.load(sys.stdin);rv=d.get('renderVerification');print('renderVerification:',json.dumps(rv,indent=2))"
}

run_plain
run_pixel_bound
run_disputed
