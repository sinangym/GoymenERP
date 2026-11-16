# Sinan Kararları — 2026-11-16 (tren-1 ek 27: kur, kg dağıtımı, numaralama, bildirim, SMM, üretim sırası, gelecek ayırma ve şirket kurulumu)

Bu belge önceki karar dosyalarını silmez; FNS-D111, SAT-D421–SAT-D422, KIM-D55–KIM-D56,
KLN-D115, URT-D110–URT-D111 ve ALS-D118'i ekler. Önceki dosyalar yerinde değiştirilmez.
Her karar aşağıda güncellediği hükmü adıyla belirtir; değinilmeyen önceki hükümler sürer.
Bu belge, #1195'teki BELGE-Q1–Q5'in ilk yazıldığında açık olduğu tarihsel kaydı değiştirmez;
sonradan alınan cevapları ayrı kaydeder. Bir `OPEN-*` kaydını kapatmaz.

**Kaynak:** Sinan'ın 16 Kasım 2026 tarihinde geliştirme oturumundaki soru pencerelerine
verdiği cevapların **ajan tarafından Issue #1189'a aktarımıdır**. Bağlantılar, Sinan'ın
GitHub'a bizzat yazdığı yorumlar veya bağımsız insan imzaları olarak sunulmaz:

- [1–5 ve 6–8 cevaplarının aktarımı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6926090415).
- [9 ve 6 takip cevaplarının aktarımı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6926238225).
- [5 takip cevabının aktarımı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6926298700).
- [6 somut sıra örneğinin sorusu](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6926698594)
  ve [son cevabın aktarımı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6926992238).

“(Önerilen)” işaretleri soruyu hazırlayana aittir. Soru penceresindeki seçenekler ve
açıklamaları aşağıda ayrılır; kaynakta verilmemiş seçenek açıklaması uydurulmaz.

### Serbest cevaplar (sözcüğü sözcüğüne)

1–5 ortak cevabı:

> 1-A, 2-A ama teorik kg lara göre oranlamak mantıklı, 3-A, 4-A, 5-önerini uygula ama burda her faturadaki her ürün satırı siparişe ve fatura geneline bağlı kalmadan fatura kesinleştikten sonrada stokta kalıp kalmamasına bakılmaksızın muhasebesel bir işlem olduğu için değiştirilebilmeli

6–8 ortak cevabı:

> 6-üretim kuyruğu üretim müdürü koşullara bakmadan değiştirebilmesi gerekir, 7-bence B seçeneği eğer mantıksız değşlse sende buna mantıklı bir yorum getir, 8-A

9 ilk cevabı:

> Bunu bilmiyorum yani soruyu anlamadım ama , yeni şirket açma yetkisi yönetim den bir kişi ve genel müdür yetkisi ile açılabilsin ve ilk yetkileri açıldıktan sonra genel müdür veya yönetimden biri verebilir

5 takip cevabı:

