# Muhasebe (Accounting modülü) — durum ve yapılacaklar

> Kapsam: `src/backend/Modules/Accounting`, `src/backend/GoymenERP.Api/Endpoints/Accounting*`,
> `src/backend/GoymenERP.Api/AccountingRuntime`, `src/frontend/goymen-erp-web/src/accounting`.
> HEAD `ddc6cba`, 23 Kasım 2026. Salt okunur inceleme; derleme ve test koşulmadı.
> Bu belge karar veya onay değildir; durum tespiti ve öneridir.

## 0. Temel soru: bu ERP'de muhasebenin rolü ne?

**Kısa cevap:** Resmî defter (yevmiye, e-Defter, beyanname, e-Fatura) Mikro'da kalıyor. Göymen ERP bugün
Mikro'nun **yanında duran ikinci bir iç defter** tutacak şekilde yazılmış: fiş taslağı üretir, iki kişinin
onayıyla kendi içinde fişe çevirir, kendi mizanını çıkarır. **Bu fişleri Mikro'ya taşıyan hiçbir şey yoktur**
ve iki sistemi karşılaştıran bir mutabakat ekranı da yoktur.

Ayrıntı:

| Soru | Karar ne diyor | Kodda bugün ne var |
|---|---|---|
| Resmî defter nerede? | Mikro'da (DEC-FIN-001; master §9 "Resmî muhasebe fişi/yasal defter: mevcut resmî sistem, ilk faz"; 22 Kasım kararı YOL-D3: "ilk yıl resmî muhasebe ve e-Defter Mikro'da kalır") | ERP'de e-Defter, beyanname, yasal defter çıktısı yok (aramalar §3.8). Karara uygun. |
| ERP ne üretir? | Taslak fiş ve mutabakat (master §9); "ERP muhasebe fişini ve KDV/tevkifat hazırlık verisini Mikro'ya aktaracak biçimde hazırlanır" (YOL-D3) | Taslak fiş → iki kişilik onay → ERP içi fiş (yevmiye kaydı) → ERP içi mizan. KDV/tevkifat hazırlık raporu yok. |
| Mikro'ya ne gider, nasıl? | Yöntem, dosya biçimi, kimlik bilgisi **açık karar** (OPEN-016); ilk sürümde entegrasyon yok (DEC-INT-001) | **Hiçbir şey gitmiyor.** Dışa aktarım (CSV/XLSX/XML), Mikro fiş biçimi, "Mikro'ya aktarıldı" durumu yok. `src/backend/Modules/Accounting` içinde "Mikro" kelimesi yalnız hesap planı aynasının açıklamalarında geçiyor. Bugün tek yol: ekrandan okuyup Mikro'ya **elle** girmek. |
| Mikro'dan ERP'ye ne gelir? | Hesap planı Mikro'nun aynasıdır, salt okunur (FNS-D7) | Hesap planı ve hesap eşlemesi iki CSV dosyasıyla yüklenir (`ApprovedAccountingSourcePackageV1.cs`). Dosya biçimi ERP'nin kendi biçimidir; kodun kendi ifadesiyle "Mikro'nun yerel dışa aktarım biçimi değildir". Yani Mikro'dan alınan listeyi biri bu biçime **elle çevirmelidir**. Yükleme ekranı yok, yalnız API var. |
| İki sistem nasıl mutabık kalır? | "Günlük/haftalık mutabakat; sessiz çift fiş yok" (DEC-FIN-001) | Mutabakat ekranı, Mikro mizanı içe alma, fark raporu **yok**. Finans tarafında "resmî fatura içe alma" var ama o da Mikro'nun yerel biçimini okumaz (`FinanceOfficialInvoiceImportParserV1.cs` satır 14). |

**Sahibi için sonuç:** Bugünkü hâliyle ERP muhasebesi, Mikro'ya **ek iş** çıkarır, iş azaltmaz: aynı fatura
hem Mikro'da resmî olarak, hem ERP'de taslak/onay olarak işlenir ve ikisi arasında köprü yoktur. Bu yüzden
üç şeyden biri seçilmelidir (karar sizindir, burada seçilmiyor):
(a) ERP muhasebesi pilotta **kapalı** kalır, yalnız dönem kilidi ve maliyet görünümü kullanılır;
(b) ERP fişleri Mikro'nun içe alabileceği bir dosyaya aktarılır (OPEN-016 kapanmalı);
(c) ERP defteri yalnız **iç yönetim/mutabakat** defteri olarak tutulur ve Mikro mizanıyla dönem sonunda karşılaştırılır.
21 Kasım kararı (KLN-D116) fiilen (a)'ya yakındır: fatura → muhasebe otomatik alımı pilotta kapalıdır.

## 1. Kısa sonuç

- **Yapılabilen (kod var, menüde açık):** muhasebe dönemi önerme, başka kişinin onayıyla açma, kapatma,
  yeniden açma; Finans kaynaklı (tahsilat, masraf ödemesi, virman, döviz bozdurma, kredi hareketi) "normal fiş"
  taslağı hazırlama ve başka kişinin onayıyla fişe çevirme; fiş listesi ve fiş detayı; mizan; kontrol kuyrukları.
- **Kod var ama kapalı (ekran sunucuda "staged", yani yayımlanmamış):** hesap planı ekranı, hesap eşleme ekranı,
  satış/alış faturasından gelen kaynak fişler, ters fiş önerisi ve onayı, sevk SMM (satılan malın maliyeti)
  önerileri, alış maliyet farkı, üretim maliyet farkı, fatura satırı SMM düzeltmesi. Menüde 16 Muhasebe girişinin
  12'si bu yüzden görünmez.
- **Hiç yok:** Mikro'ya aktarım, Mikro ile mutabakat, KDV/tevkifat hazırlık raporu, BA/BS, e-Defter, beyanname,
  kur değerleme, amortisman/sabit kıymet, masraf merkezi, yansıtma ve kapanış fişi, konsolidasyon, şirketler arası
  işlem, serbest (elle) mahsup fişi, açılış bakiyesi, fişte yevmiye numarası ve açıklama.
