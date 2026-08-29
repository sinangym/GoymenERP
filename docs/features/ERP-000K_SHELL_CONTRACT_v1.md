---
contract_id: ERP-000K-SHELL-CONTRACT
version: 1
contract_status: OPEN
readiness_document_status: ACCEPTABLE
implementation_status: BLOCKED
production_activation: NO-GO
owner: CODEX-PLATFORM
review_class: SHARED_UI_SECURITY_CONTRACT
reviewed_base_sha: 6aa4a46a964ed2dae427e83026bb449449bd09df
---

# ERP-000K — Ortak React Kabuğu Fiziksel Readiness ve Contract v1

- İlk hazırlık tarihi: 2026-08-28
- Current-main yeniden doğrulama tarihi: 2026-08-29
- Görev: `ERP-000K — Ortak React kabuğu`
- Fiziksel contract kimliği: `ERP-000K-SHELL-CONTRACT`
- Kapsam: ortak shell, güven sınırı, feature-mount arayüzü ve kabul planı
- Readiness belgesi: **ACCEPTABLE**
- Contract kapanışı: **OPEN**
- React/UI implementation: **NO-GO / BLOCKED**
- Production activation: **NO-GO**

## 1. Sonuç

Göymen ERP'nin koyu lacivert ortak kabuk yönü, sayfa anatomisi ve erişilebilirlik
ilkeleri yeterince nettir. Bununla birlikte bugünkü frontend yalnız ERP-000A+B
Organization bootstrap ekranıdır; production ortak shell değildir.

Bu dosya daha önce yalnız metin etiketi olan `ERP-000K-SHELL-CONTRACT` için repository
içindeki tek fiziksel readiness ve contract artifact'idir. Artifact'in `main`e alınması
contract'ın kapandığı veya implementation'ın başlayabileceği anlamına gelmez.

Implementation aşağıdaki nedenlerle blokludur:

1. ERP-000L ve ERP-000O dahil bütün doğrudan manifest bağımlılıkları tamamlanmış değildir.
2. Server-backed capability kaynağı mevcut değildir. `/api/v1/identity/me` bugün
   yalnız kullanıcı profili ve izinli şirket kimliklerini döndürür; permission, ABAC scope
   veya feature availability sağlamaz.
3. Production UI route envanteri ve sürümlü feature-mount sözleşmesi fiziksel runtime
   contract olarak uygulanmamıştır.
4. Tek şirket ile açıkça yetkilendirilmiş çok-şirket okuma modu aynı UI context'i gibi
   kullanılamaz; route/API bazlı fail-closed sözleşme gerekir.
5. Router, query/cache, component, accessibility ve browser-E2E paketleri güncel lisans,
   güvenlik ve repo uyumluluğu review'u olmadan seçilemez.
6. Canonical acceptance satırlarının UI/browser kanıtı eksiktir.

Bu docs-only çalışma hiçbir package, provider, endpoint şekli, business route, role,
threshold, token, breakpoint veya production davranışı uydurmaz.

## 2. Bağlayıcı kaynaklar

Kaynak önceliği kök `AGENTS.md` sırasıdır.

| Kaynak | Bağlayıcı ERP-000K sonucu |
|---|---|
| `DEC-GOV-002` | Grup görünümü CompanyId güvenlik sınırını kaldırmaz; grup toplamı yalnız yetkili server-side görünüm/rapordur. |
| `DEC-GOV-004` | Eksik yetenek feature gate ile kapalı kalır; çalışıyormuş gibi gösterilemez. |
| `DEC-GOV-007` | Ertelenmiş veya uygulanmamış alan için boş/inert menü gösterilmez. |
| `DEC-ORG-001` | Company, Site, Location ve WorkCenter ayrı kapsam kavramlarıdır. |
| `DEC-AI-001/002/005` | AI normal ERP akışını bloke etmez; yetki veya iş doğrusu değildir; write önizleme/onay/audit ister. |
| `DEC-TEC-002` | Web istemcisi React + TypeScript'tir. |
| `DEC-TEC-005` | Arama sonucu açılışında backend yetkisi yeniden doğrulanır. |
| `DEC-TEC-009` | UI hatası güvenli correlation ile tanılanabilir olmalıdır. |
| `DEC-TEC-010` | Desteklenen cihaz ve tarayıcı aileleri gerçek kabul matrisi ister. |
| `DEC-TEC-011` | Ücretli UI/grid paketi varsayılan karar değildir. |
| `DEC-TST-001/002` | Ekranın açılması acceptance değildir; yetki, accessibility ve gerçek kullanıcı akışı test edilir. |
| Master v3 bölüm 5 | Ortak shell anatomisi, sayfa tipleri, global toolbar ve durum katmanı. |
| Master v3 bölüm 6 | Sayfa içi ve sağ panel AI yerleşimi; ERP authority korunur. |
| Master v3 bölüm 26 | Koyu lacivert, açık çalışma yüzeyi, güçlü mavi aksiyon ve kontrollü semantik renk yönü. |
| `docs/ui/UI_DESIGN_SYSTEM.md` | Ortak page/state, responsive, accessibility ve üretime taşınmaması gereken prototip davranışları. |
| `DASHBOARD-REFERENCE-D-01` | D-01 ve GLOBAL-SHELL için güçlü görsel yön; mock içerik ve production acceptance değildir. |

