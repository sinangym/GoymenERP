# AGENTS.md — Göymen ERP Repository Kuralları

## Kaynakların önceliği

1. `docs/decisions/KARAR_SICILI.md`
2. `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`
3. İlgili feature spec ve ADR
4. Eski StokERP yalnız referans/golden kaynak

Çelişkide daha yeni kullanıcı kararı geçerlidir. Açık işletme politikasını tahmin ederek kilitleme; yapılandırılabilir bırak ve `OPEN-*` kimliğiyle işaretle.

## Çalışma orkestrasyonu

Her yeni çalışma döngüsünde `docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`
varsa okunur ve görev/branch/PR/doğrulama akışında uygulanır. Bu protokol yukarıdaki kaynak
önceliğini veya iş kurallarını değiştirmez ve kendi başına dış yazma ya da merge yetkisi
vermez. `AGENTS.md`, governance, CI veya merge politikası değişiklikleri otomatik merge
edilemez; insan review kapısında kalır.

`docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md` merge edilmişse ayrıca okunur.
31 Ağustos 2026 tarihli kullanıcı kararı, geliştirme PR'ları için aşağıdaki dar ve geri
alınabilir standing merge yetkisini verir:

- Şerit yazarı kendi PR'ını merge etmez; merge'i ayrı `CODEX-ORCHESTRATOR` koordinatörü
  expected-head ile yapar.
- K1/K2 implementation PR'ı ancak fresh zorunlu kontroller, exact-head bağımsız review,
  yeni davranış testi ve açık P0/P1 olmaması halinde coordinator tarafından squash merge
  edilebilir.
- K3 implementation PR'ı ancak kilitli kararları uygularsa, açık işletme politikasını
  seçmiyorsa, iki bağımsız exact-head review ve ilgili gerçek SQL/browser/failure kanıtı
  varsa coordinator tarafından merge edilebilir.
- Bu yetki `OPEN-*` kapatma, yeni rol/formül/eşik/para/stok/üretim politikası seçme,
  karar/master/task durumunu sessiz değiştirme, secret/deployment veya production
  aktivasyonu yetkisi değildir.
- `.github/**` içinde yalnız build/test/contract/security kalite kapısı değişikliği aynı
  K3 kanıtıyla merge edilebilir. Deployment, environment, secret ve production yüzeyi
  her zaman somut kullanıcı kararı ister.

GitHub teknik koruması kullanılamıyorsa lane self-merge kapalı kalır; coordinator aynı
kapıları prosedürel olarak doğrular ve kanıtı GitHub'a yazmadan merge yapmaz.

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
