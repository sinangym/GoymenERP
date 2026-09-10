# Sinan kararları — 10 Eylül 2026

## Kayıt ve yetki sınırı

Kaynak: bu çalışma görevindeki kullanıcının açık cevapları ve bunları taşıyan yerel v6.1 raporu. Bu dosya karar sicili/master raporun yerine ikinci bir yürütme listesi değildir; güncel kullanıcı kararlarının kaynak kaydıdır. Kullanıcı kararları geçerlidir; bunların mevcut karar/spec/kodla uzlaştırılması bu PR'da insan incelemesine sunulur. Bu kayıt **production aktivasyonu, açık sayısal politikaları seçme, eski kayıtları silme veya kendi PR'ını merge etme yetkisi vermez**.

Yerel önceki rapor: `GoymenERP-Nihai-Is-Akisi-ve-Uzlastirma-Raporu-v6.1.md`.
SHA-256: `B1CBF0AA9C54EB6472644F164ACD73AA2000657249A9EA1375D3E300352BE677`.
Kaynak klasör: `C:\Users\goyme\Documents\Codex\2026-09-07\g\outputs`.
Bu yeni kayıt önceki dosyayı değiştirmez; önceki rapordaki KUL önerilerini aşağıdaki son cevaplarla günceller.

Bu dosyadaki Q/F/KUL kimlikleri konuşma soru kimlikleridir; yeni bir `DEC-*` numarası uydurulmamıştır. Teknik öneriler aşağıdaki kullanıcı kararlarından ayrı ele alınır.

## 1. Son cevapların açık anlamı

