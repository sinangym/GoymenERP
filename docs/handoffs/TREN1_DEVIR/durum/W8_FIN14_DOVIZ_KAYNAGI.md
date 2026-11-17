# W8 — FIN-S14 bozdurma kaynak devamı

Durum: **PARTIAL**. Sabit kapsam B-28; FNS-D14/D24/D25/D47/D59–63 ve doğrudan Q10 cevabı
#1189/6958102286. Yeni dalga, işletme politikası, grant/rol, production veya Finance STOP değişikliği yoktur.

## Tanımlı kaynak tamamlaması — referans TL ve defter değeri ayrımı

- `CashTransferExchangeSnapshotV1` iki trailing nullable `SourceReferenceTl`/`TargetReferenceTl` kanıtını taşır.
  Gerçek verilen/alınan tutarlar kendi kayıtlı karşılaştırma kurlarıyla tek backend calculator'da çarpılır.
  `100 USD → 3300 TL`, kayıtlı TCMB `34` örneğinde referanslar ayrı `3400 TL` ve `3300 TL` olarak saklanır.
  Bu tutarlar muhasebe defter değeri, otomatik kur kârı/zararı veya fiş tutarı değildir. Mevcut işlem kuru,
  exact kur oranı ve kur sapması değiştirilmez; sapmadan ayrı ekonomik olay yaratılmaz.
- Referans çarpımı sonlu ondalık kaynakların tamamını `BigInteger` ile korur; System.Decimal taşması veya
  ara yuvarlama yüzünden değer kaybedilmez. Bu yalnız karşılaştırma kanıtıdır; yeni para profili/fiş yuvarlama
  kuralı seçmez. Kaynak sürümü `finance.cash-transfer.tl-reference.v1`, para kimliği gerçek Settings TRY'dır.
- Mevcut append-only transferin `ExchangeSnapshotJson` alanı ve değişmez komut cevabı kanıtı saklar;
  yeni model, migration veya EF gerekmez. Null ekler eski JSON'da yazılmaz; eski kayıt/replay, bugünün
  kuruyla zenginleştirilmez. Tek tarafı eksik veya kayıtlı bacak/karşılaştırma ile çelişen yeni kanıt reddedilir.
- Önizleme ve gerçek kayıt ekranı iki backend referans tutarını ayrı gösterir; bunların defter değeri
  olmadığını açıklar. UI tutarı veya kuru yeniden hesaplamaz. Eski kayıtta eksik değer açıkça gösterilir.
- Üç birim kaynak testi; exact büyük/küçük çarpım ve eski JSON roundtrip; mevcut gerçek SQL commit/replay
  fact'inde iki referansın kalıcılık kanıtı; strict decoder ve DOM tüketici kaynakları eklendi.
  İlk checkpoint'te kaynak derlemeleri QUEUED idi; aşağıdaki son kaynak doğrulaması bu değişikliği de kapsar.
  Runtime birim/SQL/HTTP/Vitest/browser/CI **NOT_RUN**.

## Somut kalan — actual book source missing

- `FinanceTreasuryLedger` onaylı kendi-para-birimi giriş/çıkışlarını toplar; kayıtlı fonksiyonel TL carrying
  değerini taşımaz. `OpeningBalanceV1` nakit/banka açılışında yalnız tutar ve para kimliği taşır; TL/kur kanıtı yoktur.
- Accounting `JournalRow`/`JournalLineRow` gerçek para birimi ve borç/alacak tutarını taşır; bir treasury hesabının
  döviz miktarıyla asıl fonksiyonel TL pozisyonunu birleştiren typed owner portu/kayıt bağı mevcut değildir.
  Genel `IAccountingStoreV1` tek fiş ve sayfalı fiş okur; bunu bozdurmanın book-value portu diye sunmak doğru olmaz.
- Örnek yalnız sentetiktir: 1000 USD'nin 35000 TL olduğu varsayılan bir defterden 400 USD çıkışta hangi kayıtlı
  payın kullanılacağını bugünkü TCMB, FIFO veya ortalama seçerek üretmek bu kararların verdiği yetki değildir.
  Q10 açıkça yalnız referansı muhasebe değerinden ayırır; değerleme yöntemini seçmez.
- Mevcut `finance.cash-transfer.recorded.v1` producer/reader ve normal journal source provider yalnız aynı
  para birimli virmanı kabul eder. FX'i bu source'a yanlış parasal karşılıkla sokma veya ilk bacak tutarını
  TL fiş tutarı sayma yapılmadı. FX için gerçek kayıtlı defter değeri, hesap eşlemesi ve fiş biçimi kanıtı
  sağlanmadan parasal köprü açılamaz; FNS-D63'ün sınırı korunur.
