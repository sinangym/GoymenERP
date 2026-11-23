# Platform (kimlik, yetki, şirket/organizasyon, denetim, uygulama kabuğu) — durum ve yapılacaklar

> Kapsam: Identity, Organization, Audit, SecurityEvents, Idempotency modülleri; API'nin Security, Multitenancy,
> Shell, Seed, Startup klasörleri; ön yüzde `identity`, `organization`, `audit`, `shell`. HEAD `ddc6cba`,
> 23 Kasım 2026. Yalnız kod okunarak hazırlandı; derleme ve test koşulmadı. Bu belge karar veya onay değildir.

## 1. Kısa sonuç

Platformun güvenlik altyapısı güçlü ve ayrıntılı yazılmış: yerel kullanıcı adı/parola ile giriş, iki adımlı
doğrulama, oturum süreleri, şirket verisi yalıtımı, rol atama (ikinci kişi onayıyla), denetim kaydı ve şirket
değiştirme kodda var ve bol test kaynağı taşıyor. Buna karşın **üç firmanın test ortamında çalışmaya başlamasını
bugün engelleyen şey bu altyapının "ilk kurulum" ve "iş rolleri" ucudur**:

1. **Sistemde yalnız 5 rol var ve hiçbiri iş rolü değil.** 287 izin adının yalnız 14'ü bir role bağlı; Satış,
   Satın Alma, Stok, Lojistik, Üretim, Finans, Muhasebe, Ürün, Belgeler ve Ayarlar izinlerinin **tamamı (sıfır
   istisna) hiçbir rolde yok**. Bu izinler olmadan ilgili ekranlar menüde hiç çizilmez.
2. **Rol oluşturma / role izin ekleme ekranı ve uç noktası yok.** Roller yalnız başlangıç dosyasından (seed)
   gelir; var olan bir role sonradan izin ekleyen tek kod parçası yalnız testlerden çağrılıyor. Sinan'ın kararı
   "roller Ayarlar ekranından tanımlanır" (SAT-D282 notu) olduğu halde bu ekran yazılmamış.
3. **Yönetici rolü kendi yönetim ekranlarını açamıyor.** "Kullanıcı yönetimi" ve "Kullanıcılar ve yetkiler"
   ekranlarının istediği iki izin (`identity.users.manage`, `identity.assignments.read`) Sistem Yöneticisi
   rolünde de yok. Yani kullanıcı açma ve rol atama ekranları yazılmış ama bugünkü rollerle kimseye görünmüyor.
4. **Geliştirme dışı bir ortamda veritabanı, şirketler ve roller kendiliğinden kurulmuyor;** ilk yönetici
   komutu ise şirket ve rol yoksa çalışmayı reddediyor. Geliştirme ortamında da hazır örnek atama yüzünden aynı
   komutun reddetmesi bekleniyor (çalıştırılarak doğrulanmalı).
5. **Sonradan şirket açma ekranı var, ama onu kullanma hakkını veren kaydı yazan hiçbir yol yok.**

