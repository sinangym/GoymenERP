---
contract_id: ORGANIZATION-COMPANY-DIRECTORY-V1
version: 1.0
status: REVIEW_PENDING
risk_class: K3
semantic_owner: CODEX-PLATFORM/ORGANIZATION
business_acceptance_owner: CHATGPT-BUSINESS
frontend_consumer: CLAUDE-FRONTEND
reviewed_base_sha: e9c48605260e52a2905609dc377e14e9dbb68582
implementation_status: PARTIAL_EXISTING_RUNTIME
production_activation: NO-GO
tracking_issue: 120
---

# Organization Company Directory Acceptance v1

## 1. Amaç

Bu sözleşme `SHELL-PILOT-01` ve ilk Cari dikey diliminin ortak şirket seçimi ile şirket adı çözümleme ihtiyacını, mevcut Organization runtime yüzeyini ikinci bir gerçek kaynağa dönüştürmeden kabul edilebilir hale getirir.

Mevcut backend ve bootstrap frontend davranışını görünür bir contract altında toplar; yeni şirket, VKN, CompanyCode, permission, schema, migration veya production activation üretmez.

`OPEN-001` nedeniyle placeholder şirket değerleri bulunabilir. `isPlaceholder=true` olan kayıtlar resmî hukuki unvan/VKN doğruluğu olarak yorumlanamaz.

## 2. Bağlayıcı kaynaklar

Kaynak önceliği kök `AGENTS.md` sırasıdır.

1. `docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md`
2. `docs/features/CARI_UI_VERTICAL_SLICE_v1.md`
3. `docs/features/ERP-000K_SHELL_CONTRACT_v1.md`
4. mevcut Organization endpoint/application/infrastructure sözleşmeleri
5. `docs/decisions/ACIK_KARARLAR.md` içindeki `OPEN-001`

Bu belge kaynaklarda bulunmayan hukuki şirket bilgisi, rol, permission veya cache provider seçmez.

## 3. Mevcut runtime doğrusu

Bugünkü uygulama aşağıdaki fiziksel yüzeyi taşır:

```http
GET /api/v1/organization/companies
Authorization: required
X-Company-Id: absent
```

Endpoint:

- mevcut `ErpPermissions.OrganizationRead` sınırını kullanır;
- authenticated local user kimliğini server tarafında çözer;
- `IOrganizationService.GetAuthorizedCompaniesAsync` çağrısını kullanır;
- actor'ın yetki kapsamındaki aktif şirketleri döndürür;
- `CompanySummary(Guid Id, string Code, string LegalName, bool IsPlaceholder)` üretir;
- mevcut uygulamada sonucu `Code` alanına göre deterministik sıralar.

Bugünkü React bootstrap, bu endpoint için TypeScript tiplerini elle tanımlar. Otonom teslim planı uyarınca nihai tüketim OpenAPI üzerinden generated client'a taşınır.

## 4. Observable HTTP contract

Contract ID:

```text
ORGANIZATION-COMPANY-DIRECTORY-V1
```

