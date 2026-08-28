# ADR-005 — Belge Kasası, Nesne Deposu ve Güvenli Dosya Yaşam Döngüsü

- Tarih: 2026-08-28
- Durum: Öneri
- Görev: ERP-000G
- Kodlama kapısı: KAPALI — feature spec, acceptance planı ve `OPEN-026`…`OPEN-029`
  aday kararları kabul edilmedi
- Karar sahibi: Sinan Göymen
- İlgili kararlar: `DEC-GOV-002`, `DEC-GOV-003`, `DEC-DOC-001`, `DEC-DOC-002`,
  `DEC-DOC-003`, `DEC-DOC-008`, `DEC-DOC-009`, `DEC-TEC-001`, `DEC-TEC-008`,
  `DEC-TEC-009`, `DEC-TST-002`, `OPEN-015`, `OPEN-017`, `OPEN-018`
- Aday açık kararlar: `OPEN-026`, `OPEN-027`, `OPEN-028`, `OPEN-029`
- İlgili ADR'ler: `ADR-002`, `ADR-004`, `ADR-009`, `ADR-011`; async dış etki
  seçilirse `ADR-006`

## Bağlam

Belge Kasası; fiziksel dosya içeriği, mantıksal belge, belge sürümü ve ERP kayıt
bağlantısını birbirinden ayırmalıdır. Bir dosyanın fiziksel olarak tekilleştirilmesi,
şirketlerin belge kimliğini, metadata'sını veya erişim bağlamını birleştirmez. Her iş
modülü dosyayı kendi tablosuna kopyalamak yerine Belge Kasası'nın güvenli referansını
taşır.

SQL Server metadata transaction'ı ile S3 uyumlu nesne deposu aynı ACID transaction'a
katılmaz. Doğrudan son anahtara upload edip sonra DB yazmak veya önce DB'de kullanılabilir
belge oluşturup sonra dosyayı yüklemek; orphan object, metadata'sı olup içeriği olmayan
belge ve yarım başarılı retry pencereleri üretir. Aynı zamanda hash tabanlı fiziksel
dedup, başka şirketin dosya varlığını açığa çıkaran bir existence oracle'a dönüşmemelidir.

Dosya extension'ı veya istemci `Content-Type` beyanı güven kaynağı değildir. Dosya temiz
olduğu doğrulanmadan preview, download, OCR/AI, başka kayda link veya dışa aktarım için
erişilebilir hale gelemez. Kesin boyut, tür, arşiv, scanner, imzalı erişim ve pilot belge
politikaları mevcut kanonik kaynaklarda seçilmemiştir; bunlar tahmin edilerek kodlanamaz.

## Seçenekler

### A. Dosyayı SQL Server içinde saklamak

DB transaction'ını basitleştirir; ancak büyük dosya yükü, backup/restore hacmi ve bağımsız
nesne yaşam döngüsü hedefiyle uyumlu değildir. Kanonik S3 uyumlu nesne deposu yönüne aykırı
olduğu için reddedildi.

### B. İstemci dosyasını doğrudan son object key'e yazmak

Basittir; fakat DB commit, malware scan, tenant authorization ve object görünürlüğü
arasında güvenli yayın noktası oluşturmaz. Hash veya kullanıcı dosya adı object key olursa
tenant oracle ve tahmin edilebilir erişim riski de doğurur. Reddedildi.

### C. Özel staging/quarantine + doğrulama + kontrollü finalize

Dosyayı erişime kapalı, tahmin edilemez teknik kimlikle staging/quarantine alanına alır;
byte/hash/MIME/malware kontrollerinden ve DB metadata commit'inden sonra yalnız yetkili
Belge Kasası sözleşmesi üzerinden kullanılabilir yapar. Crash ve retry durumları kalıcı
durumlarla reconcile edilir. Önerilen yöndür; kesin state, adapter ve operasyon
politikaları açık kararlar kapanınca kilitlenecektir.

## Önerilen karar

### 1. Sahiplik ve modül sınırı

