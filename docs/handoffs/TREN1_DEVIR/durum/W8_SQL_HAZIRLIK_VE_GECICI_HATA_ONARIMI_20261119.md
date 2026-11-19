# W8 — SQL hazırlığı ve geçici hata onarımı

Kaynak tabanı `3367954ca559bd82a2de1120b3991ec888d47f98`, son uygulama kaynağı
`b82711730425d36b911d7b8e42e69d16b0db38ad`. Sabit 174 dilim ve W8 sınırı korunur.
Bu rapor kaynak onarımı ve yazarın tekrar incelemesidir; bağımsız K3 onayı değildir.

## Düzeltmeler

1. **R1 — test veritabanı yaşam döngüsü (`72cbf0db6`).** Üç manuel oluşturma akışı,
   dosya yerini yalnız oluştururken seçen `SqlServerTestDatabase` sahibini kullanır.
   Sonraki host/probe bağlantıları aynı kataloğa gider, yeniden attach istemez.
   Master bağlantısında dosya eki taşınmaz. Bir testin oluşturamadığı katalog için
   temizleme yetkisi oluşmaz; eski/başka dosya benimsenmez. Normal CI bağlantısı,
   TLS/kimlik doğrulama ve Unicode RCSI senaryosu korunur. SQL servis ayarı değişmez.
   İki migration-history C hatasının TRX'i dosya/katalog uyuşmazlığını gösterir.
   Shell RCSI hatasının asıl SQL metni TRX'te kesildiği için kesin hata numarası
   ileri sürülmez; kaynakta aynı oluşturma/yeniden açma sorunu vardır.
2. **R2 — referans listesi geçici yazma hatası (`66b8cf8c5`, KIM-D49/KIM-D51).**
   Bütün `DbUpdateException` türleri artık kod/sürüm çakışması sayılmaz. Yalnız
   SQL 2601/2627 mevcut yarış yoluna gider; diğer SQL/EF yazma arızaları mevcut
   `settings_provider_unavailable` kanalını kullanır. Aynı-key winner replay'i ve
   fresh şirket/aktör/yetki çitleri korunur. Idempotency okuması da sağlayıcı hata
   sözleşmesini kullanır. Otomatik command retry eklenmez.
3. **R3 — parolasız bağlantıda yanlış sızıntı hatası (`b82711730`).** Eski C koşusunun
   CLI testi `DoesNotContain` için boş parola verdiğinden başarısızdı; TRX'teki
   bulunmuş alt metin boştur. CLI ve PowerShell testleri yalnız gerçekten bulunan
   parolayı arar. Parolalı bağlantıda kontrol, her durumda schema/hash/endpoint,
   dosya-stdout eşliği ve native exit denetimleri korunur. Test atlanmaz.

## Yazılan kanıt senaryoları

- Yeni gerçek-SQL yaşam döngüsü Fact'i: dosya yerleşimi, aynı kataloğu iki bağlantıyla
  yeniden açma, RCSI ON, çakışan CREATE'in 1801'i, başarısız oluşturucunun asıl
  kataloğu silememesi ve sahibin temizlemesi. Gerçek host başlangıcını ayrıca mevcut
  migration-history, Shell ve RCSI Fact'leri ölçer.
- Yeni gerçek-SQL geçici hata Fact'i: ayrı SQL oturumu idempotency tablosunu kilitler;
  change ve refusal save'leri 1222 alır. EF işleminin kapanması (`@@TRANCOUNT=0`),
  iş/command/audit yazısının kalmaması ve aynı anahtarla sonraki açık denemenin
  normal sonucu, ardından tek replay ölçülür. SQL exception uydurulmaz.
- Yeni yerel unit: katalog bağlantısı, TLS, dosya yolu kaçışı, mevcut dosya ve farklı
  host bağlamının reddi; normal CI üretim metni. Eski unit/SQL Fact'leri korunur.
- İlişkisel envanter +2 yöntem: toplam **1762 yöntem / 1769 execution**. C yeni kaynakta
  496 yöntem / 503 execution bekler. Bunlar kaynak sayımlarıdır, SDK/TRX PASS değildir.

## Düzeltme sonrası tekrar inceleme

**Yazılım:** yeni constructor seçeneğinin mevcut çağrıları değiştirmemesi, test
sahipliğinin xUnit test örnekleri arasında paylaşılmaması, iptal istisnasının
503'e çevrilmemesi, replay'in asıl correlation'ı koruması ve endpoint'in mevcut
503 eşlemesi tekrar okundu. Yeni native derleme henüz yapılmadı.

**Veri bütünlüğü:** Settings şirketli composite PK/FK ve insert-only sürüm anahtarı,
tek SaveChanges içindeki command/audit birlikteliği, başarısız save sonrasında Audit
interceptor temizliği okundu. Kasa onay/ret için ortak treasury işlem sahibi ve
tam rollback işaretçisi, Inventory transfer kilit hatasının dar dönüşümü de yeniden
okundu. Bu kontrol yeni genel bir para/stok politikası seçmez.

**SQL Server:** CREATE ile yeniden bağlantı ayrıldı; dosya çakışmasında attach yok,
başarısız CREATE ardından yabancı kataloğa DROP yok. SQL 1222'nin kendiliğinden tüm
işlemi geri almadığı dikkate alınarak yeni test EF'nin gerçekten geri aldığına da
bakar. [Microsoft işlem/kilit açıklaması](https://learn.microsoft.com/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide?view=sql-server-ver17#customize-locking-and-row-versioning).
Yeni sorgu planı, DMV/wait/IO veya performans iyileşmesi ölçülmedi. İncelenen yollar
için ek P0/P1 doğrulanmadı; bütün deponun satır satır kabul edildiği iddia edilmez.

## Gerçek doğrulama ve sıra

- `66b8cf8c5`: backend grup doğrulayıcısı **38 PASS**, ilişkisel doğrulayıcı **104 PASS**;
  ikisi native exit0, skip0. Bunlar Python araç testleridir; gerçek SQL değillerdir.
- `f2c83cb94`: recovery üreticisi exit0. Üreticinin ölçtüğü riskli kaynak 1469,
  context19, migration344, Audit canlı kaynak42; yalnız değişen riskli kaynak pin'i
  üreticiden güncellendi. R3 yalnız test kaynağıdır.
- Mimari guard'a eklenen SqlClient kaydı yalnız exception türü/numarası içindir;
  yeni SQL komutu, şirket filtresi bypass'ı veya izin yoktur. K3 review bekler.
- Yeni birleşik API/Integration build, unit/native guard, gerçek SQL ve uzak CI:
  **NOT_RUN**. Önceki 17 hedef R1/R2'yi kapsamıyordu. Hepsi korunarak toplam
  **36 tek-case SQL hedefi** hazırlandı; yeni regresyonlar, üç host akışı ve RCSI
  sınıfının tamamı dahil. Ayrıca yerel-storage unit hedefi eklenir.
- Eski `b5373e67` tam SQL koşusu korunur. Takip kaynağı, o koşu tamamlanıp native
  süreçler çıktıktan ve RAM kapısı açıldıktan sonra sırayla derlenir ve sınanır.
  İlk hata sonraki hedefleri durdurur; sonuçlar exact SDK/TRX ve skip0 ile doğrulanır.
  Yerel SQL17 sonucu CI SQL16 veya tam K3 kabulü yerine geçmez.
- Push öncesi yeni derleme ve hedefli SQL sonuçları değerlendirilir. Push sonrası
  yeni head'de tam CI ve iki bağımsız K3 review gerekir. Ready/merge/production yoktur.
