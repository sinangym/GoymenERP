# Göymen ERP — Cari Hesabı Oluşturma Yetkisi Karar Paketi v1.1

- **Durum:** TASLAK / KULLANICI KARARI GEREKLİ
- **Teknik alan:** `Counterparty` (kullanıcı alanı: Cari)
- **Karar kimliği:** `CARI-PERM-001`
- **Kanonik karar kaydı:** Issue #43
- **İlgili permission:** `counterparty.accounts.create`
- **İlgili geliştirme:** PR #36 — mevcut grup Party kaydını başka Göymen şirketine bağlama
- **Bağımlı güvenlik düzeltmesi:** PR #49 ile `main`e alınan no-op optimistic-concurrency hotfix
- **Sahip:** `CHATGPT-CARI`
- **Bağımsız inceleme:** `CODEX-PLATFORM`
- **Merge kapısı:** Açık kullanıcı kararı + exact-head CI + bağımsız review

> Bu belge hiçbir role yetki vermez, Identity seed/migration değiştirmez ve PR #36'yı tamamlanmış saymaz. Amaç, açık işletme politikasını tahmin etmeden bağlayıcı karar seçeneklerini ve teknik kabul kapısını tanımlamaktır.

## 1. Canlı GitHub truth-state ayrımı

### 1.1 Güncel `main`

PR #49 sonrasında güncel `main`:

- no-op account active-state optimistic-concurrency düzeltmesini içerir;
- `counterparty.accounts.create` permission sabitini içermez;
- mevcut Party'yi ikinci Göymen şirketine bağlayan public endpoint'i içermez;
- bu permission için fresh-install grant veya existing-install upgrade davranışı içermez.

### 1.2 Yalnız PR #36 dalında bulunan pending davranış

PR #36 dalı:

- `counterparty.accounts.create` permission sabitini ekler;
- permission'ı development seed içindeki `SYSTEM_ADMINISTRATOR` rolüne ekler;
- `POST /api/v1/counterparties/{partyId}/company-account` endpoint'ini ekler;
- existing Party → yeni şirket hesabı link command'ini ve gerçek SQL acceptance testlerini taşır.

Bu pending branch davranışı `main`de varmış gibi raporlanamaz. PR #36 yeni `main` ile senkronize edilip yeniden doğrulanmadan eski CI/review kanıtı kullanılamaz.

### 1.3 Mevcut Identity seeder gerçeği

`IdentityDataSeeder`, veritabanında role ID zaten varsa rolü tamamen atlar. Bu nedenle yalnız seed JSON'a yeni permission eklemek:

- fresh database rol oluşturma yolunda etkili olabilir;
- existing installation içindeki mevcut role yeni permission reconcile etmez;
- production upgrade kanıtı değildir;
- hangi iş rollerinin yetki alacağını belirlemez.

## 2. Risk ve amaç

PR #36, grup seviyesinde zaten var olan tek `Party` kaydını yeni Party/CariCode üretmeden aktif başka bir Göymen hukuki şirketinde ayrı `CompanyPartyAccount` ile ilişkilendirir.

Bu işlem:

- şirketler arası görünürlüğü etkiler;
- yeni şirket-yerel cari hesap oluşturur;
- ileride finans, sipariş, stok ve risk süreçlerinin bağlanacağı master-data ilişkisi üretir;
- yanlış yetkilendirilirse başka şirket adına cari hesabı açma riskine yol açar.

Teknik permission ve endpoint pending branch'te bulunabilse de rol/grant/upgrade politikası kullanıcı kararı olmadan genişletilemez.

## 3. Değişmez güvenlik sözleşmesi

Karar hangi seçenek olursa olsun:

