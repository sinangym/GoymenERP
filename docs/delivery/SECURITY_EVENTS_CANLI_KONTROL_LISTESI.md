# Güvenlik olayı kanalı — canlıya geçiş kontrol listesi

Kapsam: `security.SecurityEvents` tablosu (ADR-SEC-001; `Modules/SecurityEvents`). Bu liste yalnız **canlı öncesi
insan kapısını** tarif eder. Hiçbir madde kodla otomatik açılmaz. DB login'i, grant, secret ve production
aktivasyonu somut kullanıcı kararı ister (AGENTS.md "Değişmeyen insan kapıları").

Bağlam: ERP-000M §"güven sınırları" runtime, migration ve restore kimliklerini ayrı ister. Production'da
migration'ın nasıl yürütüleceğini (ayrı runner veya başka bir yol) ADR-010 seçer; bu liste o seçimi yapmaz.
Uygulama açılışındaki `Initialize*ModuleAsync` çağrıları yalnız Development/Testing ortamında koşar
(`Program.cs`).

## Koddaki koruma (kanıtlı, CI'da koşar)

| Koruma | Kanıt |
|---|---|
| UPDATE/DELETE reddedilir (`INSTEAD OF` trigger) | `InitialSecurityEvents` migration'ı; gerçek SQL testleri `SecurityEventsSqlServerRelationalAcceptanceTests` |
| Kod listeleri DB'de CHECK ile kapalı (ActorType, Severity, SourceChannel, ReasonCode, AI sınıfları) | `HardenSecurityEventCodeChecks` migration'ı; 547 ret testi |
| Tabloya yalnız writer → store → `AppendAsync` yazar; normal `SaveChanges` reddedilir | `SecurityEventsWriteOwnershipGuardTests` (mimari test) |
| Aynı `EventId` bir kez kaydedilir; içerik farklıysa `duplicate_event_id` | gerçek SQL testi `Duplicate_event_ids_are_recorded_once_…` |
| Saklama süresi sunucu saatiyle hesaplanır | gerçek SQL testi (`RetainUntilUtc`) |

## Trigger'ın kapatmadığı yollar (DB izniyle kapanır)

`INSTEAD OF UPDATE, DELETE` trigger'ı şunları **durdurmaz**:

- `TRUNCATE TABLE` (trigger tetiklenmez);
- `DISABLE TRIGGER` / `DROP TRIGGER` / `ALTER TABLE` (şema değişikliği);
- `sysadmin`/`db_owner` yetkisiyle yapılan her şey.

Bu yüzden ADR-SEC-001 DB iznini "ek savunma" olarak ister. Satır bütünlük hash'i (`IntegrityHash`) sonradan yapılan
değişikliği gösterir ama engellemez.

## Canlı öncesi yapılacaklar (insan kapısı)

1. **Ayrı kimlikler.** Uygulama (runtime) login'i ile migration login'i farklı principal'dır. Runtime login'i
   `db_owner`, `db_ddladmin` veya `sysadmin` üyesi değildir.
2. **Runtime login'inin `security` şeması izni.** Gereken en az izin `INSERT` ve `SELECT`'tir (writer duplicate
   kontrolü için okur). Öneri metni (DBA uygular; karar ve principal adı Sinan/DBA'nındır):

   ```sql
   -- <runtime_user> yer tutucudur; gerçek ad repoya yazılmaz.
   GRANT INSERT, SELECT ON SCHEMA::[security] TO [<runtime_user>];
   DENY UPDATE, DELETE, ALTER ON SCHEMA::[security] TO [<runtime_user>];
   ```

   `DENY ALTER` hem `TRUNCATE`'i hem trigger kapatmayı keser (ikisi de tabloda `ALTER` ister).
3. **Doğrulama.** Runtime login'iyle bağlanılıp şu sorgu çalıştırılır. Beklenen: `INSERT` ve `SELECT` var;
   `UPDATE`, `DELETE`, `ALTER` ve `CONTROL` yok.

   ```sql
   SELECT permission_name FROM fn_my_permissions('security.SecurityEvents', 'OBJECT');
   ```

   Ek olarak `TRUNCATE TABLE security.SecurityEvents;` denemesi yetki hatası vermelidir. Bu deneme yalnız boş bir
   staging veritabanında yapılır.
4. **Migration login'i** yalnız yayın sırasında kullanılır. Secret'ı runtime secret'ından ayrıdır ve repoda
   bulunmaz (ERP-000M; `.env.example` yalnız yer tutucu içerir).
5. **İmha yolu yok.** v1'de trigger'ın istisnası yoktur. Otomatik imha OPEN-021'e bağlıdır ve ayrı ADR ile gelir;
   bu listedeki izinler o kararı önceden vermez.
6. **E8 (kullanıcı adı hash anahtarı)** cevaplanana kadar başarısız girişte kullanıcı adı hash'i yazılmaz.
   Anahtar yalnız secret deposundadır.

## Kimin işi

- 1–4: DBA/altyapı ve Sinan (grant ve secret insan kapısıdır). AI Merkezi şeridi yalnız bu metni ve kod
  tarafındaki korumaları sağlar.
- 5–6: Sinan kararı (`OPEN-021`, E8).
