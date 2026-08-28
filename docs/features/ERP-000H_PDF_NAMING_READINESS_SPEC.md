# ERP-000H — Türkçe PDF ve Dosya Adlandırma PoC Hazırlık Sözleşmesi

## 1. Belge durumu

| Alan | Sonuç |
|---|---|
| Hazırlık incelemesi | **GO** — karar boşlukları ve kabul kanıtları tanımlanabilir |
| Kodlama / paket seçimi / migration | **NO-GO** |
| Production davranışı | **NO-GO** |
| Kanonik mevcut kabul senaryosu | `AT-066` — `Taslak`; kanıt alanı boş |
| Kanonik açık karar | `OPEN-019` |
| Bu çalışmanın kapsamı | Yalnız dokümantasyon; PoC giriş kapısını açıklama |

Bu belge, `ERP-000H` için implementation spec veya tamamlanmış özellik beyanı
değildir. Amaç; Türkçe Unicode PDF üretimi ve mantıklı dosya adlandırmasının
hangi değişmezlere, kararlara ve gerçek kanıtlara ihtiyaç duyduğunu belirlemek;
henüz kararlaştırılmamış ayrıntıların kod içinde sessiz varsayılmasını
engellemektir.

İncelenen `main` snapshot'ında `ERP-000H` görevi `ready` görünse de yalnız
`ERP-000A` bağımlılığı ve tek cümlelik golden test kabulü, güvenli uygulama için
yeterli değildir. Bu dal manifestte ERP-000H'yi `blocked` yapar; B/C/D/E/G
bağımlılıklarını, `OPEN-019` ve task-local `ERP-000H-DECISION-PACK` blockerını
makine-okunur hale getirir. Async yol seçilirse ERP-000F ayrıca eklenmelidir.

## 2. Bağlayıcı kaynaklar ve doğrulanmış gerçekler

Kaynak önceliği repo `AGENTS.md` kuralına göre uygulanır.

| Kaynak | Bu çalışma için bağlayıcı sonuç |
|---|---|
| `DEC-DOC-001` | Belge modeli Blob → Mantıksal Belge → Belge Sürümü → Kayıt Bağlantısı katmanlıdır. |
| `DEC-DOC-003` | Belge üzerine yazılmaz; yeni çıktı gerektiğinde sürüm ve geçmiş korunur. |
| `DEC-DOC-004` | Kullanıcıya görünen PDF adı Türkçe karakterlidir; teknik kimlik dosya adından bağımsızdır. |
| `DEC-DOC-008` | Ticari düzeltme ve saklama/imha birbirinden ayrıdır; sessiz fiziksel silme yoktur. |
| `DEC-DOC-009` | Dosya güvenliği, kısa süreli erişim ve hassas dışa aktarma yetkisi zorunludur. |
| Master v3 §7.9 | Görünen ad Unicode NFC/UTF-8 uyumlu olmalı; Türkçe karakterler korunmalı; yasak karakterler güvenli dönüştürülmelidir. |
| Master v3 §7.9 | Önerilen ad kalıbı belge türü, belge no, cari kısa ad, tarih, sürüm ve durum taşır. |
| Master v3 §25.7 | Sistemce üretilen dosya Merkezi Belge Kasasına girer; kullanıcı adı ile teknik kimlik ayrıdır. |
| `AT-066` | Türkçe karakter, yasak karakter dönüşümü ve gömülü fontla doğru render birlikte kanıtlanmalıdır. |
| `OPEN-019` | PDF font ailesi ve marka/antet varlıkları henüz kararlaştırılmamıştır; font pakete eklenemez. |
| `UI_DESIGN_SYSTEM.md` §10 | Kullanıcı metni Türkçe karakterleri korur; görünen ad anlaşılır Türkçe metin ve değişmez tanımlayıcı taşır. |

Master rapordaki dosya adı kalıbı bir başlangıç önerisidir; belge türü sözlüğü,
durum sözlüğü, kesin ayraç, azami uzunluk, collision eki, font ailesi, antet ve
pilot belge şablonu henüz kabul edilmiş production sözleşmesi değildir.

## 3. Amaç ve sınır

ERP-000H'nin amacı, seçilecek gerçek bir PDF renderer üzerinde şu teknik
iddiaları kanıtlayacak dar bir PoC hazırlamaktır:

1. Türkçe metin, PDF içeriğinde ve kullanıcıya sunulan dosya adında bozulmaz.
2. Kullanılan font PDF içine gömülür veya lisansın izin verdiği şekilde
   subset edilir; görüntüleyiciye özel sessiz font fallback'i oluşmaz.
