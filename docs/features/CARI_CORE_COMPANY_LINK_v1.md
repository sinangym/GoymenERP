# Cari Core — Mevcut Party'yi Yeni Göymen Şirketine Bağlama v1.3

- Tarih: `2026-08-30`
- Kapsam: `CARI-CORE-1C` / `CARI-AC-002`
- Current-main tabanı: `83197c50f515db959638fadd7cc11ad6eb7a4fc8`; kanonik temiz yeniden yapım: PR #77
- Kaynak kararlar: `CARI-DEC-001`, `CARI-DEC-003`, `DEC-CAR-003`, `ADR-CARI-001`, `ADR-011`, `CARI-PERM-001=A`, `AT-013`

## 1. Kullanıcı sonucu

Grup seviyesinde zaten var olan tek bir `Party`, yeni Party veya yeni `CariCode` üretilmeden başka bir Göymen hukuki şirketine ayrı `CompanyPartyAccount` ile bağlanır.

Bu akış yalnız hedef şirket-cari ilişkisini oluşturur. Party kimliği, cari kodu ve kaynak şirket ilişkisi değişmez.

Global `Party.IsActive=false` olan kayıt tarihsel görünürlük için korunur fakat yeni ticari kullanım amacıyla başka şirkete bağlanamaz. Bu durum varlık bilgisini açıklamayan terminal `404 NotFound` sonucu üretir.

## 2. Public command

`POST /api/v1/counterparties/{partyId}/company-account`

İstek gövdesi yalnız hedef şirketteki `Roles` değerini taşır. Hedef şirket route/body/query içinden alınamaz; her zaman aktif ve yetkili `CompanyContext` değeridir.

Command adı: `counterparty.company-account.create.v1`.

Kullanıcı gerekçesi bu ilk dilimde zorunlu değildir. Audit nedeni sistem tarafından kanonik olarak üretilir.

## 3. Yetki ve görünürlük

Bağlayıcı `CARI-PERM-001=A` kararı uygulanır:

- `counterparty.accounts.create` yalnız `SYSTEM_ADMINISTRATOR` rolündedir;
- diğer kanonik roller default-deny kalır;
- yeni operator rolü veya otomatik kullanıcı ataması oluşturulmaz.

Command ayrıca:

1. aktif hedef şirkette company-wide `counterparty.accounts.create`;
2. aktif hedef şirkette company-wide `counterparty.parties.read`;
3. actor'ın Party'yi en az bir mevcut, izinli şirket ilişkisinden zaten görebilmesi;
4. grup seviyesindeki `Party.IsActive` değerinin `true` olması

şartlarını birlikte sağlar.

Party görünürlüğü yalnız `currentUser.AllowedCompanyIds` içindeki ve `counterparty.parties.read` izni bulunan şirket ilişkilerinden kanıtlanır. Bilinen veya tahmin edilen PartyId yetkisiz varlık keşfi sağlamaz; görünmeyen, olmayan veya global olarak pasif kayıt `404` döner.

Kaynak `CompanyPartyAccount.IsActive` değeri bu dilimde çapraz-şirket kimlik kanıtı için yeni bir engel değildir. Şirket hesaplarının yaşam döngüsü hukuki şirket bazında bağımsızdır; global Party aktif kaldığı sürece izinli fakat pasif bir kaynak şirket hesabı kimlik/görünürlük kanıtı olabilir. Kaynak hesap pasifliği hedef şirkette yeni hesap açılmasını kendiliğinden yasaklamaz ve kaynak hesap yeniden aktifleştirilmez.

## 4. Veri değişmezleri

- `Party.Id` değişmez.
- `Party.Code` değişmez; yeni cari kodu üretilmez.
- yalnız global aktif Party yeni şirkete bağlanabilir.
- yeni `CompanyPartyAccount.CompanyId`, aktif `CompanyContext` olur.
- `(CompanyId, PartyId)` benzersizliği korunur.
- en az bir bilinen ticari rol zorunludur.
- `ExternalIntegrationCode` public input değildir ve `null` başlar.
- VKN/TCKN/MERSİS/IBAN/banka ve Party ad alanları değişmez.

## 5. Sonuç ve makine kodu sözleşmesi

- Yeni ilişki: `201 Created`, aynı PartyId/CariCode ve hedef şirket hesabı.
- Hedef ilişki mevcut: terminal `409 AlreadyLinked`; ikinci hesap/audit olayı yok; ProblemDetails `code = already_linked`.
- Party görünmüyor, yok veya global olarak pasif: non-disclosing terminal `404 NotFound`.
- Aynı key farklı actor/payload: `409 IdempotencyConflict`; ProblemDetails `code = idempotency_conflict`.

