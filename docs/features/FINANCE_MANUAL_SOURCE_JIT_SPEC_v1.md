# W4-FINANS — manuel, dağıtımsız tahsilat kaynağı: ilk JIT altı

Owner: **CHATGPT-BUSINESS** · 13 Eylül 2026 · v1 · **İŞ/KABUL TASARIMI; para yazma ve gerçek kabul HAZIR DEĞİL / NOT_RUN**.
Kaynak kesiti: `main@f6fa07d026403649d6c815b84fd850e7ceeaf092`. Uygulama: yeni Finans Backend + Finans Claude; oturum/dosya ataması aynı CODEX-ORCHESTRATOR'dadır. Para, rol, onay, kur veya production politikası bu belgeyle seçilmez.

## 1. Bağımsızlık hükmü ve ilk kullanıcı işi

[DEC-FIN-001…016][decisions], [Master §7.10–7.11/9.2/10][master] ve [OPEN-009/010/012/016][open] esas alınır. Son kullanıcı kararı üstündür; #229 insan kapısı korunur. [Mevcut W4-FINANS → W2-ACIKKALEM → W2-IRSALIYE + W1-SETTINGS][backlog] bağı tam dağıtım/ekstre modülü için kalır; DONE yapılmaz veya paralel backlog kurulmaz.

**İş anlamında faturasız/dağıtımsız manuel kaynak mümkündür:** kaynağın varlığı, hangi faturaya uygulanacağından ayrıdır. İlk aday **gerçekleşmiş gelen banka tahsilatını A şirketinin gerçek banka hesabına ve mevcut Cari hesabına manuel kaydet → liste → kaynak detayı → dağıtılmamış tutarı gör**. Bu yeni banka transferi talimatı değildir. Yalnız giriş, tek para birimi, dağıtımsız kaynak; ödeme, virman, POS/çek/senet/DBS, kur çevrimi ve mahsup ilk alt değildir.

**Bugün yürütülebilir para akışı değildir:** main'de Finance, açık-kalem ve Settings/Policy runtime yoktur. İlk kaynak dahi Finance para olayı/hesap projeksiyonu, currency-hassasiyet, dönem, şirket yetkisi ve gereken onay sınırı olmadan kesinleştirilemez. Bu minimumlar kurulurken hazırlık formu/DTO/model ve açıkça taslak kayıt ilerleyebilir; taslak para kaynağı, banka bakiyesi veya gerçekleşmiş tahsilat diye gösterilmez. Açık-kalem/fatura motorunun tamamını beklemek ise dağıtımsız kaynak için zorunlu değildir.

## 2. Alanlar, kaynak sahipliği ve tarih/para sözleşmesi

Alan adları mantıksal adaydır; route/DTO/limit ve success/error şekli Finans Backend'in tek contract'ında pinlenecek. Sayısal profil gerçek kaynaksız sabitlenmez.

