# Ayarlar ve Ana Veriler (Settings modülü) — durum ve yapılacaklar

> Kapsam: `src/backend/Modules/Settings`, `src/backend/GoymenERP.Api/Endpoints/Settings*`, `SettingsRuntime/`,
> `src/frontend/goymen-erp-web/src/settings`; diğer modüllerin Ayarlar'dan okuduğu ve okumayı beklediği değerler.
> HEAD `ddc6cba`, 23 Kasım 2026. Yalnız kod okundu; derleme ve test koşulmadı.
> Bu belge karar veya onay değildir; hiçbir `OPEN-*` kaydını kapatmaz.

Yol kısaltmaları: `SET/` = `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/`,
`SETA/` = `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Application/`,
`EP/` = `src/backend/GoymenERP.Api/Endpoints/`, `UI/` = `src/frontend/goymen-erp-web/src/settings/`,
`MOD/` = `src/backend/Modules/`.

## 1. Kısa sonuç

Ayarlar, projenin en olgun altyapı modüllerinden biridir: 28 ekran, 121 HTTP yolu, 32 veritabanı göçü ve 74 okuma/yazma
sözleşmesi vardır. Hemen her politika şirket bazlı, sürümlü, gerekçeli ve denetim izlidir; değer yoksa sistem tahmin
yürütmez, işlemi reddeder. Bu güvenli, ama **kurulum yapılmadan hiçbir ticari akış uçtan uca çalışmaz** demektir.

Bugün yapılabilen: birim (kg, adet), TL para profili, vergi varsayılanı, teslim toleransı (%1/%2/%3) ve üretim başlangıç
değerleri üç şirket için kodda hazır gelir ve Satış, Satın Alma, Üretim bunları gerçekten okur.

Bugün yapılamayan ve en önemli eksikler:

1. **Hiçbir rol Ayarlar'a giremez.** 39 Ayarlar izninin hiçbiri başlangıç rollerinde yok (`identity-seed.json` içinde
   `settings.` araması 0 sonuç). Yetki verilmeden 28 ekranın hiçbiri menüde görünmez.
2. **Döviz kuru pratikte çalışmaz.** TCMB bağlantısı kapalı gelir; kapalıyken elle kur girmenin yolu yoktur. Açılsa bile
   otomatik çekme zamanlayıcısı yok ve her gün için bir kişinin "tam gün / yarım gün" işareti koyması gerekir. Dövizli
   satış, alış, risk ve fatura bu yüzden durur.
3. **Beş ayarın arka ucu var, ekranı yok:** teklif kayıp nedenleri ve teklif geçerlilik süresi, e-Fatura senaryo varsayılanı,
   banka CSV eşlemesi, görev ayrılığı, üretim termin tamponu.
4. **Dört ayar tanımlı ama hiçbir modül okumuyor:** finans onay matrisi, kasa sayım fark eşiği, görev ayrılığı, banka CSV
   eşlemesi. Ayrıca satış siparişi, satış faturası ve irsaliye numara serileri tanımlı ama Satış ve Lojistik bunları kullanmıyor.
5. **Üç modül Ayarlar'dan değer bekliyor ama Ayarlar bunu sunmuyor:** mal kabul kg toleransı, kantar fark bantları, benzer
   ürün toleransı. Bu kontroller bugün "ayar yok" durumunda çalışıyor.
6. GSP ve GIN için muhasebe fişi ve banka tahsilat/ödeme para profili ne kodda var ne ekrandan girilebiliyor.

## 2. Hedef: bu modülden ne isteniyor

| Hedef | Kaynak |
|---|---|
| "Ana Veriler: şirket, birim, kur, depo, kalite, parametre — kontrollü master veri" | Master §4, menü sırası 16 |
| Ekranlar: AV-01 Şirket/Tesis/Depo/Lokasyon; AV-02 Birim/Para/Kur/Vergi; AV-03 Durum/İş Akışı/Numara Serisi; SY-04 Onay Matrisi/Eşik/Vekâlet | Master §27.12 |
| Her formül ve politika merkezî, sürümlü, geçerlilik tarihli; yuvarlama ve tolerans açık; ekran-rapor-API aynı hesabı kullanır | Master §10.1, §28.17 |
| Ticari miktar birimleri kg / adet / metre / m² / saat / sefer; kg 3 ondalık | Master §28.1 |
| Her faturada tek kur; önceki iş günü TCMB satış kuru; elle değişiklik sebep, yetki ve denetim izi ister | Master §10.3, §28.16; DEC-FIN-010 |
| "Önceki iş günü" = TCMB'nin kur yayımladığı en yakın önceki gün; ayrı tatil takvimi tutulmaz | `SINAN_KARARLARI_2026-10-27_KUR_GUNU.md` (SAT-D54, SAT-D55) |
| Politika değerleri tek Ayarlar kaydında, şirket bazlı, sürümlü; değer yoksa işlem kapalı kalır; kullanılan sürüm belgeye yazılır | DEC-SET-001; `docs/adr/ADR-SET-002-settings-policy-registry.md` |
| Tolerans: uyarı %1, onay %2, blok %3 (başlangıç varsayımı) | OPEN-004 (KISMEN) |
| Üretim kütle dengesi ±%2, hat bazlı; plan bazında yetkili değiştirebilir | OPEN-005 (KISMEN) |
| Formül parametreleri Ayarlar'dan yönetilir | OPEN-007 (KISMEN) |
| Finans onay eşikleri dinamik; başlangıçta sert kapı yok | OPEN-009 (KISMEN) |
| DBS/teminat oranları tür/banka/vade bazında Ayarlar'dan | OPEN-010 (KISMEN) |
| Kur alma saati ve elle istisna sınırı | OPEN-012 (KISMEN; tatil kısmı SAT-D55 ile cevaplandı) |
| Sabit eşik, tolerans, rol adı, şirket kimliği koda gömülmez | `AGENTS.md` "Kodlama kuralları" |

## 3. Bugün var olan