> Bu kapsamı uygula, bunun yapılıyor olma sebebi zaten muhsebesel ve vergisel, gerekiyorsa bunu araştır

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması ve seçilmeyen seçenekler |
|---|---|---|---|
| 1 / BELGE-Q1 | Siparişin otomatik TCMB döviz satış kuru hangi güne ait olmalı? | Ortak cevapta **1-A**. | **A (Önerilen):** SAT-D54/SAT-D170'teki kesin önceki iş günü ve arife algoritması. Seçilmeyen **B:** doğrudan işlem tarihinin yayını. |
| 2 / BELGE-Q2 | Kg farkı dağıtımının fatura/irsaliye kapsamı ve artık kuralı nedir? | Ortak cevapta **2-A ama teorik kg lara göre oranlamak mantıklı**. | **A (Önerilen):** SAT-D409 yalnız faturada teorik kg oranlı dağıtım; irsaliyede SAT-D322'nin tamamını en ağır satıra yazma kuralı sürer; faturada yuvarlama artığı eşit en ağır satırlar içinde ilk satıra yazılır. Seçilmeyen **B:** farklı kapsam/artık kuralını açıklama. |
| 3 / BELGE-Q3 | Önek+yıl+sıra ve yıllık sıfırlama alış siparişini de kapsıyor mu? | Ortak cevapta **3-A**. | **A (Önerilen):** alış siparişini de kapsar, ALS-D36 eski algoritmasının yerini alır. Seçilmeyen **B:** alış siparişi hariçtir. |
| 4 / BELGE-Q4 | Bildirimleri 90 gün sonra pasifleştirme süresi değiştirilebilir mi? | Ortak cevapta **4-A**. | **A (Önerilen):** 90 gün, Ayarlar'dan değiştirilebilir başlangıç değeridir. Seçilmeyen **B:** sabit 90 gündür. Her iki seçenekte kalıcı silme yapılmaz. |
| 5 / BELGE-Q5 | SMM hesabında ticari mal=621 / mamul=620 ayrımı hangi ürün alanından okunmalı? | Ortak cevaptaki **5-önerini uygula** ve devamındaki satır bazlı değişiklik isteği. | Öneri: ürün kartında açık ticari mal/mamul sınıfı; mevcut uygun alan varsa adını belirtme; satın alma/üretim hareketinden tahmin etmeme. Numaralı başka seçenek verilmedi. |
| 5 takip | Her satırın ticari mal/mamul sınıfı ve SMM muhasebe hesabı bağımsız değişebilir; sipariş/fatura geneli ve stokta kalması engel olmaz; tutar, miktar, vergi ve fiziksel stok bu işlemle değişmez. Önceki muhasebe kaydı korunur; gerekiyorsa açık dönemde ters kayıt+düzeltme kaydı oluşur. Daha geniş değişiklik isteniyorsa alanlar belirtilir. | Yukarıdaki **“Bu kapsamı uygula…”** cevabı. | Sorulan kapsam kabul edildi. Daha geniş alan değişikliği seçilmedi; kaynakta numaralı başka seçenek yok. |
| 6 | Üretim kuyruğunu elle taşıma hangi sınırla yapılmalı? | Ortak cevapta **üretim müdürü koşullara bakmadan değiştirebilmesi gerekir**. | İlk **A (Önerilen):** yalnız aynı termin/puan grubunda; termin/puan değişince elle sıra sıfırlanır. İlk **B:** farklı termin/puanları aşan kalıcı sıra. İlk **C:** başka kuralı açıklama. Serbest cevap grup sınırını kaldırır; kalıcılık takip sorusunda netleşir. |
| 6 takip | Üretim müdürünün elle verdiği sıra termin/puanı aşacak. Sıra sonradan termin/puan değişse de korunsun, kuyruğa yeni giren emirler listenin sonuna eklensin mi? Yetki yalnız kaydedilecek; danışılmadan rol/grant yapılmayacak. | **“Sıra korunsun; yeni emir kendi termin/puanına göre yerleşsin”** | Elle verilen mevcut sıra korunur. Yeni emri her zaman sona ekleme seçilmedi. |
| 6 somut örnek | Müdür B (20 Kasım) → A (10 Kasım) sırası verdi. Sonradan C (15 Kasım) geldiğinde B→A korunurken C nereye girsin? | **“C→B→A: daha geç terminli ilk işin önüne (Önerilen)”** | Mevcut işlerin birbirine göre elle sırası bozulmaz; C, listede kendisinden daha geç terminli ilk işin önüne girer. Seçilmeyen **B→C→A:** otomatik sıra numarası. Seçilmeyen **B→A→C:** kendisinden erken terminli son işin arkası. |
| 7 | İade/ters kayıt sonrası yeniden açılacak gelecek ayırma miktarı talep sınırına sığmazsa ne olmalı? | Ortak cevapta **7-bence B seçeneği eğer mantıksız değşlse sende buna mantıklı bir yorum getir**. | Seçilen **B:** yalnız sığan miktar açılır, kalanı bırakılmış sayılır. Seçilmeyen **A (Önerilen):** komut bütünüyle reddedilir. Her iki seçenekte daha önce bırakılan ayırmalar geri getirilmez. |
| 8 | Üretim planının ilk taslağı/revizyonunda HamKg üst sınırı ne olmalı? | Ortak cevapta **8-A**. | **A (Önerilen):** etiketin bütün pozisyonlarındaki mevcut fiziksel kg toplamı; onayda serbest kg ve tek pozisyon kontrolü ayrıca sürer. Seçilmeyen **B:** ayırmalar düşülmüş serbest kg; çoklu pozisyonun nasıl sayılacağı da belirtilmelidir. |
| 9 / KIM-S5 | Yeni şirket açma Critical izninin kapsamı ne olsun? Soru, grup genelinde ayrı yetkiyi önerdi; otomatik grant/rol yapılmayacağını söyledi. | Yukarıdaki **9 ilk cevabı**. | Kullanıcı teknik kapsam terimini anlamadığını belirterek yönetimden bir kişi veya genel müdür yetkisini tarif etti. İlk yetkileri açılıştan sonra genel müdür veya yönetimden biri verebilir. Bu serbest cevaptan teknik izin adı veya otomatik rol ataması çıkarılmaz. |
| 9 takip | Şirketi yönetimden biri veya genel müdür tek başına açabilsin; açıldıktan sonra ilk şirket yöneticisini de yönetimden biri veya genel müdür seçip atasın, açana otomatik yetki verilmesin. Bu akış mı, yoksa açılışta iki kişinin onayı mı gerekir? | **“Yetkililerden biri yeterli; ilk yöneticiyi seçer (Önerilen)”** | Yetkililerden biri yeterlidir ve ilk yöneticiyi açıkça seçer. İki kişi onayı seçilmedi. Açana otomatik yetki verilmez. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D111 | Sipariş otomatik kuru; **FNS-D98'i günceller**, FNS-D79 ve SAT-D54/SAT-D170 ile aynı algoritmayı seçer | BELGE-Q1 yanıtında kesin önceki iş günü ve arife algoritması seçildi. Bu seçim, EK23 FNS-D98'deki **“işlem tarihindeki kur”** hükmüyle çelişen kısmın yerine geçer; işlem tarihinin doğrudan yayını kullanılmaz. Döviz satış türü korunur. Bu hüküm diğer olayların kur tarihini kendiliğinden değiştirmez. | Sipariş otomatik kur kaynağı, FNS-D98, BELGE-Q1 |
| SAT-D421 | Fatura/irsaliye kg dağıtımı; **SAT-D409'u netleştirir**, SAT-D322 (3)–(4)'ün irsaliye kapsamını korur | Faturada fark teorik kg oranında dağıtılır. Fatura yuvarlama artığı eşit en ağır satırlar içinde ilk satıra gider. İrsaliyede farkın tamamını en ağır satıra, eşitse ilk satıra yazan SAT-D322 (3)–(4) sürer. | Fatura ve irsaliye deterministic kg hesapları, BELGE-Q2 |
| KIM-D55 | N-12 kapsamı; **KIM-D53'ü netleştirir ve ALS-D36'nın eski alış siparişi numara algoritmasını günceller** | Önek + yıl + sıra ve yıllık sıfırlama alış siparişini de kapsar. Alış siparişi için ALS-D36'nın eski algoritması bu yeni biçimin dışında sürdürülmez. | Şirket belge numara serileri, alış siparişi, KIM-S19, BELGE-Q3 |
| KLN-D115 | Bildirim süresi; **KLN-D114'ü netleştirir**, KLN-D111 ile Ayarlar bağını korur | Pasifleştirme için 90 gün, Ayarlar'dan değiştirilebilir başlangıç değeridir. Kalıcı silme yoktur; denetim kaydı bildirimin pasifleştirilmesiyle silinmez. Başka retention/imha politikası seçilmez. | Bildirim deposu, süre ayarı, BELGE-Q4 |
| SAT-D422 | SMM sınıfı ve satır düzeltmesi; **SAT-D419'u tamamlar**, SAT-D420'nin mevcut Finans eşleme yetkisini korur | Ürün kartında açık ticari mal/mamul sınıfı kullanılır; satın alma/üretim hareketinden tahmin edilmez. SAT-D419 varsayılanları ticari mal 621, mamul 620 olarak sürer. Kesinleşmiş faturada her satırın ticari mal/mamul sınıfı ve SMM hesabı siparişten/fatura genelinden bağımsız, stokta kalmasına bakılmadan değiştirilebilir. Bu işlem tutar, miktar, vergi ve fiziksel stoğu değiştirmez. Önceki muhasebe kaydı korunur; gerekiyorsa açık dönemde ters kayıt+düzeltme kaydı oluşur. Finans eşleme ayarını değiştirme yetkisi ile bu satır düzeltmesinin yetkisi birbirine otomatik eşitlenmez. | Ürün sınıfı, fatura satırı muhasebe snapshot'ı ve düzeltme kaydı, BELGE-Q5 |
| URT-D110 | Üretim kuyruğunu elle sıralama; URT-S5'in termin/puan sıralamasıyla birlikte uygulanır | Üretim müdürü termin/puan koşullarını aşarak elle sıra verebilir. Mevcut işlerin birbirine göre elle sırası sonraki termin/puan değişikliklerinde korunur. Yeni emir kendi termin/puanına göre, mevcut elle sırayı bozmadan yerleşir; B(20)→A(10) örneğine C(15) geldiğinde C→B→A olur. Elle sıralama yetkisi kaydedilir; bu kayıtla rol/grant atanmaz. | Üretim kuyruğu komutu, sıra deposu ve kuyruk görünümü, URT-S5 |
| ALS-D118 | İade/ters kayıt sonrası gelecek ayırmayı yeniden açma | Talep sınırına yalnız sığan miktar yeniden açılır; kalanı bırakılmış sayılır. Daha önce bırakılmış ayırmalar geri getirilmez. Ajanın aşağıdaki tutarlılık değerlendirmesi kullanıcı alıntısı değildir ve yeni stok hareketi kuralı eklemez. | İade/ters kayıt, gelecek ayırma sahibi, ALC-S6 / STK-S19 |
| URT-D111 | İlk taslak/revizyon HamKg üst sınırı | Etiketin bütün pozisyonlarındaki mevcut fiziksel kg toplamı ilk taslak/revizyondaki üst sınırdır. Onaydaki serbest kg ve tek pozisyon kontrolleri ayrıca sürer; taslak kabulü stok ayırma veya kullanılabilirlik garantisi değildir. | Plan taslağı/revizyon doğrulaması; onay ayırma kontrolü |
| KIM-D56 | Yeni şirketin açılması ve ilk yöneticisi; **KIM-D18'in sonradan açılan şirketi açana otomatik Admin hükmünü günceller**, KIM-D20'yi yeni akışla tamamlar | Yönetimden yetkili bir kişi veya genel müdür tek başına şirket açabilir. Açılıştan sonra bu yetkililerden biri ilk yöneticiyi açıkça seçip atar; açan kişi otomatik yetki almaz. Açılışta iki kişi onayı aranmaz. KIM-D18'in mevcut şirketlerde kurucu bootstrap kapsamı bu cevapla değiştirilmez. KIM-D20'nin yönetici oluşana kadar kurulum bekliyor/işlem kabul etmeme/alarm sınırı korunur; otomatik açana atama yerine açık kişi seçimi uygulanır. | Organization şirket açma, Identity ilk yönetici seçimi, KIM-S5 |

