# Sinan Kararları — 2026-11-16 (Dalga 4 ek 22: kur değişikliği gerekçesi, belge altı iskonto yuvarlaması, elle sıra taşıma, siparişe bağlanamayan çevrim, finans kredi alanı, resmi unvan izinleri)

Bu belge önceki karar dosyalarını silmez. Şu kararları ekler: FNS-D91…FNS-D97, SAT-D407, URT-D106 ve ALS-D113. Her
karar, cevapladığı `NEEDS-SINAN` maddesini ve tamamladığı veya güncellediği önceki kimliği açıkça yazar. Burada
anılmayan önceki kararlar aynen geçerlidir.

Atıf yapılan kimlikler şu karar PR'larıyla main'e girmiştir. Bu belge onlara o PR'lardaki metinle atıf yapar:

| Kimlik | Dosya | PR |
|---|---|---|
| URT-D101 | `SINAN_KARARLARI_2026-11-16_DALGA4_EK16.md` | #1180 |
| FNS-D80, FNS-D83, SAT-D403, URT-D103, URT-D105 | `SINAN_KARARLARI_2026-11-16_DALGA4_EK20.md` | #1186 |
| FNS-D87, FNS-D88, ALS-D111 | `SINAN_KARARLARI_2026-11-16_DALGA4_EK21.md` | #1187 |

Bu belge #1180, #1186 ve #1187'den sonra merge edilir. Çelişkide daha yeni olan bu belge geçerlidir.

