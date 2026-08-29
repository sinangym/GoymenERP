# Identity — Option A Yetki Uzlaştırma Sözleşmesi v1

- Tarih: `2026-08-29`
- Dal: `codex/identity-option-a-permission-reconciliation-v1`
- Doğrulanmış taban: `main@fed32839a9e0b4258efa5980fddc2846aaf85c49`
- İş kaydı: Issue `#57`
- Ürün kararı: `CARI-PERM-001 / Option A`
- Kanonik karar: Issue `#43`, comment `5461694092`
- Module-owned contract: PR `#61`, head
  `781de1a71d10d83d4b4b5bd1e3c82eac0e61df2c`
- Contract merge commit: `fed32839a9e0b4258efa5980fddc2846aaf85c49`
- Production runner takip kaydı: Issue `#62`
- Global security evidence takip kaydı: Issue `#63`
- Durum: **inert/test edilebilir çekirdek; production activation yok**

## 1. Amaç

`counterparty.accounts.create` yetkisini kabul edilmiş Option A kararına göre yalnız
mevcut kanonik `SYSTEM_ADMINISTRATOR` rolüne ekleyebilen, mevcut kurulum verisini
koruyan ve kimlik/kod uyuşmazlığında fail-closed çalışan generic Identity
rol/yetki uzlaştırma çekirdeğini tanımlamak.

Bu çalışma:

- fresh-install Identity seed'ini kabul edilmiş yetki matrisiyle hizalar,
- existing-install senaryosu için generic ve immutable bir uzlaştırma planı sağlar,
- gerçek SQL Server üzerinde transaction, tekrar, concurrency, rollback ve collision
  davranışını kanıtlar,
- fakat production veritabanında kendiliğinden veya startup sırasında çalışmaz.

## 2. Karar ve kaynak önceliği

Çelişki halinde aşağıdaki sıra bağlayıcıdır:

1. Issue `#43` authoritative product-owner comment `5461694092`.
2. PR `#61` ile merge edilen module-owned JSON politika sözleşmesi.
3. Issue `#57` implementation-start comment `5461864590`.
4. Bu belge ve uygulama testleri.

Issue `#57` body içindeki `COUNTERPARTY_ACCOUNT_OPERATOR` oluşturma veya bu role
permission paketi verme ifadeleri sonraki ürün sahibi kararıyla geçersiz olmuştur ve
uygulama kaynağı değildir.

## 3. Kilitli Option A politikası

İlk sürümde tek yetkili rol:

- Rol kodu: `SYSTEM_ADMINISTRATOR`
- Rol kimliği: `30000000-0000-0000-0000-000000000003`
- Sağlanacak yetki: `counterparty.accounts.create`
- Rol yoksa oluşturma: `false`

Değişmezler:

- `COUNTERPARTY_ACCOUNT_OPERATOR` veya başka yeni rol oluşturulmaz.
- Otomatik kullanıcı/rol assignment eklenmez.
- `ORGANIZATION_READER`, `ORGANIZATION_OPERATOR`, `SECURITY_ADMINISTRATOR`,
  `SECURITY_APPROVER` ve tüm custom roller default-deny kalır.
- Mevcut custom permission, assignment ve scope kayıtları korunur.
- Exact rol ID ile exact ordinal rol code aynı satırı göstermelidir.
- Rol eksikliği veya ID/code collision sessizce düzeltilmez.

## 4. Kapsam

- `ErpPermissions` kataloğuna `counterparty.accounts.create` eklenmesi.
- Yetkinin company-wide scope gerektiren permission olarak sınıflandırılması.
- Fresh seed'de yalnız kanonik sistem yöneticisine grant verilmesi.
- Generic Application plan/target/result/exception sözleşmesi.
- SQL Server'a özgü, explicit transaction kullanan uzlaştırma implementation'ı.
- Unit testleri ve gerçek SQL Server acceptance testleri.
- Dokümantasyon, handoff ve ayrı production takip kayıtları.

## 5. Kapsam dışı

