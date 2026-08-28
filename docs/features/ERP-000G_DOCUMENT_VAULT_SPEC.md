# ERP-000G — Merkezi Belge Kasası Çekirdeği Feature Spec

- Durum: Öneri / spec-only
- Görev: `ERP-000G`
- Uygulama kapısı: `NO-GO / BLOCKED`
- Karar sahibi: Sinan Göymen; belge, güvenlik ve altyapı alan sahipleri review'u zorunludur
- Taban: `main` / `9f08ff16447eca89682d0a084e2341b9c2b433c2`
- İlgili ADR: `ADR-002`, `ADR-004`, `ADR-005`, `ADR-009`, `ADR-011`
- Ana kararlar: `DEC-GOV-003`, `DEC-DOC-001`…`DEC-DOC-009`,
  `DEC-AI-001`…`DEC-AI-005`
- Açık kararlar: `OPEN-015`, `OPEN-017`, `OPEN-018`, `OPEN-020`,
  `OPEN-026`, `OPEN-027`, `OPEN-028`, `OPEN-029`
- Global kabul bağları: `AT-001`, `AT-003`, `AT-056`,
  `AT-057`, `AT-060`, `AT-065`, `AT-066`, `AT-067`, `AT-070`

Bu belge uygulama, migration, object-store seçimi, malware tarayıcı seçimi, sabit limit,
saklama süresi veya permission kodu üretmez. Güncel `main`de yalnız yüksek seviyeli
ADR-register yönü vardır; bu spec dalındaki
`docs/adr/ADR-005-document-vault-object-storage.md` fiziksel ADR'si ise henüz `Öneri`
durumundadır. DB–object tutarlılık sınırı ve `OPEN-026`…`OPEN-029` kapanmış karar değildir.
Bu nedenle bu spec, yalnız readiness ve review sözleşmesidir; feature kodlama yetkisi
vermez.

## 1. Amaç

Göymen ERP içindeki dışarıdan yüklenen ve sistem tarafından üretilen dosyaları tek,
yetkili ve izlenebilir belge kasasında yönetmek amaçlanır. Sistem:

- fiziksel dosya içeriğini mantıksal belge, sürüm ve iş kaydı bağlantısından ayırır;
- aynı fiziksel içeriği gereksiz kopyalamadan birden çok yetkili bağlamda kullanabilir;
- farklı şirket veya iş bağlamlarını dedup nedeniyle birleştirmez;
- dosyanın güvenlik taraması ile belgenin ticari/onay yaşam döngüsünü ayrı tutar;
- her sürümü ve bağlantıyı kaynağı, aktörü, şirketi ve korelasyonuyla izler;
- dosya okuma, dışa aktarma ve kullanıcı başlatmalı paylaşımı backend yetki kararıyla
  korur;
- DB metadata ile object-store içeriğinin kopmasını tespit ve güvenli biçimde uzlaştırır;
- şirket veri paketi, backup ve restore sırasında byte bütünlüğünü kanıtlayabilir.

## 2. Kapsam

ERP-000G'nin hedef uygulama kapsamı, giriş kapıları geçtikten sonra aşağıdaki çekirdektir:

1. Fiziksel içerik için değişmez teknik kimlik, sürümlü hash bilgisi, byte uzunluğu,
   algılanan içerik türü, object locator ve bütünlük/güvenlik metadata'sı.
2. Şirket kapsamlı mantıksal belge, değişmez belge sürümü ve ERP kayıt bağlantıları.
3. Yükleme niyeti/session, kontrollü byte alımı, doğrulama, karantina ve güvenli içerik
   kabulü sözleşmesi.
4. Aynı içeriğin tekrarında güvenli fiziksel dedup; ayrı mantıksal belge ve erişim
   bağlamlarının korunması.
5. Yeni sürüm ekleme, sürüm geçmişi, geçerli/güncel sürüm seçimi ve bağlantıların hangi
   sürümü izlediğinin açık gösterimi.
6. Şirket, permission, Site/Location/WorkCenter ve bağlı iş kaydı kapsamını birlikte
   değerlendiren metadata/preview/download/export/share erişimi.
7. Kısa süreli imzalı erişim veya eşdeğer güvenli teslim sözleşmesi; public API'de kalıcı
   object key veya ham depo adresi açıklanmaması.
8. Audit, correlation/causation, idempotent tekrar, optimistic concurrency ve hata
   sonrası güvenli yeniden deneme.
9. DB–object tutarlılık kontrolü, reconciliation, backup/restore checksum ve şirket veri
   paketi kanıtı.
10. AI/OCR araçları için yalnız güvenli, yetkili ve minimum veri sağlayan backend belge
    aracı sınırı.

## 3. Kapsam dışı

Aşağıdakiler ERP-000G çekirdeğinde kararlaştırılmaz veya uygulanmaz:

- resmî e-fatura/e-arşiv üretimi, gönderimi veya resmî durum kaynağı;
- PDF üretim motoru, Türkçe font/antet ve nihai dosya adı kalıbı (`ERP-000H` kapsamı);
- OCR motoru, belge sınıflandırma modeli veya bulut AI sağlayıcısı seçimi;
- WhatsApp Business/API entegrasyonu veya kullanıcı onayı olmadan otomatik gönderim;
- gerçek e-posta göndericisi ve alıcı/CC/konu şablonlarının iş politikası;
- modül bazlı ticari onay, iptal, iade, ters kayıt ve resmileşme durum makineleri;
- belge türü bazlı saklama/imha süresi, legal hold yetkilisi ve purge takvimi;
- izinli dosya türü, azami byte/sayfa/adet, tarama timeout'u veya retry sayısı gibi sabitler;
- object-store ürünü, bucket/container düzeni, region/topoloji, şifreleme anahtar kapsamı,
  storage tier veya malware tarayıcı ürünü;
