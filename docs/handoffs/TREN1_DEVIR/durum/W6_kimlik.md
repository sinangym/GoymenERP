# Kimlik — dalga 6 kaynak raporu

Dal: `codex/tren-1-kimlik-w6`; taban `f3224bcee`.

`SOURCE_READY`, kod ve kabul testlerinin kaynakta bulunduğunu belirtir. Global DONE, gerçek SQL, tarayıcı, CI,
bağımsız K3 incelemesi veya insan grant/aktivasyon kanıtı değildir. Aşağıdaki tüm test koşumları **NOT_RUN**;
trenin son kabul koşumuna bırakılmıştır. Merkezi plan ve üretilmiş pinler ekip dalında değiştirilmedi.

| Dilim | Kaynak durumu | Commit ve başlığı | Tamamlanan / kalan |
|---|---|---|---|
| KIM-S18 | DONE — SOURCE_READY; kabul NOT_RUN | `8ed2c8715` — `test(KIM-S18): cover KIM-D49 KIM-D51 and TK-43 reference list lifecycle` | W5'teki port, kayıt ve ekran yeniden yazılmadı. Gerçek-SQL lifecycle/Down kaynakları eklendi; masraf/hizmet ve boya/hurda okuması tüketen ekiplere bildirildi. Operasyon tipi TK-43 gereği kapalı kaldı. |
| KIM-S3 | DONE — SOURCE_READY; kabul NOT_RUN | `12b6cbd44` — `feat(KIM-S3): add KIM-D12 forced password change flow` | Zorunlu parola ekranı, mevcut session/password komutu ve shell bootstrap çiti. Bilinmeyen sonuç aynı istek/anahtarla; parolalar yalnız geçici bellekte, storage/log yok. Kimlik /me bozuk yanıtı ayrı ele alındı. |
| KIM-S13 | DONE — SOURCE_READY; kabul NOT_RUN | `011792f01` — `feat(KIM-S13): version KIM-D9 URN-D2 D3 D4 money profiles prospectively`; `347760dc2` — `fix(KIM-S13): preserve URN-D4 money profile unavailable responses on retries` | Mevcut onaylı para profili kimlikleri için şirket/para birimi/işlem sürümleri, yönetim uçları ve ekran. İlk gerçek değişimde mevcut katalog aynen kopyalanır; hiçbir yeni iş varsayılanı seçilmez. Aktarım sonrasında depo yoksa kataloğa dönülmez. Geçmiş belge anlık görüntüleri değişmez. |
| KIM-S15 | PARTIAL | `2863f92d7` — `feat(KIM-S15): expose URN-D6 manufacturing profile prepare and publish screens` | Mevcut parametre/tolerans servislerine iki ekran bağlandı. Ayrı read/prepare/publish kontrolleri; approval çağrısı yok, hazırlayan yayımlayabilir. Organization okuması olmadan iş merkezi uydurulmaz. **Version-0 aktarımı TK-38 insan kapısında**, uygulanmadı. |
| KIM-S6 | PARTIAL | `975b52960` — `feat(KIM-S6): add KIM-D1 D15 D17 D19 role assignment and approval UI` | Mevcut ID-3 atama/geri alma/talep/onay/ret/geri çekme uçları bağlandı. Atama sürümü ve rol Critical bilgisi owner okumasından gelir. Kendi/hedef talebinde karar düğmeleri çizilmez. Son Admin reddi ayrı Türkçe mesajdır. **Kullanıcı oluştur/sıfırla/kilit aç/pasifleştir uçları ve eylemleri kaynakta eksik**, bu parçalar tamamlanmadı. |
| KIM-S19 | PARTIAL | `a1a2a161f` — `feat(KIM-S19): connect B13 finance opening period UI and SQL evidence source` | Mevcut Finans açılış dönemi kaydı ekranı ve gerçek-SQL lifecycle/Down/CHECK kaynakları. Boş dönem açık sayılmaz; yeni değer kullanıcı tarafından açıkça seçilir. **Numara serisi sağlayıcısı/biçim editörü ve diğer politika ekranları eksik**. KIM-D53 kararı kaynak yorumlarında güncellendi; eski N-12 kararsızlık notu artık geçerli değildir. |
| KIM-SC32-a | DOGRULA sonucu: PARTIAL / uygulanmamış | Bu dalgada commit yok | Settings teminat türü kataloğu ve Finans adapter'ı yok; Finance sözleşmesi ve Unavailable fallback vardır. FNS-D70 kaynak işi W7'de yapılmalı; DONE değil. |
| KIM-SC32-b | DOGRULA sonucu: PARTIAL / uygulanmamış | Bu dalgada commit yok | Settings sözleşme iskonto sınırı deposu/adapter'ı yok. SAT-D364(1) ve SAT-D358/SAT-D346 geçerli; SAT-D364(2), EK19 KLN-D109 ile düşmüştür. Eksik değer yerine oran veya genel Admin yetkisi seçilmedi. |
| KIM-S5 | PARTIAL / başlanmadı | Bu dalgada commit yok | Şirket SetupPending → ilk Admin → Active olay/inbox akışı eksik. Identity Audit sahibi değişikliği entegrasyonundan sonraki W7 bazında migration üretilmeli. |
| KIM-S7 | PARTIAL; W5 kaynağı korunuyor | Bu dalgada commit yok | recover-founder kaynağı mevcut. TK-44 System Audit sözleşmesi ayrı kapıdır. SecurityEvents elle yazılmış migration/snapshot fact'i gerçek SQL kabulünde mutlaka koşulmalı. |
| KIM-S8, KIM-S9 | PARTIAL / başlanmadı | Bu dalgada commit yok | TOTP kayıt/doğrulama/kapatma, tek kullanımlık kurtarma kodları ve kayıp cihaz kurtarma entegrasyonu eksik. Ayrı, kullanılmayan kripto parçası açılmadı; W7 birleşik bazdan uçtan uca ele alınacak. |

