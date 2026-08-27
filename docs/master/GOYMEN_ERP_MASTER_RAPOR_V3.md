# Göymen ERP Nihai Tek Master Raporu v3.0

**İlk yayın:** 24 Ağustos 2026  
**Son revizyon:** 25 Ağustos 2026  
**Belge türü:** Tek ve kapsamlı hedef ürün, iş mimarisi, ekran kataloğu, UX, AI, veri, formül, teknoloji, güvenlik, test ve uygulama sözleşmesi  
**Durum:** Analiz ve tasarım hedefi; geliştirilmiş/canlı sistem beyanı değildir  
**Kaynak:** Altı uzmanlık arşivi, 523 benzersiz dosya, 151 PDF sayfası ve 401 benzersiz görsel

**Belge yetkisi:** Bu v3.0 rapor; önceki tasarım anayasası, kapsam raporu, v2.1 master rapor, uygulamaya geçiş planı ve bu konuşmadaki son kullanıcı kararlarını tek metinde birleştirir. Bundan sonraki analiz, tasarım ve geliştirme için ana başvuru belgesidir. Kaynak raporlar kanıt ve geçmiş sürüm olarak saklanır; çelişkide bu belgedeki son tarihli kullanıcı kararı geçerlidir.

**Önemli durum ayrımı:** Bu belge yapılacak sistemin hedef sözleşmesidir; kodlanmış, yayınlanmış veya kullanıcı kabulü tamamlanmış sistem beyanı değildir.

## 0. Yönetici özeti

Hedeflenen sistem, birbirinden kopuk menüler topluluğu değil; satış talebinden tahsilata, satın almadan banka mutabakatına, hammaddeden üretim soy ağacına kadar aynı işlem zincirini tek veri modeli üzerinde yürüten bağımsız bir web ERP'dir.

Sistemin tek cümlelik tanımı:

> Göymen ERP; kullanıcının günlük işini en az adımla tamamladığı, her kaydın kaynağı ve etkisinin izlenebildiği, hesapları deterministik motorların yaptığı, AI'ın her sayfada açıklayan ve taslak hazırlayan yardımcı olduğu, kritik yazmaları ise yetki–önizleme–onay–audit zinciriyle korunan tek kurumsal işletim sistemidir.

Bağlayıcı ana kararlar:

- Göymen ERP yeni ve bağımsız bir üründür. StokERP yalnızca donör iş bilgisi ve geçiş/mutabakat kaynağıdır.
- Bütün modüller aynı ana kabuk, sayfa anatomisi, etkileşim sözlüğü ve tasarım tokenlarını kullanır.
- AI her sayfada vardır; fakat ERP servislerini, yetki motorunu, onayı veya deterministik formülleri atlayamaz.
- Bir fiziksel/ticari olay bir kez kaydedilir; farklı modüller aynı olayı kendi bağlamında gösterir. Kopya cari, kopya fatura, kopya stok ve kopya finans hareketi üretilmez.
- Sipariş, tahsis, stok hareketi, üretim, sevkiyat, irsaliye, fatura, açık kalem ve para hareketi birbirinden ayrı fakat izlenebilir kayıtlardır.
- Kolaylık "az işlev" demek değildir. Yoğun işlevler sade/ayrıntılı görünüm, bağlamsal aksiyon ve görev merkezleriyle kademeli sunulur.
- Silme yerine durum değişikliği, iptal ve ters kayıt kullanılır. Kritik değişiklikler sebep, önizleme, onay ve audit gerektirir.
- İlk WhatsApp kapsamı entegrasyon veya otomatik gönderim değildir; kullanıcı `Paylaş` düğmesiyle işletim sistemi/tarayıcı paylaşım penceresini açar, hedef olarak WhatsApp'ı kendisi seçer.
- Sistem tarafından üretilen PDF ve indirilen dosya adları Türkçe karakterleri korur; kullanıcıya gösterilen ad ile değişmez teknik dosya kimliği birbirinden ayrıdır.
- AI kişi/rol bazlı yetki taslağı ve etki farkı hazırlayabilir; yetkiyi kendisi veremez, onaylayamaz veya görev ayrılığını aşamaz.

## 1. Belgenin okuma ve karar kuralları

Bu raporda dört durum kullanılır:

| Durum | Anlam |
|---|---|
| **Kilitli** | Arşivlerde son kullanıcı kararıyla kesinleşmiştir; ancak yeni bir kullanıcı kararıyla değişir. |
| **Tasarım standardı** | Uzmanlık alanları birleştirilerek bu raporda ortaklaştırılmıştır. |
| **Politika kararı gerekli** | İşletmeye ait sayısal veya operasyonel tercih eksiktir; kodlamadan önce onay gerekir. |
| **Genişleme kapsamı** | "Şirketin bütün yükünü alma" hedefi için gereklidir, fakat mevcut altı sohbet bu alanı ayrıntılı tasarlamamıştır. |

Karar önceliği: son kullanıcı talebi → nihai karar belgesi → değiştirilen kararın son hali → çözülmüş çelişki → son spesifikasyon → eski prototip → StokERP referansı.

## 2. Ürün vizyonu ve başarı ölçütü

### 2.1 Kullanıcı için

Kullanıcı uygulamayı açtığında "hangi menüye girmeliyim?" diye düşünmemelidir. Rolüne göre bekleyen işleri, istisnaları ve günün ana aksiyonlarını görmelidir. Bir kayıttan ilişkili sipariş, tahsis, üretim, sevkiyat, fatura ve finans etkisine geri dönüşsüz kaybolmadan geçebilmelidir.

Başarı ölçütleri:

- Sık işlemlerin varsayılan sade görünümde tamamlanabilmesi.
- Her sayfada tek baskın ana aksiyon.
- Kullanıcının ilk 5 saniyede "ne oldu, ne bekliyor, ne yapmalıyım?" sorularına cevap alabilmesi.
- Aynı kavramın bütün modüllerde aynı ad, renk ve davranışla sunulması.
- Hatalı işlemin kayıttan önce önlenmesi; kayıt sonrasında kaynağıyla geri izlenebilmesi.
- Masaüstünde yoğun iş, tablette saha kontrolü, mobilde barkod/fotoğraf/onay/görev kullanımı.

### 2.2 Yönetim için

- Satış, tedarik, stok, üretim, sevkiyat ve nakdin aynı gerçeklik üzerinden görülmesi.
- KPI'ın yalnızca sonuç değil, altındaki işlem zincirine inebilmesi.
- Risk, gecikme, nakit açığı, kapasite ve kalite istisnalarının erken uyarısı.
- Kim, ne zaman, hangi veriyi görerek, hangi kararla işlem yaptı sorusunun cevaplanabilmesi.

### 2.3 Teknik ekip için

- Tek uygulama deneyimi; kod içinde sınırları belli modüller.
- Kuralların ekran kodundan ayrı, test edilebilir servislerde bulunması.
- Formül, birim, yuvarlama, tolerans ve sürüm metadata'sının merkezi olması.
- API, olay, AI tool şeması ve onay sözleşmelerinin sürümlenmesi.
- Tekrarlanabilir test/staging/production yayını ve geri dönüş.

## 3. Göymen ERP tasarım anayasası

### 3.1 Değişmez iş ilkeleri

1. **Tek gerçeklik:** Her olayın tek kaynak kaydı ve izlenebilir türevleri vardır.
2. **Durum ile etki ayrı:** "Onaylı", "sevk edildi", "faturalandı", "tahsil edildi" aynı durum değildir.
3. **Plan ile gerçek ayrı:** Sipariş, tahsis, üretim planı, fiilî üretim, yükleme ve para hareketi karıştırılmaz.
4. **Sahiplik ile konum ayrı:** Malın kime ait olduğu, kimin sorumluluğunda bulunduğu ve fiziksel olarak nerede olduğu ayrı eksenlerdir.
5. **Belge ile ticari etki ayrı:** Belge yaşam döngüsü korunur; stok/cari/muhasebe etkisi belge durumuna göre oluşur.
6. **Kaynak ile dağıtım ayrı:** Para girişi/çıkışı bir kayıt, hangi açık kalemlere uygulandığı ayrı bir ilişkidir.
7. **Silme yok:** Kritik ticari, stok ve finans kayıtları ters kayıt/iptal ve sebep ile düzeltilir.
8. **AI hesap motoru değil:** Resmî tutar, stok, fiyat, risk ve üretim dengesi deterministik servislerce hesaplanır.
9. **AI yetki kazanmaz:** AI, kullanıcının göremediği veriyi göremez ve yapamadığı işlemi yapamaz.
10. **Önizleme ve onay:** Yüksek etkili yazmalar, kullanıcıya oluşacak etkileri gösterdikten sonra uygulanır.
11. **Sürüm:** Fiyat, paket, reçete, formül, tolerans, belge ve AI tool sözleşmeleri tarihsel olarak sürümlenir.
12. **Çok şirket:** Hukuki taraf ortak olabilir; cari hesap, yetki, numaralandırma, finans ve muhasebe etkisi şirket kapsamındadır.

### 3.2 Kullanıcı kolaylığı ilkeleri

- Varsayılan görünüm sade; ayrıntı ihtiyaç halinde açılır.
- Liste, detay, oluşturma/düzenleme, karşılaştırma ve onay sayfaları aynı kalıbı kullanır.
- Uzun menü yerine görev ve bağlama göre aksiyon kataloğu kullanılır.
- Bir sayfa birden fazla görevi destekleyebilir; fakat aynı anda bir baskın ana işlem gösterir.
- Durum yalnızca renkle anlatılmaz; metin, ikon ve gerektiğinde açıklama birlikte kullanılır.
- Kullanıcı kaydetmeden önce finansal, stok ve bağlı kayıt etkisini görür.
- Hata mesajı "işlem başarısız" demez; sorun, neden ve düzeltme aksiyonunu söyler.

## 4. Hedef ana menü ve görev merkezleri

Ana menü modül sorumluluklarına göre kurulmalı; her nadir işlem ayrı menü maddesi olmamalıdır.

| Sıra | Ana modül | Kullanıcıya görünen ana alan | Sorumluluk |
|---:|---|---|---|
| 1 | Ana Sayfa | Rol bazlı dashboard | Özet, alarm, KPI, hızlı aksiyon |
| 2 | Çalışma Merkezi | Benim İşlerim | Görev, onay, istisna, söz verilen iş |
| 3 | Satış | Talepler/Siparişler; İrsaliye & Fatura | Talep, teklif/sipariş, sevkiyat ve satış belgesi |
| 4 | Satın Alma | Siparişler; İrsaliye & Fatura | Tedarik, termin, mal/hizmet kabul ve alış belgesi |
| 5 | Ürün & Hizmet | Ürünler; Hizmetler; Fiyatlar | Ticari katalog, paket/birim ve fiyat masterı |
| 6 | İşletme Alımları | Sarf, bakım, yedek parça, genel alım | Ticari ürün kataloğuna karışmayan işletme ihtiyaçları |
| 7 | Cari | Cari Kartlar; Cari Hesap Takibi | Hukuki taraf, cari 360, ilişki ve ticari risk |
| 8 | Stok & Tahsis | Stok/Malzeme Pozisyonu; Tahsisler | Sahiplik, konum, miktar, rezerv/tahsis ve izlenebilirlik |
| 9 | Üretim | Kontrol Merkezi; Planlar; Gerçekleştirme | Beş üretim motoru, kapasite, fiilî tüketim/çıktı |
| 10 | Lojistik & Kantar | Yükleme; Sevkiyat; Tartım; Mal Kabul | Fiziksel hareket planı ve gerçekleşme |
| 11 | Belgeler | Merkezi Belge Arşivi | Tek dosya, çok kayıt referansı, sürüm ve AI karşılaştırma |
| 12 | Muhasebe | Kontrol; Belgeler; Cari Hesaplar; Fiş; Hesap Planı; Mizan; Dönem/Vergi | Muhasebesel sınıflama ve dönem kontrolü |
| 13 | Finans & Hazine | 13 ana ekran ailesi | Para, açık kalem dağıtımı, banka, nakit, kredi, teminat, bütçe |
| 14 | Raporlar | Kurumsal Rapor Merkezi | Modüller arası ortak analiz ve dışa aktarım |
| 15 | AI Merkezi | Asistan, öneri, günlük özet, geri bildirim | Sayfa AI'larının koordinasyonu ve yönetim özeti |
| 16 | Ana Veriler | Şirket, birim, kur, depo, kalite, parametre | Kontrollü master veri |
| 17 | Sistem Yönetimi | Kullanıcı, rol, onay, entegrasyon, log | Güvenlik ve operasyon yönetimi |

Menüde ekran sayısı değil, iş sorumluluğu gösterilir. Örneğin Finans'taki 73 işlem çalışma alanı 73 menü maddesi değil; bağlamsal "Yeni Finans İşlemi" kataloğudur.

## 5. Ortak ana şablon

### 5.1 Uygulama kabuğu

Her sayfa aşağıdaki sabit kabuğu kullanır:

- **Sol navigasyon:** Ana modüller, daraltma, favoriler ve rol bazlı sayaçlar.
- **Global üst bar:** Şirket, dönem, global arama/komut, bildirim, yardım ve kullanıcı.
- **Ekmek kırıntısı:** Kayıt bağlamını kaybetmeden geri dönüş.
- **Sayfa başlığı:** Başlık, kapsam, durum, son güncelleme ve tek ana aksiyon.
- **Özet şeridi:** 3–6 karar KPI'ı; dekoratif sayaç değil.
- **Görünüm/sekme satırı:** Durum veya iş evresine göre az sayıda sekme.
- **Filtre ve arama:** Kaydedilebilir görünüm, açılır gelişmiş filtre ve aktif filtre çipleri.
- **Ana çalışma yüzeyi:** Liste, kart, zaman çizelgesi veya işlem formu.
- **Sağ bağlam paneli:** Kayıt özeti veya AI; kullanıcı mod değiştirir.
- **Sabit alt işlem çubuğu:** Kaydet, kontrol, onaya gönder, vazgeç; uzun formda kaybolmaz.
- **Durum katmanı:** Yükleniyor, boş, kısmi hata, yetkisiz, eşzamanlı değişiklik ve başarı durumları.

### 5.2 Sayfa türleri

| Sayfa türü | Kullanım |
|---|---|
| Liste/Görev Merkezi | Arama, filtreleme, toplu seçim ve iş kuyruğu |
| 360 Detay | Bir ana kaydın özet ve ilişkili bütün bağlamları |
| Tam ekran workspace | Çok adımlı, yüksek etkili veya çok tablolu işlem |
| Modal | Kısa, geri alınabilir, 3–5 alanlı işlem |
| Drawer | Hızlı detay, açıklama, özet veya AI |
| Karşılaştırma | Mükerrer, revizyon, belge farkı veya plan–gerçek |
| Kontrol/Onay | Etki önizlemesi, istisna, yorum, kabul/red |

Modal içine uzun sihirbaz konmaz. Kullanıcının başka kayıtlara bakması, dosya eklemesi, hesap sonucu görmesi veya onay alması gerekiyorsa tam ekran workspace kullanılır.

### 5.3 Liste ve tablo standardı

- Sunucu tarafı sayfalama, sıralama ve filtreleme.
- Sabit kimlik/ana kolon; yatay kaydırmada kaybolmaz.
- Kolon göster/gizle, sırala ve kullanıcı görünümü kaydet.
- Satır genişletme, yalnızca özet ilişki için; tam işlem ayrı workspace'te.
- Toplu işlem çubuğu yalnızca satır seçildiğinde açılır.
- Tutar, miktar ve tarihler sağa hizalı; para birimi ve birim ayrı okunabilir.
- Durum çipi metin+renk+ikon taşır.
- Dışa aktarım aynı filtre ve kolon sırasını korur; arka planda büyük iş olarak çalışabilir.
- `Paylaş` eylemi kullanıcı tarafından başlatılır. Desteklenen cihaz ve tarayıcıda sistem paylaşım penceresi açılır; destek yoksa dosya indirilir ve paylaşım metni panoya hazırlanır.
- WhatsApp ilk kapsamda doğrudan ERP entegrasyonu değildir. ERP alıcıya kendi kendine mesaj göndermez, WhatsApp Business hesabı istemez ve gönderilmiş varsayımı üretmez.

### 5.4 Form standardı

- En sık alanlar ilk bölümde; ileri düzey ve nadir alanlar açılır bölümde.
- Bir alanın yanında neden gerekli olduğu ve veriyi nereden aldığı açıklanabilir.
- Para, birim, tarih, kur ve vergi alanları tek bir genel sayı alanında birleştirilmez.
- Otomatik hesaplanan alan düzenlenebilir görünmez; formül ve girdi köprüsü açılabilir.
- Kaydetme öncesi iş kuralı kontrolü ve etki önizlemesi ayrı aşamalardır.
- Taslak, onaya gönderme ve kesinleştirme durumları ayrıdır.

### 5.5 Responsive ve erişilebilirlik

- 1600 piksel altında sağ panel varsayılan olarak drawer'a dönüşür.
- Tablet, inceleme/onay ve saha kontrolüne odaklanır.
- Mobil, tam masaüstü tablosunu küçültmez; kart/liste, barkod, fotoğraf, adet/KG, onay ve görev akışı sunar.
- Klavye ile tam gezinme, görünür odak, ekran okuyucu etiketleri, yeterli kontrast ve hareket azaltma desteği zorunludur.
- Modal odak tuzağı, `Escape`, arka planın etkisizleştirilmesi ve doğru z-index uygulama kabul kriteridir.

## 6. AI: her sayfada, fakat ERP'nin üstünde değil

### 6.1 AI yerleşimi

AI üç seviyede çalışır:

1. **Sayfa içi mikro AI:** Alan açıklama, mükerrer uyarısı, belge okuma, özet ve taslak.
2. **Sağ bağlamsal AI paneli:** O anki kaydı, kullanıcının yetkili olduğu ilişkilerle birlikte açıklar ve işlem taslağı hazırlar.
3. **AI Merkezi/Yönetim AI:** Modüller arası günlük özet, risk, gecikme, nakit ve aksiyon listesi.

### 6.2 AI yetenek seviyeleri

| Seviye | Örnek | Kural |
|---|---|---|
| Oku/Açıkla | "Bu sipariş neden riskte?" | Yetkili ERP verisini özetler, kaynak kayıtlara bağ verir. |
| Öner | Tahsis, termin, nakit veya fiyat uyarısı | Gerekçe, veri tarihi, güven ve alternatif gösterir. |
| Taslak oluştur | Cari, ürün, sipariş, ödeme önerisi | Kayıt oluşturmaz; doğrulanabilir forma doldurur. |
| Önizle | Uygulanacak stok/finans/belge etkisi | Deterministik backend sonucu gösterilir. |
| Onay sonrası uygula | Yetkili kullanıcının kesin komutu | Idempotent ERP komutu, audit ve sonuç kaydı. |
| Yasak | Kendi kendine onay, yetki verme, dönem açma, kritik limit aşma | AI nihai karar mercii değildir. |