- belge tam metin arama ve arama projeksiyonu (`ERP-000I` kapsamı);
- pixel-kilitli ekran tasarımı; ilgili `B-*` ekran kartları ayrıca UI review'undan geçer;
- iş modüllerinin kendi aggregate tablolarına Belge modülünden doğrudan yazma.

## 4. Karar ve açık politika ID'leri

### 4.1 Kilitli yön

- `DEC-DOC-001`: `Blob → Mantıksal Belge → Belge Sürümü → Kayıt Bağlantısı` ayrımı.
- `DEC-DOC-002`: fiziksel hash dedup erişim veya iş bağlamını birleştirmez.
- `DEC-DOC-003`: dosya üzerine yazılmaz; revizyon yeni sürümdür.
- `DEC-DOC-004`: kullanıcı adı ile değişmez teknik kimlik ayrıdır.
- `DEC-DOC-006`/`007`: AI iletişim taslağı ve kullanıcı başlatmalı paylaşım sınırı.
- `DEC-DOC-008`: ticari düzeltme ile retention/imha farklı süreçlerdir.
- `DEC-DOC-009`: allowlist, MIME doğrulama, malware taraması, kısa imzalı erişim ve
  hassas dışa aktarma yetkisi zorunludur.
- `DEC-GOV-003` ve şirket ayırma sözleşmesi: belge paketi başka şirkete ait bağlantı veya
  metadata sızdırmadan taşınabilir olmalıdır.

### 4.2 Mevcut açık kararlar

- `OPEN-015`: belge/OCR hacmi, eşzamanlı yük ve yoğun saatler bilinmeden kapasite, limit
  veya SLO kilitlenmez.
- `OPEN-017`: kesin RPO/RTO ve felaket topolojisi kapanmadan production restore kapısı
  tamamlanmış sayılmaz.
- `OPEN-018`: saklama, imha ve legal hold politikası kapanmadan otomatik purge/garbage
  collection production'da etkinleştirilmez.
- `OPEN-020`: bulut AI/OCR'ye hangi belge sınıflarının gönderilebileceği kapanmadan bulut
  çağrısı yapılmaz.

### 4.3 ERP-000G için önerilen açık karar kapsamı

Bu spec aşağıdaki kimlikleri kapatmaz. Bu dalda açık karar siciline aday olarak eklenen
kayıtların insan review'undan geçmesi ve karar sahibi tarafından sonuçlandırılması gerekir:

| ID | Açık konu | Bu spec boyunca fail-closed davranış |
|---|---|---|
| `OPEN-026` | Şirketler arası fiziksel blob dedup kapsamı; hash/collision doğrulaması, encryption/key sınırı, reference ownership, son referans silme, şirket export/restore ve existence-oracle politikası | Hash mevcutluğu veya başka belge/şirket bilgisi açıklanmaz; cross-company physical dedup ve otomatik fiziksel silme production'da açılmaz. |
| `OPEN-027` | İzinli tür/MIME, içerik algılama, boyut ve bileşik dosya limitleri, malware tarayıcı, karantina erişimi, timeout/outage/rescan ve rejected içerik politikası | Clean kanıtı olmayan içerikte preview/OCR/AI/download/link/export/share kapalıdır; limit veya scanner davranışı uydurulmaz. |
| `OPEN-028` | Signed access, preview/download/export/share izinleri; TTL, kullanım sayısı, revoke, filename/Content-Disposition ve hassas veri indirme politikası | Public bucket/kalıcı URL ve varsayılan paylaşım yoktur; production teslim endpoint'i açılmaz. |
| `OPEN-029` | İlk pilot belge türü ve owner modül; document/version/link metadata, permission–ABAC, business/security lifecycle ve DB–object-store commit/finalize/reconciliation sözleşmesi | Gerçek pilot/permission kodu uydurulmaz; migration, kalıcı object write, otomatik finalize/link veya worker kodlanmaz. |

## 5. Kullanıcı rolleri, CompanyId ve ABAC kapsamı

### 5.1 Yetki ilkeleri

- Varsayılan `deny` ve asgari yetki uygulanır.
- Rol adı tek başına yetki değildir. Upload, metadata read, content read, preview, version
  create, link/unlink, classification change, export, share, quarantine operation,
  retention operation ve audit read ayrı capability'lerdir.
- Kesin permission kodları ve görev ayrılığı `OPEN-028`/`OPEN-029` ile kilitlenir; spec
  rol veya permission adı sabit kodlamaz.
- UI düğmesi gizlemek güvenlik değildir. Her metadata, byte, thumbnail, OCR, export ve
  signed-access isteği backend'de yeniden yetkilendirilir.
- Doğrudan ID, değiştirilmiş URL, object key, hash, filename, toplam, hata ve zamanlama
  başka şirket veya yetkisiz kaydın varlığını açıklamamalıdır.

### 5.2 CompanyId sınırı

- `LogicalDocument`, `DocumentVersion` ve `DocumentLink` şirket kapsamlıdır; aktif,
  değişmez `CompanyContext` olmadan normal uygulama yazması fail-closed olmalıdır.
