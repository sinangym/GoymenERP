# Ürün & Hizmet (fiyat listeleri dahil) — durum ve yapılacaklar

> Kapsam: Product modülü (ürün kartı, teknik aile, birim çevrimi, paket standardı, kg/m tablosu, boya üyeliği, SMM sınıfı,
> fiyat listeleri), hizmet kataloğu ve işletme alımı kataloğu. HEAD `ddc6cba`, 23 Kasım 2026. Yalnız kaynak okunarak
> hazırlandı; derleme ve test koşulmadı. Bu belge karar veya onay değildir.

## 1. Kısa sonuç

Ürün modülünde çok kod var ve özenle yazılmış: ürün kartı (taslak → aktif → pasif), geçmiş, Excel'den kart aktarma, mükerrer
uyarısı, ölçü/kesit bilgisi, adet↔kg hesaplayıcısı, paket standardı (onaylı, müşteri istisnalı), boya ürün listesi, SMM sınıfı
ve sürümlü fiyat listeleri. Satış, Satın Alma, Stok, Üretim, Lojistik ve Muhasebe ürünü gerçekten bu modülden okuyor.

Ancak bugün üç firmanın bu modülü kullanması **mümkün değil**. Sebep kodun eksikliği değil, kapıların kapalı ve ana verinin
boş olmasıdır:

1. **Modül varsayılan olarak kapalı.** İki ortam ayarı verilmedikçe bütün ürün ve fiyat uçları reddeder; Satış, Alış ve Stok
   ürün seçemez. Bu ayar hiçbir ortam dosyasında yok (yalnız test takımlarında var).
2. **Ürün yetkisini bir kullanıcıya verecek ekran ya da uç yok.** Ürün kartı yetkisi normal rol sisteminden ayrı, kişiye özel
   bir kayıttır; bunu yazan tek kod test verisi üreticisidir.
3. **Katalog verisi çok dar.** Tek kod grubu (Sac), beş yassı ürün ailesi, tek kalite (S235JR), tek kaplama (Kaplamasız).
   Bunları ekrandan çoğaltmanın yolu yok; her ekleme yazılım değişikliği gerektiriyor. Boru, profil, galvaniz, DKP satılamaz.
4. **Satışa uygunluk kuralı yalnız "kg ile satılan yassı ürün"e izin veriyor.** Adetle satılan ürün Ürün modülünün
   uygunluk kontrolünden geçemiyor; adet/kg paketi Satış tarafında yazılmış olsa da bu kapıda kesiliyor.
5. **Aktif kart düzeltilemiyor**, kg/m tablosu içe aktarılamıyor (belge kasası bağlantısı sahte), hizmet ve işletme alımı
   kataloğu yok, alış fiyat listesi yok.

Seviye özeti: ürün kartı ve fiyat listesi **1** (kod tam, kullanılamıyor); paket standardı **1-kısmi** (tüketen yok); kg/m
tablosu **1-kısmi** (içe aktarma çalışmaz); hizmetler ve işletme alımları **0**.

## 2. Hedef: bu modülden ne isteniyor

| Hedef | Kaynak |
|---|---|
| Menü: `Ürünler · Hizmetler · Fiyatlar`; sarf/bakım/yedek parça ticari katalogdan ayrı | Master §7.4, §4; işletme alımları Satın Alma menüsüne taşındı (KLN-D1, 3 Kasım) |
| Tek ürün kartı, dört rol (alınabilir, satılabilir, üretim girdisi, üretim çıktısı); girdi/çıktı reçeteden türetilir | DEC-PRD-001, Master §7.4 |
| Dinamik şablonlu aileler: yassı, bant/rulo, sac, boru/profil, uzun ürün, ara ürün, hizmet; kalite, kaplama, ölçü, renk, menşe | DEC-PRD-002 |
| Değişmez kimlik + grup ön ekli kod; eski kod takma ad olarak aranır | DEC-PRD-004 |
| Stok/alış/satış/fiyat/üretim/paket birimleri ayrı; sabit, formüllü, fiilî çevrim | DEC-PRD-006, Master §7.4 |
| Ana stok birimi kg; boru/profilde kg ve adet birlikte | DEC-PRD-007 |
| Paket standardı sürümlü, onaylı, müşteri istisnalı; sipariş/üretim kullandığı sürümü saklar | DEC-PRD-008, Master §7.4 |
| Profil ağırlığında öncelik doğrulanmış tablo; geometri yalnız tahmin | DEC-PRD-009, URN-D16, Master §28.2 |
| Taslak kart işlemde kullanılamaz; Excel aktarma eşleme → önizleme → onay | DEC-PRD-010 |
| Fiyat: şirket / cari grubu / cari özel; birim, para, tarih, miktar kademesi, iskonto, alt fiyat, marj; toplu güncellemede önce simülasyon | DEC-PRD-011, Master §7.4, §27 U-05/U-06 |
| Ekranlar U-01…U-10, IA-01, IA-02 | Master §27.4 |
| Formüller MAT-001…009, TUB, PNT, SIM | Master §28.2, §28.6, §28.7, §28.13 |
| Modül test ortamında, birim çevrimi birleşip pilot ürün verisi girilince açılır; kodda varsayılan kapalı kalır | URN-D1 (12 Kasım) |
| Katsayı ve kg yuvarlama kuralları, kantar sapması iki yönlü | URN-D7…D9 |
| Adetle satılan/alınan malın kg ile stoklanması (sekiz dilim) | `docs/delivery/ADET_KG_PAKETI_v1.md` |

