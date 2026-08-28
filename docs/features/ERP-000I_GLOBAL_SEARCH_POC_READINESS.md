# ERP-000I — Global Arama PoC Hazırlık ve Kodlama Kapısı

- Tarih: 2026-08-28
- İncelenen taban: `main` / `9f08ff16447eca89682d0a084e2341b9c2b433c2`
- Görev: `ERP-000I — Global arama PoC`
- Bu belgenin kapsamı: yalnız readiness analizi ve karar/kabul planı
- Uygulama durumu: **NO-GO**
- Readiness review durumu: **GO**

## 1. Yönetici özeti

ERP-000I için arama motoru, indeks, migration, modül adaptörü, API veya UI kodlamasına
başlanmamalıdır. Repo, aramanın güvenlik ve performans yönünü tarif eden bağlayıcı
ilkeleri içerir; ancak uygulanabilir tek bir sözleşme henüz yoktur.

Kodlamayı durduran başlıca nedenler şunlardır:

1. `ADR_REGISTER.md`, ADR-007'yi “Kabul” gösterirken fiziksel ADR-007 dosyası yoktur.
2. Master v3 arama çözümünü PostgreSQL `pg_trgm` ve GIN/GiST ayrıntılarıyla tarif eder;
   güncel karar sicili ve çalışan platform ise SQL Server'ı birincil PoC adayı yapar.
3. Türkçe karşılaştırma, normalizasyon, tokenization, aksan ve `I/İ/ı/i` davranışı
   kilitli değildir.
4. Kesin kimlik araması ile tam metin/yazım toleranslı aramanın alan, sıralama ve
   fallback sözleşmesi yoktur.
5. Arama projeksiyonunun sahipliği, modül adaptör sözleşmesi, güncellik ve yeniden
   kurma yolu tanımlı değildir.
6. Yetki değişikliğinin indeks, cache, cursor ve toplam sayılar üzerindeki anlık etkisi
   için fail-closed sözleşme yoktur.
7. Stabil keyset cursor'ın sıralama anahtarı, bağlam bağı ve invalidation koşulları
   karara bağlanmamıştır.
8. `OPEN-015` nedeniyle üç yıllık hacim, eşzamanlı kullanıcı ve yoğun saat profili
   bilinmemektedir; mevcut p95 değerleri başlangıç önerisidir, production SLO değildir.
9. Arama sorgularının audit, gizlilik, maskeleme ve saklama politikası eksiktir.
10. İlk pilot modüller ve indekslenecek alanların veri sınıfları belli değildir.
11. `task_manifest.json`, ERP-000I'yi yalnız ERP-000A'ya bağlı `ready` gösterir; bu,
    mevcut CompanyId/yetki/audit/idempotency güvenlik bağımlılıklarını ve SQL PoC
    kapısını temsil etmez.

Bu belge yeni bir production davranışı seçmez. Eksik konuları **candidate decision
required** olarak işaretler; yeni kanonik `OPEN-*` veya `AT-*` kimliği üretmez.

## 2. Bağlayıcı kaynaklar ve kanıtlanan repo gerçeği

Kaynak önceliği kök `AGENTS.md` sırasına göre uygulanmıştır:

1. `docs/decisions/KARAR_SICILI.md`
2. `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`
3. ilgili feature spec ve ADR'ler
4. eski StokERP/prototip yalnız referans

İlgili bağlayıcı kayıtlar:

- `DEC-GOV-002`: her kritik kayıtta `CompanyId`; grup toplamı yalnız yetkili
  görünüm/rapordur.
- `DEC-TEC-001`: modüler monolit; modüller başka modül tablosuna doğrudan yazmaz.
- `DEC-TEC-003`: SQL Server birincil PoC adayıdır; seçim arama, yük, audit, restore ve
  maliyet PoC'sini geçmeden kesinleşmez.
- `DEC-TEC-005`: ilişkisel indeks + full-text/trigram başlangıç yönüdür; gerekirse
  türetilmiş OpenSearch eklenebilir; indeks SoT değildir ve sonuç açılışında backend
  yetkisi yeniden doğrulanır.