## 3. Current-main gerçeği

İncelenen `main@6aa4a46a964ed2dae427e83026bb449449bd09df` üzerinde:

- ERP-000A/B/C/D/E `done` durumundadır.
- ERP-000M/O/N/L readiness artifact'leri `main`dedir; görevlerin implementation durumu
  `blocked` kalır.
- Current readiness yönü `M → O → N → L`, ardından bağımsız `J/K`dır.
- ERP-000K current-main manifest satırı hâlâ A-only ve `ready` idi; bu PR yalnız K
  objesini fail-closed `blocked` durumuna getirir.
- D-01 kaydı `CANDIDATE_EXACT`; kaynak `DESIGNED_NOT_LOCKED`, görsel
  `PROTOTYPE_REFERENCE` durumundadır.
- UI kataloğunda 72 kaynak kayıt ve 159 kanonik ekran analizi vardır. Bu ekranların
  tamamı menu item veya implementation-ready route değildir.
- Mevcut frontend React 19 + TypeScript/Vite bootstrap'ıdır; yalnız React ve ReactDOM
  runtime dependency'si vardır.
- Router, route table, route guard, capability registry, shared query/cache, error
  boundary, ortak component library veya browser-E2E harness yoktur.
- Mevcut Company selector, server'ın izinli şirket listesini kullanır ve seçili şirketi
  `X-Company-Id` ile yollar; localStorage yalnız başlangıç tercihidir.
- `AbortController` vardır, fakat dirty form, pending write, permission değişimi,
  request-generation fence ve company-scoped cache lifecycle yoktur.
- Mevcut menüde yalnız `#dashboard` bağlantısı gerçektir; diğer modül adları etkileşimsiz
  `span` öğeleridir.
- `620 px` altında navigasyon gizlenir ve mobil karşılığı yoktur.
- Site ve WorkCenter `isActive` alanı alınmasına rağmen UI bütün satırlarda sabit `Aktif`
  gösterir.
- `/api/v1/identity/me`, `UserId`, `DisplayName`, `Email` ve `AllowedCompanyIds` döndürür;
  permission/scope/capability response'u değildir.
- Organization CompanyContext endpointleri tek `X-Company-Id` ve server-side permission
  doğrulamasını kullanır.
- Counterparty read API'si yalnız kendi owner contract'ında açıklandığı durumda header
  olmadan bütün yetkili şirketleri server tarafında çözebilir. Bu davranış global shell
  için genel çok-şirket kuralı değildir.

Bu envanter implementation acceptance değildir; geriye düşmeme tabanıdır.

## 4. D-01 ve GLOBAL-SHELL kararı

### 4.1 Kabul biçimi

D-01, ortak shell için **yapısal referans + açık deltalarla kabul** edilir. Bu karar
D-01 ekranının katalog statüsünü değiştirmez ve dashboard business içeriğini kilitlemez.

Korunacak yön:

- koyu lacivert, yüksek kontrastlı ana navigasyon;
- beyaz global toolbar ve açık/nötr çalışma yüzeyi;
- güçlü mavi tek ana aksiyon;
- belirgin sınır, kontrollü gölge ve aşırı yumuşak olmayan kurumsal karakter;
- sayfa içeriği ile sağ bağlam/detay/AI alanının ayrımı;
- yüksek veri yoğunluğunu katmanlı ve okunur gösterme.

Zorunlu deltalar:

- görseldeki kişi, tarih, sayı, KPI, bildirim, badge, hızlı aksiyon, AI cevabı ve business
  sonuçları mock'tur; seed/formül/permission değildir;
- `TÜM GRUP` seçimi magic CompanyId veya client-side birleşim değildir;
- search, `+ Yeni`, notification ve AI yalnız kabul edilmiş capability ile etkinleşir;
- sağ panel Master gereği `1600 px` altında varsayılan drawer olur;
- erişilebilirlik, responsive, security ve performans pixel benzerliğinden üstündür;
- tokenlar WCAG 2.2 AA ölçümü ve marka review'u olmadan kilitli sayılmaz.

### 4.2 Dashboard ile shell ayrımı

`D-01 Rol Bazlı Ana Sayfa` içerik kompozisyonudur. `GLOBAL-SHELL`, bütün route'ların
kullandığı platform sözleşmesidir. D-01 içindeki KPI, risk tablosu, bugün/hafta özeti ve
AI yönetim özeti ERP-000K'nin domain scope'u değildir.

## 5. Amaç ve kapsam dışı

ERP-000K'nin amacı:

1. Bütün modüllerin kullandığı tek React shell ve ortak sayfa anatomisini kurmak.
2. Menu, route, action ve extension görünürlüğünü server-backed capability ile bağlamak.
3. Client görünürlüğünün backend authorization yerine geçmesini engellemek.
4. CompanyContext, group-read, route, cache ve async lifecycle sınırlarını fail-closed
   yapmak.
5. Liste, 360, form, workspace, drawer ve modal için ortak durum/etkileşim contract'ı
   sağlamak.
6. Desktop, tablet ve mobilde gerçek görev akışı; klavye ve ekran okuyucu erişimi
   sağlamak.
7. Arama, AI, bildirim ve business feature'ları için mock üretmeyen mount noktaları
   tanımlamak.
8. Error/session/correlation davranışını ERP-000L, test kapılarını ERP-000O ile bağlamak.

Kapsam dışı:

- satış, satın alma, Cari, ürün, stok, üretim, finans veya belge ekranı kodlamak;
- D-01 KPI/formül/count endpoint'i üretmek;
- ERP-000I search, ERP-000J AI veya ERP-000F notification davranışını kodlamak;
- business permission, rol, onay eşiği veya formül üretmek;
- SSO sağlayıcısı, session süresi, MFA veya logout protocolü seçmek;
- router, query/cache, grid, icon, accessibility veya E2E paketi seçmek;
- ücretli package istisnası veya lisans/TCO kararı vermek;
- kesin token, font, logo, animasyon veya touch target değeri uydurmak;
- UI katalog veya acceptance statüsü değiştirmek;
- source, package, test, workflow, container veya deployment değiştirmek.

## 6. Manifest bağımlılıkları

| Task | ERP-000K'nin tükettiği sınır |
|---|---|
| ERP-000A | React/build/test/container başlangıcı. |
| ERP-000B | CompanyContext ve tenant izolasyonu. |
| ERP-000C | Identity, RBAC/ABAC ve server-side permission. |
| ERP-000D | Audit/correlation sonucu; shell logu business audit değildir. |
| ERP-000E | Conflict, idempotent replay ve unknown write sonucu. |
| ERP-000L | Error boundary, frontend correlation, redaction ve tanılama. |
| ERP-000O | Frontend/component/browser/accessibility merge kapıları. |

Doğrudan bağımlılıklar tam olarak A/B/C/D/E/L/O'dur. M/O/N/L readiness artifact'lerinin
merge edilmiş olması runtime acceptance değildir. Bütün bağımlılıklar `done/accepted`
olmadan implementation başlamaz.

Koşullu extension sahipleri:

- Global search: ERP-000I.
- İşlevsel AI: ERP-000J.
- Notification/event feed: ERP-000F veya daha sonra kabul edilen owner.
- Cari feature: kabul edilmiş Cari owner contract'ı.

Bu koşullu feature'lar kapalı kaldığı sürece base shell'in hard manifest bağımlılığı
değildir. Etkinleştirme kendi acceptance kapısını ayrıca ister.

## 7. Contract alt kapıları

| Alt kapı | Durum | Kapanış kanıtı |
|---|---|---|
| D-01 yapısal yön ve delta | `DEFINED` | Bu dosyanın bölüm 4'ü; katalog statüsü değişmez. |
| Server-backed capability | `OPEN` | Sürümlü backend contract + permission/scope/feature negative testleri. |
| UI route registry | `OPEN` | Canonical screen/route/owner/capability/context/deep-link matrisi. |
| Feature-mount handoff | `OPEN` | Shared owner arayüzü + tek-writer ve source parity testleri. |
| Single-company context | `DEFINED_NOT_IMPLEMENTED` | Header/server permission/cache/switch E2E. |
| Authorized multi-company read | `OPEN` | Route-owner API contract + A/B/C negative tests; write yasak. |
| Period context | `OPEN_FEATURE_GATED` | Backend owner/contract olmadan toolbar kontrolü etkin değildir. |
| Dirty state ve async fence | `DEFINED_NOT_IMPLEMENTED` | Browser race, pending write ve cache isolation testleri. |
| Shared UI states | `DEFINED_NOT_IMPLEMENTED` | Component + browser state matrix. |
| Responsive ve accessibility | `DEFINED_NOT_IMPLEMENTED` | Otomatik + gerçek keyboard/screen-reader/zoom/contrast kabulü. |
| AI/search/notification slots | `DEFINED_DISABLED` | Owner task + server capability olmadan görünür işlev yok. |
| Package/tool seçimi | `OPEN` | Güncel resmî destek, lisans, security ve repo compatibility review'u. |
| Canonical UI/browser acceptance | `OPEN` | İlgili AT satırları ve task-local senaryolar. |

`ERP-000K-SHELL-CONTRACT` ancak bütün `OPEN` ve `DEFINED_NOT_IMPLEMENTED` kapılar kabul
kanıtıyla kapandıktan sonra manifest blocker'ından çıkarılabilir.

## 8. Server-backed capability sözleşmesi

### 8.1 Authority

- UI, permission veya feature availability'yi rol adından, JWT decode işleminden,
  localStorage'dan, route isminden veya görünür control'den türetmez.