## 3. Bugün var olan

"Menü" sütunu yalnız ön yüzde menü girişinin tanımlı olup olmadığını söyler; bugün hiçbir kullanıcıda yetki olmadığı için
hiçbiri görünmez (bkz. §4).

### 3.1 Ürün kartı

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Liste, arama, süzme, sıralama | Var | Var `/urun` | Var ("Ürünler") | Var | 1 | `Endpoints/ProductEndpoints.cs`, `product/ProductListPage.tsx` |
| Kart (Ürün 360) + değişiklik geçmişi | Var | Var | Listeden | Var | 1 | `ProductDetailPage.tsx`, `ProductHistoryReader.cs` |
| Taslak oluşturma / taslak düzenleme | Var | Var | Listeden | Var | 1 | `Product.cs` (`CreateDraft`, `EditBasic`) |
| Onay (taslak → aktif), pasife alma, yeniden açma | Var | Var | Karttan | Var | 1 | `ProductRow.ApplyActivation`, `ApplyLifecycleChange` |
| **Aktif kartı düzeltme** (ad, aile, ölçü alanı) | **Yok** | Yok | — | — | 0 | `EditBasic` yalnız taslağı kabul eder: "Yalnız taslak ürün temel bilgileri düzenlenebilir" |
| Kod: `ÖNEK-sıra`, tekil, eski kod takma ad | Var | — | — | Var | 1 | `ProductCode.cs`, `ProductCodeRegistration` |
| Kod değiştirme / elle kod istisnası | Yok | Yok | — | — | 0 | README: "later explicit alias/code command" |
| Kod grubu, sınıf, aile, şablon, kalite/kaplama sözlüğü **yönetimi** | **Yok** (yalnız migration) | Yok | — | Var (migration testleri) | 0 | Yazma ucu yok; `ReleaseInitialProductCodeGroup`, `ProductInitialTechnicalDefinitionV1.cs` |
| Dört rol (alınabilir/satılabilir/girdi/çıktı) elle | Var | Var | — | Var | 1 | `ProductRoleState.cs` |
| Girdi/çıktı rolünün reçeteden türetilmesi | Yok | — | — | — | 0 | `Derived` yalnız okuma eşlemesinde; türeten kod yok |
| Mükerrer uyarısı: aynı ad + aynı aile/kalınlık/en/kalite/kaplama | Var | Var | — | Var | 1 | `ProductDuplicateContractsV1.cs`; engellemez, yalnız uyarır |
| Excel'den kart aktarma | Var | Var `/urun/ice-aktar` | Listeden | Var | 1-kısmi | Yalnız 3 sütun (eski kod, ad, grup kimliği); ölçü, kalite, birim aktarılmaz; kartlar taslak doğar |
| Kalem tipi (Demir / Hurda / Hizmet) | Var | Var (kart paneli) | — | Var | 1 | `ProductItemTypeService.cs`; KDV/tevkifat için Ayarlar okur |
| SMM sınıfı (Ticari mal / Mamul) | Var | Var (kart paneli) | — | Var | 1 | `ProductCogsClassificationRuntime.cs` |

### 3.2 Teknik nitelik, ölçü, birim, ağırlık

