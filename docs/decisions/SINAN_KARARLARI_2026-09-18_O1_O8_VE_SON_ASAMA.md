# Sinan kararları — 18 Eylül 2026: Ö1–Ö8 ve son geliştirme aşaması

**Durum: KULLANICI İŞ KARARI KABUL EDİLDİ. Kod, veri tanımlama, bağımsız inceleme, gerçek kullanıcı kabulü ve production aktivasyonu ayrı durumlardır.**

Kaynak: Sinan bu Business konuşmasında Ö1–Ö7 için her birine “önerin doğrultusunda” dedi. Ö8 için “resmi belge düzenlemeye kapalı olsun evet ama erp içindeki cari bakiye durumları değişssin” ekini verdi. İleri kapsamı “en son aşamada geliştirelim” diye sıraladı. [Doğrudan aktarım ve iş emri](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5722637822).

Onaylanan öneri: [5722291162](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5722291162) ve `docs/delivery/DECISION_PACK.md` içindeki `DECISION-ROUND-2026-09-18-v1` (`71d9cae85442548035f1e7d31e8084991cd7d988`). Bu belge o sekiz önerinin kullanıcı kararı bekliyor durumunu aşağıdaki kapsamda supersede eder; tarihsel metin silinmez. Ö8'in kullanıcı eki önceki geniş resmî-belge kısıtı yorumlarından üstündür. Yeni OPEN/CASE/görev kimliği oluşturulmadı.

## 1. Kabul edilen kararlar ve kalan gerçek girdiler

| Başlık | KABUL EDİLEN İŞ KARARI | Seçilmiş sayılmayan / kalan somut girdi |
|---|---|---|
| Ö1 — ilk doğrulama ailesi | GDC'de bant/rulo, kg→kg alış–stok–satış yolu. Kalınlık/en pozitif mm; kalite ve kaplama durumu kontrollü. Kaplamasız≠bilinmiyor. Diğer aileler/şirketler kapsamda. | İlk gerçek ticari ürün, kalite/ölçü ve sözlükler mevcut yetkili kart/etiket/belgeden; araştırma örneği veya fixture otomatik gerçek master değil. |
| Ö2 — hassasiyet/profil | Kg ve nominal mm en fazla 3 ondalık, adet tam sayı; TL/USD/EUR nihai tutar 2, birim fiyat gerekirse en fazla 6 ondalık. Gereksiz sıfır gösterme, sahte ölçüm kesinliği ve sessiz basamak kesme yok. İlk kabul GDC/TRY; profil onaydan itibaren sürümlü, bitişsizse açıkça süresiz. | Birim fiyatın tümü 6 haneye zorlanmaz; ölçüm cihazı çözünürlüğü korunur. Son ticari yuvarlama yöntemi/aşaması, vergi oranı/kur seçimi ondalık desteğinden türetilemez. Teknik UUID/version uygulayıcı işidir. |
| Ö3 — lot/sertifika | Lot/etiket/soy ağacı zorunlu minimum. Heat/coil/MTC mal/alış/müşteri şartına bağlı; zorunlu belge eksikse fiziksel geliş kaydedilir, belge/kalite bekliyor; gerekli üretim/sevk serbestliği yok. Ürün kartı henüz gelmemiş lot sertifikasına bağlanmaz. | Gerçek heat/sertifika üretilmez veya uydurulmaz; sözleşmeyle zorunlu uygunluk genel override ile atlanmaz. |
| Ö4 — Finance onayı | Zaten gerçekleşmiş rutin tahsilatı dar yetkili Finans operatörü kaydeder; her rutin kayıt ikinci kişi beklemez. Ödeme talimatı, kritik banka bilgisi, kesin kayıt tersleme, dönem açma ve risk istisnası hazırlayandan farklı tek yetkili onayında. | Gerçek kişi/şirket assignment'ı ayrıca. Başlangıçta yeni genel parasal hard-gate sayısı yok. Gelecekte tutar eşiği şirket/işlem/para bazlı ayrıca seçilir. |
| Ö5 — tarih/dönem/kur | İşlem/belge tarihi, giriş zamanı ve valör ayrı. Yetkili açık dönemde kayıt, kapalı dönemde kontrollü düzeltme. Önceki iş günü TCMB satış başlangıcı, uygun önceki yayımlanan kaydın tarihi/kaynağı görünür; manuel istisna gerekçe/yetki/audit. Onaylı geçmiş snapshot korunur. | Test/UAT dönemi canlı geçmiş dönemleri açmaz. Gerçek kişi yetkileri, canlı açık dönem ve ilk kaynak profili ayrıca kaydedilir. Banka/anlaşma, ticari ve değerleme kur amaçları birleştirilmez. |
| Ö6 — ortak Cari/onay/hesap | Kritik ortak Party için dar grup-master onay yetkisi, maker≠checker tek onay; her şirketten ayrı onay yok. Hedef şirket hesabı otomatik B değil, izinli açık “Bu şirkette hesabını oluştur” eylemi. GET/arama/seçim hesap yaratmaz. | Grup-master onayı bütün şirketlerin bakiyelerini görme yetkisi değildir; gerçek approver/vekil assignment'ı ayrıca. Yeni rolün teknik kodu veya grant otomatik uygulanmaz. |
| Ö7 — tolerans/formül yönetimi | Kantar farkı, ticari teslim farkı ve proses dengesi ayrı. Süreç sahibi gerçek örneklerle hat–ürün–işlem bazlı sayı önerir; kullanıcı sayısal tabloyu onaylar. Tolerans içi rutin akış ek onaysız; istisna gerekçe/tek onay; yetkili plan override yalnız o snapshot'ta. | **%1/%2/%3 ve ±%2 nihai olarak ONAYLANMADI.** Onay yöntem/sorumluluk/iş akışı içindir; saha sayıları/formül profili somut veriyle tamamlanır. |
| Ö8 — placeholder ve iç bakiye | Gerçek VKN yoksa NULL/placeholder ve resmî belge düzenleme kapalı; **ERP içi yetkili cari hareket/bakiye takibi açık kalır.** Kimlik değişikliğinde belge/fark/gerekçe/farklı kişinin tek onayı; eski belgeler korunur. | GIN gerçek unvan/VKN kaynaktan gelmeden resmî düzenleme açılmaz. Ö8 mevcut bütün şirketleri/kişileri otomatik aktive veya yetkilendirmez. GDC/GSP geliştirmesi bu veri eksikliğiyle durmaz. |

