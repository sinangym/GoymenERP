# Sinan Kararları — 2026-11-16 (Dalga 4 ek 23: sipariş kuru, bildirim saklama, kredi planı doğrulaması, negatif kalan kg, fazla teslim toleransı, kredi izinleri, iki okuma teyidi)

Bu belge önceki karar dosyalarını silmez. Şu kararları ekler: FNS-D98, FNS-D99, FNS-D100, FNS-D101, KLN-D111,
URT-D107, URT-D108 ve ALS-D114. Her satır tamamladığı veya güncellediği önceki kararı adıyla söyler:

- FNS-D98 → FNS-D79 (EK18);
- KLN-D111 → KLN-D110 (EK20);
- FNS-D99 → FNS-D94 (EK22);
- URT-D107 → URT-D99 (EK16);
- ALS-D114 → ALS-D102 (EK11) ve fazla teslimde ALS-D43 (`SINAN_KARARLARI_2026-11-02_ALIS_EK3.md`);
- FNS-D100 → FNS-D93 (EK22);
- FNS-D101 → FNS-D85 (EK21) ve FNS-D23 (`SINAN_KARARLARI_2026-11-11_FINANS_F005.md`);
- URT-D108 → URT-D95 (EK15).

Bu kararların burada değinilmeyen kısımları aynen geçerlidir. Diğer önceki kararlar da aynen geçerlidir.

EK11, EK15, EK16, EK18, EK20, EK21 ve EK22 henüz merge edilmemiş karar PR'larındadır (#1175, #1179, #1180,
#1182, #1186, #1187, #1188). Bu belge onlara o PR'lardaki metinle atıf yapar. Çelişkide daha yeni olan bu belge
geçerlidir.

**Kaynak:** Sinan, 16 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı:

- SINAN-CEVAP 48 (48.1–48.7): Issue #1189 yorumu
  https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6914140908
