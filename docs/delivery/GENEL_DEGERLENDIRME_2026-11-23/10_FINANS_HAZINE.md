# Finans & Hazine — durum ve yapılacaklar

> Kapsam: Finans modülü (`src/backend/Modules/Finance`, 246 kaynak dosyası, 43 veritabanı sürüm adımı, 135 HTTP işlemi),
> ilgili uç noktalar, çalışma zamanı bağlantıları ve `src/frontend/goymen-erp-web/src/finance` ekranları.
> HEAD `ddc6cba`, 23 Kasım 2026. Yalnız okuma ve arama ile hazırlandı; derleme ve test koşulmadı.
> Bu belge karar veya onay değildir; hiçbir `OPEN-*` kimliğini kapatmaz.

## 1. Kısa sonuç

Finans modülünün çekirdeği yazılmış: fatura kesinleşince borç/alacak kalemi (açık kalem) otomatik doğuyor, banka
tahsilatı ve tedarikçi ödemesi kaydedilip faturalara dağıtılabiliyor, cari ekstre ve bakiye okunuyor, müşteri çek/senet
portföyü, kredi limiti, teminat, açılış bakiyesi, virman, döviz bozdurma, masraf ödemesi ve kredi kayıtları var.
Satış'ın risk kontrolü Finans'ın gerçek verisini okuyor.

Buna karşın bugün **hiçbir kullanıcı Finans'ta tek bir işlem yapamaz**, çünkü 58 Finans izninin hiçbiri hiçbir rolde
yok ve yeni rol tanımlayacak bir uç nokta da yok. İzin sorunu çözülse bile üç şirketin birlikte başlamasını engelleyen
başka eksikler var:

1. **GSP ve GIN için tahsilat, ödeme, virman ve kredi kapalı.** Para ayarı (para profili) yalnız GDC ve yalnız TL için
   onaylı; GSP/GIN'de yalnız masraf ödemesi açık. USD/EUR hiçbir şirkette hazır değil.
2. **Mevcut cari bakiyeler kullanılabilir biçimde yüklenemiyor.** Cari açılış bakiyesi tek tek elle giriliyor, ekstrede
   görünüyor ama açık kalem doğurmuyor; yani gelen tahsilat ona dağıtılamıyor ve müşteri riskine girmiyor.
3. **Kasa hesabı açma ekranı yok**; altı menü girişi (Krediler, Resmî satış faturaları ve onayları, Cari bakiye devri,
   döviz defter değeri hazırlama/onay) sunucuda kapalı işaretli.
4. **Banka ekstresi içe aktarma ve banka mutabakatı hiç yok**; nakit planı, bütçe, ödeme önerisi/talimatı, şirketler
   arası işlem, verilen çek/senet, ciro, DBS, şirket kredi kartı, kur farkı, yaşlandırma ve finans raporları da yok.
5. Finans → Muhasebe bağlantısı olay kuyruğuna bağlı; kuyruğu işleten dağıtıcı varsayılan olarak kapalı (insan kapısı).

Master raporun 13 ekran ailesinden 3'ü hiç yok, 2'si yalnız ilk adım düzeyinde, kalanlar kısmi.

## 2. Hedef: bu modülden ne isteniyor

**Master rapor §7.11 — 13 ana ekran ailesi** (ayrıntı §27.11, F-01…F-17): Genel Bakış; Tahsilat/Ödeme; Açık Kalemler ve
Finansal Dağıtım; Tahsilat Takibi ve Cari Risk; Banka/Kasa; Ödeme Yönetimi; Ödeme Araçları; 13 Haftalık Nakit Planı;
Krediler ve Limitler; Teminatlar ve Yükümlülükler; Nakit Bütçesi; Finans Kontrol Merkezi; Finans Raporları.

**Temel kurallar (§7.11, §10.2, §28.14–28.16):**

- Para olayı (banka, kasa, çek/senet, POS, kart, DBS, avans, mahsup) bir "finansal kaynak"tır; faturaya uygulanması ayrı
  bir dağıtımdır ve ikinci para hareketi yaratmaz. `Dağıtılmamış = Kaynak − Aktif Dağıtımlar`.
- POS'ta brüt tutar müşteri borcunu kapatır; banka neti ve komisyon ayrıdır. Tedarikçi POS'u yoktur.
- DBS teminat bilgisidir; gerçek banka tahsilatı gelmeden cari kapanmaz.
- Banka mutabakatı 1:1, 1:n, n:1, n:n eşleştirir. Vade, ödeme tarihi, valör ve mutabakat tarihi ayrıdır.
- Net risk = açık alacak + faturalanmamış sevk + sevk edilmemiş sipariş − DBS − teminat − dağıtılmamış tahsilat.
- Ödeme yönetimi: öneri → nakit/bütçe kontrolü → onay → talimat → banka sonucu → mutabakat → dağıtım.
- Görevler ayrılığı, ikinci onay, dönem kilidi, ters kayıt, grup içi işlem izi zorunludur.
- Zincirler (§8.1, §8.2): Fatura → Cari Açık Kalem → Tahsilat/Ödeme Kaynağı → Dağıtım → Banka Mutabakatı → Kapanış.
- Kabul senaryosu AT-058: ödeme/tahsilat açık kaleme ve banka hareketine çift yönlü bağlanır.

**Bağlayıcı kullanıcı kararları (FNS-D1…D122; `docs/decisions/SINAN_KARARLARI_2026-11-02_FINANS.md` ve sonrakiler):**