### 6.3 AI öneri kartı zorunlu alanları

- Ne tespit edildi?
- Hangi veri ve tarih aralığı kullanıldı?
- Neden önemli?
- Etki ve risk nedir?
- Önerilen aksiyon ve alternatifler nelerdir?
- Kullanıcı hangi yetkiyle uygulayabilir?
- Bu yalnızca öneri mi, deterministik kontrol sonucu mu?

### 6.4 AI veri ve audit sözleşmesi

- AI veritabanına doğrudan bağlanmaz.
- Yalnızca şemalı, sürümlü, yetki ve şirket kapsamı kontrollü ERP araçlarını çağırır.
- Modele gereksiz satır, kişisel veri veya sır gönderilmez.
- Her AI işleminde kullanıcı, sayfa, kayıt, model/prompt/tool sürümü, girdinin veri kapsamı, öneri, onay ve ERP sonucu kaydedilir.
- AI sonucu, dayanak kaydı silinmeden yeniden üretilebilir/açıklanabilir olmalıdır.
- Kullanıcı geri bildirimi kalite setine alınır; model kendi kendine kalıcı iş kuralı değiştirmez.

### 6.5 Modül bazında AI

| Modül | AI yardımı |
|---|---|
| Satış | Talep/sipariş taslağı, risk, teslim tarihi, eksik bilgi, marj ve kapanış özeti |
| Satın Alma | Termin önerisi, tedarikçi geçmişi, fiyat/koşul karşılaştırma, üçlü kontrol açıklaması |
| Ürün & Hizmet | Belgeden kart taslağı, mükerrer, birim/paket uyarısı, fiyat etki analizi |
| Cari | Belgeden cari taslağı, sicil farkı, risk açıklaması, tahsilat görevi |
| Stok & Tahsis | Uygun kaynak önerisi, geciken gelecek tahsis, blokaj ve eksik stok açıklaması |
| Üretim | Kaynak/hat/kuyruk önerisi, plan–gerçek sapması, kalite/denge istisnası |
| Lojistik | Yükleme birleştirme, rota/zaman önerisi, tartım farkı ve eksik belge |
| Muhasebe | Belge sınıflama taslağı, hesap önerisi, dönem kontrolü; fişi kendi onaylamaz |
| Finans | Dağıtım önerisi, nakit açığı, kredi/limit/teminat uyarısı, banka satırı eşleştirme |
| Belgeler | OCR, sınıflandırma, özet, revizyon farkı ve ilgili kayıt önerisi |

## 7. Modüllerin ayrıntılı hedef tasarımı

### 7.1 Ana Sayfa ve Çalışma Merkezi

Ana Sayfa yönetim görünümü, Çalışma Merkezi ise görev görünümüdür. Aynı şeyin iki dashboard kopyası olmamalıdır.

**Ana Sayfa:**

- Rol ve şirket kapsamına göre satış, tedarik, stok, üretim, sevkiyat, finans ve risk özeti.
- KPI'dan kaynak listeye ve tek kayda inme.
- Bugün, bu hafta ve dönem karşılaştırması.
- AI günlük yönetim özeti; her cümlenin dayanak kayıt bağı.
- Kullanıcının favori görünüm ve raporları.

**Çalışma Merkezi:**

- Bana atanan, rol kuyruğu, onay bekleyen, bilgi bekleyen ve geciken işler.
- İstisna önceliği; yalnızca sayı değil, iş etkisi.
- Bir görevin kaynağı, SLA'sı, sorumlusu, gözlemcisi ve tamamlanma kanıtı.
- Tek tıkla ilgili workspace'e geçiş.

### 7.2 Satış

Satışın ana nesnesi sipariştir; yükleme, irsaliye, fatura ve tahsilat siparişin türevi fakat ayrı kayıtlardır.

**Ana alanlar:**

- Satış talepleri/teklif havuzu.
- Satış siparişleri.
- Satış > İrsaliye & Fatura.
- Kayıp/fırsat analizi ve satış raporları.

**Sipariş detay sekmeleri — kilitli:**

1. Satırlar.
2. Tahsis/Kaynak.
3. Gerçekleşme/İrsaliye.
4. Faturalar.
5. Sözleşme/Belgeler.
6. Revizyon/Geçmiş.

**Sipariş kuralları:**

- Üst bilgi; şirket, cari, para birimi, fiyat/iskonto koşulu, vade, teslim adresi, satış sorumlusu ve ticari durum taşır.
- Satır; ürün/hizmet, miktar, sipariş birimi, fiyat birimi, paket, termin dilimleri ve kaynak stratejisi taşır.
- Sipariş miktarı, net aktif miktar, tahsis edilen, gerçekleşen, faturalanan ve kalan ayrı izlenir.
- Müşteri onayı, iç onay, operasyon durumu, sevkiyat ve ticari kapanış tek durum alanında birleşmez.
- Revizyon, eski sürümü yok etmez; miktar/fiyat/termin etkisi ve onay izi korunur.
- Kısmi sevkiyat, çok siparişli yükleme ve bir irsaliyede birden fazla uyumlu sipariş desteklenir.
- Siparişin operasyonel kapanışı ile finansal/ticari kapanışı ayrıdır.

**Satış > İrsaliye & Fatura:**

- Sevkiyat/İrsaliyeler.
- Faturaya Hazır.
- Faturalar.
- Kontrol Merkezi.
- İade/Düzeltme bir yaşam döngüsü görünümüdür; ayrı sol menü değildir.

### 7.3 Satın Alma

**Ana alanlar:**

- Satın alma talepleri ve siparişleri.
- Tedarik termin dilimleri.
- Tedarikçi performansı.
- Satın Alma > İrsaliye & Fatura.

**Kurallar:**

- Sipariş, kabul, gelen fatura ve ödeme planı ayrı kayıtlardır.
- "Yolda" tek bir metin değil; termin, sevk bildirimi, taşıma ve beklenen kabul miktarıyla izlenen durumlardır.
- Miktar bazlı birden fazla termin dilimi desteklenir.
- Mal kabul; sipariş, irsaliye, tartım, kalite ve fiilî miktarı karşılaştırır.
- Hizmet kabulü fiziksel stok hareketi yaratmaz; kabul kanıtı ve faturaya hazırlık oluşturur.
- Red/İade/Düzeltme kaynak belge ve miktar ilişkisi olarak saklanır.

**Satın Alma > İrsaliye & Fatura:**

- Mal/Hizmet Kabulü.
- Faturaya Hazır.
- Gelen Faturalar.
- Üçlü Kontrol: sipariş ↔ kabul/irsaliye ↔ fatura.
- Red/İade/Düzeltme durum görünümü.

### 7.4 Ürün & Hizmet

**Kilitli menü:** `Ürünler | Hizmetler | Fiyatlar`.

Bu modül yalnızca ticari olarak satılan/alınan ürün ve hizmetleri yönetir. Sarf, bakım, yedek parça, genel gider ve sabit kıymet alımları İşletme Alımları kapsamındadır.

**Veri katmanları:**

1. Grup/şablon: ortak teknik nitelikler ve form davranışı.
2. Ürün kartı: ticari ve teknik master.
3. Etiket/lot/parti: gerçek ısı, bobin, MTC, sahiplik, konum ve fiilî miktar.

**Ürün rolleri:** satın alınabilir, satılabilir, üretim girdisi, üretilen çıktı. "Üretilebilir" ve "girdi" gibi bazı roller, aktif reçete/üretim ilişkisinden türetilir; kullanıcının gerçekle çelişen serbest işaretler koyması engellenir.

**Paket standardı:**

- Paket adedi/ağırlığı yardımcı not değil, merkezi ve sürümlü standarttır.
- Geçerlilik tarihi ve onayı vardır.
- Sipariş/üretim kaydı, kullandığı sürümün snapshot'ını tutar.
- Sonradan paket standardı değişse eski işlemin anlamı değişmez.

**Birimler:** stok, alış, satış ve fiyat birimleri ayrıdır. Sabit, formüle bağlı ve işlemde değişken dönüşümler desteklenir. Teorik ve fiilî KG/miktar ayrı saklanır.

**Fiyatlar:**

- Liste fiyatı − iskonto = net fiyat; hesap zinciri gösterilebilir.
- Fiyat birimi sipariş biriminden bağımsızdır.
- Şirket/grup, cari/grup, ürün/grup, döviz, fiyat birimi, vergi, tarih aralığı, miktar ve öncelik sırası taşır.
- Gelecek fiyatlar taslak → onaylı → planlı → aktif → süresi dolmuş yaşam döngüsündedir.
- Toplu güncelleme önce simülasyon/etki, sonra onay ve yayınlama yapar.
- Minimum/maksimum fiyat ve iskonto limitleri rol ve tarihe göre uygulanır.

### 7.5 Cari

**Kilitli ana menü:** `Cari Kartlar | Cari Hesap Takibi`.

**Cari 360 sekmeleri:**

1. Özet.
2. Cari Bilgileri.
3. Siparişler.
4. Cari Hesap: Genel, Ekstre, Açık Kalemler, Risk/Limit/Teminat, Mutabakat.
5. Stok & Tahsis.
6. Belgeler & Sicil.

**Veri modeli:**

- `Taraf`, hukuki kimliktir; vergi/kimlik, unvan ve resmi kayıtları taşır.
- `CariHesap`, belirli Göymen şirketiyle müşteri/tedarikçi/fason/hizmet ilişkisidir.
- Bir taraf çok rol ve çok şirket cari hesabı taşıyabilir.
- Müşteri ve tedarikçi hesapları ayrıdır; otomatik mahsup yapılmaz. Birleşik görünüm karşılaştırma içindir.
- Para birimi defterleri ayrı izlenir; belirsiz tek TL bakiyesi üretilmez.
- Cari hareket kaynağı fatura, tahsilat/ödeme, avans, iade, fiyat/kur farkı ve ters kayıt olabilir. Sipariş ve irsaliye, kesin cari hareket değil; risk ve ticari görünüm girdisidir.

**Risk:**

`Brüt Risk = Faturalı Açık Alacak + Faturalanmamış Sevkiyat + Sevk Edilmemiş Onaylı Sipariş Kalanı`

`Net Risk = Brüt Risk − Kullanılabilir DBS − Riskte Dikkate Alınan Geçerli Teminat`

Aynı sipariş miktarı sevk edildiğinde sipariş riskinden çıkar, faturalanmamış sevkiyata geçer; faturalandığında açık alacağa geçer. Bu geçişlerde iki kez sayım yapılmaz.

Sicil ve hukuki kimlik değişikliği otomatik uygulanmaz; eski/yeni karşılaştırma, kaynak, sebep, yetki ve onay gerektirir.

### 7.6 Stok & Tahsis

Stok ekranının adı ve anlamı `Stok / Malzeme Pozisyonu`dur. "Kendi Stok", "Müşteri Malı", "Dış Fason" ayrı veri kaynakları değil; aynı satır modelinin kaydedilebilir görünümleridir.

**Dört zorunlu eksen:**

- Mülkiyet: mal kime ait?
- Sorumluluk/gözetim: mal kimin operasyonunda?
- Fiziksel konum: depo/lokasyon/saha/harici konum neresi?
- Ticari bağ: hangi cari, sipariş, hizmet veya üretim bağlamına ait?

**Miktar kartları:** fiziki, aktif satış tahsisi, üretim girdi tahsisi, yükleme için tutulan, kalite/sistem blokajı, serbest fiziki, gelecek arz ve projeksiyon.

`Serbest Fiziki = Fiziki Mevcut − Aktif Satış Tahsisleri − Üretim Girdi Tahsisleri − Yükleme İçin Tutulan − Kalite Blokları − Sistem/Hukuki Bloklar`

Tahsis stok hareketi değildir. Ayrı merkezi motor; stok, gelecek alım, üretim planı ve manuel kaynak ile sipariş/üretim ihtiyacı arasında çoktan çoğa ilişki kurar. Kaynak fiziki hale geldiğinde gelecek tahsis kontrollü biçimde fiziksel tahsise dönüşür.

Tahsis motoru atomik, eşzamanlılık kontrollü ve idempotent olmalıdır. Aynı stok iki kullanıcı tarafından aynı anda aşırı tahsis edilemez.

### 7.7 Üretim

Beş üretim motoru ortak planlama kabuğunu kullanır:

1. Boy kesme.
2. Dilme.
3. Boru/Profil.
4. Boyama.
5. Soğuk hadde.

**Ortak plan akışı:** bağlam/hedef → hammadde kaynakları → operasyon hesabı → planlanan çıktılar → denge/uyarı → kapasite/onay.

**Üretim Kontrol Merkezi:**

- Aktif kuyruk, sıra, hat/makine, kalıp/takım, vardiya ve tahmini başlangıç/bitiş.
- Kurulum, bakım, duruş, kapasite ve öncelik etkisi.
- Gantt/zaman çizelgesi ile liste aynı veri kaynağını kullanır.
- AI kuyruk önerisi yapar; planlayıcı kararı olmadan sıra değiştirmez.

**Gerçekleştirme modları:** Hızlı Tamamla, Fiilî Değerleri Düzenle, İstisna/Kalite Kaydı.

**Kurallar:**

- Plan fiziksel stok hareketi yaratmaz; tahsis yaratabilir.
- Kısmi hammaddeyle başlatma yalnızca politika eşiği ve asgari başlangıç partisi sağlanırsa mümkündür.
- Plan devam ederken hammadde ekleme/değiştirme olay, sebep ve yeniden denge ile kaydedilir.
- Nominal ticari kalınlık ürün kartında; fiilî girdi kalınlığı lot/etiket ve soy ağacındadır.
- Reçete, tolerans, formül ve paket sürümü plan kaydına snapshot olur.
- İkinci kalite, fire ve geri kazanılabilir artık aynı şey değildir.
- Kütle/miktar dengesi tamamlamadan önce gösterilir; tolerans dışı durum onay/istisna ister.

**İzlenebilirlik:** Her girdi–çıktı ilişkisi olay tabanlı ve çoktan çoğa kaydedilir. Etiket/lot soy ağacında ne, ne zaman, nerede, neden, kim ve hangi bağlam soruları cevaplanır.

### 7.8 Lojistik, Yükleme, Kantar ve Mal Kabul

Yükleme talimatı plandır; fiilî yükleme gerçekleşmedir. Talimat oluşturmak stok düşmez; gerekli miktarı yükleme için tutabilir.

**Akış:** sevk edilebilir sipariş → yükleme talimatı → araç/sürücü/nokta → fiilî etiket/miktar → kantar → fark kontrolü → irsaliye taslağı → onay/kesim.

- Bir yükleme birden fazla sipariş ve yükleme noktası taşıyabilir.
- Bir irsaliye birden fazla uyumlu siparişi kapsayabilir.
- Plan, fiilî yük, teorik ağırlık ve kantar farkı ayrıdır.
- Dış fasona gidiş/dönüş mülkiyeti değiştirmez; konum ve sorumluluğu değiştirir.
- Depo transferi iki bacaklı olabilir: çıkış/yolda ve hedef kabul. Kayıp, hasar ve kısmi kabul izlenir.
- Genel bağlamsız "Yeni İrsaliye" ekranı yoktur; işlem sipariş, yükleme, transfer veya kabul bağlamından açılır.

### 7.9 Belgeler ve dijital arşiv

Bir fiziksel dosya tek kez saklanır, çok sayıda ERP kaydına referanslanabilir. Sipariş, cari, ürün, sevkiyat, irsaliye, fatura, finans ve üretim aynı dosyanın farklı kopyalarını oluşturmaz.

Zorunlu metadata: dosya hash'i, tür, sürüm, kaynak, yükleyen, tarih, gizlilik, şirket, bağlı kayıtlar, geçerlilik ve onay. Revize dosya yeni sürümdür; eskisi sessizce üzerine yazılmaz. AI iki sürümü karşılaştırabilir, fakat değişikliği kendi uygulamaz.

**Mantıksal arşiv görünümü:** `Şirket → Yıl → Modül → Belge Türü → Cari/Kayıt`. Bu görünüm kullanıcı kolaylığı içindir; fiziksel saklama yolu klasör adına bağımlı değildir. Dosya nesne deposunda değişmez teknik kimlik/hash ile, bütün sınıflama ve kayıt bağları veritabanında tutulur. Aynı dosya farklı sanal klasör ve ERP kayıtlarında kopyalanmadan görünebilir.

**Türkçe dosya adı standardı:** Kullanıcıya sunulan PDF/dosya adı Unicode NFC ve UTF-8 uyumlu üretilir; `İ ı Ş ş Ğ ğ Ü ü Ö ö Ç ç` karakterleri korunur. Önerilen kalıp:

`{Belge Türü}_{Belge No}_{Cari Kısa Ad}_{YYYY-AA-GG}_v{NN}_{Durum}.pdf`

Örnek: `Satış_Sipariş_Sözleşmesi_SPS-2026-01842_ABC_Demir_2026-08-25_v01_Onaylı.pdf`

İşletim sistemlerinde yasak olan `<>:"/\\|?*`, kontrol karakterleri ve sondaki nokta/boşluk güvenli ayraçla değiştirilir. Dosya adı kayıt kimliği değildir; yeniden adlandırma belge bağını bozmaz. Harici bir kanal Türkçe karakter desteklemiyorsa yalnız o kanal için ASCII güvenli teknik ad üretilebilir, ERP'deki asıl görünen ad değiştirilmez. PDF fontları gömülü olmalı ve Türkçe karakter altın testi geçmelidir.

**Çıktı ve paylaşım:** Belge/rapor için PDF, düzenlenebilir tablo için XLSX, veri alışverişi için gerektiğinde CSV/XML/JSON sağlanır. `Paylaş` düğmesi ilk aşamada tarayıcı/işletim sistemi paylaşım seçicisini açar; kullanıcı WhatsApp, e-posta veya başka hedefi seçer. Dosya paylaşımı desteklenmiyorsa güvenli geri dönüş `PDF'yi indir + açıklama metnini kopyala`dır. Paylaşım öncesi belge sürümü, gizlilik ve dışa aktarma yetkisi kontrol edilir.

**İletişim şablonları:** E-posta için `Kime/CC/Konu/Gövde/Ek` taslağı AI yardımıyla hazırlanabilir ve yetkili kullanıcı onayıyla gönderilebilir. WhatsApp ilk fazda yalnız paylaşım metni ve seçili PDF'yi kullanıcıya hazırlar; ERP WhatsApp'a otomatik gönderim yapmaz. Şablonlar şirket, belge türü, cari grubu, dil ve kullanıcı rolüne göre sürümlenir.

### 7.10 Muhasebe

Muhasebe ve Finans ayrı ana modüllerdir.

**Kilitli Muhasebe menüsü:**

1. Kontrol Merkezi.
2. Faturalar & Belgeler.
3. Cari Hesaplar.
4. Muhasebe Fişleri.
5. Hesap Planı.
6. Mizan.
7. Dönem & Vergi.

