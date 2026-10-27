# ADR-SAL-001 — Satış idempotency yolu: modül sahipli deneme kaydı ve platform Idempotency ile ilişkisi

- Durum: **Öneri** (Proposed). Karar teknik ve production öncesinde geri alınabilir; işletme politikası
  (saklama, fiyat, yetki, eşik) seçmez. Bu yüzden Sinan kararı gerektirmez; kabule geçiş backend sahibi
  (Codex şeridi) onayı ve bağımsız review ile olur. Saklama/imha `OPEN-021` olarak açık kalır.
- Tarih: 2026-10-27
- Karar sahibi: Satış backend sahibi (Codex şeridi) + merkezi mimari review; yazar Claude Satış şeridi.
- Plan maddesi: `TEK-05` (`docs/delivery/SATIS_HAZIRLIK_PLANI_v1.md` §2, dalga S1).
- Kaynak tabanı: `main@e2e981d2b014e42c360d430c8fffc88bf728755b`.
- İlgili karar ID'leri: `ADR-002`, `ADR-009`, `ADR-011`, `ADR-CARI-001`, `ADR-PRD-001`, `OPEN-021`,
  `DEC-DATA-001`, `SAT-D1` (taban fiyat onayı; fiyat görünürlüğü), plan `TEK-04` (outbox = ERP-000F).

Bu belge kod, DbContext, migration, izin ya da grant getirmez. Mevcut `sales.DraftAttempts` kayıtlarına
dokunulmaz; her taşıma veya şema değişikliği ayrı planlanan bir K3 işidir.

## Bağlam

Satış planı (§2, `TEK-05`) "iki farklı idempotency yolu" riskini kaydeder: Satış kendi
`sales.DraftAttempts` tablosuna yazarken platformda ayrıca `Idempotency` modülü ve
`idempotency.CommandExecutions` tablosu vardır. S2–S6 dalgaları çok sayıda yeni Satış komutu
ekleyecek (revizyonlar, zorla kapatma, talep, teklif, siparişe dönüşüm, fatura taslağı, dışa aktarım,
eşleştirme, kesinleştirme). Bu komutlar yazılmadan önce hangi yolu kullanacakları tek ve açık olmalıdır.

İnceleme, depoda aslında **üç** mekanizma olduğunu gösteriyor:

### Yol 1 — Platform `idempotency.CommandExecutions` (ADR-011)

- Domain: `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Domain/Executions/CommandExecution.cs`
  (`Id`, `CompanyId`, `CommandName`, `IdempotencyKeyHash`, `RequestHash`, `ActorId`, `Outcome`,
  `HttpStatusCode`, `ResponseJson`, `OriginalCorrelationId`, `CompletedAtUtc`, `SchemaVersion`).
- Hash: `.../Domain/Executions/IdempotencyHash.cs` — anahtar = trim edilmiş değerin büyük harf SHA-256'sı;
  payload = `JsonSerializer` çıktısının büyük harf SHA-256'sı.
- Eşleme: `.../Infrastructure/Persistence/CommandExecutionModelBuilderExtensions.cs` — benzersiz indeks
  `(CompanyId, CommandName, IdempotencyKeyHash)`, `ISJSON(ResponseJson)`, durum kodu ve şema sürümü
  kısıtları; migration sahibi `CommandExecutionDbContext` (`InitialIdempotency`).
- Değişmezlik: EF write guard + SQL trigger `TR_CommandExecutions_RejectMutation` (`THROW 51010`,
  `.../Infrastructure/Persistence/Migrations/20260827175405_InitialIdempotency.cs`).
- Replay: kalıcı `ResponseJson` + `HttpStatusCode` **anlık görüntüsü** aynen döner (ADR-011 §4).
- Kullananlar: yalnız **Organization** ve **Counterparty**. İkisi de tabloyu kendi DbContext'ine
  `ConfigureCommandExecution(excludeFromMigrations: true)` ile "sidecar" olarak eşler
  (`OrganizationDbContext.cs:42`, `CounterpartyDbContext.cs:40`) ve bunun için
  `docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json` içinde ADR'li bir
  `*.Infrastructure → Idempotency.Infrastructure` kenarı taşır (ADR-011, ADR-CARI-001).

### Yol 2 — Grup paylaşımlı `idempotency.GroupSharedCommandTerminals` (yalnız Product)

