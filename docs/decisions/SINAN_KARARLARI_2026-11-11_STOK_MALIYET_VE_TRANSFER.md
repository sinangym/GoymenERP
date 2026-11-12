# Sinan Kararları — 2026-11-11, 2. tur (Stok: ortalama maliyette eksi stok, ek masraf, müşteri iadesi, alış iadesi, açılış stoku maliyeti ve karışık kaynak; stok transferinde sevkten vazgeçme, teslim farkı ve teslim alınmış transferin geri alınması)

Bu belge önceki karar dosyalarını silmez; SAT-D301…SAT-D306 ile KLN-D20…KLN-D22'yi ekler. EK59
(`SINAN_KARARLARI_2026-11-11_SATIS_EK59_ORTALAMA_MALIYET.md`) "Kapatılan ve kapanmayan `OPEN-INV-AVGCOST-*` kimlikleri"
tablosunda açık kalan beş kimliği ve 14 Ekim transfer yaşam döngüsü kararı (`SINAN_KARARLARI_2026-10-14_TRANSFER_YASAM_DONGUSU.md`) üzerine
sorulan iki `NEEDS-SINAN`'ı (#66 6809248294: sevk iptali/ters kayıt; #66 6809248592: teslim farkı) Sinan kararına bağlar. SAT-D256…SAT-D261 (EK51), SAT-D262…SAT-D274 (EK52), SAT-D295…SAT-D299
(EK59), BS-1 ve SAT-D300 (`SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md`), DEC-STK-008, 13/14 Ekim transfer kararları ve
`OPEN-008` geçerlidir; bu belge onların metnini değiştirmez (SAT-D258 ve SAT-D263 (3) ile ilişki aşağıda ayrıca yazılıdır).
`KARAR_SICILI` ve `ACIK_KARARLAR` değişmez.

**Kimlik öneki (neden SAT-D ve KLN-D):**

- Madde 9–13 (ortalama maliyet), EK59'un kullandığı aynı maliyet zincirinin devamıdır. Stok için ayrı bir karar öneki
  yoktur (`STK-D…` kayıtlı değildir); EK59 da bu yüzden SAT-D serisini kullanmıştır. Numara, SAT-D serisinin `main`deki
  son kimliğinden (SAT-D300, `BEKLEYEN_SORULAR`; `main` `2f81347ab`) sonra gelir: **SAT-D301…SAT-D305**; madde 22 (alış iadesi, 4. tur) **SAT-D306**.
- Madde 17–18 (stok transferi B-22a) Kalan Modüller şeridinin işidir (`docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md`
  B-22); bu yüzden o şeridin KLN-D serisi kullanıldı. `main`deki son kimlik KLN-D19'dur: **KLN-D20, KLN-D21**; madde 23 (4. tur) **KLN-D22**.
- Doğrulama: `git grep -ohE "SAT-D[0-9]{3}" origin/main -- docs` en büyük SAT-D300, `KLN-D[0-9]+` en büyük KLN-D19 verdi;
  13 açık PR dalının hiçbirinde SAT-D301+ veya KLN-D20+ geçmiyor. Yeni ön ek icat edilmedi.

**Kaynak:**

- Sinan'ın 11 Kasım 2026'da koordinatör oturumunda `AskUserQuestion` ile verdiği cevaplar; koordinatörün Issue #66'ya
  yazdığı kayıt: yorum [6809898906](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6809898906)
  ("SINAN-KARARI 2026-11-11 (2. tur) — modül soruları"). Yorumun girişi (aynen): "Bu cevaplar `OPEN-*` kapatma veya grant/rol
  yetkisi değildir; ilgili `OPEN-*` kimlikleri şeritlerin docs PR'ında Sinan kararına bağlanır."