| Konu | Karar özeti | Kimlik |
|---|---|---|
| Borç/alacak | Alış borç, satış alacak; tek kayıt Finans açık kalem defterinde, cari kartı yalnız ana veri | FNS-D2, D7, D14 |
| Cari para birimi | Tek cari, para birimi bazında ayrı bakiye; kur farkı ayrı satır | FNS-D6 |
| Döviz | Dövizli işlem olacak; TL/USD/EUR kasaları; bozdurma tek işlem | FNS-D5, D24, D59 |
| Eksi bakiye | Kasa ve banka eksiye düşmez; bakiye iç defterden (açılış + onaylı hareketler) | FNS-D11, D87, D102 |
| Açılış bakiyesi | Elle, yalnız açılış dönemi açıkken; öneren ≠ onaylayan; tek açılış hareketi; düzeltme ters kayıtla | FNS-D12, D23, D84, D85 |
| Çek/senet | Müşteri çeki tedarikçiye verilebilir (ciro), kendi çek/senedimiz de verilebilir | FNS-D8 |
| DBS | Hem müşteriden tahsilat hem tedarikçiye ödeme yönünde | FNS-D3 |
| Şirket kartı | Tam takip (kart, limit, harcama, ekstre, ödeme) | FNS-D9 |
| Krediler | Nakdi, gayrinakdi, çek/senet+faktoring, leasing; banka planı aynen girilir, sistem faiz hesaplamaz | FNS-D4, D88, D93–D99 |
| Kredi limiti/teminat | Şirket bazında, cari kartında görünür; limitsiz müşteride sevk durur, tek seferlik onayla sürer | FNS-D27, D30, D31, D56, D57 |
| Masraf | Cariye bağlı olmayan ödeme "Masraf" kalemiyle; kalemler Ayarlar kataloğundan | FNS-D25, D51 |
| Banka ekstresi | Hareket elle girilir; ekstre (XLSX, MT940, CSV) yalnız kontrol içindir; birebir tutar, kullanıcı onayı | FNS-D13, D52, D113–D122 |
| Resmî satış faturası | Elle giriş + PDF, ikinci kişi onayı; CSV içe aktarma; aynı numara/ETTN ikinci alacak açmaz | FNS-D17–D21, D26, D76 |
| Onay | Hazırlayan ≠ onaylayan (ödeme, masraf, ters kayıt); virman tek adım; tutar eşiğiyle ikinci onay **yok** | FNS-D47, D60, D118; ROL S-11 |
| Sıradaki işler | Banka ekstresi + eşleştirme, yaşlanan alacak, dışa aktarım, işletme rolleri Dalga 0–1'de | YOL-D1, YOL-D2 |

## 3. Bugün var olan

Seviye: 0 = yok, 1 = kod yazılmış, 1-kısmi = akışın bir bölümü ya da yalnız arka uç. "Menü" sütununda "kapalı",
ekranın yazıldığını ama sunucunun onu yayımlamadığını gösterir (`ImplementationAvailable = false`,
`…/Finance.Infrastructure/Shell/FinanceShellCapabilityProvider.cs` satır 241–262).
Test kaynağı: `tests/GoymenERP.Finance.UnitTests` (95 dosya, 824 test niteliği), `tests/GoymenERP.Api.IntegrationTests`
(66 `Finance*` dosyası), `tools/relational_inventory/finance.txt` (255 satır gerçek veritabanı testi), ön yüzde 82 test
dosyası. Aşağıda "var" yazan her satırın test kaynağı bu klasörlerdedir; sonuçlarını koordinatör ekleyecektir.

### 3.1 On üç ekran ailesinin koddaki karşılığı