1. Varsayılan davranış `default-deny / fail-closed` olur.
2. Hedef şirket yalnız çözülmüş aktif `CompanyContext` olur; body, query veya route ile `CompanyId` override edilemez.
3. Kullanıcı hedef şirkette `counterparty.accounts.create` permission'ına sahip olmalıdır.
4. Kullanıcı Party'yi en az bir mevcut yetkili şirket hesabı üzerinden görebilmeli ve `counterparty.parties.read` denetimini geçmelidir.
5. Tahmin edilen `PartyId` yetkisiz keşif veya existence oracle oluşturmamalıdır.
6. `(CompanyId, PartyId)` benzersizliği SQL katmanında korunur.
7. Permission grant/revoke ile company-account create ayrı audit olaylarıdır.
8. Authorization aşamasında reddedilen istekte business, audit-business ve idempotency etkisi `0` olur.
9. Authorization'ı geçmiş fakat görünmeyen Party için terminal `NotFound` sonucu durable idempotency kaydı olarak tutulabilir; business ve audit-business etkisi yine `0` olur.
10. Existing-install upgrade tekrar çalıştırıldığında duplicate grant üretmez.
11. Kaynaklarda olmayan rol, eşik veya görev ayrılığı politikası tahmin edilmez.
12. Yeni role otomatik kullanıcı ataması yapılmaz; assignment ayrı yetkili işlemdir.

## 4. Doğrulanmış mevcut teknik roller

Repository'nin mevcut canonical role kodları:

- `ORGANIZATION_READER`
- `ORGANIZATION_OPERATOR`
- `SYSTEM_ADMINISTRATOR`
- `SECURITY_ADMINISTRATOR`
- `SECURITY_APPROVER`

Bu listede `COUNTERPARTY_ACCOUNT_OPERATOR` henüz yoktur. Böyle bir rol yalnız aşağıdaki ayrı seçenek açıkça kabul edilirse deterministik olarak oluşturulabilir.

## 5. Karar seçenekleri

### Seçenek A — En dar mevcut rol modeli

- Exact role: `SYSTEM_ADMINISTRATOR`.
- Fresh install ve existing install için aynı role idempotent grant uygulanır.
- Diğer bütün roller default-deny kalır.
- Operasyonel delegasyon daha sonra açık yetki yönetimi kararıyla yapılır.

**Avantaj:** en düşük privilege-escalation riski.  
**Dezavantaj:** operasyonel kullanıcılar yöneticiye bağımlı kalabilir.

### Seçenek B — Kontrollü mevcut roller

- `SYSTEM_ADMINISTRATOR` yanında kullanıcı tarafından tek tek onaylanacak mevcut exact role kodları permission alır.
- `EXACT ROLE CODES` boş bırakılamaz.
- Rol adından veya departman isminden otomatik grant türetilmez.

**Avantaj:** günlük operasyon hızlanabilir.  
**Dezavantaj:** yanlış/geniş rol seçimi cross-company erişim riskini büyütür.

### Seçenek C — Request / approval modeli

- Doğrudan create permission dar yönetici rolünde kalır.
- Operasyonel kullanıcı ayrı request açar; başka yetkili kullanıcı onaylar.
- Maker kendi talebini onaylayamaz.
- Bu seçenek PR #36 kapsamını aşar ve ayrı approval feature'ı gerektirir.

**Avantaj:** görev ayrılığı en güçlü modeldir.  
**Dezavantaj:** ek domain, queue ve lifecycle gerektirir.

### Seçenek D — Ayrı özel Cari hesap operatörü rolü

- Yeni deterministic system role: `COUNTERPARTY_ACCOUNT_OPERATOR`.
- Önerilen görünen ad: `Cari Hesap Yönetimi`.
- Exact permission paketi ayrıca kullanıcı tarafından kabul edilmelidir. Aday minimum paket:
  - `counterparty.parties.read`
  - `counterparty.accounts.create`
  - `counterparty.accounts.status.update`
- `counterparty.parties.create` otomatik olarak bu pakete eklenmez.
- Fresh install'da rol oluşturulur fakat hiçbir kullanıcıya otomatik atanmaz.
- Existing install'da rol yoksa deterministic ID/code ile oluşturulur; varsa ID/code bütünlüğü fail-closed doğrulanır ve yalnız eksik canonical permission'lar idempotent eklenir.
- Custom permission/grant ve mevcut assignment'lar silinmez.

**Avantaj:** sistem yöneticiliği vermeden dar operasyonel delegasyon sağlar.  
**Dezavantaj:** yeni sistem rolünün yaşam döngüsü ve upgrade sözleşmesi gerekir.

