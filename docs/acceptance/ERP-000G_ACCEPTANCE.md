# ERP-000G — Belge Kasası Çekirdeği Ayrıntılı Kabul Planı

- Tarih: 2026-08-28
- Durum: Taslak; test, fixture ve kod kanıtı yok
- Kimlik kapsamı: yalnız bu görevde geçerli `ERP-000G-AC-001`…`ERP-000G-AC-025`
- Bağlayıcı kaynaklar: `DEC-DOC-001`…`DEC-DOC-003`, `DEC-DOC-008`,
  `DEC-DOC-009`, öneri durumundaki `ADR-005` ve ERP-000G feature spec
- Implementation kapısı: `ADR-005` kabul edilmeden ve `OPEN-026`, `OPEN-027`,
  `OPEN-028`, `OPEN-029` kapanmadan fixture, test-substrate, production kodu,
  migration, bucket/prefix veya scanner adapter'ı oluşturulmaz

## Kimlik ve kanıt kuralı

Bu dosyadaki `ERP-000G-AC-*` kimlikleri görev yerelidir; global
`ACCEPTANCE_MATRIX.csv` için `AT-*` kimliği ayırmaz veya tahmin etmez. Bütün senaryolar
`Taslak`tır. Gerçek test sınıfı, gerçek SQL Server ve gerçek S3-compatible CI run kanıtı
üretilmeden durum yükseltilemez. Boş `Kanıt` alanı tamamlanmışlık iddiası değildir.

## Ortak kabul düzeni

Kabul ortamı en az şunları sağlar:

- Uygulama migration'ları uygulanmış, CI ile aynı engine/sürüm ailesindeki gerçek SQL
  Server. EF InMemory, SQLite veya repository mock'u relational kanıt yerine geçmez.
- Ağ üzerinden S3 API'si konuşulan gerçek S3-compatible nesne deposu ve yalnız kabul
  testine ayrılmış bucket/prefix. Yerel klasör, byte-array/in-memory store veya yalnız
  mocked SDK çağrısı nesne deposu kanıtı sayılmaz.
- Birbirinden bağımsız A ve B şirketleri; company-wide ve dar kapsamlı kullanıcılar;
  yetkisiz, pasif ve sistem principal örnekleri.
- Aynı byte dizisine sahip iki dosya, farklı byte dizileri, sıfır byte, sınır boyutu,
  Türkçe/Unicode ad, MIME–magic uyuşmazlığı ve güvenli scanner test imzası örnekleri.
- Kontrol edilen clock; paralel upload barrier'ı; SQL commit, staged object yazımı,
  finalization, scanner sonucu ve reconciliation adımlarında ayrı failure injection.
- Her senaryoda SQL satırları ile object key/version/ETag/byte length ve yeniden hesaplanan
  content hash'in başlangıç/son sayımları. Provider ETag'i içerik hash'i sayılmaz.
- Her test için benzersiz CompanyId, DocumentId, VersionId, command/idempotency ve object
  namespace. Temizlik yalnız testin kendi kayıtlarını etkiler; production retention veya
  imha davranışı test cleanup'ı ile taklit edilmez.
- Log, metric, trace, HTTP response ve signed access grant'larında storage credential,
  ham token, erişilemeyen şirket hash'i/object key'i veya belge içeriği bulunmadığına dair
  negatif assertion.

Fiziksel dedup kapsamı, object-key reçetesi, staged/final state machine, scanner ve signed
access politikaları açık kararlarla kesinleşmeden beklenen provider/threshold/TTL değeri
uydurulmaz. Aşağıdaki senaryolar bu değerlerden bağımsız güvenlik ve bütünlük sınırlarını
tanımlar.

Bu plandaki `Staged`, `Quarantine` ve `Ready` sözcükleri semantik test durumlarını anlatan
geçici etiketlerdir; kalıcı machine-code state adları değildir. Exact adlar/geçişler
`OPEN-027` ile kapanır, fakat clean kanıtı olmadan erişim vermeme değişmezi korunur.

## ERP-000G-AC-001 — Gerçek SQL ve gerçek S3-compatible sözleşme smoke testi

**Given** kabul ortamında boş belge tabloları ve erişilebilir test bucket/prefix'i vardır.