- **En önemli 5 eksik:**
  1. Muhasebe izinleri hiçbir rolde yok (`identity-seed.json` içinde `accounting.` araması 0 sonuç); kimse giremez.
  2. Hesap planı ve fatura hesap eşlemesi (120/600/391, 320/150/153/191) yüklenmeden hiçbir fiş oluşmaz;
     yükleme ekranı yok.
  3. Modüller arası olay taşıyıcı (dispatcher) kapalı; açılmadan Satış, Satın Alma, Stok, Üretim ve Finans'tan
     muhasebeye hiçbir kayıt düşmez (tahsilat hariç).
  4. Fiş ve mizan ekranı hesap **kodu ve adını göstermez**, "Hesap referansı" (teknik kimlik) gösterir.
  5. Mikro ile ilişki (aktarım ve mutabakat) tanımsız ve kodsuz.

## 2. Hedef: bu modülden ne isteniyor

| Hedef | Kaynak |
|---|---|
| Menü: Kontrol Merkezi; Faturalar & Belgeler; Cari Hesaplar; Muhasebe Fişleri; Hesap Planı; Mizan; Dönem & Vergi | Master §7.10, §4 sıra 12 |
| Ekranlar M-01…M-09: kontrol, kaynak belge kontrolü, cari, fiş (taslak/onay/ters), hesap planı ve boyutlar (merkez, proje), mizan/defter/yasal rapor, dönem ve vergi (KDV), mahsup ve mutabakat, maliyet muhasebesi (karar kapısı) | Master §27.10 |
| Muhasebe, Satış/Alış ekranını kopyalamaz; doğruluk, hesap eşlemesi, fiş, dönem ve raporlamayı yönetir | Master §7.10 |
| Para olayının sahibi Finans; muhasebe fişi ayrı türev kayıt; operasyon modülü fişi sessizce değiştiremez | Master §9.2 |
| Resmî defter ve e-belge dış sistemde; ERP taslak, kontrol, dışa aktarım, durum | Master §7.10, §9; DEC-FIN-001/002/003 |
| Deterministik kural fiş taslağı üretir; kullanıcı onayı olmadan resmileşmez; kapalı dönem değişmez; düzeltme ters fişle | DEC-FIN-004 |
| Dönemi kapatan ile kilidi kaldıran ayrı yetki | Master §12; ROL-D12 (yeniden açma yalnız Genel Müdür) |
| Faturadan fiş: cari satırı ödenecek toplam; hesaplar Ayarlar'da ürün türüne göre eşlenir, koda yazılmaz; fiş fatura tarihinin dönemine; dövizde fatura kuru; sistem taslak üretir, muhasebe kullanıcısı onaylar; kapalı dönemde "dönem bekliyor"; iptalde taslak iptal, onaylı fişe ters taslak; kendi fişini onaylayamaz | KLN-D6…D19 (7 Kasım) |
| Fatura olaylarının muhasebeye otomatik alınması pilotta kapalı; açılması ayrı muhasebe kabulü ve ayrı karar | KLN-D116 (21 Kasım) |
| İlk yıl Mikro; ERP fişi ve KDV/tevkifat hazırlık verisini Mikro'ya aktaracak biçimde hazırlanır | YOL-D3 (22 Kasım) |
| KDV iade dosyası (tevkifatlı satış, alış, yüklenilen KDV listeleri), yöntem dönem bazında | YOL-D12 (22 Kasım) |
| Muhasebe Müdürü ayrı rol; muhasebe personeli salt okur; gelen fatura farkını Muhasebe Müdürü onaylar; onay izinleri ortak kalır | ROL-D1, D8, D10; YOL-D37 |
| Stok değerleme ve üretim maliyeti: ortalama maliyet, hurda maliyetten düşülür, yalnız kapalı emir | 11 ve 13 Kasım maliyet kararları (SAT-D301…D318) |
| Tek belge bir kez; stok/cari/muhasebe/finans aynı olay ve dosya referansını gösterir | Master §29 AT-065 |

## 3. Bugün var olan

Seviye: 0 = yok, 1 = kod yazılmış, 1-kısmi = yalnız bir bölümü. "Menü" sütununda **Açık** = sunucu ekranı
yayımlıyor; **Kapalı (staged)** = kod var ama sunucu `ImplementationAvailable=false` ilan ediyor, ekran
kullanıcıya "bulunamadı" görünür (`AccountingShellCapabilityProvider.cs`).

### 3.1 Dönem

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Dönem önerme | Var | Var | Açık (liste içinden) | Var | 1 | `POST /accounting/periods`; `PeriodProposePage.tsx` |
| Açılış onayı (hazırlayan onaylayamaz) | Var | Var | Açık (liste düğmesi) | Var | 1 | `AccountingPeriod.cs`; veritabanı kısıtı da var |
| Kapatma | Var | Var | Açık | Var | 1 | Bir sonraki dönem açık olmalı; Finans devir fişi aynı işlemde (`FinancePeriodCarryForwardPortV1.cs`) |
| Yeniden açma (öner + başka kişi onaylar) | Var | Var | Açık | Var | 1 | `AccountingEndpoints.PeriodLifecycle.cs`, `PeriodLifecyclePanel.tsx`; ayrı izin `accounting.periods.reopen` |
| Öneriyi geri çekme / reddetme | Yok | Yok | — | — | 0 | README: "Not modelled, by decision"; yanlış önerilen dönem tarihleriyle kalır |
| Dönem kapalıyken diğer modülleri kilitleme | Kısmi | — | — | Var | 1-kısmi | Dönem durumunu yalnız Finans ve Üretim (emir yeniden açma) soruyor (`IAccountingPeriodStatusPortV1` kullananlar). Satış, Satın Alma, Stok hareketleri dönem kilidine bakmıyor |