## Derleme ve koşum ayrımı

- S13 ilk kaynak durumunda Api ve Api.IntegrationTests derlemeleri: exit 0, hata/uyarı 0.
- Son S19 kaynak durumunda frontend `npx tsc -b`: exit 0. Paket sürümü/lockfile değiştirilmedi.
- W6 kod head'i `7299315a4` üzerinde Api.IntegrationTests (Api dahil), Identity.UnitTests,
  Organization.UnitTests ve Settings.UnitTests derlemeleri: **exit 0, hata/uyarı 0**. Her derleme öncesi
  en az 2 GiB boş bellek denetlendi; tek .NET slotu kullanıldı. Derleme, test koşumu değildir.
- Unit/Vitest/Architecture/gerçek SQL/tarayıcı/CI: **NOT_RUN**. Skip, silme veya karantina yapılmadı.

## Migration, izin ve üretilmiş yüzeyler

- `20261116162357_AddMoneyProfileVersions`: `dotnet ef` ile üretildi. Up yalnız iki Settings tablosunun DDL'idir:
  `MoneyProfileBaselineTransfers`, `MoneyProfileVersions`. Seed/default yok. Down, iki tablodan biri doluyken
  ilk işlemde `THROW 51940` ile reddeder; boş depoda rollback mümkündür. Numara kaynak taraması ve ekip koordinasyonuyla
  çakışmasız seçildi. Snapshot EF üretimidir.
- Yeni ad: `settings.money-profiles.manage`, yalnız kayıt. Grant/rol ataması veya Critical grant kapısı açılmadı.
- Settings shell provider: **1.9**. Identity shell provider: **1.2**. Shell registry, OpenAPI, recovery/release,
  architecture guard, migration mutation ve ilişkisel/test grubu envanterleri entegrasyonda üreticiyle yenilenmeli.
- Ortak frontend kapsamı: zorunlu parola akışı nedeniyle 36 fixture/harness dosyasına /me alanları eklendi.
  Shell API, orchestrator, mount/capability/navigation dosyalarının bu dalgadaki tek yazarı kimlik ekibidir.