- `DEC-TEC-009`: log, metric, trace, korelasyon ve yavaş sorgu alarmları platformla
  birlikte kurulmalıdır.
- `DEC-AI-001`: AI kesintisi temel ERP aramasını durdurmamalıdır.
- `DEC-AI-002` ve `DEC-AI-003`: AI ham SQL veya doğrudan DB kullanamaz; yalnız dar,
  şemalı ve yetki kontrollü backend tool'ları kullanır.
- `DEC-AI-004`: hassas veri yerel-first/default-deny sınırındadır.
- `DEC-AI-005`: AI araç kullanımının şirket, kullanıcı, sürüm, kapsam ve sonucu
  auditlenir.
- `DEC-TST-001` ve `DEC-TST-002`: otomatik güvenlik/performans kanıtı ve açık P0/P1
  bulunmaması teknik kabulün parçasıdır.
- `AT-002`: yetkisiz kayıt sonuç, öneri, toplam veya sayı üzerinden sızamaz.
- `AT-069`: yetkili arama temsili üç yıllık hacmin üç katında kilitlenecek p95'i ve
  tekrar/atlama yapmayan derin sayfalamayı kanıtlar.
- `OPEN-015`: üç yıllık veri, eşzamanlı kullanıcı, belge/OCR hacmi ve yoğun saatler
  henüz açıktır.

Çalışan platformda mevcut güvenlik temeli:

- request-scoped `CompanyContext` bir istek içinde başka şirkete değiştirilemez;
- yerel ERP yetkisi default-deny RBAC + Company/Site/Location/WorkCenter ABAC kapsamı
  üretir;
- bilinmeyen permission yetki üretmez;
- şirketler arası ID, URL, query, command, toplam ve hata sızıntısı için mevcut gerçek
  SQL Server kabul kanıtları vardır;
- append-only audit/correlation ve kalıcı idempotency/concurrency omurgası mevcuttur.

Mevcut `task_manifest.json` satırı bu omurgalara rağmen ERP-000I'yi yalnız ERP-000A'ya
bağlar ve `ready` işaretler. Bu belge paylaşılan manifesti değiştirmez; implementation
öncesi ayrı registry review'u, ERP-000B/C/D/E bağımlılıklarını, async projection
seçilirse ERP-000F'yi ve SQL/observability kanıt sahipliğini görünür biçimde
uzlaştırmalıdır.

Bu temel, aramanın güvenli olacağını kendiliğinden kanıtlamaz. Search modülü aynı
sınırları kendi query, projection, cache, pagination ve result-open yollarında ayrıca
kanıtlamalıdır.

## 3. ADR-007 drift'i ve SQL Server gerçeği

### 3.1 Kanıtlanan çelişki

- `docs/architecture/ADR_REGISTER.md`, `ADR-007 | Arama | İlişkisel full-text;
  gerekirse OpenSearch | Kabul` kaydını içerir.
- `docs/adr/` altında fiziksel ADR-007 dosyası yoktur.
- Master v3, PostgreSQL full-text, `pg_trgm`, GIN/GiST ve PostgreSQL'e özgü bir
  kademelendirme tarif eder.
- `DEC-TEC-003`, SQL Server'ı birincil PoC adayı yapar; mevcut repo EF Core ve gerçek
  SQL Server test altyapısıyla çalışır.
- `docs/architecture/SQL_SERVER_POC.md`, Türkçe kesin/tam metin aramasını henüz
  yapılacak PoC olarak listeler.

Sonuç: ADR kayıt satırı tek başına uygulanabilir ve kabul edilmiş ADR kanıtı değildir.
Master'daki PostgreSQL operatörleri SQL Server migration'ına çevrilerek sessizce
uygulanamaz.

### 3.2 Candidate decision required — fiziksel ADR-007

Kodlamadan önce ayrı review ile fiziksel ADR-007 oluşturulmalı ve en az şu konuları
kilitlemelidir:

- PoC motoru ve doğrulanacak SQL Server sürüm/edition/container özellikleri;
- ilişkisel exact index ve SQL Server full-text'in sorumluluk sınırı;
- SQL Server'da trigram eşdeğeri varmış gibi davranılmayacağı;
- yazım toleransı gerçekten gerekliyse bunun SQL Server içinde mi, uygulama düzeyinde
  mi, yoksa ölçüm sonrası OpenSearch ile mi sağlanacağı;
