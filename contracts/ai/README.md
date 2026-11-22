# Yapay zekâ sözleşmeleri (Y-A1)

| Dosya | Ne | Üretim / doğrulama |
|---|---|---|
| `decision-index-v1.json` | Karar dizini: `docs/decisions` altındaki sicil, açık kararlar, `SINAN_KARARLARI_*` ve formül golden'larından kimlik, başlık, metin, kaynak dosya + satır, durum, yürürlük tarihi, kapsam, ilişkiler, kişisel veri adayı (AI-D12, AI-D34 d, DALGA_PLANI_v2 A.4 (1)). | `python3 -I tools/generate_ai_decision_index.py` (yeniden üretir), `--check` (güncel değilse 1). Test: `tests/ai_decision_index`. |
| `decision-index-v1.schema.json` | Dizinin şeması. | Üretici her koşuda şemaya ve değişmezlere karşı doğrular. |
| `tool-catalog-v1.json` | Okuma araçları (Read, izinler, alan izin listesi, veri sınıfı). Y-A1: `decisions.lookup.v1`, `logistics.pending-shipments.v1`. WP-A3a: `sales.pending-approvals.v1`, `sales.quote-status.v1`, `sales.order-status.v1`, `logistics.order-dispatch-notes.v1`, `logistics.instruction-shipment-status.v1`, `counterparty.party-lookup.v1`, `counterparty.party-summary.v1`, `counterparty.role-counts.v1`. | `AiYa1ReadToolsTests` sözleşmeyi bütün C# tanımlarıyla karşılaştırır; modül davranışı `SalesReadAiToolsTests`, `LogisticsReadAiToolsTests`, `CounterpartyReadAiToolsTests`. |
| `eval/eval-set-wpa3a-oneriler-v1.json` | WP-A3a araçları için ÖNERİ değerlendirme maddeleri (29, SENTETİK): başarı, yetki dışı/başka şirket, eksik veri. Sete henüz katılmadı. | Doğrulanmaz; set tren head'inde güncellenir. |
| `eval/eval-set-v1.json` | SENTETİK değerlendirme seti: 150 karar sorusu (bilgi çıkarma 70, güncellenen karar 30, cevap verememeyi bilme 30, yetki dışı 20) + 20 bekleyen sevkiyat senaryosu. | `python3 -I tools/evaluate_ai_eval_set.py` (modelsiz: şema, kaynak, araç, kişisel veri). Test: `tests/ai_eval_set`; sevkiyat senaryoları ayrıca `LogisticsPendingShipmentsAiToolTests` ile araca karşı koşar. |

Sınırlar: hiçbir model/sağlayıcı çağrısı, anahtar veya dış ağ yok; `Ai:Enabled=false` ve `Ai:AllowedTools=[]` kalır.
Karar aracı dizini yalnız `Ai:Knowledge:DecisionIndexPath` yapılandırılırsa okur (yoksa "kullanılamıyor"); dosyanın
paketlenmesi dağıtım kapısıdır. Dizin tren head'inde yeniden üretilir; karar PR'larından dizin üretimi beklenmez (dizin
bayatsa `tests/ai_decision_index` kırmızı olur). Durum eşlemesi `ASSUMPTION-AI-INDEX-STATUS-1`: KAPANDI → kilitli,
KISMEN/AÇIK → açık, ERTELENDI ayrı durum, golden → belirsiz. Bekleyen sevkiyat tanımı AI-D35; `ai.decisions.read`
önerisi AI-D36 (grant insan kapısı).
