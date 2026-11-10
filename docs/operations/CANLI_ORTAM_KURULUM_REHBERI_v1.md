# Canlı Ortam Kurulum Rehberi v1

- **Tarih:** 2026-11-10
- **Okuyucu:** Sinan ve BT sorumlusu (uzman olmayan okuyucu için sade dil)
- **İncelenen taban:** `main` / `78adcd68`
- **Durum:** Rehber / hazırlık belgesi. Karar değildir; yeni `OPEN-*`, karar kimliği veya
  rol/izin ataması üretmez. `KARAR_SICILI.md` ve `docs/decisions/**` değişmez.

---

## 1. Amaç ve kapsam

Bu belge, Göymen ERP'nin ilk canlı (production) kurulumunu sırasıyla anlatır: hangi
sunucular gerekir, hangi ayarlar girilir, veritabanı nasıl hazırlanır, uygulama nasıl
yayınlanır, yedek nasıl alınır.

Rehber **yalnız bugün kodun gerçekten istediklerine** dayanır. Repoda kararı verilmemiş
her nokta **"Sinan/BT seçer"** diye işaretlenmiştir; tahminle doldurulmamıştır.

### 1.1 İnsan kapıları (bu belge hiçbirini açmaz)

`AGENTS.md` "Değişmeyen insan kapıları" gereği şu adımlar **her zaman Sinan'ın somut
kararını** ister. Bu belgeyi okumak veya uygulamak onay yerine geçmez:

| Kapı | Ne demek | Kim karar verir |
|---|---|---|
| Deployment / ortam | Sunucuya ilk yükleme, ortam (environment) açma | Sinan |
| Gizli anahtarlar (secret) | Parola, imza anahtarı, bağlantı dizesi üretme ve saklama | Sinan + BT |
| Workflow etkinleştirme | GitHub Settings → Actions'ta otomasyon açma | Sinan |
| Rol / yetki / Critical | Bir kullanıcıya rol veya izin verme | Sinan |
| Production aktivasyonu | Sistemi gerçek kullanıcılara açma | Sinan |
| `OPEN-*` kapatma | Açık bir kararı kapatma | Sinan |

Bu belge workflow, deploy betiği veya `.github/**` değişikliği **eklemez**; içinde gerçek
parola veya anahtar **yoktur**.

### 1.2 Önce bilinmesi gereken: canlıya geçişi bugün durduran eksikler

Kodu inceledik. Aşağıdaki dört konu **kodda henüz yok**; bunlar tamamlanmadan sistem
gerçek kullanıcılarla canlıya alınamaz. Rehberin ilgili adımları bu yüzden "bekliyor"
olarak işaretlidir.