- Aynı fiziksel byte başka şirkette kullanılsa bile o şirket için ayrı mantıksal belge,
  sürüm ve bağlantı oluşur. Bir şirket diğer şirketin dosya adı, linki, aktörü, sayısı,
  hash eşleşmesi veya lifecycle bilgisini göremez.
- `PhysicalBlob` bir erişim/authorization kaydı değildir. Company-neutral veya
  company-scoped fiziksel tutma tercihi `OPEN-026` ile kararlaştırılır.
- Şirket değiştirildiğinde açık upload, preview, signed access veya paylaşım taslağı yeni
  şirkete sessizce taşınmaz; bağlam yeniden doğrulanır veya işlem güvenli biçimde kapanır.

### 5.3 Veri kapsamı

- Belge erişimi yalnız belge CompanyId'sine değil, bağlı iş kaydının permission ve
  Site/Location/WorkCenter kapsamına da tabidir.
- Bir belge birden çok kayda bağlıysa, “herhangi bir bağlantı erişimi bütün belgeyi
  açar” varsayımı yapılmaz. Yetkili görünüm, sürüm ve alanlar `OPEN-028`/`OPEN-029`
  matrisine göre hesaplanır.
- Hassas alan, gizlilik, dışa aktarma/paylaşma ve belge türü kısıtları normal read
  yetkisinden daha dar olabilir.
- Bağlı kayıt silinmek yerine pasif/iptal olsa bile geçmiş belge erişimi güncel yetki ve
  retention politikasıyla değerlendirilir.

## 6. Domain nesneleri ve sahip modül

Domain sahibi `Platform.Documents`/Belge modülüdür. İş modülleri belge byte'ını veya Belge
tablolarını doğrudan sahiplenmez; sürümlü Application contract üzerinden bağlantı kurar.
Kesin tablo/kolon ve event şeması ADR/spec review'undan sonra belirlenir.

### 6.1 Kavramsal nesneler

| Nesne | Sorumluluk | Değişmez sınır |
|---|---|---|
| `PhysicalBlob` | Değişmez byte, teknik object locator, sürümlü içerik hash'i, byte uzunluğu, algılanan MIME, bütünlük ve güvenlik sonucu | Kullanıcıya görünen belge veya yetki kaynağı değildir; byte üzerine yazılmaz. |
| `LogicalDocument` | Şirket kapsamlı belge kimliği, belge türü, veri sahibi, kaynak, gizlilik ve business metadata kökü | Fiziksel blob ile birebir olmak zorunda değildir; başka şirkete ortak kimlik vermez. |
| `DocumentVersion` | Mantıksal belgenin belirli, değişmez içerik/metadata sürümü ve kaynak kanıtı | Eski sürüm overwrite edilmez; yeni içerik yeni sürümdür. |
| `DocumentLink` | Belge/sürüm ile ERP modülü, kayıt ve ilişki türü arasındaki şirket kapsamlı bağ | Byte kopyası üretmez; hedef kaynağın varlığı ve yetkisi backend'de doğrulanır. |
| `IngressIntent` | Yetkili upload niyeti, idempotency/correlation, beklenen bağlam ve geçici alım durumu | Kalıcı belge değildir; güvenli tamamlanmadan iş bağlantısı üretmez. |
| `SecurityAssessment` | İçerik doğrulama, MIME ve malware sonucu ile kanıt sürümü | Ticari onay değildir; scanner/model sonucu sessizce business state'e çevrilmez. |
| `AccessDelivery` | Yetkili download/preview/export/share için kısa ve dar teslim kanıtı | Kalıcı public URL veya object key açıklamaz. |
| `DispositionHold` | Gelecekteki retention/legal-hold kararının sürümlü referansı | `OPEN-018` kapanmadan otomatik purge davranışı üretmez. |

### 6.2 Sürüm ve bağlantı anlamı

- Bağlantının “mantıksal belgenin güncel sürümünü izlemesi” veya “belirli sürüme pinli
  olması” açık ve makine-okunur olmalıdır; örtük davranış kullanılmaz.
- Güncel/geçerli sürüm işareti ile güvenlik taraması, ticari onay, resmî belge durumu ve
  retention durumu tek enum içinde birleştirilmez.
- Aynı `PhysicalBlob`, aynı şirkette veya farklı şirketlerde birden çok `DocumentVersion`
  tarafından kullanılabilir; her version/link kendi CompanyId ve yetki sınırını korur.
- Kullanıcıya görünen orijinal ad ve güvenli indirme adı metadata'dır; object locator veya
  fiziksel klasör yolu değildir.

## 7. Durum geçişleri

Kesin enum adları ve timeout/tekrar değerleri `OPEN-027`/`OPEN-029` kapanmadan kodlanmaz.
Uygulama tasarımında en az üç bağımsız durum ekseni bulunmalıdır.

### 7.1 İçerik giriş/güvenlik ekseni

Kavramsal akış:

`Yetkili niyet → byte alımı → biçim/MIME/bütünlük doğrulaması → güvenlik taraması →
güvenli kabul veya karantina/red → kontrollü finalize`

- Eksik, bozuk, türü uyuşmayan veya taraması tamamlanmamış içerik güvenli sayılmaz.
- Scanner/validator erişilemiyorsa otomatik “temiz” sonucu üretilmez.
- Karantina içeriği normal kullanıcıya preview/download/OCR/AI/export/share edilmez.
- Geçici upload ile kalıcı belge aynı state değildir; yarım yükleme kalıcı sürüm gibi
  görünmez.
