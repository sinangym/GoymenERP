# ERP-000M — SQL Server PoC ve Migration Hazırlık Kapısı

- Tarih: 2026-08-28
- İncelenen taban: `main` / `81283e582de00fc291b848a25f1a15ff45fc4657`
- Görev: `ERP-000M — SQL Server PoC ve migrations`
- Kapsam: yalnız readiness analizi, karar kapısı, ölçüm ve kabul planı
- Uygulama durumu: **NO-GO / BLOCKED**
- Readiness belge durumu: **GO**

## 1. Yönetici özeti

Göymen ERP'nin mevcut `main` dalı SQL Server üzerinde çalışan boş bir iskelet değildir.
EF Core context'leri, modül şemaları, ayrı migration history tabloları, idempotent SQL
script üretimi ve gerçek SQL Server relational acceptance testi bulunmaktadır. Bu mevcut
kanıt korunmalı ve ERP-000M kapsamında tekrar icat edilmemelidir.

Bununla birlikte mevcut kanıt SQL Server'ı geri dönülmez production veritabanı seçimi
yapmaya yeterli değildir. Türkçe exact/full-text arama primitive'leri, temsilî hacim,
yük altında tenant/yetki davranışı, migration sırası ve arızası, deadlock/race davranışı,
büyük audit/keyset yükü ve DB-only restore/checksum henüz bağlayıcı kabul kanıtına sahip
değildir.

Kodlamayı durduran kapılar şunlardır:

1. `ADR_REGISTER.md`, ADR-003'ü “Geçici” SQL Server PoC kararı olarak gösterir; fakat
   `docs/adr/` altında ölçüm seçeneklerini, karar ölçütlerini ve geri dönüş yolunu taşıyan
   fiziksel ADR-003 yoktur.
2. `ADR_REGISTER.md`, ADR-010'u “Kabul” gösterir; fakat ayrı migration sahibi, yürütme
   kimliği, sıra, failure, rollback/forward-fix ve yayın manifestini tanımlayan fiziksel
   ADR-010 yoktur.
3. `OPEN-014`, `OPEN-015` ve `OPEN-017`; altyapı envanteri, veri/iş yükü ve kesin
   RPO/RTO/felaket topolojisini açık bırakır. Bu girdiler olmadan edition, kapasite,
   production eşikleri veya restore kabulü kilitlenemez.
4. SQL Server PoC test matrisi, migration güvenlik sözleşmesi ve kanıt formatının bu
   belgedeki task-local toplu adı `ERP-000M-SQL-POC-CONTRACT`tır. Bu etiket kanonik bir
   `OPEN-*`, `AT-*` veya ADR kimliği değildir.

Bu readiness dalı yalnız bu belgeyi ve manifestte ERP-000M satırını değiştirir. Yeni
kanonik karar/kabul kimliği üretmez; provider, edition, compatibility level, collation,
full-text dili, isolation level, topoloji, süre veya performans eşiği seçmez.

## 2. Bağlayıcı kaynaklar ve repo gerçeği

Kaynak önceliği kök `AGENTS.md` sırasına göre uygulanmıştır.

| Kaynak | ERP-000M için bağlayıcı sonuç |
|---|---|
| `DEC-GOV-002` | Her kritik kayıtta `CompanyId` zorunludur; grup görünümü veri birleştirme yetkisi değildir. |
| `DEC-GOV-003` | Şirket geçmişi ve audit'iyle ayrı taşınabilir olmalı; şirket veri paketi export/restore kanıtı gerekir. |
| `DEC-TEC-001` | Modüler monolit ve modül sahipliği korunur; bir modül başka modül tablosunu sahiplenmez. |
| `DEC-TEC-002` | .NET/EF ve ortak test altyapısı bağlayıcı başlangıçtır; sürümler yayın başlangıcında doğrulanır. |
| `DEC-TEC-003` | SQL Server yalnız birincil PoC adayıdır; PostgreSQL karşılaştırmalı alternatif olarak kalır. |
| `DEC-TEC-005` | Global arama türetilmiş ve yetkili olmalıdır; Search davranışının sahibi ERP-000I'dır. |
| `DEC-TEC-007` | RPO/RTO değerleri geçicidir; production öncesi işletme kararı ve restore tatbikatı gerekir. |
| `DEC-TEC-008` | Ortamlar ayrıdır; migration/rollback manifesti ve onaylı production yayını zorunludur. |
| `DEC-TEC-009` | Yavaş sorgu, backup ve güvenlik dahil gözlemlenebilirlik platformla birlikte kurulmalıdır. |
| `DEC-TST-001/002` | P0/P1 açık kalamaz; yetki, concurrency, performans ve restore yalnız gerçek kanıtla kabul edilir. |
| `OPEN-014` | Donanım, OS, disk, ağ, UPS, yedek ve mevcut altyapı envanteri açık karardır. |
| `OPEN-015` | Üç yıllık hacim, eşzamanlı kullanıcı ve yoğun saat iş yükü açık karardır. |
| `OPEN-017` | Kesin RPO/RTO ve felaket topolojisi açık karardır. |