| Yetenek | Arka uç | Ekran | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|
| Aile şablonu: kalınlık, en, (boy), kalite, kaplama, üretici kalitesi | Var | Var | Var | 1-kısmi | 5 aile: sıcak/soğuk rulo, sıcak/soğuk dilinmiş bant, sıcak kesilmiş sac. Kalite: yalnız S235JR. Kaplama: yalnız Kaplamasız. Üretici kalitesi: 3 kayıt |
| Kesit/ölçü satırı (25 tür: sac, rulo, boru, profil, köşebent, lama, NPU/IPE/HEA, inşaat demiri, hasır, baklavalı…) | Var | Var (kart paneli) | Var | 1 | `ProductDimensionShape.cs`; aileden bağımsız ayrı kayıt |
| Karttaki birimler | Kısmi | Var | Var | 1-kısmi | Stok (kg zorunlu), alış, satış açık; **fiyat, üretim, paket birimi reddediliyor** (`ProductDraftValidator.OpenUnitRoles`) |
| Adet↔kg hesaplayıcısı: sac kg/adet, rulo kg/m, boru kg/m, profil kg/adet, kantar toleransı | Var | — | Var | 1 | `ProductUnitConversionCalculatorV1.cs` (MAT-002, MAT-009 karşılığı) |
| Teorik kg/birim okuma portu (sipariş onayında sabitlenir) | Var | — | Var | 1 | `ProductTheoreticalKilogramsPerUnitReadPort.cs` |
| Şirket kg/m tablosu (profil, boru, yapısal profil) | Var | Var (kart paneli) | Var | **1-kısmi** | **İçe aktarma her zaman reddedilir**: belge kasası bağlantısı sahte (`UnavailableProductProfileWeightFileVault`), başka uygulayıcı yok |
| Paket standardı: Excel aktarma, tek ürün düzenleme, müşteri istisnası, ikinci kişi onayı, yürürlük tarihi, dosya saklama | Var | Var (kart paneli) | Var | **1-kısmi** | `ProductPackageStandardService*.cs`. **Okuyan modül yok** (§6.3) |
| Boya ürün listesi (hangi kart boyadır) | Var | Var `/boya-urunleri` | Var | 1 | `ProductPaintMembershipService.cs`; Üretim okuyor |
| Etiket/lot takip politikası | Yok | — | — | 0 | `IsTrackingPolicyValidAsync` her zaman "geçersiz" |
| Yoğunluk | Ayarlar'da | — | — | — | Kartta tutulmaz; şirket politikasından gelir (MAT-001 "ürün masterı üstündür" kısmı yok) |

### 3.3 Fiyat listeleri

| Yetenek | Arka uç | Ekran | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|
| Liste + numaralı sürümler, geçerlilik başlangıç/bitiş | Var | Var `/fiyat-listeleri` | Var | 1 | `Domain/PriceLists/PriceList.cs` |
| Taslak → yayım; hazırlayan yayımlayamaz; geriye tarih yok (İstanbul iş günü) | Var | Var | Var | 1 | `ProductPriceListService.cs`, `PriceListBusinessDay.cs` |
| Para birimi (listede tek, çevrim yok), fiyat birimi, KDV hariç | Var | Var | Var | 1 | Yalnız "Satış" kanalı ve "KDV hariç" kabul edilir |
| Miktar kademesi (alt dahil, üst hariç) | Var | Var | Var | 1 | `PriceListLineInput.LowerQuantity/UpperQuantity`, `PriceListLinesEditor.tsx` |
| Satır alt (taban) fiyatı | Var | Var | Var | 1 | Onaya gitmez; Satış altını reddeder |
| Cariye özel liste | Var | **Yok** | Var | 1-kısmi | API `scope` kabul eder; oluşturma ekranında kapsam alanı yok (`priceLists/*.tsx` içinde `scope` yalnız okuma çözücüsünde) |
| Cari grubu kademesi, anlaşma kademesi | Yok | — | — | 0 | `PriceTierChain.cs`: iki kademe "kullanılamıyor" döner |
| Ürün grubu kapsamı, iskonto zinciri, marj politikası, min/maks fiyat (rol/tarih) | Yok | Yok | — | 0 | README ASSUMPTION-PRL-7 |
| Toplu güncelleme / simülasyon (U-06), fiyat Excel aktarma | Yok | Yok | — | 0 | `BulkPrice`, `PriceSimulation` araması 0 sonuç |
| **Alış / tedarikçi fiyat listesi** | **Yok** | Yok | — | 0 | Kanal enum'unda yalnız `Sales`; `SupplierPrice`, `PurchasePriceList` araması 0 sonuç |
| Satış'ın listeyi okuması | Var | (Satış ekranı) | Var | 1 | `Sales.Application/SalesDraftService.LinePricing.cs` → `IProductQuantityPriceReferenceReaderV2` |

### 3.4 Hizmet ve işletme alımı

| Yetenek | Durum | Seviye | Kanıt |
|---|---|---|---|
| Hizmetler listesi / hizmet kartı (U-03, U-04) | Yok. Yalnız kartta "Hizmet" kalem tipi var; liste bu tipe göre süzülemiyor | 0 | `ServiceCatalog`, `ServiceCard`, `HizmetKart` araması 0 sonuç; `ProductListPage.tsx` içinde `itemType` yok |
| Hizmet tipi listesi | Ayarlar modülünde ("Masraf ve hizmet tipleri") | — | Ürün modülü dışında |
| İşletme alımı kataloğu (sarf, yedek parça, bakım) IA-01/IA-02 | Yok | 0 | `OperatingPurchase`, `SparePart`, `Consumable` araması yalnız Üretim'in kg dışı girdi kaydını buldu |

