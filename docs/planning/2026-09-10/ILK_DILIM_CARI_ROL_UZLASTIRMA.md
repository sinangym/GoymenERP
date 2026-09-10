# İlk Dilim — Cari Ticari Rol Zorunluluğunu Kaldırma

- Tarih: 10 Eylül 2026.
- Durum: **UYGULAMA PAKETİ ÖNERİSİ / KOD VE TEST TESLİMİ DEĞİL**.
- Yerel inceleme tabanı: `9f5b9bfdef249b9c8e89c6668f66b6aa55c34602`.
- Hazırlık dalı: `codex/erp-delivery-package-20260910`.
- Belge kapsamı: yalnız bu dar dilimin iş/teknik sözleşmesi; kanonik karar, görev bağımlılığı veya kabul statüsü değiştirilmez.
- Uygulama riski: SQL constraint/migration ve sözleşme değişikliği nedeniyle **K3**. Bu sınıflandırma uygulama PR'ı içindir; belge yazılmış olması implementation yetkisi, merge veya production aktivasyonu değildir.

## 1. Kullanıcı sonucu ve değişmezler

Kullanıcı cari oluştururken veya mevcut cariyi yetkili olduğu şirketle ilişkilendirirken “Müşteri / Tedarikçi / Fason / Hizmet” seçmek zorunda olmayacak. Aynı hukuki karşı taraftan mal/hizmet alınabilecek ve ona mal/hizmet satılabilecek; yön ilgili ticari işlemden gelecek. Bu dilim gelecekteki sipariş işlemlerini uygulamaz; Cari tarafında yanlış ön koşulu kaldırır.

**Kaldırılan:** Cari üzerindeki zorunlu ticari sınıflandırma ve bunun iş yapabilme ön şartı olması.

**Korunan:** tek `Party`, grup genelinde değişmez `CariCode`, şirket kapsamlı `CompanyPartyAccount`, kullanıcı permission/role assignment'ları, hukuki taraf türü, aktif/pasif ayrımı, şirket izolasyonu, audit ve tekrar/yarış güvenliği. `PartyRole` ticari işaretleri ile Identity kullanıcı rolleri aynı kavram değildir. Ürün kartındaki üretim/satın alma/satış rolleri bu değişikliğin konusu değildir.

Eski rol verisinin varlığı yeni cari kimliği üretmez; yokluğu erişim yetkisi vermez. “Rol yok” bütün yetkileri açmak, müşteri malını şirket malı saymak veya cari hesabı otomatik aktifleştirmek değildir.

### 1.1 Karar kaynağı ve eski belgelerle uzlaştırma

- 10 Eylül paket talimatının devraldığı son kullanıcı kararı ve v6 rapor §3.1: zorunlu cari ticari rolü yok; kullanıcı yetki modeli korunur. Bu turdaki tarihli karar derlemesi `docs/decisions/SINAN_KARARLARI_2026-09-10.md` ile birlikte okunmalıdır; teknik temsil önerisi işletme kararı gibi kilitlenmez.
- [Cari Core kullanıcı kararları](../../features/CARI_CORE_USER_DECISIONS_v1.md), `CARI-DEC-001` ve `CARI-DEC-003`: tek Party, şirket hesabı, görünürlük ve kod sürekliliği.
- [Karar Sicili](../../decisions/KARAR_SICILI.md), `DEC-CAR-001`: rol için kopya cari oluşturulmaması. “Rol taşıyabilir” cümlesi “en az bir rol şarttır” diye genişletilmez.
- [Eski C-02/link JIT şartnamesi](../../features/CARI_C02_LINK_JIT_SPEC_v1.md) §4.1 ve §7.3'teki zorunlu rol seçimleri bu dilimde uzlaştırılacak eski davranıştır. Aynı belgenin şirket, dirty form, unknown outcome, güvenli link ve audit sınırları geçerliliğini kaybetmez.
- [Cari hesap oluşturma yetki politikası](../../features/CARI_ACCOUNT_CREATE_ROLE_POLICY_v1.md) kullanıcı izinleri hakkındadır; ticari rol zorunluluğunun kaldırılması onu kendiliğinden iptal etmez veya genişletmez. Bu dosyadaki eski PR/uygulanma zamanı beyanı canlı durum yerine kullanılamaz; mevcut endpoint ve registry aşağıda ayrıca doğrulanmıştır.

