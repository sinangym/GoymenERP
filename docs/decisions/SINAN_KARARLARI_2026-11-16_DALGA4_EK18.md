# Sinan Kararları — 2026-11-16 (Dalga 4 ek 18: hizmet/masraf tipi listesi, fason alış siparişi ve alış sipariş kuru)

Bu belge önceki karar dosyalarını silmez. Şu kararları ekler: KIM-D49, ALS-D105, ALS-D106 ve FNS-D79.
Açıkça güncellediği iki karar vardır:
- ALS-D79 (`SINAN_KARARLARI_2026-11-11_ALIS_EK15.md`): hizmet tipi ve masraf tipi listeleri için KIM-D49 ile
  güncellenir. ALS-D79'daki "işlem türü" listesi ve masraf yapısının ayrıntıları (`NEEDS-SPEC`) aynen kalır.
- FNS-D73 (SINAN-CEVAP 33, `SINAN_KARARLARI_2026-11-15_DALGA2_EK12.md`): alış siparişindeki kurun nasıl
  girileceği FNS-D79 ile tamamlanır. FNS-D73'ün geri kalanı aynen geçerlidir.

Şu kararlar aynen geçerlidir:
- ALS-D80 ve SAT-D302: ek alış masrafı v1'de ortalama maliyete girmez.
- ALS-D3: sipariş onayı.
- ALS-D41: TL borç kuru tedarikçi faturasındaki kurdur.
- SAT-D381 (SINAN-CEVAP 36, `SINAN_KARARLARI_2026-11-16_DALGA4_EK15.md`).

Diğer önceki kararlar da aynen geçerlidir.