### 3.2 Hesap planı ve hesap eşlemesi

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Hesap planı + Finans kaynak eşlemesi paketi yükleme (iki CSV) | Var | **Yok** | — | Var | 1-kısmi | `POST /accounting/approved-source-catalog/packages`; ön yüzde `approved-source-catalog` araması 0 sonuç. Başlıklar: `accountId,accountCode,accountName` ve 11 sütunlu eşleme; dosya başına en çok 10.000 satır |
| Paketi etkinleştirme | Var | **Yok** | — | Var | 1-kısmi | `…/packages/{id}/activation` |
| Hesap planını görme (salt okunur) | Var | Var | **Kapalı (staged)** | Var | 1 | `GET /accounting/account-plan`, `AccountPlanPage.tsx`. Hiyerarşi, hesap sınıfı, alt hesap türetme yok |
| Hesap planını ERP'de düzenleme | Yok | Yok | — | — | 0 | Bilinçli: plan Mikro'nun aynası |
| Fatura hesap eşlemesi sürümleri (satış: alacak, gelir, hesaplanan KDV, tevkifat alacağı; alış: borç, stok, indirilecek KDV, tevkifat borcu, gider) | Var | **Yok** | — | Var | 1-kısmi | `AccountingPostingAccountMappingV1.cs`; hazırla → başka kişi etkinleştirir. Bu satırları giren ekran yok; yalnız API |
| SMM hesap eşlemesi (ticari mal 621, mamul 620) | Var | Var | **Kapalı (staged)** | Var | 1 | `CogsMappingPage.tsx`; ekran yalnız SMM satırlarını düzenler, diğer satırları aynen korur |
| Boyutlar (masraf merkezi, proje) | Yok | Yok | — | — | 0 | README FIN-S16: "no backend or decision" |

### 3.3 Fiş (yevmiye kaydı)

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Fiş listesi, döneme göre süzme | Var | Var | Açık | Var | 1 | `JournalPages.tsx` |
| Fiş detayı | Var | Var | Açık (satırdan) | Var | 1-kısmi | Satırda yalnız hesap kimliği, borç, alacak var. **Hesap kodu/adı, açıklama, cari, belge no, yevmiye no yok** (`JournalLineViewV1(LineNumber, AccountId, Debit, Credit)`) |
| Normal fiş taslağı (Finans kaynağından) | Var | Var | Açık ama **bağlantısı yok** | Var | 1-kısmi | `NormalJournalDraftPages.tsx`. Menü etiketi boş ve hiçbir ekranda bağlantı yok; yalnız adres yazılarak açılır (`/muhasebe/normal-fisler/yeni`) |
| Normal fiş onayı (başka kişi, açık dönem seçer) | Var | Var | Açık ama **liste yok** | Var | 1-kısmi | Onay bekleyen taslakları listeleyen uç ve ekran yok; onaylayan kişinin taslak adresini bilmesi gerekir |
| Serbest (kaynaksız) mahsup fişi | Yok | Yok | — | — | 0 | Her taslak sunucudaki bir Finans kaynağına ve onaylı eşlemeye bağlanmak zorunda (`NormalJournalDraftPublicApiV1.cs`). Açılış, düzeltme, dönem sonu fişi elle girilemez |
| Ters fiş önerisi | Var | Var | **Kapalı (staged)** | Var | 1 | `POST /accounting/journal-reversal-proposals`; fiş detayındaki düğme |
| Ters fiş onayı | Var | Var | **Kapalı (staged)** | Var | 1-kısmi | Sağlayıcı notu: öneriyi kimliğiyle okuyan uç yok |

### 3.4 Kaynak belgelerden fiş (satış ve alış faturası)

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Kesinleşen satış faturasından taslak fiş | Var, **ayar kapalı** | — | — | Var | 1 | `AccountingSalesInvoiceFinalizedInboxConsumer.cs`; `Accounting:InvoiceJournal:InboxAdmissionEnabled` varsayılan kapalı |
| Kesinleşen alış faturasından taslak fiş | Var, **ayar kapalı** | — | — | Var | 1 | `AccountingPurchaseInvoiceFinalizedInboxConsumer.cs` |
| Kaynak fiş listesi ve onayı (faturayı kesinleştiren onaylayamaz) | Var | Var | **Kapalı (staged)** | Var | 1 | `AccountingEndpoints.SourceVouchers.cs`, `SourceVouchersPage.tsx` |
| Eşleme eksikse bekletme, dönem kapalıysa bekletme | Var | Var | Kapalı | Var | 1 | "eşleme bekliyor", "dönem bekliyor"; hesap tahmin edilmez |
| Satış faturası iptali → taslak iptal veya ters öneri | Var, ayar kapalı | Var | Kapalı | Var | 1 | `AccountingSalesInvoiceCancelledInboxConsumerV1.cs`, `SourceVoucherReversalPanel.tsx` |
| Alış faturası iptali → muhasebe | Yok | — | — | — | 0 | Satın Alma modülünde `invoice.cancel` / `InvoiceCancelled` araması 0 sonuç |
| Satışta alıcının kestiği tevkifatın ayrı hesabı | Yok | — | — | — | 0 | Hesap açık karar (README B-04b); tevkifat satırı yazılmaz |

### 3.5 Finans kaynaklı fişler

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Tahsilat (elle banka tahsilatı) → normal fiş | Var | Var | 3.3'teki gibi | Var | 1 | Finans'tan doğrudan okunur; taşıyıcıya bağlı değil (`FinanceManualReceiptNormalJournalSourceProviderV1.cs`) |
| Masraf ödemesi, aynı para birimi virman → kaynak kuyruğu | Var, **taşıyıcı kapalı** | Var | 3.3'teki gibi | Var | 1 | `FinanceJournalSourceV1.cs`; `GET /accounting/finance-journal-sources` |
| Döviz bozdurma (belgeli defter değeri, kâr/zarar satırı) | Var, taşıyıcı kapalı | Var | — | Var | 1-kısmi | Hedef TL ise çalışır; hedef döviz ise "TargetBookValueUnavailable" |
| Kredi hareketi fiş önerisi | Var, taşıyıcı kapalı | Var (kaynak seçimi) | — | Var | 1 | `CreditMovementProposalServiceV1.cs`; `GET /accounting/credit-movement-proposals` |
| Ödeme, çek/senet, POS, DBS, kur farkı → fiş | Yok | — | — | — | 0 | Kaynak türleri listesinde yok (`ApprovedNormalJournalSourceBindingV1.cs` satır 9–14: yalnız altı tür) |