- full-text catalog, dil/word-breaker, stoplist, accent sensitivity ve collation
  stratejisi;
- OpenSearch'e geçiş için ölçülebilir fakat `OPEN-015` kapanmadan sayı kilitlemeyen
  karar kapısı;
- indeksin türetilmiş, yeniden kurulabilir ve SoT olmayan statüsü;
- sync/async güncelleme, lag, rebuild, disaster recovery ve fallback davranışı;
- rollback/forward-fix ve lisans/operasyon maliyeti.

ADR onaylanmadan provider paketi, full-text catalog/index migration'ı veya OpenSearch
altyapısı eklenmemelidir.

## 4. Güvenlik değişmezleri

### 4.1 CompanyId fail-closed

PoC'nin güvenli başlangıç koşulları:

- Her sorgu çözülmüş tek bir `CompanyContext` ister; boş, geçersiz veya istek içinde
  değişen bağlam reddedilir.
- İstemciden gelen CompanyId, filtre veya cursor içeriği yetki kaynağı değildir.
- Arama dokümanı, proje kaydı, cache key'i, cursor ve telemetry bağlamı şirketten
  ayrıştırılamaz.
- Company filtresi ranking, facet, count, suggestion ve pagination **öncesinde**
  uygulanır. Önce global sonuç üretip sonra filtrelemek yasaktır.
- Grup çapında arama için mevcut tek-şirket request sözleşmesi sessizce genişletilmez;
  ayrı güvenlik ve ürün kararı gerekir. Karar yokken fail-closed sınır tek şirkettir.
- Başka şirkete ait bilinen belge/kayıt kimliği, exact key, cursor veya route genel
  güvenli sonuç üretir; yabancı şirket, tür, varlık veya eşleşme varlığını açıklamaz.

### 4.2 RBAC + ABAC fail-closed

- Search permission tek başına bütün modül verisini okuma yetkisi değildir.
- Kullanıcı hem arama giriş permission'ına hem sonuç türünün kaynak modül read
  permission'ına sahip olmalıdır.
- Company/Site/Location/WorkCenter scope'u projection filtresinde uygulanabilir veriyle
  temsil edilmelidir.
- Bir kayıt için permission/scope eşlemesi belirsiz, eksik veya stale ise kayıt
  company-wide varsayılmaz; sonuçtan çıkarılır.
- Sonuç sayısı, facet, suggestion, autocomplete, spell-correction ve “başka sonuç var”
  ipuçları yalnız yetkili aday kümesinden üretilir.
- Sonuç route'u açılırken kaynak modül backend'i güncel yetkiyi ve CompanyId'yi yeniden
  doğrular. Search sonucu erişim bileti değildir.
- Source route/ID, snippet, highlight, para/tutar, cari/ürün adı ve hata ayrıntısı
  yetkilendirmeden önce response'a yazılmaz.

### 4.3 Yetki değişikliği ve cache/cursor

Yetki daraltıldıktan veya süreli atama sona erdikten sonra eski projection, response
cache ya da cursor veri göstermeye devam edemez.

Candidate decision required:

- permission değişikliğinin cache/cursor invalidation sinyali;
- cursor'ın kullanıcı, CompanyId, sorgu şekli, permission snapshot/version ve indeks
  nesliyle nasıl bağlanacağı;
- permission genişlemesi/daralmasında mevcut cursor'ın reddedilme veya yeniden başlama
  davranışı;
- uzun süren sorguda permission değişiminin hangi noktada yeniden kontrol edileceği;
- cache anahtarının aktör/rol yerine hangi güvenli authorization fingerprint'i
  kullanacağı ve bu fingerprint'in yaşam süresi.

Güvenli kabul sonucu davranış adı/formatı kilitlenene kadar şudur: eski bağlamla devam
etmek yerine cursor/cache kullanımı fail-closed sona erer; bunun HTTP hata kodu ve
kullanıcı metni candidate decision required'dır.

### 4.4 Bilgi sızıntısız hata ve gözlemlenebilirlik

