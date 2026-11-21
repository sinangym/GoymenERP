# W8 son birim test kaynak kapanışı

Taban `203d0536b0a02532c2d771d855ba6baeaeff9187`; GOV-D9. İlk başarısız TRX/loglar
korundu. Bu sonlu düzeltme iki Purchasing ve bir RCSI fixture hatasını kapsar; yeni
dilim, dalga veya işletme kararı eklemez.

- Purchasing supplier değişikliği artık genel alan guard'ından önce mevcut
  SupplierRevisions guard'ında reddedilir. Aynı yasak supplier mutasyonu ve exact
  InvalidOperationException korunur; mesaj mevcut revision-history ret sebebiyle eşleşir.
- Outbox constraint yalnız kapanışa izin veren eski beklenti yerine mevcut closed/revived
  iki event'li exact IN ifadesi ve owner sabitiyle birlikte doğrulanır. Envelope,
  tenant, sequence ve kimlik constraint kontrolleri korunur; üretim constraint değişmez.
- RCSI ancestor reparse testi Windows'ta symbolic-link privilege gerektiren fixture
  yerine junction üretir; diğer OS'lerde directory symbolic link sürer. Mevcut PowerShell
  bulucusu, parametreli tek kullanımlık fixture script'i ve ayrı process kullanılır.
  Link/target geçici dizin sınırı ve link'in doğrudan fixture child olması yaratmadan
  önce kontrol edilir. Gerçek ReparsePoint niteliği ve resolved hedef açıkça doğrulanır.
  Mevcut pre-secret exit-2/secret-probe ret kontrolü değişmez. Link recursive olmayan
  Directory.Delete ile önce kaldırılır, disposable container sonra temizlenir; payload
  kendi fixture sahibinde kalır. Yönetici izni, developer mode veya ortam ayarı istenmez.

Yalnız iki test dosyası ve bu rapor değişir; RCSI CLI/backend, permission, policy,
migration veya guard/pin değişmez. Test skip/silme/karantina veya SQL hatasını başarı
sayma yoktur. Fixture source düzeltmesi gerçek SQL/RCSI kabulü değildir.

`git diff --check`: exit 0. Bu head'de .NET/Node/test ve junction fixture çalıştırılması
NOT_RUN; tek ağır işlem sırası entegrasyon sahibindedir. Son exact-head test sonucu
ayrıca kaydedilmelidir. Bu yazar desteğidir, bağımsız K3 incelemesi değildir.
