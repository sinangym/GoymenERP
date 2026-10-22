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
vermez.

**Governance/süreç dosyalarının merge'i (22 Ekim 2026 kullanıcı kararı; önceki "insan review
kapısında kalır" kuralının yerine geçer):** `AGENTS.md`, `docs/governance/**`,
`docs/decisions/**`, `.claude/**` ve `.github/**` kalite kapısı değişiklikleri, aşağıdaki
şartlarla **`CODEX-ORCHESTRATOR` tarafından merge edilebilir**; Sinan'ın tıkı beklenmez:

- Yazar ≠ koordinatör; iki bağımsız exact-head review; açık P0/P1 yok.
- Yalnız doküman/governance/karar/ayar dosyası (`docs/**`, `AGENTS.md`, `.claude/**`)
  değiştiren PR'da **CI koşumu şartı aranmaz**; `git diff --check` + iki review yeter.
- `.github/**` değişikliğinde iki review ayrıca şunu doğrular: her `runs-on` self-hosted
  etikettir, hosted fallback yoktur, artifact/cache/Packages yüklemesi, secret, environment
  veya deploy yüzeyi açılmamıştır.
- Merge sonrası Issue #66'ya tek satır makbuz (PR, merge SHA, iki review bağlantısı).
- Sinan bu yetkiyi Issue #66'ya yazacağı tek satırla geri alabilir.

**Değişmeyen insan kapıları:** deployment, environment, secret, workflow'ların Settings →
Actions'ta etkinleştirilmesi, grant/rol/Critical, production aktivasyonu ve `OPEN-*`
kapatma her zaman somut kullanıcı kararı ister.

`docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md` merge edilmişse ayrıca okunur.

`docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md` merge edilmişse **önce** okunur ve
uygulanır. v3, v2'nin **rol dağılımını, dilim sırasını ve tören seviyesini** günceller;
güvenlik değişmezleri (bu dosyanın "Mimari değişmezler" ve "Yasaklar" bölümleri,
`KARAR_SICILI.md` önceliği, production aktivasyonunun insanda kalması) aynen korunur.
Çelişkide v3 süreç tarafında, v2 güvenlik tarafında üstündür.

`docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v4.md` merge edilmişse v3'ten **önce** okunur ve
uygulanır. v4 (2026-10-21 kullanıcı kararı §2), v3'ün tören seviyesini ve karar akışını
günceller (dalga START-GO, review dondurma + K1/K2 delta review `DELTA-OK`, P8 `DELEGE-*`
delegasyonu, yorum disiplini); güvenlik değişmezleri, K3 tanımı ve K3'teki iki bağımsız
exact-head review aynen korunur. K1/K2'de dondurulmuş `REVIEW-READY <sha>` üzerindeki tam
karşı-ajan review + merge edilecek son head için `DELTA-OK <sha>` imzası "exact-head
bağımsız review" şartını sağlar. Çelişkide v4 süreç tarafında, v2/v3 güvenlik tarafında
üstündür.

- Pre-production PR'ı tek karşı-ajan review + fresh zorunlu CI ile coordinator merge edilir.
  Yalnız doküman/governance/ayar dosyası değiştiren PR'da fresh CI şartı yoktur (yukarıdaki
  22 Ekim kuralı).
- İki bağımsız exact-head review yalnız **K3**'te gerekir; K3 kapsamı dardır: DB
  migration/constraint · para/formül/tolerans · permission/rol · cross-module contract ·
  `.github/**` · production activation. Diğer her şey K1/K2.
- Karara bağlanmamış nokta: ajan **durmaz**; Issue #66'ya `NEEDS-SINAN: <soru>` tek satır
  yazar ve sıradaki dilime geçer. Geri alınabilir teknik/UX ayarı ChatGPT hakemliğiyle
  `ASSUMPTION-*` olabilir; para/stok/üretim/formül/yetki/güvenlik/migration/saklama
  politikası tahminle etkinleştirilemez.
- Şeritler (v3 §6): **Codex** = backend + endpoint + gerçek-SQL + OpenAPI + formül
  calculator. **Claude** = tüm frontend / React shell / ekranlar / generated-client
  tüketimi / browser-a11y. **ChatGPT** = iş spec + acceptance + formül golden + backlog +
  hakemlik (tam-zamanında, bir dilim önde). **`CODEX-ORCHESTRATOR`** = merge. Üç ajan
  aynı dikey dilimin farklı katmanında çalışır.

En güncel kullanıcı kararları için `docs/decisions/` içindeki tarihli `SINAN_KARARLARI_*.md`
dosyaları `KARAR_SICILI.md`'den önce, "son kullanıcı kararı" seviyesinde geçerlidir.

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
  K3 kanıtıyla merge edilebilir (22 Ekim 2026 kuralı: CI koşumu olmadan da, iki review'un
  self-hosted/secret/deploy doğrulamasıyla). Deployment, environment, secret ve production
  yüzeyi her zaman somut kullanıcı kararı ister.

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