- `.../Domain/Executions/GroupSharedCommandBinding.cs`, `GroupSharedCommandReplay.cs`,
  `.../Infrastructure/Executions/SqlGroupSharedCommandTerminalStore.cs`,
  `.../Infrastructure/Persistence/GroupSharedCommandDbContext.cs`.
- Şirket kapsamlı **değildir**: anahtar `(Subject='Product', CommandName, IdempotencyKeyHash)`; köken şirket
  ve aktör anahtarın parçası değildir ama replay eşleşmesinde karşılaştırılır
  (`GroupSharedCommandBinding.cs:101-107`). CHECK kısıtı komut adlarını beş Product komutuyla sınırlar.
- Satış için uygulanamaz (Satış kayıtları şirket kapsamlıdır, ADR-002); bu ADR'de yalnız tam resim için anılır.

### Yol 3 — Modül sahipli deneme (attempt) kaydı (Satış'ın bugünkü yolu)

Satış:

- Satır: `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesRows.cs:161`
  `DraftAttemptRow` (`CompanyId`, `CommandName`, `IdempotencyKeyHash`, `RequestHash`, `ActorId`,
  `Outcome` 1/2, `DraftId`, `RefusalJson`, `ResultJson`, `CorrelationId`, `RecordedAtUtc`).
- Eşleme: `.../Persistence/SalesDbContext.cs:315` `ConfigureDraftAttempts` — **birincil anahtar**
  `(CompanyId, CommandName, IdempotencyKeyHash)`, `Latin1_General_100_BIN2`; `CompanyId → organization.Companies` ve
  `(CompanyId, DraftId) → sales.Drafts` FK'leri; yalnız create komutuna filtreli
  `UX_DraftAttempts_DraftId` (`CreateAttemptFilter`, satır 63); `CK_DraftAttempts_Outcome` ve
  `CK_DraftAttempts_Result` (`ResultJson` yalnız `sales.order-draft.approve-price-floor-exceptions.v1`
  başarısında, satır 74); global şirket filtresi.
- Değişmezlik: yalnız EF write guard (`SalesDbContext.ApplyWriteGuards`, satır 527: silme reddi, deneme
  satırında hiçbir kolon değiştirilemez). SQL trigger **yok**.
- Anahtar hash'i: `SalesStore.IdempotencyKeyHash` (`.../Persistence/SalesStore.cs:1570`) — platformla
  **aynı algoritma** (trim + UTF-8 + büyük harf SHA-256).
- İstek hash'i: `SalesDraftRequestHash`
  (`.../Sales.Application/Contracts/ISalesOrderDraftStore.cs:305`, `Hash` satır 391) — kanonik JSON'un
  **küçük harf** SHA-256'sı; değişiklik komutlarında komut adı ve hedef kimliği içeriktedir, oluşturma
  hash'inde (satır 307-311) yoktur (platform büyük harf kullanır; iki değer hiçbir
  yerde karşılaştırılmaz).
- Akış: `StoreDraftAsync` (59), `FindReplayAsync` (139), `RecordRefusalAsync` (179),
  `UpdateDraftStateAsync` ve diğer komut metotları; yarış `SaveAsync` (1451) içinde 2601/2627 →
  `ConcurrentChange`, servis anahtarı taze okur ve kazananın sonucunu döner
  (`SalesDraftService.cs:154`).
- Replay: başarı (`Outcome = 1`) **referanstır** — `DraftId` üzerinden siparişin **bugünkü** hâli okunur ve
  güncel yetki/fiyat görünürlüğüyle yeniden işlenir (`DiscloseAsync`); terminal ret (`Outcome = 2`)
  `RefusalJson` anlık görüntüsüdür; `ResultJson` yalnız taban fiyat onayında ve **iki görünürlük
  varyantıyla** (`Visible` / `Withheld`) saklanır (`ReplayResult`, satır 576).
- Komut adları: `SalesDraftCommands` (`ISalesOrderDraftStore.cs:35`), 14 sürümlü ad.

