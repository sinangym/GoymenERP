# Üretim — dalga 6

Kod dalı: `codex/tren-1-uretim-w6`. Kaynak head: `1697ca52e25fa2aae5e99d57474c2e222a2406ec`.

Bu rapordaki DONE kaynak kapsamının tamamlandığını anlatır. Birim, SQL, Vitest, tarayıcı, CI ve K3 kabul kanıtı değildir; bu koşumlar GOV-D9/GOV-D10 gereği son birleşik turdadır. Önceki 18/27 kaynak DONE sayısına URT-S6, URT-S7 ve URT-S18 eklendiğinde 21/27 (%77,8); tren toplamını entegrasyon lideri hesaplar.

| Dilim | Durum | Commitler | Sonuç ve kalan |
|---|---|---|---|
| URT-S2 takip | DONE | `13ae2d974`, `bfbfc265f` | Yeni çıktının açıkça seçilmiş plan satırını API, hash, port ve host üzerinden domain'e taşıması sağlandı; eski satırsız çıktı hash/replay'i korunur. Emir okuması seçilebilir satırları verir. Gerçek SQL commit/replay ve iki modüllü rollback fact kaynakları yazıldı. |
| URT-S4 | PARTIAL | `1ff509e09`, `4283d8900`, `1697ca52e` | 51870 Down guard fact'i; açık etiket seçimi, yeniden onay istemcisi/formu ve geçmişi; eski tahsis provenansı eksikse ret. Yeni etiket revizyonundan sonra tahsis provenansı artık iptal edilen emrin sabit girdilerinden okunur. Inventory'de aktif eski tahsis kontrolü vardır. Kalan: seçim satırlarında serbest kg/eksik miktar tablosu, bütün yeniden onay yarışlarının gerçek SQL kabulü. |
| URT-S5 | PARTIAL | `1697ca52e` | Emri iptal edilen aynı planın etiketleri değişir; önceki emir sabit kalır. İlk Ayarlar profili, hedef, iş merkezi, süreç ayrıntıları ve ticari sahiplik korunur. Güncel serbest kg Stok'un mevcut portundan okunur; eski aktif tahsis varsa host yazmaz. Yeni teknik snapshot ile mevcut backend dilme kapısı çalışır. Revizyon, bırakma ve yeniden onay PlanVersion sırasıyla geri yüklenir; mevcut append-only revizyon şeması yeterlidir. UI yeniden hesap modunda sabit alanları kilitler ve backend önizlemesine saklı dilme/süreç/hız verisini taşır. Kalan: yeni etiket ve aynı anahtarın bütün hata/yarış yollarının uygulama/host SQL kabulü; genel taslak girişinde AT-044 serbest kg sınırının ayrıca bağlanması. |
| URT-S6 | DONE | `7ccd65070`, `13ae2d974` | Cancelled decoder/filtre/etiket, iptal izi, terminal emirde yazma kontrolleri, çıktı satırı seçimi. Shell provider düzeltme ve iptal yeteneklerini yayınlar. |
| URT-S7 | DONE | `31a7add9f`, `d486b1ac8` | Owner geçmiş portu iptal edilmiş emirleri taşır. Host ve Satış tüketicisi bunları açık tedarikten ayrı HistoricalItems/CancelledOrderIds ile gösterir; açık kg/iptal etkisi hesaplarına katmaz. Sales kaynak plan ekranı iptal geçmişini gösterir. Owner SQL ve host/service/decoder/UI test kaynakları yazıldı. |
| URT-S18 | DONE | `52d6cca76` | Müşteri malı emrin şirket/müşteri/kapanış/net çıktı etiketleri owner okuması; Lojistik tüketici sözleşmesi ve host adaptörü, company fence'leri ve test kaynakları. STK-S3 tüketicisi ile sözleşme eşlendi. Merkezi DI kaydı entegrasyon liderindedir. |
| URT-SC32 | PARTIAL | kodda doğrulama | DOGRULA incelemesinde USD hareketli ortalama/üretim maliyeti runtime akışının tamam olmadığı görüldü. Üretim maliyet source portunun somut sağlayıcısı ve runtime kaydı yok. Sahte veya güncel kur varsayılanı eklenmedi; DONE yapılmamalı. |
| URT-SC33 | PARTIAL | `e63cb10ca`, `5708d5703` | Kuyrukta iptal geçmişi tek açık emir seçimini bozmaz; kalan kg ve şirket sınırı için SQL fact yazıldı. URT-D106, eski URT-D101'in izin kararının üstündedir: ayrı manufacturing.plans.reorder yalnız kayıt, manifest/test ve provider 1.11.19 birlikte değişti. Kuyruk yazma ucu olmadığı için reorder yeteneği ImplementationAvailable=false kalır. Kalan: kalıcı öncelik/sıra taşıma komutu + migration, denetim/concurrency/idempotency ve UI; şirket tampon girişi. |
| URT-SC36 | PARTIAL | `b91ec0fec` | URT-D108/D109 boya iade sınırı emir toplamında; çıktı ters kaydı ve son WIP ile golden test eklendi. Kur önceliği calculator'ına maliyet source adaptörü hâlâ bağlı değil. STK-SC36-b gerçek ilk mal kabul kur portu entegre edilince kaynak doğrulanmalı. |
| URT-S8 | READY / W7 | — | Lider önceliğiyle bu dalgada başlanmadı. ReopenOrder, muhasebe dönem kontrolü ve ikinci maliyet hesabı W7'ye kaldı. |