- Yetkisiz, bulunamayan, başka şirket, stale cursor ve bilinmeyen result type cevapları
  yabancı kaydın varlığını ayırt ettiremez.
- API response, exception, log, trace tag, metric label ve timing testleri birlikte
  incelenir.
- Yüksek cardinality sorgu metni, cari adı, belge no veya kullanıcı girdisi metric label
  yapılmaz.
- Güvenlik testi yalnız HTTP status'a bakmaz; payload, header, count, öneri, facet,
  cursor ve log kanıtını da doğrular.

## 5. Arama anlamı: exact ve full-text ayrımı

### 5.1 Exact arama sınıfı

Belge numarası, barkod, plaka, cari/ürün kodu, eski kod/alias gibi alanlar ayrı
alan-sahipli exact anahtar sözleşmesi ister.

Candidate decision required:

- her exact alanın case, whitespace, tire/eğik çizgi, noktalama, baştaki sıfır,
  Unicode normalization ve kültür davranışı;
- alanın unique olup olmadığı ve CompanyId/işletme bağlamındaki benzersizlik kapsamı;
- alias/eski kod çakışması ve aktif/pasif kayıt görünürlüğü;
- exact eşleşme bulunmadığında fuzzy/full-text önerisinin ayrı ve açık etiketle sunulup
  sunulmayacağı;
- alan bazlı güvenli masking ve highlight politikası.

Tek bir genel `ToLower`, kültürsüz string dönüşümü veya bütün alanlara uygulanan fuzzy
normalizasyon exact kimlik davranışı olarak kodlanamaz.

### 5.2 Full-text / ad-açıklama-OCR sınıfı

Ad, unvan, açıklama, not ve OCR metni exact kimlik alanlarından ayrı bir arama sınıfıdır.

Candidate decision required:

- hangi modül ve alanların full-text'e gireceği;
- field boost/ranking formülü ve eşit skorda deterministik tie-breaker;
- token sınırı, stopword, çoğul/ek/gövdeleme, phrase ve prefix davranışı;
- typo/fuzzy toleransının olup olmayacağı ve güvenli alt sınırları;
- OCR düşük güven, sayfa/versiyon ve belge erişim sınırı;
- pasif/iptal/geçersiz kayıtların varsayılan görünürlüğü;
- snippet/highlight'ın hassas alanı açığa çıkarmaması.

Ranking formülü, eşik veya ağırlık bu readiness belgesinden production kuralına
dönüştürülemez.

## 6. Türkçe arama semantiği ve golden corpus

SQL Server ortamında aşağıdakiler okunup kanıt olarak kaydedilmeden “Türkçe arama
çalışıyor” denmemelidir:

- SQL Server sürüm, edition ve işletim ortamı;
- server/database/ilgili column collation değerleri;
- case/accent sensitivity;
- kurulu full-text özelliği, catalog, language LCID, word breaker ve stoplist;
- Unicode veri tipi ve normalizasyon davranışı;
- sorgu planı ve kullanılan index/catalog.

Golden corpus en az şu sınıfları kapsamalıdır:

- `I`, `İ`, `ı`, `i` kombinasyonları;
- `ç/Ç`, `ğ/Ğ`, `ö/Ö`, `ş/Ş`, `ü/Ü` ve aksansız kullanıcı girdileri;
- NFC/NFD olarak eşdeğer görünen Unicode metinler;
- apostrof, tire, slash, nokta, boşluk ve birleşik yazım;
- Türkçe/İngilizce karma ticari unvan ve ürün adları;
- baştaki sıfırı anlamlı kodlar, barkodlar, belge numaraları ve plakalar;
- eski ürün/cari kodu ile yeni kod alias'ları;
- düşük güvenli OCR, satır sonu, sayfa kırığı ve gürültülü karakterler;
- aynı görünen anahtarın iki şirkette bulunması;
- eşleşmesi gereken, eşleşmemesi gereken ve yalnız öneri olabilecek negatif örnekler.

Her corpus vakası için ham metin, alan türü, sorgu, seçilmiş semantik policy sürümü,
beklenen yetkili result ID'leri, beklenmeyen ID'ler ve sıralama beklentisi tutulmalıdır.
Beklenti karar sahibi tarafından onaylanmadan golden sonuç üretilmiş sayılmaz.

