# ERP-000A+B Uygulama Teslim Raporu

- Tarih: 2026-08-27
- Dal: `feature/erp-000ab-platform-core`
- Taban: `main` / `f5483edd76e1141656b02a1fdebb53a0caa04f46`
- Durum: Kodlama ve yerel kabul doğrulaması tamamlandı; feature dalı review için hazırlandı.

## Kapsam sonucu

ERP-000A platform iskeleti ile ERP-000B çok şirket organizasyon çekirdeği birlikte
uygulandı. Satış, stok, üretim, finans, gerçek SSO/MFA, gerçek AI, Mikro/e-fatura ve
v1.1 R-06/R-07 raporlama görevleri bu dala alınmadı.

Faz 0 v1.1 raporlama deltası incelendi ancak `docs/faz0-v1.1-report-builder` dalı bu
feature dala merge edilmedi. Raporlama görevleri ERP-000A+B’yi bloke etmeyen sonraki
backlog olarak kaldı.

ZIP içindeki 14 dosyanın SHA-256 değeri paket manifestiyle birebir doğrulandı. Dışarıda
verilen `CODEX_START_ERP-000A_B_v1.1.md`, ZIP içindeki dosyayla aynı
`B7F274ED2EE652EC5491B8EF4B6E25E8AB0B22FB4C0F07C0B92E3488514E92D8` hash’ine sahiptir.

## Mimari

- .NET 10 çözümü ve ASP.NET Core composition root.
- Organization için Domain, Application ve Infrastructure proje sınırları.
- Ortak CompanyContext ve current-user sözleşmeleri için küçük BuildingBlocks katmanı.
- React 19 + TypeScript 6 + Vite şirket seçici/organizasyon UI iskeleti.
- SQL Server + EF Core 10.0.8, `organization` şeması ve ilk migration.
- Docker Compose ile SQL Server, API ve web servisleri.
- GitHub Actions ile backend, frontend, migration, bağımlılık, gerçek SQL Server kabulü
  ve container build kapıları.

## CompanyId güvenlik sözleşmesi

- Yetkili şirketler yalnız sunucu tarafı claim kapsamından gelir.
- `X-Company-Id`, claim kapsamına karşı doğrulanmadan CompanyContext’e alınmaz.
- CompanyContext request-scoped ve aynı istek içinde değiştirilemez.
- Site, Location ve WorkCenter kayıtlarında CompanyId zorunludur.
- EF global filtreleri bütün tenant sorgularına seçili CompanyId koşulunu ekler.
- DbContext, Added/Modified/Deleted tenant kayıtlarında çözülmüş CompanyContext ve entity
  CompanyId eşleşmesini hem senkron hem asenkron save yolunda fail-closed uygular.
- Çok şirketli seed yalnız Infrastructure içinde etkinleştirilebilen açık ve süreli
  `OrganizationBootstrapWriteScope` üzerinden yazılır; normal request write yolu bunu
  etkinleştiremez.
- `(CompanyId, Code)` unique indeksleri bulunur.
- Location→Site ve WorkCenter→Location foreign key’leri CompanyId’yi bileşik anahtarın
  parçası yapar; şirketler arası hiyerarşi bağlantısı SQL Server’da da engellenir.
- Bilinen başka-şirket ID’si query veya command hedefinde genel 404 üretir.
- Company seçimi yetkisizse genel 403 üretilir; hedef şirket adı/ID’si dönmez.
- Command sözleşmesi actor, CompanyId, correlation, idempotency key ve optimistic version
  bilgisine hazırdır. Kalıcı idempotency/audit deposu sonraki platform görevidir.

## Seed ve migration

Seed kaynağı: `src/backend/GoymenERP.Api/Seed/organization-seed.json`.

- 2 placeholder hukuki şirket.
- Her şirket için Osmaniye fabrika, Gaziantep depo ve dış depo site kayıtları.
- Her şirket için 15 lokasyon.
- Her şirket için Boyama, Boru/Profil, Dilme, Soğuk Hadde ve Boy Kesme iş merkezleri.
- Child ID’ler company + entity type + code üzerinden deterministik üretilir.
- Seeder mevcut company/code anahtarlarını kontrol ederek idempotent çalışır.

İlk migration idempotent SQL betiği olarak da üretildi. Betik 6.705 bayt ve 187 satırdır.

## Test ve kanıt

### Otomatik testler

- Backend build: 0 uyarı, 0 hata.
- Unit: 10/10 geçti; bunun 5’i tenant write guard ve bootstrap scope senaryosudur.
- HTTP integration: 10/10 geçti.
- SQL Server relational acceptance: 1/1 geçti.
- Frontend API sözleşmesi: 2/2 geçti.
- Frontend lint ve production build: geçti.
- NuGet transitif vulnerability taraması: bilinen açık yok.
- npm audit high: bilinen açık yok.
- Docker Compose model doğrulaması: geçti.
- Docker API ve web production image build: geçti.