Master rapor PostgreSQL'i başlangıç önerisi, SQL Server'ı mevcut lisans/altyapı varsa
karşılaştırma PoC'si olarak anlatır. Teknik mimari ve karar sicili ise SQL Server'ı
birincil PoC adayı yapar. Bunlar ancak “SQL Server production için nihai seçildi” diye
yorumlanmadığında tutarlıdır. Fiziksel ADR-003 bu geçici adaylığın hipotezini, karşılaştırma
ölçütlerini, kanıtını ve karar sahibini netleştirmelidir.

## 3. Mevcut SQL Server gerçeği ve kanıt sınırı

### 3.1 Mevcut olan

İncelenen `main` snapshot'ında şu altyapı fiziksel olarak vardır:

- SQL Server tabanlı EF Core context yapılandırmaları;
- `organization`, `identity`, `audit` ve `idempotency` modül şemaları;
- her context için kendi migration dosyaları ve kendi şemasındaki
  `__EFMigrationsHistory` tablosu;
- Organization için `rowversion`, tenant write guard, composite tenant foreign key ve
  benzersiz indeksler;
- Audit için append-only SQL koruması ve keyset sorgu temeli;
- Idempotency için kalıcı sonuç, unique key ve concurrency koruması;
- GitHub Actions'ta modül bazlı idempotent SQL migration script artefaktları;
- gerçek SQL Server service container üzerinde migration/seed, CompanyId izolasyonu,
  composite FK, HTTP `404/403`, audit atomikliği ve idempotency/concurrency kabulü;
- yerel geliştirme için SQL Server/API/web container bileşimi.

Bu gerçekler ERP-000A…E teslim raporları, `.github/workflows/ci.yml`, migration klasörleri
ve `SqlServerRelationalAcceptanceTests` tarafından desteklenir.

### 3.2 Henüz kanıtlanmamış olan

Mevcut relational testin yeşil olması aşağıdakileri kanıtlamaz:

- SQL Server'ın production sağlayıcısı, edition'ı veya topolojisi;
- onaylı Türkçe collation/full-text dili, word breaker veya normalizasyon semantiği;
- `OPEN-015` hacminde exact/full-text, count, keyset veya write p95 hedefleri;
- tüm DbContext migration'larının production sıra, lock, downtime ve failure davranışı;
- büyük audit tablosunda append-only/hash/keyset performansı ve arşiv etkisi;
- deadlock, uzun transaction, bağlantı havuzu baskısı veya failover davranışı;
- gerçek DB backup/restore, point-in-time/eşdeğer recovery ve checksum mutabakatı;
- veritabanı ile nesne deposunun birlikte geri yüklenmesi;
- PostgreSQL karşılaştırmasının sonucu veya toplam sahip olma maliyeti.

Bu nedenle mevcut altyapı “başlangıç kanıtı”, ERP-000M ise “karar ve eksik PoC kanıtı”dır.

## 4. Amaç, kapsam ve kapsam dışı

### 4.1 Amaç

ERP-000M'nin amacı:

1. SQL Server adayının Göymen ERP'nin kritik ilişkisel ihtiyaçlarını tekrarlanabilir
   kanıtlarla ölçmek;
2. modül başına DbContext/schema/migration sahipliğini ve güvenli uygulama sırasını
   kilitlemek;
3. runtime, migration ve restore yetkilerini asgari yetkiyle ayırmak;
4. CompanyId/RBAC/ABAC kısıtlarının sorgu, FTS, count, hata ve plan kanıtında sızmamasını
   doğrulamak;
5. Türkçe arama primitive'lerini provider seçimini sonuçtan önce kilitlemeden ölçmek;
6. rowversion/race/deadlock, audit/keyset ve migration failure davranışını ölçmek;
7. DB-only restore ve checksum kanıtını ERP-000N/G'nin birleşik restore çalışmasına girdi
   olarak üretmek;
8. ADR-003 veritabanı kararına ve ADR-010 yayın/migration sözleşmesine incelenebilir kanıt
   sağlamak.

### 4.2 Kapsam dışı

Bu readiness veya sonraki ERP-000M işi şunları sahiplenmez:

- SQL Server, PostgreSQL veya başka bir provider'ı nihai production seçimi yapmak;
- production edition, lisans, OS, disk, HA/DR, failover veya hosting topolojisi seçmek;
- database/server/table collation ya da full-text language seçmek;
- production isolation level, connection-pool, timeout, retry, batch veya index ayarı
  kilitlemek;
- p95, RPO, RTO, veri hacmi, eşzamanlı kullanıcı veya retention için sabit sayı üretmek;
- ERP-000I Global Search API, projection, ranking, suggestion veya cursor sözleşmesini
  kodlamak;
- ERP-000F Outbox/Inbox, ERP-000G Documents veya herhangi bir domain modülünü kodlamak;
- DB + object-store birleşik backup/restore runbook'unu veya `AT-070`i tamamlamak;
- mevcut migration'ları bu docs-only dalda değiştirmek ya da yeniden üretmek;
- CI, container, package, secret veya connection string değiştirmek;
- mevcut `OPEN-*`, ADR veya `AT-*` durumlarını sessizce kapatmak.

