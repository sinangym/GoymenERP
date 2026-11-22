# Yapay zekâ sözleşmeleri (Y-A1)

| Dosya | Ne | Üretim / doğrulama |
|---|---|---|
| `decision-index-v1.json` | Karar dizini: `docs/decisions` altındaki sicil, açık kararlar, `SINAN_KARARLARI_*` ve formül golden'larından kimlik, başlık, metin, kaynak dosya + satır, durum, yürürlük tarihi, kapsam, ilişkiler, kişisel veri adayı (AI-D12, AI-D34 d, DALGA_PLANI_v2 A.4 (1)). | `python3 -I tools/generate_ai_decision_index.py` (yeniden üretir), `--check` (güncel değilse 1). Test: `tests/ai_decision_index`. |
| `decision-index-v1.schema.json` | Dizinin şeması. | Üretici her koşuda şemaya ve değişmezlere karşı doğrular. |
| `tool-catalog-v1.json` | Y-A1 okuma araçları: `decisions.lookup.v1`, `logistics.pending-shipments.v1` (Read, izinler, alan izin listesi, veri sınıfı). | `AiYa1ReadToolsTests` sözleşmeyi C# tanımlarıyla karşılaştırır. |
| `eval/eval-set-v1.json` | SENTETİK değerlendirme seti: 150 karar sorusu (bilgi çıkarma 70, güncellenen karar 30, cevap verememeyi bilme 30, yetki dışı 20) + 20 bekleyen sevkiyat senaryosu. | `python3 -I tools/evaluate_ai_eval_set.py` (modelsiz: şema, kaynak, araç, kişisel veri). Test: `tests/ai_eval_set`; sevkiyat senaryoları ayrıca `LogisticsPendingShipmentsAiToolTests` ile araca karşı koşar. |

Sınırlar: hiçbir model/sağlayıcı çağrısı, anahtar veya dış ağ yok; `Ai:Enabled=false` ve `Ai:AllowedTools=[]` kalır.
Karar aracı dizini yalnız `Ai:Knowledge:DecisionIndexPath` yapılandırılırsa okur (yoksa "kullanılamıyor"); dosyanın
paketlenmesi dağıtım kapısıdır. Dizin tren head'inde yeniden üretilir; karar PR'larından dizin üretimi beklenmez (dizin
bayatsa `tests/ai_decision_index` kırmızı olur). Durum eşlemesi `ASSUMPTION-AI-INDEX-STATUS-1`: KAPANDI → kilitli,
KISMEN/AÇIK → açık, ERTELENDI ayrı durum, golden → belirsiz. Bekleyen sevkiyat tanımı AI-D35; `ai.decisions.read`
önerisi AI-D36 (grant insan kapısı).

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

