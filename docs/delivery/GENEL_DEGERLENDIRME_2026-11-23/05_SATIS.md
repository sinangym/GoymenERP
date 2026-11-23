# Satış — durum ve yapılacaklar

> Kapsam: Satış modülü (talep, teklif, sipariş, sözleşme, sevk görünümü, fatura taslağı ve eşleştirme, müşteri iadesi,
> raporlar) ve öteki modüllerle bağları. HEAD `ddc6cba`, 23 Kasım 2026. Yalnız kod okunarak hazırlandı; derleme ve test
> koşulmadı. Bu belge karar veya onay değildir; hiçbir `OPEN-*` maddesini kapatmaz.

Ölçülen büyüklük: 338 arka uç kaynak dosyası, 54 veritabanı geçişi (migration), `/api/v1/sales/` altında 125 HTTP işlemi,
Ayarlar altında satışa ait 30 işlem, 20 sayfa dosyası, 40 satış izni.

## 1. Kısa sonuç

Satış, projenin en dolu modülüdür. Talep → teklif → sipariş → onay → revizyon → iptal/kapatma zinciri, fiyat/iskonto/taban
fiyat onayları, müşteri riski, sevk görünümü, fatura taslağı → resmî faturayla eşleştirme → kesinleştirme → Finans'a alacak
kaydı, müşteri iade faturası ve fason hizmet faturası arka uçta yazılmış ve çoğunun ekranı var.

Bugün bir kullanıcı ekrandan bu zinciri **yürütemez**, çünkü 40 satış izninin hiçbiri başlangıç rollerinde yok
(`identity-seed.json` içinde `sales.` araması 0 sonuç). Roller kurulduğunda da şu beş eksik zinciri keser ya da zayıflatır:

1. **Satış sözleşmesi ekranı menüye ve adrese bağlı değil.** Arka uç ve ekran dosyası var, ama ekran hiçbir yerden açılmıyor.
2. **Sipariş "kapat" komutu her zaman reddediyor.** Kaynak, faturalama ve finansal kapanış eksenlerini besleyen bağ kurulmamış;
   yalnız "zorla kapat" çalışır.
3. **Kesinleşmiş faturanın iptali yapılamıyor.** Resmî iptal kaydı ekranı menüde, ama dış iptal kanıtı kaynağı "kullanılamıyor"
   döndüren bir yer tutucu; her kayıt denemesi 503 ile reddedilir.
4. **Satış faturası muhasebe fişine kendiliğinden gitmiyor.** Olay üretiliyor, fakat olay dağıtıcısı ve Muhasebe kabulü iki ayrı
   kapalı ayar anahtarının arkasında (insan kapısı).
5. **Karar verilmiş ama kodu olmayanlar:** baz + ekstra fiyat yapısı, önce-fatura (depo teslim) akışı, kapatılan satır/siparişi
   geri açma, teklif PDF'i, e-fatura XML dışa aktarımı, limit aşan iskonto onayının ekranı, fatura Excel dışa aktarımının ekranı.

## 2. Hedef: bu modülden ne isteniyor

Master rapor §7.2: satışın ana nesnesi sipariştir; yükleme, irsaliye, fatura ve tahsilat ondan türeyen ayrı kayıtlardır.
İstenenler:

- Menü (§4, sıra 3): "Talepler/Siparişler" ve "İrsaliye & Fatura". İade/düzeltme ayrı menü değil, yaşam döngüsü görünümüdür.
- Sipariş detayında altı kilitli sekme: Satırlar, Tahsis/Kaynak, Gerçekleşme/İrsaliye, Faturalar, Sözleşme/Belgeler,
  Revizyon/Geçmiş.
- Sipariş miktarı, net aktif, tahsis edilen, gerçekleşen, faturalanan ve kalan ayrı izlenir; müşteri onayı, iç onay, operasyon,
  sevk ve ticari kapanış tek durum alanında birleşmez; revizyon eski sürümü yok etmez.
- §8.1 siparişten nakde: talep/teklif → sipariş → risk/onay → kaynak stratejisi → tahsis → satın alma veya üretim → yükleme →
  sevk → irsaliye → fatura → açık kalem → tahsilat.
- §27.2 ekranlar S-01…S-11 (talep/teklif havuzu, sipariş listesi, yeni sipariş, Sipariş 360, fiyat–iskonto–marj–risk kontrolü,
  onaylar, sevkiyat/irsaliye, faturaya hazır, faturalar, iade/düzeltme/iptal, iletişim ve sözleşmeler).
- §28.13–28.16: fiyat ve marj hesapları tek arka uç hesaplayıcısında; müşteri riski = açık alacak + faturalanmamış sevk +
  sevk edilmemiş onaylı kalan; her faturada tek kur.
- §29.3 kabul senaryoları: taban fiyat altı doğru onaya gider (AT-021), risk aşımı sessizce serbest kalmaz (AT-022), onaylı
  siparişte kritik değişiklik onayı düşürür (AT-023), kısmi teslimde kalan doğru (AT-024), iade kaynak bağını korur (AT-028).

Bunların üstüne 25 Ekim – 22 Kasım arasında verilmiş 477 numaralı satış kararı (SAT-D1…SAT-D477; 60 "SATIS_EK" dosyası ve
sonraki dalga/tren dosyaları) vardır. Konu başlıklarına göre eşlemesi §5.1'dedir. En önemli çerçeve kararları:

- Resmî fatura ERP'de kesilmez; ERP taslağı hazırlar, dışa aktarır, resmî numara ve PDF sonradan eşleştirilir (SAT-D7).
- Hazırlayan onaylayamaz; sayısal sınırlar kod sabiti olmaz, Ayarlar'dan gelir.
- Okunamayan bir kaynak "sıfır" ya da "sorun yok" sayılmaz; işlem reddedilir (fail-closed: emin olunamayınca kapalı kalma).

## 3. Bugün var olan

Seviye: 0 = yok, 1 = kod yazılmış, 1-kısmi = yalnız bir bölümü var. "Menü" sütununda "detaydan" = sipariş detayından açılır.
Test kaynağı bütün satırlar için vardır: `tests/GoymenERP.Sales.UnitTests` (141 dosya), `tests/GoymenERP.Api.IntegrationTests`
içinde adı Sales ile başlayan 117 dosya, `tools/relational_inventory/sales.txt` (241 gerçek SQL testi adı), ön yüzde 99 test
dosyası. Koşulup koşulmadığı bu belgenin konusu değildir.