**When** izinli küçük bir byte stream staged olarak yazılır, aynı stream depodan yeniden
okunur ve metadata SQL Server'a kalıcılaştırılır.

**Then** yazılan ve okunan byte sayısı ile uygulamanın canonical content hash'i aynıdır;
SQL metadata'sı gerçek provider'ın opaque object referansına bağlanır. Test hiçbir noktada
yerel disk/in-memory fallback'e sessizce dönmez. Object store erişilemezse işlem başarılı
gösterilmez ve DB'de `Ready` belge oluşmaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-002 — Dosya kabul doğrulaması ve MIME fail-closed davranışı

**Given** izinli uzantı/MIME, yasak tür, MIME–magic uyuşmazlığı, sınır boyutu, boş içerik ve
kontrol karakterli dosya adı örnekleri vardır.

**When** upload kabul sınırı çalışır.

**Then** yalnız onaylı politika ile uyumlu içerik staged duruma geçer. Yalnız istemci
uzantısı veya `Content-Type` başlığı güven kaynağı olmaz; uyuşmazlık fail-closed reddedilir
veya quarantine'e alınır. Reddedilen içerik için kullanılabilir logical document/version,
signed URL veya dışa aktarım oluşmaz; hata iç storage key/credential açıklamaz.

Kesin allowlist, boyut ve scanner politikası açık karar kapanmadan sabit değer kazanmaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-003 — Şirket izolasyonlu dedup ve existence-oracle engeli

**Given** A şirketinde saklanmış bir içerik ve aynı byte/hash ile upload deneyen B şirketi
vardır.

**When** B upload başlatır, tekrar kontrolü yapar veya ID/hash/object key/header/error/count
ve zamanlama farklarından A'daki içeriği anlamaya çalışır.

**Then** B, A'da blob, logical document, version veya link bulunduğunu öğrenemez. Response
status/body/header, kullanıcıya gösterilen tekrar sonucu, ölçüm ve güvenli hata metni A'nın
varlığını açıklamaz. Fiziksel dedup tenantlar arasında seçilmiş olsa bile bu yalnız iç
storage detayıdır; A'nın DocumentId, hash'i, object key'i, sahibi, sayısı veya metadata'sı B
bağlamına dönmez.

Tenant-local veya global physical dedup seçimi `OPEN-026` kapanmadan expected storage
object sayısına dönüştürülmez.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-004 — Aynı şirkette tekrar içerik ve ayrı iş bağlamları

**Given** A şirketinde aynı byte içeriğini farklı yetkili iş bağlamlarına bağlayan iki
komut vardır.

**When** ikinci komut content hash kontrolünden geçer.

**Then** fiziksel dedup politikası izin veriyorsa byte kopyası yeniden yazılmaz; buna
karşılık her logical document/version/link kendi CompanyId, gizlilik, kaynak, sürüm ve iş
bağlamını korur. Bir bağlamdaki permission diğerine taşınmaz. Kullanıcıya gösterilecek
mevcut-kayıt bilgisi yalnız o kullanıcının erişebildiği A şirketi kayıtlarıyla sınırlıdır.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-005 — Paralel aynı-içerik upload yarışı

**Given** aynı CompanyId ve byte içeriği için iki bağımsız request/worker aynı anda hash ve
staged upload sınırına ulaşır.

**When** gerçek SQL unique/concurrency sınırı ile gerçek S3-compatible yazımlar yarışır.

**Then** bozuk veya kısmi final object oluşmaz; kabul edilen physical dedup politikasından
fazla canonical byte kopyası kalmaz. Her başarılı command'ın logical sonuç sayısı açık
idempotency ve iş bağlamı sözleşmesine uyar; kaybeden yarış stale storage referansı veya
ikinci business/audit etkisi üretmez. Geçici staged artıkları reconciliation tarafından
güvenli ve idempotent biçimde ele alınabilir durumdadır.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-006 — Başarılı staged upload ve görünürlük sınırı

**Given** izinli ve taramaya uygun bir dosya upload edilir.

**When** staged byte yazımı, hash doğrulaması, SQL metadata transaction'ı, scanner sonucu
ve finalization onaylı state machine sırasıyla tamamlanır.

