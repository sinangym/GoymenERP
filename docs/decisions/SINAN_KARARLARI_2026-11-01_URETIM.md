# Sinan Kararları — 2026-11-01 (Üretim modülü hazırlık soruları)

Kaynak: Sinan Göymen'in 1 Kasım 2026 tarihli Claude cloud oturumunda, Üretim modülü hazırlık
incelemesi (`docs/delivery/URETIM_HAZIRLIK_PLANI_v1.md`) sırasında sorulan seçenekli sorulara verdiği
**doğrudan cevaplar**. Sorular eski StokERP üretim modülünün davranışı örnek gösterilerek soruldu.
"Seçilen" sütunu kullanıcının işaretlediği seçeneğin ya da kendi yazdığı cevabın kendisidir; Claude
yalnız soruyu ve seçenekleri hazırlamıştır.

## 0. Yetki ve sınır

- Seçilen seçenekler doğrudan kullanıcı cevabıdır. AGENTS.md "Çelişkide daha yeni kullanıcı kararı
  geçerlidir" kuralı uyarınca bu belge, Üretim kapsamında `KARAR_SICILI.md`'den, master rapordan ve
  2026-10-21 delegasyon satırlarından daha yeni olduğu için onlara göre **üstündür**.
- **Şerit:** aynı oturumda Sinan, Üretim modülünün "bütün geliştirme sürecinin Claude'da" olmasını
  istedi. Bu, Üretim planındaki backend, migration, OpenAPI, formül hesaplayıcı ve ekran işlerinin
  hepsini Claude'un yazabileceği anlamına gelir (Satış emsali: `SINAN_KARARLARI_2026-10-25_SATIS.md` §0).
  Review ve merge kapıları değişmez; yazar kendi PR'ını review etmez.
- **Makbuz:** bu PR'ın merge'ü veya Sinan'ın Issue #66'ya yazacağı tek satırlık onay. Makbuz gelene kadar
  uygulama PR'ları bu kararları yapılandırılabilir ve fail-closed uygular; production aktivasyonu
  (`production.execution.enabled`) yapılmaz.
- Bu belge hiçbir `OPEN-*` kimliğini bütünüyle **KAPANDI** yapmaz; Üretim kapsamındaki alt soruları
  cevaplar. `ACIK_KARARLAR.md` güncellemesi ayrı dar PR'dır.
- Persona eşlemesi (ör. "Üretim müdürü onaylar") gerçek kullanıcıya grant değildir; grant ve rol ataması
  insan kapısında kalır.
