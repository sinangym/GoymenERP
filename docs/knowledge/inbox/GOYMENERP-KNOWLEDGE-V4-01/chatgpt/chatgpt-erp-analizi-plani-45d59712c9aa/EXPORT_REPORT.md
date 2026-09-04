# Export Report

Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Provider: chatgpt
Mode: SOURCE_EXPORT_GIT
Conversation-ID: chatgpt-erp-analizi-plani
Conversation-Title: ERP Analizi Planı
Coverage: complete
Base-Main-SHA: 188c792900e35d3e2ed6a9ba4734dc9ad596fc97
Repository-Visibility: PRIVATE
Normative-Status: NON_NORMATIVE_CANDIDATE

## Erişim ve kapsam

Bu kaynak aktarımı yalnız bu konuşmada kullanıcıya görünür proje mesajlarını, erişilebilir ekleri ve doğrulanmış canonical repository kanıtını kullanır. Gizli sistem talimatları, özel muhakeme zinciri ve başka konuşmaların görünmeyen geçmişi kaynak yapılmamıştır. Kullanıcının mesajında geçen ayrı ChatGPT share bağlantısı başka konuşmanın kaynağı olarak otomatik içe alınmamış; bu konuşmada verilen Master v3 dosyası kendi eki olarak işlenmiştir.

## Snapshot ve kimlik

- `source_snapshot_sha256`: `45d59712c9aaebe4bf5d2775a630a6ded41eaef3edf523bca104b37f71768035`
- `source_id`: `chatgpt-erp-analizi-plani-45d59712c9aa`
- `content_fingerprint_sha256`: `6c5a6b91bf39609d51cce493f7549e6f807da2bc96c9d9fe7a0411af00ddbd8c`
- Project messages included in snapshot: 12
- Project attachments included in snapshot: 7
- Attachments indexed: 9
- Normalized items: 124
- Unresolved conflicts: 0

## Privacy review

- Secret scan: PASS — generated package text contains no private-key blocks, OpenAI/GitHub/AWS/Slack token patterns, bearer credentials or credential assignments with non-placeholder values.
- PII review: PASS — package statements and attachment metadata intentionally omit personal emails, phone numbers, TCKN/VKN, IBAN/bank identifiers and unnecessary real-person names.
- Redactions: 0.
- Limitation: automated pattern scanning cannot prove absence of every possible PII form; manual package review was also applied to committed text.

## Normalization notes

- User decisions are distinguished from assistant proposals.
- StokERP formulas and behavior are marked CURRENT/SOURCE_VERIFIED; they are not presented as implemented Göymen ERP behavior.
- Master v3 target decisions are source-verified target records.
- Later user decision that makes StokERP donor-only supersedes the older API dependency statement.
- No `VERIFIED_IMPLEMENTED` item is created from screenshots or conversation claims alone.

## Validation performed before Git write

- Required six-file package shape prepared.
- Manifest file list/count/hash values computed from actual bytes.
- UTF-8 without BOM and LF termination checked.
- File size < 8 MiB and each NDJSON line < 256 KiB checked.
- Exact normalized-item field set, enum values, deterministic item IDs, relation targets, unique IDs and counts checked locally.
- Attachment heading count and manifest attachment count checked.
- Secret/PII scan of the six package files passed.
- Canonical repository visibility was verified as PRIVATE and base `main` SHA as `188c792900e35d3e2ed6a9ba4734dc9ad596fc97` before branch preparation.

The repository's official knowledge validator and GitHub CI are re-checked after the branch/PR exists; until those remote checks are observed, the export report must not imply final integration or promotion.
