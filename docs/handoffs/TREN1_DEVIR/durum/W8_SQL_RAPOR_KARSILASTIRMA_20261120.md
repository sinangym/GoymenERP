# W8 — bağımsız SQL raporunun güncel kaynakla karşılaştırılması

İncelenen dış rapor `b5373e67e00bc9445b32341213b323b14480eaad` ve CI run
`44481418363` içindir. Yerel düzeltme tabanı
`8f6c50648cfc9e0a15df04e6c1ce2172c2cf4bf7` farklıdır. Dış rapor K3 imzası
değildir; bu karşılaştırma da uygulama kabulü veya bağımsız K3 onayı değildir.

## Son ölçülen yerel sonuç

Taban adayda dokuz derleme, seçilmiş 6108 backend kontrolü, frontend tür kontrolü
ve 13 Vitest geçti. 46 SQL hedefinin 41'i skip0 geçti. Kasa onay/ret yarışı,
transfer ve önceki Audit/Settings düzeltmeleri bu sonuçlara dahildir.
42. hedef `Bank_csv_receipt_read_SQL_failure_is_unavailable_without_consuming_the_retry_key`
başarısızdır; kalan dört hedef ve ardından gelen beş Python paketi NOT_RUN.
Bu sonuç yeni adayın veya bütün SQL envanterinin kabulü sayılmaz.

## Bu düzeltme seti

- Ayarlar: 13 kayıt-sonucu sorgusunda doğrudan SQL hatası yanında EF'nin
  `InvalidOperationException -> SqlException` biçimi de aynı provider-unavailable
  sonucuna gider. Önceki save sarmalı onarımı bu ilk sorgu yolunu kapsamıyordu.
  Genel InvalidOperationException ve iptal aynı kategoriye alınmaz.
- Stok: sayım ve hurda fiziksel bekçisi yalnız `ConcurrentChange` için yetkiyi
  yeniden ölçer ve mevcut tipli 409 cevabını verir. Transferdeki iki eşleme
  zaten tabanda vardır. Yeni iki SQL fact, gerçek sıfır-satır UPDATE hatasını
  kontrollü üretir; rollback, 403 önceliği, aynı anahtar ile retry/replay sınar.
  Bunlar iki eşzamanlı komutun yarış kanıtı olarak sunulmaz.
- Alış faturası: `LineBusy` mevcut 409 sözleşmesine çevrilir; kayıttan dönen hata
  öncesinde yetki yeniden ölçülür. İki birim regresyonu aynı anahtar ile retry,
  replay ve kilit beklenirken yetki kaybını sınar.
- Finans, muhasebe dönem işlemi, üretim maliyet host'u ve fason stok bırakma:
  bilinen EF sorgu/save sarmalları mevcut hata-numarası ve cevap politikalarıyla
  hizalanır. Finans Unavailable kalır; muhasebe ve stokta yeni 1222→409 politikası
  seçilmez. Üretimde mevcut 1205/1222 sınırı korunur. 21 normal test vakası
  sınıflandırmayı sınar; bu testler SQL rollback veya uç nokta kabulü değildir.
- Mevcut guard fingerprint'leri ev üreticisiyle yenilenir; iki SQL fact envantere
  eklenir. Yeni raw SQL, tenant bypass, migration veya izin yoktur. K3 review bekler.

EF davranış kaynağı: [EF Core 10.0.8 yürütme stratejisi](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.SqlServer/Storage/Internal/SqlServerExecutionStrategy.cs).

## Kasa önerisinin sınırı

Eski asimetrik transaction düzeninde yalnız onay okumasında UPDLOCK kullanılması
uygulanabilir bir alternatiftir; rapordaki öneri geçersiz sayılmamıştır. Güncel
iki taraflı Serializable düzende aynı değişiklik tek başına deadlock'u kaldırmaz.
Mevcut düzeltmenin iki kontrollü SQL1205 kurbanı taban adayda geçti. Bu sette
işlem modeli veya testin hata sonucu değiştirilmedi. Aynı anahtar tüketmeme
kanıtı ile gerçek kullanıcı iş akışı kabulü ayrı tutulur.

## Kaynakta kapanmış veya hâlâ açık maddeler