- Bu belgeye alınan maddeler (aynen):

  > **Stok ortalama maliyet (OPEN-INV-AVGCOST-\*)**
  > 9. Eksi stok: çıkışın maliyeti stok düzelene kadar "hesaplanamadı" görünür; giriş gelince düzelir.
  > 10. Nakliye/gümrük gibi ek masraflar: v1'de ortalamaya girmez; masraf yapısı kurulunca ayarla açılabilir.
  > 11. Müşteri iadesi: stoka ilk çıkışın maliyetiyle ters kayıtla girer.
  > 12. Açılış stoku maliyeti: elle girilir, ikinci kişi onaylar; girilmeyen kalemde maliyet "yok" görünür.
  > 13. Karışık kaynak: alınan ve üretilen ürün tek hareketli ortalamada; fire maliyete varsayılan olarak eklenir, Ayarlar'dan kapatılabilir.

  > **Stok transfer (B-22a)**
  > 17. Sevk edilmiş transferden vazgeçme:
  >     - İrsaliye kesildiyse: sevk silinmez. İrsaliye iptal edilebiliyorsa iptal edilir, sevk de iptal edilebilir. İrsaliye iptal edilemiyorsa sevk silinmez; teslim alınmamışsa sistem uyarı verir. Sehven kesilen irsaliye için açıklamalı ters irsaliye kesilir.
  >     - İrsaliye kesilmediyse: teslim alınmamış sevk iptal edilebilir.
  > 18. Teslim farkı: teslim alanın girdiği miktar kesinleşir; fark otomatik fire yazılır. Ek onay yok.

- 4. tur kaydı: yorum [6810163182](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6810163182)
  ("Sinan kararları 2026-11-11 (gece, 4. tur)"; cevaplanan soru 6810037285, Stok). Bu belgeye alınan maddeler (aynen;
  madde 22'nin "Koordinatörün uygulama okuması" alt maddeleri aşağıda SAT-D306'da ayrıca işaretlidir):

  > 22. **Alış iadesi maliyeti (6810037285 / 1).**
  >     - Sinan'ın sözü aynen: "Alış iadesi olduğu için alış faturası ile fiyatlandırılır, stok maliyetini nasıl etkiler diye sorarsan alış fiyatından geri girsin, karışık olacak ama bence doğrusu bu sende araştırıp analiz edip mantıklı karar verelim".
  >     - **Karar:** Alış iadesi stoktan, iade edilen asıl alış satırının birim maliyetiyle çıkar. Hareketli ortalamanın o anki değeri kullanılmaz. Kalan stoğun ortalaması bu çıkışla yeniden hesaplanır.
  > 23. **Teslim alınmış transferin geri alınması (6810037285 / 2).** Ters yönde yeni transfer açılır. Kabul edilmiş transfer iptal edilmez. Yeni transferde açıklama ve ilk transfere bağlantı zorunludur.

  Aynı yorumdaki madde 24 (yuvarlak boru kg/m) Ürün şeridinin işidir ve bu belgeye alınmadı.
- Yorumda soruların seçenek listesi yer almaz; seçenekler bu belgeye yazılmadı (uydurulmadı). Aynı yorumdaki diğer maddeler
  (Platform, Finans, Masraf yapısı, Satış, Üretim) ilgili şeritlerin işidir ve bu belgeye alınmadı; yalnız madde 8'in
  "masraf yapısı" `NEEDS-SPEC` durumuna madde 10 nedeniyle atıf yapılır.

## Soru ve cevaplar