- Red/karantina byte'ının saklanması, erişimi ve imhası `OPEN-027` ile kararlaştırılır.

### 7.2 Belge iş yaşam döngüsü

Belge türü sahibi modül; taslak, incelenmiş, onaylı, geçersiz, iptal veya benzeri iş
durumlarını kendi sürümlü sözleşmesiyle tanımlar. Belge kasası:

- “malware bulunmadı” sonucunu “ticari olarak onaylı” saymaz;
- iş onayını güvenlik karantinasını aşmak için kullanmaz;
- eski iş durumunu veya sürümü silmez;
- ters/düzeltme ilişkisini fiziksel byte silme olarak yorumlamaz.

### 7.3 Saklama/bütünlük ekseni

Aktif referans, retention süresi, legal hold, export paketi, backup ve bütünlük durumu
birbirinden ayrıdır. `OPEN-018` kapanmadan otomatik silme, object GC veya imha iş akışı
çalışmaz.

## 8. Fiziksel dedup ve bilgi oracle'ı koruması

- Hash istemciden gelen güvenilir gerçek değildir; sunucu kontrollü byte akışından sürümlü
  ve onaylı algoritmayla hesaplanır. Algoritma/collision politikası `OPEN-026` ile
  kararlaştırılır.
- Hash tek başına dosyanın aynı, güvenli, yetkili veya iş açısından aynı belge olduğunu
  kanıtlamaz. Uzunluk ve gerekli ek bütünlük kontrolleri ADR-005'te tanımlanır.
- Upload cevabı, hata metni, HTTP durumu, süre, toplam veya quota davranışı üzerinden
  başka kullanıcı/şirketin aynı hash'e sahip dosyası bulunduğu açıklanmaz.
- Fiziksel reuse yapılması, mevcut logical document/version/link kimliklerinin istemciye
  dönülmesine yol açmaz.
- Aynı byte'ın tekrar yüklenmesi yetkili bağlamda yeni bir logical document, yeni sürüm
  veya mevcut bağlantı olabilir; seçim açık kullanıcı komutu ve iş sözleşmesine bağlıdır,
  hash tarafından otomatik belirlenmez.
- Hash, object locator, imzalama anahtarı ve güvenlik tarama kanıtı log veya AI prompt'una
  gereksiz biçimde yazılmaz.
- Cross-company dedup, şifreleme anahtarı ve fiziksel ayırma kararı çözülmeden production
  reuse etkinleştirilmez.

## 9. Upload, karantina ve iş yaşam döngüsünün ayrılması

1. Kullanıcı önce CompanyContext ve hedef capability için yetkilendirilir.
2. Sunucu, beklenen belge bağlamını ve idempotency/correlation bilgisini taşıyan sınırlı
   bir ingress intent üretir.
3. Byte alımı; filename, extension veya client MIME'a güvenmeden içerik doğrulamasına
   gider.
4. İçerik güvenli kabul edilmeden logical version/link iş etkisi görünür hâle gelmez.
5. Finalize, seçilen ADR-005 consistency modeline göre metadata, audit ve object kanıtını
   ilişkilendirir.
6. Güvenli içerik daha sonra ayrı business workflow'da incelenebilir/onaylanabilir; bu
   adım güvenlik sonucu değildir.

Allowlist, MIME sniffing, bileşik/arşiv dosyası açma, sıkıştırma bombası, parola korumalı
içerik, makro/aktif içerik, scanner timeout ve yeniden tarama davranışları `OPEN-027`
kapsamındadır. Bu spec bunlara sabit limit veya ürün seçmez.

## 10. DB–object tutarlılığı ve ADR-005 açığı

İlişkisel DB transaction'ı ile harici object-store write/delete işleminin tek ACID
transaction olduğu varsayılmaz. Fiziksel ADR-005 en az şu seçenekleri karşılaştırmalıdır:

1. Object önce yazılır, ardından DB finalize edilir; DB hatasında orphan riski vardır.
2. DB'de pending intent yazılır, object tamamlanır, sonra DB finalize edilir; yarım intent
   ve dangling metadata riski vardır.
3. Staging/quarantine object yazılır, taranır ve kontrollü promote/finalize edilir;
   promote ile DB arasındaki crash penceresi ayrıca çözülmelidir.
4. DB intent + transactional outbox ile idempotent worker yürütülür; bu seçenek
   `ERP-000F` kabul edilmeden uygulanamaz.

Seçilecek model aşağıdakileri açıkça tanımlamadan implementation başlamaz:

- authoritative state ve her crash penceresinde tekrar başlatma davranışı;
- idempotent object put/copy/promote/delete anahtarı;
- orphan object ve missing object tespit/karantina/onarım akışı;
- DB commit olup object finalize olmaması ve object oluşup DB commit olmaması;
- reconciliation sahibinin system identity, permission, audit ve lease davranışı;
- checksum doğrulama, periyodik bütünlük taraması ve alarm;
- retention/legal hold varken fiziksel silme yasağı;
- rollback/forward-fix ve şema/object metadata sürüm uyumluluğu;
- gerçek SQL Server ve seçilen object-store test instance'ında fault-injection kanıtı.

`OPEN-029` kapanmadan kalıcı object write, finalize worker, migration veya otomatik
reconciliation/silme işi kodlanmaz.

## 11. Birim, formül, yuvarlama ve tolerans

