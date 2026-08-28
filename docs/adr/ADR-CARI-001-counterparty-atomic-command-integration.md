# ADR-CARI-001 — Cari Kritik Command Atomikliği İçin Platform Sidecar Entegrasyonu

- Tarih: 2026-08-28
- Durum: **Kabul edildi — manuel mimari review 2026-08-28**
- Kapsam: `CARI-CORE-1B` public write dilimi
- Taban: `main@538175c2d2367d9ba7f0489acec37b72cc03e85e`
- Bağlı kararlar: `CARI-DEC-001…004`, `ADR-009`, `ADR-011`, `DEC-CAR-001…003`, `DEC-GOV-002/003`

## 1. Bağlam

Cari create ve şirket kapsamlı cari-hesap durum command'leri kritik yazmadır. `AGENTS.md`,
`ADR-009` ve `ADR-011` gereği başarılı business mutation; append-only audit kanıtı ve
kalıcı idempotency sonucu birbirinden ayrışmamalıdır.

Mevcut platformda bu atomiklik `OrganizationDbContext` içinde aynı EF `SaveChanges` ve aynı
SQL transaction ile kanıtlanmıştır. Cari için ayrı DbContext/SQL şeması korunurken aynı
platform mekanizmasının dar biçimde yeniden kullanılması gerekir.

Ayrı DbContext'lerle dağıtık/ambient transaction, audit/idempotency tablolarına ham SQL veya
kopya sidecar modeli oluşturmak tercih edilmez; bunlar modül sahipliğini ve kanıtlanmış
platform davranışını zayıflatır.

## 2. Karar

`CounterpartyDbContext` yalnız kritik Cari command'lerinin atomik transaction sınırı için:

1. `IAuditEventCollector` application contract'ına audit taslağı bırakır.
2. `AuditSaveChangesInterceptor`, resmi Audit mapping'iyle audit kaydını aynı
   `CounterpartyDbContext.SaveChanges` içine ekler.
3. `CommandExecution` kalıcı terminal sonucunu resmi Idempotency EF mapping'iyle aynı
   context içinde takip eder.
4. Audit ve Idempotency tabloları Counterparty migration sahipliğine alınmaz;
   mapping'ler `ExcludeFromMigrations` kullanır.
5. Counterparty business entity + audit + command result tek `SaveChanges` ve tek SQL
   transaction'da commit/rollback olur.
6. Counterparty başka Audit/Idempotency Infrastructure servisine erişmez; istisna yalnız
   mapping/interceptor ile aynı-transaction sidecar kalıcılığı içindir.

## 3. Exact dependency istisnaları

Bu ADR yalnız aşağıdaki iki direct project-reference kenarına izin verir:

- `Counterparty.Infrastructure -> Audit.Infrastructure`
- `Counterparty.Infrastructure -> Idempotency.Infrastructure`

Exact repository yolları `docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json` içinde bu
ADR'ye bağlanır. Başka Counterparty→Domain/Infrastructure cross-module kenarı eklenemez.

Bu karar `ADR-009` veya `ADR-011` içindeki Organization'a özel existing istisnaları
sessizce genişletmez; Cari için ayrı ve review edilebilir bir istisna tanımlar.

## 4. Güvenlik ve tenant değişmezleri

- Her public write aktif ve yetkili `CompanyContext` gerektirir.
- Yetki kontrolü idempotency replay okunmadan önce yapılır.
- `CommandExecution.CompanyId` aktif CompanyContext ile eşleşmek zorundadır.
- Audit draft `CompanyId`, actor, permission, correlation/causation ve hash'lenmiş
  idempotency kanıtını platform sözleşmesine göre taşır.
- Party grup seviyesinde tek kimliktir; create command yalnız aktif şirket için ilk
  `CompanyPartyAccount` bağını aynı transaction'da oluşturur.
- Yetkisiz başka şirket bağı, replay veya hata metni üzerinden açıklanmaz.
- VKN/TCKN/MERSİS/IBAN/banka, kritik Party mutation ve maker-checker approval bu ADR'nin
  kapsamı değildir.

## 5. Idempotency ve concurrency

- Create command adı sürümlü ve sabittir.
- Aynı key + aynı actor + aynı canonical payload kalıcı sonucu replay eder.
- Aynı key + farklı actor veya farklı payload `409` üretir.
- Paralel aynı-key create yarışını SQL unique idempotency index'i tek kazanana indirger;
  kaybeden kazananın sonucunu okur.
- CompanyPartyAccount status update `ExpectedVersion` + SQL `rowversion` kullanır;
  stale write `409` terminal sonuçtur.
- `NotFound`/`VersionConflict` gibi yetkili terminal sonuçların replay davranışı
  `ADR-011` ile aynı ilkeleri izler.

## 6. PartyCode allocator

Kullanıcı kararı grup genelinde immutable `CAR-000001…` kodudur. İlk write diliminde
SQL Server sequence kullanılır:

- sequence yalnız teknik sıra üretir; business anlamı taşımaz,
- rollback/yarış nedeniyle boşluk oluşması kabul edilir,
- tüketilmiş sıra tekrar kullanılmaz,
- DB unique index son savunma katmanıdır,
- dış istemci PartyCode gönderemez.

Bu allocator yalnız Counterparty şemasında sahiplenilir ve gerçek SQL concurrency testiyle
kanıtlanmadan production acceptance verilmez.

## 7. Kabul kanıtı

Manuel review öncesi en az:

- aynı create replay ikinci Party/CompanyPartyAccount üretmiyor,
- aynı key farklı payload/actor `409`,
- paralel create'larda PartyCode duplicate yok,
- stale account status write `409`,
- yetkisiz/yanlış CompanyId write fail-closed,
- idempotency veya audit insert hata enjeksiyonunda business write rollback,
- başarılı create/status write için business + audit + command result birlikte kalıcı,
- Counterparty migration audit/idempotency tablolarını sahiplenmiyor,
- architecture dependency guard exact iki yeni kenarı ve stale allowlist durumunu doğruluyor,
- hassas/deferred alanlar ve direct Party mutation route'u yok

kanıtlanmalıdır.

## 8. Rollback

Cross-module referanslar ancak bu write dilimi tamamen geri alınırsa allowlist'ten birlikte
çıkarılır. Production'da audit/idempotency tabloları Counterparty rollback'i nedeniyle
düşürülmez. PartyCode sequence veri üretildikten sonra binary rollback gerekiyorsa mevcut
sequence korunur ve forward-fix uygulanır; kod reuse edilmez.