`already_linked` ve `idempotency_conflict`, locale bağımsız ve kararlı `CounterpartyProblemCodesV1` sözleşmesidir. İstemci Türkçe `title` veya `detail` metnini parse etmez. Yeni sürüm veya yeni kod ancak ayrı owner-sözleşmesiyle eklenir; mevcut kodların anlamı sessizce değiştirilmez.

## 6. Idempotency ve yarış

- Yetki kontrolü replay çözümünden önce uygulanır.
- Tekil ve kanonik `Idempotency-Key` ortak fail-closed parser ile zorunludur.
- Aynı key + actor + payload ilk kalıcı sonucu ve ilk correlation kimliğini replay eder.
- `NotFound` ve `AlreadyLinked` sonuçları kalıcı terminal command sonuçlarıdır.
- Global pasif Party için ilk `NotFound` sonucu tek `CommandExecution` olarak kalıcılaşır; replay yeni account/audit üretmeden aynı `404` ve ilk correlation kimliğini döndürür.
- `AlreadyLinked` ilk sonucu ve replay'i aynı `already_linked` makine kodunu taşır.
- Same-key paralel yarış tek business/audit/command etkisi bırakır.
- Different-key aynı hedef yarışı bir `201` ve `409/code=already_linked` bırakır; çift hesap veya `500` kabul edilmez.

## 7. Audit ve atomiklik

Başarılı link:

- event: `counterparty.company-account.created.v1`
- entity: `Counterparty.CompanyPartyAccount`
- permission: `counterparty.accounts.create`

`CompanyPartyAccount + AuditEvent + CommandExecution` aynı EF `SaveChanges` / SQL transaction içinde commit veya rollback olur. Shared `AuditSaveChangesInterceptor` terminaldir; Counterparty ek bir late interceptor kaydetmez.

Global pasif Party reddinde business audit olayı ve hedef hesap oluşmaz; yalnız non-disclosing terminal `NotFound` idempotency sonucu kalıcılaşır.

Counterparty'nin Audit sidecar query filter/write guard'ı PR #75 tarafından sağlanır. Counterparty migration'ı `audit.AuditEvents` veya `idempotency.CommandExecutions` tablolarını sahiplenmez.

## 8. Zorunlu gerçek SQL/HTTP kabulü

1. A'daki mevcut aktif Party, A+B yetkili admin tarafından B'ye bağlanır; PartyId/CariCode değişmez.
2. Grup görünümü tek Party ve iki ayrı company account gösterir.
3. Default-deny kullanıcı hedef şirkette link kuramaz ve sıfır etki bırakır.
4. Party'yi hiçbir izinli mevcut ilişkiden göremeyen target admin `404` alır.
5. Global `Party.IsActive=false`, kaynak şirket hesabı aktif kalsa bile hedef şirkette `404` üretir; hedef account/audit `0`, tek terminal `NotFound` execution oluşur ve aynı-key replay ilk correlation ile döner.
6. Same-key replay, farklı payload/actor `409/code=idempotency_conflict` ve terminal NotFound/AlreadyLinked replay doğrulanır.
7. İlk ve replay edilen AlreadyLinked cevapları `code=already_linked` taşır; replay ilk correlation kimliğini korur.
8. Same-key yarış tek account/command/audit bırakır.
9. Different-key yarış `201 + 409/code=already_linked`, iki terminal execution ve tek account/audit bırakır.
10. Global aktif Party'nin izinli kaynak şirket hesabı pasif olsa dahi hedef şirkette bağlantı kurulabilir; kaynak hesap pasif kalır, hedef hesap aktif ve tekil oluşur.
11. Audit veya idempotency insert hata enjeksiyonunda yalnız yeni hedef link tamamen rollback olur; kaynak ilişki korunur.
12. Missing/multiple/comma-joined `Idempotency-Key` ve bilinmeyen roller `400` ile sıfır etki bırakır.
13. Şirket kimliği body/query/route girdisiyle aktif `CompanyContext` dışına override edilemez.
14. Yetkisi olan fakat hedef şirket kapsamı olmayan actor `403` alır ve account/audit/idempotency etkisi bırakmaz.

## 9. Kapsam dışı

Party create/update ve yeniden aktifleştirme akışı, kaynak şirket hesabının pasifliğini çapraz-şirket onboarding engeline dönüştürme, VKN/TCKN/MERSİS/IBAN/banka, maker-checker, account roles update, external integration code update, intercompany bakiye netleştirme, duplicate Party merge, frontend ve finans/risk görünümü bu dilimde yoktur.
