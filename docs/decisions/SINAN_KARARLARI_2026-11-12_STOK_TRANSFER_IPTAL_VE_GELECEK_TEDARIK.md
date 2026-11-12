# Sinan Kararları — 2026-11-12, 3., 4., 6. ve 12. tur (5., 11. ve 13. tur atıfla) (Stok: ters e-İrsaliye sonrası sevk, kısmi kabulde kayıp etiket, fire anahtarı kapalıyken fire maliyeti, gelecek tedarikten ayırma)

Bu belge önceki karar dosyalarını silmez; **KLN-D25, KLN-D26 ve SAT-D308**'i ekler. Stok şeridinin #66'da açtığı iki
`NEEDS-SINAN`'ı (6818687464: ters e-İrsaliye sonrası sevk; 6818687113: fire anahtarı kapalıyken fire maliyeti) ve
F-017'nin (gelecek tedarikten ayırma) Stok tarafını Sinan kararına bağlar. 6810239322'nin (kısmi kabulde bekleyen etiket)
4. tur cevabı yeni kimlik almaz: KLN-D23'ü teyit eder (aşağıda). 6. tur, KLN-D25'in "fiilen yola çıkmış" kaynağına ilişkin
`NEEDS-SINAN` 6819731269'u cevaplar (KLN-D25 (6)); 12. tur madde 5 e-İrsaliyesiz sevkte "yola çıktı" anını verir
(KLN-D25 (7)). 5. tur madde 3–4 ile 11.–13. turun F-017 maddeleri KLN-D26 (6)'ya atıfla bağlanır (Alış tarafı ALS-D83/ALS-D84,
#1110). `SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER.md` (SAT-D301…SAT-D307, KLN-D20…KLN-D24), DEC-STK-002/003/004/006,
DEC-SAL-004 ve 13/14 Ekim transfer kararları geçerlidir. Bu belge onların karar metnini değiştirmez; iki istisna daha yeni
Sinan kararıdır: KLN-D20 (2)'nin "irsaliye iptal edilebiliyorsa … sevk de iptal edilebilir" dalı 6. turla (KLN-D25 (6)(e)),
KLN-D20 (3)'ün "teslim alınmamış sevk iptal edilebilir" dalı 12. tur madde 5 ile (KLN-D25 (7)) inceltilir. Aynı PR'ın önceki
commit'i 11-11 belgesinde Seçilmeyenler satırını, yazar okuması işaretlerini, Doğrulama paragrafını ve uygulama notlarını
günceller; Sinan'ın karar metinleri değişmez, yazar okumaları yalnız kapalı yönde daraltılır (SAT-D306 (c)).
`KARAR_SICILI` ve `ACIK_KARARLAR` değişmez.

**Kimlikler:** KLN-D serisinde `main`deki son kimlik KLN-D24, SAT-D serisinde SAT-D307'dir
(`git grep -ohE "SAT-D3[0-9]{2}|KLN-D[0-9]+" origin/main -- docs`, `main` `58b96aa28`). Aynı anda açık 18 PR dalında
`git diff origin/main...<dal>` eklemelerinde SAT-D308+ veya KLN-D25+ geçmiyordu. Transfer ve tahsis Kalan Modüller
şeridinin işi olduğu için KLN-D; fire maliyeti SAT-D301…SAT-D307 maliyet zincirinin devamı olduğu için SAT-D kullanıldı.
Yeni ön ek icat edilmedi.

**Kaynak:**

- Sinan'ın 12 Kasım 2026'da koordinatör oturumunda `AskUserQuestion` ile verdiği cevaplar; koordinatörün Issue #66'ya
  yazdığı kayıtlar. Bu belgeye alınan maddeler (aynen):