### 3.6 Maliyet önerileri

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Sevk SMM önerisi (faturalanan sevkin gerçek stok maliyeti) hazırla / onayla | Var, taşıyıcı kapalı | Var | **Kapalı (staged)** | Var | 1 | `AccountingInventoryCostServiceV1.cs`, `InventoryCostProposalsPage.tsx` (tür 1) |
| Sevk SMM ters kayıt öner / onayla | Var | Var | Kapalı | Var | 1 | `AccountingInventoryCostCancellationV1.cs` |
| Geç gelen alış faturası maliyet farkı hazırla / onayla | Var, taşıyıcı kapalı | Var | Kapalı | Var | 1 | Aynı ekran, tür 2; kaynak Stok'un `inventory.purchase-invoice-cost-difference.proposed.v1` olayı |
| Üretim maliyet farkı hazırla / onayla | Var, taşıyıcı kapalı | Var | Kapalı | Var | 1-kısmi | `ManufacturingCostDifferenceServiceV1.cs`; README "PARTIAL": karma bileşenler ve sonradan gelen farklar eksik |
| Fatura satırı SMM / sınıf düzeltmesi | Var | Var | Kapalı | Var | 1 | `InvoiceLineCogsCorrectionServiceV1.cs`, `InvoiceLineCogsCorrectionPage.tsx` |
| Stok değerleme raporu, dönem sonu stok–muhasebe mutabakatı | Yok | Yok | — | — | 0 | `Revaluation`, değerleme raporu araması Accounting'de 0 sonuç |
| İlk SMM'nin ne zaman tanınacağı | Karar bekliyor | — | — | — | — | README SAT-D422: "initial Sales SMM recognition trigger is still a human decision" |

### 3.7 Kontrol ve rapor

| Yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Kontrol kuyrukları: eşlemesi eksik (istisna) ve tutarı bilinmeyen (değerleme bekleyen) | Var | Var | Açık | Var | 1 | `GET /accounting/posting-queues`, `PostingQueuesPage.tsx`; salt okunur, kuyruktan işlem başlatılamaz |
| Mizan (tarih aralığı, para birimi başına ayrı toplam) | Var | Var | Açık ama **menüde yok** | Var | 1-kısmi | `TrialBalanceV1.cs`, `TrialBalancePage.tsx`. `navigation.ts` satır 333'te etiket boş; hiçbir ekranda bağlantı yok. Sütun "Hesap referansı"; kod/ad, açılış bakiyesi, ana hesap toplamı, döviz→TL çevrimi yok |
| Cari Hesaplar (M-03) | Finans'ta | Finans'ta | Finans'a bağlı | — | — | `/muhasebe/cari-hesaplar` Finans'ın cari ekstresidir (`finance.party-ledger.accounting`); Finans raporunda değerlendirilir |
| Muavin / defter-i kebir / yevmiye dökümü, yasal rapor | Yok | Yok | — | — | 0 | `GeneralLedger`, `muavin`, `büyük defter` araması 0 sonuç |
| Dışa aktarım, PDF, yazdırma | Yok | Yok | — | — | 0 | `accounting` ön yüzünde `csv`, `pdf`, `yazdır`, `dışa` araması 0 sonuç |

### 3.8 Vergi, dönem sonu ve grup işlemleri — var mı?

Arama alanı: Accounting modülü, uçları ve ekranları. Sonuçların hepsi 0.

| Konu | Aranan | ERP'de | İlk fazda nerede olmalı |
|---|---|---|---|
| e-Defter, yasal defter | `e-defter`, `eDefter`, `ELedger` | Yok | Mikro (YOL-D3) |
| KDV beyannamesi, muhtasar | `beyanname`, `VatReturn`, `TaxReturn` | Yok | Mikro |
| BA/BS | `BaBs`, `BA/BS`, `FormBa` | Yok | Mikro; ERP mutabakat listesi verebilir |
| Amortisman, sabit kıymet | `amortisman`, `Depreciation`, `FixedAsset` | Yok | Mikro (master §20: kapsam boşluğu) |
| Dönem sonu yansıtma, kapanış/açılış fişi | `yansıtma`, `ClosingEntry`, `kapanış fişi` | Yok | Mikro |
| Kur değerleme | `Revaluation`, `kur değerle` | Yok | Mikro; ERP'de Finans açık kalemleri için hazırlık verisi gerekir |
| Stopaj | `stopaj` | Yok | Mikro |
| KDV / tevkifat hazırlık raporu | rapor, liste, dışa aktarım | **Yok** | **ERP (YOL-D3, YOL-D12)** — hedefte var, kodda yok |
| KDV ve tevkifat fiş satırı | eşleme anahtarları | Var (kapalı) | ERP — 391/191/360 satırları faturadan gelir (`AccountingSourceDocumentVoucherPlanV1.cs`) |
| Masraf merkezi, proje boyutu | `CostCenter`, `masraf merkezi` | Yok | Karar yok; master M-05 istiyor |
| Konsolidasyon, şirketler arası | `Consolidat`, `Intercompany`, `grup içi` | Yok | ROL-D15 görünümü öngörüyor; kod yok |

KDV oranı ve tevkifat tanımı Ayarlar modülündedir (`settings.tax-master.manage`, menü "Vergi (KDV / tevkifat)");
Muhasebe yalnız fatura üzerindeki hazır tutarları kullanır, yeniden hesaplamaz.

## 4. Kullanılabilirlik engelleri

1. **Rol/yetki:** Modülün 17 izni kayıtlı (`ErpPermissions.cs` satır 1353–1434), **hiçbiri hiçbir rolde yok.**
   Başlangıç dosyasında 5 sistem rolü var ve `accounting.` araması 0 sonuç. Muhasebe Müdürü ve muhasebe personeli
   rolleri karara bağlandı (ROL-D1, D8) ama tanımlanmadı; rol ataması insan kapısıdır.
2. **İki kişi şartı:** Dönem açma, yeniden açma, eşleme etkinleştirme, fiş onayı ve ters fiş onayı hazırlayandan
   farklı kişi ister. Her şirkette en az **iki** muhasebe yetkilisi kullanıcı gerekir.
