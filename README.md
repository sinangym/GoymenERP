# Göymen ERP

Göymen ERP’nin bağımsız, modüler monolit uygulama repository’si.

Bu dal ERP-000A+B kapsamındaki platform ve çok şirket omurgasını içerir. Satış, stok,
üretim, finans, gerçek SSO, gerçek AI, Mikro/e-fatura ve R-06/R-07 rapor oluşturucu
henüz uygulanmamıştır.

## Uygulanan kapsam

- .NET 10 ASP.NET Core API ve React/TypeScript web uygulaması.
- Domain/Application/Infrastructure/API sınırları olan Organization modülü.
- SQL Server + EF Core migration başlangıcı.
- `Company`, `Site`, `Location`, `WorkCenter` modelleri.
- Zorunlu `CompanyId`, request-scoped `CompanyContext` ve EF global query filter.
- Claim kapsamına göre şirket seçici API/UI iskeleti.
- Osmaniye, Gaziantep ve dış depoları okuyan idempotent JSON seed mekanizması.
- Query, command, ID, URL, query-parametresi, hata ve toplam sayı izolasyon testleri.
- Docker Compose, iki uygulama imajı ve GitHub Actions CI.

## Proje yapısı

```text
src/
  backend/
    BuildingBlocks/
    GoymenERP.Api/
    Modules/Organization/
      Domain/
      Application/
      Infrastructure/
  frontend/goymen-erp-web/
tests/
  GoymenERP.Organization.UnitTests/
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

Development/Testing ortamındaki geçici kimlik, `X-Development-User` başlığını sunucu
konfigürasyonundaki kullanıcılarla eşler. İstemci varsayılan olarak `company-a-user`
kullanır. Bu mekanizma Production’da kimlik doğrulamaz; ERP-000C’de gerçek OIDC/SSO ile
değiştirilecektir.

Şirket kapsamlı API çağrıları ayrıca `X-Company-Id` ister. Bu değer, kullanıcının sunucu
tarafı claim kapsamına karşı kontrol edilir; istemcinin gönderdiği ID tek başına yetki
vermez. Mutating command istekleri ayrıca `Idempotency-Key` ister; `X-Correlation-Id`
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

Idempotent SQL migration betiği:

```powershell
dotnet tool run dotnet-ef migrations script --idempotent `
  --project src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/GoymenERP.Modules.Organization.Infrastructure.csproj `
  --startup-project src/backend/GoymenERP.Api/GoymenERP.Api.csproj `
  --output artifacts/organization-migration.sql
```

## Geçici varsayımlar ve sonraki kapılar

- Hukuki şirket adları ve resmî bilgileri teyit edilmediği için `TBD-GOY-01/02`
  placeholder olarak kalır.
- Fiziksel tesis/lokasyon şablonu iki hukuki şirket için company-scoped kayıtlar üretir.
  Ortak fiziksel yer kimliği gereksinimi, gerçek sahiplik/işletmeci bilgisiyle ayrıca
  karara bağlanmalıdır.
- Production kimliği, ayrıntılı RBAC/SoD, audit pipeline, idempotency/correlation command
  zarfı ve şirket veri paketi ERP-000C ve sonraki platform görevleridir.
- SQL Server nihai ürün kararı değildir; PoC performans, Türkçe arama, restore ve üç yıllık
  veri hacmi testlerinden geçmelidir.
- ERP-000C’ye geçmeden önce Company isolation testlerinin tamamı yeşil kalmalıdır.
