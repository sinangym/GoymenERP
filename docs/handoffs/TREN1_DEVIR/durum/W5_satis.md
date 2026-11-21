# satis — dalga 5 lider raporu

Dal: `claude/tren-1-satis` (taban 318c02000, head c1b159bc2, push edildi). PR yok, CI yok.

## Birleştirilen dilimler
- SAT-SC33 (DONE): SAT-D406 plan tabanlı risk kalanı (yeni `ISalesOrderPlannedQuantityReadPortV1` + host adaptörü), termin izni başlık VEYA `sales.orders.delivery-schedule.write`, inbox kaydı.
- SAT-SC34 (PARTIAL): SAT-D376/D377/D402 fason ortak tartı teorik kg paylaştırma calculator'ı + golden; KLN-D106 açık miktar sevk toplamından.
- SAT-S2 (DONE): müşteri iadesi kesinleştirmesi Finans kabul portu v2 üzerinden, taksit okuma GET + FNS-D81 422, outbox `sales.return-invoice.finalized.v1` aynı save'de, ekran.
- SAT-S10 (DONE): `SalesLogisticsShipmentInboxConsumer` Work inbox tüketicisi olarak host'ta kayıtlı (dispatcher kapalı), ChangeTracker.Clear, real-SQL fact.
- SAT-S13 (PARTIAL): kantar farkı okuma GET + fatura kapısı (SAT-D389/D405, fark ≠ 0 ise `weighing_kg_transfer_pending` fail-closed). Ekran yok.

Lider düzeltmesi (c1b159bc2): SC33 ve S10 aynı inbox tüketicisini iki kez kaydediyordu (başlangıçta exception). S10 kaydı ve testleri kaldı; SC33'ten yalnız plan adaptörü `SalesRuntime/SalesOrderPlannedQuantityReadHost.cs`'e taşındı, SC33'ün kopya kayıt testi silindi. Çakışmalar yalnız yorum/Program.cs'teydi; pinli dosyada çakışma yok.

## Atlanan
- SAT-S8 (BLOCKED, commit yok): X1 miktar azaltma/satır silmede gelecek tahsis serbest bırakma Stok portunda demand-level reduce işlemi gerektiriyor; zamanlama (revizyon/gönder/onay) ve SAT-D115 geri alma açık. SAT-D373 geçici red sürüyor.

## Derleme (GOV-D10, flock, `-nr:false -p:UseSharedCompilation=false`)
- GoymenERP.Api: C# derlendi; bayraksız build-time OpenAPI adımı bilinen "Shell capability provider seti … eşleşmiyor" ile düştü (taban kaynaklı, shell authority pin'i).
- `-p:OpenApiGenerateDocumentsOnBuild=false` ile GoymenERP.Sales.UnitTests ve GoymenERP.Api.IntegrationTests (Api dahil): 0 hata, 0 uyarı.
- Frontend `tsc -b`: exit 0. bin/obj temizlendi. Testler koşulmadı.

## regen_needed (birleşik)
openapi (+OpenApiContractGenerationTests: S2 taksit GET/422, S13 kantar farkı GET/DTO/503, SC34 409 kodları), recovery-inventory + release-manifest, architecture-guard:PlatformTenantQueryFilterBypassGuardTests.cs (SalesModuleExtensions, SalesStore.CustomerReturns/RiskExceptionUsages, relational_inventory/sales.txt), relational-inventory:sales (S10 fact eklendi, doğrula), test grup/discovery pin'leri (yeni test sınıfları; SC33 kayıt testi silindi), shell-capability-registry (taban kaynaklı).

