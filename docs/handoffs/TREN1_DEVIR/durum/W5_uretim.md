# uretim — dalga 5 lider raporu

Dal: `claude/tren-1-uretim` (taban 318c02000), head `44b8be52b`, push edildi (PR yok, CI yok).
Önceki lider worktree'si (uretim-w4) dalı serbest bırakmak için detached HEAD'e alındı.

## Merge edilen dilimler (`--no-ff`)
- URT-S2 (ab274d176) — çıktı düzeltme ucu `POST /orders/{orderId}/facts/{factId}/correction`, izin `manufacturing.facts.correct` (yalnız kayıt).
- URT-S4 (0b57af435) — emir iptali `POST /orders/{orderId}/cancellation`, plan yeniden onayı `POST /plans/{planId}/reapproval`, izin `manufacturing.orders.cancel` (yalnız kayıt).
- URT-SC33 (bbb82da1d) — termin önerisi Ayarlar iş merkezi kapasitesini okur, kuyruk sırası + kalan kg.
- URT-SC36 (f0efbaebb) — girdi kur önceliği calculator'ı (adaptörsüz), iade sınırı golden, etiket kodu girişi kuralı.
- URT-S6 (81f7f35c8) — frontend çıktı düzeltme formu + emir iptal kontrolü.

Atlanan dilim yok.

## Lider düzeltmeleri
- `ManufacturingApiV1.cs` birincil kurucu çakışması (S2/S4/SC33): üç tarafın opsiyonel parametreleri birlikte tutuldu.
- bbadf1643: frontend iptal istemcisi taslak `/cancel` rotasından URT-S4'ün `/cancellation` rotasına çekildi (test dahil).
- 44b8be52b: eksik `AddManufacturingOrderCancellation` migration'ı dotnet-ef ile üretildi (20261116104108); Down önce
  `THROW 51870` ile reddeder (iptal edilmiş emir / PlanReapprovals satırı / planda >1 tahsis PlanVersion varken), sonra
  drop. PersistenceModelTests'e guard fact'i ve README satırı eklendi.

## Derleme (GOV-D10, flock, `-nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false`)
- `tests/GoymenERP.Manufacturing.UnitTests`: 0 hata / 0 uyarı.
- `tests/GoymenERP.Api.IntegrationTests` (API dahil): 0 hata / 0 uyarı.
- İlk API derlemesi OpenAPI build-time adımında beklenen shell authority pin uyuşmazlığıyla düştü (derleme kendisi temiz).
- Frontend `tsc -b`: exit 0. bin/obj silindi. Testler koşulmadı.

## regen_needed (birleşik)
- openapi (yeni uçlar: CorrectManufacturingOutputV1, sipariş iptali, plan yeniden onayı; yeni 409/422 kodları, declare-output 422 `manufacturing_output_label_code_entry_not_allowed`)
- recovery-inventory + release-manifest (src/backend değişti, yeni migration + PlanReapprovals tablosu)
- architecture-guard:PlatformTenantQueryFilterBypassGuardTests.cs (ManufacturingInventoryReturnOutputHost 3 GetDbConnection; ManufacturingDbContext; ManufacturingStore; yeni migration `Sql` → ReviewedMigrationMutationSources)
- shell-capability-registry (iki yeni izin; provider kontrolleri `manufacturing.orders.output-correction`, `manufacturing.orders.cancel` henüz yayınlanmıyor)
- frontend generated client (OpenAPI'den üretiliyorsa)

## NEEDS-SINAN
- `manufacturing.facts.correct` hangi persona/rolde? (grant yok, uç 403)
- `manufacturing.orders.cancel` hangi rolde? (grant yok)
- İlk onayda Settings anlık görüntüsü yoksa yeniden onay reddediliyor — amaçlanan politika mı?
- Geçmiş termin yeniden onayı engellemeli mi?
- İptalle serbest kalan plan yeniden onaylanana kadar iş merkezi kuyruğundan çıkmalı mı?
- URT-D96: kesirli gün tamponu (1.5 gün) reddediliyor — doğru mu, yoksa yuvarlama mı?
- KIM-SC33-b: plan onayında kapasite kaydı yakalanmalı mı?
- FNS-D83/EK20: bağlı emri ve ilk mal kabul kuru olmayan döviz girdisi "hesaplanamadı" mı kalsın?
- Kuru okunamayan emre bağlı girdi ilk mal kabul kuruna düşmeden reddediliyor — doğru mu?
- URT-D98: tanımlı ama kullanılamaz Ayarlar biçimi çıktı bildirimini engelliyor — amaçlanan mı?
(URT-D97/D99/D100/D101 SC36/SC37 kararlarıyla kapandı; ayrı `manufacturing.plans.reorder` izni kaydedilmez — D101 = plans.approve.)

## Mesajlar
Yanıtlanan: `koordinator__uretim__SC36-kararlari`, `koordinator__uretim__SC37-kararlari`,
`stok_lojistik__uretim__sc36-c-paket-standardi-v3`, `uretim__uretim__urt-s6-iptal-ucu-ve-yetenek`.
İşçilerin gönderdiği: `uretim__kimlik__sirket-varsayilan-guvenlik-payi-portu`, `uretim__stok_lojistik__ilk-mal-kabul-kuru-portu`.

## Sonraki dalga bilmeli
- `OrderViewV1` iptal alanlarını taşımıyor; `Cancelled` durumu frontend decoder/etiket/filtreye eklenmeli (şu an iptal sonrası okuma malformed).
- Shell provider iki kontrol kimliğini yayınlamalı + authority yeniden türetimi.
- Yeniden onay, Inventory'de artık tahsis kalıp kalmadığını kontrol etmiyor.
- Çıktı düzeltme host transaction'ı için gerçek-SQL/HTTP kabul testi yok; iptal migration'ının Down guard'ı için gerçek-SQL fact'i yok (K3 öncesi gerekli). SC33 kuyruk okuması için gerçek-SQL fact'i yok.
- SC36 kur önceliği calculator'ı hiçbir porta bağlı değil (STK-SC36-b ilk mal kabul kuru portu bekleniyor).
- URT-D100/D101: şirket tamponu girişi ve elle sıra taşıma `manufacturing.plans.approve` ile yapılacak (öncelik puanı kolonu + migration).
- Paket standardı tüketicileri `IProductPackageStandardReaderV3`'e taşınmalı (STK-SC36-c merge sonrası).
- THROW 51870 bu migration'a ayrıldı.