## 4. Kullanılabilirlik engelleri

1. **Çalışma zamanı kapalı (eski analizlerdeki B-06; hâlâ açık).** `ProductRuntimeOptions.IsEnabled` yalnız
   `ProductRuntime:IssuerLocalUserId` ve `ProductRuntime:IssuerExternalIdentityId` ayarları verilince doğru olur
   (`ProductRuntime/ProductRuntimeExtensions.cs`). Bu ayarlar `appsettings*.json` içinde yok; yalnız
   `infra/browser-suites/*.override.yml` test takımlarında sentetik değerlerle var. Kapalıyken: ürün uçları yetkisiz der,
   fiyat/kalem tipi/ölçü/paket/boya uçları 503 döner, ürün tabloları kurulmaz, Satış/Alış/Stok ürün uygunluğu 503 alır.
   Uçlar ve ekranlar `Program.cs` ve `productRoutes.tsx` içinde bağlıdır; eksik olan bağlantı değil, anahtardır.
   Canlı dışı ortamda ayrıca imza anahtarı (`CursorSigningKeyBase64`) gerekir. İkisi de insan kapısıdır (URN-D1 sınırlar).
2. **Ürün kartı yetkisi verilemiyor.** Kart okuma/oluşturma/düzenleme/onay/pasif/SMM yetkisi normal rollerden gelmez;
   kişi ve işlem başına ayrı bir kayıt (`ProductMasterAssignment`) ister ve yalnız `SYSTEM_ADMINISTRATOR` rolü altında
   geçerlidir (`SqlProductMasterAuthorizationService.cs`). Bu kaydı yazan tek kod test verisi üreticisidir
   (`Identity.Infrastructure/Fixtures/ProductMasterAssignmentFixtureStore.cs`: "no route, grant API or seeder reaches it").
   Gerçek ortamda tek yol veritabanına elle satır yazmaktır.
3. **Şirket bazlı ürün izinleri hiçbir rolde yok.** `product.price-lists.read/write/publish`, `product.item-type.manage`,
   `product.dimension.manage`, `product.profile-weights.import`, `product.package-standards.manage/approve`,
   `product.paint-memberships.manage`, `product.operational-selection.read` adları kayıtlı
   (`Security/ErpPermissions.cs`) ama `Seed/identity-seed.json` içindeki beş rolün hiçbirinde geçmiyor (dosyada `product`
   ve `price` araması 0 sonuç). `product.operational-selection.read` olmadan Satış ve Stok ürün seçemez.
4. **Pilot ürün verisi girilmemiş.** URN-D1 koşulu (b) için bir veri değişikliği bulunamadı; son katalog migration'ı 15 Ekim
   tarihli kesilmiş sac ailesidir.
5. **Ana veri ekranı yok.** Yeni kod grubu (ör. Boru, Profil), yeni kalite (S355, DC01, DX51D), yeni kaplama (galvaniz,
   boyalı) ya da yeni aile eklemek için ekran veya uç yok.
6. **Kullanıcının şirkette birim profili olmalı.** Satışa uygunluk, Ayarlar'daki şirket birim profilini ister; yoksa
   `settings_unit_profile_not_found` döner. Kurulumu Ayarlar modülünün konusudur.
7. **Fiyat yayımı iki kişi ister.** Hazırlayan yayımlayamaz (Sinan, 2 Ekim). Her şirkette en az iki yetkili gerekir.
8. **Paket standardı onayı iki kişi ister** ve onay bekleyen tek sürüm olabilir.

## 5. Eksikler

### 5.1 İsteklerinize göre

