# Sinan Kararları — 2026-11-16 (Dalga 4 ek 21: açılış bakiyesi, banka eksi bakiye ve kredi alanı, resmi satış faturası kaynakları, resmi unvan onay zinciri, alış siparişinde cari değişikliği)

Bu belge önceki karar dosyalarını silmez. Şu kararları ekler: FNS-D84…FNS-D90 ve ALS-D111…ALS-D112. Her karar,
tamamladığı veya güncellediği önceki kimliği açıkça yazar. Burada anılmayan önceki kararlar aynen geçerlidir.

Atıf yapılan kimliklerin bir kısmı henüz merge edilmemiş karar PR'larındadır. Bu belge onlara o PR'lardaki metinle atıf
yapar:

| Kimlik | Dosya | PR |
|---|---|---|
| ALS-D98 | `SINAN_KARARLARI_2026-11-15_DALGA2_SORULARI.md` | #1164 |
| FNS-D58 | `SINAN_KARARLARI_2026-11-15_DALGA2_EK4.md` | #1168 |
| FNS-D75, FNS-D76 | `SINAN_KARARLARI_2026-11-15_DALGA2_EK12.md` | #1176 |
| KLN-D104, FIN-S9 bağlamı | `SINAN_KARARLARI_2026-11-16_DALGA4_EK15.md` | #1179 |
| ALS-D108, ALS-D110 | `SINAN_KARARLARI_2026-11-16_DALGA4_EK20.md` | #1186 |

Bu belge o PR'lardan sonra merge edilir. Çelişkide daha yeni olan bu belge geçerlidir.