Muhasebe, Satış/Alış işlem ekranını kopyalamaz. Operasyon modülü belgenin oluşumunu ve ticari akışı; Muhasebe doğruluk, hesap eşlemesi, fiş, dönem ve raporlamayı yönetir.

`Muhasebe > Cari Hesaplar`, yeni cari master oluşturmaz. Aynı cari hesabın muhasebe ekstresi, ticari ekstresi, açık kalemleri, fatura, tahsilat/ödeme, mutabakat ve muhasebe bağlantısını gösterir.

Orijinal para birimi ve yerel para karşılığı ayrı saklanır. Açık kalem motoru ekstre satırlarından ayrıdır. Resmî e-fatura/e-arşiv ilk fazda dış sistemde kalabilir; ERP taslak, kontrol, dışa aktarım, durum ve etkisini yönetir.

### 7.11 Finans & Hazine

Finans v3'ün kapsamı korunur; v4'ün sade, görev ve AI odaklı sunumu kullanılır.

**13 ana ekran ailesi:**

1. Finans Genel Bakış.
2. Tahsilat/Ödeme.
3. Açık Kalemler ve Finansal Dağıtım.
4. Tahsilat Takibi ve Cari Risk.
5. Banka/Kasa.
6. Ödeme Yönetimi.
7. Ödeme Araçları.
8. 13 Haftalık Nakit Planı.
9. Krediler ve Limitler.
10. Teminatlar ve Yükümlülükler.
11. Nakit Bütçesi.
12. Finans Kontrol Merkezi.
13. Finans Raporları.

**Kaynak ve dağıtım:**

- Banka/kasa/çek/senet/POS/şirket kartı/DBS/avans/mahsup olayı bir finansal kaynaktır.
- Kaynağın fatura, sipariş avansı veya cari açık kalemlere uygulanması ayrı çoktan çoğa dağıtımdır.
- Dağıtım ikinci bir banka, kasa veya cari para hareketi yaratmaz.
- Ticari kapanış, nakit gerçekleşmesi ve risk etkisi ayrıdır.
- Vade, fiilî ödeme tarihi, valör ve banka mutabakat tarihi ayrıdır.

**Araç kuralları:**

- Çek/senetin ödeme aracı ve teminat amacı ayrıdır.
- POS'ta brüt tutar müşteri borcunu kapatır; banka neti ve komisyon ayrı kayıttır.
- Tedarikçi POS yoktur.
- DBS kabulü teminat/karşılama bilgisidir; gerçek banka tahsilatı gelmeden cari kapanmaz.
- Banka mutabakatı 1:1, 1:n, n:1 ve n:n eşleştirmeyi destekler.
- Tanımsız banka satırı banka bakiyesini etkileyebilir; cari hesabı kaynak/eşleştirme olmadan etkilemez.

**Ödeme yönetimi:** teklif → nakit/bütçe kontrolü → banka → kontrol/onay → talimat → banka sonucu → mutabakat → dağıtım.

**Hazine:** 13 haftalık plan ilk 30 gün günlük, sonrası haftalık görünür. Planlanan ve gerçek kaynağı aynı nakit olayı olarak iki kez saymaz. Kredi imkânı ile kullanım (drawdown), limit ile kullanılabilir limit, teminat ile tahsis, covenant ile görev ayrıdır.

**Kontrol:** görevler ayrılığı, ikinci onay, dönem kilidi, ters kayıt, bütçe kontrolü ve grup içi işlem izleri zorunludur.

### 7.12 Raporlar

Rapor Merkezi, modül ekranlarının kopyası değil; şirket, dönem ve veri yetkisiyle yönetilen semantik analiz katmanıdır.

- Satış: sipariş, dönüşüm, marj, termin, kayıp.
- Satın alma: termin, fiyat, kabul, kalite, tedarikçi performansı.
- Stok: pozisyon, yaş, devir, tahsis, blokaj, müşteri malı.
- Üretim: kapasite, OEE için gerekli olaylar, plan–gerçek, fire, kalite, soy ağacı.
- Lojistik: yükleme, tartım, teslim ve fark.
- Cari: risk, yaşlandırma, tahsilat performansı, mutabakat.
- Finans: nakit, banka, açık kalem, kredi, limit, teminat, bütçe.
- Kurumsal: siparişten nakde, satın almadan ödemeye, stoktan kârlılığa zincir raporları.

Her rapor veri tarihi, para birimi/kur, filtre, sürüm ve kaynak tanımını gösterir.

## 8. Uçtan uca ana işlem zincirleri

### 8.1 Siparişten nakde

`Talep/Teklif → Satış Siparişi → Risk/Onay → Kaynak Stratejisi → Tahsis → Satın Alma veya Üretim → Yükleme → Fiilî Sevkiyat → İrsaliye → Fatura → Cari Açık Kalem → Tahsilat Kaynağı → Dağıtım → Banka Mutabakatı → Ticari/Finansal Kapanış`

Kontrol noktaları:

- Cari/limit/risk onayı.
- Fiyat/iskonto ve marj yetkisi.
- Termin ve kaynak uygunluğu.
- Tahsis yarış ve toplam kontrolü.
- Plan–fiilî yük–kantar farkı.
- Faturaya hazır uyumluluk grubu.
- Açık kalem ve para kaynağı dağıtımı.

### 8.2 Satın almadan ödemeye

`Talep → Onay → Satın Alma Siparişi → Termin/Sevk Bildirimi → Mal/Hizmet Kabulü → Üçlü Kontrol → Gelen Fatura → Cari Açık Kalem → Ödeme Önerisi → Bütçe/Nakit/Onay → Banka Talimatı → Banka Sonucu → Mutabakat → Dağıtım`

### 8.3 Hammaddeden izlenebilir ürüne

`Stok/Etiket → Üretim Girdi Tahsisi → Plan → Kuyruk → Fiilî Tüketim → Çıktı/Fire/İkinci Kalite → Etiketler → Soy Ağacı → Kalite Serbest Bırakma → Satış Tahsisi → Yükleme`

### 8.4 Belgeden kayda

`Dosya/E-posta/Tarama → Hash/OCR/Sınıflandırma → AI Taslağı → Mükerrer ve Ana Veri Kontrolü → Kullanıcı Doğrulaması → ERP Kaydı → Belge Referansı → Onay/Audit`

### 8.5 Sicil değişikliğinden cari güncellemeye

`Kaynak Tarama → Taraf Eşleştirme → Eski/Yeni Fark → AI Açıklaması → Veri Sorumlusu İncelemesi → Onay → Tarihçeli Master Değişikliği → Bağlı Risk/Belge Görevi`

## 9. Ortak veri modeli ve tek gerçeklik sahipliği

### 9.1 Ana nesneler

| Alan | Ana nesneler |
|---|---|
| Organizasyon | Şirket, Şube, iş yeri, dönem, kullanıcı, rol, ekip |
| Cari | Taraf, CariHesap, rol, adres, kişi, banka hesabı, limit, teminat, sicil kaydı |
| Ürün | Ürün, Hizmet, grup/şablon, nitelik, birim, dönüşüm, paket sürümü, fiyat sürümü |
| Satış | Talep, teklif, satış siparişi, satır, termin, revizyon, onay |
| Satın alma | Talep, sipariş, satır, termin, kabul, tedarikçi performansı |
| Stok | Stok pozisyonu, stok olayı, depo, lokasyon, sahiplik, blokaj, tahsis |
| İzlenebilirlik | Etiket, lot/parti, heat/coil/MTC, soy ağacı olayı, kalite durumu |
| Üretim | Reçete sürümü, formül/tolerans, plan, operasyon, kaynak, kuyruk, gerçekleştirme |
| Lojistik | Yükleme talimatı, araç, sürücü, durak, fiilî yük, tartım, transfer, sevkiyat |
| Belge | Dosya, hash, sürüm, belge kaydı, kayıt referansı, OCR/AI sonucu |
| Ticari belge | İrsaliye, fatura, iade/düzeltme, e-belge durum ve kaynak ilişkisi |
| Cari/finans | Açık kalem, finansal kaynak, dağıtım, banka satırı, mutabakat, ödeme paketi |
| Hazine | Kasa/banka, çek/senet, POS, DBS, kredi imkânı, kullanım, limit, teminat, bütçe |
| Muhasebe | Hesap planı, fiş, fiş satırı, dönem, vergi kuralı, mizan |
| Yönetişim | Onay, görev, yorum, audit olayı, ters kayıt, entegrasyon mesajı |

### 9.2 Veri sahipliği

- Cari masterın sahibi Cari modülüdür; Muhasebe ve Finans aynı kimliği kullanır.
- Ürün/hizmet masterının sahibi Ürün & Hizmet'tir; stok ve sipariş snapshot/referans kullanır.
- Stok gerçeğinin sahibi stok olay defteridir; ekranlar türetilmiş pozisyon gösterir.
- Tahsisin sahibi merkezi tahsis motorudur; satış ve üretim ayrı rezerv tabloları kurmaz.
- Belge dosyasının sahibi merkezi arşivdir; modüller referanslar.
- Para olayının sahibi Finans; muhasebe fişi ayrı türev kayıttır.
- Muhasebe kaydının sahibi Muhasebe; operasyon modülü fişi sessizce değiştiremez.

### 9.3 Ortak kayıt metadata'sı

Her kritik kayıt: benzersiz kimlik, şirket, oluşturan, zaman, kaynak kanal, durum, sürüm/rowversion, onay durumu, korelasyon/causation kimliği, ters kayıt ilişkisi ve audit referansı taşır. Miktar/tutar kayıtları ayrıca birim/para birimi, kur kaynağı ve yuvarlama politikası taşır.

## 10. Formül, miktar ve finansal doğruluk sözleşmesi

### 10.1 Formül kataloğu

Her hesap tek merkezî katalogda aşağıdaki metadata ile tanımlanır:

- Formül kimliği ve sürümü.
- İş anlamı ve sahibi.
- Girdi/çıktı birimleri.
- Geçerlilik tarihi.
- Yuvarlama yöntemi ve basamak.
- Tolerans ve istisna politikası.
- Teorik/fiilî değer ayrımı.
- Kaynak kod/test referansı.
- Golden test seti ve beklenen sonuçlar.

Aynı formül ekran, rapor ve API'de yeniden yazılmaz. Arayüz hesap köprüsünü gösterir; sonuç backend servisinden gelir.

### 10.2 Kilitli formül aileleri

| Aile | Temel kural |
|---|---|
| Serbest stok | Fiziki stoktan aktif tahsis, yükleme tutma ve blokajlar düşülür. |
| Cari risk | Açık alacak + faturalanmamış sevk + sevk edilmemiş sipariş; DBS/teminat politikasıyla azaltılır. |
| Finans kaynağı | `Dağıtılmamış = Kaynak Tutarı − Aktif Dağıtımlar`; ters kayıtlar kaynak ilişkisiyle hesaba katılır. |
| Fiyat | Liste fiyatı, sıralı ayarlamalar/iskontolar ve net fiyat ayrı gösterilir. |
| Döviz | Orijinal para, işlem kuru, yerel karşılık ve kur kaynağı ayrı saklanır. |
| Üretim dengesi | Fiilî girdi; ana ürün, ikinci kalite, geri kazanılabilir artık ve fireye uzlaştırılır. |
| Boy kesme | Boy, adet, kesim payı ve kalan aynı birim sözleşmesiyle hesaplanır. |
| Dilme | Ham/net en, şerit, metre, teorik/fiilî KG ve kalan dengelenir. |
| Bobin sarım | Sarım yüksekliği, tahmini/maksimum dış çap ve uygunluk ayrıdır. |
| Boru/Profil | Geometri, yoğunluk, boy, paket ve adet/KG ilişkisi sürümlü masterla hesaplanır. |
| Boyama | Yüzey, geçiş metre, hat hızı ve boya tüketimi ayrı girdi/sonuçtur. |
| Soğuk hadde | Giriş/çıkış kalınlığı, metre, uzama/kısalma ve fire dengesi. |
| Nakit planı | Aynı olayın sipariş, fatura, talimat ve gerçekleşme aşamaları iki kez sayılmaz. |

Kesin matematik, katsayı, tolerans ve yuvarlama P0 saha kararıyla kilitlenmeden canlı finansal/stok etkisi yaratılamaz.

### 10.3 Fatura kuru

Arşivdeki hedef karar, her faturada tek fatura kuru bulunmasıdır. Birden fazla irsaliye/sipariş ancak para birimi, vergi, ticari koşul ve kur politikası uyumluysa aynı faturada birleşir. USD için önceki iş günü TCMB satış kuru varsayılanı arşiv kararıdır; nihai kaynak/saat/hafta sonu ve manuel değiştirme politikası uygulanmadan önce onaylanmalıdır. Manuel kur değişikliği sebep, yetki ve audit ister.

## 11. Durum makineleri

Durumlar ekrandaki renklerden ibaret değil; izin verilen geçişleri ve etkileri belirleyen sunucu tarafı kurallardır.

### 11.1 Örnek yaşam döngüleri

| Nesne | Önerilen ana durumlar | Kritik ilke |
|---|---|---|
| Sipariş | Taslak → Kontrolde → Onaylı → Operasyonda → Kısmi → Tamamlandı/Kapatıldı → İptal | Onay, sevkiyat ve ticari kapanış alt durumları ayrı eksenlerde tutulur. |
| Tahsis | Öneri → Taslak → Aktif → Kısmi Kullanıldı → Kullanıldı/Serbest → İptal | Aktif olunca miktar etkisi; stok hareketi değil. |
| Üretim planı | Taslak → Kontrolde → Onaylı → Kuyrukta → Üretimde → Kısmi/Tamamlandı → İptal/Ters | Plan, fiilî stok hareketinden ayrı. |
| Yükleme talimatı | Planlandı → Hazırlık → Yükleniyor → Tamamlandı → İptal | Talimat ve fiilî yük ayrı. |
| İrsaliye | Taslak → Kontrolde → Onaylı/Resmileşti → Kesildi → İade/Düzeltme/İptal | Kaynak sipariş/yükleme ilişkisi kaybolmaz. |
| Fatura | Taslak → Kontrolde → Onaylı → Gönderildi/Resmileşti → Kabul/Red → İade/Düzeltme | Açık kalem etkisi durum kuralıyla. |
| Finansal kaynak | Taslak → Kontrolde → Onaylı → Gerçekleşti → Mutabık → Ters | Dağıtım ayrı durum makinesi. |
| Dağıtım | Öneri → Taslak → Uygulandı → Kısmi Geri Alındı → Geri Alındı | Kaynak hareketini kopyalamaz. |
| Banka satırı | İçe Alındı → Sınıflandırıldı → Eşleşti → Mutabık → İstisna | Bilinmeyen satır cari hareket yaratmaz. |
| Fiyat sürümü | Taslak → Kontrolde → Onaylı → Planlı → Aktif → Süresi Doldu/İptal | Eski işlem snapshot'ı değişmez. |
| Belge sürümü | Yüklendi → Sınıflandırıldı → Kontrolde → Onaylı → Geçersiz/Yeni Sürüm | Fiziksel silme yerine durum ve yeni sürüm. |

Durum geçişleri için yetki, önkoşul, oluşacak etkiler, geri alma yöntemi ve audit şeması yazılmalıdır.

## 12. Yetki, onay, görev ayrılığı ve audit

### 12.1 Yetki modeli

Rol tabanlı yetki (RBAC), kayıt/şirket/depo/tutar/durum gibi nitelik tabanlı kurallarla (ABAC) birlikte kullanılır. Ekranda buton gizlemek güvenlik değildir; bütün kontroller backend'de tekrar uygulanır.

Yetki çözünürlüğü; şirket/şube/depo, modül, sayfa, işlem, kayıt sahibi, alan, tutar eşiği, durum, dışa aktarma/paylaşma ve onay yetkisini kapsar. Varsayılan `yetkisiz`, asgari yetki ve süreli istisna ilkeleri kullanılır. Kişiye doğrudan yüzlerce izin vermek yerine rol şablonu + veri kapsamı + az sayıda gerekçeli kişi istisnası uygulanır.

**AI destekli yetkilendirme:** Yetkili yönetici doğal dille “Ayşe yalnız A şirketinin satış siparişlerini görsün, fiyat değiştiremesin, 500.000 TL'ye kadar taslak hazırlayabilsin” diyebilir. AI bunu yetki matrisine çevirir; mevcut–önerilen farkı, riskleri, görev ayrılığı çakışmalarını ve kullanıcının görebileceği/yapabileceği örnekleri gösterir. AI yetkiyi kendi vermez. Yetkili yönetici önizleme sonrası uygular; kritik yönetici, ödeme, dönem, veri dışa aktarma ve yetki verme izinleri ikinci onay gerektirebilir. Her öneri, onay, uygulama, süre sonu ve geri alma audit kaydıdır.

Temel roller:

- Satış kullanıcısı/yöneticisi.
- Satın alma kullanıcısı/yöneticisi.
- Ana veri sorumlusu.
- Depo ve sevkiyat kullanıcısı.
- Kantar kullanıcısı.
- Üretim planlayıcısı, vardiya sorumlusu ve operatör.
- Kalite sorumlusu.
- Cari/risk ve tahsilat kullanıcısı.
- Muhasebe kullanıcısı/yöneticisi.
- Finans operasyon, hazine, kredi/teminat ve ödeme kullanıcısı.
- Onaylayıcı/yönetici.
- Denetçi.
- Sistem yöneticisi; işlem onaylayıcısı olmak zorunda değildir.

### 12.2 Kritik görev ayrılıkları

- Cari banka hesabı ekleyen kişi yüksek tutarlı ödeme paketini tek başına onaylayamaz.
- Ödeme talimatını hazırlayan, belirlenmiş eşik üstünde son onayı veremez.
- Fiyat/iskonto limiti isteyen ile istisnayı onaylayan ayrı olabilir.
- Stok düzeltmesi yapan ile sayımı onaylayan ayrı olabilir.
- Üretim fiilî miktarını giren ile tolerans dışı dengeyi onaylayan ayrı olabilir.
- Muhasebe dönemini kapatan ile dönem kilidini kaldıran ayrı yetkiye tabidir.
- AI hiçbir görev ayrılığı kuralını by-pass edemez.

### 12.3 Onay motoru

Onay akışı; işlem türü, şirket, tutar, para birimi, marj, risk, limit, tolerans ve istisnaya göre belirlenir. Onay kararı: onay, red, düzeltmeye iade, bilgi iste ve koşullu onaydır. Vekâlet, zaman aşımı ve eskalasyon kayıtlıdır.

### 12.4 Audit olayı

Her kritik işlemde: kim, ne zaman, hangi şirket/rol, hangi kayıt/sürüm, önceki/yeni değer, sebep, onay, istemci/IP/kanal, korelasyon ve varsa AI yardımı saklanır. Audit kaydı normal uygulama kullanıcısı tarafından değiştirilemez.

## 13. Hedef teknik mimari

### 13.1 Mimari stil