- Accounting model/snapshot, shared Money ve diğer Accounting/Inventory/Purchasing/Manufacturing owner
  dosyaları değiştirilmedi. Yeni FX event-kind/schema tasarımı ve gerekiyorsa Accounting source-kind
  constraint'i diğer owner ile koordine edilmeden genişletilmedi. FIN-S14 bu dar tamamlamayla DONE sayılmaz.

## Sonraki doğrudan karar — belgeli gerçek değer ve farklı kişi onayı

Kaynak: #1189/6960684942, kullanıcı cevabı **“Belgeli gerçek değer ve farklı kişi onayı (Önerilen)”**.
Bu cevap bir değerleme yöntemi seçmez. Kaynak dövizin gerçek TL defter değeri yetkili muhasebe
hazırlayıcısının belgeli beyanıdır; başka muhasebe yetkilisi ayrıca onaylar. Önceki “actual book source missing”
bölümü bu cevap öncesindeki teknik boşluğu kaydeder; yeni kaynak bu boşluğu yöntem tahminiyle doldurmaz.

- Sözleşme paylaşımı `08b0d6dc9`: ayrı `finance.cash-exchange.recorded.v1` ve
  `finance.cash-exchange.book-value-approved.v1`, `finance.outbox.v1`/schema 1. İlk olay gerçek
  `CashTransferView`'ın iki değişmez bacağını taşır. İkinci olay aynı gerçek transferi ve onaylı defter
  değeri önerisini taşır. Hiçbiri ikinci para hareketi veya otomatik muhasebe fişi değildir.
- Finance-owned hazırlama, ayrı karar ve komut makbuzu append-only'dir. Gerçek transfer sürümü,
  tam kaynak JSON/hash'i, verilen/alınan özgün tutarlar, belge sürümü/hash'i, hazırlayan ve farklı karar
  veren korunur. Yeni kaynak yalnız `SourceBookValueTl` taşır; TCMB değeriyle doldurulmaz.
- Mevcut `accounting.normal-journal.draft.create`/`.approve` izinleri güncel şirket geneli scope ile
  ölçülür. Başka aktör/şirket ve aynı anahtarın başka gövdesi reddedilir. Hazırlayan kendi önerisini
  karara bağlayamaz. Onay sonrası eski hazırlama anahtarı ilk Pending yanıtını aynen tekrarlar.
- Her yeni yazma Finance-owned Serializable işleminde tek kez yapılır; karar, makbuz, onay olayı ve
  Audit owner append aynı işlemde kalır. Audit veya son güncel yetki çiti başarısızsa tümü geri alınır.
  Önceden açılmış başka bir transaction veya pending owner değişiklikleri devralınmaz; 503 sınırı vardır.
- Gerçek Documents servisinden okunabilir belge sürümü ve hash doğrulanır. Yeni `finance.cash-exchange`
  owner bağlantısı yalnız mevcut Accounting komut amacıyla gerçek transferi okur; normal Documents
  dosya okuma/yükleme/bağlama yetkilerini kaldırmaz veya grant eklemez. Ekran belgeyi gerçek listeden
  seçer; kimlik, sürüm veya hash kullanıcıya elle yazdırılmaz. Eski/başka şirket belge başarı sayılmaz.
- API purpose aday listesi ve workspace hazırlama/onay izinlerinden biriyle okunur; ek Finans rapor
  read grant'i türetilmez. UI backend referansını/gerçek tutarını yeniden hesaplamaz. Sonucu bilinmeyen
  komut aynı anahtar ve gövdeyle kilitli tekrar edilir; 403/503 keyfî hata metni ekrana taşınmaz.
- Kaynak testleri: iki event sözleşmesi ve defter/referans ayrımı; model immutability/maker FK;
  gerçek SQL distinct checker, immutable hazırlama replay'i, gerçek Audit sonrası rollback, SQL save
  sonrası yetki kaybı ve iki onaycı yarışı; HTTP strict body/şirket sınırı; frontend strict decoder,
  seçili belge pin'i, aynı anahtarlı belirsiz tekrar ve maker UI sınırı. Runtime **NOT_RUN**.

### Merkezi bağlar ve henüz alınmayan kanıt

- `FinanceCashExchangeBookValueRuntime.AddFinanceCashExchangeBookValuesV1()` ve
  `FinanceEndpoints.MapFinanceCashExchangeBookValuesV1()` gerçek DI/endpoint bağları merkez sahibindedir.
- `CashExchangeBookValuePage(companyId, actorId, canPrepare, canApprove)` mevcut iki Accounting izninin
  OR purpose gateway'iyle bağlanmalıdır; yeni ERP izni, grant/rol veya Critical sınıfı yoktur.
- Gerçek EF üreticisi `20261117212319_AddCashExchangeBookValues` migration'ını üretti. Üç yeni tablo,
  CHECK/FK/index ve snapshot/Designer üretici çıktısıdır. Kontrollü pencerede ilk Down'a 51915 koruması,
  Up'a 51912/51913/51914 UPDATE/DELETE trigger'ları eklendi. Eski migration'lar değiştirilmedi.
