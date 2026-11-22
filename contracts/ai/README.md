# Yapay zekâ sözleşmeleri (Y-A1, WP-A4)

| Dosya | Ne | Üretim / doğrulama |
|---|---|---|
| `decision-index-v1.json` | Karar dizini: `docs/decisions` altındaki sicil, açık kararlar, `SINAN_KARARLARI_*` ve formül golden'larından kimlik, başlık, metin, kaynak dosya + satır, durum, yürürlük tarihi, kapsam, ilişkiler, kişisel veri adayı (AI-D12, AI-D34 d, DALGA_PLANI_v2 A.4 (1)). | `python3 -I tools/generate_ai_decision_index.py` (yeniden üretir), `--check` (güncel değilse 1). Test: `tests/ai_decision_index`. |
| `decision-index-v1.schema.json` | Dizinin şeması. | Üretici her koşuda şemaya ve değişmezlere karşı doğrular. |
| `release-knowledge-v1.json` | Üçüncü kaynak (WP-A4, A.4 (3)): yayındaki sürümün davranış bilgisi. Yalnız `durum: yayinda` olan **ve** kanıt dosyası bu ağaçta ifadesiyle bulunan notlar metniyle girer; uygulanmamış özellik yalnız kimlik + neden olarak listelenir. | `python3 -I tools/generate_ai_release_knowledge.py` (`--check`). Kaynak: `docs/release-knowledge/RK-*.md`. Test: `tests/ai_release_knowledge`. |
| `release-knowledge-v1.schema.json` | Yayın bilgisi paketinin şeması. | Üretici her koşuda doğrular. |
| `tool-catalog-v1.json` | Okuma araçları: `decisions.lookup.v1`, `logistics.pending-shipments.v1` (Y-A1); `security.events.summary.v1`, `security.tech-watch.summary.v1` (WP-C10, bkz. `contracts/security/README.md`) (Read, izinler, alan izin listesi, veri sınıfı). | `AiYa1ReadToolsTests` sözleşmeyi C# tanımlarıyla karşılaştırır. |
| `tool-catalog-v1.json` | Okuma araçları (Read, izinler, alan izin listesi, veri sınıfı). Y-A1: `decisions.lookup.v1`, `logistics.pending-shipments.v1`. WP-A3a: `sales.pending-approvals.v1`, `sales.quote-status.v1`, `sales.order-status.v1`, `logistics.order-dispatch-notes.v1`, `logistics.instruction-shipment-status.v1`, `counterparty.party-lookup.v1`, `counterparty.party-summary.v1`, `counterparty.role-counts.v1`, `counterparty.party-contacts.v1` (AI-D42: yalnız bu araç Gizli; alan sınıfları çıktıda). | `AiYa1ReadToolsTests` sözleşmeyi bütün C# tanımlarıyla karşılaştırır; modül davranışı `SalesReadAiToolsTests`, `LogisticsReadAiToolsTests`, `CounterpartyReadAiToolsTests`. |
| `eval/eval-set-wpa3a-oneriler-v1.json` | WP-A3a araçları için ÖNERİ değerlendirme maddeleri (35, SENTETİK): başarı, yetki dışı/başka şirket, eksik veri. Sete henüz katılmadı. | Doğrulanmaz; set tren head'inde güncellenir. |
| `tool-catalog-v1.json` | Okuma araçları: Y-A1 `decisions.lookup.v1`, `logistics.pending-shipments.v1`; WP-A3b Stok (`inventory.stock-positions.v1`, `inventory.negative-stock-attempts.v1`, `inventory.pending-stock-counts.v1`, `inventory.average-cost.v1` — **Confidential**, AI-D45), Üretim (`manufacturing.order-status.v1`, `manufacturing.plan-status.v1`, `manufacturing.work-center-queue.v1`), Satınalma (`purchasing.order-status.v1`, `purchasing.pending-receipts.v1`, `purchasing.invoice-variances.v1` — **Confidential**). Read, izinler, alan izin listesi, veri sınıfı, birim kuralı. | `AiYa1ReadToolsTests` sözleşmeyi C# tanımlarıyla (argüman adları dahil) karşılaştırır; `AiWpa3bReadToolsTests` host kaydını ve varsayılan-ret'i doğrular. Senaryo önerileri: `docs/delivery/arastirma/work_ai/2026-11-22_wpa3b_eval_senaryo_onerileri.md`. |
| `tool-catalog-v1.json` | Okuma araçları. Y-A1: `decisions.lookup.v1`, `logistics.pending-shipments.v1`. WP-A3c: `finance.due-open-items.v1`, `finance.due-customer-instruments.v1`, `finance.treasury-accounts.v1`, `accounting.period-status.v1`, `accounting.posting-queues.v1`, `accounting.period-journals.v1`, `work.inbox-awaiting-me.v1`, `work.overdue-approvals.v1`, `work.my-tasks.v1` (Read, izinler, alan izin listesi, veri sınıfı; Finans tutarlı araçlar Confidential). | `AiYa1ReadToolsTests` ve `AiWpA3cReadToolsTests` sözleşmeyi C# tanımlarıyla karşılaştırır. WP-A3c eval önerileri: `docs/delivery/WPA3C_AI_ARAC_EVAL_ONERILERI.md`. |
| `tool-catalog-v1.json` | Okuma araçları: `decisions.lookup.v1`, `release-knowledge.lookup.v1`, `logistics.pending-shipments.v1` (Read, izinler, alan izin listesi, veri sınıfı). | `AiYa1ReadToolsTests` sözleşmeyi C# tanımlarıyla karşılaştırır. |
| `eval/eval-set-v1.json` | SENTETİK değerlendirme seti: 150 karar sorusu (bilgi çıkarma 70, güncellenen karar 30, cevap verememeyi bilme 30, yetki dışı 20) + 20 bekleyen sevkiyat senaryosu. | `python3 -I tools/evaluate_ai_eval_set.py` (modelsiz: şema, kaynak, araç, kişisel veri). Test: `tests/ai_eval_set`; sevkiyat senaryoları ayrıca `LogisticsPendingShipmentsAiToolTests` ile araca karşı koşar. |

