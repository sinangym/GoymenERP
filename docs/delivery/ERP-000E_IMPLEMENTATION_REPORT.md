# ERP-000E Uygulama Teslim Raporu

- Tarih: 2026-08-27
- Dal: `codex/erp-000e-idempotency-concurrency`
- Taban: `main` / `908bbfd5083debfabbe660d45b7ec99fa6d1b9d6`
- Durum: Uygulama ve yerel kabul tamamlandı; review/uzak CI bekleniyor
- Bağlayıcı ADR: `docs/adr/ADR-011-idempotency-and-concurrency.md`

## Sonuç

ERP-000E, kritik lokasyon aktif/pasif komutunu kalıcı sonuç replay'i ve iki katmanlı
optimistic concurrency ile tamamladı. Aynı ağ isteği ikinci business veya audit olayı
üretmez; farklı payload/aktör aynı anahtarı kullanamaz. Başarılı command result, lokasyon
değişikliği ve ERP-000D audit olayı aynı SQL transaction'ında commit/rollback olur.

ERP-000F outbox/inbox bu dala alınmadı.

## Uygulanan sözleşme

- Ayrı `Idempotency.Domain/Infrastructure` projeleri ve `idempotency` SQL şeması.
- Ham anahtar yerine SHA-256 `IdempotencyKeyHash`.
- Canonical path+payload için SHA-256 `RequestHash`.
- Şirket, sürümlü command adı, aktör, outcome, response JSON, ilk correlation, zaman ve
  schema version taşıyan append-only `CommandExecution`.
- `(CompanyId, CommandName, IdempotencyKeyHash)` unique index.
- Aynı aktör/payload replay; farklı aktör veya payload için `409`.
- Replay response header'ları ve ilk correlation kanıtı.
- `Succeeded`, `NotFound` ve `VersionConflict` terminal sonuçlarının güvenli replay'i.
- Company global filter ve runtime write guard.
- EF update/delete reddi ve SQL `51010` immutable trigger.
- İş `Version` token'ına ek gerçek SQL Server `rowversion`.
- CI için dördüncü idempotent migration script artefaktı.

## Atomiklik ve yarış davranışı

İlk yürütmede servis sonucu önceden bulamazsa business kuralını uygular ve aynı
`SaveChanges` içine command result ile audit'i ekler. Aynı-key paralel isteklerden yalnız
biri unique index'i kazanır. Kaybeden transaction rollback olduktan sonra kazanan sonucu
okur. Farklı-key aynı-version yarışında yalnız bir business update başarılı olur; diğeri
`409 VersionConflict` sonucunu kendi idempotency kaydına yazar.

Idempotency insert'ini hata `51011` ile reddeden SQL test trigger'ı altında lokasyon,
audit ve command result birlikte rollback olur.

## Doğrulama

- Release backend: **53/53**.
  - Idempotency unit: 3/3.
  - Organization unit: 12/12.
  - Identity unit: 7/7.
  - Audit unit: 5/5.
  - HTTP integration: 25/25.
  - Gerçek Docker SQL Server relational acceptance: 1/1.
- Build: 0 uyarı, 0 hata.
- NuGet ve npm bilinen güvenlik açığı taraması: 0 bulgu.
- Frontend: lint, 2/2 test ve production build başarılı.
- Docker Compose model doğrulaması ile API/web image build başarılı.
- Gerçek SQL: migration/history, kalıcı response snapshot, şirket bazlı aynı-key izolasyonu,
  replay, request/actor conflict, aynı-key paralel yarış, farklı-key concurrency,
  `rowversion`, atomik rollback ve `51010` doğrulandı.

## Açık sınır ve sonraki kapı

- `OPEN-021`: idempotency retention/partition/privileged purge.
- ERP-000F: transactional outbox/inbox ve consumer dedup.
- ERP-000E review ve uzak CI geçmeden `done` yapılmamalı; ERP-000F başlatılmamalıdır.