Başlangıç hedefi modüler monolittir. Satış, stok, tahsis, üretim, belge, cari ve finans aynı işlemde tutarlılık gerektirir. Erken mikroservis, dağıtık işlem ve operasyon maliyetini artırır. Modül sınırları kod, şema, API ve olay sözleşmelerinde korunur; yoğun belge/OCR, bildirim, entegrasyon ve browser connector işleri ayrı worker/servis olabilir.

### 13.2 Önerilen teknoloji başlangıç yönü

| Katman | Hedef yön |
|---|---|
| Web/PWA | React + TypeScript, katı tip kontrolü, ortak grid/form/workspace bileşenleri |
| Backend | C# / ASP.NET Core üzerinde .NET 10 LTS; aylık güvenlik yamalarıyla güncel tutulur |
| Mimari | Modüler monolit + ayrı worker/connector sınırı |
| Veri | Önerilen başlangıç PostgreSQL 18; mevcut lisans/ekip/entegrasyon zorunluluğu varsa SQL Server karşılaştırma PoC'si |
| Realtime | SignalR benzeri bildirim ve görev güncellemesi |
| Dosya | Şirket içi S3/MinIO veya güvenli nesne deposu; DB'de metadata/hash |
| Arka plan | Dayanıklı iş kuyruğu, outbox/inbox ve idempotent handler |
| AI | Backend aracılı şemalı tool/function çağrıları; metin/görsel/ses yetenekleri |
| Entegrasyon | REST/JSON, dosya, e-posta ve gerektiğinde Playwright connector; teknoloji bağımsız sözleşme |
| Dağıtım | Docker, reverse proxy, ayrı test/staging/production |
| Gözlemlenebilirlik | Merkezî log, metrik, trace, iş korelasyonu ve alarm |

Bu, kodlamaya başlama önerisidir; barındırma, SQL Server zorunluluğu ve ölçek sonuçları Faz 0 PoC'sinde doğrulanmadan geri dönülmez sözleşmeye dönüşmez.

### 13.3 Katmanlar

1. Web/PWA ve ileride mobil adaptör.
2. API/BFF; kimlik, kapsam, şema ve rate limit.
3. Uygulama komut/sorgu servisleri.
4. Domain modülleri ve deterministik motorlar.
5. Veri, olay defteri, dosya deposu ve arama/raporlama projeksiyonları.
6. Entegrasyon, belge, AI ve bildirim worker'ları.
7. Audit, gözlemlenebilirlik, güvenlik ve yapılandırma.

### 13.4 Transaction ve olay

- Aynı modül/DB transaction'ındaki kritik etkiler atomik olmalıdır.
- Dış entegrasyon mesajları transactional outbox'tan yayınlanır.
- Her komut idempotency key kabul eder; tekrar gönderim ikinci işlem yaratmaz.
- Consumer inbox/deduplication uygular.
- Olay şemaları sürümlenir; geriye uyumluluk ve yeniden oynatma politikası vardır.

### 13.5 Arama ve raporlama

Operasyonel gerçek ilişkisel modelde kalır. Arama indeksi ve rapor projeksiyonu türevdir; kaynak kayıt yerine geçmez. Rapor gecikmesi ve veri kesim zamanı ekranda gösterilir.

**Hızlı arama kademeleri:**

1. Belge numarası, barkod, cari/ürün kodu ve plaka gibi kesin aramalar uygun B-tree/unique index ile doğrudan ilişkisel veride çalışır.
2. Ad, açıklama, OCR metni ve yazım hatalı arama için PostgreSQL tam metin arama, `pg_trgm` ve GIN/GiST indeksleri kullanılır. Orijinal Türkçe metin korunur; `I/İ/ı/i` ve aksan eşlemesi ayrı normalize arama alanında test edilir.
3. Arama, ERP ana tablolarına rastgele çoklu `LIKE` sorguları atmaz; şirket/yetki kapsamlı birleşik bir arama projeksiyonu kullanır.
4. Temsili veri hacminin en az üç katıyla yük testi yapılır. PostgreSQL çözümü hedef gecikmeyi karşılamaz veya OCR/tam metin/faset hacmi bağımsız ölçek gerektirirse OpenSearch gibi ayrı arama kümesi devreye alınır.
5. Ayrı indeks hiçbir zaman tek gerçeklik değildir. Outbox ile güncellenir, gecikme izlenir, yeniden kurulabilir ve sonuç açıldığında yetki backend'de tekrar kontrol edilir.

Liste ekranları sunucu tarafı filtreleme, sıralama ve cursor/keyset sayfalama kullanır; milyonlarca satır tarayıcıya taşınmaz ve derin `OFFSET` varsayılanı kullanılmaz. Büyük dışa aktarımlar kuyrukta hazırlanır. Sorgu planı/regresyon izlemesi, yavaş sorgu bütçesi ve performans testleri yayın kapısıdır.

### 13.6 Çevrimdışı/PWA

Tam ERP'nin çevrimdışı kopyası hedeflenmez. Saha akışları için kontrollü taslak/outbox; barkod, fotoğraf, sayım, tartım yardımcı girdisi ve görev tamamlamada kullanılır. Sunucuya dönüşte yetki, sürüm ve çakışma tekrar doğrulanır.

## 14. Güvenlik ve iş sürekliliği

- OIDC/kurumsal SSO'ya hazır kimlik; MFA ve riskli oturum politikaları.
- Şirket, rol ve veri kapsamına göre sunucu tarafı yetki.
- Aktarımda ve depoda şifreleme; anahtarlar gizli kasa/secret store'da.
- Hassas alan maskeleme ve dışa aktarım yetkisi.
- Dosyada zararlı içerik tarama, MIME/doğrulama ve imzalı erişim.
- API rate limit, CSRF/XSS/SQL injection ve güvenli başlık kontrolleri.
- Bağımlılık/SBOM, secret ve statik/dinamik güvenlik taraması.
- Değiştirilemez/yazma korumalı audit arşivi.
- Yedek, nokta-zaman geri dönüş, nesne deposu sürümleme ve düzenli geri yükleme testi.
- RPO/RTO değerleri iş birimi tarafından onaylanmadan production açılmaz.
- Entegrasyon/AI kesintisinde temel ERP işlemleri çalışmaya devam eder veya kontrollü kuyrukta bekler.
- Kritik kayıtlar ACID transaction, benzersiz kısıt, foreign key ve optimistic concurrency ile korunur; yalnız uygulama koduna güvenilmez.
- Yatay büyütülebilen stateless API/worker, bağlantı havuzu, backpressure, devre kesici, sınırlandırılmış retry ve kuyruk derinliği alarmı uygulanır.
- Performans ve güvenilirlik hedefleri kullanıcı senaryosu bazında tanımlanır. Başlangıç önerisi: sık aramada p95 ≤ 300 ms, karmaşık aramada p95 ≤ 1 saniye, temel ERP yazmalarında harici entegrasyon hariç p95 ≤ 1 saniye ve aylık %99,9 kullanılabilirlik; Faz 0 yük testinde onaylanır.
- Backup almak yeterli değildir; zamanlanmış geri yükleme provası, bütünlük mutabakatı ve felaket senaryosu kanıtı tutulur.

## 15. StokERP'den devralma ve sistem geçişi

### 15.1 Devralma sınıfları

| Sınıf | Yaklaşım | Örnek |
|---|---|---|
| Doğrudan iş bilgisi devri | Kural doğrulanır, yeniden uygulanır | Saf formül, miktar dengesi, etiket ilişkisi |
| Uyarlayarak devral | Hedef modele ve UX'e dönüştürülür | Stok, rezerv, üretim kuyruğu, gerçekleştirme |
| Referans olarak sakla | Karşılaştırma/test kanıtı | Eski ekran, PDF, rapor, kullanıcı alışkanlığı |
| Devralma | Hedefte yeniden kullanılmaz | Genel bağlamsız irsaliye, UI kopyası, sessiz düzeltme, sabit kod/HTTP bağımlılığı |

### 15.2 Bağımsızlık kararı

Yeni sistem ayrı repository, domain, veritabanı, frontend, deployment ve test altyapısına sahip olacaktır. StokERP production çalışma zamanında kalıcı zorunlu API bağı değildir. Geçiş sırasında salt-okunur kaynak, veri çıkarımı ve mutabakat adaptörü olarak kullanılabilir.

### 15.3 Geçiş ilkeleri

- Her modül için kaynak sistem sahibi, veri sözlüğü, kalite profili ve hedef eşleme hazırlanır.
- Her kaydın kaynak kimliği ve migrasyon batch'i korunur.
- Deneme taşımaları; satır sayısı, toplam tutar/miktar, durum ve örnek kayıt mutabakatıyla doğrulanır.
- Formüller eski–yeni golden veri setinde karşılaştırılır.
- Uzun süreli çift yazma yapılmaz. Modül bazında kesin "tek gerçeklik tarihi" belirlenir.
- Cutover öncesi dondurma, delta taşıma, rollback ve kullanıcı kabul kanıtı vardır.
- Eski sistem yalnızca okuma/arşiv döneminden sonra, yasal saklama ve geri dönüş koşulları sağlanınca kapatılır.

### 15.4 Önerilen taşıma sırası

1. Şirket, kullanıcı/rol ve ana parametre.
2. Taraf/cari, adres/kişi ve veri temizliği.
3. Ürün/hizmet, birim, paket, fiyat ve kod eşleme.
4. Depo/lokasyon, etiket/lot ve açılış stok pozisyonu.
5. Açık satış/satın alma siparişleri ve terminler.
6. Aktif tahsis ve üretim planları.
7. Açık sevkiyat/kabul/fatura belgeleri.
8. Cari açık kalemler, banka/kasa, çek/senet, kredi/limit/teminat.
9. Tarihsel belge ve rapor arşivi.

## 16. Aşamalı uygulama yol haritası

AI, güvenlik, audit, test ve gözlemlenebilirlik son faza bırakılmaz; platformla birlikte başlar. Takvimler arşivdeki ön fizibilite aralıklarıdır, teklif veya kesin teslim taahhüdü değildir.

### Faz 0 — Karar kapıları ve keşif (4–6 hafta)

**Çıktılar:**

- Bu master raporun onaylı v3 karar sicili.
- P0 iş politikalarının karara bağlanması.
- Saha süreçleri ve rol/görev ayrılığı atölyeleri.
- StokERP veri/kod/veritabanı canlı envanteri.
- Teknik PoC: kimlik, React/PWA, ASP.NET, DB, dosya, AI tool ve deploy.
- Teknik PoC: Türkçe global arama, kişi/rol bazlı AI yetki taslağı, Türkçe PDF üretimi/adlandırması ve kullanıcı başlatmalı dosya paylaşımı.
- Kaynak–hedef veri sözlüğü ve formül golden seti.

**Çıkış kapısı:** P0 karar sahipleri, kabul senaryoları, mimari karar kayıtları ve ilk migrasyon provasi onaylı.

### Faz 1 — Platform, ortak kabuk ve ana veriler (8–12 hafta)

**Kapsam:** kimlik/yetki, şirket, AI destekli yetki taslağı/önizlemesi, onay/audit, ortak UI, görev merkezi, belge deposu, Türkçe PDF/dosya adı standardı, kullanıcı başlatmalı paylaşım, global arama, bildirim, AI panel sözleşmesi, Cari temel, Ürün/Hizmet temel, birim/paket/fiyat altyapısı, depo/lokasyon masterı.

**Kabul:** şirket veri izolasyonu, klavye/erişilebilirlik, taslak–onay, audit, dosya sürümü, AI yetki kapsamı ve ortak ekran bileşenleri otomatik testli.

### Faz 2 — Ticari MVP: Satış, Satın Alma, Cari ve Belgeler (10–14 hafta)

**Kapsam:** satış/satın alma siparişi, termin, fiyat/iskonto, Cari 360, risk iskeleti, sözleşme/belge, mal/hizmet kabul, İrsaliye & Fatura taslak/kontrol, açık kalem temel akışı.

**Kabul:** sipariş revizyonu, kısmi teslim, belge soy ağacı, üçlü kontrol, para/birim/kur ve riskte çifte sayım yok.

### Faz 3 — Stok, Tahsis, Üretim ve Lojistik (12–18 hafta)

**Kapsam:** tek Stok/Malzeme Pozisyonu, merkezi tahsis, etiket/soy ağacı, beş plan motoru, Üretim kontrol merkezi, gerçekleştirme, kalite kategorileri, yükleme, kantar, transfer ve mal kabul.

**Kabul:** eşzamanlı tahsis, stok denklemi, formül golden testleri, plan–gerçek, kütle dengesi, soy ağacı ve ters işlem senaryoları geçer.

### Faz 4 — Muhasebe, Finans ve Hazine (10–14 hafta)

**Kapsam:** muhasebe kontrol/fiş/mizan/dönem, finansal kaynak ve dağıtım, banka/kasa, mutabakat, ödeme yönetimi, çek/senet/POS/DBS, 13 haftalık nakit, kredi/limit/teminat ve nakit bütçesi.

**Kabul:** para kaynağı kopyalanmaz, 1:1–n:n mutabakat, açık kalem/dağıtım, görev ayrılığı, dönem kilidi, nakitte çifte sayım ve ters kayıt testleri geçer.

### Faz 5 — Kurumsal raporlama, optimizasyon ve yaygınlaştırma (8–12 hafta)

**Kapsam:** semantik raporlama, yönetim AI özeti, performans/kapasite optimizasyonu, ileri entegrasyonlar, mobil saha olgunlaştırma, kullanıcı eğitimi ve StokERP kapanışı.

Arşiv ön fizibilitesi, 7–10 kişilik güçlü bir çekirdek ekiple ticari MVP için yaklaşık 5–7 ay, bütünleşik kapsam için 12–18 ay bandı öngörür. Bu aralık Faz 0 sonunda gerçek veri, ekip ve entegrasyonlara göre yeniden tahmin edilmelidir.

## 17. Ekip ve yönetişim

Asgari çekirdek sorumluluklar:

- Ürün sahibi/karar kurulu.
- ERP iş analisti ve alan sahipleri.
- Çözüm mimarı/teknik lider.
- 2–3 backend, 2 frontend geliştirici.
- QA/test otomasyonu.
- UX/UI ve erişilebilirlik.
- DevOps/SRE ve güvenlik desteği.
- Veri/migrasyon uzmanı.
- AI/entegrasyon sorumlusu.

Her modül için bir iş sahibi, bir teknik sahibi ve bir kabul sahibi atanır. "Herkes karar versin" modeli yerine karar tarihi, gerekçe, etki ve sürümü olan karar sicili kullanılır.

## 18. Test ve kabul stratejisi

### 18.1 Zorunlu test katmanları

1. Domain ve formül birim testleri.
2. Golden formül ve yuvarlama veri setleri.
3. DB transaction, eşzamanlılık ve idempotency testleri.
4. API/olay/AI tool contract testleri.
5. Yetki, şirket kapsamı ve görev ayrılığı testleri.
6. Entegrasyon ve hata/yeniden deneme testleri.
7. Playwright benzeri uçtan uca kullanıcı akışları.
8. Görsel regresyon, responsive ve klavye/erişilebilirlik testleri.
9. Performans/yük: büyük tablolar, tahsis yarışı, rapor ve dosya.
10. Güvenlik, bağımlılık, secret ve sızma testleri.
11. Migrasyon sayı/tutar/miktar mutabakatı.
12. Yedekten geri dönüş ve felaket kurtarma provası.
13. AI golden senaryoları, halüsinasyon, veri sızıntısı ve yetki testleri.

### 18.2 Definition of Ready

Bir iş paketi kodlamaya hazır sayılmaz; ta ki iş sahibi, amaç/kapsam dışı, veri sahibi, durum geçişleri, formül/birim/yuvarlama, yetki/onay, audit, istisna, ekran durumları, API/olay ve kabul örnekleri yazılı olana kadar.

### 18.3 Definition of Done

- Kabul senaryoları ve kritik testler geçti.
- Finans/stok/bağlı kayıt etkisi doğrulandı.
- Yetki, audit, ters işlem ve eşzamanlılık test edildi.
- Boş/yükleniyor/hata/çakışma durumları tamamlandı.
- Masaüstü/tablet/mobil ve klavye/erişilebilirlik kontrol edildi.
- Gözlemlenebilirlik, alarm ve runbook mevcut.
- Veri migrasyonu/geri dönüş etkisi belgelendi.
- P0/P1 açık hata veya güvenlik riski yok.
- Yayın artefaktı, migration ve sürüm manifesti aynı kaynaktan üretilip doğrulandı.

## 19. Kodlama öncesi P0 karar listesi

### 19.1 Ürün, birim ve fiyat

1. "Ticari Ürün" sabit sınıf mı, rollerden türeyen görünüm mü?
2. Malzeme/kaplama/kalite ve ticari grup taksonomisi.
3. Ürün kodlama algoritması, manuel istisna ve değişmezlik.
4. Stok–alış–satış–fiyat birimi ve dönüşüm yetki akışı.
5. Fiyat ve paket standardının şirket/grup kapsamı.
6. Etiket/lot takip politikası ve hangi üründe zorunlu olduğu.
7. Taslak kartın sipariş/plan içinde kullanılıp kullanılamayacağı.
8. Toplu içe aktarma nihai adımı ve onay modeli.

### 19.2 Stok, üretim ve lojistik

9. Stok kabulünün kaynak olayı: irsaliye, mal kabul veya kantar kesinliği.
10. Kısmi hammaddeyle üretime başlama eşiği.
11. Aynı pakette farklı girdi lotları ve parti sınırı.
12. İkinci kalite, geri kazanılabilir artık ve fire stok etkileri.
13. Bütün formüllerin yuvarlama, tolerans ve birim politikaları.
14. Teorik profil ağırlığı masterının kaynak önceliği.
15. Kantar/yükleme toleransı: eski ±20 ile yeni ±10 örnekleri arasındaki nihai politika.
16. Etiket ve belge imza/kaşe zorunluluğu.
17. Dış fason, transit ve iki bacaklı transferde sahiplik/sorumluluk olayları.

### 19.3 Cari, muhasebe ve finans

18. Riskte DBS ve teminatın kullanılabilirlik/iskonto oranları.
19. Cari limit ve sevkiyat istisnası onay eşikleri.
20. Muhasebe fişi ve resmî program/entegrasyon için ilk faz tek gerçeklik sınırı.
21. E-fatura/e-arşiv, vergi ve iade/düzeltme mevzuatı akışı.
22. Kur kaynağı, saat, tatil ve manuel kur yetki politikası.
23. BTM teriminin kesin kurumsal anlamı.
24. Ödeme, kredi, limit, teminat ve bütçe onay eşikleri.
25. Minimum nakit tamponu ve 13 haftalık plan kaynak önceliği.
26. POS, şirket kartı ve DBS operasyon ayrıntıları.
27. Grup içi finans ve mahsup politikası.

### 19.4 Teknik ve kurumsal