**Then** yalnız tam ve hash'i doğrulanmış version `Ready` olur. Staged/quarantine object
doğrudan belge sorgusunda, indirmede, signed URL'de, export'ta veya aramada görünmez. SQL ve
object-store arasında distributed transaction/exactly-once iddiası yapılmaz; görünürlük
kalıcı state ve reconciliation sözleşmesiyle sağlanır.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-007 — Crash, retry ve reconciliation matrisi

**Given** kontrollü crash noktaları en az staged object öncesi, staged object sonrası/SQL
öncesi, SQL commit sonrası/finalization öncesi, final object sonrası/SQL state öncesi ve
scanner sonucu sırasında tanımlanmıştır.

**When** her noktada worker durdurulur, aynı command güvenli biçimde retry edilir ve
reconciliation birden fazla kez çalıştırılır.

**Then** hiçbir durumda kısmi byte `Ready` görünmez, kayıp metadata ile kullanıcıya açık
object kalmaz, aynı logical version sessizce iki kez oluşmaz ve yeni byte eski version'ın
üzerine yazılmaz. Reconciliation aynı girdide idempotenttir; yalnız kendi tenant/prefix
kapsamını işler, aktif lease/upload'ı silmez ve çözemediklerini görünür quarantine/alarm
durumuna taşır.

Staged orphan yaşı, lease ve imha eşikleri `OPEN-018`, `OPEN-026`, `OPEN-027` ve
`OPEN-029` kapanmadan
sayı olarak uydurulmaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-008 — Malware taraması, quarantine ve scanner arızası

**Given** temiz içerik, güvenli test imzasıyla zararlı kabul edilen içerik, scanner timeout,
scanner erişilemez ve desteklenmeyen scanner-result sürümü vardır.

**When** scan pipeline çalışır.

**Then** yalnız doğrulanmış temiz sonuç version'ı indirme/paylaşım/export için uygun yapar.
Zararlı, belirsiz, timeout veya doğrulanamayan sonuç fail-closed quarantine durumunda kalır;
signed access grant üretilmez ve önceki temiz version sessizce değiştirilmez. Yetkili
quarantine görünümü içerik veya başka şirket varlığı sızdırmadan neden kategorisi,
correlation ve audit referansı taşır.

Scanner seçimi ve retry/timeout eşikleri `OPEN-027` kapanmadan kodlanmaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-009 — Kalıcı byte ve content-hash değişmezliği

**Given** `Ready` bir DocumentVersion yeni DB context'i ve object-store client'ıyla yeniden
okunmuştur.

**When 1** normal application writer ve doğrudan SQL, aynı VersionId'nin content hash,
byte length veya object referansını update/delete ile değiştirmeye çalışır.

**Then 1** işlem reddedilir; yeni içerik yalnız yeni version ile temsil edilebilir.

**When 2** normal runtime storage principal aynı final object key'in byte'ını overwrite
etmeye çalışır; ayrıca kontrollü test altyapısı object byte'ını dışarıdan değiştirir veya
yanlış object referansı döndürür.

**Then 2** runtime overwrite provider/policy tarafından reddedilir; yetkili tamper
fixture'ının oluşturduğu canonical hash/length uyuşmazlığı indirmeden önce veya integrity
verification işinde yakalanır. İçerik sunulmaz, güvenli quarantine/alarm/audit oluşur.
Provider ETag eşleşmesi tek bütünlük kanıtı değildir.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-010 — Logical document ve version yaşam döngüsü

**Given** A şirketinde bir logical document ve ilk temiz version vardır.

**When** yetkili kullanıcı revize byte, metadata veya belge durumu oluşturur.

**Then** önceki version üzerine yazılmaz; yeni immutable version oluşur. Aktif/geçersiz
işaretleme onaylı concurrency kuralına uyar, geçmiş sırası ve hash'ler korunur. İki kullanıcı
aynı expected version ile aktif sürümü değiştirmek için yarışırsa yalnız biri kazanır;
diğeri güvenli conflict alır ve gizli metadata öğrenmez.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-011 — Kayıt linkleri ve CompanyId bütünlüğü

**Given** A ve B şirketlerinde logical document/version ile farklı modüllere ait kayıt
tanımlayıcıları vardır.

