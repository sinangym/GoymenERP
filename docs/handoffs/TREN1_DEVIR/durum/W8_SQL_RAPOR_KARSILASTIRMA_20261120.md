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
- Açık: Identity atama akışında SQL1205/1222'nin güvenli nonterminal çevirisi.
  Kör dış catch güvenli değildir: standalone refusal receipt commit edildikten
  sonra audit yazımı hata verebilir. Bu aşama anahtar tüketilmedi diye sunulamaz.
- Açık: Counterparty `IsDatedHistoryRace`, varlık tipine göre bütün DbUpdateException
  hatalarını yarış sayıyor. Yalnız ilgili unique ihlallere daraltma gerekir.
- Açık: 17 çıplak süreli SignalAndWait ifadesi ve bir Task dönüşümü sonucu yok
  sayıyor. Başarısız bariyer, yarış kurulmuş gibi kabul edilmemelidir.
- Açık inceleme: Lojistik teknik guard çakışmasını kalıcı ret olarak kaydediyor;
  aynı kod gerçek stale-state için de kullanılıyor. Teknik alt ayrım ve mevcut
  idempotency sözleşmesi doğrulanmadan bütün kod terminal listeden çıkarılmamalı.
- Dış raporun diğer R maddeleri tam doğrulanmış sayılmadı. Özellikle tüm sistemin
  tek kök nedenle bozulduğu veya yerel süre farkının yalnız donanımdan olduğu
  sonucu eldeki kanıtla kurulamaz.

## Sonraki doğrulama

Kaynak dondurulup recovery pinleri üretildikten sonra derleme ve hedef testler
çalıştırılır. Önceki 46 SQL hedefi korunur; iki stok guard fact'i ve mevcut alış
faturası yarış testiyle 49 hedef olur. Bağımsız tamamlanmış test hataları toplanır;
bir hata varken bütün sonuç FAIL kalır. SDK/TRX kimliği, skip0, tek native süreç,
40 dakika süreç ve 180 dakika toplam SQL bütçesi korunur. Timeout veya kaynak
değişikliği güvenlik sınırında kuyruk durur. Bu yerel tanı kuyruğu resmi tam SQL
shard kanıtının yerine geçmez; GitHub ve exact-head K3 kapıları ayrıca bekler.

Bu belge yazılırken yeni kaynak setinde build/test/SQL henüz NOT_RUN.