- Production startup, deployment veya migration runner aktivasyonu.
- Global Identity security-change audit/evidence modelinin uygulanması.
- PR `#36`, PR `#60` veya ChatGPT-owned branch'lere yazma.
- Counterparty endpoint, service, domain veya persistence değişikliği.
- Yeni rol, kullanıcı veya assignment üretimi.
- Schema veya EF migration değişikliği.
- `.github/**`, task manifest, governance veya gate gevşetmesi.
- Yetkinin `Critical` ilan edilmesi; bu yönde kabul edilmiş ürün kararı yoktur.
- Provider, deployment topolojisi, retry eşiği veya production davranışı tahmini.

## 6. Permission registry ve fresh seed

`ErpPermissions.CounterpartyAccountsCreate`:

- `All` kümesinde bilinen bir permission'dır,
- `CompanyWideOnly` kümesindedir,
- `Critical` kümesine eklenmemiştir.

Fresh `identity-seed.json` yalnız mevcut `SYSTEM_ADMINISTRATOR` permission listesine
bu kodu ekler. Beş kanonik rol ve altı assignment aynı kalır. Bu seed hizalaması,
mevcut production kurulumlarının yükseltildiği anlamına gelmez.

## 7. Generic Application sözleşmesi

Uygulama katmanı aşağıdaki provider-bağımsız tipleri taşır:

- `ISystemRolePermissionReconciler`
- `SystemRolePermissionReconciliationPlan`
- `SystemRolePermissionTarget`
- `SystemRolePermissionReconciliationResult`
- `SystemRolePermissionReconciliationException`

Plan construction aşamasında aşağıdakiler fail-closed reddedilir:

- boş plan veya target listesi,
- boş rol kimliği,
- boş, trim gerektiren veya uzun rol/permission değeri,
- duplicate rol ID veya exact rol code,
- duplicate veya `ErpPermissions` kataloğunda bilinmeyen permission.

Generic plan birden çok rol hedefini taşıyabilir. Option A consumer planı exact tek rol
ve tek permission'dan oluşur. PR `#61` JSON'u runtime tarafından otomatik okunmaz;
testlerde doğrulanıp generic plana eşlenir. Identity modülü Counterparty assembly'sine
referans vermez.

## 8. SQL Server uzlaştırma algoritması

1. Provider SQL Server değilse açık hata üretilir; atomikmiş gibi davranan InMemory yolu
   sunulmaz.
2. `Serializable` isolation ile explicit transaction açılır.
3. Target'lar role ID'ye göre deterministik sıralanır.
4. Her rol `UPDLOCK, HOLDLOCK` ile ID veya code üzerinden kilitli okunur.
5. Aynı satır hem exact ID hem exact ordinal code karşılığı değilse işlem fail-closed olur.
6. Yalnız eksik permission'lar `RoleDefinition.AddPermission` ile eklenir.
7. Bütün target'lar doğrulandıktan sonra tek `SaveChanges` ve tek commit yapılır.
8. Değişiklik yoksa save üretmeden no-op commit edilir.
9. Her hatada transaction rollback edilir ve change tracker temizlenir.

Reconciler rol oluşturmaz, rol metadata'sını sessizce tamir etmez, permission silmez,
assignment veya scope tablosuna dokunmaz.

## 9. Yetki ve tenant davranışı

Permission rol üzerinde bulunsa bile kullanıcı hedef şirkette geçerli company-wide rol
assignment taşımalıdır. Dar Site/Location/WorkCenter scope bu permission için yeterli
değildir. Yetkisiz roller default-deny kalır.

Bu PR yalnız Identity grant ve uzlaştırma çekirdeğini sağlar. Counterparty endpoint'indeki
403/404, audit ve idempotency etkileri PR `#36` kabul zincirinin sorumluluğundadır.

## 10. Kabul kanıtları

