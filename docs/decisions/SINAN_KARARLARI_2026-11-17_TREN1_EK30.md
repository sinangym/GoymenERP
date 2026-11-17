# Sinan Kararları — 17 Kasım 2026 (irsaliyeden faturaya taşıma ve atıf takibi)

Bu belge eski karar dosyalarını yerinde değiştirmez. **SAT-D425**, EK29
SAT-D424'ün birden çok irsaliyeden fatura oluşturma kapsamını tamamlar.
EK26–EK29'un imzalı head'leri korunur. Araştırma sonuçları kullanıcı kararıyla
aynı şey değildir; aşağıda ayrı ve kaynak sınırıyla gösterilir.

**Kaynak:** 17 Kasım 2026 sohbet cevabının
[#1189/6931407966 ajan aktarımı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6931407966).
Bu yorum kullanıcının GitHub'a bizzat yazdığı cevap değildir. Önceki soru
[#1189/6931354457](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6931354457)'dedir.
Soru penceresi aşağıda ayrıca belirtilir; GitHub'daki kısa soruyla aynı metin
olduğu iddia edilmez. Resmî kaynak ve ERP araştırmasının kontrol tarihi 17 Kasım 2026'dır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 / SAT-D425 | **Oturumdaki soru penceresi:** “SAT-D424: Birden çok irsaliye tek faturada toplandığında kg dağılımı irsaliyelerden aynen taşınsın mı, yoksa fatura düzeyinde yeniden mi hesaplansın? Bu karar gelmeden ilgili hesaplama koduna başlamayacağım.” | “İrsaliyeden taşınmalı resmide de böyle olur, parçalanabilir belki ama toplam tutar satır bazında eşit olmalı yada kayıp kaçak olmamalı erp veya muhasebe sistemlerinde nasıl olduğunu araştır” | Numaralı seçenek veya önerilen işaretli seçim yoktu. Kesin seçim irsaliyeden taşımadır. Kullanıcı satır bazında toplamın korunmasını ve ERP/muhasebe uygulamalarının araştırılmasını istedi. “Parçalanabilir belki” ifadesi ayrıntılı bir parçalama algoritması veya otomatik birleştirme kararı olarak kaydedilmez. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D425 | Birden çok irsaliyeden tek fatura; **SAT-D424'ü tamamlar** | Kg dağılımı kaynak irsaliyelerden faturaya **taşınır**; fatura düzeyinde yeni teorik kg oranıyla yeniden dağıtılmaz. Satır bazında kaynak pay ile taşınan toplam korunur; kayıp veya çift sayım oluşmaz. Kullanıcının “toplam tutar” şartı da kaynak satırla izlenir; cevapta bulunmayan fiyat, vergi veya yuvarlama politikası bundan türetilmez. Araştırma istenmiştir; araştırmanın bulguları aşağıdadır. | İrsaliye–fatura kaynak satır bağları, kg dağılımı, aktarım toplamlarının doğrulanması |

**Uygulama okuması (geri alınabilir):** kaynak irsaliye/satır kimliklerini ve
aktarılmış payları ayrı izlemek, bir kaynak payı ikinci kez faturalamayı önlemek
ve toplamları kaynak bazında karşılaştırmak kararın izlenebilirliğini sağlar.
Bu teknik okuma henüz yeni bir parçalı faturalama, satır birleştirme veya kuruş
artığı algoritmasını etkinleştirmez. Miktar ile para aynı tür değildir; irsaliyede
bulunmayan parasal tutar uydurulmaz. Mevcut fiyat/vergi kararları ayrıca geçerlidir.

## ERP araştırması ve sınırı

- Microsoft'un [Business Central birleşik fatura belgesi](https://learn.microsoft.com/en-us/dynamics365/business-central/sales-how-to-combine-shipments-on-a-single-invoice),
  kaydedilmiş sevkiyatların satırlarını seçerek faturaya almayı ve faturalanan
  miktarın kaynak siparişte izlenmesini anlatır. Bu, kullanıcının kaynaktan taşıma
  seçimiyle uyumlu bir ERP örneğidir; bütün ERP'ler veya Türk mevzuatı hakkında
  genelleme değildir.
- Microsoft'un [kısmi sevkiyat belgesi](https://learn.microsoft.com/en-gb/dynamics365/business-central/sales-how-send-partial-shipments),
  sevk ve faturalama miktarlarını satır düzeyinde ayrı izleyerek kısmi işlem
  yapılabildiğini açıklar. Bu özellik GöymenERP için kendiliğinden etkinleştirilmez.
- [OASIS UBL 2.1 resmî Invoice modeli](https://docs.oasis-open.org/ubl/os-UBL-2.1/mod/summary/reports/UBL-Invoice-2.1.html),
  fatura satırında irsaliye satırı referansını (`DespatchLineReference`), miktarı
  (`InvoicedQuantity`) ve parasal satır tutarını (`LineExtensionAmount`) ayrı
  gösterir. Bu yapı kaynak bağını taşımaya elverişlidir. UBL'nin bu genel modeli,
  güncel UBL-TR profilinin bütün kısıtlarının veya her durumda birebir parasal
  eşitliğin hukuken zorunlu olduğunun kanıtı olarak sunulmaz.

**Henüz seçilmeyen ayrıntılar:** bir kaynak irsaliye satırının birden çok faturaya
parçalanmasının kesin kapsamı, farklı kaynak satırların tek görünür fatura
satırında birleştirilme şartları ve varsa bu işlemlerin parasal artık yöntemi.
Mevcut kararlarda cevap yoksa ilgili ayrıntının kodundan önce NEEDS-SINAN sorulur;
araştırma kullanıcının yerine bu politikaları seçmez. Çoklu irsaliyeden taşımaya
ilişkin asıl soru ise yukarıdaki cevapla yanıtlanmıştır.

## Eski belgeleri değiştirmeden atıf takibi

### EK29:30–37 — VUK 215/217

GİB'in [tam olarak `/madde/7115` adresi](https://gib.gov.tr/mevzuat/kanun/434/madde/7115),
canlı tarayıcıda **215. madde** başlığı ve defter/kayıt dili ile para birimi
metniyle açıldı. [GİB Van Defterdarlığı resmî 213 sayılı Kanun sayfasında](https://van.gib.gov.tr/mevzuat/kanun/434)
**217. madde**, yanlış kayıtların düzeltilmesini ve eski kaydı okunamaz hâle
getirmeme sınırını düzenleyen başlığı ve metniyle okundu.

**Erişim ve doğrulama sınırı:** doğrudan HTTP yanıtı ve ilk metin çıkarımı yalnız
sayfa kabuğunu verdi. Doğrulama bununla sınırlı bırakılmadı; sayfa yüklendikten
sonra resmî sitenin tarayıcıda görünen madde metni okundu. Doğrulanan şey adres,
madde numarası ve atfın konusudur. Bu, her elektronik düzeltmenin tek bir yöntemle
yapılmasının kanunen zorunlu olduğuna veya bütün vergi sonuçlarının incelendiğine
dair bir iddia değildir. Açık dönemde ters/düzeltme ve farklı onaylayan hükümleri
EK28'deki SAT-D423/FNS-D112 kullanıcı kararlarından gelir; 215'e atıf değişikliği yapılmaz.

### EK29:18 — soru penceresinin kaynağı

EK29'un soru hücresi **oturumdaki soru penceresinin metnidir**. GitHub'daki kısa
soru ve cevap aktarımıyla sözcüğü sözcüğüne aynı olduğu ileri sürülmez.
Kullanıcı cevabının GitHub'daki kaydı yine
[#1189/6929718990 ajan aktarımıdır](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6929718990);
doğrudan GitHub insan cevabı olarak yeniden adlandırılmaz.

### EK27 irsaliye artığı sorusu

EK27'nin tarihsel NEEDS-SINAN satırı yerinde değiştirilmez. İrsaliyedeki hesaplanan
dağılımın artığı EK29 **SAT-D424** ile yanıtlanmıştır. Bu dosyanın **SAT-D425**
cevabı çoklu irsaliyeden faturaya taşımayı ayrıca netleştirir; yeni artık kuralı koymaz.

## Sınırlar

- Kesinleşmiş gerçek ETTN'li dış belgeyi esas alma hükmü korunur; dış belgenin
  kg dağılımı yeni bir hesapla değiştirilmez. ETTN doluluğu tek başına gerçek kesim
  kanıtı sayılmaz; yeni e-belge gönderimi açılmaz.
- Kaynak payının izlenmesi yeni fiyat, iskonto, vergi, kur, tolerans veya parasal
  yuvarlama seçimi değildir. Backend hesabı UI'de yeniden yapılmaz.
- Eski dosyalar ve #1194–#1198'in imzalı head'leri değişmez. Deployment,
  environment, secret, grant/rol/Critical, production, OPEN kapatma, Finance STOP
  ve `.claude/settings.json` sınırları korunur.
- Belge uygulama/test/SQL/browser/CI kabulü değildir. İki bağımsız exact-head
  DOCS review ve beş hafif kontrol gerekir; yazar kendi imzasını veya merge'ünü vermez.