3. **Kurulum verisi ve sırası:** (a) hesap planı + Finans kaynak eşleme paketi CSV ile yüklenip etkinleştirilmeli,
   (b) fatura hesap eşleme sürümü hazırlanıp etkinleştirilmeli, (c) Ayarlar'da şirketin para profili muhasebe
   işlem anahtarını (`accounting.journal-amount.v1`) içermeli, (d) dönem önerilip onaylanmalı. (a) ve (b)'nin
   ekranı yok. Biri eksikse sistem fiş üretmez, bekletir; hata değil tasarımdır.
4. **Kapalı anahtarlar:**
   - `Work:OutboxDispatcher:Enabled` — modüller arası olay taşıyıcı. `appsettings.json` içinde bölümü yok,
     varsayılan kapalı. Kapalıyken fatura, masraf, virman, bozdurma, kredi, maliyet farkı olayları muhasebeye ulaşmaz.
   - `Accounting:InvoiceJournal:InboxAdmissionEnabled` — fatura → taslak fiş kabulü. Varsayılan kapalı; KLN-D116
     gereği pilotta kapalı kalacak.
5. **Yayımlanmamış ekranlar:** 3. bölümde "Kapalı (staged)" yazan 12 menü girişi ve 2 kontrol. Açılması için
   tarayıcı kabulü ve ayrı "flip" değişikliği gerekiyor (`SHARED_MOUNT_HANDOFF.md`).
6. **Bağlantısı olmayan sayfalar:** Mizan, normal fiş hazırlama, normal fiş onayı, ters fiş onayı menüde ve diğer
   ekranlarda bağlı değil.
7. **İnsan kapıları:** rol ataması, taşıyıcının açılması, fatura alımının açılması (KLN-D116), OPEN-016.

### "Otomatik fiş alımı kapalı" ne demek?

- **Kapalıyken:** Satış veya alış faturası kesinleşince muhasebede hiçbir şey olmaz. "Kaynak fişler" ekranı
  (açılsa bile) boştur; fiş listesi ve mizan yalnız Finans kaynaklı elle hazırlanan fişleri gösterir. Yani mizanda
  **satış geliri, alış, KDV ve SMM görünmez**; ERP mizanı Mikro mizanıyla karşılaştırılamaz.
- **Açılınca** (iki anahtar birden açılmalı): kesinleşen her fatura için sistem bir taslak fiş üretir
  (satış: 120 borç = matrah + KDV − tevkifat; 600/601 alacak; 391 alacak. Alış: 150/153/770 borç, 191 borç,
  320 alacak, 360 alacak). Taslak "Kaynak fişler"e düşer; faturayı kesinleştirenden farklı bir muhasebe yetkilisi
  onaylar ve fiş, fatura tarihinin dönemine yazılır. Eşleme veya açık dönem yoksa taslak bekler.
- **Açılmadan önce biriken faturalar:** Kod "geçmiş olay doldurma (backfill) yapılmaz" diyor (README FIN-S14).
  Kapalı dönemde kesinleşen faturaların sonradan muhasebeye nasıl alınacağı tanımlı değil (§9).

## 5. Eksikler

### 5.1 Sizin isteklerinize göre

| Hedef | Durum |
|---|---|
| Mikro'ya fiş aktarımı (YOL-D3) | Yok. OPEN-016 açık; biçim ve yöntem seçilmedi |
| KDV/tevkifat hazırlık verisi, KDV iade dosyası (YOL-D3, YOL-D12) | Yok |
| Mikro ile günlük/haftalık mutabakat (DEC-FIN-001) | Yok |
| M-05 Hesap planı **ve boyutlar** | Plan salt okunur ve kapalı; boyut yok |
| M-06 Mizan / **defter / yasal rapor** | Yalnız basit mizan; menüde yok |
| M-07 Dönem **ve vergi** | Dönem var; vergi bölümü yok |
| M-08 Mahsup ve mutabakat | Muhasebe'de yok (açık kalem Finans'ta) |
| M-09 Maliyet muhasebesi | Öneri/onay akışları var ama kapalı; rapor yok |
| M-02 Faturalar ve belgeler | Kaynak fiş listesi var, kapalı; belge (PDF) bağlantısı yok |
| Hesap eşlemesinin Ayarlar'da ürün türüne göre yapılması (KLN-D7) | Veri modeli var; ekran yok |
| Alış faturası iptalinde ters taslak (KLN-D16) | Alış tarafı yok |
| İhracat satışında 601 seçimi (KLN-D18) | Satış olayında senaryo alanı yok; `IsExport` boş gelir (README FIN-S13) |
| Ödeme vadesi fişte | `DueDate` boş (README FIN-S13) |
| AI hesap önerisi (DEC-FIN-004) | Yok |
| Tek belge, tek dosya referansı (AT-065) | Fişte kaynak kimliği var; belge dosyası bağlantısı yok |

### 5.2 Bir ERP'de olması gerekene göre

İlk yıl Mikro'da kalacağı için **ERP'de gerekmeyenler:** e-Defter, beyanname, amortisman, yansıtma ve kapanış fişi.
Mikro kalsa bile **ERP'de gerekenler:**

- **Cari alt hesap:** Fiş satırında cari kimliği yok; alacak eşlemesi tek hesaba (120) gider. Mikro'da her cari
  ayrı alt hesaptır (120.01.xxx). Aktarım için cari → Mikro hesap kodu eşlemesi ve satırda cari gerekir.
- **Fiş numarası ve açıklaması:** Yevmiye no, fiş açıklaması, satır açıklaması, belge no yok. Mikro'da karşılığını
  bulmak ve denetçiye göstermek için gerekir (bir şirket bağımsız denetime tabi).
- **TL karşılığı:** Fiş tek para birimindedir ve mizan para birimlerini ayrı toplar. Dövizli Finans fişleri TL
  mizana girmez; yasal defter TL'dir.
- **Açılış bakiyeleri:** Muhasebe için açılış fişi girişi yok; ERP mizanı sıfırdan başlar.
- **Dönem kilidinin operasyona etkisi:** Kapalı döneme fatura, stok hareketi, alış kabulü girilebiliyor mu sorusu
  modül modül farklı; Satış, Satın Alma ve Stok dönem durumunu sormuyor.
- **Tevkifat ve KDV listeleri:** 5/10 demir-çelik tevkifatı satışların çoğunda uygulanıyor
  (`MALI_MUSAVIR_SORULARI_v1.md` A1); tevkifat iade alacağı takibi ve aylık liste ERP'den çıkmalı.