3. Aynı kanonik girdiler aynı görünen adı ve aynı görünür belge içeriğini
   üretir.
4. Kullanıcıya görünen Türkçe ad, nesne deposundaki teknik anahtar veya belge
   kimliği olarak kullanılmaz.
5. Dosya adı path traversal, header injection, kontrol karakteri, işletim
   sistemi yasakları ve collision nedeniyle üzerine yazma üretmez.
6. Üretilen artifact; şirket, kaynak kayıt sürümü, template sürümü, font
   varlığı ve audit bağlamıyla izlenebilir.
7. Gerçek render, font gömme, text extraction, görsel karşılaştırma ve temel
   erişilebilirlik kanıtları otomatikleştirilebilir.

### 3.1 Bu hazırlık çalışmasının dışında kalanlar

- PDF kütüphanesi, ticari/açık kaynak provider veya NuGet/NPM paketi seçmek.
- Font ailesi, font dosyası, lisans paketi veya redistribution modelini seçmek.
- Logo, antet, kaşe, imza, sertifika veya marka varlığı eklemek.
- Pilot sözleşme/fatura/irsaliye/rapor türünü ve iş alanlarını uydurmak.
- PDF/A, PDF/UA, elektronik imza, mali mühür veya resmî e-belge uyum seviyesi
  ilan etmek.
- E-posta/WhatsApp otomatik gönderimi geliştirmek.
- Belge Kasasının blob, document, version veya link tablolarını ERP-000H içinde
  sahiplenmek.
- Kod, test fixture, migration, template, binary font, logo veya örnek müşteri
  verisi eklemek.

## 4. Bağımlılıklar ve modül sahipliği

ERP-000H bağımsız bir belge deposu veya paylaşım modülü değildir.

| Yetenek | Sistem sahibi | ERP-000H sınırı |
|---|---|---|
| CompanyContext ve izolasyon | Platform organizasyon altyapısı | Renderer çağrısına güvenilir şirket bağlamı taşır; kendi tenant bypass'ını kurmaz. |
| Yetki ve dışa aktarma izni | Kimlik/RBAC+ABAC katmanı | Üretim/önizleme/indirme öncesi server-side izin sonucunu tüketir. |
| Audit + correlation | ERP-000D | Render talebi ve sonucu için kanonik audit bağlamını taşır. |
| Idempotency + concurrency | ERP-000E | Tekrar komutunda ikinci artifact/sürüm oluşmasını engeller; kaynak sürüm çatışmasını görünür kılar. |
| Blob/document/version/link | ERP-000G Belge Kasası | PDF bytes ve metadata'yı yalnız kabul edilmiş Belge Kasası sözleşmesiyle kalıcılaştırır. |
| Asenkron iş / dış etki | ERP-000F, yalnız seçilirse | Render veya dağıtım async olacaksa outbox/inbox sözleşmesi olmadan worker kurulmaz. |

ERP-000H, nesne deposuna doğrudan key yazamaz ve Belge Kasası metadata'sını
ikinci kez modelleyemez. Kalıcı artifact akışı, ERP-000G'nin şirket izolasyonu,
güvenlik yaşam döngüsü, version/link ve DB–object storage uzlaştırma kararları
main üzerinde kabul edilmeden kodlanamaz.

İzole bir renderer deneyi ileride yalnız geçici test çıktısı üretebilir; bu
çıktı production belgesi, arşiv kaydı veya paylaşılabilir artifact sayılamaz.
Kalıcılaştırma veya kullanıcıya erişim başladığı anda ERP-000G sınırı
zorunludur.

## 5. Kavramsal render girdisi ve çıktısı

Bu bölüm API sınıfı veya provider imzası tanımlamaz. Seçilecek uygulamanın
taşıması gereken semantik bağlamı gösterir.

### 5.1 Render girdisi

- `CompanyId` ve güvenilir request/company context.
- Yetkili actor ve permission/ABAC değerlendirme sonucu.
- Kaynak modül, kayıt kimliği ve beklenen kayıt sürümü.
- Kullanılacak belge türü kimliği ve yerelleştirme bağlamı.
- Kabul edilmiş template kimliği ve değişmez template sürümü.
- Kabul edilmiş font varlığı sürümü/hash'i ve lisans kanıtı referansı.
- Kanonik render veri snapshot'ı; serbest HTML veya ham SQL değildir.
- İstenen kullanıcı dili, saat dilimi ve business date bağlamı.
- Idempotency key, correlation ve causation kimlikleri.
- Kullanıcı tarafından başlatılan üretim/önizleme/paylaşım amacı.
- Varsa AI katılımı; model/tool/politika sonucu fakat doğrudan DB erişimi değil.

