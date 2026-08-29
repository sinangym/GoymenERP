# Cari Hesap Oluşturma Yetki Politikası v1

- Karar: `CARI-PERM-001`
- Kanonik ürün sahibi kararı: Issue #43, `AUTHORITATIVE PRODUCT-OWNER CORRECTION`
- Exact karar kaydı: `issuecomment-5461694092`
- Owner: `CHATGPT-CARI`
- Generic Identity uygulama sahibi: `CODEX-PLATFORM / Identity`
- Durum: **A seçeneği kabul edildi / production reconciliation ayrı PR bekliyor**

## 1. Amaç

Mevcut grup-seviyesi `Party` kaydını başka bir Göymen hukuki şirketine yeni
`CompanyPartyAccount` ile bağlayan `counterparty.accounts.create` yetkisinin ilk sürümde
hangi role verileceğini ve existing-install reconciliation sınırını makine-okunur biçimde
kilitlemek.

Bu belge ve yanındaki JSON sözleşmesi kendi başına:

- `ErpPermissions` production registry'sini değiştirmez,
- Identity seed veya veritabanını değiştirmez,
- kullanıcıya rol atamaz,
- PR #36 endpointini `main`e getirmez,
- yeni sistem rolü oluşturmaz,
- geniş satış/finans/muhasebe/sevkiyat rollerine grant vermez.

`counterparty.accounts.create`, ürün sahibi tarafından kabul edilmiş pending permission
kodudur. Production permission sabiti ve endpoint davranışı PR #36/final implementation
hattında; existing-install reconciliation ise Issue #57 kapsamındaki
`CODEX-PLATFORM / Identity` hattında uygulanır.

## 2. Kabul edilmiş A seçeneği

İlk sürümde yalnız:

### `SYSTEM_ADMINISTRATOR`

- Deterministik ID: `30000000-0000-0000-0000-000000000003`
- Mevcut kanonik sistem rolüdür.
- `counterparty.accounts.create` permission'ı eksikse idempotent reconciliation ile eklenir.
- Rol mevcut değilse bu consumer sessizce yeni bir admin rolü üretmez.
- Aynı ID farklı code veya aynı code farklı ID ile bulunursa fail-closed davranılır.

İlk sürümde `COUNTERPARTY_ACCOUNT_OPERATOR` oluşturulmaz, permission veya assignment almaz.
Bu veya başka özel bir operasyon rolü ancak ayrı ve açık ürün sahibi kararıyla sonraki
sürümde tanımlanabilir.

## 3. Default-deny sınırı

Aşağıdaki mevcut roller bu kararla otomatik grant kazanmaz:

- `ORGANIZATION_READER`
- `ORGANIZATION_OPERATOR`
- `SECURITY_ADMINISTRATOR`
- `SECURITY_APPROVER`

Başka custom veya gelecekteki roller de açık admin grant'i ve ayrı kabul edilmiş politika
olmadan permission kazanmaz.

## 4. Existing-install reconciliation değişmezleri

Generic Identity implementation aşağıdaki davranışları sağlamalıdır:

1. Kanonik `SYSTEM_ADMINISTRATOR` rolü ID ve code birlikte kullanılarak bulunur.
2. Rol eksikse deployment fail-closed durur; bu consumer yeni admin rolü yaratmaz.
3. Aynı ID farklı code veya aynı code farklı ID ile bulunursa sessiz merge yapılmaz.
4. Eksik `counterparty.accounts.create` permission'ı eklenir.
5. Tekrar veya eşzamanlı çalıştırma duplicate `RolePermission` üretmez.
6. Mevcut custom/ek permission'lar silinmez.
7. Mevcut role assignment kayıtları korunur.
8. Otomatik user assignment eklenmez.
9. Reconciliation tek transaction içinde tamamlanır.
10. Hata durumunda yarım permission upgrade kalmaz.

## 5. Company scope ve kullanım

Permission rol üzerinde bulunsa bile kullanıcı ilgili `CompanyId` kapsamında geçerli role
assignment taşımak zorundadır.

- Doğru Company scope: endpoint authorization değerlendirilebilir.
- Yanlış veya eksik Company scope: `403`; business/audit/idempotency etkisi `0`.
- Party görünürlüğü ayrıca `counterparty.parties.read` ve mevcut yetkili
  `CompanyPartyAccount` ilişkisi üzerinden doğrulanır.
- Tahmin edilen `PartyId` existence oracle oluşturmaz.

## 6. Şirketler arası Cari bağlantısı gerekçesi

Kanonik ürün sahibi kararına göre kullanıcı tarafından girilen ayrı gerekçe zorunlu değildir.
Missing reason tek başına `400` nedeni olamaz.

Append-only audit olayı yine actor, CompanyContext, kullanılan permission, source channel,
correlation/causation, event type ve before/after bağlamını taşımalıdır. Audit altyapısı teknik
bir açıklama gerektiriyorsa deterministik sistem açıklaması kullanılabilir; bu değer kullanıcı
gerekçesi gibi sunulamaz.

## 7. Acceptance kapısı

### Role policy contract

- Strict JSON; duplicate, unknown, missing veya blank alanlar reddedilir.
- Exact tek authorized role `SYSTEM_ADMINISTRATOR`dır.
- ID/code/name/lifecycle flag ve permission paketi birebir eşleşir.
- `create_if_missing=false` korunur.
- `add_missing_roles=false`, `add_missing_permissions=true` korunur.
- Automatic assignment listesi boştur.
- Default-deny rol listesi exact ve duplicate-free'dir.
- Pending permission exception yalnız exact `counterparty.accounts.create` için geçerlidir;
  başka bilinmeyen permission fail-closed reddedilir.

### Generic Identity reconciliation

- Fresh database kanonik admin rolünde exact permission.
- Eski seed snapshot'lı database üzerinde idempotent permission upgrade.
- İkinci ve concurrent çalıştırmada duplicate yok.
- Custom permission ve mevcut assignment korunur.
- Missing admin role ile ID/code collision fail-closed.
- Transaction rollback kanıtı.

### Counterparty endpoint

- Yetkili sistem yöneticisi + doğru Company scope: success path.
- Permission almayan roller ve yanlış Company scope: `403`, tüm etkiler `0`.
- Görünmeyen Party: non-disclosing terminal `404` ve güvenli same-key replay.
- Same-key replay/mismatch, different-key duplicate race ve atomik rollback gerçek SQL ile
  doğrulanır.
- Kullanıcı gerekçesi missing olduğu için request reddedilmez.

## 8. Sahiplik ve sıra

1. `CHATGPT-CARI` bu module-owned contract'ı yayımlar ve test eder.
2. `CODEX-PLATFORM / Identity` generic permission reconciliation'ı ayrı branch/PR'da uygular.
3. ChatGPT exact-head Identity/security review yapar.
4. Reconciliation `main`e girdikten sonra PR #36 current main ile senkronize edilir.
5. PR #36 fresh SQL/HTTP acceptance ve Codex exact-head review sonrasında merge kapısına gelir.

Aynı Identity seeder/domain/persistence dosyasında eşzamanlı iki writer çalışmaz.