- `Platform.Documents` önerilen modül sınırıdır. Blob metadata'sı, mantıksal belge, belge
  sürümü, kayıt bağlantısı, upload session ve güvenlik tarama sonucu bu sınırın
  sorumluluğundadır.
- Başka modüller Documents tablolarına veya bucket/object key'lerine doğrudan erişmez.
  Yalnız sürümlü Application contract/command/query üzerinden güvenli belge referansı
  oluşturur veya çözer.
- Kayıt bağlantısı serbest `EntityType + EntityId` metniyle yetki atlamaz. Bağlanabilir
  kaynak türü, sahip modül Application contract'ı, CompanyId ve kaynak yetkisiyle kayıtlı
  bir contract olmalıdır. İlk gerçek link/producer/consumer sözleşmesi `OPEN-029`
  kapanmadan seçilmiş sayılmaz.
- Yeni cross-module Infrastructure project reference bu ADR ile otomatik izinli değildir.
  Gerekirse exact edge, gerekçe, architecture test ve
  `MODULE_DEPENDENCY_ALLOWLIST.json` aynı review değişikliğinde eklenir.

### 2. Fiziksel blob ve tenant-scoped belge ayrımı

- Fiziksel object/blob, business authorization nesnesi değildir. Kullanıcı veya başka
  modül blob hash'i, object key'i veya storage URI'siyle doğrudan erişim kazanamaz.
- Mantıksal belge, belge sürümü, kayıt bağlantısı, upload isteği ve erişim/audit kanıtı
  zorunlu `CompanyId` taşır. Global fiziksel dedup kullanılırsa şirket kapsamlı referans/
  kullanım bağı ayrıca tutulur; tek bir şirket kaydının silinmesi ortak blob'u silemez.
- Dedup lookup ve upload response; başka şirketin aynı hash'e sahip dosyası, belge adı,
  metadata'sı, owner'ı, link sayısı veya varlığı hakkında doğrudan ya da zamanlama/ID/
  durum farkıyla bilgi vermez. Kullanıcıya yalnız halihazırda görmeye yetkili olduğu
  mantıksal belgeler gösterilebilir.
- Fiziksel dedup scope'u, content hash algoritması/sürümü, encryption/key scope'u, object
  key türetimi, shared reference ve şirket export davranışı `OPEN-026` ile kapanır. S3
  ETag'i content hash veya bütünlük kanıtı sayılmaz.
- `OPEN-026` açıkken production cross-company physical dedup kapalıdır. Aynı byte farklı
  şirketlerde ayrı tenant-scoped physical object olarak tutulabilir; bu geçici davranış
  ilerideki onaylı dedup'a veya logical kimliklere dair hak/varlık bilgisi üretmez.
- Aynı hash altında length, algorithm/version veya doğrulanmış byte bütünlüğü uyuşmazlığı
  sessiz dedup değildir; fail-closed quarantine/integrity incident olur.

### 3. İki ayrı yaşam döngüsü

Belge iş yaşam döngüsü ile dosya güvenlik/erişilebilirlik yaşam döngüsü tek status alanına
sıkıştırılmaz.

- Business/version ekseni mevcut kanonik yönü korur: yüklenen sürüm sınıflandırılır,
  kontrolden/onaydan geçebilir ve yeni sürümle geçersiz hale gelebilir; eski sürümün
  içeriği sessizce üzerine yazılmaz.
- Security ekseni en az staging/quarantine, doğrulama/tarama, temiz ve reddedilmiş/tarama
  hatası ayrımını taşır. Exact state isimleri ve geçiş yetkileri `OPEN-027` ile kapanır.
- Security sonucu `Clean/Available` eşdeğerine ulaşmadan dosya business kaydına aktif link,
  preview/download, OCR/AI, search indexing veya dışa aktarım kaynağı olamaz.
- Scanner timeout/unavailable, MIME/extension uyuşmazlığı, bozuk dosya, şifreli veya
  desteklenmeyen arşiv ve kaynak limiti aşımı başarılı/temiz sayılmaz. Fail-closed sonuç ve
  güvenli kullanıcı mesajı üretir; dosya normal bucket/path içinde görünür olmaz.
