# Export report

Status: NON_NORMATIVE_CANDIDATE
Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Source-ID: codex-goymenerp-canli-kapilar-bf943d77d499

Mode: SOURCE_EXPORT_GIT
Base-Main-SHA: 9f5b9bfdef249b9c8e89c6668f66b6aa55c34602

## Snapshot

- Conversation-ID: codex-goymenerp-canli-kapilar
- Başlık: GöymenERP canlı kapılar
- Önceki immutable source: codex-goymenerp-canli-kapilar-263a4ba06b2e; Draft PR #223.
- Snapshot kayıtları: 18 (message=16, attachment=1, repository_base=1).
- Snapshot JSONL: compact JSON, alfabetik anahtarlar, UTF-8 non-ASCII, LF-sonlu.
- Source snapshot SHA-256: bf943d77d4990376217e432cb92216867faf5835c17a7f8fc1bfd8c6c3ef062f
- Source-ID: codex-goymenerp-canli-kapilar-bf943d77d499
- Operasyon promptunun içeriği ve üretilmiş paket alanları proje bilgisinden hariç tutuldu.

## Kapsam ve erişilemeyen içerik

- Coverage: visible_only
- Status: partial
- Erişilemeyen: platform aktarımında açıkça kesilmiş/özetlenmiş önceki konuşma bölümü.
- Complete değerleri kullanılmadı; eklenen tek dosya tam ham sohbet dökümü değil,
  operasyon promptudur.

## Revision ilişkileri

- Önceki paket veya PR değiştirilmedi.
- Yalnız değişen main, PR #169, PR #160 ve PR #172 durum kayıtları gerçek eski item
  kimliklerine supersedes ile bağlandı.
- Değişmeyen kayıtlar yeniden kimliklendirildi; uydurma cross-source ilişki kurulmadı.

## Çıktı

- Normalize kayıt: 43
- Ek: 1
- Çatışma: 0
- Değişiklik knowledge inbox kaynak dizinleriyle sınırlıdır.

## Privacy review

- Secret scan: passed
- PII review: passed
- Redactions: 0

## Doğrulama sözleşmesi

- python3 -I -S -B -m unittest discover -s tests/knowledge_package_validator -p test_*.py -v
- python3 -I -S -B tools/validate_knowledge_packages.py
- git diff --cached --check
- Paket yalnız üç kapı PASS olduktan sonra push/Draft PR için uygundur.
