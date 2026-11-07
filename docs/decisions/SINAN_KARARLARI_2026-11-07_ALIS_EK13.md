# Sinan Kararları — 2026-11-07 Alış EK13 (A2a-2: alış satırı KDV oranı, vade farkı hesabı, doğrudan girilen vadeli fiyat, KDV vade farkı dahil)

Kaynak: Sinan Göymen'in 7 Kasım 2026 tarihli Claude cloud oturumunda (Alış şeridi), A2a-2 (ALS-D38 vade farkı ve
API-20 `GET orders/{id}/totals`) için sorulan üç soruya ve aynı gün sorulan ek soruya (S4) verdiği **doğrudan cevaplar** (makbuzlar: S1–S3
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6708738330; S4
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6708953113). Sorular Türkçe soruldu; sorular, bütün
seçenekler ve seçenek açıklamaları aynen aktarılmıştır. "(Önerilen)" işareti Claude'un soruyu sorarken koyduğu
işarettir. Claude yalnız soruları ve seçenekleri hazırlamıştır; seçimi Sinan yapmıştır.

Numaralama: ALS-D73 EK12'dedir. Bu dosya ALS-D74…D77'yi içerir. EK11 ve EK12'nin karar içeriği (§1) değişmez.

## 0. Yetki ve sınır

- `SINAN_KARARLARI_2026-11-02_ALIS.md` §0 (ALS-D45 ile daraltılmış hâliyle), EK8 §0, EK9 §0 (ALS-D57), EK10 §0,
  EK11 §0 ve EK12 §0 geçerlidir. ALS-D74…D77'de seçilen seçenekler doğrudan kullanıcı cevabıdır.
- **Değişmeyen insan kapıları:** production aktivasyonu, secret/deployment, Ayarlar'da gerçek vergi/para kaydı
  aktivasyonu, grant/rol/Critical ve `OPEN-*` kapatma insanda kalır (AGENTS.md).
- **Mali müşavir teyidi (N-20) yalnız tevkifat ve tedarikçi muafiyeti için açık kalır** (ALS-D77): vade farkının
  KDV matrahına girişi ALS-D77 ile karara bağlandı. Bu dosya hiçbir `OPEN-*` kimliğini
  kapatmaz, grant/rol vermez.

## 1. Kararlar