- Malware signature/engine sürümü ve scan zamanı kalıcı kanıttır. Politika gerektirirse
  yeniden tarama eski sonucu yerinde değiştirmez; yeni operasyon kanıtı oluşturur.

### 4. Upload, hash ve DB–object consistency sınırı

- Her upload/finalize command'ı CompanyId, authorization, expected version, idempotency
  key, correlation/causation ve audit bağlamı taşır.
- Object key kullanıcı dosya adı veya doğrudan hash değildir; server-controlled,
  tahmin edilemez teknik kimliktir. Kullanıcı görünen Unicode dosya adı metadata'dır.
- Upload önce erişime kapalı staging/quarantine alanına yapılır. Doğrudan istemci upload
  kullanılacaksa dar upload capability yalnız tek object, boyut ve kısa süre için verilir;
  completion beyanı güvenilmez, server-side object metadata/byte/hash doğrulaması gerekir.
- Hash güvenilir byte stream üzerinden, kayıtlı algoritma/sürümle hesaplanır. İstemci hash'i
  yalnız ipucu olabilir; dedup veya güvenlik kararı değildir.
- SQL transaction ile object-store çağrısı atomikmiş gibi gösterilmez. En az şu crash
  pencereleri kalıcı ve idempotent reconcile edilebilir olmalıdır:
  - object başarılı, upload/metadata DB commit başarısız;
  - DB upload session başarılı, object hiç oluşmamış veya eksik;
  - scan başarılı, finalize DB commit başarısız;
  - DB metadata var, object missing/corrupt/version mismatch;
  - retry aynı idempotency key ile yeniden geldiğinde ikinci logical version/link oluşması.
- Orphan object kullanıcıya açılmaz. Reconciler onu kanıtlı upload session'a bağlar veya
  retention/legal-hold politikasına göre daha sonra temizlenmek üzere işaretler. Otomatik
  purge `OPEN-018` ve `OPEN-026` kapanmadan kapalıdır.
- Scan/finalize/reconcile veya dış notification async worker/outbox gerektirirse ERP-000F
  kabul edilmeden ilgili kod yolu açılmaz. F kullanılmayacaksa eşdeğer crash-safe staged
  orchestration ve retry kanıtı bu ADR'nin kabulünde açıkça seçilmelidir. Kesin DB–object
  commit/reconciliation akışı ve synchronous/async ayrımı `OPEN-029` kapanışında pilot
  contract ile birlikte kilitlenir.

### 5. MIME, malware ve kaynak sınırları

- Extension, istemci MIME beyanı ve server-side content sniff sonucu ayrı kanıttır.
  Allowlist gerçek içerik türü üzerinden fail-closed uygulanır.
- İzinli MIME/extension kombinasyonları, dosya ve toplam request boyutu, page/entry sayısı,
  sıkıştırma oranı/derinliği, password-protected içerik, macro/active-content davranışı,
  scanner engine ve timeout/retry politikası `OPEN-027` ile kapanır. Magic number olarak
  koda gömülmez.
- Archive bomb, path traversal dosya adı, nested archive ve parser resource exhaustion
  acceptance/security testine girer. Extract edilen ad hiçbir zaman object key veya server
  filesystem path'i olmaz.
- Aktarımda ve depoda şifreleme zorunludur. Credential/key source kodunda, metadata'da,
  logda veya imzalı URL query'si dışında yeniden kullanılabilir secret olarak tutulmaz.

### 6. Yetkili erişim, imzalı URL ve dışa aktarım

- Bucket/object private'dır; kalıcı public URL yoktur. Her preview/download/export isteği
  güncel CompanyContext, permission ve kaynak bağlamını server tarafında yeniden doğrular.
- İmzalı URL veya proxy capability; tek object/version, izinli operation, kısa TTL ve
  server-controlled response headers ile sınırlıdır. Kullanıcı dosya adı güvenli
  `Content-Disposition` metadata'sıdır; header injection veya object key olamaz.