| Alan | Zorunluluk / boş değer / doğrulama |
|---|---|
| Company/actor/recordedAtUtc | Doğrulanmış şirket context'i ve server actor/UTC kayıt anı. Body ile başka şirket/actor atama yok. Her kaynak tek şirkette; grup görünümü yalnız okumadır. |
| id / kaynak no / version | Sunucu tarafından; numara banka referansı veya Cari kodu değildir. Version kayıpsız string; create'te yok, öneri güncelleme/ters komutta taze version gerekir. |
| source kind / yön | İlk nihai işlem yalnız **manuel gerçekleşmiş banka tahsilatı / giriş**. Taslak, beklenen ödeme, DBS kabulü veya ödeme talimatı tahsilata dönüşmez. İşaretli negatif kullanıcı tutarıyla gizli ödeme/ters işlem yok. |
| treasuryAccountId | A şirketine ait, yetkili ve bu para biriminde kullanılabilir gerçek banka hesabı. Cari'nin IBAN satırı Göymen banka hesabı değildir. Finance sahibinin minimal master/read kaynağı; keyfî UUID veya company default hesabı yok. |
| partyId + companyAccountId | Aynı hukuki taraf ve A hesabı; roles0 geçerli. Görünen Party A hesabını veya Finans yazma yetkisini garanti etmez. Mevcut Cari owner read/uygunluk kontrolü kullanılır; otomatik B/link/reaktivasyon yok. Pasif/eski borç istisnası bu ilk pozitif örneğe eklenmez. |
| amount + currencyId | Pozitif, sonlu, kayıpsız decimal metni + gerçek currency kimliği. Hesap/işlem parası aynı. Binlik ayırıcı, bilimsel gösterim veya floating-point ile hesap yok. `null`/boş/0 gerçekleşmiş tutar değildir. |
| para hassasiyet profili | Currency/işlem profileId+version, kabul edilmiş scale ve varsa rounding kaynağı. DB precision bunun teknik taşıyıcısı; finansal ölçek değildir. Fazla basamak sessiz yuvarlanmaz; profil yoksa kesin para yazması kapalı. TRY için bile otomatik2 basamak varsayımı yok. |
| occurredOn / valueOn / dueOn / reconciledAt | Fiilî banka işlem tarihi zorunlu date-only. Valör biliniyorsa ayrı date-only, değilse null. Vade bu gerçekleşmiş kaydın yerine geçmez. Banka mutabakat zamanı gerçek mutabakat yoksa null; elle kaydetmek mutabık statüsü yaratmaz. |
| period / yerel karşılık | Dönem ve işlem tarihine uygunluk owner tarafından çözülür, kapalı dönem değiştirilmez. İlk same-currency yol kur istemez. Yerel para farklıysa FX/OPEN-012 kapısı olmadan yerel karşılık üretilmez; sınırsız geçmiş tarih izni verilmez. |
| bank reference / kanıt / açıklama | Gerçekten varsa kaynak banka referansı/kanıt bağlantısı; yoksa boş ve doğrulanmamış. Banka satırı integration kaydı uydurulmaz. Başka şirketin belge bağlantısı reddedilir. Ek yükleme gerekiyorsa Documents owner; bu ilk form upload altyapısı kurmaz. |
| approval / reversal metadata | Server policy sonucu; client approved/posted yazamaz. Hazırlayan, varsa gerekli onay ve gerekçe/audit ayrı; ters kaynak bağı server tarafından orijinale bağlanır. |

Eksik patch alanı değişmez; null yalnız opsiyonel alanı temizler. Kesin kaydın tutar/para/hedefi serbest edit edilmez; hatalı gerçekleşme kaydı kaynak-bağlı düzeltme/ters işlemdir. Aynı tutar+tarih iki gerçek tahsilat olabilir; otomatik birleştirme anahtarı değildir. Aynı girişim idempotency ile korunur; kaynak referansı olan mükerrerler owner'ın izlenebilir eşlemesiyle ayrılır.

## 3. Bir para olayı; dağıtım, nakit ve kapanış ayrımı

Kaynak hazırlığı → kullanıcı etki önizlemesi → güncel yetki/politika ve dönem kontrolü → varsa gerekli onay → **bir kez kesin kaynak kaydı** → fresh liste/detay. Durum tokenları Backend'de pinlenir. Hazırlama veya onaya gönderme para hareketi değildir. [OPEN-009'un ilk aşamada hard-gate yok kararından][open] sınırsız kullanıcı yazması veya olmayan politikanın onaylı olduğu çıkarılmaz; yeni tutar eşiği/ikinci-onay tablosu burada icat edilmez.

Kesin yazmada Finance kaynak/para olayı ve kendi banka/Cari para projeksiyonu, audit ve durable sonuç tutarlı atomik sınırdadır. Aynı olayı hem kaynak hem yeni bağımsız banka hareketi diye iki kez sayma yok. Cari modülü parasal defter tutuyormuş gibi doğrudan tabloya yazılmaz; Finance sahibinin tek olayı aynı kimlikle gösterilir. Muhasebe fişi ayrı türevdir, bu ilk teslim resmî fiş üretmez. Durable cross-module yayın gerektiğinde mevcut Outbox owner bağı gerekir; var olmayan worker/outbox PASS sayılmaz.

`Dağıtılmamış = geçerli kaynak tutarı − aktif dağıtımlar` aynı şirket/aynı para biriminde hesaplanır. İlk pozitif kaynakta allocation ilişkisi gerçekten yoktur: dağıtılan0, dağıtılmamış=tutar. Dağıtım servisi erişilemiyorsa 0 varsayılmaz. Fatura/açık kalem seçilmeden avans/dağıtımsız kaynak kalabilir; fatura borcu kapanmaz. İleride dağıtım iptali parayı bankadan geri çıkarmaz; sadece aktif eşlemeyi kaldırır. Siparişin ticari kapanışı ve nakit gerçekleşmesi ayrı kalır. AT-058 bütünü bu kaynak-only altla kapanmaz.

