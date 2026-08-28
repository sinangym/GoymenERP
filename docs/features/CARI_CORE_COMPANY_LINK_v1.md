# Cari Core — Mevcut Party'yi Yeni Göymen Şirketine Bağlama v1.0

- Tarih: 2026-08-28
- Kapsam: `CARI-CORE-1C` / `CARI-AC-002`
- Taban: `main@a3b58b9266ea192dd029889ac60abfe8b2ec194e`
- Kaynak kararlar: `CARI-DEC-001`, `CARI-DEC-003`, `ADR-CARI-001`, `ADR-011`

## 1. Amaç

Grup seviyesinde zaten var olan tek bir `Party` kaydını, Party kimliğini veya `CariCode` değerini çoğaltmadan başka bir Göymen hukuki şirketine ayrı `CompanyPartyAccount` ile bağlamak.

Bu akış yeni Party oluşturmaz. Yalnız hedef şirket için yeni şirket-cari ilişkisi oluşturur.

## 2. Public command

`POST /api/v1/counterparties/{partyId}/company-account`

İstek gövdesi:

- `Roles`: hedef Göymen şirketindeki ticari roller.

Hedef şirket route/body içinden alınmaz. Hedef şirket her zaman aktif ve yetkili `CompanyContext` değeridir.

Command adı:

`counterparty.company-account.create.v1`

## 3. Yetki ve görünürlük

Command iki bağımsız güvenlik şartını birlikte sağlamalıdır:

1. Aktif hedef şirkette company-wide `counterparty.accounts.create` yetkisi.
2. Actor'ın aynı Party'yi mevcut `CompanyPartyAccount` ilişkilerinden en az biri üzerinden görmeye yetkili olması.

Party görünürlüğü yalnız `currentUser.AllowedCompanyIds` içindeki ve `counterparty.parties.read` izni bulunan şirket ilişkilerinden kanıtlanır.

Sonuç:

- yalnız hedef B şirketine yetkili fakat Party'yi hiçbir yetkili mevcut ilişkiden görmeyen actor, bilinen/tahmin edilen `PartyId` ile link kuramaz;
- A+B yetkili actor, Party A'da görünürse aktif B bağlamında B hesabını oluşturabilir;
- Party var/yok ayrımı yetkisiz actor'a hata, audit veya replay üzerinden açıklanmaz.

## 4. Veri değişmezleri

- `Party.Id` değişmez.
- `Party.Code` değişmez ve yeni kod üretilmez.
- yeni `CompanyPartyAccount.CompanyId = aktif CompanyContext` olur.
- `(CompanyId, PartyId)` benzersizliği korunur.
- hedef hesap rolleri request'ten gelir ve en az bir bilinen rol zorunludur.
- `ExternalIntegrationCode` bu dilimde public input değildir ve `null` başlar.
- Party legal/trade name, VKN/TCKN/MERSİS/IBAN/banka alanları değişmez.

## 5. Sonuç sözleşmesi

Başarılı yeni link:

- HTTP `201 Created`
- aynı `Party.Id` ve aynı `CariCode`
- response içinde aktif hedef şirketin yeni `CompanyPartyAccount` görünümü.

Hedef şirket ilişkisi zaten varsa:

- HTTP `409 Conflict`
- `AlreadyLinked` terminal sonucu
- ikinci CompanyPartyAccount veya audit business olayı oluşturulmaz.

Party actor tarafından mevcut yetkili bir ilişkiden görünmüyorsa:

- HTTP `404 Not Found`
- varlık keşfi yapılmaz.

## 6. Idempotency ve yarış

- yetki kontrolü replay okumadan önce uygulanır;
- aynı key + actor + canonical payload ilk kalıcı sonucu replay eder;
- farklı payload/actor aynı key ile `409 IdempotencyConflict`;
- terminal `NotFound` / `AlreadyLinked` kalıcı sonuçtur ve aynı key ile replay edilir;
- aynı key paralel link yarışında tek business/audit etkisi kalır;
- farklı key ile aynı hedef link yarışı bir `201` ve bir terminal `409 AlreadyLinked` ile sonuçlanır; `500` veya çift satır kabul edilmez.

Idempotency lookup aktif hedef CompanyId ile scope edilir.

## 7. Audit / atomiklik

Başarılı link için:

- event: `counterparty.company-account.created.v1`
- entity: `Counterparty.CompanyPartyAccount`
- target CompanyId, PartyId, account Id, roles, actor, permission, correlation/causation ve hash'lenmiş idempotency kanıtı tutulur.

`CompanyPartyAccount + AuditEvent + CommandExecution` aynı EF `SaveChanges` / SQL transaction içinde commit veya rollback olur.

`AlreadyLinked` / `NotFound` terminal command sonucu business mutation/audit olayı üretmeden kalıcılaştırılabilir.

## 8. Acceptance

En az aşağıdakiler gerçek SQL/HTTP ile kanıtlanmalıdır:

1. A'da mevcut Party, A+B yetkili actor tarafından B'ye bağlanır; PartyId/CariCode değişmez.
2. grup görünümünde aynı Party tek kart ve iki ayrı company account olarak görünür.
3. B-only ve Party'yi önceden göremeyen yetkili target actor `PartyId` tahminiyle link kuramaz.
4. same-key replay ikinci hesap/audit üretmez.
5. same-key farklı payload `409`.
6. hedef hesap zaten varsa yeni key ile terminal `409 AlreadyLinked`.
7. same-key race tek business/audit/command etkisi bırakır.
8. different-key same-target race `201 + 409`, çift account yok.
9. audit veya idempotency insert hata enjeksiyonunda yeni company account rollback olur.
10. target CompanyId request gövdesi/route ile override edilemez.

## 9. Kapsam dışı

- Party create
- Party legal/trade name update
- VKN/TCKN/MERSİS/IBAN/banka
- kritik değişiklik maker-checker
- company-account roles update
- external integration code update
- intercompany bakiye netleştirme
- duplicate Party merge
- frontend