## Derleme ve koşum

- `dotnet build tests/GoymenERP.Manufacturing.UnitTests --no-restore -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false`: exit 0, 0 uyarı, 0 hata.
- `dotnet build tests/GoymenERP.Api.IntegrationTests --no-restore -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false`: Api dahil exit 0, 0 uyarı, 0 hata.
- `dotnet build tests/GoymenERP.Sales.UnitTests --no-restore -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false`: exit 0, 0 uyarı, 0 hata.
- Kaynak düzeltmesinden önce görülen iki Task dönüş tipi ve iki ClassLine çağrı hatası düzeltildi; son derlemeler temizdir.
- Unit / gerçek SQL / Vitest / tarayıcı / CI: **NOT_RUN**. Frontend tsc: **NOT_RUN** (ekip çalışma alanında bağımlılıklar kurulu değil; birleşik turda çalıştırılacak).
- `git diff --check`: exit 0.
- Yeni migration / THROW: yok. S4 mevcut 51870 numarasını test eder; numara ayrılmadı.

## Entegrasyon ve yeniden üretim

- Program: `ILogisticsSubcontractOrderReadPortV1` -> `ManufacturingLogisticsSubcontractOrderReadPort`; Manufacturing owner kaydı modül uzantısındadır. STK-S3 şirket/müşteri/etiket üyeliği kontrolünü bu portla tüketir; Product/version ve fiziksel ownership kendi owner portlarından doğrulanır.
- Shared shell dosyalarına dokunulmadı; Manufacturing provider sürümü 1.11, kontrol sayısı 19. Yeni izin `manufacturing.plans.reorder` yalnız kayıttır; grant, rol ataması ve Critical değişikliği yok.
- Merkezi ShellCapabilityRegistryAuthorities, OpenAPI/contract generated dosyalar, recovery/release, architecture fingerprint, ilişkisel envanter ve test grupları liderde yeniden üretilecek.
- Yeni SQL fact tam adlarının ön eki `GoymenERP.Api.IntegrationTests.ManufacturingSqlServerRelationalAcceptanceTests.`; son ekleri:
  - `A_cancelled_linked_order_is_retained_in_history_but_is_not_open_supply`
  - `The_order_cancellation_Down_passes_with_open_orders_and_refuses_without_losing_a_cancelled_order`
  - `A_legacy_cancelled_plan_without_allocation_provenance_cannot_start_a_new_allocation_generation`
  - `A_cancelled_orders_plan_revision_roundtrips_new_labels_without_rewriting_the_cancelled_order_or_first_approval`
  - `The_work_center_queue_reads_remaining_mass_and_ignores_cancelled_order_history_in_the_resolved_company`
  - `An_output_correction_commits_both_owners_once_and_replays_the_original_fact_and_movement`
  - `An_output_correction_failure_after_inventory_staging_rolls_back_both_facts_audits_and_versions`
- Satış tüketici dosyaları: SalesOrderCancellationImpactContractsV1, SalesDraftService.Cancellation, SalesDraftSourcePlanContractsV1 / SourcePlan dönüşü, sourcePlan decoder ve ekranı; ilgili host/service/Vitest kaynakları. Entegrasyonda Satış lane değişiklikleriyle iki ek alan korunmalı.
- Yeni ham SQL / tenant filter bypass izin listesi eklenmedi. Önceki K3 review bekleyen girdilere dokunulmadı.

## Karar ve insan kapıları

Yeni işletme kararı tahmin edilmedi. Önceki W5 sorularını yeni cevap varmış gibi kapatmadık. Üretim maliyeti kaynağı eksikliği teknik uygulama açığıdır; yeni bir para/kur varsayılanı veya production aktivasyonu değildir. İzin grantleri ve bağımsız K3 review insan kapısında kalır.