| Hedef | Durum |
|---|---|
| Boru/profil, uzun ürün, hizmet aileleri (DEC-PRD-002) | Teknik aile kaydı yalnız 5 yassı aile. Ölçü satırında 25 kesit türü var ama satışa uygunluk aileye bakıyor, kesit türüne değil; ailesi olmayan kart işlemde kullanılamaz (`product_family_policy_unavailable`) |
| Satış birimi ≠ stok birimi (DEC-PRD-006/007) | Satışa uygunluk kuralı satış birimi = stok birimi (kg) ister (`ProductFamilyRequirementPolicyV2.cs`). Adetle satış bu kapıdan geçemez |
| Fiyat birimi kartta (ALS-D51 varsayılan fiyat birimi) | Kart fiyat birimini reddediyor; port `DefaultPriceUnitId` alanını taşıyor ama dolamaz |
| Paket standardını sipariş/üretim saklar (DEC-PRD-008) | Master var, tüketen yok |
| Rollerin reçeteden türetilmesi | Yok |
| Kod değişimi ve eski kodun aranması (DEC-PRD-004) | Eski kod yalnız Excel aktarmada yazılır; değiştirme komutu yok |
| Fiyat: cari grubu, ürün grubu, iskonto, marj, min/maks, simülasyon, planlı/onaylı durum (DEC-PRD-011) | Yok; durumlar Taslak/Planlı/Geçerli/Geçmiş olarak türetilir, ayrı "onaylı" adımı yok |
| Hizmetler menüsü (U-03/U-04), işletme alımları (IA-01/02) | Yok |
| Birim ve dönüşümler ekranı (U-08), şablon ekranı (U-09) | Ekran yok; hesaplayıcı var |
| Excel aktarmada eşleme–önizleme–onay (DEC-PRD-010) | Akış var, ama yalnız ad ve grup; grup sütunu teknik kimlik (GUID) ister |
| kg/m tablosu içe aktarma (URN-D13, D16, D18) | Kod var, kasa bağlantısı sahte olduğundan çalışmaz |
| Etiket takip politikası (DEC-PRD-003) | Yok |

### 5.2 Bir ERP'de olması gerekene göre (çelik servis merkezi, üç şirket)

- **Kalite ve standart kataloğu:** EN 10025 (S235/S275/S355), EN 10111/10130 (DD/DC), EN 10346 (DX51D+Z…), yüzey ve
  kaplama kütlesi (Z100–Z275), boyalı renk/RAL. Bugün tek kalite, tek kaplama.
- **Tolerans sınıfı** (EN 10051, EN 10131 vb.): `ToleranceClass`, `EN 10051` araması 0 sonuç.
- **GTİP ve menşe:** `gtip`, `HsCode`, `TariffCode` araması arka uçta 0 sonuç. Menşe kararla lot özelliğidir
  (DEC-PRD-003); kartta varsayılan GTİP ise ihracat, e-fatura ve gümrük için gerekir.
- **MTC/sertifika bağı:** kartta yok; Stok'ta lot ve etikette var (`Inventory.Domain/Lots/MaterialLot.cs`). Kartta
  "sertifika zorunlu mu" alanı yok.
- **Mekanik değer beklentisi** (akma/çekme): kartta yok.
- **Alış fiyatı / tedarikçi fiyat listesi ve son alış fiyatı.**
- **Müşteri/tedarikçi ürün kodu:** bilinçli olarak ilk kapsam dışı (DEC-PRD-005).
- **Asgari stok / yeniden sipariş noktası:** `MinimumStock`, `ReorderPoint` araması 0 sonuç.
- **Ürün görseli, teknik çizim, ürün belgesi sekmesi:** kartta belge sekmesi yok.
- **Muadil ürün:** Stok benzerlik için Ürün'den olgu okuyor; kartta muadil tanımı yok.
- **Kartın toplu düzeltilmesi ve dışa aktarım:** yok.

### 5.3 Ekran ve kullanım kolaylığı

- Liste: arama, yaşam döngüsü/rol/eksiklik süzgeci, sıralama, sayfa boyutu var; yükleniyor/boş/yetkisiz/çevrimdışı/hata
  durumları ortak `StateBlock` ile çiziliyor. Toplam kayıt sayısı gösterilmez. Excel/CSV dışa aktarım ve PDF yok.
- Kart sayfası altı paneli tek sayfada taşıyor (kalem tipi, SMM, ölçü, kg/m tablosu, paket aktarma, paket onayı).
  kg/m tablosu ve paket tablosu şirket genelidir, tek ürüne ait değildir; ürün kartının içinde durmaları kafa karıştırır
  ve bu tablolara menüden ulaşılamaz.
- "Fiyat listeleri" menüde Satış grubunda; master "Ürün & Hizmet › Fiyatlar" der (`shell/navigation.ts` `NAV_GROUPS`).
- Excel aktarmada kullanıcı sütun adlarını elle yazar ve grup için GUID vermek zorundadır.
- Toplu onay (birçok taslağı birden aktifleştirme) yok; aktarılan her kart tek tek tamamlanıp onaylanmalı.
- Ön yüz test kaynağı geniş: `src/product` altında 38 test dosyası.

## 6. Diğer modüllerle bağlantılar

### 6.1 Ürün kimden ne alır

