# Yapay zekâ sözleşmeleri (Y-A1)

| Dosya | Ne | Üretim / doğrulama |
|---|---|---|
| `decision-index-v1.json` | Karar dizini: `docs/decisions` altındaki sicil, açık kararlar, `SINAN_KARARLARI_*` ve formül golden'larından kimlik, başlık, metin, kaynak dosya + satır, durum, yürürlük tarihi, kapsam, ilişkiler, kişisel veri adayı (AI-D12, AI-D34 d, DALGA_PLANI_v2 A.4 (1)). | `python3 -I tools/generate_ai_decision_index.py` (yeniden üretir), `--check` (güncel değilse 1). Test: `tests/ai_decision_index`. |
| `decision-index-v1.schema.json` | Dizinin şeması. | Üretici her koşuda şemaya ve değişmezlere karşı doğrular. |
| `tool-catalog-v1.json` | Okuma araçları: Y-A1 `decisions.lookup.v1`, `logistics.pending-shipments.v1`; WP-A3b Stok (`inventory.stock-positions.v1`, `inventory.negative-stock-attempts.v1`, `inventory.pending-stock-counts.v1`), Üretim (`manufacturing.order-status.v1`, `manufacturing.plan-status.v1`, `manufacturing.work-center-queue.v1`), Satınalma (`purchasing.order-status.v1`, `purchasing.pending-receipts.v1`, `purchasing.invoice-variances.v1` — **Confidential**). Read, izinler, alan izin listesi, veri sınıfı, birim kuralı. | `AiYa1ReadToolsTests` sözleşmeyi C# tanımlarıyla (argüman adları dahil) karşılaştırır; `AiWpa3bReadToolsTests` host kaydını ve varsayılan-ret'i doğrular. Senaryo önerileri: `docs/delivery/arastirma/work_ai/2026-11-22_wpa3b_eval_senaryo_onerileri.md`. |
| `eval/eval-set-v1.json` | SENTETİK değerlendirme seti: 150 karar sorusu (bilgi çıkarma 70, güncellenen karar 30, cevap verememeyi bilme 30, yetki dışı 20) + 20 bekleyen sevkiyat senaryosu. | `python3 -I tools/evaluate_ai_eval_set.py` (modelsiz: şema, kaynak, araç, kişisel veri). Test: `tests/ai_eval_set`; sevkiyat senaryoları ayrıca `LogisticsPendingShipmentsAiToolTests` ile araca karşı koşar. |

Sınırlar: hiçbir model/sağlayıcı çağrısı, anahtar veya dış ağ yok; `Ai:Enabled=false` ve `Ai:AllowedTools=[]` kalır.
Karar aracı dizini yalnız `Ai:Knowledge:DecisionIndexPath` yapılandırılırsa okur (yoksa "kullanılamıyor"); dosyanın
paketlenmesi dağıtım kapısıdır. Dizin tren head'inde yeniden üretilir; karar PR'larından dizin üretimi beklenmez (dizin
bayatsa `tests/ai_decision_index` kırmızı olur). Durum eşlemesi `ASSUMPTION-AI-INDEX-STATUS-1`: KAPANDI → kilitli,
KISMEN/AÇIK → açık, ERTELENDI ayrı durum, golden → belirsiz. Bekleyen sevkiyat tanımı AI-D35; `ai.decisions.read`
önerisi AI-D36 (grant insan kapısı).

WP-A3b araçları yalnız sahip modülün yetkili okuma servisinden okur, izinlerini sunucu şirketi için okumadan önce ve sonra
şirket çapında ölçer (tesis/lokasyon/iş merkezi kapsamlı izin reddedilir), sayıları kesin verir; tarama sınırı aşılırsa
sayılar verilmez ve `complete=false` döner. Stok ve üretim yalnız kg'dır; satınalma miktarları satırın kendi birimindedir ve
birimler toplanmaz. Hiçbir izin bir role verilmez.