7 Eylül Issue #66 çalışma yönündeki **ilk yetkili iş komutunda şirket hesabı oluşturulması** ayrı ilişki/permission/transaction sözleşmesidir: [iş yönü kaynak yorumu](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5573067471), [UX kaynak yorumu](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5573793580). Bu exact bağlantılar paket koordinatöründen alınmıştır; bu alt görevde GitHub yeniden sorgulanmamıştır. GET/liste/detay açmak hesap oluşturamaz. O yönün ayrıntılı delegasyon, pasif kayıt, eşzamanlı ilk işlem ve rollback politikası bu dilimle tamamlanmış sayılamaz. Mevcut açık link yolunu korumak bu ayrı kullanıcı yönünü reddetmek değildir; gizli yeni yetki vererek tamamlanmış gibi göstermek de değildir. Uygulamaya geçerken exact kaynak yorum ve owner handoff'u yeniden doğrulanmalıdır.

## 2. Kapsam ve kapsam dışı

### Kapsam

1. Rol seçmeden yeni Party + aktif şirket hesabı oluşturma ve mevcut yetkili Party'ye hedef şirket hesabı bağlama.
2. Domain, iki command service, endpoint doğrulaması, mutation projection, SQL constraint, OpenAPI ve bütün ilgili frontend decoder'larını aynı semantiğe getirme.
3. Cari liste/detay/create/link akışında rolü “eksik zorunlu bilgi” yapmama; rol bulunmayan kayıtların liste, count ve aday görünümünden düşmemesi.
4. Mevcut 1–15 rol değerleri, ilişki kimlikleri, audit snapshot'ları ve durable command sonuçlarının kayıpsız geçişi.
5. Karma istemci sürümü, tekrar, yarış, yetki ve gerçek SQL migration kabulü.

### Kapsam dışı

- Yeni satış/satın alma/üretim/fatura API'si; tam Cari 360, bakiye veya stok davranışı.
- Yeni permission, sistem rolü, otomatik kullanıcı ataması; mevcut role grant genişletme.
- İlk ticari komutta örtük şirket hesabı açma özelliğinin uygulanması; GET ile ilişki yaratma.
- VKN/TCKN/MERSİS/IBAN alanları ve mükerrer kimlik motoru; global Party aktiflik command'i.
- Toplu rol silme, eski audit/imza/belge/idempotency kaydını yeniden yazma; veri saklama süresi seçme.
- Ürün/hizmet master rollerini değiştirme; legacy ERP veri aktarımı; production aktivasyonu.

## 3. Mevcut kod gerçeği ve hedef alanlar

Bu tablo **mevcut taban** ile **önerilen uyarlama**yı ayırır. `0` gösterimi aşağıdaki hafif teknik genişletme önerisidir; yeni işletme rolü değildir.

`None=0`, missing/0 normalleştirmesi ve legacy uyumluluk yolu insan/contract review kapısından geçmeden migration veya etkin davranış olarak kilitlenmez. Kullanıcının onayladığı sonuç zorunlu ticari rolün kaldırılmasıdır; bu teknik gösterimi kendisinin tek tek seçtiği iddia edilmez.

| Alan/ilişki | Mevcut gerçek | Bu dilimin önerisi / korunacak sınır |
|---|---|---|
| `Party.Id`, `Code` | Grup taraf kimliği; server-generated değişmez kod | Aynen korunur; rol yönü için ikinci Party/kod yok |
| `Party.Kind` | `LegalEntity` / `NaturalPerson` | Ticari rol kaldırılırken hukuki tür kaldırılmaz |
| `LegalName`, `TradeName` | Hukuki ad 1–250; ticari ad opsiyonel, en çok 200 | Mevcut trim/validasyon korunur; alan genişletmesi bu dilim değildir |
| `CompanyPartyAccount.Id`, `CompanyId`, `PartyId` | Şirket ilişkisi; `(CompanyId, PartyId)` unique | Aynen korunur; rol başına hesap satırı yok; hedef şirket body'den alınmaz |
| `CompanyPartyAccount.Roles` | NOT NULL int flags; `None=0` tanımlı ama kayıt için reddediliyor; `1/2/4/8` eski bitler | Geçişte `0=belirtilmiş ticari sınıflandırma yok`; eski 1–15 değerler korunur. Bilinmeyen/negatif bitler yine geçersiz. Kullanıcı yeni rol seçmeye mecbur edilmez |
| `IsActive` | Global Party ve şirket hesabının iki ayrı durumu | Rolün olmaması pasiflik değildir. Pasifi aktifleştirme mevcut ayrı yetkili command olarak kalır |
| `Version`, `RowVersion` | Domain logical version + SQL concurrency token | Korunur; create için olmayan sürüm uydurulmaz; status command'in expected version kontrolü gevşemez |
| `ExternalIntegrationCode` | Şirket hesabında; read/mutation güvenli projection'larında özellikle dışarıda | UI'ye veya telemetry'ye yeni sızıntı yaratılmaz; rollerden otomatik dış kod türetilmez |
| Eski rol verisi / audit | Account alanı, create fingerprint ve audit snapshot içinde bulunuyor | Toplu 0'a çekme veya silme yok. Geçmiş değer “bu kullanıcı şu yetkiye sahipti” diye yorumlanmaz |