## 7. Search projection ve modül sahipliği

### 7.1 Sınır

- Operasyonel SoT kaynak modülde kalır.
- `Platform.Search` yalnız türetilmiş, yeniden kurulabilir projection ve sorgu
  orkestrasyonunun sahibidir.
- Search, başka modül tablolarına doğrudan sorgu/yazma bağımlılığı kuramaz.
- Search sonucu kaynak kaydı değiştiremez; kalıcı command üretmez.
- Projection silinirse yetkili kaynaklardan yeniden kurulabilmelidir.

### 7.2 Candidate per-module adapter contract

Her pilot modül, kendi verisini açık ve sürümlü Application contract üzerinden Search'e
sunmalıdır. Contract'ın kesin alanları ayrı review ister; en az şu kavramları taşımaya
yeterli olmalıdır:

- zorunlu `CompanyId` ve kaynak modül/result type kimliği;
- opaque projection ID ile kaynak record ID/route bağı;
- kaynak record/version ve contract/schema version;
- exact anahtarların alan türleri ve normalizasyon policy sürümü;
- izinli display title/subtitle ve full-text alanları;
- Site/Location/WorkCenter gibi ABAC kapsam nitelikleri;
- kaynak read permission kodu ve hassasiyet/veri sınıfı;
- aktif/pasif/iptal ve güvenlik görünürlük sinyali;
- değişiklik zamanı, projection zamanı ve rebuild nesli;
- silme/tombstone veya visibility-withdrawal sinyali.

Search contract'ı alan sahibi olmayan modülün iş kuralını, para/risk formülünü veya
görünürlük kararını yeniden hesaplamaz. Bilinmeyen contract sürümü indekslenmez.

İlk pilot modüller, result type'lar, exact alanlar, full-text alanlar, route'lar ve veri
sınıfları **candidate decision required** durumundadır. Bu liste onaylanmadan modül
adaptörü kodlanmamalıdır.

## 8. Projection güncelliği, yeniden kurma ve arıza davranışı

Candidate decision required:

- synchronous DB projection ile transactional outbox tabanlı asynchronous projection
  arasında seçim;
- async ise ERP-000F bağımlılığı, event contract/version, ordering ve replay;
- kabul edilebilir indeks gecikmesi ve kullanıcıya gösterilecek veri kesim zamanı;
- retry, poison, re-drive, dead-letter ve eksik olay tespiti;
- tam/şirket/modül/result-type bazlı rebuild ve doğrulama checksum'ı;
- source silme/pasife alma/yetki daraltma olaylarının önceliği;
- DB commit başarılı, indeks güncellemesi başarısız senaryosundaki görünür durum;
- eski nesil projection ile yeni cursor'ın karıştırılmaması;
- SQL Server ve olası OpenSearch yedek/restore/rebuild sorumluluğu.

Güncellik SLO'su kilitlenene kadar indeks “anlık” diye sunulamaz. UI/API mevcutsa
projection zamanı/lag durumunu saklamadan veya belirsizliği gizleyerek kesin güncel
olduğu izlenimi veremez.

Arama altyapısı arızasında temel ERP ve modül içi yetkili exact erişim davranışı ayrıca
karara bağlanmalıdır. Sessizce global çoklu `LIKE` sorgusuna düşmek veya security
filter'ı zayıflatmak fallback değildir.

## 9. Stabil keyset cursor sözleşmesi

Derin `OFFSET` varsayılanı yasaktır; fakat “keyset kullan” ifadesi tek başına yeterli
değildir.

Candidate decision required:

- exact ve full-text result set'leri için toplam/deterministik sıralama;
- score eşitliğinde kullanılacak değişmez tie-breaker;
- cursor envelope sürümü ve koruma yöntemi;
- cursor'ın CompanyId, aktör/authorization fingerprint, sorgu hash'i, filtre, sort,
  page size, semantik policy ve projection generation ile bağı;
- süre sonu, yetki değişimi, indeks rebuild'i ve query değişiminde invalidation;
- concurrent insert/update/delete altında snapshot veya eventual semantics;
- duplicate/skip kabulü: hedef sıfır olmakla birlikte bunun hangi veri değişim
  modelinde kanıtlanacağı;
