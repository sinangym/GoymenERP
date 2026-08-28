# Göymen ERP — Cari Hesabı Oluşturma Yetkisi Karar Paketi v1

- **Durum:** TASLAK / KULLANICI KARARI GEREKLİ
- **Teknik alan:** `Counterparty` (kullanıcı alanı: Cari)
- **Karar kimliği:** `CARI-PERM-001`
- **İlgili permission:** `counterparty.accounts.create`
- **İlgili geliştirme:** PR #36 — mevcut grup Party kaydını başka Göymen şirketine bağlama
- **Bağımlı güvenlik düzeltmesi:** PR #38 — no-op optimistic concurrency hotfix
- **Sahip:** `ChatGPT-CARI`
- **Bağımsız inceleme:** `CODEX-PLATFORM`
- **Merge kapısı:** İnsan review + açık kullanıcı kararı

> Bu belge hiçbir role yetki vermez, seed veya migration değiştirmez ve PR #36'yı tamamlanmış saymaz. Amaç, açık işletme politikasını tahmin etmeden bağlayıcı karar ve teknik kabul kapısını tanımlamaktır.

## 1. Sorun

PR #36, grup seviyesinde zaten var olan tek `Party` kaydını yeni Party veya yeni CariCode üretmeden, aktif başka bir Göymen hukuki şirketinde ayrı `CompanyPartyAccount` ile ilişkilendirir.

Bu işlem:

- şirketler arası görünürlüğü etkiler,
- yeni bir şirket-yerel cari hesap oluşturur,
- ileride finans, sipariş, stok ve risk süreçlerinin bağlanacağı bir master-data ilişkisi üretir,
- yanlış yetkilendirilirse kullanıcının başka bir şirket adına cari hesap açmasına yol açabilir.

Teknik permission tanımlı olsa da hangi gerçek Göymen rollerinin bu permission'ı alacağı ve mevcut kurulumların permission'ı nasıl kazanacağı bağlayıcı kullanıcı kararı olmadan genişletilemez.

## 2. Değişmez güvenlik sözleşmesi

Karar hangi seçenek olursa olsun aşağıdakiler değişmez:

1. Varsayılan davranış **default-deny / fail-closed** olur.
2. Hedef şirket yalnız çözülmüş aktif `CompanyContext` olur; body, query veya route ile `CompanyId` override edilemez.
3. Kullanıcı hedef şirkette `counterparty.accounts.create` permission'ına sahip olmalıdır.
4. Kullanıcı Party'yi en az bir mevcut yetkili şirket hesabı üzerinden görebilmeli ve ayrıca `counterparty.parties.read` denetimini geçmelidir.
5. Tahmin edilen `PartyId` ile yetkisiz keşif, existence oracle veya link oluşturma mümkün olmamalıdır.
6. `(CompanyId, PartyId)` benzersizliği SQL katmanında korunur.
7. Permission grant'i ile cari hesabı oluşturma komutu ayrı audit olaylarıdır; biri diğerinin yerine geçmez.
8. Permission verilmemiş kullanıcıda business kaydı, audit business olayı veya idempotency execution kaydı oluşmamalıdır.
9. Existing-installation upgrade tekrar çalıştırıldığında duplicate grant üretmemelidir.
10. Kaynaklarda olmayan rol adı, eşik veya görev ayrılığı politikası tahmin edilmez.

## 3. Karar verilmesi gereken alanlar

Kullanıcı kararı aşağıdaki alanların tamamını açıkça belirlemelidir:

| Alan | Gerekli karar |
|---|---|
| İzin alacak roller | Repository'deki exact rol kodları |
| Yeni kurulum | Hangi roller permission ile seed edilir |
| Mevcut kurulum | İdempotent upgrade/grant davranışı |
| Grant kaynağı | Seed/migration, admin yetki yönetimi veya kontrollü birleşim |
| Görev ayrılığı | Doğrudan create, gelecekte approval veya request modeli |
| Geri alma | Yanlış grant için güvenli revoke/forward-fix yaklaşımı |
| Audit | Grant/revoke reason ve actor kanıtı |
| Etkin tarih | Kararın hangi sürümden itibaren geçerli olduğu |

## 4. Güvenli seçenekler

### Seçenek A — En dar ilk sürüm