- Server, authenticated user ve gerekli aktif context için izin verilen capability ile
  desteklenen scope bilgisinin tek yetkili kaynağıdır.
- Exact endpoint, response shape ve cache/version alanları ayrı platform contract'ında
  kabul edilmeden kodlanmaz.
- Capability response'u backend endpoint reauthorization yerine geçmez.
- Unknown, eksik, malformed veya stale capability fail-closed olur.
- Dar Site/Location/WorkCenter scope, company-wide boolean'a yükseltilmez. Route dar
  scope'u doğru temsil edemiyorsa görünmez veya güvenli ret verir.
- Feature uygulanmamışsa kullanıcının permission'ı olsa bile boş menu/route/action açılmaz.
- Permission yoksa route label, badge, count, recent, favorite, title veya hata süresi
  üzerinden kayıt/feature varlığı sızdırılmaz.

### 8.2 Lifecycle

Capability cache'i en az authenticated user, session, aktif context ve server policy
revision'ıyla ilişkilendirilmelidir. Kesin key/transport bu contract'ta seçilmez.

Şu değişikliklerde eski capability kullanılmaz:

- login/logout veya kullanıcı değişimi;
- CompanyContext veya group-read mode değişimi;
- permission/scope daraltması;
- feature availability veya deployment revision değişimi;
- 401/403 ve server tarafından stale/invalid bildirimi.

## 9. Company ve organizasyon context'i

### 9.1 Tek şirket modu

- Seçili şirket, server'ın yetkili şirket listesinde bulunmalıdır.
- Tenant endpointlerinde exact `X-Company-Id` gönderilir ve server yeniden doğrular.
- localStorage yalnız kullanıcı kolaylığı tercihidir; authentication/authorization veya
  CompanyContext kaynağı değildir.
- Site, Location ve WorkCenter bağımsız scope'tur; serbest tek filtreye indirgenmez.
- Transactional write, açık ve server-doğrulanmış tek CompanyContext olmadan çalışmaz.

### 9.2 Yetkili çok-şirket okuma modu

`DEC-GOV-002` grup görünümünü kabul eder; ancak her route otomatik grup görünümü değildir.

- Route/feature owner, çok-şirket okumayı açıkça desteklediğini beyan etmelidir.
- Server izinli şirket setini ve her şirket için permission'ı kendisi çözer.
- İstemci şirket kimliklerini birleştirip bütün veri setini browser'a çekmez.
- Magic `Guid`, `all`, boş string veya sahte CompanyId kullanılmaz.
- Header'ın bilinçli olarak gönderilmemesi yalnız ilgili API contract'ında kabul edilmişse
  anlamlıdır; diğer endpointlere genellenmez.
- Grup total/count, yetkisiz şirketi sayı veya zaman farkıyla sızdırmaz.
- Grup görünümü kaynak company bilgisini kaybetmez; şirket hesaplarını birleştirip ikinci
  business truth üretmez.
- Write, group mode içinden başlatılsa bile kullanıcı hedef şirketi seçer ve işlem tek
  server-doğrulanmış CompanyContext'e geçmeden uygulanmaz.

### 9.3 Dönem bağlamı

Master global toolbar'da dönem bağlamı ister. Bugün kabul edilmiş dönem backend owner ve
contract'ı yoktur. Shell içinde yerleşim noktası ayrılabilir; görünür/işlevsel dönem
seçici, tarih varsayımı veya business filter uygulanamaz. Dönem context'i CompanyContext
ve authorization yerine geçmez.

## 10. Route ve feature-mount sözleşmesi

### 10.1 Route registry

Production route registry en az şu gerçekleri taşımadan route açamaz:

- stable feature ve route identity;
- canonical screen/workspace kimliği;
- owner modül/contract ve implementation availability;
- path/deep-link kalıbı;
- page type ve shell layout ihtiyacı;
- gerekli server capability ve temsil edilen scope;
- `single-company`, açık `authorized-multi-company-read` veya context-free sınıfı;
- breadcrumb ve güvenli geri dönüş;
- loader/action server reauthorization sınırı;
- 401/403/non-disclosing 404/session-expiry sonucu;
- query/cache namespace ve company-switch invalidation;
- search, AI, notification veya drawer extension bağı.

Exact serialization biçimi ve router package'i bu contract'ta seçilmez.

### 10.2 Güvenlik

- Menu visibility, disabled state veya client route guard authorization değildir.
- Direct URL, refresh, yeni sekme, browser back/forward ve favorite/recent her açılışta
  güncel backend yetkisini kullanır.
- Route parametresindeki CompanyId/record ID server scope'unu genişletemez.
- Yetkisiz veya uygulanmamış route, başka şirket kaydının varlığını title, breadcrumb,
  count, toast veya hata ayrıntısıyla göstermez.
- `+ Yeni` yalnız uygulanmış ve yetkili action'ları sunar; boş modal veya mock kayıt akışı
  açmaz.
