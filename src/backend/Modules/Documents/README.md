# Documents (ERP-000G core — document vault; AT-065 first owner: the Cari party)

Source of truth: `KARAR_SICILI.md` DEC-DOC-001…003, DEC-DOC-008/009; ADR register ADR-005 (object store + DB
metadata/hash, Kabul); AGENTS.md "Files: S3-compatible object storage abstraction". Open and **not decided** here:
OPEN-018/021 (retention, deletion, legal hold), OPEN-015 (volume/size), the scanner product, the S3 provider, signed-URL
policy, cross-company dedup.

## What is here

| Concern | Rule |
|---|---|
| Model (DEC-DOC-001) | `documents.ContentBlobs` (company + SHA-256, size, kind, object key) → `Documents` → `DocumentVersions` (file name, hash, scan state + scanner id) → `DocumentLinks` (owner kind + owner id). `DocumentAttempts` is the ADR-011 attempt (company + command + key hash). Every table is company-first, tenant-filtered and **append-only**: the write guard refuses any update or delete (DEC-DOC-003 no overwrite; OPEN-018/021 no deletion). |
| Object storage | `IDocumentObjectStoreV1`: immutable, content-addressed `companies/{company:N}/sha256/{hash}` objects; put-if-absent; different bytes under a key are refused. Dev/test implementation: `FileSystemDocumentObjectStore` (no credentials), registered **only** in Development/Testing. No production store is registered → upload/download 503 until the S3-compatible adapter is decided. |
| Physical dedup (DEC-DOC-002) | Within the company only: the same bytes are one blob row and one object per company; a re-upload is a new logical document. Cross-company dedup is closed. |
| Duplicate warning (AT-057) | Warn, never refuse: the upload answer lists the earlier documents of the same company with the same content **whose owner record the actor may read**; the audit event records `sameContentEarlierVersions`. |
| Content rules (DEC-DOC-009) | Allowlist PDF / PNG / JPEG; the bytes must carry the kind's signature; a contradicting declared type is refused; the served type is the canonical one. File name: last segment, NFC, control/bidi/forbidden characters → `_`, ≤ 200 chars. Size bound `Documents:MaximumUploadBytes` (ASSUMPTION-DOC-1: 20 MiB; OPEN-015). |
| Malware scan (DEC-DOC-009) | `IDocumentContentScannerV1`. Infected → 422 `document_content_rejected`, nothing stored. No scanner or no answer → stored `NotScanned` and **never served** (409 `document_not_scanned`). Development/Testing only: `DevelopmentSignatureScanner` (EICAR test string only; its id is recorded on every version it clears). No production scanner exists (product undecided); no rescan path yet. |
| Access | The vault's own names `documents.files.read` / `documents.files.upload` (company-wide, measured afresh at every fence) **and** the owner record's read through the owner-declared `IDocumentOwnerLinkPortV1` (Cari: `counterparty.parties.read` via the owner's authorized detail read, party must hold an account in the context company). Owner not readable / another company → 404. |
| Download | Backend-streamed, re-authorized per request, hash and length re-verified; attachment, `nosniff`, sandbox CSP, `no-store`, `Repr-Digest`. No public or signed object URL is issued (signed short access, OPEN policy, is not built). |
| Audit | `documents.document.uploaded.v1` in the same save (ADR-009), source channel from the request (Web). Downloads are not audited yet. |
| Migration | `20260930120000_InitialDocuments`; `Down` refuses with `THROW 51058` while any document exists. |

Routes: `POST /api/v1/documents` (multipart `ownerKind`, `ownerId`, `file`; `Idempotency-Key`), `GET /api/v1/documents?ownerKind&ownerId`,
`GET /api/v1/documents/{id}/content`. No delete/rename/overwrite route.

Evidence: `tests/GoymenERP.Documents.UnitTests` (rules, service fences, model, store, registration);
`DocumentVaultSqlServerRelationalAcceptanceTests` (real SQL); `src/frontend/.../documents` + `cari/CariDocuments.test.tsx`;
browser `tests/e2e/cari-c02/specs/c02-documents.spec.ts`.