## 6. ChatGPT-CARI risk önerisi

En düşük ilk-sürüm riski için **Seçenek A** önerilir. Ayrı delegasyon ihtiyacı başlangıçtan kesin ise **Seçenek D**, geniş mevcut rollere otomatik grant veren Seçenek B'den daha güvenli ayrıştırma sağlar.

Bu öneri kullanıcı kararı değildir. Açık kabul olmadan:

- Issue #43 kapatılmaz;
- hiçbir role yeni grant uygulanmaz;
- yeni rol oluşturulmaz;
- PR #36 Ready/merge yapılmaz.

## 7. Bağlayıcı karar şablonu

```text
DECISION-ACCEPTED: CARI-PERM-001
APPROVED OPTION: A | B | C | D
EXACT ROLE CODES:
NEW ROLE CODE / DETERMINISTIC ID (yalnız D):
CANONICAL PERMISSION BUNDLE (yalnız D):
NEW INSTALL DEFAULT:
EXISTING INSTALL UPGRADE:
AUTOMATIC USER ASSIGNMENT: NONE | <explicit policy>
GRANT SOURCE:
APPROVAL / SoD NOTE:
INACTIVE ROLE POLICY:
REVOKE / FORWARD-FIX NOTE:
EFFECTIVE VERSION / DATE:
APPROVED BY:
```

## 8. Existing-install upgrade sözleşmesi

Kabul edilen karara göre upgrade:

1. Tekrar çalıştırılabilir ve idempotent olmalıdır.
2. Eşzamanlı iki upgrade duplicate `RolePermission` üretememelidir.
3. Mevcut custom permission/grant ve assignment'ları silmemelidir.
4. Seçili olmayan role permission eklememelidir.
5. Mevcut role için expected ID/code uyuşmazlığında sessiz merge yerine fail-closed hata üretmelidir.
6. Seçenek A/B'de beklenen mevcut role yoksa yeni benzer rol uydurmamalıdır.
7. Seçenek D'de yeni rol yalnız kabul edilen deterministic ID/code ile oluşturulmalıdır.
8. Yeni role otomatik kullanıcı assignment'ı yapmamalıdır; kullanıcı kararı aksi yönde açıkça yazılmadıkça default `NONE` olur.
9. Pasif rol davranışı karar şablonunda açıkça belirlenmelidir.
10. Partial failure transaction veya açık forward-fix planıyla yönetilmelidir.
11. Upgrade sürümü ve actor/system identity audit edilebilir olmalıdır.
12. Rollback yerine revoke gerekiyorsa ayrı, gerekçeli ve auditli işlem olmalıdır.

## 9. Teknik kabul matrisi