- 3. tur, yorum [6819147253](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819147253) (madde 1 Üretim/Satış,
  madde 3 Üretim şeridinindir; alınmadı):

  > 2. **Gelecek tedarikten ayırma (F-017):** Açık satınalma siparişi satırından satış siparişine **miktar bazında (kg/ton)** ayırma yapılabilir; aynı miktar iki siparişe verilemez. Mal kabulde ilgili satış talebine bağlanır, etiket ataması o zaman yapılır. (Önerilen seçenek.)
  > 4. **Ters e-İrsaliye sonrası transfer (KLN-D20):** Açıklamalı ters e-İrsaliye kaydedilince **sevk iptal olur, stok ters hareketle kaynak depoya döner** (eski hareket silinmez). Mal fiilen yola çıkmışsa iptal yerine teslim + ters transfer yolu kullanılır. (Önerilen seçenek; NEEDS-SINAN 6818687464'ü cevaplar.)
  >
  > Ayrıca: NEEDS-SINAN 6818687113 (fire anahtarı kapalı) 1. tur madde 4 ile cevaplıdır (6818566934): ayrı fire/dönem gideri.

- 1. tur, yorum [6818566934](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6818566934) (madde 1–3 Platform/Ürün
  şeritlerinindir; alınmadı):

  > 4. **Fire maliyeti, anahtar kapalıyken (SAT-D307 (3)):** Fire ürün maliyetine yüklenmez; **ayrı fire/dönem gideri** olarak raporlanır (anahtar açıkken SAT-D307 (1)-(2) geçerli).

- 4. tur, yorum [6819201876](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819201876) (madde 2–4 Platform,
  Finans ve CI işidir; alınmadı):

  > 1. **Transfer kısmi kabul (KLN-D21; NEEDS-SINAN 6810239322):** Gelmeyen etiket varken transfer **kapatılamaz**. Kayıp kesinleşirse açıklama + ikinci kişi onayıyla **teslim farkı kaydı** açılır ve fire olarak yazılır; mal geri dönerse kaynak depoya ters hareketle iade edilir.

- 6. tur, yorum [6819835966](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819835966) (madde 2–3 Üretim,
  madde 4 Finans şeridinindir; alınmadı):

  > 1. **KLN-D25 "mal yola çıktı" kaynağı (Stok NEEDS-SINAN 6819731269):** e-İrsaliye **GİB'e gönderilince** mal yola çıkmış sayılır. Gönderilene kadar hatalı sevk iptal edilebilir; gönderildikten sonra iptal kapalıdır, geri dönüş teslim + ters transferle olur. Karşı tarafın reddi (ters e-İrsaliye, KLN-D20) yolu aynen çalışır. (Önerilen seçenek; Stok'un "sevk kaydedilince" önerisi seçilmedi.)

