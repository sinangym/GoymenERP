# W8 — yanıt bekleyen mevcut kararların dizini

17 Kasım 2026 kaynak kontrolü. Bu dosya yeni iş kapsamı veya karar oluşturmaz.
İlk kayıtta aşağıdaki 12 soru bekliyordu. Güncel durumda 1–12 doğrudan kullanıcı
tarafından yanıtlandı. Kullanıcıdan beklenen yeni bir işletme kararı yoktur. 12: bugün API sağlayıcısı yok; ileride Mikro ePortal/AI kontrolü fikri mevcut aktivasyon yetkisi değildir. [Ajan aktarımı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6959047458). Gerçek sağlayıcı kanıtı bulunmayan akış 503 ile kapalı kalır.
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
