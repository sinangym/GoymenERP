# Yapay zekâ sözleşmeleri (Y-A1, WP-A4)

| Dosya | Ne | Üretim / doğrulama |
|---|---|---|
| `decision-index-v1.json` | Karar dizini: `docs/decisions` altındaki sicil, açık kararlar, `SINAN_KARARLARI_*` ve formül golden'larından kimlik, başlık, metin, kaynak dosya + satır, durum, yürürlük tarihi, kapsam, ilişkiler, kişisel veri adayı (AI-D12, AI-D34 d, DALGA_PLANI_v2 A.4 (1)). | `python3 -I tools/generate_ai_decision_index.py` (yeniden üretir), `--check` (güncel değilse 1). Test: `tests/ai_decision_index`. |
| `decision-index-v1.schema.json` | Dizinin şeması. | Üretici her koşuda şemaya ve değişmezlere karşı doğrular. |
| `release-knowledge-v1.json` | Üçüncü kaynak (WP-A4, A.4 (3)): yayındaki sürümün davranış bilgisi. Yalnız `durum: yayinda` olan **ve** kanıt dosyası bu ağaçta ifadesiyle bulunan notlar metniyle girer; uygulanmamış özellik yalnız kimlik + neden olarak listelenir. | `python3 -I tools/generate_ai_release_knowledge.py` (`--check`). Kaynak: `docs/release-knowledge/RK-*.md`. Test: `tests/ai_release_knowledge`. |
| `release-knowledge-v1.schema.json` | Yayın bilgisi paketinin şeması. | Üretici her koşuda doğrular. |
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
