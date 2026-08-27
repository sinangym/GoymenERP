# Göymen ERP

Göymen ERP’nin bağımsız, modüler monolit uygulama repository’si.

Bu dal ERP-000A+B temelinin üzerine ERP-000C kimlik ve yetki iskeletini uygular. Satış,
stok, üretim, finans, seçilmiş production SSO sağlayıcısı, gerçek AI, Mikro/e-fatura ve
R-06/R-07 rapor oluşturucu henüz uygulanmamıştır.

## Uygulanan kapsam

- .NET 10 ASP.NET Core API ve React/TypeScript web uygulaması.
- Domain/Application/Infrastructure/API sınırları olan Organization modülü.
- SQL Server + EF Core migration başlangıcı.
- `Company`, `Site`, `Location`, `WorkCenter` modelleri.
- Zorunlu `CompanyId`, request-scoped `CompanyContext`, EF global query filter ve
  DbContext seviyesinde fail-closed tenant write guard.
- Claim kapsamına göre şirket seçici API/UI iskeleti.
- Sağlayıcıdan bağımsız OIDC/JWT doğrulama yolu ve yalnız Development/Testing ortamında
  çalışabilen yerel bootstrap kimliği.
- Dış `issuer + subject` değerini değişmez yerel ERP kullanıcısına bağlayan Identity modülü.
- Rol→izin RBAC modeli; şirket/tesis/lokasyon/iş merkezi kapsamlı ABAC filtreleri.
- Backend policy enforcement, varsayılan deny ve eşlenmemiş dış kimliğin reddi.
- Kritik yetki taleplerinde talep eden/hedef kullanıcı ile onaylayanın ayrılması için SoD
  domain temeli.
- Osmaniye, Gaziantep ve dış depoları okuyan idempotent JSON seed mekanizması.
- Query, command, ID, URL, query-parametresi, hata ve toplam sayı izolasyon testleri.
- Açık ve yalnız bootstrap/seed akışına ayrılmış çok şirketli sistem-write scope.
- Docker Compose, iki uygulama imajı ve gerçek SQL Server kabul işi içeren GitHub Actions CI.

## Proje yapısı

```text
src/
  backend/
    BuildingBlocks/
    GoymenERP.Api/
    Modules/
      Identity/
      Organization/
      Domain/
      Application/
      Infrastructure/
  frontend/goymen-erp-web/
tests/
  GoymenERP.Organization.UnitTests/
  GoymenERP.Identity.UnitTests/
  GoymenERP.Api.IntegrationTests/
infra/
  docker-compose.yml
docs/adr/
```

## Docker ile yerel çalıştırma

1. `.env.example` dosyasını `.env` adıyla kopyalayın.
2. `MSSQL_SA_PASSWORD` değerini uzun ve rastgele bir geliştirme parolasıyla değiştirin.
3. Repository kökünde çalıştırın:

```powershell
docker compose --env-file .env -f infra/docker-compose.yml up --build
```

- Web: `http://localhost:5173`
- API: `http://localhost:5080`
- Canlılık: `http://localhost:5080/health/live`

Compose, Development ortamında SQL Server hazır olduğunda migration’ı uygular ve seed
verisini idempotent olarak yükler. Production migration’ı CI/CD adımı olarak ayrıca
uygulanmalıdır. Parola veya gerçek secret repository’ye yazılmamalıdır.

## Native geliştirme

Çalışan bir SQL Server örneği için bağlantıyı environment secret olarak tanımlayın:

```powershell
$env:DB_PROVIDER = "SqlServer"
$env:DB_CONNECTION_STRING = "Server=localhost,1433;Database=GoymenErp;User Id=sa;Password=<yerel-secret>;TrustServerCertificate=True"
dotnet tool restore
dotnet run --project src/backend/GoymenERP.Api/GoymenERP.Api.csproj
```

Ayrı terminalde:

```powershell
Set-Location src/frontend/goymen-erp-web
npm ci
npm run dev
```

Development/Testing ortamındaki geçici kimlik, `X-Development-User` başlığını yalnız
izinli bootstrap subject listesiyle doğrular. Ardından subject, Identity veritabanındaki
yerel kullanıcı/rol/atama kayıtlarıyla eşlenir. İstemci varsayılan olarak
`company-a-user` kullanır. Bootstrap mekanizması Production’da başlatılamaz.

Production yolu `Authentication:Mode=OidcJwtBearer` ile çalışır; `Authority` ve
`Audience` verilmezse uygulama fail-closed açılmaz. OIDC sağlayıcısı kullanıcıyı
doğrular, ancak şirket/rol/izin kapsamının kaynağı ERP veritabanıdır. `OPEN-013`
kapanana kadar belirli bir sağlayıcı, MFA veya dış erişim politikası sabitlenmemiştir.

