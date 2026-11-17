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