- Canonical katalogdaki 159 ekranın tamamı ana menu item değildir.
- Cari prototipindeki hash route'lar kanonik production route değildir.

### 10.3 Feature-mount ownership

Shared shell owner şunlara sahiptir:

- root `App` composition;
- router ve route registry altyapısı;
- shared query/cache ve CompanyContext fence'i;
- global token/layout/shared component ve browser/a11y harness;
- generic feature-mount contract.

Feature owner şunlara sahiptir:

- kendi canonical screen ve route tanımı;
- business permission/capability gereksinimi;
- API mapping, business state ve domain empty/error/conflict sonucu;
- kendi acceptance fixture'ı.

Feature, shared router/App/global CSS/query client üzerinde ayrı writer olamaz. Shell de
feature business kuralını veya permission semantiğini tahmin edemez.

## 11. Ortak shell ve sayfa anatomisi

Her uygulanmış ve izinli sayfa şu ortak katmanlardan ihtiyacı olanları kullanır:

1. Koyu lacivert ana navigasyon ve mobil karşılığı.
2. Global toolbar: şirket/group bağlamı, feature-gated dönem, search, `+ Yeni`,
   notification, yardım ve kullanıcı/session.
3. Breadcrumb.
4. Sayfa başlığı, kapsam, durum, son güncelleme ve tek ana aksiyon.
5. Gerektiğinde 3-6 karar KPI'ı, görünüm ve filtre satırı.
6. Liste, 360, form, workspace veya kontrol yüzeyi.
7. Sağ context/detail/AI alanı.
8. Uzun veya etkili işlemde sabit alt işlem çubuğu.
9. Loading/empty/error/conflict/success ve correlation durum katmanı.

KPI dekoratif değildir. Server kaynağı/formülü/capability'si olmayan sayı veya badge
gösterilmez.

### 11.1 Sayfa tipleri

| Tip | Shell sınırı |
|---|---|
| Liste/Görev Merkezi | Server-side filter/sort/page; keyboard action; dar scope totalini company total gibi göstermez. |
| 360 detay | Kimlik/context sabit; tab/deep-link; bağlı record açılışında yeniden authorization. |
| Rehberli form | Dirty/validation/save/conflict/replay; başarısız save form verisini kaybettirmez. |
| Tam ekran workspace | Context → kaynak → girdi → deterministic preview → kontrol/onay → sonuç. |
| Drawer | Hızlı detay/özet; deep-link/back/focus-return; uzun zincir işleme dönüşmez. |
| Modal | Kısa/tek odak; focus trap, Escape, inert background ve focus-return. |
| Karşılaştırma | Eski-yeni veya kaynak-hedef farkı, risk, yetki/onay ve preview referansı. |

## 12. Ortak UI durumları

Her page/feature uygulanabilir durumları açıkça ayırır:

- ilk loading/skeleton;
- gerçek veri yok;
- filter sonucu boş;
- unauthorized veya dar scope;
- partial, stale veya freshness bilinmiyor;
- validation error ve field/summary bağlantısı;
- saving ve result bilinmiyor;
- idempotent replay;
- optimistic concurrency conflict ve güvenli fark;
- başarı, etkilenen kayıt ve audit/correlation referansı;
- capability/search/AI/notification/integration geçici olarak kullanılamıyor;
- malformed response veya beklenmeyen route/page error.

Loading eski company verisini yeni context'in güncel sonucu gibi göstermez. Empty,
unauthorized, 404 ve service error aynı metin veya state değildir. Retry yeni write
üretmez; write sonucu bilinmiyorsa ERP-000E result/replay sözleşmesi kullanılır.

## 13. Company switch, dirty state ve async fence

1. Dirty form, edit workspace veya onay preview'i sessizce yeni şirkete taşınmaz.
2. Kullanıcı açıkça kal/vazgeç veya owner'ın kabul edilmiş güvenli save akışını
   tamamlamadan context commit edilmez.
3. Pending write sonucu bilinmiyorsa company switch duplicate write tetiklemez.
4. Eski şirket read/write/drawer/lazy request'i iptal edilir ve request generation /
   context fence ile geç kalan response atılır.
5. Query/cache/optimistic state/company total/suggestion contextler arasında paylaşılmaz.
6. Yeni context server tarafından yeniden yetkilendirilmeden route/data açılmaz.
7. Eski şirket verisi skeleton, animation, error fallback veya browser history içinde
   kısa süreli de olsa görünmez.
8. Açık modal/drawer kapanır veya yeni context'te yeniden yetkilendirilir; focus güvenli
   yere döner.
9. Logout ve kullanıcı değişiminde hassas view/form state'i yeni session'a taşınmaz.
10. Permission daraltması açık page, favorite, recent ve background refresh'i yeniden
    doğrular.

Yalnız `AbortController` bulunması bu kabulü geçirmez; gecikmiş resolve ve uncancellable
write senaryoları browser testinde kanıtlanır.

## 14. Session, error ve shell güvenliği