Aynı modül sahipli desen depoda baskındır: `purchasing.PurchaseAttempts`, `logistics.LoadingAttempts` /
`CustomerReturnAttempts` / `TransferOutgoingDocumentAttempts`, `documents.DocumentAttempts`,
`product.PriceListAttempts` (çoğunlukla Outcome + referans biçimi; istisnalar: `DocumentAttempts`'te
`Outcome` yok, `TransferOutgoingDocumentAttempts` `Outcome` taşımaz ve zorunlu `ResultJson` saklar,
`CustomerReturnAttempts` başarıda `ResultJson` ister) ve `inventory.Commands`, `accounting.Commands`,
`finance.Commands`, `manufacturing.Commands` (`ResultJson` anlık görüntü biçimi). Hepsi ADR-011'in
anahtar, hash, aktör ve çakışma kurallarını kendi şemalarında uygular; platform tablosunu kullanan yalnız
Organization ve Counterparty'dir.

### İki yolun karşılaştırması

| Konu | Platform `CommandExecutions` | Satış `DraftAttempts` |
|---|---|---|
| Şema/migration sahibi | `idempotency` / Idempotency modülü | `sales` / Satış modülü |
| Kimlik | `Id` PK + benzersiz `(CompanyId, CommandName, KeyHash)` | PK `(CompanyId, CommandName, KeyHash)`, BIN2 |
| Ham anahtar | saklanmaz | saklanmaz |
| Anahtar hash'i | büyük harf SHA-256(trim) | aynı |
| İstek hash'i | büyük harf SHA-256 | küçük harf SHA-256, komut adı içerikte |
| Aktör | saklanır, karşılaştırılır | aynı |
| Farklı içerik/aktör | tek `409`, eski sonucu açıklamaz | tek `409 same_key_payload_mismatch` |
| Başarı replay'i | `ResponseJson` anlık görüntüsü | iş kaydına referans; bugünkü hâl, güncel görünürlük |
| Terminal ret replay'i | `ResponseJson` + durum kodu | `RefusalJson` (durum, kod, alan hataları) |
| Şema sürümü | `SchemaVersion` kolonu | örtük (komut adı sürümlü) |
| İş kaydına FK | yok | var (`sales.Drafts`) |
| Değişmezlik | EF guard + SQL trigger `51010` | yalnız EF guard |
| Aynı-anahtar yarışı | benzersiz indeks; kaybeden kazananı okur | PK çakışması; servis kazananı okur |
| Atomiklik | iş + audit + sonuç tek `SaveChanges` (sidecar) | iş + audit + deneme tek `SaveChanges` |
| Modül kenarı | `*.Infrastructure → Idempotency.Infrastructure` allowlist | yok (yalnız Audit kenarı) |
| Saklama/imha | yok; `OPEN-021` | yok; `OPEN-021` |

## Seçenekler

### A — Yeni Satış komutları platform `CommandExecutions`'a yazar

`SalesDbContext` tabloyu sidecar olarak eşler (Counterparty gibi); `DraftAttempts` eski komutlar için kalır.

- Artı: tek merkezi tablo; SQL değişmezlik trigger'ı hazır; `OPEN-021` tek noktada uygulanabilir.
- Eksi: Satış iki yolu **kalıcı** olarak taşır (eski komutlar `DraftAttempts`, yeniler platform) — `TEK-05`'in
  çözmek istediği durumu derinleştirir. Yeni `Sales.Infrastructure → Idempotency.Infrastructure` kenarı
  (merkezi mimari değişikliği) gerekir. `ResponseJson` anlık görüntüsü, kullanıcının fiyat görme yetkisine
  göre değişen yanıtla (`SAT-D1`, `sales.orders.price.read`) çakışır: ya her görünürlük varyantı saklanır ya da
  yetkisi daralan aktöre eski fiyat yeniden gösterilir. İş kaydına FK ve "create başına tek kayıt" filtresi
  kaybolur. Gelecekteki Satış outbox'ı (`TEK-04`) ile birlikte üç sahipli tabloya yayılan bir transaction olur.

### B — Bütün Satış komutları platforma taşınır (mevcut `DraftAttempts` dahil)

- Artı: Satış'ta tek yol.
- Eksi: A'nın bütün eksileri + mevcut deneme kayıtlarının veri taşıması (replay'in kesintisiz sürmesi
  gereken, append-only kayıtlar), referans replay'inden anlık görüntü replay'ine anlam değişikliği ve
  `RequestHash` biçim dönüşümü. Production öncesi olsa da yüksek risk / düşük kazanç.

### C — Satış'ın tek yolu modül sahipli deneme kaydıdır; platform ile ilişki "sözleşme uyumu"dur (önerilen)