Şirket kapsamlı API çağrıları ayrıca `X-Company-Id` ister. Bu değer, kullanıcının sunucu
tarafı aktif rol atamalarından üretilen şirket kapsamına karşı kontrol edilir; istemcinin
gönderdiği ID tek başına yetki vermez. Organization sorguları ayrıca şirket/tesis/
lokasyon/iş merkezi ABAC kapsamıyla filtrelenir. Mutating command istekleri ayrıca
`Idempotency-Key` ister; `X-Correlation-Id`
gelmezse sunucu üretir. Kalıcı idempotency kaydı ve audit pipeline sonraki platform
görevinde tamamlanacaktır.

## Doğrulama komutları

```powershell
dotnet restore GoymenERP.slnx
dotnet build GoymenERP.slnx --configuration Release --no-restore
dotnet test GoymenERP.slnx --configuration Release --no-build
dotnet list GoymenERP.slnx package --vulnerable --include-transitive

Set-Location src/frontend/goymen-erp-web
npm ci
npm audit --audit-level=high
npm run lint
npm test
npm run build
```

Gerçek SQL Server relational kabul testi, çalışan bir test SQL Server bağlantısını yalnız
environment değişkeninden alır ve benzersiz geçici veritabanını test sonunda siler:

```powershell
$env:GOYMEN_ERP_TEST_SQLSERVER_CONNECTION = "Server=localhost,1433;Database=master;User Id=sa;Password=<yerel-secret>;Encrypt=True;TrustServerCertificate=True"
dotnet test tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj `
  --configuration Release `
  --filter "FullyQualifiedName~SqlServerRelationalAcceptanceTests"
```

Bağlantı değişkeni yoksa bu özel test standart yerel test koşusunda açıkça `Skipped`
görünür; CI’daki `SQL Server relational acceptance` işi değişkeni zorunlu tanımlar ve
testi atlamadan çalıştırır.

Idempotent SQL migration betiği:

```powershell
dotnet tool run dotnet-ef migrations script --idempotent `
  --project src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/GoymenERP.Modules.Organization.Infrastructure.csproj `
  --startup-project src/backend/GoymenERP.Api/GoymenERP.Api.csproj `
  --output artifacts/organization-migration.sql

dotnet tool run dotnet-ef migrations script --idempotent `
  --project src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/GoymenERP.Modules.Identity.Infrastructure.csproj `
  --startup-project src/backend/GoymenERP.Api/GoymenERP.Api.csproj `
  --context IdentityDbContext `
  --output artifacts/identity-migration.sql

dotnet tool run dotnet-ef migrations script --idempotent `
  --project src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/GoymenERP.Modules.Audit.Infrastructure.csproj `
  --startup-project src/backend/GoymenERP.Api/GoymenERP.Api.csproj `
  --context AuditDbContext `
  --output artifacts/audit-migration.sql

dotnet tool run dotnet-ef migrations script --idempotent `
  --project src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/GoymenERP.Modules.Idempotency.Infrastructure.csproj `
  --startup-project src/backend/GoymenERP.Api/GoymenERP.Api.csproj `
  --context CommandExecutionDbContext `
  --output artifacts/idempotency-migration.sql
```

## Geçici varsayımlar ve sonraki kapılar

- Hukuki şirket adları ve resmî bilgileri teyit edilmediği için `TBD-GOY-01/02`
  placeholder olarak kalır.
- Fiziksel tesis/lokasyon şablonu iki hukuki şirket için company-scoped kayıtlar üretir.
  Ortak fiziksel yer kimliği gereksinimi, gerçek sahiplik/işletmeci bilgisiyle ayrıca
  karara bağlanmalıdır.
- Production SSO sağlayıcısı, MFA ve dış erişim politikası `OPEN-013` kapanışında seçilir;
  mevcut OIDC/JWT yolu sağlayıcıya özel kod içermez.
- Kalıcı append-only audit/korelasyon ERP-000D olarak `main`dedir. ERP-000E kalıcı
  idempotency store, response replay ve SQL `rowversion` ekler; outbox/inbox ERP-000F
  görevidir.
- SQL Server nihai ürün kararı değildir; PoC performans, Türkçe arama, restore ve üç yıllık
  veri hacmi testlerinden geçmelidir.
- ERP-000F’ye geçmeden önce kimlik, Company isolation, audit, idempotency ve concurrency
  kabul testlerinin tamamı yeşil kalmalıdır.