| Kanıt | Doğrulanan davranış |
|---|---|
| Plan unit testleri | Known/company-wide/not-critical sınıflandırması; PR #61 JSON'unun exact plana eşlenmesi; unknown, duplicate ve non-exact değerlerin reddi |
| Fresh SQL Server | Exact beş rol; yalnız admin grant; operator rolü yok; altı assignment; admin allowed, diğer roller denied |
| Existing-install SQL Server | Eski seed snapshot'ına tek permission upgrade; custom permission, assignment ve scope parity |
| Repeat ve concurrency | İkinci çalışma no-op; iki paralel caller sonunda toplam tek grant |
| Collision ve missing role | ID/code drift veya eksik admin fail-closed; snapshot değişmez |
| Rollback | Trigger ile insert failure; iki-permission planında tam rollback ve sonraki recovery |

2026-08-29 yerel kanıtı:

- Release solution build: `0` warning, `0` error.
- Tüm solution testleri: `99` başarılı, SQL bağlantısı olmayan normal koşuda `10`
  ilişkisel test beklenen şekilde skip.
- Gerçek SQL Server relational acceptance: `10/10` başarılı; Option A hedefli iki test
  bu paketin içindedir.
- Vulnerable NuGet dependency taraması: bulgu yok.
- `git diff --check`: temiz.
- Bağımsız exact-diff self-review: P0/P1 yok.

2026-08-29 uzak exact-head ve merge kanıtı:

- PR `#64` exact head `b395da940a9928c515132cb550608dbb7a037aad` için standart CI
  `33249484752` ve UI golden `33249484747` başarılıdır.
- ChatGPT-CARI exact-head salt-okunur incelemesi P0/P1 bulmadan `HANDOFF-ACCEPTED`
  sonucuyla tamamlanmıştır.
- Açık insan Identity/security onayı sonrasında expected-head korumasıyla squash merge
  yapılmıştır; merge commit `7e832d2b98a07428d46ead54a9d136b24c62203a` olmuştur.
- Merge sonrası aynı `main` SHA için standart CI `33251655476` ve UI golden
  `33251655426` tamamen başarılıdır.

Eski branch veya eski SHA üzerindeki yeşil run yeni head için yeniden kullanılamaz.

## 11. Migration ve geri dönüş

Yeni migration veya schema değişikliği yoktur. Mevcut güvenlik çitleri kullanılır:

- `Roles.Code` unique constraint,
- `RolePermissions(RoleId, PermissionCode)` composite primary key.

Runtime hata geri dönüşü transaction rollback ve güvenli tekrar çalıştırmadır. Uygulanmış
bir permission'ı production'da geri alma ayrı yetkili, auditli ve insan onaylı forward-fix
sürecidir; bu PR sessiz revoke yolu eklemez.

## 12. Production activation ve global evidence kapıları

İki ayrı human-reviewed takip işi açılmıştır:

- Issue `#62`: fail-closed production deployment/upgrade runner ve invocation.
- Issue `#63`: company bağımsız, immutable global Identity security-change evidence.

Bu kararlar kapanmadan:

- reconciler production startup'a bağlanamaz,
- production database'in yükseltildiği iddia edilemez,
- permission grant sessiz startup yan etkisi olamaz,
- dummy `CompanyId`, `Guid.Empty` veya tenant audit filtresi kestirmesi kullanılamaz.

## 13. Definition of Done

- [x] Option A exact permission/rol sınırı kod ve seed'de korunur.
- [x] Existing-install generic reconciliation çekirdeği inert olarak uygulanır.
- [x] Unit, full solution ve gerçek SQL Server testleri yerelde geçer.
- [x] Migration/schema ve sahiplik taşması yoktur.
- [x] Production runner ve global evidence ayrı issue'larda görünürdür.
- [x] Draft PR exact head için standart CI ve UI golden yeşildir.
- [x] ChatGPT-CARI exact-head read-only Identity/security review tamamlanır.
- [x] İnsan Identity/security merge kararı verilir; auto-merge yapılmaz.
- [x] Merge sonrası new-main CI yeşildir.

Production activation ve global security evidence bu çekirdeğin merge edilmesinden sonra
da Issue `#62` ve `#63` kapanana kadar bloklu kalır.