## 5. Bağımlılıklar ve sahiplik yönü

| Yetenek | Sahip | ERP-000M sınırı |
|---|---|---|
| Repository/solution/CI temeli | ERP-000A | Tekrarlanabilir test ve artefakt temelini tüketir. |
| CompanyContext ve tenant constraints | ERP-000B | Gerçek SQL izolasyon kanıtını genişletir; tenant modelini yeniden tasarlamaz. |
| RBAC/ABAC ve scope | ERP-000C | Permission-safe sorgu testini tüketir; yetki üretmez. |
| Append-only audit/correlation | ERP-000D | Büyük audit ve keyset yükünü ölçer; audit sözleşmesini değiştirmez. |
| Idempotency/rowversion/concurrency | ERP-000E | Race/deadlock/failure kanıtını genişletir; E davranışını yeniden uygulamaz. |
| Gözlemlenebilirlik | ERP-000L, sonraki tüketici/entegrasyon | M, tekrarlanabilir PoC ölçüm harness'i ve sinyalleri üretir; L bunları production telemetry/alert kanalına bağlar. M ile L arasında hard dependency döngüsü kurulmaz. |
| Global Search | ERP-000I | M yalnız DB primitive kanıtı üretir; I güvenli arama davranışını sahiplenir. |
| Documents ve birleşik restore | ERP-000G + ERP-000N | M DB-only restore kanıtı üretir; DB+object tutarlılığı G/N'dedir. |

Manifest bağımlılıkları fail-closed olarak A/B/C/D/E'dir. ERP-000M, ERP-000I veya
ERP-000N'ye bağlanmaz; aksi halde M'nin üreteceği temel DB kanıtını tüketen görevlerle
döngü oluşur. ERP-000I ve ERP-000N/G kendi kabul kapsamlarında M kanıtına referans
vermelidir. ERP-000L de M'nin ürettiği slow-query/deadlock/migration/restore sinyallerini
production telemetry'ye bağlayan sonraki tüketicidir; M'nin ölçüm harness'i için hard
dependency değildir.

## 6. DbContext, schema ve migration sahipliği

### 6.1 Sahiplik değişmezi

Her kalıcı modül:

- kendi DbContext'inin, schema'sının, entity mapping'inin ve migration klasörünün sahibidir;
- kendi şemasındaki ayrı `__EFMigrationsHistory` tablosunu kullanır;
- başka modülün migration dosyasını veya tablosunu doğrudan değiştirmez;
- cross-module veri etkisini Application contract veya kabul edilmiş event/outbox sınırıyla
  yürütür;
- ortak DB nesnesi gerekiyorsa sahip, tüketici, upgrade sırası ve rollback davranışı ADR'de
  açık olmadan nesneyi oluşturmaz.

Migration artefakt manifesti en az modül/context kimliği, önceki/hedef schema sürümü,
uygulama sırası, script hash'i, uygulama binary uyumluluğu, beklenen nesne değişikliği,
rollback veya forward-fix yolu ve kanıt referansını taşımalıdır.

### 6.2 Deterministik sıra

Fiziksel ADR-010 aşağıdakileri açıkça seçmelidir:

- yeni ve mevcut veritabanı için topolojik context sırası;
- aynı context içindeki migration sırası ve tekrar çalıştırma davranışı;
- uygulama startup'ı, ayrı deployment runner'ı veya başka yürütme yolunun hangi ortamda
  izinli olduğu;
- bir context başarılı, sonraki context başarısız olduğunda güvenli durma ve yeniden
  başlama noktası;
- uygulama binary'sinin kısmi schema sürümünde başlayıp başlamayacağı;
- migration history ile gerçek DB nesnesi drift'inin nasıl tespit edileceği;
- aynı migration'ın iki runner tarafından eşzamanlı başlatılmasının nasıl önleneceği;
- publish artefaktı ile migration manifestinin aynı kaynak commit'ten geldiğinin kanıtı.

Bu belge production'da startup migration veya ayrı runner seçmez. Seçim fiziksel ADR-010
ve dağıtım güvenlik review'una aittir.

## 7. Kimlikler ve asgari yetki

En az üç güven sınırı ayrı değerlendirilmelidir:

1. **Runtime kimliği:** yalnız uygulamanın izinli DML ve zorunlu stored object erişimi;
   schema/permission yönetimi veya backup/restore yetkisi yoktur.
2. **Migration kimliği:** yalnız onaylı artefaktı belirlenen schema'larda uygulayacak süreli
   ve auditli DDL yetkisi; normal kullanıcı trafiği yürütmez.
3. **Backup/restore/operasyon kimliği:** yalnız kabul edilmiş runbook ve izole hedefte
   kullanılan ayrı, kontrollü, auditli yetki; runtime secret'ıyla aynı değildir.