## 2. Ö8'in bağlayıcı uygulama sınırı — belge yetkisi ile iç cari işlevi ayrıdır

`InvoiceCapable=false` veya resmî düzenleme için eksik kimlik, iç cari hesabını salt-okunur veya donmuş bakiye hâline getiren genel bir anahtar olarak kullanılamaz. Hangi işlemin hangi bağımsız kabiliyeti gerektirdiği kendi API/servisinde doğrulanır.

1. Yetkili kullanıcı ilgili mevcut şirket bağında iç cari/Finance kaynak hareketi ve onaylı açılış/devir/düzeltme kaydedebilir; tahsilat/ödeme kaydı ve dağıtım açık kalem/bakiye durumunu kendi iş anlamıyla değiştirir. İç işlem kimliği, şirket, aktör, tarih, para ve kaynak gerekçesi kaybolmaz.
2. İç borç/alacak veya dekont kaydı **resmî fatura/e-belge değildir**. Ekran ve çıktı bu niteliği açık gösterir; resmî düzenleme/numara/entegrasyon yolu eksik resmî kimlikle çalışmaz. Gerçek bir dış belgenin sistemde referans olarak tutulmasıyla sistemin resmî belge düzenlemesi karıştırılmaz.
3. Bakiye ham sayı üzerine yazılmaz; onaylı hareketlerden türetilir. İade/ters/düzeltme kaynak işlem bağlantısıyla yapılır. Taslak sipariş veya fiyat teklifi kendi başına kesin cari hareket yaratmaz; taahhüt/risk görünümü ayrı.
4. Dağıtım ikinci para hareketi değildir. Aynı kaynak farklı belge/ekran/retry ile ikinci kez işlenmez; audit/idempotency/expectedVersion ve şirket/para ayrımı korunur. Kullanıcı onayı şirketler arası otomatik mahsup değildir.
5. Placeholder'ın gerçek kimliğe tamamlanması eski iç hareketleri silmez, yeniden üretmez ve geçmiş belge snapshot'ını değiştirmez. Resmî düzenleme ancak gerekli gerçek bilgi ve ayrı yetkili aktivasyon koşulları sağlandığında açılır.

### Ö8 için uygulanacak örnek kabul (tasarım; henüz çalıştırılmadı)

Onaylı test şirketi/hesabı, `InvoiceCapable=false`, başlangıç iç bakiye 0: izinli iç borç kaynağı 10.000 TL → bakiye 10.000; 4.000 TL gerçekleşmiş tahsilat → kalan 6.000; aynı kaynak/key replay → hâlâ 6.000. Dağıtım bu tahsilatı ikinci kez düşmez. Yetkisiz kullanıcı, yabancı şirket veya resmî belge düzenleme denemesi ilgili sınırda reddedilir. Onaylı kaynak düzeltmesi/tersleme iz bırakır; VKN tamamlamak tek başına 6.000 TL bakiyeyi değiştirmez. Sayılar sentetik test girdisi, gerçek hareket veya vergi yorumu değildir.