Başarılı satır şekli:

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "code": "string",
  "legalName": "string",
  "isPlaceholder": true
}
```

Alan anlamları:

| Alan | Anlam |
|---|---|
| `id` | Organization tarafından üretilen immutable Company UUID referansı |
| `code` | Organization projection'ındaki görünür şirket kodu |
| `legalName` | Organization projection'ındaki hukuki ad değeri |
| `isPlaceholder` | `OPEN-001` doğrulama bekleyen geçici master değeri işareti |

Browser bu alanlardan hiçbirini uydurmaz veya farklı bir kaynaktan overwrite etmez.

## 5. Authorization ve non-disclosure değişmezleri

1. Yalnız actor'ın güncel server-side erişim kapsamındaki **aktif** şirketler döner.
2. Request header/body/query/route, JWT metni, localStorage, URL veya istemci cache'i directory kümesini genişletemez.
3. Bu endpoint için `X-Company-Id` gerekmez; bu durum Group authority veya foreign-company yetkisi üretmez.
4. Directory'de görünen bir şirket, o şirketteki tüm business endpointlerine yetki vermez. Her business endpoint permission ve CompanyContext'i yeniden doğrular.
5. Şirket kapsamı olmayan, fakat endpoint permission'ı bulunan authenticated actor güvenli boş liste alır; yabancı şirket sayısı veya kimliği açıklanmaz.
6. Permission'ı olmayan actor shared `403 forbidden`, anonymous actor shared `401 authentication_required` sonucuna gider.
7. Inactive şirket, actor kapsamı içinde bulunsa bile dönmez.
8. Cari tarafındaki tek join anahtarı:

```text
CompanyPartyAccount.companyId == CompanySummary.id
```

9. Projection yoksa veya malformed ise UI şirket adı uydurmaz, ham UUID'yi kullanıcı etiketi yapmaz ve şirkete bağlama hedefini etkinleştirmez.
10. `isPlaceholder=true`, resmî hukuki doğrulama, VKN teyidi veya production master kabulü değildir.

## 6. Deterministik sıra

Mevcut runtime sonucu şirket koduna göre sıralar. V1 compatibility tabanı bu deterministik davranışı korur.

Bu sözleşme kod sırasına ticari öncelik anlamı vermez ve yeni preferred/default şirket politikası üretmez. Default seçim yalnız actor'ın o anda dönen yetkili listesi içinden, güvenli frontend politikasıyla yapılabilir; localStorage authority değildir.

## 7. CompanyContext ve stale-response fence

Exact cache kütüphanesi, provider veya revision endpoint şekli bu sözleşmede seçilmez. Zorunlu davranış şudur:

- directory snapshot yalnız actor/session ve güncel yetkili-şirket kümesine bağlı private state'tir;
- login/logout, actor/session değişimi, kesin authorization kaybı, malformed cevap veya server-backed policy/company-set revision değişimi eski snapshotı geçersiz kılar;
- güvenilir revision bağı henüz yoksa snapshot persistent veya cross-session browser cache'e yazılmaz;
- localStorage yalnız başlangıç tercihidir; permission veya Company UUID kaynağı değildir;
- stored CompanyId yeni directory içinde yoksa silinir ve güvenli `no-company` state açılır;
- şirket değişimi bütün company-scoped requestler için yeni generation üretir;
- önceki generation'a ait geç response header, breadcrumb, route, form, liste veya Cari state'ine yazamaz;
- geç response yeni şirket için cache girdisi oluşturamaz;
- pending write, dirty form ve timeout/unknown-result akışları shell sözleşmesindeki daha sıkı kuralları ayrıca tüketir.

## 8. Hata ve malformed-response davranışı

Shared platform sahipliği korunur:

| HTTP | Stable code |
|---:|---|
| 401 | `authentication_required` |
| 403 | `forbidden` |
| 500 | `internal_error` |
| 503 | `service_unavailable` |

Bu contract yeni ProblemDetails kodu üretmez.

Frontend aşağıdaki durumları birbirinden ayırır:

- initial loading;
- güvenli empty/no-authorized-company;
- 401;
- 403;
- 500/503;
- offline/network failure;
- malformed payload;
- stale company response.

Malformed kabul edilen örnekler:

- array olmayan root;
- boş/invalid Company UUID;
- aynı `id` için duplicate satır;
- gerekli alanın eksik veya yanlış türde olması;
- `code` veya `legalName` değerinin UI güvenli metin sözleşmesini karşılamaması.

Malformed payload partial render edilmez; önceki directory snapshotı yetki varmış gibi gösterilmez. Localized `title/detail` branching kaynağı değildir.

## 9. Codex backend/OpenAPI teslimi

Codex:

1. Mevcut endpointi bu contract ID ve sürümle repository-visible OpenAPI contractına bağlar.
2. Exact method, route, response alanları, authentication, permission metadata ve `X-Company-Id` yokluğu için contract testleri ekler.
3. A-only, B-only, A+B, no-scope ve inactive-company gerçek SQL/HTTP kabul matrisini çalıştırır.
4. Header/body/query/route/JWT metniyle directory expansion olmadığını kanıtlar.
5. Permission kaybı veya actor değişimi sonrası eski response'un server-side authority olmadığını doğrular.
6. Deterministik OpenAPI üretimini ve generated-client drift girdisini hazırlar.
7. Yeni permission, role, schema, CompanyId istisnası veya business default üretmez.

## 10. Claude frontend/browser teslimi

Claude:

1. El yazısı `CompanySummary` contractını generated OpenAPI client tüketimine taşır.
2. Loading, no-company, 401, 403, 500/503, offline, malformed ve stale-response state'lerini uygular.
3. Duplicate veya invalid Company UUID içeren payloadı partial render etmeden reddeder.
4. `isPlaceholder=true` kaydını açık 'resmî bilgi teyidi bekliyor' davranışıyla gösterir.
5. Ham UUID fallback'i veya sabit şirket adı üretmez.
6. Stored selection artık yetkili listede değilse temizler.
7. Company switch request-generation/stale-response browser yarış testlerini ekler.
8. Keyboard, visible focus, screen-reader label, %200 zoom ve mobil selector kabulünü kanıtlar.

## 11. ChatGPT business/acceptance teslimi

ChatGPT:

- bu Markdown ile makine-okunur JSON contract parity'sini korur;
- golden actor/company matrisini sağlar;
- Codex ve Claude PR'larında fake Company, mock authority, ikinci DTO gerçeği veya `OPEN-001` aşımı olup olmadığını inceler;
- exact-head evidence bulunmadan acceptance iddiası yazmaz.

## 12. Golden actor/company matrisi

| Senaryo | Beklenen |
|---|---|
| actor yalnız aktif A'ya yetkili | yalnız A |
| actor yalnız aktif B'ye yetkili | yalnız B |
| actor aktif A+B'ye yetkili | A ve B, deterministik sıra |
| actor yalnız inactive B'ye yetkili | boş liste |
| actor'ın şirket kapsamı yok | boş liste |
| actor'ın Organization read permission'ı yok | `403 forbidden` |
| anonymous | `401 authentication_required` |
| request foreign CompanyId taşımaya çalışıyor | directory genişlemez |
| duplicate/invalid UUID payload | UI safe malformed state, partial data yok |
| stored CompanyId artık yetkili değil | preference temizlenir, no-company/safe selection |
| A response'u B seçildikten sonra geç geliyor | A verisi B state'ine yazılmaz |
| `isPlaceholder=true` | teyit-bekliyor işareti; resmî bilgi iddiası yok |

Her senaryoda foreign `id`, `code`, `legalName`, count ve permission envanteri sızıntısı sıfırdır.

## 13. Definition of Ready

- [x] Mevcut route ve response alanları runtime'da vardır.
- [x] Cari UI join ve no-raw-UUID sınırı merged contractta tanımlıdır.
- [x] `OPEN-001` placeholder sınırı tanımlıdır.
- [x] Backend ve frontend tek-yazar sorumlulukları otonom v2 planında tanımlıdır.
- [ ] OpenAPI export ve generated-client drift hattı merge edilmiştir.
- [ ] Codex backend test planı bu issue'ya bağlanmıştır.
- [ ] Claude browser test planı bu issue'ya bağlanmıştır.
- [ ] İki implementation PR'ı exact current-main tabanından ve çakışmayan yüzeylerden açılmıştır.

Eksik DoR maddeleri hazırlık ve implementation'ın paralel başlamasını engellemez; fakat ilgili tarafın merge kapısıdır.

## 14. Definition of Done

- runner-backed backend, frontend, gerçek SQL Server, container ve UI golden yeşildir;
- contract-drift yeşildir;
- red→green backend ve browser acceptance kanıtı vardır;
- P0/P1 yoktur;
- Codex ve Claude exact-head karşı-review'ları tamamlanmıştır;
- ChatGPT source/business review `HANDOFF-ACCEPTED` vermiştir;
- expected-head squash merge uygulanmıştır;
- sonuç `main` üzerinde fresh CI ile yeniden doğrulanmıştır.

## 15. Risk ve sınır

```text
KATMAN: K3
CONTRACT: REVIEW_PENDING
EXISTING RUNTIME: PARTIAL
OPENAPI/GENERATED CLIENT: PENDING
SHELL/CARI CONSUMPTION: PENDING
PRODUCTION ACTIVATION: NO-GO
```

Bu sözleşmenin merge edilmesi tek başına ERP-000K'yi `done`, Cari UI'yı production-ready veya placeholder hukuki bilgileri doğrulanmış yapmaz.

Related: Issue #120, #66, #78, #109, #117, `OPEN-001`