Kesin DB rol adları, secret sistemi veya kimlik sağlayıcısı bu belgede seçilmez. Ancak:

- secret repository, migration script, log veya CI çıktısına yazılamaz;
- production kimliği geliştirici/local `sa` veya eşdeğer geniş yetkili hesap olamaz;
- bootstrap/system write yolu normal uygulama yazmalarına genel bypass veremez;
- restore edilen DB önce izole ağ/hedefte açılır ve normal uygulama trafiğine otomatik
  bağlanmaz;
- yetki grant/revoke ve migration uygulaması correlation ile kanıtlanabilir olmalıdır.

## 8. Migration değişiklik güvenliği

### 8.1 Expand-contract

Canlı veriye dokunan değişiklikler için varsayılan yaklaşım:

1. geriye uyumlu additive/expand değişikliği;
2. gerekiyorsa ayrı, gözlemlenebilir ve yeniden başlatılabilir backfill;
3. eski ve yeni binary/schema uyumluluğu için doğrulama;
4. trafik ve veri doğrulandıktan sonra contract/cleanup;
5. destructive contract adımı için ayrı onay ve geri dönüş/forward-fix kanıtı.

Nullable/not-null, default, index build, büyük tablo rewrite, computed alan, trigger,
constraint validation veya veri dönüşümü “küçük migration” varsayılmaz. Lock, log growth,
blocking, disk ve süre etkisi gerçek ortamı temsil eden PoC ile ölçülür; edition özelliğine
dayanan online davranış önceden varsayılmaz.

### 8.2 Failure, rollback ve forward-fix

- Her migration temiz veritabanında ve önceki desteklenen schema sürümünden yükseltmede
  denenir.
- Aynı artefaktın güvenli yeniden çalıştırma/tespit davranışı kanıtlanır.
- Kontrollü failure injection; DDL, backfill, constraint ve index adımlarının her birinde
  kısmi başarıyı görünür kılar.
- Data-loss yaratabilecek `Down` veya tablo/kolon düşürme otomatik rollback sayılmaz.
- Append-only audit, event/idempotency sonucu ve hukuki kayıt şemaları binary rollback
  nedeniyle silinmez; uyumsuzlukta açık forward-fix planı kullanılır.
- Failed migration sonrasında uygulama sessizce eski/yeni karışık schema'da yazma açmaz.
- Migration başarısızlığı business başarı olayı üretmez; deployment/security kanıtı ve
  telemetry doğru kanala gider.

Hangi değişikliğin transactional DDL ile atomik olacağı provider davranışı doğrulanmadan
varsayılmaz.

## 9. Tenant, RBAC/ABAC ve sorgu güvenliği

SQL Server'ın bir sorguyu hızlı çalıştırması, sorgunun yetkili olduğu anlamına gelmez.
CompanyId ve permission/ABAC sınırı:

- request-scoped, server-side doğrulanmış kullanıcı ve CompanyContext'ten gelir;
- model, istemci veya sorgu metnindeki CompanyId ile genişletilemez;
- FTS, exact arama, count, facet, suggestion, snippet, hata ve timing üretilmeden önce
  uygulanır;
- join/projection/index tasarımında yabancı şirket satırını ara sonuç veya toplam olarak
  taşımaz;
- scope temsil edilemiyorsa Company-wide varsayılmaz; fail-closed olur;
- kaynak kayıt açılışında sahip modül backend'i güncel yetkiyi yeniden doğrular;
- query plan, log, deadlock graph, metric label ve test artefaktında hassas kayıt değeri
  sızdırmaz;
- cross-company foreign key, unique key ve write guard korumalarını migration sırasında
  gevşetmez.

PoC'de aynı arama terimini taşıyan A ve B şirketi kayıtları, dar Site/Location/WorkCenter
scope'ları, doğrudan bilinen yabancı ID ve yetki daraltma senaryoları bulunmalıdır. Sonuç,
count, snippet, süre farkı ve hata hiçbir yabancı varlık bilgisini açmamalıdır.

Bu maddeler ERP-000I Global Search sözleşmesinin yerine geçmez. M yalnız ilişkisel
primitive ve güvenli ölçüm altyapısını doğrular.

## 10. Türkçe exact/full-text primitive PoC

Fiziksel ADR-003 ve ERP-000I karar paketine girdi olacak PoC:

- orijinal Türkçe metni değişmeden korur;
- exact identifier/code araması ile doğal dil/full-text aramayı ayrı workload olarak
  ölçer;
- `I`, `İ`, `ı`, `i`, aksan, büyük/küçük harf, boşluk, noktalama, tire, birleşik kelime,
  sayı/kod ve yaygın yazım varyantlarından oluşan onaylı golden corpus kullanır;
- aday collation, full-text language/word-breaker ve normalizasyon davranışını gerçek
  sorgu sonucu ile kaydeder;
- desteklenmeyen veya ortamlar arasında farklı capability'yi sessiz fallback ile geçmez;
- index/catalog build, incremental update, rebuild, failure ve veri değişikliği sonrası
  freshness kanıtı üretir;