ERP-000G finans, stok veya üretim formülü hesaplamaz. Byte uzunluğu, sürüm sıra bilgisi,
hash algoritma sürümü ve tarih/saat teknik alanları açık türlerle taşınır. Boyut limiti,
timeout, retry, signed-access süresi, tarama yaşı veya benzeri değerler magic number olarak
kodlanmaz; `OPEN-015`, `OPEN-026`…`OPEN-029` ve yapılandırma sözleşmesiyle belirlenir.

## 12. Yetki, onay ve görev ayrılığı

- Upload yapabilmek otomatik olarak content read, export, share, classification change,
  version promote, quarantine release veya purge yetkisi vermez.
- Kendi yüklediği hassas belgenin güvenlik karantinasını kaldırma veya imhasını tek başına
  onaylama hakkı varsayılmaz.
- Quarantine işlemleri, hassas dışa aktarma/paylaşım, classification düşürme, legal hold
  kaldırma ve purge için gereken onay/görev ayrılığı
  `OPEN-027`/`OPEN-028`/`OPEN-029`/`OPEN-018`
  kapanışında belirlenir.
- Permission/scope kararı link oluştururken, link okurken, content teslim ederken ve
  replay sırasında yeniden uygulanır.
- Yetki sonradan kaldırılırsa eski signed-access veya idempotency sonucu üzerinden belge
  içeriği açıklanamaz; erişim teslim sözleşmesi revocation/expiry davranışını tanımlar.

## 13. Audit, idempotency, concurrency ve ters işlem

### 13.1 Audit

En az aşağıdaki güvenlik ve iş olayları actor, CompanyId, permission/rol bağlamı,
correlation/causation, source channel, önce/sonra veya sonuç, neden ve varsa AI involvement
ile append-only audit üretir:

- logical document/version/link oluşturma ve durum/metadata değişikliği;
- upload finalize, güvenlik red/karantina ve yetkili karantina işlemi;
- hassas preview/download/export/share teslimi;
- classification veya erişim kapsamı değişikliği;
- retention/legal hold/disposition işlemi;
- reconciliation ile bulunan veya düzeltilen bütünlük uyuşmazlığı;
- AI/OCR çıkarımı, kullanıcı düzeltmesi ve sürüm karşılaştırması.

Audit; dosya byte'ı, imzalı URL/token, secret, object credential veya gereksiz hassas metin
taşımaz. Hash gibi korelasyon değeri yalnız izinli ve oracle üretmeyen audit görünümünde
kullanılabilir. Hangi sıradan read olaylarının audit, hangilerinin güvenlik telemetry'si
olduğu `OPEN-028`/`OPEN-029` ile netleştirilir.

### 13.2 Idempotency ve atomiklik

- Upload intent/finalize, version create, link/unlink ve export/share request gibi tekrar
  edilebilir command'lar sürümlü idempotency sözleşmesi kullanır.
- Aynı key + aynı canonical payload + aynı yetkili actor önceki güvenli sonucu replay
  eder; aynı key + farklı payload/actor kesin conflict üretir.
- Yetki, aktif CompanyContext ve hedef kaynak kapsamı replay'den önce yeniden doğrulanır.
- Tekrar/yarış ikinci logical document, version, link veya audit üretmemelidir.
- DB içindeki metadata değişikliği, command result ve audit aynı transaction sınırında
  olmalıdır. Object-store etkisi bu transaction'a varsayımla dahil edilmez; ADR-005
  consistency modeli uygulanır.
- Optimistic concurrency; metadata, current-version seçimi ve link değişikliklerinde
  sessiz last-write-wins davranışını engeller.

### 13.3 Ters işlem ve silme

- Yayınlanmış/geçmiş version overwrite veya normal CRUD delete ile yok edilmez.
- Yanlış link kontrollü unlink/ters ilişkiyle tarihçeli düzeltilir.
- İş iptali fiziksel blob silmez.
- Fiziksel silme yalnız sıfır etkin/yasal referans, retention/legal-hold ve yetkili imha
  politikası birlikte sağlandığında değerlendirilebilir; `OPEN-018` kapanana kadar
  production'da kapalıdır.

## 14. API, event ve AI tool sözleşmesi

### 14.1 API sınırı

Kesin route ve JSON şemaları implementation review'unda sürümlenir. Kavramsal contract'lar:

- upload niyeti oluşturma ve kontrollü byte alımı;
- upload durumunu sorgulama ve güvenli finalize;
- logical document ve version metadata okuma/oluşturma;
- kayıt bağlantısı ekleme/kaldırma ve version pin/follow davranışı;
- yetkili preview/download teslimi isteme;
- export/share paketi veya kullanıcı başlatmalı paylaşım hazırlama;
- version geçmişi, audit referansı ve bütünlük durumu sorgulama.

Public contract:

- ham DB erişimi, object-store credential, kalıcı object key veya sınırsız URL vermez;
- client hash/MIME/filename değerini tek doğruluk kaynağı kabul etmez;
- CompanyId'yi payload'dan güvenilir kabul etmez; request-scoped CompanyContext ile
  doğrular;
- hata detayında başka şirket, hash eşleşmesi, scanner veya storage iç ayrıntısı sızdırmaz;
- signed teslimi belirli company, document version, actor/audience ve action için daraltır;
  kesin süre ve revocation politikası `OPEN-028` ile belirlenir.

### 14.2 Event sınırı

Event ownership, şema, outbox/inbox ve tüketici davranışı `ERP-000F` kabul edilmeden
çalışan event/worker olarak kodlanmaz. İleride yayınlanacak event:

- object byte'ın kendisini taşımaz;
- CompanyId, belge/version kimliği, olay sürümü, correlation/causation ve minimum gerekli
  sınıflandırılmış metadata'yı taşır;