**When** aynı version birden çok A kaydına bağlanır veya A belgesi B bağlamına/target'ına
bağlanmaya çalışılır.

**Then** A içindeki izinli linkler byte kopyalamadan ayrı link metadata'sı oluşturur.
Document–Version–Link arasındaki CompanyId uyumu gerçek SQL constraint ve application
guard ile korunur; A→B linki fail-closed reddedilir. Document modülü başka modülün
Infrastructure/tablosuna doğrudan yazmaz; target ownership onaylı Application contract ile
doğrulanır. Hata B kaydının varlığını açıklamaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-012 — Signed access grant üretme yetkisi

**Given** erişilebilir hazır version, quarantine version, başka şirkete ait version ve
dışa aktarma/indirme yetkisi farklı kullanıcılar vardır.

**When** signed access istenir.

**Then** server aktif CompanyContext, version state'i, permission/ABAC, gizlilik ve export
politikasını issue anında fail-closed doğrular. Yalnız erişilebilir hazır version için kısa
ömürlü, tek amaçlı ve exact method/resource'a bağlı grant üretilebilir. Client storage
credential, bucket listing yetkisi, raw object key veya başka tenant hash'i alamaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-013 — Signed access redeem ve kaynak bağlama

**Given** geçerli bir grant exact CompanyId/VersionId/işlem için üretilmiştir.

**When** grant doğru kaynakta redeem edilir; sonra object key, version, HTTP method,
content disposition, range veya tenant bağlamı manipüle edilir.

**Then** yalnız onaylı kaynak/işlem başarılıdır. Değiştirilmiş veya tekrar kullanım
politikasını aşan istek reddedilir; açık bucket/object keşfi yoktur. İndirilen byte'ın
length/hash'i immutable version metadata'sıyla doğrulanır. Kullanıcı görünen dosya adı
header injection'a yol açmadan güvenli encode edilir.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-014 — Signed access süresinin dolması

**Given** onaylı TTL ile üretilmiş grant ve kontrol edilen server clock vardır.

**When** grant expiry öncesinde ve tam sınır/sonrasında redeem edilir.

**Then** yalnız politika tarafından geçerli pencere kabul edilir; expiry sonrasında object
store byte'ı dönmez. Client saati otorite değildir. Hata token, imza girdisi, object key veya
belge varlığını açıklamaz; clock-skew davranışı açık politika kapanmadan uydurulmaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-015 — Signed access iptali ve sonradan daralan yetki

**Given** henüz süresi dolmamış grant vardır.

**When** grant açıkça revoke edilir; kullanıcı/permission/company membership pasif olur;
version quarantine/geçersiz durumuna alınır veya document için dış erişim kapatılır.

**Then** onaylı revocation kapısına göre sonraki redeem fail-closed reddedilir ve olay
auditlidir. Uygulama politikasını atlayarak expiry'ye kadar çalışmaya devam eden çıplak
provider URL'si, anlık revocation gerektiren kabulü karşılamaz. Revocation kapsamı ve SLA'sı
`OPEN-028` kapanmadan production davranışı sayılmaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-016 — Paylaşılan referans altında silme ve garbage collection

**Given** aynı physical content'e bağlı birden çok logical document/version/link ve en az
iki iş bağlamı vardır.

**When** bir link kaldırılır, logical document pasif/iptal edilir veya yetkili imha işi
çalışır.

**Then** kalan erişilebilir referansların byte'ı ve metadata'sı korunur. Business silme
doğrudan object delete değildir. Physical object ancak onaylı retention/legal hold,
reference ve reconciliation kontrollerinin tamamı uygun olduğunda yetkili/auditli GC ile
ele alınabilir. A şirketindeki işlem B şirketi referansının varlığını veya sayısını
göstermez ve B'nin erişimini bozmaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-017 — Şirket kapsamlı export bütünlüğü

**Given** A şirketinde version'lar, linkler, paylaşılan physical content, quarantine ve
pasif/geçersiz kayıtlar; B şirketinde aynı hash veya çakışan teknik tanımlayıcıları kullanan
ayrı kayıtlar vardır.

**When** yetkili A export'u alınır.