| # (yorumdaki) | Soru (yorumdaki konu) | Cevap (Sinan, kayıttaki metin) | Kayda geçen anlam |
|---|---|---|---|
| 9 | Eksi stok (`OPEN-INV-AVGCOST-NEGATIVE-STOCK`) | "Çıkışın maliyeti stok düzelene kadar 'hesaplanamadı' görünür; giriş gelince düzelir." | Eksi stokta maliyet okunmaz; bu durum kalıcı değildir, giriş gelince maliyet yeniden okunur |
| 10 | Nakliye/gümrük gibi ek masraflar (`OPEN-INV-AVGCOST-LANDED`) | "v1'de ortalamaya girmez; masraf yapısı kurulunca ayarla açılabilir." | v1'de ek masraf ortalamaya katılmaz; ileride bir ayarla açılabilir |
| 11 | Müşteri iadesi (`OPEN-INV-AVGCOST-RETURN`) | "Stoka ilk çıkışın maliyetiyle ters kayıtla girer." | İade, ilk çıkıştaki maliyetle ve ters kayıt olarak stoka girer |
| 12 | Açılış stoku maliyeti (`OPEN-INV-AVGCOST-UNCOSTED-STOCK`) | "Elle girilir, ikinci kişi onaylar; girilmeyen kalemde maliyet 'yok' görünür." | Açılış maliyeti iki kişili (giren ≠ onaylayan) bir girişle gelir; girilmemişse maliyet "yok"tur, sıfır değildir |
| 13 | Karışık kaynak (`OPEN-INV-AVGCOST-MIXED-SOURCE`) | "Alınan ve üretilen ürün tek hareketli ortalamada; fire maliyete varsayılan olarak eklenir, Ayarlar'dan kapatılabilir." | Alış ve üretim girişleri aynı hareketli ortalamayı besler; fire varsayılan olarak maliyete eklenir, Ayarlar'dan kapatılabilir |
| 17 | Sevk edilmiş transferden vazgeçme (B-22a) | Yukarıdaki alıntı (iki alt madde) | Sevk hiçbir durumda silinmez. İrsaliye kesildiyse ve iptal edilebiliyorsa irsaliye iptal edilir, sevk de iptal edilebilir; iptal edilemiyorsa sevk silinmez ve teslim alınmamışsa uyarı verilir. İrsaliye kesilmediyse teslim alınmamış sevk iptal edilebilir. Kabul edilmiş transfer için madde 23 geçerlidir |
| 18 | Teslim farkı (B-22a) | "Teslim alanın girdiği miktar kesinleşir; fark otomatik fire yazılır. Ek onay yok." | Kabul edilen miktar teslim alanın girdiği miktardır; sevk ile kabul arasındaki fark ek onaysız fire olur |
| 22 | Alış iadesi maliyeti | "Alış iadesi stoktan, iade edilen asıl alış satırının birim maliyetiyle çıkar. Hareketli ortalamanın o anki değeri kullanılmaz. Kalan stoğun ortalaması bu çıkışla yeniden hesaplanır." | Alış iadesi ortalamayla değil, asıl alış satırının birim maliyetiyle çıkar; kalan stoğun ortalaması yeniden hesaplanır |
| 23 | Teslim alınmış transferin geri alınması | "Ters yönde yeni transfer açılır. Kabul edilmiş transfer iptal edilmez. Yeni transferde açıklama ve ilk transfere bağlantı zorunludur." | Kabul edilmiş transfer iptal edilmez; geri alma, açıklamalı ve ilk transfere bağlı ters yönde yeni transferdir |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D301 | Eksi stok (madde 9) | (1) Stok eksiye düşerse çıkışın maliyeti **"hesaplanamadı"** okunur; değer uydurulmaz, sıfır sayılmaz. (2) Bu durum **kalıcı değildir**: stok bir girişle düzelince maliyet yeniden okunur. Düzelmenin hesap biçimi (hangi girişten sonra, hangi ortalamayla yeniden başlanacağı) Sinan'ın metninde yazmaz; **uygulama okunuşu (geri alınabilir)** kod PR'ında yazılır ve review edilir. | `AverageCostCalculator`, `AverageCostUnreadableReason.NegativeStock`, okuma portu |
| SAT-D302 | Ek (landed) masraf (madde 10) | (1) Nakliye, gümrük gibi ek masraflar **v1'de ortalamaya girmez**. (2) Masraf yapısı (#66 6809898906 madde 8, `NEEDS-SPEC`) kurulunca **bir ayarla** açılabilir; ayar anahtarı, varsayılanı ve sahibi (Ayarlar) o zaman tanımlanır. SAT-D263 (3)'ün "ileride verilecek karar" dediği ek maliyetin v1 cevabı budur; vade farkı ve öteki giderler bu maddede seçilmedi. | `AverageCostCalculator` (ek masraf girdisi), Ayarlar (ileride) |
| SAT-D303 | Müşteri iadesi (madde 11) | Müşteri iadesi stoka **ilk çıkışın maliyetiyle**, **ters kayıt** olarak girer. "İlk çıkış"ın hangi kayıtla eşleneceği (iade belgesinin bağlandığı çıkış hareketi) **uygulama okunuşu (geri alınabilir)** olarak kod PR'ında yazılır. Alış iadesi SAT-D306'dadır. | `AverageCostMovementKind.Return`, `AverageCostCalculator`, Satış iade akışı (SAT-D300 ile birlikte) |
| SAT-D304 | Açılış stoku maliyeti (madde 12) | (1) Açılış stokunun maliyeti **elle girilir** ve **ikinci kişi onaylar** (giren ≠ onaylayan). (2) Maliyeti girilmemiş kalemde maliyet **"yok"** görünür; sıfır sayılmaz, tahmin edilmez. Hangi izin adlarının açılacağı kod PR'ının işidir; izinlerin rollere verilmesi (grant/rol) **insan kapısıdır**. | Stok açılış (INV-OPEN-1, `OpeningCount`), maliyet defteri girdisi, okuma portu |
| SAT-D305 | Karışık kaynak ve fire (madde 13) | (1) Alınan ve üretilen ürün **tek hareketli ortalamada** tutulur; üretim girişi ayrı ortalama açmaz. (2) **Fire maliyete varsayılan olarak eklenir** (varsayılan "açık" Sinan'ın kararıdır); Ayarlar'dan kapatılabilir. Ayar anahtarının adı kod PR'ında tanımlanır, sahibi Ayarlar modülüdür. (3) Kodda geçen `OPEN-INV-AVGCOST-PRODUCTION` aynı konudur (EK59) ve bu maddeye bağlanır. (4) Üretim girişinin birim maliyeti için yön #66 6808848950 madde 6'dadır (F-011): tüketilen hammaddenin ortalama maliyeti + fire mamule yüklenir; işçilik ve genel gider v1'de yoktur, yapı bunları sonradan ekleyebilecek kadar esnek kurulur. Bu yön henüz ayrı bir karar dosyasına işlenmedi (ChatGPT şeridi); bu belge onu yalnız atıfla bağlar. Değeri Üretim modülü sürümlü bir portla verir (cross-module, K3). (5) Madde 6 fireyi ayarsız yazar; daha yeni olan madde 13 (23:41) "varsayılan olarak eklenir, Ayarlar'dan kapatılabilir" der. Daha yeni karar geçerli olduğundan (2), madde 6'yı inceltir: fire varsayılan olarak mamul maliyetine yüklenir, ayar kapalıysa yüklenmez. (6) `OPEN-008`in resmî kapanışı insan kapısıdır; sicilde açık kalır. | `AverageCostCalculator` (`ProductionReceipt`), Ayarlar (fire anahtarı), Üretim maliyet portu (ayrı dilim) |
| KLN-D20 | Sevk edilmiş transferden vazgeçme (madde 17) | (1) **Sevk silinmez** (13/14 Ekim kararı 5 ile aynı yönde). (2) İrsaliye kesildiyse: irsaliye iptal edilebiliyorsa iptal edilir ve sevk de iptal edilebilir; irsaliye iptal edilemiyorsa **sevk silinmez** ve teslim alınmamışsa sistem **uyarı verir**. Sehven kesilen irsaliye için **açıklamalı ters irsaliye** kesilir. (3) İrsaliye kesilmediyse: **teslim alınmamış** sevk iptal edilebilir. (4) Kabul edilmiş transfer iptal edilmez; geri alma KLN-D22'dir (madde 23). (5) İrsaliye iptal edilemediğinde sevkin iptal komutunun kapalı tutulması **yazarın okumasıdır (geri alınabilir)**; Sinan "silinmez" dedi, "iptal edilmez" demedi. Ters irsaliye kesildikten sonra sevke ne olacağı cevapta yoktur (Seçilmeyenler). | Stok transfer (B-22b, iptal), Lojistik transfer çıkış belgesi (irsaliye) iptali ve ters irsaliye |
| KLN-D21 | Teslim farkı (madde 18) | (1) Teslim alanın girdiği miktar **kesinleşir**. (2) Sevk ile kabul arasındaki fark **otomatik fire** yazılır. (3) **Ek onay yoktur**. (4) Sınır (**uygulama okunuşu, geri alınabilir**): "fark", teslim alanın kesinleştirdiği satır/etiket için sevk edilen kg ile girilen kg arasındaki farktır. Henüz teslim alınmamış etiket/satır yolda kalır (14 Ekim kısmi kabul değişmezi) ve fireye dönmez. Transfer bekleyen miktarla kapatılırsa o miktarın ne olacağı bu cevapta yoktur: `NEEDS-SINAN`. Tolerans cevapta yoktur; "fark otomatik fire" her farka uygulanır (**uygulama okunuşu, geri alınabilir**; tolerans istenirse ayrı Sinan kararıdır). Farkın hangi stok hareketi türüyle kaydedileceği B-22c PR'ının uygulama seçimidir. | Stok transfer (B-22c, teslim alma/kabul), fire hareketi |
| SAT-D306 | Alış iadesi maliyeti (madde 22) | (1) Alış iadesi stoktan, **iade edilen asıl alış satırının birim maliyetiyle** çıkar; hareketli ortalamanın o anki değeri kullanılmaz. (2) Kalan stoğun ortalaması bu çıkışla **yeniden hesaplanır**. Koordinatörün uygulama okuması (Sinan'ın "mantıklı karar verelim" yetkisiyle, **geri alınabilir**): (a) iade asıl alış satırına (alış faturası veya mal kabul satırı) bağlanmak zorundadır; iade miktarı o satırdan alınan − önceki iadeleri aşamaz; (b) birim maliyet, alış girişinde stoğa yazılan TL birim maliyettir; dövizli faturada giriş anındaki TL karşılığı kullanılır; navlun/gümrük v1'de yoktur (SAT-D302); (c) stok sıfıra inerken değer kalırsa veya eksiye düşerse fark ortalamaya yedirilmez, ayrı "maliyet farkı" kaydı olarak görünür; hesap eşlemesi `NEEDS-SPEC` (masraf/muhasebe yapısı, 6809898906 madde 7); (d) stok eksideyse SAT-D301 geçerlidir ("hesaplanamadı"). | `AverageCostMovementKind.Return` (alış iadesi), `AverageCostCalculator`, Alış iade akışı ve asıl alış satırı referansı (cross-module) |
| KLN-D22 | Teslim alınmış transferin geri alınması (madde 23) | (1) Kabul edilmiş transfer **iptal edilmez**. (2) Geri alma için **ters yönde yeni transfer** açılır. (3) Yeni transferde **açıklama** ve **ilk transfere bağlantı** zorunludur. Bağlantı alanının adı ve doğrulaması B-22b/c PR'ının işidir. | Stok transfer (yeni transfer + ilk transfer referansı) |

**SAT-D258 ile ilişki:** SAT-D258 (1) "ortalamaya şimdilik yalnız satınalma maliyeti girer" der ve karma kaynaklı ürünü
`NEEDS-SINAN` bırakır. SAT-D305 bu `NEEDS-SINAN`'ı cevaplar ve yönü "tek hareketli ortalama" olarak verir. Üretim
maliyetinin yönü #66 6808848950 madde 6'da verilmiştir (SAT-D305 (4)); ancak değer Üretim modülünün sürümlü portundan
gelmedikçe üretim girişi ortalamaya bir sayıyla katılamaz. O port gelene kadar SAT-D258 (2)'deki "kullanılamıyor" sonucu
fiilen sürer. Bu ara durum yazarın okumasıdır (Sinan kararı değildir); farklı bir ara davranış ancak ayrı Sinan kararıyla
seçilir.

## `OPEN-*` kimliklerinin bağlanması

`OPEN-INV-AVGCOST-*` kimlikleri yalnız Stok kodunda ve README'sinde geçer; `KARAR_SICILI.*` ve `ACIK_KARARLAR.md` içinde
kayıtlı değildir (EK59). Kaynak yorum bu cevapların `OPEN-*` kapatma olmadığını söyler; bu yüzden aşağıdaki durum bir
sicil kapanışı değildir, kimliği Sinan kararına bağlar. Sicil dosyaları değişmez; registry MD/CSV eşliği ve hash yeniden
üretimi gerekmez.

| Kimlik | Durum | Dayanak |
|---|---|---|
| `OPEN-INV-AVGCOST-NEGATIVE-STOCK` | Sinan kararı verildi (#66 6809898906); kod uygulanınca kod yorumu güncellenir | SAT-D301 (madde 9) |
| `OPEN-INV-AVGCOST-LANDED` | Sinan kararı verildi (#66 6809898906); kod uygulanınca kod yorumu güncellenir | SAT-D302 (madde 10) |
| `OPEN-INV-AVGCOST-RETURN` | Sinan kararı verildi: müşteri iadesi #66 6809898906, alış iadesi #66 6810163182; kod uygulanınca kod yorumu güncellenir | SAT-D303 (madde 11), SAT-D306 (madde 22) |
| `OPEN-INV-AVGCOST-UNCOSTED-STOCK` | Sinan kararı verildi (#66 6809898906); kod uygulanınca kod yorumu güncellenir | SAT-D304 (madde 12) |
| `OPEN-INV-AVGCOST-MIXED-SOURCE` | Sinan kararı verildi (#66 6809898906); kod uygulanınca kod yorumu güncellenir | SAT-D305 (madde 13) |
| `OPEN-INV-AVGCOST-PRODUCTION` (test yorumu; MIXED-SOURCE ile aynı konu) | Sinan kararı verildi (#66 6809898906); kod uygulanınca kod yorumu güncellenir | SAT-D305 (madde 13) |
| `OPEN-INV-AVGCOST-FX` | Daha önce karara bağlandı; bu belge değiştirmez | BS-1 (`SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md`, #66 6806579823) |
| `OPEN-008` (üretim maliyeti) | Yön Sinan kararıyla verildi (#66 6808848950 madde 6, F-011); karar dosyasına işlenmesi ChatGPT şeridinde, resmî kapanış insan kapısı; sicilde açık kalır | SAT-D305 (4)–(6) |

## Uygulama notu (kod şeridi; bu PR'da yapılmadı)

Bunlar sonraki **K3** kod PR'larının işidir (para/formül, stok, gerektiğinde migration ve cross-module sözleşme; iki
bağımsız exact-head review, gerçek SQL/failure kanıtı). Bu belge kod değiştirmez. Aşağıdaki "bugünkü kod" tespitleri
yazarın `main` `2f81347ab` taramasıdır.

- **SAT-D301 (eksi stok) — değişiklik gerekir.** Bugünkü kod: `AverageCostCalculator.ApplyIssue` stoktan fazla çıkışta
  pozisyonu `Unreadable(NegativeStock)` yapar (bu "hesaplanamadı" yönüyle uyumlu), ama `Apply` okunamaz pozisyonu hiç
  açmaz: "Unreadable is sticky" (`AverageCostPosition.cs`, `Inventory/README.md`). Sinan "giriş gelince düzelir" dediği için
  eksi stok nedeni yapışkan kalamaz; diğer nedenler (ör. `ZeroCostReceipt`, SAT-D297) yapışkan kalır. Düzelme hesabı
  uygulama okunuşudur ve kod PR'ında yazılır; enum numaraları değişmez.
- **SAT-D302 (ek masraf) — değişiklik gerekir.** Bugünkü kod: `IncludesAdditionalCost = true` olan alış girişi pozisyonu
  `Unreadable(LandedOrAdditionalCost)` yapar. Karara göre v1'de ek masraf yalnız ortalamaya **katılmaz**; girişin kendi alış
  fiyatıyla ortalamaya girmesi, ek masrafın ayrı tutulması uygulama okunuşudur (geri alınabilir). Masraf yapısı ayarı
  madde 8 (`NEEDS-SPEC`) sonrasına kalır.
- **SAT-D303 (müşteri iadesi) — değişiklik gerekir.** Bugünkü kod: her `Return` hareketi pozisyonu `Unreadable(Return)` yapar.
  Karara göre müşteri iadesi ilk çıkışın maliyetiyle **ters kayıt** olarak girer (silme/yeniden hesap yok). İade girişi
  ilk çıkışın maliyetini taşımalıdır; bunu Satış/Lojistik iade akışından hangi port ile alacağı cross-module sözleşmedir
  (`Modül bağlantıları` satırıyla). Alış iadesi SAT-D306'ya göre asıl alış satırının TL birim maliyetiyle çıkar
  ve ortalamayı yeniden hesaplatır; asıl satır referansı Alış'tan sürümlü sözleşmeyle gelir, gelene kadar bugünkü gibi
  Unreadable kalır. "Maliyet farkı" kaydının hesap eşlemesi `NEEDS-SPEC`'tir.
- **SAT-D304 (açılış maliyeti) — yeni komut gerekir.** Bugünkü kod: açılış sayımı (`OpeningCount`, INV-OPEN-1) maliyet
  taşımaz; defterin hiç maliyetlemediği stok `Unreadable(LedgerStockMismatch)` olur (bu "yok" yönüyle uyumlu). Karara göre
  giren ≠ onaylayan bir açılış maliyeti komutu (maker-checker) gerekir; onaylanmamış giriş defteri değiştirmez. Yeni izin
  adları kod PR'ında kaydedilir; grant/rol ataması ve Critical sınıfı insan kapısıdır.
- **SAT-D305 (karışık kaynak) — değişiklik gerekir, ama üretim maliyeti girdisine bağlıdır.** Bugünkü kod: `ProductionReceipt`
  pozisyonu `Unreadable(ProductionSource)` yapar (SAT-D258'e göre). Tek ortalamaya geçiş, üretim girişinin birim maliyeti
  Üretim modülünün sürümlü portundan gelmedikçe sayı üretemez; hesap yönü #66 6808848950 madde 6'dadır (hammadde
  ortalama maliyeti + fire; işçilik/genel gider v1'de yok) ve port cross-module K3 dilimidir. Fire anahtarı (ayar adı, varsayılan "açık") Ayarlar modülünde
  tanımlanır; fire'nin maliyete eklenme hesabı (ör. iyi çıktı kg'sine dağıtma) uygulama okunuşudur ve formül olduğu için
  K3 review'unda açıkça yazılır.
- **KLN-D20 (sevkten vazgeçme) — B-22b takip dilimi.** Bugünkü kod: `InventoryTransferStatus` yalnız Planned / InTransit /
  PartiallyAccepted / Accepted taşır; iptal durumu yoktur (yeni durum + migration, K3). Lojistik'te transfer çıkış
  belgesinin `Cancel` komutu vardır (`logistics.transfer-outgoing-document.cancel.v1`); ters irsaliye yoktur. "İrsaliye iptal
  edilebiliyor mu" kuralı ve ters irsaliye Lojistik şeridinin sahipliğindedir; Stok, irsaliyenin iptal edilebilirliğini ve iptal
  sonucunu Lojistik'in sürümlü sözleşmesi (okuma portu veya outbox olayı) ile öğrenir; Stok Lojistik tablosuna yazmaz ve
  Lojistik komutunu kendi transaction'ında çağırmaz. PR'da `Modül bağlantıları` satırı yazılır. Bu belge Lojistik kuralını
  seçmez. Uyarı metni Türkçe kullanıcı metnidir (kaynak dosya).
- **KLN-D21 (teslim farkı) — B-22c takip dilimi.** Bugünkü kod: kabul bütün etiket satırları üzerinden yapılır; v1
  sözleşmesi tek etikette kısmi kg kabul etmez ve sevkte planlanan/gerçek kg farkını (`DifferenceKg`) ayrı tutar. Teslim
  alanın kg girmesi ve farkın fire yazılması sözleşme değişikliğidir. Mevcut hurda kaydı (`StockScrap`) kayıt + onay
  izinleriyle iki adımlıdır; KLN-D21 "ek onay yok" dediği için fark bu onay akışına sokulamaz. Hangi hareket türünün
  kullanılacağı B-22c PR'ının seçimidir. 14 Ekim "kısmi kabul değişmezi" (sevk/yolda/kabul/fark ayrı gerçekler) aynen
  geçerlidir.

| Karar | İş | Şerit / Kapı |
|---|---|---|
| SAT-D301 | Eksi stok nedeninin yapışkan olmaması, düzelme hesabı ve testleri | Stok backend; K3 (para/formül) |
| SAT-D302 | Ek masrafı Unreadable yerine ortalama dışında tutma; ileride ayar | Stok backend; K3. Ayar: Ayarlar şeridi, madde 8 sonrası |
| SAT-D303 | Müşteri iadesi ters kaydı, ilk çıkış maliyeti girdisi | Stok + Satış/Lojistik iade; cross-module contract K3 |
| SAT-D304 | Açılış maliyeti maker-checker komutu ve izin adları | Stok backend; K3 (grant insan kapısı) |
| SAT-D305 | Tek hareketli ortalama, fire anahtarı | Stok + Ayarlar; üretim maliyet değeri Üretim portuyla (madde 6 yönü, cross-module); K3 |
| KLN-D20 | B-22b sevk iptali (yeni durum + migration) | Stok; Lojistik irsaliye iptaline bağımlı; K3 |
| KLN-D21 | B-22c teslim alma ve fark → otomatik fire | Stok; K3 |
| SAT-D306 | Alış iadesi: asıl alış satırı maliyetiyle çıkış, ortalamanın yeniden hesabı, maliyet farkı kaydı | Stok + Alış (asıl satır referansı); cross-module K3; hesap eşlemesi `NEEDS-SPEC` |
| KLN-D22 | Kabul edilmiş transferin ters yönde yeni transferle geri alınması (açıklama + bağlantı zorunlu) | Stok; K3 (gerekirse migration) |

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz.
- **Kapatma yoktur.** `OPEN-INV-AVGCOST-*` kimlikleri yalnız Sinan kararına bağlanır; `KARAR_SICILI`/`ACIK_KARARLAR`
  ve `OPEN-008` değişmez.
- **Seçilmeyenler:** vade farkı ve öteki giderler; ters irsaliye kesildikten sonra sevke ne olacağı; Lojistik'te irsaliyenin hangi durumda iptal edilebileceği; kısmi kabulde bekleyen miktarla transfer kapatılırsa ne olacağı (#66 6810239322); "maliyet farkı" kaydının hesap eşlemesi (`NEEDS-SPEC`). Bunlar ilgili
  şeritte `NEEDS-SINAN` veya sahip şeridin kararıdır; bu belge tahminle seçmez. Üretim girişinin birim maliyeti
  seçilmeyen değildir: yönü madde 6'da vardır, karar dosyasına işlenmemiştir; port/sözleşme ayrı dilimdir (SAT-D305 (4)).
- "Uygulama okunuşu (geri alınabilir)" diye işaretli noktalar Sinan'ın sözü değildir; kod PR'ında yazılır, Sinan farklı
  isterse ayrı karar gerekir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
