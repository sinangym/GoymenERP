# stok_lojistik — dalga 5 durum

- Dal: `claude/tren-1-stok_lojistik` (base 318c02000), head `85eb14085`, push edildi. PR açılmadı.

## Birleştirilen dilimler (--no-ff)
- STK-S3 (48707ad49): yalnız domain katmanı. LOJ-D1 sevk türü, LOJ-D12 fason tartı sırası, LOJ-D13 ekleme usulü tartı düzeltmesi, LOJ-D2/D5 sahiplik doğrulaması (henüz bağlı değil).
- STK-SC36-a (989287366): SAT-D384 dönüştürülmüş etiket sayımı 409, FNS-D92 kur çözücü (bağlı değil), SAT-D385/D404 ve SAT-D386/D403/D407 hesaplayıcıları (bağlı değil).
- STK-SC36-b (44d6ebc7e): KLN-D107 fazla için yeniden talep, KLN-D108 kayıp talebini geri çekme, FNS-D77/D78 kuru (dry) ilk mal kabul kur portu. Elle yazılan Inventory migration'ı `has-pending-model-changes` ile doğrulandı: model farkı yok.
- STK-SC36-c (ae455bd23): paket standardı onayı, müşteri istisnası, saklanan import dosyaları (SAT-D382/D383/D397/D401). Yeni izin `product.package-standards.approve` yalnız kayıtlı; grant, rol veya Critical yok.
- Atlanan dilim: yok.

## Lead düzeltmeleri
- a26ca64a7: SC36-c'de CS1061 (`answer.ProblemCode` yerine `answer.Code`).
- 85eb14085: eksik `AddProductPackageStandardApprovalAndFiles` migration'ı dotnet-ef ile üretildi (Designer ve snapshot dahil).
  - Up: append-only trigger (THROW 51871).
  - Down: önce THROW 51872 guard'ı (forward fix), sonra DROP TRIGGER.

## Derleme (flock altında, `-p:OpenApiGenerateDocumentsOnBuild=false`)
- Başarılı (0 hata): Api, Logistics.UnitTests, Inventory.UnitTests, Product.UnitTests, Api.IntegrationTests.
- Frontend `tsc -b`: exit 0.
- `bin/` ve `obj/` silindi. Test koşulmadı.
- OpenAPI build adımı kapalı tutuldu. Sebep: yeni izin, sabitlenmiş ShellCapabilityRegistryAuthorities ile uyuşmuyor. Regen sonrası bu bayrak gerekmez.

## regen_needed (birleşim)
- openapi: SC36-c V3 endpoint'leri ve SC36-a/b'nin yeni 409 kodları.
- recovery-inventory ve release-manifest: 2 yeni migration, Inventory ve Product.
- architecture-guard: PlatformTenantQueryFilterBypassGuardTests.
  - Fingerprint'i değişenler: InventoryStore, InventoryDbContext, ProductPriceListDbContext, ModuleExtensions.
  - Yeni migration'lar için ReviewedMigrationMutationSources: Inventory (Sql) ve Product (Sql=2).
  - inventory.txt ve product.txt relational fingerprint'leri.
- test-naming-and-groups: yeni test sınıfları ve relational facts için grup doğrulayıcı pin'leri.
- shell-capability-registry: `product.package-standards.approve` ve Product provider sürümü.

## NEEDS-SINAN
- S3: Fason teslimin Inventory tahsis talep türü (demand kind) adı ve sürümü ne olacak?
- S3: Counterparty sahibinin OwnerId'si, talimattaki CustomerPartyId ile aynı Party kimliği mi?
- S3: Çok siparişli talimatta risk kapısı hep-ya-hiç mi işleyecek, yoksa kısmi sevke izin var mı?
- S3: Satış fark faturası hazır olmadan LOJ-D13 tartı düzeltmesi etkinleştirilebilir mi?
- S3: Kesin sevk iptal edilince tüketilmiş tek sevk istisnası yeniden kullanılabilir mi?
- SC36-a: SAT-D385 — pozisyonda stok yoksa geç fatura maliyet farkı nereye gidecek?
- SC36-a: SAT-D386/D403/D407 — belge iskontosu alanının sahibi hangi modül?
- SC36-a: SAT-D392/D393/D396 — okunamayan maliyetle tedarikçi iadesinde istisna onayı nasıl işleyecek, düzeltilen maliyete tolerans sınırı var mı?
- SC36-b: FNS-D77/D78 — onaylı kuru kim ve ne zaman kaydedecek? Birleştirme ve üretim etiketlerinde soy kuralı ne olacak?
- SC36-b: KLN-D108 — geri çekilen kayıp talebinden sonra yeni talep açılabilir mi, geri çekmeyi kim yapabilir?
- SC36-b: KLN-D107 — maker≠approver kuralı hem teslim alanı hem yenileyeni kapsıyor mu?
- SC36-c: Paket standardını kim onaylar; hazırlayan da karar verebilir mi?
- SC36-c: Onaya geçmiş tarihli yürürlük yazılabilir mi?
- SC36-c: Saklanan .xlsx indirilebilir mi; indirilirse vault veya malware taraması gerekir mi?
- SC36-c: Saklama süresi ve imha nasıl olacak?
- SC36-c: Müşteri istisnasındaki carinin müşteri rolü taşıması şart mı?
- SC36-c: Draft durumundaki ürüne standart yazılabilir mi?

## Mesajlar
- Yanıtlananlar ("## Yanıt" eklendi):
  - `alis_cari__stok_lojistik__ALC-SC34-kln-d110-supurme`
  - `koordinator__stok_lojistik__SC36-kararlari`
  - `satis__stok_lojistik__SAT-S8-x1-demand-reduce`
  - `satis__stok_lojistik__sat-d378-faturali-tarti`
  - `satis__stok_lojistik__sat-s13-fatura-kg-satir-okumasi`
  - `uretim__stok_lojistik__ilk-mal-kabul-kuru-portu`
- Worker'ların gönderdikleri:
  - `stok_lojistik__alis_cari__STK-SC36-a_maliyet_portlari`
  - `stok_lojistik__ust__sc36b-dotnet-lock`
  - `stok_lojistik__satis__sc36-c-paket-standardi-v3`
  - `stok_lojistik__uretim__sc36-c-paket-standardi-v3`

## Sonraki dalga için
- STK-S3 yalnız domain katmanında. Henüz yapılmayanlar:
  - kalıcı kayıt, migration, endpoint ve izin;
  - fason risk kapısı;
  - sahiplik doğrulamasının bağlanması.
  Satış sahiplik kapısı ve 4 adet `TODO(#66)` yeri değişmedi.
- SAT-D387/D388/D390/D391 hiç ele alınmadı.
- SC36-a'daki hesaplayıcılar, Purchasing'deki fatura olayı ve iskonto alanı gelince bağlanacak.
- SAT-S8 demand-reduce ve SAT-S13 V2 tartı satırı okuması sonraki dalgaya kaldı.
- Satış'tan faturalı tartı okuma portu istendi.
- İlk mal kabul kur portu `IInventoryFirstReceiptExchangeRateReadPortV1` kuru: RateNotRecorded döner.
- THROW numaraları 51836, 51871 ve 51872 kullanıldı.