**Then** export yalnız politika kapsamındaki A logical metadata/link/version ve gerekli
byte'ları taşır; B kimliği, metadata'sı, sayısı, object key'i veya iş bağı görünmez.
Manifest her dahil byte için canonical hash/length, şema sürümü ve kaynak logical ilişkiyi
taşır. Aynı byte export paketinde onaylı biçimde tekilleşebilir fakat bütün A referansları
yeniden kurulabilir kalır. Quarantine/retention dahil etme davranışı açık politika ile
belirlenir; sessizce varsayılmaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-018 — Gerçek restore ve checksum mutabakatı

**Given** ERP-000G-AC-017 ile üretilmiş export/backup kanıtı ve boş, izole gerçek SQL +
gerçek S3-compatible restore hedefi vardır.

**When** restore uygulanır, yarıda crash edilip retry edilir ve sonrasında reconciliation
çalışır.

**Then** restore idempotenttir; her restored byte yeniden hash/length kontrolünden geçer,
logical document–version–link ilişkileri manifestle sayısal olarak mutabıktır ve kısmi
restore `Ready` görünmez. Storage key'lerin aynı kalması gerekmez; immutable technical
identity ve içerik bütünlüğü korunur. Eksik/bozuk object fail-closed raporlanır, sessizce boş
dosyayla tamamlanmaz ve B tenant verisi oluşmaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-019 — Bütün okuma yollarında no-leak

**Given** yalnız A yetkili bir kullanıcı ve B'ye ait bilinen/tahmin edilen DocumentId,
VersionId, LinkId, hash, object key ve signed grant örnekleri vardır.

**When** query, detay, download, HEAD/range, signed issue/redeem, export, tekrar kontrolü,
arama, count ve hata yolları ID/route/filter/header manipülasyonuyla çağrılır.

**Then** B byte'ı, adı, hash'i, state'i, toplamı, linki, owner'ı, correlation'ı veya varlık
bilgisi açıklanmaz. Dar scope'lu kullanıcı company-wide belge toplamını öğrenemez. 403/404
ve güvenli hata politikası tutarlıdır; log/metric de hassas B tanımlayıcısını A aktörüne
yansıtmaz. Repeated timing gözlemleri dedup fast-path'i güvenilir existence oracle'a
dönüştürmez; ölçüm bütçesi açık politikada tanımlanır.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-020 — Audit, correlation, idempotency ve atomik DB davranışı

**Given** upload başlatma, version oluşturma, link ekleme/kaldırma, quarantine, access grant,
revocation ve yetkili imha komutları vardır.

**When** her komut başarı, aynı-key replay, aynı-key/farklı-payload, expected-version
conflict ve object-store hata yollarında çalışır.

**Then** yalnız onaylı DB transaction sınırındaki business metadata + audit + idempotency
sonucu atomik commit/rollback olur. Aynı komut ikinci logical effect/version/link/audit
oluşturmaz; farklı payload fail-closed reddedilir; conflict eski byte/metadata sızdırmaz.
Object-store yan etkisi SQL transaction'a dahilmiş gibi gösterilmez; staged state ve
reconciliation correlation/causation zincirini korur. Audit actor, CompanyId, reason,
source channel, before/after ve ilgili Document/Version referansını taşır; ham token veya
dosya içeriği taşımaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-021 — Gözlemlenebilirlik ve veri minimizasyonu

**Given** upload başarı/hata, staged yaşlanma, reconciliation, hash mismatch, scanner
quarantine, signed issue/redeem/expiry/revocation ve GC aday olayları vardır.

**When** structured log, metric ve trace kayıtları incelenir.

**Then** correlation ile pending/staged/quarantine derinliği, en yaşlı kayıt, upload/scan/
finalize latency, retry/reconciliation, integrity failure, access grant ve restore sonucu
tanılanabilir. Log/metric/trace içinde byte içeriği, malware örneği, storage secret,
presigned query/token, ham hash ile existence oracle veya yetkisiz tenant metadata'sı yoktur.
Alarm eşikleri açık karar kapanmadan sabitlenmez.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-022 — Migration, forward-fix ve provider değişimi güvenliği

**Given 1** temiz SQL Server; **Given 2** A–E platform migration'ları uygulanmış mevcut
SQL Server; her ikisi için izole gerçek S3-compatible test alanı vardır.