28. SQL Server/PostgreSQL karşılaştırma sonucu.
29. Kimlik/SSO, MFA ve şirket dışı erişim modeli.
30. Veri saklama, kişisel veri, belge gizliliği ve AI veri gönderim politikası.
31. RPO/RTO, on-prem/cloud ve felaket kurtarma hedefi.
32. StokERP modül bazı SoT tarihleri ve kapanış kapıları.
33. İlk üç yıllık veri hacmi: şirket, kullanıcı, cari, ürün, sipariş satırı, stok hareketi, audit olayı, belge ve OCR metni tahmini.
34. Global arama p95 hedefi, eşzamanlı kullanıcı ve en ağır rapor/dışa aktarım senaryosu.
35. Rol şablonları, kişi istisnası, süreli yetki ve ikinci onay gerektiren kritik izinler.
36. Türkçe PDF adı kalıbı, belge türü sözlüğü, `Onaylı/Taslak/İptal` adlandırması ve PDF font ailesi.
37. WhatsApp ilk faz paylaşımında desteklenecek istemciler ve `dosya paylaşımı yoksa indir + metni kopyala` geri dönüş kabulü.

P0 listesindeki maddeler "sonra bakarız" diyerek geliştirme içine gizlenmemelidir. Her madde karar sahibi, tarih, seçenek, etki ve kabul senaryosuyla kapatılmalıdır.

## 20. Şirketin bütün yükünü alma hedefindeki kapsam boşlukları

Mevcut arşivler ticari operasyon, stok, üretim, lojistik, cari, muhasebe, finans, belge ve AI için derindir. Aşağıdaki alanlar ise ayrı uzmanlık analizi olmadan "tamamlandı" sayılamaz:

| Genişleme alanı | Neden gerekir | İlk karar |
|---|---|---|
| İnsan Kaynakları & Bordro | Özlük, izin, vardiya, puantaj, bordro ve yasal bildirim | ERP içi mi, bordro entegrasyonu mu? |
| Bakım Yönetimi | Makine/hat bakımı kapasite ve duruşu etkiler | Üretim içi hafif modül mü, bağımsız CMMS mi? |
| Kalite Yönetimi | Girdi, proses, çıktı, uygunsuzluk ve sertifika | Üretim kalite kaydının kapsamı yeterli mi? |
| Sabit Kıymet | Demirbaş, amortisman ve zimmet | Muhasebe kapsamı veya entegrasyon sınırı |
| CRM | Fırsat, aktivite, kampanya ve satış öncesi ilişki | Satış talep havuzu yeterli mi? |
| Proje/Maliyet | Proje, iş emri, maliyet merkezi ve kârlılık | Sipariş bazı maliyet mi, ayrı proje modülü mü? |
| Mevzuat/Uyum | KVKK, iş sağlığı, arşiv ve sektörel yükümlülük | Hangi yükümlülük ERP tarafında? |

Bu alanlar tek ERP vizyonuna dâhildir; fakat ilk ticari/operasyonel çekirdeği geciktirmemek için Faz 0'da kapsam kararı, sonrasında kendi analiz paketi yapılmalıdır.

## 21. İlk 90 gün için somut ilerleme planı

### Gün 1–15

- Karar kurulu ve alan sahiplerini belirle.
- Bu raporun Kilitli/Politika/Genişleme maddelerini onay oturumuna çıkar.
- StokERP canlı kaynak, DB, ekran, kullanıcı ve entegrasyon envanterini yeniden doğrula.
- P0 maddelerine sahip ve son tarih ata.
- İlk üç yıllık veri hacmi, eşzamanlı kullanıcı, arama sorguları, belge büyüklüğü ve büyüme tahminini sayısallaştır.
- Beş gerçek kullanıcı/kişi üzerinden rol, veri kapsamı, dışa aktarma ve onay yetki örneklerini çıkar.

### Gün 16–30

- Ortak dil sözlüğü, veri sahipliği ve durum makinelerini onayla.
- Cari, ürün, sipariş, stok/tahsis ve belge için ilk API/olay sözleşmelerini yaz.
- Formül kataloğu ve golden test veri setini başlat.
- Ortak kabuk ve AI paneli için tıklanabilir tek tasarım sistemi prototipi oluştur.
- Türkçe dosya adı/karakter test setini ve belge türü adlandırma sözlüğünü onayla.

### Gün 31–45

- Teknoloji ve veritabanı PoC'sini tamamla.
- Kimlik, şirket kapsamı, audit, dosya deposu, outbox ve AI tool protokolünü çalıştır.
- Temsili hacimde tam metin/trigram arama, keyset listeleme, paralel kullanıcı ve büyük dışa aktarım testini çalıştır.
- AI yetki taslağı → risk/fark önizlemesi → yönetici onayı → audit akışını doğrula.
- Eski–yeni veri eşlemesiyle ilk migrasyon provasını yap.
- Rol bazlı kullanıcı testleriyle tasarımı sadeleştir.

### Gün 46–60

- Faz 1 backlog'unu kabul senaryolarıyla kilitle.
- Ortak bileşenler, yetki/onay/audit ve ana veriler için dikey dilimi geliştir.
- CI/CD, test/staging, log/metric/trace ve güvenlik kapılarını kur.

### Gün 61–90

- Cari + Ürün + Belge içeren ilk gerçek uçtan uca dilimi kullanıcı kabulüne aç.
- AI'nın belge okuma → taslak → kullanıcı onayı akışını güvenlik testiyle doğrula.
- Onaylı sipariş sözleşmesi → Türkçe adlı PDF → merkezi arşiv → kullanıcı başlatmalı paylaşım → audit dikey dilimini doğrula.
- İlk migrasyon kalite raporu ve Faz 2 yeniden tahminini yayınla.
- P0 açık kalmışsa ilgili modülün kesin uygulamasını başlatma.

## 22. Nihai kabul resmi

Göymen ERP başarılı sayılır, eğer:

- Kullanıcı tek giriş ve ortak kabukla günlük işini yürütebiliyorsa.
- Aynı ticari/fiziksel/finansal olay modüller arasında kopyalanmıyorsa.
- Siparişten tahsilata ve hammaddeden sevkiyata tam soy ağacı kurulabiliyorsa.
- Stok, risk, fiyat, üretim ve nakit formülleri sürümlü ve golden testli ise.
- Yüksek etkili işlemler önizleme, yetki, onay, ters kayıt ve audit ile korunuyorsa.
- AI her sayfada faydalı, açıklanabilir ve sınırlıysa; ERP'nin yerine geçmiyorsa.
- Mobil saha akışları masaüstünü küçültmek yerine gerçek saha görevlerini çözüyorsa.
- StokERP'ye canlı çalışma zamanı bağı kalmadan veri ve iş bilgisi devri tamamlandıysa.
- Her yayın geri dönüşlü, gözlemlenebilir, testli ve kanıtlanabilir ise.

## 23. Son hüküm

Kullanıcının istediği tek ERP; "her uzman sohbette çizilen bütün ekranları yan yana koymak" değildir. Doğru ürün, uzmanlaşmış kararları tek veri sahipliği, ortak durum makineleri, ortak UI şablonu ve ortak AI güvenlik sözleşmesi altında birleştirir.

Bu nedenle uygulama sırası ekran ekran değil, omurga önceliklidir:

`Karar ve sözleşmeler → Ortak platform → Ana veriler → Sipariş/Belge → Stok/Tahsis → Üretim/Lojistik → Muhasebe/Finans → Kurumsal rapor/optimizasyon → Kontrollü genişleme`

Nihai hedef sade görünen fakat arkasında tam iş kuralları, izlenebilirlik ve denetim taşıyan; çalışanın veri aramakla değil karar vermekle uğraştığı bir Göymen ERP'dir.

## 24. Kaynak kapsamı, kanıt ve bilinen sınır

Bu nihai rapor aşağıdaki doğrulanmış arşiv kapsamına dayanır:

| Kanıt | Sonuç |
|---|---:|
| Ana ZIP | 6 |
| İç içe ZIP katmanı | 14 |
| Dosya oluşumu | 906 |
| Benzersiz içerik | 523 |
| Metin çıkarılabilir nesne | 126 |
| Çıkarılan metin | 4.881.049 karakter |
| Benzersiz DOCX | 9 |
| Benzersiz PDF | 4 / 151 sayfa |
| Benzersiz HTML prototip | 13 |
| Benzersiz görsel | 401 |
| Arşivde XLS/XLSX/TSV | 0 |
| Güvensiz ZIP yolu / okuma hatası | 0 |

Konuşma tamlık durumu:

| Alan | Görünen içerik | Durum |
|---|---:|---|
| Cari | 122.302 karakter / 15 mesaj | Tam görünür döküm |
| ERP Analizi / StokERP | 60.939 karakter / 13 mesaj | Tam görünür döküm |
| Finans | 67.692 karakter / 24 mesaj | Tam görünür döküm |
| İrsaliye–Fatura–Muhasebe | 32.204 karakter / 8 mesaj | Tam görünür döküm |
| Ürün–Hizmet | 173.829 karakter / 29 mesaj | Tam görünür döküm |
| Şirket içi AI / ana operasyon | 13.726 karakter | Anlatı rekonstrüksiyonu; 7 açık verbatim boşluk |

Son satırdaki yedi boşluk arşivde bulunmayan kelimelerdir. Bu rapor eksik cümleleri uydurmaz; mevcut karar dosyaları, belgeler, ekranlar ve görsellerle doğrulanan anlamı kullanır. HTML dosyaları çalışan görsel prototiptir; backend, canlı veritabanı veya tamamlanmış ERP değildir.

## 25. Bu konuşmada sonradan kesinleşen bağlayıcı ek kararlar

### 25.1 Bir evrakın birden fazla ekran ve kayda etkisi

Bir evrak fiziksel olarak bir kez saklanır ve bir ana iş olayı olarak işlenir. Aynı evrak için stok, cari, muhasebe, finans, sipariş veya belge modülünde ayrı ayrı kopya kayıt girilmez.

AI ve ERP şu sırayı kullanır:

1. Dosyayı tanı, hash ve mükerrer kontrolü yap.
2. Belge türü, şirket, cari, dönem ve kaynak kaydı öner.
3. Oluşacak ana kayıt ile bütün bağlı etkileri bir **etki planı** olarak göster.
4. Stok, cari, açık kalem, muhasebe, finans, sipariş ve arşiv etkisini ayrı satırlarda önizlet.
5. Kullanıcı düzeltmesi ve yetki kontrolünden sonra tek ERP komutu uygula.
6. Aynı transaction içindeki etkileri atomik yaz; dış etkileri outbox ile yayınla.
7. Her ekranda aynı olayın kendi bağlamındaki projeksiyonunu göster.

AI hangi ekranların etkileneceğini bilir; fakat ekranlara doğrudan yazmaz. İş kurallarını atlamadan şemalı ERP servisini çağırır. Bir etki başarısızsa işlem yarım kalmış başarılı gibi gösterilmez.

### 25.2 AI yalnız kullanımı değil, kurumsal sistemi de bilir

AI'ın bilgi kapsamı:

- Sayfa, alan ve kullanıcı yardım metinleri.
- Veri modeli ve varlık ilişkileri.
- Durum makineleri ve geçiş ön koşulları.
- Formüller, birimler, toleranslar ve sürümler.
- Modüller arası belge/olay etkileri.
- Yetki, görev ayrılığı ve onay politikaları.
- Şirket sözlüğü, ürün/taksonomi ve kontrollü master veriler.
- Süreç performansı, istisnalar ve açıklanabilir raporlar.

Bu bilgi sürümlü doküman, şema, politika, servis sözleşmesi ve yetkili kurumsal arama üzerinden verilir. Model kullanıcı davranışından kendi kendine kalıcı iş kuralı üretmez. Geri bildirim öneri/eval havuzuna alınır; veri sorumlusu ve alan sahibi onaylarsa yeni sürüm yayımlanır.

### 25.3 Üretim hesapları düğmesiz anlık önizleme üretir

Boy Kesme, Dilme, Boru/Profil, Boyama ve Soğuk Hadde planlama ekranları StokERP'deki kolaylık seviyesini korur; ekranları birebir kopyalamaz.

- Kullanıcı geçerli bir girdiyi değiştirdiğinde ayrı `Hesapla` düğmesine basmaz.
- Kısa bir debounce sonrasında deterministik backend calculator otomatik çalışır.
- Planlanan çıktı, kalan, fire, metre, kg, adet, süre, kapasite ve uyarılar aynı anda güncellenir.
- Geçersiz veya eksik alan varsa son doğrulanmış sonuç “güncel” gibi gösterilmez; hangi girdi eksik olduğu açıklanır.
- Önizleme kaydetme değildir ve stok hareketi oluşturmaz.
- Plan onayı calculator/formül sürümü, girdi snapshot'ı ve görülen önizleme hash'ini kaydeder.
- AI sonucu hesaplamaz; sonucu açıklar, alternatif önerir ve anormalliği işaretler.

### 25.4 Stok ekranı aynı zamanda bağlamsal işlem merkezidir

`Stok / Malzeme Pozisyonu` satırı veya seçili etiket üzerinden yetkiye göre şu işlemler açılır:

- Üretim planla.
- Satışa tahsis et.
- Tahsisi görüntüle/revize et/serbest bırak.
- Yükleme talimatı oluştur.
- Transfer veya dış fason hareketi başlat.
- Sayım/fark ve kalite blokajı başlat.
- Etiket/lot/ısı/MTC soy ağacını aç.
- Ürün, cari, sipariş, satın alma ve üretim kaynağına git.

İşlem satır bağlamından açıldığı için ürün, lot, sahiplik, depo ve serbest miktar yeniden seçtirilmez. Her işlem yine kendi tam ekran workspace ve onay kurallarına gider.

### 25.5 Henüz teslim alınmamış alış siparişi stok görünümü

Alış siparişinin teslim alınmamış net kalanı Stok ekranında `gelecek arz` satırı/projeksiyonu olarak görünür.

Zorunlu kolonlar:

- `Kaynak/Durum = Alış Siparişi`.
- Alış siparişi ve satır numarası.
- Tedarikçi.
- Ürün/ölçü/kalite.
- Sipariş, iptal, kabul, red ve kalan miktar.
- Beklenen termin ve termin dilimi.
- Yolda bildirilen miktar ve son kaynak tarihi.
- Varsa gelecek tahsis edilen sipariş/üretim ihtiyacı.

`Alış Kalan = Onaylı Sipariş − İptal − Kabul Edilen Net − Kesin Red/İade Etkisi`

Bu miktar fiziksel, serbest veya sevk edilebilir stok değildir. Mal kabul ve stok kabul politikası gerçekleşmeden fiziksel stoğu artırmaz. Kaynak türleri aynı listede açıkça ayrılır: `Fiziksel Stok`, `Alış Siparişi`, `Üretim Bekleniyor`, `Yolda`, `Kalite Bekliyor`.

### 25.6 Şirket-geneli boşluklar kontrollü gelecek menü katmanıdır

İK/bordro, bakım, ileri kalite, sabit kıymet, CRM, proje/maliyet, sözleşme/uyum ve İSG çekirdek ERP'yi geciktirmeden sonraki menü katmanı olabilir. Bununla birlikte şirket, kişi, belge, yetki, maliyet merkezi, varlık, görev ve olay sözleşmelerindeki genişleme noktaları Faz 0'da rezerve edilir. Bu modüller geliştirilmeden “şirketin bütün yükü tamamlandı” denmez.

### 25.7 Belge, paylaşım ve iletişim son kararı

- Dışarıdan yüklenen ve sistemin ürettiği bütün dosyalar Merkezi Belge Kasasına girer.
- PDF, görsel, Excel, Word, e-posta eki ve entegrasyon dosyalarının orijinali korunur.
- Sistem çıktıları PDF; düzenlenebilir tablolar XLSX; veri değişimi gerektiğinde CSV/XML/JSON olabilir.
- E-posta taslağında Kime, CC, Konu, Gövde ve Ek AI tarafından önerilebilir; kullanıcı onayı olmadan gönderilmez.
- `Ayarlar → Belge ve İletişim Şablonları` şirket, belge türü, cari grubu, dil ve rol bazında varsayılanları yönetir.
- WhatsApp ilk fazda Business/API entegrasyonu değildir; yalnız kullanıcı başlatmalı işletim sistemi/tarayıcı paylaşımıdır.
- Kullanıcıya sunulan PDF adı Türkçe karakterleri korur; değişmez teknik belge kimliği dosya adından ayrıdır.

### 25.8 AI destekli kişi bazlı yetki son kararı

AI doğal dildeki yetki isteğini rol + veri kapsamı + kişi istisnası politikasına çevirir. Mevcut/önerilen fark, risk, görev ayrılığı, erişilebilen örnek kayıtlar ve süre sonunu gösterir. AI yetki veremez; yetkili yönetici uygular. Kritik yönetici, ödeme, dönem, dışa aktarma ve yetki verme izinleri ikinci onaya bağlanabilir.

## 26. Görsel tasarım sistemi ve lacivert yön

Kullanıcının paylaştığı dashboard yönü benimsenir: koyu lacivert ana navigasyon, açık çalışma yüzeyi, güçlü mavi ana aksiyon ve kontrollü semantik renkler. Arayüz fazla pastel veya aşırı yumuşak olmayacaktır.

### 26.1 Önerilen başlangıç tokenları

| Token | Başlangıç yönü | Kullanım |
|---|---|---|
| `nav-bg` | `#071F3D` | Sol ana menü |
| `nav-bg-deep` | `#04172E` | Menü alt/yoğun alan |
| `primary` | `#0B6FE8` | Birincil eylem, aktif bağlantı |
| `primary-strong` | `#075BC4` | Hover/pressed |
| `page-bg` | `#F5F7FA` | Ana arka plan |
| `surface` | `#FFFFFF` | Kart, panel, form |
| `text-strong` | `#12213A` | Başlık ve ana metin |
| `text-muted` | `#5E6B7F` | Yardımcı metin |
| `border` | `#DDE3EC` | İnce sınır |
| `success` | `#168A4A` | Olumlu/tamam |
| `warning` | `#C56A00` | Uyarı/gecikme |
| `danger` | `#D63649` | Kritik/hata/iptal |
| `info` | `#087FBF` | Bilgi/aktif bağlam |

Bu değerler prototip başlangıcıdır; WCAG 2.2 AA kontrast testi ve marka onayı sonrası kilitlenir.

### 26.2 Görsel karakter

- Lacivert sol menü sabit, daraltılabilir ve yüksek kontrastlıdır.
- Aktif menü canlı mavi zemin veya belirgin sol çizgi + metinle gösterilir.
- Ana içerik beyaz/açık gri; kart gölgeleri çok hafif, sınırlar belirgindir.
- Köşe yarıçapı çoğunlukla 8–12 px; finansal/operasyonel ciddiyeti bozacak aşırı yuvarlaklık kullanılmaz.
- Degrade yalnız marka/özel vurgu için sınırlıdır; iş durumu rengiyle karışmaz.
- Aynı ekranda çok sayıda pastel ikon rengi yerine 1 ana vurgu + semantik renk kullanılır.
- Sağ AI paneli masaüstünde yaklaşık 320–380 px, daraltılabilir/kapatılabilir; 1600 px altında drawer olur.
- Tutar, miktar ve kodlar yoğun tabloda net; başlık, grup ve satır çizgisi hiyerarşisi güçlüdür.
- Renk tek başına anlam taşımaz; ikon, metin ve durum rozeti birlikte kullanılır.