- farklı result type'lardan birleşik sıralamanın sahibi.

Cursor istemcinin düzenleyebileceği ham score/ID filtresi olamaz; başka CompanyId,
kullanıcı, permission seti veya sorguda replay edilemez. Cursor decode hatası iç yapı,
indeks nesli veya yabancı kayıt varlığı sızdırmaz.

## 10. Performans, kapasite ve `OPEN-015`

Master'daki sık arama p95 `≤ 300 ms` ve karmaşık arama p95 `≤ 1 s` değerleri başlangıç
önerisidir. `OPEN-015` kapanmadan production SLO veya merge kabul eşiği olarak
sabitlenemez.

Candidate decision required:

- üç yıllık şirket, kullanıcı, cari, ürün, sipariş, stok hareketi, audit, belge ve OCR
  büyüme tahmini;
- yoğun saat eşzamanlı kullanıcı ve sorgu/saniye;
- exact/full-text/autocomplete/facet/result-open iş yükü oranı;
- en ağır sorgu, filtre kombinasyonu, page size ve dışa aktarım davranışı;
- veri tazelik, hata oranı, timeout ve kullanılabilirlik SLO'ları;
- PoC donanımı ile production donanımı arasındaki normalizasyon;
- warm/cold cache ve indeks rebuild sırasında beklenen davranış.

Benchmark planı:

1. `OPEN-015` girdilerinden parametreli sentetik veri üreticisi tanımlanır.
2. Ana kabul seti, hedef üç yıllık hacmin en az üç katında çalıştırılır.
3. Aynı veri CompanyId ve ABAC scope dağılımlarını gerçekçi biçimde taşır.
4. Exact, full-text, typo/OCR, boş sonuç, çok sonuç, derin pagination ve permission
   değişimi ayrı workload olarak ölçülür.
5. p50/p95/p99, throughput, timeout/error, CPU, memory, IO, lock/wait, plan regression,
   full-text lag ve cache etkisi kaydedilir.
6. Ölçüm gerçek SQL Server instance'ında; migration/catalog/index'ler sıfırdan
   kurulmuşken tekrarlanır.
7. Query Store/actual execution plan kanıtı korunur; rastgele çoklu `LIKE`, full table
   scan veya filtre sonrası authorization gizlenmez.
8. OpenSearch karşılaştırması ancak onaylı ADR-007 ölçüm kapısı tetiklenirse yapılır.

## 11. Audit, gizlilik ve veri minimizasyonu

Arama kullanımı müşteri, finans, personel, belge ve ticari sır niyetini açığa
çıkarabilir. Raw query metnini sınırsız loglamak kabul edilemez.

Candidate decision required:

- auditlenmesi gereken search/open/export/denied olayları;
- query metninin ham, normalize, hash, sınıflandırılmış veya redacted saklama biçimi;
- sonuç ID/count/snippet ve filtrelerin audit kapsamı;
- hassas arama yetkisi, maskeleme ve dışa aktarım ilişkisi;
- retention, legal hold, erişim permission'ı ve güvenlik ekibi görünürlüğü;
- destek/debug trace'lerinde PII ve ticari veri redaction'ı;
- anomali/rate-limit sinyalleri ve yüksek hacimli veri toplama denemesi tespiti.

Minimum değişmezler:

- normal log/metric'e raw sonuç payload'ı veya hassas query yazılmaz;
- telemetry CompanyId ve correlation taşır fakat yabancı kaydı ifşa etmez;
- audit kaydı append-only platform sözleşmesini kullanır;
- audit arama sonucunun kendisi ikinci bir yetki sızıntısı oluşturmaz;
- privacy kararı alınmadan AI sağlayıcısına query/snippet gönderilmez.

## 12. AI ve global arama sınırı

- AI aramanın sahibi veya alternatif güvenlik katmanı değildir.
- AI yalnız sürümlü, dar şemalı backend Search tool'u çağırabilir; doğrudan SQL,
  full-text catalog, OpenSearch veya projection deposuna erişemez.