Üç şirket (GDC, GSP, GIN) başlangıç dosyasında tanımlı; fakat tesis/depo/iş merkezi şablonu yalnız GDC'ye
uygulanıyor, GIN "geçici" (VKN'siz) şirket. Bir kullanıcı birden çok şirkette çalışabiliyor ve üst çubuktan
şirket değiştirebiliyor. "Kim ne yaptı" sorusu Denetim kaydı ekranından kısmen cevaplanıyor (kişiye ve tarihe
göre filtre yok); yetki reddi kayıtlarının ekranı yok.

## 2. Hedef: bu modülden ne isteniyor

| Hedef | Kaynak |
|---|---|
| Rol tabanlı yetki + şirket/depo/tutar gibi kapsam kuralları; varsayılan "yetkisiz"; rol şablonu + veri kapsamı + az sayıda kişi istisnası; 13 temel rol ailesi (satış, satın alma, ana veri, depo/sevkiyat, kantar, üretim, kalite, cari/risk, muhasebe, finans, onaylayıcı, denetçi, sistem yöneticisi) | Master §12.1 |
| Kritik görev ayrılıkları, onay motoru (tutar/eşik, vekâlet, zaman aşımı) | Master §12.2–12.3 |
| Her kritik işlemde kim/ne zaman/hangi şirket/önceki-yeni değer/sebep/onay/korelasyon; kayıt değiştirilemez | Master §12.4 |
| Ekranlar: SY-01 Kullanıcılar, SY-02 Roller ve Yetkiler, SY-03 AI yetki taslağı, SY-04 Onay matrisi/eşik/vekâlet, SY-06 Entegrasyon, SY-07 Denetim ve sistem sağlığı, SY-08 Güvenlik/saklama; AV-01 Şirket/tesis/depo/lokasyon | Master §27.12 |
| Menü: 13 başlık; Ayarlar altında "Ana Veriler" ve "Sistem Yönetimi" | KLN-D1, KLN-D2 (3 Kasım 2026) |
| Ortak kabuk: sol menü, üst çubukta şirket/dönem/arama/bildirim/kullanıcı, rol bazlı ana sayfa | Master §5.1, §7.1 |
| Giriş: şimdilik yerel hesap + isteğe bağlı iki adımlı doğrulama; OIDC sonra | KIM-D2, KIM-D3 |
| Parola en az 8 karakter, 10 hatada 15 dk kilit, boşta 8 saat / en çok 12 saat oturum; hepsi Ayarlar'dan değişir | KIM-D4, D12–D14 |
| Admin rolleri ekrandan atar; Critical (kritik) rolde ikinci kişi onayı; kurucu yönetici istisnası; son yönetici korunur | KIM-D1, D5, D15, D17 |
| Kurucu, o an tanımlı bütün şirketlerde yönetici olur; sonradan şirketi yetkili kişi açar ve ilk yöneticiyi açıkça seçer | KIM-D18, KIM-D56 |
| Kurucu kurtarma yalnız sunucu komutuyla | KIM-D22, D23, D27, D54 |
| Reddedilen bütün işlem denemeleri kayda geçer; yetki retleri güvenlik olay kanalında; 2 yıl saklama | KIM-D7, D8, D10, D25, D26 |
| Roller kodla değil **Ayarlar rol ekranından** tanımlanır ve izinler kurulumda oradan verilir | SAT-D282 notu (10 Kasım 2026), `docs/operations/CANLI_ORTAM_KURULUM_REHBERI_v1.md` §4.6 |
| Şirket VKN'si zorunlu, değişiklik hazırlayan ≠ onaylayan; GIN "VKN girilmemiş" | KIM-D28, D35, D36, D40 |
| Her kritik kayıtta şirket kimliği; şirket filtresi yalnız ekranda değil | AGENTS.md mimari değişmezler, ADR-002 |

## 3. Bugün var olan

Seviye: 1 = kod yazılmış; 1-kısmi = akışın bir bölümü var. "Test kaynağı" sütunu testin var olduğunu söyler,
geçtiğini değil.

### 3.1 Giriş, parola, oturum

| Yetenek | Arka uç | Ekran | Menüden erişim | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Yerel hesapla giriş / çıkış | Var | Var | Giriş ekranı 401'de açılır | Var | 1 | `GoymenERP.Api/Endpoints/IdentityEndpoints.cs` (`/session/login`, `/session/logout`), `Security/LocalAccountSession.cs`, ön yüz `identity/LoginPage.tsx`, `LogoutControl.tsx`. Ön yüzde `VITE_AUTH_MODE=LocalAccount` ile derlenmeli (`shell/authMode.ts`). |
| Kilit, deneme sınırı, CSRF koruması | Var | — | — | Var | 1 | `appsettings.json` → `Identity:LocalAccount` (10 hata / 15 dk; dakikada 10 deneme), `Security/LocalAccountLoginRateLimiter.cs`, `LocalAccountAntiforgery.cs` |
| Oturum süresi (boşta 8 saat, en çok 12 saat) | Var | — | — | Var | 1 | `appsettings.json` → `Session`; `LocalAccount/LocalAccountSessionValidator.cs` |
| Zorunlu parola değişimi (ilk giriş / sıfırlama sonrası) | Var | Var | Otomatik açılır | Var | 1 | `Security/LocalAccountPasswordChangeFence.cs`, `identity/ChangePasswordPage.tsx` |
| Kullanıcının kendi isteğiyle parola değiştirmesi | Var (`POST /session/password`) | **Bağlantı yok** | Yok | — | 1-kısmi | `ChangePasswordPage` yalnız `shell/AppShell.tsx` içinde zorunlu durumda çiziliyor; gönüllü değişim için menü/bağlantı araması 0 sonuç. |
| İki adımlı doğrulama (TOTP, isteğe bağlı) | Var | Var | Yalnız ana sayfadaki bağlantı | Var | 1 | `IdentityEndpoints.TwoFactor.cs`, `LocalAccount/LocalTwoFactorServiceV1.cs`, `identity/LocalTwoFactorPage.tsx`; bağlantı `shell/ShellHome.tsx` |
| Geliştirme girişi (başlıkla kullanıcı seçme) | Var | — | — | Var | 1 | `Security/DevelopmentAuthenticationHandler.cs`; yalnız Development/Testing ortamı. Kullanıcı ön yüz **derlenirken** sabitlenir (`VITE_DEV_USER`), çok kullanıcılı deneme için uygun değil. |
| OIDC (Microsoft/Google) | Yalnız arka uç | **Yok** | — | Var (arka uç) | 1-kısmi | `Security/ErpAuthenticationExtensions.cs` JWT doğrulaması var; ön yüzde `Bearer` gönderen kod araması 0 sonuç. `appsettings.json` varsayılan modu `OidcJwtBearer`, adresleri boş. |
| Parola/kilit/oturum değerlerini Ayarlar ekranından değiştirme | Yalnız deneme sınırı | Yalnız deneme sınırı | Var | Var | 1-kısmi | `settings/LoginRateLimitPage.tsx`; parola uzunluğu, kilit ve oturum süreleri yalnız sunucu ayar dosyasında (KIM-D4 "Ayarlar'dan değişir" karşılanmıyor). |

### 3.2 Kullanıcı ve rol

| Yetenek | Arka uç | Ekran | Menüden erişim | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Kullanıcı açma, parola sıfırlama, kilit açma, pasifleştirme | Var | Var | Ayarlar › Kullanıcı yönetimi (izin hiçbir rolde yok) | Var | 1 | `IdentityEndpoints.UserManagement.cs`, `identity/IdentityUserManagementPage.tsx` |
| Kullanıcıyı yeniden aktifleştirme, ad/e-posta düzeltme | **Yok** | Yok | — | — | 0 | Uçlar yalnız `password-reset`, `unlock`, `deactivate`; `reactivate` araması 0 sonuç (yalnız kurucu için sunucu komutu var). |
| Kullanıcı ve atama listesi, rol kartı (salt okunur) | Var | Var | Ayarlar › Kullanıcılar ve yetkiler (izin hiçbir rolde yok) | Var | 1 | `IdentityEndpoints.cs` (`/access/users`, `/access/roles`), `identity/IdentityAccessPage.tsx`, `IdentityRoleDetailPage.tsx` |
| Rol atama / geri alma | Var | Var | Yukarıdaki iki ekranın içinde | Var | 1 | `IdentityEndpoints.AccessAssignments.cs`, `identity/AccessGrantPanel.tsx`. Atama yalnız şirket geneli kapsamla yapılır (`IdentityAccessAssignmentService.Rules.cs:149`); tesis/depo kapsamlı atama ekrandan verilemez. |
| Kritik rolde ikinci kişi onayı (talep, onay, ret, geri çekme) | Var | Var | Aynı ekran | Var | 1 | `/access/grant-requests*`; `AccessGrantQueue` |
| Son yönetici koruması | Var | Hata metni var | — | Var | 1 | `Services/IdentityAdminLock.cs`; problem kodu `identity.last_admin_required` |
| **Rol oluşturma, rol düzenleme, role izin ekleme/çıkarma** | **Yok** | **Yok** | — | — | **0** | `CreateRole`, `UpdateRole`, `RolePermission` yazan uç/servis araması: yalnız `Seeding/IdentityDataSeeder.cs` ve `Seeding/SystemRolePermissionReconciler.cs`. İkincisi kayıtlı ama yalnız testlerden çağrılıyor (`tests/.../IdentityPermissionReconciliationSqlServerRelationalAcceptanceTests.cs`). |
| İzin kataloğu | Var | Rol kartında görünür | — | Var | 1 | `BuildingBlocks/.../Security/ErpPermissions.cs`: 287 izin, 17'si Critical |
| AI yetki taslağı (SY-03), onay matrisi/vekâlet (SY-04) | Yok | Yok | — | — | 0 | Identity'de `Delegat`, `Vekalet` araması 0 sonuç. |

### 3.3 Şirket ve organizasyon

| Yetenek | Arka uç | Ekran | Menüden erişim | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Üç şirketin başlangıç kaydı | Var (seed) | — | — | Var | 1-kısmi | `GoymenERP.Api/Seed/organization-seed.json`: GDC ve GSP gerçek VKN'li; GIN geçici, fatura kesemez. Yalnız Development/Testing ortamında yüklenir (`Program.cs:669-671`). |
| Tesis / depo / iş merkezi şablonu | Var (seed) | — | — | Var | 1-kısmi | Aynı dosyada `ApplyTopologyTemplates` yalnız GDC'de `true`; GSP ve GIN tesis/deposuz başlar. |
| Şirket değiştirme, kullanıcının birden çok şirkette çalışması | Var | Var | Üst çubukta "Şirket" seçici | Var | 1 | `Multitenancy/CompanyContextMiddleware.cs` (istekte `X-Company-Id`, üyelik kontrolü), `shell/AppShell.tsx`, `SwitchConfirmDialog.tsx`. Atamalar şirket bazlı tutulur. |
| Tesis, lokasyon (depo), iş merkezi görme | Var | Var | Ayarlar › Ana Veriler | Var | 1 | `Endpoints/OrganizationEndpoints.cs`, `organization/OrganizationSettingsPage.tsx`, `WorkCenterListPage.tsx` |
| Tesis, lokasyon, iş merkezi açma/düzenleme; iş merkezi takvimi | Var | Var | Aynı ekranlar (yazma izinleri hiçbir rolde yok) | Var | 1 | `organization/MasterRecordForm.tsx`, `WorkCenterCalendarSection.tsx` |
| Şirket VKN değişikliği (hazırlayan ≠ onaylayan) | Var | Var | Ayarlar › Ana Veriler (izinler hiçbir rolde yok) | Var | 1 | `OrganizationEndpoints.CompanyTaxId.cs`, `organization/CompanyTaxIdPage.tsx` |
| Sonradan şirket açma + ilk yöneticiyi seçme | Var | Var | Menüde yok; `/kurulum/sirketler` | Var | 1-kısmi | `OrganizationEndpoints.CompanyOpening.cs`, `IdentityEndpoints.CompanySetup.cs`, `organization/CompanySetupPage.tsx`. Yetki kaydını yazan yol yok (bkz. §4). |
| Şirket kartı (adres, vergi dairesi, iletişim), şirket pasifleştirme | Yok | Yok | — | — | 0 | `Organization.Domain/Companies/Company.cs` alanları: kod, unvan, VKN, geçici/fatura bayrakları, durum. Logo/antet Belgeler modülünde. |

### 3.4 Kurulum ve kurtarma komutları

| Yetenek | Arka uç | Ekran | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|
| Kurucu yönetici oluşturma (`identity bootstrap-founder`) | Var | Sunucu komutu | Var | 1-kısmi | `Startup/IdentityCommandLine.cs`, `Bootstrap/FounderBootstrapService.cs`. Ön koşullar §4.1'de. |
| Kurucu kurtarma (yeniden aktifleştirme, parola sıfırlama) | Var | Sunucu komutu | Var | 1 | `Bootstrap/FounderRecoveryService.cs` |
| İki adımlı doğrulama kurtarma | Var | Sunucu komutu | Var | 1 | `LocalAccount/LocalTwoFactorOperatorRecoveryServiceV1.cs` |

### 3.5 Şirket verisi yalıtımı

| Yetenek | Kanıt | Test kaynağı | Seviye |
|---|---|---|---|
| Her istekte şirket seçimi doğrulanır; üye olunmayan şirket 403 alır ve güvenlik olayına yazılır | `Multitenancy/CompanyContextMiddleware.cs` | `tests/GoymenERP.Api.IntegrationTests/CompanyIsolationTests.cs`, `CompanyDirectory*Tests.cs` | 1 |
| Veritabanı sorgularında otomatik şirket filtresi | Modüllerde 432 `HasQueryFilter` satırı, 17 modülün hepsinde | Modül birim testleri | 1 |
| Filtreyi atlayan her kod yerinin elle onaylı parmak izi listesi | `tests/GoymenERP.ArchitectureTests/PlatformTenantQueryFilterBypassGuard*Tests.cs` (4 dosya) | Kendisi test | 1 |
| Gerçek SQL Server üzerinde şirketler arası sızıntı denemeleri | `tools/relational_inventory/identity.txt` (52), `organization.txt` (38), `platform.txt` (181 senaryo adı) | Var | 1 |
| Modüller arası bağımlılık sınırı | `tests/GoymenERP.ArchitectureTests/ModuleDependencyGuardTests.cs` | Var | 1 |

### 3.6 Denetim, güvenlik olayı, tekrar koruması

| Yetenek | Arka uç | Ekran | Menüden erişim | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Denetim kaydı yazımı (kim, ne zaman, önce/sonra, gerekçe, korelasyon, bütünlük özeti) | Var | — | — | Var | 1 | `Modules/Audit/.../Domain/Events/AuditEvent.cs`, `AuditEventIntegrity.cs`; 17 modülden yazılıyor. |
| Denetim kaydı ekranı | Var | Var | Ayarlar › Sistem Yönetimi › Denetim kaydı | Var | 1-kısmi | `Endpoints/AuditEndpoints.cs`, `audit/AuditLogPage.tsx`. Filtreler: korelasyon, kayıt türü, kayıt kimliği, sonuç. **Kişiye ve tarih aralığına göre filtre yok; dışa aktarım yok.** |
| Reddedilen iş denemelerinin kaydı (KIM-D7) | Kısmi | Aynı ekranda "Reddedildi" | — | Var | 1-kısmi | Merkezî kaydedici `IRefusedAttemptRecorderV1` yalnız Identity, Organization, Documents, Product ve Inventory'de (1 dosya) çağrılıyor. Sales, Purchasing, Finance, Accounting, Logistics, Manufacturing, Settings, Counterparty içinde `RefusedAttempt` araması 0 sonuç. |
| Yetki retleri ve güvenlik olayları | Var | **Yok** | Yok | Var | 1-kısmi | `Endpoints/SecurityEventsEndpoints.cs` (`GET /api/v1/security-events`), `Modules/SecurityEvents/.../Writing/AuthorizationDenialRecorder.cs`. Ön yüzde `security-events` araması 0 sonuç; izin (`platform.security-events.read`) hiçbir rolde yok. |
| Aynı işlemin iki kez işlenmesini önleme (idempotency) | Var | Ekranlar anahtar gönderir | — | Var | 1 | 337 uçta `WithIdempotencyContract`; `Modules/Idempotency`. Eski kayıtların temizliği kapalı (`Idempotency:Retention:Purge:Enabled=false`). |
| Reddedilen kayıtların 2 yıl sonra imhası | Kod var, kapalı | — | — | Var | 1 | `appsettings.json` → `Audit:RefusedAttempts:Retention:AutoDisposalEnabled=false` (KIM-D26 gereği kapalı). |

### 3.7 Uygulama kabuğu ve menü

| Yetenek | Durum | Kanıt |
|---|---|---|
| Sol menü, yalnız yetkili girişler çizilir, boş grup çizilmez | Var | `shell/navigation.ts`, `shell/AppShell.tsx`; sunucu `GET /api/v1/shell/capabilities` |
| Çizilen gruplar (9): Satış · Satın Alma · Üretim · Stok & Lojistik · Ürün & Hizmet · Cari · Finans & Hazine · Muhasebe · Ayarlar; üstte "Ana sayfa" | Var | `navigation.ts` `NAV_GROUPS` — 107 menü girişi |
| KLN-D1'deki **Çalışma Merkezi, Belgeler, Raporlar** başlıkları | Çizilmiyor | `navigation.ts` açıklaması: "no published entry belongs to them yet" |
| Onaylarım / Bildirimlerim / İş kuyruğu | Sayfa var, menüde yok | `work/workRoutes.tsx`; yalnız ana sayfadaki bağlantılar (`shell/ShellHome.tsx`) |
| Genel arama, AI paneli | Üst çubukta | `search/GlobalSearchBox.tsx`, `ai/AiPanel.tsx` (AI `Ai:Enabled=false`) |
| Ana sayfa | Yer tutucu | `shell/ShellHome.tsx`: "Ortak kabuk hazır" başlığı, aktif şirket adı; gösterge/alarm/hızlı işlem yok |
| Üst çubukta dönem seçici, yardım, favoriler, rol bazlı sayaç (Master §5.1) | Yok | `AppShell.tsx` içinde şirket seçici, arama, AI ve çıkış var |
| Rotası olup menü girişi olmayan sayfalar | 156 rotanın 106'sı menü girişi | Kalan 50'nin çoğu bir listeden açılan detay/oluşturma sayfası. Menüsüz bağımsız sayfalar: şirket kurulumu, iki adımlı doğrulama, bildirimler, onay kutusu, iş kuyruğu, arama sonuçları. Modül bazında liste ilgili modül raporlarında. |

## 4. Kullanılabilirlik engelleri

### 4.1 İlk kurulum zinciri kopuk

- Uygulama veritabanı şemasını ve başlangıç kayıtlarını **yalnız `Development` ve `Testing` adlı ortamda**
  kendisi kurar (`Program.cs:669-703`). Başka adlı bir ortamda şema elle uygulanmalı; şirketler ve roller
  hiç oluşmaz.
- Kurucu komutu şu durumlarda reddeder (`FounderBootstrapService.cs:122-160`): `Identity:AdminRoleCode` ayarı
  boş (kod varsayılanı yok); o kodda aktif ve en az bir Critical izin taşıyan rol yok; hiç şirket yok; o rolde
  süresi dolmamış herhangi bir atama var.
- Sonuç: geliştirme dışı ortamda şirket ve rol olmadığı için komut reddeder. Geliştirme ortamında ise hazır
  örnek veri "Grup Geliştirme Yöneticisi"ne Sistem Yöneticisi rolünü üç şirkette de atadığı için
  (`Seed/identity-seed.json`, atama 3, 4, 7) aynı rol koduyla komutun "yönetici zaten atanmış" diye reddetmesi
  beklenir. Kutudan çıktığı haliyle yerel hesapla bir kurucuya ulaşan, elle veritabanı müdahalesi gerektirmeyen
  uçtan uca yol kodda görünmüyor; depoda bu yolu koşan bir betik veya uçtan uca test araması
  (`bootstrap-founder`, `infra/`, `tests/e2e/`) 0 sonuç.
- Komut yalnız yerel hesap modunda çalışır (`IdentityCommandLine.cs`); depodaki bütün hazır ortam dosyaları
  (`.env.example`, `infra/docker-compose*.yml`) geliştirme girişini kullanıyor, yerel hesap modunu açan hazır
  bir ortam tanımı yok.

### 4.2 Rol ve yetki (bütün modüllerin ön koşulu)

Ölçüm (`ErpPermissions.cs` ile `Seed/identity-seed.json` karşılaştırması):

| Alan | İzin sayısı | Bir rolde olan |
|---|---:|---:|
| finance | 56 | 0 |
| sales | 40 | 0 |
| settings | 39 | 0 |
| logistics | 22 | 0 |
| inventory | 19 | 0 |
| purchasing | 19 | 0 |
| accounting | 17 | 0 |
| manufacturing | 16 | 0 |
| counterparty / counterparties | 17 | 4 |
| organization | 12 | 5 |
| product | 11 | 0 |
| documents | 10 | 0 |
| identity | 8 | 5 |
| platform, ai | 2 | 0 |
| **Toplam** | **287** | **14** |

- Beş rol: Organizasyon Okuma (5 izin), Organizasyon Operasyonu (6), Sistem Yöneticisi (14), Yetki Yöneticisi
  (3), Yetki Onaylayıcısı (3). Hepsi "sistem rolü".
- Platformun kendi izinlerinden hiçbir rolde olmayanlar: `identity.users.manage`, `identity.assignments.read`,
  `identity.company-setup.first-admin.assign`, `organization.companies.open`, `organization.sites.write`,
  `organization.locations.write`, `organization.work-centers.write`, `organization.work-centers.calendar.write`,
  `organization.company.tax-id.request`, `organization.company.tax-id.approve`, `platform.security-events.read`.
- Sistem Yöneticisi rolü rol atama iznini (`identity.roles.manage`) taşıyor, ama atama formunu barındıran iki
  ekran başka izin istiyor (`Identity/.../Shell/IdentityShellCapabilityProvider.cs`): "Kullanıcı yönetimi" →
  `identity.users.manage`; "Kullanıcılar ve yetkiler" → `identity.assignments.read`. İkisi de rolde olmadığından
  ekranlar menüde çizilmez.
- Başlangıç dosyası yalnız **yeni** rolleri ekler; var olan bir rolün izin listesini güncellemez
  (`IdentityDataSeeder.cs`, "knownRoleIds" dalı). Dosyaya izin eklemek kurulu bir veritabanını değiştirmez.
- İş izinlerini bir role bağlayan tek mevcut yol tarayıcı testlerinin kendi veri yazıcısıdır
  (`tests/GoymenERP.BrowserSuiteFixtureTool`, `Shell/C01E2eFixtureWriterActivation.cs`); yalnız CI koşusunda
  açılır, gerçek ortamda kullanılamaz.
- İş rollerinin hedef dağılımı belge olarak hazır: `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` (14
  persona), `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md`. Bunları sisteme işleyecek araç yok.

### 4.3 Şirket açma yetkisi

Şirket açma ve ilk yöneticiyi seçme hakkı rol izniyle değil, ayrı bir "grup yetkisi" kaydıyla ölçülür
(`Services/SqlCompanySetupGroupAuthorityV1.cs`). Bu kaydın sınıf açıklaması açıkça şunu söyler: "No seeder, HTTP
grant command or role-name inference writes these rows" (`Identity.Domain/CompanySetup/CompanySetupGroupAssignment.cs`).
Kaydı yazan kod araması yalnız birim testlerini buluyor. Ekran yazılmış, ama onu açabilecek kimse yok.

### 4.4 Diğer

- **GSP ve GIN'de tesis, depo, iş merkezi yok.** Stok, sevk ve üretim bu kayıtlara bağlı; ekrandan açılabilir
  ama yazma izinleri hiçbir rolde değil.
- **GIN geçici şirket:** VKN'siz ve fatura kesemez (KIM-D35). Gerçek unvan ve VKN bekleniyor (`OPEN-001-GIN`).
- **Giriş modu:** geliştirme girişi çok kullanıcılı denemeye uygun değil; OIDC ön yüzde yok. Test ortamı için
  kullanılabilecek tek mod yerel hesap; ön yüz ve arka uç ayrı ayarlarla bu moda alınmalı.
- **Kapalı anahtarlar:** tekrar koruması kayıt temizliği, reddedilen kayıt imhası, AI, OTLP izleme kapalı
  (`appsettings.json`); hepsi karar gereği kapalı, kullanımı engellemez.
- **E-posta yok:** `smtp`, `IEmailSender` araması 0 sonuç. Parola sıfırlama yalnız yönetici eliyle; davet
  e-postası yok (KIM-D23'te e-posta ile sıfırlama zaten seçilmedi).
- **İnsan kapıları:** rol/izin verme, Critical sınıfı, ortam ve gizli anahtarlar, production etkinleştirme.

## 5. Eksikler

### 5.1 İsteklere göre (hedef var, kod yok ya da kısmi)

1. Rol tanımlama ekranı ve arka ucu (SY-02; SAT-D282 notundaki "Ayarlar rol ekranı").
2. İş rollerinin (Master §12.1'deki 13 aile; persona kartındaki 14 persona) sisteme girilmesi.
3. Yönetici rolünün kendi yönetim ekranlarını açabilmesi.
4. İlk kurulumun tek, tekrarlanabilir bir yolu: şema + şirketler + roller + kurucu.
5. Şirket açma grup yetkisini veren denetimli yol (KIM-D56).
6. Parola/kilit/oturum değerlerinin Ayarlar ekranından değişmesi (KIM-D4, D13, D14).
7. Reddedilen denemelerin bütün modüllerde kayda geçmesi (KIM-D7); bugün beş modülde.
8. Güvenlik olayları ekranı (KIM-D10: "yalnız güvenlik olayı okuma yetkisi olan görür").
9. Menüde Çalışma Merkezi, Belgeler, Raporlar başlıkları (KLN-D1); rol bazlı ana sayfa (Master §7.1).
10. Onay matrisi/eşik/vekâlet ekranı (SY-04), AI yetki taslağı (SY-03), entegrasyon ekranı (SY-06), sistem
    sağlığı (SY-07'nin ikinci yarısı), güvenlik/saklama ekranı (SY-08).
11. Tesis/depo kapsamlı rol ataması (Master §12.1 "şirket/şube/depo"); model destekliyor, ekran ve atama komutu
    yalnız şirket geneli veriyor.
12. OIDC ile tarayıcıdan giriş (KIM-D2 "sonra"; `OPEN-013` açık).

### 5.2 Bir ERP'de olması gerekene göre

- **Rol kopyalama ve rol karşılaştırma:** üç şirkette aynı rol setini kurarken gerekli. Rol tanımı grup
  genelinde, atama şirket bazında; bu doğru tasarım, ama rol şablonundan toplu atama yok.
- **Kullanıcının bütün şirketlerdeki yetkisini tek bakışta görme:** bugünkü ekran seçili şirkete çitli.
- **Toplu kullanıcı açma / içe aktarma:** üç firmanın personeli tek tek girilecek.
- **Kullanıcı yaşam döngüsü:** yeniden aktifleştirme, ad düzeltme, işten ayrılan kişinin bütün şirketlerde tek
  adımda kapatılması, son giriş zamanı, açık oturumları görme ve sonlandırma (Master SY-01 "oturum").
- **Görev ayrılığı çakışma raporu:** bir kişide hem hazırlama hem onaylama izni birikince uyarı.
- **Denetim kaydında kişi ve tarih filtresi, dışa aktarım:** mali denetim ve iç soruşturma için temel ihtiyaç.
- **Şirket kartı:** adres, vergi dairesi, ticaret sicil/MERSİS, iletişim; resmî belgelerde ve e-belgede gerekir.
- **Şirketler arası ortak görünüm:** grup yöneticisi için üç şirketi birlikte gösteren ekran. Kabukta "grup"
  kapsam modu tanımlı (`shell/shellState.ts`) ama seçici yalnız tek şirket seçtiriyor.
- **Vekâlet:** izindeki onaylayıcının yerine geçici yetki (süreli atama var, vekâlet akışı yok).

### 5.3 Ekran ve kullanım kolaylığı

- Durum blokları (yükleniyor, boş, yetkisiz, bulunamadı, çakışma, çevrimdışı, 503) ortak bileşenle her ekranda
  var (`shell/StateBlock.tsx`); bu alan iyi durumda.
- Kullanıcı yönetimi ekranı arama, sayfalama ve sıralama içermiyor; kullanıcılar alt alta kart olarak diziliyor
  (`IdentityUserManagementPage.tsx`, 78 satır). Onlarca kullanıcıda zorlaşır.
- Rol atama formunda ret gerekçesi serbest metin değil **kod** istiyor (`AccessGrantPanel.tsx`, `reasonCode`
  küçük harf/desen kontrolü); işletme kullanıcısı için anlaşılmaz.
- Denetim kaydı filtreleri "tam eşleşme" ile çalışıyor; kayıt türü ve kimliği teknik değer olarak yazılmalı.
- Ana sayfa metni teknik ("Ortak kabuk hazır", "Server yetenek bilgisi…").
- İki adımlı doğrulama ve şirket kurulumu sayfalarına menüden gidilemiyor; kullanıcı menüsü (profil) yok.
- Yazdırma/PDF/Excel bu alandaki hiçbir ekranda yok (kullanıcı listesi, rol–izin matrisi, denetim kaydı).

## 6. Diğer modüllerle bağlantılar

### 6.1 Platform kimden ne alır

| Kimden | Ne | Mekanizma |
|---|---|---|
| Organization → Identity | Şirket listesi (kurucu atamaları, üyelik) | Identity, `organization.Companies` tablosunu salt okunur eşler (`IdentityDbContext.cs:209`, migration dışı) |
| Organization → Identity | "Şirket açıldı" bildirimi, ilk yönetici kurulumu | Outbox/inbox: `Organization.Domain/CompanySetup/OrganizationCompanySetupOutboxMessage.cs`, `Identity.Domain/CompanySetup/CompanySetupInboxReceipt.cs` |
| Settings → Identity | Giriş deneme sınırı, güvenilir vekil sunucu listesi | Okuma portu; `Security/LocalAccountLoginRateLimiter.cs`, `LoginTrustedProxyList.cs` |
| Bütün modüller → Kabuk | Hangi ekran hangi izinle açılır | Her modülün `*ShellCapabilityProvider` sınıfı; merkezî kayıt `Shell/ShellCapabilityRegistry.cs`, onaylı sürümler `ShellCapabilityRegistryAuthorities.cs` |

### 6.2 Platform kime ne verir

| Kime | Ne | Mekanizma |
|---|---|---|
| Bütün modüller | Yetki kontrolü | Her uçta `RequireAuthorization(<izin>)`; `Security/ErpPermissionAuthorizationHandler.cs`, `Identity/.../Services/ErpAuthorizationService.cs` |
| Bütün modüller | Şirket bağlamı | `ICompanyContext` + sorgu filtreleri |
| Bütün modüller | Denetim kaydı yazımı | `IAuditEventCollector`, `IAuditTransactionWriterV1` (aynı veritabanı işleminde) |
| Bütün modüller | Tekrar koruması | `Idempotency-Key` başlığı; merkezî kayıt Counterparty, Organization, Settings, Identity'de; diğer modüller kendi depolarını kullanıyor |
| Inventory, Logistics, Manufacturing, Settings | Depo, lokasyon, iş merkezi, çalışma takvimi | Okuma portları: `Organization.Application/Contracts/IOrganizationLocation*`, `IOrganizationWorkCenter*`, `IOrganizationTransferDestination*` |
| Finance / Settings | Finans onay politikalarında rol referansı | `Identity.Application/Contracts/IdentityFinancePolicyRoleReferencesV1.cs` |
| Work, Sales vb. | Kullanıcı adı gösterimi | `Services/UserDirectoryReadPortV1.cs` |
| Finans/Satış belgeleri | Şirket VKN durumu (VKN'siz şirket resmî belge kesemez) | `IOrganizationCompanyTaxIdStatusReaderV1` |

### 6.3 Kopuk ya da eksik halkalar

- **İzin ↔ rol halkası kopuk:** modüller izinlerini kaydediyor, ekranlarını bu izinlere bağlıyor, ama izinleri
  role bağlayan halka yok. Her modülün "kullanıcı ekrandan yapabiliyor" seviyesi bu tek eksiğe takılıyor.
- **Reddedilen deneme kaydı** büyük iş modüllerinde merkezî kaydediciye bağlı değil.
- **Güvenlik olayı** yazılıyor ama okunacak ekran yok.
- **Identity'nin Organization tablosunu doğrudan okuması** yazma değil okuma; "başka modül tablosuna yazmaz"
  kuralını bozmuyor, ama port yerine tablo eşlemesi olduğu için not edilmeli.
- Çift kayıt riski görülmedi: rol, atama ve şirket kayıtlarının tek sahibi var.

## 7. Açık kararlar

| Konu | Durum | Kullanımı engelliyor mu |
|---|---|---|
| `OPEN-013` production giriş sağlayıcısı (OIDC/SSO), MFA, dış erişim | Kısmen; test için yerel hesap kararı var (KIM-D2) | Test ortamını engellemez |
| `OPEN-001-GIN` GIN'in gerçek unvanı ve VKN'si | Açık | GIN'de resmî belgeyi engeller |
| İlk rol dağılımının hangi yolla verileceği (kurulum rehberi S6) | Biçim cevaplı ("Ayarlar ekranı"), ekran yok | **Evet** |
| İlk yönetici ve şirket kaydının yöntemi, kurucunun kim olacağı (kurulum rehberi S5) | Araç yazıldı, yöntem ve kişi kararı açık | **Evet** |
| Hangi rolün "Admin" sayılacağı (`Identity:AdminRoleCode`) | Kod varsayılanı yok; insan kapısı | **Evet** |
| Şirket açma grup yetkisinin kime, hangi yolla verileceği (KIM-D56) | Kişi "yönetimden yetkili / genel müdür"; kayıt yolu yok | Yeni şirket açmayı engeller |
| Critical işlem listesi | "Şimdilik hiçbiri" (KIM-D9), 17 izin Critical | Hayır |
| Reddedilen kayıt imhası, toplulaştırma eşikleri | Kapalı, ayrı karar (KIM-D25, D26) | Hayır |
| `OPEN-017` yedekleme hedefleri, `OPEN-021` tekrar koruması kayıt saklama | Kısmen | Test için hayır |
| Menüde AI Merkezi | Açık (KLN notu) | Hayır |

## 8. Yapılacaklar (öncelik sırasıyla)

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---|---|---|---|---|---|
| 1 | Rol tanımlama arka ucu ve ekranı: rol oluştur, adlandır, izin ekle/çıkar, kopyala; her değişiklik denetime; Critical izin eklemede ikinci onay | Bütün modüllerin kullanılabilirliğinin ön koşulu; karar "Ayarlar rol ekranı" | P0 | B | Rol değişikliğinde onay kuralı için Sinan kararı (yetki insan kapısı) |
| 2 | İş rollerinin ilk seti: persona kartındaki 14 personanın 287 izne eşlenmiş listesi; Sinan onayıyla sisteme girilmesi | Roller olmadan hiçbir iş ekranı açılmaz | P0 | O | 1 (ya da geçici olarak onaylı bir başlangıç dosyası); modül raporlarındaki izin listeleri |
| 3 | Yönetici rolüne `identity.users.manage` ve `identity.assignments.read` verilmesi | Yönetici kullanıcı açamıyor, rol atayamıyor | P0 | K | Sinan kararı (grant) |
| 4 | Test ortamı kurulum yolu: şema, üç şirket, roller ve kurucunun tek belgeli akışla kurulması; örnek geliştirme kullanıcıları olmadan; baştan sona bir kez denenmesi | Bugün elle veritabanı müdahalesi gerekiyor | P0 | O | Ortam adı ve `AdminRoleCode` kararı; kurucunun kim olacağı |
| 5 | Yerel hesap modlu hazır ortam tanımı (arka uç `Authentication:Mode=LocalAccount`, ön yüz `VITE_AUTH_MODE=LocalAccount`) ve giriş için uçtan uca tarayıcı testi | Test için kullanılabilir tek giriş modu; hazır tanımı ve uçtan uca testi yok | P0 | K | 4 |
| 6 | GSP (ve kullanılacaksa GIN) için tesis, depo, iş merkezi kayıtları; organizasyon yazma izinlerinin role bağlanması | Stok/sevk/üretim bu kayıtlara bağlı | P0 | K | 2, 3; gerçek depo listesi |
| 7 | Kullanıcı yönetimi ekranına arama, sayfalama, yeniden aktifleştirme, ad düzeltme; ret gerekçesinin düz metin olması | Üç firmanın personeli girilecek | P1 | O | — |
| 8 | Denetim kaydına kişi ve tarih aralığı filtresi, okunur kayıt türü seçimi, dışa aktarım | "Kim ne yaptı" sorusu bugün kişiden başlayarak sorulamıyor | P1 | O | Dışa aktarım izni kararı |
| 9 | Güvenlik olayları ekranı | Yetki retleri yazılıyor, görülemiyor (KIM-D10) | P1 | K | İznin hangi role verileceği |
| 10 | Reddedilen deneme kaydının Satış, Satın Alma, Finans, Muhasebe, Lojistik, Üretim, Ayarlar, Cari'ye bağlanması | KIM-D7 bütün işlemleri kapsıyor | P1 | B | — |
| 11 | Şirket açma grup yetkisini veren denetimli yol (sunucu komutu ya da onaylı ekran) | Yazılmış ekran kullanılamıyor | P1 | K | Sinan kararı (kişi ve yol) |
| 12 | Menüde Çalışma Merkezi başlığı (onaylar, bildirimler, iş kuyruğu); kullanıcı menüsü (parola değiştir, iki adımlı doğrulama) | Sayfalar var, ulaşmak ana sayfaya bağlı | P1 | K | — |
| 13 | Parola, kilit ve oturum değerlerinin Ayarlar ekranı | KIM-D4, D13, D14 | P1 | O | — |
| 14 | Tesis/depo kapsamlı rol ataması | Master §12.1; depocu yalnız kendi deposunu görsün | P1 | O | Hangi izinlerin dar kapsamla verilebileceği |
| 15 | Kullanıcının bütün şirketlerdeki yetki özeti; rol–izin matrisi çıktısı; görev ayrılığı çakışma raporu | Üç şirketli grupta yetki denetimi | P1 | O | 1 |
| 16 | Şirket kartı (adres, vergi dairesi, sicil, iletişim) | Resmî belge ve e-belge | P1 | O | Finans/Belgeler ile alan listesi |
| 17 | Rol bazlı ana sayfa; Belgeler ve Raporlar başlıkları | Master §7.1, KLN-D1 | P2 | B | İlgili modüller |
| 18 | OIDC ile tarayıcıdan giriş | `OPEN-013` | P2 | O | Sağlayıcı kararı |
| 19 | Onay matrisi/vekâlet, AI yetki taslağı, entegrasyon ve sistem sağlığı ekranları | Master SY-03…SY-08 | P2 | B | Ayrı kararlar |
| 20 | Kurulum rehberinin koda göre güncellenmesi | Rehber eski (bkz. §9) | P1 | K | 4 |

## 9. Doğrulanamayanlar ve belge–kod çelişkileri

Doğrulanamayanlar (kod okundu, çalıştırılmadı):

- Kurucu komutunun geliştirme ortamında hazır örnek atama yüzünden reddedeceği koddan çıkarıldı; komut
  çalıştırılmadı. Başka bir rol koduyla (ör. Yetki Yöneticisi) kurulup kurulamayacağı denenmedi.
- Geliştirme ortamında yerel hesap modunun örnek veriyle birlikte sorunsuz açıldığı denenmedi.
- Sales, Purchasing ve Logistics'te "TerminalRefusal" adlı kayıtlar var; bunların tekrar koruması kaydı olduğu,
  denetim ekranındaki "Reddedildi" satırı olmadığı sınıf açıklamasından (`AuditModels.cs:30`) çıkarıldı. İlgili
  modül raporlarıyla karşılaştırılmalı.
- Denetim ve güvenlik olayı tablolarının veritabanı düzeyinde değiştirilemezliği (tetikleyici içeren 3 dosya
  var) ayrıntılı incelenmedi.
- Testlerin geçip geçmediği bilinmiyor. Test kaynağı sayıları: Identity 61 dosya / 422 test, Organization
  29 / 197, Audit 10 / 76, SecurityEvents 8 / 84, Idempotency 3 / 22 (birim); API bütünleşik testlerinde kimlik
  17, yerel hesap/kurucu 15, organizasyon/şirket 18, kabuk 31 dosya; ön yüzde bu dört klasörde 76 test dosyası.
- Performans (her istekte yetki ve oturum okuması) ölçülmedi.

Belge ile kod arasındaki çelişkiler:

- Analiz talimatı "287 izin, 5 sistem rolü" diyor: kodla uyumlu (287 izin; 14'ü rolde).
- `docs/operations/CANLI_ORTAM_KURULUM_REHBERI_v1.md` E1 "ön yüzde giriş yok", E3 "ilk kullanıcıyı oluşturan
  yol yok", E4 "rol atama ekranı yok, yalnız okuma uçları var" diyor. Kodda yerel giriş ekranı, kurucu komutu ve
  rol atama uçları ile ekranı var. Rehberin E2 maddesi (şema kendiliğinden kurulmaz) ve "iş rolleri için kodda
  rol tanımı yok" tespiti hâlâ doğru.
- `ACIK_KARARLAR.md` `OPEN-013` satırı "yerel bootstrap hesaplarıyla geliştirme" diyor; kodda bunun ötesinde
  tam bir yerel hesap sistemi var.
- `IdentityShellCapabilityProvider` açıklaması ekranları "read-only" diye anlatıyor; aynı dosya atama ve
  kullanıcı yönetimi kontrollerini de yayımlıyor.
- KIM-D18 "sonradan açılan şirkette açan kişi ilk yönetici olur" hükmü KIM-D56 ile değişti; kod KIM-D56'ya
  uyuyor (açan kişi otomatik yetki almaz).