## NEEDS-SINAN
- SAT-S8 X1: miktar azaltma/satır silmede tahsis serbest bırakma ne zaman (revizyon kaydı/gönderim/onay) ve müşteri reddinde (SAT-D115) nasıl geri alınır?
- SAT-S8 X1: Stok `IInventoryFutureAllocationReleasePortV1` demand-level reduce işlemini hangi stok_lojistik dilimi, hangi dalgada yapar? (STK-S17 dalga 6 SAT-S8'e bağlı; döngü.)
- SAT-S8 X1: release katılımcısında "bulunamadı/başka şirket" terminal (kayıtlı) red sayılsın mı?
- SAT-D406: ThisOrder deltası da plan tabanlı mı olmalı (şimdi onaylı − sevk)?
- SAT-D406: plan miktarı satır başına onaylı miktarla sınırlansın mı (şimdi sınırsız)?
- SAT-D406: sipariş biriminden farklı birimli plan satırı fail-closed reddediliyor; Lojistik sipariş biriminde mi cevaplasın, Satış mı çevirsin?
- SAT-D400: termin için shell capability de VEYA olsun mu (ShellCapabilityRegistryAuthorities regen ister)? (İzin VEYA'sının kendisi EK20 SAT-D400 ile cevaplı.)
- SAT-D376: teorik kg kaynağı Lojistik `LoadedKgAtWeighing` (WGT-002) — doğru mu?
- SAT-S2: müşteri iadesinin Finans BusinessDate'i kesinleştirme günü mü (şimdi) müşteri belge tarihi mi?
- SAT-S2: önce Finans'a sun sonra Satış'ta commit (SAT-D294, kodlanan) mı, önce Satış commit + outbox (SAT-D208) mı?
- SAT-S13: fark ≠ 0 iken ürün kantar kg'ı birden çok fatura satırına nasıl bölünür (SAT-D322 (3)-(4) en ağır satır kuralı)?
- SAT-S13: SAT-D343 irsaliye toplam oranının üstüne ürün başına ayrı fark üst sınırı olsun mu?
- SAT-S10: başlık "iptalde geri açılma" diyor, kilitli SAT-D285 düşürme diyor; SAT-D285 uygulandı. Geri açma istenirse yeni karar gerekir.
- (Bilgi, yeni değil) LOJ-D19 iptal edilmiş sevk olayının üreticisi yok (STK-S5).

## Mesajlar
- Gönderilen (işçiler): `satis__stok_lojistik__SAT-S8-x1-demand-reduce.md`, `satis__stok_lojistik__sat-d378-faturali-tarti.md`, `satis__stok_lojistik__sat-s13-fatura-kg-satir-okumasi.md`.
- Yanıtlanan: `finans__satis__fin-s13-invoice-finalized-accounting-fields.md` (dalga 6'ya alındı), `koordinator__satis__SC36-kararlari.md`, `koordinator__satis__SC37-kararlari.md`.

## Sonraki dalga bilmeli
- FIN-S13 isteği (Satış'a ait, yapılmadı): `sales.invoice.finalized.v1` sonuna `invoiceDate` + `finalizedBy`; Satış outbox'ını Work kaynağı `"sales"` olarak kaydet; KLN-D18/KLN-D103 senaryo alanı + migration.
- SC34 kalanları: SAT-D380 kur snapshot (migration), SAT-D381/D395 fason KDV/tevkifat varsayılanı (`purchasing.orders.approve`, SC37) + denetimli satır override, SAT-D379/D399 vade esası, KLN-D110 tahsis serbest bırakıldı tüketicisi, KLN-D102, KLN-D105, SAT-D373.
- SAT-S13: ekran ve fark ≠ 0 kg taşıma Lojistik V2 satır kantar kg okumasını bekliyor; S13 kapısı kayıtsız irsaliyeli mevcut fikstürlerde 409 verir (K3 davranış değişikliği).
- SAT-S8 X1 stok portu ve kararlar gelmeden yapılamaz.
- Lider derlemesi shell pin'i yenilenene kadar `-p:OpenApiGenerateDocumentsOnBuild=false` ile.
- Önceki lider worktree'si `lead/satis-w4` detach edildi (dal bu worktree'ye alınabilsin diye); dosyaları duruyor. SAT-S8 worktree'si de (değişiklik yoktu) kaldırıldı; tüm yerel dallar duruyor.