| # | Eksik | Kanıt | Ne gerekiyor |
|---|---|---|---|
| E1 | **Ön yüzde canlı giriş (OIDC oturum açma) yok.** Tarayıcı istemcisi yalnız geliştirme başlığı `X-Development-User` gönderiyor; `Authorization: Bearer` token göndermiyor. Canlı derlemede (`VITE_BOOTSTRAP_IDENTITY_ENABLED=false`) her API isteği 401 alır. | `src/frontend/goymen-erp-web/src/identity/api.ts:28`, `src/frontend/goymen-erp-web/src/sales/api.ts:134`, `src/frontend/goymen-erp-web/Dockerfile` | Kimlik sağlayıcı seçimi (`OPEN-013`) + ön yüz giriş akışı (Claude şeridi işi) |
| E2 | **Canlıda veritabanı şeması ve ilk kayıtlar otomatik kurulmaz.** Uygulama migration ve seed'i yalnız `Development`/`Testing` ortamında başlangıçta çalıştırır. Canlı için ayrı bir migration paketi/betiği repoda yok. | `src/backend/GoymenERP.Api/Program.cs` (dosya sonundaki `Initialize…ModuleAsync` bloğu, `IsDevelopment() \|\| IsEnvironment("Testing")` koşulu) | Bölüm 4.2'deki elle uygulama yolu veya backend şeridinin (Codex) hazırlayacağı paket |
| E3 | **Canlıda ilk kullanıcı ve şirket kaydını oluşturan bir yol yok.** Şirketler `Seed/organization-seed.json`, kullanıcı/rol/atamalar `Seed/identity-seed.json` dosyasından yalnız geliştirmede yüklenir. Kimlik seed'indeki kullanıcılar geliştirme hesaplarıdır (`urn:goymen:development-bootstrap`). | `src/backend/GoymenERP.Api/Seed/identity-seed.json`, `src/backend/GoymenERP.Api/Seed/organization-seed.json` | Canlı ilk kayıt (bootstrap) yöntemi kararı ve backend işi |
| E4 | **Rol atama ekranı yok.** Ayarlar → "Kullanıcılar ve yetkiler" yalnız okuma ekranıdır; rol verme/geri alma bilerek kapalıdır. Sunucuda da yalnız okuma uçları (`GET /api/v1/identity/access/users` ve `GET /api/v1/identity/access/roles`) vardır. | `src/frontend/goymen-erp-web/src/identity/capabilities.ts` (NEEDS-SINAN #66 5738113623, `OPEN-013`), `src/backend/GoymenERP.Api/Endpoints/IdentityEndpoints.cs` | Sinan kararı + backend ve ekran işi |

Bu eksikler kapandığında rehber v2'ye güncellenmelidir.

---

## 2. Gerekenler listesi

Yalnız kodun bugün istedikleri yazılmıştır. Donanım boyutu (CPU/RAM/disk/RAID/UPS)
`OPEN-014` kapsamında **açıktır**; gerçek envanter repoda yoktur
(`docs/decisions/ACIK_KARARLAR.md`).

| Bileşen | Kodun istediği | Seçim |
|---|---|---|
| **Barındırma yönü** | On-prem/hibrid, VPN erişimli, tesis dışı şifreli yedek yönü korunur (`docs/architecture/DEPLOY_BACKUP_RPO_RTO.md`, `OPEN-014`). | Kendi sunucu odası mı, kiralık sunucu mu, karma mı: **Sinan/BT seçer** |
| **API sunucusu** | .NET 10 ASP.NET Core çalışma zamanı (`global.json` SDK `10.0.300`). Linux konteyner (`src/backend/GoymenERP.Api/Dockerfile`, port 8080) veya doğrudan Windows/Linux kurulumu. **Belge Kasası açılacaksa Windows Server zorunlu** (aşağıya bakın). | İşletim sistemi ve konteyner/doğrudan kurulum: **Sinan/BT seçer** |
| **Veritabanı** | Microsoft SQL Server. Testler ve CI `mcr.microsoft.com/mssql/server:2022-latest` ile koşuyor (`infra/docker-compose.yml`). Türkçe harmanlama `Turkish_100_CI_AS` kullanılır (`ProductPriceListDbContext.cs`). Kimlik modülü veritabanında **RCSI** (`READ_COMMITTED_SNAPSHOT ON`) açık olmasını şart koşar (`docs/operations/SQL_SERVER_RCSI_READINESS_RUNBOOK.md`). | Sürüm (2022 önerilen, CI ile aynı), lisans türü (Standard/Enterprise; Developer canlıda kullanılamaz), tek sunucu / kümeleme: **Sinan/BT seçer** |
| **Belge deposu (dosyalar)** | Canlıda belgeler **sunucudaki bir klasöre** yazılır (`Documents:ObjectStore:RootPath`) ve her yükleme **Windows Defender** (`MpCmdRun.exe`) ile taranır. İkisi birlikte verilmezse yükleme/indirme 503 döner; yarım ayar başlangıçta hata verir. Defender yalnız Windows'ta çalışır. (`DocumentsModuleExtensions.cs`, `DocumentsRuntimeExtensions.cs`) | Belge Kasası ilk gün açılacak mı, klasör hangi diskte/paylaşımda: **Sinan/BT seçer**. Not: `.env.example`'daki `OBJECT_STORAGE_*` (S3) satırları bugün API tarafından **okunmuyor**. |
| **Ön yüz (web) sunucusu** | Statik dosyalar (`npm run build` çıktısı `dist/`). Örnek: nginx (`src/frontend/goymen-erp-web/nginx.conf`, `Dockerfile`). `/api/` isteklerini API'ye yönlendirir; diğer her yolu `index.html`'e düşürür. | nginx / IIS / başka: **Sinan/BT seçer** |
| **Alan adı + TLS (HTTPS)** | API kendi içinde HTTPS yönlendirmesi veya HSTS yapmıyor (`Program.cs`'te yok); TLS önündeki ters vekilde (reverse proxy) sonlanmalı. Kimlik sağlayıcı adresi canlıda **mutlaka HTTPS** olmalı (`ErpAuthenticationExtensions.cs`). | Alan adı, sertifika kaynağı (kurumsal CA / Let's Encrypt / ticari), yalnız VPN içi mi dış erişim mi: **Sinan/BT seçer** |
| **Kimlik sağlayıcı (OIDC/SSO)** | Standart OIDC/JWT: bir `Authority` (HTTPS adres) ve `Audience`. Sağlayıcıdan bağımsız yazılmış (`docs/adr/ADR-004-identity-and-authorization.md`). | Sağlayıcı (ör. Microsoft Entra ID, Keycloak vb.), MFA, dış erişim: **`OPEN-013`, Sinan seçer** |
| **E-posta** | Kodda e-posta gönderen bir servis veya SMTP ayarı **yok**. | Gerekmez (bugün) |
| **Dış entegrasyon** | İlk sürümde muhasebe/e-belge/banka entegrasyonu yok (`OPEN-016`). TCMB kur çekme kapalı varsayılanla gelir (`Settings:ExchangeRates`). | TCMB açılsın mı: **insan kapısı, Sinan seçer** |
| **Yedekleme alanı** | Tesis dışı, şifreli yedek hedefi (`DEPLOY_BACKUP_RPO_RTO.md`). | Hedef (ikinci lokasyon, bulut, bant): **Sinan/BT seçer** |

---

## 3. Gizli anahtarlar ve yapılandırma anahtarları

**Değer yazılmaz.** Gizli olanlar (🔒) kaynak koda, `appsettings*.json`'a veya bu belgeye
**asla** yazılmaz; sunucunun ortam değişkenine veya bir gizli kasa (secret store)
ürününe konur. Hangi kasanın kullanılacağı **Sinan/BT seçer**.

ASP.NET Core kuralı: `Bolum:Alt` anahtarı ortam değişkeninde `Bolum__Alt` (iki alt çizgi)
olarak yazılır. Örnek: `Authentication:Oidc:Authority` → `Authentication__Oidc__Authority`.

### 3.1 Zorunlu olanlar

| Anahtar | Gizli mi | Ne işe yarar | Nereden okunur |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Hayır | Ortam adı. Canlıda `Production` olmalı. `Development`/`Testing` canlıda **kullanılmaz** (geliştirme girişi ve otomatik seed bu adlara bağlı). | ASP.NET Core; `Program.cs`, `ErpAuthenticationExtensions.cs` |
| `DB_CONNECTION_STRING` (veya `ConnectionStrings:GoymenErp`) | 🔒 | SQL Server bağlantı dizesi. Canlıda `Encrypt=True` ve geçerli sertifika önerilir; `TrustServerCertificate=True` yalnız geliştirme içindir. | Tüm modül `*ModuleExtensions.cs` dosyaları (`configuration["DB_CONNECTION_STRING"] ?? GetConnectionString("GoymenErp")`) |
| `DB_PROVIDER` | Hayır | Yalnız `SqlServer` desteklenir; boşsa `SqlServer` varsayılır. | `DocumentsModuleExtensions.cs` ve diğer modüller |
| `Authentication:Mode` | Hayır | Canlıda `OidcJwtBearer` (boşsa da bu). `DevelopmentBootstrap` canlıda başlangıçta **reddedilir**. | `src/backend/GoymenERP.Api/Security/ErpAuthenticationExtensions.cs` |
| `Authentication:Oidc:Authority` | Hayır | Kimlik sağlayıcının HTTPS adresi (kullanıcı bilgisi, sorgu, `#` içermez). | Aynı dosya |
| `Authentication:Oidc:Audience` | Hayır | Token'ın bu API için verildiğini gösteren hedef adı. | Aynı dosya |
| `Authentication:Oidc:RequireHttpsMetadata` | Hayır | Canlıda `true` olmak **zorunda**; `false` başlangıçta reddedilir. | Aynı dosya |
| `Cors:AllowedOrigins` | Hayır | Tarayıcının API'ye istek atabileceği ön yüz adres(ler)i. Varsayılan `http://localhost:5173` canlıda **değiştirilmeli**. Ön yüz ve API aynı alan adında `/api/` ile sunuluyorsa gerekmeyebilir. | `Program.cs` (`GetSection("Cors:AllowedOrigins")`) |
| `Counterparty:CursorSigningKey` | 🔒 | Cari listelerinde sayfalama imleçlerini imzalar. Base64, **en az 32 bayt**. Canlıda geçici anahtar üretilmez; verilmezse Cari sayfalaması çalışmaz. | `CounterpartyCursorCodec.cs`, `CounterpartyModuleExtensions.cs` |
| `Inventory:StockPositionCursorSigningKeyBase64` | 🔒 | Stok durumu ve hareket zaman çizelgesi sayfalaması için imza anahtarı. Base64, en az 32 bayt. Verilmezse bu okumalar 503 döner. | `Program.cs`, `InventoryStockPositionCursorCodec.cs` |

İmza anahtarları rastgele üretilmeli (ör. 32 bayt kriptografik rastgele → base64) ve
her ortamda farklı olmalı. Üretme ve saklama yöntemi: **Sinan/BT seçer**.

### 3.2 Özelliğe bağlı olanlar (açılmazsa boş kalır)

| Anahtar | Gizli mi | Ne işe yarar | Boş kalırsa | Nereden okunur |
|---|---|---|---|---|
| `Documents:ObjectStore:RootPath` | Hayır (yol) | Belge dosyalarının yazılacağı sunucu klasörü. | Belge yükleme/indirme 503 | `DocumentsModuleExtensions.cs` |
| `Documents:Scanner:Mode` | Hayır | Canlıda `WindowsDefender`, boş veya `None` (`None` yalnız klasör ve Defender hiç ayarlanmamışsa geçerlidir; belge kasası kapalı kalır). `DevelopmentSignature` canlıda reddedilir. | — | Aynı dosya |
| `Documents:Scanner:Defender:ExecutablePath` | Hayır | `MpCmdRun.exe` tam yolu. `RootPath` ile **birlikte** verilmeli. | Belge Kasası kapalı | Aynı dosya |
| `Documents:Scanner:Defender:QuarantinePath` | Hayır | Tarama sırasında geçici klasör. | Sistem geçici klasörü | Aynı dosya |
| `Documents:Scanner:Defender:TimeoutSeconds` | Hayır | Tarama zaman aşımı (varsayılan 120). | Varsayılan | `appsettings.json` |
| `Documents:Scanner:Defender:MaximumConcurrentScans` | Hayır | Aynı anda en çok tarama (varsayılan 2). | Varsayılan | `appsettings.json` |
| `Documents:MaximumUploadBytes` | Hayır | En büyük dosya (varsayılan 20 MiB; 2026-09-30 kullanıcı kararı). | Varsayılan | `appsettings.json` |
| `ProductRuntime:IssuerLocalUserId`, `ProductRuntime:IssuerExternalIdentityId` | Hayır | Ürün pilot yetkilisinin doğrulanmış kimlik bağı. İkisi birlikte verilir. | Ürün modülü kapalı | `src/backend/GoymenERP.Api/ProductRuntime/ProductRuntimeExtensions.cs` |
| `ProductRuntime:CursorSigningKeyBase64` | 🔒 | Ürün listesi imleç imzası (ürün modülü açıksa zorunlu). | — | Aynı dosya |
| `Settings:ExchangeRates:TcmbEnabled`, `TcmbBaseAddress`, `TcmbTimeoutSeconds` | Hayır | TCMB kur bültenini çekme. Açılması **insan kapısı**. | Kapalı (kur içe alma reddedilir) | `ExchangeRateSources.cs` |
| `Logging:LogLevel:*` | Hayır | Log seviyesi (varsayılan `Information`, `Microsoft.AspNetCore` = `Warning`). | Varsayılan | `appsettings.json` |
| `AllowedHosts` | Hayır | Kabul edilen alan adları (varsayılan `*`). Canlıda gerçek alan adıyla daraltılması önerilir. | `*` | `appsettings.json` |

### 3.3 Canlıda **verilmemesi** gerekenler

| Anahtar | Neden |
|---|---|
| `DatabaseStartup:Mode` | Yalnız `Initialize`/`ValidateOnly`; `ValidateOnly` canlıda reddedilir (`Startup/DatabaseStartupMode.cs`). Boş bırakın. |
| `BootstrapIdentity:*`, `VITE_BOOTSTRAP_IDENTITY_ENABLED=true`, `VITE_DEV_USER` | Geliştirme girişi. Canlıda açılmaz. |
| `C01E2e:*`, `BrowserSuiteFixture:*`, `BrowserSuiteApi:*` | Test/E2E düzeneği anahtarları. |
| `MSSQL_SA_PASSWORD` | Yalnız yerel Docker geliştirmesi (`infra/docker-compose.yml`). Uygulama `sa` hesabıyla bağlanmamalı. |
| `Work:OutboxDispatcher:*` | Dağıtıcı bugün API'ye bağlanmamış (`AddWorkOutboxDispatcher` `Program.cs`'te çağrılmıyor); açılması insan kapısı ve değerleri NEEDS-SINAN (`WorkOutboxDispatcherOptions.cs`). |