| # | Ekran ailesi (master §7.11) | Kodda ne var | Seviye |
|---|---|---|---|
| 1 | Finans Genel Bakış | Yok. Finans rotalarında özet sayfası yok (`finance/financeRoutes.tsx`) | 0 |
| 2 | Tahsilat/Ödeme | Banka tahsilatı, POS tahsilatı, tedarikçi banka ödemesi, masraf ödemesi, ters kayıt önerisi/onayı | 1 |
| 3 | Açık Kalemler ve Finansal Dağıtım | Açık kalem listesi/detayı, tahsilat ve ödeme dağıtımı, dağıtımı geri alma | 1-kısmi |
| 4 | Tahsilat Takibi ve Cari Risk | Kredi limiti, teminat, evraklı risk, cari kartında kredi paneli, Satış'a risk portu. Gecikme takibi yok | 1-kısmi |
| 5 | Banka/Kasa | Banka hesabı (ekranlı), kasa hesabı (ekransız), virman, döviz bozdurma. Ekstre ve mutabakat yok | 1-kısmi |
| 6 | Ödeme Yönetimi | Yok (öneri, paket, talimat, banka sonucu). Yalnız tek tek elle ödeme var | 0 |
| 7 | Ödeme Araçları | Müşteri çek/senet portföyü ve POS. Verilen çek/senet, ciro, DBS, şirket kartı yok | 1-kısmi |
| 8 | 13 Haftalık Nakit Planı | Yok (`CashFlow`, `NakitPlan` araması 0 sonuç) | 0 |
| 9 | Krediler ve Limitler | Kredi kaydı + geri ödeme planı, banka/tür limiti. Kredi hareketleri kapalı, menü kapalı | 1-kısmi |
| 10 | Teminatlar ve Yükümlülükler | Cari bazında alınan/verilen teminat kaydı. Banka teminat tahsisi, covenant yok | 1-kısmi |
| 11 | Nakit Bütçesi | Yok (Finans'ta `Budget` araması 0 sonuç) | 0 |
| 12 | Finans Kontrol Merkezi | Ayrı onay sayfaları (ters kayıt, fatura aktarımı). Tek merkez yok | 1-kısmi |
| 13 | Finans Raporları | Yok. Yalnız cari ekstre ve bakiye okuması var; yaşlandırma yok (`Aging` 0 sonuç) | 0 |

### 3.2 Banka, kasa, tahsilat ve ödeme

| Yetenek | Arka uç | Ekran | Menü | Seviye | Not / kanıt |
|---|---|---|---|---|---|
| Banka hesabı (öner, onayla, IBAN değişikliği) | Var | Var | Var | 1 | `FinanceBankAccountService.cs`; `BankAccountsPage.tsx` |
| Kasa hesabı (öner, onayla, pasife al, yeniden aç) | Var | **Yok** | Yok | 1-kısmi | `FinanceCashAccountService.cs`, 12 uç nokta; ön yüzde yalnız liste okuması (`expensePaymentHttp.ts`) |
| Banka tahsilatı (havale/EFT): taslak → kayıt | Var | Var | Var | 1 | `ReceiptDraftService.cs`; `ReceiptPages.tsx` |
| POS tahsilatı ve komisyon | Var | Var | Var | 1 | `PosCollectionV1.cs`. Komisyonu müşteri üstlenirse seçim kaydedilir ama müşteriye borç yazılmaz |
| Nakit (kasadan) tahsilat ve cari ödeme | Yok | Yok | — | 0 | Tahsilat ve ödeme servislerinde kasa hesabı geçmiyor; yalnız banka |
| Tedarikçi banka ödemesi: taslak → onay → kayıt | Var | Var | Var | 1 | `ManualBankPaymentService.cs`; hazırlayan ≠ onaylayan |
| Masraf ödemesi (banka veya kasadan) | Var | Var | Var | 1-kısmi | `ExpensePaymentService.cs`. Tek kalem "Genel masraf" (`SingleExpenseItemCatalog`); Ayarlar kataloğu yok |
| Virman (banka↔banka, banka↔kasa) | Var | Var | Var | 1 | `CashTransferService.cs`; tek kayıt, iki bacak; tek adım |
| Döviz bozdurma (verilen + alınan tutar, kur hesaplanır) | Var | Var | Var | 1 | Kur kuralı Ayarlar'a bağlı (`FinanceRuntime/FinanceCashExchangeSources.cs`) |
| Döviz defter değeri (bozdurmanın muhasebe değeri) | Var | Var | **Kapalı** | 1-kısmi | `CashExchangeBookValueServiceV1.cs` |
| Tahsilat ters kaydı (öner → başka kişi onaylar) | Var | Var | Var | 1 | `ReversalProposalService.cs`; `ReversalApprovalsPage.tsx` |
| Ödeme ters kaydı (öner → başka kişi onaylar) | Var | Var | Var | 1 | `PaymentReversalProposalService.cs` |
| Masraf ödemesi ve virman ters kaydı | Yok | Yok | — | 0 | Bu iki kayıt için ters kayıt uç noktası yok |
| Eksi bakiye engeli | Var | — | — | 1 | `FinanceTreasuryLedger.cs`: açılış + onaylı hareketlerden hesaplanır |
| Belge/makbuz numarası | Var | Var | — | 1 | `FinanceDocumentNumbering.cs` |

### 3.3 Açık kalem, dağıtım, cari ekstre ve bakiye

| Yetenek | Arka uç | Ekran | Menü | Seviye | Not / kanıt |
|---|---|---|---|---|---|
| Satış faturasından alacak kalemi (taksit başına) | Var | — | — | 1 | Fatura kesinleşince Satış, Finans portunu aynı istekte çağırır; hata olursa "yeniden gönder" var (`Shell/SalesFinanceAdmissionHost.cs`) |
| Alış faturasından borç kalemi | Var | — | — | 1 | `PurchasingRuntime/PurchaseInvoiceFinanceAdmissionHost.cs` |
| Fatura iptalinde kalemin ters kaydı | Var | — | — | 1 | `FinanceInvoiceOpenItemCancellationService.cs` |
| Müşteri iadesinde taksit düşümü (kullanıcı seçer) | Var | Satış ekranında | — | 1 | `FinanceCustomerReturnInstallmentCalculator.cs` |
| Açık kalem listesi ve detayı | Var | Var | Var | 1-kısmi | Liste yalnız sayfa imleci alır; cari, yön, vade, gecikme süzgeci yok (`FinanceOpenItemReadEndpoints.cs` satır 27–33) |
| Tahsilatı faturalara dağıtma, kısmi kapama, geri alma | Var | Var | Var | 1 | `FinanceAllocationService.cs`, `FinanceAllocationPolicy.cs` (tam ondalık hesap) |
| Ödemeyi borç kalemlerine dağıtma | Var | Var | Ödeme detayında | 1 | `FinancePaymentAllocationService.cs`; `PaymentAllocationPanel.tsx` |
| Farklı para birimleri arasında dağıtım ve kur farkı | Yok | Yok | — | 0 | `ExchangeDifference`, `KurFark`, `RealizedFx` araması 0 sonuç |
| Avans (siparişe bağlı ön ödeme) | Yok | Yok | — | 0 | Dağıtım yalnız açık kaleme yapılır |
| Mahsup (aynı firmanın borcu ile alacağı) | Yok | Yok | — | 0 | Plan API-07; kod yok |
| Vade | Kalemde alan var | Gösterilir | — | 1-kısmi | `FinanceOpenItemRow.DueOn`; vadeye göre liste, uyarı, yaşlandırma yok |
| Cari ekstre (yürüyen bakiye, para birimi ayrı) | Var | Var | Var (Cari ve Muhasebe menüsünde) | 1 | `PartyLedgerService.cs`; 15 hareket türü |
| Cari bakiye özeti | Var | Var | Ekstre içinde | 1 | `GET /finance/party-balance` |
| Cari bakiye devri (birleşen/halef cari) | Var | Var | **Kapalı** | 1-kısmi | `FinancePartyBalanceTransferServiceV1.cs`. Şirketler arası devir değildir |
| Açılış bakiyesi: kasa, banka, cari | Var | Var | Var | 1-kısmi | `FinanceOpeningBalanceService.cs`. Cari açılışı açık kalem doğurmaz (bkz. §5.1) |
| Dönem devri (muhasebe dönemi kapanınca devir fişi) | Var | Ekstrede görünür | — | 1 | `PartyLedgerService.PeriodCarry.cs`; Muhasebe çağırır |

### 3.4 Çek/senet, risk, kredi, teminat

| Yetenek | Arka uç | Ekran | Menü | Seviye | Not / kanıt |
|---|---|---|---|---|---|
| Müşteri çeki/senedi: alma, bankaya tahsile verme, geri çekme, karşılıksız/protesto, müşteriye iade, tahsil | Var | Var | Var | 1 | `CustomerInstrumentService.cs`; tahsil bir tahsilat kaynağı üretir |
| Ciro (müşteri çekini tedarikçiye verme) | Yok | Yok | — | 0 | Ekran metninde "karar bekleniyor" (`customerInstrumentText.ts` satır 90) |
| Verilen çek/senet (kendi çekimiz) | Yok | Yok | — | 0 | `IssuedInstrument`, `OwnCheque` vb. araması 0 sonuç |
| Tahsil edilmiş çekin bankadan dönmesi | Yok | Yok | — | 0 | Ters kayıt bu kaynağı reddeder (`CustomerInstrumentV1.cs` açıklaması) |
| DBS | Yok | Yok | — | 0 | Yalnız açıklama satırlarında geçiyor |
| Şirket kredi kartı | Yok | Yok | — | 0 | Yalnız kredi türü adı olarak var |
| Müşteri kredi limiti (belirle, kaldır, geçmiş) | Var | Var | Var | 1 | `FinancePartyCreditLimitService.cs`; `CreditLimitPage.tsx` |
| Müşteri teminatı (kaydet, değiştir, çöz) | Var | Var | Var | 1 | `FinancePartyCollateralService.cs`; tür ve oran Ayarlar'dan |
| Evraklı risk (tahsil edilmemiş çek/senet) | Var | Var | Portföy içinde | 1 | `CustomerInstrumentRiskPolicy.cs`; brüt riske eklenmez, ayrı gösterilir |
| Cari kartında kredi paneli | Var | Var | Cari detayında | 1 | `FinancePartyCreditPanelService.cs`; `cari/CariDetailPage.tsx` |
| Satış'a risk verisi (açık alacak, limit, teminat) | Var | — | — | 1 | `FinanceSalesPartyRiskReadPortV1.cs`; Satış taslağı risk kontrolünde kullanılır |
| Kredi kaydı ve banka geri ödeme planı (dört grup) | Var | Var | **Kapalı** | 1-kısmi | `FinanceCreditAccountService.cs`, `FinanceCreditPlanCalculator.cs` |
| Kredi hareketi (kullanım, geri ödeme, faiz) | Var ama **kapalı** | Var | Kapalı | 1-kısmi | `FinanceCreditGroupGateV1.NoneApproved`: her öneri reddedilir (grup alan tanımı onayı bekliyor) |
| Banka ve kredi türü bazında limit | Var | Var | Kapalı | 1-kısmi | `FinanceCreditLimitServiceV1.cs` |
| Tahsilat görevi, ödeme sözü, hukuki takip | Yok | Yok | — | 0 | `Dunning`, `PromiseToPay` araması 0 sonuç |

### 3.5 Resmî fatura kayıtları

| Yetenek | Arka uç | Ekran | Menü | Seviye | Not / kanıt |
|---|---|---|---|---|---|
| Resmî satış faturası: elle giriş + PDF, onay, ret, geri çekme | Var | Var | **Kapalı** | 1-kısmi | `FinanceOfficialSalesInvoiceServiceV1.cs`; `OfficialSalesInvoicesPage.tsx` |
| CSV içe aktarma (v1 taksit; v2/v3 resmî no, ETTN, tarih, TL) + ikinci kişi onayı | Var | Var | Var | 1 | `FinanceOfficialInvoiceImportParserV1.cs`; yükleme ve onay sayfaları |
| Aynı resmî no/ETTN ile ikinci alacağın reddi | Var | — | — | 1 | `FinanceOfficialInvoiceIdentityClaimPolicy.cs` |
| Vergi tutarlılığı (matrah + KDV − tevkifat = ödenecek) | Var | — | — | 1 | `FinanceOfficialInvoiceTaxConsistencyV1.cs` |
| Satış faturasıyla eşleşme (Satış okur) | Var | Satış ekranında | — | 1 | `FinanceSalesOfficialInvoiceReadPortV1.cs`; yalnız onaylı kayıt geçerli |
| Gelen (alış) resmî faturası | Finans'ta yok | — | — | — | Kararla Satın Alma'nın işidir; Finans yalnız borç kalemini açar |

### 3.6 Hiç olmayanlar (arama ile doğrulandı)

Banka ekstresi içe aktarma ve mutabakat (`BankStatement`, `MT940`, `Reconcil`: Finans'ta 0 sonuç; yalnız Ayarlar'da
banka CSV sütun eşlemesi ve Belgeler'de dosya türü tanımı var), ödeme önerisi/talimatı (`PaymentProposal`,
`PaymentInstruction`: 0), nakit akış planı, bütçe, şirketler arası işlem (`Intercompany`, `GrupIci`: 0), covenant (0),
kasa sayımı (Finans'ta 0; yalnız Ayarlar'da sayım farkı politikası kaydı), yaşlandırma, dışa aktarım ve yazdırma.

## 4. Kullanılabilirlik engelleri

1. **İzinler hiçbir rolde yok.** `ErpPermissions.cs` içinde 58 `finance.*` izni kayıtlı. Başlangıç rollerinin beşi de
   (`Seed/identity-seed.json`) organizasyon ve güvenlik rolüdür; hiçbirinde Finans izni yok. Kimlik uç noktalarında rol
   listeleme ve atama var, yeni rol oluşturma yok. Sonuç: her Finans ekranı "yetkiniz yok" verir, menüde Finans görünmez.
   Rol taslağı kararı verilmiş (YOL-D2) ama yalnız Satış için taslak var (`SATIS_ROL_YETKI_ONERISI_v1.md`).
2. **Para profili yalnız GDC/TL.** Onaylı profil listesinde (`Settings.Application/ApprovedMoneyProfileCatalog.cs`)
   tahsilat ve banka ödemesi yalnız GDC'nin TL satırında. GSP ve GIN satırlarında Finans'tan yalnız masraf ödemesi var;
   gerekçe olarak "ayrı karar gerekir" yazılmış (SAT-D18, 26 Ekim). Virman ve kredi planı profili hiçbir şirkette
   onaylı değil; yetkili kişinin Ayarlar'dan sürüm yayımlaması gerekir ve bu yalnız GDC için mümkün
   (`MoneyProfileAuthoringCatalogV1.cs`). USD ve EUR için de aynı yayımlama gerekir.
3. **Altı menü girişi kapalı işaretli:** Krediler, Resmî satış faturaları, Resmî satış faturası onayları, Cari bakiye
   devri, Döviz defter değeri hazırla, Döviz defter değeri onayları. Sunucu bu girişleri kullanıcıya göndermez.
4. **Kasa hesabı ekranı yok.** Kasa açılamadığı için kasadan masraf ödemesi, kasa virmanı ve kasa açılış bakiyesi
   ekrandan baştan sona yapılamaz.
5. **Kredi hareketleri kapalı.** Dört kredi grubunun alan tanımı Sinan onayı bekliyor (FNS-D106); onaylanana kadar
   kullanım, geri ödeme ve faiz kaydı reddedilir.
6. **Olay dağıtıcısı kapalı.** Finans'ın Muhasebe'ye gönderdiği kayıtlar bir kuyruğa yazılır; kuyruğu işleten dağıtıcı
   `Work:OutboxDispatcher:Enabled` ayarı açılmadıkça çalışmaz (`Program.cs` satır 501–507; `appsettings.json` içinde bu
   anahtar yok). Açmak insan kapısıdır.
7. **Kurulum sırası.** Tahsilat/ödeme için önce banka kurumu (Ayarlar), banka hesabı (öner + başka kişi onaylar),
   açılış dönemi (Ayarlar) ve açılış bakiyesi gerekir. Açılış bakiyesi ya da kayıtlı girişi olmayan hesaptan ödeme
   "yetersiz bakiye" ile reddedilir (bakiye yalnız açılış ve onaylı hareketlerden hesaplanır). Her adımda iki ayrı kişi gerektiği için test ortamında şirket başına en az iki Finans kullanıcısı gerekir.
8. **Elle banka ödemesine belge bağlama çalışmaz.** Belge doğrulama bağlantısı "kullanılamaz" yanıtı veren varsayılanla
   kayıtlı (`UnavailableManualBankPaymentDocumentValidationPort`, `FinanceModuleExtensions.cs` satır 109); belge
   eklenmeden ödeme kaydedilebilir, belge eklenirse reddedilir.

**OPEN-009 (tutar/rol/onay matrisi) boşken yazma işlemleri:** Finans kodu bu matrisi hiç okumuyor (Finans modülünde
`ApprovalMatri` araması 0 sonuç). Ayarlar'da matris hazırlama/yayımlama uç noktaları var ama tüketen yok. Yazma işlemleri
matris boş diye reddetmez; yalnız izin ve sabit "hazırlayan ≠ onaylayan" kuralıyla çalışır, tutar eşiği uygulanmaz. Bu,
22 Kasım kararıyla uyumludur (tek adımlı finans kayıtlarına tutar eşiğiyle ikinci onay istenmedi, ROL S-11). Bugünkü
fiilî ret nedeni matris değil, izinlerin hiçbir rolde olmamasıdır.

## 5. Eksikler

### 5.1 İsteklerine göre (hedef var, kod yok ya da kısmi)

- **Mevcut cari bakiyelerin yüklenmesi.** Cari açılış bakiyesi ekstrede ve bakiyede görünür, ancak açık kalem
  doğurmaz (`FinanceOpeningBalanceService.cs` içinde `OpenItem` 0 kez geçer; sözleşme dosyası da "yapılmadı" der).
  Sonuçları: devreden alacağa tahsilat dağıtılamaz, devreden alacak müşteri riskinde sayılmaz, vadesi izlenemez. Tek tek
  elle giriş ve her kayıt için ikinci kişi onayı var; toplu yükleme yok. Bugünkü tek dolaylı yol, açık satış faturalarını
  CSV ile "onaylı fatura taksiti" olarak içe aktarmaktır; bu yalnız alacak yönünde çalışır, tedarikçi borçları için
  karşılığı yoktur. Portföydeki çek/senet, kredi kalanı ve teminat için açılış da yapılmamış.
- **Banka ekstresi ve mutabakat** (§7.11, AT-058, FNS-D13/D52/D113–D122, YOL-D1): kararları verilmiş, kod yok. Ayarlar'da
  yalnız banka başına CSV sütun eşlemesi hazır. Zincirin "Banka Mutabakatı" halkası eksik.
- **Nakit tahsilat/ödeme** (kasa üzerinden cari hareket) yok.
- **Verilen çek/senet ve ciro** (FNS-D8), **DBS** (FNS-D3), **şirket kredi kartı** (FNS-D9) yok.
- **Kur farkı** (FNS-D6 "kur farkı ayrı satır") ve farklı para birimiyle kapama yok. Bozdurmada da kur farkı satırı bu
  dalgada bilinçli olarak üretilmiyor (FNS-D63).
- **Masraf kalemi kataloğu** (FNS-D51): Ayarlar kataloğu yerine tek sabit "Genel masraf" var.
- **Ödeme yönetimi** (§8.2: öneri → onay → talimat → banka sonucu), **13 haftalık nakit planı**, **nakit bütçesi**,
  **finans genel bakış**, **finans raporları**, **tahsilat takibi** (gecikme, söz, hukuki) yok.
- **Şirketler arası** (grup içi) borç/alacak, karşı kayıt, netleştirme yok. Üç şirket birbirine yalnız sıradan cari
  olarak tanımlanabilir; iki taraf ayrı ayrı elle girilir, eşlik denetimi yoktur.
- **Mahsup ve avans** yok.
- **Kasa sayımı ve gün sonu** yok (Ayarlar'da yalnız sayım farkı politikası kaydı var).
- **Onaylı resmî fatura kaydının düzeltilmesi/iptali** kararda ertelenmiş (FNS-D20); kod yok.

### 5.2 Bir ERP'de olması gerekene göre

- **Yaşlandırma ve vadesi geçenler listesi.** Metal ticaretinde vadeli satış ana riskken alacakların 0–30/31–60/61–90+
  dökümü ve vadesi gelen ödemeler listesi yok.
- **Faturanın ödeme durumu geri bildirimi.** Satış ve Satın Alma ekranlarının "tahsil edildi / ödendi / kısmi" göstermesi
  için Finans'ın yayımladığı bir kapanış olayı yok (`open-item.settled` araması 0 sonuç).
- **Cari mutabakat mektubu ve BA/BS hazırlığı**, cari ekstre PDF/Excel çıktısı yok.
- **Tahsilat makbuzu / ödeme dekontu çıktısı** yok (numara üretiliyor, basılı belge yok).
- **Banka hesabı defteri** (hesap bazında hareket listesi ve bakiye ekranı) yok; bakiye yalnız ödeme anında içeride hesaplanır.
- **Çek/senet vade takvimi ve bordrosu** (bankaya toplu tahsile verme) yok; her evrak tek tek işlenir.
- **Kur değerlemesi** (dönem sonu dövizli bakiye değerlemesi) ve **vade farkı/gecikme faizi** yok.
- **Toplu ödeme ve banka ödeme dosyası** yok.
- **Kredi taksit hatırlatması ve borç servisi görünümü** yok (plan saklanıyor, takvimi yok).

### 5.3 Ekran ve kullanım kolaylığı

- Hata, boş, yükleniyor, yetkisiz, çevrimdışı, çakışma durumları ortak bileşenle ele alınmış: 44 ekran dosyasının 35'i
  `StateBlock`/`RemoteViewState` kullanıyor. Belirsiz yazma sonucunda aynı istek anahtarıyla yeniden deneme var.
- Listelerde arama ve süzme zayıf: açık kalem listesi hiçbir süzgeç almıyor; tahsilat, ödeme ve dağıtım listeleri
  sayfa imleciyle ilerliyor.
- Toplu işlem yok (toplu onay, toplu dağıtım, çoklu çek girişi).
- Çıktı, PDF ve dışa aktarım Finans ekranlarında yok (`download=`, `window.print`, "Dışa aktar", "Yazdır" araması:
  yalnız döviz defter değeri sayfasında dosya işlemi).
- Master §5 ortak şablonundaki "Yeni Finans İşlemi" bağlamsal kataloğu yok; her işlem ayrı menü girişi (Finans grubunda
  20 giriş, `shell/navigation.ts` satır 522–544). Menüde kullanıcıya göre anlamlı gruplama (Banka/Kasa, Cari, Araçlar) yok.
- Finans ana sayfası ve onay bekleyenlerin tek listesi yok; ters kayıt, fatura aktarımı, kredi ve açılış onayları ayrı
  sayfalarda.

## 6. Diğer modüllerle bağlantılar

### 6.1 Finans kimden ne alır

| Kaynak | Ne | Mekanizma | Kanıt |
|---|---|---|---|
| Satış | Kesinleşen fatura (taksitler, vade, kur, TL karşılığı) → alacak kalemi | Doğrudan port; Satış önce kendi kaydını yazar, sonra Finans'ı çağırır; aynı anahtarla tekrar güvenli | `Api/Shell/SalesFinanceAdmissionHost.cs` |
| Satış | Fatura iptali, müşteri iadesi → kalem ters kaydı / taksit düşümü | Doğrudan port | `Api/Shell/SalesFinanceCancellationHost.cs`, `FinanceInvoiceAcceptanceHost.cs` |
| Satın Alma | Kesinleşen gelen fatura → borç kalemi | Doğrudan port | `Api/PurchasingRuntime/PurchaseInvoiceFinanceAdmissionHost.cs` |
| Cari | Cari ve şirket hesabı geçerliliği, ad, tedarikçi IBAN durumu, halef/birleşme bağı, kredi veren banka | Okuma portları | `Program.cs` satır 206–208; `FinanceRuntime/Counterparty*.cs` |
| Ayarlar | Para profili, para birimi adayları, kur, açılış dönemi, teminat türü ve oranı, banka kurumu | Okuma portları | `Program.cs` satır 258–259; `FinanceRuntime/FinanceCashExchangeSources.cs` |
| Muhasebe | Kayıt tarihinin açık dönemde olması; dönem kapanışında devir isteği | Port | `FinanceRuntime/TransactionalOccurredOnFinanceRecordPeriodRule.cs`, `FinancePeriodCarryForwardPortV1.cs` |
| Belgeler | Resmî fatura PDF'i, bakiye devri ve döviz değeri kanıt belgesi | Port | `FinanceRuntime/Documents*.cs` |

### 6.2 Finans kime ne verir

| Hedef | Ne | Mekanizma | Kanıt |
|---|---|---|---|
| Satış | Müşteri riski: açık alacak, kredi limiti, ağırlıklı teminat | Doğrudan okuma portu | `FinanceSalesPartyRiskReadPortV1.cs`; `Sales.Application/SalesDraftService.RiskCheck.cs` |
| Satış | Onaylı resmî fatura kaydı (eşleşme için) | Doğrudan okuma portu | `FinanceSalesOfficialInvoiceReadPortV1.cs` |
| Cari | Kredi paneli, cari ekstre | Ekran bileşeni + uç nokta | `cari/CariDetailPage.tsx` |
| Muhasebe | Fiş kaynağı: masraf ödemesi, aynı para virman, döviz bozdurma, döviz defter değeri, kredi hareketi | Olay kuyruğu (outbox) → Muhasebe tüketicisi | `Api/Shell/TrainCompletionHostComposition.cs` satır 86–108 |
| Muhasebe | Dönem devir kayıtları | Port | `PartyLedgerService.PeriodCarry.cs` |

### 6.3 Kopuk ya da eksik halkalar; çift kayıt riski

- **Tahsilat ve tedarikçi ödemesi Muhasebe'ye olay olarak gitmiyor.** Kuyruğa yazılan Finans olayları yalnız masraf,
  virman, bozdurma, defter değeri ve kredi hareketidir. Muhasebe'nin desteklediği kaynak listesinde banka tahsilatı
  var, **tedarikçi banka ödemesi, çek/senet tahsili, POS komisyonu ve açılış bakiyesi yok**
  (`Accounting.Application/ApprovedNormalJournalSourceBindingV1.cs`). Bu kayıtların fişi bugün üretilemez.
- **Muhasebe eşleme kataloğu boş.** Finans kaynağından fiş için onaylı hesap eşlemesi gerekir; varsayılan katalog
  bilinçli olarak boştur (aynı dosya). Ayrıntı Muhasebe raporundadır.
- **Banka mutabakatı halkası yok**; §8.1 ve §8.2 zincirleri "Dağıtım"da bitiyor.
- **Ödeme durumu Satış ve Satın Alma'ya dönmüyor** (§5.2).
- **Satış kesinleşmesi resmî fatura kaydına bağlı, o ekran kapalı.** Satış, faturayı kesinleştirmek için Finans'taki
  onaylı resmî kayıtla eşleşme ister. Elle kayıt ekranı menüde kapalı olduğundan bugün tek erişilebilir yol CSV v2/v3
  içe aktarmadır.
- **Çift kayıt korumaları (var):** aynı fatura ve sürüm ikinci kalem açmaz (istek anahtarı); aynı resmî no/ETTN ikinci
  alacak açmaz; çek tahsili açık kalemi kendiliğinden kapatmaz, dağıtım ister (çift sayım yok); POS'ta banka defterine
  net tutar bir kez girer; virman tek kayıttır; kredi hareketinin banka bacağı bir kez okunur.
- **Kalan çift kayıt riski:** (a) Cari açılış bakiyesi girilip aynı alacak ayrıca CSV ile fatura taksiti olarak
  aktarılırsa ekstrede iki kez görünür; bunu engelleyen denetim yok. (b) Şirketler arası işlemde iki şirketteki kayıtlar
  birbirinden bağımsızdır. (c) Tedarikçi ödemesinde ödeme amacı alanı yok; müşteri olan bir cariye yapılan ödeme risk
  hesabında müşteriye iade sayılır (`FinanceSalesPartyRiskReadPortV1.cs` açıklaması). Aynı firma hem müşteri hem
  tedarikçi ise müşteri riski olduğundan düşük görünebilir.
- **Veri sahipliği:** ihlal görülmedi. Finans başka modülün tablosuna yazmıyor; Satış ve Satın Alma Finans'a port
  üzerinden geliyor. Limit ve teminatın sahibi Finans, cari kartı yalnız gösteriyor.

## 7. Açık kararlar

| Kimlik | Konu | Kullanımı engelliyor mu |
|---|---|---|
| YOL-D2 / ROL kararları | Finans rollerinin (Finans Uzmanı, Finans Müdürü, Muhasebe, Genel Müdür) izin tablosu ve atanması | **Evet** — en büyük engel; taslak onayı ve atama Sinan'da |
| SAT-D18 | GSP ve GIN'de Finans para işlemleri için ayrı karar | **Evet** — iki şirkette tahsilat/ödeme kapalı |
| FNS-D106 | Dört kredi grubunun alan tanımı onayı | Kredi hareketleri için evet |
| OPEN-009 | Tutar/rol/onay matrisi | Hayır; kod matrisi okumuyor, S-11 ile eşik istenmedi. Kimliğin kapatılması ayrı karar |
| OPEN-010 | DBS/teminat oranları, risk motoru aktivasyonu | Teminat oranı Ayarlar'dan giriliyor; DBS kodu yok |
| OPEN-011 | Nakit tamponu ve 13 haftalık plan kaynak önceliği | Nakit planı yazılmadan önce gerekir |
| OPEN-012 | Kur alma saati, tatil takvimi, elle kur sınırı | Dövizli işlemler için; ayrıntı Ayarlar raporunda |
| OPEN-016 | Banka/e-belge/Mikro entegrasyon yöntemi | Ekstre dosyası için dosya yolu seçilmiş; otomatik bağlantı için evet |
| OPEN-018 | Dönemler arası açık kayıt temsili, saklama süreleri | Devir yazılmış (FNS-D42); saklama kısmı açık |
| FNS-D20 | Onaylı resmî fatura kaydının düzeltilmesi/iptali | Yanlış onaylanan kayıt düzeltilemez |
| Çek cirosu | Ciroda müşteri alacağının ne zaman kapanacağı ve tedarikçi tarafı | Ciro için evet |
| Cari açılışının açık kaleme dönüşmesi | Açılış bakiyesi dağıtıma ve riske girsin mi, vadesi nasıl girilsin | **Evet** — karar yok, kod "onaylanmadığı için yapılmadı" diyor |
| Mali müşavir soruları | KDV/tevkifat, kur farkı faturası vb. (`MALI_MUSAVIR_SORULARI_v1.md`) | Kur farkı ve vergi ayarları için |

## 8. Yapılacaklar (öncelik sırasıyla)

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---|---|---|---|---|---|
| 1 | Finans rollerini ve izin tablosunu hazırlayıp onaya sunmak; atama | 58 izin hiçbir rolde yok, kimse işlem yapamıyor | P0 | O | Sinan onayı (insan kapısı); Kimlik'te rol tanımlama yolu |
| 2 | GSP ve GIN için Finans para profillerini açmak; USD/EUR ve virman profillerini yayımlamak | Üç şirket aynı anda başlayacak; bugün yalnız GDC/TL | P0 | K | SAT-D18'in yerine geçecek karar |
| 3 | Cari açılış bakiyesinin açık kalem doğurması (vade ile) ve toplu yükleme (dosyadan); borç yönü dahil | Devreden bakiyeler dağıtıma ve riske girmiyor; tek tek giriş üç şirkette uygulanamaz | P0 | O–B | Karar: açılışın açık kaleme dönüşmesi, vade kaynağı |
| 4 | Portföydeki müşteri çek/senetleri için açılış girişi | Başlangıçta eldeki evrak ve evraklı risk eksik kalır | P0 | K–O | — |
| 5 | Kasa hesabı ekranı (aç, onayla, pasife al) | Arka uç hazır, ekran yok; kasa kullanılamıyor | P0 | K | — |
| 6 | Kapalı altı menü girişini açmak (özellikle resmî satış faturası kaydı/onayı ve Krediler) | Ekranlar yazılı ama yayımlanmıyor; Satış kesinleşmesi resmî kayda bağlı | P0 | K | Tarayıcı kabul kanıtı |
| 7 | Test ortamında olay dağıtıcısını açmak; tedarikçi ödemesi, çek tahsili, POS komisyonu, açılış için Muhasebe kaynağı | Finans → Muhasebe fiş zinciri çalışmıyor | P0 (dağıtıcı) / P1 (yeni kaynaklar) | K / O | İnsan kapısı; Muhasebe hesap eşlemesi |
| 8 | Nakit (kasa) tahsilat ve cari ödeme | Kasadan cari hareketi yok | P1 | O | 5 |
| 9 | Açık kalem listesine süzgeç (cari, yön, vade, gecikme) ve yaşlandırma raporu | Alacak takibi yapılamıyor; YOL-D1 | P1 | O | — |
| 10 | Banka ekstresi içe aktarma (XLSX, MT940, CSV) ve eşleştirme | §8 zincirinin eksik halkası; kararları hazır (FNS-D52, D113–D122) | P1 | B | Cari IBAN listesi (Cari modülü) |
| 11 | Fatura ödeme durumunun Satış ve Satın Alma'ya dönmesi | Sipariş/fatura ekranlarında "tahsil edildi/ödendi" yok | P1 | O | — |
| 12 | Verilen çek/senet, ciro, tahsil sonrası dönen çek | FNS-D8; tedarikçi ödemelerinin önemli bölümü | P1 | B | Ciro kararı |
| 13 | Kur farkı satırı ve farklı para birimiyle kapama | FNS-D6; dövizli satış/alış var | P1 | B | OPEN-012, mali müşavir cevabı |
| 14 | Ödeme amacı alanı (tedarikçi ödemesi / müşteriye iade) | Hem müşteri hem tedarikçi olan caride risk yanlış düşüyor | P1 | K–O | — |
| 15 | Masraf kalemi kataloğu (Ayarlar) ve masraf/virman ters kaydı | FNS-D51; yanlış kayıt düzeltilemiyor | P1 | O | — |
| 16 | Kredi hareketlerini açmak (grup alan tanımları) | FNS-D106 onayı bekliyor | P1 | K | Sinan onayı |
| 17 | Mahsup ve avans | Aynı firmayla borç/alacak netleştirme, ön ödeme | P1 | O–B | — |
| 18 | Şirketler arası işlem (karşı kayıt, eşlik denetimi, netleştirme) | Üç şirket birbirine mal ve para aktarıyor | P1 | B | Karar gerekir |
| 19 | Çıktılar: cari ekstre PDF/Excel, tahsilat makbuzu, ödeme dekontu, liste dışa aktarımı | Müşteriye/tedarikçiye belge verilemiyor | P1 | O | — |
| 20 | Elle banka ödemesine belge bağlama | Bağlantı varsayılan "kullanılamaz" | P1 | K | — |
| 21 | Finans ana sayfası ve onay bekleyenlerin tek listesi; menünün gruplanması | 20 ayrı menü girişi; onaylar dağınık | P1 | O | — |
| 22 | DBS, şirket kredi kartı | FNS-D3, FNS-D9 | P2 | B | OPEN-010 |
| 23 | Ödeme önerisi/talimatı, 13 haftalık nakit planı, nakit bütçesi, finans raporları | Master §7.11 aile 6, 8, 11, 13 | P2 | B | OPEN-011 |
| 24 | Tahsilat takibi (söz, görev, hukuki), kasa sayımı, kur değerlemesi | Master aile 4 ve genel ERP gereği | P2 | B | — |

## 9. Doğrulanamayanlar

- Testlerin ve CI'ın sonucu; yalnız test kaynaklarının varlığı sayıldı.
- Yetkili bir kullanıcıyla ekranların baştan sona çalıştığı; tarayıcıda denenmedi.
- Ayarlar'da elle yayımlanan para profili sürümünün (USD/EUR, virman) Finans tarafından sorunsuz okunduğu; yayımlama
  yolunun var olduğu görüldü, uçtan uca izlenmedi.
- Lojistik sevk kapısının Finans risk verisini nasıl kullandığı (Lojistik ve Satış raporlarının konusu); burada yalnız
  Satış taslağı risk kontrolünün Finans portunu çağırdığı görüldü.
- Cari kartındaki IBAN listesinin (FNS-D121) kodda olup olmadığı; Cari modülünün konusu.
- Sunucudaki menü kayıt özetinin (shell yetki sabiti) güncel sürümle eşleştiği; eşleşmezse uygulama açılmaz. Derleme
  koşulmadığı için bakılmadı.
- Açılış bakiyesi girilmemiş hesapta tahsilatın (giriş yönü) reddedilip reddedilmediği; yalnız çıkış yönündeki bakiye
  denetimi incelendi.
