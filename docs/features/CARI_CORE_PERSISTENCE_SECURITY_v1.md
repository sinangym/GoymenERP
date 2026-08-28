# Cari Core — Persistence ve Tenant Güvenlik Sözleşmesi v1.0

- Tarih: 2026-08-28
- Taban: `main@6eca67fc41c4921ab9bb80099841c70e61a27ed3`
- Kapsam: `CARI-CORE-1B`
- Durum: **Persistence design GO / migration ve API write PARTIAL-GO**
- Kaynak: `CARI_CORE_USER_DECISIONS_v1.md`, `CARI_CORE_READINESS_v1.md`, `CARI_CORE_ACCEPTANCE_DRAFT_v1.md`, `AGENTS.md`, DEC-CAR-001…003, DEC-GOV-002/003.

## 1. Amaç

`CARI-CORE-1A` ile kabul edilmiş `Party` + `CompanyPartyAccount` domain modelini SQL/API katmanına taşırken iki değişmezi aynı anda korumak:

1. Aynı hukuki karşı taraf grup genelinde tek `Party` olarak kalır.
2. Kullanıcı yalnız yetkili olduğu Göymen şirketlerinin Party ilişkilerini görebilir; başka şirket varlığı, count'u, arama sonucu veya hata davranışı üzerinden sızmaz.

Bu belge finans, risk, açık kalem, belge kasası, dış sicil entegrasyonu veya kritik master approval implementasyonu değildir.

## 2. CompanyId ile grup-seviyesi Party uzlaştırması

### 2.1 Fiziksel model

- `counterparty.Parties`: grup-seviyesi canonical kimlik tablosu. `PartyId`, immutable `PartyCode`, `PartyKind`, mevcut canonical legal/trade name, status/version/rowversion taşır.
- `counterparty.CompanyPartyAccounts`: zorunlu `CompanyId + PartyId` bağlantısı; şirket rolleri, external integration code, status/version/rowversion taşır.
- Party tablosu doğrudan kullanıcıya açık collection değildir.
- Her Party okumasının erişim kökü `CompanyPartyAccounts` olur.

### 2.2 Güvenlik invariantı

Bir kullanıcı bir `PartyId` biliyor olsa bile backend:

`AuthorizedCompanyIds → CompanyPartyAccounts → Party`

sırasıyla erişim doğrulamadan Party verisi döndüremez.

Yetkili `CompanyPartyAccount` bağı olmayan Party için:

- liste sonucu yok,
- detail sonucu yok,
- count/facet yok,
- duplicate candidate yok,
- hata metninde varlık teyidi yok.

### 2.3 CompanyId kuralının yorumu

`Party`, kullanıcıya tek başına şirketler arası serbest master olarak açılmaz; erişim ve ticari kullanım her zaman CompanyId taşıyan `CompanyPartyAccount` üzerinden gerçekleşir. Bu grup-seviyesi canonical identity, DEC-CAR-001/002 ile kullanıcı tarafından kabul edilmiş istisnai aggregate köküdür.

Bu istisna başka modüllere genellenemez. Finans, stok, sipariş, açık kalem, belge etkisi veya şirket-özel master veri Party tablosuna taşınamaz.

## 3. İlk migration kapsamı

İlk SQL migration yalnız aşağıdakileri oluşturabilir:

### `counterparty.Parties`

- `Id` UUID / uniqueidentifier PK
- `Code` kanonik `CAR-000001` biçimini taşıyan immutable string
- `Kind`
- `LegalName`
- `TradeName` nullable
- `IsActive`
- `Version`
- `RowVersion`
- created/updated UTC metadata

Kısıtlar:

- `Code` unique
- kod reuse yok
- non-canonical code write reddedilir
- physical delete uygulama yolunda yok

### `counterparty.CompanyPartyAccounts`

- `Id`
- `CompanyId`
- `PartyId`
- role flags
- `ExternalIntegrationCode` nullable
- `IsActive`
- `Version`
- `RowVersion`
- created/updated UTC metadata

Kısıtlar:

- `(CompanyId, PartyId)` unique
- `CompanyId` Organization company ile tenant-safe FK/constraint modeliyle doğrulanır
- `PartyId` Party FK
- boş/unknown role reddedilir
- physical delete yok

## 4. CariCode üretimi

Kullanıcı kararı: grup genelinde tek immutable `CAR-000001` formatı.

İlk persistence implementasyonu:

- dış istemciden serbest PartyCode kabul etmez,
- kodu backend üretir,
- aynı kod iki Party'ye verilemez,
- concurrency altında duplicate code oluşamaz,
- sequence/number allocator implementation detayı migration PR review'unda tekleştirilir,
- kod numarası business anlamı taşımaz ve silinen/pasif kayıttan yeniden kullanılmaz.

SQL sequence, allocator table veya eşdeğer yöntem seçimi gerçek concurrency testiyle kanıtlanmadan bağlayıcı kabul edilmez.

## 5. Write modeli — bu dilimde izinli

### İzinli

- yeni Party + ilk CompanyPartyAccount atomik create,
- mevcut Party'ye yeni yetkili CompanyPartyAccount ilişkilendirme için yalnız ayrıca review edilmiş command contract,
- CompanyPartyAccount rollerini güncelleme,
- CompanyPartyAccount external integration code güncelleme,
- CompanyPartyAccount pasife alma/yeniden aktifleştirme.

