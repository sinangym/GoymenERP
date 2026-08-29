# Cari Core — Mevcut Party'yi Yeni Göymen Şirketine Bağlama v1.1

- Tarih: 2026-08-29
- Kapsam: `CARI-CORE-1C` / `CARI-AC-002`
- Stacked taban: PR #75 head `1bc4cf8f7d41fea2cc6f0765c8719f0856816262`
- Kaynak kararlar: `CARI-DEC-001`, `CARI-DEC-003`, `ADR-CARI-001`, `ADR-011`, `CARI-PERM-001=A`

## 1. Kullanıcı sonucu

Grup seviyesinde zaten var olan tek bir `Party`, yeni Party veya yeni `CariCode` üretilmeden başka bir Göymen hukuki şirketine ayrı `CompanyPartyAccount` ile bağlanır.

Bu akış yalnız hedef şirket-cari ilişkisini oluşturur. Party kimliği, cari kodu ve kaynak şirket ilişkisi değişmez.

## 2. Public command

`POST /api/v1/counterparties/{partyId}/company-account`

İstek gövdesi yalnız hedef şirketteki `Roles` değerini taşır. Hedef şirket route/body içinden alınamaz; her zaman aktif ve yetkili `CompanyContext` değeridir.

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
3. actor'ın Party'yi en az bir mevcut, izinli şirket ilişkisinden zaten görebilmesi

şartlarını birlikte sağlar.

Party görünürlüğü yalnız `currentUser.AllowedCompanyIds` içindeki ve `counterparty.parties.read` izni bulunan şirket ilişkilerinden kanıtlanır. Bilinen veya tahmin edilen PartyId yetkisiz varlık keşfi sağlamaz; görünmeyen kayıt `404` döner.

## 4. Veri değişmezleri

- `Party.Id` değişmez.
- `Party.Code` değişmez; yeni cari kodu üretilmez.
- yeni `CompanyPartyAccount.CompanyId`, aktif `CompanyContext` olur.
- `(CompanyId, PartyId)` benzersizliği korunur.
- en az bir bilinen ticari rol zorunludur.
- `ExternalIntegrationCode` public input değildir ve `null` başlar.
- VKN/TCKN/MERSİS/IBAN/banka ve Party ad alanları değişmez.

## 5. Sonuç sözleşmesi

- Yeni ilişki: `201 Created`, aynı PartyId/CariCode ve hedef şirket hesabı.
- Hedef ilişki mevcut: terminal `409 AlreadyLinked`; ikinci hesap/audit olayı yok.
- Party görünmüyor veya yok: non-disclosing terminal `404 NotFound`.
- Aynı key farklı actor/payload: `409 IdempotencyConflict`.

## 6. Idempotency ve yarış

- Yetki kontrolü replay çözümünden önce uygulanır.
- Tekil ve kanonik `Idempotency-Key` ortak fail-closed parser ile zorunludur.
- Aynı key + actor + payload ilk kalıcı sonucu ve ilk correlation kimliğini replay eder.
- `NotFound` ve `AlreadyLinked` sonuçları kalıcı terminal command sonuçlarıdır.
- Same-key paralel yarış tek business/audit/command etkisi bırakır.
- Different-key aynı hedef yarışı bir `201` ve bir `409 AlreadyLinked` bırakır; çift hesap veya `500` kabul edilmez.

## 7. Audit ve atomiklik

Başarılı link:

- event: `counterparty.company-account.created.v1`
- entity: `Counterparty.CompanyPartyAccount`
- permission: `counterparty.accounts.create`

`CompanyPartyAccount + AuditEvent + CommandExecution` aynı EF `SaveChanges` / SQL transaction içinde commit veya rollback olur. Shared `AuditSaveChangesInterceptor` terminaldir; Counterparty ek bir late interceptor kaydetmez.

Counterparty'nin Audit sidecar query filter/write guard'ı PR #75 tarafından sağlanır. Counterparty migration'ı `audit.AuditEvents` veya `idempotency.CommandExecutions` tablolarını sahiplenmez.

## 8. Zorunlu gerçek SQL/HTTP kabulü

1. A'daki mevcut Party, A+B yetkili admin tarafından B'ye bağlanır; PartyId/CariCode değişmez.
2. Grup görünümü tek Party ve iki ayrı company account gösterir.
3. Default-deny kullanıcı hedef şirkette link kuramaz ve sıfır etki bırakır.
4. Party'yi hiçbir izinli mevcut ilişkiden göremeyen target admin `404` alır.
5. Same-key replay, payload conflict ve terminal replay doğrulanır.
6. Same-key yarış tek account/command/audit bırakır.
7. Different-key yarış `201 + 409`, iki terminal execution ve tek account bırakır.
8. Audit veya idempotency insert hata enjeksiyonunda yalnız yeni hedef link tamamen rollback olur; kaynak ilişki korunur.
9. Missing/multiple/comma-joined `Idempotency-Key` ve bilinmeyen roller `400` ile sıfır etki bırakır.
10. Şirket kimliği request gövdesi/route ile override edilemez.

## 9. Kapsam dışı

Party create/update, VKN/TCKN/MERSİS/IBAN/banka, maker-checker, account roles update, external integration code update, intercompany bakiye netleştirme, duplicate Party merge, frontend ve finans/risk görünümü bu dilimde yoktur.
