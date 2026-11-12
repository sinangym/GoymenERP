# Sinan Kararları — 2026-11-12, 1., 3., 4., 6., 12., 16. ve 17. tur (5., 11. ve 13. tur atıfla) ve 6821569022 kararı (Stok: ters e-İrsaliye sonrası sevk, kantarlı depoda e-İrsaliye'li sevk iptali, kısmi kabulde kayıp etiket, fire anahtarı kapalıyken fire maliyeti, gelecek tedarikten ayırma)

Bu belge önceki karar dosyalarını silmez; **KLN-D25, KLN-D26, KLN-D27 ve SAT-D308**'i ekler. Stok şeridinin #66'da açtığı iki
`NEEDS-SINAN`'ı (6818687464: ters e-İrsaliye sonrası sevk; 6818687113: fire anahtarı kapalıyken fire maliyeti) ve
F-017'nin (gelecek tedarikten ayırma) Stok tarafını Sinan kararına bağlar. 6810239322'nin (kısmi kabulde bekleyen etiket)
4. tur cevabı yeni kimlik almaz: KLN-D23'ü teyit eder (aşağıda). 6. tur, KLN-D25'in "fiilen yola çıkmış" kaynağına ilişkin
`NEEDS-SINAN` 6819731269'u cevaplar (KLN-D25 (6)); 12. tur madde 5 e-İrsaliyesiz sevkte "yola çıktı" anını verir,
16. tur madde 1 bunu depo ayarına bağlar (KLN-D25 (7)); 17. tur madde 3 gönderenin kendi ters e-İrsaliyesini kapatır
(KLN-D25 (6)(c)); 6821569022 kararı kantarlı depodan e-İrsaliye'li transferde iptal penceresini verir (KLN-D27).
5. tur madde 3–4 ile 11.–13. turun F-017 maddeleri KLN-D26 (6)'ya atıfla bağlanır (Alış tarafı ALS-D83/ALS-D84,
#1110). `SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER.md` (SAT-D301…SAT-D307, KLN-D20…KLN-D24), DEC-STK-002/003/004/006,
DEC-SAL-004 ve 13/14 Ekim transfer kararları geçerlidir. Bu belge onların karar metnini değiştirmez; üç istisna daha yeni
Sinan kararıdır: KLN-D20 (2)'nin "irsaliye iptal edilebiliyorsa … sevk de iptal edilebilir" dalı 6. turla (KLN-D25 (6)(e)),
KLN-D20 (3)'ün "teslim alınmamış sevk iptal edilebilir" dalı 12. tur madde 5 ve 16. tur madde 1 ile (KLN-D25 (7)),
KLN-D20 (2)'deki "sehven kesilen irsaliye için açıklamalı ters irsaliye" yolunun GİB'e gitmiş transferde sevki iptal
etmesi 17. tur madde 3 ile (KLN-D25 (6)(c)) inceltilir. Bu PR 11-11 belgesinin KLN-D20 satırına ve iş tablosuna bu
inceltmelere ileri atıf ekler (#1108). #1108'in önceki
commit'i 11-11 belgesinde Seçilmeyenler satırını, yazar okuması işaretlerini, Doğrulama paragrafını ve uygulama notlarını
günceller; Sinan'ın karar metinleri değişmez, yazar okumaları yalnız kapalı yönde daraltılır (SAT-D306 (c)).
`KARAR_SICILI` ve `ACIK_KARARLAR` değişmez.

**Kimlikler:** KLN-D serisinde `main`deki son kimlik KLN-D24, SAT-D serisinde SAT-D307'dir
(`git grep -ohE "SAT-D3[0-9]{2}|KLN-D[0-9]+" origin/main -- docs`, `main` `58b96aa28`). Aynı anda açık 18 PR dalında
`git diff origin/main...<dal>` eklemelerinde SAT-D308+ veya KLN-D25+ geçmiyordu. Transfer ve tahsis Kalan Modüller
şeridinin işi olduğu için KLN-D; fire maliyeti SAT-D301…SAT-D307 maliyet zincirinin devamı olduğu için SAT-D kullanıldı.
Yeni ön ek icat edilmedi. KLN-D27 takip PR'ında eklendi: `main` `77a221352`'de son KLN kimliği KLN-D26'ydı ve açık PR
dallarının `git diff origin/main...<dal>` eklemelerinde KLN-D27+ geçmiyordu.

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

- 11.–13. tur, yorum [6820601088](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6820601088) (madde 2 yeniden
  açılan üretim emrinde çıktının Stok maliyetidir ve henüz hiçbir karar dosyasında kayıtlı değildir (Seçilmeyenler,
  `NEEDS-SPEC`); madde 4 Üretim/Satış şeridine aittir; bu belgeye alınmadı. Madde 1, 3 ve 6–8 11. ve 12. turun, madde 9 13. turun
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

- 16. tur, yorum [6821105608](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821105608) (madde 2–3 Lojistik,
  madde 4 Platform şeridinindir; alınmadı):

  > 1. **Transfer "yola çıktı" ayırıcısı (LOJ-D10 / KLN-D25 (7), 6820896677)** — Soru: sistem bir transferin kantardan geçip geçmeyeceğini nereden bilsin; sevk kaydedilip henüz tartılmamışken iptal açık mı kalsın? **Cevap: Depo ayarı.** Ayarlardan "bu depodan çıkan transferler kantardan geçer" denir. O depoda iptal çıkış tartısına kadar açık kalır. Diğer depolarda sevk kaydedilince kapanır. Kullanıcıya her sevkte ekstra soru yok.
  >
  > Notlar: […] "Kantardan geçer" depo ayarının varsayılanı kapalıdır (ayar açılmadıkça sevk kaydı anı geçerli).

  "Varsayılan kapalı" notu koordinatörün notudur; Sinan'ın cevap metninde geçmez (**geri alınabilir**).

- 17. tur, yorum [6821262098](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821262098) (madde 1–2 Satış/Finans,
  madde 4 Platform, 18. tur Finans/Alış/Ürün şeritlerinindir; alınmadı):

  > 3. **Gönderenin kendi ters e-İrsaliyesi (#1108 KLN-D25 (6)(c))** — Soru: gönderen depo, GİB'e gitmiş transfer e-İrsaliyesi için kendisi ters e-İrsaliye keserse transfer otomatik iptal olsun mu? **Cevap: Hiç iptal yok.** GİB'e gitmiş transfer gönderenin ters irsaliyesiyle iptal olmaz; her durumda varışta kabul + ters transfer.

- Kantarlı depoda e-İrsaliye'li sevk, yorum [6821569022](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821569022)
  (koordinatörün `AskUserQuestion` kaydı; aynı yorumdaki merge makbuzları alınmadı):

  > **SINAN KARARI (2026-11-12, AskUserQuestion):** Kantar zorunlu depodan e-İrsaliye'li transferde iptal penceresi **"hangisi önce gelirse"** orada kapanır: çıkış tartısı **veya** e-İrsaliye'nin GİB'e gönderimi — hangisi önce olursa. (Stok KLN-D25 (7) devamı, Lojistik LOJ-D11 için; şeritler karar dosyasına bu metinle işler.)

- Yorumların sonundaki not (özetle; metinler aynı değildir, 1. tur notu "grant/rol ataması … Gerçek rol grant'ı …" diye
  devam eder): bu cevaplar `OPEN-*` kapatma, grant/rol veya production aktivasyonu değildir. 6821569022'de bu not yoktur;
  Sınırlar bölümü o karar için de geçerlidir.

## Kararlar

| Kimlik | Konu | Karar | Etkilenen yüzey |
|---|---|---|---|
| KLN-D25 | Ters e-İrsaliye sonrası sevk (3. tur madde 4) | (1) Sevke bağlı e-İrsaliye için **açıklamalı ters e-İrsaliye kaydedilince sevk iptal olur**. (2) Kaynak depodan düşen stok **ters hareketle kaynak depoya döner**; dispatch hareketi silinmez (KLN-D20 (1), 14 Ekim kararı 5). Ters hareket kaynak depoya dispatch hareketinin kendi birim maliyetiyle döner; kaynak deponun o anki ortalaması kullanılmaz (SAT-D260 (3) "aynı maliyet bir kez taşınır" ile tutarlı; **yazarın okumasıdır, geri alınabilir**). Bu okuma kilitli karar sayılmaz: K3 kod PR'ı ters hareket maliyetini golden örnek (ChatGPT şeridi) veya `NEEDS-SINAN` cevabı olmadan etkinleştirmez (SAT-D308 uygulama notu gibi). (3) Mal **fiilen yola çıkmışsa** iptal yolu kullanılmaz: alıcı depo teslim alır, geri dönüş **ters transferle** (KLN-D22) yapılır. (4) KLN-D20 (2)'deki "ters irsaliye kesilir" ile KLN-D20 (5)'teki "ters irsaliye kesildikten sonra sevke ne olacağı cevapta yoktur" notu bu maddeyle cevaplanır; KLN-D20 (5)'in "iptal edilemeyen irsaliyede iptal komutu kapalı" okuması, ters e-İrsaliye kaydı gelene kadar geçerli kalır. (5) "Kaydedilince" Stok'un ters e-İrsaliye kaydını **sürümlü bir sözleşmeyle** (okuma portu veya outbox olayı) öğrenmesi demektir; Stok e-İrsaliye/Lojistik tablosuna yazmaz (**yazarın okumasıdır, geri alınabilir**). (6) **"Fiilen yola çıkmış" = sevke bağlı e-İrsaliye GİB'e gönderilmiş** (6. tur madde 1). (a) e-İrsaliye GİB'e gönderilene kadar hatalı sevk iptal edilebilir; iptal (2)'deki ters hareketle yapılır. (b) Gönderildikten sonra kullanıcı iptali kapalıdır; geri dönüş teslim + ters transferle (KLN-D22) olur. (c) Karşı tarafın reddi (ters e-İrsaliye, KLN-D20) yolu aynen çalışır: karşı tarafın ters e-İrsaliyesi kaydedilince (1)–(2) uygulanır; (3)'teki sınır bu yolu kapatmaz. **Otomatik iptal yalnız karşı tarafın reddidir.** GİB'e gitmiş transfer e-İrsaliyesi için **gönderenin kendi kestiği ters e-İrsaliye sevki iptal etmez**; her durumda varışta kabul + ters transfer (KLN-D22) yolu kullanılır (17. tur madde 3). **Sınır:** (1)–(2) yalnız teslim alınmamış sevke uygulanır. `Accepted` veya `PartiallyAccepted` sevke gelen ters e-İrsaliye otomatik iptal ve ters hareket üretmez; alıcı depoya girmiş stok kaynak depoya ikinci kez yazılmaz (AGENTS "aynı stok olayını ikinci hareket olarak üretme"). Bu durumda KLN-D20 (4), KLN-D22 ve KLN-D23 geçerli kalır; ne yapılacağı cevapta yoktur (Seçilmeyenler). Ters e-İrsaliye tüketicisi inbox/dedup ile idempotenttir. (d) Stok GİB gönderim durumunu e-İrsaliye/Lojistik'in sürümlü sözleşmesinden öğrenir, tabloya yazmaz; durum okunamıyorsa (sözleşme yok, `Unreadable`) **kullanıcı iptali** kapalıdır (KLN-D20 (7) kapalı başarısızlık); (c)'deki karşı tarafın ters e-İrsaliyesi yolu bundan etkilenmez. Ters e-İrsaliyenin karşı taraftan mı gönderenden mi geldiği sözleşmeyle okunamıyorsa otomatik iptal yapılmaz (kapalı yön). (e) KLN-D20 (2)'deki "irsaliye iptal edilebiliyorsa iptal edilir ve sevk de iptal edilebilir" dalı **GİB'e gönderilmemiş e-İrsaliyeye** karşılık gelir. GİB'e gönderilmiş e-İrsaliyede entegrasyon iptale izin verse bile kullanıcı iptali kapalıdır; tek otomatik yol (c)'deki karşı tarafın ters e-İrsaliyesidir (6. tur madde 1, 17. tur madde 3). (f) Gönderimden önceki iptal, e-İrsaliye tarafının taslağı iptal ettiğini ya da iptal edilmiş sevk için gönderimi reddettiğini sürümlü sözleşmeyle doğrular; doğrulanamazsa iptal kapalıdır. Taslağın hangi kuralla iptal edileceği e-İrsaliye/Lojistik şeridinindir (Seçilmeyenler); iptal ile GİB gönderimi arasındaki yarış cross-module K3 testinde kanıtlanır. (c)'deki (3) ile birlikte okuma ve sınır cümlesi, (d) ve (f) **yazarın okumasıdır, geri alınabilir**. Bağlı sevkte çıkış tartısından sonra GİB gönderimine kadar iptalin açık kalması Sinan'ın 12. tur madde 5 kuralıdır ("e-İrsaliye'li sevkte 6. tur m.1 geçerli kalır"); "kantardan geçer" ayarı açık olan ya da okunamayan (KLN-D27 (7); okunamayan eki **yazarın okumasıdır, geri alınabilir**) depoda daha yeni Sinan kararı KLN-D27 bunu daraltır (çıkış tartısı veya GİB gönderimi, hangisi önce olursa; araç çıkışı KLN-D27 (7)–(8) gereği). (7) **e-İrsaliye bağlı olmayan sevkte "fiilen yola çıkmış" anı kaynak deponun ayarına bağlıdır** (12. tur madde 5; 16. tur madde 1). (a) Ayarlar'da kaynak depo için **"bu depodan çıkan transferler kantardan geçer"** ayarı vardır (Lojistik karşılığı LOJ-D11, `SINAN_KARARLARI_2026-11-12_LOJISTIK_EK3.md`, #1115). **Varsayılanı kapalıdır** (koordinatörün 16. tur notu, Sinan'ın cevap metninde değil; geri alınabilir). Ayar, (b)'deki Lojistik çıkış okuma portu (LOJ-D10) çalışmadan açılamaz (**yazarın okumasıdır**, kapalı yön). (b) Ayar **açık** depoda mal, araç çıkış tartısı ya da araç çıkışı kaydedilince yola çıkmış sayılır; iptal bu kayda kadar açıktır. Stok kaydı Lojistik'in sürümlü sözleşmesinden okur (LOJ-D10, `SINAN_KARARLARI_2026-11-12_LOJISTIK_EK2.md`, #1115), tabloya yazmaz; kayıt okunamıyorsa (`Unreadable`) mal yola çıkmış sayılır ve iptal kapalıdır (fail-closed). (c) Ayar **kapalı** depoda mal sevkin kaydedildiği an yola çıkmış sayılır; iptal sevk kaydedilince kapanır. Kullanıcıya her sevkte ayrıca soru sorulmaz. (d) Yola çıkıştan sonra iptal yoktur; geri dönüş teslim + ters transferle (KLN-D22) olur. KLN-D20 (3)'ün "teslim alınmamış sevk iptal edilebilir" dalı bu anla daralır: kamyon yoldayken iptal yoktur. e-İrsaliye bağlı sevkte (6) geçerlidir. (e) **Yarış:** Lojistik çıkış kaydı outbox ile geç gelebilir. İptal, "henüz çıkmadı" sonucunu iptal isteği sırasında Lojistik'in sürümlü okuma portundan alır (önbelleğe alınmış veya olaydan türetilmiş bayat değer kullanılmaz). Lojistik transfer çıkış belgesinin sürümü yalnız kesinleştirme/iptalde arttığı için, sürümü expected version olarak taşımak yarışı tek başına kapatmaz. Bu yüzden Stok ters hareketi, Lojistik'in o sevk için çıkış kaydı olmadığını ve bundan sonra çıkış kaydı kabul etmeyeceğini sürümlü sözleşmeyle onaylamasından sonra yazar ((6)(f) ile aynı desen); onay alınamazsa iptal kapalıdır. Onayın Lojistik tarafında nasıl uygulanacağı Lojistik şeridinindir (Seçilmeyenler). Çıkış kaydı ile iptal yarışırsa biri reddedilir ve stok iki kez hareket etmez. Bu, (6)(f)'deki gibi cross-module K3 testinde kanıtlanır. (f) Ayar sevk kaydı anında okunur ve sevke yazılır. e-İrsaliye bağlı olmayan sevkte iptal anında sevke yazılan değer ile ayarın güncel değerinden **kapalı olanı** geçerlidir: ayar sonradan kapatılırsa (örneğin kantar arızası) eski sevkler gelmeyecek tartıyı beklemez, iptalleri kapanır; sonradan açılması kaydedilmiş sevklerde iptali yeniden açmaz. Ayar okunamıyorsa (c) uygulanır (kapalı yön). e-İrsaliye bağlı sevkte bu karşılaştırma ters yönde okunur, çünkü orada ayarın açık olması pencereyi daraltır (KLN-D27 (7)). (g) Yola çıkış bir kez oluşunca geri dönmez: sonradan iptal işaretlenen tartı kaydı (7. tur madde 4) veya sevke sonradan bağlanan e-İrsaliye iptali yeniden açmaz. (e)–(g) **yazarın okumasıdır, geri alınabilir** (kapalı yön). Lojistik LOJ-D10 ve LOJ-D11 aynı ayırıcıyı kullanır; metinler birbirine atıf verir. e-İrsaliye bağlı sevkte ayar açıksa ya da okunamıyorsa KLN-D27 uygulanır (KLN-D27 (7)). | Stok transfer (B-22b iptal), e-İrsaliye ters kayıt sonucu, GİB gönderim durumu ve Lojistik çıkış tartısı/araç çıkışı (sürümlü sözleşmeler) |
| KLN-D26 | Gelecek tedarikten ayırma — Stok tarafı (F-017, 3. tur madde 2) | (1) Açık satınalma siparişi satırından satış siparişine **miktar bazında (kg/ton)** ayırma yapılır; DEC-STK-003'teki "alış satırı" kaynağının miktar biçimidir. (2) **Aynı miktar iki siparişe verilemez**; serbest miktar aşılmaz, ayırma atomik ve idempotenttir (DEC-STK-004). (3) Ayırma fiziksel hareket değildir (DEC-STK-003); stok miktarı değişmez. (4) **Mal kabulde** ayrılan miktar ilgili satış talebine bağlanır; **etiket ataması o zaman** yapılır. (5) Alış satırının ayrılabilir açık miktarını **Alış modülü** sürümlü bir portla verir; Stok Alış tablosuna yazmaz, Alış Stok tablosuna yazmaz (AGENTS "Bir modül başka modül tablosuna doğrudan yazmaz"). Port sözleşmesi Alış şeridiyle ortak yazılır; Alış tarafı kararı ALS-D82'dir (`SINAN_KARARLARI_2026-11-12_ALIS_EK17.md`, PR #1110; merge edilince `main`de). (6) Ayırmanın akıbeti Sinan kararlarıyla cevaplıdır (metinler Kaynak'ta aynen): (a) **Satınalma satırı ayrılmış miktarın altına inerse satınalma tarafı satış ayırma bağını düşürebilir;** alınacak miktar bundan dolayı azalmaz, serbest kalan miktar başka satış siparişine ayrılabilir ya da stok için alınır (5. tur m.3; ALS-D83). (b) **Kısmi kabulde sıra:** termini en yakın satış önce alır; sıra elle değiştirilebilir veya seçilebilir (5. tur m.4; ALS-D84). (c) **Az gelirse** gelen miktar ayrılır, kalan ayırma satınalma satırının gelecek kısmında açık kalır; satır kapanırsa kalan bırakılır ve satış uyarılır. **Fazla gelen** kısım serbest stoğa girer (12. tur m.6). (d) **Ayırması olan satır/sipariş iptali:** önce bağlı satış siparişleri gösterilir; gerekçeli tek onayla bağlar düşer, iptal aynı işlemde yapılır; satışa "kaynak iptal edildi" uyarısı gider (11. tur m.1). (e) **Kabul geri alma/iade:** fiziksel tahsis ve etiket ataması geri alınır; satırda hâlâ gelecek miktar varsa gelecek ayırma yeniden açılır, yoksa ayırma bırakılır ve satış uyarılır (11. tur m.3). (f) **Ayırmayı** izinli kişi (satış, satınalma ya da depo) tek başına yapar, ikinci onay yoktur; izin adı kodlanır, grant insan kapısıdır (12. tur m.7). (g) **Kabulde bağlama başarısızsa** (bloklu etiket dahil) mal kabulü durmaz, mal stoğa girer, satış ayırması bırakılır ve satış uyarılır (12. tur m.8). (h) **Ayırmanın son tarihi yoktur;** satınalma termini geçerse satış ve satınalma kullanıcısı uyarılır, ayırma açık kalır (13. tur m.9). Uygulama ayrıntıları (izin adı, teknik/genel satır eşlemesi, port alanları) F-017 spec'inde (#1116) gerekçelendirilir. | Stok tahsis modeli (gelecek tedarik kaynağı), mal kabulde talebe bağlama, Alış ayrılabilir açık miktar portu (cross-module K3) |
| KLN-D27 | Kantarlı depodan e-İrsaliye'li transferde iptal penceresi (6821569022; KLN-D25 (7) devamı) | (1) Kaynak deposunda "kantardan geçer" ayarı açık olan (KLN-D25 (7)(a); hangi değerin geçerli olduğu (7)'dedir) ve sevke e-İrsaliye bağlı transferde iptal penceresi **çıkış tartısı veya e-İrsaliye'nin GİB'e gönderimi, hangisi önce olursa** orada kapanır. Sinan'ın "kantar zorunlu depo" ifadesi bu ayardır (yorumdaki "KLN-D25 (7) devamı, LOJ-D11 için" etiketi). (2) Bu, KLN-D25 (6)'nın sonundaki 12. tur madde 5 kuralını (çıkış tartısından sonra GİB gönderimine kadar iptal açık) bu depolar için daraltır; daha yeni Sinan kararıdır. KLN-D27 yalnız kullanıcı iptalini daraltır; KLN-D25 (6)(c)–(d)'deki karşı tarafın ters e-İrsaliyesi yolu değişmez (**yazarın okumasıdır, geri alınabilir**). (3) Ayar kapalı depoda (sevke yazılan değer ve güncel değer okunabilir ve ikisi de kapalı) e-İrsaliye'li sevkte KLN-D25 (6) (GİB gönderimi) aynen geçerlidir. (4) İki olaydan biri sürümlü sözleşmeyle okunamıyorsa (`Unreadable`) o olay olmuş sayılır ve iptal kapalıdır (KLN-D25 (6)(d) ve (7)(b) ile aynı kapalı yön). (5) İptal anındaki okuma, Lojistik/e-İrsaliye onayı ve yarış KLN-D25 (6)(f) ve (7)(e) ile aynıdır; pencere bir kez kapanınca yeniden açılmaz (KLN-D25 (7)(g)). Kapanış (an ve dayanağı: çıkış tartısı, araç çıkışı veya GİB gönderimi) sevke kalıcı yazılır; sonradan ayar değişikliği ve sevke sonradan bağlanan e-İrsaliye cross-module K3 test senaryosudur. (6) Lojistik karşılığı LOJ-D11'dir (`SINAN_KARARLARI_2026-11-12_LOJISTIK_EK3.md`, #1115; merge edilince `main`de). (7) **e-İrsaliye bağlı sevkte KLN-D25 (7)(f) ters yönde okunur:** sevke yazılan değer veya güncel değerden biri açıksa ya da ayar okunamıyorsa KLN-D27 uygulanır. KLN-D27 her durumda KLN-D25 (6)'dan dar ya da ona eşittir (kapanış olaylarına çıkış tartısını ya da araç çıkışını ekler); bu yüzden bu okuma pencereyi hiçbir durumda uzatmaz. Ayarın sonradan kapatılması, kaydedilmiş bir çıkış tartısı ya da araç çıkışıyla kapanmış pencereyi yeniden açmaz; çıkış tartısı ya da araç çıkışı okunamıyorsa (4) gereği iptal kapalıdır. Lojistik'te araç çıkışı kaydı da (8) gereği iptali kapatır: çıkış tartısı ya da araç çıkışı (KLN-D25 (7)(b)) kaydedilmişse Lojistik onayı alınamaz. Ayarın "okunamaz" olması okuma hatasıdır; "tanımsız" ayar varsayılan değeri (kapalı, KLN-D25 (7)(a)) taşır; B-22b migration'ı eski sevklere varsayılan "kapalı" değerini yazar (teknik not). (8) Bu depolarda e-İrsaliye bağlı sevkin iptali **iki onayın ikisini de** ister: (6)(f)'deki e-İrsaliye onayı (taslak iptali veya gönderim reddi) ve (7)(e)'deki Lojistik onayı (çıkış kaydı yok, artık kabul edilmez); ikisi alınmadan Stok ters hareketi yazılmaz. Onayların sırası Stok ile e-İrsaliye/Lojistik arasındaki ortak sözleşmedir; biri alınıp öteki alınamazsa telafi ilgili şeridindir (Seçilmeyenler); cross-module K3 testinde kanıtlanır. (1)'deki ayar eşlemesi ve (3)–(8) **yazarın okumasıdır, geri alınabilir** (kapalı yön). | Stok transfer (B-22b iptal), Ayarlar depo ayarı, Lojistik çıkış tartısı/araç çıkışı (LOJ-D10/LOJ-D11) ve e-İrsaliye GİB gönderim durumu (sürümlü sözleşmeler) |
| SAT-D308 | Fire anahtarı kapalıyken fire maliyeti (1. tur madde 4) | (1) SAT-D305 (2)'deki fire anahtarı **kapalıyken** fire maliyeti **ürün maliyetine yüklenmez**. (2) Fire maliyeti **ayrı fire/dönem gideri** olarak raporlanır. (3) Anahtar açıkken SAT-D307 (1)–(2) geçerlidir. (4) SAT-D307 (3)'teki "anahtar kapalıyken fire maliyetinin nereye gideceği cevapta yoktur" notu bu maddeyle cevaplanır. (5) Fire/dönem giderinin hesap eşlemesi masraf/muhasebe yapısına bağlıdır (`NEEDS-SPEC`, 6809898906 madde 7–8); K3 kod PR'ı hesap eşlemesi gelmeden gider kaydını etkinleştirmez, yalnız raporlanacak tutarı hesaplar. (6) Anahtarın "kapalı" konumunun anlamı (1)–(2) ile tanımlıdır; Ayarlar'da seçilebilir hale gelmesi, kapalı yolu calculator'da uygulayan K3 PR'ı merge edilince olur. O zamana kadar mevcut davranış değişmez; seçilebilir olduğunda da gider kaydı (5)'e göre kapalı kalır (5–6: **yazarın okumasıdır, geri alınabilir**). | `AverageCostCalculator` (`ProductionReceipt`), Üretim maliyet portu, fire raporu |

**KLN-D23 teyidi (4. tur madde 1; 6810239322):** Yeni karar değildir. "Gelmeyen etiket varken transfer kapatılamaz" KLN-D23 (1),
"kayıp kesinleşirse açıklama + ikinci kişi onayı ile fire" KLN-D23 (2)(c) ve (3), "mal geri dönerse kaynak depoya ters
hareketle iade" KLN-D23 (2)(b) ile aynıdır. Cevaptaki "**teslim farkı kaydı**" adı, KLN-D23 (2)(c)'deki kayıp/fire kaydının
adıdır; KLN-D21'deki otomatik eksik teslim firesinden ayrıdır (KLN-D23 (4): bekleyen etikete otomatik fire uygulanmaz)
(**yazarın okumasıdır, geri alınabilir**).
"Ters hareketle iade"nin KLN-D23 (2)(b)'deki ters transferle yapılması **yazarın okumasıdır, geri alınabilir**.

## Uygulama notları

- **KLN-D25 / KLN-D27 — B-22b.** Sevk iptali yeni durum + migration gerektirir (K3). İptal, dispatch hareketini ters
  hareketle geri alır; ters hareketin türü B-22b PR'ının seçimidir. e-İrsaliye bağlı sevkte kullanıcı iptali yalnız
  e-İrsaliye GİB'e gönderilmemişken açıktır; gönderim durumu sürümlü sözleşmeyle okunamıyorsa kapalıdır. Gönderildikten
  sonra otomatik iptal yalnız **henüz kabul edilmemiş** sevke karşı tarafın ters e-İrsaliye kaydı sürümlü sözleşmeyle
  gelince olur; gönderenin kendi ters e-İrsaliyesi ve `Accepted`/`PartiallyAccepted` sevke gelen ters e-İrsaliye iptal
  üretmez (kapalı başarısızlık; KLN-D25 (6)(c)). Gönderim öncesi iptal, taslağın iptalini veya gönderimin reddini sürümlü
  sözleşmeyle doğrular; yarış cross-module K3 testinde kanıtlanır (KLN-D25 (6)(f)).
- Kaynak deponun "kantardan geçer" ayarı (Ayarlar; LOJ-D11; varsayılan kapalı, koordinatör notu) sevke yazılır ve
  iptal anında güncel değerle karşılaştırılır. İkisi de daha erken kapanan pencereyi seçer: e-İrsaliyesiz sevkte
  kapalı olan geçerlidir; e-İrsaliye bağlı sevkte biri açıksa ya da ayar okunamıyorsa KLN-D27 uygulanır (KLN-D27 (7)).
  Ayar Lojistik çıkış okuma portu (LOJ-D10) çalışmadan açılamaz. e-İrsaliyesiz sevkte ayar açıksa iptal Lojistik çıkış
  tartısı/araç çıkışı kaydına kadar açıktır, kayıt okunamıyorsa kapalıdır; ayar kapalıysa (tartı kaydı beklenmeyen
  depo: "kayıt yoksa sevk kaydı anı") iptal sevk kaydı anında kapanır (KLN-D25 (7)). e-İrsaliye bağlı sevkte ayar
  açıksa pencere çıkış tartısı ya da araç çıkışı veya GİB gönderiminden hangisi önce olursa orada kapanır (KLN-D27
  (1), (7)); iptal e-İrsaliye ve Lojistik onaylarının ikisini de ister (KLN-D27 (8)). Stok ters hareketi Lojistik'in
  "çıkış kaydı yok, artık kabul edilmez" onayından sonra yazar; expected version tek başına yetmez. Yarış cross-module
  K3 testinde kanıtlanır (KLN-D25 (7)(e)).
- Ters hareket dispatch'in kendi birim maliyetiyle döner (KLN-D25 (2)); bu yazar okumasıdır, golden örnek veya
  `NEEDS-SINAN` cevabı gelmeden etkinleştirilmez. PR'da `Modül bağlantıları` satırı yazılır.
- **KLN-D21 / KLN-D23 — B-22c.** Kısa kabulde KLN-D21'deki otomatik eksik teslim firesi uygulanacaksa kabul ekranı
  kullanıcıyı kabulden önce uyarır (koordinatör notu, kaynağı #1108 DOCS-REVIEW B 6821488400 "Not"; geri alınabilir UX
  ayarı; fire kuralını değiştirmez). Mal fiilen çıkmamışken alıcı eksik kg girerse otomatik firenin yazılıp yazılmayacağı
  stok politikasıdır ve Seçilmeyenler'dedir.
- **KLN-D26 — F-017 dilimi.** Önce spec + dilim planı (docs PR), sonra kod. Stok tarafı: tahsis kaynağına "alış satırı"
  türü, miktar (kg/ton) ile ayırma, aynı miktarın iki siparişe verilmemesi için DB kısıtı ve concurrency testi
  (DEC-STK-004), mal kabulde talebe bağlama ve etiket ataması. Alış tarafı: ayrılabilir açık miktar portu. Birim
  çevrimi SAT-D298'e göre merkezi servisle yapılır; ekran yeniden hesaplamaz.
- **SAT-D308 — Üretim maliyet portu dilimi.** Anahtar kapalı yolu calculator'da tek yerde hesaplanır; golden örnek
  ChatGPT şeridinden veya `NEEDS-SINAN`; K3 review'unda doğrulanır.

| Karar | İş | Şerit / Kapı |
|---|---|---|
| KLN-D25 | GİB gönderiminden önce hatalı sevk iptali; e-İrsaliyesizde "kantardan geçer" depo ayarı açıksa çıkış tartısı/araç çıkışından önce, kapalıysa (tartı kaydı beklenmeyen depo) sevk kaydı anında kapanır (e-İrsaliye'li ve kantarlı depoda KLN-D27); sonra yalnız karşı tarafın ters e-İrsaliyesiyle, teslim alınmamış sevkte iptal + ters hareket; aksi halde teslim + ters transfer | Stok + Ayarlar (depo ayarı); e-İrsaliye ters kayıt + GİB gönderim durumu ve Lojistik çıkış tartısı (LOJ-D10) sözleşmelerine bağımlı; LOJ-D11 depo ayarı; K3 (migration, cross-module) |
| KLN-D27 | Kantarlı depoda e-İrsaliye'li sevkte iptal, çıkış tartısı (ya da araç çıkışı, KLN-D27 (7)) veya GİB gönderiminden hangisi önce olursa orada kapanır | Stok + Ayarlar; Lojistik (LOJ-D10/LOJ-D11) ve e-İrsaliye sözleşmeleri; KLN-D25 ile aynı B-22b PR'ında K3 |
| KLN-D26 | Gelecek tedarikten miktar bazında ayırma, mal kabulde talebe bağlama | Stok + Alış (port); cross-module K3 |
| SAT-D308 | Anahtar kapalıyken fire → ayrı fire/dönem gideri | Stok + Üretim; formül K3; hesap eşlemesi `NEEDS-SPEC` |

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz.
- **Kapatma yoktur.** `KARAR_SICILI`/`ACIK_KARARLAR` değişmez; `OPEN-*` kapatma insan kapısıdır.
- **Seçilmeyenler:** sevk iptal edilince Lojistik transfer çıkış belgesine ne olacağı (11-11 belgesi KLN-D20 (7), Seçilmeyenler);
  `Accepted`/`PartiallyAccepted` sevke ters e-İrsaliye gelirse ne yapılacağı (KLN-D25 (6)(c); 11-11 Seçilmeyenler'deki
  `PartiallyAccepted` sorusu); gönderilmemiş e-İrsaliye taslağının hangi kuralla iptal edileceği (e-İrsaliye/Lojistik şeridi,
  KLN-D25 (6)(f)); Lojistik'in "çıkış kaydı yok, artık kabul edilmez" onayını nasıl uygulayacağı (Lojistik şeridi,
  KLN-D25 (7)(e)); fire/dönem giderinin hesap eşlemesi (`NEEDS-SPEC`); 11. tur madde 2'deki yeniden açılan üretim emrinde
  çıktının Stok maliyeti kuralının hangi karar dosyasına ve kimliğe yazılacağı (Stok + Üretim, `NEEDS-SPEC`; bu belge
  kimlik açmaz); kantarlı depoda e-İrsaliye bağlı sevkte iki onayın sırası (Stok ile e-İrsaliye/Lojistik ortak sözleşmesi) ve biri alınıp öteki alınamazsa telafi, ayrıca
  e-İrsaliyesiz sevkte Lojistik onayından sonra Stok ters hareketi yazamazsa Lojistik kilidinin akıbeti (telafi ve kilit akıbeti ilgili
  e-İrsaliye/Lojistik şeridinin, KLN-D27 (8), KLN-D25 (7)(e)); 17. turdaki "her durumda kabul + ters transfer" kuralında mal fiilen çıkmamışken alıcı
  eksik kg girerse KLN-D21 otomatik firesinin yazılıp yazılmayacağı (stok politikası, `NEEDS-SINAN` adayı). Bunlar ilgili
  dilimde `NEEDS-SINAN`/`NEEDS-SPEC` olarak sorulur; bu belge tahminle seçmez. F-017 ayırma akıbeti sorularının hepsi
  cevaplıdır (KLN-D26 (6)).
- "Yazarın okumasıdır (geri alınabilir)" diye işaretli noktalar Sinan'ın sözü değildir; Sinan farklı isterse ayrı karar
  gerekir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma) değişmez.