- Identity Audit yazıcı geçişi, Identity DbContext Audit mapping'i ve onun metadata migration'ı platform ekibinindir;
  bu dalda o dosyalara yazılmadı. Settings para profili sağlayıcısı birim/dönem davranışını mevcut sağlayıcıya devreder.

## Yeni ilişkisel fact kaynakları

Hepsi `GoymenERP.Api.IntegrationTests` namespace'indedir, koşumları **NOT_RUN**:

1. `SettingsReferenceListSqlServerRelationalAcceptanceTests.Reference_lists_preserve_company_latest_version_replay_authority_and_audit_on_real_sql`
2. `SettingsReferenceListSqlServerRelationalAcceptanceTests.Reference_list_migration_Down_refuses_existing_entries_and_passes_on_an_empty_store_on_real_sql`
3. `SettingsMoneyProfileSqlServerRelationalAcceptanceTests.Money_profiles_preserve_baseline_snapshots_and_operation_isolation_with_atomic_audit_and_replay_on_real_sql`
4. `IdentityAccessViewSqlServerRelationalAcceptanceTests.Access_reads_return_the_persisted_assignment_version_and_owner_classified_role_criticality`
5. `SettingsCompanyPolicySqlServerRelationalAcceptanceTests.Opening_period_versions_preserve_company_fences_replay_concurrency_audit_and_forward_fix_on_real_sql`

## W5 kırmızılarına kaynak düzeltmesi

`7299315a4` — `test(KIM-S23 KIM-SC34): align KIM-D28 D41 D48 tax fixtures with guarded topology`:

- TaxIdService ve TaxIdCorrectionService fixture'larının boş topolojiyle seeder tarafından komuttan önce
  reddedildiği kaynakta görüldü. Yalnız sentetik fixture'a tek Factory/topoloji eklendi; production seeder ve
  guard'lar gevşetilmedi. Duplicate VKN testi artık ilgisiz boş-seed hatası yerine VKN kontrolüne ulaşır.
- Eski fiscal model/tüm-history migration beklentilerindeki "VKN unique index yok" kuralı KIM-D28/DEC-ORG-005'e
  göre düzeltildi. Unique index, null filtresi ve gerçek şirket VKN zorunluluğu pozitif olarak doğrulanır.
- W5 raporunda Identity tek kırmızısının adı yoktur. Kaynak adayları: Finance approve Critical sınıfıyla uyumsuz
  `None_is_critical(approve)` ve Identity model/snapshot hizalaması. Finans ekibi ilkini ayrı commit'te düzelttiği
  için bu dalda o dosyaya dokunulmadı. Platformun Identity Audit metadata migration'ından sonra
  eski migration beklentisi de platformun `1cf78e1f3` commit'inde son dört migration, snapshot ve boş metadata
  Up/Down pozitif kontrolleriyle hizalandı; bu dalda o dosyaya dokunulmadı. Birleşik bazda kabul koşumu gerekir.
- 25/25 veya 1/1 yeşil olduğu iddia edilmez; nihai koşum henüz yapılmadı.

## Tüketen ekipler ve açık kapılar

- `ISettingsReferenceListReaderV1`: `PaintColor`, `ScrapKind`, `ExpenseType`, `ServiceType`; liste kayıtları
  `{Id,Code,Name,Active,Version}`, tek kayıt sonucu `SettingsReferenceListLookupV1.Outcome`:
  Found / NotFound / Inactive / Unavailable. Şirket filtresi owner'da, tüketen izin çiti tüketendedir.
- Birim kimliği sözleşmesinde yalnız `IsKilogram` ve boyut kimliği vardır. Ton/metre/adet sınıfı veya dönüşüm faktörü
  portu yoktur; stok ekibine koddan semantik türetilmemesi bildirildi.
- Yeni NEEDS-SINAN sorusu üretilmedi. Var olan grant, TK-38/TK-44, production ve diğer insan kapıları korunuyor.
- K3 review bekleyen önceki 12 bypass ve 5 context construction girdisine dokunulmadı. Yeni ham SQL runtime bypass'ı
  açılmadı. SqlServerFact içindeki SQL yalnız izole test verisi/constraint/Down kabul kaynağıdır.