### 3.1 Talep ve teklif

| Yetenek | Arka uç | Ekran | Menü | Seviye | Kanıt / not |
|---|---|---|---|---|---|
| Satış talebi: oluştur, listele, onayla, reddet | Var | Var | Var | 1 | `SalesDraftService.Requests.cs`, `SalesEndpoints.Requests.cs`, `SalesRequest*Page.tsx` |
| Teklif: oluştur, revize et, listele | Var | Var | Var | 1 | `SalesDraftService.Quotes.cs`, `SalesQuote*Page.tsx` |
| Teklif kaybı ve kayıp nedeni listesi | Var | Var | Var | 1 | `POST quotes/{id}/loss`, `GET quotes/loss-reasons` |
| Teklif geçerlilik süresi ve önerisi | Var | Var | Var | 1 | `GET quotes/validity-suggestion` |
| Süresi geçmiş teklifin yönetici onayıyla siparişe dönmesi | Var | Var | detaydan | 1-kısmi | `POST quotes/{id}/expiry-approval`. Talep kaydı ve iş kutusuna düşme (SAT-D476) yok |
| Tekliften siparişe dönüştürme | Var | Var | detaydan | 1 | `POST quotes/{id}/conversion` |
| Teklif PDF'i | Yok | Yok | — | 0 | `QuotePdf`, `quote.*pdf` araması 0 sonuç |
| Kayıp nedeni ve varsayılan geçerlilik ayar ekranı | Var | Yok | — | 1-kısmi | `settings/sales-policies/quote-policy` uçları var; ön yüzde `quote-policy` araması 0 sonuç |

### 3.2 Sipariş

| Yetenek | Arka uç | Ekran | Menü | Seviye | Kanıt / not |
|---|---|---|---|---|---|
| Sipariş taslağı oluştur, listele, detay | Var | Var | Var | 1 | `SalesEndpoints.cs`, `SalesDraftCreatePage.tsx`, `SalesDraftListPage.tsx` |
| Sipariş 360: altı sekme | Var | Var | detaydan | 1 | `orderTabs.ts` (satirlar, tahsis, gerceklesme, faturalar, belgeler, gecmis) |
| Onaya gönder, onayla, reddet, geri çek | Var | Var | detaydan | 1 | `drafts/{id}/submission`, `approval`, `rejection`, `withdrawal` |
| Müşteri kabulü ve müşteri reddi | Var | Var | detaydan | 1 | `customer-acceptance`, `customer-rejection` |
| Fiyat girme, liste fiyatı kademe zinciri | Var | Var | detaydan | 1 | `SalesDraftService.LinePricing.cs`, `lines/{lineId}/price-references` |
| Taban fiyat altı onayı | Var | Var | detaydan | 1 | `below-floor-exception-approval`, `SalesDraftPriceFloorApproval.tsx` |
| Limit aşan iskonto onayı | Var | **Yok** | — | 1-kısmi | `SalesDraftService.DiscountException.cs`; ön yüzde `discount-exception` araması 0 sonuç |
| İskonto limit grupları ve kişi bağları | Var | Var | Var (Ayarlar) | 1 | `settings/sales-discount-limits/*`, "İskonto limitleri" |
| Marj (tutar ve yüzde) | Var | Var | detaydan | 1 | `SalesDraftService.MarginCosts.cs`; maliyet Stok'tan: `InventorySalesMarginCostReadPortV1.cs` |
| Fiyat–iskonto–marj–risk kontrol sayfası (S-05) | Var | Var | detaydan | 1 | `SalesPriceControlPage.tsx`, adres `/satis/siparisler/:id/kontrol` |
| Onay tutar eşiği | Ayar var | Ayar ekranı var | Var (Ayarlar) | 1-kısmi | Karar gereği kapalı (SAT-D174); Satış modülünde `Threshold` araması 0 sonuç, onay akışı eşiği okumuyor |
| Müşteri riski ve kredi limiti kontrolü | Var | Var | detaydan | 1 | `SalesDraftService.RiskCheck.cs`; veriler Finans'tan: `FinanceSalesPartyRiskReadPortV1.cs` |
| Risk uyarı kaydı (onaya gönderme ve onayda) | Var | **Yok** | — | 1-kısmi | `GET drafts/{id}/risk-warnings`; ön yüzde `risk-warnings` araması 0 sonuç |
| Risk istisnası: iste, onayla, reddet, geri çek | Var | Var | detaydan | 1 | `SalesRiskExceptionService.cs`, `SalesOrderRiskExceptions.tsx` |
| Termin (teslim planı) değiştirme ve geçmişi | Var | Var | detaydan | 1 | `delivery-schedule`, `delivery-schedules` |
| Revizyonlar: ticari, miktar, satır, başlık, kalan değişikliği, fason vergi | Var | Var | detaydan | 1 | `commercial-revision`, `quantity-revision`, `line-revision`, `header-revision`, `remainder-changes`, `subcontract-tax-revision` |
| Revizyon geçmişi | Var | Var | detaydan | 1 | `GET drafts/{id}/revisions`, `SalesDraftRevisionHistory.tsx` |
| İptal ve etki önizleme | Var | Var | detaydan | 1 | `cancellation-impact`, `cancellation`; Stok ve Lojistik'ten okur |
| Zorla kapatma ve etki önizleme | Var | Var | detaydan | 1 | `force-close-impact`, `force-close` |
| Satır kapama | Var | Var | detaydan | 1 | `line-closure`, `SalesLineClosure.tsx` |
| Kullanıcı kapatması ("kapanmaya hazır") | Var | Var | detaydan | 1-kısmi | Her zaman 409: `ISalesOrderOwnerAxesSourceV1` hiçbir yerde kayıtlı değil (`SalesDraftService.Status.cs:119`) |
| Kapatılanı geri açma | Yok | Yok | — | 0 | `reopen` adlı uç yok; karar SAT-D217/D218 |
| Satış onayları kuyruğu (S-06) | Var | Var | liste içinden | 1-kısmi | Yalnız sipariş iç onayı listelenir; öteki sekiz onay türü için kuyruk yok (`SalesApprovalsPage.tsx` başlık notu) |
| Sipariş PDF'i | Var | Var | detaydan | 1 | `SalesOrderPdfComposer.cs`, `GET drafts/{id}/pdf` |
| Genel aramada sipariş | Var | Var | üst arama | 1 | `SalesOrderGlobalSearchSource.cs` |

