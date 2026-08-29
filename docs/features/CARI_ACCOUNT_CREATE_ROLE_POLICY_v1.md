# Cari Hesap Oluşturma Yetki Politikası v1

- Karar: `CARI-PERM-001`
- Kanonik ürün sahibi kararı: Issue #43, `PRODUCT-OWNER CONFIRMATION — 2026-08-29`
- Exact karar kaydı: `issuecomment-5461669614`
- Owner: `CHATGPT-CARI`
- Generic Identity uygulama sahibi: `CODEX-PLATFORM / Identity`
- Durum: **Kabul edilmiş iş politikası / production reconciliation henüz ayrı PR bekliyor**

## 1. Amaç

Mevcut grup-seviyesi `Party` kaydını başka bir Göymen hukuki şirketine yeni
`CompanyPartyAccount` ile bağlayan `counterparty.accounts.create` yetkisinin hangi sistem
rollerinde bulunacağını ve existing-install reconciliation sınırını makine-okunur biçimde
kilitlemek.

Bu belge ve yanındaki JSON sözleşmesi kendi başına:

- `ErpPermissions` production registry'sini değiştirmez,
- Identity seed veya veritabanını değiştirmez,
- kullanıcıya rol atamaz,
- PR #36 endpointini `main`e getirmez,
- maker-checker akışı oluşturmaz,
- geniş satış/finans/muhasebe/sevkiyat rollerine grant vermez.

`counterparty.accounts.create` ürün sahibi tarafından kabul edilmiş pending permission
kodudur. Production permission sabiti ve endpoint davranışı PR #36/final implementation
hattında; existing-install reconciliation ise Issue #57 kapsamındaki CODEX-PLATFORM/Identity
hattında uygulanır. Bu contract PR'ı henüz `main`de bulunmayan production sabitine derleme
zamanında bağlanmaz.

## 2. Kabul edilmiş rol modeli

### `SYSTEM_ADMINISTRATOR`

- Deterministik ID: `30000000-0000-0000-0000-000000000003`
- Mevcut sistem rolüdür.
- `counterparty.accounts.create` permission'ı eksikse idempotent reconciliation ile eklenir.
- ID/code uyuşmazlığı sessizce birleştirilmez.

### `COUNTERPARTY_ACCOUNT_OPERATOR`

- Deterministik ID: `30000000-0000-0000-0000-000000000006`
- Görünen ad: `Cari Hesap Yönetimi`
- System role: `true`
- Canonical permission paketi:
  - `counterparty.parties.read`
  - `counterparty.accounts.create`
  - `counterparty.accounts.status.update`
- `counterparty.parties.create` pakete dahil değildir.
- Fresh install'da rol tanımı oluşturulur; otomatik kullanıcı ataması yapılmaz.
- Existing install'da rol yoksa exact ID/code/name/system flag ile oluşturulur; varsa yalnız
  eksik canonical permission'lar eklenir.

## 3. Default-deny roller

Aşağıdaki mevcut roller bu kararla otomatik grant kazanmaz:

- `ORGANIZATION_READER`
- `ORGANIZATION_OPERATOR`
- `SECURITY_ADMINISTRATOR`
- `SECURITY_APPROVER`

Başka custom roller de açık bir admin grant'i olmadan permission kazanmaz.

## 4. Existing-install reconciliation değişmezleri

Generic Identity implementation aşağıdaki davranışları sağlamalıdır:

1. Sistem rolleri ID ve code birlikte kullanılarak bulunur.
2. Aynı ID farklı code veya aynı code farklı ID ile bulunursa deployment fail-closed durur.
3. Eksik canonical permission eklenir; tekrar çalıştırma duplicate üretmez.
4. Mevcut custom/ek permission'lar silinmez.
5. Mevcut role assignment kayıtları korunur.
6. Yeni operator rolüne otomatik user assignment eklenmez.
7. Bütün reconciliation tek transaction içinde tamamlanır.
8. Concurrent veya tekrar çalıştırma duplicate role/permission üretmez.
9. Contract'taki exact pending `counterparty.accounts.create` dışında kalan her permission,
   mevcut `ErpPermissions.IsKnown` registry sınırını geçmelidir. Production uygulama
   tamamlandığında pending kod da registry'de exact olarak bulunmalıdır.
10. Hata durumunda yarım role/permission upgrade kalmaz.

## 5. Company scope ve kullanım

Rol tanımının bulunması tek başına kullanıcıya erişim vermez. Kullanıcıya ilgili
`CompanyId` kapsamında açık `RoleAssignment` verilmelidir.

- Doğru company scope + iki gerekli endpoint policy: işlem değerlendirilebilir.
- Yanlış veya eksik company scope: `403` ve business/audit/idempotency etkisi `0`.
- Party görünürlüğü ayrıca mevcut yetkili `CompanyPartyAccount` ilişkisi üzerinden
  doğrulanır; tahmin edilen `PartyId` existence oracle oluşturmaz.

## 6. Acceptance kapısı

### Role policy contract

- Strict JSON; duplicate, unknown, missing veya blank alanlar reddedilir.
- Exact iki authorized system role bulunur.
- ID/code/name/permission paketleri birebir eşleşir.
- Operator paketi `counterparty.parties.create` içermez.
- Automatic assignment listesi boştur.
- Default-deny role listesi exact ve duplicate-free'dir.
- Pending permission exception yalnız exact `counterparty.accounts.create` için geçerlidir;
  başka bilinmeyen permission fail-closed reddedilir.

### Generic Identity reconciliation

- Fresh database exact role/permission seti.
- Eski seed snapshot'lı database üzerinde idempotent upgrade.
- İkinci ve concurrent çalıştırmada duplicate yok.
- Custom permission ve mevcut assignment korunur.
- ID/code collision fail-closed.
- Transaction rollback kanıtı.

### Counterparty endpoint

- Authorized operator + doğru Company scope: success path.
- Permission almayan roller ve yanlış company scope: `403`, tüm etkiler `0`.
- Görünmeyen Party: non-disclosing terminal `404` ve güvenli same-key replay.
- Same-key replay/mismatch, different-key duplicate race ve atomik rollback gerçek SQL ile
  doğrulanır.
- Link request kullanıcı gerekçesi taşır; normalize edilmiş `1–1000` karakter gerekçe audit
  olayına aynen yazılır, invalid gerekçe bütün etkiler `0` olacak şekilde reddedilir.

## 7. Sahiplik ve sıra

1. `CHATGPT-CARI` bu module-owned contract'ı yayımlar ve test eder.
2. `CODEX-PLATFORM / Identity` generic reconciliation'ı ayrı branch/PR'da uygular.
3. ChatGPT exact-head Identity/security review yapar.
4. Reconciliation `main`e girdikten sonra PR #36 current main ile senkronize edilir.
5. PR #36 fresh SQL/HTTP acceptance ve Codex exact-head review sonrasında merge kapısına gelir.

Aynı Identity seeder/domain/persistence dosyasında eşzamanlı iki writer çalışmaz.
