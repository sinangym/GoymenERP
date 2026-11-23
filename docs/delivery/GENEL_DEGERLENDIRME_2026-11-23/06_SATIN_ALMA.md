# Satın Alma ve İşletme Alımları — durum ve yapılacaklar

> Kapsam: Satın Alma modülü (talep, sipariş, mal/hizmet kabul, gelen fatura, tedarikçi iadesi, ek masraf) ve master
> raporun 6. menü maddesi "İşletme Alımları". Depo HEAD `ddc6cba`, 23 Kasım 2026. Yalnız kod okunarak hazırlandı;
> derleme ve test koşulmadı. Bu belge karar veya onay değildir; hiçbir açık kararı kapatmaz.

## 1. Kısa sonuç

- Satın alma zincirinin **çekirdeği kodda yazılmış**: talep (gönder, onayla, reddet, geri çek), sipariş (fiyat,
  iskonto, vade farkı, döviz, termin dilimleri, onay, kalan iptali, cari değişikliği), mal kabul (tekli ve toplu,
  kantar kg'ı elle, geri alma), tedarikçiye iade, hizmet ve giden fason siparişi, ek masraf kaydı, gelen fatura
  (dosyadan içe aktarma, kabulle eşleştirme, fark onayı, kesinleştirme, Finans'a borç devri).
- **Mal kabul gerçekten stok girişi yapıyor**: Satın Alma ve Stok kayıtları tek veritabanı işleminde birlikte yazılıyor.
- **Gelen fatura ekranları yazılmış ama menüde kapalı**: sunucu bu ekranları "henüz yayımlanmadı" diye bildiriyor;
  kullanıcı fatura listesine ve içe aktarmaya ulaşamıyor. Zincir bugün ekrandan "mal kabul"de bitiyor.
- **Hiçbir satın alma yetkisi hiçbir rolde yok**: 19 izin adı tanımlı, başlangıç rollerinin hiçbirinde değil. Rol
  atanmadan kimse hiçbir satın alma ekranını açamaz.
- **Talep ile sipariş ekranda birbirine bağlı değil**: yeni sipariş formu talepten bağımsız, tek satırlı sipariş açıyor.
- **Tolerans ayarları eksik bağlı**: mal kabul kg toleransını okuyan bağlantı yazılmamış; toplu kabulde kalan kg'dan
  her fark onaya düşüyor. Fazla teslim hiç kabul edilmiyor (karar var, kod yok).
- **Hiç olmayanlar**: işletme alımları (sarf, bakım, yedek parça), ithalat (akreditif, gümrük, konşimento, antrepo),
  teklif toplama/karşılaştırma, tedarikçi fiyat listesi, tedarikçi performansı, minimum stoktan sipariş önerisi,
  sipariş PDF'i, tedarikçi teyidi, sevk bildirimi, siparişsiz kabul, tedarikçi avansı.
- En önemli beş eksik: (1) rol/yetki, (2) fatura ekranlarının açılması, (3) talepten çok satırlı sipariş, (4) tolerans
  ve fazla teslim, (5) işletme alımları ile ithalatın hiç olmaması.

## 2. Hedef: bu modülden ne isteniyor

**Master rapor.** §4 menü sırası 4: "Siparişler; İrsaliye & Fatura". §7.3: talep ve sipariş, miktar bazlı termin
dilimleri, tedarikçi performansı; sipariş, kabul, gelen fatura ve ödeme planı ayrı kayıt; mal kabul siparişi,
irsaliyeyi, tartımı, kaliteyi ve fiilî miktarı karşılaştırır; hizmet kabulü stok hareketi yaratmaz; üçlü kontrol
(sipariş ↔ kabul ↔ fatura). §8.2 zinciri: Talep → Onay → Sipariş → Termin/Sevk Bildirimi → Kabul → Üçlü Kontrol →
Gelen Fatura → Cari Açık Kalem → Ödeme. §25.5: teslim alınmamış sipariş kalanı Stok'ta "gelecek arz" görünür, fiziksel
stok sayılmaz. §27.3 on iki ekran (P-01…P-12). §27.4 IA-01/IA-02 işletme alımları. §29 kabul senaryoları AT-025
(alış/satış listeleri karışmaz), AT-026 (fazla/eksik teslim toleransı), AT-027 (fiyat/miktar farkı onaysız faturaya
geçmez), AT-028 (iade kaynak bağını korur), AT-064 (kalan, kabulden önce stoğu artırmaz).

**Bağlayıcı kararlar** (`docs/decisions/SINAN_KARARLARI_2026-11-02_ALIS.md` ve EK1…EK17, sonraki dalga ekleri):

| Konu | Karar (özet) | Kimlik |
|---|---|---|
| Talep | Müdür onaylar; hazırlayan ≠ onaylayan; sipariş yalnız onaylı talepten; oluşturan/gönderen geri çekebilir | ALS-D2, D46, D47 |
| Sipariş onayı | Uzman hazırlar, müdür onaylar; tutar eşiği Ayarlar'da, başlangıçta kapalı | ALS-D3 |
| Fiyat | Elle, iskontolu; fiyatsız sipariş onaylanabilir, fiyat sonra Rev+1 ile girilir; 4 ondalık; vade farkı ayrı | ALS-D5, D18, D38, D72 |
| Revizyon | Onaylı siparişte değişiklik Rev+1 ve yeniden onay; yalnız termin değişirse onay istenmez | ALS-D9, D94 |
| Kabul ve kalite | Kabul edilen mal serbest stoğa girer; sorun olursa etiket bloklanır | ALS-D4 |
| Tolerans | Uyarı %1 / onay %2 / blok %3, Ayarlar'dan; fazla teslim tolerans içinde kabul, üstü ayrı izinle onay | OPEN-004, ALS-D43, D102, D114, D116 |
| Satır kapama | Tolerans içi eksik teslimde tek tıkla kapanış; dışında gerekçeli; yeniden açılabilir | ALS-D10, D12, D101 |
| Kur | Kabul gününden önceki iş günü TCMB; her kabul kendi günü; borç kuru tedarikçi faturasının kuru | ALS-D6, D14, D41 |
| Gelen fatura | Mikro'dan dosya ile içe aktar ve kabulle eşleştir; her fark (ucuz gelse de) müdür onayı; tolerans başlangıçta 0 | ALS-D7, D8, D15 |
| Borç | Tek kayıt Finans'ta; fatura kesinleşince Finans'a devredilir | ALS-D59, D62, D64 |
| Ek masraf | Siparişe/kabule masraf satırı; başka cariden de olabilir; v1'de ortalama maliyete girmez | ALS-D23, D27, D80, D107 |
| Hizmet ve fason | Hizmet satırıyla alınır, stok hareketi yok; fason = kendi malımızı dışarıda işletmek | ALS-D29, D40, D105, D106 |
| Gelecek tedarik | Açık alış satırından satış siparişine miktar bazında ayırma; aynı miktar iki siparişe verilmez | ALS-D30, D82…D93 |
| Ertelenenler | Teklif karşılaştırma son dalgada; avans ve gümrük/ithalat "ikisi de sonra"; minimum stok önerisi yok | ALS-D11, D50, D61 |
| Diğer | Tedarikçi teyidi (isteğe bağlı), fiyatlı sipariş PDF'i, siparişsiz kabul müdür onayıyla, şoför bilgisi, tedarikçi performans ölçütleri, mükerrer fatura uyarısı | ALS-D13, D34, D35, D39, D48, D125 |

## 3. Bugün var olan

Seviye: 0 = yok, 1 = kod yazılmış, 1-kısmi = bir tarafı eksik. Test kaynağı sütunu yalnız dosyanın var olduğunu
söyler; koşulup koşulmadığını koordinatör ekleyecek. Arka uç kökü `src/backend/Modules/Purchasing/`, uçlar
`src/backend/GoymenERP.Api/Endpoints/Purchasing*.cs` (79 adlı uç), ekranlar `src/frontend/goymen-erp-web/src/purchasing/`.

### 3.1 Talep

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Talep oluşturma (çok satırlı, elle) | Var | Var | Talepler listesinden | Var | 1 | `Domain/Requests/PurchaseRequest.cs`, `PurchaseRequestCreatePage.tsx` |
| Gönder, onayla, reddet | Var | Var | Talep detayı | Var | 1 | `PurchasingService.RequestDecisions.cs`, `PurchaseRequestDecisionPanel.tsx`; hazırlayan ≠ onaylayan serviste |
| Geri çekme (oluşturan/gönderen) | Var | Var | Talep detayı | Var | 1 | `PurchaseRequestWithdrawalPanel.tsx` |
| Talep kaynağı: Satış satırı | Var | Yalnız gösterim | — | Var | 1-kısmi | `PurchaseRequestLineSource.cs`; ekran yalnız "elle" talep açıyor |
| Talep kaynağı: Üretim, Stok | Yalnız alan | Yok | — | — | 0 | Üretim ve Stok kodunda talep açan çağrı yok (`PurchaseRequest`, `purchasing/requests` araması bu modüllerde 0) |
| Talepten sipariş açma | Var (isteğe bağlı) | **Yok** | — | Var | 1-kısmi | Servis onaylı talebi doğruluyor (`PurchasingService.cs:292`), ama form `sourceRequestId: null` gönderiyor (`PurchaseOrderCreatePage.tsx:243`) |

### 3.2 Sipariş

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Sipariş oluşturma | Var (çok satır) | **Tek satır** | Siparişler listesinden | Var | 1-kısmi | Form tek ürün satırı gönderiyor (`PurchaseOrderCreatePage.tsx:236-246`) |
| Fiyat, iskonto (% / tutar), döviz | Var | Var | Yeni sipariş | Var | 1 | `Domain/Orders/PurchaseOrderPricing.cs`; para profili Ayarlar'dan (`purchasing.order.v1`) |
| Vade farkı, KDV, tevkifat toplamları | Var | Bulunamadı | — | Var | 1-kısmi | `PurchasingService.Totals.cs`, uç `GetPurchaseOrderTotalsV1`; ekranda çağıran yok |
| Sipariş numarası (Ayarlar serisinden) | Var | Gösterim | — | Var | 1 | `PurchasingStore.DocumentNumbers.cs`; seri tanımlı değilse sipariş açılmaz (503) |
| Onay (hazırlayan ≠ onaylayan) | Var | Var | Sipariş detayı | Var | 1 | `PurchaseOrder.Approve`, `approvalApi.ts` |
| Sipariş kuru görme/değiştirme | Var | **Yok** | — | Var | 1-kısmi | `PurchasingService.OrderExchangeRate.cs`; ekranda `exchange-rate` çağrısı yok |
| Termin dilimleri ve geçmişi | Var | Var | Sipariş detayı | Var | 1 | `PurchasingService.LineSchedule.cs`, `LineSchedulePanel.tsx`; teslim alınan kısım kilitli |
| Kalan iptali + etki önizleme | Var | Var | Sipariş detayı | Var | 1 | `PurchasingService.OrderCancellation.cs`, `PurchaseOrderCancellationPanel.tsx` |
| Ayırması olan siparişte "bağları düşür ve iptal et" | Var | Var | Sipariş detayı | Var | 1 | `AllocationLinkPanel.tsx`, `allocationLinkApi.ts` |
| Satır kapama / yeniden açma (eksik teslim) | Var | **Yok** | — | Var | 1-kısmi | `PurchasingService.LineClosure.cs`; ekranda `/closure`, `/reopening` çağrısı yok |
| Cari (tedarikçi) değişikliği revizyonu | Var | Var | Sipariş detayı | Var | 1 | `PurchaseOrder.SupplierRevision.cs`, `SupplierRevisionPanel.tsx`; kabul/fatura varsa reddedilir |
| Miktar/fiyat revizyonu (Rev+1) | **Yok** | Yok | — | — | 0 | `PurchaseOrder.cs` içinde yalnız termin, kur, cari revizyonu var; izin açıklaması "geldiğinde" diyor (`ErpPermissions.cs:1288`) |
| Taslak siparişi düzenleme | Yok | Yok | — | — | 0 | Sipariş tek komutla oluşur; satır ekleme/düzeltme ucu yok |
| Liste: durum ve tedarikçi süzgeci, sayfalama | Var | Var | Var | Var | 1 | `PurchaseOrderListPage.tsx`; serbest arama ve dışa aktarım yok |
| Sipariş PDF'i | Yok | Yok | — | — | 0 | Alış klasörlerinde `pdf` araması yalnız kabul metninde geçiyor |
| Sipariş onay tutar eşiği | Yok | Yok | — | — | 0 | Karar "Ayarlar'da var, kapalı" diyor; kodda eşik okuması bulunamadı |

### 3.3 Mal kabul, geri alma, iade

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Tekli mal kabul = stok girişi | Var | Var | Sipariş detayı | Var | 1 | `PurchasingRuntime/PurchaseGoodsReceiptHost.cs`: Alış payı, Stok girişi ve maliyet tek işlemde |
| Toplu kabul (çok etiket) | Var | Var | Sipariş detayı | Var | 1 | `PurchasingService.GoodsReceiptBatch.cs`, `GoodsReceiptBatchPanel.tsx` |
| Kantar kg (elle), adetli satırda teorik/tartılan kg karşılaştırma | Var | Var | Sipariş detayı | Var | 1 | `GoodsReceiptQuantityComparison.tsx`; kantar cihazı bağlantısı yok (karar gereği elle) |
| Kg toleransı ve tolerans dışı onay | Kural var, **ayar bağlı değil** | Var | — | Var | 1-kısmi | `IPurchaseGoodsReceiptPolicyReadPortV1` için uygulama yok (yalnız testte sahte); çalışan sistemde tolerans "ayarlanmamış" sayılır |
| Fazla teslim | **Yok** | Yok | — | — | 0 | Kabul kalanı aşamaz (`PurchaseOrder.cs:456`, toplu kabulde `ExceedsRemaining`); `approve-overdelivery` izni tanımlı ama kullanılmıyor |
| Kabul geri alma (tekli/toplu), sipariş canlanması | Var | Var | Sipariş detayı | Var | 1 | `PurchaseReceiptReversalHost*.cs`, `ReceiptAdjustmentWorkspace.tsx` |
| Tedarikçiye iade (stok çıkışı, maliyet, soy izi) | Var | Var | Sipariş detayı | Var | 1 | `PurchaseSupplierReturnHost.cs`, `SupplierReturnPanel.tsx`; iade faturası/cari etkisi yok (bkz. §6.3) |
| Beklenen teslimatlar (operasyon iş listesi) | Var | Var | Lojistik menüsünde | Var | 1 | `PurchaseOpenLineReadPort.cs` → `logistics/OperationsWorkPage.tsx` |
| Siparişsiz kabul istisnası | Yok | Yok | — | — | 0 | `UnorderedReceipt`, `siparişsiz` araması 0 |
| Etiket kodu üretme/öneri | Kural var, bağlı değil | Var | — | Var | 1-kısmi | `IPurchaseLabelCodeRegisterReadPortV1` uygulaması yok |

### 3.4 Gelen fatura ve üçlü kontrol

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Dosyadan içe aktarma (UBL-TR XML, XLSX, CSV) | Var | Yazılmış | **Kapalı** | Var | 1-kısmi | `Infrastructure/Imports/PurchaseInvoiceFileReader*.cs`, `InvoiceImportPanel.tsx` |
| Tedarikçi ve ürün eşleme, kayıt | Var | Yazılmış | Kapalı | Var | 1-kısmi | `PurchaseInvoiceImportAdmission.cs`, `InvoiceImportAdmissionPanel.tsx` |
| Kabulle eşleştirme (üçlü kontrol) | Var | Yazılmış | Kapalı | Var | 1-kısmi | `Domain/Invoices/PurchaseIncomingInvoice.cs` `Match`; miktar, fiyat ve kur farkı ayrı ayrı kaydedilir |
| Fark onayı (hazırlayan onaylayamaz) | Var | Yazılmış | Kapalı | Var | 1-kısmi | `ApproveVariance`; onaysız kesinleştirme reddedilir |
| Kesinleştirme ve Finans'a borç devri | Var | Yazılmış | Kapalı | Var | 1-kısmi | `PurchaseInvoiceFinanceAdmissionHost.cs` Finans açık kalem servisini doğrudan çağırıyor |
| Ek masraf ve hizmet satırının faturaya bağlanması | Var | Yazılmış | Kapalı | Var | 1-kısmi | `PurchaseIncomingInvoiceMatchSource.AdditionalCosts.cs`, `.Services.cs` |
| Elle fatura girişi | Yok | Yok | — | — | 0 | Fatura yalnız dosyadan gelir (karar gereği) |
| Mükerrer fatura uyarısı | Yok | Yok | — | — | 0 | `ALS-D125`, `duplicate`, `mükerrer` araması fatura kodunda 0 |

Menü kapalılığının kanıtı: fatura ekranları sunucuya `ImplementationAvailable: false` ile bildiriliyor
(`Infrastructure/Shell/PurchasingShellCapabilityProvider.cs:113`); kabuk bu değeri taşıyan ekranları kullanıcıya hiç
vermiyor (`GoymenERP.Api/Shell/ShellCapabilityService.cs:76`). Ön yüz rotası da aynı nedenle kapalı kalıyor
(`purchasingRoutes.tsx` açıklaması: "staged invoice routes fail closed until published").

### 3.5 Hizmet, fason, ek masraf

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Hizmet siparişi (oluştur, onayla, fiyat revizyonu) | Var | Var | "Hizmet ve fason alışları" | Var | 1 | `PurchaseServiceOrderServiceV1.cs`, `ServiceOrderPages.tsx` |
| Hizmet kabulü (stok hareketi yok) | Var | Var | Hizmet siparişi detayı | Var | 1 | `Domain/Services/PurchaseServiceOrder.cs`: "neither stock nor ownership is mutated" |
| Giden fason siparişi (kendi malzeme etiketi + beklenen çıktı) | Var | Var | Aynı ekran | Var | 1 | `PurchaseServiceOrderReferenceProofV1.cs` malzemenin şirkete ait olduğunu Stok'tan doğruluyor; fiziksel gönderim/dönüş Üretim–Stok tarafında |
| Fason KDV/tevkifat varsayılanı | Var | Var | Ayarlar | Var | 1 | `SettingsCompanyPolicyEndpoints.PurchasingSubcontractTax.cs`, `settings/SubcontractTaxDefaultPage.tsx` |
| Ek masraf kaydı (navlun vb.; başka cariden de) | Var | Var | Sipariş detayından | Var | 1 | `Domain/Expenses/PurchaseAdditionalCost.cs`, `AdditionalCostPage.tsx` |
| Ek masrafın stok maliyetine yansıması | **Bilerek yok** | — | — | Var | 0 | `Inventory/.../AverageCostCalculator.cs:288`: ek masraf v1 ortalamasına girmez (karar ALS-D80) |

### 3.6 Hiç olmayanlar (arama kanıtı)

Aşağıdaki aramalar `src/backend` ve `src/frontend/goymen-erp-web/src` altında `.cs/.ts/.tsx` dosyalarında yapıldı.

| Yetenek | Aranan | Sonuç |
|---|---|---|
| İthalat: akreditif | `akreditif`, `LetterOfCredit` | Alış'ta 0 (yalnız Finans kredi hesabı metninde 3 dosya) |
| Gümrük, beyanname | `gümrük`, `Customs` | Alış'ta 0 (Stok maliyet açıklamasında ve Ayarlar testinde geçiyor) |
| Konşimento, antrepo, teslim şekli | `konşimento`, `BillOfLading`, `antrepo`, `Incoterm` | 0 |
| Teklif toplama/karşılaştırma (P-02) | `RFQ`, `RequestForQuot`, `SupplierQuot`, `QuoteComparison` | 0 |
| Tedarikçi fiyat listesi | `SupplierPriceList`, `TedarikçiFiyat` | 0 |
| Tedarikçi performansı (P-12) | `SupplierPerformance`, `Tedarikçi performans` | 0 |
| Minimum stok / yeniden sipariş önerisi | `ReorderPoint`, `MinimumStock`, `SafetyStock`, `minimum stok` | Alış'ta anlamlı sonuç 0 |
| İşletme alımları (IA-01/IA-02) | `İşletme Alım`, `OperatingPurchase`, `IndirectPurchase`, `Sarf` | Alış'ta 0 (yalnız Üretim'de kg dışı girdi) |
| Masraf merkezi | `CostCenter`, `masraf merkezi` | 0 |
| Tedarikçi teyidi | `SupplierConfirm`, `tedarikçi teyi` | 0 |
| Sevk bildirimi, şoför bilgisi | `ShipmentNotice`, `sevk bildirim`, `DriverNational` | 0 |
| Tedarikçi avansı | `avans`, `Advance` | Alış'ta anlamlı sonuç 0 |
| Satın alma onay kuyruğu (P-11) | Ayrı ekran/rota | Yok; onaylar talep ve sipariş detayından verilir |

## 4. Kullanılabilirlik engelleri

1. **Rol ve yetki.** Satın alma izinleri (`ErpPermissions.cs:1233-1343`; 19 ad: sipariş oluştur/oku/onayla/iptal/
   revize/kapat, fiyat görme, talep onay/geri çekme, kabul kaydı, tolerans dışı kabul onayı, fazla teslim onayı,
   gelecek ayırma bırakma, iade kaydı, beş fatura izni) başlangıç rol dosyasında (`Seed/identity-seed.json`) hiçbir
   rolde yok; dosyada yalnız 5 sistem rolü var. Karar persona tanımlıyor (Satın Alma Uzmanı, Müdürü; ALS-D53) ama rol
   ve atama insan kapısı.
2. **Fatura ekranları kapalı** (§3.4). Açılmadan fatura, üçlü kontrol ve Finans'a borç devri ekrandan yapılamaz.
3. **Ön koşul ayarlar.** Sipariş açmak için: Ayarlar'da alış siparişi numara serisi (yoksa 503), para profili, döviz
   için kur kaynağı, KDV varsayılanı; Ürün'de "satın alınabilir" ürün; Cari'de tedarikçi rolü taşıyan hesap. Hizmet
   siparişi ve ek masraf için Ayarlar referans listeleri (hizmet tipi, masraf tipi). Fatura eşleştirme için "Alış
   faturası fark toleransı" yayımlanmış olmalı (yoksa eşleştirme reddedilir). Bu ayar ekranları var
   (`settings/PurchaseInvoiceVariancePolicyPage.tsx`, `ReferenceListPage.tsx`), ama izinleri de hiçbir rolde değil.
4. **Bağlı olmayan ayar.** Mal kabul kg toleransı ve etiket kodu biçimi için ayar okuma bağlantısı yok
   (`PurchasingModuleExtensions.cs:132` isteğe bağlı servis; kayıt eden yer yok).
5. **Olay dağıtıcı kapalı.** Kesinleşen faturanın Muhasebe'ye ve Stok maliyetine gitmesi, sipariş canlanma bildirimi
   "olay kutusu" (bir modülün diğerine haber bıraktığı kuyruk) üzerinden. Dağıtıcı `Work:OutboxDispatcher:Enabled`
   ayarı açılmadan çalışmaz (`Program.cs:501`); `appsettings.json` içinde bu bölüm yok. Açılması insan kapısı.
6. **Ürün çalışma zamanı.** Ürün tarafı kapalıysa sipariş formu ürün seçemez (`Program.cs:292`, 503 ile kapanır).
7. **Tek şirket bağlamı.** Bütün satın alma ekranları tek şirket seçiliyken açılır; grup görünümü yok.
8. **Veri geçişi.** Açık siparişlerin eski sistemden taşınması (ALS-D31) için araç bulunamadı.

## 5. Eksikler

### 5.1 Senin isteklerine göre (hedef var, kod yok ya da kısmi)

- **Talep → sipariş bağı ekranda yok.** "Sipariş yalnız onaylı talepten açılır" kararı ekranda uygulanmıyor; arka uç
  da talepsiz siparişe izin veriyor. Karar bu haliyle mi kalacak, netleştirilmeli (§7).
- **Çok satırlı sipariş formu yok**; taslak sipariş düzenlenemiyor.
- **Miktar/fiyat revizyonu (Rev+1) yok.** Fiyatsız onaylanan siparişe sonradan fiyat girme yolu bulunamadı; böyle
  bir sipariş faturayla eşleşemez (karar ALS-D18 bunu Rev+1 ile çözüyordu).
- **Fazla teslim ve üç kademeli tolerans yok.** Kodda tek yüzde var ve o da bağlı değil; %1/%2/%3 kademeleri ve
  fazla teslim onayı (ALS-D102, D114, D116) yazılmamış. AT-026 karşılanmıyor.
- **Tolerans içi tek tıkla satır kapama** (ALS-D101) yok; satır kapama ve yeniden açmanın ekranı yok.
- **Sipariş onay tutar eşiği** yok.
- **Tedarikçi teyidi, sevk bildirimi/şoför bilgisi, "üreticide hazır" durumu** (ALS-D13, D22, D39) yok; "yolda"
  yalnız termin dilimi olarak izleniyor.
- **Sipariş PDF'i** (ALS-D34), **siparişsiz kabul** (ALS-D35), **mükerrer fatura uyarısı** (ALS-D125) yok.
- **Siparişte ödeme planı/vade** (ALS-D24, D28): plan yalnız fatura içe aktarılırken taksit olarak giriliyor;
  siparişte vade tarihi alanı bulunamadı.
- **"Tedarik no" ve "sipariş pozisyonu"** (ALS-D20: hizmet/ticari/iade/teşvikli/ihraç kayıtlı) alanları bulunamadı.
- **Tedarikçi performansı** (P-12, ALS-D48), **teklif karşılaştırma** (P-02, ALS-D11), **satın alma onay kuyruğu**
  (P-11) yok. İlk ikisi kararla sona bırakılmış.
- **İşletme alımları** (menü sırası 6, IA-01/IA-02) hiç yok; ilgili masraf yapısı kararı Ayarlar'ı bekliyor (ALS-D79).
- **İade faturası ve cari etkisi** yok; tedarikçiye iade yalnız stok çıkışı ve maliyet kaydı.

### 5.2 Bir ERP'de olması gerekene göre

- **İthalat dosyası.** Çelik ithalatı yapan bir grup için akreditif, gümrük beyannamesi, konşimento, antrepo, teslim
  şekli, ithalat masraflarının (navlun, gümrük vergisi, ardiye, sigorta) partiye dağıtılması. Kararla ertelenmiş
  (ALS-D50), kodda hiçbir parça yok. Dövizli sipariş ve kur var; bu ithalat takibi değildir.
- **Ek masrafın stok maliyetine dağıtılması (landed cost).** Bugün masraf yalnız kayıt ve faturaya bağ; stok maliyeti
  yalnız alış fiyatını görüyor. İthal ve nakliyeli alımda stok maliyeti ve satış kârlılığı eksik çıkar.
- **Stok dışı gider alımı ve masraf merkezi.** Sarf, bakım, yedek parça, kırtasiye, hizmet faturalarının bir gider
  yerine (makine, hat, bölüm) yazılması. Yok.
- **Minimum stok ve ihtiyaç önerisi.** Stok seviyesi, açık satış ve üretim ihtiyacından satın alma önerisi. Yok;
  talepler yalnız elle açılıyor.
- **Tedarikçi fiyat listesi ve son alış fiyatı önerisi**, çerçeve (bağlantı) sipariş takibi. Yok; fiyat her
  siparişte elle (karar gereği), ama geçmiş fiyatı gösteren yardımcı da yok.
- **Tedarikçi avansı ve avansın faturaya mahsubu.** Çelikte peşin ödemeli alım yaygın. Alış'ta yok.
- **Kalite girişi.** Girdi kalite kontrol kaydı, sertifika (mill test) eşleştirme, karantina. Kabul doğrudan serbest
  stoğa giriyor (karar gereği); kalite kaydı bu modülde yok.
- **Fiyat farkı / iade faturası süreçleri**, tevkifatlı ve ihraç kayıtlı alış belgeleri.
- **Raporlar.** Açık siparişler, geciken terminler, alış hacmi, fiyat seyri, faturalanmamış kabuller (dönem sonu
  tahakkuk listesi). Hiçbiri yok.

### 5.3 Ekran ve kullanım kolaylığı

- Listelerde yalnız durum ve tedarikçi süzgeci var; serbest arama, tarih aralığı, sıralama, toplam sayı, dışa aktarım
  (Excel) ve kayıtlı görünüm yok. Talep listesi çok yalın (`PurchaseRequestListPage.tsx`, 74 satır).
- Yazdırma/PDF yok (sipariş, kabul fişi, iade).
- Sipariş detayı çok sayıda paneli tek sayfada topluyor; ayrı "Sipariş 360", "Beklenen Teslimatlar", "Faturaya Hazır",
  "Üçlü Kontrol" görünümleri (P-05, P-06, P-09) yok.
- Hata, boş, yükleniyor, yetkisiz, çakışma, çevrimdışı durumları ortak bileşenle gösteriliyor (`shell/StateBlock`);
  sonuçsuz kalan komutta aynı istek güvenle yinelenebiliyor (ör. `pendingCreateStore.ts`). Bu taraf düzenli.
- Ön yüzde 64 test dosyası var; tarayıcıda uçtan uca (E2E) satın alma senaryosu dosyası bulunamadı.

## 6. Diğer modüllerle bağlantılar

### 6.1 Bu modül kimden ne alır

| Kimden | Ne | Mekanizma | Durum | Kanıt |
|---|---|---|---|---|
| Cari | Tedarikçi hesabı, rol, sürüm, görünen ad | Port (okuma bağlantısı) | Gerçek | `Program.cs:434`, `PurchasingSupplierDisplayHost.cs` |
| Ürün | Satın alınabilir ürün, birim, adet başına teorik kg | Port | Gerçek (Ürün açıkken) | `PurchasingProductSelectionHostPorts.cs`, `ProductOwnerPurchaseTheoreticalKilogramsPerUnitPort.cs` |
| Ayarlar | Para profili, kur, KDV/tevkifat, numara serisi, fatura fark toleransı, referans listeler | Port | Gerçek | `PurchasingModuleExtensions.cs`, `Settings/.../SettingsPurchaseInvoiceVariancePolicyV1.cs` |
| Ayarlar | Mal kabul kg toleransı, etiket kodu biçimi | Port | **Yalnız sözleşme** | Uygulayan sınıf yok |
| Organizasyon | Kabul lokasyonu | Port | Gerçek | `PurchaseGoodsReceiptHost.cs` |
| Satış | Satış satırına bağlı talep doğrulaması | Port | Gerçek (arka uç); ekran yok | `PurchasingSalesLinkedSupplyHostPorts.cs` |
| Üretim | Hammadde ihtiyacından talep | — | **Yok** | Üretim'de talep açan kod yok; yalnız kaynak türü tanımlı |
| Stok | Minimum stoktan talep | — | **Yok** | Üretici yok |

### 6.2 Bu modül kime ne verir

| Kime | Ne | Mekanizma | Durum | Kanıt |
|---|---|---|---|---|
| Stok | Mal kabul = stok girişi, etiket, ilk maliyet | Ortak işlem (tek veritabanı işlemi) | Gerçek | `PurchaseGoodsReceiptHost.cs`, `PurchaseGoodsReceiptBatchHost.cs` |
| Stok | Kabul geri alma, tedarikçi iadesi çıkışı | Ortak işlem | Gerçek | `PurchaseReceiptReversalHost.cs`, `PurchaseSupplierReturnHost.cs` |
| Stok | Gelecek arz (açık alış kalanı, termin dilimleri), satışa ayırma | Port | Gerçek | `PurchaseFutureSupplyReadPort.cs`, `PurchasingOwnerInventoryFutureSupplyScheduleSource.cs`, ekran `inventory/PurchaseSupplySection.tsx` |
| Stok | Fatura kuru ve sipariş fiyatı (ortalama maliyet için) | Port + olay | Port gerçek; olay dağıtıcıya bağlı | `InventoryPurchaseInvoiceCostRateHost.cs`, `TrainCompletionHostComposition.cs` |
| Satış | Siparişe bağlı açık alış satırları | Port | Gerçek | `PurchaseSalesLinkedSupplyReadPort.cs` |
| Lojistik | Kabul bekleyen onaylı satırlar | Port | Gerçek | `PurchaseOpenLineReadPort.cs` |
| Finans | Fatura borcu (taksitli açık kalem) | Doğrudan çağrı, kesinleştirme sonrası; yinelenebilir | Gerçek kod; **ekranı kapalı** | `PurchaseInvoiceFinanceAdmissionHost.cs` |
| Muhasebe | Kesinleşen fatura (alış fişi kaynağı), maliyet farkı | Olay kutusu → tüketici | Kod var; **dağıtıcı kapalı** | `Accounting/.../AccountingPurchaseInvoiceFinalizedInboxContractsV1.cs` |
| Çalışma Merkezi | Sipariş canlandı bildirimi | Olay kutusu | Kod var; dağıtıcı kapalı | `PurchasingOrderLineRevivedEventV1.cs` |
| Üretim | Giden fason siparişi ve hizmet kabulü okuma | Port | Gerçek | `PurchaseServiceOrderStoreV1.cs` (`IPurchaseSubcontractOrderReadPortV1`) |
| Belgeler | Siparişe belge ekleme | Ortak panel | Gerçek | `PurchaseOrderDetailPage.tsx` (`DocumentsPanel`) |

### 6.3 Kopuk ya da eksik halkalar

- **Kabul → fatura → borç → ödeme** zinciri kodda var, kullanıcı için fatura adımında kopuk (ekran kapalı). Ödeme
  tarafı Finans değerlendirmesinde.
- **Fatura → Muhasebe fişi ve maliyet farkı** dağıtıcı açılmadan çalışmaz; açıkken de uçtan uca doğrulandığına dair
  bu incelemede kanıt toplanmadı.
- **Tedarikçi iadesi → Finans/Muhasebe** bağı yok: iade stoktan çıkıyor ama borcu azaltan kayıt ya da iade faturası
  üretilmiyor (`Finance`, `OpenItem`, `CreditNote` araması iade kodunda 0). Stok ile cari bakiyesi ayrışır.
- **Kabul geri alma, fatura kesinleştikten sonra**: faturalanmış kabulün geri alınmasının borca etkisi için bu
  incelemede kanıt toplanmadı (§9).
- **Üretim ve Stok → Talep** yok; ihtiyaç havuzu (P-01'in "stok, satış, üretim ihtiyacı" kapsamı) yalnız elle doluyor.
- **Ek masraf → stok maliyeti** bilerek bağlanmamış; ithal/nakliyeli malda maliyet eksik kalır.
- **Çift kayıt riski**: görülmedi. Kabul tek işlemde yazılıyor; Finans devri sabit kabul kimliğiyle ikinci borcu
  engelliyor (`PurchaseInvoiceFinanceAdmissionAdapter`). Borç yalnız Finans'ta tutuluyor.
- **Veri sahipliği**: modüller arası doğrudan tablo yazımı görülmedi; ortak işlemler sunucu katmanında her modülün
  kendi katılımcısıyla yapılıyor.

## 7. Açık kararlar

| Konu | Durum | Kullanımı engeller mi |
|---|---|---|
| Tolerans değerleri (uyarı %1 / onay %2 / blok %3) saha onayı | `OPEN-004` KISMEN | Evet: kabul toleransı ve fazla teslim buna bağlı |
| Kur alma saati, tatil takvimi, elle istisna sınırı | `OPEN-012` KISMEN | Dövizli fatura kontrolünde kısmen |
| Satın alma rolleri ve atamaları (Uzman, Müdür, depo, muhasebe) | İnsan kapısı | **Evet** |
| Fatura ekranlarının yayımlanması | Kodda kapalı; gerekçesi belgelerde bulunamadı | **Evet** |
| Olay dağıtıcının açılması | İnsan kapısı | Muhasebe ve maliyet farkı için evet |
| "Sipariş yalnız onaylı talepten" kuralı zorunlu mu | Karar var (ALS-D2), kod zorlamıyor | Hayır, ama netleşmeli |
| Masraf yapısı ve işletme alımları (sarf, bakım, makine bakım-onarım…) | Ayarlar modülünü bekliyor (ALS-D79) | İşletme alımları için evet |
| İthalat/gümrük ve tedarikçi avansı | "İkisi de sonra" (ALS-D50) | İthalat yapan şirket için evet |
| Ek masrafın ortalama maliyete girmesi | "Masraf yapısı kurulunca ayarla açılabilir" (ALS-D80) | Hayır |
| KDVK 26 "TCMB alış kuru" ile seçilen kur türü; tevkifatta tedarikçi muafiyeti | Mali müşavir teyidi bekliyor | Canlı öncesi |
| Hubvogal (eski alış uygulaması) incelemesi ve açık sipariş geçişi | Erişim sağlanamamış (ALS-D5 notu, ALS-D31) | Canlı öncesi |

## 8. Yapılacaklar (öncelik sırasıyla)

Öncelik: P0 = üç firmanın test ortamında başlaması için şart; P1 = hedefin tamamı; P2 = sonra.
Büyüklük: K = gün, O = 1–2 hafta, B = daha fazla.

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---|---|---|---|---|---|
| 1 | Satın alma rollerini tanımla ve izinleri ata (Uzman, Müdür, depo kabul, muhasebe; ayar izinleri dahil) | Bugün hiçbir ekran açılamıyor | P0 | K | Sinan kararı (insan kapısı) |
| 2 | Gelen fatura ekranlarını yayımla; içe aktarma → eşleştirme → fark onayı → kesinleştirme → Finans devrini üç firmada gerçek dosyayla dene | Zincir kabulde bitiyor | P0 | O | #1; Finans açık kalem hazır |
| 3 | Kurulum kontrol listesi: numara serisi, para profili, kur, KDV, fatura toleransı, hizmet/masraf tipleri, tedarikçi cari, satın alınabilir ürün; eksikte anlaşılır uyarı | Eksik ayarda sipariş 503 ile kapanıyor | P0 | K | Ayarlar ve Ürün ekranları |
| 4 | Yeni sipariş formu: çok satır, onaylı talepten satır çekme, termin ve kur girişi | Gerçek sipariş tek satır değildir | P0 | O | — |
| 5 | Mal kabul kg toleransı ayarını bağla (ayar ekranı + okuma) ve fazla teslim kuralını yaz | Her kg farkı onaya düşüyor; fazla gelen mal kabul edilemiyor | P0 | O | `OPEN-004` değerlerinin teyidi |
| 6 | Satır kapama/yeniden açma ve sipariş kuru ekranları; sipariş toplamlarının (KDV, tevkifat) gösterimi | Arka uç hazır, ekran yok | P1 | K | — |
| 7 | Miktar/fiyat revizyonu (Rev+1) ve fiyatsız siparişe sonradan fiyat | Fiyatsız sipariş faturalanamıyor | P1 | O | — |
| 8 | Olay dağıtıcıyı test ortamında aç; fatura → Muhasebe fişi ve maliyet farkını uçtan uca doğrula | Muhasebe ve maliyet bağı | P1 | K–O | İnsan kapısı; Muhasebe hesap eşlemesi |
| 9 | Tedarikçi iadesinin cari/borç ve iade faturası etkisi | Stok ile cari ayrışıyor | P1 | O | Finans/Muhasebe kararı |
| 10 | Sipariş PDF'i (fiyatlı, taslak filigranlı) ve kabul fişi çıktısı | Tedarikçiye belge elle gidiyor | P1 | K | Belge altyapısı |
| 11 | Üretim ve Stok ihtiyacından talep açma; ihtiyaç havuzu görünümü | İhtiyaç yalnız elle giriliyor | P1 | O | Üretim/Stok tarafı |
| 12 | Liste iyileştirmeleri: arama, tarih, sıralama, Excel dışa aktarım; beklenen teslimat, faturaya hazır, üçlü kontrol görünümleri | Günlük kullanım | P1 | O | — |
| 13 | Tedarikçi teyidi, sevk bildirimi/şoför bilgisi, siparişsiz kabul, mükerrer fatura uyarısı, siparişte vade | Kararı verilmiş küçük işler | P1 | O | — |
| 14 | İşletme alımları: stok dışı gider alımı, masraf türü ve masraf merkezi | Menü sırası 6; sarf/bakım/yedek parça takibi | P1 | B | Masraf yapısı kararı |
| 15 | İthalat dosyası ve ek masrafın stok maliyetine dağıtımı | Çelik ithalatı; doğru maliyet | P1 | B | Kapsam kararı |
| 16 | Açık siparişlerin eski sistemden geçişi | Canlıya geçiş | P1 | O | Hubvogal erişimi |
| 17 | Tedarikçi performansı, teklif karşılaştırma, fiyat geçmişi, minimum stok önerisi, raporlar | Kararla sona bırakılanlar | P2 | B | Zincirin tamamlanması |

## 9. Doğrulanamayanlar

- Testlerin geçip geçmediği: yalnız kaynakların varlığı sayıldı (birim testte 94 dosya, bütünleşik testte adında
  "Purch" geçen 99 dosya, gerçek veritabanı envanterinde 149 satır, ön yüzde 64 test dosyası). Devir belgeleri
  (`docs/handoffs/TREN1_DEVIR/durum/W8_alis_cari.md`) birçok dilim için "koşulmadı" diyor.
- Fatura ekranlarının neden kapalı bırakıldığı; kapatma `9e6107f` (#1201) ile gelmiş, gerekçe notu bulunamadı.
- Fiyatsız onaylanmış siparişe, cari değiştirmeden fiyat girilebiliyor mu (yalnız cari revizyonu içinde fiyat ucu var).
- Faturası kesinleşmiş bir kabul geri alınırsa ya da iade edilirse borç ve maliyetin nasıl düzeldiği.
- Ürün çalışma zamanının ve Ayarlar ön koşullarının üç şirket için test ortamında gerçekten kurulu olup olmadığı.
- Olay dağıtıcı açıldığında Muhasebe alış fişinin doğru hesaplara yazılması (Muhasebe değerlendirmesinin konusu).
- Hizmet/fason siparişinde iptal durumu tanımlı; iptal ucu ve ekranı ayrıca incelenmedi.
- Toplu kabulde tolerans dışı partinin onay/ret ekran akışı kod düzeyinde görüldü, çalışırken denenmedi.