### 3.3 Sözleşme, taksit, fason, döviz

| Yetenek | Arka uç | Ekran | Menü | Seviye | Kanıt / not |
|---|---|---|---|---|---|
| Satış sözleşmesi: numara, sürüm, geçerlilik, taksit planı, iskonto, maddeler | Var | Dosya var, **bağlı değil** | Yok | 1-kısmi | `SalesContractService.cs`, 5 uç; `SalesContracts.tsx` başlığında "STATUS: NOT ROUTED"; hiçbir dosya içe aktarmıyor |
| Sözleşme iskonto sınırı ayarı | Var | Var | Var (Ayarlar) | 1 | `settings/company-policies/sales-contract-discount-limit` |
| Sözleşmenin siparişe bağlanması | Yok | Yok | — | 0 | Sipariş ve teklif alanlarında `ContractId` araması 0 sonuç |
| Vade tipleri, karma vade, taksit planı (siparişte) | Var | Var | detaydan | 1 | `SalesDueTerms.cs`, `SalesDueTermsEditor.tsx` |
| Faturada taksit tutarları (dövizde TL karşılığı dahil) | Var | Var | detaydan | 1 | `SalesInvoiceInstalmentCalculator.cs` |
| Fason hizmet siparişi | Var | Var | Var | 1 | `SalesDraftService.Subcontract.cs`, `SalesDraftCreatePage.subcontract.test.tsx` |
| Fason hizmet faturası yaşam döngüsü | Var | Var | detaydan | 1 | `SalesInvoiceService.SubcontractService.cs`, `.SubcontractLifecycle.cs`, 12 uç, `SubcontractInvoiceCreate.tsx` |
| Fason vergi varsayılanı | Var | Var | Var (Ayarlar) | 1 | `sales-subcontract-tax-default` |
| Dövizli sipariş; kurun ilk çözülebildiği onayda dondurulması | Var | Var | detaydan | 1 | `SalesDraftService.MarginCosts.cs` (SAT-D429/D430); kur Ayarlar'dan okunur |
| Dövizli faturada tek kur ve TL karşılığı | Var | Var | detaydan | 1 | `SalesInvoiceCalculator.cs`, `SalesInvoiceService.Match.cs` |

### 3.4 Sevk, fatura, iade

| Yetenek | Arka uç | Ekran | Menü | Seviye | Kanıt / not |
|---|---|---|---|---|---|
| Sevkiyat/irsaliye listesi (S-07) | Var (okuma) | Var | Var | 1 | `SalesDispatchReadService.cs`; kayıt Lojistik'tedir, Satış yalnız gösterir |
| İrsaliyede teorik–kantar farkı | Var (okuma) | Var | satırdan | 1 | `dispatches/{id}/weighing-difference`, `SalesDispatchWeighingSummary.tsx` |
| Fazla teslim onayı | Var | Var | detaydan | 1-kısmi | `over-delivery-approval`. Fazla sevk denendiğinde talep kaydı (SAT-D475) Satış'ta yok |
| Faturaya hazır (S-08) | Var | Var | Var | 1 | `GET invoice-candidates`, `SalesInvoiceCandidatesPage.tsx` |
| Fatura taslağı: oluştur, onaya gönder, onayla, reddet, iptal | Var | Var | Var | 1 | `SalesInvoiceService.cs`, `SalesInvoiceDraftCreate.tsx`, `SalesInvoiceDraftActions.tsx` |
| Resmî faturayla eşleştirme | Var | Var | Var | 1 | `invoices/{id}/external-match`; resmî kayıt Finans'tadır (`finance/official-sales-invoices`) |
| Kesinleştirme ve Finans'a alacak kaydı | Var | Var | Var | 1 | `SalesInvoiceService.Finalize.cs`, `SalesFinanceAdmissionHost.cs`; elle "Finans'a yeniden gönder" var |
| Eşleşmede fark varken kesinleştirme | Reddediyor | Gösteriyor | — | 1-kısmi | Fark politikası kararsız (kodda "NEEDS-SINAN #3"): `SalesInvoiceFinalization.cs:58` |
| Fatura Excel dışa aktarımı | Var | **Yok** | — | 1-kısmi | `SalesInvoiceService.Export.cs`; ön yüzde `invoices/export` araması 0 sonuç |
| Fatura e-fatura XML (UBL-TR) dışa aktarımı | Yok | Yok | — | 0 | `Export.cs:57`: XML istenirse reddedilir |
| Önce-fatura (depo teslim) akışı | Yok | Yok | — | 0 | Fatura yalnız sevk payından açılır: `CreateSalesInvoiceRequest(CompanyAccountId, Shares)` |
| Kesinleşmiş faturanın resmî iptal kaydı | Var | Var | Var | 1-kısmi | Kanıt kaynağı yer tutucu: `UnavailableSalesOfficialCancellationEvidenceReadPortV1`; kayıt 503 |
| Müşteri iade faturası: oluştur, onaya gönder, onayla, reddet, kesinleştir, iptal | Var | Var | Var | 1 | `SalesCustomerReturnService.cs`, `SalesCustomerReturn*Page.tsx` |
| Fiyat farkı faturası | Yok | Yok | — | 0 | `PriceDifference`, `DebitNote`, `CreditNote` araması yalnız rapor sözleşmesinde |
| Satış faturası PDF'i | Bilinçli yok | — | — | — | Karar: ERP ayrı PDF üretmez, resmî PDF gösterilir (SAT-D470) |

### 3.5 Kontrol merkezi ve raporlar

| Yetenek | Arka uç | Ekran | Menü | Seviye | Kanıt / not |
|---|---|---|---|---|---|
| Satış Kontrol Merkezi | Var | Var | Yok; sevkiyat sayfasındaki sekmeden | 1 | `SalesControlCenterService.cs`, `SalesDispatchShared.tsx:25` |
| Açık siparişler raporu | Var | Var | Var | 1 | `GET reports/open-orders` |
| Kayıp fırsatlar raporu | Var | Var | Var | 1 | `GET reports/lost-opportunities` |
| Fiyat sapması raporu | Yer tutucu | Var | Var | 1-kısmi | Her zaman "kaynak yok": `SalesDraftService.Reports.cs:116` |
| Sipariş durum eksenleri (yaşam döngüsü, kabul, gerçekleşme, faturalama) | Var | Var | detaydan | 1-kısmi | Kaynak, faturalama ve finansal kapanış eksenleri "kaynak yok" döner |

