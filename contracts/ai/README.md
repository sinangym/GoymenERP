# Yapay zekâ sözleşmeleri (Y-A1)

| Dosya | Ne | Üretim / doğrulama |
|---|---|---|
| `decision-index-v1.json` | Karar dizini: `docs/decisions` altındaki sicil, açık kararlar, `SINAN_KARARLARI_*` ve formül golden'larından kimlik, başlık, metin, kaynak dosya + satır, durum, yürürlük tarihi, kapsam, ilişkiler, kişisel veri adayı (AI-D12, AI-D34 d, DALGA_PLANI_v2 A.4 (1)). | `python3 -I tools/generate_ai_decision_index.py` (yeniden üretir), `--check` (güncel değilse 1). Test: `tests/ai_decision_index`. |
| `decision-index-v1.schema.json` | Dizinin şeması. | Üretici her koşuda şemaya ve değişmezlere karşı doğrular. |
| `tool-catalog-v1.json` | Okuma araçları. Y-A1: `decisions.lookup.v1`, `logistics.pending-shipments.v1`. WP-A3c: `finance.due-open-items.v1`, `finance.due-customer-instruments.v1`, `finance.treasury-accounts.v1`, `accounting.period-status.v1`, `accounting.posting-queues.v1`, `accounting.period-journals.v1`, `work.inbox-awaiting-me.v1`, `work.overdue-approvals.v1`, `work.my-tasks.v1` (Read, izinler, alan izin listesi, veri sınıfı; Finans tutarlı araçlar Confidential). | `AiYa1ReadToolsTests` ve `AiWpA3cReadToolsTests` sözleşmeyi C# tanımlarıyla karşılaştırır. WP-A3c eval önerileri: `docs/delivery/WPA3C_AI_ARAC_EVAL_ONERILERI.md`. |
| `eval/eval-set-v1.json` | SENTETİK değerlendirme seti: 150 karar sorusu (bilgi çıkarma 70, güncellenen karar 30, cevap verememeyi bilme 30, yetki dışı 20) + 20 bekleyen sevkiyat senaryosu. | `python3 -I tools/evaluate_ai_eval_set.py` (modelsiz: şema, kaynak, araç, kişisel veri). Test: `tests/ai_eval_set`; sevkiyat senaryoları ayrıca `LogisticsPendingShipmentsAiToolTests` ile araca karşı koşar. |

Sınırlar: hiçbir model/sağlayıcı çağrısı, anahtar veya dış ağ yok; `Ai:Enabled=false` ve `Ai:AllowedTools=[]` kalır.
Karar aracı dizini yalnız `Ai:Knowledge:DecisionIndexPath` yapılandırılırsa okur (yoksa "kullanılamıyor"); dosyanın
paketlenmesi dağıtım kapısıdır. Dizin tren head'inde yeniden üretilir; karar PR'larından dizin üretimi beklenmez (dizin
bayatsa `tests/ai_decision_index` kırmızı olur). Durum eşlemesi `ASSUMPTION-AI-INDEX-STATUS-1`: KAPANDI → kilitli,
KISMEN/AÇIK → açık, ERTELENDI ayrı durum, golden → belirsiz. Bekleyen sevkiyat tanımı AI-D35; `ai.decisions.read`
önerisi AI-D36 (grant insan kapısı).