- yetkilendirme kanıtı yerine geçmez; consumer hedef kaynağı kendi policy'siyle doğrular;
- AI, OCR, arama veya bildirim consumer'ının Belge SoT'sini değiştirmesine izin vermez.

### 14.3 AI/OCR sınırı

- AI doğrudan DB veya object-store'a bağlanmaz; yalnız dar, sürümlü backend tool kullanır.
- Tool çağrısından önce CompanyId, permission/ABAC, güvenlik taraması, belge veri sınıfı ve
  `OPEN-020` bulut politikası kontrol edilir.
- Karantina, taraması tamamlanmamış veya yetkisiz byte AI/OCR'ye gönderilmez.
- Minimum gerekli version ve sayfa/alan sağlanır; başka link/şirket metadata'sı eklenmez.
- OCR/AI sonucu taslak/çıkarımdır; orijinal byte'ı, resmi belgeyi, güvenlik sonucunu veya
  business onayını değiştirmez.
- AI version farkını açıklayabilir, bağlantı ve metadata önerisi hazırlayabilir; kendi
  başına version oluşturamaz, classification düşüremez, paylaşamaz veya imha edemez.
- Model/prompt/tool sürümü, veri kapsamı, sonuç, kullanıcı düzeltmesi ve uygulanan etki
  auditlenir.

## 15. Signed erişim, dışa aktarma ve kullanıcı başlatmalı paylaşım

- Preview/download/export/share öncesinde güncel CompanyContext, version, permission,
  ABAC, gizlilik ve dışa aktarma/paylaşma yetkisi yeniden değerlendirilir.
- Signed access kısa, dar ve yeniden kullanılabilirliği sınırlı olmalıdır; kesin süre,
  audience, revocation ve cache politikası sabitlenmez.
- Signed URL/token log, audit payload, analytics, referrer veya AI prompt'una yazılmaz.
- Kullanıcıya görünen ad Unicode/Türkçe metadata'dan güvenli `Content-Disposition`
  davranışıyla üretilir; teknik object locator açıklanmaz. Nihai adlandırma `ERP-000H`'dir.
- Export; hangi version'ı ve hangi metadata'yı içerdiğini manifest/checksum ile açıklar.
  Başka şirket linki veya hassas alanı dolaylı biçimde pakete eklemez.
- İlk WhatsApp kapsamı otomatik gönderim değildir. Kullanıcı `Paylaş` eylemini başlatır;
  desteklenen ortamda sistem paylaşım seçicisi, destek yoksa güvenli indir + metin kopyala
  fallback'i kullanılır.
- E-posta Kime/CC/Konu/Gövde/Ek AI taslağı kullanıcı onayı olmadan gönderilmez. Gerçek
  mail transport ERP-000G kapsamında değildir.
- Paylaşım veya dışa aktarım başarısız olursa belge business durumu değişmez; güvenli hata
  ve yeniden deneme/audit kanıtı üretilir.

## 16. UI durumları ve erişilebilirlik

Belge kasası ekranları ortak kabuk ve mevcut `B-*` ekran kataloğu review'una bağlıdır.
Feature uygulaması en az şu durumları ayrı ve anlaşılır göstermelidir:

- boş liste ve yetkili filtrede sonuç yok;
- upload seçimi, ilerleme, kullanıcı iptali ve güvenli retry;
- doğrulama/tarama bekliyor;
- karantina/red ve kullanıcıya açıklanabilir, sır sızdırmayan neden;
- object-store/scanner geçici erişilemiyor;
- partial metadata var fakat content henüz kullanılamıyor;
- version conflict ve başka kullanıcı değişikliği;
- content missing/checksum mismatch ve güvenli destek correlation kimliği;
- yetkisiz/gizli kayıt; başka kaydın varlığını açıklamayan durum;
- signed access süresi/erişimi geçersiz;
- export/share hazırlanıyor, hazır, başarısız veya kullanıcı aksiyonu gerekiyor;
- offline/bağlantı kesintisi; tamamlanmamış upload'ı kalıcı belge gibi göstermeme.

Erişilebilirlik değişmezleri:

- bütün eylemler klavye ile erişilebilir, görünür focus sırası mantıklıdır;
- progress ve durum değişimi screen reader'a canlı fakat gürültüsüz bildirilir;
- hata, karantina ve version bilgisi yalnız renk/ikonla anlatılmaz;
- dosya türü, ad, version, boyut ve durum anlamlı label taşır;
- dialog kapanınca focus güvenli kaynağa döner; uzun akış küçük modal içine sıkıştırılmaz;
- hareketli progress ve preview kullanıcı tercihlerini gözetir;
- şirket değişimi, açık form/upload/share bağlamını sessizce yeniden kullanmaz.

## 17. Retention, restore ve şirket veri paketi

### 17.1 Retention/legal hold

- `OPEN-018` kapanmadan belge türü bazlı gün/yıl, purge schedule veya otomatik object GC
  kodlanmaz.
- Retention süresi dolması tek başına fiziksel silme komutu değildir; aktif link, version,
  audit, legal hold, export/restore ve yetkili imha kararı birlikte değerlendirilir.
- İmha; actor, company, policy version, kapsam, neden, onay ve kanıtla auditlenir.
- Ticari iptal/ters işlem retention imhasına dönüştürülmez.

### 17.2 Backup/restore

- DB metadata ve object byte yedekleri ortak snapshot/manifest, kaynak kimliği ve checksum
  ile ilişkilendirilir.