- tenant/permission filtresi uygulanmış sorgu planı ve count/snippet sızıntı testini taşır;
- warm/cold çalışma ve stabil tekrar ölçümlerini ayırır;
- sonucu, kullanılan engine sürümü/capability/collation ayarıyla birlikte kaydeder.

Bu belge belirli bir collation, language ID, tokenizer, full-text catalog tasarımı,
normalizasyon algoritması veya index tipi seçmez. Global ranking, projection ownership,
cursor token ve arama API'si ERP-000I'da kararlaştırılır.

## 11. Concurrency, race, deadlock ve kalıcı sonuç

PoC en az şu davranış ailelerini gerçek SQL üzerinde gözlemlemelidir:

- aynı kayıt sürümünde paralel farklı komutlardan yalnız geçerli olanın etkisi;
- stale `rowversion`/expected version reddi ve farkın güvenli gösterimi;
- aynı idempotency kapsamı + aynı payload için güvenli replay;
- aynı key + farklı payload/actor/company için kesin conflict ve bilgi sızıntısız hata;
- transaction rollback'inde business, audit ve kalıcı command result atomikliği;
- kontrollü lock-order çatışması/deadlock ve yeniden deneme güvenliği;
- timeout veya bağlantı kopmasında sonucun “bilinmiyor” olduğu durumda ikinci iş etkisinin
  oluşmaması;
- retry'nin yalnız idempotent ve policy tarafından izinli sınırda çalışması;
- long-running migration/backfill ile normal write yarışında fail-closed davranış;
- bütün bu durumlarda correlation, hata sınıfı ve ölçüm kanıtı.

Bu belge isolation level, deadlock retry sayısı, backoff, timeout veya transaction süresi
seçmez. Seçenekler ölçülür, iş semantiği ve gerçek kanıtla ADR'ye taşınır.

## 12. Audit ve keyset yükü

Büyük audit/event tabloları için PoC:

- append-only SQL ve EF korumasının yük altında devam ettiğini;
- canonical integrity hash'in persisted satırdan yeniden üretilebildiğini;
- Company-wide audit permission zorunluluğunu ve tenant filtresini;
- seçilen sıralama anahtarında stabil, tekrar/atlama yapmayan keyset davranışını;
- derin `OFFSET` kullanmadan ardışık sayfa kanıtını;
- insert yükü ile eşzamanlı okuma, retention/partition varsayımı olmadan büyüyen hacmi;
- index bakımının lock, log, disk ve yazma gecikmesi etkisini;
- query plan/regresyon ve yavaş sorgu tanısını

ölçmelidir.

Audit retention/partition/WORM politikası burada uydurulmaz. Açık retention kararları
parametreli veri üreticisinde senaryo girdisi olarak tutulur; production imha işi açılmaz.

## 13. Performans, kapasite ve gözlemlenebilirlik

`SQL_SERVER_POC.md` ve Master rapordaki p95 değerleri başlangıç önerisidir. `OPEN-015`
kapanmadan production SLO, merge eşiği veya magic number değildir.

Parametreli workload paketi en az:

- şirket, kullanıcı, cari, ürün, sipariş, stok hareketi, audit, belge metadata ve OCR hacmi;
- eşzamanlı read/write, yoğun saat ve uzun sorgu dağılımı;
- exact/full-text/count/keyset sorguları;
- rowversion race, deadlock, idempotency replay ve append-only audit yazması;
- migration/index/backfill sırasında uygulama trafiği;
- büyük export kuyruğu için yalnız DB sorgu/okuma primitive'i;
- DB-only backup/restore ve checksum

girdilerini ayrı tanımlar. Gerçek iş yükü sahibi ve değerleri `OPEN-015` kapanışında
onaylanır; bu dal sayı üretmez.

Her ölçüm kanıtı en az şunları taşır:

- source commit ve migration artefakt hash'leri;
- engine/provider sürümü, compatibility/capability ve kullanılan ortam;
- edition, OS, CPU, RAM, disk ve ağ bilgisi; bunlar seçim değil kanıt metadata'sıdır;
- DB/collation/full-text ayarları ve değiştirilmiş varsayımlar;
- veri generator sürümü, seed/hash ve satır dağılımı;
- workload adı, warm/cold koşulu, eşzamanlılık ve ölçüm penceresi;
- latency dağılımı, throughput, error/timeout/deadlock/lock/log/disk sonuçları;
- query plan/regresyon ve yavaş sorgu referansı;
- test temizleme ve tekrar üretme adımları.

M'nin PoC ölçümü, sürümlü ve tekrarlanabilir test harness'iyle ERP-000L tamamlanmadan
üretilebilir; bu sonuç production telemetry/alert kabulü sayılmaz. ERP-000L daha sonra
aynı sinyalleri işletim kanalına bağlar. Metric label'ları ham CompanyId, kullanıcı, kayıt,
belge veya müşteri bilgisi taşımaz; tenant-scoped güvenli aggregate ve correlation
kullanılır.

## 14. DB-only backup, restore ve checksum sınırı