HTTP integration senaryoları; anonim erişim, authorized company selector, yetkisiz
şirket başlığı, query parametresiyle filtre atlama, doğrudan B şirketi ID’si, B şirketi
command hedefi, seçili şirket command’ı, eksik CompanyContext, eksik Idempotency-Key ve
work-center kapsamını içerir.

Relational CI senaryosu gerçek SQL Server 2022 service container üzerinde benzersiz bir
veritabanı oluşturur; migration ve seed sayımlarını, çözümsüz/eşleşmeyen tenant write
reddini, çapraz-şirket composite FK için SQL hata 547’yi ve HTTP 404/403 izolasyonunu
doğrular. Geçici veritabanı test sonunda kapatılıp silinir.

### Gerçek SQL Server/LocalDB kanıtı

Benzersiz isimli geçici doğrulama veritabanında API başlangıcı migration ve seed’i
uyguladı. Sonuç:

- Health: 200.
- A kullanıcısının company selector sonucu: 1 şirket.
- A şirketi lokasyon toplamı: 15.
- B şirketi lokasyon ID’sine doğrudan URL: 404.
- A kullanıcısının B CompanyId başlığıyla yükseltme denemesi: 403.
- Hata payload’larında B şirketi veya kayıt ID’si: yok.
- Relational sayımlar: 2 Company, 6 Site, 30 Location, 10 WorkCenter, 1 migration.
- A CompanyId + B SiteId ile çapraz Location insert denemesi: SQL hata 547; kayıt sayısı 0.

Geçici doğrulama veritabanı önce hedef adı ve tekil varlığı doğrulandıktan sonra silindi;
silme sonrası varlık sayısı 0 olarak doğrulandı.

### Docker Compose canlı kanıtı

Docker Desktop üzerinde SQL Server 2022, API ve web servisleri birlikte başlatıldı.
İlk image build sırasında `global.json` yalnız `10.0.300` patch bandına izin verdiği için
güncel .NET 10 SDK image'ındaki `10.0.400` seçilemiyordu. Ana/minor sürüm sınırı korunarak
`rollForward` değeri `latestFeature` yapıldı; yerel `10.0.300` ve container `10.0.400`
SDK'larıyla build tekrar doğrulandı.

- SQL Server container health: healthy.
- API health: 200.
- Nginx üzerinden web ana sayfa: 200.
- A kullanıcısının company selector sonucu: 1 şirket.
- A ve B şirketlerinin lokasyon toplamı: ayrı ayrı 15.
- A kullanıcısının B lokasyon ID'sine doğrudan erişimi: 404.
- A kullanıcısının B CompanyId başlığıyla erişimi: 403.
- Hata payload'larında yabancı CompanyId veya LocationId sızıntısı: yok.

Test için oluşturulan `goymen-erp` container, network ve SQL Server volume'u doğrulama
sonunda kaldırıldı. Yeniden kullanılabilir yerel application image'ları korundu.

## Açık noktalar

- Placeholder hukuki şirket bilgileri gerçek verilerle değiştirilmelidir.
- Ortak fiziksel lokasyon ile company-scoped organizasyon kaydı arasındaki nihai sahiplik
  modeli gerçek şirket/işletmeci bilgisi sonrası karara bağlanmalıdır.
- Gerçek OIDC/SSO ve ayrıntılı RBAC/SoD ERP-000C kapsamıdır.
- Kalıcı audit, idempotency store, correlation pipeline ve sistem hesabı sonraki platform
  görevleridir.
- SQL Server seçimi; arama, yük, restore ve üç yıllık hacim PoC’si geçmeden nihai değildir.

## Önerilen PR özeti

Başlık: `feat(platform): implement ERP-000A+B multi-company core`

Özet:

1. .NET 10 + React/TypeScript modüler monolit başlangıcını ekler.
2. Organization domain modelini, SQL Server migration ve idempotent seed’i ekler.
3. Request-scoped CompanyContext ve backend/EF CompanyId izolasyonunu uygular.
4. Şirket seçici API/UI iskeletini ekler.
5. Query/command/ID/URL/filter/error/count izolasyon testlerini ekler.
6. Docker/dev ve CI doğrulama zincirini ekler.

Merge kapısı: CI yeşil olmalı; Company isolation ve SQL Server relational acceptance
testlerinden hiçbiri atlanmamalıdır.
Bu PR, Draft PR #2 raporlama deltasını merge etmez ve ERP-000C’yi başlatmaz.
