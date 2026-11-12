# FEATURE — Yerel hesapla giriş (F-002) ve iki kişi kurallı rol atama (F-001)

- Tarih: `2026-11-12`
- Durum: **taslak spec; kod yok, grant yok, production aktivasyonu yok**
- Kaynak karar: Sinan, Issue #66 yorumları `6818566934` (F-001, F-002/OPEN-013), `6820046672`, `6820237902`,
  `6820838938`, `6821105608` (m.4), `6821262098` (m.4), `6821897620`, `6822303233` (m.2), `6823243819` (m.3) — hepsi 2026-11-12; karar dosyası
  `docs/decisions/SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md` (`KIM-D1`…`KIM-D23`)
- Önceki ilgili kararlar: `SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` BS-6 (F-001) ve BS-7
  (OPEN-013 yönü); `SINAN_KARARLARI_2026-08-31.md` §2 (OPEN-013 → GECICI_VARSAYIM); ADR-004
- Not: Bu spec `OPEN-013`'ü veya başka bir `OPEN-*` kaydını **kapatmaz**; hiçbir role, seed'e veya
  gerçek kullanıcıya grant vermez; production aktivasyonu değildir. 2026-11-12 kararları (1. ve 8. tur,
  #66 `6818566934`, `6820046672`) aynı PR'daki
  `docs/decisions/SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md` dosyasına `KIM-D1`…`KIM-D6`, `KIM-D9` (10. tur `6820237902`), `KIM-D12`…`KIM-D16` (14.–15. tur `6820838938`), `KIM-D17` (16. tur `6821105608`), `KIM-D18` (17. tur
  `6821262098`), `KIM-D19`…`KIM-D21` (18. tur `6821897620`) `KIM-D22` (20. tur `6822303233` m.2) ve `KIM-D23` (23. tur `6823243819` m.3) olarak yazıldı.

## Amaç

1. **F-002 (giriş):** Şimdilik ERP'nin kendi yerel hesabıyla (kullanıcı adı + parola) giriş; güçlü
   parola politikası ve isteğe bağlı iki adımlı doğrulama (2FA). Microsoft/Google gibi OIDC
   sağlayıcıları daha sonra **bir ayar olarak** eklenebilir olmalıdır (esnek yapı).
2. **F-001 (rol atama):** Admin rolleri bir ekrandan atar. Critical roller (finans onayı, yetki
   yönetimi) için **ikinci bir kişinin onayı zorunludur** (maker-checker). İlk Admin kurulumda
   **bir kez** tanımlanır ve o anda tanımlı bütün şirketlerde Admin olur (KIM-D18); her şirkette en az bir
   aktif Admin kalır (KIM-D17). Her atama ve her ret audit log'a yazılır.
3. Varsayılan kimlik bilgisi yasaktır: repo, seed verisi ve örnek dosyalar (`.env.example` vb.) kullanıcı adı
   veya parola içermez. Bu spec bunun nasıl garanti edildiğini tanımlar (§"Varsayılan kimlik bilgisi yasağı").

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
| #66 `6820046672` (2026-11-12, 8. tur) | Kilitli | KIM-D3…KIM-D6: 2FA herkese isteğe bağlı; başlangıç parola/kilit/oturum değerleri Ayarlar'dan değişir; kurucu Admin Critical atamayı ikinci onaysız yapabilir, Critical liste seçilmedi; varsayılan/zayıf kimlik bilgisi yalnız yerel Development ortamında, kullanıcı gizli ayarıyla. Karar dosyası: `docs/decisions/SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md`. |
| `OPEN-013` | KISMEN, açık kalır | Yerel hesap = geçici yol; OIDC soyutlaması korunur (ADR-004). |
| `DEC-TEC-008` | Kilitli | Dev/test/staging/prod ayrı; weak-password istisnası yalnız Development. |
| ADR-004 §1–§6 | Uygulandı | Kimlik doğrulama ≠ yetkilendirme; `(issuer, subject)` → yerel kullanıcı; Critical talepte talep eden/hedef onaylayamaz. |
| #66 `6820838938` (2026-11-12, 14.–15. tur) | Kilitli | KIM-D12…KIM-D16: karakter sınıfı ve dönemsel değişim yok, PBKDF2 framework varsayılanı; kilit 15 dk; oturum mutlak 12 saat; Critical rol geri alma tek adım + audit; grup geneli sıfırlama/kilit açma yalnız her üyelik şirketinde `identity.users.manage` ile. |
| #66 `6821105608` m.4 (16. tur) ve `6821262098` m.4 (17. tur) | Kilitli | KIM-D17: her şirkette en az bir aktif Admin; son Admin'in rolü geri alınamaz, kullanıcı pasifleştirilemez. KIM-D18: bootstrap kurucuya o anda tanımlı bütün şirketlerde Admin atar; sonradan açılan şirkette açan kişi ilk Admin olur; Admin devredilebilir. |
| #66 `6821897620` (18. tur, #1112 review P2'leri) | Kilitli | KIM-D18 "bütün şirketler" pasif şirketleri de kapsar. KIM-D19: hiçbir şirkette rolü olmayan kullanıcıda önce şirkete atama; özel/kurucu yetkisi yok. KIM-D20: açana Admin ataması arızalanırsa şirket "kurulum bekliyor"da kalır, işlem kabul etmez, alarm çıkar, kurucu Admin elle atar (audit'li). KIM-D21: şirket açma izni Critical. |
| #66 `6822303233` (20. tur, m.2) | Kilitli | KIM-D22: Admin'i olmayan ve kurucusu da pasif şirkette sunucu kurtarma komutu; yalnız sunucuya erişimi olan kişi kurucuyu yeniden aktif eder, şifre değiştirme zorunlu, ekrandan yapılamaz, her kullanım audit'e yazılır. |
| #66 `6823243819` (23. tur, m.3) | Kilitli | KIM-D23: aktif kurucu parolasını unutmuşsa ve ekrandan sıfırlayacak başka aktif Admin yoksa sunucu komutu parolayı sıfırlar; kayıt altına alınır, ilk girişte yeni parola zorunlu. |
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
  yetkilendirileceği KIM-D16 ile kararlıdır: işlemi yapan, hedef kullanıcının üye olduğu **her** şirkette
  `identity.users.manage` iznini şirket genelinde taşımalıdır; biri eksikse `403` (fail-closed, sunucuda
  hedefin aktif atamalarından çözülür).
- **KIM-D16 yazar okuması (geri alınabilir, fail-closed):** "üye olunan şirket" = hedefin bugün geçerli
  (`ValidFromUtc` ≤ şimdi, süresi dolmamış, `Active`) ataması bulunan şirket; Site/Location/WorkCenter kapsamlı
  atama o şirkette üyelik sayılır; `Expired`/`Revoked`/ileri tarihli atama sayılmaz. Aynı kapsam
  pasifleştirmeye de uygulanır (kaynak kararı yalnız sıfırlama/kilit açmayı sayar; pasifleştirme bu okumayla
  eklenmiştir). Bu kontrol hedefin atamaları okunduktan sonra yapılsa da izin çitinin parçasıdır: `403` yalnız
  güvenlik kanalına tek kayıt olarak yazılır (KIM-D10, F-023 "çit" tanımı).
- **Hiçbir şirkette rolü olmayan hedef (KIM-D19, 18. tur: "Önce şirkete atanır"):** hedefin bugün geçerli hiçbir
  ataması yoksa (yeni oluşturulmuş veya bütün rolleri geri alınmış) parola sıfırlama, kilit açma ve
  pasifleştirme **kimse tarafından** yapılamaz; özel veya kurucu yetkisi yoktur. Kullanıcı önce bir şirkete
  atanır, sonra o şirketin `identity.users.manage` yetkilisi işlemi yapar. Yanıt `409` +
  `identity.user.company_assignment_required` (öneri kod; UI "Önce kullanıcıyı bir şirkete atayın." der). Bu
  iş kuralı retidir, izin kararı değildir: işlemi yapan, isteğin şirket bağlamında (sunucuda çözülen `CompanyId`) izni taşıyarak çitten geçmiştir; ret
  aktörün şirket bağlamında denetime (`Outcome=Refused`) bir kez yazılır.
  **`ASSUMPTION-ID-LEAK-1` (yazar okuması, geri alınabilir):** KIM-D19 `409` ile KIM-D16 `403` farklı yanıt
  verdiği için yetkili bir şirket kullanıcısı hedefin "hiç ataması yok" ile "başka şirkette ataması var"
  durumlarını ayırt edebilir. Kullanıcı hesapları grup genelidir ve ID-4 listesinde zaten görünür; bu nedenle
  ayrım yeni bir bilgi sızdırmaz kabul edilir. Alternatif, iki durumda da tek tip `403`'tür (UI yönlendirmesi
  kaybolur). Seçim teknik/UX ayarıdır; ChatGPT hakemliğine açıktır.

**Persona (öneri, grant değildir):** Kurucu Admin (bootstrap), Admin (maker), İkinci onaylayıcı (checker),
sıradan kullanıcı. Persona → rol eşlemesi `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` gibi insan
kapısıdır.

## Domain nesneleri ve sahip modül

Sahip modül: **Identity** (`identity` şeması). Başka modül bu tablolara yazmaz.

