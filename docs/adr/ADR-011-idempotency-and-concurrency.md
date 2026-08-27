# ADR-011 — Kalıcı Idempotency ve Optimistic Concurrency

- Tarih: 2026-08-27
- Durum: Uygulandı, review bekliyor
- Görev: ERP-000E
- Bağımlılık: ERP-000D / `908bbfd5083debfabbe660d45b7ec99fa6d1b9d6`

## Bağlam

Ağ timeout'u, çift tıklama, mobil yeniden gönderim veya istemci retry davranışı aynı
kritik komutu ikinci kez çalıştırmamalıdır. Yalnız HTTP header zorunluluğu bunu sağlamaz;
sonuç kalıcı ve şirket kapsamlı saklanmalı, paralel yarış veritabanında kapanmalı ve iş
değişikliği ile audit kanıtından ayrışmamalıdır.

## Karar

1. `Platform.Idempotency` ayrı domain/infrastructure sınırı ve `idempotency` SQL şeması
   sahibidir. `CommandExecutions` yalnız tamamlanmış komut sonuçlarını taşır.
2. Benzersiz iş anahtarı `(CompanyId, CommandName, IdempotencyKeyHash)` bileşimidir.
   Ham `Idempotency-Key` saklanmaz; trim edilmiş değerin büyük harf SHA-256 özeti tutulur.
3. Canonical request fingerprint; sürümlü command adı, path kimliği ve normalize edilmiş
   iş payload'ından üretilir. Aktör ayrıca saklanır. Aynı anahtar + farklı request hash
   veya farklı aktör kesin `409` üretir ve eski sonucu açıklamaz.
4. Aynı anahtar + aynı payload + aynı aktör, kalıcı `ResponseJson/Outcome` sonucunu
   replay eder. Response `Idempotency-Replayed: true` ve ilk correlation kimliğini
   taşıyan `Idempotency-Original-Correlation-Id` header'larını ekler.
5. Yetki ve aktif `CompanyContext` kontrolü replay kontrolünden önce kalır. Yetkisi
   kaldırılan aktör eski sonucu idempotency üzerinden okuyamaz. Site/Location/WorkCenter
   kapsamı olan komutlarda hedef kaynağın güncel kapsamı da kalıcı sonuç okunmadan önce
   yeniden doğrulanır; kapsam dışına çıkan hedef `404` döner ve replay/original-correlation
   header'ları açıklanmaz. Global query filter ve write guard şirket sınırını ayrıca
   zorunlu tutar.
6. Başarılı kritik komutta business update + append-only audit insert + command result
   insert aynı EF `SaveChanges` ve SQL transaction'ındadır. Üçünden biri reddedilirse
   tamamı rollback olur. Transient/500 sonuç kalıcı başarı gibi kaydedilmez.
7. Yetkili terminal `NotFound` ve `VersionConflict` sonuçları da semantik olarak replay
   edilebilir. Böylece aynı key'in daha sonra farklı sonuç üretmesi engellenir.
   `VersionConflict` snapshot'ı beklenen ve kalıcı kayıttan okunan mevcut sürümü taşır;
   API bu farkı makine-okunur `conflict` uzantısı olarak döndürür.
8. Paralel aynı-key yarışında SQL unique index tek kazanan bırakır; kaybeden rollback
   sonrası kazananın kalıcı sonucunu okur. Farklı key yarışında beklenen iş `Version`
   değeri ve SQL Server `rowversion` birlikte optimistic concurrency uygular.
9. Command execution kayıtları normal uygulama yolunda update/delete edilemez; EF write
   guard ve SQL trigger `51010` ile append-only korunur.

## Kapsam dışı ve açık politika

- Transactional outbox/inbox, consumer dedup ve retry ERP-000F kapsamıdır.
- Replay retention/partition/privileged purge politikası `OPEN-021` kapanmadan production
  temizleme işi etkinleştirilmez.
- Genel middleware bütün endpoint'leri sihirli biçimde idempotent yapmaz. Her kritik
  command sürümlü canonical fingerprint ve kalıcı response sözleşmesini açıkça bağlar.

## Dar modül bağımlılığı istisnası

`OrganizationDbContext`, business update, audit insert ve tamamlanmış command result
insert'ini aynı EF `SaveChanges` ve SQL transaction'ında atomik tutmak için Idempotency
Infrastructure'ın EF model mapping uzantısını kullanır. Bu karar yalnız aşağıdaki exact
project-reference kenarına izin verir:

`Organization.Infrastructure -> Idempotency.Infrastructure`

İstisna `docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json` içinde bu ADR'ye bağlıdır.
Organization'dan Idempotency Domain'e ayrıca doğrudan project reference verilmez;
başka Idempotency Infrastructure servisine erişim veya kenarın genişletilmesi bu kararın
kapsamında değildir. Architecture dependency guard, allowlist dışındaki ve artık gerçek
grafikte bulunmayan stale kenarları reddeder.

## Migration ve rollback

- `InitialIdempotency`, ayrı `[idempotency].[__EFMigrationsHistory]`, unique index,
  JSON/hash check constraint'leri ve immutable trigger oluşturur.
- `AddSqlRowVersionConcurrency`, Company/Site/Location/WorkCenter tablolarına SQL Server
  `rowversion` ekler; mevcut iş `Version` değeri API conflict sözleşmesi olarak korunur.
- Production'da command result üretildikten sonra tablo düşürülmez. Binary rollback
  gerekirse şema korunur ve forward-fix uygulanır.

## Test ve kanıt

- Unit: canonical key/payload hash, hash/JSON doğrulaması ve Company write guard.
- HTTP: aynı request replay, payload/aktör çakışması, `NotFound` ve `VersionConflict`
  terminal replay, beklenen/mevcut sürüm farkı, yetki kapsamı daraltıldıktan sonra güvenli
  replay reddi, stale expected version ve şirketler arası aynı-key izolasyonu.
- Gerçek SQL Server: kalıcı HTTP response snapshot, şirketler arası aynı-key izolasyonu,
  aynı-key paralel retry, farklı-key concurrency yarışı, unique index, gerçek `rowversion`,
  daraltılmış kaynak kapsamından replay reddi, kalıcı conflict farkı, idempotency insert
  hata enjeksiyonunda business+audit rollback ve update/delete `51010` reddi.

ERP-000E ancak review ve uzak CI kapıları geçtikten sonra `done` yapılır. Bu gerçekleşmeden
ERP-000F başlatılmaz.