### 5.2 Render sonucu

- Teknik belge/version kimliği; kullanıcı dosya adından türetilmez.
- Kullanıcıya görünen Türkçe dosya adı.
- Güvenli ASCII download fallback adı.
- MIME türü ve byte uzunluğu.
- Artifact content hash'i.
- Kaynak kayıt ve kaynak kayıt sürümü.
- Template, font asset ve renderer sürüm bilgileri.
- Render zamanı ve kullanılan locale/timezone bağlamı.
- Belge Kasası tarafından verilecek blob/document/version/link referansları.
- Audit/correlation referansı.
- Başarı, validation failure, authorization failure, conflict veya retryable
  infrastructure failure sonucu.

Render sonucu kaynak modülün ticari durumunu kendiliğinden değiştirmez. Belge
üretimi, iş kaydının onaylandığı anlamına gelmez; onay durumu kaynak modülün
kanonik durum makinesinden gelir.

## 6. Türkçe kullanıcı dosya adı sözleşmesi

### 6.1 İki ayrı ad zorunludur

1. **Görünen/indirilen ad:** Türkçe, kullanıcı tarafından anlaşılır,
   version'lı ve dosya uzantılı addır.
2. **Teknik storage key:** ERP-000G tarafından üretilen opaque ve değişmez
   anahtardır. Şirket adı, cari adı, belge no veya başka hassas kullanıcı
   metninden türetilmez.

Görünen ad DB'de belge sürümü metadata'sı olarak saklanabilir; fiziksel klasör
yolu veya kayıt kimliği olamaz. Kullanıcı görünen adı değiştirirse teknik key,
belge bağlantısı ve content hash değişmez.

### 6.2 Kanonikleştirme hattı

Kesin uygulama, onaylı golden örneklerle şu sıralı davranışı kilitlemelidir:

1. Dosya adı parçalarını kaynak kayıt snapshot'ından server-side al.
2. Her parçayı Unicode NFC'ye normalize et.
3. Türkçe harfleri (`İ ı Ş ş Ğ ğ Ü ü Ö ö Ç ç`) koru; genel ASCII
   transliterasyonunu asıl ad üzerinde uygulama.
4. NUL, CR/LF, C0/C1 kontrol karakterleri, yön/zero-width gizleme
   karakterleri ve path traversal bileşenlerini reddet veya kararlaştırılmış
   güvenli ayraçla dönüştür.
5. `<>:"/\\|?*` karakterlerini tek ve belgelenmiş güvenli ayraçla dönüştür.
6. Sondaki nokta/boşluğu kaldır; boş kalan parçayı doğrudan kabul etme.
7. Tekrarlı ayraç, boşluk ve noktalama kanonikleştirme kuralını deterministik
   uygula.
8. İşletim sistemi reserved name, başta/sonda nokta, azami byte/karakter
   uzunluğu ve uzantı kurallarını doğrula.
9. Truncation gerekiyorsa uzantıyı, belge/version kimliğini ve collision
   güvenliğini koruyan deterministik bir yöntem kullan.
10. Uzantıyı serbest kullanıcı girdisinden değil çıktı MIME sözleşmesinden
    üret.

Kesin safe separator, boş parça metni, reserved-name eki ve uzunluk/truncation
değeri repo kararlarında kilitli değildir. Bunlar implementation öncesi test
ve karar girdisi olmalıdır; koda magic value olarak gömülemez.

### 6.3 Ad kalıbı

Master v3'teki aşağıdaki kalıp kabul oturumunun başlangıç girdisidir:

`{Belge Türü}_{Belge No}_{Cari Kısa Ad}_{YYYY-AA-GG}_v{NN}_{Durum}.pdf`

Bu kalıptaki belge türü, durum sözlüğü, tarih semantiği, cari kısa adının
zorunluluğu ve version basamak sayısı henüz tüm belgeler için kanonik değildir.
Pilot belge türü seçilmeden örnek gerçek iş belgesi üretilemez.

### 6.4 Sürüm ve collision birbirinden ayrıdır

- `vNN`, Belge Kasasındaki gerçek mantıksal belge sürümünü ifade eder;
  dosya sistemi collision sayacı değildir.
- Aynı kaynak kayıt ve aynı artifact sürümü için tekrar çağrı, aynı
  idempotency kapsamında ikinci belge/version oluşturmamalıdır.