- Restore yalnız DB satır sayısı değil; version→blob→object erişimi, checksum, link
  bütünlüğü, CompanyId izolasyonu ve legal-hold kanıtını doğrular.
- Missing/corrupt object restore sırasında sessizce atlanmaz; mutabakat raporu ve güvenli
  hata üretir.
- Kesin topoloji ve RPO/RTO `OPEN-017` kapanmadan production hazır sayılmaz.

### 17.3 Şirket veri paketi

- Bir şirket paketi yalnız o şirketin logical documents, versions, links, izinli metadata
  ve gerekli object byte alt kümesini içerir.
- Fiziksel blob başka şirketçe de kullanılıyorsa paket diğer şirketin kimlik, link, ad,
  actor veya sayısını içermez; gerekli byte kendi manifest referansıyla taşınır.
- Import/restore hash ve teknik kimlik çakışmasını yetki bağlamı olarak kullanmaz; yeni
  ortamda şirket bağlantıları yeniden doğrulanır.

## 18. Gözlemlenebilirlik ve güvenlik telemetry'si

Uygulama; değerleri `OPEN-015`/altyapı policy'siyle belirlenecek log, metrik, trace ve
alarmları üretmelidir:

- ingress başlatma/tamamlama/hata ve bekleme süresi;
- validation, MIME mismatch, scan pending/failure/quarantine sonuçları;
- object put/get/promote/delete/reconciliation sonucu ve latency;
- orphan, dangling metadata, missing object ve checksum mismatch sayısı;
- idempotency replay/conflict ve concurrency conflict;
- signed delivery üretimi/reddi, export/share sonucu;
- 401/403/404 ve cross-company erişim denemeleri;
- scanner/object-store bağımlılık sağlığı, queue yaşı ve backpressure;
- backup/restore/package checksum mutabakatı;
- kapasite ve dedup verim metriği; bireysel hash veya şirketler arası varlık oracle'ı
  üretmeyecek toplulaştırmayla.

Log/trace içinde byte, belge metni, object credential, signed URL/token, hassas filename,
kişisel veri veya gereksiz hash bulunmaz. Alarm eşiği, sampling, saklama ve dashboard
değerleri sabit sayı olarak kodlanmaz.

## 19. Migration ve rollback

- Bu spec migration üretmez.
- Fiziksel ADR-005 ve `OPEN-026`…`OPEN-029` kapanmadan Documents schema, object bucket,
  trigger, worker veya production seed oluşturulmaz.
- Gelecek migration; CompanyId/write guard, composite key/FK, version uniqueness,
  immutable content/version, optimistic concurrency ve gerekli DB constraint'lerini
  gerçek SQL Server'da kanıtlar.
- Binary rollback geçmiş logical document/version/link, audit veya object byte'ı silmez;
  şema korunur ve gerektiğinde forward-fix uygulanır.
- Object metadata ve hash algoritması sürümlüdür; uygulama rollback'i eski içeriği
  okunamaz veya yanlış dedup edilir hâle getiremez.
- Storage vendor/topoloji geçişi checksum ve manifestli kopyalama/verify/cutover/rollback
  planı olmadan yapılmaz.

## 20. Kabul senaryoları

Ayrıntılı Given/When/Then planı
`docs/acceptance/ERP-000G_ACCEPTANCE.md` dosyasındaki görev-yerel
`ERP-000G-AC-001`…`ERP-000G-AC-025` kimlikleridir. Bunlar global `AT-*` kimliği ayırmaz,
fixture veya kanıt varmış gibi yorumlanmaz ve tamamı `Taslak`tır.

| Kimlik | Kanıt alanı |
|---|---|
| `ERP-000G-AC-001` | Gerçek SQL Server ve gerçek S3-compatible sözleşme smoke testi |
| `ERP-000G-AC-002` | Dosya kabul doğrulaması ve MIME fail-closed davranışı |
| `ERP-000G-AC-003` | Şirket izolasyonlu dedup ve existence-oracle engeli |
| `ERP-000G-AC-004` | Aynı şirkette tekrar içerik ve ayrı iş bağlamları |
| `ERP-000G-AC-005` | Paralel aynı-içerik upload yarışı |
| `ERP-000G-AC-006` | Başarılı staged upload ve görünürlük sınırı |
| `ERP-000G-AC-007` | Crash, retry ve reconciliation matrisi |
| `ERP-000G-AC-008` | Malware taraması, quarantine ve scanner arızası |
| `ERP-000G-AC-009` | Kalıcı byte ve content-hash değişmezliği |
| `ERP-000G-AC-010` | Logical document ve version yaşam döngüsü |
| `ERP-000G-AC-011` | Kayıt linkleri ve CompanyId bütünlüğü |
| `ERP-000G-AC-012` | Signed access üretme yetkisi ve dar kaynak kapsamı |
| `ERP-000G-AC-013` | Signed access redeem ve resource/method bağlama |
| `ERP-000G-AC-014` | Signed access expiry ve server-clock sınırı |
| `ERP-000G-AC-015` | Revocation ve sonradan daralan yetki |
| `ERP-000G-AC-016` | Paylaşılan referans altında silme/garbage-collection güvenliği |
| `ERP-000G-AC-017` | Şirket kapsamlı export bütünlüğü |
| `ERP-000G-AC-018` | Gerçek restore ve checksum mutabakatı |
| `ERP-000G-AC-019` | Metadata, byte, hash, toplam, hata ve signed URL dahil bütün okuma yollarında no-leak |
| `ERP-000G-AC-020` | Audit, correlation, idempotency ve atomik DB davranışı |
| `ERP-000G-AC-021` | Gözlemlenebilirlik ve veri minimizasyonu |
| `ERP-000G-AC-022` | Migration, forward-fix ve provider değişimi güvenliği |
| `ERP-000G-AC-023` | UI durumları, klavye ve screen reader erişilebilirliği |
| `ERP-000G-AC-024` | Açık işlem sırasında CompanyContext değişimi ve no-leak |
| `ERP-000G-AC-025` | Kullanıcı başlatmalı sistem paylaşımı ve güvenli Unicode ad sınırı |

