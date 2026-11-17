# W8 — yanıt bekleyen mevcut kararların dizini

17 Kasım 2026 kaynak kontrolü. Bu dosya yeni iş kapsamı veya karar oluşturmaz.
İlk kayıtta aşağıdaki 12 soru bekliyordu. Güncel durumda 1–12 doğrudan kullanıcı
tarafından yanıtlandı. Bu ilk 12 soru için bekleyen işletme kararı yoktur; sonradan kaynak taramasında bulunan üç KIM-S19 açıklığı aşağıda ayrıca listelenmiştir. 12: bugün API sağlayıcısı yok; ileride Mikro ePortal/AI kontrolü fikri mevcut aktivasyon yetkisi değildir. [Ajan aktarımı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6959047458). Gerçek sağlayıcı kanıtı bulunmayan akış 503 ile kapalı kalır.
Tam eski soru ve seçenekler tablodaki kaynak bağlantılarındadır.
Bu başlıklardan bağımsız mevcut kaynak işleri devam eder. Yanıt bekleyen dal
başarıya çevrilmez ve ilgili dilim bütün olarak DONE sayılmaz.

| Dilim | İlk kayıttaki konu | Eski soru kaynağı |
|---|---|---|
| FIN-S17 / FNS-SC32 | Banka devir bakiyesinde gerçek banka tarihi ile muhasebe tarihi ayrımı | [#1189 / 6952221919](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6952221919) |
| FIN-S17 / FNS-SC32 | Eski dönemi yeniden açarken sonraki kapalı devir zincirinin sırası | [#1189 / 6952770954](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6952770954) |
| SAT-SC34 | Dışarıda resmen iptal edilen normal/fason satış faturasını kaydetme yetkisi | [#1189 / 6952955890](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6952955890) |
| STK-S3 | Araç sevkinde kullanılmayan ayırmanın aynı işlemde mi, ayrı komutla mı bırakılacağı | [#1189 / 6953319371](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6953319371) |
| STK-SC36-a / BS-1 | İlk alış maliyeti tanınmasında stok/SMM payı ve stok konumlarına kuruş artığı | [#1189 / 6954297311](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6954297311) |
| STK-SC36-a / BS-1 | Kısmi ilk faturanın stokta/sevkte bulunan miktarlara nasıl bağlanacağı | [#1189 / 6954342313](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6954342313) |
| KIM-S19 | Finans makbuz/dekont serilerinin kapsamı ve numaranın ayrılma anı | [#1189 / 6955226953](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6955226953) |
| URT-S8 / URT-SC36 | Maliyeti tanınan çıktı transfer edilmişken maliyet neslini tersleyip yeniden açma | [#1189 / 6955281586](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6955281586) |
| FIN-SC47-KREDI / TK-30 | Kredi anapara/faiz/vergi/bildirilen masraf hesap eşlemeleri ve fiş akışı | [#1189 / 6955817730](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6955817730) |
| FIN-S14 | Bozdurmadaki iki referans TL karşılığı ile gerçek muhasebe değerinin ayrımı | [#1189 / 6955922417](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6955922417) |
| STK-SC36-a | İlk SMM/geç alış farkının fiş ölçeğine yuvarlanması ve exact artığın kanıtı | [#1189 / 6955945690](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6955945690) |
| STK-S8 | Gerçek GİB gönderim/iptal kanıtının alınacağı mevcut program veya entegratör kaynağı | [#1189 / 6956098674](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6956098674) |

## Bu turda yanıtlanan iki ayrı attribution sorusu

- [6956807403](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6956807403): mevcut üretim SMM tutarının gerçek sevk/fatura payına kg oranıyla bağlanması; signed artık en ağır ve eşitlikte önce kaydedilen sevke, ikinci SMM olmadan.
- [6957337880](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6957337880): artık bir payı ters işarete geçirecekse sıfırda durup kalan artığın aynı sıradaki sonraki paylara uygulanması.

Bu iki bağlantı doğrudan oturum cevabının açıkça etiketlenmiş ajan aktarımıdır;
bağımsız insan GitHub yorumu diye sunulmaz. İlk BS1 payı, ilk SMM fiş ölçeği
ve diğer yukarıdaki sorular bu iki cevapla seçilmiş değildir.

## Sonraki doğrudan kullanıcı cevapları

- 1: [6957888388](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6957888388), banka devri gerçek banka işlem tarihi; muhasebe kayıt tarihi korunur.
- 2: [6957901618](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6957901618), sonraki kapalı dönemler kendi iki kişili akışlarıyla önce açılır; otomatik açılış yok.
- 3: [6957924089](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6957924089), dış resmî iptali mevcut satış faturası kesinleştirme yetkilisi kaydeder; dış programda iptal veya muhasebe onayı yetkisi türetilmez.
- 4: [6957941048](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6957941048), kullanılmayan fason ayırmayı yetkili ayrı komutla bırakır; araç sevkinde otomatik bırakılmaz.
- 5–9: [6958072219](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6958072219), ilk BS1 stok/SMM pay ve artık kuralı, kısmi ilk faturada fiziksel kaynak bağı yoksa oranlı dağıtım, gerçek kayıtta iki şirket/yıl makbuz-dekont serisi, üretim maliyeti terslemeden önce gerçek transferi geri alma ve kredi bileşen hesap eşlemeleri/farklı onay.
- 10–11: [6958102286](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6958102286), TCMB referansı gerçek muhasebe değerinden ayrıdır; ilk SMM/geç alış kaynak bileşeninde tek profil yuvarlaması ve exact signed artık izi.

Bu bağlantıların tamamı doğrudan çalışma sohbetindeki insan cevaplarının açıkça
etiketlenmiş ajan aktarımıdır. Bağımsız insan GitHub yazarlığı değildir. Yanıtlanan
karar, ilgili kaynağın yazılıp trene alındığı veya runtime kabulünün geçtiği anlamına gelmez.


## İlk alış SMM tutarının gerçek sevklere parasal atfı — yanıtlandı

[6960115841](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6960115841) doğrudan oturum yanıtının ajan aktarımıdır. Kullanıcı üretimdeki artık kuralını alışa da seçti. Her sevkin kendi exact maliyeti korunur; yalnız yuvarlama farkı en yüksek kg/eşitlikte önce kaydedilen sevkten başlayarak, sıfırı aşmadan sonraki paya geçer. Tek 0,01 TL iki eşit payda 0,01+0,00 olur; ikinci fiş/fiziksel hareket olmaz. Soru açık değildir.

## KIM-S19 / mevcut B-13 — üç dar semantik yanıtlandı

[6960365506](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6960365506) soru kaydıdır, karar değildir. Kullanıcıya doğrudan üç ayrı kısa soru gösterildi:

1. Tanımlanan genel onay eşiğinde tam sınır dahil mi, yalnız üstü mü?
2. Kasa farkı eşiği kasa+para birimi başına tutar mı, kayıtlı kasa bakiyesinin yüzdesi mi?
3. K6 teminat vade aralığı bitişine kalan gün mü, başlangıç-bitiş toplam süre mi?

[6960531086](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6960531086) doğrudan kullanıcı cevaplarının ajan aktarımıdır: (1) eşik dahil; (2) her kasa ve para birimi için tutar; (3) bitişine kalan gün. Bu üç soru açık değildir. Sayısal eşik/varsayılan/grant seçilmedi. Mevcut zorunlu maker-checker, yayın lifecycle, OPEN ve Finance STOP sınırları korunur. Teminat tür/oranının zaten seçilmiş FNS-D31/D70 ve iş gününün FNS-D75 hükmü bu sorulara yeniden açılmadı.

## FIN-S14 — belgeli gerçek TL defter değeri yanıtlandı

[6960684942](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6960684942) doğrudan oturum cevabının ajan aktarımıdır: “Belgeli gerçek değer ve farklı kişi onayı (Önerilen)”. Bozdurulan dövizin mevcut muhasebedeki gerçek TL defter değerini muhasebe yetkilisi kaynak belgesiyle girer; farklı muhasebe yetkilisi onaylar. TCMB karşılaştırması gerçek değerin yerine geçmez; FIFO/ortalama yöntemi veya hedef yabancı döviz için yeni değerleme politikası seçilmez. Kaynak uygulaması FIN-S14 sahibi tarafından sürdürülür.

## SAT-S12 — bağımsız golden incelemesi geldi, açılış kapısı sürüyor

Kullanıcının ilettiği bağımsız inceleme kaynak adayı `e20f0ad14a44ecfe927060dbe0a2ff7e3eda9c8c` formül çekirdeğini şartlı APPROVE etti. Bu bir GitHub DOCS/K3 imzası değildir. P2-1 gerçek Settings TL kimliği bağı `3aa5b4c58` ile düzeltildi; birleşik `65618ac1e` kaynak derlemeleri API/Accounting.Unit/Sales.Unit/Integration/TSC exit0. Golden [#1200](https://github.com/sinangym/GoymenERP/pull/1200), güncel exact head `dfab7e934fd6e7fc3b0dd5ca40bd9a3643d3ee4f`; önceki63913 head'i bir REQUEST-CHANGES ve bir APPROVE aldı, yeni head'de iki imza yeniden gerekir. BS-1 ile karara bağlı maliyet kuru ile TL dışı satış marjı ayrı yazıldı; eski OPEN atfı açık karar sayılmıyor. Kullanıcı bu aday için iki ayrı bağımsız Claude incelemesini kabul etti ([6961293621](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6961293621)); genel K3 değişmez. İki yeni DOCS/main koşulu tamamlanmadan `MarginGoldenAccepted` açılmaz. Doğrudan sıfır maliyet cevabı [6960913297](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6960913297), marj kullanılamıyor/sipariş onayı engellenmez şeklindedir. Kg dışı Stok dönüşümü ve yalnız yüzde taşması bu cevapla seçilmedi.

## KIM-S19 / K6 gerçek banka kurumu — yanıtlandı

[6961276925](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6961276925), doğrudan kullanıcı cevabının açık ajan aktarımıdır: “Banka listesinden açıkça seçilsin (Önerilen)”. Ayarlar'da ad/EFT kodlu gerçek banka listesi ve teminatta açık seçim uygulanır. Eski kayıtta ad/IBAN/hesap kimliğinden otomatik eşleme yoktur; sayısal oran/eşik/grant seçilmez. Bu soru artık açık değildir; Settings sahibi banka/K6 kaynağında ilerler, Finance model tek-yazar sınırı korunur.

## Sonraki durum — golden kapısı ve hedef döviz değeri

- Golden #1200 iki yeni DOCS imzasıyla main `3c1b47878447570210b933b83d874646f9c52e4b` oldu. Ayrı uygulama `19ca53a17` kaynakta golden kabul koşulunu karşılar; Sales.Unit kaynak derleme0. Golden/DOCS insan sorusu açık değildir. Son K3 ve runtime kabulü sürer.
- [6961618871](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6961618871) açık ajan aktarımıdır: “İki TL tutarı da belgeli girilsin (Önerilen)”. Kaynak dövizin gerçek TL defter değeri ve alınan dövizin gerçek TL muhasebe tutarı ayrı, kaynak belgeyle ve farklı kişi onayıyla saklanır. TCMB'den bu gerçek değerler türetilmez. Hedef TRY ise gerçek alınan TL tutarı korunur. Bu soru açık değildir; kaynak uygulaması FIN14 owner/consumer checkpointlerinde sürer.
- K6 “30 güne kadar” örneğinin orijinal soru penceresi [6961524705](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6961524705) ile ayrı aktarılmıştır; bitişe kalan gün üst sınırıdır. Bu aktarım yeni bir kullanıcı cevabı veya sayısal varsayılan değildir.