- `401`, login/session expiry akışıdır; dirty form için güvenli kayıp önleme ve hassas
  veriyi yeniden login ekranına sızdırmama gerekir.
- `403`, kayıt yokmuş veya gerçek empty gibi gösterilmez; forbidden detail sızdırılmaz.
- `404`, yabancı kayıt varlığını doğrulamayan ortak non-disclosing sözleşmeye uyar.
- `409`, stable domain/error contract ile concurrency/idempotency/business conflict
  ayrımını owner API'den tüketir; metinden tahmin etmez.
- Error boundary route/page/drawer seviyesinde güvenli fallback ve correlation reference
  sağlar; ham stack, token, SQL, CompanyId veya foreign record ID göstermez.
- URL, title, breadcrumb, recent, favorite, toast, analytics ve client log da veri
  sızıntısı yüzeyidir.
- Dinamik içerik kontrolsüz `innerHTML` ile render edilmez. Prototip inline handler'ları
  production davranışı değildir.
- Development identity/header production shell'de etkinleştirilmez; environment badge
  security kontrolü değildir.
- Client-supplied correlation authorization/CompanyContext kaynağı değildir.
- Frontend telemetry ham token, personal data, business payload veya yüksek-cardinality
  raw identifier taşımaz; ERP-000L redaction contract'ına uyar.

## 15. Responsive ve accessibility

### 15.1 Layout

- `1600 px` altında sağ context/detail/AI alanı Master gereği varsayılan drawer olur.
- `1440 px+`: tam navigasyon ve çok kolon; `1440-1599` aralığında sağ alan drawer'dır.
- `1024-1439 px`: daraltılabilir navigasyon ve yeniden akan kartlar.
- `768-1023 px`: overlay navigasyon, overlay drawer/AI ve en fazla uygun iki kolon.
- `<768 px`: tek kolon, tam ekran workspace; tablo için görev odaklı kart veya kontrollü
  yatay çözüm.

Breakpointler source baseline'dır; yeni magic değer eklenmez. Mobil navigasyonu
`display:none` ile kaybetmek veya desktop tabloyu yalnız küçültmek kabul değildir.

### 15.2 Keyboard ve ekran okuyucu

- İlk odakta görünür skip-link ile ana içeriğe geçilir.
- Landmark, heading, nav label, form label/error ilişkisi ve semantik control kullanılır.
- Aktif route `aria-current` ve metin/görsel işaret taşır.
- Focus her etkileşimde görünür; hover tek keşif yolu değildir.
- Modal/drawer focus trap, Escape, inert background ve focus-return test edilir.
- Async loading/error/success uygun live region ile duyurulur; KPI spam üretmez.
- Icon-only action erişilebilir ada sahiptir; badge tek başına anlam taşımaz.
- Sort, validation summary, error focus ve table/card alternatifi ekran okuyucuya anlamlıdır.

### 15.3 Görsel kabul

- Başlangıç tokenları WCAG 2.2 AA ölçümü ve marka review'u olmadan kilitli değildir.
- Renk tek başına state taşımaz; metin ve icon/şekil birlikte kullanılır.
- Zoom/reflow, uzun Türkçe metin, yüksek data density ve focus outline kesilmesi gerçek
  içerikle test edilir.
- Reduced-motion gereksiz hareketi azaltır; context/state değişimi görünür kalır.
- Skeleton veya animation flash/sürekli hareketle görevi engellemez.

## 16. Search, AI ve notification extension'ları

### 16.1 Global search

ERP-000I ve server capability kabul edilmeden shell mock result, suggestion, recent,
count veya autocomplete üretmez. Bütün tablolar browser'a yüklenip filtrelenmez. Feature
etkinleştiğinde AT-002 ve AT-069 ile source reauthorization gerekir.

### 16.2 AI

ERP-000J ve server capability kabul edilmeden AI slotu internal extension point olarak
kalır; görünür prompt, cevap, attachment, tool veya `Uygula` davranışı yoktur. AI kapalı
veya arızalıyken normal ERP görevi tamamlanır.

Etkinleştiğinde:

- active route/record ve Company/Site/Location/WorkCenter scope server tarafından
  yeniden doğrulanır;
- model çıktısı permission, onay, formül veya business truth değildir;
- write preview/onay/idempotency/audit zincirini atlayamaz;
- AT-059/060 ve ilgili akışta AT-068 geçer;
- panel kapatılabilir, keyboard/focus ve `1600 px` drawer sınırına uyar.

### 16.3 Notification

Kabul edilmiş event/feed owner olmadan fake unread badge, count veya delivery/read state
gösterilmez. Feature etkinleştiğinde her link current CompanyContext ve permission ile
yeniden doğrulanır.

## 17. Cari feature-mount sınırı

- ChatGPT-Cari; Cari canonical screen, business UI, API mapping, paging/filter/search ve
  business state sahibidir.
- Codex-Platform; common shell, shared router/query/cache, feature-mount, shared component
  ve browser/accessibility harness sahibidir.
- PR #77 veya onun accepted successor'ı, Cari company-link backend davranışını kapatmadan
  link action mount edilmez.