| Kimden | Ne | Mekanizma / dosya |
|---|---|---|
| Ayarlar | Birim kimlikleri (kg, t, m, adet), şirket birim profili, yoğunluk ve çevrim politikası | `ISettingsUnitIdentityCatalogV1`, `ProductUnitProfileAdapter`, `ProductPurchaseUnitConversionService.cs` |
| Kimlik | Küresel ürün yetkisi, şirket izinleri | `SqlProductMasterAuthorizationService.cs`, `IErpAuthorityRevalidationService` |
| Cari | Cariye özel fiyat listesi ve paket istisnasında müşteri doğrulama | `ProductRuntime/CounterpartyProductPriceListCustomerPortV1.cs`, `CounterpartyProductPackageStandardCustomerPortV3.cs` |
| Belgeler | kg/m dosyası için yükleme boyut sınırı | `DocumentsProductProfileWeightUploadCeilingSource.cs` (dosyayı saklayan bağlantı yok) |
| Denetim, Tekrar koruması | Her komutta denetim kaydı ve aynı isteğin tekrarı | `ProductCommandGateway.cs`, `GroupSharedCommandDbContext` |

### 6.2 Ürün kime ne verir

| Kime | Ne | Port → tüketen dosya | Eski işlem nasıl korunur |
|---|---|---|---|
| Satış | Satışa uygunluk + kart anlık görüntüsü | `IProductSalesEligibilityV1` → `SalesDraftService.cs` | Satır ürün kimliği, sürümü, kodu, adı ve birimini saklar |
| Satış | Ürün arama | `IProductSalesCandidatesV1` → `SalesProductCandidateEndpoints.cs` | — |
| Satış | Liste fiyatı (kademe zinciri) | `IProductQuantityPriceReferenceReaderV2` → `SalesDraftService.LinePricing.cs` | Satır liste, sürüm ve fiyatı saklar; yayımlanmış sürüm değişmez |
| Satış | Teorik kg/adet | `IProductTheoreticalKilogramsPerUnitReadPortV1` → `SalesDraftService.KilogramsPerUnit.cs` | Onayda belgeye yazılır; kart sonradan değişse belge değişmez |
| Satış | Kalem tipi | `IProductItemTypeReaderV1` → `SalesDraftService.Subcontract.cs` | — |
| Satın Alma | Alışa uygunluk, arama, çevrim, teorik kg | `IProductPurchaseEligibilityV1`, `IProductPurchaseCandidatesV1`, `IProductPurchaseUnitConversionV1` → `PurchasingProductSelectionHostPorts.cs`, `PurchasingRuntime/*` | Kaynak sürümleriyle döner |
| Stok | Stok uygunluğu, arama, gösterim, benzerlik, çevrim | `IProductStockEligibilityV1`, `IProductStockDisplayReferencesV2`, `IProductStockSimilarityFactsV1` → `InventoryOpeningHostPorts.cs`, `InventoryProductSimilarityFactsHost.cs`, `InventoryRuntime/ProductOwnerInventoryUnitConversionSource.cs` | Etiket kendi verisini tutar |
| Üretim | Teknik anlık görüntü (v1), çıktı ürünü seçimi, teorik kg, boya üyeliği | `IProductManufacturingTechnicalSnapshotV1` → `ManufacturingApiV1.cs`; `ManufacturingOutputSelectionReader.cs`; `ManufacturingPaintSelectionSourceV1.cs` | Plan anlık görüntü saklar |
| Lojistik | İrsaliye çıktısında ürün gösterimi | `IProductLogisticsDocumentDisplayV1` → `LogisticsDispatchPdfEndpoints.cs` | — |
| Muhasebe | SMM sınıfı | `IProductCogsClassificationReadPortV1` → `AccountingRuntime/AccountingInvoiceLineCogsSource.cs` | Sınıf sürümlü, ekleme-yalnız |
| Ayarlar | Vergi satırı için kalem tipi | `ISettingsProductItemTypeSourceV1` → `SettingsProductItemTypeHost.cs` | — |

Hiçbir modül Ürün tablosuna doğrudan yazmıyor; okumalar port üzerinden. Bu yönüyle mimari kurala uyuluyor.

**Şirket mi, grup mu?** Ürün kartı **grup ortaktır**: `product.Products` tablosunda şirket alanı yoktur
(`Product.cs`: "Group-shared master identity"). Kalem tipi, ölçü satırı ve SMM sınıfı da ortak kartta tek değerdir; son
değişikliğin hangi şirkette yapıldığı kaydedilir. **Şirkete özel** olanlar: fiyat listeleri, paket standardı, kg/m tablosu,
boya ürün listesi ve bir ürünün o şirkette kullanılabilirliği (birim profili). Yani GDC, GSP ve GIN aynı ürün kartını
paylaşır, fiyatını ve paketini ayrı tutar.

### 6.3 Kopuk ya da eksik halkalar

