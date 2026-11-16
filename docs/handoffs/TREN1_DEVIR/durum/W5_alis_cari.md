# alis_cari — dalga 5 durum raporu

Dal: `claude/tren-1-alis_cari` (base 318c02000) → head 161d4ae03, push edildi. PR yok.

## Merge edilen dilimler
- ALC-SC33 (DONE) — sipariş kuru (FNS-D73/D79/D80/D83/D91), migration AddPurchaseOrderExchangeRate (THROW 51833).
- ALC-SC34 (DONE) — KLN-D94/D110 batch futureSupplyReleases + LinkFailed sonrası tahsis süpürme.
- ALC-S3 (DONE) — F17-4 bağ düşürme + tahsis korumalı iptal/kapama, AllocationLinkPanel; yeni izin `purchasing.future-allocation.release`.
- ALC-S5 (PARTIAL) — tedarikçi iadesi + maliyet istisnası onay/ret, migration AddPurchaseSupplierReturns (THROW 51893), salt-okur SupplierReturnPanel; yeni izin `purchasing.supplier-returns.record`. Eksik: API-44 kabul ters kaydı, oluştur/onay UI, real-SQL Down testi.
- ALC-SC47 (PARTIAL) — cari yasal ad maker-checker (ALS-D111/D113), migration AddCounterpartyLegalNameChangeRequests (THROW 51847); ALS-D112 yapılmadı.
Atlanan dilim yok.

## Çakışmalar (kaynak, iki taraf korundu)
PurchasingStoreContracts (komut adları/IsOrderCommand/IsOrderMutation), PurchasingService ctor, PurchasingModuleExtensions,
ErpPermissions, PurchasingPermissionManifestV1.Names (12 → 13 ad), PurchasingServiceBehavior izin parametreleri, izin testleri,
PurchasingPersistenceModelTests migration listesi (TakeLast(11), ExchangeRate + SupplierReturns).
Lead düzeltmeleri: iki API test stub'ına ChangeOrderExchangeRateAsync/GetOrderExchangeRateAsync; OrderExchangeRate real-SQL
fact'inde nullable struct `.Value`; AddPurchaseSupplierReturns.Designer hedef modeline SC33 tablosu eklendi (snapshot ile birebir).

## Derleme
- `dotnet build` (flock, UseSharedCompilation=false, `-p:OpenApiGenerateDocumentsOnBuild=false` — base'deki shell registry
  drift'i build-time OpenAPI'yi kırdığı için yalnız komut satırında kapatıldı): Api.IntegrationTests (Api + tüm modüller dahil),
  Purchasing.UnitTests, Counterparty.UnitTests, Identity.UnitTests → 0 hata. bin/obj silindi.
- Frontend `tsc -b` → exit 0. Test koşulmadı (SINAN-CEVAP 13).

## regen_needed (birleşik)
openapi (+OpenApiContractGenerationTests; SC47 eski PUT legal-name ve canUpdateLegalName referansları dahil) ·
recovery-inventory + release-manifest (3 yeni migration: Purchasing ×2, Counterparty ×1) ·
architecture-guard: PlatformTenantQueryFilterBypassGuardTests (PurchasingDbContext, PurchasingStore, PurchaseSupplierReturnStore,
PurchaseAcceptanceParticipant, PurchaseOrderExchangeRateReadPort, PurchasingModuleExtensions, CounterpartyDbContext, Api host
participant'ları; ReviewedMigrationMutationSources: AddPurchaseOrderExchangeRate, AddPurchaseSupplierReturns,
AddCounterpartyLegalNameChangeRequests Sql=1) · relational-inventory:purchasing, relational-inventory:counterparty ·
test-naming-and-groups (yeni sınıflar/fact'ler) · shell-capability-registry (ShellCapabilityRegistryAuthorities; base'de zaten
kırık, ayrıca 3 yeni Purchasing + 2 Counterparty izni).

## NEEDS-SINAN
- SC33: kur değiştirme izni (şimdi purchasing.orders.create); onay sonrası kur revizyonu akışı; varsayılan kur günü
  (oluşturma günü mü, EffectiveOn/teslim mi); kur çözülemezse oluşturma reddedilsin mi.
- SC34: KLN-D110 süpürmede ayrı genel reason kodu gerekli mi; released.v1 payload'ına AcceptanceId/ham kod eklensin mi.
- S3: ALS-D83 reasonText sınırı (Purchasing mı Inventory mi); §3.6 düz kapama aktif bağda 409 doğru mu; ALS-D85 miktar azaltma guard'ı.
- S5: SAT-D396 aynı kişi istisnayı karara bağlayabilir mi; SAT-D392 "ilgili fatura" kaynağı; SAT-D371/D393 onaylı istisna
  cost_difference_unposted'ı aşar mı; ALS-D91 yerine-mal iadesi akışı.
- SC47: legal_name.propose/approve grant'ları (insan kapısı); ALS-D112 mekaniği; stale kuralı.

## Mesajlar
Gönderilen (işçiler): alis_cari__finans__ALC-SC33-order-rate-rated.md, alis_cari__stok_lojistik__ALC-SC34-kln-d110-supurme.md,
alis_cari__satis__ALC-SC34-kln-d94-sorumlu-uyarisi.md (Ek), alis_cari__kimlik__ALC-SC47-legal-name-permissions.md.
Yanıtlanan (lead): finans__alis_cari__fin-s13-purchasing-invoice-finalized.md, stok_lojistik__alis_cari__STK-SC36-a_maliyet_portlari.md.

## Sonraki dalga bilmeli
- Alış faturası aggregate'i + `purchasing.invoice.finalized.v1` (FIN-S13 alan listesi) + invoice-recorded/fatura maliyet portu
  (STK-SC36-a 2, SAT-D392) yok — en çok beklenen iş.
- Belge altı iskonto (STK-SC36-a 1) Stok hesaplayıcısıyla.
- S5 kalanları: oluştur/onay UI, API-44, real-SQL Down testi + relational inventory; STK-SC34-b iki 409 kodunun Türkçe UI metni.
- SC33 frontend: sipariş formunda kur alanı/gerekçe ve kayıtlı kur gösterimi (ekran hesaplamaz); servis seviyesi testler.
- S3: release izni için shell capability (registry regen ile).
- CounterpartyCommandResult 31-35 değerleri paralel dillerle çakışabilir.
