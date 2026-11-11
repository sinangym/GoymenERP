# Sinan Kararları — 2026-11-11, 2. tur (Stok: ortalama maliyette eksi stok, ek masraf, müşteri iadesi, açılış stoku maliyeti ve karışık kaynak; stok transferinde sevkten vazgeçme ve teslim farkı)

Bu belge önceki karar dosyalarını silmez; SAT-D301…SAT-D305 ile KLN-D20 ve KLN-D21'i ekler. EK59
(`SINAN_KARARLARI_2026-11-11_SATIS_EK59_ORTALAMA_MALIYET.md`) "Kapatılan ve kapanmayan `OPEN-INV-AVGCOST-*` kimlikleri"
tablosunda açık kalan beş kimliği ve 14 Ekim transfer yaşam döngüsü kararının (`SINAN_KARARLARI_2026-10-14_TRANSFER_YASAM_DONGUSU.md`)
açık bıraktığı iki konuyu Sinan kararına bağlar. SAT-D256…SAT-D261 (EK51), SAT-D262…SAT-D274 (EK52), SAT-D295…SAT-D299
(EK59), BS-1 ve SAT-D300 (`SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md`), DEC-STK-008, 13/14 Ekim transfer kararları ve
`OPEN-008` geçerlidir; bu belge onların metnini değiştirmez (SAT-D258 ve SAT-D263 (3) ile ilişki aşağıda ayrıca yazılıdır).
`KARAR_SICILI` ve `ACIK_KARARLAR` değişmez.

**Kimlik öneki (neden SAT-D ve KLN-D):**

- Madde 9–13 (ortalama maliyet), EK59'un kullandığı aynı maliyet zincirinin devamıdır. Stok için ayrı bir karar öneki
  yoktur (`STK-D…` kayıtlı değildir); EK59 da bu yüzden SAT-D serisini kullanmıştır. Numara, SAT-D serisinin `main`deki
  son kimliğinden (SAT-D300, `BEKLEYEN_SORULAR`; `main` `2f81347ab`) sonra gelir: **SAT-D301…SAT-D305**.