- URL issuance ile redeem arasındaki yetki daralması, belge invalidation'ı, tenant
  değişikliği ve acil revocation davranışı; TTL, range/preview, cache/CDN ve hassas export
  görev ayrılığı `OPEN-028` ile kapanır.
- Hassas download/export ve manuel quarantine/re-scan/release işlemleri ayrı default-deny
  izin, gerekçe ve immutable audit ister. Scanner sonucu normal kullanıcı tarafından
  temiz olarak değiştirilemez.
- Hash veya object key bilen A şirketi kullanıcısı B şirketinin URL'sini, metadata'sını,
  content length'ini, hata ayrıntısını ya da varlık bilgisini alamaz.

### 7. Version, link, audit ve idempotency

- DocumentVersion içeriği ve blob referansı oluşturulduktan sonra immutable'dır. Revizyon
  yeni version oluşturur; optimistic concurrency aynı document için paralel version/
  active-version yarışını tek kazananla kapatır.
- Aynı idempotency key ve canonical upload/finalize request ikinci logical document,
  version, link veya audit üretmez. Aynı key farklı payload/target ile fail-closed reddedilir.
- Upload başlatma/tamamlama, scan sonucu, sınıflandırma, version oluşturma/geçersiz kılma,
  link ekleme/kaldırma, signed access issuance, hassas export ve privileged retention
  işlemleri contract'ın riskine göre audit üretir. Teknik scan attempt log/metric olabilir;
  business audit yerine geçmez.
- Link kaldırmak business geçmişini veya immutable audit'i silmez. İçerik imhası yalnız
  retention/legal hold ve bütün şirket referansları değerlendirilerek ayrı yetkili akıştır.

### 8. Bağımlılıklar ve coding gate

ERP-000G yalnız repository iskeletine bağlı bir upload özelliği değildir. Implementation
öncesi task dependency en az aşağıdaki kabul edilmiş platform sınırlarını açıkça taşımalıdır:

- ERP-000B: CompanyId ve şirket izolasyonu;
- ERP-000C: authorization/RBAC+ABAC;
- ERP-000D: immutable audit/correlation;
- ERP-000E: critical command idempotency/concurrency;
- async scan/finalize/reconcile/outbox seçilirse ERP-000F.

`OPEN-029`; ilk gerçek document type, zorunlu metadata, producer/owner module, link target,
business/security lifecycle, version/emission kuralları, veri sınıfı, yetki sahibi ve
DB–object-store commit/reconciliation sınırını seçmeden production pilot contract
oluşturmaz. Async yol seçilirse ERP-000F bağımlılığı da burada açılır. Generic platform
sözleşmesi bunu bir satın alma, finans veya OCR belgesi olarak tahmin etmez.

## Retention, backup ve şirket ayrıştırma

- `OPEN-018` kapanana kadar physical/logical automatic delete veya purge job'ı yoktur.
  Geçersiz/pasif version içeriği silinmez; legal hold fail-closed davranır.
- DB backup ile object-store version/checksum manifesti birlikte restore edilmeden Belge
  Kasası production-ready sayılmaz. Metadata-object eksikleri ve fazla/orphan object'ler
  mutabakat raporunda görünürdür.
- Şirket dışa aktarımı; yalnız o şirketin yetkili logical document/version/link metadata'sı
  ile referans verdiği object byte'larını checksum manifestiyle taşır. Ortak blob'un başka
  şirket metadata veya bağlantıları pakete sızmaz.
- Encryption key rotation, object versioning, backup/restore ve ortak blob export ayrıntısı
  `OPEN-026`, `OPEN-017` ve ERP-000N kanıtıyla kapanır.

## Migration ve geri alma/yeniden değerlendirme

- Önerilen SQL sahipliği `documents` schema ve ayrı migrations history'dir. Kesin entity,
  index, constraint, trigger ve object-store adapter'ı bu ADR kabulünden sonra seçilir.
- CompanyId/link scope, document/version optimistic concurrency, idempotency ve blob
  identity constraint'leri gerçek SQL Server'da kanıtlanır.
