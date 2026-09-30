# Documents (ERP-000G core — document vault; AT-065 first owner: the Cari party)

Source of truth: `KARAR_SICILI.md` DEC-DOC-001…003, DEC-DOC-008/009; ADR register ADR-005 (object store + DB
metadata/hash, Kabul); AGENTS.md "Files: S3-compatible object storage abstraction".

**User decisions 2026-09-30 (binding):** files are stored on the company's **own server** (local disk or NAS path that
the existing backups include) — no cloud; malware scan with **Windows Defender** on the server; **no deletion during the
legal retention period (10 years)** — archive only; maximum file size **20 MB** (20 MiB = 20 971 520 bytes).

Still open: archive and legal-hold mechanics (OPEN-018/021 beyond "no deletion"), a rescan path for NotScanned versions,
signed-URL policy, cross-company dedup.

## What is here

| Concern | Rule |
|---|---|
| Model (DEC-DOC-001) | `documents.ContentBlobs` (company + SHA-256, size, kind, object key) → `Documents` → `DocumentVersions` (file name, hash, scan state + scanner id) → `DocumentLinks` (owner kind + owner id). `DocumentAttempts` is the ADR-011 attempt (company + command + key hash). Every table is company-first, tenant-filtered and **append-only**: the write guard refuses any update or delete (DEC-DOC-003 no overwrite; OPEN-018/021 no deletion). |
| Object storage | `IDocumentObjectStoreV1`: immutable, content-addressed `companies/{company:N}/sha256/{hash}` objects; put-if-absent; different bytes under a key are refused. **Production:** `ServerDirectoryDocumentObjectStore` — the configured root must exist and be writable (checked at startup), keys resolve strictly inside it, no symlink/junction below the root is followed, a write goes to a temp file with write-through + flush-to-disk, is re-hashed, then renamed into place **without overwrite** and marked read-only; every read is re-hashed against the key (mismatch → 503, never wrong bytes). **Dev/test:** `FileSystemDocumentObjectStore`, registered only in Development/Testing. |
| Physical dedup (DEC-DOC-002) | Within the company only: the same bytes are one blob row and one object per company; a re-upload is a new logical document. Cross-company dedup is closed. |
| Duplicate warning (AT-057) | Warn, never refuse: the upload answer lists the earlier documents of the same company with the same content **whose owner record the actor may read**; the audit event records `sameContentEarlierVersions`. |
| Content rules (DEC-DOC-009) | Allowlist PDF / PNG / JPEG; the bytes must carry the kind's signature; a contradicting declared type is refused; the served type is the canonical one. File name: last segment, NFC, control/bidi/forbidden characters → `_`, ≤ 200 chars. Size bound `Documents:MaximumUploadBytes`: 20 MiB by user decision (code default and `appsettings.json`). |
| Malware scan (DEC-DOC-009) | `IDocumentContentScannerV1`. Infected → 422 `document_content_rejected`, nothing stored. No scanner or no answer → stored `NotScanned` and **never served** (409 `document_not_scanned`). **Production:** `WindowsDefenderDocumentScanner` (id `windows-defender-mpcmdrun-v1`): the bytes are written to a quarantined temp copy (own folder, not the store) and scanned with `MpCmdRun.exe -Scan -ScanType 3 -File <copy> -DisableRemediation` **before** anything is stored; exit 0 (and the copy unchanged) → Clean; exit 2, or real-time protection blocking the copy → Infected; any other exit code, timeout, start failure, or a changed/vanished copy → NotScanned (fail-closed). The copy is removed afterwards; at most `MaximumConcurrentScans` scans run at once. **Dev/test:** `DevelopmentSignatureScanner` (EICAR string only; its id is recorded on every version it clears). No rescan path yet. |
| Access | The vault's own names `documents.files.read` / `documents.files.upload` (company-wide, measured afresh at every fence) **and** the owner record's read through the owner-declared `IDocumentOwnerLinkPortV1` (Cari: `counterparty.parties.read` via the owner's authorized detail read, party must hold an account in the context company). Owner not readable / another company → 404. |
| Download | Backend-streamed, re-authorized per request, hash and length re-verified; attachment, `nosniff`, sandbox CSP, `no-store`, `Repr-Digest`. No public or signed object URL is issued (signed short access, OPEN policy, is not built). |
| Audit | `documents.document.uploaded.v1` in the same save (ADR-009), source channel from the request (Web). Downloads are not audited yet. |
| Retention | **Policy: 10-year legal retention, no deletion — archive only** (user decision 2026-09-30). Enforced today by the absence of any delete path: the tables are append-only (write guard), the object stores never delete or overwrite, there is no delete/rename/overwrite route, and the migration `Down` refuses while documents exist. Archive and legal hold are later work; nothing may delete a document object in the meantime. |
| Migration | `20260930120000_InitialDocuments`; `Down` refuses with `THROW 51058` while any document exists. |

Routes: `POST /api/v1/documents` (multipart `ownerKind`, `ownerId`, `file`; `Idempotency-Key`), `GET /api/v1/documents?ownerKind&ownerId`,
`GET /api/v1/documents/{id}/content`. No delete/rename/overwrite route.

## Configuration and registration

| Key | Meaning |
|---|---|
| `Documents:ObjectStore:RootPath` | Production: fully qualified, **existing** folder on the server's disk or a UNC NAS path (`D:\GoymenERP\Belgeler`, `\\nas01\erp\Belgeler`). Dev/test: stand-in store folder (default: temp). |
| `Documents:Scanner:Defender:ExecutablePath` | Full path of `MpCmdRun.exe` (e.g. `C:\Program Files\Windows Defender\MpCmdRun.exe`). |
| `Documents:Scanner:Defender:QuarantinePath` | Local folder for the temporary scan copies (default `%TEMP%\goymen-erp-document-scan` of the service account). |
| `Documents:Scanner:Defender:TimeoutSeconds` | Per-scan timeout, 1…1800 (default 120); longer → NotScanned. |
| `Documents:Scanner:Defender:MaximumConcurrentScans` | 1…16 (default 2). |
| `Documents:Scanner:Mode` | Production: empty or `WindowsDefender`. Dev/test: `DevelopmentSignature` (default), `None`, or `WindowsDefender`. |
| `Documents:MaximumUploadBytes` | Default 20 971 520 (20 MiB). |

As environment variables: `Documents__ObjectStore__RootPath`, `Documents__Scanner__Defender__ExecutablePath`, …

Production (any environment other than Development/Testing):

* **Nothing configured** → no store and no scanner → upload/download **503** (fail-closed; the app still starts).
* **RootPath + Defender ExecutablePath** → server store + Defender scanner.
* **Only one of them**, `Mode=None` beside them, `Mode=DevelopmentSignature`, a missing/relative/unwritable root, a
  missing `MpCmdRun.exe`, an unwritable quarantine folder, a bad number, or a non-Windows host → **startup fails**.

## Operations runbook (server)

1. **Folder.** Create the document root on the server before the first start, on a disk or NAS share that the
   **existing backup already covers** (e.g. `D:\GoymenERP\Belgeler` or `\\nas01\erp\Belgeler`). The app never creates
   the root (a typo must not silently write outside the backups). Layout: `companies\{company}\sha256\{hash}`; files
   are read-only and named by their SHA-256; the database holds names, owners and hashes, so **back up the database and
   this folder together** (same backup window) and restore them together.
2. **Backup inclusion.** Add the root to the backup job; verify one restore of a file and compare its SHA-256 with the
   file name (`Get-FileHash -Algorithm SHA256`). No cleanup, dedup or archiving tool may delete or rewrite files there —
   retention is 10 years, no deletion.
3. **Service account.** Run the API under a dedicated account (a domain account or gMSA for a NAS share;
   `LocalSystem`/`NetworkService` reach a share only as the computer account). Grant it **Modify** on the document root
   (create files/folders, write, rename, read, set attributes — the app deletes only its own unfinished `.partial` temp
   files and write probes) and on the quarantine folder, and the share permission for a NAS. Ordinary users get **no**
   access to either folder: every access goes through the ERP's per-request authorization.
4. **Windows Defender.** Microsoft Defender Antivirus must be **active** on the server with current signatures and
   real-time protection on, and `MpCmdRun.exe` present at the configured path (use the stable
   `C:\Program Files\Windows Defender\MpCmdRun.exe`, not the versioned `ProgramData\...\Platform\<version>` copy).
   Do **not** add the quarantine folder or the document root to Defender exclusions. If another antivirus product puts
   Defender in passive/disabled mode, scans fail and every upload is stored NotScanned (not downloadable) — fail-closed.
5. **Acceptance check (manual, as the service account; not an automated test):**
   `& "C:\Program Files\Windows Defender\MpCmdRun.exe" -Scan -ScanType 3 -File <a clean PDF> -DisableRemediation`
   must exit 0; the same on a file holding the EICAR test string must exit 2 (real-time protection may already block
   writing it — also a pass). Then upload a PDF in the ERP and download it (Clean), and upload an EICAR file (refused).
6. **Quarantine folder.** Local disk, not the NAS, not backed up; it holds only transient scan copies removed after
   each scan. Leftovers (e.g. after a crash) may be deleted by ops at any time — they are not documents.
7. **Size.** 20 MiB per file; the endpoint's request-body bound follows it. A reverse proxy in front (IIS, nginx) must
   allow request bodies of at least ~21 MB on `POST /api/v1/documents`.

Evidence: `tests/GoymenERP.Documents.UnitTests` (rules, service fences, model, stores, the Defender verdict mapping via an
injected process runner, registration — `DocumentsProductionAdaptersTests`; a real Defender scan is the manual step 5
above, not a test);
`DocumentVaultSqlServerRelationalAcceptanceTests` (real SQL); `src/frontend/.../documents` + `cari/CariDocuments.test.tsx`;
browser `tests/e2e/cari-c02/specs/c02-documents.spec.ts`.