- Tool, çağıran gerçek kullanıcının CompanyContext, RBAC ve ABAC kapsamını aynen uygular;
  servis hesabı ile yetki yükseltme yapmaz.
- AI cevap, özet, kaynak, öneri, “kaç kayıt var” ve hata üzerinden AT-002'yi ihlal
  edemez.
- Kaynak açılışı güncel backend authorization'dan geçer; model çıktısındaki route/ID
  erişim hakkı değildir.
- AI query genişletme, eşanlam, typo düzeltme veya semantik/vector search davranışı
  mevcut kararlarla onaylanmış değildir; candidate decision required'dır.
- AI tool çağrısı model/tool/prompt sürümü, aktör, CompanyId, kapsam, correlation ve
  dönen güvenli kaynak referanslarıyla auditlenir.
- AI kesilirse normal exact/full-text kullanıcı araması çalışmaya devam eder.
- Global arama read-only'dir; AI veya kullanıcı arama sonucundan doğrudan kalıcı write
  uygulayamaz.

## 13. Gerçek SQL Server golden ve güvenlik kabul planı

Aşağıdaki maddeler yeni kanonik `AT-*` kimliği değildir; ERP-000I task-local **aday
kabul senaryolarıdır**. Global kimlik tahsisi karar/acceptance registry review'undan
sonra yapılmalıdır.

### Şirket ve yetki

- A şirketi kullanıcısı B şirketindeki aynı exact kodu sonuç, öneri, facet, count,
  cursor, hata, timing sınıfı veya log üzerinden öğrenemez.
- Site/Location/WorkCenter kapsamlı kullanıcı yalnız kapsamındaki kayıtları görür;
  company-wide kullanıcı onaylı result type'larda şirket kapsamını görür.
- Permission'sız veya bilinmeyen result type/permission eşlemesi sıfır güvenli sonuç
  verir; company-wide'a düşmez.
- Sonuç üretildikten sonra permission daraltılırsa eski cursor/cache ile devam etmek
  kayıt veya önceki toplamı göstermez.
- Result route'u açılırken kaynak modül güncel yetkiyi yeniden kontrol eder.

### Exact ve Türkçe full-text

- Onaylı golden corpus'un tüm pozitif ve negatif vakaları gerçek SQL Server'da aynı
  sonucu verir.
- Exact kod, barkod, plaka ve belge numarası full-text/fuzzy eşleşme ile yanlış kayda
  dönmez.
- `I/İ/ı/i`, aksan, Unicode normalization, noktalama ve baştaki sıfır davranışı onaylı
  policy ile birebir kanıtlanır.
- OCR ve açıklama araması hassas/yetkisiz snippet üretmez.
- SQL Server full-text özelliği veya doğru dil kaynağı yoksa test sessiz fallback yerine
  fail eder.

### Pagination ve değişim

- Aynı sabit veri setinde ilk sayfadan son sayfaya hiçbir result ID atlanmaz veya iki
  kez dönmez.
- Eşit score'lar deterministik tie-breaker ile aynı sırada kalır.
- Cursor başka CompanyId, aktör, permission, sorgu, filtre, page size veya projection
  neslinde kullanılamaz.
- Onaylı concurrent update senaryolarında seçilen snapshot/eventual semantics açıkça
  kanıtlanır; belirsiz duplicate/skip kabul edilmez.

### Projection ve arıza

- Kaynak değişiklik projection'a seçilen güncellik sözleşmesi içinde yansır.
- Pasif/silinmiş/yetkisi daraltılmış kayıt öncelikli visibility withdrawal kuralına
  uyar.
- Aynı event/replay ikinci projection kaydı üretmez; eski event yeni source version'ı
  ezmez.
- Projection şirket/modül/result type bazında silinip kaynaktan yeniden kurulabilir;
  sayım ve checksum mutabakatı geçer.
- DB commit ile indeks arızası arasındaki her crash noktası görünür, tekrar edilebilir
  ve veri sızıntısız toparlanır.

### Performans ve gözlemlenebilirlik

- `OPEN-015` kapandıktan sonra üç kat veriyle exact/full-text/derin sayfalama workload'u
  onaylı p95/p99 ve hata oranı kapılarını geçer.