- Production metadata veya object oluştuğunda rollback tablo/bucket drop değildir. Şema ve
  object kanıtı korunur; binary rollback uyumsuzsa forward-fix migration uygulanır.
- Migration script'i temiz ve mevcut DB'de idempotent çalışmalı; object-store provision,
  bucket policy, encryption ve versioning ayrı doğrulanabilir manifest taşır.

## Gözlemlenebilirlik

Payload, original document byte'ı, signed URL, credential, access token veya hassas dosya
adı loglanmadan en az şu ölçümler bulunmalıdır:

- staged/quarantined/scanning/clean/rejected sayı ve en eski yaş;
- upload/finalize/scan latency ve failure sınıfları;
- orphan object, missing object, checksum/hash mismatch ve reconcile sonucu;
- dedup oranı yalnız yetkili aggregate metric olarak;
- signed access issue/redeem/expiry/revocation ve yetki reddi;
- object-store/scanner availability, retry/backpressure ve kapasite;
- retention/legal hold ve backup/restore mutabakat sonucu.

Alarm eşikleri ve hacim hedefleri `OPEN-015`, `OPEN-017`, `OPEN-027` ve gerçek yük kanıtı
olmadan sabitlenmez.

## Kodlama kapısı

ERP-000G için bu öneri tek başına yeterli değildir. Aşağıdakilerin tamamı sağlanmadan
feature veya test-substrate kodu, package/provider seçimi, object-store container/bucket,
migration, upload endpoint'i, worker, signed URL veya pilot document contract geliştirilmez:

1. Fiziksel ADR-005 insan/ChatGPT mimari ve security review'uyla `Kabul` olur.
2. Tam ERP-000G feature spec ve task-local acceptance planı kabul edilir.
3. `OPEN-026`, `OPEN-027`, `OPEN-028`, `OPEN-029` karar sahibi tarafından kapanır;
   `OPEN-018` açık kalacaksa production purge/delete açıkça disabled kalır.
4. Task manifest dependency/status gerçeği B/C/D/E ve seçilen async tasarım için F ile
   uyumlu hale getirilir.
5. Pilot document/version/link owner, CompanyId, metadata, lifecycle, permission ve veri
   sınıfı yazılı kabul edilir.
6. Gerekli cross-module edge varsa ADR, exact allowlist ve architecture test aynı atomik
   review değişikliğinde tanımlanır.
7. Gerçek SQL Server + gerçek S3-compatible engine acceptance tasarımı onaylanır.

## Test ve kanıt planı

- Contract/unit: hash algorithm/version, canonical metadata, MIME sniff/allowlist,
  version/link state transition, permission ve signed capability kapsamı.
- Gerçek SQL + gerçek S3-compatible engine: aynı byte'ın aynı ve farklı şirkette paralel
  upload'ı; tek physical blob olabilmesi fakat ayrı CompanyId logical belge/link ve sıfır
  cross-tenant oracle/sızıntı.
- Crash/failure injection: object-success/DB-fail, DB-session/object-missing,
  scan-success/finalize-fail, missing/corrupt object, retry ve orphan reconciliation.
- Security: extension/MIME mismatch, malware, scanner unavailable/timeout, archive bomb,
  password/nested archive, path traversal adı ve clean olmadan preview/download/link/AI
  reddi.
- Authorization: B hash/object/version kimliğini bilen A kullanıcısına metadata/count/error/
  signed URL sızıntısı olmaması; permission daralması, expiry/revocation ve hassas export.
- Concurrency/idempotency: aynı upload key replay'i, farklı payload conflict'i, paralel yeni
  version/active-version yarışı, link ekleme/kaldırma ve shared-ref delete koruması.
- Migration/restore: clean upgrade/repeat script, DB+object version/checksum restore,
  orphan/missing reconciliation ve şirket export paketinde başka tenant metadata'sı olmaması.
- Architecture: başka modül table/object-key erişimi yok; yalnız Application contract veya
  kabul edilmiş exact edge; default-deny dependency guard yeşil.
