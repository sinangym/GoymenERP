# Cari / Counterparty API Güvenlik Metadata Sözleşmesi v1.0

- Tarih: 2026-08-29
- Görev: `CARI-API-ROUTE-CONTRACT`
- Sahip: `CHATGPT-CARI`
- Taban: `main@a3b58b9266ea192dd029889ac60abfe8b2ec194e`
- Durum: **Mevcut main route sözleşmesi GO / generic platform guard entegrasyonu CODEX-PLATFORM işi**
- Makine-okunur kaynak: `src/backend/Modules/Counterparty/COUNTERPARTY_API_ENDPOINTS.json`

## 1. Amaç

`main` içinde gerçekten bulunan Counterparty HTTP yüzeyini method, route, authorization modeli, permission, CompanyContext ve endpoint tag bilgileriyle exact olarak tanımlamak. Bu sözleşme PR #29 gibi generic platform metadata guard'larının Cari route'larını tahmin etmeden ve Cari iş güvenliğini kendi sahiplik alanına almadan doğrulamasını sağlar.

Bu belge yeni endpoint açmaz, route davranışı değiştirmez ve PR #36'daki henüz merge edilmemiş company-account link endpoint'ini şimdiden kabul edilmiş saymaz.

## 2. Mevcut main route envanteri

Exact dört endpoint vardır:

1. `GET /api/v1/counterparties`
2. `GET /api/v1/counterparties/{partyId:guid}`
3. `POST /api/v1/counterparties`
4. `PUT /api/v1/counterparties/{partyId:guid}/company-account/active-state`

Tümü exact `Counterparty` tag'i ve authentication metadata'sı taşır. `AllowAnonymous`, role string veya endpoint-specific authentication scheme kabul edilmez.

## 3. İki farklı authorization modeli

### 3.1 `endpoint_permission`

Write endpointlerinde permission metadata endpoint üzerinde exact bulunur:

- create → `counterparty.parties.create`
- account active-state → `counterparty.accounts.status.update`

Bu endpointler `RequiresCompanyContextAttribute` taşır. CompanyId body veya route ile seçilmez; aktif request-scoped CompanyContext kullanılır.

### 3.2 `dynamic_authorized_company_set`

Cari liste ve detay okumaları, kullanıcının bir veya birden fazla yetkili şirketindeki Party ilişkilerini compose edebildiği için zorunlu tek CompanyContext metadata'sı taşımaz. Endpoint authentication ister; gerçek permission ve görünür CompanyId kümesi `ICounterpartyReadService` içinde server-side uygulanır:

`AllowedCompanyIds + counterparty.parties.read → CompanyPartyAccounts → Party`

Bu model genel bir "authenticated-only" istisnası değildir. Yalnız bu iki exact route ve exact `counterparty.parties.read` contract'ı için kabul edilmiştir. Generic guard:

- bu iki route'u wildcard/global exemption yapamaz,
- permission kontrolünün UI veya client'a bırakıldığını varsayamaz,
- yeni bir dinamik route'u sözleşme güncellenmeden kabul edemez.

## 4. CompanyContext semantiği

- GET route'larında CompanyContext opsiyoneldir. Header varsa backend yetkili şirket kümesine karşı doğrular; yoksa yalnız yetkili şirketlerin read-only grup görünümü oluşturulabilir.
- POST ve PUT route'larında CompanyContext zorunludur.
- `requires_company_context=true` ve `optional_company_context=true` aynı endpointte birlikte olamaz.
- Yetkisiz veya geçersiz CompanyId fail-closed davranır; route sözleşmesi veri görünürlüğü acceptance testlerinin yerine geçmez.

## 5. Sahiplik ve generic guard tüketimi

- `CHATGPT-CARI`: route listesi, permission anlamı, dinamik read modeli ve Counterparty acceptance sahibidir.
- `CODEX-PLATFORM`: generic endpoint metadata guard sahibidir.
- Platform guard bu JSON'u exact input olarak okuyabilir; Cari route/permission üretmez veya değiştirmez.
- Manifestte olmayan runtime Counterparty endpoint fail-closed reddedilir.
- Manifestte kalıp runtime'da bulunmayan stale endpoint fail-closed reddedilir.
- Method, route casing, route constraint, tag, CompanyContext veya permission drift fail-closed reddedilir.

## 6. PR #29 entegrasyon sırası

1. Bu Cari-owned contract bağımsız PR ile exact CI ve Codex review'dan geçer.
2. Merge edildiğinde PR #29 current main ile senkronize edilir.
3. PR #29 generic guard, platform-owned sabit envanter ile Cari-owned manifesti ayrı kaynaklar olarak birleştirir.
4. `dynamic_authorized_company_set` yalnız manifestteki exact iki GET route için özel, fail-closed doğrulanır.
5. Yeni main runtime endpoint inventory üzerinde full integration test ve CI yeniden çalışır.

## 7. Gelecek route değişikliği

PR #36 merge edilirse aşağıdaki route ancak o yeni main üzerinde ayrı contract revision ile eklenebilir:

`POST /api/v1/counterparties/{partyId:guid}/company-account`

Eski contract/CI/review bu yeni route için kullanılamaz. Permission kararı Issue #43 kapanmadan route sözleşmesi production kabulü sayılmaz.

## 8. Kabul kriterleri

- JSON `schema_version=1`, owner/module/tag exact.
- Exact dört endpoint ve benzersiz `(method, normalized route)` anahtarı.
- Wildcard, duplicate, unknown property, boş permission ve bilinmeyen permission reddi.
- Runtime Counterparty endpoint seti manifestle birebir.
- Endpoint permission modelinde exact tek non-empty permission policy.
- Dynamic read modelinde non-empty endpoint permission policy yok; authentication zorunlu, declared read permission known, CompanyContext opsiyonel.
- `AllowAnonymous`, role string ve authentication scheme yok.
- Tag exact `Counterparty`.
- Production endpoint, permission, schema ve migration değişmez.

## 9. Kapsam dışı

- Read-service iç güvenlik testlerinin yerine geçmek
- PR #36 permission/upgrade kararı
- PR #41 project-reference contract'ı
- Counterparty migration SQL artifact CI
- Başka modüllerin route sözleşmesini Cari tarafından belirlemek