- 11.–13. tur, yorum [6820601088](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820601088) (madde 2 Stok maliyeti
  ST-F009-D1'e, madde 4 Üretim/Satış şeridine aittir; bu belgeye alınmadı. Madde 1, 3 ve 6–8 11. ve 12. turun, madde 9 13. turun
  F-017 maddeleridir):

  > 5. **e-İrsaliyesiz transferde "yola çıktı" anı (#1108 review B P2-1; KLN-D20 (3)/KLN-D25):** **Çıkış tartısında** (önerilen). Araç çıkış tartısı ya da araç çıkışı kaydedilince mal yola çıkmış sayılır. Tartı kaydı yoksa sevkin kaydedildiği an esas alınır. Bu andan sonra iptal yoktur; geri dönüş teslim + ters transferle olur. e-İrsaliye'li sevkte 6. tur m.1 (GİB'e gönderim) geçerli kalır.
  >
  > 1. **F-017 S4 — ayırması olan satınalma satırı/sipariş iptali (Alış 6820012101; #1116 S2):** **Önce bağları göster** (önerilen). İptal doğrudan yapılmaz. Ekran bağlı satış siparişlerini listeler ve "bağları düşür ve iptal et" seçeneğini sunar. Gerekçeli tek onayla önce bağlar düşer (ALS-D83), sonra iptal aynı işlemde yapılır. Satışa "kaynak iptal edildi" uyarısı gider.
  > 3. **F-017 S12 — kabul geri alma/iade (#1116 S12; #1110 S3):** **Ayırma geri açılsın** (önerilen). Fiziksel tahsis ve etiket ataması geri alınır. Satınalma satırında hâlâ gelecek miktar varsa gelecek ayırma yeniden açılır. Gelecek miktar yoksa ayırma bırakılır ve satış uyarılır.
  > 6. **F-017 S1/S3 — kabulde farklı miktar (#1116 S1, S3):** **Az gelirse kalan açık, fazlası serbest** (önerilen). Az gelirse gelen miktar ayrılır, kalan ayırma satınalma satırının gelecek kısmında açık kalır. Satır kapanırsa kalan bırakılır ve satış uyarılır. Fazla gelen kısım (kg farkı) serbest stoğa girer.
  > 7. **F-017 S5 — ayırmayı kim yapar (#1116 S5):** **Yetkili, tek kişi** (önerilen). İzinli kişi (satış, satınalma ya da depo) tek başına ayırır, ikinci onay yoktur. İzin adı kodlanır; grant insan kapısıdır.
  > 8. **F-017 S7 — kabulde bağlama başarısızsa (#1116 S7, S8 dahil):** **Kabul durmasın, ayırma bırakılsın** (önerilen "kabul yapılsın, ayırma açık kalsın, uyar" seçilmedi). Mal kabulü durmaz, mal stoğa girer. Satış ayırması bırakılır ve satış uyarılır. Bloklu etiket (S8) de bu kurala girer.
  > 9. **F-017 S11 — ayırma süresi (#1116 S11):** **Süre yok, uyar** (önerilen). Ayırmanın son tarihi yoktur. Satınalma termini geçerse satış ve satınalma kullanıcısı uyarılır, ayırma açık kalır. Süre ileride ayarlardan tanımlanabilir.

- 5. tur, yorum [6819640816](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819640816) (madde 1–2 Finans/Üretim
  şeritlerinindir; alınmadı; Alış tarafı ALS-D83/ALS-D84, #1110):

  > 3. **F-017 S1 — ayrılmış miktarın altına inen satınalma satırı (Alış 6819290503):** Sinan'ın metni (aynen): "Satınalma tarafından bu bağ düşürülmeli ama satınalma tarafındaki alınacak miktar düşmez ya başka satış siparişine ayrılabilir yada stok için alınabilir, özellikle ilgili alış sipariş satırı veya siparişin komplesi iptal edilmek istenirse o ayrı bi durum"
  >    - Koordinatör okuması (geri alınabilir): satınalma tarafı satış ayırma bağını düşürebilir (ayırmayı bırakır); satınalma miktarı bundan dolayı azalmaz — serbest kalan miktar başka satış siparişine ayrılabilir veya stok için alınır. Satınalma satırının/siparişin **iptali** ayrı bir durumdur ve ayrıca analiz edilir (Alış şeridi gerekirse yeni NEEDS-SINAN açar).
  > 4. **F-017 S2 — kısmi kabulde ayırma sırası (Alış 6819290503):** Termini en yakın satış önce alır; Sinan ekledi: sıra **elle değiştirilebilir veya seçilebilir** olmalı. (Önerilen seçenek + elle seçim.)

- Yorumların sonundaki not (özetle; metinler aynı değildir, 1. tur notu "grant/rol ataması … Gerçek rol grant'ı …" diye
  devam eder): bu cevaplar `OPEN-*` kapatma, grant/rol veya production aktivasyonu değildir.

## Kararlar

| Kimlik | Konu | Karar | Etkilenen yüzey |
|---|---|---|---|
| KLN-D25 | Ters e-İrsaliye sonrası sevk (3. tur madde 4) | (1) Sevke bağlı e-İrsaliye için **açıklamalı ters e-İrsaliye kaydedilince sevk iptal olur**. (2) Kaynak depodan düşen stok **ters hareketle kaynak depoya döner**; dispatch hareketi silinmez (KLN-D20 (1), 14 Ekim kararı 5). (3) Mal **fiilen yola çıkmışsa** iptal yolu kullanılmaz: alıcı depo teslim alır, geri dönüş **ters transferle** (KLN-D22) yapılır. (4) KLN-D20 (2)'deki "ters irsaliye kesilir" ile KLN-D20 (5)'teki "ters irsaliye kesildikten sonra sevke ne olacağı cevapta yoktur" notu bu maddeyle cevaplanır; KLN-D20 (5)'in "iptal edilemeyen irsaliyede iptal komutu kapalı" okuması, ters e-İrsaliye kaydı gelene kadar geçerli kalır. (5) "Kaydedilince" Stok'un ters e-İrsaliye kaydını **sürümlü bir sözleşmeyle** (okuma portu veya outbox olayı) öğrenmesi demektir; Stok e-İrsaliye/Lojistik tablosuna yazmaz (**yazarın okumasıdır, geri alınabilir**). (6) **"Fiilen yola çıkmış" = sevke bağlı e-İrsaliye GİB'e gönderilmiş** (6. tur madde 1). (a) e-İrsaliye GİB'e gönderilene kadar hatalı sevk iptal edilebilir; iptal (2)'deki ters hareketle yapılır. (b) Gönderildikten sonra kullanıcı iptali kapalıdır; geri dönüş teslim + ters transferle (KLN-D22) olur. (c) Karşı tarafın reddi (ters e-İrsaliye, KLN-D20) yolu aynen çalışır: ters e-İrsaliye kaydedilince (1)–(2) uygulanır; (3)'teki sınır bu yolu kapatmaz. Ters e-İrsaliyeyi alıcının (ret) ya da gönderenin (sehven kesilen irsaliye, KLN-D20 (2)) kesmesi fark etmez. **Sınır:** (1)–(2) yalnız teslim alınmamış sevke uygulanır. `Accepted` veya `PartiallyAccepted` sevke gelen ters e-İrsaliye otomatik iptal ve ters hareket üretmez; alıcı depoya girmiş stok kaynak depoya ikinci kez yazılmaz (AGENTS "aynı stok olayını ikinci hareket olarak üretme"). Bu durumda KLN-D20 (4), KLN-D22 ve KLN-D23 geçerli kalır; ne yapılacağı cevapta yoktur (Seçilmeyenler). Ters e-İrsaliye tüketicisi inbox/dedup ile idempotenttir. (d) Stok GİB gönderim durumunu e-İrsaliye/Lojistik'in sürümlü sözleşmesinden öğrenir, tabloya yazmaz; durum okunamıyorsa (sözleşme yok, `Unreadable`) **kullanıcı iptali** kapalıdır (KLN-D20 (7) kapalı başarısızlık); (c)'deki ters e-İrsaliye yolu bundan etkilenmez. (e) KLN-D20 (2)'deki "irsaliye iptal edilebiliyorsa iptal edilir ve sevk de iptal edilebilir" dalı **GİB'e gönderilmemiş e-İrsaliyeye** karşılık gelir. GİB'e gönderilmiş e-İrsaliyede entegrasyon iptale izin verse bile kullanıcı iptali kapalıdır; tek yol (c)'deki ters e-İrsaliyedir (6. tur madde 1, daha yeni karar). (f) Gönderimden önceki iptal, e-İrsaliye tarafının taslağı iptal ettiğini ya da iptal edilmiş sevk için gönderimi reddettiğini sürümlü sözleşmeyle doğrular; doğrulanamazsa iptal kapalıdır. Taslağın hangi kuralla iptal edileceği e-İrsaliye/Lojistik şeridinindir (Seçilmeyenler); iptal ile GİB gönderimi arasındaki yarış cross-module K3 testinde kanıtlanır. (c)'deki (3) ile birlikte okuma ve sınır cümlesi, (d) ve (f) **yazarın okumasıdır, geri alınabilir**. (7) **e-İrsaliye bağlı olmayan sevkte "fiilen yola çıkmış" = araç çıkış tartısı ya da araç çıkışı kaydedilmiş; tartı kaydı yoksa sevkin kaydedildiği an** (12. tur madde 5). Bu andan sonra iptal yoktur; geri dönüş teslim + ters transferle (KLN-D22) olur. KLN-D20 (3)'ün "teslim alınmamış sevk iptal edilebilir" dalı bu anla daralır: kamyon yoldayken iptal yoktur. e-İrsaliye bağlı sevkte (6) geçerlidir. Stok çıkış tartısı/araç çıkışı kaydını Lojistik'in sürümlü sözleşmesinden okur, tabloya yazmaz; kayıt okunamıyorsa (`Unreadable`) mal yola çıkmış sayılır ve iptal kapalıdır (**yazarın okumasıdır, geri alınabilir**). | Stok transfer (B-22b iptal), e-İrsaliye ters kayıt sonucu, GİB gönderim durumu ve Lojistik çıkış tartısı/araç çıkışı (sürümlü sözleşmeler) |
| KLN-D26 | Gelecek tedarikten ayırma — Stok tarafı (F-017, 3. tur madde 2) | (1) Açık satınalma siparişi satırından satış siparişine **miktar bazında (kg/ton)** ayırma yapılır; DEC-STK-003'teki "alış satırı" kaynağının miktar biçimidir. (2) **Aynı miktar iki siparişe verilemez**; serbest miktar aşılmaz, ayırma atomik ve idempotenttir (DEC-STK-004). (3) Ayırma fiziksel hareket değildir (DEC-STK-003); stok miktarı değişmez. (4) **Mal kabulde** ayrılan miktar ilgili satış talebine bağlanır; **etiket ataması o zaman** yapılır. (5) Alış satırının ayrılabilir açık miktarını **Alış modülü** sürümlü bir portla verir; Stok Alış tablosuna yazmaz, Alış Stok tablosuna yazmaz (AGENTS "Bir modül başka modül tablosuna doğrudan yazmaz"). Port sözleşmesi Alış şeridiyle ortak yazılır; Alış tarafı kararı ALS-D82'dir (`SINAN_KARARLARI_2026-11-12_ALIS_EK17.md`, PR #1110; merge edilince `main`de). (6) Ayırmanın akıbeti Sinan kararlarıyla cevaplıdır (metinler Kaynak'ta aynen): (a) **Satınalma tarafı satış ayırma bağını düşürebilir;** alınacak miktar bundan dolayı azalmaz, serbest kalan miktar başka satış siparişine ayrılabilir ya da stok için alınır (5. tur m.3; ALS-D83). (b) **Kısmi kabulde sıra:** termini en yakın satış önce alır; sıra elle değiştirilebilir veya seçilebilir (5. tur m.4; ALS-D84). (c) **Az gelirse** gelen miktar ayrılır, kalan ayırma satınalma satırının gelecek kısmında açık kalır; satır kapanırsa kalan bırakılır ve satış uyarılır. **Fazla gelen** kısım serbest stoğa girer (12. tur m.6). (d) **Ayırması olan satır/sipariş iptali:** önce bağlı satış siparişleri gösterilir; gerekçeli tek onayla bağlar düşer, iptal aynı işlemde yapılır; satışa "kaynak iptal edildi" uyarısı gider (11. tur m.1). (e) **Kabul geri alma/iade:** fiziksel tahsis ve etiket ataması geri alınır; satırda hâlâ gelecek miktar varsa gelecek ayırma yeniden açılır, yoksa ayırma bırakılır ve satış uyarılır (11. tur m.3). (f) **Ayırmayı** izinli kişi (satış, satınalma ya da depo) tek başına yapar, ikinci onay yoktur; izin adı kodlanır, grant insan kapısıdır (12. tur m.7). (g) **Kabulde bağlama başarısızsa** (bloklu etiket dahil) mal kabulü durmaz, mal stoğa girer, satış ayırması bırakılır ve satış uyarılır (12. tur m.8). (h) **Ayırmanın son tarihi yoktur;** satınalma termini geçerse satış ve satınalma kullanıcısı uyarılır, ayırma açık kalır (13. tur m.9). Uygulama ayrıntıları (izin adı, teknik/genel satır eşlemesi, port alanları) F-017 spec'inde (#1116) gerekçelendirilir. | Stok tahsis modeli (gelecek tedarik kaynağı), mal kabulde talebe bağlama, Alış ayrılabilir açık miktar portu (cross-module K3) |
| SAT-D308 | Fire anahtarı kapalıyken fire maliyeti (1. tur madde 4) | (1) SAT-D305 (2)'deki fire anahtarı **kapalıyken** fire maliyeti **ürün maliyetine yüklenmez**. (2) Fire maliyeti **ayrı fire/dönem gideri** olarak raporlanır. (3) Anahtar açıkken SAT-D307 (1)–(2) geçerlidir. (4) SAT-D307 (3)'teki "anahtar kapalıyken fire maliyetinin nereye gideceği cevapta yoktur" notu bu maddeyle cevaplanır. (5) Fire/dönem giderinin hesap eşlemesi masraf/muhasebe yapısına bağlıdır (`NEEDS-SPEC`, 6809898906 madde 7–8); K3 kod PR'ı hesap eşlemesi gelmeden gider kaydını etkinleştirmez, yalnız raporlanacak tutarı hesaplar. (6) Anahtarın "kapalı" konumunun anlamı (1)–(2) ile tanımlıdır; Ayarlar'da seçilebilir hale gelmesi, kapalı yolu calculator'da uygulayan K3 PR'ı merge edilince olur. O zamana kadar mevcut davranış değişmez; seçilebilir olduğunda da gider kaydı (5)'e göre kapalı kalır (5–6: **yazarın okumasıdır, geri alınabilir**). | `AverageCostCalculator` (`ProductionReceipt`), Üretim maliyet portu, fire raporu |

**KLN-D23 teyidi (4. tur madde 1; 6810239322):** Yeni karar değildir. "Gelmeyen etiket varken transfer kapatılamaz" KLN-D23 (1),
"kayıp kesinleşirse açıklama + ikinci kişi onayı ile fire" KLN-D23 (2)(c) ve (3), "mal geri dönerse kaynak depoya ters
hareketle iade" KLN-D23 (2)(b) ile aynıdır. Cevaptaki "**teslim farkı kaydı**" adı, KLN-D23 (2)(c)'deki kayıp/fire kaydının
adıdır; KLN-D21'deki otomatik eksik teslim firesinden ayrıdır (KLN-D23 (4): bekleyen etikete otomatik fire uygulanmaz)
(**yazarın okumasıdır, geri alınabilir**).
"Ters hareketle iade"nin KLN-D23 (2)(b)'deki ters transferle yapılması **yazarın okumasıdır, geri alınabilir**.

## Uygulama notları

- **KLN-D25 — B-22b.** Sevk iptali yeni durum + migration gerektirir (K3). İptal, dispatch hareketini ters hareketle geri
  alır; ters hareketin türü B-22b PR'ının seçimidir. e-İrsaliye bağlı sevkte kullanıcı iptali yalnız e-İrsaliye
  GİB'e gönderilmemişken açıktır; gönderim durumu sürümlü sözleşmeyle okunamıyorsa kapalıdır. Gönderildikten sonra iptal
  yalnız ters e-İrsaliye kaydı sürümlü sözleşmeyle gelince olur (kapalı başarısızlık; KLN-D25 (6)). Gönderim öncesi iptal,
  taslağın iptalini veya gönderimin reddini sürümlü sözleşmeyle doğrular; yarış cross-module K3 testinde kanıtlanır (KLN-D25
  (6)(f)). e-İrsaliyesiz sevkte iptal, Lojistik çıkış tartısı/araç çıkışı kaydından önce açıktır; kayıt yoksa sevk
  kaydıyla kapanır, okunamıyorsa kapalıdır (KLN-D25 (7)). PR'da `Modül bağlantıları` satırı yazılır.
- **KLN-D26 — F-017 dilimi.** Önce spec + dilim planı (docs PR), sonra kod. Stok tarafı: tahsis kaynağına "alış satırı"
  türü, miktar (kg/ton) ile ayırma, aynı miktarın iki siparişe verilmemesi için DB kısıtı ve concurrency testi
  (DEC-STK-004), mal kabulde talebe bağlama ve etiket ataması. Alış tarafı: ayrılabilir açık miktar portu. Birim
  çevrimi SAT-D298'e göre merkezi servisle yapılır; ekran yeniden hesaplamaz.
- **SAT-D308 — Üretim maliyet portu dilimi.** Anahtar kapalı yolu calculator'da tek yerde hesaplanır; golden örnek
  ChatGPT şeridinden veya `NEEDS-SINAN`; K3 review'unda doğrulanır.

| Karar | İş | Şerit / Kapı |
|---|---|---|
| KLN-D25 | GİB gönderiminden (e-İrsaliyesizde çıkış tartısından) önce hatalı sevk iptali; sonra yalnız ters e-İrsaliye kaydıyla iptal + ters hareket; aksi halde teslim + ters transfer | Stok; e-İrsaliye ters kayıt + GİB gönderim durumu ve Lojistik çıkış tartısı sözleşmelerine bağımlı; K3 (migration, cross-module) |
| KLN-D26 | Gelecek tedarikten miktar bazında ayırma, mal kabulde talebe bağlama | Stok + Alış (port); cross-module K3 |
| SAT-D308 | Anahtar kapalıyken fire → ayrı fire/dönem gideri | Stok + Üretim; formül K3; hesap eşlemesi `NEEDS-SPEC` |

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz.
- **Kapatma yoktur.** `KARAR_SICILI`/`ACIK_KARARLAR` değişmez; `OPEN-*` kapatma insan kapısıdır.
- **Seçilmeyenler:** sevk iptal edilince Lojistik transfer çıkış belgesine ne olacağı (11-11 belgesi KLN-D20 (7), Seçilmeyenler);
  `Accepted`/`PartiallyAccepted` sevke ters e-İrsaliye gelirse ne yapılacağı (KLN-D25 (6)(c); 11-11 Seçilmeyenler'deki
  `PartiallyAccepted` sorusu); gönderilmemiş e-İrsaliye taslağının hangi kuralla iptal edileceği (e-İrsaliye/Lojistik şeridi,
  KLN-D25 (6)(f)); fire/dönem giderinin hesap eşlemesi (`NEEDS-SPEC`). Bunlar ilgili dilimde `NEEDS-SINAN`/`NEEDS-SPEC`
  olarak sorulur; bu belge tahminle seçmez. F-017 ayırma akıbeti sorularının hepsi cevaplıdır (KLN-D26 (6)).
- "Yazarın okumasıdır (geri alınabilir)" diye işaretli noktalar Sinan'ın sözü değildir; Sinan farklı isterse ayrı karar
  gerekir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma) değişmez.