| Konuşma kararı | Geçerli davranış | Sınır / gelecekte değişecek bölüm |
|---|---|---|
| Q01 — önce B, sonra A | Başlangıçta yetkili iç onay yeterlidir. | Gelecekte siparişe bağlı fiziksel üretim/sevk için müşteri kaşeli yazısı aranacak. Sözlü onay yeterli sayılmaz. Geçiş tarihi seçilmedi; plansız makine durdurma veya siparişsiz stok üretimine müşteri onayı getirme yok. |
| Q02 — önce B, sonra A | Kritik revizyon onay beklerken önceki onaylı sipariş sürümü geçerli kalır. Yeni sürüm onayında gerçekleşen işlemler yeniden kontrol edilir. | Gelecekte yalnız etkilenen ve henüz gerçekleşmemiş bölüm bekletilir. Gerçekleşmiş sevk/üretim/fatura geçmişi değiştirilmez. Geçiş tarihi seçilmedi. |
| Q03 — C | Fason/hizmet siparişinin ürünleri işlenip teslim/sevk edildikçe ilgili bölüm faturalanabilir olur. | Sadece üretimden çıkış, tek başına teslim veya resmî fatura değildir. Hizmet alışında geri geliş/doğrudan teslim kanıtı da tanımlanır. Miktar/fiyat esası ilgili sipariş sözleşmesinden gelir; otomatik resmî fatura gönderimi yoktur. |
| Q04 — A | İade amacı: ikame/yeniden teslim veya ticari kapatma/fiyat düzeltme olarak tutulur; miktar bölünebilir. | Otomatik para iadesi veya yeni üretim emri doğurmaz. |
| Q05 — A | İhtilaflı fatura için yetkili kişi olay bazında gerekçe ve kanıtla karar verir. | Asıl belge ve fiilî stok gerçeği korunur; hukukî sonuç veya mevzuat süresi tahmin edilmez. |
| Q06 — A | Zincirde iç ve dış operasyon birlikte yönetilir. | Müşteri malının dış işleme gönderilmesi iş bazında açık yetkiye bağlıdır; mal sahibi değişmez. |
| Q07 — A | Ortak hat ve araç kullanılabilir; fiziksel paketler mal sahibine göre ayrılır. | Bir paket/etikette farklı sahiplerin malı karışık tutulmaz. |
| Q08 — A | Homojen grup tartımı dağıtım önerisi ve yetkili onayıyla kullanılabilir. | Gerçek ölçüm ile dağıtılan değer ayrılır; ham tartı verisi korunur. |
| Q09 — A, boya sadeleştirmesiyle | Stokta fiilî ambalajsız net kg esas alınır. | Boya gramı/kaplama kg'ı için zorunlu ayrı stok/sarf alanı açılmaz. Giriş–çıkış farkı görünür kalır; farkın tamamı otomatik boya veya negatif fire sayılmaz. Ölçülmeyen boyayla tam kütle dengesi kanıtlandığı söylenmez. |
| Q10 — B | Yükleme artı/eksi toleransla yapılabilir. | Yükleme planı sapması, kümülatif sipariş teslim toleransı ve üretim/tartı dengelemesi ayrı kurallardır. Sayısal değer seçilmedi. |
| Q11 — B | Fiyatı görmeyen kullanıcıya ayrıca yetki verilirse onaylı sabit belgeyi gerçek alıcıya fiyatı görmeden gönderme mümkündür. | Gönderene/AI'ye fiyatlı dosya, önizleme veya erişim URL'si verilmez. Alıcı/adres/kanal/CC/BCC denetlenir; gönderimde yetki tekrar kontrol edilir. Desteklemeyen kanalda yetkiliye devir yapılır. |
| Q12 — A | Ses geçicidir; kullanıcı tarafından doğrulanan metin ve gerekli denetim izi ayrı tutulur. | Ham ses kalıcı yedeğe alınmaz. Hata/iptal/çökme temizliği gerekir. Kesin saklama süreleri seçilmedi. |
| Q13 — A | Kendi şirketinin VKN değişikliği hazırlayan dışında bir yetkilinin onayını gerektirir. | İki ayrı onaycı zorunlu değildir. Eski belgenin byte'ları değişmez; VKN kimliği/benzersizliği korunur. |
| F01 — A, kabul edildi | Tolerans içinde eksik son teslimde kalan açık kalır; yetkili gerekçeyle kapatır. | Kapatılan kalan, yapılmış teslim veya fatura değildir. Borç affı/para iadesi yaratmaz. Güncel sürüm ve bağlı plan/tahsis etkisi önizlenir. |
| KUL-01 — B | Hareketli sayım: sayım sürerken hareketler devam eder; ölçüm zamanı ve hareketler uzlaştırılır. | Genel depo dondurma varsayımı yoktur. Kesim zamanı, kayıtta eşzamanlı hareket, tekrar sayım ve sayım farkının bir defa işlenmesi teknik sözleşmede tanımlanacaktır. |
| KUL-02 — B | ERP'ye erişilemiyorsa yeni fiziksel başlangıç ve mal çıkışı bekler. | İnternet kesilmesi her mimaride ERP kesintisi demek değildir; ölçüt ERP'ye güvenilir erişimdir. Devam eden makinenin insan ve ekipman güvenliği saha prosedürüdür. Otomatik makine durdurma veya onaysız çevrimdışı stok kaydı tasarlanmaz. |

## 2. Korunan ürün ve iş akışı kararları

### Sipariş, cari ve belge