1. **Adetle satış Ürün kapısında kesiliyor.** Satış onayda adet satırı için teorik kg okuyor, ama aynı satırın ürünü
   önce satışa uygunluktan geçmek zorunda ve bu kontrol satış birimi = stok birimi (kg) istiyor. Kesilmiş sac kartında
   satış birimi "adet" seçilirse `family_unit_path_mismatch`; profil/boru kartında aile olmadığından 503. Adet/kg paketinin
   Satış tarafı bu yüzden gerçek kartla çalışamaz.
2. **Paket standardını kimse okumuyor.** `IProductPackageStandardReaderV2/V3` için Ürün ve API bağlama dosyaları dışında
   kullanan yok. Satış'a dönen fiyat cevabındaki paket alanı hâlâ sabit "kullanılamıyor"
   (`ProductPriceReferenceReader.cs`: "No package-standard master exists"). Üretim paket içi adedi planda elle alıyor.
   Modül README'si "consumed by Sales and Production" diyor; kod bunu doğrulamıyor.
3. **Teknik anlık görüntü v2 tüketilmiyor.** `IProductManufacturingTechnicalSnapshotV2` kayıtlı, Üretim v1'i kullanıyor.
4. **Ölçü okuma portu doğrudan tüketilmiyor.** `IProductDimensionReaderV1` yalnız Ürün'ün kendi çevrim servisinde.
5. **İki ayrı ölçü kaynağı.** Yassı ailede kalınlık/en hem şablon alanında hem "Ebat/kalınlık" satırında girilebiliyor;
   ikisini eşitleyen kural yok (README: "overlap with flat-family template fields" açık). Tutarsız kg hesabı riski.
6. **kg/m tablosu boş kalacağı için** profil kartına ölçü kaydedilemez (`product_dimension_weight_source_missing`),
   dolayısıyla profil için teorik kg hiç üretilemez.
7. **Alış fiyatı için Ürün'de karşılık yok**; Alış fiyatı siparişte elle girer.

## 7. Açık kararlar