- Kaynak veri, template veya onaylı render girdisi değişirse eski PDF üzerine
  yazılmaz; yeni artifact/version ilişkisi açıkça kurulur.
- İki farklı belge aynı sanitize edilmiş görünen ada düşerse teknik kimlikler
  yine farklı kalır ve biri diğerini overwrite etmez.
- Kullanıcıya sunulan collision suffix'in kesin biçimi kararlaştırılmalıdır.
  Güvenli yön, değişmez belge veya version kimliğinden deterministik ve
  açıklanabilir kısa bir ek kullanmaktır; rastgele OS `(1)` davranışı arşiv
  kimliği veya sürüm anlamı olarak kullanılamaz.
- Bir toplu ZIP/dışa aktarım içinde isimler paket oluşturulmadan önce
  çakışmasız hale getirilir; exact policy kabul edilmeden paketleme kodlanmaz.

### 6.5 `Content-Disposition`

HTTP indirmesinde kullanıcıya görünen Unicode ad header içine ham olarak
birleştirilemez. Kabul edilecek download contract en az şunları kanıtlamalıdır:

- `attachment` indirmesinde UTF-8 percent-encoded `filename*` değeri.
- Eski/uyumsuz istemciler için yalnız güvenli karakterlerden oluşan ayrı ASCII
  `filename` fallback değeri.
- CR/LF, çift tırnak, ters bölü, noktalı virgül ve kontrol karakteriyle header
  injection oluşmaması.
- `filename` ile `filename*` arasında aynı artifact'i ifade eden, tutarlı fakat
  farklı encoding'li adlar.
- Preview için `inline` kullanılacaksa bunun yetki, CSP/sandbox ve hassas
  içerik politikasıyla ayrıca kabul edilmesi.
- Header'ın storage key, object URL, şirket içi path veya hassas teknik kimlik
  sızdırmaması.

ASCII fallback, ERP'deki asıl Türkçe adı değiştirmez ve Belge Kasasında yeni
belge sürümü oluşturmaz.

## 7. PDF render ve font sözleşmesi

### 7.1 Unicode ve font gömme

PoC en az şu karakter dizisini başlık, paragraf, tablo ve metadata içinde gerçek
render ederek doğrulamalıdır:

`I i İ ı Ş ş Ğ ğ Ü ü Ö ö Ç ç — Göymen Çelik, Satış Siparişi, Onaylı`

Kanıt yalnız ekranda iyi görünen screenshot değildir:

- PDF font tablosunda kullanılan fontun embedded/subset olduğu doğrulanır.
- Seçilebilir/kopyalanabilir metin extraction sonucunda Türkçe karakterler
  birebir korunur.
- Eksik glyph, tofu (`□`), soru işareti veya sessiz font fallback'i yoktur.
- Bold/italic/regular gibi kullanılan her ağırlığın gerçek asset'i ve embedding
  durumu ayrıca doğrulanır; sentetik stil varsayılmaz.
- Linux/CI render sonucu yalnız geliştirici Windows makinesindeki kurulu fonta
  bağımlı değildir.
- Font yüklenemiyorsa belge hatalı fontla üretilmek yerine açıklanabilir ve
  auditlenebilir biçimde fail-closed olur.

### 7.2 `OPEN-019` ve lisans kapısı

`OPEN-019` kapanmadan font binary'si, marka/antet veya logo repoya/pakete
eklenemez. Kapanış kanıtı en az şunları içermelidir:

- Seçilen font ailesi ve ihtiyaç duyulan ağırlık/glyph kapsamı.
- Ticari kullanım, server-side render, PDF embedding, subsetting,
  redistribution ve container/image içine paketleme izinleri.
- Lisans metninin ve font kaynağının izlenebilir referansı.
- Font dosyası hash'i, sürümü ve tedarik zinciri taraması.
- Marka/antet/logo sahipliği, kullanım izni ve sürümlü asset kaydı.
- Eksik/iptal lisans durumunda build/release'in nasıl fail edeceği.

Bu belge belirli bir font veya lisans modelini öneri diye bile seçmez.

### 7.3 Template ve sürüm

- Template, şirket ve belge türü bağlamında değişmez kimlik ve version taşır.
- Kullanılan template/font/logo hash'leri artifact metadata'sında izlenebilir.
- Yeni template sürümü geçmiş PDF'yi sessizce yeniden render etmez veya eski
  bytes üzerine yazmaz.
- Template'teki serbest metin, HTML/CSS, image URL veya script kullanıcı
  girdisi olarak doğrudan çalıştırılmaz.