Sınırlar: hiçbir model/sağlayıcı çağrısı, anahtar veya dış ağ yok; `Ai:Enabled=false` ve `Ai:AllowedTools=[]` kalır.

**WP-A4 bilgi kaynakları:**

- **Paketleme:** `decision-index-v1.json` ve `release-knowledge-v1.json`, `GoymenERP.Api.csproj` içindeki `Content` öğeleriyle
  API derleme ve yayın çıktısına (derlemenin yanına) kopyalanır; eski `Ai:Knowledge:DecisionIndexPath` ayarı kaldırıldı. Bilgi,
  onu sunan derlemeyle aynı ağaçtan gelir. Paket sürümü = dosya baytlarının SHA-256'sı. Mimari guard API projesindeki
  onaysız `Content` öğesini reddeder; gerekli inceleme kaydı (PDF yazı tipi içeriği gibi) **tren-2 head'inde** yapılır.
- **Onay (AI-D32 "onaylı karar dizini"):** bir paket sürümü, şirkette `ai.knowledge.approve` iznine sahip bir kişinin tam o
  baytları onaylamasıyla (`POST /api/v1/ai/knowledge/approvals`, `GET .../packages`, `POST .../approvals/{id}/withdrawal`)
  kaynak olur. İzin kayıtlıdır, hiçbir role verilmez, Critical işaretlenmemiştir (insan kapısı). Onaysız ya da onayı
  okunamayan pakette araçlar **kapalıdır** (`Unavailable`); "onaylı değil" uyarısıyla içerik dönülmez. Paket değişirse
  (yeni SHA-256) yeniden onay gerekir. Kayıtlar `aiknowledge` şemasında; geri alma silmez, sürüm adımıyla `geri_alindi` olur.