EK12 (#1176) ve EK15 (#1179) henüz merge edilmemiş karar PR'larındadır. Bu belge onlara o PR'lardaki metinle atıf
yapar. Çelişkide daha yeni olan bu belge geçerlidir.

**Kaynak:** Sinan, 16 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 40, Issue #66
yorumu 6910907285). Sorular, Alış/Cari ekibinin ALC-S7 (fason hizmet alış siparişi) ve ALC-S8 (ek alış masrafı)
dilimlerini durduran `NEEDS-SINAN` maddelerinden geldi. 40.4'ün cevabı serbest metindir ve aynen alıntılanmıştır.
"(Önerilen)" işaretleri soranındır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 40.1 | Ayarlar'daki hizmet tipi ve masraf tipi listesi (KIM-S18) şimdi açılsın mı? Fason hizmet alış siparişi (ALC-S7) ve nakliye/gümrük gibi ek alış masrafı (ALC-S8) bu listeye bağlı. ALS-D79 'ortak masraf yapısının ayrıntıları yazılana kadar liste açılmaz' demişti; bu yüzden ikisi de duruyor. | **Şimdi açılsın (Önerilen)** | "Liste boş başlar, değerleri siz Ayarlar'dan girersiniz; ALS-D79 bu kadarıyla güncellenir. Ek masraf yine ortalama maliyete katılmaz (ALS-D80). ALC-S7 ve ALC-S8 5. dalgada kodlanır." Seçilmeyen: "Ayrıntılar yazılana kadar beklesin" — ALC-S7 ve ALC-S8 kapalı kalır; ortak masraf yapısının ayrıntıları ayrıca sorulur. |
| 40.2 | Dışarıya verilen fason işin alış siparişinde (ALC-S7) KDV ve tevkifat kodu nereden gelsin? Satış tarafında SAT-D381 'Ayarlar'da tek varsayılan, satırda değiştirilebilir' demişti. | **SAT-D381 gibi (Önerilen)** | "Ayarlar'da alış fasonu için tek varsayılan KDV/tevkifat kodu tutulur; satırda değiştirilebilir." Seçilmeyen: "Her satırda elle seçilsin" — Varsayılan yok; kullanıcı her satırda kodu seçer. |
| 40.3 | Fason hizmet alış siparişinde fiyat neye göre girilsin ve onay nasıl işlesin? | **kg başına, normal onay (Önerilen)** | "İşlenecek malın kg'ı başına birim fiyat. Onay eşiği ve 'hazırlayan onaylayamaz' kuralı normal alış siparişiyle aynı." Seçilmeyenler: "Hizmet birimi, normal onay" — İş/adet gibi hizmet birimi başına fiyat; onay normal alış siparişiyle aynı. "Satırda birim seçilsin" — kg veya hizmet birimi satırda seçilir; onay normal alış siparişiyle aynı. |
| 40.4 | FNS-D73: Dövizli alış siparişinde sipariş kuru nerede, kim tarafından ve ne zaman girilsin? | Serbest metin: **"Hazırlayan değiştirebilir ama günün kuru otomatik varsayılan satış faturası ile aynı mantık"** | Seçeneklerin hiçbiri seçilmedi. Sunulan seçenekler: "Hazırlayan girer, onayda kilit (Önerilen)" — Siparişi hazırlayan, sipariş oluşturulurken kuru girer (günün kuru önerilir). Dövizli siparişte zorunludur. Onaydan sonra değişmez; değişiklik için sipariş revize edilir. "Onaylayan girer" — Kuru onaylayan kişi onay anında girer; onaydan sonra değişmez. "Zorunlu olmasın" — Kur boş bırakılabilir; boşsa fatura kuru kullanılır. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KIM-D49 | Hizmet tipi ve masraf tipi listesi (40.1; ALS-D79'u günceller) | Ayarlar'daki **hizmet tipi** ve **masraf tipi** listeleri **şimdi açılır** (KIM-S18 dilimi). Listeler **boş başlar**; değerleri kullanıcı Ayarlar'dan girer, seed veya örnek değer yüklenmez. Listelerin sahibi Ayarlar modülüdür. Alış bu listeleri Ayarlar'ın okuma portundan okur ve kendi içinde kopya liste tutmaz. ALS-D79'un "Ayarlar modülünü bekler" hükmü bu iki liste için bu kadarıyla kalkar. ALS-D79'daki **işlem türü** listesi ve masraf yapısının ayrıntıları (hesap planı/mizan uyumu, Alış-Satış ortaklığı; `NEEDS-SPEC`) bu kararla açılmaz ve değişmez. ALS-D80/SAT-D302 aynen geçerlidir: ek masraf v1'de ortalama maliyete girmez. ALC-S7 ve ALC-S8 bu listeyle 5. dalgada kodlanır. | Ayarlar (KIM-S18 liste + okuma portu), Alış ALC-S7/ALC-S8 |
| ALS-D105 | Fason hizmet alış siparişinde KDV ve tevkifat kodu (40.2) | SAT-D381'in alış karşılığıdır. Dışarıya verilen fason işin alış siparişi için Ayarlar'da **tek bir varsayılan KDV ve tevkifat kodu** tutulur ve oradan değiştirilir. Bu varsayılan, satış fasonunun SAT-D381 varsayılanından ayrı bir kayıttır. Fason alış sipariş satırında kod **satır bazında değiştirilebilir**; değişiklik denetime yazılır. Kod/oran listesinin içeriği mevzuat ayarıdır. Ayar ve satır değişikliğinin hangi mevcut izinle yapılacağı `NEEDS-SINAN`'dır (bkz. Sınırlar). | Ayarlar (alış fasonu vergi varsayılanı), Alış fason sipariş satırı (ALC-S7) |
| ALS-D106 | Fason hizmet alış siparişinde fiyat birimi ve onay (40.3) | Fason hizmet alış siparişinde birim fiyat, **işlenecek malın kg'ı başına** girilir. Onay normal alış siparişiyle aynıdır (ALS-D3): satın alma uzmanı hazırlar, müdür onaylar; **hazırlayan kendi siparişini onaylayamaz**. Ayarlar'daki tutar eşiği normal alış siparişindekiyle aynıdır; başlangıçta kapalıdır (ALS-D3). Fason alış için ayrı bir onay akışı veya ayrı izin açılmaz. | Alış fason sipariş (ALC-S7) |
| FNS-D79 | Dövizli alış siparişinde sipariş kuru (40.4; FNS-D73'ü tamamlar) | Cevap aynen: "Hazırlayan değiştirebilir ama günün kuru otomatik varsayılan satış faturası ile aynı mantık". (1) Dövizli alış siparişinde sipariş kuru **otomatik varsayılan** olarak gelir. Varsayılan, satış faturasındaki kur mantığıyla bulunur: **SAT-D54/SAT-D170** algoritması (TCMB'nin yayımladığı, kesin olarak önceki iş gününün döviz satış kuru, arife kuralı dahil) ve **SAT-D169** (kur otomatik TCMB'den gelir). (2) Siparişi **hazırlayan** bu kuru **değiştirebilir**; değişiklik denetime yazılır. (3) Kur, satış faturasındaki SAT-D172 gibi **sayı olarak siparişe yazılır**; sonradan çekilen veya düzeltilen kur kayıtlı siparişi kendiliğinden değiştirmez. (4) FNS-D73 aynen geçerlidir: alıştan gelen maliyet çevriminde bu sipariş kuru kullanılır. ALS-D41 (TL borç kuru = tedarikçi faturasındaki kur) değişmez. **Yorum (geri alınabilir):** "günün kuru", siparişin oluşturulduğu iş günü için SAT-D54 algoritmasının verdiği kurdur. Satış faturasındaki sevk günü bağlantısı (SAT-D90) siparişte yoktur. | Alış sipariş başlığı/satırı (kur alanı), Ayarlar kur çözümleyicisi, Finans kur çevirici, Üretim döviz değerlemesi (FNS-D73) |

## Sınırlar

- Bu belge yeni izin adı açmaz ve rol/grant ataması yapmaz. Grant ataması insanda kalır.
- `NEEDS-SINAN` olarak kalanlar:
  - ALS-D105: alış fasonu vergi varsayılanını Ayarlar'da kimin değiştireceği.
  - ALS-D105: satırdaki kodu hangi mevcut iznin değiştireceği.
  - FNS-D79: hazırlayanın değişikliğinin gerekçe isteyip istemediği.
- SINAN-CEVAP 42 ile cevaplananlar (`SINAN_KARARLARI_2026-11-16_DALGA4_EK20.md`, #1186):
  - KIM-D49 liste yetkisi: SINAN-CEVAP 42.3, EK20 KIM-D51.
  - FNS-D79 kurun zorunluluğu, onaydan sonra değişikliği ve kur yokken davranış: SINAN-CEVAP 42.2, EK20 FNS-D80.
  - ALC-S8 tip seçilmeden kayıt: SINAN-CEVAP 42.1, EK20 ALS-D107.
  - KLN-D94 (EK13) uyarının alıcısı/kanalı ve "kalan ayrımın tamamı": SINAN-CEVAP 42.5 ve 42.6, EK20 KLN-D110.
- İnsan kapıları değişmez:
  - deployment, environment ve secret;
  - Settings → Actions etkinleştirmesi;
  - grant/rol/Critical;
  - production aktivasyonu;
  - `OPEN-*` kapatma;
  - `.claude/settings.json`.