- **BA/BS mutabakat listesi:** Cari bazında aylık fatura toplamı; Mikro'da beyan edilse de karşılaştırma verisi
  ERP'dedir.
- **Kur farkı ve vade farkı faturaları, iade faturaları:** fiş kuralı yok (README: "FX differences are not
  assigned invented rules").
- **Masraf merkezi:** Dilme, boy kesme, boya gibi hatların kârlılığı için gider satırında merkez boyutu.
- **Şirketler arası işlem ve konsolide görünüm:** üç şirket arasında satış, hizmet, borç (soru D1, D2) için
  karşı kayıt eşleştirmesi ve eliminasyon yok.
- **Çek/senet, ödeme, POS, DBS fişleri:** Finans kaynak türleri altı tanedir; ödeme ve çek/senet yok.
- **Stok–muhasebe mutabakatı:** dönem sonunda stok değeri ile 150/152/153 bakiyesinin karşılaştırması.

### 5.3 Ekran ve kullanım kolaylığı

- Hata, boş, yükleniyor, yetkisiz, çakışma durumları ortak bileşenle işlenmiş (`StateBlock` 15 ekran dosyasında
  kullanılıyor); sonucu bilinmeyen komutta aynı anahtarla yeniden gönderme var. Bu kısım iyi durumda.
- Fiş ve mizanda hesap kodu/adı yerine "Hesap referansı" gösteriliyor; ekrandaki not hâlâ "Hesap planı henüz
  tanımlı değil" diyor (`accountingText.ts` satır 65–68). Plan yüklense bile bu iki ekran ad göstermez.
- Onay bekleyen işler için tek liste yok: normal fiş taslakları ve ters fiş önerileri listelenemiyor; İş Kutusu'na
  (Work) düşmüyor (Work modülünde `accounting` araması 0 sonuç).
- Arama, sütun süzme, toplu onay, dışa aktarım, yazdırma, PDF yok. Fiş listesi yalnız döneme göre süzülür.
- Kontrol kuyruğu salt okunur: eksik eşlemeyi kuyruktan düzeltmeye geçiş yok.
- Menü adları iç dile yakın ("Sevk SMM hazırlama", "Kaynak fişler"); 16 giriş düz liste, master'daki 7 başlık
  altında gruplanmamış.
- Ekranlarda kaynak kayıt adı yerine teknik referans ("ref.…") görünüyor; faturanın resmî numarası yalnız kaynak
  fiş ekranında var.

## 6. Diğer modüllerle bağlantılar

### 6.1 Muhasebe kimden ne alır

| Kimden | Olay / veri | Mekanizma | Muhasebede ne doğar | Durum |
|---|---|---|---|---|
| Satış | `sales.invoice.finalized.v1` | Olay kuyruğu (outbox/inbox) | Taslak kaynak fiş (120/600/391) | Bağlı; iki anahtar kapalı |
| Satış | `sales.invoice.finalized.v1` | Olay kuyruğu | Sevk SMM önerisi kaynağı | Bağlı; taşıyıcı kapalı |
| Satış | `sales.invoice.cancelled.v1` | Olay kuyruğu | Taslak iptali veya ters fiş bekliyor | Bağlı; kapalı |
| Satış | Fatura satırı kimliği, sınıfı | Doğrudan okuma (`AccountingInvoiceLineCogsSource.cs`) | Satır SMM düzeltmesi | Bağlı; ekran kapalı |
| Satın Alma | `purchasing.invoice.finalized.v1` | Olay kuyruğu | Taslak kaynak fiş (150/153/770, 191, 320, 360) | Bağlı; iki anahtar kapalı |
| Satın Alma | Fatura iptali, iade | — | — | **Bağlı değil** |
| Stok | `inventory.purchase-invoice-cost-difference.proposed.v1` | Olay kuyruğu | Alış maliyet farkı önerisi | Bağlı; taşıyıcı kapalı |
| Stok | Üretim maliyet farkı olayı (`ManufacturingCostDifferenceProposed`) | Olay kuyruğu | Üretim maliyet farkı önerisi | Bağlı; taşıyıcı kapalı |
| Stok | Sevkin gerçek maliyet damgası, soy ağacı | Doğrudan okuma (`AccountingInventoryCostSourceReaderV1.cs`, `ManufacturingAttributionLineageReaderV1.cs`) | SMM tutarı kanıtı | Bağlı |
| Stok | Sayım farkı, fire, hurda, depo transferi, iade girişi | — | — | **Bağlı değil** (olay ve tüketici yok) |
| Üretim | Tüketim, mamul girişi, işçilik/genel gider yükleme | — | — | **Bağlı değil**; yalnız Stok üzerinden maliyet farkı gelir |
| Finans | Elle banka tahsilatı | Doğrudan okuma (port) | Normal fiş kaynağı | Bağlı; taşıyıcı gerekmez |
| Finans | Masraf ödemesi, virman, bozdurma (2 olay) | Olay kuyruğu | Finans kaynak kuyruğu → normal fiş | Bağlı; taşıyıcı kapalı |
| Finans | Kredi hareketi | Olay kuyruğu | Kredi fiş önerisi | Bağlı; taşıyıcı kapalı |
| Finans | Ödeme, çek/senet, POS, DBS, mahsup, kur farkı | — | — | **Bağlı değil** |
| Ayarlar | Para profili (ondalık basamak) | Doğrudan okuma (`AccountingAmountScaleVerifier.cs`) | Tutar basamak kontrolü | Bağlı |
| Lojistik | Sevk satırı kanıtı | Sunucu tarafı okuma (SMM kaynağı içinde) | SMM'nin fiziksel dayanağı | Bağlı |

Olay kayıtları tek yerde: `src/backend/GoymenERP.Api/Shell/TrainCompletionHostComposition.cs` satır 53–110.

### 6.2 Muhasebe kime ne verir

| Kime | Ne | Mekanizma | Dosya |
|---|---|---|---|
| Finans | "Bu tarihin dönemi açık mı?" | Port | `Shell/AccountingPeriodStatusPort.cs`, `OccurredOnFinanceRecordPeriodRule.cs` |
| Finans | Dönem kapanışında devir fişi, yeniden açmada geri alma | Aynı veritabanı işleminde port | `FinanceRuntime/FinancePeriodCarryForwardPortV1.cs` |
| Finans (kredi) | Dönem durumu | Port | `FinanceRuntime/FinanceCreditAccountingPeriodPortV1.cs` |
| Üretim | Emir yeniden açmada dönem durumu | Port | `ManufacturingRuntime/ManufacturingReopeningAccountingPeriodStatusPortV1.cs` |
| Denetim (Audit) | Her komutun denetim kaydı | Aynı işlem | `MODULE_DEPENDENCY_ALLOWLIST.json` (tek Accounting kenarı) |
| Mikro | — | **Yok** | — |
| Raporlar, Ana Sayfa | — | Yok | — |

### 6.3 Kopuk ya da eksik halkalar, riskler

- **Çift kayıt riski (iş düzeyinde):** Aynı fatura hem Mikro'ya hem ERP'ye işlenir; ERP içinde çift fiş kod
  düzeyinde engelli (kaynak + amaç başına tek asıl fiş, `UX_Journals_Original`), ama Mikro–ERP arasında hiçbir
  eşleştirme yok.
- **Mizan eksik gösterir:** Pilot ayarıyla mizanda yalnız Finans kaynaklı fişler olur; okuyan kişi bunu tam mizan
  sanabilir. Ekranda bu yönde uyarı görmedim.
- **Veri sahipliği:** İhlal görmedim. Muhasebe başka modülün tablosuna yazmıyor; hesapları tahmin etmiyor;
  tutarları yeniden hesaplamıyor (kaynak modülün dondurulmuş tutarı kullanılıyor).
- **Stok ve üretimin büyük kısmı muhasebesiz:** yalnız satılan malın maliyeti ve fark önerileri var; stok giriş
  değeri, yarı mamul, hurda geliri, sayım farkı fişe dönmüyor. Mikro'da kalacaksa sorun değil, ama o zaman
  ERP'deki SMM fişleri de Mikro'daki SMM ile ayrı hesaplanmış olur.
- **Dönem kilidi yarım:** Finans kayıt tarihinin dönemini soruyor; Satış ve Satın Alma faturası sormuyor (muhasebe
  taslağı "dönem bekliyor"a düşer). Operasyonel dönem yönetimi OPEN-018 ile açık.
- **Belgedeki eskimiş ifadeler:** modül README'sinin FIN-S13 bölümü "tüketici taşıyıcıya kayıtlı değil" ve "alış
  tarafı yok" diyor; kodda ikisi de kayıtlı (`TrainCompletionHostComposition.cs` satır 101–105).
  `SHARED_MOUNT_HANDOFF.md` "Hesap planı yok; fiş oluşturma yok" diyor; ikisi de artık kodda var.

## 7. Açık kararlar

| Karar | Konu | Kullanımı engelliyor mu? |
|---|---|---|
| OPEN-016 | Mikro'ya aktarımın yöntemi, dosya biçimi, kimlik bilgisi, hata/ters işlem sözleşmesi | **Evet** — muhasebenin rolünü belirliyor |
| KLN-D116 devamı | Fatura alımı ne zaman, hangi kabulle açılacak; kapalıyken biriken faturalar ne olacak | Muhasebe ekranlarının dolu olması için evet |
| Taşıyıcı ayarları | `Work:OutboxDispatcher` açılınca beş sayı (parti boyu, deneme sayısı, bekleme süreleri) seçilmeli; kod varsayılan vermiyor | Evet (tüm modüller için) |
| Rol atamaları | Muhasebe Müdürü, muhasebe personeli, Genel Müdür (dönem yeniden açma) izinleri | **Evet** |
| OPEN-008 | Stok değerleme ve üretim maliyeti yöntemi (kısmen kapalı: ortalama maliyet seçildi) | SMM fişleri için kısmen |
| OPEN-018 | Yıllık dönem, devir, saklama süresi | Dönem kilidinin kapsamı için |
| OPEN-009 | Finans onay eşikleri ve rol matrisi | Finans kaynaklı fişler için dolaylı |
| Tevkifat alacağı hesabı | Satışta alıcının kestiği tevkifat hangi hesapta izlenecek (README B-04b) | Tevkifatlı satış fişinin tamlığı için |
| İlk SMM tanıma anı | Satışta SMM hangi olayla ilk kez yazılır (README SAT-D422) | SMM akışı için evet |
| Dönem önerisini geri çekme/reddetme | README "NEEDS-BUSINESS" | Hayır (yanlış öneri kalır) |
| Eşleme sürümünün geçerlilik tarihi | Geçmiş tarihli sürüm eski faturaları yeniden eşler; ileri tarihli sürüm arada her şeyi bekletir (README B-04a) | Hayır, ama ilk kurulumda dikkat ister |
| Ters fişin dönemi ve kur farkı | İptal edilen faturanın ters fişi hangi döneme (README B-04b) | Fatura iptali akışı için |
| Mali müşavir soruları | `MALI_MUSAVIR_SORULARI_v1.md` A1–A4, D1–D2, E1–E2 cevapsız | Vergi ayarları için |
| 120/320 eşlemesinin teyidi | FNS-D7: mali müşavir teyidine kadar anlam eşlemesi | Hesap planı yüklemesi için |

## 8. Yapılacaklar (öncelik sırasıyla)

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---|---|---|---|---|---|
| 1 | Muhasebenin pilot rolünü karara bağlamak: kapalı mı, iç defter mi, Mikro'ya aktarım mı (§0'daki üç seçenek) | Aşağıdaki işlerin yarısı bu karara bağlı | P0 | K | Sinan + mali müşavir |
| 2 | Muhasebe rollerini tanımlayıp izinleri atamak (Muhasebe Müdürü, personel, Genel Müdür); şirket başına en az iki yetkili | İzinler hiçbir rolde yok; kimse giremez | P0 | K | Rol–yetki taslağı (YOL-D2), insan kapısı |
| 3 | Üç şirketin dönemlerini açmak (öner + onayla) | Finans kayıt tarihinin dönem durumunu soruyor; fiş onayı açık dönem ister | P0 | K | 2 |
| 4 | Mikro hesap planını ERP CSV biçimine çevirme yönergesi veya dönüştürücü; yükleme ve etkinleştirme ekranı | Plan olmadan hiçbir fiş oluşmaz; bugün yalnız API | P0 (rol "iç defter" veya "aktarım" ise) | O | 1, 2; mali müşavir teyidi (FNS-D7) |
| 5 | Fiş ve mizanda hesap kodu ve adını göstermek | Muhasebeci teknik kimlikle çalışamaz | P0 (4 ile birlikte) | K | 4 |
| 6 | Mizanı menüye koymak; normal fiş hazırlama bağlantısı; onay bekleyen taslak ve ters fiş listesi (tercihen İş Kutusu'nda) | Sayfalar var ama ulaşılamıyor; onaylayan işini bulamıyor | P0 (Finans fişleri kullanılacaksa) | O | 2 |
| 7 | Modüller arası taşıyıcıyı test ortamında açmak ve ayarlarını seçmek | Masraf, virman, kredi, maliyet farkı muhasebeye ancak böyle ulaşır | P0 (bütün modüller için ortak) | K | İnsan kapısı; beş ayar değeri |
| 8 | Hesap planı, hesap eşleme, kaynak fiş, ters fiş, maliyet ekranlarının tarayıcı kabulü ve yayımlanması | 12 menü girişi kapalı | P1 | O | 4, 7 |
| 9 | Fatura hesap eşlemesi (120/600/391, 320/150/153/191/360/770) için Ayarlar ekranı | KLN-D7; bugün yalnız API | P1 | O | 4 |
| 10 | Fatura → muhasebe alımını açma kabulü ve kapalı dönemde biriken faturaların alınma yöntemi | KLN-D116; mizanın tam olması | P1 | O | 8, 9; ayrı Sinan kararı |
| 11 | Mikro aktarımı: fiş dışa aktarım dosyası, cari → Mikro hesap kodu eşlemesi, fişte cari/açıklama/numara, "aktarıldı" durumu | YOL-D3; çift girişi kaldırır | P1 | B | OPEN-016 kapanmalı |
| 12 | Mikro mutabakatı: Mikro mizanı içe alma ve fark raporu | DEC-FIN-001 | P1 | O | 11 |
| 13 | KDV / tevkifat hazırlık listeleri ve KDV iade dosyası | YOL-D3, YOL-D12 | P1 | O | 10; mali müşavir A1–A3 |
| 14 | Alış faturası iptali ve iadelerin muhasebeye bağlanması; satış tevkifat hesabı; ihracat senaryo alanı | KLN-D16, D12, D18 eksikleri | P1 | O | İlgili kararlar |
| 15 | Ödeme, çek/senet, POS, DBS, kur farkı için Finans fiş kaynakları | Finans fişlerinin tamamı | P1 | B | OPEN-009; hesap eşlemeleri |
| 16 | Serbest mahsup fişi ve açılış bakiyesi girişi | Açılış ve düzeltme kayıtları | P1 | O | 1 |
| 17 | Dönem kilidinin Satış, Satın Alma ve Stok'a uygulanması | Kapalı döneme belge girişi | P1 | O | OPEN-018 |
| 18 | TL karşılığı, masraf merkezi boyutu, stok–muhasebe mutabakat raporu | Yönetim raporu ve denetim | P2 | B | Karar |
| 19 | Şirketler arası işlem eşleştirmesi ve konsolide görünüm | ROL-D15; mali müşavir D1–D2 | P2 | B | Karar |
| 20 | README ve `SHARED_MOUNT_HANDOFF.md` içindeki eskimiş ifadelerin düzeltilmesi | Yanlış yönlendiriyor | P2 | K | — |

**Üç şirket:** Her şirketin dönemi, hesap planı paketi, eşleme sürümü, fişi ve mizanı ayrıdır (her tabloda
şirket kimliği; ekranlar "tek şirket" bağlamı ister). Bu yüzden 2, 3, 4 ve 9 numaralı işler **üç kez** yapılır.
Şirketler arası tek adımda kayıt veya ortak hesap planı yoktur.

## 9. Doğrulanamayanlar

- Testlerin ve CI'ın yeşil olduğu: test kaynağı var (birim: `tests/GoymenERP.Accounting.UnitTests`, 63 dosya,
  462 test işareti; gerçek SQL: `tools/relational_inventory/accounting.txt` 151 satır; ön yüz: 24 test dosyası;
  tarayıcı: `tests/e2e/accounting-acc01`), ama koşmadım. `W8_FIN13_KAYNAK_KAPANISI.md` de "runtime kabulü NOT_RUN" diyor.