- **Maskeleme (plan D2, AI-D36):** kişi adları dizin **üretiminde** `[KİŞİ]`, e-posta/telefon/kimlik/IBAN `[GİZLİ]` olur;
  dizin ad listesini taşımaz (yalnız sayısı). Üretim sonrası ad kalırsa üretim hatadır. Okuyucu ad listesi içeren ya da
  `mask_policy` taşımayan dizini reddeder. `[KİŞİ] ERP` gibi fazla maskeleme bilerek güvenli yönde bırakıldı.
- **Bulut uygunluğu (AI-D34 d):** her kayıt/not içerik ve veri sınıfından etiketlenir: `bulut_uygun_aday`,
  `maskeli_bulut_uygun_aday`, `yerel_kalir` (para tutarı: ticari hassas aday → `confidential`). Internal sınıflı araçlar
  `yerel_kalir` içeriği vermez, yalnız sayar. Hangi sınıfın buluta gidebileceği ağ geçidi politikasıdır (insan kapısı).
- Dizin ve yayın paketi tren head'inde yeniden üretilir; karar PR'larından dizin üretimi beklenmez (dizin bayatsa
  `tests/ai_decision_index` kırmızı olur). Durum eşlemesi `ASSUMPTION-AI-INDEX-STATUS-1`: KAPANDI → kilitli, KISMEN/AÇIK →
  açık, ERTELENDI ayrı durum, golden → belirsiz. Bekleyen sevkiyat tanımı AI-D35; `ai.decisions.read` önerisi AI-D36 (grant
  insan kapısı).

WP-A3b araçları yalnız sahip modülün yetkili okuma servisinden okur, izinlerini sunucu şirketi için okumadan önce ve sonra
şirket çapında ölçer (tesis/lokasyon/iş merkezi kapsamlı izin reddedilir), sayıları kesin verir; tarama sınırı aşılırsa
sayılar verilmez ve `complete=false` döner. Stok ve üretim yalnız kg'dır; satınalma miktarları satırın kendi birimindedir ve
birimler toplanmaz. Hiçbir izin bir role verilmez.

AI-D45: `inventory.average-cost.v1` yalnız `inventory.average-cost.read` (maliyet görme izni, kayıtlı, hiçbir role verilmez) ile
okunur; Gizli sınıftır ve ağ geçidi Gizli içeriği bulut modele yönlendirmez. KLN-D42: "açık alış siparişi" =
`purchasing.pending-receipts.v1` (onaylı siparişin mal kabul bekleyen satırı); `purchasing.order-status.v1` yalnız belge durumu sayar.

## Araç kartı pin'i ve dış metin karantinası (WP-A6)

- `Ai:AllowedTools` girdisi `DescriptionSha256` taşır: aracın model kartının (kimlik, sürüm, sahip, risk, veri sınıfı,
  açıklama, izinler, alan listesi) SHA-256'sı (`AiToolDescriptionPinV1`). Kart değişirse (açıklama yeniden yazılırsa,
  alan eklenirse) araç onaylı yeni pin girilene kadar katalogda yoktur; pin'i olmayan araç da yoktur (varsayılan red).
- Dışarıdan gelen her metin (e-posta, PDF, yapıştırılan metin, tablo hücresi, OCR çıktısı) `AiExternalContentQuarantineV1`
  ile veri olarak tutulur (AI-D34 c). Bulgu yalnız türdür; değer güvenilmez sayılır. Dış içerik görmüş model turu araç
  çağrısı planlarsa çıktının tamamı atılır.

## Belgeden taslak (WP-B3)

`POST /api/v1/ai/document-drafts`: satış siparişi, tedarikçi teyidi, gelen fatura TASLAĞI. Kademeler: yapılı dosya
(UBL-TR, Satınalma'nın kendi okuyucusuyla; CSV/XLSX bilinen başlıkla) → yerel PDF metni → OCR portu (motor yok) → yerel
model portu (OPEN-014'e kadar kapalı). Taslak kayıt oluşturmaz; `Ai:Enabled` ve `Ai:DocumentDrafts:Enabled` kapalıdır.