- SINAN-CEVAP 48 ek (48.8–48.9, 48.5'teki iki noktanın netleştirmesi): Issue #1189 yorumu
  https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6914209115

Sorular, Issue #1189'daki `NEEDS-SINAN` satırlarından geldi (FNS-D79, URT-D99, ALS-D102/OPEN-004, FNS-D85,
FNS-D94, FNS-D93, URT-D95, KLN-D110 saklama). "(Önerilen)" işaretleri soranındır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 48.1 | FNS-D79 (#1182): Kararda geçen "günün kuru" hangi kur olsun? | **TCMB döviz satış (Önerilen)** | "İşlem tarihindeki TCMB döviz satış kuru. FNS-D92 ile aynı kaynak, tutarlı olur." Seçilmeyen: "TCMB döviz alış" — İşlem tarihindeki TCMB döviz alış kuru. "Banka kuru elle" — Kullanıcı o günün banka kurunu elle girer; denetime yazılır. |
| 48.2 | KLN-D110: Uygulama içi bildirimler (ör. "ayrım bırakıldı" uyarısı) ne kadar saklansın? | **Ayardan, 1 yıl (Önerilen)** | "Süre Ayarlar'dan değişir, başlangıç değeri 1 yıl. Kalıcı silme kapalı başlar, ayrı onayla açılır." Seçilmeyen: "Ayardan, 90 gün" — Daha kısa saklama, başlangıç değeri 90 gün. Silme yine kapalı başlar. "Süresiz" — Bildirimler hiç silinmez. |
| 48.3 | FNS-D94 (#1188): Kredi ödeme planı içe alınınca sistem hangi toplamları doğrulasın, plan tutmazsa ne olsun? | **2 eşitlik, engelle (Önerilen)** | "Her taksit = anapara+faiz+BSMV+KKDF ve anapara toplamı = kredi tutarı. Satır başına 0,01 TL tolerans. Tutmayan plan onaya gönderilemez." Seçilmeyen: "2 eşitlik, uyar" — Aynı iki eşitlik ve tolerans. Tutmayan planda uyarı çıkar ama kayıt engellenmez. "Yalnız anapara toplamı" — Yalnız anapara toplamının kredi tutarına eşitliği doğrulanır. Tutmazsa engellenir. |
| 48.4 | URT-D99 (#1180): Kuyruk kapasite yükünde kalan kg eksi çıkarsa (kayıtlı çıktı ve tüketim, planlanan girdiyi aşarsa) ne olsun? | **Sıfır say, işaretle (Önerilen)** | "Kapasite yükü 0 kg sayılır. Planda 'planı aştı' işareti gösterilir." Seçilmeyen: "Eksi göster" — Eksi değer olduğu gibi gösterilir ve toplam yükü azaltır. "Kaydı engelle" — Planlanan girdiyi aşan kayıt, plan güncellenmeden kabul edilmez. |
| 48.5 | ALS-D102 / OPEN-004 (#1175): Alış giriş toleransının ayrıntıları nasıl olsun? (OPEN-004 yine açık kalır.) | **Ayardan, girişte, yoksa ret (Önerilen)** | "kg bantları (%1/%2/%3) Ayarlar değeri olarak girilir ve giriş anında okunur. Adet/metre satırlarına tolerans uygulanmaz. Ayarlarda değer yoksa kayıt reddedilir." Seçilmeyen: "Ayardan, siparişte" — Değer sipariş anında okunur ve siparişe sabitlenir. Diğerleri önerilenle aynı. "Ayardan, yoksa %0" — Değer yoksa tolerans %0 kabul edilir; yalnız tam miktar geçer. |
| 48.6 | FNS-D93 (#1188): Kredi girişi ve onayı için ayrı izinler açılsın mı? Grant yapılmaz, insan kapısında kalır. | **İki izin açılsın (Önerilen)** | "finance.credits.propose (Finans Uzmanı) ve finance.credits.approve (Finans Müdürü / Genel Müdür) açılır. Kaydı giren onaylayamaz." Seçilmeyen: "Mevcut finans izni" — Yeni izin açılmaz; mevcut finans izinleri kullanılır. Kaydı giren onaylayamaz kuralı yine geçerli. |
| 48.7 | FNS-D85 (#1187) ve URT-D95 (#1179): Cevapta olmayıp yazarın önceki karar ve koddan çıkardığı iki okuma var. Teyit ediyor musun? FNS-D85 kuralı FNS-D23 ile aynı olur; URT-D95'te "tüketilen" = önceki iadeler − net çıktı − WIP. | **İkisini de teyit (Önerilen)** | "İki okuma da karar olarak yazılır; URT-D95 hesabı formül golden'ında sabitlenir." Seçilmeyen: "Yalnız FNS-D85" — FNS-D85 teyit edilir; URT-D95 hesabı sonra ayrıca konuşulur. "Yalnız URT-D95" — URT-D95 teyit edilir; FNS-D85 ayrıca konuşulur. "İkisi de bekler" — İkisi de NEEDS-SINAN olarak kalır ve etkinleştirilmez. |
| 48.8 | ALS-D102 fazla teslim: "adet/metre satırlarına tolerans uygulanmaz" seçtin. Ancak ALS-D43, %1/%2/%3 toleransın adet ve metreye de uygulandığını söylüyor. Adet/metre satırında fazla teslim gelirse ne olsun? | **Her fazla onaya düşsün (Önerilen)** | "Fazla teslimde adet/metre için tolerans payı yok: en küçük fazla bile uyarıyla Satın Alma Müdürü onayına düşer. ALS-D43 yalnız bu noktada güncellenir; eksik teslim ve diğer durumlar aynı kalır." Seçilmeyen: "ALS-D43 aynen kalsın" — Cevabımı geri alıyorum: adet/metre satırlarına da %1/%2/%3 tolerans uygulanır. "Fazla reddedilsin" — Adet/metre satırında fazla teslim kabul edilmez. |
| 48.9 | ALS-D102: Kararda tek bir "kg toleransı" geçiyor. Bu, %1/%2/%3 bantlarından hangisi olsun? | **%1 uyarı, %2 onay, %3 üstü onay (Önerilen)** | "%1'e kadar sessiz kabul, %1–%2 arası uyarıyla kabul, %2 üstü onaya düşer. Blok bandı fazla teslim için kalkar (ALS-D102 'reddedilmez' dediği için)." Seçilmeyen: "Tek değer: %2" — %2'ye kadar kabul, üstü uyarıyla onaya düşer. "Tek değer: %1" — %1'e kadar kabul, üstü uyarıyla onaya düşer. |

**Netleştirme notu:** 48.5'teki "%1/%2/%3" anlatımı 48.9 ile netleşti. kg satırında fazla teslim için yalnız iki
eşik vardır: uyarı eşiği ve onay eşiği. %3 blok bandı fazla teslimde uygulanmaz. 48.5'teki "adet/metre satırlarına
tolerans uygulanmaz" ifadesi de 48.8 ile netleşti: tolerans payı yoktur, ama fazla teslim reddedilmez, onaya düşer.

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D98 | Sipariş "günün kuru" (48.1; FNS-D79'u tamamlar) | FNS-D79'da siparişe otomatik gelen "günün kuru", **işlem tarihindeki TCMB döviz satış kurudur**. Satış faturasındaki gibi varsayılan olarak gelir. FNS-D79'un geri kalanı aynen geçerlidir. | Sipariş kur alanı, kur kaynağı okuması |
| KLN-D111 | Uygulama içi bildirim saklama (48.2; KLN-D110'u tamamlar) | Bildirim saklama süresi **Ayarlar'dan** gelir; başlangıç değeri **1 yıl**dır. Süresi dolan bildirimin **kalıcı silinmesi kapalı başlar**; açılması ayrı bir onay ister (insan kapısı). KLN-D110'un uygulama içi bildirim ve "ayrım bırakıldı" işareti aynen geçerlidir; e-posta kanalı açılmaz. | Bildirim deposu, Ayarlar |
| FNS-D99 | Kredi geri ödeme planı doğrulaması (48.3; FNS-D94'ü tamamlar) | Sistem iki eşitliği doğrular: (1) her taksit için **taksit = anapara + faiz + BSMV + KKDF**; (2) **anapara toplamı = kredi tutarı**. Tolerans **satır başına 0,01 TL**'dir. Eşitlikleri tutmayan plan **onaya gönderilemez**. Hesap backend calculator'ındadır; ekran aynı sonucu gösterir. FNS-D94'ün "sistem yalnız toplamları doğrular" kuralı aynen geçerlidir: taksit satırları elle girilir, sistem satır üretmez. | Kredi planı kaydı ve onaya gönderme, finans calculator'ı |
| URT-D107 | Negatif kalan kg (48.4; URT-D99'u tamamlar) | URT-D99'daki kalan kg (planlanan girdi kg − kayıtlı çıktı ve tüketim) eksiye düşerse kapasite yükünde **0 kg** sayılır. Plan **"planı aştı"** işaretiyle gösterilir. Kayıt engellenmez. | Üretim kapasite yükü hesabı ve ekranı |
| ALS-D114 | Fazla teslim toleransı (48.5, 48.8, 48.9; ALS-D102'yi tamamlar, fazla teslimde ALS-D43'ü günceller) | 1. **kg satırı:** fazla teslim oranı uyarı eşiğine kadar sessiz kabul edilir; uyarı eşiği ile onay eşiği arasında uyarıyla kabul edilir; onay eşiğinin üstünde ALS-D102'deki gibi uyarıyla **Satın Alma Müdürü ve üstü onayına** düşer. Başlangıç değerleri ALS-D43'teki %1 (uyarı) ve %2 (onay) eşikleridir. **%3 blok bandı fazla teslimde uygulanmaz**; fazla teslim reddedilmez (ALS-D102). 2. **Adet ve metre satırı:** fazla teslimde tolerans payı yoktur; **en küçük fazla bile** uyarıyla aynı onaya düşer. ALS-D43 yalnız bu noktada güncellenir; eksik teslim ve diğer durumlarda ALS-D43 aynen geçerlidir. 3. **Okuma zamanı:** eşikler Ayarlar değeridir ve **teslim girişi anında** okunur. Bu, ALS-D102 PR'ındaki "onay anında okunur" yazar okumasının yerine geçer. 4. **Değer yoksa:** Ayarlar'da eşik değeri yoksa teslim kaydı **reddedilir** (fail-closed). 5. **OPEN-004 açık kalır**; bu belge onu kapatmaz. | Alış teslim girişi, fazla teslim onayı, Ayarlar |
| FNS-D100 | Kredi izinleri (48.6; FNS-D93'ü tamamlar) | İki yeni izin adı açılır: **`finance.credits.propose`** (öneri; kişi: Finans Uzmanı) ve **`finance.credits.approve`** (onay; kişi: Finans Müdürü / Genel Müdür). **Öneren aynı kaydı onaylayamaz** (maker ≠ checker). Bu belge izinleri yalnız adlandırır; **grant, rol ataması ve Critical sınıfı yapılmaz** (insan kapısı). | Finans izin kaydı (ErpPermissions, modül manifesti), kredi öneri/onay komutları |
| FNS-D101 | Açılış bakiyesi düzeltmesi (48.7; FNS-D85 yazar okumasını teyit eder) | FNS-D85'teki yazar okuması karar olur: ters kayıt ve yeni öneri de **yalnız açılış dönemi ayarı açıkken** yapılabilir; kural FNS-D23 ile aynıdır. FNS-D85'in geri kalanı (ters kayıt + yeni öneri, giren onaylayamaz, "Açılış" türü) aynen geçerlidir. Açılış dönemi kapandıktan sonra düzeltmenin nasıl yapılacağı karara bağlanmamıştır (Sınırlar). | FIN-S9 açılış bakiyesi |
| URT-D108 | Üretimden iade üst sınırı formülü (48.7; URT-D95 yazar okumasını teyit eder) | URT-D95'teki yazar okuması karar olur: "çekilen − tüketilen" kuralı, koddaki **çekilen − önceki iadeler − net çıktı − son yarı mamul (WIP)** hesabıyla uygulanır. Sorudaki "tüketilen = önceki iadeler − net çıktı − WIP" kısaltması, çekilenden düşülen bu üç kalemi sayar. "Net çıktı" URT-D95'teki gibi bütün çıktı sınıflarını kapsar. Hesap **formül golden'ında sabitlenir** ve backend calculator'ındadır. URT-D95'in boya emri toplamı kapsamı bu teyide girmez (Sınırlar). | Üretim iade komutu (URT-S6/S2), formül golden'ı |

Uygulama ayrıntısı (geri alınabilir): FNS-D99'daki eşitlik hatası, URT-D107'deki "planı aştı" işareti ve
ALS-D114'teki "değer yok" reddi için hata/durum kodlarının adları kodlayan şeridin seçimidir.

## Sınırlar

- **URT-D95 boya emri kapsamı:** rulo bazlı süreçlerde ana ruloda, boyada (URT-D49) emir toplamında ölçülmesi
  bu belgede de sorulmadı → `NEEDS-SINAN` olarak kalır.
- **FNS-D85 dönem sonrası düzeltme:** açılış dönemi kapandıktan sonra açılış kredisinin nasıl düzeltileceği
  karara bağlanmamıştır → `NEEDS-SINAN`.
- **Yeni izinlerin Critical sınıfı ve grant'ı:** `finance.credits.propose` / `finance.credits.approve` için
  Critical sınıfı ve rol/grant ataması seçilmemiştir → `NEEDS-SINAN`; ataması insanda kalır.
- **ALS-D114 eşik değerleri:** bu belge Ayarlar'daki eşiklerin ürün veya grup bazındaki değerlerini seçmez;
  başlangıç değerleri ALS-D43'tekilerdir. Satın Alma Müdürü onay izninin adı (mevcut izin mi, yeni izin mi)
  seçilmemiştir → `NEEDS-SINAN`. OPEN-004 açık kalır.
- **KLN-D111 kalıcı silme:** silmenin açılması ayrı onay ister; bu belge silmeyi açmaz. Saklama/imha politikası
  ayrı politikadır.
- İnsan kapıları değişmez:
  - deployment, environment ve secret;
  - Settings → Actions etkinleştirmesi;
  - grant/rol/Critical;
  - production aktivasyonu;
  - `OPEN-*` kapatma;
  - `.claude/settings.json`.