- Tarayıcı kabul paketi (`acc01`) yalnız dönem ve okuma ekranlarını kapsıyor görünüyor (dosya adı
  `acc01-periods-and-reads.spec.ts`); diğer ekranların tarayıcıda yürünüp yürünmediğini doğrulamadım.
- Taşıyıcı ve fatura alımı açıkken uçtan uca (fatura kesinleşir → taslak düşer → onay → mizan) gerçek çalışmayı
  görmedim; yalnız kod yolunu okudum.
- Fatura alımı kapalıyken üretilen fatura olaylarının, anahtar sonradan açıldığında kuyruktan işlenip
  işlenmeyeceği (olaylar outbox'ta birikir mi, zaman aşımına uğrar mı) Work modülünün davranışına bağlı; incelemedim.
- Satış müşteri iade faturasının ve satın alma iadesinin muhasebe tarafında herhangi bir fişe dönüp dönmediğini
  ayrıntılı izlemedim; Accounting'de bu türler için tüketici görmedim.
- Finans'ın `/muhasebe/cari-hesaplar` ekranının yayımlanmış olup olmadığı Finans raporunun konusudur.
- Mikro'nun hangi içe aktarım biçimlerini kabul ettiği (Excel, XML, veritabanı) depoda yazılı değil.
- `MODULE_DEPENDENCY_ALLOWLIST.json` Accounting için tek kenar (Denetim) listeliyor; oysa Accounting altyapısı
  Finans sözleşme projesine ve Ayarlar'a da başvuruyor (`*.csproj`). Listenin kapsam kuralını incelemedim.
