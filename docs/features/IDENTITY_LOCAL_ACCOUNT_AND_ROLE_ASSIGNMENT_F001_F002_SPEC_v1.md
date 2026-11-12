# FEATURE — Yerel hesapla giriş (F-002) ve iki kişi kurallı rol atama (F-001)

- Tarih: `2026-11-12`
- Durum: **taslak spec; kod yok, grant yok, production aktivasyonu yok**
- Kaynak karar: Sinan, Issue #66 yorum `6818566934` (2026-11-12) — F-001 ve F-002/OPEN-013
- Önceki ilgili kararlar: `SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` BS-6 (F-001) ve BS-7
  (OPEN-013 yönü); `SINAN_KARARLARI_2026-08-31.md` §2 (OPEN-013 → GECICI_VARSAYIM); ADR-004
- Not: Bu spec `OPEN-013`'ü veya başka bir `OPEN-*` kaydını **kapatmaz**; hiçbir role, seed'e veya
  gerçek kullanıcıya grant vermez; production aktivasyonu değildir. 2026-11-12 kararları (1. ve 8. tur,
  #66 `6818566934`, `6820046672`) aynı PR'daki
  `docs/decisions/SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md` dosyasına `KIM-D1`…`KIM-D6` ve `KIM-D9` olarak yazıldı (10. tur `6820237902` dahil).

## Amaç

1. **F-002 (giriş):** Şimdilik ERP'nin kendi yerel hesabıyla (kullanıcı adı + parola) giriş; güçlü
   parola politikası ve isteğe bağlı iki adımlı doğrulama (2FA). Microsoft/Google gibi OIDC
   sağlayıcıları daha sonra **bir ayar olarak** eklenebilir olmalıdır (esnek yapı).
2. **F-001 (rol atama):** Admin rolleri bir ekrandan atar. Critical roller (finans onayı, yetki
   yönetimi) için **ikinci bir kişinin onayı zorunludur** (maker-checker). İlk Admin kurulumda
   **bir kez** tanımlanır. Her atama ve her ret audit log'a yazılır.
3. Yerel `admin/admin` hesabı yalnız Sinan'ın kendi bilgisayarında, geçici olarak vardır. Bu hesap
   **hiçbir zaman** repoya, seed verisine veya örnek dosyalara (`.env.example` vb.) girmez; ilk girişte
   parola değişikliği zorunludur. Bu spec bunun nasıl garanti edildiğini tanımlar (§"Varsayılan kimlik
   bilgisi yasağı").

## Kapsam

- Yerel kimlik bilgisi deposu (Identity modülü sahipliğinde), parola hash'leme, giriş/çıkış endpoint'i,
  oturum, hesap kilitleme, zorunlu parola değişikliği.
- Tek seferlik kurucu Admin bootstrap komutu (operatörün girdiği parola ile; varsayılan yok).
- Rol atama/geri alma API'si: Critical olmayan rol tek adım; Critical rol talep → ikinci kişi onayı.
- "Kullanıcılar ve yetkiler" ekranına atama/onay eylemleri ve giriş/parola değiştirme ekranları.
- İsteğe bağlı TOTP 2FA.
- OIDC sağlayıcı soyutlaması (varsayılan kapalı; ayarla açılır).
- Başarısız giriş / kilitleme olaylarının güvenlik olay kanalına yazılması (ADR-SEC-001).
- Varsayılan kimlik bilgisi yasağını zorlayan guard testleri.

## Kapsam dışı

- `OPEN-013` kapatma, production kimlik sağlayıcısı seçimi, VPN/dış erişim kurulumu (BS-7, insan kapısı).
- Hangi gerçek kişiye hangi rolün verileceği; `ErpPermissions.Critical` kümesinin genişletilmesi
  (grant/rol/Critical insan kapısıdır).
- Rol tanımı oluşturma/düzenleme (roller bugün `Seed/identity-seed.json` + `SystemRolePermissionReconciler`
  ile yönetilir); bu spec yalnız **mevcut** rolleri atar.
- Self-servis "parolamı unuttum" e-posta akışı (e-posta altyapısı kararı yok; Admin sıfırlaması kapsamda).
- Production'da ilk Admin'in **kim** olduğu ve bootstrap'ın production'da çalıştırılması (insan kapısı;
  `docs/operations/CANLI_ORTAM_KURULUM_REHBERI_v1.md` §4.5, E3).
- Güvenlik olay kanalındaki başarısız giriş kullanıcı adı hash anahtarı (ADR-SEC-001 NEEDS-SINAN-8, E8).

## Karar ve açık politika ID'leri

| ID | Durum | Bu spec'teki etkisi |
|---|---|---|
| Issue #66 `6818566934` (2026-11-12) | Son kullanıcı kararı | F-001 mekanizması ve F-002 "şimdilik yerel hesap" yönü bağlayıcıdır. |
| BS-6 (2026-11-11) | Kilitli tasarım yönü | İki kişi kuralı + tek seferlik kurucu admin; grant/Critical insan kapısı. |
| BS-7 (2026-11-11) | Yön; `OPEN-013` açık | Production yönü "kurumsal hesap OIDC + **MFA zorunlu** + VPN". 2026-11-12 kararı "şimdilik yerel + **isteğe bağlı** 2FA" der; çelişki 8. turda (KIM-D3) 12 Kasım kararı lehine çözüldü. |
| #66 `6820046672` (2026-11-12, 8. tur) | Kilitli | KIM-D3…KIM-D6: 2FA herkese isteğe bağlı; başlangıç parola/kilit/oturum değerleri Ayarlar'dan değişir; kurucu Admin Critical atamayı ikinci onaysız yapabilir, Critical liste seçilmedi; admin/admin yalnız Sinan'ın bilgisayarındaki Development ortamında. Karar dosyası: `docs/decisions/SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md`. |
| `OPEN-013` | KISMEN, açık kalır | Yerel hesap = geçici yol; OIDC soyutlaması korunur (ADR-004). |
| `DEC-TEC-008` | Kilitli | Dev/test/staging/prod ayrı; weak-password istisnası yalnız Development. |
| ADR-004 §1–§6 | Uygulandı | Kimlik doğrulama ≠ yetkilendirme; `(issuer, subject)` → yerel kullanıcı; Critical talepte talep eden/hedef onaylayamaz. |
| ADR-SEC-001 | PR #1093 açık | `auth.login.failed/succeeded`, `identity.permission.changed` olayları; E8 insan kapısı. |
| NEEDS-SINAN #66 `5738113623` | Açık blok | Atama yazma rotası bugün bilerek yok (`IdentityEndpoints.MapAccessView` yorumu); 2026-11-12 kararı mekanizmayı seçer, ama gerçek grant yine insan kapısıdır. |

## Kullanıcı rolleri ve CompanyId kapsamı

**Bugünkü durum (kod):**

- `UserAccount` (`src/backend/Modules/Identity/GoymenERP.Modules.Identity.Domain/Users/UserAccount.cs`)
  **grup genelidir** — `CompanyId` taşımaz; parola/kimlik bilgisi alanı yoktur.
- `ExternalIdentityLink` `(Issuer, Subject)` → `UserId` eşlemesidir; `ErpClaimsTransformation`
  (`src/backend/GoymenERP.Api/Security/ErpClaimsTransformation.cs`) her istekte bu çiftle yerel kullanıcıyı
  çözer ve token claim'lerine güvenmez.
- `RoleAssignment` `CompanyId` + `RoleAssignmentScope` (Company/Site/Location/WorkCenter) taşır;
  `ApprovedByUserId` alanı zaten vardır.
- Kimlik modu `ErpAuthenticationOptions.Mode` ile seçilir: `DevelopmentBootstrap` (yalnız
  Development/Testing; `X-Development-User` başlığı, `DevelopmentAuthenticationHandler`) veya
  `OidcJwtBearer` (production varsayılanı). Yerel parola yolu **yoktur**.

**Hedef kapsam:**

- Yerel kimlik bilgisi kullanıcıya bağlıdır (grup geneli). Rol ataması şirket kapsamlıdır.
- Atama yapan, atamanın `CompanyId`'sinde ilgili izni **şirket genelinde** taşımalıdır; checker da aynı
  şirkette `identity.access.approve` taşımalıdır. Şirket sunucu tarafında çözülür (`RequireCompanyContext`).
- Parola sıfırlama/kilit açma/hesap pasifleştirme grup geneli etkiler (kullanıcı birden çok şirkette
  çalışabilir). `identity.users.manage` Critical değildir (KIM-D9, 10. tur); bu eylemin hangi şirket kapsamıyla
  yetkilendirileceği → `NEEDS-SINAN-ID-07b`.

**Persona (öneri, grant değildir):** Kurucu Admin (bootstrap), Admin (maker), İkinci onaylayıcı (checker),
sıradan kullanıcı. Persona → rol eşlemesi `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` gibi insan
kapısıdır.

## Domain nesneleri ve sahip modül

Sahip modül: **Identity** (`identity` şeması). Başka modül bu tablolara yazmaz.

| Nesne | Durum | Not |
|---|---|---|
| `UserAccount` | Var | `IsActive` mevcut. Kullanıcı adı yerel kimlik bilgisinde tutulur (aşağıda). |
| `LocalCredential` (yeni, öneri) | Yok | `UserId` (PK, FK → `Users`), `UserName`, `NormalizedUserName` (unique), `PasswordHash`, `HashFormatVersion`, `MustChangePassword`, `PasswordChangedAtUtc`, `FailedAccessCount`, `LockoutEndUtc`, `TwoFactorEnabled`, `Version` (concurrency token). |
| `LocalTwoFactorSecret` (yeni, ID-5) | Yok | Şifreli TOTP sırrı (ASP.NET Core Data Protection ile korunur; anahtar secret'tır, repoda yok) + tek kullanımlık kurtarma kodlarının hash'leri. |
| `ExternalIdentityLink` | Var | Yerel hesap da bir `(issuer, subject)` satırı alır: issuer öneri `urn:goymen:local-account`, subject = `UserId`. Böylece `ErpClaimsTransformation` ve `IExternalIdentityResolver` değişmeden kalır; OIDC (ID-6) aynı tabloya ikinci satır ekler. |
| `IdentityBootstrapState` (yeni, ID-2) | Yok | Tek satırlı işaret: `Id` sabit tekil anahtar + check constraint, `CompletedAtUtc`, `FounderUserId`. Bootstrap'ın ikinci kez çalışmasını DB düzeyinde engeller. |
| `RoleAssignment` | Var | Critical olmayan atama doğrudan; Critical atama yalnız onaylanmış `AccessGrantRequest`'ten doğar ve `ApprovedByUserId` dolu olur. |
| `AccessGrantRequest` | Var | `RequiresSecondApproval`, `Approve` içinde talep eden/hedef ≠ onaylayan kuralı ve `CK_AccessGrantRequests_SecondApproval` mevcut. Eksik: geçerlilik aralığı, kapsam listesi, geri çekme durumu, gerekçe (ID-3 migration'ı). |
| `RoleDefinition` | Var | "Critical rol" tanımı: rol izinlerinden en az biri `ErpPermissions.IsCritical(...)` ise. Sunucu talep anında hesaplar; istemci bayrağına güvenilmez. |

Bugünkü `ErpPermissions.Critical`
(`src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs`):
`identity.access.approve`, `identity.roles.manage`, `identity.audit.read`, `identity.assignments.read`,
`finance.bank-account.propose`, `finance.bank-account.approve`,
`counterparty.parties.tax-identity.approve`. Sinan'ın "finans onayı" ifadesi
`finance.official-invoice-import.approve`, `finance.manual-receipt.*` gibi şu an Critical olmayan adları da
kapsıyor olabilir. **Karar (KIM-D9, 10. tur, #66 `6820237902`; Sinan: "Bilemiyorum ama bence şuanda hiçbiri"):**
şimdilik **yeni işlem Critical'e eklenmez** — ödeme/tahsilat onayı, `identity.users.manage`, resmî fatura içe
aktarma ve manuel makbuz Critical değildir. Yukarıdaki mevcut Critical sınıflandırmalar kaldırılmaz (ayrı karar
ister). Liste ileride karar ile genişletilebilir; `ErpPermissions.Critical` kümesine ekleme insan kapısıdır.

## Durum geçişleri

**Yerel hesap:**

```
Oluşturuldu(MustChangePassword=true) → [ilk giriş] → ParolaDeğiştirmeZorunlu
  → [politikaya uygun yeni parola] → Etkin
Etkin → [N ardışık başarısız deneme] → Kilitli(LockoutEndUtc) → [süre doldu | Admin kilit açtı] → Etkin
Etkin → [Admin sıfırladı] → Oluşturuldu(MustChangePassword=true)
Herhangi → [UserAccount.IsActive=false] → Pasif (giriş reddedilir; silme yok)
```

`MustChangePassword=true` iken oturum yalnız `POST /api/v1/identity/session/password` ve çıkışa izin verir;
diğer her endpoint `403` + problem kodu `identity.password_change_required` döner (öneri).

**Kurucu bootstrap:** `Yok → Tamamlandı` (tek yön; geri alınamaz, yeniden çalıştırma reddedilir).

**Critical rol talebi** (`AccessGrantRequestStatus` bugün `Pending/Approved/Rejected`):

```
Pending → Approved (checker ≠ requester ∧ checker ≠ target) → RoleAssignment(Active, ApprovedByUserId=checker)
Pending → Rejected (checker, gerekçe kodu ile)
Pending → Withdrawn (yalnız requester; öneri — yeni enum değeri, ID-3)
```

**Atama:** `RoleAssignmentStatus` `Active → Revoked | Expired` (mevcut). Critical rolün geri alınmasının
da iki kişi isteyip istemediği → `NEEDS-SINAN-ID-05` (öneri: tek adım + audit; yetki azaltır).

## Birim/formül/yuvarlama/tolerans

Para/stok/formül yoktur. Yalnız güvenlik politika parametreleri vardır; **hiçbiri kodda sabit değildir**.
Hepsi `Identity:LocalAccount` ayar bölümünden (öneri ad; Options pattern, startup'ta doğrulanır, fail-closed)
okunur. Değer seçimi güvenlik politikasıdır ve insan kararıdır:

| Parametre | Ayar anahtarı (öneri) | Öneri (yalnız referans, seçim değil) | Karar |
|---|---|---|---|
| Asgari parola uzunluğu | `PasswordPolicy:MinimumLength` | **Başlangıç 8** (KIM-D4; Ayarlar'dan değişir). Azami ≥ 64 kabul edilmeli (NIST SP 800-63B) | Karar verildi |
| Karakter sınıfı kuralı | `PasswordPolicy:RequireCharacterClasses` | NIST kompozisyon kuralı önermez; Sinan "güçlü parola" dedi | `NEEDS-SINAN-ID-02b` |
| Yasaklı/sık parola listesi | `PasswordPolicy:BlocklistEnabled` | NIST: zorunlu karşılaştırma; liste `admin`, kullanıcı adı, şirket adı türevlerini içerir | Açık olması önerilir |
| Periyodik zorunlu değişim | `PasswordPolicy:MaximumAgeDays` | NIST önermez (yalnız ihlal şüphesinde) | `NEEDS-SINAN-ID-02b` |
| Kilit eşiği | `Lockout:MaxFailedAttempts` | **Başlangıç 10** ardışık hata (KIM-D4; Ayarlar'dan değişir) | Karar verildi |
| Kilit süresi | `Lockout:Duration` | ASP.NET Core Identity varsayılanı 5 dk | `NEEDS-SINAN-ID-03b` |
| Oturum boşta kalma / mutlak süre | `Session:IdleTimeout`, `Session:AbsoluteLifetime` | Boşta **başlangıç 8 saat** (KIM-D4; Ayarlar'dan değişir); mutlak süre seçilmedi | Boşta: karar verildi; mutlak süre `NEEDS-SINAN-ID-06` |
| Hash algoritması ve maliyet | `PasswordHashing:Algorithm`, `:Pbkdf2Iterations` | Öneri A: ASP.NET Core Identity `PasswordHasher<T>` (PBKDF2-HMAC-SHA512, V3 biçim, sürümlü hash, yeniden hash desteği); OWASP önerisi PBKDF2-SHA512 ≥ 210 000 iterasyon. Öneri B: Argon2id (OWASP: m=19 MiB, t=2, p=1) — üçüncü taraf paket ister (tedarik zinciri incelemesi). **Öneri: A** (framework içi, yeni bağımlılık minimal). | Maliyet `NEEDS-SINAN-ID-02b` |
| 2FA | `TwoFactor:Methods`, `TwoFactor:Required` | **Herkese isteğe bağlı**, kimseye zorunlu değil (KIM-D3; pilot sonrası yeniden bakılır). Yöntem önerisi TOTP (RFC 6238) + kurtarma kodları; SMS önerilmez | Zorunluluk: karar verildi; yöntem uygulama ayrıntısı (ID-5 review'unda) |

Hash sürümü satırda tutulur (`HashFormatVersion`); parametre artırıldığında başarılı girişte yeniden hash'lenir
(`PasswordVerificationResult.SuccessRehashNeeded`). Parola karşılaştırması sabit zamanlıdır (hasher içi).

## Yetki/onay/görev ayrılığı

**İzin adları** (kayıt `permission-capability-registration` skill'ine göre; kayıt grant değildir):

| Eylem | İzin | Durum |
|---|---|---|
| Atamaları görme | `identity.assignments.read` | Var, Critical, şirket geneli |
| Critical olmayan rol atama / geri alma, Critical talep açma (maker) | `identity.roles.manage` | Var, Critical |
| Critical talebi onaylama/reddetme (checker) | `identity.access.approve` | Var, Critical |
| Yerel kullanıcı oluşturma, parola sıfırlama, kilit açma, pasifleştirme | `identity.users.manage` | **Yeni ad (öneri)**; Critical **değil** (KIM-D9); kapsam `NEEDS-SINAN-ID-07b` |

`identity.access.request` mevcut adının bu akışta rolü (ör. kullanıcının kendisi için talep) bu spec'te
kullanılmaz; kullanılacaksa ayrı karar.

**Görev ayrılığı kuralları (F-001 kararı + ADR-004 §6 + `docs/security/GOREV_AYRILIGI_ONAY.md`):**

1. Critical rol: checker ≠ requester **ve** checker ≠ target. Domain (`AccessGrantRequest.Approve`) +
   SQL `CK_AccessGrantRequests_SecondApproval` iki katmanda korur. Kimlikler `uniqueidentifier` olduğundan
   karşılaştırma doğrudandır; Purchasing'deki `COLLATE Latin1_General_100_BIN2` deseni (#1083,
   `CK_PurchaseOrders_Approval`) burada string aktör kimliği olmadığı için gerekmez, ama yeni bir string
   aktör sütunu eklenirse aynı ikili collation kuralı uygulanır.
2. Critical rol hiçbir yoldan tek adımda `RoleAssignment` olamaz: doğrudan atama endpoint'i Critical rolü
   `409` + `identity.assignment.second_approval_required` ile reddeder (öneri kod). SQL tarafında, Critical
   bilgisi rol izinlerinden türediği için check constraint ile ifade edilemez; bunun yerine store yalnız
   onaylı talepten `ApprovedByUserId` dolu satır yazar ve gerçek SQL testi doğrudan yazma yolunun
   olmadığını kanıtlar.
3. Critical olmayan rol: tek adım, aynı audit.
4. Kendine Critical rol talebi açılabilir; checker yine üçüncü kişi olmalıdır.
5. **Kurucu istisnası (bootstrap):** Bootstrap komutu tek kez, `System` aktörü + operatör beyanıyla
   `SYSTEM_ADMINISTRATOR` atamasını yapar (bu rol `identity.roles.manage` ve `identity.access.approve`
   taşır, yani Critical'dir; BS-6 "tek seferlik kurucu admin").
6. **Kurucu Admin istisnası (KIM-D5, 8. tur: "Kurulumcu admin her zaman yapabilsin"):** Bootstrap'ta
   oluşturulan kurucu Admin, Critical rol atamasını **ikinci kişi onayı olmadan, her zaman** yapabilir. Diğer
   her atayan için ikinci kişi onayı sürer. Uygulama: kurucu kimliği `IdentityBootstrapState.FounderUserId`
   ile sunucuda belirlenir (istemci beyanı değil); kurucunun doğrudan Critical ataması `Approve` yolunu
   atlamaz, `AccessGrantRequest` satırı `ApprovalMode = FounderDirect` (öneri ad) ile tek kişi olarak kapanır
   ve audit'te ayrıca işaretlenir; check constraint "ikinci onaylayıcı dolu **veya** FounderDirect ve
   requester = kurucu" biçimine genişler. Böylece tavuk-yumurta sorunu (tek Admin varken ilk checker'ın
   doğması) da çözülür; eski `NEEDS-SINAN-ID-08` kapandı. Kurucu Admin pasifleştirilirse istisna onunla
   biter (devredilmez; devretme ayrı karar).

## Audit, idempotency ve ters işlem

Her yazma komutu `k3-command-skeleton` iskeletini izler: izin çiti → expected version → `Idempotency-Key`
replay → korumalı okumalar → tek `SaveChanges` (deneme + audit aynı transaction) → terminal ret kaydı.

- **Audit (ERP-000D, `IAuditEventCollector.Enqueue`):** rol atama, Critical talep açma, onay, ret, geri
  çekme, geri alma, kullanıcı oluşturma, parola sıfırlama, kilit açma, bootstrap. Ret de (SoD ihlali,
  yetkisiz, şirket dışı) audit'e yazılır. Audit'e **asla** parola, hash, TOTP sırrı veya kurtarma kodu
  yazılmaz.
- **Güvenlik olay kanalı (ADR-SEC-001, PR #1093, `ISecurityEventWriterV1`):** `auth.login.succeeded`,
  `auth.login.failed` (`credentials_invalid`, `account_inactive`), `identity.permission.changed`.
  Kilitleme için kapalı listede kod yoktur; `account_locked` (ve 2FA için `second_factor_invalid`) eklemek
  ADR-SEC-001 sözleşme değişikliğidir (liste + check constraint aynı PR) → ID-1 ile birlikte ayrı K3 madde.
  E8 kararı gelene kadar başarısız girişte kullanıcı adı hash'i **yazılmaz**; aktör `Anonymous`.
  `auth.login.*` fail-open + alarmdır (ADR-SEC-001 tablosu): kanal yazılamazsa giriş kararı değişmez.
- **Idempotency:** atama/talep/onay komutları `Idempotency-Key` ister. Giriş endpoint'i idempotent değildir
  (her deneme sayılır); idempotency anahtarı kabul etmez.
- **Concurrency:** `LocalCredential.Version`, `AccessGrantRequest.Version`, `RoleAssignment.Version`
  concurrency token; çakışma `409`.
- **Ters işlem:** silme yok. Atama → `Revoked`; talep → `Rejected/Withdrawn`; hesap → `IsActive=false`.

## API/event/AI tool sözleşmesi

Tüm yollar `/api/v1/identity` altında (mevcut `IdentityEndpoints.cs` grubu). Hepsi `NoStore`, problem
kodları sürümlü; OpenAPI `openapi-regenerate` ile yeniden üretilir. Adlar öneridir:

| Yöntem | Yol | İzin | Not |
|---|---|---|---|
| POST | `/session/login` | anonim | `{userName, password, totpCode?}`; başarısızlıkta tek tip `401` (kullanıcı var/yok ayrımı sızdırılmaz); kilitliyken de aynı yanıt + olay. Hız sınırı (IP + hesap) zorunlu. |
| POST | `/session/logout` | oturum | |
| POST | `/session/password` | oturum | Mevcut parola + yeni parola; politika ihlali `400` + alan kodları. |
| GET | `/me` | oturum | Mevcut; `mustChangePassword` alanı eklenir (sözleşme sürüm artışı). |
| POST | `/access/users` | `identity.users.manage` | Yerel kullanıcı oluştur; geçici parolayı Admin girer, hesap `MustChangePassword=true`. |
| POST | `/access/users/{userId}/password-reset` | `identity.users.manage` | |
| POST | `/access/users/{userId}/unlock` | `identity.users.manage` | |
| POST | `/access/assignments` | `identity.roles.manage` | Critical olmayan rol; Critical rolde `409 second_approval_required`. |
| POST | `/access/assignments/{id}/revoke` | `identity.roles.manage` | `NEEDS-SINAN-ID-05` (8. turda sorulmadı). |
| POST | `/access/grant-requests` | `identity.roles.manage` | Critical rol talebi. |
| POST | `/access/grant-requests/{id}/approve` · `/reject` · `/withdraw` | approve/reject: `identity.access.approve`; withdraw: requester | |
| GET | `/access/grant-requests?status=pending` | `identity.access.approve` veya `identity.assignments.read` | |

**Oturum mekanizması (öneri, geri alınabilir teknik ayar → `ASSUMPTION-*` adayı):** sunucu tarafı oturum +
`HttpOnly`, `Secure`, `SameSite=Strict` çerez ve antiforgery; ya da yerel olarak imzalanmış kısa ömürlü
token. Her iki durumda kimlik doğrulama `(urn:goymen:local-account, UserId)` claim çiftini üretir ve mevcut
`ErpClaimsTransformation` yetkiyi DB'den çözer (ADR-004 §1/§3). İmza/Data Protection anahtarları secret'tır.

**Kimlik modu ayarı:** `AuthenticationModes`'a `LocalAccount` eklenir. ID-6 ile `Authentication:Providers`
listesi (örn. `LocalAccount` + `OidcJwtBearer`) birden çok şemayı açar; OIDC varsayılan **kapalı**,
sağlayıcıya özel kod/tenant sabiti yok (ADR-004). `DevelopmentBootstrap` yalnız Development/Testing olarak
kalır (mevcut startup kontrolü).

**Event:** Dış outbox olayı yok (ilk dilimlerde). **AI tool:** yok; AI hiçbir kimlik bilgisi veya atama
aracına erişmez (`docs/security/AI_YETKI_SOZLESMESI.md`).

**Kurucu bootstrap komutu (ID-2):** API'nin HTTP yüzeyi değil, operatörün sunucuda çalıştırdığı tek seferlik
komut (öneri: `GoymenERP.Api identity bootstrap-founder --company <kod>`; ayrı CLI projesi de olabilir).

- Parola **etkileşimli** ve yankısız okunur; komut satırı argümanı, ortam değişkeni veya dosya ile parola
  kabul etmez (kabuk geçmişine/loglara düşmesin).
- Parola, ortamın politikasından geçmek zorundadır; hesap `MustChangePassword=true` oluşur.
- `IdentityBootstrapState` satırı varsa veya herhangi bir şirkette etkin `SYSTEM_ADMINISTRATOR` ataması
  varsa reddeder; tek transaction içinde kullanıcı + kimlik bilgisi + `ExternalIdentityLink` + atama +
  işaret + audit yazar.
- Production'da çalıştırılması insan kapısıdır.

## UI durumları ve erişilebilirlik

Konum (Claude şeridi, `src/frontend/goymen-erp-web/src`):

- **Atama ekranı:** mevcut `identity/IdentityAccessPage.tsx` / `IdentityRoleDetailPage.tsx`,
  rota `/ayarlar/kullanicilar-ve-yetkiler` (`identity/identityRoutes.tsx`, `identity/capabilities.ts`). Bugün
  salt okunur; atama, talep, onay/ret eylemleri buraya eklenir; yeni kontrol capability'leri sunucu shell
  provider'ından (`IdentityShellCapabilityProvider.cs`) gelir.
- **Giriş ve zorunlu parola değişikliği:** kimlik doğrulama öncesi ekranlar; shell'in auth sınırı
  `shell/authBoundary.ts` / `useAuthBoundary.ts` ile bağlanır. Bugün `identity/api.ts` yalnız
  `DEV_IDENTITY_ENABLED` iken `X-Development-User` gönderir; yerel modda bu başlık gönderilmez.

Durumlar (`frontend-screen-states`): yükleniyor, boş, yetkisiz, yasak, bulunamadı, çakışma (`409` sürüm ve
`second_approval_required` ayrı metin), ağ hatası, 503, bozuk yanıt, bayat. Critical rol seçildiğinde ekran
"Bu rol ikinci bir kişinin onayını gerektirir" uyarısını gösterir ve düğme "Onaya gönder" olur — ancak karar
sunucudadır, UI tek güvenlik değildir. Checker, kendi açtığı veya kendisini hedefleyen talepte onay düğmesini
görmez **ve** sunucu yine reddeder.

Giriş formu: etiketli alanlar, parola göster/gizle, yapıştırmaya izin (NIST), `autocomplete="username"` /
`"current-password"` / `"new-password"`; hata mesajı tek tiptir ("Kullanıcı adı veya parola hatalı"); kilit
mesajı hesabın varlığını sızdırmayacak biçimde nötr. Tüm metinler `identity/identityText.ts` benzeri Türkçe
kaynak dosyasında; klavye ile tam erişim, odak yönetimi, `aria-live` hata bölgesi.

## Migration/rollback

- **ID-1:** `identity.LocalCredentials` tablosu (unique `NormalizedUserName`, FK `Users`). Down: kimlik
  bilgisi satırı varken `THROW 51xxx` ile reddeder (forward-fix; `ef-migration-forward-fix` skill'i).
- **ID-2:** `identity.IdentityBootstrapState` (tekil satır check constraint). Down: satır varken reddeder.
- **ID-3:** `AccessGrantRequests`'e geçerlilik/kapsam/gerekçe/`Withdrawn`; `CK_AccessGrantRequests_DecisionState`
  genişler. Down: yeni durumda veya dolu yeni sütunda veri varken reddeder.
- **ID-5:** `identity.LocalTwoFactorSecrets`. Down: kayıt varken reddeder.
- Her migration recovery envanteri, architecture guard ve relational inventory'yi günceller; `SCOPE`
  kapsamındadır (AGENTS.md main tazelik kontrolü).
- Seed: `Seed/identity-seed.json`'a **hiçbir** yerel kimlik bilgisi eklenmez. Mevcut geliştirme aktörleri
  (`urn:goymen:development-bootstrap`) değişmez ve canlıda bulunmaz (kurulum rehberi kontrol listesi).

## Varsayılan kimlik bilgisi yasağı (admin/admin garantisi)

1. **Repoda varsayılan yok:** kod, `appsettings*.json`, `Seed/*.json`, `.env.example`, `infra/**` compose
   dosyaları, test fixture'ları ve dokümanlarda kullanılabilir kullanıcı adı + parola çifti bulunmaz. Testler
   parolayı çalışma anında rastgele üretir.
2. **Bootstrap operatör girdisi ister:** varsayılan parola yoktur; boş/argümanla verilen parola reddedilir.
3. **Yasaklı/sızmış parola listesi:** `admin`, `password`, kullanıcı adının kendisi ve sızmış parolalar politika
   tarafından reddedilir (KIM-D4 korur). Tek istisna madde 4'teki **yalnız Development + yerel ayar** durumudur;
   test (`Testing`/Staging) ve canlı (Production) ortamda liste kapatılamaz ve `admin/admin` hiç kabul edilmez.
4. **Sinan'ın yerel geçici hesabı (KIM-D6, 8. tur):** `admin/admin` **yalnız Sinan'ın bilgisayarındaki
   Development ortamında** kabul edilir. Bunu açan ayar (`Identity:LocalAccount:DevelopmentAllowWeakPassword=true`,
   öneri ad) repoya, `appsettings*.json`'a, seed'e veya örnek dosyaya yazılmaz; yalnız Sinan'ın makinesindeki
   kullanıcı-gizli dosyasında (`dotnet user-secrets`) veya yerel ortam değişkeninde durur. Ayar Development
   dışında `true` ise uygulama **başlamaz** (mevcut `DevelopmentBootstrap` startup kontrolünün deseni). Hesabı
   Sinan kendi makinesinde bootstrap komutuyla kendisi oluşturur ve parolayı kendisi girer. İstisna açıkken
   ilk girişteki zorunlu değişim de bu yerel istisnaya tabidir (Development'ta `admin` yeniden kabul
   edilebilir). Karar gereği **başka kullanıcı bağlanmadan önce** parola değiştirilir; öneri uygulama: zayıf
   parolayla kabul edilmiş hesap `WeakPasswordAccepted=true` işaretlenir ve bu işaret dururken ikinci bir yerel
   kullanıcı oluşturma ve başka bir kullanıcıya giriş reddedilir (`identity.weak_founder_password_must_change`,
   öneri kod). Eski `NEEDS-SINAN-ID-09` kapandı.
5. **Guard testleri (ID-1/ID-2 ile birlikte):**
   - Repo taraması (öneri `tests/default_credential_guard/`): kod, seed, `appsettings*.json`, `.env.example`,
     `infra/**` compose ve test fixture dosyalarında `admin`/`admin` vb. bilinen varsayılan çiftler ve
     `Password`/`PasswordHash` alanlı yerel kimlik bilgisi satırları bulunursa kırmızı. Bu spec gibi yasağı
     yalnız **adıyla anan** dokümanlar (`docs/**`) kimlik bilgisi değildir; taramanın doküman istisnası dar
     tutulur (yalnız `docs/**/*.md`, kullanılabilir yapılandırma biçiminde değil) ve istisna listesi review'da
     görünür.
   - Integration: `Production`/`Staging`/`Testing` ortamında `DevelopmentAllowWeakPassword=true` ile startup
     hatası; bu ortamlarda `admin` parolası hem bootstrap'ta hem zorunlu değişimde reddedilir; bootstrap ikinci
     kez reddedilir; Development'ta `WeakPasswordAccepted` dururken ikinci kullanıcı oluşturma reddedilir.
   - Integration: boş veritabanında hiçbir giriş başarılı olmaz (bootstrap öncesi kullanıcı yok).

## Kabul senaryoları

**KS-1 — Critical rolde maker ≠ checker**
Given Ayşe ve Mehmet aynı şirkette `identity.roles.manage` taşıyor, Mehmet ayrıca `identity.access.approve`
taşıyor ve "Finans Onaycısı" rolü Critical bir izin içeriyor
When Ayşe, Can için bu rolü talep eder ve Ayşe talebi kendisi onaylamaya çalışır
Then sunucu SoD problem koduyla (öneri `identity.grant_request.self_approval_refused`) reddeder, ret audit'e yazılır, atama oluşmaz;
And Mehmet onayladığında `RoleAssignment` `ApprovedByUserId=Mehmet` ile oluşur ve audit'e yazılır.

**KS-2 — Hedef kullanıcı kendi talebini onaylayamaz**
Given Can için Critical rol talebi bekliyor ve Can `identity.access.approve` taşıyor
When Can talebi onaylamaya çalışır
Then istek reddedilir (domain + `CK_AccessGrantRequests_SecondApproval`), talep `Pending` kalır.

**KS-3 — Critical olmayan rol tek adım**
Given "Organizasyon Okuma" rolü Critical izin içermiyor
When Ayşe bu rolü Can'a atar
Then atama tek adımda `Active` olur, ikinci onay istenmez, audit kaydı aktör/rol/şirket/kapsam içerir.

**KS-4 — Critical rol doğrudan atanamaz**
Given Ayşe Critical bir rolü doğrudan atama endpoint'iyle vermeye çalışır
Then `409 identity.assignment.second_approval_required` döner ve ret audit'e yazılır.

**KS-5 — Kurucu Admin yalnız bir kez**
Given boş bir kurulum
When operatör bootstrap komutunu çalıştırıp parolayı girer
Then kurucu kullanıcı `MustChangePassword=true` ile oluşur, `IdentityBootstrapState` tamamlanır, audit yazılır;
And komut ikinci kez çalıştırıldığında hiçbir şey yazmadan reddeder.

**KS-6 — admin/admin Development dışında kabul edilmez**
Given ortam `Staging` veya `Production`
When biri `admin` parolalı hesap oluşturmaya veya `DevelopmentAllowWeakPassword=true` ile başlatmaya çalışır
Then parola politika tarafından reddedilir / uygulama başlamaz; repo guard testi seed ve örnek dosyalarda
varsayılan kimlik bilgisi bulamaz.

**KS-7 — İlk girişte zorunlu parola değişikliği**
Given `MustChangePassword=true` bir hesap
When kullanıcı doğru parolayla giriş yapar ve `/api/v1/organization/...` gibi bir endpoint'i çağırır
Then `403 identity.password_change_required` alır; parolayı politikaya uygun değiştirince normal erişim açılır.

**KS-8 — Kilitleme**
Given `Lockout:MaxFailedAttempts` = yapılandırılmış N
When N ardışık hatalı parola girilir
Then hesap `LockoutEndUtc`'ye kadar kilitlenir, doğru parola da reddedilir, yanıt hesabın varlığını
sızdırmaz, her deneme güvenlik olay kanalına `auth.login.failed` olarak yazılır (kullanıcı adı yazılmaz);
And Admin kilidi açtığında audit kaydı oluşur.

**KS-9 — Ret de audit'te**
Given Mehmet bir Critical talebi reddeder
Then talep `Rejected` ve `DecidedByUserId=Mehmet` olur, ret audit'e yazılır; yetki değişmediği için
`identity.permission.changed` olayı üretilmez.

**KS-10 — Şirket kapsamı**
Given Ayşe yalnız Şirket A'da `identity.roles.manage` taşıyor
When Ayşe Şirket B'de rol atamaya veya Şirket B talebini onaylamaya çalışır
Then istek reddedilir (şirket dışı kayıt kodsuz 404, şirket bağlamı uyuşmazlığı 403), audit/olay yazılır.

**KS-11 — İsteğe bağlı 2FA (ID-5)**
Given TOTP etkin bir hesap
When doğru parola fakat hatalı TOTP girilir
Then giriş reddedilir ve başarısız deneme sayacına eklenir.

**KS-12 — OIDC kapalıyken etkisiz (ID-6)**
Given `Authentication:Providers` yalnız `LocalAccount` içeriyor
Then OIDC token'ı ile yapılan istek `401` alır; ayar açılıp sağlayıcı yapılandırılınca aynı kullanıcı
`ExternalIdentityLink` üzerinden çözülür.

## Definition of Done

- Karar referansı (#66 `6818566934`, BS-6, BS-7, ADR-004, ADR-SEC-001) PR gövdesinde.
- CompanyId ve yetki testleri (şirket dışı, kapsam dışı, izin yok, SoD).
- Gerçek SQL kanıtı: check constraint'ler, Down reddi, bootstrap tekilliği, doğrudan Critical yazma yolunun
  olmadığı.
- Audit + güvenlik olayı + idempotency replay + `409` concurrency testleri.
- Varsayılan kimlik bilgisi guard testi yeşil.
- OpenAPI yeniden üretildi; frontend decoder'ları strict.
- UI durumları ve a11y testleri (Vitest + browser).
- Log/metric/trace: giriş başarı/başarısızlık sayaçları, kilit sayısı; parola/sır asla loglanmaz.
- Açık P0/P1 yok; `NEEDS-SINAN-*` maddeleri Issue #66'ya yazıldı; hiçbir grant, seed rolü veya production
  ayarı açılmadı.

## Dilim planı

Şeritler AGENTS.md v3 §6'ya göre: **Codex** = backend/endpoint/gerçek-SQL/OpenAPI; **Claude** = frontend.
K3 dilimleri iki bağımsız exact-head review ister.

| Dilim | İçerik | Şerit | K | Bağımlılık |
|---|---|---|---|---|
| **ID-1** | `LocalCredential` + migration, `PasswordHasher` (PBKDF2, sürümlü), `LocalAccount` kimlik modu, `/session/login`·`/logout`, hız sınırı, kilitleme, `auth.login.*` olayları, ayar doğrulaması, varsayılan kimlik bilgisi guard testi | Codex | K3 (migration + güvenlik) | PR #1093 (olay kanalı) merge'ü veya geçici no-op writer; `account_locked` kodu ADR-SEC-001 sözleşme değişikliği |
| **ID-1F** | Giriş ekranı, auth sınırı entegrasyonu, yerel modda dev başlığının kapanması | Claude | K2 | ID-1 OpenAPI |
| **ID-2** | `MustChangePassword` çiti + `/session/password`, `IdentityBootstrapState` + migration, `bootstrap-founder` komutu (etkileşimli parola), `/me` sözleşme sürümü | Codex | K3 | ID-1; `NEEDS-SINAN-ID-08` (ikinci onaylayıcı) |
| **ID-2F** | Zorunlu parola değiştirme ekranı | Claude | K2 | ID-2 |
| **ID-3** | Atama/geri alma, Critical talep/onay/ret/geri çekme API'si, `AccessGrantRequest` migration'ı, `identity.users.manage` kaydı (grant yok), shell capability'leri, audit + `identity.permission.changed` | Codex | K3 (permission/rol + migration) | ID-1; `NEEDS-SINAN-ID-04/05/07` |
| **ID-4** | "Kullanıcılar ve yetkiler" ekranına atama, onay kuyruğu, kullanıcı oluştur/sıfırla/kilit aç eylemleri | Claude | K2 | ID-3 |
| **ID-5** | İsteğe bağlı TOTP 2FA + kurtarma kodları + migration; UI kayıt akışı ayrı Claude alt dilimi | Codex (+Claude) | K3 | ID-1; zorunluluk KIM-D3 ile kararlı (isteğe bağlı) |
| **ID-6** | `Authentication:Providers` çoklu şema; OIDC varsayılan kapalı; yerel kullanıcıya dış kimlik bağlama (Admin eylemi) | Codex | K2 (migration yoksa; güvenlik yüzeyi nedeniyle iki review önerilir) | ID-1; production sağlayıcısı `OPEN-013` insan kapısı |

## NEEDS-SINAN listesi

8. tur (#66 `6820046672`) ile kapananlar: ID-01 (2FA herkese isteğe bağlı), ID-02/03/06'nın başlangıç
değerleri (8 karakter, 10 hata, boşta 8 saat; Ayarlar'dan değişir), ID-08 (kurucu Admin istisnası), ID-09
(admin/admin yalnız Sinan'ın Development ortamında). 10. tur (#66 `6820237902`) ile kapananlar: ID-04 ve ID-07'nin
Critical sorusu (yeni işlem Critical'e eklenmez; `identity.users.manage` Critical değil). Açık kalanlar (5–10.
turlarda sorulmadı):

- `NEEDS-SINAN-ID-02b`: Karakter sınıfı kuralı, periyodik değişim ve hash maliyeti (öneri: sınıf kuralı yok,
  periyodik değişim yok, PBKDF2 framework varsayılanı).
- `NEEDS-SINAN-ID-03b`: Kilit süresi ve kilidi kim açar (öneri: 15 dk otomatik + `identity.users.manage`).
- `NEEDS-SINAN-ID-05`: Critical rolün geri alınması da iki kişi ister mi? (8. turda sorulmadı.)
- `NEEDS-SINAN-ID-06b`: Oturum mutlak süresi.
- `NEEDS-SINAN-ID-07b`: Grup geneli etkili parola sıfırlama/kilit açma hangi şirket kapsamıyla yetkilendirilir
  (öneri: kullanıcının üye olduğu her şirkette `identity.users.manage` şirket genelinde)?
- (Mevcut) ADR-SEC-001 NEEDS-SINAN-8 / E8: başarısız giriş kullanıcı adı hash anahtarı.