ERP-000M'nin restore kanıtı yalnız veritabanı primitive'idir:

1. kabul edilmiş test snapshot/backup'ı oluşturulur;
2. restore, canlı/shared hedefi ezmeyen benzersiz ve izole hedefe yapılır;
3. migration history, schema/constraint/index varlığı ve row counts doğrulanır;
4. örnek kritik business/audit/idempotency satırları için deterministic checksum/hash
   mutabakatı yapılır;
5. CompanyId izolasyonu, composite FK, RBAC/ABAC test fixture'ı ve append-only audit
   koruması restore sonrasında yeniden çalıştırılır;
6. beklenen restore noktası ve gerçekleşen veri zamanı kaydedilir;
7. başarısız/eksik restore production'a bağlanmadan görünür biçimde reddedilir;
8. kanıt ve kullanılan operasyon kimliği audit/correlation ile ilişkilendirilir.

Point-in-time veya eşdeğer restore yöntemi, backup türü, şifreleme anahtarı, storage,
schedule ve topoloji bu belgede seçilmez. `OPEN-014` ve `OPEN-017` kapandıktan sonra
fiziksel ADR/runbook içinde kararlaştırılır.

ERP-000M şunları **kanıtlamaz**:

- DB ile object-store'un aynı iş noktasında birlikte geri yüklendiğini;
- blob version/hash/link metadata'nın fiziksel nesnelerle mutabık olduğunu;
- legal hold/retention ve dosya erişim politikasını;
- production `AT-070` veya felaket tatbikatının tamamlandığını.

Bu birleşik kabul ERP-000N'nin runbook'u ve ERP-000G'nin belge/blob sözleşmesiyle birlikte
yürütülür. ERP-000M yalnız SQL tarafı girdisini verir.

## 15. Provider, edition ve ortam ayrımı

Mevcut CI/dev SQL Server image'ı ve local geliştirme ortamı production kararı değildir.
Fiziksel ADR-003:

- SQL Server adayının ve karşılaştırmalı alternatifin test matrisini;
- mevcut lisans/altyapı envanteri ile ölçülen capability'leri;
- edition/OS/topolojiye bağlı özelliklerin hangi ortamda bulunduğunu;
- dev/test/staging/production parity farklarını;
- taşınabilir EF model ile provider-specific constraint/index/full-text davranışının
  sınırını;
- maliyet, operasyon, restore, güvenlik ve yetkinlik değerlendirmesini;
- kabul, ret veya ek PoC karar sahibini

tanımlamalıdır.

PoC sonucu çıkmadan production provider/edition seçilmez. Bir capability yalnız belirli
edition veya ortamda varsa “dev'de geçti” production kabulü sayılmaz; eksik capability
fail-closed karar girdisidir.

## 16. Task-local kabul planı

Aşağıdaki senaryolar readiness planıdır; kanonik `AT-*` değildir ve geçmiş sayılmaz.
Merkezî acceptance sicili sahibi gerekli eşlemeyi ayrı review ile yapar.

### 16.1 Karar, environment ve artefakt

1. Fiziksel ADR-003; aday/alternatif, ölçüm matrisi, karar sahibi ve geri dönüş yoluyla
   mevcuttur; PoC sonucu çıkmadan nihai provider iddiası üretmez.
2. Fiziksel ADR-010; DbContext sahipliği, sıra, yürütme kimliği, failure ve
   rollback/forward-fix sözleşmesini taşır.
3. `OPEN-014/015/017` sahip, girdi, kanıt ve kapanış kararıyla çözülmeden task `ready/done`
   olmaz.
4. Aynı commit'ten gelen migration artefakt manifesti ve script hash'leri doğrulanır.
5. Environment/capability drift veya desteklenmeyen özellik sessiz fallback yapmaz.

### 16.2 Migration güvenliği

6. Her DbContext yalnız kendi schema/migration/history nesnelerini değiştirir.
7. Temiz DB ve desteklenen önceki schema'dan yükseltme aynı beklenen sonucu verir.
8. Aynı migration'ın paralel runner denemesi çift uygulama veya history drift üretmez.
9. Context sırasının ortasında failure injection, uygulamayı karışık schema'da yazmaya
   açmaz; yeniden başlama noktası ve forward-fix kanıtlıdır.
10. Runtime kimliği DDL/backup yetkisi, migration kimliği sınırsız runtime veri yetkisi,
    restore kimliği normal uygulama erişimi kazanamaz.
11. Expand/backfill/contract sırasında lock, log, disk ve traffic etkisi görünür ölçülür;
    destructive rollback veri kaybıyla “başarılı” gösterilmez.
12. Audit/idempotency geçmişi binary rollback veya forward-fix sırasında silinmez.

### 16.3 Tenant, arama primitive'i ve performans

13. A şirketi ve dar scope kullanıcısı, B verisini exact/FTS sonucu, count, snippet,
    hata, timing, plan veya metric üzerinden öğrenemez.
14. Türkçe golden corpus bütün aday primitive ayarlarında gerçek sonuç ve capability
    metadata'sıyla ölçülür; bu ölçüm collation/word-breaker seçmiş sayılmaz.