- Yalnız repository'deki sistem yöneticisi rolü permission alır.
- Yeni ve mevcut kurulumlarda aynı exact role idempotent grant uygulanır.
- Diğer bütün roller default-deny kalır.
- Operasyonel roller daha sonra açık kullanıcı kararı veya yetki yönetimi üzerinden eklenir.

**Avantajlar**

- En düşük privilege-escalation riski.
- Maker-checker henüz yokken cross-company master-data oluşturma dar tutulur.
- PR #36'nın teknik kabulü iş rolü tahmini yapılmadan tamamlanabilir.

**Dezavantajlar**

- Günlük işlem için sistem yöneticisine operasyonel bağımlılık doğurabilir.
- Yetki yönetimi ekranı gelene kadar delegasyon sınırlı olur.

### Seçenek B — Kontrollü operasyon rolleri

- Sistem yöneticisine ek olarak kullanıcı tarafından açıkça seçilecek mevcut rol kodları permission alır.
- Exact rol listesi repository'den çıkarılır ve karar kaydında tek tek onaylanır.
- Yeni ve mevcut kurulum upgrade'i yalnız bu listedeki rollere uygulanır.

**Avantajlar**

- Günlük operasyon daha hızlı yürür.
- Mevcut görev dağılımına göre yetki devri yapılabilir.

**Dezavantajlar**

- Hatalı veya geniş rol seçimi kalıcı cross-company erişim riski yaratır.
- Rol kapsamları değişirse permission drift'i ayrıca yönetilmelidir.

### Seçenek C — Request / approval temelli model

- Doğrudan permission ilk aşamada yalnız sistem yöneticisinde kalır.
- Operasyonel kullanıcılar ayrı bir request oluşturur; yetkili başka kullanıcı onaylar.
- Değişikliği yapan kendi talebini onaylayamaz.

**Avantajlar**

- Hedef maker-checker ve görev ayrılığı modeline en yakın yaklaşımdır.
- Kritik cross-company ilişki için açık onay izi oluşur.

**Dezavantajlar**

- PR #36 kapsamını aşar.
- Approval domain'i, queue, timeout/reject/cancel ve audit acceptance'ı gerektirir.
- Ayrı feature/PR olarak geliştirilmelidir.

## 5. ChatGPT-CARI önerisi

İlk sürümde maker-checker uygulanmadığı ve işlem cross-company master-data ilişkisi oluşturduğu için **Seçenek A** en güvenli başlangıç önerisidir.

Bu yalnız teknik risk önerisidir. Kullanıcı açıkça onaylamadan:

- karar `CLOSED` sayılmaz,
- başka role permission verilmez,
- PR #36 Draft durumundan çıkarılmaz,
- mevcut kurulumlara upgrade uygulanmaz.

## 6. Bağlayıcı karar şablonu

```text
DECISION: CARI-PERM-001
STATUS: APPROVED
APPROVED OPTION: A | B | C
EXACT ROLE CODES:
NEW INSTALL DEFAULT:
EXISTING INSTALL UPGRADE:
GRANT SOURCE:
APPROVAL / SoD NOTE:
REVOKE / FORWARD-FIX NOTE:
EFFECTIVE VERSION / DATE:
APPROVED BY:
```

Karar seçeneği B ise `EXACT ROLE CODES` alanı boş bırakılamaz. Görünen Türkçe rol adı değil repository'deki teknik rol kodu yazılmalıdır.

## 7. Existing-installation upgrade sözleşmesi

Karar sonrası uygulanacak upgrade aşağıdaki özelliklere sahip olmalıdır:

1. Tekrar çalıştırılabilir ve idempotent olmalı.
2. Mevcut grant'i silip yeniden üretmemeli.
3. Seçili olmayan role permission eklememeli.
4. Pasif/archived role davranışı açıkça test edilmeli.
5. Rol yoksa sessizce yeni rol üretmemeli; fail-closed veya açık migration error üretmeli.
6. Partial failure durumunda transaction/forward-fix davranışı belgelenmeli.
7. Upgrade sürümü ve uygulayan actor/system identity audit edilebilmeli.
8. Rollback yerine revoke gerekiyorsa ayrı ve izlenebilir değişiklik olmalı.

## 8. Teknik kabul matrisi