## 4. Kullanılabilirlik engelleri

**Rol ve yetki.** `ErpPermissions.cs` içinde 40 satış izni kayıtlı; `identity-seed.json` içindeki beş sistem rolünün hiçbirinde
satış izni yok. Rol önerisi hazır ve onaylı (SAT-D230, `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md`), ama rol tanımı ve izin
ataması insan kapısıdır. Bu yapılmadan menüde "Satış" grubu boş görünür. Ayrıca görev ayrılığı kuralları en az iki kişi ister:
siparişi hazırlayan onaylayamaz, fatura taslağını hazırlayan onaylayamaz, sevki yapan kendi fazla teslimini onaylayamaz.

**Menü ve adres.**

- Sözleşme ekranı: adres yok, menü yok, sunucu yetenek kimliğini yayımlamıyor (`SalesShellCapabilityProvider.cs` içinde
  `contracts` araması 0 sonuç).
- Kontrol Merkezi, Satış Onayları ve fiyat kontrol sayfası menüde yok; başka sayfalardaki bağlantılardan açılır.
- Menüde "Satış talepleri" ve "Satış teklifleri" iki ayrı madde; master rapor tek "Talep/Teklif Havuzu" tarif eder.

**Kapalı ayar anahtarları (insan kapısı).**

- `Work:OutboxDispatcher:Enabled` (olay dağıtıcısı): `appsettings.json` içinde yok, varsayılan kapalı. Kapalıyken satış
  faturası olayı Muhasebe'ye, sevk olayı Satış'ın risk istisnası kullanım kaydına ulaşmaz.
- `Accounting:InvoiceJournal:InboxAdmissionEnabled`: kapalıyken Muhasebe satış faturası fişini kabul etmez
  (`AccountingInvoiceJournalHost.cs:43`).

**Yer tutucu sağlayıcılar.**

- Resmî fatura iptal kanıtı: `UnavailableSalesOfficialCancellationEvidenceReadPortV1` (her zaman "kullanılamıyor").
- Sipariş kapanış eksenleri: `ISalesOrderOwnerAxesSourceV1` için kayıt yok.
- Kaynak planı önerisinde serbest stok: `SalesDraftService.SourcePlan.cs:115` her zaman `freeStock: null` gönderir; öneri
  yalnız mevcut tahsis kalanı karşılıyorsa çıkar, aksi halde "kullanılamıyor".

**Kurulum verisi.** Sipariş açılabilmesi için başka modüllerde şunlar kurulmuş olmalı: cari hesap; satışa uygun ürün ve
kg/adet bilgisi (adetli satırın onayı bu olmadan reddedilir); şirketin para ve birim profili; vergi oranları ve tevkifat
listesi; kur kaydı (dövizli satış); teslim toleransı; iskonto limit grupları; fiyat listesi (yoksa her satır elle fiyat ve
onay ister); Finans'ta kredi limiti. Bunlardan e-fatura senaryosu ile teklif politikasının ayar ekranı yok; uçları var.
Başlangıç politikalarının şirkete aktarımı (`baseline-transfer` uçları) ayrı bir adımdır; ekranda yapılıp yapılamadığı §9'da.

**Dış bağımlılık.** Resmî fatura ERP dışında kesilir. Akış: ERP'de taslak → Excel dışa aktarım (ekranı yok) → dış programda
fatura → Finans'ta "Resmî satış faturaları" ekranına numara, ETTN ve PDF girişi ve ayrı kişinin onayı → Satış'ta eşleştirme →
kesinleştirme. Bu, her fatura için iki modülde birden çok kişinin elinden geçen bir iştir.

## 5. Eksikler

### 5.1 Senin isteklerine göre

Kararlar konu başlıklarına göre gruplanmıştır. "Kodda" sütunu arka uç ve ekranı birlikte değerlendirir.