- Remote resource fetch varsayılan değildir; izin verilirse allowlist,
  timeout, boyut ve SSRF koruması ayrıca kanıtlanır.
- Tarih, sayı, para ve miktar biçimleri locale ile; kaynak business date ve
  timezone semantics ise backend contract ile belirlenir.
- Pilot belge alanları, resmi örnek ve iş sahibi onayı olmadan template'e
  uydurulmaz.

### 7.4 PDF metadata

Üretilen PDF'nin metadata sözleşmesi sürümlü ve allowlist tabanlı olmalıdır.
PoC; en az başlık, dil (`tr-TR`), template/renderer sürümü ve artifact
izlenebilirliğinin nerede tutulacağını göstermelidir. Ancak müşteri adı, vergi
numarası, iç sunucu yolu, kullanıcı e-postası, object key veya secret'ın PDF
metadata'sına kendiliğinden yazılması yasaktır.

Creation/Modification timestamp, producer alanı, document ID ve locale gibi
değişken alanların deterministiklik üzerindeki etkisi açıklanmalıdır. Aynı
kanonik girdi için görünen içerik ve kullanıcı dosya adı deterministik olmak
zorundadır. Byte-for-byte aynı PDF hash'i hedefleniyorsa renderer'ın volatile
metadata, compression ve object ordering davranışı ayrıca kanıtlanmalıdır;
kanıt olmadan byte determinism iddiası yapılamaz.

## 8. Güvenlik ve şirket izolasyonu

PDF üretimi bir dışa aktarma işlemidir ve sadece buton görünürlüğüyle
korunamaz.

- Her render/preview/download çağrısı aktif `CompanyContext`, permission ve
  kayıt ABAC kapsamını server-side tekrar doğrular.
- Kaynak kaydın ID'sini değiştirerek başka şirkete ait içerik, template,
  logo, dosya adı, toplam, metadata veya existence bilgisi öğrenilemez.
- Template ve font asset erişimi şirket kapsamını karıştırmaz; ortak fiziksel
  asset kullanılsa bile logical authorization birleşmez.
- Kullanıcı dosya adı storage path olarak kullanılmaz; `../`, encoded slash,
  Unicode confusable ve CRLF girdileri path/header etkisi yaratamaz.
- PDF içine eklenen metin ve görseller uygun encoding/escaping ve boyut
  sınırlarından geçer; script, active content ve uzak kaynak varsayılan olarak
  çalıştırılmaz.
- Hassas alan redaction'ı gerekiyorsa ayrı, test edilmiş bir policy olmalıdır;
  sadece görsel üstünü kapatmak veriyi PDF'den kaldırdığı anlamına gelmez.
- Preview/cache/log/metric/error mesajları PDF bytes veya yetkisiz iş verisi
  sızdırmaz.
- Üretilen artifact private kalır; kalıcı public URL yaratılmaz. Erişim ve
  paylaşım ERP-000G/`DEC-DOC-009` kısa erişim ve export izni üzerinden yürür.
- Gerçek kişisel, ticari veya finansal veri golden fixture'a maskesiz
  konulmaz.

## 9. Audit, idempotency, concurrency ve hata davranışı

### 9.1 Audit

Başarılı ve reddedilmiş render/preview/download girişimleri için audit kapsamı
en az şunları taşımalıdır:

- Actor, CompanyId, rol/permission ve veri kapsamı.
- Kaynak modül/kayıt ve beklenen kaynak sürümü.
- Belge türü, template sürümü, font asset sürümü ve renderer sürümü.
- Girdi snapshot hash'i ve sonuç artifact hash'i; iş verisinin tamamı audit
  metnine kopyalanmaz.
- Görünen Türkçe ad ve güvenli fallback adın kanonikleştirme sürümü.
- Correlation, causation, source channel, reason/purpose ve approval referansı.
- AI tetiklediyse AI involvement; AI belgeyi kendi başına onaylamış sayılmaz.
- Başarı, validation/authorization/conflict/renderer/storage hata sınıfı.

Audit içeriği Belge Kasası iş metadata'sının ikinci sahibi değildir; referans
ve bütünlük kanıtı taşır.

### 9.2 Idempotency ve concurrency

- Aynı CompanyId + aynı komut bağlamı + aynı idempotency key + aynı kanonik
  payload önceki güvenli sonucu replay eder; ikinci document/version/blob
  bağlantısı üretmez.
- Aynı key farklı payload ile kullanılırsa kesin reddedilir.
- Kaynak kayıt veya template beklenen sürümden değiştiyse eski snapshot'tan
  sessiz PDF üretilmez; conflict ve fark kullanıcıya gösterilir.