- PR #79 veya onun accepted successor'ı, Cari UI route ve capability contract'ını
  kapatmadan `/cari` route ailesi production registry'ye alınmaz.
- Bu PR numaraları coordination evidence'dır; ERP-000K manifest dependency'si değildir.
- Cari contract'ı K contract'ının physical path/version bilgisini tüketir; K contract'ı
  Cari business rule veya route değerini sahiplenmez.
- Cari prototype hash route'ları production route, mock risk/bakiye/vergi/iletişim verisi
  veya permission kaynağı değildir.
- Cari PR/branch/docs/code bu ERP-000K dalında değiştirilmez.

## 18. Acceptance ve test planı

### 18.1 Canonical acceptance bağı

| Acceptance | ERP-000K sonucu |
|---|---|
| AT-001 | Route, menu, list, count ve detail yalnız yetkili Company/Site/Location/WorkCenter kapsamını gösterir. |
| AT-003 | Kritik action sonucu audit/correlation referansını doğru gösterir; shell logu audit değildir. |
| AT-005 | Concurrency conflict ve güvenli fark UI'da görünür; silent overwrite yoktur. |
| AT-006 | Başarısız save form verisini kaybettirmez. |
| AT-008 | CompanyContext değişimi açık formu sessiz başka şirkete taşımaz. |
| AT-002/069 | Yalnız search etkinse permission-safe ve hacim/keyset kabulü. |
| AT-059/060/068 | Yalnız AI etkinse preview/onay, tenant ve permission güvenliği. |

Bu docs-only PR acceptance statüsü veya kanıt alanı değiştirmez.

### 18.2 Task-local güvenlik senaryoları

1. A yetkili kullanıcı URL/localStorage/cache üzerinden B label/title/count/record
   varlığını öğrenemez.
2. Missing/stale/malformed capability fail-closed olur; menu görünürlüğü permission
   üretmez.
3. Narrow scope'u temsil edemeyen route company-wide açılmaz.
4. Direct URL/refresh/back-forward ve yeni sekme aynı server authorization sonucunu verir.
5. Group-read route yalnız server'ın yetkili company setini kullanır; write tek company
   context'e geçmeden uygulanmaz.
6. Dirty formda switch sessiz ilerlemez; karar öncesi context değişmez.
7. A'nın yavaş response'u B context'ine uygulanmaz.
8. Unknown pending write duplicate write üretmez; replay/result akışı kullanılır.
9. Cache/count/suggestion/optimistic state şirket ve kullanıcılar arasında paylaşılmaz.
10. Permission daraltması açık route/drawer/favorite/recent'i yeniden doğrular.
11. 401/403/non-disclosing 404/409 ve service error ayrı güvenli state üretir.
12. `isActive=false` satırı `Aktif` gösterilmez; unknown state uydurulmaz.
13. AI/search/notification kapalıyken mock sonuç veya inert çalışan control yoktur.
14. Dev identity production build/runtime'da request header üretemez; backend guard korunur.

### 18.3 Responsive ve accessibility senaryoları

15. Onaylı desktop/tablet/mobile matrix'te navigasyon ve route seçimi erişilebilir kalır.
16. `1440-1599 px` dahil `1600 px` altı right panel drawer'dır.
17. Keyboard ile skip-link, menu, company picker, page action, list, drawer, modal ve
    action bar tamamlanır.
18. Focus visible; modal/drawer focus trap/return ve Escape doğrudur.
19. Screen reader landmark, heading, label, validation, loading/error/success ve
    `aria-current` semantiğini okur.
20. WCAG 2.2 AA contrast, zoom/reflow, uzun Türkçe metin ve reduced-motion kabulü geçer.
21. Mobilde desktop tabloyu küçültmek yerine owner'ın kabul ettiği görev odaklı alternatif
    veya kontrollü yatay kullanım çalışır.

### 18.4 Test katmanları

- Unit/component: route metadata, capability/feature gating, state reducer, focus ve
  company-switch fence.
- Contract/API: CompanyContext, permission/scope, 401/403/404/409, correlation ve server
  reauthorization.
- Browser E2E: direct route, rapid switch/race, dirty form, cache invalidation, session
  expiry, responsive navigation, keyboard/focus/modal/drawer.
- Accessibility: automated scan + gerçek keyboard/screen-reader/zoom/contrast insan
  kontrolü; otomatik araç tek kabul değildir.
- Visual regression: yalnız kabul edilen D-01 deltası ve onaylı viewport/token matrix;
  pixel benzerliği security/accessibility yerine geçmez.
- Adversarial: foreign company/record ID, stale cache, open redirect, dynamic content,
  URL/title/count/client-log leakage ve dev identity production guard.

## 19. Package ve implementation seçimi

Bugünkü runtime package seti ortak shell için yeterli değildir; bu durum package ekleme
izni vermez. Router, query/cache, form, grid, icon, accessibility ve browser-E2E
seçenekleri implementation öncesi:

- güncel resmî destek ve maintenance;
- security/vulnerability ve supply-chain;
- lisans, ücretli istisna ve üç yıllık TCO;
- React/TypeScript/Vite uyumluluğu;
- SSR/PWA gerektirmeyen bugünkü architecture uyumu;
- bundle/performance ve accessibility;
- deterministic test ve upgrade/rollback

açısından ayrı review edilir. Provider/package adı bu contract'ta kilitlenmez.

## 20. Contract kapanış koşulları

`ERP-000K-SHELL-CONTRACT` ancak:

- A/B/C/D/E/L/O kabul edilip `main`e alınmış;
- bu fiziksel artifact güncel source truth ile review edilmiş;
- server-backed capability ve scope contract'ı uygulanmış/test edilmiş;
- UI route registry ve feature-mount contract'ı sürümlü ve fail-closed olmuş;
- single-company ve authorized-multi-company-read ayrımı test edilmiş;
- period context açık owner contract'ı yoksa görünür feature olarak kapalı kalmış;
- dirty form, pending write, cache ve stale-response CompanyContext fence'i geçmiş;
- shared states, deep-link, session, error, correlation/redaction uygulanmış;
- responsive matrix, `1600 px` drawer ve mobile navigation uygulanmış;
- keyboard/focus/skip-link/aria/contrast/zoom/reduced-motion kabulü geçmiş;
- AI/search/notification disabled/enabled davranışı owner contract'larıyla kanıtlanmış;
- package/tool seçimleri lisans/security/repo review'undan geçmiş;
- canonical ve task-local acceptance browser kanıtı üretmiş;
- P0/P1 security, accessibility veya architecture bulgusu kalmamış

ise `OPEN` durumundan çıkarılabilir.

Bu kapanış current D-01 veya acceptance katalog statüsünü kendiliğinden değiştirmez.

## 21. Şimdi izinli ve yasak

İzinli:

- capability/route/feature-mount contract taslağını ayrı review'a hazırlamak;
- package seçmeden component/route/test sorumluluk envanteri çıkarmak;
- gerçek veri içermeyen adversarial/keyboard/viewport fixture planlamak;
- owner feature contract'larına fiziksel shell path/version handoff'u vermek;
- yalnız docs-only current-main truth sync yapmak.

Yasak:

- React/CSS/router/cache/component/test/package source değiştirmek;
- D-01/UI katalog statüsünü kilitlemek veya asset/token değiştirmek;
- period/search/AI/notification/Cari davranışı uydurmak;
- role/permission/business route/formül veya CompanyId kuralı üretmek;
- client visibility'yi authorization saymak;
- group mode için magic CompanyId veya client-side data union kullanmak;
- canonical `OPEN-*`, `AT-*`, `DEC-*` veya ADR kimliği uydurmak;
- CI/workflow/container/deployment/production activation değiştirmek;
- bütün bağımlılıklar ve contract kapanmadan ERP-000K'yi `ready/done` yapmak.

## 22. Current-main rebuild ve merge sınırı

Bu artifact `main@6aa4a46a964ed2dae427e83026bb449449bd09df` üzerinde eski PR commitleri
rebase/cherry-pick edilmeden yeniden üretilmiştir.

Exact allowlist:

1. `docs/backlog/task_manifest.json` — yalnız ERP-000K objesi.
2. `docs/features/ERP-000K_SHELL_CONTRACT_v1.md` — bu tek fiziksel artifact.
3. `docs/delivery/AUTONOMOUS_PROGRESS.md` — append-only current cycle.

Manifest root ve K dışındaki 14 task objesi current-main ile exact kalmalıdır. Birleşik
graph 15 task/44 dependency edge ve döngüsüz olmalıdır. ERP-000E=`done`,
ERP-000M/O/N/L=`blocked` gerçeği korunur.

Eski PR #18 head'i `4f9a91c0a33acf910aee84dabc6dea8794eccf80` ve CI
`33134544768` tarihsel kanıttır; kabul kanıtı olarak kullanılamaz.

Bu PR shared task manifest, route/capability ve CompanyContext security contract'ına
dokunduğundan Draft ve exact-SHA bağımsız review kapısında kalır. Fresh standard CI,
gerçek SQL Server job'ı ve UI golden başarılı olmadan merge edilmez. Head veya base
değişirse eski review/CI stale olur.

Readiness artifact'inin merge'i:

- `contract_status: OPEN` değerini değiştirmez;
- ERP-000K implementation başlatmaz;
- Cari route mount'u etkinleştirmez;
- D-01 veya acceptance statüsünü yükseltmez;
- production activation izni vermez.

## 23. Nihai durum

- Fiziksel readiness/contract artifact: **ACCEPTABLE**
- Contract: **OPEN**
- ERP-000K task: **BLOCKED**
- React/UI implementation: **NO-GO**
- Production activation: **NO-GO**
- D-01: **yapısal referans + açık delta; katalog statüsü değişmedi**
- Search/AI/notification/Cari: **owner contract ve server capability olmadan disabled**