SAT-D420, ayrı EK26 / #1194 belgesinde kayıtlıdır; bu belge #1194'ün inceleme veya merge
durumunu yükseltmez. Kimlikler, main'deki EK25 ve EK26 adayı birlikte okunarak ayrılmıştır.

### 7B için ajan değerlendirmesi (karar alıntısı değildir)

Kısmi yeniden açma talep tavanıyla tutarlıdır: yeniden etkin miktar ile bırakılmış kalan
ayrı ve izlenebilir olmalı; miktar kaybolmamalı, talep tavanı aşılmamalı, eski bırakılmış
ayırmalar diriltilmemelidir. Bu, seçilen B'nin uygulanabilirlik açıklamasıdır; fiziksel
stoğu artıran ikinci hareket veya bağımsız işletme politikası değildir.

### Muhasebe kaydını koruma araştırması (işletme kararı değildir)

[GİB'deki Vergi Usul Kanunu, madde 217](https://sanliurfa.gib.gov.tr/mevzuat/kanun/434),
yanlış kayıtların düzeltilmesini ve önceki kaydı okunamaz hale getirmeme ilkesini destekler.
**Açık dönemde ters kayıt+düzeltme kaydı**, burada kullanıcının kabul ettiği uygulama
kapsamıdır; araştırma bu kesin yöntemin her durum için kanunen zorunlu olduğu iddiası
değildir. 621/620 bu belgenin vergi mevzuatından çıkardığı yeni hesap seçimi değildir;
SAT-D419'daki kullanıcı varsayılanlarıdır. Beyanname/KDV değişikliği veya kesinleşmiş
vergi belgesini değiştirme yetkisi bu araştırmadan çıkarılmaz.

## Sınırlar

- Cevaplanmış BELGE-Q1–Q5, 6–9 ve 5/6 takip soruları yukarıdaki kapsamla kaydedilir;
  teknik kaynağın yokluğu veya eksik test bu cevapları uygulama kabulüne dönüştürmez.
- **NEEDS-SINAN:** Kesinleşmiş fatura satırının muhasebe sınıfı/SMM hesabı düzeltmesini
  hangi mevcut yetki sahibinin gireceği ve ayrı onay gerekip gerekmediği bu cevaplarda
  seçilmedi. Finans eşleme ayarı yetkisini kendiliğinden bu komuta taşımak yoktur.
- Kapalı dönemdeki muhasebe kaydı yerinde değiştirilmez. FNS-D108/FNS-D110'daki kredi
  düzeltmesinin izinleri başka bir fatura düzeltme komutuna otomatik taşınmaz. Fatura
  düzeltmesi için cevaplanmamış ek kapalı dönem politikası **NEEDS-SINAN** kalır.
- Şirket açma/ilk yönetici seçimi için gerçek grup yetkisi ve üretim sıralama yetkisi
  kodda kaydedilebilir; kullanıcıya gerçek grant/rol ataması yapılmaz. Sırf mevcut bir
  şirkette yetkili olmak bütün şirketlerde kurulum yetkisi sayılmaz.
- İzin adı ve veri yapısı geri alınabilir uygulama ayrıntısıdır; Critical sınıfı için
  bu cevaplarda verilmeyen yeni bir karar çıkarılmaz. Eski otomatik atama tarihçesi silinmez.
- Eski karar dosyaları, karar sicili, master ve `OPEN-*` durumları yerinde değiştirilmez.
  Kod/test/gerçek SQL/browser/CI kabulü veya bağımsız review bu belgeyle kazanılmaz.
- Deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production,
  `OPEN-*` kapatma ve `.claude/settings.json` insan kapıları değişmez; Finance STOP sürer.
- İki bağımsız exact-head DOCS review gerekir; yazar kendi inceleme imzasını vermez ve
  kendi PR'ını merge etmez. Eksik işletme politikası tahminle etkinleştirilmez.
