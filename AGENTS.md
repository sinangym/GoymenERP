# AGENTS.md — Göymen ERP Repository Kuralları

## Kaynakların önceliği

1. `docs/decisions/KARAR_SICILI.md`
2. `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`
3. İlgili feature spec ve ADR
4. Eski StokERP yalnız referans/golden kaynak

Çelişkide daha yeni kullanıcı kararı geçerlidir. Açık işletme politikasını tahmin ederek kilitleme; yapılandırılabilir bırak ve `OPEN-*` kimliğiyle işaretle.

## Mimari değişmezler

- Modüler monolit; modül sınırları kod, şema ve servis sözleşmesinde korunur.
- Her kritik kayıtta `CompanyId`; şirket filtresi yalnız UI değildir.
- Bir modül başka modül tablosuna doğrudan yazmaz.
- Kritik commands: authorization, expected version, idempotency key, correlation/causation, audit.
- Dış etkiler transactional outbox; consumers inbox/dedup kullanır.
- Fiziksel/ticari/finansal olay bir kez kaydedilir; kopya hareket üretme.
- Silme yerine pasif, iptal veya ters kayıt; retention/imha ayrı politikadır.
- AI doğrudan DB veya ham SQL kullanamaz; yalnız şemalı backend tools.
- Formula/UI/report aynı deterministic backend calculator’ı kullanır.

## Stack başlangıcı

- Backend: C# / ASP.NET Core / .NET 10
- Frontend: React + TypeScript
- DB PoC: SQL Server; EF Core
- Files: S3-compatible object storage abstraction
- Tests: unit, integration, contract, E2E, security and restore

## Kodlama kuralları

- Kod ve teknik isimler İngilizce; kullanıcı metinleri Türkçe ve kaynak dosyalı/localizable.
- Nullable/units/money/date semantics açık türlerle temsil edilir.
- Money, Quantity, Unit, ExchangeRate, BusinessDate gibi value objects kullan.
- Magic number, hard-coded tolerance, role name veya CompanyId kullanma.
- Migration geri alınabilir veya açık forward-fix planlı olmalı.
- Secrets commit edilmez; `.env.example` yalnız placeholder içerir.
- Her public API ve event sürümlü contract testine sahip olmalıdır.

## PR kabul kapısı

- İlgili karar/feature spec referansı
- CompanyId ve yetki testleri
- Audit/idempotency/concurrency etkisi
- Error/empty/loading/conflict UI durumları
- Migration ve rollback etkisi
- Log/metric/trace
- Unit + integration + gerektiğinde E2E
- P0/P1 açık hata yok

## Yasaklar

- StokERP kodunu kör kopyalama
- Ham SQL’i AI aracına açma
- UI doğrulamasını tek güvenlik olarak kullanma
- Fiyat/formül/toleransı ekran içinde yeniden yazma
- Aynı para/stok olayını ikinci hareket olarak üretme
- Açık kararları sessiz varsayımla production’a alma