**Kaynak:** Sinan, 16 Kasım 2026, Claude Code oturumunda soru penceresiyle cevapladı: SINAN-CEVAP 45 (Issue #66 yorumu
6911663004, 8 satır) ve SINAN-CEVAP 46 (Issue #66 yorumu 6911840954, 4 satır). Sorular, açık karar PR'larındaki
`NEEDS-SINAN` maddelerinden ve FIN-S9 açılış bakiyesi dilimi hazırlığından geldi. "(Önerilen)" işaretleri soranındır.
Serbest metin cevaplar aynen alıntılanmıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 45.1 | FIN-S9 açılış bakiyesi: onaylanan açılış neyi etkiler? | **Hepsi (Önerilen)** | "Kasa/banka, cari ve açık kalem için tek açılış hareketi; iki kez üretilmez." |
| 45.2 | Banka hesabı eksi bakiyeye düşebilir mi? | Serbest metin: "Kredilerin tamamını düşünerek bu nasıl olması gerekiyor, bence hesap eksiye düşmesin bunu ayrı bir kredi hesabında yönetsin, firma her türlü krediyi kullanabilir bunu araştır ve eklememiz gerekecek finans modülündeki kredilerde" | Seçenek seçilmedi; cevap serbest metindir. Seçilmeyenler: "Yalnız kredili hesapta (Önerilen)"; "Her banka hesabında"; "Hayır, yalnız pozitif". |
| 45.3 | Onaylı açılış bakiyesinin düzeltilmesi | **Ters kayıt + yeni öneri (Önerilen)** | "Silme yok; ters kayıt; uzman yeni öneri girer, müdür onaylar; raporlarda "Açılış" türü görünür." Seçilmeyenler: "Yalnız yeni öneri (fark)"; "Açılış dönemi açıkken düzeltilebilir". |
| 45.4 | Açılış tarihi (AsOfDate) ve ayar sürümü | **İş günü + yeniden onay (Önerilen)** | "Şirket iş günü, Europe/Istanbul (FNS-D75 gibi); öneriden sonra açılış dönemi ayarı değiştiyse onay/ret reddedilir." Seçilmeyenler: "İş günü, sürüm önemsiz"; "Serbest tarih". |
| 45.5 | Finans modülündeki kredi alanı hangi kredi gruplarını kapsar? (çoklu seçim) | **Nakdi (Önerilen)**, **Gayrinakdi (Önerilen)**, **Senet/çek karşılığı + faktoring**, **Leasing** — dördü de seçildi | Nakdi: "spot/rotatif, taksitli ticari, KMH/ticari esnek hesap ayrı kredi hesabı olarak, dövize endeksli, Eximbank/ihracat, kredi kartı; ödeme planı, faiz/BSMV/KKDF, vade, limit." Gayrinakdi: "teminat mektubu, akreditif, kabul kredisi/aval; limit ve komisyon; nakit yalnız tazmin/ödemede." Senet/çek karşılığı + faktoring: "iskonto, çek karşılığı kredi, rücu'lu/rücu'suz; evrak riski FNS-D58 ile." Leasing: "sözleşme, kira planı, döviz taksitler." |
| 45.6 | FNS-D76 import: resmi satış faturasının TL karşılığı nereden alınır? | **İmporttaki TL alanı (Önerilen)** | "İmport dosyasındaki TL alanı kullanılır; yoksa "hesaplanamadı"." Seçilmeyenler: "Fatura tarihi TCMB döviz satış"; "Elle kayıt gelene kadar yok". |
| 45.7 | FNS-D76: bekleyen (onaylanmamış) elle kayıt importu engeller mi? | **Engellemez (Önerilen)** | "Yalnız onaylı elle kayıt üstündür; "bekleyen elle kayıt var" notu gösterilir." Seçilmeyen: "Engeller". |
| 45.8 | FNS-D76: import ile elle kayıt arasındaki fark uyarısı nerede gösterilir? | **Fatura detayı + onay anı (Önerilen)** | "Fatura detayında ve import önce geldiyse elle kaydın onayı anında uyarı." Seçilmeyenler: "Yalnız fatura detayı"; "Bildirim de gönder". |
| 46.1 | 44.2'deki "alış tarafındaki mevcut onay izni" cevabı | Serbest metin: "Bunu anlamadım bir carinin ünvan değişikliğini muhasebe onaylar alışla ne alakası var, eğerki bir alış siparişi içerisindeki cari değişiyorsa bunu alış müdürü onaylar" | Seçenek seçilmedi; cevap serbest metindir. 44.2 cevabını düzeltir. |
| 46.2 | Resmi unvan değişikliğinin izin ve adım yapısı | Serbest metin: "Önerilen evet ama burda onay zinciri kırılmasın, muhasebe uzmanı girer muhasebe müdürü ve genel müdür onaylar" | Önerilen seçenek: "Muhasebe tek adımda yapar; … cari modülünde ayrı 'resmi unvan değiştir' izni açılır; grant insanda". Cevap önerileni kabul eder, tek adımı iki adımlı onay zinciriyle değiştirir. Diğer seçenek: "Biri girer, muhasebe onaylar (iki adım)". |
| 46.3 | Onayda Muhasebe Müdürü ve Genel Müdür ikisi de mi gerekir? | **Biri yeter (Önerilen)** | "Muhasebe Müdürü VEYA Genel Müdür onaylar; giren kendi kaydını onaylayamaz." Seçilmeyen: "İki kademe: önce müdür, sonra GM". |
| 46.4 | Alış siparişinde carinin değişmesi | **Alış Müdürü onaylar (Önerilen)** | "Siparişteki cari değişikliği `purchasing.orders.approve` onayına gider; onaylı sipariş yeniden onay ister." Seçilmeyen: "Yalnız taslakta değişir". |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D84 | Açılış bakiyesinin etkisi (45.1; FNS-D23'ü tamamlar) | Onaylanan açılış bakiyesi kasa/banka, cari ve açık kalem için **tek açılış hareketi** üretir. Aynı açılış ikinci kez hareket üretmez (idempotency). FNS-D23 (yalnız açılış dönemi ayarı açıkken; Finans Uzmanı girer, Finans Müdürü onaylar; giren onaylayamaz; dönem kapanınca ekran gizlenir) aynen geçerlidir. | FIN-S9 açılış bakiyesi; kasa/banka, cari, açık kalem |
| FNS-D85 | Onaylı açılış bakiyesinin düzeltilmesi (45.3; FNS-D23'ü tamamlar) | Onaylı açılış silinmez. Düzeltme **ters kayıt + yeni öneri** ile yapılır: ters kayıt önceki açılışın etkisini geri alır; Finans Uzmanı yeni öneri girer, Finans Müdürü onaylar (giren onaylayamaz). Raporlarda bu hareketler **"Açılış"** türüyle görünür. Yazar okuması (FNS-D23'ten çıkarılmıştır; 45.3 cevabında yoktur; geri alınabilir): ters kayıt ve yeni öneri de yalnız açılış dönemi ayarı açıkken yapılabilir. Açılış dönemi kapandıktan sonra düzeltmenin nasıl yapılacağı `NEEDS-SINAN`'dır (Sınırlar). | FIN-S9 açılış bakiyesi, raporlar |
| FNS-D86 | Açılış tarihi ve ayar sürümü (45.4; FNS-D75 ile aynı gün kuralı) | AsOfDate **şirket iş günüdür** (Europe/Istanbul), UTC değildir (FNS-D75 gibi). Öneri, girildiği andaki açılış dönemi ayarının sürümünü taşır; öneriden sonra bu ayar değiştiyse **onay ve ret reddedilir**; öneri yeniden gözden geçirilir. | FIN-S9 açılış bakiyesi, açılış dönemi ayarı |
| FNS-D87 | Banka hesabında eksi bakiye (45.2; FNS-D11'in KMH/BCH istisnasını kaldırır, FNS-D4 ve FNS-D22'yi günceller) | Serbest metin cevap (Soru ve cevaplar, 45.2). Banka hesabı **eksi bakiyeye düşmez**. Kredi kullanımı banka hesabında eksi bakiye olarak değil, **ayrı kredi hesaplarında** yönetilir (KMH/ticari esnek hesap dahil; 45.5). Kredi alanı uygulanana kadar banka hesabına eksiye düşürecek hareket kabul edilmez. **FNS-D11** (`SINAN_KARARLARI_2026-11-02_FINANS.md`) eksi bakiye engeliyle aynen geçerlidir; FNS-D11'deki **"KMH/BCH gibi kredili hesaplar hariç, tanımlı limit içinde" istisnası bu kararla kalkar**. **FNS-D22** (`SINAN_KARARLARI_2026-11-11_FINANS_F005.md`; kontrol kayıt anındaki güncel bakiyeyle yapılır, geçmiş günlerde eksiye düşme kaydı reddetmez, raporda uyarı olur) aynen geçerlidir; yalnız FNS-D22'deki "KMH/BCH limit istisnası (FNS-D4, FNS-D11) aynen kalır" cümlesi bu kararla düşer. **FNS-D4**'teki KMH/BCH kredi türü olarak kalır, ancak banka hesabı türü değil ayrı kredi hesabıdır (FNS-D88). | Finans banka hesapları |
| FNS-D88 | Finans modülünde kredi alt alanı — kapsam (45.2, 45.5; FNS-D4'ü genişletir/günceller) | Finans modülüne **kredi alt alanı** eklenir. Kapsam, seçilen dört grubun tamamıdır: **nakdi** (spot/rotatif, taksitli ticari, KMH/ticari esnek hesap ayrı kredi hesabı olarak, dövize endeksli, Eximbank/ihracat, kredi kartı; ödeme planı, faiz/BSMV/KKDF, vade, limit), **gayrinakdi** (teminat mektubu, akreditif, kabul kredisi/aval; limit ve komisyon; nakit yalnız tazmin/ödemede), **senet/çek karşılığı + faktoring** (iskonto, çek karşılığı kredi, rücu'lu/rücu'suz; evrak riski FNS-D58 ile) ve **leasing** (sözleşme, kira planı, döviz taksitler). Bu karar **FNS-D4'ü genişletir/günceller**: FNS-D4'te seçilen kredi türleri (taksitli ticari kredi, rotatif/spot, KMH/BCH, Eximbank/akreditif; ek cevap "Hepsini") bu kapsamın içindedir; KMH/BCH ayrı kredi hesabı olarak yönetilir (FNS-D87). Bu karar **yalnız kapsam kararıdır**; izinler, personalar, muhasebe yansıması, faiz/BSMV/KKDF hesap yöntemi ve limit kuralları ayrı bir spec'te sorulur (`NEEDS-SINAN`). Bu ayrıntılar karara bağlanmadan kredi hareketi etkinleştirilmez. | Finans kredi alt alanı (yeni) |
| FNS-D89 | Resmi satış faturasında importun TL karşılığı (45.6; FNS-D76'yı tamamlar) | CSV v2 import (FIN-S5) ile gelen resmi satış faturasının TL karşılığı **import dosyasındaki TL alanından** alınır. TL alanı yoksa TL karşılığı **"hesaplanamadı"** gösterilir; sistem kendi kurla hesaplamaz. | API-35 resmi satış faturası, FIN-S5 import |
| FNS-D90 | Bekleyen elle kayıt ve fark uyarısı (45.7, 45.8; FNS-D76 ve KLN-D104'ü tamamlar) | Bekleyen (onaylanmamış) elle kayıt importu **engellemez**; yalnız onaylı elle kayıt importtan üstündür (KLN-D104). Bekleyen elle kayıt varken fatura detayında **"bekleyen elle kayıt var"** notu gösterilir. İmport ile elle kayıt arasında alan farkı varsa uyarı **fatura detayında** ve import önce geldiyse **elle kaydın onayı anında** gösterilir; ayrı bildirim gönderilmez. Fark uyarısının denetim kaydı (KLN-D104) aynen geçerlidir. | API-35 resmi satış faturası, elle kayıt onayı |
| ALS-D111 | Resmi unvan değişikliği: muhasebe işlemi, iki adımlı onay (46.1, 46.2, 46.3; ALS-D110'un yerine geçer, ALS-D95/ALS-D98/ALS-D108'i kısmen değiştirir) | Serbest metin cevaplar (Soru ve cevaplar, 46.1 ve 46.2). Cari resmi unvan değişikliği **muhasebe işlemidir**; alış izinleriyle ilgisi yoktur. **ALS-D110 bu kararla geçerli olmaz** (44.2 cevabının "alış tarafındaki mevcut onay izni" kısmı dahil); ALS-D110'daki `NEEDS-SINAN` (iki alış onay izninden hangisi) düşer. Değişiklik **iki adımlıdır (maker-checker)**: **Muhasebe Uzmanı** girer; **Muhasebe Müdürü VEYA Genel Müdür** onaylar (biri yeter); giren kendi kaydını onaylayamaz. Onaylanana kadar resmi unvan değişmez. Bu karar ALS-D95, ALS-D98 ve ALS-D108'deki **"onaysız, tek adım"** kısmını değiştirir: ALS-D108'deki Muhasebe Müdürü ve Genel Müdür artık onaylayan personalardır, giren persona Muhasebe Uzmanıdır. Bu kararların geri kalanı (expected version, idempotency, denetim kaydı) aynen geçerlidir. ALS-D98'in "mevcut cari düzenleme yetkisiyle" kısmı ALS-D108 ile zaten geçerli değildir; cari modülündeki ayrı izin ALS-D95/19.22'den ve 46.2'de kabul edilen önerilen seçenekten gelir. Giriş ve onay için cari modülünde **ayrı izin** gerekir (46.2'de kabul edilen önerilen seçenek); yeni izin adlarının açılması ve grant'leri insan kapısında / ayrı karardadır. İzinler açılıp grant edilene kadar bu işlem etkinleştirilmez. | Cari (Counterparty) resmi unvan, onay akışı |
| ALS-D112 | Alış siparişinde cari değişikliği (46.1, 46.4) | Alış siparişindeki carinin değişmesi **`purchasing.orders.approve` onayına** gider (Alış/Satınalma Müdürü onaylar). **Onaylı siparişte** cari değişikliği siparişi yeniden onaya düşürür. Mevcut izin kullanılır; yeni izin adı açılmaz. Bu değişiklik carinin resmi unvanını değiştirmez (ALS-D111 ayrıdır). | Satınalma siparişi, onay akışı |

## Sınırlar

- Bu belge rol açmaz ve grant ataması yapmaz. ALS-D111 cari modülünde ayrı giriş/onay izni gerektirir; yeni izin adlarının
  açılması ve grant'leri insan kapısında / ayrı karardadır. ALS-D112 mevcut `purchasing.orders.approve` iznini kullanır.
- Açık kalan `NEEDS-SINAN` maddeleri:
  - FNS-D88: kredi alt alanının izinleri ve personaları, muhasebe yansıması, faiz/BSMV/KKDF hesap yöntemi, limit
    kuralları ve dört grubun ayrıntıları (ayrı spec).
  - ALS-D111: resmi unvan giriş ve onay izinlerinin adları ve grant'leri (insan kapısı / ayrı karar).
  - FNS-D85: açılış dönemi kapandıktan sonra onaylı açılış bakiyesinin nasıl düzeltileceği ("yalnız dönem açıkken"
    kuralı yazar okumasıdır, FNS-D23'ten çıkarılmıştır).
- FNS-D87, FNS-D11'in KMH/BCH istisnasını ve FNS-D22'deki ona ilişkin cümleyi kaldırır; FNS-D88 FNS-D4'ü genişletir.
- ALS-D110'daki `NEEDS-SINAN` (iki alış onay izninden hangisi) ALS-D111 ile düşmüştür.
- E-posta bildirim kanalı açılmaz; FNS-D90 ayrı bildirim göndermez.
- `OPEN-*` kararları bu belgeyle kapanmaz.
- İnsan kapıları değişmez:
  - deployment, environment ve secret;
  - Settings → Actions etkinleştirmesi;
  - grant/rol/Critical;
  - production aktivasyonu;
  - `OPEN-*` kapatma;
  - `.claude/settings.json`.