| No | Konu | Soru (aynen) | Seçenekler (aynen) | Seçilen (aynen) | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|---|---|
| ALS-D74 | Alış satırı KDV oranı | **"Alış siparişi satırında KDV oranı nereden gelsin? (Tevkifat zaten mali müşavir teyidine kadar hesaplanmıyor.)"** | **"Ürün türü varsayılanı (Önerilen)"** — "Satıştaki gibi ürün türünün KDV oranı (çelik/hurda/hizmet) kullanılır, sipariş toplamında KDV gösterilir. Tedarikçinin muafiyeti (ihraç kayıtlı vb.) sonra eklenir."; **"Teyide kadar hesaplanmasın"** — "Mali müşavir teyidi (N-20) gelene kadar alış siparişinde KDV de 'hesaplanamıyor' görünür; yalnız net tutar toplanır." | **"Ürün türü varsayılanı (Önerilen)"** | Plan A2a bölünmesi: API-20 toplamı ortak vergi calculator'ı ve mevcut Ayarlar vergi masterıyla; N-20 teyidine kadar alış tevkifatı "hesaplanamıyor". Alış siparişinde KDV yoktu | N-20, API-20, A2a-2, SAT-D139/D140 (Satış emsali), ALS-D58 |
| ALS-D75 | Vade farkının hesabı | **"Vade farkı alış siparişinde nasıl hesaplansın?"** | **"Satış'la aynı (Önerilen)"** — "Vade farkı iskonto sonrası peşin fiyata eklenir; yüzde ise iskontolu fiyatın yüzdesi, tutar ise birim başına. 0 fiyatlı satırda vade farkı girilemez."; **"İskontodan önce"** — "Vade farkı liste (peşin) fiyata eklenir, iskonto sonra uygulanır." | **"Satış'la aynı (Önerilen)"** | ALS-D38 (EK3): satırda peşin + vade farkı ya da tek vadeli fiyat; hesap biçimi yazılı değildi. PR #944: satırda vade farkı yok | ALS-D38, ALS-D5, ALS-D71, SAT-D35/SAT-D42 (benzetme), SAT-D76, N-20, A2a-2 |
| ALS-D76 | Doğrudan girilen vadeli fiyat | **"Satıra peşin fiyat yerine doğrudan 'vadeli fiyat' girilirse, bu satır vadeli olarak işaretlensin mi?"** | **"Evet, işaretlensin (Önerilen)"** — "Satırda 'vadeli fiyat' işareti olur; vade farkı olan satır gibi davranır (fatura kontrolünde vadeli sayılır)."; **"Hayır, düz fiyat"** — "Doğrudan girilen fiyat normal fiyat sayılır; ayrı bir vadeli işareti tutulmaz." | **"Hayır, düz fiyat"** (önerilen seçeneğe karşı) | ALS-D38'in "tek fiyatta … vadeli alış fiyatı" yolu; işaret tutulup tutulmayacağı yazılı değildi | ALS-D38, ALS-D8 (fatura farkı), A2a-2 |
| ALS-D77 | Vade farklı satırda KDV | **"Satıra peşin fiyat + vade farkı girildiğinde KDV nasıl hesaplansın? Eski bir kararda (ALS-D38) "mali müşavir teyidine kadar vade farklı satırda vergi hesaplanmaz" yazıyor. Ama siz doğrudan girilen vadeli fiyatı düz fiyat saydınız; o satırda KDV hesaplanıyor. İkisi tutarsız kalıyor."** | **"Vade farkı dahil hesaplansın (Önerilen)"** — "KDV, vade farkı eklenmiş tutar üzerinden hesaplanır; iki yol (peşin+fark ve doğrudan vadeli fiyat) aynı sonucu verir. Mali müşavir teyidi (N-20) yalnız tevkifat ve tedarikçi muafiyeti için açık kalır."; **"Teyide kadar hesaplanmasın"** — "Eski karar geçerli: peşin+vade farkı girilen satırda KDV 'hesaplanamıyor' görünür, sipariş KDV toplamı 'eksik' olur. Doğrudan vadeli fiyat girilen satırda ise KDV hesaplanır." | **"Vade farkı dahil hesaplansın (Önerilen)"** | EK3 §2 ALS-D38 yorumu: "teyide kadar vade farkı girilen satır vergi hesaplamaz ("hesaplanamıyor")"; ALS-D76 ile çelişiyordu (PR #968 review P1/P2) | ALS-D38, ALS-D74, ALS-D75, ALS-D76, N-20, A2a-2 |

## 2. Etkiler ve yorumlar

Bu bölümdeki **yorum** etiketli maddeler geri alınabilir; karar değildir.

- **ALS-D74 (KDV oranı):** Alış siparişi satırının KDV oranı, Satış'taki gibi **ürün türünün varsayılan KDV'sidir**
  (çelik/hurda/hizmet); sipariş toplamında KDV gösterilir. Tedarikçi muafiyeti (ihraç kayıtlı vb.) **sonra** eklenir
  (N-20 teyidiyle). Tevkifat N-20 teyidine kadar **"hesaplanamıyor"** kalır (soru metni; plan A2a bölünmesi).
  - *Yorum (uygulama ayrıntısı, A2a-2):* oran mevcut Ayarlar portu `ISettingsLineTaxResolverV1` ile okunur (ürün
    türünün varsayılanı; Satış'taki gibi), alıcı durumu **`Normal`** olarak verilir — alış tarafında "alıcı" Göymen'in
    kendisidir ve muafiyet seçimi henüz yoktur. Ayarlar'da yeni kayıt yoktur (ALS-D58). Geri alınabilir.
  - *Yorum:* **ürün türü olmayan satır** (ör. ürüne bağlanamayan genel satır) ya da Ayarlar'ın `Unresolved` döndürdüğü
    satır için KDV **"hesaplanamıyor"** görünür; 0 ya da tahmin yazılmaz (port sözleşmesi, `SettingsTaxMasterV1.cs`
    `TaxResolutionStatesV1`: "never 0, never a guessed default"). Geri alınabilir.
- **ALS-D75 (vade farkı Satış'la aynı):** Vade farkı **iskonto sonrası peşin fiyata eklenir**. Yüzde girilirse
  **iskontolu fiyatın yüzdesidir**; tutar girilirse **birim başınadır**. **0 fiyatlı satırda vade farkı girilemez**
  (ALS-D71'in açık 0 fiyatı için). İskonto önce, vade farkı sonra; "İskontodan önce" seçilmedi.
  - *Yorum (SAT-D42 benzetmesi):* Satış'ta iskonto ölçümü vade farkı eklenmeden önceki iskontolu fiyata bakar
    (SAT-D35/SAT-D42; vade farkı iskontoyu gizleyemez). Alış'ta iskonto limiti yoktur (ALS-D5), ama sıra aynıdır. Net
    birim fiyat (iskonto + vade farkı sonrası) yuvarlanmaz, satır tutarı bir kez yuvarlanır (SAT-D76 benzetmesi; EK11
    ALS-D72 yorumu). Geri alınabilir.
  - Vade farkının KDV matrahına girişi ALS-D77 ile karara bağlandı (aşağıda).
- **ALS-D76 (doğrudan vadeli fiyat = düz fiyat; Sinan önerilene karşı seçti):** ALS-D38'in ikinci yolu ("tek fiyatta …
  vadeli alış fiyatı") **normal fiyat** olarak girilir; satırda ayrı bir "vadeli" işareti **tutulmaz**. Böyle bir satır
  vade farkı olan satır gibi davranmaz; fatura kontrolünde (ALS-D8) normal fiyat olarak karşılaştırılır.
  - Kayıt: Claude'un önerdiği seçenek **"Evet, işaretlensin"** idi; Sinan **"Hayır, düz fiyat"** seçti.
  - *Yorum:* Vade farkı alanı yalnız ALS-D38'in birinci yolu (peşin + vade farkı) için doludur; tek vadeli fiyat
    girilen satırda boştur. Geri alınabilir.
- **ALS-D77 (KDV vade farkı dahil):** KDV, **vade farkı eklenmiş tutar** üzerinden hesaplanır; (a) peşin fiyat + vade
  farkı ve (b) doğrudan vadeli fiyat (ALS-D76) aynı tutarda **aynı KDV'yi** verir. N-20 (mali müşavir teyidi)
  **yalnız tevkifat ve tedarikçi muafiyeti** için açık kalır.
  - **Yerine geçme:** ALS-D77, EK3 §2'deki ALS-D38 yorumunun "teyide kadar vade farkı girilen satır vergi hesaplamaz
    ("hesaplanamıyor")" ifadesinin **yerini alır**. EK3 dosyası değiştirilmedi; yerine geçme yalnız burada kayıtlıdır.
    ALS-D38'in kendisi (iki giriş yolu) aynen geçerlidir.
  - *Yorum:* ihraç kayıtlı/teşvikli KDV tedarikçi muafiyetinin türleridir, N-20'de kalır; "ödenecek tutar" tanımı
    tevkifata bağlı olduğu için onunla birlikte N-20'de kalır. Geri alınabilir.
- **Kod:** uygulama **A2a-2** dilimidir (plan §12, sürüyor). Bu belge kod değiştirmez.

## 3. Değişmeyenler

- ALS-D38 (EK3; iki giriş yolu) aynen; ALS-D74…D77 yalnız KDV oranının kaynağını, vade farkı hesabını, doğrudan
  vadeli fiyatın işaretsiz olduğunu ve KDV'nin vade farkı dahil hesaplandığını seçer. **Tek istisna:** EK3 §2 ALS-D38
  yorumundaki "teyide kadar vade farkı girilen satır vergi hesaplamaz" ifadesi ALS-D77 ile geçersizdir (EK3
  değiştirilmedi). ALS-D5, ALS-D8, ALS-D18, ALS-D58, ALS-D69…D72 aynen.
- N-20 yalnız tevkifat ve tedarikçi muafiyeti (ihraç kayıtlı/teşvikli KDV dahil; "ödenecek tutar" tevkifatla birlikte)
  için açık.
- **EK12 §2 atıf notu (#959 review P3):** EK12 §2'deki "create/approve/cancel … `—` (ALS-D73 seçenek açıklaması)"
  cümlesinde **iptal** (`purchasing.orders.cancel`) hücresinin dayanağı ALS-D73 değil, **ALS-D66 / EK10 §2**'dir
  (kalan iptali onaycılarda; Muhasebe ve Muhasebe Müdürü onaycı değil). ALS-D73'ün seçenek açıklaması "create, edit or
  approve" der, iptali saymaz. EK12'nin karar içeriği değişmedi; persona kartındaki iptal hücreleri ve not ALS-D66 /
  EK10 §2'ye dayandırıldı.
- Grant/rol ataması, Ayarlar'da gerçek kayıt aktivasyonu, production aktivasyonu, secret/deployment ve `OPEN-*`
  kapatma insan kapısıdır.