- Ayrı ön sipariş ekranı olmayacak. Normal sipariş ekranında **Talep / Sipariş** ayrımı bulunacak. Talep renk, simge ve anlaşılır durum sütunuyla görünür olacak; fiziksel malzemeyi bloke etmeyecek ve kesin tahsis gibi adlandırılmayacak. Kesin siparişe dönüşüm ayrıca uygunluk/yetki kontrolü gerektirir.
- Aynı hukukî kişi için tek cari kimlik; müşteri/tedarikçi/ticari/fason ticaret rolünü önceden seçmek zorunlu olmayacak. Cari hem mal/hizmet alabilir hem satabilir. Firma/Kişi türü, hukukî kimlik ve kullanıcı güvenlik rolleri bundan farklıdır ve kaldırılmaz.
- Yetkili kullanıcı birden fazla Göymen şirketinin ilişkilerini birlikte görebilir. Şirket/currency bakiyeleri sessizce birleştirilmez veya mahsuplaştırılmaz. Her yazma işlemi tek ve açık şirkete bağlıdır.
- Cari ekranı firma bilgileri, ilişkili şirketler, açık/kapalı sipariş ve kalanlar, giriş/çıkış irsaliyeleri, gerçek faturalar, dekontlar, ekstre, belgeler ve uyarılara bağlanan günlük çalışma merkezi olacaktır.
- 7 Eylül yönü: şirket hesabı gerekiyorsa ilk yetkili iş işleminde atomik/idempotent açılması hedeflenir; günlük zorunlu ayrı bağlama adımı hedef değildir. GET/listeleme kayıt oluşturamaz. Bunun ayrıntılı yetki ve komut sözleşmesi ayrıca uzlaştırılmadan mevcut link izinleri genişletilmez.
- Ticari/hizmet ve mal sahibi bilgisi siparişe özgüdür; akışta taşınır. Carinin sabit ticaret rolünden türetilmez.
- Hizmet alış ve satış siparişlerinde mal kabul ve çıkışı hem irsaliye başlığı hem satır/miktar bazında, kısmi ve çoklu belge ilişkileriyle takip edilir.
- Siparişsiz gerçek alış/satış faturası ve yalnız stok transferi için irsaliye kaydı mümkündür. Sonradan siparişle ilişkilendirmek aynı fiziksel/finansal olayı ikinci kez oluşturmaz.
- Bir sipariş sürümünden antetli sözleşme, Türkçe/İngilizce proforma ve ihracat hazırlık belgeleri türetilebilir. Kullanıcının “invoice” dediği İngilizce proformadır; muhasebe borç/alacak, resmî fatura veya stok olayı değildir.
- Eski sipariş sürümleri ve bunlardan üretilmiş sözleşmeler değişmez, sürümlü, kaynağına bağlı korunur. Normal kullanıcı/yönetici silme işlemiyle kaldırılmaz. Tarihsel çıktı güncel şablon veya cari bilgilerle sessizce yeniden yazılmaz. Yasal saklama/imha politikası ayrı karardır.
- Word/Excel/PDF/görsel karışık antet ve şablon yüklenebilir. AI alan–veri kaynağı eşlemesi önerir; yetkili onayından sonra izinli alan kataloğuyla deterministik doldurma yapılır.

### Stok ve üretim

- Beş operasyon: dilme (D), hadde (H), boru/profil (P), boy kesme (K), boya (B).
- Asgari kabul yolları: **D→P→B; D→H→P→B; P→B; D→K; yalnız D; yalnız H; yalnız P; yalnız K; yalnız B**.
- Planın beklenen çıkışı üzerinden sonraki plan kurulabilir. Henüz gerçekleşmeyen beklenen çıktı fiziksel stok sayılamaz. Her ara operasyonda kısmi gerçek çıktı sonraki operasyona ilerleyebilir; plan bağı ve gerçekleşme bağı ayrı izlenir.
- Üst plan değişikliği/iptali gerçekleşmiş alt kayıtları yeniden yazmaz; etkisi görünür olur. Eksik miktara ilişkin planlayıcı kararı ve bağlı sipariş/tahsis etkileri yönetilir.
- Mal şirketinse hurda şirket stoğunda; müşteri malıysa müşteri mülkiyetinde stokta ve geri verilmek üzere kalır. Açık mülkiyet devri olmadan şirket malına dönüşmez. Kullanılabilir artan parça, hurda ve geri kazanılamayan kayıp ayrıdır.
- Teknik kalite/alt kalite, ticari sınıf (standart/ikinci kalite), çoklu kusur (kırıklı, dalgalı, çapak vb.), ürün/çıktı türü ve operasyon durumu ayrı kavramlardır. Uçbaş/kullanılabilir artık bir çıktı türü olabilir; otomatik kusur veya hurda değildir. Fiziksel stok satırı/paket/lot/etiket ve gerekiyorsa ayrılan kısmında tutulur. Bilinmeyen otomatik standart değildir.
- Normal akışı yeni bir kalite kullanılabilirlik/onay kapısına bağlama yoktur. İlk kapsam için zorunlu minimum lot eşiği eklenmez.

