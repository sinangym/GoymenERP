# Cari / Counterparty API Güvenlik Metadata Sözleşmesi v1.1

- Tarih: 2026-08-29
- Görev: `CARI-API-ROUTE-CONTRACT`
- Sahip: `CHATGPT-CARI`
- Son doğrulanan ana dal: `main@fc4c806ec61cf97e7177136f78f3f9d64cfb4d13`
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

Tümü exact `Counterparty` tag'i ve authentication metadata'sı taşır. `AllowAnonymous`, role string, endpoint-specific authentication scheme veya null olmayan boş/whitespace authorization policy kabul edilmez.

Method değerleri exact uppercase, route değerleri exact leading-slash ve trailing-slash içermeyen kanonik biçimde olmalıdır. Normalizasyon yalnız runtime karşılaştırma yardımcısıdır; manifestteki non-canonical yazımı sessizce kabul etmek için kullanılamaz.

## 3. İki farklı authorization modeli

### 3.1 `endpoint_permission`

Write endpointlerinde permission metadata endpoint üzerinde exact bulunur:

- create → `counterparty.parties.create`
- account active-state → `counterparty.accounts.status.update`

Bu endpointler `RequiresCompanyContextAttribute` taşır. CompanyId body veya route ile seçilmez; aktif request-scoped CompanyContext kullanılır.

### 3.2 `dynamic_authorized_company_set`

Cari liste ve detay okumaları, kullanıcının bir veya birden fazla yetkili şirketindeki Party ilişkilerini compose edebildiği için zorunlu tek CompanyContext metadata'sı taşımaz. Endpoint authentication ister; gerçek permission ve görünür CompanyId kümesi `ICounterpartyReadService` içinde server-side uygulanır:

`AllowedCompanyIds + counterparty.parties.read → CompanyPartyAccounts → Party`

Bu model genel bir `authenticated-only` istisnası değildir. Yalnız bu iki exact route ve exact `counterparty.parties.read` contract'ı için kabul edilmiştir. Generic guard:

- bu iki route'u wildcard/global exemption yapamaz,
- permission kontrolünün UI veya client'a bırakıldığını varsayamaz,
- yeni bir dinamik route'u sözleşme güncellenmeden kabul edemez,
- blank policy metadata'yı authentication metadata'sı yokmuş gibi filtreleyemez.

## 4. CompanyContext semantiği

- GET route'larında CompanyContext opsiyoneldir.
- Header yoksa yalnız kullanıcının permission taşıdığı yetkili şirketlerin read-only grup görünümü oluşturulabilir.
- Geçerli ve yetkili `X-Company-Id` varsa görünüm o aktif şirketle sınırlandırılır.
- Geçersiz header `400`, kullanıcının erişemediği şirket header'ı `403` üretir.
- Bu davranış yalnız manifest boolean'larıyla varsayılmaz; gerçek HTTP acceptance ile ölçülür.
- POST ve PUT route'larında CompanyContext zorunludur.
- `requires_company_context=true` ve `optional_company_context=true` aynı endpointte birlikte olamaz.
- Route sözleşmesi veri görünürlüğü ve service-level permission acceptance testlerinin yerine geçmez.

## 5. Strict JSON ve fail-closed parser

Makine-okunur contract:

- trailing comma ve comment kabul etmez,
- duplicate property kabul etmez,
- unknown property kabul etmez,
- bütün root ve endpoint constructor alanlarını zorunlu sayar,
- eksik bool alanını CLR varsayılanıyla sessizce tamamlamaz,
- null endpoint veya null endpoint dizisini kabul etmez,
- exact kanonik `contract_source` yolunu doğrular.

Zorunlu endpoint alanları:

- `method`
- `route`
- `authorization_mode`
- `permission`
- `requires_company_context`
- `optional_company_context`

Her birinin silinmesi adversarial mutation testiyle fail-closed doğrulanır.

## 6. Sahiplik ve generic guard tüketimi

- `CHATGPT-CARI`: route listesi, permission anlamı, dinamik read modeli ve Counterparty acceptance sahibidir.
- `CODEX-PLATFORM`: generic endpoint metadata guard sahibidir.
- Platform guard bu JSON'u exact input olarak okuyabilir; Cari route/permission üretmez veya değiştirmez.
- Manifestte olmayan runtime Counterparty endpoint fail-closed reddedilir.
- Manifestte kalıp runtime'da bulunmayan stale endpoint fail-closed reddedilir.
- Method, route casing/constraint, tag, CompanyContext, permission veya authorization metadata drift fail-closed reddedilir.

## 7. PR #29 entegrasyon sırası

1. Bu Cari-owned contract bağımsız PR ile exact CI ve Codex review'dan geçer.
2. Merge edildiğinde PR #29 current main ile senkronize edilir.
3. PR #29 generic guard, platform-owned sabit envanter ile Cari-owned manifesti ayrı kaynaklar olarak birleştirir.
4. `dynamic_authorized_company_set` yalnız manifestteki exact iki GET route için özel, fail-closed doğrulanır.
5. Yeni main runtime endpoint inventory üzerinde full integration test ve CI yeniden çalışır.

## 8. Gelecek route değişikliği

PR #36 merge edilirse aşağıdaki route ancak o yeni main üzerinde ayrı contract revision ile eklenebilir:

`POST /api/v1/counterparties/{partyId:guid}/company-account`

Eski contract/CI/review bu yeni route için kullanılamaz. Permission kararı Issue #43 kapanmadan route sözleşmesi production kabulü sayılmaz.

## 9. Kabul kriterleri

- JSON `schema_version=1`, owner/module/tag/contract_source exact.
- Bütün zorunlu root ve endpoint alanları missing-field mutation testleriyle korunur.
- Exact dört endpoint ve benzersiz `(canonical method, canonical route)` anahtarı.
- Wildcard, duplicate property, unknown property, null/blank alan, boş permission ve bilinmeyen permission reddi.
- Runtime Counterparty endpoint seti manifestle birebir.
- Endpoint permission modelinde exact tek non-empty permission policy.
- Dynamic read modelinde non-empty endpoint permission policy yok; authentication zorunlu, declared read permission known, CompanyContext opsiyonel.
- Null olmayan blank/whitespace policy metadata iki modelde de reddedilir.
- Optional CompanyContext filter gerçek HTTP ile absent/valid/invalid/unauthorized senaryolarında kanıtlanır.
- `AllowAnonymous`, role string ve authentication scheme yok.
- Tag exact `Counterparty`.
- Production endpoint, permission, schema ve migration değişmez.

## 10. Kapsam dışı

- Read-service iç güvenlik testlerinin yerine geçmek
- PR #36 permission/upgrade kararı
- PR #41 project-reference generic guard implementasyonu
- Counterparty migration SQL artifact CI
- Başka modüllerin route sözleşmesini Cari tarafından belirlemek