**When** belge migration'ları/idempotent script uygulanır ve yeniden çalıştırılır; ardından
binary rollback/forward-fix provası staged, ready, quarantine ve shared-ref kayıtlarıyla
yapılır.

**Then** schema, composite CompanyId constraint, immutable metadata guard ve index sonuçları
iki ortamda aynıdır; tekrar ikinci nesne/veri oluşturmaz. Rollback tablo/object drop ederek
kanıt kaybetmez. Forward-fix mevcut object referansını, hash'i, version/link geçmişini ve
tenant izolasyonunu korur. Provider değişimi kabul kapsamına alınırsa byte'lar yeniden hash
doğrulamasıyla taşınır; yalnız ETag veya dosya adı mutabakat sayılmaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-023 — UI durumları ve erişilebilirlik

**Given** klavye ve screen reader kullanan yetkili bir kullanıcı; boş liste, upload
ilerlemesi, tarama bekliyor, quarantine/red, geçici bağımlılık hatası, version conflict,
checksum mismatch ve expired access durumları vardır.

**When** kullanıcı liste, upload, version geçmişi, güvenli hata ve yeniden deneme akışlarını
tamamlar.

**Then** bütün eylemler klavye ile erişilebilir; focus mantıklı sırada ve dialog kapanınca
güvenli kaynaktadır. Progress/durum değişimleri anlamlı label ve canlı duyuru taşır; hata,
quarantine ve conflict yalnız renk/ikonla anlatılmaz. Yükleniyor veya taranıyor durumundaki
byte hazır belge gibi gösterilmez; güvenli correlation kimliği storage ayrıntısı sızdırmadan
sunulur.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-024 — İşlem sırasında CompanyContext değişimi

**Given** A şirketinde açık upload, preview, signed-access hazırlığı veya paylaşım taslağı
bulunan bir kullanıcı vardır.

**When** aktif şirket bağlamı B'ye değiştirilir, eski sekme/istek A işlemine devam etmeye
veya A sonucunu B ekranında kullanmaya çalışır.

**Then** eski işlem B bağlamına taşınmaz. Server request-scoped immutable CompanyContext'i
yeniden doğrular; A içeriği, metadata'sı, sonucu veya grant'i B'ye açıklanmadan işlem iptal,
yeniden yetkilendirme ya da güvenli kapanış sözleşmesine gider. A'daki yarım upload normal
cleanup/reconciliation kanıtını korur; B altında ikinci document/version/link oluşmaz.

**Durum:** Taslak

**Kanıt:**

## ERP-000G-AC-025 — Kullanıcı başlatmalı paylaşım ve güvenli Unicode ad

**Given** paylaşmaya yetkili kullanıcı, temiz ve erişilebilir bir version ile Türkçe/Unicode
ve yasak dosya karakterleri içeren görünen ada sahiptir.

**When 1** istemci işletim sistemi/tarayıcı dosya paylaşım seçicisini destekler.

**Then 1** paylaşım yalnız kullanıcının açık eylemiyle, yeniden yetkilendirilen seçili
version için hazırlanır; WhatsApp Business/API veya otomatik gönderim çağrısı yapılmaz.

**When 2** paylaşım seçicisi desteklenmez.

**Then 2** güvenli fallback, yetkili dosyayı indirme ve açıklama metnini kopyalama imkânı
verir. Kullanıcı görünen adı Türkçe karakterleri koruyan, header injection/yasak karakter
riski taşımayan `Content-Disposition` metadata'sıdır; object locator açıklanmaz. Exact PDF
adlandırma ve font/render oracle'ı ERP-000H kabulüne bırakılır.

**Durum:** Taslak

**Kanıt:**

## Kanıt matrisi

