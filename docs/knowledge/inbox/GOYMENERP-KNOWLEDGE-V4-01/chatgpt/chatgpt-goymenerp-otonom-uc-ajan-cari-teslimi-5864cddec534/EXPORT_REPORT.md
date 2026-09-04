# Export Report

## Mod ve kapsam

- Mode: `SOURCE_EXPORT_GIT`
- Provider: `chatgpt`
- Run-ID: `GOYMENERP-KNOWLEDGE-V4-01`
- Source-ID: `chatgpt-goymenerp-otonom-uc-ajan-cari-teslimi-5864cddec534`
- Conversation-ID: `chatgpt-goymenerp-autonomous-cari-thread`
- Coverage: `partial`
- Status: `partial`
- Repository visibility: `PRIVATE` doğrulandı.
- Repository base: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`

Aktif model bağlamında çok sayıda önceki tur `Skipped N messages` olarak temsil edildiği için
bu paket eksiksiz sohbet arşivi iddiası taşımaz. Görünür ve güvenilir şekilde
konumlandırılabilen proje anlamı normalize edilmiştir; görünmeyen mesajlar tamamlanmış
sayılmamıştır.

## Source snapshot

`source_snapshot_sha256` operasyon promptu hariç tutularak; görünür proje mesajlarından
seçilmiş, redakte edilmiş kaynak kayıtları ile exact repository-base kaydının
alphabetic-key, compact UTF-8 JSON Lines temsilinden hesaplandı.

- Snapshot record count: `22`
- Snapshot SHA-256: `5864cddec53445b46ed30dff002ab9c9dc0a6f605a775833bdf113834df6bbca`
- Repository-base record: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`
- Operational attachment snapshot'a dahil edilmedi.

## Normalize kayıtlar

- Item count: `42`
- Conflict count: `1`
- Content fingerprint (`NORMALIZED_ITEMS.ndjson`): `e41ee03f4037c38d3e8fc5e8937f8f1a7bcb41aa2e6ce1221bb5d29bb5b58eb3`
- `EXPLICIT_USER_DECISION`, `USER_REQUIREMENT`, repo-current `SOURCE_VERIFIED`,
  assistant-originated `PROPOSED`/acceptance ve gerçek `MISSING` kayıtları birbirinden ayrıldı.
- `CONFLICT` kaydı gerçek iki package item ID'sini `conflicts_with` alanında taşır.

## Privacy / security review

- Secret scan: `passed`
- PII review: `passed`
- Redactions: `3`
- Kaynakta görülen şirket vergi kimlikleri normalize kaynak snapshot'ında locator-tabanlı
  `[REDACTED:tax_id:<hash8>]` işaretleriyle maskelendi ve NDJSON statement'larına gerçek
  değerleri yazılmadı.
- Kişisel e-posta, telefon, TCKN, IBAN, private key, token, API key veya production
  connection string paket dosyalarına aktarılmadı.
- Otomatik tarama bütün PII'yi matematiksel olarak kanıtlayamaz; bu export ayrıca manuel
  içerik incelemesinden geçirildi.

## Erişilemeyen kapsam

1. Aktif model bağlamında `Skipped N messages` ile temsil edilen önceki kullanıcı/asistan
   turlarının ham metni.
2. Bu run sırasında mount edilmemiş eski ekran görüntüleri, ZIP/PDF/DOCX ve diğer önceki
   konuşma eklerinin byte içerikleri.
3. Platform tarafından sunulmayan gerçek conversation-id/transcript-export API'si; bu
   nedenle conversation_id başlık + görünür proje bağlamından deterministik türetildi.
4. Görünmeyen önceki asistan özel muhakemesi; kaynak olarak kullanılmadı.

## Repository kuralları okundu

Güncel main üzerinden `AGENTS.md`, knowledge README/registry, Hızlı Teslim v3, Otonom Teslim
v2.1, Codex Orkestratörü, tarihli kullanıcı kararları, Karar Sicili, Açık Kararlar,
Master Rapor v3, task manifest, delivery backlog, Decision Pack ve ilerleme günlüğü okundu.
Knowledge paketi bu normatif kaynakları değiştirmez.

## Doğrulama

Connector-only ortamda private repository'nin tam checkout'u bulunmadığından repository'nin
orijinal unittest komutlarını pre-push birebir çalıştırmak mümkün değildi. Bunun yerine
current `tools/validate_knowledge_packages.py` sözleşmesi canlı main'den okunarak aynı
package-level alan/hash/id/relation/UTF-8/LF/attachment/secret/PII kuralları yerelde
uygulandı. Draft PR açıldıktan sonra fresh repository CI ayrıca merge kapısıdır.

Bu sınırlama nedeniyle paket `status=partial` tutulur; final entegratör tam validator
komutlarını canonical checkout'ta yeniden çalıştırmalıdır.

## Değişiklik sınırı

Bu kaynak branch yalnız şu dizini değiştirmelidir:

`docs/knowledge/inbox/GOYMENERP-KNOWLEDGE-V4-01/chatgpt/chatgpt-goymenerp-otonom-uc-ajan-cari-teslimi-5864cddec534/`

`SOURCE_REGISTRY.json`, decisions, master, governance, backlog, delivery, source code,
tests, contracts, infra ve workflows değiştirilmez.