## 27. Eksiksiz ekran ve çalışma alanı kataloğu

Bu katalog menüde yüzlerce satır gösterilmesi anlamına gelmez. Alt çalışma alanları görev, sekme, görünüm veya bağlamsal eylem olarak açılır. Her ekran ortak kabuk, yetki, AI, audit ve çıktı/paylaşım sözleşmesini kullanır.

### 27.1 Ana Sayfa ve Çalışma Merkezi

| Kod | Ekran/alan | Sayfa modeli |
|---|---|---|
| D-01 | Rol Bazlı Ana Sayfa | Dashboard/KPI |
| D-02 | Benim İşlerim | Görev listesi + detay |
| D-03 | Yönetim Özeti | Kontrol merkezi |
| D-04 | Operasyon Nabzı | Kontrol merkezi |
| D-05 | Onaylarım | Liste + onay workspace |
| D-06 | Bildirim ve İstisnalar | Liste + detay |
| D-07 | Favoriler / Son Kullanılanlar / Kaydedilmiş Görünümler | Kişisel çalışma alanı |

### 27.2 Satış

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| S-01 | Satış Talebi / Teklif Havuzu | Talep, teklif ve fırsat bağlantısı |
| S-02 | Satış Siparişleri | Liste + hızlı detay |
| S-03 | Yeni/Düzenle Satış Siparişi | Rehberli form |
| S-04 | Sipariş 360 | Satırlar, tahsis, gerçekleşme, faturalar, belgeler, revizyon |
| S-05 | Fiyat–İskonto–Marj–Risk Kontrolü | Karşılaştırma/önizleme |
| S-06 | Satış Onayları | Onay kuyruğu |
| S-07 | Sevkiyat / İrsaliyeler | Kaynak sipariş ve yükleme bağlamı |
| S-08 | Faturaya Hazır | Gerçekleşme–belge kontrolü |
| S-09 | Satış Faturaları | Liste, detay, durum ve gönderim |
| S-10 | İade / Düzeltme / İptal | Kaynak yaşam döngüsü görünümü |
| S-11 | Satış İletişimi ve Sözleşmeler | Belge ve iletişim zaman çizgisi |

### 27.3 Satın Alma

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| P-01 | Satın Alma Talepleri / İhtiyaç Havuzu | Stok, satış, üretim ve manuel ihtiyaç |
| P-02 | Teklif ve Tedarikçi Karşılaştırma | Fiyat, kalite, termin, koşul |
| P-03 | Satın Alma Siparişleri | Liste + hızlı detay |
| P-04 | Yeni/Düzenle Satın Alma Siparişi | Rehberli form |
| P-05 | Sipariş 360 / Termin Dilimleri | Sipariş, teyit, yolda, kabul, fatura |
| P-06 | Beklenen Teslimatlar | Gelecek arz ve gecikme |
| P-07 | Mal/Hizmet Kabulü | Sipariş–irsaliye–kantar–kalite |
| P-08 | Gelen Faturalar | Liste ve belge kontrolü |
| P-09 | Üçlü Kontrol | Sipariş ↔ kabul/irsaliye ↔ fatura |
| P-10 | Red / İade / Düzeltme | Kaynak miktar ilişkisi |
| P-11 | Satın Alma Onayları | Onay kuyruğu |
| P-12 | Tedarikçi Performansı | Termin, fiyat ve kalite |

### 27.4 Ürün, Hizmet, Fiyat ve İşletme Alımları

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| U-01 | Ürünler | Liste + Ürün 360 |
| U-02 | Yeni/Düzenle Ürün | Kimlik, sınıf, roller, teknik nitelik |
| U-03 | Hizmetler | Liste + detay |
| U-04 | Yeni/Düzenle Hizmet | Ticari hizmet kartı |
| U-05 | Fiyatlar | Liste, sürüm ve geçerlilik |
| U-06 | Fiyat Simülasyonu / Toplu Güncelleme | Etki önizlemesi |
| U-07 | Paket Standartları | Sürümlü paket/adet/ağırlık |
| U-08 | Birim ve Dönüşümler | Birim sözleşmesi |
| U-09 | Teknik Şablon ve Dinamik Nitelikler | Grup/aile şablonu |
| U-10 | Excel İçe Aktarma | Eşleme–önizleme–onay |
| IA-01 | İşletme Alım İhtiyaçları | Sarf/bakım/yedek parça/genel gider |
| IA-02 | İşletme Alım Sipariş/Kabul | Ticari katalogdan ayrı operasyon |

### 27.5 Cari

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| C-01 | Cari Kartlar | Tek hukuki kimlik listesi |
| C-02 | Yeni/Düzenle Cari | Kimlik, roller, şirket hesapları |
| C-03 | Cari 360 | Özet, roller, risk, zaman çizgisi |
| C-04 | Cari Hesap Takibi | Ekstre ve hesap görünümü |
| C-05 | Açık Kalemler | Fatura/taksit ve kapama |
| C-06 | Adres / Kişi / İletişim | Şube ve muhataplar |
| C-07 | Banka / Vergi / Sicil | Hassas ve tarihçeli kayıt |
| C-08 | Risk / Limit / Vade | Brüt/net risk ve sevkiyat kararı |
| C-09 | Mutabakat | Taslak, gönderim, cevap ve fark |
| C-10 | Belgeler / Sözleşmeler | Cari belge referansları |
| C-11 | Mükerrer ve Birleştirme | Karşılaştırma + onay |
| C-12 | İlişki / Takip Görevleri | Görüşme, söz ve sorumluluk |

### 27.6 Stok, Malzeme ve Tahsis

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| ST-01 | Stok / Malzeme Pozisyonu | Fiziksel + gelecek arz birleşik görünüm |
| ST-02 | Malzeme/Etiket 360 | Kimlik, sahiplik, konum, miktar, soy ağacı |
| ST-03 | Depo/Lokasyon Görünümü | Tesis–depo–alan–raf |
| ST-04 | Stok Hareket Zaman Çizgisi | Giriş, çıkış, transfer, ters kayıt |
| ST-05 | Sayım ve Fark Yönetimi | Sayım, karşılaştırma, onay |
| ST-06 | Kalite/Sistem Blokajı | Karantina, blok, serbest bırakma |
| ST-07 | Transfer / Emanet / Dış Fason | İki bacaklı hareket |
| ST-08 | Gelecek Arz | Alış siparişi, yolda, üretim bekleniyor |
| AL-01 | Talep Havuzu | Satış/üretim/sevkiyat ihtiyacı |
| AL-02 | Kaynak Önerisi | Uygun stok/gelecek alım/üretim |
| AL-03 | Aktif Tahsisler | Çoktan çoğa tahsis |
| AL-04 | Tahsis Revizyonu | Miktar, kaynak, öncelik ve etki |
| AL-05 | Serbest Bırakma / Sona Erme | Kontrollü geri açma |
| AL-06 | Çakışma ve Öncelik | Eşzamanlılık ve istisna |

### 27.7 Üretim ve Kalite

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| PR-01 | Üretim Yönetimi Ana Bakış | KPI, darboğaz, istisna |
| PR-02 | Üretim Planları | Liste + detay |
| PR-03 | Yeni Üretim Planı Ortak Kabuğu | Bağlam, kaynak, hesap, çıktı, denge, onay |
| PR-04 | Boy Kesme Planlayıcı | Boy/adet/kg/metre/kalan/kerf |
| PR-05 | Dilme ve Bobin Sarım Planlayıcı | En kombinasyonu, kg, metre, çap |
| PR-06 | Boru/Profil Planlayıcı | Paket/adet/boy/kg/hat/süre |
| PR-07 | Boyama Planlayıcı | Yüzey, geçiş, hız, boya, kalite |
| PR-08 | Soğuk Hadde Planlayıcı | Kalınlık, metre, redüksiyon, fire |
| PR-09 | Plan Detayı / Revizyon | Snapshot ve fark |
| PR-10 | Aktif Üretim Kuyruğu | Sıra, hat, kalıp, setup |
| PR-11 | Üretim Takvimi / Gantt | İş merkezi takvimi |
| PR-12 | Üretim/Baç Başlatma | Kaynak ve ön koşul kontrolü |
| PR-13 | Hammadde Ekle/Değiştir | Tahsis ve genealoji |
| PR-14 | Üretim Gerçekleştirme | Hızlı tamamlama / fiilî / istisna |
| PR-15 | Gerçekleşmeler | Plan–gerçek ve ters işlem |
| PR-16 | Reçete / Formül / Tolerans | Sürümlü master |
| PR-17 | Hat / Kalıp / Kapasite | İş merkezi masterı |
| Q-01 | Giriş Kalite | Mal kabul bağlantısı |
| Q-02 | Proses Kalite | Üretim içi kontrol |
| Q-03 | Nihai Kalite | Çıkış/serbest bırakma |
| Q-04 | Karantina ve Bloklar | Stok uygunluğu |
| Q-05 | Uygunsuzluk / Sapma / Yeniden İşleme | CAPA genişleme bağı |
| Q-06 | Numune / Test / Sertifika / MTC | Kalite kanıtı |

### 27.8 Lojistik, Sevkiyat, Kantar ve Mal Kabul

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| L-01 | Sevk Edilebilir Siparişler | Kaynak/tahsis kontrolü |
| L-02 | Yükleme Talimatları | Plan, araç, durak, miktar |
| L-03 | Yeni/Düzenle Yükleme Talimatı | Bağlamsal workspace |
| L-04 | Araç / Sürücü / Kapı | Operasyon master ve oturum |
| L-05 | Fiilî Yükleme | Etiket ve gerçek miktar |
| L-06 | Kantar Oturumu | Brüt, dara, net ve fark |
| L-07 | Sevkiyat Gerçekleşmesi | Plan–gerçek uzlaştırma |
| L-08 | Satış İrsaliyesi Hazırlığı | Gerçekleşmeden belgeye |
| L-09 | Beklenen Alış / Kapı Kabulü | Tedarik geliş kaydı |
| L-10 | Mal Kabulü | İrsaliye, kantar, kalite ve kabul |
| L-11 | Transfer / Yolda / Hedef Kabul | İki bacaklı lojistik |
| L-12 | Teslimat ve Farklar | Eksik, fazla, hasar, red |

### 27.9 Belgeler ve iletişim

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| B-01 | Belge Gelen Kutusu | Dosya/e-posta/tarama girişleri |
| B-02 | Belge Görüntüleme ve OCR | Orijinal + çıkarım |
| B-03 | Sınıflandırma / Kayıt Eşleştirme | AI taslak ve kullanıcı doğrulama |
| B-04 | Mükerrer Kontrol | Hash + anahtar karşılaştırma |
| B-05 | Onay ve İşleme Kuyruğu | Belgeden ERP kaydına |
| B-06 | Merkezi Belge Arşivi | Sanal klasör, metadata, sürüm |
| B-07 | Belge 360 / Bağlı Kayıtlar | Tek dosya–çok referans |
| B-08 | Çıktı / Dışa Aktar / Paylaş | PDF/XLSX/CSV/XML/JSON |
| B-09 | E-posta Taslağı ve Gönderim | Kime/CC/Konu/Gövde/Ek |
| B-10 | Paylaşım ve Gönderim Geçmişi | Yetkili audit |
| B-11 | E-belge Durumu | Adaptör/entegrasyon durumu |

### 27.10 Muhasebe

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| M-01 | Muhasebe Kontrol Merkezi | Hata, bekleyen, dönem |
| M-02 | Faturalar ve Belgeler | Kaynak belge kontrolü |
| M-03 | Cari Hesaplar | Muhasebe/ticari bağlantı |
| M-04 | Muhasebe Fişleri | Taslak, onay, ters kayıt |
| M-05 | Hesap Planı ve Boyutlar | Hesap, merkez, proje boyutu |
| M-06 | Mizan / Defter / Yasal Rapor | Dönemsel rapor |
| M-07 | Dönem ve Vergi | Kilit, kontrol, KDV/vergi |
| M-08 | Mahsup ve Mutabakat | Açık kalem bağlantısı |
| M-09 | Maliyet Muhasebesi | Genişleme/karar kapısı |

### 27.11 Finans ve Hazine

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| F-01 | Finans Genel Bakış | Likidite, vade, risk, onay |
| F-02 | Tahsilat / Ödeme Kaydı | Finansal kaynak oluşturma |
| F-03 | Finansal Dağıtım ve Açık Kalem Kontrolü | Çoktan çoğa dağıtım |
| F-04 | Banka Hesapları ve Ekstre | Defter/ekstre/valör/mutabakat |
| F-05 | Kasa ve Virman | Nakit ve hesap transferi |
| F-06 | Çek / Senet | Portföy, ciro, tahsil, red |
| F-07 | POS / Şirket Kartı / DBS | Şirket kullanımı; tedarikçi POS yok |
| F-08 | Krediler ve Finansman | Tesis, kullandırım, taksit, maliyet |
| F-09 | Banka Limitleri | Nakdi/gayrinakdi, kullanım, boş limit |
| F-10 | Teminatlar ve Covenant | Tahsis, sözleşme, uyarı |
| F-11 | Nakit Akışı | İlk 30 gün günlük, 13 hafta haftalık |
| F-12 | Bütçe / Borç Servisi / Senaryo | Baz–iyimser–stres |
| F-13 | Cari Risk ve Tahsilat Takibi | Limit, gecikme, hukuk, söz |
| F-14 | Grup İçi Finans | Karşı hareket, netleştirme, eliminasyon |
| F-15 | Ödeme Önerisi ve Banka Talimatı | Hazırla–kontrol–onay–sonuç |
| F-16 | Finans Mutabakatı | Banka/Mikro/entegrasyon farkı |
| F-17 | Finans Raporları ve Audit | Yaşlandırma, maliyet, performans |

### 27.12 Raporlar, AI Merkezi, Ana Veriler ve Sistem Yönetimi

| Kod | Ekran/alan | Ana kapsam |
|---|---|---|
| R-01 | Kurumsal Rapor Merkezi | Yetkili semantik raporlar |
| R-02 | Satış / Alış / Stok / Üretim / Lojistik | Alan raporları |
| R-03 | Finans / Cari / Kârlılık / Nakit | Finansal analiz |
| R-04 | Veri Kalitesi / Entegrasyon Sağlığı | Mükerrer, eksik, gecikme |
| R-05 | Zamanlanmış Rapor / Dışa Aktarım | Kuyruk ve teslimat |
| AI-01 | AI Merkezi | Görev, öneri ve günlük özet |
| AI-02 | Öneri / Onay / Sonuç Kalitesi | Eval ve geri bildirim |
| AI-03 | Araç / Politika / Veri Kapsamı | Sürüm ve güvenlik |
| AV-01 | Şirket / Tesis / Şube / Depo / Lokasyon | Organizasyon masterı |
| AV-02 | Birim / Para / Kur / Vergi | Ortak master |
| AV-03 | Durum / İş Akışı / Numara Serisi | Ortak politika |
| SY-01 | Kullanıcılar | Kimlik, durum, oturum |
| SY-02 | Roller ve Yetkiler | RBAC + ABAC |
| SY-03 | AI Yetki Taslağı | Fark/risk/önizleme |
| SY-04 | Onay Matrisi / Eşik / Vekâlet | Görev ayrılığı |
| SY-05 | Belge ve İletişim Şablonları | PDF/e-posta/paylaşım varsayılanı |
| SY-06 | Entegrasyon ve Veri Eşlemeleri | Durum, hata, tekrar |
| SY-07 | Audit ve Sistem Sağlığı | İş izi, log, metrik, alarm |
| SY-08 | Güvenlik / Saklama / Gizlilik | MFA, maskeleme, retention |

### 27.13 Gelecek menü katmanı

İK & Bordro, Bakım Yönetimi, ileri Kalite/CAPA, Sabit Kıymet, CRM, Proje/Maliyet, Sözleşme/Hukuk/Uyum, İSG ve sürdürülebilirlik ayrı keşif ve kabul paketinden sonra eklenir. Menü yeri rezerve edilir; tamamlanmamış modül üretimde boş/yanıltıcı menü olarak gösterilmez.

## 28. Ayrıntılı ve sürümlü formül kataloğu

Bu bölümdeki formüller StokERP donör analizindeki saf hesaplar ile Göymen ERP'nin stok, tahsis, finans ve risk kararlarını tek katalogda toplar. Bir formülün burada bulunması bütün katsayılarının canlı kullanım için onaylandığı anlamına gelmez. Her formül `FormulaId`, sürüm, kapsam, birim, yuvarlama, tolerans, geçerlilik tarihi, iş sahibi, kaynak ve golden test taşımadan production etkisi üretemez.

### 28.1 Birim ve yuvarlama sözleşmesi

| Boyut | İç birim | Sunum |
|---|---|---|
| Kalınlık, en, boy, çap | mm | mm |
| Uzunluk | m | bağlama göre m/mm |
| Ağırlık | kg | kg veya ton |
| Yoğunluk | açık birimli kg/dm³ veya kg/m³ | master kaynağıyla |
| Boya kalınlığı | µm | mikron |
| Zaman | dakika | dk/saat/gün |
| Ticari miktar | decimal + UnitId | kg/adet/metre/m²/saat/sefer |
| Para | orijinal para + yerel karşılık | ISO para koduyla |

Başlangıç önerisi: operasyonel kg toplamlarında 3 ondalık ve `AwayFromZero`; birim kg hesaplarında 4–6 hane iç hassasiyet, arayüzde 4 hane. Toplamlar satırları erken yuvarlayarak değil yüksek hassasiyetten hesaplanıp sonda yuvarlanır. Finansal yuvarlama para birimi ve muhasebe politikasının ayrı motorudur. Bu öneri saha ve muhasebe onayıyla kilitlenir.

### 28.2 Malzeme geometrisi ve teorik ağırlık

| ID | Hesap | Formül / kural |
|---|---|---|
| MAT-001 | Varsayılan çelik yoğunluğu | `7,85 kg/dm³ = 7850 kg/m³`; ürün/malzeme masterı üstündür |
| MAT-002 | Sac birim kg | `t(mm) × en(mm) × boy(mm) × yoğunluk(kg/dm³) / 1.000.000` |
| MAT-003 | Sac toplam kg | `BirimKg × Adet` |
| MAT-004 | Teorik adet | `GirilenKg / BirimKg` |
| MAT-005 | Kare dış çevre | `4 × A` |
| MAT-006 | Dikdörtgen dış çevre | `2 × (A + B)` |
| MAT-007 | Yuvarlak dış çevre | `π × DışÇap` |
| MAT-008 | Etkin çevre | Yuvarlak: `Çevre − π×t`; kare/dikdörtgen: `Çevre − 4×t` |
| MAT-009 | Profil/boru birim kg | `EtkinÇevre × t × Boy × Yoğunluk / 1.000.000` |

