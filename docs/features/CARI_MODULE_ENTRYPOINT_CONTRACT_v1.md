# Cari / Counterparty Modül Giriş Noktası Sözleşmesi v1.0

- Tarih: 2026-08-29
- Görev: `CARI-MODULE-ENTRYPOINT-CONTRACT`
- Sahip: `CHATGPT-CARI`
- Taban: `main@a3b58b9266ea192dd029889ac60abfe8b2ec194e`
- Durum: **Sözleşme GO / generic platform guard entegrasyonu ayrı CODEX-PLATFORM işi**
- Makine-okunur kaynak: `src/backend/Modules/Counterparty/COUNTERPARTY_MODULE_ENTRYPOINTS.json`

## 1. Amaç

Counterparty modülünün API composition root tarafından kullanılan meşru proje giriş noktalarını exact ve makine-okunur biçimde tanımlamak. Böylece generic platform architecture guard'ları Counterparty/Cari kaynaklarını kendi sahiplik alanına almadan, yalnız kabul edilmiş composition-root referanslarını fail-closed doğrulayabilir.

Bu belge yeni endpoint, permission, migration, schema veya business davranışı oluşturmaz.

## 2. Kanonik composition-root kenarları

Yalnız aşağıdaki iki proje referansı kabul edilmiştir:

1. `src/backend/GoymenERP.Api/GoymenERP.Api.csproj`
   → `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/GoymenERP.Modules.Counterparty.Application.csproj`
2. `src/backend/GoymenERP.Api/GoymenERP.Api.csproj`
   → `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/GoymenERP.Modules.Counterparty.Infrastructure.csproj`

Bu kenarlar API composition root'un Counterparty endpoint/application contract'larını bağlaması ve persistence/command servislerini kaydetmesi içindir.

## 3. Sahiplik sınırı

- `CHATGPT-CARI`, Counterparty kaynakları, contract içeriği ve bu exact giriş noktalarının sahibidir.
- `CODEX-PLATFORM`, generic architecture guard ve CI entegrasyonunun sahibidir.
- Generic guard bu sözleşmeyi okuyabilir fakat Counterparty source tree'sini platform-owned inventory kapsamına alamaz.
- Wildcard, klasör-geneli skip, isim benzerliği veya tahmini giriş noktası kabul edilmez.
- Sözleşmede olmayan yeni API → Counterparty project reference fail-closed hata üretmelidir.
- Sözleşmede kalıp gerçek csproj'den kaldırılan stale edge de fail-closed hata üretmelidir.

## 4. PR #41 entegrasyon kuralı

PR #41'in mevcut kırmızı sonucu test gevşetilerek kapatılmamalıdır. Güvenli entegrasyon:

1. Bu Cari-owned contract bağımsız PR ile review edilip `main`e alınır.
2. PR #31 ve PR #34 kendi insan-review/merge kapılarından geçer.
3. CODEX-PLATFORM, PR #41'i o güncel `main` ile senkronize eder.
4. Generic guard, bu JSON'daki exact source/target edge'lerini tüketir.
5. Counterparty source scanning exclusion korunur.
6. Windows, Linux/Docker ve protected CI kanıtları exact yeni head üzerinde yeniden üretilir.

## 5. Kabul kriterleri

- JSON schema version tam olarak `1`.
- Owner `CHATGPT-CARI`, module `Counterparty`.
- `excluded_source_prefix` tam olarak `src/backend/Modules/Counterparty/`.
- Exact iki edge vardır; duplicate, wildcard, eksik dosya veya modül dışı target reddedilir.
- `GoymenERP.Api.csproj` içindeki gerçek Counterparty `ProjectReference` seti sözleşmeyle birebir eşleşir.
- Contract source ve bütün source/target dosyaları repository içinde bulunur.
- Cari production davranışı, permission, migration ve endpoint seti değişmez.

## 6. Kapsam dışı

- PR #36 rol/permission upgrade kararı
- PR #38 merge işlemi
- Counterparty migration SQL artifact CI entegrasyonu
- HTTP route manifesti / PR #29 güncellemesi
- Tenant query-filter davranışının kendisi
- Counterparty kaynaklarının platform guard tarafından taranması

## 7. Handoff

Bu sözleşme merge edildikten sonra CODEX-PLATFORM, exact main SHA üzerinden PR #41 entegrasyonunu yapar ve sonucu `HANDOFF-ACCEPTED` veya `REQUEST_CHANGES` olarak geri döndürür. Yeni commit veya base değişiminde eski CI/review kanıtı yeniden kullanılmaz.