15. FTS/index build, update, rebuild ve failure sonrası freshness görünürdür; bozuk/stale
    sonuç sessizce güncel sayılmaz.
16. `OPEN-015` parametreli workload'u tekrar üretilebilir seed/hash ile çalışır; eşik
    kullanıcı kararı olmadan sabitlenmez.
17. Query plan, log, trace ve metric hassas tenant/kayıt değerini sızdırmaz.

### 16.4 Concurrency, audit ve restore

18. Rowversion yarışında yalnız geçerli write etkisi oluşur; stale writer conflict alır.
19. Aynı/farklı idempotency key yarışları ikinci business/audit etkisi üretmez.
20. Kontrollü deadlock/timeout/connection-loss deneyi belirsiz sonucu otomatik yeniden
    yazmaz; güvenli retry yalnız kalıcı idempotency ile çalışır.
21. Büyük audit yükünde append-only/hash ve tenant-safe keyset tekrar/atlama olmadan
    korunur; retention/partition değeri uydurulmaz.
22. DB-only restore izole hedefte migration history, constraints, tenant guard, audit hash,
    idempotency sonucu ve checksum mutabakatını geçer.
23. DB-only başarı, DB+object restore veya `AT-070` tamamlandı iddiası üretmez.
24. Test kanıtı source SHA, environment/capability, dataset, workload, artefakt hash'i,
    ölçüm ve failure sonucuyla yeniden üretilebilir.

## 17. Mevcut kanonik acceptance sınırı

Bu docs-only çalışma `ACCEPTANCE_MATRIX.csv` dosyasını değiştirmez ve hiçbir mevcut
senaryonun durumunu yükseltmez.

- `AT-001` tenant görünürlüğü için M ilişkisel kanıt üretir; uçtan uca sahipliği değiştirmez.
- `AT-003` mevcut audit kanıtını büyük yük/failure yönünden genişletebilir; mevcut kanıtı
  yeniden yazmaz.
- `AT-005` için rowversion/race SQL kanıtı üretebilir; UI/E2E conflict kabulünü tamamlamaz.
- `AT-002` ve `AT-069` Global Search sahibinde kalır; M yalnız SQL primitive ve ölçüm
  girdisi sağlar.
- `AT-070` ERP-000N/G birleşik DB+object restore kabulünde kalır; M tek başına kapatamaz.

Yeni acceptance kimliği gerekiyorsa yalnız merkezî sicil review'u verir. Bu belgedeki
numaralı senaryolar `AT-*` değildir.

## 18. Test ve kanıt stratejisi

### 18.1 Static/contract

- DbContext → schema → migration folder → history table ownership matrisi;
- migration artefakt manifest schema'sı ve hash doğrulaması;
- module dependency/ownership guard;
- yasak cross-module table mutation denetimi;
- connection/secret ve least-privilege policy denetimi;
- migration scriptte destructive/unsupported operation review kapısı.

### 18.2 Gerçek SQL integration

- temiz DB migration + seed + tekrar uygulama;
- supported-old-schema → current upgrade;
- tenant write guard, composite FK, permission-safe query;
- rowversion, idempotency, transaction rollback ve failure injection;
- audit append-only/hash/keyset;
- Turkish exact/full-text primitive corpus;
- migration lock/failure/parallel runner;
- izole DB-only backup/restore/checksum.

### 18.3 Load/resilience

- `OPEN-015` tabanlı parametreli sentetik workload;
- warm/cold, read/write ve migration-with-traffic koşulları;
- deadlock, timeout, connection loss, disk/log baskısı ve kontrollü yeniden başlama;
- yavaş sorgu/query-plan regression ve bounded telemetry;
- aynı seed ve environment metadata'sıyla tekrar üretim.

Gerçek şirket verisi, production secret'ı veya paylaşılan canlı DB bu PoC için varsayılan
test girdisi değildir.

## 19. Fiziksel ADR karar paketleri

### 19.1 ADR-003 — Veritabanı PoC kararı

Fiziksel ADR-003 en az:

1. adaylar ve tarafsız karşılaştırma hipotezi;
2. mevcut altyapı/lisans bilgisinin kanıt kaynağı;
3. workload, security, Türkçe search, migration, restore ve maliyet ölçütleri;
4. environment parity ve provider-specific capability sınırı;
5. test verisi/generator ve kanıt formatı;
6. karar sahibi, kabul/ret/escalation yolu;
7. geri dönüş ve veri taşınabilirliği etkisi

taşımalıdır. ADR fiziksel olduğunda bile PoC sonucu çıkmadan SQL Server nihai production
kararı sayılmaz.

### 19.2 ADR-010 — Migration ve yayın sözleşmesi

Fiziksel ADR-010 en az:

1. DbContext/schema/history sahipliği;
2. deterministic apply order ve dependency graph;
3. runtime/migration/restore kimlik ayrımı;
4. artefakt üretimi, imza/hash ve deployment approval;
5. expand/backfill/contract yaklaşımı;
6. lock/downtime ve compatibility kapısı;
7. failure injection, retry/restart ve partial-success davranışı;
8. rollback ile forward-fix seçim ölçütü;
9. audit/idempotency/veri koruma yasağı;
10. migration sonrası smoke, checksum ve rollback manifesti

taşımalıdır.

## 20. `ERP-000M-SQL-POC-CONTRACT` kapanış koşulları

Bu task-local aggregate blocker ancak aşağıdakilerin tamamı insan review'uyla
doğrulandığında kaldırılabilir:

- bütün manifest bağımlılıkları A/B/C/D/E kabul edilip `main`e alınmıştır;
- fiziksel ADR-003 ve ADR-010 uygulanabilir karar/ölçüm sözleşmesiyle review edilmiştir;
- `OPEN-014`, `OPEN-015` ve `OPEN-017` sahipli kanıtla kapanmıştır;
- DbContext/schema/migration/history ownership matrisi onaylanmıştır;
- migration sıra, kimlik, expand-contract, failure ve forward-fix sözleşmesi kilitlidir;
- tenant/RBAC/ABAC güvenlik ve Türkçe primitive corpus planı onaylanmıştır;
- rowversion/race/deadlock/audit/keyset/load planı onaylanmıştır;
- DB-only restore/checksum ile ERP-000N/G sınırı kilitlidir;
- task-local senaryolar gerekli kanonik acceptance eşlemelerine merkezî review ile
  bağlanmıştır;
- kanıt formatı, ortam metadata'sı ve yeniden üretim planı onaylanmıştır;
- provider/edition/collation/threshold kararı kanıtsız varsayılmamıştır;
- P0/P1 güvenlik, veri kaybı veya mimari bulgusu kalmamıştır.

## 21. Kodlama giriş kapısı

### Şimdi izinli

- fiziksel ADR-003 ve ADR-010 taslaklarını hazırlamak;
- `OPEN-014/015/017` için sahipli envanter ve karar paketini hazırlamak;
- gerçek veri içermeyen workload/golden corpus/evidence schema taslağı hazırlamak;
- DbContext/migration ownership ve dependency envanteri çıkarmak;
- task-local senaryoları merkezî acceptance siciliyle review etmek;
- yalnız dokümantasyon farklarını güncel `main` ile conflict-safe uzlaştırmak.

### Şimdi yasak

- provider/edition/collation/isolation/topoloji veya eşik seçmek;
- SQL/EF package, migration, constraint, index veya full-text catalog eklemek;
- CI, container, connection string, secret veya environment değiştirmek;
- production migration runner, backup veya restore scripti yazmak;
- ERP-000I Search, ERP-000N restore veya ERP-000G object davranışını sahiplenmek;
- mevcut `OPEN-*`, ADR veya `AT-*` kaydını sessizce kapatmak;
- bütün bağımlılıklar ve contract kapıları kapanmadan task'ı `ready/done` yapmak.

## 22. Shared manifest ve merge/sync kapısı

Bu dal `docs/backlog/task_manifest.json` içinde yalnız ERP-000M satırını değiştirir.
ERP-000E kabul edilmiş ve `main`e merge edilmiştir. Bu dal, güncel `main` ile
senkronizasyon sırasında başka task satırlarının durum, dependency veya blocker alanlarını
sessizce değiştirmez; gerekiyorsa ayrı truth-sync review'u açılır.

Shared manifest için kabul edilmiş readiness sırası `ERP-000M → ERP-000O → ERP-000N →
ERP-000L`, ardından bağımsız `ERP-000J` ve `ERP-000K` dallarıdır. ERP-000F/G/H/I/J
readiness PR'larının ERP-000M'den önce merge edilmesi bir önkoşul değildir. Her adım,
öncülü `main`e merge edildikten sonra güncel `main` ile yeniden senkronize edilir ve insan
review'uyla doğrulanır.

Conflict, silinen blocker, değişen dependency veya statü farkı otomatik/sezgisel çözülmez.
ERP-000L, M'nin ürettiği sinyalleri production telemetry'ye bağlayan sonraki tüketicidir;
M implementation kapısı değildir. M'nin docs-only readiness merge'i de kendi başına
implementation izni vermez.

## 23. Sonuç

ERP-000M için sonuç **readiness belgesi GO, implementation NO-GO**'dur. Mevcut SQL Server
ve EF Core altyapısı doğru bir başlangıç kanıtıdır; ancak production veritabanı kararı,
Türkçe arama primitive'i, migration güvenliği, yük ve DB-only restore ancak A/B/C/D/E
bağımlılıkları ile ADR-003, ADR-010, OPEN-014/015/017 ve
`ERP-000M-SQL-POC-CONTRACT` birlikte kapandıktan sonra uygulanabilir ve kabul edilebilir.

O zamana kadar provider, edition, collation, isolation, topoloji, threshold veya migration
davranışı tahmin edilmeyecek; ERP-000I Global Search ve ERP-000N/G birleşik restore
sahipliği aşılmayacaktır.