MAT-004 yalnız yardımcı teorik değerdir; fiilî adet veya kg'yi ezmez. Profil ağırlığında öncelik `manuel doğrulanmış → ürün/paket masterı → geometrik tahmin`dir. Köşe radyüsü, kaynak payı, gerçek et kalınlığı ve üretici toleransı bulunmadan geometrik tahmin maliyet/fiilî stok gerçeği sayılmaz.

### 28.3 Boy Kesme

| ID | Hesap | Formül |
|---|---|---|
| CUT-001 | Satır birim kg | `SacBirimKg(t, en, hedefBoy)` |
| CUT-002 | Satır plan kg | `BirimKg × Adet` |
| CUT-003 | Satır çıkış metre | `HedefBoy(mm) / 1000 × Adet` |
| CUT-004 | Ham toplam metre | `HamKg × 1000 / (t × en × yoğunluk)` |
| CUT-005 | Kalan kg | `HamKg − ΣPlanKg` |
| CUT-006 | Kalan metre | `HamToplamMetre − ΣPlanMetre` |
| CUT-007 | Bir ham boydan kesim adedi | `floor(HamBoy / (HedefBoy + KesimPayı))` hedef davranış |
| CUT-008 | Boy firesi | `HamBoy − (HedefBoy × KesimAdedi) − (KesimPayı × KesimSayısı)` hedef davranış |

Donör sistem CUT-007/008 için kerf/testere payını tam kullanmıyordu; hedefte malzeme, makine ve testere profiline göre sürümlü `KesimPayı` zorunludur. Negatif kalan normal kayıt değildir; tolerans dâhil otomatik engel veya yetkili istisnadır.

Golden örnek: `Ham 28.750 kg, 3×1500 mm; 3000×50 + 6000×55 + 4500×10 → 525,000 m; 18.545,625 kg plan; 10.204,375 kg kalan`. Kerf eklenince beklenen değer yeni sürümde ayrıca hesaplanır.

### 28.4 Dilme

| ID | Hesap | Formül |
|---|---|---|
| SLT-001 | 1 mm en kg | `HamKg / NetKullanılabilirEn(mm)` |
| SLT-002 | Satır kullanılan en | `BantEn × BantAdet` |
| SLT-003 | Satır kg | `1mmEnKg × KullanılanEn` |
| SLT-004 | Toplam kullanılan en | `ΣSatırKullanılanEn` |
| SLT-005 | Kalan en | `NetEn − ToplamKullanılanEn` |
| SLT-006 | Plan kg | `ΣSatırKg` |
| SLT-007 | Kalan kg | `HamKg − PlanKg` |
| SLT-008 | Teorik bant adedi | `floor(HamEn / BantEn)` |
| SLT-009 | En firesi | `HamEn − BantEn × Adet`; bıçak/kenar kaybı ayrıca |
| SLT-010 | Hat süresi | `AnaBobinYürüyenMetre / HatHızı(m/dk)` |

Kritik değişmez: süre, çıkış bant metrelerinin toplamından değil ana bobinin hattan geçen metresinden hesaplanır. Kullanılan en net eni aşamaz.

Golden örnek: `28.750 kg / 1500 mm net en; 150×4 + 240×3 → 1320 mm kullanılan, 180 mm kalan, 25.300 kg plan, 3.450 kg kalan`.

Süre golden örneği: `13.400 kg, 1,25×1240 → 1101,294 m ana bobin; 120 m/dk → 9,177 dk`.

### 28.5 Bobin Sarım ve Dış Çap

| ID | Hesap | Formül |
|---|---|---|
| WND-001 | Kg/metre | `Yoğunluk(kg/m³) × t(m) × BantEn(m)` |
| WND-002 | Gerekli metre | `HedefKg / KgMetre` |
| WND-003 | Dış çap | `sqrt(İçÇap² + 4 × t(mm) × GerekliMetre(m) × 1000 / π)` |
| WND-004 | Sarım yüksekliği | `(DışÇap − İçÇap) / 2` |
| WND-005 | Maksimum metre | `((MaxDışÇap² − İçÇap²) × π) / (4 × t × 1000)` |
| WND-006 | Maksimum tek bobin kg | `MaksimumMetre × KgMetre` |
| WND-007 | Grup maksimum kg | `MaksTekBobinKg × Adet` |
| WND-008 | Metre yeterli | `YürüyenMetre + ToleransMetre ≥ GerekliMetre` veya kg toleransı |
| WND-009 | Çap uygun | `HesaplananDışÇap ≤ MaxDışÇap` |
| WND-010 | Bobin kırılımı | Toplam kg → hedef kg'lik tam bobinler + tolerans üstü kalan bobin |

Donördeki `0,01 m`, `0,1 kg` ve `1000 bobin` sabitleri production politikası değildir; parametre profiline taşınır.

Golden örnek: `3×1500 mm, 15.000 kg hedef, iç çap 508 → 35,325 kg/m; 424,628 m; 1371,140 mm dış çap; 431,570 mm sarım yüksekliği`.

### 28.6 Boru / Profil

| ID | Hesap | Formül |
|---|---|---|
| TUB-001 | Toplam adet | `PaketSayısı × PaketİçiAdet` |
| TUB-002 | Toplam metre | `ToplamAdet × Boy(mm) / 1000` |
| TUB-003 | Tahmini çalışma süresi | `ToplamMetre / HatHızı(m/dk)` |
| TUB-004 | Toplam kg | `BirimKg × ToplamAdet` |

Setup, kalıp değişimi, planlı duruş ve temizlik TUB-003'ün parçası değildir; kapasite takviminde ayrıca eklenir. Birim kg ürün/paket masterından gelir; matematik tahmin alt önceliktedir.

Golden örnek: `20 paket × 50 adet × 6 m, 30 m/dk → 1000 adet, 6000 m, 200 dk çalışma`.

### 28.7 Boyama

| ID | Hesap | Formül |
|---|---|---|
| PNT-001 | Toplam adet | `PaketSayısı × PaketİçiAdet` |
| PNT-002 | Toplam profil metre | `Adet × Boy(mm) / 1000` |
| PNT-003 | Hat geçiş metre | `ToplamProfilMetre / EşZamanlıProfilAdedi` |
| PNT-004 | Tahmini süre | `HatGeçişMetre / HatHızı` |
| PNT-005 | Boyanacak alan | `ProfilÇevre(mm) × Boy(mm) × Adet / 1.000.000` |
| PNT-006 | Teorik boya litre | `Alan × Mikron / (KatıMaddeOranı × 1000) × FireKatsayısı` |

Eşzamanlı profil adedi, katı madde oranı ve fire katsayısı hard-code değildir. Renk değişimi, transfer verimi, boya yoğunluğu, kat sayısı, inceltici, tabanca/hat kaybı ve yeniden işleme ayrı parametre/gerçekleşme olmalıdır. Teorik litre maliyet ve fiilî tüketim yerine geçmez.

Golden örnek: `10×50 adet, 6 m, çevre 160 mm, hız 20 m/dk, 80 µm, katı madde 0,55, katsayı 1,10 → 480 m², 76,800 L, 50 dk`.

### 28.8 Soğuk Hadde

| ID | Hesap | Formül |
|---|---|---|
| CR-001 | Giriş metre | `HamKg × 1000 / (GirişKalınlık × En × Yoğunluk)` |
| CR-002 | Çıkış kg | `HamKg − FireKg` |
| CR-003 | Teorik uzama oranı | `GirişKalınlık / HedefKalınlık` |
| CR-004 | Çıkış metre | `ÇıkışKg × 1000 / (HedefKalınlık × En × Yoğunluk)` |
| CR-005 | Redüksiyon % | `(GirişKalınlık − HedefKalınlık) / GirişKalınlık × 100` |

Hedef kalınlık girişten küçük olmalı; fire ham kg'ye eşit veya büyük olamaz. Çok geçişli hadde, pass planı, tolerans, iş sertleşmesi ve kalite sonuçları süreç modelinde ayrıca tutulur.

Golden örnek: `28.750 kg, 1500 mm, 2,00 → 1,50 mm → 1220,807 m giriş; 1627,742 m çıkış; 1,333 uzama; %25 redüksiyon`.

### 28.9 Üretim kütle dengesi ve dağıtım

Hedef ortak denklem:

`Kütle Farkı = Toplam Fiilî Girdi Kg − (İyi Ürün Kg + İkinci Kalite Kg + Geri Kazanılabilir Yan Ürün Kg + Hurda Kg + Stok Dışı Proses Kaybı Kg + Kalan WIP Kg + Onaylı Ölçüm Düzeltmesi Kg)`

| ID | Hesap | Kural |
|---|---|---|
| BAL-001 | Donör beklenen ana ürün | `HammaddeKg − Fire/Bakiye/İkinciKaliteKg`; yalnız eski davranış referansı |
| BAL-002 | Donör fark | `BeklenenAnaÜrün − GerçekleşenAnaÜrün` |
| BAL-003 | Hedef kütle farkı | Yukarıdaki tüm kategorili denklem |
| BAL-004 | Fire % | `FireKg / HammaddeKg × 100` |
| BAL-005 | Boş gerçek miktar dağıtımı | Ağırlık: plan teorik kg → birimkg×adet → adet; son satır yuvarlama farkı |
| BAL-006 | Fiilî birim kg | `GerçekKg / GerçekAdet` |

İkinci kalite fire değildir. Geri kazanılabilir bakiye, hurda, stok dışı kayıp ve WIP ayrı mülkiyet, stok ve maliyet etkisine sahiptir. Tolerans dışı fark, açıklama ve gerekli onay olmadan kapanamaz.

### 28.10 Stok, tahsis ve projeksiyon

`Serbest Fiziki = Fiziki Mevcut − Aktif Satış Tahsisi − Aktif Üretim Girdi Tahsisi − Yükleme/Sevk Tutma − Kalite Blokları − Sistem/Hukuki Bloklar`

| ID | Hesap | Formül / kural |
|---|---|---|
| INV-001 | Fiziksel kalan | `Σ İşaret × FiilîHareketMiktarı`; her birim ayrı |
| INV-002 | Eski müsait | `Kalan − ManuelRezerv`; hedefte kullanılmaz |
| INV-003 | Serbest fiziki | Yukarıdaki hedef denklem |
| INV-004 | Tahsis kalan | `TahsisMiktarı − Tüketilen − SerbestBırakılan − İptal` |
| INV-005 | Satış satırı açık ihtiyaç | `NetAktifSipariş − Gerçekleşen − İptal` |
| INV-006 | Alış siparişi kalan | `OnaylıSipariş − İptal − KabulNet − KesinRed/İadeEtkisi` |
| INV-007 | Üretim planı kalan çıktı | `OnaylıPlanÇıktısı − GerçekleşenÇıktı − İptal` |
| INV-008 | Tahsis edilebilir paket | `floor(KalanPaket × SerbestKg / KalanKg)`; yalnız homojen pakette yardımcı |
| INV-009 | Projected ATP(t) | `SerbestFiziki + t'ye kadar uygun alış + uygun plan çıktısı − t'ye kadar açık ihtiyaç` |
| INV-010 | Müşteri koruması | `GelecekTahsis + FizikselRezervasyon`; kaynak türü dönüşürken toplam korunur |

Fiziksel, gelecek, teorik ve planlanan miktarlar birbirine eklenmiş tek “stok” sayısı olarak sunulmaz. Kg, adet, metre ve paket karma birimde toplanmaz.

### 28.11 Üretim takvimi ve kapasite

`PlannedStart(job) = max(WorkCenterAvailableTime, MaterialReadyTime, ApprovalReadyTime, PredecessorFinish)`

`RunMinutes = ProcessSpecificThroughputCalculation(planLines, versionedLineSpeedProfile)`

`PlannedFinish = WorkCalendar.AddWorkingMinutes(PlannedStart, SetupMinutes + RunMinutes + CleanupMinutes)`

Takvim vardiya, mola, tatil, bakım ve duruşu atlar. Donördeki 08:00, 480 dakika ve tek vardiya varsayımı canlı politika değildir.

### 28.12 Kantar, kabul ve plan–gerçek

| ID | Hesap | Formül |
|---|---|---|
| WGT-001 | Net tartım kg | `|Tartım2 − Tartım1|`; brüt/dara yönü metadata |
| WGT-002 | Kantar farkı | `Gerçek/KantarKg − Beklenen/İrsaliyeKg` |
| GR-001 | Mal kabul kg farkı | `GerçekKg − PlanKg` |
| GR-002 | Mal kabul adet farkı | `GerçekAdet − PlanAdet` |
| GR-003 | Büyük sapma | Ürün farkı veya `|KgFark| > PlanKg × Tolerans%` |
| VAR-001 | Plan–gerçek kg farkı | `ΣGerçekKg − ΣPlanKg` |
| VAR-002 | Plan–gerçek adet farkı | `ΣGerçekAdet − ΣPlanAdet` |
| VAR-003 | Süre sapması | `GerçekÜretimSüresi − PlanlananSüre` |

Stok kabulünde fiilî kantar mı kabul edilen irsaliye miktarı mı ana kaynak olacak sorusu ürün/işlem politikasıdır ve P0 kararıdır. Fatura miktarı stok miktarını değiştirmez; üçlü kontrol hedefidir.

### 28.13 Fiyat ve ticari hesap

| ID | Hesap | Formül / kural |
|---|---|---|
| PRICE-001 | Efektif gerçek TL/kg | `GerçekleşenTicariTutar / GerçekİrsaliyeKantarKg` |
| PRICE-002 | Efektif teorik TL/kg | `GerçekleşenTicariTutar / TeorikKg`; teorik etiketi zorunlu |
| SIM-001 | İskontolu TL/birim | `Liste × (100 − İskonto%) / 100` |
| SIM-002 | Profil USD/kg | `İskontoluTL/metre / KgMetre / Kur` |
| SIM-003 | Sac kg/adet | MAT-002 ile aynı servis |
| SIM-004 | Sac toplam TL | `ToplamKg × İskontoluTL/kg` |
| SIM-005 | USD/kg | `İskontoluTL/kg / Kur` |

Fiyat simülatörü resmî sipariş fiyat motorunun yerine geçmez. Fiyat birimi, sipariş birimi, KDV, para birimi, kur kaynağı ve geçerlilik snapshot olarak tutulur.

### 28.14 Cari risk

`Brüt Risk = Faturalı Açık Alacak + Faturalanmamış Sevkiyat + Sevk Edilmemiş Onaylı Sipariş Kalanı`

`Net Risk = Brüt Risk − Kullanılabilir DBS − Riskte Dikkate Alınan Geçerli Teminat − Politika Gereği Risk Azaltan Onaylı Dağıtılmamış Tahsilat`

DBS ve teminatın kullanılabilirlik/iskonto oranı şirket politikasıdır. Sipariş sevk edilince sipariş riskinden çıkar ve faturalanmamış sevkiyata geçer; faturalanınca açık alacağa geçer. Aynı miktar iki aşamada birden sayılmaz.

### 28.15 Finansal kaynak, dağıtım ve tarihler

`Dağıtılmamış = Kaynak Nominal Tutarı − Aktif Dağıtımlar − Terslenmiş/İptal Etkisi`

`Net Banka Tutarı = Brüt/Nominal Tutar − Komisyon − Vergi − Diğer Kesinti`

`Ödeme Gün Farkı = Banka İşlem Tarihi − Vade Tarihi`

`Valör Farkı = Valör Tarihi − Banka İşlem Tarihi`

`İç Dağıtım Süresi = Dağıtım Onay Tarihi − Banka İşlem Tarihi`

Kaynak ile dağıtım ayrıdır. Dağıtım ikinci banka/cari hareketi yaratmaz. Bir kaynak çok açık kaleme, bir açık kalem çok kaynağa bağlanabilir. Banka/kasa, çek/senet, POS, şirket kartı, DBS, avans ve mahsup aynı dağıtım motorunu kullanır; ticari kapama, nakitleşme ve risk etkileri araç tipine göre ayrıdır.

### 28.16 Kur ve fatura

Her faturada tek fatura kuru vardır. Birden fazla sipariş/irsaliye ancak şirket, cari, para, vergi, ticari koşul ve kur politikası uyumluysa birleşir. Önceki iş günü TCMB satış kuru arşivde USD için önerilen varsayımdır; kur kaynağı, saat, tatil, manuel değişiklik ve diğer para politikası P0 onayıdır. Manuel kur sebep, yetki ve audit gerektirir.

### 28.17 Formül yayın kapısı

Her formül için zorunlu kanıt:

- Değişmez FormulaId ve yeni davranışta yeni sürüm.
- İş sahibi ve onaylayan alan uzmanı.
- Girdi/çıktı adı, veri tipi, birim ve sınır.
- Yuvarlama, tolerans ve null/sıfır davranışı.
- Teorik/fiilî ayrımı.
- En az normal, sınır, geçersiz ve yüksek hacim testi.
- StokERP ile eski–yeni golden karşılaştırma.
- UI, API ve raporun aynı hesap servisini kullanması.
- Plan/iş emrinde kullanılan sürüm ve parametre snapshot'ı.
- Formül değişince geçmiş kaydın sessizce yeniden hesaplanmaması.

## 29. Yetmiş kritik kabul senaryosu

Bu senaryolar başlangıç regresyon ve kullanıcı kabul omurgasıdır. Her biri test verisi, ön koşul, kullanıcı rolü, adımlar, beklenen kayıtlar, beklenmeyen etkiler ve audit kanıtıyla ayrıntılandırılır.

### 29.1 Temel, şirket, yetki ve denetim

1. **AT-001:** Kullanıcı yalnız yetkili şirket, şube ve depo kayıtlarını görür.
2. **AT-002:** Global arama yetkisiz kaydı sonuç, öneri, toplam veya sayı olarak sızdırmaz.
3. **AT-003:** Kritik değişiklik önce/sonra, neden, kullanıcı, rol ve korelasyonla audit üretir.
4. **AT-004:** Oluşturan kişi yüksek riskli kendi işlemini görev ayrılığına aykırı onaylayamaz.
5. **AT-005:** Eşzamanlı değişiklikte ikinci kullanıcıya çatışma ve fark gösterilir.
6. **AT-006:** Başarısız kaydetme kullanıcının form verisini kaybettirmez.
7. **AT-007:** İptal ve ters işlem geçmişi silmez.
8. **AT-008:** Şirket kapsamı değişikliği açık formu sessizce başka şirkete taşımaz.

### 29.2 Cari ve ana veri

