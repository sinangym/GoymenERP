# W8 Satış — kaynak tamamlama

W8 son çalışma paketidir; yeni dalga açılmadı. Bu rapor kaynak durumudur, çalışma zamanı kabulü değildir.

## SAT-S13 — PARTIAL (entegrasyon bekliyor)

Kararlar: SAT-D343, SAT-D421, SAT-D424, SAT-D425. İrsaliye kg kaydı ve ekranı, gerçek Documents sürüm/hash kanıtı, teorik kg oranlı backend dağıtımı, en ağır ilk satıra artık ve faturaya kaynak satır kg'sının aynen aktarımı yazıldı. Birleşen faturada yeniden dağıtım yapılmaz. Fiziksel stok miktarı bu aktarımda değişmez. Belgesiz eski kayıtlara kg kaynağı uydurulmaz.

- Migration: `20261117010812_AddDispatchKilogramAllocations`, EF üretimi; nullable eski kayıt uyumu; Down ilk işlemde kaynak dağılımı varken **51973** ile reddeder.
- Yeni izin adı yok; mevcut Logistics record/read/approve sınırları kullanılır. Doküman erişimi actual owner ve company çitindedir. Belge seçimi, GİB doğrulaması veya dış sisteme gönderim değildir.
- Kaynak testleri: kesin dağıtım/precision, sahte/eksik/başka şirkete ait belge, source sürümü, replay, lost authority, HTTP wire/status, SQL immutable kayıt+attempt+audit/rollback/Down, Vitest ekran durumları ve aynı anahtar retry.
- Derleme: Api, Logistics.UnitTests, Sales.UnitTests, Api.IntegrationTests exit 0, 0 uyarı/hata. Api için `OpenApiGenerateDocumentsOnBuild=false` kullanıldı; OpenAPI üretimi merkezi entegrasyona kaldı. Frontend `npx --no-install tsc -b` exit 0. `git diff --check` exit 0.
- İlk Api denemesi yeni worktree'de restore bulunmadığı için NETSDK1004 verdi; restore içeren sonraki derleme başarılı oldu. Bu bir test sonucu değildir.
- Unit/SQL/HTTP/Vitest/browser/CI: **NOT_RUN** (GOV-D9). Veritabanı kurulmadı veya migration uygulanmadı.
- Entegrasyon: Shell/OpenAPI/recovery/architecture/ilişkisel envanter pinleri merkezi sırayla üretilecek. `KALAN_PLAN.json` DONE sayısı bu kaynak commit'iyle değişmedi.
- K3 review listesi: immutable JSON kaynak+strict decoder, gerçek Documents owner bağlama/sürüm sabitleme ve commit öncesi yeniden okuma, Sales kg taşıma ve physical quantity ayrımı, 51973 Down. Yeni tenant bypass allowlist girdisi eklenmedi.

SAT-S9, SAT-S12 ve SAT-SC34 ile sipariş kurunun gerçek üretim tüketicisine bağlanması mevcut açık W8 kapsamı olarak sürer.

### SAT-S13 — tek fatura kuru ve USD sevk günü sınırı

- 17 Kasım kullanıcı cevabı, Issue #1189 `6943483644` ajan aktarımıdır; doğrudan insan
  GitHub yorumu olarak sunulmaz. USD fiyatlı farklı İstanbul iş günlerinin sevkleri aynı
  fatura taslağına alınmaz; TCMB yayını veya kur aynı çıksa da gün sınırı korunur.
  Aynı İstanbul gününün UTC gece sınırındaki sevkleri birlikte kalabilir. TL sevklerine
  bu cevaptan yeni gün kısıtı çıkarılmadı; mevcut para birimi/vade/tek kur kontrolleri sürer.
- Üç birim test kaynağı ve Türkçe ret metni eklendi. API, Sales.UnitTests ve
  Api.IntegrationTests kaynak derlemeleri exit 0, uyarı/hata 0; TypeScript exit 0.
  Sales.UnitTests ilk no-restore denemesinde assets eksikti; yalnız ilgili proje restore
  edildikten sonra derlendi. Runtime Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**.
- Bu kayıt genel vergi mevzuatı yorumu değildir. Merkezi pinler ve trene entegrasyon
  tamamlanmadan SAT-S13 için DONE veya yüzde artışı yapılmadı.

## URT-SC36 bağlantısı — gerçek satış siparişi kur kaynağı

FNS-D73/D83/D107 için Sales owner okuma portu eklendi. Mevcut onaylı revizyon ve arşivlenmiş onaylı revizyon kendi satır kimliğiyle doğrulanır. Siparişte ayrı kur türü/tarihi alanı bulunmadığından FNS-D83'ün sipariş tarihi ve TCMB döviz satış varsayılanı, mevcut Ayarlar SAT-D54/D170 çözümleyicisine aktarılır. Tarih algoritması burada tekrarlanmaz. Üretimin talep ettiği kaynak para birimi açıkça iletilir; okunamayan sipariş veya kur için mal kabul kuruna geçilmez. TL stok maliyeti değiştirilmez.

- Üretim Application sözleşmesi bağımlılığı: `64d0e3495` (bu dalda `9d256f2e3`); kesin oran türü üretim yazarının `f65b9026b` kaynağıyla aynıdır.
- Kaynak testleri: gerçek Sales owner, ilk revizyonun sıfır olması, arşivlenmiş onay, yanlış şirket/satır/revizyon, değişen kaynak sürümü, aktör/şirket kaybı, kur hassasiyeti ve iptal. Gerçek SQL fact'i actual Sales store üzerinden kalıcı kayıt okumasını ve yazma oluşmamasını kapsar.
- Api, Sales.UnitTests ve Api.IntegrationTests derlemeleri exit 0; 0 uyarı/hata. İlk Integration derlemesindeki sentetik şirket bağlamının eksik arayüz üyesi tamamlandı; yeniden derleme exit 0. OpenAPI üretimi kapalı, merkezi entegrasyon bekliyor.
- Test çalıştırılması, SQL ve CI: **NOT_RUN**. Yeni migration, izin veya grant yok. Üretimin gerçek tüketicisiyle birleştirme ve pin üretimi tamamlanmadan dilim DONE sayılmaz.