| Kabul | Contract/Unit | Gerçek SQL | Gerçek S3-compatible | Concurrency/Crash | Security/Audit | Restore/Observability |
|---|---:|---:|---:|---:|---:|---:|
| ERP-000G-AC-001 | Zorunlu | Zorunlu | Zorunlu | — | Credential no-leak | — |
| ERP-000G-AC-002 | Zorunlu | Gerekirse | Zorunlu | — | Fail-closed | — |
| ERP-000G-AC-003 | Zorunlu | Zorunlu | Zorunlu | Timing oracle | Zorunlu | Metric no-leak |
| ERP-000G-AC-004 | Zorunlu | Zorunlu | Zorunlu | — | Scope zorunlu | — |
| ERP-000G-AC-005 | Idempotency | Zorunlu | Zorunlu | Zorunlu | Audit count | Reconciliation |
| ERP-000G-AC-006 | State contract | Zorunlu | Zorunlu | — | Görünürlük | — |
| ERP-000G-AC-007 | Zorunlu | Zorunlu | Zorunlu | Her failure point | Tenant kapsamı | Reconciliation |
| ERP-000G-AC-008 | Scanner contract | Zorunlu | Zorunlu | Timeout/retry | Quarantine | Alarm |
| ERP-000G-AC-009 | Hash contract | Zorunlu | Zorunlu | Tamper | Audit | Integrity alarm |
| ERP-000G-AC-010 | Version contract | Zorunlu | Zorunlu | Concurrency | Audit | — |
| ERP-000G-AC-011 | Link contract | Zorunlu | Gerekirse | — | CompanyId | — |
| ERP-000G-AC-012 | Grant contract | Zorunlu | Zorunlu | — | Authorization | Audit |
| ERP-000G-AC-013 | Grant contract | Gerekirse | Zorunlu | Manipülasyon | Authorization | Access trace |
| ERP-000G-AC-014 | Clock/TTL | — | Zorunlu | Expiry boundary | Safe error | Metric |
| ERP-000G-AC-015 | Revocation | Zorunlu | Zorunlu | Yetki yarışı | Zorunlu | Audit |
| ERP-000G-AC-016 | Ref/GC contract | Zorunlu | Zorunlu | GC yarışı | Retention/audit | Reconciliation |
| ERP-000G-AC-017 | Export contract | Zorunlu | Zorunlu | — | Tenant no-leak | Manifest |
| ERP-000G-AC-018 | Restore contract | Zorunlu | Zorunlu | Crash/retry | Tenant no-leak | Zorunlu |
| ERP-000G-AC-019 | API contract | Zorunlu | Zorunlu | Timing/count | Zorunlu | Log no-leak |
| ERP-000G-AC-020 | Idempotency | Zorunlu | Zorunlu | Conflict/retry | Zorunlu | Correlation |
| ERP-000G-AC-021 | Redaction | Gerekirse | Gerekirse | Senaryo seti | Zorunlu | Zorunlu |
| ERP-000G-AC-022 | Migration | Zorunlu | Zorunlu | Forward-fix | Kanıt korunur | Zorunlu |
| ERP-000G-AC-023 | UI/a11y | — | — | UI state | Klavye/screen reader | Güvenli correlation |
| ERP-000G-AC-024 | Context contract | Zorunlu | Zorunlu | Context switch | CompanyId no-leak | Reconciliation |
| ERP-000G-AC-025 | Share/filename contract | Gerekirse | Zorunlu | Client capability | User initiated/no auto-send | Access audit |

## Kabul çıkış kapısı

- `ADR-005` kabul edilmeden ve `OPEN-026`…`OPEN-029` kapanmadan bu belge fixture, paket,
  provider, bucket, test container veya production davranışı seçme yetkisi vermez.
- Bütün senaryoların `Kanıt` alanı gerçek test sınıfı ve CI run kimliğiyle doldurulmadan
  `Taslak` durumu değişmez.
- Gerçek S3-compatible kanıt gereken senaryoda fake/in-memory/local-folder; gerçek SQL
  gereken senaryoda EF InMemory/SQLite kabul edilmez.
- Crash testinin gerçekten hedef failure point'e ulaştığı ve reconciliation'ın en az iki
  kez aynı sonuçla çalıştığı ayrıca kanıtlanır.
- Signed access testi yalnız URL üretmeyi değil issue, redeem, expiry ve revocation
  davranışlarının tamamını doğrular.
- Cross-company, shared-reference ve restore testlerinde başlangıç/son SQL + object-store
  sayımı ve hash mutabakatı bulunur.
- P0/P1 açık bulgu, skipped/ignored kritik test, sahte kanıt, stale `OPEN-*` veya
  kanıtsız manuel iddia varken ERP-000G `done` veya `accepted` sayılamaz.