**Kaynak:** Sinan, 16 Kasım 2026, Claude Code oturumunda soru penceresiyle cevapladı: SINAN-CEVAP 47 (Issue #66 yorumu
6912114197, 10 satır). Sorular, EK20 ve EK21'de açık kalan `NEEDS-SINAN` maddelerinden geldi. "(Önerilen)" işaretleri
soranındır. Serbest metin cevap aynen alıntılanmıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 47.1 | Dövizli alış siparişinde kur onayda sabitlenir (FNS-D80). Onaydan önce hazırlayan otomatik gelen kuru elle değiştirirse gerekçe yazması zorunlu olsun mu? | **Gerekçe opsiyonel** | "Gerekçe alanı ekranda olur ama boş bırakılabilir. Değişiklik yine de denetim kaydına girer." Seçilmeyenler: "Gerekçe zorunlu (Önerilen)" — "Hazırlayan otomatik kuru değiştirirse kısa bir gerekçe yazmak zorunda. Gerekçe denetim kaydına ve onaycının ekranına düşer."; "Gerekçe yok" — "Gerekçe alanı hiç olmaz; yalnız eski ve yeni kur denetim kaydına yazılır." |
| 47.2 | Belge altı iskonto satırlara satır tutarına oranla dağıtılıyor (SAT-D403). Dağıtımda yuvarlamadan kuruş farkı kalırsa bu fark hangi satıra yazılsın? | **En büyük satıra (Önerilen)** | "Her satır kuruşa yuvarlanır. Kalan kuruş farkı tutarı en büyük satıra eklenir; tutarlar eşitse ilk satıra. Belge toplamı her zaman tam tutar." Seçilmeyenler: "Son satıra" — "Her satır kuruşa yuvarlanır, kalan fark belgenin son satırına eklenir."; "En büyük kalan yöntemi" — "Yuvarlamada atılan küsuru en büyük olan satırlara birer kuruş dağıtılır. En adil yöntem, ama hesabı takip etmek zor." |
| 47.3 | Üretim kuyruğunda bir planı elle öne veya arkaya taşımayı kim yapabilsin (URT-D103)? | **Yalnız Üretim Müdürü** | "Taşıma için yeni ayrı bir izin açılır ve yalnız Üretim Müdürü personasına verilir. Grant adımı insan kapısında kalır." Seçilmeyenler: "Plan onay izni (Önerilen)" — "Öncelik puanını giren mevcut `manufacturing.plans.approve` izni taşımayı da yapar. Yeni izin açılmaz; her taşıma gerekçe ister ve denetim kaydına girer."; "Elle taşıma olmasın" — "Sıra yalnız öncelik puanıyla belirlenir; sırayı değiştirmek isteyen puanı değiştirir." |
| 47.4 | Bir alış girişinin döviz maliyeti siparişe hiç bağlanamıyorsa hangi kurla TL'ye çevrilsin (FNS-D83)? | **Giriş tarihi TCMB satış (Önerilen)** | "Stoğa giriş tarihindeki TCMB döviz satış kuru kullanılır, URT-D105 ile aynı kur türü. Kur bulunamazsa maliyet 'hesaplanamadı' olur ve uyarı çıkar." Seçilmeyenler: "Fatura tarihi kuru" — "Faturanın tarihindeki TCMB döviz satış kuru kullanılır. Fatura gelene kadar maliyet geçici kalır."; "Elle girilsin" — "Kur kullanıcıdan istenir; girilmezse maliyet 'hesaplanamadı' kalır." |
| 47.5 | Kredi alt alanında (FNS-D88) kredi kaydını kim girsin, kim onaylasın? | **Uzman girer, Müdür/GM onaylar (Önerilen)** | "Finans Uzmanı kredi kaydını ve geri ödeme planını girer. Finans Müdürü veya Genel Müdür onaylar; biri yeter, kaydı giren onaylayamaz." Seçilmeyenler: "Yalnız Genel Müdür onaylar" — "Finans Uzmanı girer, yalnız Genel Müdür onaylar."; "Müdür girer ve onaylar" — "Finans Müdürü tek adımda girer ve onaylar; ayrı bir onay adımı olmaz." |
| 47.6 | Kredi faizi ve BSMV/KKDF tutarları nasıl belirlensin? | **Banka planı içe alınır (Önerilen)** | "Bankanın verdiği geri ödeme planı (taksit, anapara, faiz, BSMV, KKDF) aynen girilir veya içe aktarılır. Sistem yalnız toplamları doğrular, faizi kendisi hesaplamaz." Seçilmeyenler: "Sistem hesaplar" — "Oran, vade ve yöntem girilir; plan ortak backend hesaplayıcıda üretilir. Hesap yöntemlerinin ayrı ayrı kararı ve golden testi gerekir."; "İkisi de" — "Varsayılan olarak sistem hesaplar, banka planı gelince sistemin planının yerine geçer." |
| 47.7 | Banka kredi limitleri sistemde izlensin mi? | **İzlensin, uyarı versin (Önerilen)** | "Banka ve kredi türü bazında limit girilir. Kullanım limiti aşarsa kayıtta uyarı çıkar ama kayıt engellenmez." Seçilmeyenler: "İzlensin, engellesin" — "Limit aşılırsa kredi kaydı onaya gönderilemez."; "İzlenmesin" — "Limitler sistemde tutulmaz, yalnız kullanılan kredi raporlanır." |
| 47.8 | Dört kredi grubundan hangisi ilk teslimde gelsin? | Serbest metin: "Hepsi" | Seçenek seçilmedi; cevap serbest metindir: dört grup ilk teslimde gelir. Seçilmeyenler: "Nakdi kredi (Önerilen)" — "Rotatif, spot, taksitli ticari kredi ve KMH."; "Çek/senet + faktoring" — "Çek/senet kırdırma ve faktoring ilk gelir."; "Gayrinakdi" — "Teminat mektubu ve akreditif ilk gelir."; "Leasing" — "Finansal kiralama sözleşmeleri ve taksitleri ilk gelir." |
| 47.9 | Resmi unvan değişikliği (ALS-D111) için iki yeni izin açılsın mı: `counterparties.legal_name.propose` ve `counterparties.legal_name.approve`? | **Bu iki adla açılsın (Önerilen)** | "Muhasebe Uzmanı persona listesine giriş izni, Muhasebe Müdürü ve Genel Müdür persona listesine onay izni önerilir. Gerçek grant sizde kalır." Seçilmeyen: "Mevcut cari onay izni kullanılsın" — "Yeni izin açılmaz; cari modülündeki mevcut onay izni kullanılır. Ayrım daha zayıf olur." |
| 47.10 | Kredi kullanımı, geri ödemesi ve faiz tahakkuku muhasebeye nasıl yansısın (FNS-D88)? | **Muhasebe fişi önerisi (Önerilen)** | "Her kredi olayı mevcut muhasebe fişi akışına öneri olarak düşer; muhasebe onaylar. Hesap kodları Ayarlar'dan eşlenir, eşleme yoksa fiş 'eşleme eksik' durumunda bekler." Seçilmeyenler: "Otomatik fiş" — "Onaylanan kredi olayı doğrudan kesin muhasebe fişi oluşturur."; "Şimdilik yansımasın" — "Kredi yalnız Finans'ta izlenir; muhasebe yansıması sonra eklenir." |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D91 | Onay öncesi kur değişikliğinin gerekçesi (47.1; FNS-D80'i tamamlar) | Dövizli alış siparişinde hazırlayan, onaydan önce otomatik gelen kuru elle değiştirirse **gerekçe alanı gösterilir ama opsiyoneldir**; boş bırakılabilir. Her değişiklik (eski kur, yeni kur, gerekçe varsa gerekçe) denetim kaydına girer. FNS-D80'in geri kalanı (kur zorunlu, onayda sabit, sonrası yalnız revizyonla, kur yoksa onaya gönderilemez) aynen geçerlidir. FNS-D80'deki gerekçe `NEEDS-SINAN`'ı bu kararla kapanır. | Alış siparişi (dövizli), denetim kaydı |
| SAT-D407 | Belge altı iskonto dağıtımında yuvarlama (47.2; SAT-D403'ü tamamlar) | Belge altı iskontosu satır tutarına oranla dağıtılırken her satırın payı **kuruşa yuvarlanır**; yuvarlamadan kalan fark **tutarı en büyük satıra** eklenir, en büyük tutar birden fazla satırdaysa **ilk satıra**. Dağıtılan payların toplamı her zaman belge altı iskonto tutarına eşittir. Hesap deterministic backend calculator'da yapılır ve formül golden'ı bu kuralla yazılır; ekran yeniden hesaplamaz. SAT-D403'teki yuvarlama `NEEDS-SINAN`'ı bu kararla kapanır. | Stok/Alış maliyet çözücüsü, formül golden |
| URT-D106 | Elle sıra taşımanın izni (47.3; EK16 URT-D101'i (#1180) günceller) | Üretim kuyruğunda planı elle öne/arkaya taşımak **ayrı yeni bir izinle** yapılır ve bu izin **yalnız Üretim Müdürü** personasına önerilir. **Bu karar EK16 URT-D101'i (#1180) günceller:** elle taşıma mevcut `manufacturing.plans.approve` izni yerine ayrı yeni izinle yapılır; URT-D101'in geri kalanı (her taşıma denetime yazılır, rol/grant ataması yapılmaz) aynen geçerlidir. İzin adı açılır; **grant yapılmaz** (grant/rol/Critical insan kapısında). Grant yapılana kadar elle taşıma fiilen kapalı kalır. Her taşıma denetim kaydına girer. URT-D103 (EK20, #1186) elle taşıma iznini URT-D101'e bağlar ve iznin bu kararla değiştiğini yazar; URT-D103'teki öncelik puanını `manufacturing.plans.approve` girer kuralı aynen sürer. Uygulama ayrıntısı (geri alınabilir): izin adı `manufacturing.plans.reorder`. | Üretim kuyruk/kapasite hesaplayıcısı, izin kataloğu |
| FNS-D92 | Siparişe bağlanamayan girişin döviz maliyet çevrimi (47.4; FNS-D83'ü tamamlar) | Alış girişi siparişe hiç bağlanamıyorsa (siparişsiz giriş veya bağı kopmuş satır) döviz maliyeti **stoğa giriş tarihindeki TCMB döviz satış kuruyla** TL'ye çevrilir (URT-D105 ile aynı kur türü). Kur bulunamazsa maliyet **"hesaplanamadı"** olur ve uyarı çıkar; tahmini kur kullanılmaz. FNS-D83'teki bağlanamayan çevrim `NEEDS-SINAN`'ı bu kararla kapanır. | Stok/Alış maliyet çözücüsü |
| FNS-D93 | Kredi kaydının giriş ve onayı (47.5; FNS-D88'i tamamlar) | Kredi kaydını ve geri ödeme planını **Finans Uzmanı** girer; **Finans Müdürü veya Genel Müdür** onaylar (biri yeter); kaydı giren onaylayamaz (maker-checker). Uygulama ayrıntısı / yazar okuması (47.5 persona ve maker-checker'ı seçer, yeni izin açılıp açılmayacağını söylemez; geri alınabilir): giriş ve onay için kredi alanında ayrı izinler açılır; **grant yapılmaz** (insan kapısı). Uygulama ayrıntısı (geri alınabilir): izin adları `finance.credits.propose` ve `finance.credits.approve`. | Finans kredi alt alanı, izin kataloğu |
| FNS-D94 | Faiz, BSMV ve KKDF tutarları (47.6; FNS-D88'i tamamlar) | Bankanın geri ödeme planı (taksit, anapara, faiz, BSMV, KKDF) **aynen girilir veya içe aktarılır**. Sistem faizi **hesaplamaz**; **sistem yalnız toplamları doğrular** (47.6). Doğrulama deterministic backend calculator'dadır. Hangi toplamların ve eşitliklerin doğrulanacağı, banka planındaki başka kalemlerin (masraf, komisyon) ve kredi gruplarının (gayrinakdi, KMH, leasing) nasıl ele alınacağı, kuruş toleransı ve tutmayan planın engellenip engellenmeyeceği cevapta yoktur; formül golden'ında Sinan teyidiyle belirlenir (`NEEDS-SINAN`, Sınırlar). Bu ayrıntılar tahminle etkinleştirilmez. FNS-D88'deki faiz/BSMV/KKDF yöntemi `NEEDS-SINAN`'ı yöntem kısmıyla (banka planı içe alınır, sistem hesaplamaz) kapanır. | Finans kredi alt alanı, içe aktarma |
| FNS-D95 | Kredi limitleri (47.7; FNS-D88'i tamamlar) | Limit **banka ve kredi türü bazında** girilir. Kullanım limiti aşarsa kayıtta **uyarı** çıkar; kayıt **engellenmez**. Uygulama ayrıntısı (geri alınabilir): uyarı onay ekranında da gösterilir. FNS-D88'deki limit `NEEDS-SINAN`'ı bu kararla kapanır. | Finans kredi alt alanı |
| FNS-D96 | İlk teslimin kapsamı (47.8; FNS-D88'i tamamlar) | Serbest metin cevap: "Hepsi". Dört kredi grubu — **nakdi, gayrinakdi, çek/senet + faktoring, leasing** — **ilk teslimde** gelir; grup sırası yoktur. FNS-D87 (banka hesabı eksiye düşmez, kredi ayrı kredi hesabında) aynen geçerlidir. | Finans kredi alt alanı, teslim planı |
| FNS-D97 | Kredinin muhasebe yansıması (47.10; FNS-D88'i tamamlar) | Kredi kullanımı, geri ödemesi ve faiz tahakkuku mevcut muhasebe fişi akışına **fiş önerisi** olarak düşer; muhasebe onaylar. Kesin fiş otomatik oluşmaz. Hesap kodları **Ayarlar'dan** eşlenir; eşleme yoksa öneri **"eşleme eksik"** durumunda bekler. Olay bir kez kaydedilir; aynı olay ikinci öneri üretmez (idempotency). FNS-D88'deki muhasebe yansıması `NEEDS-SINAN`'ı bu kararla kapanır. | Finans kredi alt alanı, muhasebe fişi akışı, Ayarlar |
| ALS-D113 | Resmi unvan değişikliği izinleri (47.9; ALS-D111'i tamamlar) | İki yeni izin açılır: giriş için **`counterparties.legal_name.propose`**, onay için **`counterparties.legal_name.approve`**. Persona önerisi: giriş izni Muhasebe Uzmanı; onay izni Muhasebe Müdürü ve Genel Müdür. **Grant yapılmaz** (insan kapısı); grant yapılana kadar unvan değişikliği kapalı kalır. ALS-D111'in iki adımlı zinciri (biri yeter; giren onaylayamaz) aynen geçerlidir. ALS-D111'deki izin adı `NEEDS-SINAN`'ı bu kararla kapanır; grant kısmı insan kapısında kalır. | Cari modülü, izin kataloğu |

## Sınırlar

- Bu belge **grant yapmaz ve rol açmaz**. İzinler: ALS-D113'teki `counterparties.legal_name.propose` ve
  `counterparties.legal_name.approve` adları 47.9 cevabıyla seçilmiştir. URT-D106'da ayrı yeni izin 47.3 cevabıyla
  seçilmiştir; `manufacturing.plans.reorder` adı uygulama ayrıntısıdır (geri alınabilir). FNS-D93'te ayrı izin açılması
  yazar okumasıdır; `finance.credits.propose` ve `finance.credits.approve` adları uygulama ayrıntısıdır (geri
  alınabilir). Persona önerileri yalnız öneridir; gerçek grant insan kapısındadır.
- Açık kalan `NEEDS-SINAN` maddeleri:
  - Yeni izinlerin Critical sınıfı (insan kapısı).
  - FNS-D94: plan doğrulamasında hangi toplamların/eşitliklerin doğrulanacağı, masraf/komisyon gibi ek kalemler, grup
    bazındaki farklar, kuruş toleransı ve tutmayan planın onaya gönderilmesinin engellenip engellenmeyeceği (formül
    golden'ında Sinan teyidiyle).
  - FNS-D93: kredi giriş/onayı için ayrı izin açılması (yazar okuması; 47.5 bunu söylemez).
  - Kredi gruplarının grup bazındaki ayrıntı alanları (ör. teminat mektubu türleri, faktoring rücu türü, leasing
    sözleşme alanları); ayrı kredi spec'inde sorulur. Bu ayrıntılar karara bağlanmadan ilgili grubun hareketi
    etkinleştirilmez.
- SINAN-CEVAP 43'te seçilmeyen tolerans ve iskonto tavanı değerleri `NEEDS-SINAN` olarak kalır (EK20).
- E-posta bildirim kanalı açılmaz.
- `OPEN-*` kararları bu belgeyle kapanmaz.
- İnsan kapıları değişmez:
  - deployment, environment ve secret;
  - Settings → Actions etkinleştirmesi;
  - grant/rol/Critical;
  - production aktivasyonu;
  - `OPEN-*` kapatma;
  - `.claude/settings.json`.