### 3.4 Ön yüz derleme değişkenleri (derleme anında gömülür)

| Değişken | Ne işe yarar | Canlı değeri |
|---|---|---|
| `VITE_API_BASE_URL` | API adresi. Boşsa `/api/v1` (aynı alan adı). | Aynı alan adı kullanılırsa **hiç tanımlamayın** (boş değer verilmez) |
| `VITE_BOOTSTRAP_IDENTITY_ENABLED` | Geliştirme girişi | `false` (Dockerfile varsayılanı) |

Ön yüz değişkenleri tarayıcıya gömülür; bunlara **asla gizli bilgi** konmaz.

---

## 4. Adım adım kurulum

Öneri: önce aynı adımlarla bir **Test/UAT** ortamı kurun, orada deneyin, sonra canlıya
tekrarlayın (`DEPLOY_BACKUP_RPO_RTO.md` "Ortamlar").

### 4.1 Veritabanı

1. SQL Server'ı kurun (sürüm/lisans: Bölüm 2). Sunucu harmanlaması Türkçe karakterleri
   desteklemeli; uygulama gereken yerde `Turkish_100_CI_AS`'yi kendisi belirtir.
2. Boş bir veritabanı oluşturun (adı **Sinan/BT seçer**; geliştirmede `GoymenErp`).
3. **RCSI'yi açın.** Kimlik modülü `sys.databases.is_read_committed_snapshot_on = 1`
   değilse yetki okumasını reddeder. Kontrol ve bakım penceresi adımları için
   `docs/operations/SQL_SERVER_RCSI_READINESS_RUNBOOK.md` dosyasını izleyin
   (özellikle "RCSI-OFF remediation"). `ALTER DATABASE` komutunu BT, onaylı bakım
   penceresinde çalıştırır.