- Madde 17–18 (stok transferi B-22a) Kalan Modüller şeridinin işidir (`docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md`
  B-22); bu yüzden o şeridin KLN-D serisi kullanıldı. `main`deki son kimlik KLN-D19'dur: **KLN-D20, KLN-D21**.
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
| 17 | Sevk edilmiş transferden vazgeçme (B-22a) | Yukarıdaki alıntı (iki alt madde) | Sevk hiçbir durumda silinmez; iptal yalnız teslim alınmamış sevkte ve, irsaliye kesildiyse, irsaliye iptal edilebildiğinde mümkündür |
| 18 | Teslim farkı (B-22a) | "Teslim alanın girdiği miktar kesinleşir; fark otomatik fire yazılır. Ek onay yok." | Kabul edilen miktar teslim alanın girdiği miktardır; sevk ile kabul arasındaki fark ek onaysız fire olur |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D301 | Eksi stok (madde 9) | (1) Stok eksiye düşerse çıkışın maliyeti **"hesaplanamadı"** okunur; değer uydurulmaz, sıfır sayılmaz. (2) Bu durum **kalıcı değildir**: stok bir girişle düzelince maliyet yeniden okunur. Düzelmenin hesap biçimi (hangi girişten sonra, hangi ortalamayla yeniden başlanacağı) Sinan'ın metninde yazmaz; **uygulama okunuşu (geri alınabilir)** kod PR'ında yazılır ve review edilir. | `AverageCostCalculator`, `AverageCostUnreadableReason.NegativeStock`, okuma portu |
| SAT-D302 | Ek (landed) masraf (madde 10) | (1) Nakliye, gümrük gibi ek masraflar **v1'de ortalamaya girmez**. (2) Masraf yapısı (#66 6809898906 madde 8, `NEEDS-SPEC`) kurulunca **bir ayarla** açılabilir; ayar anahtarı, varsayılanı ve sahibi (Ayarlar) o zaman tanımlanır. SAT-D263 (3)'ün "ileride verilecek karar" dediği ek maliyetin v1 cevabı budur; vade farkı ve öteki giderler bu maddede seçilmedi. | `AverageCostCalculator` (ek masraf girdisi), Ayarlar (ileride) |
| SAT-D303 | Müşteri iadesi (madde 11) | Müşteri iadesi stoka **ilk çıkışın maliyetiyle**, **ters kayıt** olarak girer. "İlk çıkış"ın hangi kayıtla eşleneceği (iade belgesinin bağlandığı çıkış hareketi) **uygulama okunuşu (geri alınabilir)** olarak kod PR'ında yazılır. Alış iadesi bu cevapta yoktur; açık kalır. | `AverageCostMovementKind.Return`, `AverageCostCalculator`, Satış iade akışı (SAT-D300 ile birlikte) |
| SAT-D304 | Açılış stoku maliyeti (madde 12) | (1) Açılış stokunun maliyeti **elle girilir** ve **ikinci kişi onaylar** (giren ≠ onaylayan). (2) Maliyeti girilmemiş kalemde maliyet **"yok"** görünür; sıfır sayılmaz, tahmin edilmez. Hangi izin adlarının açılacağı kod PR'ının işidir; izinlerin rollere verilmesi (grant/rol) **insan kapısıdır**. | Stok açılış (INV-OPEN-1, `OpeningCount`), maliyet defteri girdisi, okuma portu |
| SAT-D305 | Karışık kaynak ve fire (madde 13) | (1) Alınan ve üretilen ürün **tek hareketli ortalamada** tutulur; üretim girişi ayrı ortalama açmaz. (2) **Fire maliyete varsayılan olarak eklenir**; Ayarlar'dan kapatılabilir. Ayar anahtarı ve varsayılanı kod PR'ında tanımlanır, sahibi Ayarlar modülüdür. (3) Kodda geçen `OPEN-INV-AVGCOST-PRODUCTION` aynı konudur (EK59) ve bu maddeye bağlanır. (4) **Üretim girişinin birim maliyetinin nereden geleceği bu cevapta yazmaz**; üretim maliyeti konusu (`OPEN-008`) açık kalır ve bu belge onu seçmez. | `AverageCostCalculator` (`ProductionReceipt`), Ayarlar (fire anahtarı), Üretim maliyet girdisi (ayrı dilim) |
| KLN-D20 | Sevk edilmiş transferden vazgeçme (madde 17) | (1) **Sevk silinmez** (13/14 Ekim kararı 5 ile aynı yönde). (2) İrsaliye kesildiyse: irsaliye iptal edilebiliyorsa iptal edilir ve sevk de iptal edilebilir; irsaliye iptal edilemiyorsa sevk iptal edilmez ve teslim alınmamışsa sistem **uyarı verir**. Sehven kesilen irsaliye için **açıklamalı ters irsaliye** kesilir. (3) İrsaliye kesilmediyse: **teslim alınmamış** sevk iptal edilebilir. (4) Teslim alınmış (kabul edilmiş) sevkin iptali bu cevapta yoktur. | Stok transfer (B-22b, iptal), Lojistik transfer çıkış belgesi (irsaliye) iptali ve ters irsaliye |
| KLN-D21 | Teslim farkı (madde 18) | (1) Teslim alanın girdiği miktar **kesinleşir**. (2) Sevk ile kabul arasındaki fark **otomatik fire** yazılır. (3) **Ek onay yoktur**. Farkın hangi stok hareketi türüyle kaydedileceği B-22c PR'ının uygulama seçimidir. | Stok transfer (B-22c, teslim alma/kabul), fire hareketi |

**SAT-D258 ile ilişki:** SAT-D258 (1) "ortalamaya şimdilik yalnız satınalma maliyeti girer" der ve karma kaynaklı ürünü
`NEEDS-SINAN` bırakır. SAT-D305 bu `NEEDS-SINAN`'ı cevaplar ve yönü "tek hareketli ortalama" olarak verir. Üretim
maliyetinin değeri (`OPEN-008`) gelmedikçe üretim girişi ortalamaya bir sayıyla katılamaz; o güne kadar SAT-D258 (2)'deki
"kullanılamıyor" sonucu fiilen sürer. Bu, yazarın okumasıdır (Sinan kararı değildir); farklı bir ara davranış ancak ayrı
Sinan kararıyla seçilir.

## `OPEN-*` kimliklerinin bağlanması

`OPEN-INV-AVGCOST-*` kimlikleri yalnız Stok kodunda ve README'sinde geçer; `KARAR_SICILI.*` ve `ACIK_KARARLAR.md` içinde
kayıtlı değildir (EK59). Kaynak yorum bu cevapların `OPEN-*` kapatma olmadığını söyler; bu yüzden aşağıdaki durum bir
sicil kapanışı değildir, kimliği Sinan kararına bağlar. Sicil dosyaları değişmez; registry MD/CSV eşliği ve hash yeniden
üretimi gerekmez.