Global `AT-056`, `AT-060`, `AT-066` ve `AT-067` içindeki AI/OCR, nihai Türkçe PDF adı/font
ve platform paylaşım davranışları görev-yerel kanıtı tamamlayacak şekilde ayrıca
kanıtlanmalıdır. Spec varlığı bunların hiçbirini otomatik tamamlanmış saymaz.

## 21. Coding gate

ERP-000G implementation, test-substrate, schema/migration, object adapter, upload endpoint,
scanner/OCR worker, signed-access veya paylaşım kodu için mevcut durum **NO-GO**'dur.
Aşağıdakilerin tamamı gerçekleşmeden kodlama başlamaz:

1. Fiziksel `ADR-005` seçenek, karar, risk, geri alma ve gerçek test planıyla yazılır ve
   insan mimari review'unda kabul edilir.
2. `OPEN-026`, `OPEN-027`, `OPEN-028`, `OPEN-029` aday kayıtları insan review'undan geçer
   ve karar sahibi tarafından kapatılır.
3. Permission–ABAC ve görev ayrılığı matrisi; cross-company/dedup-oracle saldırı
   senaryolarıyla birlikte kabul edilir.
4. Pilot belge türü, veri sahibi, gizlilik sınıfı, kaynak ve business lifecycle sahibi
   belirlenir.
5. DB–object consistency, crash windows, idempotent recovery ve reconciliation seçeneği
   kesinleşir.
6. İzinli içerik ve quarantine/scanner sözleşmesi, fail-closed kesinti davranışı ve
   güvenlik test fixture'ı onaylanır.
7. `ERP-000B`, `ERP-000C`, `ERP-000D` ve `ERP-000E` taban davranışları güncel `main`de
   kabul edilmiş olarak doğrulanır. Seçenek outbox/worker gerektiriyorsa `ERP-000F` de
   merge edilmeden bağımlı kod yazılmaz.
8. `G-AC-*` planı global acceptance/kanıt matrisiyle çakışmadan kanonikleştirilir.
9. Seçilen object-store ve SQL Server için gerçek integration/fault-injection ortamı ile
   migration/rollback planı tanımlanır.
10. `OPEN-018` kapanmadan production purge/GC; `OPEN-020` kapanmadan bulut AI/OCR;
    `OPEN-017` kapanmadan production restore hazır iddiası yapılmayacağı kabul edilir.

Spec review'u bu kapıları kaldırmaz; yalnız blocker'ları görünür yapar.

## 22. Definition of Done

ERP-000G ancak aşağıdakilerin tamamı kanıtlandığında `done` olabilir:

- ADR-005 kabul edilmiş; `OPEN-026`…`OPEN-029` kapalıdır.
- Document domain/Application/Infrastructure sınırı, başka modül tablosuna doğrudan
  yazmadan architecture guard'ı geçer.
- CompanyId global filtre/write guard ve permission–ABAC; query, command, direct ID,
  signed access, hash/oracle, toplam ve hata kanallarında fail-closed test edilmiştir.
- Immutable blob/version, logical document ve link şeması gerçek SQL Server migration,
  constraint, concurrency ve rollback/forward-fix kanıtına sahiptir.
- Seçilen object-store ile upload, scan/quarantine, dedup, finalize, checksum,
  reconciliation ve bütün crash-window fault testleri geçmiştir.
- DB metadata + audit + idempotency atomikliği ve object recovery modeli gerçek
  integration testlerinde kanıtlanmıştır.
- Yetkisiz/karantina içeriği preview, download, export, share, OCR ve AI yollarının
  hiçbirinden sızmamıştır.
- Sürüm geçmişi overwrite olmadan korunmuş; linkler byte kopyalamadan ve yetki sızdırmadan
  çalışmıştır.
- Signed access, export ve kullanıcı başlatmalı paylaşım güvenlik/erişilebilirlik testlerini
  geçmiştir; otomatik WhatsApp gönderimi yoktur.
- Backup/restore ve şirket veri paketi checksum/link/company-isolation mutabakatı gerçek
  provada geçmiştir.
- Log/metrik/trace/alarm/runbook vardır; telemetry secret, signed token veya hassas belge
  içeriği taşımaz.
- `OPEN-015` girdileriyle temsilî yük testi yapılmış; onaylı SLO kanıtı kaydedilmiştir.
- Unit, contract, HTTP, security, gerçek SQL Server, gerçek object-store, fault-injection,
  accessibility ve restore testleri CI/manuel kapılarda başarılıdır.
- P0/P1 güvenlik, veri kaybı, yetki sızıntısı veya açık review blocker'ı yoktur.
- Task manifest, ADR register, acceptance matrisi ve delivery raporu doğrulanmış gerçek
  durumla güncellenmiştir; yalnız belge/spec varlığı tamamlandı sayılmamıştır.