| Nesne | Durum | Not |
|---|---|---|
| `UserAccount` | Var | `IsActive` mevcut. Kullanıcı adı yerel kimlik bilgisinde tutulur (aşağıda). |
| `LocalCredential` (yeni, öneri) | Yok | `UserId` (PK, FK → `Users`), `UserName`, `NormalizedUserName` (unique), `PasswordHash`, `HashFormatVersion`, `MustChangePassword`, `PasswordChangedAtUtc`, `FailedAccessCount`, `LockoutEndUtc`, `TwoFactorEnabled`, `TemporaryPasswordIssuedAtUtc` (null'lanabilir; KIM-D23 geçici parolası), `TemporaryPasswordExpiresAtUtc` (null'lanabilir; sıfırlama anında yazılır), `TemporaryPasswordConsumedAtUtc` (null'lanabilir), `Version` (concurrency token; giriş sayaçları onu artırmaz, §"Kilit sayacı"). |
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
Etkin → [N ardışık başarısız deneme] → Kilitli(LockoutEndUtc) → [süre doldu | yetkili kilidi açtı] → Etkin
Etkin → [yetkili sıfırladı] → Oluşturuldu(MustChangePassword=true)
Herhangi → [UserAccount.IsActive=false; son Admin ise reddedilir (KIM-D17)] → Pasif (giriş reddedilir; silme yok)
Pasif → [yalnız FounderUserId; KIM-D22 recover-founder reactivate] → Oluşturuldu(MustChangePassword=true)
IsActive=true (Oluşturuldu | Etkin | Kilitli | ParolaDeğiştirmeZorunlu; yalnız FounderUserId) → [KIM-D23 recover-founder reset-password] → Oluşturuldu(MustChangePassword=true)
Oluşturuldu(KIM-D23 geçici parolası tüketildi | süresi doldu) → giriş kapalı; çıkış yalnız [KIM-D23 reset-password | yetkili sıfırlaması]
```

`Pasif`'ten çıkışın HTTP/ekran yolu yoktur; tek istisna `FounderUserId` için KIM-D22 sunucu komutudur.

`MustChangePassword=true` iken oturum yalnız `POST /api/v1/identity/session/password` ve çıkışa izin verir;
diğer her endpoint `403` + problem kodu `identity.password_change_required` döner (öneri). "Yetkili" =
hedefin üye olduğu her şirkette `identity.users.manage` taşıyan (KIM-D13/D16).

**Teknik varsayılanlar (`ASSUMPTION-*` adayı, geri alınabilir; K3 testleri bunlara göre deterministiktir):**

- **Oturum iptali:** `LocalCredential`'a `SecurityStamp` (öneri ad) eklenir; parola değişimi, yetkili parola
  sıfırlaması, pasifleştirme ve 2FA kapatma stamp'i yeniler. Oturum (çerez veya imzalı token) stamp'i taşır ve
  her istekte DB'deki değerle karşılaştırılır; uyuşmazlıkta `401`. Böylece imzalı token seçeneğinde de açık
  oturum 12 saat sürmez. `MustChangePassword` ve `IsActive` her istekte DB'den okunur (`ErpClaimsTransformation`
  zaten her istekte kullanıcıyı çözer); token içindeki değere güvenilmez.
- **Kilit sayacı:** başarılı girişte `FailedAccessCount = 0`; kilit süresi dolunca veya yetkili kilidi açınca ya da
  parolayı sıfırlayınca `0`'dan başlar; kilitliyken gelen deneme süreyi **uzatmaz**, sayacı artırmaz, yalnız
  `auth.login.failed` olayı yazılır.
- **Giriş sayaçları ve `Version` (KIM-D23 ile hizalı; yazar okuması, geri alınabilir teknik tasarım):** giriş
  yolunun yazdığı alanlar (`FailedAccessCount`, `LockoutEndUtc`, başarılı girişte yeniden hash, geçici parola
  tüketimi) tek koşullu `UPDATE` ile yazılır ve `Version`'ı **artırmaz**. `Version` yalnız komut/yetkili
  değişikliklerinde artar (parola değişimi/sıfırlama, `MustChangePassword`, 2FA, `SecurityStamp`, kilit açma).
  Koşullu `UPDATE` okunan `PasswordHash`, `SecurityStamp` ve `TemporaryPasswordIssuedAtUtc` değerleriyle
  karşılaştırır; sayaç göreli yazılır (`FailedAccessCount = FailedAccessCount + 1`); etkilenen satır 0 ise giriş
  genel hatayla reddedilir. Böylece başarılı girişteki yeniden hash veya tüketim, arada yazılmış yeni bir hash'i
  ezemez. Başarısız giriş denemeleri `recover-founder` koşumunu `concurrent_change`'e düşürmez (`recover-founder`
  koşumunu `concurrent_change`'e düşürme yolu yoktur; genel kilitleme DoS'u KIM-D4 kapsamındadır); komutun yazması
  kilidi ve sayacı zaten sıfırlar.

**Kurucu bootstrap:** `Yok → Tamamlandı` (tek yön; geri alınamaz, yeniden çalıştırma reddedilir).

**Critical rol talebi** (`AccessGrantRequestStatus` bugün `Pending/Approved/Rejected`):

```
Pending → Approved (checker ≠ requester ∧ checker ≠ target) → RoleAssignment(Active, ApprovedByUserId=checker)
Pending → Rejected (checker, gerekçe kodu ile)
Pending → Withdrawn (yalnız requester; öneri — yeni enum değeri, ID-3)
```

**Atama:** `RoleAssignmentStatus` `Active → Revoked | Expired` (mevcut). Critical rolün geri alınması
tek adımdır, ikinci onay yoktur (KIM-D15); audit ve `identity.permission.changed` yazılır.

## Birim/formül/yuvarlama/tolerans

Para/stok/formül yoktur. Yalnız güvenlik politika parametreleri vardır; **hiçbiri kodda sabit değildir**.
Hepsi `Identity:LocalAccount` ayar bölümünden (öneri ad; Options pattern, startup'ta doğrulanır, fail-closed)
okunur. Değer seçimi güvenlik politikasıdır ve insan kararıdır. Tek istisna, mevcut korumayı gevşetmeyen,
yalnız koruma **ekleyen** geri alınabilir teknik ayardır (giriş hız sınırı): bu bir `ASSUMPTION-*` adayıdır,
hakem onayıyla etkinleşir ve Ayarlar'dan değişir:

| Parametre | Ayar anahtarı (öneri) | Öneri (yalnız referans, seçim değil) | Karar |
|---|---|---|---|
| Asgari parola uzunluğu | `PasswordPolicy:MinimumLength` | **Başlangıç 8** (KIM-D4; Ayarlar'dan değişir). Azami ≥ 64 kabul edilmeli (NIST SP 800-63B) | Karar verildi |
| Karakter sınıfı kuralı | `PasswordPolicy:RequireCharacterClasses` | **Varsayılan kapalı** (KIM-D12); Ayarlar'dan açılabilir. NIST kompozisyon kuralı önermez | Karar verildi |
| Yasaklı/sık parola listesi | `PasswordPolicy:BlocklistEnabled` | NIST: zorunlu karşılaştırma; liste `admin`, kullanıcı adı, şirket adı türevlerini içerir; varsayılan açık, Development istisnası §4 | Karar verildi (KIM-D4) |
| Periyodik zorunlu değişim | `PasswordPolicy:MaximumAgeDays` | **Varsayılan kapalı** (KIM-D12); değişim yalnız sızıntı şüphesinde; Ayarlar'dan açılabilir | Karar verildi |
| Kilit eşiği | `Lockout:MaxFailedAttempts` | **Başlangıç 10** ardışık hata (KIM-D4; Ayarlar'dan değişir) | Karar verildi |
| Kilit süresi | `Lockout:Duration` | **Başlangıç 15 dk**, sonra kendiliğinden açılır (KIM-D13; Ayarlar'dan değişir); `identity.users.manage` sahibi daha erken açabilir | Karar verildi |
| Oturum boşta kalma / mutlak süre | `Session:IdleTimeout`, `Session:AbsoluteLifetime` | Boşta **başlangıç 8 saat** (KIM-D4), mutlak **başlangıç 12 saat** (KIM-D14); ikisi de Ayarlar'dan değişir | Karar verildi |
| Giriş hız sınırı | `RateLimit:Login:PermitLimit`, `:Window`, `:PartitionBy` | Değer seçilmedi; `ASSUMPTION-ID-RL-1` adayı (geri alınabilir teknik ayar, ID-1 review'unda hakem onayıyla): IP + normalize kullanıcı adı başına 1 dakikada 10 deneme, aşımda `429`; değer Ayarlar'dan değişir. Hız sınırı kilit sayacından ayrıdır | Açık (ASSUMPTION adayı) |
| Hash algoritması ve maliyet | `PasswordHashing:Algorithm`, `:Pbkdf2Iterations` | Öneri A: ASP.NET Core Identity `PasswordHasher<T>` (PBKDF2-HMAC-SHA512, V3 biçim, sürümlü hash, yeniden hash desteği); OWASP önerisi PBKDF2-SHA512 ≥ 210 000 iterasyon. Öneri B: Argon2id (OWASP: m=19 MiB, t=2, p=1) — üçüncü taraf paket ister (tedarik zinciri incelemesi). **Seçilen: A** (KIM-D12; framework içi, yeni bağımlılık yok); iterasyon sayısı teknik ayardır. | Karar verildi |
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
| Yerel kullanıcı oluşturma, parola sıfırlama, kilit açma, pasifleştirme | `identity.users.manage` | **Yeni ad (öneri)**; Critical **değil** (KIM-D9); grup geneli etkide kapsam KIM-D16 (pasifleştirme yazar okumasıyla); son Admin pasifleştirilemez (KIM-D17) |

`identity.access.request` mevcut adının bu akışta rolü (ör. kullanıcının kendisi için talep) bu spec'te
kullanılmaz; kullanılacaksa ayrı karar.

**Görev ayrılığı kuralları (F-001 kararı + ADR-004 §6 + `docs/security/GOREV_AYRILIGI_ONAY.md`):**

1. Critical rol: checker ≠ requester **ve** checker ≠ target. Domain (`AccessGrantRequest.Approve`) +
   SQL `CK_AccessGrantRequests_SecondApproval` iki katmanda korur. Kimlikler `uniqueidentifier` olduğundan
   karşılaştırma doğrudandır; Purchasing'deki `COLLATE Latin1_General_100_BIN2` deseni (#1083,
   `CK_PurchaseOrders_Approval`) burada string aktör kimliği olmadığı için gerekmez, ama yeni bir string
   aktör sütunu eklenirse aynı ikili collation kuralı uygulanır.
2. Kural 5 (bootstrap), 6 (kurucu Admin) ve 8 (şirket açan kişi) dar istisnaları dışında Critical rol hiçbir
   yoldan tek adımda `RoleAssignment` olamaz: doğrudan atama endpoint'i Critical rolü
   `409` + `identity.assignment.second_approval_required` ile reddeder (öneri kod). SQL tarafında, Critical
   bilgisi rol izinlerinden türediği için check constraint ile ifade edilemez; bunun yerine store yalnız
   onaylı talepten `ApprovedByUserId` dolu satır yazar ve gerçek SQL testi doğrudan yazma yolunun
   olmadığını kanıtlar.
3. Critical olmayan rol: tek adım, aynı audit.
4. Kendine Critical rol talebi açılabilir; checker yine üçüncü kişi olmalıdır.
5. **Kurucu istisnası (bootstrap, KIM-D18):** Bootstrap komutu tek kez, `System` aktörü + operatör beyanıyla
   kurucuya o anda tanımlı **bütün şirketlerde** (aktif ve pasif; Sinan "hepsinde" dedi, kapsam 18. tur `6821897620` ile teyitli) Admin rolünü atar
   (bugün seed'deki
   `SYSTEM_ADMINISTRATOR`; bu rol `identity.roles.manage` ve `identity.access.approve` taşır, yani Critical'dir;
   BS-6 "tek seferlik kurucu admin"). "Admin rolü" kodda sabit değildir: mevcut Admin persona tanımına eşlenen
   rol seed/ayar işaretiyle bulunur (öneri `Identity:AdminRoleCode`, startup'ta rolün var ve Critical olduğu
   doğrulanır).
6. **Kurucu Admin istisnası (KIM-D5, 8. tur: "Kurulumcu admin her zaman yapabilsin"):** Bootstrap'ta
   oluşturulan kurucu Admin, Critical rol atamasını **ikinci kişi onayı olmadan, her zaman** yapabilir. Diğer
   her atayan için ikinci kişi onayı sürer. Uygulama: kurucu kimliği `IdentityBootstrapState.FounderUserId`
   ile sunucuda belirlenir (istemci beyanı değil); kurucunun doğrudan Critical ataması `Approve` yolunu
   atlamaz, `AccessGrantRequest` satırı `ApprovalMode = FounderDirect` (öneri ad) ile tek kişi olarak kapanır
   ve audit'te ayrıca işaretlenir. "requester = kurucu" koşulu ayrı tablodaki (`IdentityBootstrapState`)
   değere bağlı olduğu için CHECK constraint ile ifade edilemez: check constraint yalnız "ikinci onaylayıcı dolu
   **veya** `ApprovalMode ∈ {Bootstrap, FounderDirect, CompanyOpener}`" (öneri adlar) biçimine genişler. Her tek
   kişilik modu store aynı transaction içinde doğrular: `Bootstrap` yalnız bootstrap komutundan ve
   `IdentityBootstrapState` boşken; `FounderDirect` yalnız requester = `FounderUserId` ve kurucu `IsActive=true`
   iken, iki durumdan birinde: (a) şirket `Active` ise kural 6'nın normal koşullarıyla (kurucu o şirkette şirket
   geneli `identity.roles.manage` taşır, çitten geçer); (b) şirket `SetupPending` ise — durum Organization'ın
   sürümlü okuma portundan (öneri `ICompanyLifecycleReadPortV1`) okunur, durum bilinmiyor veya port erişilemezse
   ret (fail-closed) — yalnız Admin rolü ataması ve yalnız kural 7'deki şirket kilidi altında yapılan kilitli
   "şirkette aktif Admin yok" kontrolü geçerse; `CompanyOpener` yalnız kural 8'deki inbox tüketicisinden ve aynı
   kilitli "aktif Admin yok" kontrolü geçerse. `Active` şirket için bayat bir `SetupPending` okuması (b) yolunu
   açmaz: `Active` durum zaten bir aktif Admin'in var olduğu anlamına gelir ve kilitli kontrol reddeder.
   (b) yolu için şirket bağlamı çözümündeki `SetupPending` reddinin istisnası yalnız tek "ilk Admin ataması"
   endpoint'ine, endpoint metadata'sıyla tanınır; genel bir atlama değildir. Gerçek SQL testleri her mod için başka
   yazma yolunun olmadığını kanıtlar (kural 2'deki desen). Böylece tavuk-yumurta sorunu (tek Admin varken ilk checker'ın
   doğması) da çözülür; eski `NEEDS-SINAN-ID-08` kapandı. Kurucu Admin pasif olduğu sürece istisna
   kullanılamaz; KIM-D22 kurtarmasıyla aynı `FounderUserId` yeniden aktif olunca geri döner; başkasına devredilmez
   (devretme ayrı karar). Admin **rolünün** devri (KIM-D18) bu istisnayı devretmez.
   **Negatif testler (kanal ile):**

   | Durum | Yanıt | Kayıt kanalı |
   |---|---|---|
   | `Active` şirket, kurucu üye değil | `403` | güvenlik (çit) |
   | `SetupPending` şirkette Admin dışı rol | `409 identity.company.first_admin_role_required` (öneri) | audit |
   | Kurucu pasif | `401` (security stamp) | auth olayı, güvenlik |
   | Kurucu olmayan kullanıcı, `SetupPending` şirket | `409 organization.company.setup_pending` (bağlam çözümü) | güvenlik (çitten önce) |
   | Şirkette zaten aktif Admin var | `409 identity.company.admin_already_assigned` (öneri) | audit |
7. **Son Admin koruması (KIM-D17, 16. tur: "Sistem engellesin"):** her şirkette en az bir aktif Admin kalır.
   "Aktif Admin" = `IsActive=true` kullanıcı + o şirkette şirket geneli, başlamış (`ValidFromUtc` ≤ şimdi),
   **süresiz** (`ValidUntilUtc IS NULL`) Admin rolü ataması + rol tanımı aktif (`RoleDefinition.IsActive`).
   Süreli Admin ataması sayıma girmez (süresi dolunca şirket Adminsiz kalmasın diye); bootstrap ve şirket açan
   kişi atamaları süresiz yazılır. Bir işlem sonunda bir şirkette aktif Admin sayısı 0 olacaksa reddedilir:
   Admin atamasının geri alınması (kendisi veya başkası), kullanıcının pasifleştirilmesi (kullanıcı birden çok
   şirkette son Admin olabilir; herhangi birinde son ise ret), Admin atamasına bitiş tarihi verilmesi, Admin
   rolünün pasifleştirilmesi veya Admin rolünden izin çıkarılması. Yanıt `409` + `identity.last_admin_required`
   (öneri kod), ret izin çitinden sonra verilen iş reddidir ve audit'e yazılır; hiçbir şey değişmez.
   **Kilit:** sayıyı düşürebilen her işlem (geri alma, pasifleştirme, bitiş tarihi, rol pasifleştirme/izin
   daraltma) aynı kilidi alır: sayım ve yazma tek transaction'da, şirketin Admin atama satırları üzerinde
   `UPDLOCK, HOLDLOCK` ile ya da şirket başına `sp_getapplock` (`identity.admin:<CompanyId>`) ile. Kullanıcı
   pasifleştirme ve rol işlemleri, etkilediği bütün şirketlerin kilidini `CompanyId` sırasıyla alır (kilitlenme
   sırası sabit, deadlock önlenir). RCSI açık olduğundan sayım kilit alındıktan sonra, kilitli okuma ile yapılır
   (satır sürümü okuması sayıma esas alınmaz). Gerçek SQL eşzamanlılık testleri zorunlu. Önce başka birine
   süresiz Admin verilir, sonra eski Admin çıkarılır.
   **İlk Admin yarışı:** sayıyı 0'dan 1'e çıkaran iki yol (`CompanyOpener` tüketicisi ve `SetupPending`
   şirkette `FounderDirect`) aynı şirket kilidini alır ve kilit altında "aktif Admin yok" kontrolünü kilitli
   okumayla yapar. Admin varsa `CompanyOpener` hiçbir şey yazmaz (idempotent; inbox kaydı işlenmiş olarak
   kapanır), `FounderDirect` `409 identity.company.admin_already_assigned` (öneri kod) ile reddedilir ve audit'e
   yazılır. `identity.company.first_admin_assigned.v1` yalnız 0→1 geçişinde, aynı transaction'da, şirket başına
   deterministik olay kimliğiyle yayınlanır. Ek savunma olarak aktif Admin atamaları için filtreli tekil indeks
   önerilir (migration, K3). Gerçek SQL yarış testleri: `CompanyOpener` ∥ `FounderDirect` ve aynı olayın
   eşzamanlı çift teslimi; sonuçta tek ilk Admin ve tek olay.
8. **Sonradan açılan şirkette ilk Admin (KIM-D18, 17. tur: "Kurucu hepsinde Admin"):** yeni şirketi açan kişi o
   şirketin ilk Admin'i olur. Şirketi Organization modülü açar; Identity tablosuna yazmaz. Önerilen sözleşme:
   Organization şirket açılışını outbox olayı (öneri `organization.company.opened.v1`) yayınlar; açan `UserId`
   istemci beyanı değil, Organization komutunun sunucuda çözülen aktöründen gelir. Identity inbox/dedup ile
   tüketir; açan kullanıcı `IsActive=true` ise tek süresiz atamayı `ApprovalMode = CompanyOpener` (öneri ad)
   ile, ikinci onaysız ve audit'li yazar; şirkette zaten aktif Admin varsa hiçbir şey yazmaz.
   **Kurulum bekliyor (KIM-D20, 18. tur: "Kurulum bekler, kurucu atar"):** yeni şirket Admin'i olana kadar
   "kurulum bekliyor" durumundadır ve işlem kabul etmez. Önerilen uygulama: Organization şirketi `SetupPending`
   durumunda açar; şirket bağlamı çözümü `SetupPending` şirkete gelen her isteği tek kodla reddeder (öneri `409`
   + `organization.company.setup_pending`; aşağıdaki kurucu ataması hariç). Bugünkü `CompanyContextMiddleware`
   yalnız `RequireCompanyContext` endpoint'lerinde çalışır ve şirket durumunu bilmez; ret, başlık olmadan
   `companyId` alan endpoint'lere, tüketicilere ve arka plan işlerine de genişletilmelidir (Organization/Platform
   devri). `SetupPending` mevcut `Company.IsActive` alanından ayrı tutulur (öneri: ayrı `LifecycleState` alanı,
   `IsActive` anlamı değişmez; Organization devri). Identity şirketteki ilk süresiz
   Admin atamasını yazınca aynı transaction'da outbox olayı (öneri `identity.company.first_admin_assigned.v1`)
   yayınlar; Organization inbox/dedup ile tüketip şirketi `Active` yapar. Böylece olay işlenene kadar geçen süre
   de "kurulum bekliyor" olarak görünür, Adminsiz ama işlem kabul eden bir pencere kalmaz. Açan kullanıcı
   pasifse veya yazma başarısızsa: sınırlı yeniden deneme → dead-letter → alarm; "kurulum bekleyen şirket
   sayısı" ölçülebilir metrik olarak yayınlanır. Kurtarma: kurucu Admin (`FounderUserId`, aktif) o şirkete elle
   Admin atar; bu, kural 6'daki `FounderDirect` yoludur (ikinci onaysız, audit'li). Kurucu o şirkette üye
   olmasa da bu yol yalnız `SetupPending` şirkette ve yalnız Admin rolü ataması için açıktır; `Active` şirkette
   kural 6'nın normal koşulları geçerlidir. Kurucu da pasifse (KIM-D22, 20. tur: "Sunucu kurtarma
   komutu"): yalnız sunucuya erişimi olan kişi kurucu kurtarma komutuyla (aşağıda) kurucuyu yeniden aktif eder;
   kurucu ilk girişte parolasını değiştirmek zorundadır, sonra yukarıdaki `FounderDirect` yoluyla Admin atar.
   Bu yol ekrandan veya HTTP'den yapılamaz; o zamana kadar şirket "kurulum bekliyor"da kalır ve alarm sürer.
   Bu cross-module sözleşmedir (K3).
   **Şirket açma izni Critical (KIM-D21, 18. tur: "Evet, Critical"):** şirket açma izni Critical sınıfındadır;
   verilmesi Critical rol kurallarına (KIM-D1 ikinci kişi onayı, KIM-D5 kurucu istisnası, KIM-D15 geri alma)
   tabidir. Bu bir sınıflandırmadır: iznin adı ve kaydı Organization'ın şirket açma diliminde yapılır; mevcut
   grant'larda değişiklik ayrı somut insan kararı ister.

## Audit, idempotency ve ters işlem

Her yazma komutu `k3-command-skeleton` iskeletini izler: izin çiti → expected version → `Idempotency-Key`
replay → korumalı okumalar → tek `SaveChanges` (deneme + audit aynı transaction) → terminal ret kaydı.

- **Audit (ERP-000D, `IAuditEventCollector.Enqueue`):** rol atama, Critical talep açma, onay, ret, geri
  çekme, geri alma, kullanıcı oluşturma, parola sıfırlama, kilit açma, bootstrap. İzin çitinden sonra
  verilen iş reddi (SoD ihlali, `identity.last_admin_required`, durum uyuşmazlığı) audit'e yazılır. Yetkisiz ve
  şirket dışı ret (çit reddi, KIM-D16 kapsam reddi dahil) audit'e değil, yalnız güvenlik kanalına tek kayıt
  olarak yazılır (KIM-D10, F-023). Audit'e **asla** parola, hash, TOTP sırrı veya kurtarma kodu
  yazılmaz.
- **Güvenlik olay kanalı (ADR-SEC-001, PR #1093, `ISecurityEventWriterV1`):** `auth.login.succeeded`,
  `auth.login.failed` (`credentials_invalid`, `account_inactive`), `identity.permission.changed`.
  Kilitleme için kapalı listede kod yoktur; `account_locked` (ve 2FA için `second_factor_invalid`) eklemek
  ADR-SEC-001 sözleşme değişikliğidir (liste + check constraint aynı PR) → ID-1 ile birlikte ayrı K3 madde.
  E8 kararı gelene kadar başarısız girişte kullanıcı adı hash'i **yazılmaz**; aktör `Anonymous`.
  `auth.login.*` fail-open + alarmdır (ADR-SEC-001 tablosu): kanal yazılamazsa giriş kararı değişmez.
- **Idempotency:** atama/talep/onay/geri alma komutları ile parola taşıyan yönetim komutları
  (`/access/users`, `/password-reset`) ve `/unlock`, `/deactivate`, `/session/password` `Idempotency-Key` ve
  expected version ister. Parola taşıyan komutlarda istek parmak izine parola **girmez**: parmak izi yalnız
  istek kimliği + hedef `UserId` + expected version'dan hesaplanır (tuzsuz SHA-256'nın parolayı DB'ye düşürmemesi
  için). Aynı anahtarla farklı parola gelmesi (`ASSUMPTION-ID-IDEM-1` adayı): parmak izine parolanın anahtarlı
  HMAC'i eklenirse `422` (anahtar farklı istek) döner; HMAC anahtarı bir secret olduğundan E8 gibi insan
  kapısıdır. Anahtar gelene kadar replay eski sonucu `Idempotency-Replayed` başlığıyla döner, parola değişmez
  ve UI "bu istek daha önce işlendi; yeni parola uygulanmadı" mesajını gösterir. Giriş endpoint'i
  idempotent değildir (her deneme sayılır); idempotency anahtarı kabul etmez.
- **Concurrency:** `LocalCredential.Version`, `AccessGrantRequest.Version`, `RoleAssignment.Version`
  concurrency token; çakışma `409`.
- **Ters işlem:** silme yok. Atama → `Revoked`; talep → `Rejected/Withdrawn`; hesap → `IsActive=false`.

## API/event/AI tool sözleşmesi

Tüm yollar `/api/v1/identity` altında (mevcut `IdentityEndpoints.cs` grubu). Hepsi `NoStore`, problem
kodları sürümlü; OpenAPI `openapi-regenerate` ile yeniden üretilir. Adlar öneridir:

| Yöntem | Yol | İzin | Not |
|---|---|---|---|
| POST | `/session/login` | anonim | `{userName, password, totpCode?}`; başarısızlıkta tek tip `401` (kullanıcı var/yok ayrımı sızdırılmaz); kilitliyken de aynı yanıt + olay. Hız sınırı (IP + hesap) zorunlu; ayar `RateLimit:Login:*` (politika tablosu). |
| POST | `/session/logout` | oturum | |
| POST | `/session/password` | oturum | Mevcut parola + yeni parola; politika ihlali `400` + alan kodları. |
| GET | `/me` | oturum | Mevcut; `mustChangePassword` alanı eklenir (sözleşme sürüm artışı). |
| POST | `/access/users` | `identity.users.manage` | Yerel kullanıcı oluştur; geçici parolayı Admin girer, hesap `MustChangePassword=true`; `TemporaryPassword*` alanları null'dır. |
| POST | `/access/users/{userId}/password-reset` | `identity.users.manage` | Kapsam KIM-D16; `MustChangePassword=true`; oturumlar düşer; `TemporaryPassword*` alanları null'lanır; parola parmak izine girmez. |
| POST | `/access/users/{userId}/unlock` | `identity.users.manage` | Kapsam KIM-D16; sayaç sıfırlanır; audit. |
| POST | `/access/users/{userId}/deactivate` | `identity.users.manage` | `UserAccount.IsActive=false`; kapsam KIM-D16 (yazar okumasıyla); son Admin ise `409 identity.last_admin_required` (KIM-D17); oturumlar düşer (security stamp); audit. |
| POST | `/access/assignments` | `identity.roles.manage` | Critical olmayan rol; Critical rolde `409 second_approval_required`. |
| POST | `/access/assignments/{id}/revoke` | `identity.roles.manage` | Tek adım, Critical rolde de ikinci onay yok (KIM-D15); audit + `identity.permission.changed`. Şirketin son aktif Admin ataması ise `409 identity.last_admin_required` (KIM-D17). |
| POST | `/access/grant-requests` | `identity.roles.manage` | Critical rol talebi. |
| POST | `/access/grant-requests/{id}/approve` · `/reject` · `/withdraw` | approve/reject: `identity.access.approve`; withdraw: requester | |
| GET | `/access/grant-requests?status=pending` | `identity.access.approve` veya `identity.assignments.read` | |

**Oturum mekanizması (öneri, geri alınabilir teknik ayar → `ASSUMPTION-*` adayı):** sunucu tarafı oturum +
`HttpOnly`, `Secure`, `SameSite=Strict` çerez ve antiforgery; ya da yerel olarak imzalanmış kısa ömürlü
token. İki seçenekte de security stamp her istekte DB'den doğrulanır (§Durum geçişleri, teknik varsayılanlar). Her iki durumda kimlik doğrulama `(urn:goymen:local-account, UserId)` claim çiftini üretir ve mevcut
`ErpClaimsTransformation` yetkiyi DB'den çözer (ADR-004 §1/§3). İmza/Data Protection anahtarları secret'tır.

**Kimlik modu ayarı:** `AuthenticationModes`'a `LocalAccount` eklenir. ID-6 ile `Authentication:Providers`
listesi (örn. `LocalAccount` + `OidcJwtBearer`) birden çok şemayı açar; OIDC varsayılan **kapalı**,
sağlayıcıya özel kod/tenant sabiti yok (ADR-004). `DevelopmentBootstrap` yalnız Development/Testing olarak
kalır (mevcut startup kontrolü).

**Event:** Dış outbox olayı yok (ilk dilimlerde). **AI tool:** yok; AI hiçbir kimlik bilgisi veya atama
aracına erişmez (`docs/security/AI_YETKI_SOZLESMESI.md`).

**Kurucu bootstrap komutu (ID-2):** API'nin HTTP yüzeyi değil, operatörün sunucuda çalıştırdığı tek seferlik
komut (öneri: `GoymenERP.Api identity bootstrap-founder`; ayrı CLI projesi de olabilir). Şirket argümanı
almaz: Admin ataması o anda tanımlı bütün şirketlere (aktif ve pasif) yapılır (KIM-D18); hiç şirket yoksa
reddeder.

- Parola **etkileşimli** ve yankısız okunur; komut satırı argümanı, ortam değişkeni veya dosya ile parola
  kabul etmez (kabuk geçmişine/loglara düşmesin). stdin terminal (TTY) değilse hiçbir şey yazmadan reddeder
  (`cmd < dosya` veya boru ile parola verilemez); parola iki kez girilir ve eşleşmesi doğrulanır.
- Parola, ortamın politikasından geçmek zorundadır; hesap `MustChangePassword=true` oluşur.
- `IdentityBootstrapState` satırı varsa veya herhangi bir şirkette etkin Admin rolü ataması
  (`Identity:AdminRoleCode`) varsa reddeder; tek transaction içinde kullanıcı + kimlik bilgisi + `ExternalIdentityLink` + her şirket
  için bir Admin ataması + işaret + audit yazar.
- Production'da çalıştırılması insan kapısıdır.

**Kurucu kurtarma komutu (KIM-D22, KIM-D23, ID-2):** Tek sunucu komutu (öneri:
`GoymenERP.Api identity recover-founder`), birbirini dışlayan iki açık alt yol:
`reactivate` (KIM-D22: pasif kurucuyu yeniden aktif eder) ve `reset-password` (KIM-D23: aktif kurucunun
parolasını sıfırlar). Alt yol argümanla açıkça seçilir; komut durumdan alt yol tahmin etmez, alt yolsuz çağrı
hesapta ve kimlik bilgisinde değişiklik yapmadan reddeder; yalnız ret kaydı yazılır (aşağıdaki ret listesi, kod
öneri `identity.founder_recovery.subcommand_required`). HTTP ucu, ekran veya AI aracı yoktur: komut modunda web
host/Kestrel başlatılmaz ve komut işleyicisi endpoint routing'e kaydedilmez (OpenAPI'de ve shell kayıtlarında
bulunmadığı testle doğrulanır). Aşağıdaki maddeler, ayrıca belirtilmedikçe
her iki alt yol için geçerlidir; `reset-password`'a özgü kurallar ayrı başlıktadır.

- Yalnız `IdentityBootstrapState` satırı varsa çalışır ve yalnız oradaki `FounderUserId` hesabını hedefler;
  kullanıcı adı veya kullanıcı kimliği argümanı almaz, başka hesabı aktif edemez.
- `reactivate` ön koşulları (ikisi birden; KIM-D22: "Admin'i olmayan **ve** kurucusu da pasif şirket"):
  1. Hedef hesap pasiftir (`IsActive=false`). Aktif kurucuda `identity.founder_recovery.founder_active` (öneri kod)
     ile reddeder; parola ve oturumlar değişmez. Aktif kurucunun Admin atama yolu zaten `FounderDirect`'tir
     (kural 8). Başarılı koşum hesabı aktif yaptığı için ikinci koşum da bu kodla reddedilir; ilk parola ezilmez.
     Aktif ama parolasını unutmuş kurucunun yolu `reset-password` alt yoludur (KIM-D23, aşağıda).
  2. En az bir şirkette (aktif, pasif veya `SetupPending`) KIM-D17 anlamında etkin Admin yoktur (yazar okuması,
     review'da değişebilir). Her şirkette etkin Admin varsa `identity.founder_recovery.not_needed` (öneri kod) ile
     reddeder. Ekrandan yeniden aktif etme yolu bu spec'te **yoktur** (API'de yalnız `/deactivate` var; `Pasif`
     HTTP/ekran için terminaldir, tek çıkış bu komuttur ve yalnız `FounderUserId` içindir); gerekirse ayrı karar
     ister, bu spec seçmez.
- Adım sırası sabittir; etkileşim kilit altında **yapılmaz**:
  1. Kilitsiz ön okuma: `IdentityBootstrapState`, hedef hesap (`Version` concurrency token'ı dahil) ve ön
     koşullar okunur; sağlanmıyorsa ilgili kodla reddeder. Bu okuma, operatöre onay için gösterilen durumdur.
  2. Etkileşim (transaction ve kilit dışında): stdin terminal (TTY) değilse reddeder (`cmd < dosya` veya boru ile
     parola verilemez). Yeni parola **etkileşimli** ve yankısız okunur (argüman, ortam değişkeni, dosya yok), iki
     kez girilir, eşleşmesi ve ortam politikası doğrulanır. Sonra komut hedef ortamı, veritabanını (sunucu ve
     katalog adı; parola içermez), kurucunun kullanıcı adını ve ön okumadaki şirketleri gösterir ve açık etkileşimli
     onay ister; `--yes`/`--force` gibi etkileşimsiz atlama bayrağı yoktur.
  3. Transaction açılır; KIM-D17/kural 7'deki şirket kilitleri **bütün** şirketler için `CompanyId` sırasıyla,
     sonra kurucunun `UserAccount` satırı alınır. Ön koşullar kilitli okumayla **yeniden** doğrulanır; ön okumadan
     bu yana değiştiyse ilgili kodla (`founder_active`, `founder_inactive`, `not_needed`) reddeder.
  4. Yazma: kullanıcı ve kimlik bilgisi satırları `Version` concurrency token'ıyla güncellenir. Beklenen
     `Version`, adım 1'de okunan ve operatörün onayladığı değerdir (kilit altında yeniden okunan değer değil;
     "onaylanan durum yazılan durumdur"). Arada hesap değiştiyse (çalışan uygulamada ekrandan sıfırlama,
     kurucunun kendi parola değişimi, eşzamanlı başka bir `recover-founder` koşumu) transaction geri alınır ve
     komut `identity.founder_recovery.concurrent_change` (öneri kod) ile reddeder. İki eşzamanlı koşumdan biri
     başarılı olur, diğeri bu kodla reddedilir. Komut replay yapmaz: sonraki her başarılı koşum yeni bir
     işlemdir.
- Onay verilmezse, TTY yoksa veya parolalar eşleşmez/politikadan geçmezse komut hesapta ve kimlik bilgisinde hiçbir
  değişiklik yapmadan çıkar; yalnız ret kaydı yazılır (aşağıda). Production'da çalıştırılması insan kapısıdır (AGENTS.md); bu onay adımı kapının yerine
  geçmez, prosedürel kapıya kanıt üretir.
- `reactivate` başarısında hesap `IsActive=true`, `MustChangePassword=true` olur, kilit sıfırlanır, `TemporaryPassword*` alanları null'lanır, security stamp
  yenilenir (eski oturumlar düşer). Kurucuya yeni rol ataması yapmaz; Admin ataması ancak giriş + parola değişiminden sonra
  `FounderDirect` ile yapılır.
- Yan etkiler (kararın lafzi sonucu; daraltılmaz, görünür kılınır): pasifleştirme atamaları geri almadığı için
  yeniden aktif etme, kurucunun geri alınmamış eski atamalarını (ör. başka şirketlerdeki Admin) bütün şirketlerde
  kendiliğinden geri getirir; audit kaydı geri gelen etkin atamaların listesini taşır ve etkin ataması geri gelen
  her şirkete de o şirketin `CompanyId`'siyle bir audit satırı yazılır (aynı transaction). KIM-D5 `FounderDirect`
  istisnası da geri gelir (kural 6). Kurucunun 2FA durumu korunur; komut 2FA'yı kapatmaz veya sıfırlamaz (kararda
  yok, fail-closed).
- Varsayılan kimlik bilgisi içermez; repo, seed, `appsettings*` veya örnek dosyaya kullanıcı/parola yazılmaz.
- Audit (her çalıştırma, ret dahil; KIM-D22): olay `identity.founder.recovered` / `Outcome=Refused` (öneri kod).
  Aktör `System` + operatör beyanıdır (kural 5'teki bootstrap ile aynı terim); operatör beyanı işletim sistemi
  kullanıcısını ve makine adını içerir. İçerik: hedef `FounderUserId`, ön koşulu sağlayan şirket kimlikleri, ret
  kodu, korelasyon kimliği, `SourceChannel` (öneri `Cli`) ve geri gelen etkin atamalar; parola, hash veya stamp
  yazılmaz. Şirket kapsamı: başarıda ön koşulu sağlayan her Admin'siz şirket için o şirketin `CompanyId`'siyle bir
  audit satırı yazılır. Retlerde (alt yol yok, `IdentityBootstrapState` yok, `not_needed`, `founder_active`,
  `founder_inactive`, `concurrent_change`, `configuration_missing` (yalnız `reset-password`), TTY yok, parolalar eşleşmedi, politika ihlali, onay verilmedi) sabit veya sentinel
  `CompanyId` **uydurulmaz**; her ret güvenlik kanalına `ScopeKind=Platform`, `CompanyId=null` (ADR-SEC-001) tek
  kayıt olarak yazılır (yazar okuması; KIM-D10 tek kayıt deseni: "her kullanım audit'e" şartı bu kayıtla
  karşılanır). `concurrent_change` ve kilitli yeniden doğrulama retleri asıl transaction geri alındıktan sonra
  **ayrı** transaction'da yazılır. Her koşum yeni bir `IdempotencyKey` ve korelasyon kimliği üretir. Komutun
  izni olmadığından `PermissionCode` için izin adı **uydurulmaz**; `AuditEventDraft`'ın zorunlu `PermissionCode`,
  `EntityType`, `EntityId`, `Operation` alanlarının bu komutta nasıl doldurulacağı ve operatör beyanı bağlam alanı
  Audit sözleşme değişikliğidir (ID-2 içinde ayrı K3 madde).
- Güvenlik olay kanalı: ADR-SEC-001 kapalı listesinde kurtarma için kod yoktur. Eklemek (öneri
  `identity.founder.recovered` ve `identity.founder.recovery_refused`; yüksek `Severity` + alarm metriği, geri
  alınabilir teknik ayar) ADR-SEC-001 sözleşme değişikliğidir (liste + check constraint aynı PR), `account_locked`
  ile aynı desen. Şirket bağlamı olmayan retlerin tek kaydı bu kanalda olduğundan kod ID-2'nin ön şartıdır; komut
  kodsuz merge edilmez. Başarı olayı audit ile aynı SQL transaction'ında yazılır ve **fail-closed**'dur:
  kanal veya audit yazılamazsa transaction geri alınır, hesap değişmez ve komut başarısız olur
  (`identity.permission.changed` deseni; ADR-SEC-001 E1 "yetki değişikliği" sınıfı, BS-11). Retler `authz.denied`
  desenindedir: ret geçerli kalır, kanal yazılamazsa alarm üretilir. Geri gelen etkin atamalar için ayrıca
  `identity.permission.changed` yayınlanıp yayınlanmayacağı aynı ADR-SEC-001 sözleşme değişikliğinin iki
  review'unda kesinleşir (öneri: yayınlanır). Olay kanalı gerçek writer ister; geçici no-op writer
  `recover-founder` için **yeterli değildir**.
- Tek transaction: kullanıcı güncellemesi + kimlik bilgisi + audit + güvenlik olayı.

**`reset-password` alt yolu (KIM-D23, 23. tur: "Sunucu komutu parola sıfırlar"):**

- Ön koşullar (ikisi birden; kararın sorusu: "Kurucu hesabı aktif ama parolasını unuttuysa ve ekrandan
  sıfırlayacak başka aktif Admin yoksa"):
  1. Hedef `FounderUserId` hesabı aktiftir (`IsActive=true`). Pasif kurucuda
     `identity.founder_recovery.founder_inactive` (öneri kod) ile reddeder; pasif kurucunun yolu `reactivate`'tir.
  2. Ekrandan sıfırlayabilecek başka aktif kullanıcı yoktur. Yazar okuması (review'da değişebilir; kilitli
     KIM-D16 ile hizalı, yeni politika seçmez): "ekrandan sıfırlayacak başka aktif Admin" = kurucu dışında, kurucunun
     bugün geçerli ataması olan (KIM-D16 üyelik) **her** şirkette şirket geneli `identity.users.manage` taşıyan
     **tek bir** aktif (`IsActive=true`) kullanıcı. Böyle bir kullanıcı varsa komut
     `identity.founder_recovery.not_needed` ile reddeder (ret kaydı kullanıcıyı değil, yalnız ret kodunu taşır);
     o kullanıcı ekrandan `POST /access/users/{userId}/password-reset` yolunu kullanır. Yetki şirketlere bölünmüşse
     (ör. Ayşe yalnız A'da, Deniz yalnız B'de Admin) KIM-D16 gereği ikisi de ekrandan `403` alır; komut çalışır.
     Kurucunun hiç geçerli ataması yoksa ekrandan sıfırlama KIM-D19 gereği `409
     identity.user.company_assignment_required` ile reddedilir; ön koşul sağlanmış sayılır ve komut çalışır. Bu dal değerlendiriciden önce çalışır.
     Değerlendirici: ön koşul, HTTP `POST /access/users/{userId}/password-reset` yetki kontrolünün kullandığı
     **aynı** KIM-D16 değerlendiricisiyle hesaplanır (ayrı bir kopya yazılmaz); `not_needed` ⇔ kurucu dışında, HTTP yolunun kurucuyu
     gerçekten sıfırlayabildiği (KIM-D16 çiti ve KIM-D19'dan sonra `200`) en az bir aktif kullanıcı vardır. Parity
     testi aynı veri setinde iki yolun aynı sonucu verdiğini doğrular (KS-23); veri seti site kapsamlı atamaları,
     pasif rolleri, süresi dolmuş ve henüz başlamamış atamaları ve `SetupPending` şirketleri içerir.
     Eşzamanlılık: ön koşul kilitli okumayla yeniden doğrulanır. KIM-D17 kilidi yalnız Admin sayısını azaltan
     işlemleri sıralar; Admin olmayan bir rolden gelen `identity.users.manage` iznini kaldıran işlemler bu kilidi
     almaz. İki yön de güvenlidir: yeniden okumadan sonra izin artarsa sonuç gereksiz ama audit'li bir sıfırlamadır;
     azalırsa en kötü durum `not_needed` reddi ve yeniden çalıştırmadır.
     Bilinen sınır (`NEEDS-SINAN` adayı; koordinatör Sinan'a sorabilir, bu spec seçmez): yetkili diğer kullanıcı da
     giriş yapamıyorsa (parolasını unutmuş, kilitli, 2FA cihazını kaybetmiş) komut yine `not_needed` döner; bu tam
     kilitlenme durumu için kararlı bir yol yoktur.
- Adım sırası (kilitsiz ön okuma → kilit dışında etkileşim ve onay → bütün şirket kilitleri + kurucu satırı altında
  yeniden doğrulama → `Version` token'lı yazma), `concurrent_change`, TTY, iki kez giriş ve ret kayıtları
  `reactivate` ile aynıdır; `--yes`/`--force` yoktur. `configuration_missing` adım 1'de (parola istenmeden önce)
  ve adım 3'te kilit altında yeniden kontrol edilir.
- Girilen parola geçicidir: başarıda parola hash'i değişir, `MustChangePassword=true` olur, kilit sıfırlanır,
  security stamp yenilenir (eski oturumlar düşer). Kurucu ilk girişte yeni parola belirlemeden `MustChangePassword`
  çiti dışındaki hiçbir işlemi yapamaz. "Tek kullanımlık" (koordinatör notu, Sinan metni değil) şöyle tanımlanır
  (yazar okuması, fail-closed; review'da değişebilir): geçici parola **ilk başarılı girişte tüketilir**; yalnız o
  çitli oturumdaki `POST /api/v1/identity/session/password` çağrısı için geçerli kalır. Değişim o oturumda
  yapılmazsa (çıkış, oturum süresi dolması) geçici parolayla yeni giriş, var olmayan kullanıcıyla aynı genel
  hatayla reddedilir ve komutun yeniden çalıştırılması gerekir. Değişimden sonra geçici parola zaten geçersizdir.
  Tüketim atomiktir: komut `TemporaryPasswordIssuedAtUtc`'yi ve `TemporaryPasswordExpiresAtUtc`'yi yazar,
  `TemporaryPasswordConsumedAtUtc`'yi null'lar; oturum açılırken tek koşullu
  `UPDATE … SET TemporaryPasswordConsumedAtUtc = now WHERE TemporaryPasswordConsumedAtUtc IS NULL AND
  TemporaryPasswordIssuedAtUtc = @okunan AND PasswordHash = @okunan AND SecurityStamp = @okunan` çalışır,
  etkilenen satır 1 değilse oturum açılmaz (genel hata); arada verilmiş yeni bir sıfırlama böylece ezilmez. İki eşzamanlı girişten yalnız biri geçer. 2FA açıksa
  tüketim parola adımında değil, 2FA da geçip oturum açılırken sayılır (yarım kalan giriş parolayı tüketmez).
  Tüketilmiş veya süresi dolmuş geçici parolayla deneme, yanlış parola gibi başarısız deneme sayılır
  (`FailedAccessCount` artar, aynı genel hata). `reset-password` dışındaki her parola yazımı (oturumdaki parola
  değişimi, HTTP `password-reset`, `POST /access/users`, `reactivate`, bootstrap) üç `TemporaryPassword*` alanını
  null'lar. Tüketim/süre kontrolü yalnız `TemporaryPasswordIssuedAtUtc` doluyken çalışır; doluyken
  `TemporaryPasswordExpiresAtUtc` boşsa giriş fail-closed reddedilir.
  Hiç kullanılmayan geçici parolanın geçerlilik süresi (TTL) `ASSUMPTION-ID-TMP-1` adayıdır (geri alınabilir
  teknik ayar, öneri `Identity:FounderRecovery:TemporaryPasswordLifetime`); değer bu spec'te seçilmez. Ayar
  yoksa veya geçersizse `reset-password` hesapta değişiklik yapmadan reddeder (öneri kod
  `identity.founder_recovery.configuration_missing`; ret kaydı yazılır) — süresiz geçici parola üretilmez
  (fail-closed).
- Yalnız parola sıfırlanır: `IsActive`, rol tanımları, atamalar, `IdentityBootstrapState`, KIM-D5 istisnası ve
  2FA durumu **değişmez** (koordinatör notu #66 `6823243819`: "rol/atama değiştirmez"). 2FA cihazını da kaybetmiş
  kurucu bu kararın kapsamı dışındadır; komut 2FA'yı kapatmaz veya sıfırlamaz (fail-closed). Gerekirse ayrı
  `NEEDS-SINAN` olur; bu spec seçmez.
- Parola, hash veya stamp hiçbir log, audit, güvenlik olayı, konsol çıktısı veya dosyaya yazılmaz; repo, seed,
  `appsettings*` veya örnek dosyaya kullanıcı/parola girmez.
- Audit (her çalıştırma, ret dahil): olay `identity.founder.password_reset` / `Outcome=Refused` (öneri kod); aktör,
  operatör beyanı, `SourceChannel` (öneri `Cli`) ve korelasyon `reactivate` ile aynıdır. Parola grup geneli
  olduğundan başarıda kurucunun bugün geçerli ataması olan **her** şirkete o şirketin `CompanyId`'siyle bir audit
  satırı yazılır (aynı transaction; `reactivate`'teki geri gelen atama kuralının karşılığı); içerik hedef
  `FounderUserId` ve şirket kimlikleridir. Başarıda her durumda aynı transaction'da tek bir `ScopeKind=Platform`,
  `CompanyId=null` başarı güvenlik olayı yazılır; şirket audit satırları buna eklenir. Kurucunun hiç geçerli
  ataması yoksa şirket audit satırı yazılmaz ve "her kullanım audit'e" şartı bu Platform olayıyla karşılanır
  (fail-closed: yazılamazsa sıfırlama geri alınır). Retler `reactivate`'teki gibi güvenlik kanalına `ScopeKind=Platform`,
  `CompanyId=null` tek kayıt olarak yazılır; `IdempotencyKey`, korelasyon ve `PermissionCode` kuralı aynıdır.
- Güvenlik olay kanalı: öneri kod `identity.founder.password_reset` (yüksek `Severity` + alarm metriği) aynı
  ADR-SEC-001 sözleşme değişikliğine eklenir; ret kodu `identity.founder.recovery_refused` ortaktır. Başarı olayı
  fail-closed ve aynı transaction'dadır, retler ret geçerli + alarm; `reactivate` ile aynı kural.
- Tek transaction: kimlik bilgisi + kullanıcı güncellemesi + audit + güvenlik olayı.

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

## Varsayılan kimlik bilgisi yasağı

1. **Repoda varsayılan yok:** kod, `appsettings*.json`, `Seed/*.json`, `.env.example`, `infra/**` compose
   dosyaları, test fixture'ları ve dokümanlarda kullanılabilir kullanıcı adı + parola çifti bulunmaz. Testler
   parolayı çalışma anında rastgele üretir.
2. **Bootstrap operatör girdisi ister:** varsayılan parola yoktur; boş/argümanla verilen parola reddedilir.
3. **Yasaklı/sızmış parola listesi:** yaygın varsayılan parolalar, kullanıcı adının kendisi ve sızmış parolalar
   politika tarafından reddedilir (KIM-D4 korur). Tek istisna madde 4'teki **yalnız Development + yerel ayar**
   durumudur; test (`Testing`/Staging) ve canlı (Production) ortamda liste kapatılamaz ve varsayılan/zayıf kimlik
   bilgisi hiç kabul edilmez.
4. **Yerel Development istisnası (KIM-D6, 8. tur):** varsayılan/zayıf kimlik bilgisi yalnız yerel Development
   ortamında, kullanıcı gizli ayarı (user-secrets) ile kabul edilebilir; repoya, örnek dosyaya, seed'e,
   `appsettings*`'e girmez; Development dışında hiç kabul edilmez. Bunu açan ayar
   (`Identity:LocalAccount:DevelopmentAllowWeakPassword=true`, öneri ad) yalnız yerel kullanıcı-gizli dosyasında
   (`dotnet user-secrets`) veya yerel ortam değişkeninde durur. Ayar Development dışında `true` ise uygulama
   **başlamaz** (mevcut `DevelopmentBootstrap` startup kontrolünün deseni). İstisna yalnız madde 3'teki listeyi
   gevşetir; bootstrap yine operatörün etkileşimli girdiği parolayı ister (madde 2) ve ilk girişteki zorunlu
   değişim yine çalışır. Karar gereği başka kullanıcı bağlanmadan önce zayıf parola kalmaz; öneri uygulama:
   zayıf parolayla kabul edilmiş hesap `WeakPasswordAccepted=true` işaretlenir ve bu işaret dururken ikinci bir
   yerel kullanıcı oluşturma ve başka bir kullanıcıya giriş reddedilir
   (`identity.weak_founder_password_must_change`, öneri kod). Eski `NEEDS-SINAN-ID-09` kapandı.
5. **Guard testleri (ID-1/ID-2 ile birlikte):**
   - Repo taraması (öneri `tests/default_credential_guard/`): kod, seed, `appsettings*.json`, `.env.example`,
     `infra/**` compose ve test fixture dosyalarında bilinen varsayılan kullanıcı adı/parola çiftleri ve
     `Password`/`PasswordHash` alanlı yerel kimlik bilgisi satırları bulunursa kırmızı. Bu spec gibi yasağı
     yalnız **adıyla anan** dokümanlar (`docs/**`) kimlik bilgisi değildir; taramanın doküman istisnası dar
     tutulur (yalnız `docs/**/*.md`, kullanılabilir yapılandırma biçiminde değil) ve istisna listesi review'da
     görünür.
   - Integration: `Production`/`Staging`/`Testing` ortamında `DevelopmentAllowWeakPassword=true` ile startup
     hatası; bu ortamlarda yasaklı listedeki parola hem bootstrap'ta hem zorunlu değişimde reddedilir; bootstrap ikinci
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
And komut ikinci kez çalıştırıldığında hiçbir şey yazmadan reddeder; stdin terminal değilken veya iki parola
eşleşmezken de hiçbir şey yazmadan reddeder.

**KS-6 — Varsayılan/zayıf kimlik bilgisi Development dışında kabul edilmez**
Given ortam `Staging` veya `Production`
When biri yasaklı listedeki parolayla hesap oluşturmaya veya `DevelopmentAllowWeakPassword=true` ile başlatmaya çalışır
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
And hedefin üye olduğu her şirkette `identity.users.manage` taşıyan kişi kilidi açtığında sayaç sıfırlanır ve
audit kaydı oluşur.

**KS-9 — Ret de audit'te**
Given Mehmet bir Critical talebi reddeder
Then talep `Rejected` ve `DecidedByUserId=Mehmet` olur, ret audit'e yazılır; yetki değişmediği için
`identity.permission.changed` olayı üretilmez.

**KS-10 — Şirket kapsamı**
Given Ayşe yalnız Şirket A'da `identity.roles.manage` taşıyor
When Ayşe Şirket B'de rol atamaya veya Şirket B talebini onaylamaya çalışır
Then istek reddedilir (şirket dışı kayıt kodsuz 404, şirket bağlamı uyuşmazlığı 403); ret audit'e değil,
güvenlik kanalına tek kayıt olarak yazılır (KIM-D10).

**KS-11 — İsteğe bağlı 2FA (ID-5)**
Given TOTP etkin bir hesap
When doğru parola fakat hatalı TOTP girilir
Then giriş reddedilir ve başarısız deneme sayacına eklenir.

**KS-12 — OIDC kapalıyken etkisiz (ID-6)**
Given `Authentication:Providers` yalnız `LocalAccount` içeriyor
Then OIDC token'ı ile yapılan istek `401` alır; ayar açılıp sağlayıcı yapılandırılınca aynı kullanıcı
`ExternalIdentityLink` üzerinden çözülür.

**KS-13 — Kilit kendiliğinden açılır (KIM-D13)**
Given `Lockout:Duration` = 15 dk ve hesap 10:00'da kilitlendi
When kullanıcı 10:10'da doğru parolayla dener
Then tek tip `401` döner, kilit süresi uzamaz; 10:15'ten sonra doğru parolayla giriş başarılı olur ve sayaç `0`
olur.

**KS-14 — Mutlak oturum süresi (KIM-D14)**
Given `Session:AbsoluteLifetime` = 12 saat, `Session:IdleTimeout` = 8 saat ve kullanıcı her 10 dakikada işlem yapıyor
When girişten 12 saat sonra yeni istek gelir
Then istek `401` alır ve yeniden giriş istenir.

**KS-15 — Critical rol tek adımda geri alınır (KIM-D15)**
Given Can'ın Critical "Finans Onaycısı" ataması var ve Ayşe `identity.roles.manage` taşıyor
When Ayşe atamayı geri alır
Then atama ikinci onay beklemeden `Revoked` olur, audit ve `identity.permission.changed` yazılır; Can'ın bir
sonraki isteği bu izinle reddedilir.

**KS-16 — Grup geneli işlemde eksik şirket (KIM-D16)**
Given Can Şirket A ve Şirket B'de üye, Ayşe yalnız Şirket A'da `identity.users.manage` taşıyor
When Ayşe Can'ın parolasını sıfırlamaya, kilidini açmaya veya hesabını pasifleştirmeye çalışır
Then `403` döner; parola, kilit ve hesap değişmez, ret güvenlik kanalına yazılır.

**KS-17 — Hiçbir şirkette rolü olmayan hedef (KIM-D19)**
Given Deniz'in bugün geçerli hiçbir ataması yok (yalnız süresi dolmuş bir atama var)
When Şirket A'da `identity.users.manage` taşıyan Ayşe (veya kurucu Admin) Deniz'in parolasını sıfırlamaya,
kilidini açmaya ya da Deniz'i pasifleştirmeye çalışır
Then `409 identity.user.company_assignment_required` döner, parola, kilit ve hesap değişmez, ret işlemi yapanın
şirket bağlamında denetime bir kez yazılır;
And Deniz Şirket A'ya atandıktan sonra Ayşe aynı işlemi yapabilir.

**KS-18 — Son Admin korunur (KIM-D17)**
Given Şirket A'da tek aktif Admin Ayşe
When Ayşe kendi Admin atamasını geri almaya ya da biri Ayşe'yi pasifleştirmeye çalışır
Then `409 identity.last_admin_required` döner, ret audit'e yazılır, atama ve hesap değişmez;
And Mehmet'e Admin verildikten sonra Ayşe'nin ataması geri alınabilir;
And Şirket A'da Ayşe'nin yanında yalnız 30 gün süreli Admin ataması olan Emre varsa Ayşe'nin geri alınması
yine `409` alır (süreli atama sayılmaz);
And Şirket A'da iki Admin birbirinin atamasını aynı anda geri almaya çalıştığında yalnız biri başarılı olur;
And iki Admin'den birinin pasifleştirilmesi ile diğerinin atamasının geri alınması aynı anda geldiğinde yalnız
biri başarılı olur (gerçek SQL eşzamanlılık testleri).

**KS-19 — Kurucu bütün şirketlerde Admin (KIM-D18)**
Given kurulumda üç şirket tanımlı (biri pasif) ve bootstrap henüz çalışmadı
When operatör bootstrap komutunu çalıştırır
Then kurucunun üç şirketin her birinde Admin ataması oluşur ve her biri audit'e yazılır.

**KS-20 — Sonradan açılan şirkette açan kişi ilk Admin (KIM-D18)**
Given bootstrap tamamlandı ve Ayşe Organization'da yeni Şirket D'yi açıyor
When şirket açılış olayı Identity'ye ulaşır
Then Ayşe Şirket D'de ikinci onay beklemeden süresiz Admin olur, audit yazılır; olay ikinci kez gelirse yeni
atama oluşmaz;
And olay işlenene kadar Şirket D "kurulum bekliyor" durumundadır ve işlem kabul etmez; ilk Admin yazılınca
Şirket D `Active` olur;
And Ayşe olay işlenmeden pasifleştirildiyse atama yazılmaz, olay yeniden deneme sonrası dead-letter'a düşer,
alarm ve "kurulum bekleyen şirket" metriği görünür; Şirket D kurulum bekliyor kalır;
And kurucu Admin Şirket D'ye Mehmet'i elle Admin atayınca (ikinci onaysız, audit'li) Şirket D `Active` olur
(KIM-D20);
And Ayşe'nin açılış olayı ile kurucunun elle Admin ataması aynı anda gelirse yalnız biri ilk Admin'i yazar,
diğeri hiçbir şey yazmaz (`CompanyOpener`) ya da `409 identity.company.admin_already_assigned` alır
(`FounderDirect`); `identity.company.first_admin_assigned.v1` tek kez yayınlanır; aynı açılış olayı eşzamanlı
iki kez teslim edilirse tek atama oluşur (gerçek SQL);
And Şirket D `Active` iken kurucu (üye değil) Admin ataması denerse `403` alır; `SetupPending` iken Admin dışı
rol `409 identity.company.first_admin_role_required` alır.

**KS-21 — Parola değişimi açık oturumları kapatır**
Given Can iki cihazda oturum açmış
When Can parolasını değiştirir ya da yetkili Can'ın parolasını sıfırlar veya hesabını pasifleştirir
Then diğer cihazdaki oturumun bir sonraki isteği `401` alır (security stamp uyuşmaz).

**KS-22 — Kurucu pasif ve şirkette Admin yokken sunucu kurtarma komutu (KIM-D22)**
Given kurucu pasifleştirilmiş, Şirket E `SetupPending` ve hiç etkin Admin'i yok
When sunucuya erişimi olan operatör `recover-founder` komutunu çalıştırıp yeni parolayı etkileşimli girer
Then kurucu hesabı aktif olur, `MustChangePassword=true`, eski oturumları `401` alır ve audit'te
`identity.founder.recovered` kaydı vardır (parola yok); kurucu parolasını değiştirmeden başka işlem yapamaz,
değiştirdikten sonra Şirket E'ye `FounderDirect` ile Admin atar ve şirket `Active` olur;
And kurucu aktifken `reactivate` alt yolu `identity.founder_recovery.founder_active` ile reddeder, parola ve oturumlar değişmez;
başarılı koşumdan sonra kurucu Admin atamadan komut ikinci kez çalıştırılırsa aynı kodla reddeder ve ilk parola
geçerli kalır;
And her şirkette etkin Admin varken komut `identity.founder_recovery.not_needed` ile reddeder;
`IdentityBootstrapState` yokken reddeder; stdin terminal değilken reddeder; onay verilmezse hesapta ve kimlik
bilgisinde hiçbir değişiklik yapmadan çıkar, yalnız ret kaydı yazılır; bu retler `ScopeKind=Platform`, `CompanyId=null` güvenlik kaydı olarak yazılır
(sentinel `CompanyId` yok);
And aynı işlemi yapan HTTP ucu veya ekran yoktur (OpenAPI'de ve shell kayıtlarında bulunmadığı testle
doğrulanır); komut parolayı argüman, ortam değişkeni veya dosyadan kabul etmez.
And operatör onay ekranında beklerken hiçbir şirket kilidi veya kurucu satırı kilidi tutulmaz (eşzamanlı bir
Admin geri alma bloke olmaz); onaydan sonra ön koşul değişmişse ilgili kodla reddeder;
And başarı güvenlik olayı yazılamazsa transaction geri alınır ve hesap pasif kalır.
Komut ve ret yolları ID-2'de test edilir; `SetupPending` ve `FounderDirect` adımı ID-3b ile uçtan uca doğrulanır.

**KS-23 — Aktif kurucu parolasını unuttu, başka aktif Admin yok (KIM-D23)**
Given kurucu aktif, Şirket A'da tek aktif Admin kurucudur ve kurucu parolasını unutmuştur
When sunucuya erişimi olan operatör `recover-founder reset-password` alt yolunu çalıştırıp geçici parolayı iki kez
etkileşimli girer ve onaylar
Then kurucunun parolası değişir, `MustChangePassword=true`, eski oturumları `401` alır, audit'te
`identity.founder.password_reset` kaydı vardır (parola, hash veya stamp yok); kurucu girişte yeni parola belirlemeden
başka işlem yapamaz, belirledikten sonra geçici parola ile giriş reddedilir;
And kurucunun `IsActive`, rol atamaları ve 2FA durumu değişmez;
And kurucu kilitliyken (`LockoutEndUtc` gelecekte) de komut çalışır ve kilit sıfırlanır;
And kurucu A ve B'de üye, A'daki tek diğer Admin Ayşe B'de, B'deki tek diğer Admin Deniz A'da yetkisizken
(ikisi de ekrandan `403` alır) komut çalışır; bir kullanıcı A ve B'nin ikisinde de şirket geneli
`identity.users.manage` taşıyorsa komut `identity.founder_recovery.not_needed` ile reddeder; kurucunun hiç geçerli
ataması yoksa komut çalışır ve yalnız `ScopeKind=Platform`, `CompanyId=null` başarı güvenlik olayı yazılır
(şirket audit satırı yok);
And aynı kullanıcı/atama veri setlerinde ön koşul ile HTTP `password-reset` yetki kontrolü aynı sonucu verir
(parity testi; Admin olmayan rolden gelen `identity.users.manage`, site kapsamlı atamalar, pasif roller, süresi
dolmuş ve henüz başlamamış atamalar ve `SetupPending` şirketler dahil);
And kurucu `Oluşturuldu` durumundayken (önceki sıfırlamanın geçici parolası hiç kullanılmamış) komut yeniden çalışır;
And `Identity:FounderRecovery:TemporaryPasswordLifetime` yokken komut `configuration_missing` ile hesapta
değişiklik yapmadan reddeder; süre dolduktan sonra geçici parolayla giriş reddedilir;
And geçici parolayla iki eşzamanlı girişten yalnız biri oturum açar; 2FA açık kurucuda 2FA adımı tamamlanmadan
geçici parola tüketilmez; tüketilmiş geçici parolayla deneme `FailedAccessCount`'u artırır;
And KIM-D23 geçici parolası tüketildikten sonra `reactivate` veya HTTP `password-reset` ile verilen parolayla
giriş başarılıdır (iki yol `TemporaryPassword*` alanlarını null'lar); `TemporaryPasswordIssuedAtUtc` dolu ve
`TemporaryPasswordExpiresAtUtc` boşken giriş reddedilir;
And tüketim ∥ yeni `reset-password` yarışında tüketen girişin koşullu `UPDATE`'i 0 satır etkiler ve giriş genel
hatayla reddedilir; yeni sıfırlamanın hash'i yeniden hash ile ezilmez;
And `configuration_missing` parola istenmeden önce ve kilit altında yeniden kontrol edilir;
And onay beklerken kurucu adına başarısız giriş denemeleri koşumu `concurrent_change`'e düşürmez;
And kurucu geçici parolayla girip değiştirmeden çıkarsa aynı geçici parolayla yeni giriş reddedilir;
And iki eşzamanlı `reset-password` koşumundan biri başarılı olur, diğeri `identity.founder_recovery.concurrent_change`
ile reddeder; onay ile yazma arasında kurucu parolasını ekrandan değiştirirse koşum aynı kodla reddeder; sonraki
başarılı her koşum yeni bir sıfırlamadır, önceki geçici parolayı geçersiz kılar ve oturumları yeniden düşürür;
And başarıda tek bir Platform başarı güvenlik olayı ve kurucunun geçerli ataması olan her şirkette bir audit
satırı vardır;
And kurucu pasifken `reset-password` `identity.founder_recovery.founder_inactive` ile reddeder; alt yolsuz çağrı,
stdin terminal değilken veya onay verilmezse hesapta değişiklik yapmadan reddeder (yalnız ret kaydı); şirket bağlamı olmayan retler
`ScopeKind=Platform`, `CompanyId=null` güvenlik kaydıdır;
And aynı işlemi yapan HTTP ucu veya ekran yoktur (OpenAPI'de ve shell kayıtlarında bulunmadığı testle
doğrulanır); komut parolayı argüman, ortam değişkeni veya dosyadan kabul etmez.

## Definition of Done

- Karar referansı (#66 `6818566934`, `6820046672`, `6820237902`, `6820838938`, `6821105608`, `6821262098`,
  `6821897620`, `6822303233`, `6823243819`; `SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md` KIM-D1…D23; BS-6, BS-7, ADR-004, ADR-SEC-001) PR gövdesinde.
- CompanyId ve yetki testleri (şirket dışı, kapsam dışı, izin yok, SoD).
- Gerçek SQL kanıtı: check constraint'ler, Down reddi, bootstrap tekilliği, doğrudan Critical yazma yolunun
  olmadığı, `Bootstrap`/`FounderDirect`/`CompanyOpener` modlarının kendi yolları dışında yazılamadığı, son Admin
  eşzamanlılık koruması (geri alma ∥ geri alma, pasifleştirme ∥ geri alma), ilk Admin yarışı (`CompanyOpener` ∥
  `FounderDirect`, eşzamanlı çift teslim; tek atama ve tek `first_admin_assigned.v1`), `recover-founder` ret yolları (`founder_active`, `founder_inactive`,
  `not_needed`, alt yol yok, eşzamanlı yazma), `reset-password` bölünmüş Admin ön koşulu (çalışır / `not_needed`
  çifti), ön koşul ile HTTP `password-reset` değerlendiricisi parity testi, atamasız kurucuda Platform başarı
  olayı, geçici parolanın atomik tüketimi (iki eşzamanlı giriş), TTL ayarı yokken ret, başarısız girişin
  `Version`'ı değiştirmediği, iki eşzamanlı `reset-password` (biri `concurrent_change`), iki alt yolun kilitli ön koşul yeniden doğrulaması, onay beklerken kilit
  tutulmadığı ve başarı güvenlik olayı yazılamazsa geri alma; `bootstrap-founder` TTY yokken ret.
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
| **ID-2** | `MustChangePassword` çiti + `/session/password`, `IdentityBootstrapState` + migration, `bootstrap-founder` ve `recover-founder` komutları (`reactivate` + `reset-password` alt yolları, etkileşimli parola; KIM-D22, KIM-D23, KS-22, KS-23), `/me` sözleşme sürümü | Codex | K3 | ID-1; kurucu Admin istisnası KIM-D5; bütün şirketlerde Admin KIM-D18; `recover-founder` ayrı PR'dır (öneri ID-2r; ID-3'teki KIM-D17 kilidinden ve gerçek güvenlik olay writer'ından sonra merge edilir), ID-2'nin geri kalanını ve ID-2F'yi bekletmez; KS-22'nin uçtan uca kısmı ID-3b'nin DoD'sindedir; kurtarma güvenlik kodları ve Audit zarf alanları ADR-SEC-001 / Audit sözleşme değişikliği |
| **ID-2F** | Zorunlu parola değiştirme ekranı | Claude | K2 | ID-2 |
| **ID-3** | Atama/geri alma, Critical talep/onay/ret/geri çekme API'si, `AccessGrantRequest` migration'ı, `identity.users.manage` kaydı (grant yok), shell capability'leri, audit + `identity.permission.changed` | Codex | K3 (permission/rol + migration) | ID-1; KIM-D9, KIM-D15, KIM-D16, son Admin KIM-D17 |
| **ID-3b** | Şirket açılış olayıyla açan kişiye ilk Admin ataması (KIM-D18, KIM-D20): Organization `SetupPending` durumu ve şirket bağlamı reddi, `organization.company.opened.v1` outbox olayı + Identity inbox tüketicisi, açan kullanıcı aktiflik kontrolü, retry → dead-letter → alarm, "kurulum bekleyen şirket" metriği, kurucunun `SetupPending` şirkete elle Admin ataması, `identity.company.first_admin_assigned.v1` olayı + Organization tüketicisi (`Active`), `CompanyOpener`/`FounderDirect` dışı yazma yolu olmadığının gerçek SQL testi, şirket kilidi altında ilk Admin yarışı ve 0→1 tek olay testleri, Organization tüketicisinin idempotent geçişi (`SetupPending`→`Active` bir kez; `Active` yok sayılır; inbox dedup), `ICompanyLifecycleReadPortV1` (öneri) fail-closed okuması, şirket bağlamı reddinin başlıksız endpoint/tüketici/arka plan işlerine genişlemesi (Organization/Platform); şirket açma izni Critical olarak kaydedilir (KIM-D21, grant yok) | Codex (Organization + Identity) | K3 (cross-module contract) | ID-3; Organization şirket açma akışı |
| **ID-4** | "Kullanıcılar ve yetkiler" ekranına atama, onay kuyruğu, kullanıcı oluştur/sıfırla/kilit aç eylemleri; kullanıcının son şirket atamasını geri alırken uyarı: "Bu, kullanıcının son şirket ataması; geri alınırsa parola sıfırlama/kilit açma/pasifleştirme yapılamaz (KIM-D19)" | Claude | K2 | ID-3 |
| **ID-5** | İsteğe bağlı TOTP 2FA + kurtarma kodları + migration; UI kayıt akışı ayrı Claude alt dilimi | Codex (+Claude) | K3 | ID-1; zorunluluk KIM-D3 ile kararlı (isteğe bağlı) |
| **ID-6** | `Authentication:Providers` çoklu şema; OIDC varsayılan kapalı; yerel kullanıcıya dış kimlik bağlama (Admin eylemi) | Codex | K2 (migration yoksa; güvenlik yüzeyi nedeniyle iki review önerilir) | ID-1; production sağlayıcısı `OPEN-013` insan kapısı |

## NEEDS-SINAN listesi

8. tur (#66 `6820046672`) ile kapananlar: ID-01 (2FA herkese isteğe bağlı), ID-02/03/06'nın başlangıç
değerleri (8 karakter, 10 hata, boşta 8 saat; Ayarlar'dan değişir), ID-08 (kurucu Admin istisnası), ID-09
(varsayılan/zayıf kimlik bilgisi yalnız yerel Development ortamında). 10. tur (#66 `6820237902`) ile kapananlar: ID-04 ve ID-07'nin
Critical sorusu (yeni işlem Critical'e eklenmez; `identity.users.manage` Critical değil). 14.–15. tur (#66
`6820838938`) ile kapananlar: ID-02b (KIM-D12), ID-03b (KIM-D13), ID-06b (KIM-D14), ID-05 (KIM-D15), ID-07b
(KIM-D16). 16. tur (#66 `6821105608` m.4) ile `NEEDS-SINAN-ID-10` (son Admin, KIM-D17), 17. tur (#66
`6821262098` m.4) ile çok şirkette ilk Admin (KIM-D18), 18. tur (#66 `6821897620`) ile `NEEDS-SINAN-ID-11`
(kurulum bekliyor + kurucu ataması, KIM-D20) ve `NEEDS-SINAN-ID-12` (şirket açma izni Critical, KIM-D21)
kapandı; üyeliği olmayan hedef KIM-D19 ile, kurucu da pasifken kurtarma 20. tur (#66 `6822303233` m.2) ile
sunucu kurtarma komutu olarak (KIM-D22), aktif kurucunun unutulan parolası 23. tur (#66 `6823243819` m.3) ile
sunucu komutuyla parola sıfırlama olarak (KIM-D23) karara bağlandı. Açık kalan iki durum (ikisi de `NEEDS-SINAN` adayı;
koordinatör Sinan'a sorabilir, bu spec seçmez): (1) KIM-D23'te ekrandan sıfırlayabilecek diğer yetkili de giriş
yapamıyorsa komut `not_needed` döner (tam kilitlenme); (2) 2FA cihazını da kaybetmiş kurucu (§`reset-password`). Teknik varsayılanlar `ASSUMPTION-*` adayı olarak işaretlidir (hız sınırı değeri
`ASSUMPTION-ID-RL-1`, farklı parolalı replay `ASSUMPTION-ID-IDEM-1`, KIM-D19/KIM-D16 yanıt ayrımı
`ASSUMPTION-ID-LEAK-1`, kullanılmayan kurtarma geçici parolası süresi `ASSUMPTION-ID-TMP-1`).

- ~~`NEEDS-SINAN-ID-11`~~ — kapandı: KIM-D20 (kurulum bekler, kurucu atar).
- ~~`NEEDS-SINAN-ID-12`~~ — kapandı: KIM-D21 (şirket açma izni Critical; grant değişikliği ayrı karar).
- ~~`NEEDS-SINAN-ID-13`~~ — kapandı: KIM-D23 (sunucu komutu aktif kurucunun parolasını sıfırlar; `reset-password`
  alt yolu).

- (Mevcut, bu spec'in kararı değil) ADR-SEC-001 NEEDS-SINAN-8 / E8: başarısız giriş kullanıcı adı hash
  anahtarı. Anahtar bir secret olduğundan insan kapısıdır (secret yönetimi); gelene kadar hash yazılmaz,
  aktör `Anonymous` kalır (fail-safe, ID-1'i durdurmaz).