- Test warm/cold cache, eşzamanlı kullanıcı, indeks güncellemesi ve rebuild etkisini
  ayrı raporlar.
- Execution plan, index kullanımı, full-text lag, timeout ve slow-query telemetry'si
  correlation ile izlenebilir.
- Telemetry ve audit içinde raw hassas payload bulunmaz.

### AI sınırı

- AI Search tool'u aynı kullanıcının normal global aramasından daha geniş sonuç,
  toplam, snippet veya kaynak döndüremez.
- Tool'a prompt içinde yabancı CompanyId/record ID verilmesi yetkiyi değiştirmez.
- AI ve arama sağlayıcısı kapalıyken temel ERP akışı çalışır; global arama hatası
  kontrollü ve veri sızıntısızdır.

## 14. Uygulama öncesi bağımlılık ve karar kapısı

ERP-000I implementation ancak aşağıdaki kanıtların tamamı ayrı review ile
onaylandığında GO olabilir:

1. Fiziksel ADR-007 oluşturulmuş, register ile durumu tutarlı ve SQL Server/PostgreSQL
   drift'i çözülmüş olmalıdır.
2. `OPEN-015` için iş hacmi sahibi, değerler ve performance/SLO kapanış planı
   bulunmalıdır.
3. Türkçe exact/full-text semantik policy ve golden corpus onaylanmalıdır.
4. İlk pilot modüller, result type'lar, alanlar, route'lar, veri sınıfları ve kaynak
   permission/ABAC eşlemeleri onaylanmalıdır.
5. Per-module adapter contract ve Search modül ownership sınırı sürümlü olmalıdır.
6. Projection update/freshness/rebuild/fallback seçimi onaylanmalıdır; async çözüm
   seçilirse ERP-000F açık bağımlılık olmalıdır.
7. Stable keyset cursor ve permission-change invalidation sözleşmesi onaylanmalıdır.
8. Audit/privacy/redaction/retention ve AI Search tool sınırı onaylanmalıdır.
9. Gerçek SQL Server test ortamında gerekli full-text/locale özellikleri kanıtlanmalı;
   container/CI parity planı yazılmalıdır.
10. Existing `AT-002` ve `AT-069` ile task-local aday senaryoların global acceptance
    eşlemesi review edilmelidir; yeni kimlikler merkezî sicilden verilmelidir.
11. ERP-000B/C/D/E güvenlik omurgasıyla contract test bağlantısı kurulmalıdır;
    observability için ERP-000L ve SQL/load kanıtı için ERP-000M sorumluluk sınırı
    netleştirilmelidir.
12. Migration/rollback, feature flag ve devre dışı bırakma planı onaylanmalıdır.

Bu kapılar kapanmadan:

- `Platform.Search` modülü veya public Search API oluşturulmaz;
- SQL full-text catalog/index migration'ı yazılmaz;
- OpenSearch/container/package eklenmez;
- kaynak modül tablosuna doğrudan Search bağımlılığı kurulmaz;
- production ranking, normalizasyon, timeout, page size veya SLO değeri tahmin edilmez;
- task `ready/done` kabul edilmez.

## 15. Önerilen sonraki kesin adım

Kodlama değil, insan review'una sunulacak bir **ADR-007 + Search Contract karar paketi**
hazırlanmalıdır. Paket önce mevcut karar kimliklerini ve `OPEN-015`'i kullanmalı; yeni
kanonik decision/acceptance kimliği gerekiyorsa yalnız merkezî registry sahibi tarafından
tahsis edilmelidir.

İlk review oturumu şu dört çıktıyla sona ermelidir:

1. SQL Server PoC seçenek ve ölçüm matrisi,
2. Türkçe exact/full-text golden corpus taslağı,
3. ilk pilot modül/result type/permission/ABAC veri sınıflandırma matrisi,
4. projection + cursor + permission invalidation + audit/privacy sözleşme taslağı.

Bu dört çıktı onaylanır ve yukarıdaki 12 kapı kapanırsa ERP-000I için ayrı bir
implementation branch/worktree açılabilir. O zamana kadar sonuç değişmez:

> **ERP-000I implementation: NO-GO. Readiness/specification review: GO.**