Satış platform tablosuna yazmaz; ADR-011'in kurallarını kendi şemasında, tek bir Satış içi yolla uygular.

- Artı: Satış zaten bu yolda; depodaki baskın desenle (Purchasing, Logistics, Documents, Inventory,
  Accounting, Finance, Manufacturing) aynı; yeni modül kenarı yok; iş kaydına FK korunur; fiyat görünürlüğü
  güncel yetkiyle hesaplanır; outbox (`TEK-04`) aynı `SalesDbContext` transaction'ına doğal olarak girer.
  Veri taşıması gerekmez.
- Eksi: `OPEN-021` uygulandığında birden çok tabloyu kapsaması gerekir; SQL değişmezlik trigger'ı modül
  tarafından ayrıca eklenmelidir; ADR-011 §4'ten (anlık görüntü) bilinçli bir ayrılık yazılı olmalıdır.

### D — Platform ortak bir kütüphane/sözleşme sağlar, depolama modülde kalır

Idempotency modülü hash, bağlama ve çakışma kurallarını bir Application sözleşmesi olarak yayınlar;
modüller kendi tablolarında kullanır.

- Artı: depo genelinde gerçek tek kural kaynağı.
- Eksi: cross-module contract (K3) ve bütün modülleri etkiler; `TEK-05` kapsamını aşar. C ile çelişmez;
  ileride C'nin üstüne kurulabilir (bkz. yeniden değerlendirme).

## Karar

**Seçenek C.** Satış'ın idempotency yolu, `sales` şemasında Satış'ın sahip olduğu deneme kaydıdır.

1. **Tek yol.** Yeni Satış komutları (S2–S6: `API-02`, `API-04/05`, `API-20…24`, `API-32…36` ve sonrakiler)
   `idempotency.CommandExecutions`'a veya `GroupSharedCommandTerminals`'a yazmaz;
   `Sales.Infrastructure → Idempotency.Infrastructure` allowlist kenarı açılmaz. Platform tablosu Organization
   ve Counterparty'nin yolu olarak kalır.
2. **Tablo seçimi.** Sipariş aggregate'ini değiştiren yeni komutlar (zorla kapatma, başlık/satır revizyonu,
   toplamlar vb.) mevcut `sales.DraftAttempts`'e yeni sürümlü komut adıyla yazar. Yeni bir aggregate (talep,
   teklif, fatura taslağı) kendi deneme tablosunu `DraftAttempts` ile **aynı biçimde** alır: PK
   `(CompanyId, CommandName, IdempotencyKeyHash)` BIN2, `RequestHash`, `ActorId`, `Outcome` (1 = kayıt,
   2 = terminal ret), aggregate'e nullable FK, `RefusalJson`, komut listesiyle sınırlı nullable `ResultJson`,
   `CorrelationId`, `RecordedAtUtc`, create'e filtreli tek-kayıt indeksi, şirket filtresi ve append-only write
   guard. (Tek genel `sales.CommandAttempts` yerine aggregate başına tablo: FK bütünlüğü ve Logistics emsali.
   Tablo adları ilgili dilimin migration PR'ında, K3 olarak kesinleşir.)
3. **Bir komut = bir deneme satırı.** Bir komut iki aggregate'e dokunsa da (ör. `API-23` teklifi siparişe
   dönüştürme) tek deneme satırı yazar; hangi tabloya yazacağı o dilimin tasarımında yazılır. Başka modülün
   etkisi (Finance açık kalemi, Lojistik/Stok kayıtları) Satış'ın deneme satırıyla temsil edilmez: tüketici
   kendi yetkisi, idempotency'si ve audit'iyle yazar (outbox/inbox dedup ERP-000F, `TEK-04`).