- Başarısız render sonrasında tekrar deneme, yarım artifact'i başarılı sonuç
  saymaz. Staged bytes ve DB metadata uzlaştırması ERP-000G politikasına uyar.
- Artifact başarıyla üretildi fakat istemci yanıtı kaybolduysa retry aynı
  kalıcı sonucu bulur; yeni sürüm açmaz.
- İki paralel kullanıcı aynı kaynak sürüm için üretim başlatırsa collision ve
  version davranışı SQL/Belge Kasası kısıtlarıyla kanıtlanır; UI kilidi tek
  güvenlik değildir.

Render işi asenkronlaştırılır veya başka sisteme dağıtılırsa ERP-000F kabul
edilmeden güvenilir worker/retry/outbox davranışı uydurulamaz.

## 10. Kullanıcı deneyimi ve erişilebilirlik

PoC sadece başarılı download düğmesini göstermemelidir. Kullanıcı arayüzü şu
durumları açıkça tasarlamalıdır:

- Hazırlanıyor/uzun süren render.
- Önizleme hazır.
- Yetkisiz veya dar kapsamlı.
- Kaynak sürüm çatışması.
- Template/font/renderer kullanılamıyor.
- Belge Kasasına kalıcılaştırma veya güvenlik taraması bekliyor.
- Güvenli retry ve idempotent replay.
- Başarılı sonuç, görünen dosya adı ve audit/correlation referansı.

Erişilebilirlik kanıtı en az şunları kapsar:

- UI düğmeleri klavye ile erişilebilir, görünür focus ve anlamlı Türkçe label
  taşır; durum yalnız renkle anlatılmaz.
- PDF document language `tr-TR` olarak tanımlanır.
- Başlık/paragraf/tablo yapısı, okuma sırası ve link açıklamaları seçilecek
  renderer'ın gerçek çıktısında incelenir.
- Anlam taşıyan logo/görsel için alternatif açıklama politikası; dekoratif
  varlığın yardımcı teknolojiden gizlendiği işaretleme belirlenir.
- Text extraction sırası görsel sırayla çelişmez.
- Zoom/büyütmede metin kırpılmaz; uzun Türkçe ad, cari ve tablo hücreleri
  kontrollü taşar/sarılır.

PDF/UA veya başka resmî uyum seviyesi ayrıca kabul edilmeden “uyumlu” olarak
ilan edilmez. PoC yalnız ölçtüğü minimum erişilebilirlik özelliklerini kanıtlar.

## 11. Zorunlu gerçek kanıt planı

Bu tabloda yeni kanonik `AT-*` kimliği oluşturulmamıştır. Kanıtlar `AT-066`
altında ve ileride karar sahibi tarafından atanacak kabul yapısında tutulur.

