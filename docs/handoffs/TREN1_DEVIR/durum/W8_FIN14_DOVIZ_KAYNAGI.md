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
  **Kaynak derlemeleri QUEUED; runtime birim/SQL/HTTP/Vitest/browser/CI NOT_RUN.**

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