### Yetki, AI, dış sistem ve yedek

- Merkezde belirlenen yöneticiler ayarlardan kullanıcı–şirket–sayfa–işlem–onay–fiyat/tutar görme–belge çıktı yetkilerini yönetir. AI hazırlamaya yardımcı olur; kendi yetkisini genişletemez.
- Siparişi oluşturan normal kullanıcı yetkili onayına gönderir; yetkili oluşturduğunda kendi siparişini onaylayabilir. Kritik erişim değişikliklerinde hazırlayan/hedef kullanıcının kendi erişimini onaylamaması ayrı güvenlik kuralıdır.
- AI sistemi bağlama uygun öğrenir: kaynaklı bilgi, arama, araçlar, görsel/ses anlama ve öneriler. Onaysız iş politikası öğrenip değiştirme veya ham SQL/DB erişimi yoktur. AI olmasa da normal ERP işlevleri çalışır.
- Dış AI'ye gerekirse yetkili veri güvenli kapsamda sağlanabilir; bu toplu sınırsız veri gönderimi değildir. Şirket/işlem/alan yetkisi, belge çıktısı, prompt injection ve sır yönetimi sınırları korunur.
- Mikro/Ticaret Sicili şirket adına ortak hesap kullanır; sır kasasında saklanır, modele verilmez. Tarayıcı tabanlı otomasyon hedeflenebilir; sağlayıcı API'si ön koşul değildir. MFA/CAPTCHA, oturum kaybı ve değişen sayfa yapısında güvenli insan devri vardır. Sayfa değişimlerinden etkilenmeme garantisi yoktur.
- Mikro hedefi: gerçek yükleme → taslak → önizleme → yetkili onayı → dış gönderim → arşiv/status uzlaştırması. Sonucu belirsiz gönderim otomatik tekrar edilmez. Gelen irsaliye öneri oluşturabilir ama kendi başına fiziksel mal kabul değildir.
- Ticaret Sicili hedefi: haftalık kontrol aktif/pasif bütün carileri kapsar; yetkili kullanıcı tümünün kontrol durumunu görür. Haftalık ve tek tuş sorgu aynı izlenebilir iş hattını kullanır; aynı hukukî kimlik için gereksiz kaynak sorgusu ve mükerrer uyarı önlenir. Kimlik eksikliği, kapsam dışılık, çelişkili eşleşme veya sorgu başarısızlığı “değişiklik yok” sayılmaz. Yabancı firmanın Türkiye'deki sicilli şubesi yalnız ülke alanına bakılarak atlanmaz. Kaynak/yayın/sorgu tarihi korunur; otomatik kritik cari değişikliği olmaz.
- Şirket bazlı yedek ve **temiz hedefe tek şirket geri yükleme** hedefi korunur. Yalnız yüklenen şirket aktif görünür. Ortak cari ve kullanılan/izinli ürün, hizmet, birim, politika/sürüm gibi master referanslarının gerekli alt kümesi ile bu şirketin kendi ticareti korunur; diğer şirketin defteri/stoğu görünmez. Dolu hedefe sessiz birleştirme/silme yapılmaz.
- Restore kapsamı yalnız DB değildir: dosyalar, audit, anahtar erişimi, arama indeksleri ve iş kuyruklarının güvenli toparlanması da kapsanır. Restore sonrası eski oturumların ve dış gönderimlerin kendiliğinden tekrarı engellenir.