**Ters düzeltme sınırı:** henüz dağıtılmamış hatalı kaydın tam kaynak-bağlı tersini hazırlama, gerekli yetki/politika/dönemle bir kez uygulama. Orijinal tutar/kimlik/audit silinmez; ters etki aynı para/şirket/hesaba bağlıdır, aynı-key tekrar ikinci ters yaratmaz. Dağıtımlı kaynak bu dar ters akışın dışında açık kalır; dağıtımları sessiz iptal etme yok. Bu düzeltme gerçek bankadan müşteriye para gönderme değildir; fiilî para iadesi ayrı ödeme olayıdır. Politika/ters komut runtime'ı gelmeden UI uygulandı demez.

## 4. Mantıksal contract ve ekran

Backend kaynak DraftSave/Record, authorized List/Detail ve aynı kaynağa bağlı Reverse davranışını bu sınırla ayırır; route/method/başarı status'ü mevcut API gibi ilan edilmez. Örnek sadece sözleşme niyeti, gerçek ID/politika ataması değil:

```json
{"partyId":"<yetkili-Party>","companyAccountId":"<A-Cari-hesabı>","treasuryAccountId":"<A-banka-hesabı>","currencyId":"<hesap-parası>","amount":"1000","occurredOn":"2026-09-13","valueOn":null,"bankReference":"örnek-tahsilat-belgesi"}
```

Tahsilat/Ödeme alanı ve mevcut kaynak detay kalıbı kullanılır; yeni 13-ekran tasarımı yok. Listede şirket, taraf, banka hesabı, işlem tarihi, kaynak tutarı/para ve Draft/Kesin/Ters bilgisi açık. Yetkisiz tutar DOM/JSON/log içinde saklanarak gizlenmez. Giriş yetkisiyle fiyat-tutar okuma/ters/onay yetkileri aynı kabul edilmez. Eksik owner endpoint varken dashboard/ekstre/borç0 yok.

Loading, geçerli boş sonuç, forbidden, schema/profil eksikliği ve bağlantı hatası ayrı; kayıt hatası taslağı korur. Yeni gönderim yalnız güncel context ve kullanıcının açık eylemiyle. Unknown'da aynı key/company/account/actor/payload tutulur; sonraki500/timeout key'i sıfırlamaz. Aynı key başka payload çatışma, otomatik yeni key yok. Güvenli başarılı gövde veya boş204+fresh GET ayrımı contract'a göre; onaysız/bozuk2xx para kaydı makbuzu değildir. Stale version'da overwrite yok; yeniden oku/incele. Başarı sonrası yalnız GET bozulduysa ikinci para komutu gönderilmez. Dirty şirket değişimi, geç yanıt ve klavye odağı mevcut shell kalıbında; gerçek IdP kabulü fixture'dan çıkarılmaz.

## 5. En küçük bağımlılıklar ve tek gerçek açık soru

| Bağ / durumu | Sahip ve gerekli somut çıktı |
|---|---|
| Finance runtime **YOK** | Yeni Finans Backend: kaynak/hesap/ters/projection contract'ı, tek-olay ve audit/idempotency transaction/migration sınırı. Finans Claude aynı field/örnek/error kaynağını tüketir. |
| Banka hesabı + currency/hassasiyet + dönem/policy **eksik** | Finance + mevcut Settings/Identity/dönem sahibi, coordinator tek-yazar tahsisiyle. En az bir kabul edilmiş profile ve gerçek yetkili hesap okuması gerekir; tüm Settings ekranları değil. Hassasiyet/yuvarlama kaynağı bulunamazsa yalnız kesin hesap/yazma kapısı açık kalır. |
| Cari seçimi/ilişki | Mevcut Cari Backend [yetkili read][party] + Party/account ayrımı. Yeni Cari master/otomatik link yok. Pasif borç kapama gibi ek politika bu ilk senaryoya sızmaz. |
| Read/draft/record/reverse/amount/approve izinleri **henüz registry'de yok** | Finans Backend + mevcut Identity sahibi; şirket-kapsamlı eylem sözleşmesi, gerçek source yoksa default-deny. Product global grant veya şirket adminliği Finans write izni değildir. Bootstrap/sentetik fixture gerçek kişi grant'i değil. |
| Açık kalem/fatura/dağıtım **YOK** | W2-ACIKKALEM ve W2-IRSALIYE mevcut sahipleri. Bu ilk kaynağa fatura seçimi zorunlu değil; dağıtım/ekstre/AT-058 tamamlanması bu owner kayıtlarını bekler. |
| Banka/e-belge/muhasebe entegrasyonu | OPEN-016 nedeniyle ilk sürüm dışında; bu manuel akışa başlangıç koşulu yapılmaz. Risk/DBS haircut ve döviz dönüşümü seçilmediğinden OPEN-010/012 bu same-currency altı durdurmaz. |