4. **Ortak anlam (ADR-011 uyumu).** Her yeni komut:
   - anahtarı `SalesStore.IdempotencyKeyHash` ile (büyük harf SHA-256, trim, ham anahtar saklanmaz) hash'ler;
     HTTP katmanı tek kanonik `Idempotency-Key` (≤128, virgülsüz) ister, yoksa `400 command_metadata_invalid`;
   - içerik hash'ini `SalesDraftRequestHash` biçiminde (sürümlü komut adı + hedef kimlik + kanonik gövde,
     küçük harf SHA-256) üretir; mevcut biçim değiştirilmez;
   - aktörü saklar ve karşılaştırır; farklı içerik veya aktör tek `409 same_key_payload_mismatch` olur ve eski
     sonucu, sahibini ya da korelasyonu açıklamaz;
   - yetki ve şirket bağlamını replay okunmadan **önce**, her sahip okumasından sonra ve tek kayıttan hemen
     önce ölçer (ADR-011 §5); commit sonrası çit yoktur (S03 / #321 P3-1 kararı);
   - başarıyı iş kaydına referans olarak saklar; replay kaydın **bugünkü** hâlini güncel yetki ve görünürlükle
     döner, `Idempotency-Replayed: true` ve `Idempotency-Original-Correlation-Id` ekler;
   - yetkili terminal retleri (`version_conflict`, durum çakışması, iş kuralı blokajı) `RefusalJson` olarak
     kaydeder ve aynen replay eder; geçici/5xx sonuç kaydedilmez (ADR-011 §6–§7);
   - `ResultJson` anlık görüntüsünü yalnız yanıt kalıcı aggregate'ten yeniden üretilemiyorsa kullanır; yanıt
     görünürlüğe göre değişiyorsa her varyantı saklar (taban fiyat onayı emsali);
   - aggregate + deneme + audit (+ gelecekte outbox) yazımlarını tek `SalesDbContext.SaveChanges` / tek SQL
     transaction'ında yapar; aynı-anahtar yarışında kaybeden hiçbir şey yazmaz, servis anahtarı taze okuyup
     kazananın sonucunu döner (ADR-011 §8).
5. **ADR-011 §4'ten bilinçli ayrılık.** Satış başarı replay'inde anlık görüntü yerine referans kullanır.
   Gerekçe: Satış yanıtı fiyat görünürlüğüne bağlıdır (`DiscloseAsync`); yetkisi daralan aktöre eski fiyatın
   anlık görüntüden yeniden gösterilmesi önlenir. Sonuç: replay gövdesi ilk yanıtla bayt bayt aynı olmayabilir
   (sipariş o arada ilerlediyse güncel hâl gelir); istemci gövdeyi "kaydın güncel hâli" olarak ele alır
   (`isReplay` bilgisi gövdede). Replay'de değişmeyenler: sonuç (başarı/ret), HTTP durum kodu, `Location`
   ve sipariş kimliği. Güncel hâl replay'i Satış README'sinde SALES-A1 notunda (satır 159) geçer; ancak
   README satır 98 Satış'ı §4'e uyan "Counterparty / Manufacturing deseni" olarak anar (Counterparty anlık
   görüntü replay eder). §4'ten ayrılık ilk kez bu ADR'de adlandırılır; README düzeltmesi takip işidir.
   (#257/#321 bu konuyla değil, replay durum kodunun 200→201 değişimiyle ilgilidir.)
6. **Değişmezlik.** Yeni deneme tabloları, oluşturuldukları migration'da UPDATE/DELETE'i reddeden bir SQL
   trigger'ı da taşır (platformdaki `51010` emsali; hata numarası o PR'da seçilir). Mevcut
   `sales.DraftAttempts`'e trigger eklenmesi bu ADR'nin işi değildir; ayrı planlanan K3 migration'ıdır
   (aşağıdaki takip işleri).
7. **Saklama/imha seçilmez.** Deneme satırları silinmez; Satış write guard'ı silmeyi reddeder. Saklama süresi,
   partition/arşiv ve ayrıcalıklı purge `OPEN-021` kapanana kadar uygulanmaz. `OPEN-021` çözüldüğünde
   kapsamı platform tablosuyla birlikte modül sahipli deneme tablolarını (`sales.*Attempts` ve diğer
   modüllerin karşılıkları) içermelidir; bu ADR yöntem önermez. DELETE'i reddeden trigger'lar için
   ayrıcalıklı purge yolu da `OPEN-021` ADR'sinde tasarlanır (bkz. `docs/decisions/ACIK_KARARLAR.md`).
8. **Mevcut kayıtlar.** `sales.DraftAttempts` ne taşınır ne yeniden biçimlenir; mevcut replay'ler aynen sürer.

## Sonuçlar ve riskler

- Satış'ta "iki yol" riski kapanır: Satış'ın tek yolu Yol 3'tür; platformla ilişki sözleşme uyumudur, ortak tablo
  değildir.
- Depo genelinde iki depolama biçimi (platform sidecar ve modül sahipli) sürer; bu, Satış'a özgü değil, mevcut
  mimari durumdur. Tek kural kaynağı istenirse Seçenek D ayrı bir ADR'dir.
- Anlam sapması riski: her yeni Satış komutu kuralları elle tekrarlarsa sapma olabilir. Azaltma: deneme
  okuma/karşılaştırma/ret kaydı mantığı Satış içinde tek yardımcıda toplanır (`TEK-07` dosya bölmesiyle
  birlikte yapılabilir) ve aşağıdaki testler her yeni komut için zorunludur.
- `RequestHash` biçimi platformdan farklıdır (küçük/büyük harf); değerler modüller arasında taşınmadığı için
  işlevsel etkisi yoktur, ancak belgeli kalmalıdır.
- `OPEN-021` kapanana kadar deneme tabloları büyür; production purge etkinleştirilmez.

## Takip işleri (ayrı planlanır, bu ADR ile yapılmaz)

- `sales.DraftAttempts` için SQL değişmezlik trigger'ı (K3 migration, geri alma: trigger drop; veri değişmez).
- `src/backend/Modules/Sales/README.md` satır 98'in düzeltilmesi: başarı replay'i §4'e uyan anlık görüntü
  değil, bu ADR'deki referans replay'idir (§4'ten bilinçli ayrılık).
- İsteğe bağlı kabul testi: replay'de sonuç, durum kodu, `Location` ve sipariş kimliğinin ilk yanıtla aynı
  kaldığını kanıtlayan test.
- Satış içi ortak deneme yardımcısının çıkarılması (davranış değişikliği yok; mevcut testlerle kanıt).
- İsteğe bağlı kanıt testi: `SalesStore.IdempotencyKeyHash(k) == IdempotencyHash.ComputeKey(k)` (yalnız test
  projesi iki modüle referans verir; üretim kodunda yeni kenar yok).

## Geri alma/yeniden değerlendirme koşulu

- Production aktivasyonundan önce, yeni deneme tablosu verisi oluşmadan Seçenek A/B'ye dönmek kod ve migration
  düzeyinde geri alınabilir.
- Yeniden değerlendirme tetikleyicileri: `OPEN-021` tek merkezi saklama noktası şart koşarsa; platform Seçenek
  D'yi (ortak sözleşme) yayınlarsa; Satış komutları için grup paylaşımlı (şirket dışı) bir idempotency
  ihtiyacı doğarsa.
- Production'da oluşmuş deneme kayıtları düşürülmez; her konsolidasyon forward-fix ve replay sürekliliği
  kanıtıyla yapılır.

## Test ve kanıt

Mevcut kanıt (Satış, değişmeden kalır): `tests/GoymenERP.Sales.UnitTests/SalesDraftServiceTests*.cs`,
`SalesPersistenceModelTests.cs`, `SalesOrderDraftContractTests.cs` ve gerçek SQL
`tests/GoymenERP.Api.IntegrationTests/SalesDraft*SqlServerRelationalAcceptanceTests.cs`,
`SalesPriceFloorApprovalSqlServerRelationalAcceptanceTests.cs`,
`SalesOrderCancellationSqlServerRelationalAcceptanceTests.cs`.

Her yeni Satış komutu için asgari kabul:

- aynı anahtar + içerik + aktör replay'i ikinci kayıt üretmez, `Idempotency-Replayed` ve ilk korelasyon döner;
- aynı anahtar + farklı içerik veya aktör tek `409 same_key_payload_mismatch`, eski sonucu açıklamaz;
- başka şirketin aynı anahtarı görünmez (yeni deneme);
- terminal ret kaydedilir ve aynen replay edilir; geçici hata kaydedilmez;
- yetkisi kaldırılan aktör replay'den eski sonucu alamaz (çit replay okumasından önce);
- paralel aynı-anahtar yarışında tek kazanan, kaybeden kazananın sonucunu alır (gerçek SQL);
- deneme veya audit insert hatasında iş yazımı geri alınır (gerçek SQL);
- yeni deneme tablosunda UPDATE/DELETE SQL tarafından reddedilir (gerçek SQL);
- yeni `[SqlServerFact]` testleri `tools/relational_inventory/sales.txt` ve tenant guard pin'ine kaydedilir.