Ortak özellikler (aşağıdaki her satır için geçerli): kayıtlar eklemeli ve sürümlüdür, silinmez; her yazma gerekçe,
beklenen sürüm, tekrar güvenliği anahtarı ve denetim kaydı ister. Bütün ekranlar menüde "Ayarlar" grubuna bağlıdır
(`src/frontend/goymen-erp-web/src/shell/navigation.ts` satır 555–620), ama **izin verilmediği için bugün kimseye
görünmez**. "Menü" sütunundaki "var" bu koşulla okunmalıdır.

Test kaynağı ölçümü: `tests/GoymenERP.Settings.UnitTests` 29 test dosyası; `tests/GoymenERP.Api.IntegrationTests` içinde
`Settings*` adlı 76 dosya (37'si gerçek SQL Server kabul testi); ön yüzde 45 test dosyası. Modülün kendi README'si
bunların birçoğu için "çalıştırma NOT_RUN" diyor; sonuç koordinatörün koşusundan gelmelidir.

### 3.1 Birim, para, vergi, kur (AV-02)

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Birim profili (hangi birim, kaç ondalık) | var | var | var | var | 1 | `SET/SettingsUnitProfileAdministration.cs`, `UI/UnitProfilePage.tsx`. Yalnız kg (3 ondalık) ve adet (0 ondalık) profili var. |
| Birim çevrim politikası (yoğunluk, katsayı ondalığı, kantar toleransı, sınır kuralı) | var | var | var | var | 1 | `SET/SettingsUnitConversionPolicyAdministration.cs`, `UI/UnitConversionPolicyPage.tsx`. Kodda hazır değer yok; girilmeden çalışmaz. |
| Para profili (para birimi + işlem bazında ondalık ve birim fiyat ondalığı) | var | var | var | var | 1 | `SET/SettingsMoneyProfileAdministration.cs`, `UI/MoneyProfilePage.tsx`. TL hazır; USD ve EUR boş başlar. |
| Vergi: kalem türüne göre KDV/tevkifat varsayılanı, istisna ve tevkifat kod listeleri, VUK 232 eşiği, alıcı durumu listesi, oran listeleri | var | var | var | var | 1 | `SET/SettingsTaxMasterAdministration*.cs`, `UI/TaxMasterPage.tsx`, `UI/TaxRateListSection.tsx` |
| Fason vergi varsayılanı (satış ve alış ayrı) | var | var | var | var | 1 | `EP/SettingsCompanyPolicyEndpoints.PurchasingSubcontractTax.cs`, `UI/SubcontractTaxDefaultPage.tsx` |
| Kur günleri listesi, kur çözümleme | var | var | var | var | 1 | `SET/SettingsExchangeRateAdministration.cs`, `SET/SettingsExchangeRateResolverV1.cs`, `UI/ExchangeRatesPage.tsx` |
| TCMB'den kur alma | var, **kapalı anahtar** | var | var | var | 1-kısmi | `…Infrastructure/ExchangeRates/ExchangeRateSources.cs`: `Settings:ExchangeRates:TcmbEnabled` verilmezse `DisabledExchangeRateBulletinSource` çalışır, istek 503 döner. Depoda bu anahtar için değer yok. Zamanlayıcı yok; kur "Kur al" düğmesiyle elle çekilir. |
| Elle kur düzeltme | var | var | var | var | 1-kısmi | Yalnız TCMB'nin o gün kur yayımladığı kayıtlı günde düzeltme yapılır; kayıt yoksa 422. Yani bağımsız "elle kur girme" yok. |
| Yarım gün (arife) işareti | elle var; otomatik kaynak **sahte** | var | var | var | 1-kısmi | `DisabledExchangeRateHalfDaySource` tek uygulamadır. İşaret yoksa kur çözülmez (`RateDayExchangeRate.cs` satır 141–150). |

### 3.2 Numara serisi ve ortak politika (AV-03)

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Belge numara serisi (ön ek + yıl + sıra; yıllık sıfırlama; boşluk raporu) | var | var | var | var | 1-kısmi | `SET/SettingsDocumentNumberSeriesV1.cs`, `UI/DocumentNumberSeriesPage.tsx`. Altı seri tanımlı; yalnız üçünün tüketicisi var (bkz. 6.2). |
| Giriş deneme sınırı (grup ortak) | var | var | var | var | 1 | `SET/SettingsLoginRateLimitAdministration.cs`, `UI/LoginRateLimitPage.tsx` |
| Bildirim etkinlik süresi | var | var | var | var | 1 | `EP/SettingsCompanyPolicyEndpoints.Operational.cs`, `UI/OperationalPolicyPage.tsx` |
| Görev ayrılığı (hazırlayan onaylayabilir mi) | var | **yok** | yok | var | 1-kısmi | `EP/SettingsSegregationOfDutiesPolicyEndpoints.cs`. Ön yüzde `segregation-of-duties` araması 0 sonuç. Tüketici de yok. |
| Durum / iş akışı tanımı | yok | yok | yok | yok | 0 | Ayarlar modülünde `Workflow`, `StatusDefinition` karşılığı dosya yok. |
| Referans listeleri: masraf tipi, hizmet tipi | var | var | var | var | 1 | `SET/SettingsReferenceListAdministration.cs`, `UI/ReferenceListPage.tsx` |
| Referans listeleri: boya rengi, hurda türü | var | var | var | var | 1-kısmi | Boya rengini Üretim okur; hurda türünü okuyan modül yok. |

### 3.3 Satış politikaları

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Teslim toleransı (uyarı / onay / blok yüzdesi) | var | var | var | var | 1 | `SET/SettingsSalesPolicyAdministration.Tolerance.cs`, `UI/SalesPolicyDeliveryTolerancePage.tsx` |
| Sipariş onay tutar eşiği (para birimi bazında) | var | var | var | var | 1 | `…Threshold.cs`, `UI/SalesPolicyApprovalThresholdsPage.tsx`. Başlangıçta "kapalı, tutar yok". |
| İskonto limit grupları, limitler, kişi–grup bağları | var | var | var | var | 1 | `SET/SettingsSalesDiscountLimitAdministration*.cs`, `UI/SalesDiscountLimitsPage.tsx` |
| Sözleşme iskonto sınırı | var | var | var | var | 1 | `EP/SettingsCompanyPolicyEndpoints.cs`, `UI/SalesContractDiscountLimitPage.tsx` |
| Teklif kayıp nedenleri ve varsayılan geçerlilik süresi | var | **yok** | yok | var | 1-kısmi | `EP/SettingsSalesQuoteAndEInvoicePolicyEndpoints.cs`. Ön yüzde `quote-policy` araması 0 sonuç. |
| e-Fatura senaryo kod listesi ve varsayılanı | var | **yok** | yok | var | 1-kısmi | Aynı dosya. Ön yüzde `einvoice-scenarios` araması 0 sonuç. |

### 3.4 Satın alma, stok, lojistik

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Alış faturası fark toleransı (tutar ve oran) | var | var | var | var | 1 | `SET/SettingsPurchaseInvoiceVariancePolicy.cs`, `UI/PurchaseInvoiceVariancePolicyPage.tsx` |
| Depo transfer çıkış tartısı (depo bazında aç/kapa) | var | var | var | var | 1 | `SET/SettingsWarehouseTransferScalePolicy.cs`, `UI/WarehouseTransferScalePage.tsx` |
| Mal kabul kg toleransı | **yok** | yok | yok | — | 0 | Satın Alma kendi okuma kapısını tanımlamış (`IPurchaseGoodsReceiptPolicyReadPortV1`), uygulayan sınıf yok. |
| Kantar fark bantları (uyarı / onay / blok, fazla sevk onay oranı) | **yok** | yok | yok | — | 0 | Lojistik `ILogisticsWeighingDifferencePolicyReadPortV1` bekliyor; tek uygulama `Unavailable…`. |
| Benzer ürün toleransı (tahsis önerisi) | **yok** | yok | yok | — | 0 | Stok `IInventorySimilarProductToleranceSourceV1` bekliyor; tek uygulama `Unconfigured…`. |

### 3.5 Üretim

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Üretim parametre profili (yoğunluk, kesim payı, kenar payı, boya hat hızı, film, katı oranı, fire katsayısı) | var | var | var | var | 1 | `SET/SettingsManufacturingProfileAdministration.Parameters.cs`, `UI/ManufacturingProfilePage.tsx` |
| Üretim tolerans profili (azami fire %, kütle dengesi ±%) | var | var | var | var | 1 | `…Tolerance.cs`. Süreç ve iş merkezi kapsamı var. |
| İş merkezi kapasitesi (kg/saat) ve termin emniyet tamponu | var | var | var | var | 1 | `SET/SettingsWorkCenterCapacityAdministration.cs`, `UI/WorkCenterCapacityPage.tsx` |
| Şirket geneli termin tamponu (iş günü) | var | **yok** | yok | var | 1-kısmi | `EP/SettingsCompanyPolicyEndpoints.cs` `/manufacturing-due-date-buffer`. Ön yüzde araması 0 sonuç. Kayıt yoksa 0 gün sayılır. |
| Fire maliyete katılımı | var | var | var | var | 1 | `UI/OperationalPolicyPage.tsx` (`scrap-cost`) |

### 3.6 Finans

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Finans açılış dönemi (aç / kapat) | var | var | var | var | 1 | `SET/SettingsCompanyPolicyAdministration.cs`, `UI/FinanceOpeningPeriodPage.tsx` |
| Teminat türleri ve oranları | var | var | var | var | 1 | `SET/SettingsCollateralTypeAdministration.cs`, `UI/CollateralTypesPage.tsx` |
| Banka kurumları; tür/banka/kalan gün bazlı teminat oranı | var | var | var | var | 1 | `SET/SettingsFinancePolicyAdministration.Banks.cs`, `…CollateralRates.cs`, `UI/CollateralPoliciesPage.tsx` |
| Finans onay matrisi (işlem, rol, tutar) | var | var | var | var | 1-kısmi | `EP/SettingsFinancePolicyEndpoints.cs`, `UI/FinancePoliciesPage.tsx`. **Tüketici yok.** |
| Kasa sayım fark eşiği | var | var | var | var | 1-kısmi | Aynı dosyalar. **Tüketici yok.** |
| Banka CSV sütun eşlemesi | var | **yok** | yok | var | 1-kısmi | `EP/SettingsBankCsvMappingEndpoints.cs`. Ekran ve tüketici yok. |
| Kredi hesabı eşlemesi | yalnız okuma | yok | yok | — | 0 | Tablo ve okuma kapısı var, yazma yolu yok; cevap hep "eşleme eksik". |

### 3.7 Şirket, depo, kalite (AV-01)

Şirket, tesis, depo/lokasyon ve iş merkezi kayıtları Ayarlar modülünde değil, Organizasyon modülündedir
(`MOD/Organization/…Domain/Companies|Sites|Locations|WorkCenters`); menüde Ayarlar grubunun başında yer alır. Bu belge
onları değerlendirmez. Ayarlar modülünde kalite sınıfı ya da çelik kalitesi ana verisi yoktur (`QualityGrade`,
`SteelGrade`, `KaliteSınıf` araması bütün modüllerde 0 sonuç).

## 4. Kullanılabilirlik engelleri

**Rol ve yetki.** `ErpPermissions.cs` içinde 39 `settings.*` izni tanımlı. Başlangıç rolleri (Organizasyon Okuma,
Organizasyon Operasyonu, Sistem Yöneticisi, Yetki Yöneticisi, Yetki Onaylayıcısı) yalnız kimlik, organizasyon ve cari
izni taşır; Ayarlar izni hiçbirinde yok. İzin verme her izin için insan kapısıdır. Kararlar kimin alacağını çoğu yerde
söylüyor (ör. kur düzeltme: Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin), ama boya rengi/hurda türü listesi, alış fason
vergi varsayılanı ve finans açılış dönemini kimin yöneteceği soruları açık.

**Kapalı anahtarlar ve sahte sağlayıcılar.**

| Konu | Durum | Sonuç |
|---|---|---|
| TCMB kur kaynağı | `TcmbEnabled` varsayılan kapalı; adres depoda yok; dış bağlantı açmak insan kapısı | Kur alınamaz, düzeltilemez |
| Yarım gün kaynağı | Tek uygulama "kapalı" sınıftır; gerçek kaynak yazılmamış | Her kur günü için elle işaret şart |
| Kur zamanlayıcısı | Yok (arka plan servisi araması: yalnız kimlik, tekrar kaydı temizliği ve iş bildirimi servisleri) | Kur her gün elle çekilir; hafta sonu ve tatil günleri ikinci çekimde kesinleşir |

**Kurulum sırası.** Aşağıdaki sıra koddaki bağımlılıklardan çıkarılmıştır. "Hazır" sütunu, üç şirket için kodda gözden
geçirilmiş başlangıç değeri olup olmadığını söyler.

| # | Ayar | Hazır değer | Boşsa ne olur |
|---|---|---|---|
| 0 | Ayarlar izinlerinin kişilere verilmesi | yok | Ekranlar görünmez |
| 1 | Şirket, tesis, depo, iş merkezi (Organizasyon) | şirketler tohumlu | Depo ve iş merkezine bağlı ayarlar girilemez |
| 2 | Birim profili | kg ve adet, üç şirket | Hazır; değiştirmek için önce "başlangıç değerlerini aktar" adımı gerekir |
| 3 | Birim çevrim politikası | **yok** | Ürün alış birimi çevrimi ve stok ortalama maliyet birimi okuması reddedilir |
| 4 | Para profili | yalnız TL; GDC'de 6 işlem, GSP/GIN'de 3 işlem | USD/EUR için işlem bazında ilk sürüm girilmeden dövizli belge reddedilir |
| 5 | Kur günleri | **yok** | Dövizli sipariş riski "hesaplanamadı", fatura adayı ayrı kalır, alış eşleşmesi reddedilir |
| 6 | Vergi varsayılanı | demir %20 KDV + 5/10 tevkifat; hurda KDV yok; hizmet %20 | Hazır; ama kod listeleri boş (aşağı) |
| 7 | İstisna ve tevkifat kod listeleri, VUK 232 eşiği, oran listeleri | **yok** | Fatura "kaynak yok" durumunda bekler; satırda vergi değişikliği için seçenek çıkmaz |
| 8 | Teslim toleransı | %1 / %2 / %3, üç şirket | Hazır |
| 9 | Sipariş onay tutar eşiği | "kapalı", üç şirket, yalnız TL | Hazır (eşik uygulanmaz) |
| 10 | İskonto limit grupları ve kişi bağları | **yok** | Her pozitif iskonto onaya gider |
| 11 | Belge numara serileri | **yok** | Alış siparişi ve finans makbuz/fiş numarası alınamaz (ilgili modül ayrıntısı o modülün raporunda) |
| 12 | Alış faturası fark toleransı | **yok** | Alış faturası eşleştirme reddedilir (`PurchaseIncomingInvoiceMatchSource.cs`) |
| 13 | Masraf ve hizmet tipi listeleri | **yok** | Hizmet siparişi ve ek maliyet kaydında seçenek yok |
| 14 | Üretim parametre ve tolerans profili | yoğunluk 7850, fire %5, kütle dengesi ±%2, boya değerleri | Hazır; kesim modu, baş/son kırpma, bobin çapları, hat hızları yok |
| 15 | İş merkezi kapasitesi | **yok** | Termin önerisi "hesaplanamadı" |
| 16 | Boya rengi listesi | **yok** | Boya seçimi boş |
| 17 | Finans açılış dönemi | **yok** | Açılış bakiyesi girilemez (dönem açık sayılmaz) |
| 18 | Teminat türleri, banka kurumları, teminat oranları | **yok** | Teminat kaydı ve teminatlı risk hesabı "kaynak yok" |
| 19 | Fason vergi varsayılanları | **yok** | Alış hizmet siparişi 503 ile reddedilir (`PurchaseServiceOrderServiceV1.cs` satır 200–208) |
| 20 | e-Fatura senaryo varsayılanı, teklif kayıp nedenleri | **yok, ekranı da yok** | Teklif "kaybedildi" kapatılamaz; geçerlilik süresi önerilmez |

Sessiz varsayılan uygulanan yerler (işlem reddedilmez): görev ayrılığı (kayıt yoksa "hazırlayan onaylayamaz"), termin
tamponu (0 gün), bildirim etkinlik süresi (90 gün), fire maliyete katılımı, giriş deneme sınırı (yapılandırma dosyasındaki
dakikada 10 deneme), depo transfer tartısı (kapalı), sözleşme iskonto sınırı (yok = her iskonto onaya).

**"Başlangıç değerlerini aktar" adımı.** Hazır gelen değerler koddan okunur. Bir şirket bu değeri ilk kez değiştirmek
istediğinde önce tek seferlik aktarım yapar (birim profili, teslim toleransı, onay eşiği, teklif politikası, e-Fatura
senaryosu, üretim profilleri); aktarımdan sonra yalnız veritabanı okunur ve koda geri dönülmez. Aktarım yapılmadan kayıt
girmeye çalışmak 409 ile reddedilir.

## 5. Eksikler

### 5.1 Senin isteklerine göre

1. **Kur: elle kur girişi yok.** Master §10.3 "manuel kur değişikliği sebep, yetki ve denetim ister" diyor; kodda yalnız
   TCMB'nin yayımladığı günün kuru düzeltilebiliyor. TCMB kapalıysa hiçbir kur oluşmaz. Karar notu (KUR_GUNU) bunu bilinçli
   seçmiş görünüyor; ama test ortamında dış bağlantı açılmazsa dövizli hiçbir akış denenemez.
2. **Kur: otomatik alma ve yarım gün kaynağı yok.** SAT-D171 arife günleri için otomatik resmî kaynak istiyor; yazılmamış.
3. **Onay matrisi ve eşikler (SY-04) işlemlere bağlı değil.** Finans onay matrisi ve kasa sayım fark eşiği kaydedilebiliyor
   ama Finans modülü okumuyor (`ISettingsFinanceApprovalPolicyProviderV1` ve `ISettingsCashCountDifferencePolicyProviderV1`
   için Ayarlar dışında kullanım 0). Vekâlet (onay devri) için Ayarlar'da kod yok.
4. **Görev ayrılığı ayarı hiçbir onay akışına bağlı değil.** `SegregationOfDutiesRuleV1` Ayarlar dışında kullanılmıyor.
5. **Numara serileri yarım bağlı.** Altı seriden alış siparişi, finans makbuzu ve finans fişi kullanılıyor; satış siparişi,
   satış faturası ve irsaliye serileri Satış ve Lojistik'te kullanılmıyor (`SettingsDocumentSeries` araması bu iki modülde 0).
6. **OPEN-004 stok/kabul toleransı Ayarlar'da yok.** Yalnız satış teslim toleransı var. Mal kabul ve kantar bantları için
   Ayarlar kaydı, ekranı ve sağlayıcısı yazılmamış; tüketiciler hazır bekliyor.
7. **Ürün/işlem bazlı tolerans (OPEN-004) yok.** Teslim toleransı şirket geneli tek değerdir.
8. **Plan bazında yetkili tolerans değiştirme (OPEN-005) yok.** README "yeni izin ve plan alanı gerekir, yapılmadı" diyor.
9. **Birimler eksik.** Master kg/adet/metre/m²/saat/sefer istiyor; ton ve metre için yalnız kimlik tanımlı
   (`SETA/Contracts/V1/SettingsUnitIdentitiesV1.cs`), profilleri yok; m², saat, sefer hiç yok.
10. **Para birimleri üçle sınırlı.** Yazılabilir hedefler TL, USD, EUR (`SETA/MoneyProfileAuthoringCatalogV1.cs`).
11. **GSP ve GIN'de muhasebe ve banka işlemleri için para profili yok ve ekrandan açılamıyor.** Yazılabilir işlemler
    şirketin koddaki listesinden türetiliyor; GSP/GIN listesinde yalnız satış siparişi, alış siparişi ve masraf ödemesi var.
    Karar (SAT-D18) "ayrı karar ister" diyor; bu karar bulunamadı.
12. **Formül kataloğu (Master §10.1, §28.17) yok.** Ayarlar yalnız parametre ve tolerans tutuyor; formül kimliği, sürümü,
    golden test seti kaydı yok (`FormulaId`, `FormulaCatalog` araması Ayarlar'da 0).
13. **Beş ekran eksik** (bölüm 3'te "ekran yok" satırları).

### 5.2 Bir ERP'de olması gerekene göre

1. **Tek ekrandan kurulum denetimi yok.** 20 adımlık kurulumun hangisinin eksik olduğunu gösteren bir "kurulum durumu"
   sayfası yok; eksik ayar ancak işlem reddedilince fark edilir.
2. **Şirketten şirkete kopyalama yok.** Üç şirket aynı anda başlayacak; her ayar üç kez elle girilir (yalnız kişi–iskonto
   grubu bağı çok şirkete birden yazılıyor).
3. **Ödeme/vade koşulu, teslim şekli, sevk yöntemi, banka hesabı, kasa tanımı gibi ortak listeler** Ayarlar'da yok;
   varsa ilgili modüllerdedir (bu rapor kapsamı dışı).
4. **Resmî tatil ve çalışma takvimi** Ayarlar'da yok; iş merkezi takvimi Organizasyon'da. Termin tamponunun iş günü mü
   takvim günü mü sayılacağı açık.
5. **Kur farkı, vade farkı, gecikme faizi oranı** gibi finansal parametreler için kayıt yok.
6. **Tevkifat ve istisna kodları mali müşavir teyidi bekliyor;** girilmeden demir satış faturası (varsayılan tevkifatlı)
   kesilemez.
7. **Toplu içe aktarma yok.** Referans listeleri, oran listeleri, banka kurumları tek tek girilir.

### 5.3 Ekran ve kullanım kolaylığı

- 22 sayfa dosyasının hepsi ortak durum bileşenini kullanıyor (yükleniyor, boş, yetkisiz, çakışma, hata); bu yönden
  Master §5 şablonuna uygun.
- Dışa aktarım, yazdırma ve PDF yok (`UI/` altında `Dışa aktar`, `Yazdır`, `.csv`, `xlsx` araması 0 sonuç).
- Ekranlar değer önermez; karar değerleri yalnız hatırlatma metni olarak görünür. Güvenli, ama ilk kurulumu yavaşlatır.
- Menüde Ayarlar tek düz liste: 28 Ayarlar girişi, organizasyon, belge şablonu ve kullanıcı girişleriyle birlikte 40'a
  yakın satır; yalnız üç alt başlık var (Ana Veriler, Sistem Yönetimi, İlk kurulum). Satış, finans, üretim gibi alt gruplar yok.
- Kur ekranında "kaynak kapalı" bilgisi gösteriliyor; ancak her gün için yarım gün işareti gerektiği bir kullanıcı için
  açık değil.

## 6. Diğer modüllerle bağlantılar

### 6.1 Ayarlar kimden ne alır

| Kaynak | Ne | Mekanizma / dosya |
|---|---|---|
| Kimlik | Kişi dizini, şirketteki etkin kullanıcı sayısı, rol listesi | `EP/SettingsSalesDiscountLimitHosts.cs`, `EP/SettingsSegregationOfDutiesHosts.cs`, `SettingsRuntime/SettingsFinancePolicyRuntime.cs` |
| Organizasyon | İş merkezi ve depo doğrulaması | `EP/SettingsWorkCenterReferenceHost.cs`, `LogisticsRuntime/InventoryTransferPolicyHostPorts.cs` |
| Ürün | Ürünün kalem türü (vergi çözümü için) | `EP/SettingsProductItemTypeHost.cs` |
| Finans | Kasa hesapları (kasa fark eşiği için) | `SettingsRuntime/SettingsFinancePolicyRuntime.cs` |
| Denetim, Tekrar Kaydı | Her yazmada denetim olayı ve komut kaydı | `SET/SettingsDbContext.cs` |

### 6.2 Ayarlar kime ne verir (her politikanın tüketicisi)

Kapsam: Ş = şirket bazlı, G = grup ortak.

| Politika | Kapsam | Okuyan modül ve dosya | Durum |
|---|---|---|---|
| Birim profili | Ş | Satın Alma `…Application/PurchaseUnitVerifier.cs`; Satış, Ürün, Stok, Lojistik, Üretim, Finans, Muhasebe (`ISettingsPrerequisiteProvider`, 15 klasörde kullanım) | Bağlı |
| Birim kimlik kataloğu | G (kodda sabit) | Ürün `…/ProductDraftOperation.cs`, `…/PriceLists/ProductPriceListService.cs`; Satın Alma | Bağlı |
| Birim çevrim politikası | Ş | Ürün `…/ProductPurchaseUnitConversionService.cs`; Stok `…/InventoryAverageCostUnitReadPort.cs` | Bağlı |
| Para profili ve para adayları | Ş | Satış `SalesReferenceReadHandler.cs`; Satın Alma `PurchasingService.cs`; Finans `FinanceCreditAccountService.cs`, `FinanceOpeningBalanceService.cs`; Muhasebe `AccountingSourceVoucherStoreV1.cs` | Bağlı |
| Vergi çözümü | Ş | Satış `SalesDraftService.Tax.cs`; Satın Alma `PurchasingService.cs` | Bağlı |
| Vergi oran listeleri | Ş | Satış `SalesDraftService.cs`; Satın Alma `PurchaseAdditionalCostServiceV1.cs` | Bağlı |
| Fason vergi varsayılanı (alış) | Ş | Satın Alma `PurchaseServiceOrderServiceV1.cs` | Bağlı |
| Fason vergi varsayılanı (satış) | Ş | Satış modülünde `SubcontractTaxDefault` kullanımı bulunamadı | **Tüketici yok** |
| Kur çözümleme | Ş | Satış `SalesDraftService.cs`, `SalesInvoiceService.cs`, `SalesDispatchReadService.cs`; Satın Alma `PurchaseIncomingInvoiceMatchSource.cs`; Stok `InventoryUnlinkedReceiptFxRateResolverV1.cs`; Finans `FinanceRuntime/FinanceCashExchangeSources.cs` | Bağlı |
| Teslim toleransı, onay eşiği | Ş | Satış `SalesDraftService.Status.cs`, `…Domain/Drafts/SalesRealization.cs` | Bağlı |
| İskonto limiti ve kişi bağı | Ş | Satış `SalesDraftService.DiscountException.cs` (`EP/SettingsSalesDiscountLimitHosts.cs` üzerinden) | Bağlı |
| Sözleşme iskonto sınırı | Ş | Satış `SalesContractService.cs` | Bağlı |
| Teklif politikası | Ş | Satış `SalesDraftService.QuoteFollowUps.cs` | Bağlı; veri girilemiyor (ekran yok) |
| e-Fatura senaryosu | Ş | Cari `…/CounterpartyEInvoiceScenarioSettings.cs`, `CounterpartyCommandService.cs` | Bağlı; varsayılan girilemiyor |
| Numara serisi: alış siparişi | Ş | Satın Alma `PurchasingStore.DocumentNumbers.cs`, `PurchaseServiceOrderStoreV1.cs` | Bağlı |
| Numara serisi: finans makbuz, fiş | Ş | Finans `FinanceDocumentNumbering.cs` | Bağlı |
| Numara serisi: satış siparişi, satış faturası, irsaliye | Ş | — | **Tüketici yok** |
| Alış faturası fark toleransı | Ş | Satın Alma `PurchaseIncomingInvoiceMatchSource.cs` | Bağlı |
| Masraf ve hizmet tipi | Ş | Satın Alma `PurchaseServiceOrderServiceV1.cs`, `PurchaseAdditionalCostServiceV1.cs` | Bağlı |
| Boya rengi | Ş | Üretim `ManufacturingRuntime/ManufacturingPaintSelectionSourceV1.cs` | Bağlı |
| Hurda türü | Ş | — (`ScrapKind` yalnız Ayarlar uç noktasında) | **Tüketici yok** |
| Depo transfer tartısı | Ş, depo | Lojistik/Stok `LogisticsRuntime/InventoryTransferPolicyHostPorts.cs` | Bağlı |
| Üretim parametre ve tolerans profili | Ş, süreç, iş merkezi | Üretim `ManufacturingApiV1.cs`, `ManufacturingPlanParametersV1.cs`, `…Domain/Orders/OrderClosureAssessment.cs` | Bağlı |
| İş merkezi kapasitesi, termin tamponu | Ş | Üretim `ManufacturingPlanDueDateSuggestionV1.cs`, `ManufacturingApiV1.cs` | Bağlı |
| Fire maliyete katılımı | Ş | Üretim `ManufacturingRuntime/ManufacturingSettingsScrapCostPolicyReader.cs` | Bağlı |
| Finans açılış dönemi | Ş | Finans (`Shell/SettingsFinanceOpeningPeriodReader.cs`) | Bağlı |
| Teminat türü, banka kurumu, teminat oranı | Ş | Finans `FinancePartyCollateralService.cs`, `FinanceSalesPartyRiskReadPortV1.cs` (`SettingsRuntime/SettingsFinanceCollateralPolicyReader.cs` üzerinden) | Bağlı |
| Kredi hesabı eşlemesi | Ş | Muhasebe `CreditMovementProposalStoreV1.cs` | Bağlı; yazma yolu yok, hep "eksik" |
| Finans onay matrisi | Ş | — | **Tüketici yok** |
| Kasa sayım fark eşiği | Ş | — | **Tüketici yok** |
| Görev ayrılığı | Ş | — | **Tüketici yok** |
| Banka CSV eşlemesi | Ş | — | **Tüketici yok** |
| Bildirim etkinlik süresi | Ş | İş Merkezi `WorkRuntime/WorkNotificationHostV1.cs` | Bağlı |
| Giriş deneme sınırı | G | Kimlik girişi `Security/LocalAccountLoginRateLimiter.cs` | Bağlı |
| Dönem uygunluğu (işlem tarihi açık mı) | Ş | Sözleşmede var; uygulama "henüz onaylanmadı" cevabı veriyor (`SETA/ApprovedSettingsPrerequisiteProvider.cs` satır 501) | Sahte |

Grup ortak olanlar yalnız giriş deneme sınırı ile birim ve para birimi kimlikleridir. Geri kalan her ayar şirket bazlıdır;
başlangıç değerleri üç şirkette aynı olsa da her şirketin kendi kaydıdır ve biri değişince diğerleri değişmez.

### 6.3 Kopuk ya da eksik halkalar

**Tüketici bekliyor, Ayarlar sunmuyor:**

| Bekleyen | Beklenen değer | Bugünkü davranış | Dosya |
|---|---|---|---|
| Satın Alma mal kabul | kg tolerans yüzdesi | "Tolerans ayarlı değil" sonucu; fark değerlendirilmez | `MOD/Purchasing/…/Contracts/PurchaseGoodsReceiptBatchContractsV1.cs` satır 169–181 |
| Lojistik kantar farkı | Fazla sevk onay oranı, uyarı/onay/blok bantları | "Okunamadı"; kayıt onay bekler, bant değerlendirilmez | `MOD/Logistics/…/Contracts/WeighingDifferenceContractsV1.cs` satır 160–186; `Program.cs` satır 421–422 |
| Stok tahsis önerisi | Benzer ürün toleransı | Yalnız aynı ürün önerilir | `MOD/Inventory/…/InventoryAllocationSuggestionContractsV1.cs` satır 93–110 |

**Koda gömülü iş değerleri (AGENTS.md "sabit eşik yasak" kuralına göre):**

| Değer | Yer | Not |
|---|---|---|
| Resmî fatura eşleşme toleransı 0,05 TL | `MOD/Sales/…/Contracts/SalesInvoiceMatchContractsV1.cs` satır 71 | Karar değeri (SAT-D229); Ayarlar bağlantısı ertelenmiş |
| Faturasız sevk için 7 gün | `MOD/Sales/…/Contracts/SalesControlCenterContractsV1.cs` satır 21 | Karar değeri (SAT-D95); Ayarlar kaydı yok |
| Teslim toleransı %1/%2/%3, vergi %20 ve 5/10, yoğunluk 7850, fire %5, kütle dengesi ±%2, boya 20 m/dk – 80 µm – 0,55 – 1,10 – 3 profil | `SETA/Approved*Catalog.cs` | Başlangıç değeri; aktarımdan sonra ekrandan değişir |
| Üç şirketin kimliği (`10000000-…-0001/2/3`) | `SETA/ApprovedUnitProfileCatalog.cs` satır 40–47 | Başlangıç değerleri bu kimliklere bağlı; dördüncü şirket açılırsa hazır değer gelmez |
| Giriş kilidi, oturum süresi, parola kuralı | `appsettings.json` `Identity:LocalAccount:*` | Yapılandırma dosyasında; ekranı yok (giriş deneme sınırı hariç) |

Aranan ama bulunmayan: modül kodlarında `const … Tolerance/Percent/Threshold/Rate = sayı` kalıbıyla yapılan aramada
yukarıdakiler dışında iş eşiği çıkmadı; kalan sabitler teknik sınırdır (sayfa boyu, azami satır, alan uzunluğu).

**Çift kayıt ve sahiplik.** Ayarlar başka modül tablosuna yazmıyor; diğer modüller Ayarlar tablosuna yazmıyor (bağlantılar
okuma kapıları üzerinden). Tek dikkat noktası: kişi–iskonto grubu bağı Ayarlar'da tutuluyor (karar önerisi Kimlik'ti);
README bunu bilinçli seçim olarak yazıyor.

## 7. Açık kararlar

| Konu | Durum | Kullanımı engelliyor mu |
|---|---|---|
| OPEN-012: kur alma saati, elle istisna sınırı | KISMEN | Hayır; ama TCMB bağlantısının açılması ayrı insan kapısı ve **engelliyor** |
| OPEN-004: tolerans sayılarının saha onayı; ürün/işlem bazı | KISMEN | Hayır (satış değeri hazır); mal kabul ve kantar değerleri için kayıt yeri yok |
| OPEN-005: kütle dengesi varsayılanı | KISMEN | Hayır |
| OPEN-007: kesin formül parametreleri | KISMEN | Kısmen: kesim modu, kırpma, bobin çapı, hat hızları boş |
| OPEN-009: finans onay matrisi gerçek değerleri | KISMEN | Hayır (matris zaten bağlı değil) |
| OPEN-010: gerçek teminat oranları | KISMEN | Teminatlı risk için evet |
| GSP/GIN muhasebe ve finans para profili (SAT-D18 "ayrı karar ister") | Karar bulunamadı | **Evet** (GSP/GIN muhasebe fişi, banka tahsilat/ödeme) |
| İstisna/tevkifat kodları ve VUK 232 eşiği (mali müşavir) | Bekliyor | **Evet** (fatura) |
| Ayarlar izinlerinin kimlere verileceği | İnsan kapısı | **Evet** |
| Boya rengi ve hurda türü listesini kim yönetir | NEEDS-SINAN | Hayır |
| Alış fason vergi varsayılanını kim yazar; satış tarafı izninin teyidi | NEEDS-SINAN | Alış hizmet siparişi için evet |
| Finans açılış dönemini kim açar/kapatır | NEEDS-SINAN | Açılış bakiyesi için evet |
| Termin tamponu: iş günü mü takvim günü mü; üst sınır | NEEDS-SINAN | Hayır |
| Kredi hesabı eşleme ayrıntısı (FNS-D97) | NEEDS-SINAN | Kredi muhasebesi için evet |
| Faturasız sevk süresinin Ayarlar'a bağlanması (SAT-D95) | Açık | Hayır |
| Uzun vadede iskonto bağlarının hangi ekrandan yönetileceği; Müdür ek puanının ilk değeri | Açık | Hayır |

## 8. Yapılacaklar (öncelik sırasıyla)

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---|---|---|---|---|---|
| 1 | Ayarlar izinlerini taşıyan iş rollerini tanımla ve test ortamında ata | Ekranlar kimseye görünmüyor | P0 | K | Sinan'ın rol/izin kararı (insan kapısı) |
| 2 | Test ortamında TCMB bağlantısını aç (`TcmbEnabled`, adres) | Dövizli akışların tamamı kura bağlı | P0 | K | İnsan kapısı: dış bağlantı |
| 3 | Kur için günlük otomatik alma ve yarım gün sorununa çözüm (otomatik kaynak ya da "işaret yoksa tam gün" kuralı) | Her gün elle iki işlem gerekmesi kullanılamaz | P0 | O | Karar: SAT-D171 için kaynak ya da varsayılan |
| 4 | TCMB kapalıyken de çalışan elle kur girişi (ya da test için kur yükleme) | Bağlantı açılamazsa hiçbir dövizli işlem denenemez | P0 | O | Karar: KUR_GUNU "yalnız TCMB" kuralına istisna |
| 5 | Kurulum durumu ekranı: 20 adımın şirket bazında dolu/boş listesi | Eksik ayar ancak işlem reddinde anlaşılıyor | P0 | O | — |
| 6 | Üç şirket için ilk kurulum verisini gir (bölüm 4 tablosu: çevrim politikası, numara serileri, alış fark toleransı, referans listeleri, kapasite, açılış dönemi, teminat türleri, USD/EUR para profilleri) | Boşken işlemler reddediliyor | P0 | K | 1; değerler için Sinan onayı |
| 7 | İstisna/tevkifat kod listeleri ve VUK 232 eşiğini gir | Fatura "kaynak yok" durumunda bekliyor | P0 | K | Mali müşavir teyidi |
| 8 | GSP ve GIN için muhasebe ve banka işlemi para profillerini aç | İki şirkette muhasebe fişi ve banka kaydı reddedilir | P0 | K | Sinan kararı (SAT-D18 devamı) |
| 9 | Teklif politikası ve e-Fatura senaryosu ekranları | Arka uç hazır, veri girilemiyor; teklif kaybı kapatılamıyor | P0 | O | — |
| 10 | Mal kabul kg toleransı ve kantar fark bantları: Ayarlar kaydı, ekranı ve sağlayıcısı | Tüketiciler hazır, değer yok; OPEN-004'ün stok tarafı | P1 | O | Değerlerin onayı |
| 11 | Satış siparişi, satış faturası, irsaliye numara serilerini Satış ve Lojistik'e bağla | Numara biçimi tek yerden yönetilmiyor | P1 | O | İlgili modül sahipleri |
| 12 | Finans onay matrisi ve kasa fark eşiğini Finans işlemlerine bağla | Tanımlı ama etkisiz | P1 | B | OPEN-009 gerçek değerler |
| 13 | Görev ayrılığı ayarını onay akışlarına bağla; ekranını yaz | Tanımlı ama etkisiz | P1 | O | — |
| 14 | Banka CSV eşleme ekranı ve Finans ekstre içe aktarma bağlantısı | Tanımlı ama etkisiz | P1 | O | Finans ekstre işi |
| 15 | Kredi hesabı eşlemesi yazma yolu ve ekranı | Okuma hep "eksik" | P1 | O | FNS-D97 ayrıntı kararı |
| 16 | Üretim termin tamponu ekranı; satış fason vergi varsayılanının Satış'a bağlanması; hurda türü listesinin tüketicisi | Yarım bağlı üç ayar | P1 | K | — |
| 17 | Benzer ürün toleransı kaydı ve sağlayıcısı | Tahsis önerisi yalnız aynı ürünü görüyor | P1 | O | Değer kararı |
| 18 | Eksik birimler (metre, ton, m², saat, sefer) ve yeni para birimi ekleme | Master §28.1 | P1 | O | Modüllerin birim desteği |
| 19 | 0,05 TL ve 7 gün değerlerini Ayarlar kaydına taşı | Koda gömülü iş değeri | P1 | K | — |
| 20 | Şirketten şirkete ayar kopyalama; toplu içe aktarma | Üç şirkette aynı girişin tekrarı | P2 | O | — |
| 21 | Ayarlar menüsünü alt gruplara ayır; dışa aktarım | 40'a yakın düz satır | P2 | K | — |
| 22 | Formül kataloğu kaydı (kimlik, sürüm, golden test bağı) | Master §10.1, §28.17 | P2 | B | OPEN-007 |
| 23 | Ürün/işlem bazlı tolerans; plan bazında tolerans değiştirme | OPEN-004, OPEN-005 | P2 | O | Yeni izin kararı |

## 9. Doğrulanamayanlar

- Testlerin sonucu: kaynaklar var, çalıştırılmadı. README birçok dilim için "NOT_RUN" diyor.
- Bir yöneticinin ekrandan yeni rol oluşturup `settings.*` iznini atayıp atayamayacağı (Kimlik modülü kapsamı) incelenmedi.
- Satış ve Lojistik'in belge numaralarını bugün nasıl ürettiği ayrıntılı okunmadı; yalnız Ayarlar serisini kullanmadıkları görüldü.
- `SettingsShellCapabilityProvider` içindeki sunucu tarafı yetenek–izin eşlemesi satır satır karşılaştırılmadı; ön yüz
  dosyası "birebir yansıtır" diyor.
- GSP/GIN muhasebe ve finans para profili için sonraki bir karar yalnız `docs/decisions` içinde işlem anahtarı adıyla
  arandı (tek sonuç SAT-D18); başka sözcüklerle yazılmış bir karar gözden kaçmış olabilir.
- Demir için varsayılan 5/10 tevkifatın her demir satışına uygulanmasının mevzuata uygunluğu bu incelemenin konusu değil;
  değer Sinan kararıdır (SAT-D139), kodlar mali müşavir teyidini bekliyor.

### Belge ile kod arasındaki çelişkiler

- `ACIK_KARARLAR.md` OPEN-004/005/007/009/010/012 satırlarının "repo durumu" sütunu "Policy Registry henüz uygulanmadı",
  "kur adapter runtime henüz yok" diyor. Kodda sürümlü kayıt, sağlayıcılar, kur kaydı ve TCMB okuyucusu var.
- `ADR-SET-002` durumu "Taslak; tablo, migration veya servis eklemez". Kodda `CompanyPolicyVersions` tablosu, göçü ve
  dört anahtarı çalışır durumda.
- Ayarlar README'sinin ilk bölümü "kalıcı sağlayıcı yok, gerçek satış taslağı açılamaz" diyor; sonraki bölümler ve kod
  bunu aşmış.
- README "referans listeleri ve şirket politikası için gerçek SQL testi henüz yok" diyor;
  `SettingsReferenceListSqlServerRelationalAcceptanceTests` ve `SettingsCompanyPolicySqlServerRelationalAcceptanceTests`
  dosyaları var.
- README birim çevrim politikası, teslim toleransı ve onay eşiği için "ekran bu dilimde yok" diyor; üçünün ekranı var.
- `UI/capabilities.ts` baş yorumu "on bir rota" diyor; dosyada 28 rota tanımlı.