- Kapanmış: Product test temizliğinde master/AttachDBFilename ayrımı, RCSI boş
  secret kontrolü, gözlenen sonuçla grup kabulünü ayıran özet ve gerçek süre
  bütçesini yazan timeout mesajı.
- Kaynak düzeltildi, SQL bekliyor: Identity atamasında ilk receipt okuması ve
  pre-commit SQL1205/1222 ayrı ele alınır. Başarılı rollback/dispose ve taze yetki
  kontrolünden sonra mevcut nonterminal 503 kullanılır. Commit başladıktan sonra,
  replay veya standalone receipt aşamasında bu çeviri yapılmaz. Üç yeni SQL fact;
  query/save 1222, aynı anahtarla retry/replay, yetki kaybı ve commit sınırını sınar.
  Recorder'ın normal provider-hata sözleşmesi hata yutmaktır; replay audit'inin
  hata fırlatması negatif test dekoratörüdür, kanıtlanmış üretim hatası değildir.
  Managed-user Serializable yolu bu yeni SQL fault fixture'ının kapsamı değildir.
- Kaynak düzeltildi, SQL bekliyor: Counterparty `IsDatedHistoryRace` yalnız üç
  tarihçe unique index'inin gerçek 2601/2627 hatasını ve eşleşen entity tipini yarış
  sayar. İlgisiz PK, CHECK ve uzunluk hataları artık kalıcı conflict değildir.
  Provider hatasının HTTP 500 olarak kaçması ayrı açık sınırdır; bu patch bunu
  kapatmış sayılmaz. Bir yeni SQL fact gerçek index/constraint hatalarını sınar.
- Kaynak düzeltildi, SQL bekliyor: 17 çıplak süreli SignalAndWait ve bir Task
  dönüşümü false sonucunda açık TimeoutException verir; süre, katılımcı sayısı ve
  yarış konumu korunur. Etkilenen 41 mevcut SQL fact tanı kuyruğuna alınır.
- Kaynak düzeltildi, SQL bekliyor: Lojistik yalnız Inventory physical guard'ın
  bilinen inner-exception kökenini mevcut nonterminal concurrency yoluna taşır.
  Rollback doğrulanamazsa 503 kalır. Aynı kodun gerçek stale-state kullanımları
  terminal kalır; Subcontract zaten nonterminal olduğundan değiştirilmez.
  Bir yeni SQL fact kontrollü gerçek sıfır-satır UPDATE, rollback, iki modülün
  outbox/stock/audit etkileri ve aynı anahtarla retry/replay sınar; ayrıca mevcut
  terminal-ret SQL regresyonu korunur. Bu fact iki thread yarış kanıtı değildir.
- Dış raporun diğer R maddeleri tam doğrulanmış sayılmadı. Özellikle tüm sistemin
  tek kök nedenle bozulduğu veya yerel süre farkının yalnız donanımdan olduğu
  sonucu eldeki kanıtla kurulamaz.

## Sonraki doğrulama

Kaynak dondurulup recovery pinleri üretildikten sonra derleme ve hedef testler
çalıştırılır. Önceki 46 SQL hedefi korunur; yeni owner regresyonları, mevcut alış
faturası yarışı, Lojistik terminal-ret testi ve bariyerlerden etkilenen mevcut
testlerle tekilleştirilmiş 95 hedef olur. Tamamlanmış test hataları toplanır;
bir hata varken bütün sonuç FAIL kalır. SDK/TRX kimliği, skip0, tek native süreç,
40 dakika süreç ve 180 dakika toplam SQL bütçesi korunur. Timeout veya kaynak
değişikliği güvenlik sınırında kuyruk durur. Bu yerel tanı kuyruğu resmi tam SQL
shard kanıtının yerine geçmez; GitHub ve exact-head K3 kapıları ayrıca bekler.

Bu belge yazılırken yeni kaynak setinde build/test/SQL henüz NOT_RUN.

Kaynak commitleri: ilk eşleme ve wrapper düzeltmeleri `abcdbe263`; Cari
`3acca9a75`; Kimlik `4423522ad`; bariyerler `627a7b132`. Bunlar yerel kaynak
kanıtıdır; GitHub'a yayımlanma veya tam K3 kabulü anlamına gelmez.