4. Uygulama için **ayrı bir SQL kullanıcısı** oluşturun (`sa` değil). Gereken izin
   düzeyi (migration'ı aynı kullanıcı mı, ayrı yönetici hesabı mı uygular) **Sinan/BT
   seçer**. Öneri: migration için yetkili ayrı hesap, uygulama için daha dar hesap.
5. Bağlantı dizesini (🔒 `DB_CONNECTION_STRING`) gizli kasaya koyun.

### 4.2 Migration (şema) uygulama

Canlıda uygulama şemayı **kendisi kurmaz** (E2). Şema, her modülün EF Core migration'larından
elle uygulanır. Repoda araç hazır: `dotnet-tools.json` içinde `dotnet-ef 10.0.8` ve 18 DbContext'in
17'si için `*DesignTimeDbContextFactory.cs`. `ProductPriceListDbContext` için fabrika yoktur;
onun betiği `--startup-project src/backend/GoymenERP.Api` ile üretilmelidir. Bu yolda API
host'u ayağa kalktığı için `DB_CONNECTION_STRING` ve ürün modülü ayarları (`ProductRuntime:Issuer*`)
tanımlı olmalıdır; değilse komut hata ile durur (yanlış betik üretmez).

Önerilen güvenli yol (yöntem kesinleşene kadar; paket mi betik mi **Sinan/BT + Codex seçer**):

1. Yayınlanacak **tam commit**'i belirleyin. İsteğe bağlı olarak sürüm manifestini üretin:
   `python3 tools/generate_release_manifest.py --source-commit <40 karakterlik küçük harf tam SHA> > release-manifest.json`
   (`HEAD` veya kısa SHA kabul edilmez; araç exit 2 ile durur)
   (yalnız kaynak bilgisini yazar; derlemez, migration çalıştırmaz).
2. O commit'te, her DbContext için **idempotent SQL betiği** üretin:
   `dotnet tool restore` ve ardından her context için
   `dotnet ef migrations script --idempotent --context <ContextAdı> --project <modül Infrastructure projesi> --output <dosya>.sql`
   Betik üretimi veritabanına bağlanmaz.
3. Betikleri bir DBA/BT gözden geçirir, önce **Test/UAT** veritabanında çalıştırır.
4. Sıra önemlidir (yabancı anahtarlar). Uygulamanın geliştirmede kullandığı sıra
   `Program.cs` sonundaki bloktadır: Organization → Settings → Identity → Audit →
   Idempotency (CommandExecution) → Counterparty → Inventory → Product +
   GroupSharedCommand → ProductPriceList (yalnız ürün modülü açıksa; `Program.cs`
   `ProductRuntimeOptions.IsEnabled` koşulu) → Finance → Sales → Manufacturing → Logistics →
   Purchasing → Accounting → Documents.
5. **Work** modülünün migration'ı API başlangıç zincirinde çağrılmıyor
   (`InitializeWorkModuleAsync` kullanılmıyor). Canlıda uygulanıp uygulanmayacağı backend
   şeridine (Codex) sorulmalı — açık seçim.
6. Uygulama başladıktan sonra `GET /health/ready` çağrısı, koddaki migration listesi ile
   veritabanındaki listenin **birebir** aynı olduğunu **yalnız şu 14 context için** kontrol
   eder (`Startup/DatabaseSchemaCompatibilityHealthCheck.cs`): Organization, Identity, Audit,
   Idempotency, Counterparty, Inventory, Product, GroupSharedCommand, Finance, Sales,
   Manufacturing, Logistics, Purchasing, Accounting. Ürün modülü kapalıysa Product ve
   GroupSharedCommand da atlanır. 200 dönmüyorsa şema eksiktir.
7. **Settings, ProductPriceList, Documents ve Work** bu kontrolde **yoktur**; `/health/ready`
   bunlar eksikken de 200 dönebilir. Bu dört context için migration geçmiş tablosundaki
   kayıtlar, `dotnet ef migrations list --context <ad> --no-connect` çıktısıyla **elle**
   karşılaştırılmalıdır. Geçmiş tabloları: `settings.__EFMigrationsHistory`,
   `product.__EFMigrationsHistory_PriceLists` (dikkat: `product.__EFMigrationsHistory`
   ProductDbContext'indir), `documents.__EFMigrationsHistory`, `work.__EFMigrationsHistory`.
   Tasarım zamanı fabrikalarında yerel (localdb) bağlantı dizesi gömülü olduğu için
   `--no-connect` kullanılmalıdır.

Geri alma: migration'ların bir kısmı veri kaybı riskinde `Down` adımını bilerek reddeder
(`THROW 51xxx`, "forward fix"). Yani hatalı bir sürümden dönüş **geri sarma değil ileri
düzeltme** veya yedekten geri yükleme ile yapılır. Bu yüzden her migration öncesi tam
yedek alınmalıdır (Bölüm 5).

### 4.3 API yayını

1. Yayın paketini aynı commit'ten üretin. İki hazır yol var:
   - Konteyner: `src/backend/GoymenERP.Api/Dockerfile` (port 8080, `dotnet GoymenERP.Api.dll`).
   - Doğrudan: `dotnet publish src/backend/GoymenERP.Api/GoymenERP.Api.csproj -c Release -o <klasör>`.
   Belge Kasası açılacaksa Windows sunucu ve doğrudan kurulum (veya Windows konteyner)
   gerekir, çünkü Defender taraması Linux'ta çalışmaz.
2. Bölüm 3.1'deki zorunlu anahtarları ortam değişkeni/gizli kasa üzerinden verin;
   `ASPNETCORE_ENVIRONMENT=Production`.
3. Uygulamayı servis olarak çalıştırın (Windows Service / systemd / konteyner
   orkestrasyonu: **Sinan/BT seçer**).
4. Başlangıç hata verirse mesaj Türkçedir ve hangi anahtarın eksik/yanlış olduğunu söyler
   (örn. "OIDC/JWT için Authentication:Oidc:Authority ve Audience zorunludur.").
5. Kontrol: `GET /health/live` → 200 (uygulama ayakta), `GET /health/ready` → 200 (şema
   uyumlu). Bu iki uç kimlik istemez; dışarıya açılıp açılmayacağı **Sinan/BT seçer**
   (öneri: yalnız iç ağ/izleme).

### 4.4 Ön yüz yayını

1. `src/frontend/goymen-erp-web` klasöründe: `npm ci` → `npm run lint` → `npm test` →
   `npm run build`. Çıktı `dist/` klasörüdür (`package.json`).
2. `VITE_BOOTSTRAP_IDENTITY_ENABLED=false` ile derleyin (Dockerfile varsayılanı).
3. `dist/` içeriğini web sunucusuna koyun. Örnek nginx ayarı repoda:
   `src/frontend/goymen-erp-web/nginx.conf` — `/api/` isteklerini API'ye yönlendirir,
   SPA yollarını `index.html`'e düşürür. Canlıda bunun önüne **443/TLS** dinleyen bir
   blok ve gerçek alan adı eklenmelidir (örnek dosya yalnız port 80 dinler).
4. **Not (E1):** Canlı girişi henüz ön yüzde olmadığı için bu adım sonunda ekran açılır
   ama veriler gelmez (API 401 döner). Canlı kullanım E1 kapanınca mümkündür.

### 4.5 İlk Admin kullanıcı ve şirket kaydı — **bekliyor (E2, E3)**

Bugünkü kodda canlı için bu adımı yapan bir araç yoktur. Nasıl çalışacağı bilinmesi için
mevcut tasarım:

- Kişi kimlik sağlayıcıda oturum açar; token'daki `iss` (sağlayıcı) + `sub` (kişi kimliği)
  çifti ERP'deki **yerel kullanıcıya** eşlenir. Eşlenmemiş kişi reddedilir
  (`ErpClaimsTransformation.cs`, `ADR-004`).
- Şirketler, kullanıcılar, roller ve atamalar Identity/Organization tablolarında tutulur.
  Geliştirmede bunlar `Seed/*.json` dosyalarından gelir; `organization-seed.json`
  GDC, GSP ve GIN (yer tutucu) şirketlerini içerir.
- `SYSTEM_ADMINISTRATOR` sistem rolü tanımlıdır; kime verileceği insan kapısıdır.

Yapılacak karar (açık seçim): canlıda ilk Admin ve şirket kayıtları **hangi yolla**
oluşturulacak (onaylı bir tek seferlik bootstrap komutu, gözden geçirilmiş SQL betiği,
veya canlı seed dosyası) ve ilk Admin **kim** olacak. Karar sonrası backend şeridi aracı
hazırlar; bu rehber o zaman güncellenir.

### 4.6 Rollerin Ayarlar ekranından atanması — **bekliyor (E4)**

Ayarlar → "Kullanıcılar ve yetkiler" ekranı (`/ayarlar/kullanicilar-ve-yetkiler`) bugün
**yalnız kimin hangi role sahip olduğunu gösterir**; rol verme düğmesi yoktur ve bilerek
eklenmemiştir (NEEDS-SINAN #66 5738113623, `OPEN-013`).

Rol atama açıldığında hangi role hangi iznin verileceği için hazır öneriler:

- **Satış:** `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md` (SAT-D230 ile onaylı varsayılan
  set; Yönetici / Satış Müdürü / Satış Personeli tabloları ve görev ayrılığı kuralları).
- Genel persona kartı: `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md`.
- Yetki modeli ve görev ayrılığı: `docs/security/YETKI_MODELI.md`,
  `docs/security/GOREV_AYRILIGI_ONAY.md`.

Her atama yine Sinan'ın somut kararıdır (insan kapısı).

#### Satış izinleri kurulum kontrol listesi

Sinan aşağıdaki üç izin için somut rol kararını verdi (SAT-D282 —
`docs/decisions/SINAN_KARARLARI_2026-11-10_SATIS_EK55.md`). İş rolleri ve satış izinleri için
kodda seed/grant yoktur. Ayrı rol tanımı adımının biçimi (kurulumda Ayarlar rol ekranı mı, kodda
rol tanımı PR'ı mı) henüz seçilmedi (`NEEDS-SINAN`); o zamana kadar EK54 SAT-D276 (3) gereği
kurulum operatörü bu atamaları Ayarlar rol ekranından yapar. Ekran gelene kadar (E4) bu liste
bekler; ilk rol dağılımının başka bir yolla verilmesi ayrıca Sinan kararıdır.

- [ ] `sales.invoices.export` (satış faturalarını Excel'e aktarma, API-34): **Muhasebe,
  Muhasebe Müdürü, Genel Müdür, Admin**. Satış Müdürü ve Satış Uzmanı **almaz** (SAT-D276).
- [ ] `sales.orders.margin.read` (siparişte marjı görme, S-05): **Genel Müdür, Admin**;
  başka rol almaz (SAT-D250, SAT-D276).
- [ ] `sales.orders.source-plan.write` (sipariş satırının kaynak planını belirleme, API-11):
  **Satış Destek, Satış Uzmanı, Satış Müdürü, Genel Müdür, Admin** (SAT-D179; "Müdür"
  SAT-D188 ile Satış Müdürü ve Genel Müdür'dür). Sevkiyatçı, Muhasebe ve Muhasebe Müdürü
  almaz.

---

## 5. Yedekleme ve geri yükleme

Bu konuda repoda ayrıntılı bir hazırlık belgesi var; burada tekrar edilmez:

- `docs/architecture/DEPLOY_BACKUP_RPO_RTO.md` — geçici hedefler ve production kapısı.
- `docs/features/ERP-000N_BACKUP_RESTORE_READINESS.md` — yedek/geri yükleme hazırlık
  analizi; bugün **NO-GO** (backup/restore betiği ve ortak geri dönüş noktası henüz yok).
- `contracts/recovery/recovery-surface-inventory-v1.schema.json` ve
  `tools/generate_recovery_surface_inventory.py` — hangi tablo/şemaların geri yüklemede
  kapsanması gerektiğinin kaynak envanteri (yalnız kaynak; yedek almaz).

Asgari uygulama (BT için sade özet):

1. **Ne yedeklenir:** (a) SQL Server veritabanının tamamı, (b) Belge Kasası klasörü
   (`Documents:ObjectStore:RootPath`), (c) yapılandırma ve gizli anahtarlar (ayrı ve
   şifreli; imza anahtarları kaybolursa sayfalama imleçleri geçersiz olur).
2. **Veritabanı:** tam yedek + işlem günlüğü (log) yedeği ile nokta-zamana dönüş.
   Geçici hedef RPO 15 dk / RTO 4 saat **yalnız varsayımdır** (`OPEN-017`); yedek sıklığı
   ve saklama süresi **Sinan/BT seçer**.
3. **Veritabanı ve belge klasörü birlikte** geri yüklenmeli ve sayı/özet (checksum)
   mutabakatı yapılmalıdır (`DEPLOY_BACKUP_RPO_RTO.md` "Production kapısı").
4. Kopyalardan biri **tesis dışında ve şifreli** tutulur.
5. Bugünkü kodda belge silme yolu yoktur. Yasal alt sınır (TTK 10 yıl / VUK 5 yıl —
   büyüğü) kullanıcı kararıyla **sistemce zorlanır** (`SINAN_KARARLARI_2026-10-21.md` EK-43);
   bunun altına inilemez. Açık olan belge türüne göre süre tablosu, yasal tutma (legal hold)
   ve imha yöntemidir (`DEC-DOC-008`, `OPEN-018`/`OPEN-021`); bunlar **Sinan kararıdır**,
   yedek kopyaların saklama süresi bu alt sınırla çelişmeyecek şekilde ayarlanır.
6. Canlıya geçmeden önce en az bir kez **gerçek geri yükleme tatbikatı** yapılır ve
   sonucu yazılır.

---

## 6. İzleme ve loglar

Bugün kodda olan:

- **Loglar:** ASP.NET Core'un yerleşik loglaması (varsayılan konsol). Seviye
  `Logging:LogLevel` ile ayarlanır. Merkezi log toplama (ör. dosyaya yazma, log sunucusu)
  kodda **yok**; servis yöneticisinin (Windows Event Log / journald / konteyner) çıktıyı
  toplaması gerekir. Araç: **Sinan/BT seçer**.
- **Korelasyon:** her isteğe korelasyon kimliği eklenir (`CorrelationContextMiddleware.cs`);
  sorun bildirirken bu kimlik paylaşılır. Kritik işlemler ayrıca veritabanındaki denetim
  (audit) kaydına yazılır.
- **Sağlık uçları:** `GET /health/live` (ayakta mı) ve `GET /health/ready` (veritabanı ve
  şema uyumlu mu). İzleme aracı bunları düzenli çağırmalı.

Hedeflenen ama kodda henüz olmayan: merkezi metrik/trace (OpenTelemetry vb.), yavaş
sorgu, kuyruk, disk ve **yedek alarmı** (`docs/security/GOZLEMLENEBILIRLIK_GUVENLIK.md`,
DEC-TEC-009). İlk aşamada BT'nin asgari izlemesi: sağlık uçları, disk doluluğu, SQL
Server ajan işleri (yedek başarı/başarısızlık), sertifika bitiş tarihi.

---

## 7. Canlıya geçiş öncesi kontrol listesi

Her madde işaretlenmeden production aktivasyonu istenmez. Aktivasyon kararı Sinan'dadır.

**Engelleyiciler**

- [ ] E1 kapandı: ön yüzde OIDC girişi var ve test ortamında gerçek kullanıcıyla denendi.
- [ ] E2 kapandı: migration uygulama yöntemi seçildi, Test/UAT'ta denendi.
- [ ] E3 kapandı: ilk Admin ve şirket kaydı yöntemi seçildi ve uygulandı.
- [ ] E4: rol atama yolu var **veya** ilk rollerin nasıl verileceği Sinan kararıyla yazıldı.
- [ ] `OPEN-013` (kimlik sağlayıcı, MFA, dış erişim) için Sinan kararı var.

**Ortam ve güvenlik**

- [ ] `ASPNETCORE_ENVIRONMENT=Production`; `DevelopmentBootstrap` ve `VITE_BOOTSTRAP_IDENTITY_ENABLED=true` yok.
- [ ] Tüm 🔒 değerler gizli kasada; repoda, `appsettings*.json`'da veya e-postada değer yok.
- [ ] Uygulama SQL'e `sa` ile değil kendi hesabıyla bağlanıyor; bağlantı şifreli.
- [ ] HTTPS çalışıyor; sertifika geçerli; HTTP → HTTPS yönlendirmesi ters vekilde.
- [ ] `Cors:AllowedOrigins` ve `AllowedHosts` gerçek alan adına göre ayarlandı.
- [ ] RCSI açık (`SQL_SERVER_RCSI_READINESS_RUNBOOK.md`).
- [ ] `GET /health/ready` = 200 (14 context) **ve** Settings, ProductPriceList, Documents, Work migration geçmişi elle karşılaştırıldı (Bölüm 4.2 adım 7).
- [ ] Belge Kasası açılacaksa: klasör + Defender birlikte ayarlı, bir dosya yükleme/indirme denendi.

**Veri**

- [ ] **Test verisi temizlendi:** canlı veritabanı boş kurulumdan başladı; geliştirme/test
      ortamındaki örnek cari, ürün, sipariş, stok ve belgeler canlıya **taşınmadı**.
- [ ] Geliştirme kullanıcıları (`company-a-user`, `company-b-user`,
      `group-bootstrap-admin`, `company-a-viewer`, `site-audit-reader` — issuer
      `urn:goymen:development-bootstrap`) canlı veritabanında **yok**.
- [ ] `organization-seed.json`'daki yer tutucu şirket (GIN, `IsPlaceholder: true`,
      VKN `1111111111`) canlıya alınıp alınmayacağı Sinan kararıyla belirlendi.
- [ ] Eski sistemden veri aktarımı yapılacaksa `docs/migration/CUTOVER_RUNBOOK.md`
      (prova, final delta, mutabakat, geri dönüş zaman sınırı) izlendi.

**Yedek ve operasyon**

- [ ] Veritabanı + belge klasörü yedeği otomatik alınıyor; bir kopya tesis dışında, şifreli.
- [ ] En az bir geri yükleme tatbikatı yapıldı, süre ve sonuç yazıldı.
- [ ] Sağlık uçları ve yedek işleri izleniyor; uyarı kime gidecek belli.
- [ ] Yayınlanan commit ve (varsa) sürüm manifesti kaydedildi; geri dönüş planı
      (önceki sürüm paketi + migration öncesi yedek) hazır.

---

## 8. Açık seçimler listesi

| # | Seçim | İlgili | Kim |
|---|---|---|---|
| S1 | Kimlik sağlayıcı (OIDC/SSO), MFA, dış erişim politikası | `OPEN-013` | Sinan |
| S2 | Sunucu donanımı, işletim sistemi, ağ, UPS; on-prem/kiralık/karma | `OPEN-014` | Sinan/BT |
| S3 | SQL Server sürümü, lisansı, tek sunucu/kümeleme; uygulama ve migration hesaplarının izinleri | — | Sinan/BT |
| S4 | Migration uygulama yöntemi (idempotent SQL betiği / EF bundle / onaylı araç) ve Work modülünün dahil edilmesi | E2 | Sinan/BT + Codex |
| S5 | Canlı ilk Admin ve şirket kaydının yöntemi ve ilk Admin kişisi; GIN yer tutucusunun durumu | E3 | Sinan |
| S6 | Rol atama yolu ve ilk rol dağılımı | E4, `OPEN-013`, `SATIS_ROL_YETKI_ONERISI_v1.md` | Sinan |
| S7 | Belge Kasası ilk gün açılsın mı; klasör konumu; Windows sunucu | DEC-DOC-009 | Sinan/BT |
| S8 | Alan adı, TLS sertifika kaynağı, ters vekil (nginx/IIS), yalnız VPN mi dış erişim mi | `OPEN-014` | Sinan/BT |
| S9 | Gizli kasa ürünü ve anahtar üretme/yenileme yöntemi | — | Sinan/BT |
| S10 | Yedek sıklığı, saklama süresi, tesis dışı hedef; RPO/RTO'nun kesinleşmesi | `OPEN-017` | Sinan/BT |
| S11 | Log toplama ve izleme aracı; alarm alıcıları | DEC-TEC-009 | Sinan/BT |
| S12 | Sağlık uçlarının dışarıya açık olup olmayacağı | — | BT |
| S13 | TCMB kur kaynağının açılması | `Settings:ExchangeRates`, insan kapısı | Sinan |
| S14 | Outbox dağıtıcısının bağlanması ve değerleri (BatchSize, MaxAttempts vb.) | `WorkOutboxDispatcherOptions.cs`, NEEDS-SINAN | Sinan + Codex |
| S15 | Ürün pilot yetkilisi bağı (`ProductRuntime:*`) — Ürün modülü canlıda açılsın mı | — | Sinan |

---

## Kaynaklar

- `AGENTS.md` (insan kapıları, mimari değişmezler)
- `src/backend/GoymenERP.Api/Program.cs`, `appsettings.json`, `appsettings.Development.json`, `Seed/*.json`, `Dockerfile`
- `src/backend/GoymenERP.Api/Security/ErpAuthenticationExtensions.cs`, `ErpClaimsTransformation.cs`
- `src/backend/GoymenERP.Api/Startup/DatabaseStartupMode.cs`, `DatabaseSchemaCompatibilityHealthCheck.cs`
- `src/backend/GoymenERP.Api/Endpoints/HealthEndpoints.cs`, `IdentityEndpoints.cs`
- `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/DocumentsModuleExtensions.cs`
- `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyCursorCodec.cs`
- `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/ExchangeRates/ExchangeRateSources.cs`
- `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcherOptions.cs`
- `src/frontend/goymen-erp-web/package.json`, `Dockerfile`, `nginx.conf`, `src/identity/capabilities.ts`
- `.env.example`, `global.json`, `dotnet-tools.json`, `infra/docker-compose.yml`
- `docs/architecture/DEPLOY_BACKUP_RPO_RTO.md`, `docs/features/ERP-000N_BACKUP_RESTORE_READINESS.md`
- `docs/operations/SQL_SERVER_RCSI_READINESS_RUNBOOK.md`, `docs/migration/CUTOVER_RUNBOOK.md`
- `docs/adr/ADR-004-identity-and-authorization.md`, `docs/security/GOZLEMLENEBILIRLIK_GUVENLIK.md`
- `docs/decisions/ACIK_KARARLAR.md` (`OPEN-013`, `OPEN-014`, `OPEN-016`, `OPEN-017`)
- `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md`
- `tools/generate_release_manifest.py`, `tools/generate_recovery_surface_inventory.py`