- Accounting consumer/inbox/model/snapshot/fiş köprüsü ayrı owner'dadır. TRY hedef bacağına ait gerçek
  tutar `Preparation.TargetMoney`'dir. İki dövizli işlemde hedefin gerçek TL değeri mevcut kaynaktan
  kanıtlanmıyorsa referanstan türetilmez. Güncel hesap eşlemesi, plan/profil ve dönem çitleri Accounting
  hazırlama/onay akışında korunmalıdır; bu Finance onayı bunların yerine geçmez.
- OpenAPI, registry, recovery/release, guard ve relational/test-group pinleri merkez üreticisinde
  yenilenecektir. Unit/SQL/HTTP/Vitest/browser/CI koşumu yapılmadı; kaynak hazır/kabul edilmiş ayrıdır.

### Son kaynak doğrulaması

| İşlem | Gerçek sonuç |
|---|---|
| Mevcut frontend lock ile `npm ci --ignore-scripts --no-audit --no-fund` | EXIT 0; lock değişmedi |
| API ve iki mevcut test projesinin dar restore'u | EXIT 0; paket/sürüm eklenmedi |
| API kaynak derlemesi ve son protocol deltası derlemesi | İkisi EXIT 0; 0 uyarı / 0 hata |
| `dotnet tool restore`, ardından gerçek `dotnet-ef migrations add AddCashExchangeBookValues --no-build` | İkisi EXIT 0; EF 10.0.8 |
| Finance.UnitTests kaynak derlemesi | EXIT 0; 0 uyarı / 0 hata |
| Api.IntegrationTests kaynak derlemesi | EXIT 0; 0 uyarı / 0 hata |
| Son pinned-belge deltası `tsc -b` | İlk deneme EXIT 2: async closure nullable erişimi; dar const düzeltmesi sonrası EXIT 0 |
| `git diff --check` | EXIT 0 |

Derlemeler `--no-restore -m:1 -nr:false -p:UseSharedCompilation=false`
ve `-p:OpenApiGenerateDocumentsOnBuild=false` ile seri alındı. Her ağır süreç girişinde en az 2 GiB
fiziksel RAM doğrulandı. Native loglar ve çıkış kodları depo dışında korundu. İlk TSC hatası saklandı;
önceki başarılı TSC sonucu son arayüz deltası için kanıt sayılmadı.

Farklı onaylayan, hazırlama yetkisi olmasa da gerçek Documents panelini ve önerinin sabitlenmiş belge
sürümünü görebilir. Sürüm ve hash uyuşmazsa indirme sunulmaz. İndirme kendi sürümünün byte uzunluğunu ve
SHA-256'sını doğrular; keyfî dış URL açılmaz. Kaynak Vitest buna ilişkin checker ve bozuk hash örneklerini içerir.
Normal Documents yetkileri korunur; bu ekran dosya erişimi sağlamış sayılmaz.

Yeni gerçek SQL kaynaklarının tam adları aşağıdadır. Hiçbiri koşulmadı (**NOT_RUN**):

- `GoymenERP.Api.IntegrationTests.FinanceExpensePaymentSqlServerRelationalAcceptanceTests.Exchange_book_value_SQL_guards_reject_mutation_and_lossy_Down_but_allow_an_empty_store`
- `GoymenERP.Api.IntegrationTests.FinanceExpensePaymentSqlServerRelationalAcceptanceTests.Exchange_book_value_distinct_checker_and_original_replay_do_not_move_money_or_requote_TCMB`
- `GoymenERP.Api.IntegrationTests.FinanceExpensePaymentSqlServerRelationalAcceptanceTests.Exchange_book_value_failure_after_real_Audit_append_rolls_back_decision_event_and_key`
- `GoymenERP.Api.IntegrationTests.FinanceExpensePaymentSqlServerRelationalAcceptanceTests.Exchange_book_value_permission_loss_after_real_save_rolls_back_and_same_key_remains_retryable`
- `GoymenERP.Api.IntegrationTests.FinanceExpensePaymentSqlServerRelationalAcceptanceTests.Exchange_book_value_two_SQL_checkers_cannot_approve_one_economic_source_twice`

K3 bekleyen kaynak incelemesi: yeni üç immutable store, migration SQL/trigger'ları ve aynı transaction'da
Audit owner append. Guard izin listeleri genişletilmedi. DB apply, runtime unit/SQL/HTTP/Vitest/browser,
CI ve üretim aktivasyonu yapılmadı. Merkezi mount/consumer/pin ve kabul kanıtları tamamlanmadan
FIN-S14 kaynak checkpoint'i kullanıma hazır veya tüm dilim DONE sayılmaz.