| Kanıt alanı | En az doğrulama | Başarı ölçütü |
|---|---|---|
| Gerçek render | Seçilmiş provider ile gerçek PDF bytes üretimi | Mock/string fixture değil; açılabilir ve parse edilebilir PDF |
| Türkçe glyph golden | Büyük/küçük Türkçe karakterler; başlık, paragraf, tablo ve metadata | Görsel ve extracted text birebir doğru |
| Font gömme | PDF font inventory incelemesi | Kullanılan her font/ağırlık embedded veya izinli subset |
| Sessiz fallback | CI ortamında sistem fontları olmadan render | Aynı sonuç veya açıklanabilir fail-closed; gizli host bağımlılığı yok |
| NFC | NFC ve decomposed eşdeğer girdiler | Onaylı kurala göre aynı kanonik görünen ad |
| Yasak karakter | `<>:"/\\|?*`, NUL, CR/LF, trailing dot/space | Path/header injection yok; golden dönüşüm deterministik |
| Reserved/uzun ad | OS reserved name, çok uzun Türkçe metin, emoji/non-BMP sınırı | Overwrite/path hatası yok; extension ve kimlik korunur |
| Collision | İki farklı kaydın aynı sanitize edilmiş ada düşmesi | İki teknik artifact korunur; version/collision karışmaz |
| Content-Disposition | Modern UTF-8 ve ASCII fallback istemci senaryosu | Türkçe ad desteklenen istemcide doğru; header injection yok |
| Template version | Aynı veri + aynı sürüm; sonra template/font sürümü değişimi | Eski artifact değişmez; yeni ilişkinin nedeni görünür |
| Semantik determinizm | Aynı kanonik girdi iki ayrı süreç/container render'ı | Görünen içerik, ad, metadata sözleşmesi ve extraction aynı |
| Byte determinism | Yalnız hedeflenirse PDF hash karşılaştırması | Volatile alanlar kontrol edildiğine dair kanıt; aksi halde iddia yok |
| Company isolation | ID/header/query değiştirerek başka şirkete erişim | Bytes, filename, metadata, count ve existence sızıntısı yok |
| Permission/ABAC | Export yetkisi olmayan ve dar kapsamlı kullanıcı | Server-side red; doğrudan URL ile bypass yok |
| Idempotent retry | Yanıt kaybı sonrası aynı key/payload | Aynı sonuç replay; ikinci document/version yok |
| Payload mismatch | Aynı key, farklı kaynak/template/version | Kesin red; ilk sonuç bozulmaz |
| Concurrency | Paralel iki render ve stale source version | Overwrite yok; conflict/tek sonuç sözleşmesi açık |
| DB–object failure | DB/nesne deposu hata noktaları | Orphan/yarım kayıt başarılı sayılmaz; ERP-G reconcile kanıtı |
| Görsel golden | Kontrollü renderer ile sayfa raster diff | Baseline değişimi review'lü; tolerans ve environment sabit |
| Görsel inceleme | İlk baseline ve her kasıtlı layout değişimi | Kırpma, taşma, glyph, antet ve sayfa kırılımı insan review'ünden geçer |
| Metadata güvenliği | PDF properties ve binary/string scan | Secret, path, object key ve gereksiz kişisel veri yok |
| Erişilebilirlik | Dil, yapı/tag, okuma sırası, klavye UI smoke | Ölçülen gereksinimler gerçek PDF ve UI üzerinde geçer |
| Lisans/tedarik | Font/provider/template asset provenance | Kullanım/embedding/paketleme hakkı ve hash kanıtı mevcut |

Görsel golden tek başına yeterli değildir; font inventory, extracted text,
metadata ve güvenlik testleriyle birlikte değerlendirilir. Benzer şekilde unit
test, gerçek renderer/container ve gerçek Belge Kasası adaptör kabulünün yerine
geçmez.

## 12. Implementation öncesi kapatılması gereken kararlar

### 12.1 Kanonik açık karar

`OPEN-019` en az font ailesi, kullanılan ağırlıklar, embedding/subsetting,
lisans/redistribution, marka/antet asset'leri ve paketleme politikasını karar
sahibiyle kapatmalıdır.

### 12.2 Henüz kanonik kimlik atanmamış hazırlık girdileri

Aşağıdakilere bu belgede yeni `OPEN-*` veya `AT-*` kimliği verilmez. Yönetişim
süreci bunları mevcut bir karara bağlamalı veya yeni kimlikleri merkezi olarak
atamalıdır:

- PDF renderer/provider/package seçimi, lisansı ve desteklenen platformlar.
- İlk gerçek pilot belge türü, iş sahibi ve maskelenmiş resmi örnek.
- Belge türü ve durum adlandırma sözlüğü.
- Kesin dosya adı ayıracı, boş parça, azami uzunluk ve truncation kuralı.
- Collision suffix ve toplu export isim politikası.
- `inline` preview, cache ve download header politikası.
- Template/company/locale inheritance ve version onay süreci.
- PDF metadata allowlist'i ve byte determinism hedefi.
- Erişilebilirlik hedef seviyesi ve ölçüm aracı.
- Render'ın senkron/asenkron olması ve dolayısıyla ERP-000F bağımlılığı.
- ERP-000G'nin kabul edilmiş kalıcılaştırma/finalize/reconcile sözleşmesi.

`ERP-000H-DECISION-PACK`, yeni kanonik bir karar veya acceptance kimliği değildir.
Manifest tabanlı orkestrasyonun yukarıdaki kimliksiz hazırlık girdileri kapanmadan
ERP-000H'yi seçmesini engelleyen task-local aggregate blocker etiketidir. Merkezî karar
sicili bu girdileri mevcut kararlara bağladığında veya yeni kanonik kimlikler atadığında
manifestte onların yerini almalıdır.

Bu girdiler kapanmadan kütüphane kurulması veya örnek sözleşme alanlarının
kodlanması, production davranışı tahmin etmek olur.

## 13. Güvenli ilerleme sırası

Kararlar tamamlandıktan sonra önerilen kapı sırası şöyledir:

1. `OPEN-019` ve kimliksiz hazırlık girdilerini karar sahibiyle kapat.
2. ERP-000G'nin belge/version/storage/access sözleşmesini main üzerinde kabul
   et; ERP-000H sahipliğini bu sınıra göre doğrula.
