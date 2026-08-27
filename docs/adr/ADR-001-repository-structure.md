# ADR-001 — Modüler monolit repository yapısı

- Durum: Kabul
- Tarih: 2026-08-27
- Karar sahibi: Göymen ERP teknik ekip
- İlgili karar ID’leri: DEC-TEC-001, DEC-TEC-002, DEC-TEC-008

## Bağlam

ERP-000A+B; yeni repository’de çalışabilir bir platform omurgası, React istemcisi,
SQL Server/EF Core başlangıcı ve ilk organizasyon modülünü birlikte teslim etmelidir.
İlerideki modüllerin aynı süreç içinde güvenli transaction kullanması, fakat doğrudan
birbirinin tablolarına yazmaması gerekir.

## Seçenekler

1. Tek ASP.NET Core projesi içinde katmansız başlangıç.
2. Her modül için ayrı servis ve ayrı dağıtım.
3. Tek dağıtılan uygulama içinde açık Domain/Application/Infrastructure/API sınırları.

## Karar

Üçüncü seçenek uygulanır. Çözüm yapısı:

- `BuildingBlocks`: modüller arası küçük ve kararlı teknik sözleşmeler.
- `Modules/Organization/Domain`: iş modeli ve değişmezleri.
- `Modules/Organization/Application`: use-case portları ve DTO sözleşmeleri.
- `Modules/Organization/Infrastructure`: EF Core, SQL Server, seed ve adapter’lar.
- `GoymenERP.Api`: composition root, HTTP kimliği, CompanyContext middleware ve endpoint’ler.
- `src/frontend`: React/TypeScript şirket seçici ve organizasyon görünümü.
- `tests`: unit ve process içi HTTP integration testleri.

## Sonuçlar ve riskler

- Tek deployment ve tek transaction sınırı korunur.
- Modül bağımlılıkları proje referanslarıyla görünür olur.
- Yeni modüller aynı şablonu izlemelidir; gereksiz mikroservis ayrıştırması yapılmaz.
- BuildingBlocks büyüyüp ortak iş mantığı deposuna dönüşmemelidir.

## Geri alma/yeniden değerlendirme koşulu

Bağımsız ölçekleme, bağımsız yayın veya düzenleyici veri ayrımı somut ölçümle zorunlu
hale gelirse yalnız ilgili modül için servis ayrıştırması yeniden değerlendirilir.

## Test ve kanıt

`dotnet build GoymenERP.slnx` çözüm bağımlılıklarını; unit/integration testleri ise
modülün composition root üzerinden çalıştığını doğrular.