**OPEN-009 / yalnız finansal yazma UAT kapısına tek NEEDS-SINAN:** İlk manuel banka tahsilatı ve hatalı kayıt tersinde, mevcut güvenlik/onay kurallarına göre gereken şirket-kapsamlı kesinleştirme/onay yetkisi yalnız sizde mi olsun, yoksa açıkça belirleyeceğiniz mevcut şirket-finans yetkililerinde mi? Öneri ikinci seçenek: günlük işlemler tek kişiyi beklemez; kimin hangi şirkette onaylayacağı açıkça atanmalıdır. İlk seçenek yetkiyi dar tutar fakat bütün onaylar sizde birikir. Hiçbiri seçilmiş değildir. Hazırlayanın yüksek riskli kendi işlemini onaylamama kuralı, ilk aşamada tutar hard-gate olmaması ve dinamik politika kararı değişmez; yeni eşik veya genel ikinci-onay zorunluluğu seçilmez. Soru Product grantissuer kararını Finans'a genellemez; doküman/DTO/öneri formunu değil gerçek finansal kesinleştirme yetkisini etkiler.

Finansın para hassasiyeti ve dönem verisi teknik owner tarafından onaylı kaynak/örnekle pinlenecek; kaynak yoksa sayısal politika uydurulmaz. Bu kayıt yeni geniş soru listesi açmaz. Backend–Claude yalnız bir kısa uzlaştırmada request/response/null/version/error, authority+amount visibility, idempotency ve form/recovery farklarını sonuçlandırır; her kod değişikliği Business hakemliği beklemez.

## 6. Kabul ve deterministik golden — tamamı NOT_RUN

Aşağıdaki A/B, banka hesapları ve para profili sentetik önkoşuldur; gerçek grant/DB veya currency politikasını oluşturmaz. Tam sayılı örnekler yuvarlama gerektirmeyen iş-etkisi golden'ıdır, para hassasiyeti onayı değildir. Mevcut [AT-001/003/004/005/006/007/008/011/014/058][acceptance] alt bağları kullanılır; yeni global kabul numarası yok.

| Başlangıç / kullanıcı adımı | Beklenen etki; olmaması gereken | Katman |
|---|---|---|
| A banka ve Cari hesabı uygun, mevcut source0; 1000 aynı-paralı tahsilatı incele/kaydet/listeden aç | Kabul edilmiş write/policy sonrası bir kaynak/tek para olayı, audit ve terminal; dağıtılan0, kullanılabilir1000. Yeni fatura/açık-borç kapaması, ikinci banka/cari olayı, B kaydı0. Taslak aşamasında bütün para etkisi0. | HTTP + gerçek SQL + tek browser yolculuğu |
| Başarı yanıtını kontrollü kaybettir; aynı key tekrar | Tek1000 kaynak/olay; ikinci para etkisi0, original/replay kanıtları ayrı. Aynı key farklı tutar ret; permission yoksa result disclosure yok. | SQL+HTTP, temsilî browser |
| Kaynak1000/dağıtım0; hatalı girişi gerekçeli tam tersle, tekrar et | Orijinal+bağlı ters kayıt korunur; net para etkisi0, yeniden dağıtılabilir tutar0; ikinci ters yok. Fiilî bankaya refund talimatı0. Dağıtımlı kaynak bu ilk ters yolunda sessiz işlenmez. | Gerçek SQL; policy kabulüne bağlı browser |
| Başka şirket bankası/Cari hesabı, para mismatch, amount0/negatif/null, fazla scale veya kapalı dönem | Güvenli ret, partial kaynak/audit/para etkisi0. Eksik policy/profil success değil. Alan örnekleri doğru şirket/sürümle tekrar denenebilir. | Contract + SQL negatif + component |
| Fresh version sonrası başka onay/ters işlem; eski version veya yetkisiz actor gönder | Conflict/denial, overwrite0; fresh authority. Dirty A→B ve geç response B'ye taslak/tutar taşımaz; 204 sonrası okuma hatası ikinci write yaratmaz. | Pending component + SQL, temsilî browser |
| Formula-only: source1000; aktif dağıtımlar600+250;250 dağıtımı tersle | Dağıtılmamış150, sonra400; kaynak para olayı hâlâ1000/tek.1001 dağıtım fazla;1000 TRY ile1000 USD netlenmez. **Gelecek dağıtım motoru semantik testi**, ilk kaynak UI veya AT-058 PASS'i değil. | Deterministik test tasarımı; gerçek allocation UAT sonraki alt |