## 3. Geliştirme sırası — ertelenenler iptal değil, SON AŞAMA

**Önce çekirdek teslim:** Ürün, Cari, Organizasyon, Sipariş/Satış, Finans, Stok/Etiket/Tahsis, Satınalma, beş motorlu Üretim, Sevkiyat, temel Genel Muhasebe ve gerekli kalite/belge/onay/AI/raporlama altyapısı. Gerçek kullanıcı akışları tamamlanır; yalnız taslak ekran/servis tamamlanma sayılmaz.

**Son geliştirme aşamasında:** ayrıntılı maliyet; ileri nakit ve 13 haftalık planlama; çek kırdırarak USD alma–kâr koruma optimizasyonu; yaklaşık 15 günlük üretim çevrimi optimizasyonu; İK/Bordro, tam Bakım, ileri CAPA, CRM, Sabit Kıymet gibi sonraki tam modüller. Bunlar toplam proje hedefinden çıkarılmaz. Önceki “bu çekirdek sürümde aktif değil” cümleleri erken evre sınırıdır, kalıcı iptal değildir.

Bu sıra son aşamanın bütün alan/formül/yasal/rol kurallarının bugün seçildiği veya hemen yeni uygulayıcılar açılacağı anlamına gelmez. Çekirdek kabul ve hazır bağımlılıklar sonrası mevcut Koordinatör son evreyi görevleştirir; o zaman hâlâ gerçek iş kararı gerekiyorsa yalnız somut eksik sorulur. Aynı geliştirme hedefi için tekrar genel “bunu yapalım mı?” sorusu gereksizdir.

Gerçek miktar, lot, satınalma/fatura tutarı/para, sarf/süre/WIP ve kaynak ilişkileri şimdi tutulur. Temel para/fiyat/kalite/muhasebe ertelenmez. Bilinmeyen maliyet null/değerlenmedi, sıfır maliyet veya hesaplanmış kâr değildir. Tam mali değerleme kapanışı eksikken operasyonel kabul ile mali değerleme kabulü ayrı raporlanır.

## 4. Kapanan seçimler ile kalan girdilerin ayrımı

Ö1–Ö8 yaklaşım onayı alındı; tekrar aynı seçimler sorulmaz. Kalanlar politika geri açılması değil belirli gerçek veri veya ayrıca onaylanacak değerlerdir: ilk gerçek ürün/katalog, gerçek kişi/şirket görevlendirmeleri, canlı açık dönem sınırları, saha tolerans/formül sayıları, gerçek GIN bilgileri, uygulanacak son yuvarlama yöntemi/aşaması için mevcut onaylı kaynak veya dar tamamlayıcı karar.

Ö2 onayı ilk profil yapısı/hassasiyet ve geçerlilik yönünü bağlar; hayalî üretim malı/stok veya gerçek grant yaratma hakkı değildir. Ö4 onayı tutar/rol/para matrisinin iş akışını netleştirir; bütün OPEN-009 satırındaki kişi/atanmış limitleri tamamlandı saymaz. Ö7 yöntemi kabul; OPEN-004/005/007 sayısal saha kapanışı ayrı. Ö8 placeholder yaklaşımı ve iç bakiye açık; gerçek şirket kimliği eksikliği ayrı.

## 5. Koordinatöre mevcut iş üzerinden uygulama

İlk olarak Product+Settings sahipleri kabul edilmiş aile/profil/hassasiyeti gerçek kaynağa bağlayan dar işi; Finance sahibi Ö4–Ö5 kayıt/onay akışını; Cari+Organization+Finance mevcut sahipleri Ö6–Ö8 kapsam ayrımını görevleştirir. Merkezi dosyada tek yazar. Normal uygulamada gerçek provider ve yetkili test actor'ı; fixture sadece izole test kanıtı. Eski spec'lerdeki “kullanıcı kararı bekliyor” bu yeni karara göre dar güncellenir; yeni genel tasarım raporu veya ikinci kabul kuyruğu yok.

Güncel main/PR head/owner/öncül doğrulanmadan duplicate iş yok. Önceden atanmış işi kesme veya aynı bitmiş testi yeniden başlatma. K3 bağımsız review, fresh CI ve gerekli SQL/browser aynı. Ödeme kilidi ve Chromium kurulumu kullanıcı tarafından tamamlandı diye varsayılmaz. Gerçek veri/grant/migration/deployment değişikliği ayrı yetkili teknik iş ve doğrulama gerektirir. Business bu dokümanla gerçek hesap bakiyesi veya kaynak kodu değiştirmedi.
