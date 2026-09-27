# l04: yükleme talimatı kullanıcı yolu, gerçek tarayıcı kabulü (`browser:l04:ci`)

**Sahip:** SİPARİŞ (Logistics / Sales şeridi).
**Ekranlar:** LOG-4b, `/lojistik/yukleme-talimatlari` (L-02 liste), `/lojistik/yukleme-talimatlari/yeni` (L-03 yeni), `/lojistik/yukleme-talimatlari/:instructionId` (L-02-DETAIL detay + yükleme payı formu); master v3 §27.8.
**Kaynak:** LOG-4 kartı D1–D7 (`tmp/log4-card.md`), koordinatörün LOG-4b brifingi, Business 5737790175 (talimat → yükleme payı, tek stok/tahsis etkisi LOG-5'te), Business 5743412166 (Sevkiyatçı kuralı: ekranda fiyat/finans alanı yok).

## Ne ölçer
- **Stack:** gerçek Chromium, exact head'in web build'i, gerçek API, gerçek SQL Server, gerçek Settings kg profili, gerçek Cari ve Sales portları.
- **Fixture kapsamı (yalnız stage-2 seed, `tests/GoymenERP.BrowserSuiteFixtureTool/Suites/L04`):**
  1. BİR Cari müşteri (parti + şirket hesabı) — gerçek Counterparty API'si, `group-bootstrap-admin` (fin01 yolu).
  2. BİR Sales taslağı — kapılı Sales fixture yazıcısı (W1/C: sentetik ürün/para snapshot'ları, deneme/audit yok) GERÇEK müşteri kimlikleri ve GERÇEK Settings kg birimiyle; sonra iki Sales aktörüyle gerçek Sales API'sinden **gönderilir ve onaylanır** (Ö4).
  3. BİR yükleme talimatı (onaylı satıra iki satır) — gerçek Logistics API'sinden hazırlayan aktörle **oluşturulur ve yayımlanır**; ikinci satırı kapılı Logistics fixture yazıcısıyla **bloklanır** — 4a'da hiçbir API'nin üretmediği tek durum (D2 blok kaynağı LOG-5'te).
- **Fiyatsız yol (deny-first, Sinan §4 #66 5738113623):** hazırlayan **hiç Sales grant'i taşımaz**; seçici, `logistics.loading-instructions.create` altındaki FİYATSIZ Logistics okumasını (`GET /api/v1/logistics/loading-instructions/approved-order-lines`) kullanır; Sales taslak rotaları hazırlayana 403 verir ve fiyatsız okumanın JSON'unda hiçbir fiyat anahtarı yoktur (spec ağacı gezerek doğrular).
- **Müşteri adı (Business 5745076917):** müşteri seçicisi fiyatsız okumanın taşıdığı Cari ADINI (`customerName`: ticari ad, yoksa yasal ad) gösterir; spec müşteriyi bu görünür metinle seçer (kimlikle ya da belge numarasıyla değil).
- **Pozitif yol:** hazırlayan, normal ekranda müşteriyi ve onaylı satırı **görünür etiketiyle** seçer (fixture kimliğiyle değil), "4.5" miktar metnini yazar (Ö2, birebir); ilk cevabın teslimi kesilir, aynı anahtarla yeniden gönderim replay olur (`Idempotency-Replayed`); detay gerçek okumadır; talimat yayımlanır; yükleyici pay kaydeder.
- **Negatifler:** kapak aşımı (409 `logistics_load_exceeds_instructed`), bayat sekme (409 `version_conflict`, yeniden okuma), aynı yükleme kimliği yeni anahtarla (409 `logistics_duplicate_share_id`), bloklu satır (formda seçilemez + gerçek rotada 409 `logistics_line_blocked`), grant'siz oluşturma rotası kapalı, okuyucunun pay gönderimi 403 (hiçbir şey kaydedilmez), diğer şirkette hazırlayan hiçbir şey görmez.
- **AT-024 (sipariş kalanı, L04-05):** kesin sevkten sonra, teslimden önce ve sonra seed'in Sales onaylayıcısı (`l04-sales-approver`, override'daki deterministik kimlik; `sales.drafts.read`, fiyat/oluşturma yok) S-04 "Teslimat durumu"nu kendi sayfasında açar: önce sevk "2" / teslim "0" / kalan "6"; teslimden sonra yeniden açılışta ve sayfa yenilemede sipariş "6" / sevk "2" / teslim "1.5" / kalan "4.5" / fazla "0" (birebir metin); aynı teslim tarayıcının gönderdiği anahtar ve gövdeyle gerçek rotadan yeniden gönderilir (200 + `Idempotency-Replayed`), ikinci teslim payı oluşmaz ve S-04 yine "1.5" gösterir.
- **Fiyat yok:** her ekranın metninde fiyat/finans sözcüğü aranır ve bulunmaz.
- **Mock yok:** yalnız kimlik başlığı (X-Development-User) ve BİR vakada teslim kesintisi. Hiçbir durum ya da gövde yerelde üretilmez.

## Girdiler (varsayılan yok)
- BROWSER-CI sözleşmesi: `BROWSER_E2E_AUTHORIZED_BASE_URL`, `BROWSER_E2E_API_BASE_URL`, `BROWSER_E2E_ARTIFACTS_DIR`, `BROWSER_E2E_JUNIT_FILE`, `BROWSER_E2E_TESTED_COMMIT`.
- `BROWSER_E2E_L04_SEED` (JSON): `preparerActor`, `loaderActor`, `readerActor`, `companyId`, `otherCompanyId`, `customerPartyId`, `companyAccountId`, `customerName`, `draftId`, `approvedLineId`, `instructionId`, `blockedLineId`, `openLineId` (`src/env.ts`).
  - Aktörler ve grant'ler `infra/browser-suites/l04.override.yml`'de: hazırlayan `logistics.loading-instructions.create` + `.read` (Sales grant'i yok); yükleyici `logistics.loads.record` + `.read`; okuyucu yalnız `.read`; iki Sales aktörü yalnız seed için.
- **Kabiliyet profili:** önce `log-staged` (Logistics ekranları normal runtime'da staged; disposable zarf yalnız bu üç kabiliyeti açar — FIN-UI-1c deseni), l04 yeşil olunca ayrı bir exact-head değişikliğiyle `normal`.

## Engeller (bu dosyanın sahibi dışında)
- Merkezi satırlar (CARİ): shell mount + `capabilities.ts` + `SHARED_MOUNT_HANDOFF.md`; `l04` suite kaydı (workflow case + `browser:l04` etiketi, `SUITES` / `SUITE_PROFILES` → `log-staged`, `SUITE_API_EXTENSIONS`, `SUITE_IDENTITY_PERMISSIONS`, seed alanları); `log-staged` profili (aktivasyon tablosu, compose dosyası, authority sabiti, validator tabloları, activation-boundary satırları); Logistics blok fixture yazıcısının aktivasyon kaydı; `logistics.txt` pini.
- Bunlar gelene kadar suite koşmaz; koşarsa girdileri reddeder. Yerel koşu yok (Docker WSL'de yok, yuva yok); ilk kanıt etiketli BROWSER-CI koşusudur.