### Şimdilik yasak

- Party legal/trade name direct update,
- VKN/TCKN/MERSİS/IBAN storage/write,
- Party status global mutation,
- cross-company Party mutation,
- duplicate merge,
- finance/open-item/risk write,
- başka modül tablosuna doğrudan write.

Party-level kritik mutation, maker-checker approval persistence ve cross-company etki sözleşmesi ayrı dilimde açılacaktır.

## 6. Create atomikliği

`CreateParty` işlemi tek kullanıcı aksiyonunda:

1. aktif CompanyContext ve create permission doğrular,
2. idempotency key doğrular,
3. PartyCode üretir,
4. Party oluşturur,
5. yalnız aktif/yetkili CompanyId için ilk CompanyPartyAccount oluşturur,
6. audit üretir,
7. kalıcı idempotency sonucunu yazar,
8. tek SQL transaction içinde commit eder.

Herhangi bir adım hata verirse Party tek başına veya CompanyPartyAccount tek başına kalamaz.

## 7. Grup görünümü

Birden çok şirkete yetkili kullanıcı için read query server-side authorized CompanyId seti alır.

- aynı Party tek üst kayıt olarak compose edilir,
- yalnız yetkili CompanyPartyAccount alt kayıtları döner,
- yetkisiz şirket ilişkisi count dahil görünmez,
- bu dilimde bakiye/finans alanı yoktur.

Birleşik yürüyen bakiye kararı korunur fakat Finance owner contract hazır olmadan uygulanmaz.

## 8. Hassas veri sınırı

Bu migration/dilim VKN, TCKN, MERSİS, IBAN veya banka hesabı saklamaz.

Bunlar için ayrıca:

- storage/index ihtiyaçları,
- masking/redaction,
- duplicate exact-match güvenliği,
- log/trace sızıntısı,
- approval proposal payload koruması,
- export/restore etkisi

security review ile kilitlenmelidir. Provider-spesifik şifreleme tekniği ERP-000M kararı tamamlanmadan uydurulmaz.

## 9. API sınırı

İlk API ailesi yalnız core alanlar için açılabilir:

- `GET /api/v1/counterparties`
- `GET /api/v1/counterparties/{partyId}`
- `POST /api/v1/counterparties`
- company-account role/external-code/status command'leri

Türkçe UI adı Cari olarak kalır; teknik route/module adı `Counterparty` olabilir.

Her public write:

- authorization
- CompanyContext
- idempotency key
- expected version (create hariç)
- correlation/causation
- audit

zorunluluklarını backend'de uygular.

## 10. Kabul kanıtı

Aşağıdaki 12 madde `CARI-CORE-1B` write-capable persistence/API diliminin **tamamlanma kabulüdür**. Teslimat, birbirinden bağımsız ve manuel review edilen PR'lara bölünebilir.

Yalnız schema + read-only foundation açan ve public write endpoint'i eklemeyen ara PR en az 1–4, 11–12 ve §11 migration kanıtlarını sağlamalıdır. Böyle bir foundation PR `CARI-CORE-1B DONE` veya production write acceptance olarak işaretlenemez. 5–10 maddeleri public write dilimi merge edilmeden ve `CARI-CORE-1B` tamamlanmış sayılmadan önce zorunlu kalır; ertelenmiş olmak bu acceptance'ları kaldırmaz veya zayıflatmaz.

`CARI-CORE-1B` tamamlanmadan önce gerçek SQL/HTTP testleri şunları kanıtlamalıdır:

1. aynı Party iki şirkete bağlanabilir, Party tek kalır,
2. yalnız A yetkili kullanıcı B ilişkisinin varlığını göremez,
3. A+B yetkili kullanıcı tek Party + iki yetkili şirket kırılımı görür,
4. `(CompanyId, PartyId)` duplicate ilişki DB seviyesinde engellenir,
5. PartyCode duplicate üretilemez,
6. aynı idempotency key replay ikinci Party üretmez,
7. aynı key farklı payload 409 verir,
8. stale account update 409 verir,
9. unauthorized CompanyId write 403/fail-closed olur,
10. business write + audit + idempotency sonucu atomiktir,
11. direct Party sensitive mutation route'u yoktur,
12. physical delete route'u yoktur.

## 11. Migration/rollback

- yeni `counterparty` schema kendi DbContext/migration history sahibi olmalıdır,
- başka modül migration'ı değiştirilmez,
- migration idempotent script üretimine girmelidir,
- rollback veri kaybı doğuracaksa açık forward-fix planı yazılmadan production acceptance verilmez,
- ERP-000M migration ownership/order kapılarıyla çelişen deployment davranışı eklenmez.

## 12. Sonuç

`CARI-CORE-1B` için tenant-safe persistence modeli teknik olarak hazırlanmıştır. Teslimat schema + read-only foundation ile başlayabilir; bu ara dilim write acceptance'ı tamamladığını iddia edemez. Write-capable dilim yalnız Party + CompanyPartyAccount core alanlarını ve create/read/company-account local write'larını açabilir ve §10'daki kalan acceptance'ları tamamlamalıdır. Hassas kimlik/banka, Party-level kritik edit, maker-checker approval, bakiye/risk/open-item ve cross-module merge sonraki kontrollü dilimlerde kalır.