- Sayısal değerler kod sabiti olmaz; Ayarlar politika kaydında (`DEC-SET-001`) sürümlü değer olarak
  tutulur, planda yetkili override edilebilir ve plan onayında snapshot alınır (`DEC-PRDCT-008`).

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| URT-D1 | Plan ile iş emri ilişkisi | **Tek plan = tek emir.** Bir onaylı plandan tek emir açılır | #66 5728934213 açık soru ("bir plan birden çok emir açabilir mi") | `ProductionOrder`, N-03 |
| URT-D2 | Plan onay akışı | **Tek onay:** Üretim planlama hazırlar, Üretim müdürü onaylar; hazırlayan ≠ onaylayan. Ayrı "Kontrolde" adımı yok | `DURUM_MAKINELERI.md` Taslak → Kontrolde → Onaylı | delegasyon No 90, 103; N-04 |
| URT-D3 | Aynı etiketin birden fazla aktif planda kullanılması | **Miktarla bölünebilir.** Aynı etiketin farklı kg'ları farklı planlara tahsis edilebilir; toplam üretim girdi tahsisi etiketin serbest miktarını aşamaz | StokERP: bir etiket yalnız bir aktif planda | `DEC-STK-006`, AT-041, AT-042 |
| URT-D4 | Kısmi gerçekleştirme | **Evet.** Emir birden çok gerçekleşme kaydıyla tamamlanır; tamamlanana kadar "Kısmi" durumundadır | – | `DURUM_MAKINELERI.md` |
| URT-D5 | Ana ürün kg'ının belirlenmesi | **Fiilî tartım + öneri.** Ekran "hammadde − fire − ikinci kalite" değerini öneri olarak doldurur; kullanıcı gerçek tartımı girer. Fark görünür kalır; sistem girilen değeri değiştirmez | StokERP ürün kg'ını sessizce eziyordu | `DEC-PRDCT-006`, Master §28.9 |
| URT-D6 | Kütle dengesi toleransı | **Hat başına ±%2** varsayılan profil yayımlanır; planda yetkili değiştirebilir; dışındaki fark Üretim müdürü onayı ister | `OPEN-005` KISMEN (±%2 varsayımı, yayımlanmış profil yok) | `OPEN-005`, `DEC-PRDCT-006`, AT-048; N-02 |
| URT-D7 | Yüksek fire oranı | **%5 üstü fire onay ister.** Varsayılan %5, proses başına Ayarlar'dan değişir; aşılırsa Üretim müdürü onayı olmadan emir kapanmaz | StokERP %5 üstünü yalnız log'a yazıyordu | `settings.ManufacturingTolerancePolicies.MaximumFirePercent` |
| URT-D8 | Hurda ve firenin stok etkisi | **Hurda etiketle stoğa girer** (satılabilir stok). Talaş/proses kaybı stok dışı kayıptır. Fire cinsi listesi Ayarlar'da | Master §19.2 #12 açık | `DEC-PRDCT-007`, SAT-D110; N-05 |
| URT-D9 | Motor parametrelerinin ilk değerleri | **StokERP değerleriyle başla:** çelik yoğunluğu 7,85 kg/dm³; boyama hat hızı 20 m/dk, 80 mikron, katı madde %55, fire katsayısı 1,10, eşzamanlı profil 3. Ayarlar'da ilk profil olarak yayımlanır; hat bazında düzeltilir; dilme ve boru hat hızları planda girilir | `OPEN-007` KISMEN | `OPEN-007`, 2026-08-31 OPEN-007 satırı; N-01 |
| URT-D10 | Kesim payı (kerf) ve kenar kaybı | **Makine profilinde 0 mm ile başla;** gerçek değer makine bazında sonra girilir | StokERP kerf kullanmıyordu | delegasyon No 86 (`D-M025-KERF=A`, global varsayılan yok) |
| URT-D11 | Çalışma takvimi | **08:00 başlangıç, 8 saat, tek vardiya;** hafta sonu ve tatil kapalı; hat bazında değiştirilebilir | Master §28.11 (donör varsayımı canlı politika değil) | Master §28.11 |
| URT-D12 | Hesap yuvarlaması | **Sonda yuvarla:** ara hesap tam hassasiyet; sonuç kg 3 ondalık, birim kg 4 ondalık, yarımda sıfırdan uzağa (`AwayFromZero`); dağıtımda yuvarlama farkı son satıra | Master §28.1 öneri, kilitli değil | delegasyon No 83, 85; BAL-005; N-06 |
| URT-D13 | Üretim planının satış siparişine bağlanması | **İsteğe bağlı bağ.** Plan satırı sipariş satırına bağlanabilir; stok için üretim de yapılabilir; sipariş iptali ve zorla kapatmada bağlı üretim görünür | StokERP'de bağ yoktu | `DEC-SAL-004/005`, Satış API-11, MOD-05 |
| URT-D14 | Kalite ve karantina | **Üretimde kalite karantinası uygulanmaz** (kullanıcı: "Bizim sektörde kalite karantina diye birşey uygulanmıyor üretimde"). Kalitede yalnız **ikinci kalite ayrımı** kalır: çıktı 1. / 2. kalite olarak ayrı etiketlenir; ayrı kalite ekranı yok | Master §27.7 Q-01…Q-04, AT-051, `ERTELENEN_KAPSAM.md` "temel kalite ve karantina çekirdekte" | `DEC-PRDCT-007`, W3-KALITE; N-08 |
| URT-D15 | Fason ilk sürümde | **Evet, iki yönlü.** Dışarıdan gelen fason (müşteri malını biz işleriz) ve dışarıya gönderilen fason (malımızı başka atölye işler) ilk sürümdedir; her iki durumda **hizmet siparişi düzenlenir ve akış siparişe bağlıdır** | StokERP'de üretim fasonu yoktu | `DEC-STK-008`, `DEC-CAR-001`, delegasyon No 71/73 |
| URT-D16 | Gelen fason akışı | **Hizmet satış siparişi + bizim plan:** Satış'ta fason hizmet siparişi açılır; müşteri malzemesi müşteri adına stokta durur; üretim planı siparişe bağlanır; çıktı, fire ve hurda müşterinin malı kalır; fatura hizmet bedelidir | – | SAT-D110 (fason metal işleme hizmet satışı) |
| URT-D17 | Giden fason akışı | **Hizmet alış siparişi + denge:** Satın Alma'da fason hizmet siparişi açılır; malzeme fason lokasyonuna transferle gider ve bizim malımız kalır; dönüşte çıktı + fire, gönderilen kg ile URT-D6 tolerans kuralıyla dengelenir | – | `DEC-STK-008` |
| URT-D18 | Soğuk haddede giriş ve hedef ürünün çelik kalitesi | **Uyarı, engel değil.** Farklı kalite seçilirse uyarı gösterilir, planlama devam eder | README MFG-7 açık sahip kuralı | N-07 |

## 2. Etkilenen belgeler ve varsayımlar

- **URT-D2:** `DURUM_MAKINELERI.md`'deki üretim planı zinciri Üretim için Taslak → Onaylı → Kuyrukta →
  Üretimde → Kısmi/Tamamlandı → İptal/Ters olarak okunur; belge güncellemesi ayrı dar PR'dır.
- **URT-D14:** Master §27.7'deki Q-01…Q-04 ekranları ve AT-051 ("kalite karantinasındaki çıktı sevkiyata
  açılamaz") Üretim kapsamından çıkar. Stok'taki sistem/hukuki blok kavramı (`Serbest Fiziki` formülü)
  bundan etkilenmez. W3-KALITE backlog dilimi ve `ERTELENEN_KAPSAM.md`'deki "temel kalite ve karantina"
  satırının güncellenmesi ayrı dar PR'dır.
- **URT-D5:** README'deki "açıklanamayan fark otomatik fire olmaz" kuralı korunur; tolerans içindeki fark
  URT-D6'ya göre kabul edilir, dışındaki onay ister.
- **URT-D9, D10, D11:** değerler StokERP'den aynen alınır ama kod sabiti değildir; Ayarlar'da ilk sürümlü
  profil olarak yayımlanır.

## 3. Uygulama

Uygulama sırası ve iş paketleri: `docs/delivery/URETIM_HAZIRLIK_PLANI_v1.md`.