### 3.1 Somut değişiklik yüzeyleri

Repository köküne göre mevcut dosyalar ve bu tabandaki önemli satırlar:

| Yüzey | Kaynak | Gerçek bağımlılık |
|---|---|---|
| Domain | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Domain/Accounts/CompanyPartyAccount.cs` | `RequireRoles` constructor/`SetRoles` içinde None'ı reddediyor; `HasRole` ayrı ticari sorgu |
| Enum | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Domain/Accounts/PartyRole.cs:3` | Flags: None 0, Customer 1, Supplier 2, Tolling 4, Service 8 |
| SQL model | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyDbContext.cs:104` | `CK_CompanyPartyAccounts_Roles`: `[Roles] BETWEEN 1 AND 15`; unique, FK ve query filter ayrı korunacak |
| Geçmiş migration | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/Migrations/20260828123500_InitialCounterparty.cs:46` | Eski constraint ve int sütun; uygulanan migration dosyası değiştirilmez |
| Command contract | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/Contracts/ICounterpartyCommandService.cs:30` | `CreateCounterparty(..., Roles)` ve `CreateCompanyPartyAccount(Roles)` |
| Create service | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyCommandService.cs:480` | None doğrulaması; Roles request fingerprint ve audit'te de mevcut |
| Link service | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyCompanyAccountLinkService.cs:361` | Ayrı None doğrulaması; kaynak görünürlüğü, hedef permission ve atomiklik korunacak |
| HTTP | `src/backend/GoymenERP.Api/Endpoints/CounterpartyEndpoints.cs:229` ve `:402` | Link ve create zorunlu rol hatası; yalnız UI düzeltmesi yetersiz |
| Mutation güvenli cevabı | `src/backend/GoymenERP.Api/Contracts/CounterpartyMutationView.cs:123` | Projector da None'ı reddediyor; atlanırsa DB yazısı sonrası başarı cevabı bozulabilir |
| Okuma / filtre | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyReadService.cs:413` ve `:877` | Query None = filtre yok. Hesabın Roles=0 olmasıyla “filtre yok” farklı semantikler |
| Link adayı | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyLinkCandidateReadService.cs:341` | Source account roles response'a taşınıyor; yetki yerine kullanılamaz, hedefe otomatik kopyalanmaz |
| OpenAPI | `src/backend/GoymenERP.Api/OpenApi/ErpOpenApiContract.cs:143`; `contracts/openapi/goymen-erp-v1.json:4506` | PartyRole minimum 1; yalnız C# enum değişikliği wire contract'ı düzeltmez |
| Liste decoder | `src/frontend/goymen-erp-web/src/cari/contract.ts:121` | `roles < 1` bütün cevabı malformed yapıyor |
| Detay decoder | `src/frontend/goymen-erp-web/src/cari/detailContract.ts:143` | Aynı minimum; şirket eşleşmesi ve exact account koşulları gevşetilmez |
| Görünüm / sorgu | `src/frontend/goymen-erp-web/src/cari/CariListPage.tsx:48`, `CariDetailPage.tsx:34`, `api.ts:23` | Rol adları, seçim ve `role`/`roleMatch` sorgu yüzeyi |

Yeni `CariRoles` tablosu veya her cari için bütün rolleri seçen seed önerilmez. Mevcut `Roles=15` değerini rol yokmuş gibi üretmek, gerçekte seçilmemiş dört sınıflandırma uydurur.

## 4. Mevcut endpoint ve command sözleşmeleri

Yeni endpoint/route icat edilmez. Aşağıdakiler tabanda gerçekten vardır; mevcut ad/permission korunarak contract revizyonu yapılır.

| HTTP yol | Mevcut command / sonuç | Dilim etkisi |
|---|---|---|
| `GET /api/v1/counterparties` | Yetkili, cursor-paged liste; `CounterpartyPage` | Rolü olmayan kayıt, filtresiz yetkili liste ve count içinde görünür; read yazmaz |
| `GET /api/v1/counterparties/{partyId}` | `CounterpartyDetailView` | Geçerli rol-yok kaydı malformed olmaz; görünürlük/şirket eşleşmesi aynı kalır |
| `GET /api/v1/counterparties/company-account-link-candidates` | Hedef şirket ve kaynak görünürlüğü denetlenen adaylar | Roles=0 adayın yetkisini düşürmez/yükseltmez; query yeni hesap yaratmaz |
| `POST /api/v1/counterparties` | `CreateCounterparty`; 201 `CounterpartyMutationView` | `kind`, `legalName`, opsiyonel `tradeName`; ticari rol seçmeden gönderim desteklenir |
| `POST /api/v1/counterparties/{partyId}/company-account` | `CreateCompanyPartyAccount`; 201 mutation view; 404/409 | Rol seçmeden link; aynı Party, hedefte bir hesap; kullanıcı gerekçesi eklenmez |
| `PUT /api/v1/counterparties/{partyId}/company-account/active-state` | `SetCompanyPartyAccountActiveState(IsActive, ExpectedVersion, Reason)`; 204 | Davranış değişmez; mevcut pozitif sürüm ve 1–1000 karakter gerekçe koşulları korunur |

### 4.1 Hafif wire genişletme önerisi

- Create/link request'lerinde `roles` alanını opsiyonel hale getirmek; eksik alanı veya açık `0` değerini aynı normalleştirilmiş “ticari sınıflandırma yok” anlamında değerlendirmek. Link için JSON nesnesi `{}` geçerli olabilir; boş/bozuk HTTP body ile karıştırılmaz. `null`, string veya bilinmeyen bit maskesi kendi declared contract'ına göre reddedilir; sessiz rol tahmini yok.
- Geçiş response'larında `roles` numeric alanını hemen silmemek: yeni kayıtta `0`, eski kayıtta eski 1–15. Bu yalnız taşınan tarihsel/uyumluluk bilgisidir; alış/satış yetki veya uygunluk kuralı değildir.
- Eski role query parametrelerinin geçişte korunduğu açıkça belgelenir; yalnız kayıtlı legacy işaretleri filtreler, “bugün satış yapılabilecek bütün cariler” anlamına gelmez. Normal UI bu filtreleri varsayılan iş engeli olarak kullanmaz. Aktif eski bir role filtresi temizlenmeden bütün carileri gösteriyormuş gibi sunulmaz; filtreyi temizleme görünür olmalıdır. Tam API kaldırılması bu dilimin sonuna gizlenmez, ayrı contract daraltmasıdır.
- `PartyRole` minimum, request required alanları, response decoder'ları ve OpenAPI testleri birlikte değişir. `roles=0` için response sözleşmesinin genişlemesi, eski katı istemciler bakımından uyumsuzdur; “sadece opsiyonel alan, hiç risk yok” denmez.
- Liste DTO'sunda `Version` halen number/int64, detay ve mutation wire DTO'larında decimal string'dir. Bu dilim bunları birbirine kör kopyalamaz veya JavaScript `Number` dönüşümüyle kaybettirmez. Ayrı Int64 temsil işi ortaya çıkarsa kendi contract kapsamıyla koordine edilir; rol değişikliğinin içine sessizce katılmaz.

### 4.2 Yetki, hata, concurrency ve audit

- Registry: `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs:13`. Create: `counterparty.parties.create`; link: `counterparty.accounts.create` **ve** `counterparty.parties.read`; status: `counterparty.accounts.status.update`. Company-wide scope ve endpoint/service reauthorization korunur. Yeni Identity rolü/grant yok.
- Yalnız çözülmüş aktif `CompanyContext` hedefi belirler. Başka şirketin Party/account varlığı, sayısı, rolü veya dış kodu response/hata/cursor/telemetry ile sızmaz.
- `validation_failed`, `command_metadata_invalid`, `not_found`, `already_linked`, `version_conflict`, `idempotency_conflict`, platform `authentication_required`/`forbidden` stable kodları kullanılır. Rol eksikliği artık zorunluluk hatası değildir; unknown bits ve hukuki ad/tür hataları yine geçersizdir. HTTP durumu tek başına başarı sayılmaz.
- Create/link yeni kayıt command'leri için `ExpectedVersion` icat edilmez; mevcut unique/race/fresh authorization kapıları korunur. Active-state command'i mevcut hesap version'ına göre çalışır. None kabulü status/no-op version artışını değiştirmez.
- Logical attempt, payload/actor/company ve mevcut `Idempotency-Key` bağı korunur. Aynı attempt retry tek Party/account/business audit üretir. Yeni key üretip belirsiz sonucu tekrar yazma yok. Farklı payload/actor aynı key ile conflict kalır.
- Create/link fingerprint'leri `Roles` içeriyor; mevcut `CommandExecutionSchemaVersion=1` ve durable response'lar vardır. Eski fingerprint/response'ları topluca yeniden hash'leme veya silme yok. Yeni normalleştirmede missing/0 eşdeğerliğinin eski 1–15 request hash'lerini değiştirmediği golden/replay testle gösterilir. Schema sürümü gerekiyorsa eski sonuçları güvenli okuyan uyumluluk yolu önce tasarlanır; unknown schema default-deny davranışı kaldırılmaz.
- Mevcut audit olayları `counterparty.party.created.v1` ve `counterparty.company-account.created.v1` korunur. Kullanıcı/şirket/permission/correlation/source ve snapshot gerçek durumu taşır. Rol kaldırma yeni stok, fatura, açık kalem veya para olayı doğurmaz. Normal okuma business audit yazısı değildir; güvenlik telemetry'si ayrı kavramdır.

## 5. SQL geçişi ve geri dönüş

Bu bir legacy ERP veri aktarımı değildir; GöymenERP'nin kendi şema/uyumluluk migration'ıdır. Veri aktarımı kapsam dışı diye bu gereklilik atlanamaz.

1. **Ön sayım:** seçili test/upgrade veri kümesinde account sayısı, `(CompanyId, PartyId)` benzersizliği, mevcut rol dağılımı, FK'ler ve audit/idempotency örneklerinin değişmezliği kaydedilir. Canlı veriyi bu belge hazırlama turunda okumak/yazmak yetkisi yoktur.
2. **Genişletme:** uygulanmış initial migration'ı değiştirmeden yeni Counterparty migration önerilir; `CK_CompanyPartyAccounts_Roles` aralığı `0..15` olacak şekilde genişletilir. Runtime model ve `CounterpartyDbContextModelSnapshot.cs` birlikte uyumlu olur. NOT NULL, FK, unique, CompanyId guard, version/rowversion korunur.
3. **Geçmiş:** mevcut 1–15 değerler dönüştürülmez; Party/account kimliği, Code, timestamps, audit JSON ve eski command sonuçları silinmez/yeniden yazılmaz. Ayrı legacy arşiv tablosu bu dar hedef için zorunlu tutulmaz. Daha sonra sütun kaldırılacaksa tarihsel anlamın korunması ayrı contract/migration kararıdır.
4. **Başarısız migration:** kısmi DDL/uygulama yayılımına karşı transaction ve yeniden çalışma kanıtı; mevcut iki migration'dan upgrade ve sıfır DB kurulumu ayrı test edilir. Testteki migration sayısını yalnız artırmak yeterli kanıt değildir.
5. **Geri dönüş sınırı:** Roles=0 kaydı oluşmadan eski `1..15` constraint'ine dönüş ön kontrolle mümkün olabilir. Roles=0 kaydı oluştuktan sonra eski uygulama/constraint'e kör rollback güvenli değildir. Kayıt silme, bütününe Customer atama veya 15'e dönüştürme yasaktır. Yeni yazıları durdurup genişletilmiş şemayı ve uyumlu okuyucuları koruyan forward-fix önceliklidir; veri kayıplı geri dönüş “rollback hazır” diye sunulamaz.
6. **Contract daraltma yok:** Roles alanının/sorgularının fiziksel kaldırılması, eski client desteğini sonlandırma ve retention kararı bu dilime eklenmez.

## 6. Kademeli yayın ve mevcut PR'larla koordinasyon

[PR #227](https://github.com/sinangym/GoymenERP/pull/227) ve [PR #228](https://github.com/sinangym/GoymenERP/pull/228), önceki inceleme kesitinde Cari create/link ve birleşik arayüz adayları olarak görülmüştür. Bu belge onların güncel head'ini, merge durumunu veya testlerini doğrulamış saymaz. İkisinin de bütün kapsamı eskimiş değildir; yalnız zorunlu ticari rol parçaları güncel kararla çelişebilir.

Uygulama başlamadan coordinator:

1. Güncel main SHA, iki PR'ın exact head/base/owner'ı, açık review'ları, dokunduğu dosyalar ve handoff kapsamını okur.
2. Eski rol UI/decoder/contract parçalarını ayrı işaretler; create/link, güvenli projection, dirty form, retry, şirket fence ve a11y emeğini topluca silmez. Açık dala izinsiz commit veya başka yazarın PR'ını devralma yok.
3. Aynı endpoint/model/migration için tek backend writer; aynı create/link/router/decoder için tek frontend writer belirler. Gerekirse önce owner kendi adayını uzlaştırır; bağımlı implementation güncel kabul edilmiş tabandan başlar.
4. Kod paketleri aynı dikey dilimin parçalarıdır: Codex backend/SQL/OpenAPI; Claude frontend/client/browser; ChatGPT iş/kabul hakemliği; coordinator bağımsız exact-head review/merge.

Önerilen yayın sırası:

- Önce geçiş contract'ı ve `0`/eski rolleri birlikte okuyabilen bütün etkin consumer'lar; yeni rol-yok yazıları kapalı.
- Sonra genişletilmiş SQL + uyumlu domain/service/projector; eski ve yeni request replay testi. Bu aşamada eski istemciye rol-yok response düşmeyeceği gösterilir.
- Ardından create/link UI rol zorunluluğu kaldırılır ve tek bir kabul edilmiş sürüm bileşimiyle yeni yazılar açılır. Açık eski browser sekmeleri için upgrade/reload/engelleme davranışı açıkça test edilir; sadece client cache temizlenmiş varsayılmaz.
- Normal role-dayalı filtrelerin yanlış kapsama yol açmadığı doğrulanır. Yeni alan bütün otomatik/ticari consumer'lar için yetki olmaktan çıkar; mevcut olmayan sipariş modüllerinin tamamlandığı iddia edilmez.
- Güncel capability sağlayıcısındaki `ImplementationAvailable=false` kayıtları, bu belge sayesinde `true` yapılmaz. Hedef create/link/list/detail bileşimi merge, gerçek SQL, browser, şirket/permission ve fresh review kapıları sonrası ayrı kontrollü aktivasyon ister. Bu kontrolün mevcut bir feature-flag implementation'ı olduğu varsayılmaz; rollout mekanizması ilgili owner tarafından somutlaştırılır.

Kullanıcının 7 Eylül örtük hesap yönünün detay kapısı henüz eksikse yalnız o davranış bekler; zorunlu ticari rolü kaldıran bu dar iş durmaz. Bunun karşılığında “her kullanıcı, her cariyi herhangi bir şirkete bağlayabilir” gibi bir politika seçilmez.

## 7. Kullanıcı arayüzü durumları

- Yeni Cari/link formunda zorunlu ticari rol seçimi ve bunun validasyon metni kalkar; kullanıcıdan yerine aynı anlama gelen yeni sınıf/checkbox istenmez. Gerekli hukuki ad/tür ve salt okunur aktif şirket bağlamı kalır.
- Liste/detayda rol-yok normal kayıttır: “Eksik”, “Yetkisiz”, “Pasif” veya engelleyici uyarı değildir. Legacy işaret gösterilecekse geçmiş sınıflandırma olduğu açık olmalı; ana iş yönü gibi kullanılmamalıdır.
- Loading/empty/no-results ayrı; filtresiz liste boşluğu ile eski rol filtresinin sonuç vermemesi ayrıdır. Link adayının boşluğu yeni Party açmak veya görünmeyen Party'yi tahmin etmek için authority değildir.
- `401` oturum kapanışı, `403` yetkisizlik, `404` güvenli bulunamama, `409` already-linked/version/idempotency çatışmaları birbirinden ayrılır. Başka şirketin varlığı için açıklama üretme yok.
- Dirty form ve şirket değişimi aynı sınırı korur; değiştirilmiş payload eski idempotency attempt'iyle gönderilmez. Unknown outcome sırasında mevcut submitted snapshot korunur; aynı key retry ve fresh yetki kontrolü.
- Decoder bilinmeyen bitleri/yanlış şirketi yine reddeder; yalnız minimumun değişmesi bütün cevabı gevşetmek değildir. Mutation'ın tam başarısından önce toast/navigation yok.
- Mevcut C-01 continuation davranışı korunur: yalnız kabul edilmiş offline/network/500/503 hallerinde doğrulanmış satırlar korunur; 401/403/malformed eski verinin arkasına saklanmaz. Klavye, odak, ekran okuyucu ve metinli hata kabulü zorunludur.

## 8. On önerilen kabul senaryosu — henüz çalıştırılmadı

Aşağıdaki `CR-*` işaretleri yalnız bu belge içindeki senaryolardır; kanonik AT/DEC/OPEN kaydı değildir. Her satır implementation'da önce eski davranışın kırmızı örneğini, sonra gerçek kanıtı ister.

| Yerel no | Kurulum / eylem | Beklenen kayıt ve ekran | Kanıt |
|---|---|---|---|
| CR-01 | Yetkili A kullanıcısı geçerli kind/ad ile roles alanı göndermeden Cari oluşturur; ayrıca açık 0 varyantı | 201; bir Party, bir A hesabı, gerçek role-yok değeri; kopya kod/rol satırı yok; mutation ve C-03 okunabilir | Domain + HTTP + gerçek SQL + browser |
| CR-02 | Eski 1, 3 ve 15 değerli hesaplar üzerinden yeni migration uygulanır; yeni 0 hesabı eklenir | Eski kimlik/rol/audit değişmez; 0 geçerli; -1 ve 16 reddedilir; FK/unique/CompanyId guard bozulmaz | Fresh/upgrade SQL + model/snapshot |
| CR-03 | A'da görülebilen mevcut Party, yetkili B context'inde roles seçmeden bağlanır | Yeni Party yok; tek B account ve tek created audit; A hesabı ve legacy rolü değişmez; güvenli B projection | Link HTTP/SQL + browser |
| CR-04 | Create/link/status için yetkisiz veya yanlış şirketli kullanıcı; ayrıca foreign/hidden PartyId | Mevcut 403/404 non-disclosure; yazı/grant/reaktivasyon yok; doğrulanmamış global Party varlığı sızmaz | Authorization/tenant SQL + negatif browser |
| CR-05 | Aynı actor/company/payload/key tekrar ve çift tıklama; ayrı olarak farklı payload/actor aynı key | Bir business etkisi; replay; farklı logical payload/actor conflict; timeout'ta yeni key ile kör tekrar yok | SQL concurrency/failure + browser |
| CR-06 | Farklı key'lerle aynı Party/B link yarışı; audit veya idempotency insert'i hata verir | Tek account/created audit; kaybedende existing stable conflict; yarım business kayıt yok | Gerçek SQL yarış/rollback |
| CR-07 | Eski 1–15 request'in upgrade öncesi durable sonucu yeni sürümde replay edilir; ayrıca role-yok sonucu | Eski hash/snapshot korunur; ikinci business etkisi yok; actor/şirket yeniden yetkilendirilir; unknown schema güvenli reddedilir | Golden fingerprint + durable SQL replay |
| CR-08 | Roles=0 cari filtresiz liste/detay/adayda; legacy filtre ve pagination/şirket geçişi | Doğru yetkili kayıt/count; gizli rol filtresiyle kayıp yok; 0 malformed değil, unknown bits/foreign account malformed; okumada account açılmaz | Read SQL + TS decoder + stale/continuation |
| CR-09 | Role-yok pasif hesap; active-state stale/no-op command; yalnız eski role sahip kullanıcı | Rol yok pasifliği aşmaz; mevcut expected version/no-op ilerleme ve gerekçe korunur; ticari rol permission vermez | Domain + SQL conflict + permission |
| CR-10 | Eski browser/client sürümü açıkken uyumlu deployment; oluşturma sonucu gelirken company/logout değişir | Rol-yok yazılar yalnız consumer uyumu sonrası; eski context cevabı gösterilmez; traceable rollback/forward-fix provası; normal kullanıcıya zorunlu rol kalmaz | Karma-sürüm browser + deployment provası |

Gelecekte aynı carinin alış ve satış siparişinde kullanılma testi sipariş diliminde eklenecektir. Bu dilimde yalnız Cari ön koşulunun kaldırıldığı kanıtlanabilir; var olmayan sipariş endpoint'iyle sahte E2E yazılmaz.

### 8.1 Yeniden kullanılacak gerçek test kaynakları

- `tests/GoymenERP.Counterparty.UnitTests/CompanyPartyAccountTests.cs`: `Account_requires_at_least_one_known_role` içindeki None reddi değişecek; unknown bit reddi ayrı korunacak. Kimlik ve no-op version testleri korunur.
- `tests/GoymenERP.Counterparty.UnitTests/CounterpartyPersistenceModelTests.cs`: schema/FK/unique, physical-delete guard ve model-snapshot parity.
- `tests/GoymenERP.Api.IntegrationTests/CounterpartySqlServerRelationalAcceptanceTests.cs`: gerçek migration, tenant-safe read ve Int64 örnekleri. Sabit eski migration sayısı yeni zincirin doğruluğuyla birlikte güncellenir; kör beklenti değişimi yok.
- `tests/GoymenERP.Api.IntegrationTests/CounterpartyMutationViewProjectorTests.cs`: normalized request, exact target account, rol-yok ve unknown-role güvenliği.
- `tests/GoymenERP.Api.IntegrationTests/CounterpartyWriteSqlServerRelationalAcceptanceTests.cs` ve `CounterpartyWriteSafetySqlServerRelationalAcceptanceTests.cs`: create/status, audit/idempotency atomikliği ve concurrent yazma.
- `tests/GoymenERP.Api.IntegrationTests/CounterpartyCompanyLinkSqlServerRelationalAcceptanceTests.cs`, `CounterpartyCompanyLinkReviewGapSqlServerRelationalAcceptanceTests.cs`, `CounterpartyCompanyLinkSecurityBoundarySqlServerRelationalAcceptanceTests.cs`: source visibility, hedef permission, replay, race ve failure.
- `tests/GoymenERP.Api.IntegrationTests/CounterpartyLinkCandidateSqlServerRelationalAcceptanceTests.cs`, `CounterpartyReadPageSqlServerRelationalAcceptanceTests.cs`: cursor, company-wide yetki, bounded read-only sorgu ve filtre.
- `tests/GoymenERP.Api.IntegrationTests/OpenApiContractGenerationTests.cs`: generated/baseline ve PartyRole contract'ı.
- `src/frontend/goymen-erp-web/src/cari/contract.test.ts`, `detailContract.test.ts`, `CariListPage.test.tsx`, `CariDetailPage.test.tsx`, `authBoundaryIntegration.test.tsx`, `staleResponseFence.test.tsx`: minimum aralık, rol görünümü ve mevcut fence regressions. PR #227/#228'in create/link test dosyaları exact head görülmeden bu listede icat edilmez.

## 9. Gerçek repository komutları ve kanıt toplama

**Bu paket hazırlanırken aşağıdaki build/test/migration komutları çalıştırılmadı.** Komutlar mevcut solution, package scripts, CI ve doğrulayıcılardan çıkarılmıştır. Implementation sahibi gerekli SDK/Node/Python ve test SQL ortamını doğrular; production connection kullanılmaz. PowerShell örnekleri repository kökünden çalıştırılır.

```powershell
python -I -S -B tools/validate_dotnet_bootstrap_integrity.py
dotnet tool restore --tool-manifest dotnet-tools.json --configfile NuGet.Config
dotnet restore GoymenERP.slnx -noAutoResponse --configfile NuGet.Config
dotnet build GoymenERP.slnx -noAutoResponse --configuration Release --no-restore
dotnet test tests/GoymenERP.Counterparty.UnitTests/GoymenERP.Counterparty.UnitTests.csproj -noAutoResponse --configuration Release --no-build
dotnet test tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj -noAutoResponse --configuration Release --no-build --filter "FullyQualifiedName~CounterpartyMutationViewProjectorTests"
```

Gerçek SQL runner `GOYMEN_ERP_TEST_SQLSERVER_CONNECTION` değişkenini güvenli test ortamından sağlamalıdır; gerçek secret komuta/belgeye yazılmaz. Bağlantı yokken SQL testinin skip edilmesi başarı değildir. Mevcut strict runner ve contract kapıları:

```powershell
python -I -S -B tools/verify_rcsi_relational_acceptance.py --broad
python -I -S -B tools/verify_rcsi_relational_acceptance.py --rcsi
dotnet test tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj -noAutoResponse --configuration Release --no-restore --filter "FullyQualifiedName~OpenApiContractGenerationTests" --logger "console;verbosity=normal"
python -I -S -B tools/validate_openapi_contract.py --baseline contracts/openapi/goymen-erp-v1.json --generated artifacts/openapi.json
```

Yeni SQL migration için **script üretim** komutu; DB'ye uygulama emri değildir. Yeni migration adı/timestamp'i bu belgede uydurulmaz:

```powershell
dotnet tool run dotnet-ef migrations script --idempotent --no-build --configuration Release --project src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/GoymenERP.Modules.Counterparty.Infrastructure.csproj --startup-project src/backend/GoymenERP.Api/GoymenERP.Api.csproj --context CounterpartyDbContext --output artifacts/counterparty-role-migration.sql
```

Frontend mevcut scripts:

```powershell
npm --prefix src/frontend/goymen-erp-web ci
npm --prefix src/frontend/goymen-erp-web test
npm --prefix src/frontend/goymen-erp-web run lint
npm --prefix src/frontend/goymen-erp-web run build
git diff --check
```

`package.json` içinde ayrı E2E script'i yoktur; `npm run e2e` varmış gibi verilmez. Browser kabulü güncel owner'ın gerçekten kurulu test altyapısı ve exact-head handoff'u üzerinden ayrıca gösterilir. Generated-client tüketimi güncel iki PR ve platform generator yönüyle doğrulanır; type dosyasını elle üretip generated ilan etmek yoktur.

Kaynaklar: [.github/workflows/ci.yml](../../../.github/workflows/ci.yml) backend/relational/openapi işleri; [frontend package.json](../../../src/frontend/goymen-erp-web/package.json); [strict SQL runner](../../../tools/verify_rcsi_relational_acceptance.py); [OpenAPI doğrulayıcısı](../../../tools/validate_openapi_contract.py).

## 10. Teslim kapısı

Dar dilim ancak zorunlu ticari rol create/link/read/UI/SQL zincirinde kalktığında; legacy veri ve replay korunduğunda; permission/CompanyId/unique/concurrency sınırları değişmediğinde; gerçek SQL ve browser örnekleri geçtiğinde kabul adayına dönüşür. Test sayısı, skip sayısı, exact head/base, migration upgrade/forward-fix kanıtı ve açık P0/P1 sonuçları raporlanır.

K3 implementation için mevcut governance'ın iki bağımsız exact-head review ve ayrı coordinator merge kapısı korunur. Belge, PR #227/#228'e yazma, yeni grant, kanonik task/karar kapatma veya production aktivasyonu yetkisi vermez. Yalnız bu dilim bitti diye **Cari 360**, **ilk işte örtük hesap**, **alış/satış siparişleri** veya **ERP tam kullanıma hazır** denmez.