| Kimlik | Senaryo | Beklenen sonuç |
|---|---|---|
| CARI-PERM-AC-001 | Permission olmayan kullanıcı company-account link çağırır | `403`; business/audit/idempotency etkisi `0` |
| CARI-PERM-AC-002 | Permission olan kullanıcı görünür Party'yi aktif hedef şirkete bağlar | `201`; tek account + tek audit + terminal idempotency sonucu |
| CARI-PERM-AC-003 | Permission var, Party görünürlüğü yok | Non-disclosing `404`; etki `0` |
| CARI-PERM-AC-004 | Body/query/route ile başka CompanyId gönderme denemesi | Override kabul edilmez; aktif CompanyContext dışına çıkılmaz |
| CARI-PERM-AC-005 | Aynı upgrade iki kez çalışır | Duplicate role-permission kaydı yok |
| CARI-PERM-AC-006 | Seçili olmayan rol upgrade öncesi/sonrası | Permission kazanmaz |
| CARI-PERM-AC-007 | Same-key replay | İlk terminal sonuç original correlation kanıtıyla replay edilir |
| CARI-PERM-AC-008 | Same-key farklı payload/actor | `409` mismatch; ek etki yok |
| CARI-PERM-AC-009 | Different-key eşzamanlı duplicate link | Tam bir `201`, tam bir terminal `409 AlreadyLinked` |
| CARI-PERM-AC-010 | Audit insert reddi | Business + audit + command result atomik rollback |
| CARI-PERM-AC-011 | Idempotency insert reddi | Business + audit + command result atomik rollback |
| CARI-PERM-AC-012 | Yeni permission grant/revoke | Ayrı güvenlik/audit izi; cari create audit'ine karışmaz |
| CARI-PERM-AC-013 | Eksik/çoklu/comma-joined Idempotency-Key | `400`; etki `0` |
| CARI-PERM-AC-014 | PR #38 sonrası stale expected version | Terminal `409 VersionConflict` |

Gerçek concurrency, unique constraint ve rollback senaryoları gerçek SQL Server üzerinde kanıtlanmalıdır. InMemory/mock kanıtı yeterli değildir.

## 9. Kod ve sahiplik sınırı

### ChatGPT-CARI

- Permission'ın Cari command üzerinde zorunlu tutulması
- Cari endpoint/application acceptance testleri
- Cari karar ve handoff belgeleri
- PR #36'nın current-main revalidation'ı

### CODEX-PLATFORM

Yalnız kabul edilen karar generic Identity seed/upgrade mekanizması gerektirirse:

- Identity tarafındaki idempotent role-permission upgrade altyapısı
- Generic migration/seed guard'ları
- Platform security acceptance ve CI entegrasyonu

### İnsan / kullanıcı

- Seçenek ve exact rol kodlarının onayı
- Kritik permission değişikliğinin merge kararı

## 10. Merge kapısı

PR #36 aşağıdakilerin tamamı olmadan merge edilmez:

- [ ] PR #38 `main`e alınmış ve PR #36 yeni main ile senkronize edilmiş
- [ ] `CARI-PERM-001` kullanıcı tarafından onaylanmış
- [ ] Exact rol kodları doğrulanmış
- [ ] Yeni kurulum ve existing-install upgrade testleri geçmiş
- [ ] Yetkisiz rol fail-closed acceptance geçmiş
- [ ] Same-key/different-key/replay/rollback/visibility testleri gerçek SQL'de geçmiş
- [ ] Standard CI ve UI golden exact head üzerinde yeşil
- [ ] Codex exact-head bağımsız review bırakmış
- [ ] İnsan Cari/security review tamamlanmış

## 11. HANDOFF-V1

```text
OWNER: ChatGPT-CARI
TASK: CARI-PERM-001
WRITE SCOPE: docs/features/CARI_ACCOUNT_CREATE_PERMISSION_DECISION_v1.md
READ-ONLY BOUNDARIES: .github/**, Identity production code, Codex branches
COMPLETED: decision options, recommendation, upgrade contract, acceptance matrix
OPEN DECISION: user option + exact role codes
NEXT SAFE ACTION: repository role inventory and user decision
DO NOT: grant permission, mark PR #36 ready, reuse stale CI
```