3. Provider/font/template lisans ve tedarik zinciri incelemesini tamamla.
4. Maskelenmiş, iş sahibi onaylı tek pilot belge ve beklenen golden PDF'yi
   hazırla.
5. Önce izole gerçek renderer PoC ile Unicode, embedding, extraction,
   deterministik görünen ad ve metadata güvenliğini kanıtla.
6. Dosya adı + `Content-Disposition` contract testlerini çalıştır.
7. Company/permission/idempotency/concurrency testlerini gerçek backend
   sınırında çalıştır.
8. Kalıcı artifact gerekiyorsa gerçek S3-compatible ERP-000G adaptörü ve gerçek
   SQL Server ile failure/reconcile kabulünü çalıştır.
9. Görsel golden + insan incelemesi + temel PDF accessibility kanıtını ekle.
10. Ancak kanıtlar review edildikten sonra görev durumunu değiştirmeyi öner.

### 13.1 Paralel Draft PR ve merge sırası kapısı

Bu dal `docs/backlog/task_manifest.json` dosyasını değiştirir. Aynı dosyanın ERP-000E/F
gerçeği Draft PR #10'da, ERP-000G gerçeği Draft PR #11'de ve ERP-000I gerçeği Draft PR
#12'de güncellenmektedir. Bu dal PR #10 → PR #11 → PR #12 sırası tamamlanmadan merge
edilmez. Sonrasında güncel `main` ile senkronize edilip E/F/G/H/I durum, dependency ve
blocker gerçeğinin birlikte korunduğu yeniden doğrulanır. Conflict veya semantik fark
sessizce çözülmez; insan review'una taşınır.

## 14. Definition of Ready ve Definition of Done

### 14.1 Kodlama için Ready

ERP-000H ancak aşağıdakilerin tamamı sağlanırsa kodlamaya hazırdır:

- `OPEN-019` kapalı ve font/marka asset'leri hukuken kullanılabilir.
- Provider/package ve lisans seçimi review edilmiştir.
- İlk pilot belge türü, iş sahibi, template alanları ve maskelenmiş örnek
  onaylıdır.
- Dosya adı/collision/header/metadata golden kuralları kesindir.
- ERP-000G sınırı kabul edilmiş ve kalıcılaştırma davranışı açıktır.
- Async kararına göre ERP-000F bağımlılığı açıklanmıştır.
- Güvenlik, tenant, audit, idempotency ve erişilebilirlik kabul planı
  yürütülebilirdir.

Bugünkü durum: **karşılanmıyor; implementation NO-GO**.

### 14.2 Tamamlandı sayılmak için Done

- Gerçek renderer çıktısı ve lisans/provenance kanıtı vardır.
- `AT-066` gerçek PDF, gerçek embedded font ve golden isim testleriyle geçer.
- Türkçe dosya adı ile opaque storage key ayrımı gerçek Belge Kasasında
  doğrulanır.
- Collision/version/Content-Disposition güvenlik testleri geçer.
- CompanyId ve permission/ABAC negatif testleri sızıntısız geçer.
- Audit, idempotent replay, payload mismatch ve concurrency testleri geçer.
- DB–object storage failure/reconcile senaryoları gerekiyorsa gerçek altyapıda
  geçer.
- Görsel golden, extracted text, metadata ve erişilebilirlik kanıtları birlikte
  review edilmiştir.
- P0/P1 açık kusur yoktur.
- Kod, migration ve paket değişiklikleri ayrı kritik review/PR kapısından
  geçmiştir.

## 15. Hazırlık incelemesi sonucu

ERP-000H hedefi, Master v3 ve kilitli belge kararlarıyla tutarlıdır; ancak
incelenen `main` snapshot'ındaki tek satırlık görev tanımı gerçek implementasyona
başlamak için yeterli değildir. Font/marka kararı `OPEN-019` ile açık, renderer ve
pilot belge girdisi kararsız, ERP-000G kalıcılaştırma sınırı kabul kanıtına bağlı ve
acceptance kanıtı henüz üretilmemiştir.

Sonuç:

- **Readiness/spec çalışması: GO**
- **Kod, paket, font, template, migration veya production entegrasyonu: NO-GO**
- **Yeni kanonik `OPEN-*`/`AT-*`: oluşturulmadı**
- **Ortak dosya etkisi:** `task_manifest.json` yalnız ERP-000H gerçeği için değiştirildi;
  PR #10 → PR #11 → PR #12 sonrası senkronizasyon ve insan review'u zorunludur.
