# Export report

Status: NON_NORMATIVE_CANDIDATE
Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Source-ID: codex-goymenerp-canli-kapilar-263a4ba06b2e
Mode: SOURCE_EXPORT_GIT
Base-Main-SHA: 188c792900e35d3e2ed6a9ba4734dc9ad596fc97

## Snapshot

- Conversation-ID: `codex-goymenerp-canli-kapilar`
- Başlık: `GöymenERP canlı kapılar`
- Kimlik türetimi: provider + ASCII başlık slug'ı; ham platform conversation ID görünür değildi.
- Snapshot kayıtları: 16 (`message=15`, `repository_base=1`).
- Tekilleştirme: transport/automation kabukları ve exact tekrarlar çıkarıldı; ilk görünür
  tekil proje talebi ile 14 tekil durum mesajı kaynak sırasında tutuldu.
- Snapshot JSONL: compact JSON, alfabetik anahtarlar, UTF-8 non-ASCII, LF-sonlu.
- Source snapshot SHA-256: `263a4ba06b2eb8c3b95e05296247b579ba79be936b255fba3c786d2a4ee6c9f4`
- Source-ID: `codex-goymenerp-canli-kapilar-263a4ba06b2e`
- Operasyon promptu ve üretilmiş paket alanları snapshot girdisine katılmadı.

## Kapsam ve erişilemeyen içerik

- Coverage: `visible_only`
- Status: `partial`
- Erişilemeyen: konuşmanın platform tarafından açıkça kesilmiş/özetlenmiş önceki bölümü.
- Başka konuşmalara ait source_id, branch veya PR kullanılmadı. Özellikle PR #220 ve
  `codex-pr-181-k3-takibi-1ddb47fa532a` bu pakete dahil edilmedi.

## Çıktı

- Normalize kayıt: 37
- Ek: 1 (operasyon promptu; proje bilgisinden hariç)
- Çatışma: 0
- Değişen repository alanı yalnız
  `docs/knowledge/inbox/GOYMENERP-KNOWLEDGE-V4-01/codex/codex-goymenerp-canli-kapilar-263a4ba06b2e/` dizinidir.
- `SOURCE_REGISTRY.json`, governance, decisions, master, backlog, delivery, uygulama,
  test ve CI dosyaları değiştirilmedi.

## Privacy review

- Secret scan: passed
- PII review: passed
- Redactions: 0
- Repository kimliği ve PR/SHA/run numaraları izlenebilirlik için korundu.
- Otomatik tarama tüm olası kişisel veriyi tek başına kanıtlamaz; görünür proje içeriği
  ayrıca elle gözden geçirildi.

## Doğrulama sözleşmesi

- `python3 -I -S -B -m unittest discover -s tests/knowledge_package_validator -p "test_*.py" -v`
- `python3 -I -S -B tools/validate_knowledge_packages.py`
- `git diff --cached --check`
- Paket yalnız bu üç kapı PASS olduktan sonra push/Draft PR için uygundur.