| Konu | Kararın istediği (özet) | Kodda |
|---|---|---|
| Talep ve teklif (SAT-D22…D26, D38…D40, D44, D72, D234) | Tek onaycılı talep; teklif geçerliliği; Ayarlar'dan kayıp nedeni; süresi geçmiş teklif yönetici onayıyla | Var. Eksik: teklif PDF'i; ayar ekranı |
| Süresi dolan teklif için dönüşüm talebi (SAT-D476) | Satışçı talep açar, iş kutusuna düşer, onay sonrası dönüşür | Yok. Bugün yönetici doğrudan onaylar; talep kaydı yok (kodda `SAT-D476` araması 0 sonuç) |
| Taban fiyat ve iskonto (SAT-D1, D2, D34…D37, D42, D43, D45, D61…D70, D77…D84, D116…D118) | Taban altı ve limit aşımı ayrı kişinin onayına gider; limit grupları Ayarlar'da | Arka uç var. Limit aşımı onayının ekranı yok |
| Fiyat kademe zinciri (SAT-D9, D57…D60) | Anlaşma > cari özel > cari grubu > şirket; yoksa yetki ve gerekçeyle elle | Var (`IProductQuantityPriceReferenceReaderV2`) |
| Baz + ekstra fiyat yapısı (SAT-D446…D449) | Günlük baz fiyat listede; kalınlık, genişlik, kalite, kaplama ekstraları Ayarlar'da; onayda sabitlenir; kırılım ekranda ve teklif PDF'inde | **Yok.** `PriceExtra`, `ExtraRule`, `Ekstra` araması 0 sonuç. Karar, alanlar ve formül için ayrı karar ve örnek hesap istiyor |
| Marj (SAT-D242…D275, D426…D431, D438) | Stok'un ortalama maliyetiyle marj; iç onayda sabitlenir; yalnız yetkiliye görünür | Var ve açık (`MarginGoldenAccepted => true`). Hem alınan hem üretilen ürün ile birim farkı olan satırda "kullanılamıyor" |
| Risk (SAT-D46…D53, D80, D108, D153…D162, D183, D281…D287, D310, D313, D348…D354, D406, D415) | Limit kontrolü; risk istisnası; sevkte risk kapısı; sevk planına göre kalan | Var. İstisnanın tüketildiği bilgisi Satış'a olay dağıtıcısıyla gelir; dağıtıcı kapalı |
| Kredi limiti uyarı kaydı (SAT-D357) | Onaya gönderme ve onayda uyarı kaydı | Arka uç var, ekranı yok |
| Sipariş onay eşiği (SAT-D6, D174) | Şimdilik eşik yok | Uyumlu; ayar ekranı var ama Satış okumuyor |
| Revizyonlar (SAT-D3, D103…D105, D111, D112, D126, D128, D144, D167, D210…D212, D219, D220, D373) | Kritik değişiklik onayı düşürür; eski sürüm korunur | Var |
| Termin (SAT-D370, D375, D398, D400) | Sipariş düzenleme izni ya da termin izni olan değiştirir; onayı düşürmez | Var (`SalesDraftService.cs` içinde SAT-D400) |
| İptal ve kapatma (SAT-D4, D12, D31, D168, D213…D216, D221, D228) | Etki önizlemeli iptal; satır kapama; zorla kapatma | Var. Kullanıcı kapatması karar gereği kapalı (SAT-D221), ama faturalama artık var olduğu hâlde açılamıyor (§6.3) |
| Geri açma (SAT-D217, D218) | Kapatılan satır veya sipariş onayla geri açılır | **Yok** |
| Müşteri kabulü (SAT-D50, D113…D115, D121, D165, D166, D173) | Kabul beklerken sevk ve fatura kuralları | Var |
| İmzalı sipariş sözleşmesi (SAT-D471) | İmzalı belgenin kaydı müşteri kabulünü aynı işlemde gerçekleştirir | Yok (`SAT-D471`, `WP13`, `SignedContract` araması 0 sonuç). Kabul elle ayrı kaydedilir |
| Sözleşme (SAT-D28, D345, D346, D358, D361…D364, D368, D369, D374) | Ayrı kayıt; taksit planı; iskonto sınırı aşılırsa onay; belge kategorileri Ayarlar'dan | Arka uç var; ekran bağlı değil; belge kategorisi listesi ve iletişim günlüğü yok (`SalesOrderDocumentsWorkspace.tsx` başlık notu) |
| Vade ve taksit (SAT-D86, D123…D125, D127, D129, D131, D132, D138, D156, D157, D227, D233, D237, D245) | Vade tipleri, karma vade, taksit yuvarlaması, tatil günü | Var |
| Vergi (SAT-D73…D76, D82, D93, D97, D133…D135, D139, D140, D147, D148, D158, D180…D186, D231, D381, D395, D469) | KDV ve tevkifat satır bazında, Ayarlar'dan; alıcı durumu sipariş bazında | Var |
| İhraç kayıtlı satışta süre takibi (SAT-D100) | Üç şirkette yapılır; ERP ihracat süresini izler ve uyarır (öneri) | Alıcı durumu var; süre takibi ve uyarı yok |
| Fason (SAT-D175…D178, D195, D201, D309, D312, D376…D380, D399, D402) | Fason hizmet siparişi; tartıya göre fatura; kur günü; vade | Var. Kesinleşmiş fason faturanın iptali ve tartı düzeltmesi yok |
| Fatura akışı (SAT-D7, D85…D92, D101, D102, D107, D172, D202, D204, D208, D223…D226, D229, D232, D241, D311, D427, D428) | Taslak, onay, eşleştirme, kesinleştirme, Finans'a kayıt; arka planda otomatik yeniden gönderim yok | Var |
| Önce-fatura / depo teslim (SAT-D94, D98, D99, D108, D109, D131, D193) | Siparişe istinaden toplu fatura, sonra irsaliyeler; fazlası için ek fatura | **Yok.** Risk formülünde yeri hazır, fatura tarafı yok |
| Dışa aktarım (SAT-D91, D236, D244, D248, D278…D280) | Excel ve e-fatura XML; tarih aralığı; toplu | Excel arka uçta var, ekranı yok. XML yok; fatura tipi, VKN/adres kaynağı ve paketleme kararı bekliyor |
| Resmî fatura iptali (SAT-D96, D378) | Dışarıda iptal edilen fatura ERP'ye kaydedilir; Finans kalemi ters çevrilir | Akış yazılmış, kanıt kaynağı yer tutucu olduğu için çalışmaz |
| Müşteri iadesi (SAT-D8, D33, D288…D294, D412) | İade faturası; orantılı tutar; alacak azaltma; ayrı menü yok | Var. Menüde ayrı "Müşteri iadeleri" maddesi var (karar ve master "ayrı menü yok" diyor) |
| Kg/adet ve kantar (SAT-D319…D325, D332, D341…D344, D389, D405, D409, D421, D424, D425, D434…D439, D465, D473, D474, D477) | Adet fiyatlı satış, kg fiyatı göstergesi, kantar farkı | Var (`SalesKilogramPriceCalculator.cs`, `SalesDispatchWeighingDifferenceHost.cs`) |
| Kaynak planı (SAT-D179, D251…D255) | Sistem kaynak önerir; karma bölme; teslime kadar serbest | Kayıt ve ekran var. Öneri çalışmıyor: serbest stok okunmuyor; ürün tipi → üretim/satın alma eşlemesi kararsız |
| Fazla teslim onay talebi (SAT-D417, D475) | Plan üstü sevkte talep kaydı, iş kutusu, onay | Lojistik'te plan üstü kaydı var (`LogisticsDbContext.PlanExcess.cs`); iş kutusuna düşmesi doğrulanamadı |
| Roller (SAT-D141…D146, D150, D151, D163, D164, D187…D192, D194, D196, D197, D230, D249, D276, D282, D287) | Persona ve izin eşlemesi | İzin adları kayıtlı; hiçbir role verilmemiş |

Master rapora göre ayrıca eksik: S-11 iletişim zaman çizgisi yalnız belgeleri gösterir, iletişim kaydı yok; S-06 onay kutusu
dokuz onay türünden yalnız birini listeler; fiyat sapması raporu boş.

### 5.2 Bir ERP'de olması gerekene göre

Hedef belgelerinde açık karşılığı olmayan, ama çelik servis merkezi ve metal ticareti yapan üç şirketli bir grupta beklenenler:

- **Fiyat farkı ve vade farkı faturası.** İade faturası var; fiyat düzeltmesi (alacak/borç dekontu) yok.
- **Kesinleşmiş faturanın iptali.** Yanlış kesinleştirilen fatura bugün yalnız iade faturasıyla dengelenebilir.
- **Peşinat/avans ve sipariş bağı.** Sipariş avansının siparişle ilişkisi Finans'ta kalır; siparişte avans görünümü yok.
- **Sevk öncesi proforma.** `Proforma` araması 0 sonuç. İhracat ve peşin satışta istenir.
- **Sipariş kopyalama ve tekrar eden sipariş.** Satış uçlarında `copy`, `clone` araması 0 sonuç.
- **Sipariş listesinde arama ve süzme.** Liste yalnız onay durumuna göre süzülür (`approvalState`); müşteri, tarih, ürün,
  sorumlu süzgeci yok. Üst genel arama sipariş numarasını bulur.
- **Satışçı performansı, müşteri/ürün bazında ciro ve karlılık raporu.** Üç rapor var; ciro ve karlılık raporu yok.
- **Grup içi satış (GDC ↔ GSP ↔ GIN).** Bir şirketin satış siparişinin öteki şirkette alış siparişine dönmesi için bağ
  yok (Satış ve Satın Alma'da `intercompany`, `grup içi` araması 0 sonuç).
- **Müşteriye bildirim.** Sipariş onayı, sevk ve fatura için e-posta gönderimi yok (karar gereği ilk sürümde entegrasyon yok).
- **Tahsilat görünümü.** Sipariş 360'ta faturalar görünür; faturanın tahsil durumu görünmez (finansal kapanış ekseni boş).

### 5.3 Ekran ve kullanım kolaylığı

- **Durumlar iyi.** Sayfalar ortak durum bloğunu kullanır: yükleniyor, boş, yetkisiz, bulunamadı, çakışma, çevrimdışı, 503,
  bozuk yanıt ayrı gösterilir (`StateBlock`, `classifyOutcome`). Sonucu belirsiz kalan komut aynı anahtarla yeniden denenir.
- **Toplu işlem yok.** Onay, fatura taslağı ve kapama tek tek yapılır.
- **Dışa aktarım yok.** Ön yüzde listelerden Excel/CSV indirme görülmedi; tek dışa aktarım fatura Excel'idir ve ekranı yok.
- **Çıktı.** Yalnız sipariş PDF'i var. Teklif, proforma ve sözleşme çıktısı yok.
- **Sipariş detayı çok yüklü.** `SalesDraftDetailPage.tsx` 34 ayrı satış bileşenini tek sayfada toplar (fiyat, vergi, risk,
  kaynak planı, revizyonlar, kapama, fason fatura). Master §5 ortak şablonuna uyum ekranda görülerek doğrulanmalı.
- **Sayfalama.** Sözleşme ve iade listelerinde sayfalama parametresi yok; kayıt arttıkça yavaşlar.
- **Onay kutusu dağınık.** Onaylar hem `/satis/onaylar` hem Çalışma Merkezi'nde görünür; ikisi de yalnız sipariş onayını gösterir.

## 6. Diğer modüllerle bağlantılar

"Port" bir modülün başka modüle sorduğu salt okunur sorudur; "olay" kayıtla aynı işlemde yazılan ve sonradan dağıtılan
bildirimdir; "katılımcı" bir modülün işlemine aynı veritabanı işlemi içinde katılan öteki modül parçasıdır.

### 6.1 Satış kimden ne alır

| Kimden | Ne | Mekanizma | Durum | Kanıt |
|---|---|---|---|---|
| Cari | Müşteri hesabı, adres ve ilgili kişi | Port | Gerçek | `Program.cs:241-244` (`CounterpartySalesPartyAccountPortV1`) |
| Ürün | Satışa uygunluk, kalem tipi, fiyat kademesi, teorik kg/adet | Port | Gerçek | `ProductPriceListRegistration.cs:50,55` |
| Ayarlar | Para/birim profili, vergi, kur, tolerans, iskonto limiti, teklif politikası | Port | Gerçek | `SettingsModuleExtensions.cs:63,133` |
| Kimlik | Satış sorumlusu adayları, kullanıcı adı | Port | Gerçek | `SalesUserDisplayHost.cs` |
| Stok | Siparişin tahsisleri (fiziksel ve ileri tarihli) | Port | Gerçek | `InventoryModuleExtensions.cs:70,73` |
| Stok | Marj için ortalama maliyet | Port | Gerçek | `TrainCompletionHostComposition.cs:60` |
| Stok | Serbest stok (kaynak önerisi için) | — | **Yok** | `SalesDraftService.SourcePlan.cs:115` |
| Lojistik | Sevk edilen miktar, yükleme satırları, irsaliyeler, faturalanabilir sevk payları, müşteri iadeleri, sevk planı miktarı | Port | Gerçek | `LogisticsModuleExtensions.cs:62-83` |
| Lojistik | Sevkin risk istisnasını kullandığı bilgisi | Olay | Kayıtlı, dağıtıcı kapalı | `SalesRiskExceptionUsageInboxHost.cs` |
| Finans | Açık alacak, kredi limiti, teminat | Port | Gerçek | `FinanceModuleExtensions.cs:137` |
| Finans | Onaylı resmî fatura kaydı (eşleştirme için) | Port | Gerçek | `FinanceModuleExtensions.cs:142` |
| Satın Alma, Üretim | Sipariş satırına bağlı açık tedarik ve üretim | Port | Gerçek | `Program.cs:440-447` |
| Üretim | Fason siparişin plana bağlı olup olmadığı | Port | Gerçek | `Program.cs:453` |
| Belgeler | Sipariş belgeleri; PDF'te logo ve antet | Belge kasası | Gerçek | `SalesOrderDocumentsWorkspace.tsx` |

### 6.2 Satış kime ne verir

| Kime | Ne | Mekanizma | Durum | Kanıt |
|---|---|---|---|---|
| Stok | Onaylı sipariş satırı (tahsis için) | Port | Gerçek | `ISalesOrderLineReferencePortV1`, Stok'ta 3 dosya kullanır |
| Stok | İptal veya miktar azaltmada ileri tarihli ayırmanın bırakılması | Katılımcı | Gerçek | `Program.cs:474-476` |
| Lojistik | Onaylı sipariş satırı (yükleme talimatı için) | Port | Gerçek | Lojistik'te 5 dosya kullanır |
| Lojistik | Sevkte risk kapısı sonucu | Port | Gerçek | `LogisticsModuleExtensions.cs:140`, `LogisticsStore.cs:444` |
| Satın Alma | Sipariş satırından alım talebi kaynağı | Port | Gerçek; alım talebini Satın Alma açar | `PurchasingRequestSalesSourcePort` (`Program.cs:442`) |
| Üretim | Plan–sipariş bağı için onaylı satır; sipariş kuru | Port | Gerçek; bağı Üretim kurar | `POST manufacturing/plans/{planId}/sales-order-link`, `ManufacturingSalesOrderInputRateReadPortV1` |
| Finans | Kesinleşen fatura → açık kalem | Aynı istekte doğrudan çağrı, elle yeniden gönderim | Gerçek | `SalesFinanceAdmissionHost.cs` |
| Finans | Kesinleşen iade faturası → alacak azaltma | Doğrudan çağrı | Gerçek | `FinanceInvoiceAcceptanceHost.cs:31` |
| Finans | Resmî iptal → ters kalem | Doğrudan çağrı | Yazılmış; kanıt kaynağı olmadığı için tetiklenemez | `SalesFinanceCancellationHost.cs` |
| Muhasebe | `sales.invoice.finalized`, iade ve resmî iptal olayları → fiş taslağı | Olay | Kayıtlı; iki anahtar kapalı | `SalesOutboxEventSourceV1.cs`, `Program.cs:497-499` |
| Muhasebe | Sevkten kesinleşmiş fatura payına ters okuma (satılan malın maliyeti için) | Port | Gerçek | `AccountingInventoryCostSourceReaderV1.cs` |
| Çalışma Merkezi | Onay bekleyen siparişler; satır sorumlusu | Port | Gerçek, yalnız sipariş onayı | `Program.cs:203` |

### 6.3 Kopuk ya da eksik halkalar

- **Satış üretim talebi ya da alım talebi açmaz.** Kaynak planı yalnız niyeti kaydeder ("şu kadarı stoktan, şu kadarı
  üretimden"); kaydetmek tahsis, üretim emri ya da alım talebi oluşturmaz. Bağı öteki modüldeki kullanıcı kendi ekranından
  kurar. §8.1'deki "Kaynak Stratejisi → Satın Alma veya Üretim" halkası bu yüzden elle yürür.
- **Sipariş kapanışı.** Faturalama verisi artık Satış'ın kendi kayıtlarında, tahsilat verisi Finans'ta duruyor; ama bunları
  "kapanmaya hazır" hesabına taşıyan bağ yazılmamış. Karar "hepsi gelene kadar kapalı" diyor (SAT-D221); sahiplerin bir kısmı
  geldi, bağ gelmedi.
- **Fatura → muhasebe fişi.** Olay yazılıyor; dağıtıcı kapalı olduğu sürece fiş doğmaz. Çift kayıt riski görülmedi: Finans
  kabulü aynı kimlikle tekrar edilebilir biçimde yazılmış.
- **Risk istisnasının tüketimi.** Lojistik kendi tarafında tek kullanım kaydını tutuyor (`ShipmentRiskExceptionUses`), yani
  aynı istisnayla ikinci sevk engelleniyor. Satış ekranındaki "kullanıldı" bilgisi ise dağıtıcı açılınca güncellenir.
- **Sözleşme yalıtık.** Sözleşmedeki taksit planı ve iskonto siparişe ya da faturaya taşınmıyor.
- **Veri sahipliği.** İhlal görülmedi: Satış başka modülün tablosuna yazmıyor; irsaliye Lojistik'te, açık kalem Finans'ta,
  resmî fatura kaydı Finans'ta, maliyet Stok'ta.

## 7. Açık kararlar

**Açık üst kararlar.** OPEN-004 (tolerans sayılarının saha onayı), OPEN-009 (onay eşikleri matrisi), OPEN-013 (canlı kimlik
sağlayıcısı ve rol ataması), OPEN-016 (e-belge ve muhasebe entegrasyonu) "kısmen" durumundadır
(`docs/decisions/ACIK_KARARLAR.md`). Test ortamında kullanımı engelleyen OPEN-013'ün rol atama kısmıdır.

**Kodda "NEEDS-SINAN" (karar bekliyor) işaretleri.** Satış arka uç kaynaklarında 25, modül README'sinde 17, satış ekranlarında
0 adet. Kaynaktaki 25 işaretin konuları:

| Konu | Adet | Etkisi | Kullanımı engeller mi |
|---|---:|---|---|
| Kaynak planı: ürün tipi → üretim/satın alma eşlemesi; kg dışı birim; "teslim edilen" sevk mi teslim mi; teslim edilen kısmın kaynağı; kaldırılacak satır | 11 | Öneri "kullanılamıyor"; kg olmayan satırda plan kaydedilemez | Hayır; kaynak planı elle girilir, adetli satırda girilemez |
| Eşleştirme farkı: resmî belge ERP tutarından farklıysa Finans hangi tutarı açar | 5 | Farklı eşleşen fatura kesinleştirilemez | **Evet**, kuruş farkı toleransı (0,05 TL) aşan her faturada |
| Marj: hem alınan hem üretilen ürün; maliyet birimi sipariş biriminden farklı | 4 | O satırda marj "kullanılamıyor" | Hayır |
| İki iznin hangi personada olacağı | 2 | Atama insan kapısı | Rol kurulumunun parçası |
| Risk istisnası tüketimini Satış'ın nasıl öğreneceği | 1 | Olay dağıtıcısına bağlı | Hayır |
| Net fiyat yuvarlaması | 1 | Fiyat yuvarlanmadan saklanır | Hayır |
| Fiyatlı satırı olan siparişte müşteri ya da para birimi değişikliği | 1 | Değişiklik reddedilir | Hayır |

**Karar verilmiş, ayrıntısı bekleyenler.** Baz + ekstra fiyat tablosunun alanları ve formülü (SAT-D446 "Sınırlar"); e-fatura
XML'inde fatura tipi, VKN/adres kaynağı ve paketleme (SAT-D278); kesinleşmiş fatura iptalinde ters fişin dönemi ve tarihi
(`W8_satis.md`, SAT-SC34 notu); geri açılan siparişte faturalama ve finansal kapanış ekseninin yeniden hesabı (SAT-D218).

## 8. Yapılacaklar

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---:|---|---|---|---|---|
| 1 | Satış iş rollerini tanımla ve 40 izni ata (onaylı öneri tablosuna göre) | İzin olmadan hiçbir satış ekranı açılmaz | P0 | K | Sinan'ın rol ataması (insan kapısı) |
| 2 | Üç şirket için satış kurulum verisi kontrol listesi: profil, vergi, kur, tolerans, iskonto grupları, fiyat listesi, kredi limiti | Biri eksikse sipariş ya da onay 503/422 ile durur | P0 | K | Ayarlar, Ürün, Cari, Finans kurulumları |
| 3 | Limit aşan iskonto onayına ekran ekle | Arka uç onay istiyor, onaylayacak ekran yok; iskontolu sipariş takılır | P0 | K | — |
| 4 | Fatura Excel dışa aktarımına ekran ekle | Resmî faturayı dışarıda kesecek kişi veriyi ERP'den alamıyor | P0 | K | — |
| 5 | Eşleştirme farkı politikasını karara bağla ve uygula | Farklı eşleşen fatura kesinleşmiyor, alacak açılmıyor | P0 | K–O | Sinan kararı |
| 6 | Olay dağıtıcısı ve Muhasebe kabul anahtarlarını test ortamında aç | Satış faturası muhasebe fişine ulaşsın | P0 | K | Sinan onayı (insan kapısı); Muhasebe hazır olmalı |
| 7 | Resmî fatura iptal kaydı için kanıt kaynağını tanımla (dış entegrasyon yokken neyin kanıt sayılacağı) | Kesinleşmiş hatalı fatura geri alınamıyor | P0 | O | Sinan kararı; ters fiş dönemi kararı |
| 8 | Sözleşme ekranını adrese ve menüye bağla, yetenek kimliğini yayımla | Yazılmış ekran kullanılamıyor | P1 | K | — |
| 9 | Sipariş kapanış eksenlerini bağla (faturalama Satış'tan, finansal kapanış Finans'tan, kaynak Stok/Üretim/Alış'tan) | Sipariş yalnız zorla kapatılabiliyor | P1 | O | Finans'ta fatura bazında tahsil durumu okuması |
| 10 | Kapatılan satır ve siparişi onayla geri açma | Karar var (SAT-D217/D218), kod yok | P1 | O | — |
| 11 | Baz + ekstra fiyat yapısı | Günlük fiyat verme biçiminin karşılığı; karar var | P1 | B | Tablo alanları ve formül kararı, örnek hesap |
| 12 | Önce-fatura (depo teslim) akışı | Karar var (SAT-D94), fatura tarafı yok | P1 | B | Lojistik'te faturaya istinaden irsaliye |
| 13 | Teklif PDF'i (baz/ekstra kırılımıyla) | Müşteriye teklif gönderilemiyor | P1 | O | 11 (kırılım için) |
| 14 | Onay kutusuna öteki sekiz satış onayını ekle | Bekleyen onaylar görünmüyor, işler takılı kalır | P1 | O | Çalışma Merkezi okuma sözleşmesi |
| 15 | Süresi dolan teklif ve fazla teslim için talep kaydı | Karar var (SAT-D475/D476) | P1 | O | 14 |
| 16 | Kaynak planında serbest stok okuması ve ürün tipi eşlemesi | Öneri çalışsın; adetli satır planlanabilsin | P1 | O | Stok serbest miktar portu; Sinan kararı |
| 17 | Fiyat sapması raporunu fatura kayıtlarına bağla | Rapor boş | P1 | K | — |
| 18 | Kredi limiti uyarı kayıtlarını ekranda göster | Kayıt var, görülmüyor | P1 | K | — |
| 19 | Teklif politikası ve e-fatura senaryosu ayar ekranları | Uçlar var, ekran yok | P1 | K | — |
| 20 | İmzalı sözleşme kaydının müşteri kabulünü tetiklemesi | Karar var (SAT-D471) | P1 | K | Belgeler'de imzalı belge türü |
| 21 | Sipariş listesine müşteri, tarih, sorumlu süzgeci ve Excel indirme | Günlük kullanımda liste aranamıyor | P1 | O | — |
| 22 | e-fatura XML dışa aktarımı | Karar var; ayrıntılar açık | P2 | O | Fatura tipi, VKN/adres, paketleme kararı |
| 23 | Fiyat farkı faturası, proforma, sipariş kopyalama | ERP beklentisi | P2 | O | Karar |
| 24 | İhraç kayıtlı satışta süre takibi ve uyarı | Karar önerisi (SAT-D100) | P2 | K | — |
| 25 | Sözleşmeyi siparişe bağlama (taksit ve iskonto taşınması) | Sözleşme yalıtık | P2 | O | Karar |
| 26 | Modül README'sini koda göre güncelle | Bağlama, marj ve satır kapama bölümleri eski | P2 | K | — |

## 9. Doğrulanamayanlar

- Testlerin geçip geçmediği ve CI durumu (koşulmadı; `W8_satis.md` çoğu dilim için "NOT_RUN" diyor).
- Ekranların gerçek tarayıcıda görünümü, master §5 şablonuna uyumu ve telefonda kullanımı.
- İş rolü oluşturup izin atamanın ekrandan yapılıp yapılamadığı (Kimlik parçasının konusu; `identity/IdentityRoleDetailPage.tsx`
  ve `AccessGrantPanel.tsx` var, akış izlenmedi).
- Başlangıç satış politikalarının (`baseline-transfer` uçları) hangi ekrandan aktarıldığı ve yeni şirkette toleransın ve
  onay eşiğinin kendiliğinden gelip gelmediği.
- Fazla sevk talebinin (SAT-D475) Lojistik'te iş kutusuna düşüp düşmediği; Lojistik parçasında bakılmalı.
- GSP ve GIN için kg/TRY profilinin (SAT-D18, D19) kurulu olup olmadığı; veriye bağlı.
- Dövizli siparişte kurun Ayarlar'a nasıl girildiği (elle içe aktarım, SAT-D205); Ayarlar parçasının konusu.
- `KilogramPriceGoldenAccepted` ve `MarginGoldenAccepted` anahtarları kodda açık; ilgili örnek hesap kabulünün (PR #1201)
  bağımsız inceleme imzasını aldığı bu çalışmada doğrulanmadı.