| Kimlik | Durum | Dayanak |
|---|---|---|
| `OPEN-INV-AVGCOST-NEGATIVE-STOCK` | Sinan kararı verildi (#66 6809898906); kod uygulanınca kod yorumu güncellenir | SAT-D301 (madde 9) |
| `OPEN-INV-AVGCOST-LANDED` | Sinan kararı verildi (#66 6809898906); kod uygulanınca kod yorumu güncellenir | SAT-D302 (madde 10) |
| `OPEN-INV-AVGCOST-RETURN` | Sinan kararı verildi (#66 6809898906), yalnız müşteri iadesi için; kod uygulanınca kod yorumu güncellenir | SAT-D303 (madde 11) |
| `OPEN-INV-AVGCOST-UNCOSTED-STOCK` | Sinan kararı verildi (#66 6809898906); kod uygulanınca kod yorumu güncellenir | SAT-D304 (madde 12) |
| `OPEN-INV-AVGCOST-MIXED-SOURCE` | Sinan kararı verildi (#66 6809898906); kod uygulanınca kod yorumu güncellenir | SAT-D305 (madde 13) |
| `OPEN-INV-AVGCOST-PRODUCTION` (test yorumu; MIXED-SOURCE ile aynı konu) | Sinan kararı verildi (#66 6809898906); kod uygulanınca kod yorumu güncellenir | SAT-D305 (madde 13) |
| `OPEN-INV-AVGCOST-FX` | Daha önce karara bağlandı; bu belge değiştirmez | BS-1 (`SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md`, #66 6806579823) |
| `OPEN-008` (üretim maliyeti) | Açık kalır | SAT-D305 (4) |

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
  (`Modül bağlantıları` satırıyla). Alış iadesi açıktır; o hareket bugünkü gibi Unreadable kalır.
- **SAT-D304 (açılış maliyeti) — yeni komut gerekir.** Bugünkü kod: açılış sayımı (`OpeningCount`, INV-OPEN-1) maliyet
  taşımaz; defterin hiç maliyetlemediği stok `Unreadable(LedgerStockMismatch)` olur (bu "yok" yönüyle uyumlu). Karara göre
  giren ≠ onaylayan bir açılış maliyeti komutu (maker-checker) gerekir; onaylanmamış giriş defteri değiştirmez. Yeni izin
  adları kod PR'ında kaydedilir; grant/rol ataması ve Critical sınıfı insan kapısıdır.
- **SAT-D305 (karışık kaynak) — değişiklik gerekir, ama üretim maliyeti girdisine bağlıdır.** Bugünkü kod: `ProductionReceipt`
  pozisyonu `Unreadable(ProductionSource)` yapar (SAT-D258'e göre). Tek ortalamaya geçiş, üretim girişinin birim maliyeti
  bir kaynaktan gelmedikçe sayı üretemez (`OPEN-008`). Fire anahtarı (ayar adı, varsayılan "açık") Ayarlar modülünde
  tanımlanır; fire'nin maliyete eklenme hesabı (ör. iyi çıktı kg'sine dağıtma) uygulama okunuşudur ve formül olduğu için
  K3 review'unda açıkça yazılır.
- **KLN-D20 (sevkten vazgeçme) — B-22b takip dilimi.** Bugünkü kod: `InventoryTransferStatus` yalnız Planned / InTransit /
  PartiallyAccepted / Accepted taşır; iptal durumu yoktur (yeni durum + migration, K3). Lojistik'te transfer çıkış
  belgesinin `Cancel` komutu vardır (`logistics.transfer-outgoing-document.cancel.v1`); ters irsaliye yoktur. "İrsaliye iptal
  edilebiliyor mu" kuralı ve ters irsaliye Lojistik şeridinin sahipliğindedir; Stok iptali bu yeteneğe **modüller arası
  bağımlılık** olarak bağlanır, bu belge Lojistik kuralını seçmez. Uyarı metni Türkçe kullanıcı metnidir (kaynak dosya).
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
| SAT-D305 | Tek hareketli ortalama, fire anahtarı | Stok + Ayarlar; üretim maliyet girdisi `OPEN-008` sonrası; K3 |
| KLN-D20 | B-22b sevk iptali (yeni durum + migration) | Stok; Lojistik irsaliye iptaline bağımlı; K3 |
| KLN-D21 | B-22c teslim alma ve fark → otomatik fire | Stok; K3 |

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz.
- **Kapatma yoktur.** `OPEN-INV-AVGCOST-*` kimlikleri yalnız Sinan kararına bağlanır; `KARAR_SICILI`/`ACIK_KARARLAR`
  ve `OPEN-008` değişmez.
- **Seçilmeyenler:** alış iadesinin maliyeti; vade farkı ve öteki giderler; üretim girişinin birim maliyet kaynağı
  (`OPEN-008`); teslim alınmış sevkin iptali; Lojistik'te irsaliyenin hangi durumda iptal edilebileceği. Bunlar ilgili
  şeritte `NEEDS-SINAN` veya sahip şeridin kararıdır; bu belge tahminle seçmez.
- "Uygulama okunuşu (geri alınabilir)" diye işaretli noktalar Sinan'ın sözü değildir; kod PR'ında yazılır, Sinan farklı
  isterse ayrı karar gerekir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