## 3. Kabul edilen risk giderme yönü (§13.4)

Kullanıcı aşağıdaki riskler için önerilen yönde çözüm hazırlanmasını kabul etti. Bunlar bir sayısal eşik veya yeni rol seçimi değildir:

1. Eski karar/spec/kod ile yeni kararın farkını açık eşleme ve geçiş planıyla uzlaştır.
2. Aynı stok/miktar üzerinde eşzamanlılık ve yinelenen komutları backend sürüm/idempotency/transaction kurallarıyla denetle.
3. Modüller arası kısmi etkiyi transactional outbox, inbox/dedup ve uzlaştırma ile yönet.
4. Önizleme ile uygulama arasında sürüm/yetki/bağlı gerçekleşmeleri tekrar denetle.
5. Üst üretim düzeltmesinin hayalî stok veya yeniden yazılmış gerçek geçmiş üretmesini önle.
6. Kg/adet/fiyat/yuvarlama için tek deterministik hesaplayıcı ve onaylı golden örnekler kullan.
7. Sipariş kalanı kapatmayı stok, teslim, fatura ve alacak silmeden ayır.
8. Dosya–metadata tutarlılığı, sürüm değişmezliği ve hata sonrası toparlanmayı doğrula.
9. Fiyat/şirket sızıntısını ekran yanında API, arama, export, dosya, log ve AI araçlarında test et.
10. Önceki yıl kapalı işleme referansla iade/düzeltme yapılabilmesini, geçmişi değiştirmeden doğrula.
11. Ödenmiş faturaya iade ile ödeme aracı başarısızlığının ayrı finansal/fiziksel etkilerini doğrula.
12. DB, dosya, kuyruk, yedek ve erişim sağlığını işletilebilir hale getir.
13. Dosya/anahtar/audit dahil şirket restore ve dış etkinin yeniden gönderilmemesini prova et.
14. Temiz kurulum, sürüm yükseltme, migration ve geri dönüş/forward-fix provası yap; geliştirici bilgisayarında açılmayı ürün kabulü sayma.

## 4. Yeni açık işletme politikası seçilmedi

- Sayısal toleranslar, formül sabitleri, para yuvarlama kuralları, RPO/RTO ve saklama/imha süreleri bu karar kaydıyla tahminen doldurulmaz.
- Mevcut `OPEN-*` kayıtları sessiz kapatılmaz. Eksik gerçek girdiler ilgili işlevin kabul/canlı kapısına bağlanır; bağımsız teknik işler devam edebilir.
- Maliyet/değerleme, banka entegrasyonları ve ayrıca ertelenmiş kapsam tekrar ilk teslim şartı yapılmaz.
- §13.5 için **hangi girdinin ne zaman gerektiği** [başlangıç planında](../planning/2026-09-10/SON_RAPOR_VE_BASLANGIC_PLANI.md) teknik öneri olarak açıklanır.
- GitHub + yerel kopya ortak kayıt seçimi kullanıcı tarafından kabul edilmiştir. AI'lerin özel depoya erişimi ayrıca gerekir; bütün oturumların kendiliğinden senkronize olduğu iddia edilmez.

## 5. Sonraki uzlaştırma

Karar–işlev–test eşlemesi ve çalışma paketleri [paket girişinde](../planning/2026-09-10/README.md) bulunur. Bunlar mevcut tek backlog/ilerleme kaydına ilgili sahip tarafından işlenmeden yeni bağımsız otomatik yürütme otoritesi değildir. Karar/governance incelemesi insandadır; uygulama dilimlerinde mevcut bağımsız review ve CI kapıları geçerlidir.

7 Eylül cari yönü için depo içi kayıt: [Issue #66 — ortak cari / ilk iş işleminde hesap](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5573067471), [Issue #66 — UX-CARI-001](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5573793580).