| Konu | Durum | Kullanımı engeller mi |
|---|---|---|
| Modülün test ortamında açılması: issuer bağlama, imza anahtarı, pilot veri (URN-D1) | Karar var; işlem Sinan'da, veri hazırlanmamış | **Evet** |
| Ürün kartı yetkisinin kime verileceği ve nasıl yazılacağı | Yazma yolu yok; grant insan kapısı | **Evet** |
| Şirket bazlı ürün izinlerinin rollere dağıtımı (URN-D12, URN-D15 persona eşlemeleri var) | Grant insan kapısı | **Evet** |
| Yoğunluk, tolerans, yuvarlama değerlerinin kesinleşmesi (OPEN-007) | Kısmen | Kısmen (teorik kg) |
| Alış/ortak kanal ve KDV dahil fiyat listesi | NEEDS-SINAN (modül README) | Hayır |
| Taban fiyat altı satışı kim onaylar; marj politikası | NEEDS-SINAN | Hayır |
| Cari grubu fiyat kademesi (Cari kartında grup alanı, SAT-D57) ve anlaşma kademesi (SAT-D58) | Sahip modüller bekleniyor | Hayır |
| Paket standardında hazırlayan onaylayabilir mi; dosya saklama süresi | NEEDS-SINAN | Hayır |
| Profil en/boy yönü; yapısal profil tablosunun kaynağı; diğer uzun ürünlerde kg/m kaynağı | NEEDS-SINAN (README) | Profil için evet |
| Ölçü satırının şirket ortak olması ve tür başına zorunlu ölçüler | NEEDS-SINAN (README) | Hayır |
| URN-D8 katsayı yuvarlama yönü | Yazar okuması, kilitli değil | Hayır |
| İşletme alımları için ayrı katalog olacak mı (menüde Satın Alma'ya taşındı) | Katalog kapsamı belirsiz | Hayır |

## 8. Yapılacaklar (öncelik sırasıyla)

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---|---|---|---|---|---|
| 1 | Test ortamında Ürün çalışma zamanını aç: issuer bağlama + imza anahtarı | Kapalıyken Satış, Alış, Stok ürün seçemez | P0 | K | Sinan işlemi (insan kapısı) |
| 2 | Ürün kartı yetkisi için yönetim ucu ve ekranı (ya da normal izin modeline geçiş kararı) | Bugün yalnız veritabanına elle yazılabiliyor | P0 | O | Yetki modeli kararı |
| 3 | Ürün izinlerini iş rollerine bağla (operasyonel seçim, fiyat, ölçü, paket, boya) | Hiçbir rolde yok | P0 | K | İş rolleri tanımı; grant Sinan'da |
| 4 | Katalog yönetimi: kod grubu, aile, şablon, kalite, kaplama için uç + ekran (ya da en azından geniş bir sürüm paketi) | Tek grup, tek kalite, tek kaplama ile üç firma çalışamaz | P0 | B | Taksonomi onayı |
| 5 | Pilot/başlangıç ürün verisi: gerçek kalite, kaplama, aile listesi ve kartlar | URN-D1 koşulu (b) | P0 | O | #4 |
| 6 | Boru/profil/uzun ürün aileleri ve satış birimi ≠ stok birimi kuralı (adetle satış) | Adet/kg paketi Ürün kapısında kesiliyor | P0 | O | #4; kural kararı |
| 7 | kg/m tablosu için gerçek belge kasası bağlantısı | İçe aktarma her zaman 503; profil kartı tamamlanamaz | P0 (profil satılacaksa) | O | Belgeler modülü kullanıcı yükleme portu |
| 8 | Aktif kartı düzeltme komutu (gerekçeli, sürümlü, denetimli) | Yazım hatası düzeltilemiyor; tek yol pasife alıp yeniden açmak | P0 | O | Hangi alan değişebilir kararı |
| 9 | Excel aktarmayı genişlet: aile, ölçü, kalite, kaplama, birim; grup adıyla eşleme; toplu onay | StokERP'den geçişte her kartı elle tamamlamak gerekir | P1 | O | #4 |
| 10 | Paket standardını Satış ve Üretim'e bağla; fiyat cevabındaki sabit "kullanılamıyor"u kaldır | DEC-PRD-008 anlık görüntü şartı | P1 | O | — |
| 11 | Kartta fiyat, üretim ve paket birimini aç | DEC-PRD-006; alışta varsayılan fiyat birimi | P1 | K | Birim kararı |
| 12 | Cariye özel fiyat listesi için ekran alanı | Arka uç hazır, ekran yok | P1 | K | — |
| 13 | Hizmetler görünümü (kalem tipi = Hizmet süzgeci) ve hizmet kartı | Menü hedefi U-03/U-04 | P1 | K–O | Hizmet kartı alanları |
| 14 | Alış fiyat listesi / tedarikçi fiyatı | Alışta referans fiyat yok | P1 | O | Kanal kararı |
| 15 | Fiyat: cari grubu kademesi, iskonto zinciri, toplu güncelleme/simülasyon, Excel aktarma | DEC-PRD-011 | P1 | B | Cari grup alanı; politika kararları |
| 16 | GTİP, tolerans sınıfı, standart, sertifika zorunluluğu alanları | Çelik ticareti ve ihracat belgeleri | P1 | O | Alan listesi onayı |
| 17 | Şablon ölçüsü ile "Ebat/kalınlık" satırını tek kaynağa indir | Çift giriş, tutarsız kg riski | P1 | O | Karar |
| 18 | kg/m ve paket tablolarını kart sayfasından ayrı menü ekranlarına taşı; fiyat listelerini Ürün & Hizmet grubuna al | Master menü; bulunabilirlik | P2 | K | — |
| 19 | Rollerin reçeteden türetilmesi; teknik anlık görüntü v2'nin Üretim'e bağlanması | Master §7.4 | P2 | O | Üretim reçete modeli |
| 20 | Liste dışa aktarımı, ürün belgeleri sekmesi, asgari stok, muadil tanımı | ERP beklentisi | P2 | O | — |
| 21 | İşletme alımı kataloğu (sarf, yedek parça) | Master IA-01/02 | P2 | B | Kapsam kararı |

## 9. Doğrulanamayanlar

- Testlerin geçtiği: koşulmadı. Kaynak olarak `tests/GoymenERP.Product.UnitTests` (53 dosya, 588 test niteliği),
  `tests/GoymenERP.Api.IntegrationTests` içinde adı Product ile başlayan 41 dosya, `tools/relational_inventory/product.txt`
  (102 satır) ve `tests/e2e/product-u02-suite` (6 tarayıcı senaryosu) mevcut.
- Yerel hesapla (kullanıcı adı/parola) giriş yapan bir kullanıcının issuer bağlamadaki "dış kimlik" şartını karşılayıp
  karşılamadığı incelenmedi; karşılamıyorsa #1 yalnız OIDC girişiyle çözülür.
- Satış'ın adet satırında Ürün uygunluk kontrolünü atlayan ayrı bir yol olup olmadığı Satış kodunda uçtan uca izlenmedi;
  Ürün tarafındaki kural kesindir (§6.3 madde 1).
- Sistemde iş rollerinin ekrandan oluşturulup izinlerin oraya bağlanabildiği (Kimlik modülü) bu parçada incelenmedi; #3
  buna bağlıdır.
- Test ortamının gerçek yapılandırması depoda yok; ayarların depo dışında verilmiş olma ihtimali dışlanamaz.
- Üretim'in plan hesabında paket içi adedi başka bir yoldan Ürün'den alıp almadığı yalnız port araması ile kontrol edildi.