Ölçek/rounding golden'ı: onaylı profile göre sınır içi değer kayıpsız roundtrip; fazla scale ya da range açık ret. ProfileId/version ve gerçek parametre olmadan para/formül golden'ı PASS denmez. Mevcut fonksiyon yokken frontend resmi sonucu hesaplamaz. İlk actual paket request/original/replay + typed snapshot + audit/terminal/para cardinality ve owned cleanup'ı ayrı bağlar; SQL verifier browser kararı vermez.

## 7. Reuse ve sonraki tek alt

[LESSONS][lessons] önerisindeki kaynaklar: original HTTP ≠ SQL terminal JSON → [ProductSqlAssertions][typed] → Finance orijinal/replay gövdelerini gerçek SQL etkisine ayrı bağla, Product global modeli kopyalama. Company-scoped idempotency/audit ve [Organizasyon lifecycle SQL][org-sql] örneği → Finance transaction hatasında tüm para/audit/terminal rollback. [T09][t09] ve Cari pending-attempt testleri → dirty modal/gerçek tıklama, unknown aynı-key ve geç context; Cari özel ACK protokolü aktarılmaz. Request nullable/response required roundtrip, erken test discovery/typecheck/`--list` kendi implementation'ında doğrulanır. Source, helper/component, kontrollü browser, gerçek SQL ve gerçek browser ayrı etiketlenir.

**Sonraki tek alt:** Kaynak kesin yazması kabul edildikten ve gerçek açık kalem owner'ı hazır olduktan sonra aynı şirket/para açık kalemine kısmi dağıtım. Bugün dağıtım ekranı, risk/kur/nakit planı veya tüm Finans tamamlanması açılmaz. #229/B, gerçek kişi yetkilendirmesi, insan/production kapıları ve diğer modüllerin tam onaylı kapsamı korunur.

[decisions]: https://github.com/sinangym/GoymenERP/blob/f6fa07d026403649d6c815b84fd850e7ceeaf092/docs/decisions/KARAR_SICILI.md#L77
[master]: https://github.com/sinangym/GoymenERP/blob/f6fa07d026403649d6c815b84fd850e7ceeaf092/docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md#L481
[open]: https://github.com/sinangym/GoymenERP/blob/f6fa07d026403649d6c815b84fd850e7ceeaf092/docs/decisions/ACIK_KARARLAR.md
[backlog]: ../backlog/DELIVERY_BACKLOG_v1.json
[acceptance]: ../acceptance/ACCEPTANCE_MATRIX.csv
[party]: ../../src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/Contracts/ICounterpartyReadService.cs
[lessons]: https://github.com/sinangym/GoymenERP/blob/1bb9feda3e937bbd615daef41267d7afbad66c54/docs/proposals/module-delivery-learning-20260913/LESSONS.md
[typed]: ../../tests/GoymenERP.Product.UnitTests/SqlAcceptance/ProductSqlAssertions.cs
[org-sql]: ../../tests/GoymenERP.Api.IntegrationTests/OrganizationLocationLifecycleSqlServerRelationalAcceptanceTests.cs
[t09]: ../../tests/e2e/cari-acceptance-009/specs/t-09-account-status-switch-layering.spec.ts