| Kimlik | Senaryo | Beklenen sonuç |
|---|---|---|
| CARI-PERM-AC-001 | Permission olmayan kullanıcı link çağırır | `403`; business=`0`, audit-business=`0`, idempotency=`0` |
| CARI-PERM-AC-002 | Permission olan kullanıcı görünür Party'yi aktif hedef şirkete bağlar | `201`; tek account + tek audit + tek terminal idempotency sonucu |
| CARI-PERM-AC-003 | Permission var, Party görünürlüğü yok | Non-disclosing `404`; business=`0`, audit-business=`0`, terminal `NotFound` idempotency=`1`; aynı key replay aynı sonucu döndürür |
| CARI-PERM-AC-004 | Body/query/route ile başka CompanyId denemesi | Override kabul edilmez; aktif CompanyContext dışına çıkılmaz |
| CARI-PERM-AC-005 | Aynı upgrade iki kez çalışır | Duplicate role-permission yok |
| CARI-PERM-AC-006 | İki upgrade eşzamanlı çalışır | Tek canonical grant/rol; duplicate yok |
| CARI-PERM-AC-007 | Seçili olmayan rol upgrade öncesi/sonrası | Permission kazanmaz |
| CARI-PERM-AC-008 | Existing custom permission ve assignment | Korunur; sessiz silme/yeniden yazma yok |
| CARI-PERM-AC-009 | Role ID/code uyuşmazlığı | Fail-closed; sessiz merge yok |
| CARI-PERM-AC-010 | Seçenek D fresh install | Deterministic rol oluşur; otomatik user assignment yok |
| CARI-PERM-AC-011 | Same-key link replay | İlk terminal sonuç original correlation kanıtıyla replay edilir |
| CARI-PERM-AC-012 | Same-key farklı payload/actor | `409` mismatch; ek business/audit etkisi yok |
| CARI-PERM-AC-013 | Different-key eşzamanlı duplicate link | Tam bir `201`, tam bir terminal `409 AlreadyLinked`; tek account/audit-business etkisi |
| CARI-PERM-AC-014 | Audit insert reddi | Business + audit + command result atomik rollback |
| CARI-PERM-AC-015 | Idempotency insert reddi | Business + audit + command result atomik rollback |
| CARI-PERM-AC-016 | Yeni permission grant/revoke | Ayrı security/audit izi; link audit'ine karışmaz |
| CARI-PERM-AC-017 | Eksik/çoklu/comma-joined Idempotency-Key | `400`; business/audit/idempotency etkisi `0` |
| CARI-PERM-AC-018 | Yanlış Company scope taşıyan yetkili rol | `403`; business/audit/idempotency etkisi `0` |
| CARI-PERM-AC-019 | Pasif role grant/upgrade | Kullanıcı kararındaki inactive-role politikasına exact uyum |

No-op account active-state concurrency, PR #49 ile ayrı command ailesinde düzeltilmiştir. Bu davranış link permission acceptance'ı değildir; tam regresyon suite içinde korunur fakat link command'e `ExpectedVersion` uydurulmaz.

Gerçek concurrency, unique constraint, upgrade ve rollback senaryoları gerçek SQL Server üzerinde kanıtlanmalıdır. InMemory/mock tek başına yeterli değildir.

## 10. Sahiplik sınırı

### CHATGPT-CARI

- İş kararı seçenekleri ve Cari permission contract'ı
- Link endpoint/application/gerçek SQL acceptance
- PR #36 current-main revalidation
- Cari handoff ve exact-head review kapıları

### CODEX-PLATFORM / Identity owner

Yalnız kabul edilen karar generic Identity role-permission reconciliation gerektirirse:

- Identity idempotent role/permission upgrade altyapısı
- role ID/code fail-closed guard'ları
- generic migration/seed güvenliği ve platform acceptance

### Kullanıcı / insan review

- Seçenek, exact rol kodları ve yeni rol paketinin kabulü
- Kritik permission değişikliğinin merge kararı

## 11. PR #36 merge kapısı

- [x] No-op concurrency hotfix PR #49 ile `main`e alındı
- [ ] PR #36 yeni `main` ile non-destructive sync edildi
- [ ] PR #36 exact yeni head standard CI + UI golden yeşil
- [ ] `CARI-PERM-001` kullanıcı tarafından kabul edildi
- [ ] Exact rol/permission/upgrade implementasyonu ayrı review edildi
- [ ] Fresh ve existing-install gerçek SQL acceptance geçti
- [ ] Yetkisiz/wrong-scope rol fail-closed acceptance geçti
- [ ] Same-key/different-key/replay/rollback/visibility/header testleri geçti
- [ ] CODEX-PLATFORM exact-head bağımsız review bıraktı
- [ ] İnsan Cari/security review tamamlandı

## 12. HANDOFF-V1

```text
OWNER: CHATGPT-CARI
TASK: CARI-PERM-001
CANONICAL DECISION: Issue #43
WRITE SCOPE: docs/features/CARI_ACCOUNT_CREATE_PERMISSION_DECISION_v1.md
READ-ONLY BOUNDARIES: .github/**, Identity production code, Codex branches
COMPLETED: truth-state split, A/B/C/D options, upgrade contract, corrected acceptance matrix
OPEN DECISION: user option + exact roles/new-role package
NEXT SAFE ACTION: CODEX exact-head re-review + user decision
DO NOT: grant permission, create role, mark PR #36 ready, reuse stale CI
```