9. **AT-009:** Aynı VKN/kimlik için ikinci cari kontrollü mükerrer uyarısı üretir.
10. **AT-010:** Tek cari kimliği müşteri ve tedarikçi rollerini ayrı hesaplarla taşır.
11. **AT-011:** TRY, USD ve EUR bakiyeleri açıklamasız tek sayıya netleştirilmez.
12. **AT-012:** Cari birleştirme ilişkili belge ve audit bağlarını kaybetmez.
13. **AT-013:** Pasif cari yeni işleme seçilemez; geçmiş kayıtta görünür kalır.
14. **AT-014:** Eksik zorunlu vergi/banka belgesi doğru akışı doğru noktada durdurur.
15. **AT-015:** Ürün sınıfı ve ticari rol bağımsızdır; çelişki kontrol edilir.
16. **AT-016:** Birim dönüşümü yuvarlama/tolerans golden örneğini geçer.
17. **AT-017:** Paket standardının yeni sürümü geçmiş siparişi değiştirmez.
18. **AT-018:** Fiyat sürümü para, birim, KDV, kanal ve geçerlilik bağlamını korur.
19. **AT-019:** Excel içe aktarma eşleme/önizleme/onay olmadan kayıt yazmaz.
20. **AT-020:** Aynı Excel dosyasının tekrarı mükerrer kalıcı kayıt üretmez.

### 29.3 Satış ve satın alma

21. **AT-021:** Satış siparişi fiyat alt sınırını aşarsa doğru onaya gider.
22. **AT-022:** Risk limiti aşımı siparişi sessizce serbest bırakmaz.
23. **AT-023:** Onaylı siparişte kritik satır değişikliği eski onayı geçersizleştirir.
24. **AT-024:** Kısmi teslimat sipariş kalanını doğru tutar.
25. **AT-025:** Satış ve satın alma belgeleri operasyon listelerinde karışmaz.
26. **AT-026:** Satın alma siparişi fazla/eksik teslimat toleransını uygular.
27. **AT-027:** Tedarikçi fiyat/miktar farkı kullanıcı onayı olmadan faturaya geçmez.
28. **AT-028:** İade kaynak sipariş, teslimat ve fatura bağını korur.

### 29.4 Stok, etiket ve tahsis

29. **AT-029:** Fiziksel mevcut ile mülkiyet sahibi ayrı doğru gösterilir.
30. **AT-030:** Müşteri malzemesi şirket serbest stoku gibi satılamaz.
31. **AT-031:** Fasoncudaki şirket malzemesi doğru emanet/lokasyon durumunda görünür.
32. **AT-032:** Aktif tahsis serbest miktarı düşürür ama stok hareketi oluşturmaz.
33. **AT-033:** Aynı etiket izin verilenden fazla aktif tahsis edilemez.
34. **AT-034:** Tahsis serbest bırakılınca kalan doğru geri döner.
35. **AT-035:** Kalite bloğu serbest miktardan doğru düşer.
36. **AT-036:** Yüklemeye tutulan miktar başka siparişe kaynak olamaz.
37. **AT-037:** Sayım farkı neden ve onay olmadan kesinleşmez.
38. **AT-038:** Etiket bölme/birleştirme soy ağacını korur.
39. **AT-039:** Lot/rulo/ısı/MTC bağı girişten çıkışa izlenir.
40. **AT-040:** Serbest fiziksel miktarın her bileşeni kaynak kayda açılır.

### 29.5 Üretim ve kalite

41. **AT-041:** Kaynağı hazır olmayan plan hazır durumuna geçemez.
42. **AT-042:** Fiilî üretim tüketimi tahsis ve fiziksel mevcudu aşamaz.
43. **AT-043:** Boy kesme çıktıları + kalan + fire girdiyi tolerans içinde dengeler.
44. **AT-044:** Dilme kombinasyonu genişlik ve kenar firesini dengeler.
45. **AT-045:** Boru/profil adet–boy–kg dönüşümü golden örneğini geçer.
46. **AT-046:** Boyama girdisi iyi/ikinci kalite/yeniden işleme/kayıp ile dengelenir.
47. **AT-047:** Soğuk hadde giriş/çıkış/proses kaybı ortak dengeyi sağlar.
48. **AT-048:** Tolerans dışı kütle farkı onaysız kapanamaz.
49. **AT-049:** Yeni reçete/formül/tolerans sürümü geçmiş gerçekleşmeyi değiştirmez.
50. **AT-050:** Çıktı etiketi bütün kaynak girdilere geri izlenir.
51. **AT-051:** Kalite karantinasındaki çıktı sevkiyata açılamaz.
52. **AT-052:** Düzeltme önceki gerçekleşmeyi silmez; ters/düzeltme zinciri kurar.

### 29.6 Lojistik, belge, finans ve AI

53. **AT-053:** Kantar neti iki tartım ve brüt/dara yönüyle tutarlıdır.
54. **AT-054:** Fiilî yüklenen etiketler tahsis ve sevkiyat planıyla uzlaştırılır.
55. **AT-055:** Aynı sevkiyat gerçekleşmesi iki kez irsaliye/faturaya bağlanamaz.
56. **AT-056:** OCR/AI çıkarımı ham belge ve kullanıcı düzeltmesiyle izlenir.
57. **AT-057:** Aynı belge hash'i ve benzer anahtarlar mükerrer uyarısı üretir.
58. **AT-058:** Ödeme/tahsilat açık kalem ve banka hareketine çift yönlü bağlanır.
59. **AT-059:** AI kalıcı değişikliği fark önizlemesi ve yetkili onay olmadan uygulamaz.
60. **AT-060:** AI yetkisiz şirket verisini cevap, özet, araç çağrısı veya kaynakta sızdırmaz.

### 29.7 Bu konuşmayla eklenen senaryolar

61. **AT-061:** Üretim planı girdisi değişince `Hesapla` düğmesi olmadan doğru önizleme güncellenir; kayıt oluşmaz.
62. **AT-062:** Eksik/geçersiz üretim girdisinde eski sonuç güncel gibi kalmaz; neden ve eksik alan görünür.
63. **AT-063:** Stok satırından Üretim Planla, Tahsis Revize ve Yükleme Talimatı işlemleri doğru satır bağlamıyla açılır.
64. **AT-064:** Alış siparişi kalanı `Kaynak/Durum = Alış Siparişi` görünür fakat mal kabulden önce fiziksel/serbest stoğu artırmaz.
65. **AT-065:** Tek belge bir kez saklanır; stok/cari/muhasebe/finans ekranları aynı olay ve dosya referansını gösterir.
66. **AT-066:** PDF adı Türkçe karakterleri korur, yasak dosya karakterlerini güvenli dönüştürür ve gömülü fontla doğru render olur.
67. **AT-067:** WhatsApp Business olmadan Paylaş işlemi desteklenen cihazda sistem paylaşım penceresini açar; destek yoksa indir + metin kopyala çalışır.
68. **AT-068:** AI kişi bazlı yetki taslağı ve risk/fark üretir fakat yetkili yönetici onayı olmadan izin değişmez.
69. **AT-069:** Yetkili global arama temsilî üç yıllık hacmin üç katında belirlenen p95 hedefini geçer; derin sayfalama veri atlamaz/tekrarlamaz.
70. **AT-070:** Yedekten nokta-zaman geri yükleme ve nesne dosyası bütünlüğü gerçek provada mutabık sonuç verir.

## 30. Teknoloji, performans ve güvenilirlik karar özeti

### 30.1 Önerilen başlangıç stack'i

| Katman | Seçim / yön |
|---|---|
| Web/PWA | React + TypeScript, ortak bileşen ve sanallaştırılmış grid |
| Backend | ASP.NET Core / .NET 10 LTS |
| Ana veri | PostgreSQL 18 önerisi; zorunlu altyapı varsa SQL Server PoC karşılaştırması |
| Mimari | Modüler monolit + worker/connector sınırları |
| Dosya | S3 uyumlu nesne deposu + DB metadata/hash |
| Arama 1 | B-tree/unique + PostgreSQL full-text + `pg_trgm` + GIN/GiST |
| Arama 2 | Ölçüm sonrası gerekirse OpenSearch; türetilmiş ve yeniden kurulabilir indeks |
| Kimlik | OIDC/SSO'ya hazır, MFA, RBAC + ABAC |
| Olay | ACID transaction, outbox/inbox, idempotency ve sürümlü event |
| Realtime | SignalR benzeri görev/bildirim güncellemesi |
| Dağıtım | Container, reverse proxy, ayrı dev/test/staging/production |
| İzleme | Merkezî log, metrik, trace, iş korelasyonu ve alarm |

### 30.2 Arama performansı

- Belge no, barkod, ürün/cari kodu: kesin indeksli arama.
- Ad ve yazım hatası: trigram benzerliği.
- Açıklama/OCR: tam metin arama.
- Şirket ve yetki filtresi indeks/projeksiyonun parçasıdır.
- Sonuç açılırken backend yetkiyi yeniden kontrol eder.
- Liste sunucu tarafı filtre/sıralama ve cursor/keyset sayfalama kullanır.
- Büyük rapor/Excel/PDF arka plan işidir.
- Milyonlarca satır tarayıcıya taşınmaz; rastgele çoklu `LIKE` sorgusu kullanılmaz.

Başlangıç SLO önerisi: sık global arama p95 ≤ 300 ms; karmaşık tam metin p95 ≤ 1 s; liste ilk sayfa p95 ≤ 1 s; harici entegrasyon dışı temel yazma p95 ≤ 1 s; aylık kullanılabilirlik hedefi %99,9. Faz 0 gerçek hacim testiyle kilitlenir.

### 30.3 Güvenilirlik değişmezleri

- Kritik veri yalnız UI doğrulamasına bırakılmaz; DB constraint/foreign key/unique index uygulanır.
- Her komut idempotency key ve beklenen kayıt sürümü taşır.
- Sınırlı retry, timeout, circuit breaker ve backpressure bulunur.
- Audit normal uygulama kullanıcısı tarafından değiştirilemez.
- Depoda ve aktarımda şifreleme; secret kasası; hassas alan maskeleme uygulanır.
- Dosya MIME/doğrulama, zararlı içerik tarama, hash ve sürümleme kullanır.
- Nokta-zaman DB geri dönüşü, nesne depo sürümü ve düzenli geri yükleme provası vardır.
- RPO/RTO şirket onayı olmadan production açılmaz.
- AI veya entegrasyon kesilirse temel ERP çalışır ya da görünür kontrollü kuyrukta bekler.

### 30.4 Güncel resmî teknik dayanaklar

- [.NET destek politikası](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [PostgreSQL güncel dokümantasyonu](https://www.postgresql.org/docs/)
- [PostgreSQL tam metin arama](https://www.postgresql.org/docs/current/textsearch-controls.html)
- [PostgreSQL pg_trgm](https://www.postgresql.org/docs/current/pgtrgm.html)
- [W3C Web Share API](https://www.w3.org/TR/web-share/)
- [OpenSearch shard/replica ve ölçek yönü](https://docs.opensearch.org/latest/getting-started/intro/)

Sürüm numaraları geliştirme başlangıcında yeniden doğrulanır. Teknoloji, gerçek veri ve altyapı PoC'si geçmeden geri dönülmez kurumsal sözleşme sayılmaz.

## 31. Uygulamaya geçiş emri

### 31.1 Şimdi yapılacak iş

Yeni ekran fikirleri toplamaya devam etmek yerine Faz 0 başlatılır. Bütün modüller aynı anda kodlanmaz. İlk çalışan dikey dilim:

`Giriş/Şirket → Kullanıcı Yetkisi → Benim İşlerim → Cari → Ürün → Satış Siparişi → AI Eksik Kontrolü → Onay → Türkçe Adlı PDF → Merkezi Arşiv → Kullanıcı Başlatmalı Paylaşım → Global Arama → Audit`

Bu dilim şu omurgaları birlikte ispatlar:

- Ortak lacivert kabuk ve sayfa modeli.
- Şirket izolasyonu ve RBAC/ABAC.
- AI'nın yetkili veriyle taslak üretmesi fakat onaylamaması.
- Ana veri ve sipariş durum makinesi.
- Belgeden PDF'ye, arşive ve paylaşıma kadar belge yaşam döngüsü.
- Türkçe karakter ve dosya adı.
- Global arama ve performans.
- Audit, idempotency, backup/restore ve gözlemlenebilirlik.

Bu dikey dilim geçmeden üretim/finans ekranlarının geniş paralel geliştirmesi başlatılmaz. Formül golden seti ve ekran akışları paralel analiz edilebilir.

### 31.2 İlk iki haftada işletmeden alınacak girdiler

1. Hukuki şirket, tesis, şube, depo ve lokasyon listesi.
2. Pilot 5–10 gerçek kullanıcı; görev, şirket, depo, tutar ve onay sınırları.
3. Kimlerin aynı işlemi hazırlayıp onaylayamayacağı görev ayrılıkları.
4. Gerçekçi fakat maskelenmiş cari, ürün, sipariş, irsaliye, fatura, sözleşme ve üretim örnekleri.
5. StokERP şema/veri miktarı ve son üç yıllık büyüme.
6. Cari, ürün, sipariş satırı, stok hareketi, audit, belge ve OCR hacmi tahmini.
7. Beklenen eşzamanlı kullanıcı ve yoğun saatler.
8. Sunucu, işletim sistemi, lisans, ağ, yedek ve barındırma envanteri.
9. Şirket logo/marka varlıkları ve lacivert renk yönü onayı.
10. Resmî PDF örnekleri, belge adları, numara serileri ve imza/kaşe kuralları.
11. İlk pilotta her gün kullanılan 20 kritik görev.
12. RPO/RTO ve dış erişim/MFA beklentisi.

Gerçek kişisel, finansal ve ticari veri geliştirme ortamına maskesiz kopyalanmaz.

### 31.3 İlk 90 gün

| Dönem | Ana çıktı | Çıkış kanıtı |
|---|---|---|
| Gün 1–15 | Karar kurulu, P0 sahipleri, veri/yetki/hacim keşfi | Sahipli karar ve veri envanteri |
| Gün 16–30 | Sözlük, veri sahipliği, API/olay sözleşmesi, tıklanabilir kabuk | Kullanıcı ve mimari onayı |
| Gün 31–45 | React/.NET/DB/kimlik/dosya/arama/AI/yetki PoC | Hedef hacim, güvenlik ve restore kanıtı |
| Gün 46–60 | İlk gerçek dikey dilim | Otomatik test + audit + PDF/paylaşım |
| Gün 61–90 | 5–10 kullanıcı pilotu ve Faz 1 yeniden tahmini | UAT, p95, hata, geri bildirim, kapalı P0 |

### 31.4 Faz 0 çıkış kapısı

- Onaylı terminoloji/veri sözlüğü.
- Veri sahipliği ve sistem-of-record matrisi.
- Rol, kişi kapsamı, görev ayrılığı ve onay matrisi.
- Gerçek hacimle arama ve eşzamanlı kullanıcı PoC'si.
- Türkçe PDF, dosya adı ve belge arşivi kanıtı.
- Kullanıcı başlatmalı paylaşım ve fallback kanıtı.
- AI yetki taslağı; AI'ın yetki veremediği güvenlik testi.
- Formül golden seti ve en az bir üretim motoru pilotu.
- Idempotent komut, outbox/inbox ve audit kanıtı.
- Backup/restore tatbikatı.
- Barındırma, RPO/RTO ve yayın/rollback kararı.
- P0/P1 kritik açık bulunmaması veya ilgili kapsamın başlamaması.

## 32. Kapsam durumu ve nihai kontrol sözleşmesi

### 32.1 Kilitli yönler

- Tek ve bağımsız Göymen ERP; StokERP donör/referans.
- Tek olay/tek kayıt, modüllerde türetilmiş görünüm.
- Ortak sayfa kabuğu ve lacivert navigasyon yönü.
- ERP-first AI; şemalı araç, önizleme, yetki, onay ve audit.
- Satış/Alış operasyon ayrımı; bağlamsız genel irsaliye yok.
- Tek cari kimliği; finans/muhasebe cari kopyası yok.
- Tek Stok/Malzeme Pozisyonu; mülkiyet, custody, konum ve ticari bağ ayrı.
- Merkezî çoktan çoğa tahsis.
- Beş üretim motoru + ortak planlama/denge kabuğu.
- Değer değişiminde düğmesiz deterministik üretim önizlemesi.
- Stok ekranından üretim, tahsis revizyonu ve yükleme talimatı.
- Gelecek alışların stokta `Alış Siparişi` olarak ayrı gösterimi.
- Kaynak finansal hareket ile açık kalem dağıtımının ayrılması.
- Muhasebe ve Finans/Hazine sorumluluk ayrımı.
- Tek dosya–çok kayıt referanslı belge kasası.
- Türkçe PDF/dosya adı.
- İlk WhatsApp kapsamının kullanıcı başlatmalı paylaşım olması.
- AI destekli fakat yönetici onaylı kişi/rol yetkilendirme.
- Modüler monolit, ölçümlü performans ve geri yükleme kanıtı.

### 32.2 Kodlama öncesi saha/politika kararı gereken başlıklar

Master raporun 19. bölümündeki 37 P0 kararın tamamı geçerlidir. Özellikle:

- Mal kabulde stok miktarı kaynağı: kabul irsaliyesi mi fiilî kantar mı?
- Formül tolerans, yuvarlama, yoğunluk, hız, kerf, boya ve paket parametreleri.
- Riskte DBS/teminat ve dağıtılmamış tahsilat oranları.
- Kur kaynağı/saat/tatil/manüel değişiklik.
- Kantar/yükleme toleransları.
- Kısmi hammaddeyle üretim başlangıcı.
- RPO/RTO ve on-prem/cloud yönü.
- SQL Server zorunluluğu olup olmadığı.
- İlk üç yıllık veri hacmi ve p95 hedeflerinin işletme onayı.
- İkinci onay gerektiren yetki ve parasal eşikler.
- E-belge/vergi/Mikro entegrasyonunun ilk tek gerçeklik sınırı.
- StokERP modül bazlı cutover tarihleri.

### 32.3 Tamamlanmış sayılma koşulu

Göymen ERP ancak:

- bu rapordaki hedef akışlar gerçek kullanıcıyla çalışıyor,
- bütün P0 kararlar kayıtlı,
- 70 kritik senaryo geçiyor,
- performans SLO'ları temsilî hacimde sağlanıyor,
- yedek geri yüklenebiliyor,
- yetkisiz veri sızmıyor,
- formüller golden testli,
- belgeler ve kayıt etkileri uçtan uca izleniyor,
- AI onay/yetki sınırını aşmıyor,
- StokERP bağı olmadan çalışma ve mutabakat kanıtlanıyor,
- açık P0/P1 hata bulunmuyor

ise ilgili faz/modül için “hazır” sayılır. Görsel prototip, ekranın açılması veya yalnız kaynak kodun yazılmış olması tamamlanma kanıtı değildir.

## 33. Nihai tek cümlelik proje kararı

Göymen ERP; şirketin satış, satın alma, ürün/hizmet, cari, stok, tahsis, beş üretim süreci, kalite, lojistik, kantar, belge, muhasebe, finans, hazine, rapor, kullanıcı ve AI işlerini tek veri gerçekliği üzerinde; sade lacivert ortak arayüz, anlık deterministik hesap, hızlı yetkili arama, Türkçe belge arşivi, açıklanabilir AI, kontrollü onay ve eksiksiz audit ile yürüten bağımsız kurumsal işletim sistemi olarak geliştirilecektir.
