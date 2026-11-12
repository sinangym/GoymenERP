# Sinan Kararları — 2026-11-12, 3. ve 4. tur (Stok: ters e-İrsaliye sonrası sevk, kısmi kabulde kayıp etiket, fire anahtarı kapalıyken fire maliyeti, gelecek tedarikten ayırma)

Bu belge önceki karar dosyalarını silmez; **KLN-D25, KLN-D26 ve SAT-D308**'i ekler. Stok şeridinin #66'da açtığı iki
`NEEDS-SINAN`'ı (6818687464: ters e-İrsaliye sonrası sevk; 6818687113: fire anahtarı kapalıyken fire maliyeti) ve
F-017'nin (gelecek tedarikten ayırma) Stok tarafını Sinan kararına bağlar. 6810239322'nin (kısmi kabulde bekleyen etiket)
4. tur cevabı yeni kimlik almaz: KLN-D23'ü teyit eder (aşağıda). `SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER.md`
(SAT-D301…SAT-D307, KLN-D20…KLN-D24), DEC-STK-002/003/004/006, DEC-SAL-004 ve 13/14 Ekim transfer kararları geçerlidir;
bu belge onların karar metnini değiştirmez. Aynı PR'da yalnız `STOK_MALIYET_VE_TRANSFER` belgesinin "Seçilmeyenler"
satırındaki iki `NEEDS-SINAN` maddesi bu belgeye işaret edecek şekilde güncellenir. `KARAR_SICILI` ve `ACIK_KARARLAR`
değişmez.

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

- Üç yorumun sonundaki not (özetle; metinler aynı değildir, 1. tur notu "grant/rol ataması … Gerçek rol grant'ı …" diye
  devam eder): bu cevaplar `OPEN-*` kapatma, grant/rol veya production aktivasyonu değildir.

## Kararlar

| Kimlik | Konu | Karar | Etkilenen yüzey |
|---|---|---|---|
| KLN-D25 | Ters e-İrsaliye sonrası sevk (3. tur madde 4) | (1) Sevke bağlı e-İrsaliye için **açıklamalı ters e-İrsaliye kaydedilince sevk iptal olur**. (2) Kaynak depodan düşen stok **ters hareketle kaynak depoya döner**; dispatch hareketi silinmez (KLN-D20 (1), 14 Ekim kararı 5). (3) Mal **fiilen yola çıkmışsa** iptal yolu kullanılmaz: alıcı depo teslim alır, geri dönüş **ters transferle** (KLN-D22) yapılır. (4) KLN-D20 (2)'deki "ters irsaliye kesilir" ile KLN-D20 (5)'teki "ters irsaliye kesildikten sonra sevke ne olacağı cevapta yoktur" notu bu maddeyle cevaplanır; KLN-D20 (5)'in "iptal edilemeyen irsaliyede iptal komutu kapalı" okuması, ters e-İrsaliye kaydı gelene kadar geçerli kalır. (5) "Kaydedilince" Stok'un ters e-İrsaliye kaydını **sürümlü bir sözleşmeyle** (okuma portu veya outbox olayı) öğrenmesi demektir; Stok e-İrsaliye/Lojistik tablosuna yazmaz (**yazarın okumasıdır, geri alınabilir**). (6) "Fiilen yola çıkmış" bilgisinin hangi kayıttan okunacağı (ör. araç çıkışı, Lojistik yükleme kaydı) cevapta yoktur; B-22b PR'ı bunu seçmez. "Teslim alınmamış" sevk "yola çıkmamış" demek değildir. Bu yüzden kaynak Sinan kararıyla seçilene kadar **e-İrsaliye bağlı sevkte iptal komutu, ters e-İrsaliye kaydı gelse bile kapalı kalır** (KLN-D20 (7) kapalı başarısızlık); böyle bir sevk teslim + ters transfer yoluyla (KLN-D22) kapanır. Kaynak sorusu #66'ya `NEEDS-SINAN` olarak yazılır. | Stok transfer (B-22b iptal), e-İrsaliye ters kayıt sonucu (sürümlü sözleşme) |
| KLN-D26 | Gelecek tedarikten ayırma — Stok tarafı (F-017, 3. tur madde 2) | (1) Açık satınalma siparişi satırından satış siparişine **miktar bazında (kg/ton)** ayırma yapılır; DEC-STK-003'teki "alış satırı" kaynağının miktar biçimidir. (2) **Aynı miktar iki siparişe verilemez**; serbest miktar aşılmaz, ayırma atomik ve idempotenttir (DEC-STK-004). (3) Ayırma fiziksel hareket değildir (DEC-STK-003); stok miktarı değişmez. (4) **Mal kabulde** ayrılan miktar ilgili satış talebine bağlanır; **etiket ataması o zaman** yapılır. (5) Alış satırının ayrılabilir açık miktarını **Alış modülü** sürümlü bir portla verir; Stok Alış tablosuna yazmaz, Alış Stok tablosuna yazmaz (AGENTS "Bir modül başka modül tablosuna doğrudan yazmaz"). Port sözleşmesi Alış şeridiyle ortak yazılır; Alış tarafı kararı ALS-D82'dir (`SINAN_KARARLARI_2026-11-12_ALIS_EK17.md`, PR #1110; merge edilince `main`de). (6) Mal kabulde gelen miktar ayrılandan az veya fazla olursa, alış siparişi kapatılır ya da iptal edilirse ayırmaya ne olacağı cevapta yoktur (Seçilmeyenler). | Stok tahsis modeli (gelecek tedarik kaynağı), mal kabulde talebe bağlama, Alış ayrılabilir açık miktar portu (cross-module K3) |
| SAT-D308 | Fire anahtarı kapalıyken fire maliyeti (1. tur madde 4) | (1) SAT-D305 (2)'deki fire anahtarı **kapalıyken** fire maliyeti **ürün maliyetine yüklenmez**. (2) Fire maliyeti **ayrı fire/dönem gideri** olarak raporlanır. (3) Anahtar açıkken SAT-D307 (1)–(2) geçerlidir. (4) SAT-D307 (3)'teki "anahtar kapalıyken fire maliyetinin nereye gideceği cevapta yoktur" notu bu maddeyle cevaplanır. (5) Fire/dönem giderinin hesap eşlemesi masraf/muhasebe yapısına bağlıdır (`NEEDS-SPEC`, 6809898906 madde 7–8); K3 kod PR'ı hesap eşlemesi gelmeden gider kaydını etkinleştirmez, yalnız raporlanacak tutarı hesaplar. (6) Anahtarın "kapalı" konumunun anlamı (1)–(2) ile tanımlıdır; Ayarlar'da seçilebilir hale gelmesi, kapalı yolu calculator'da uygulayan K3 PR'ı merge edilince olur. O zamana kadar mevcut davranış değişmez; seçilebilir olduğunda da gider kaydı (5)'e göre kapalı kalır (5–6: **yazarın okumasıdır, geri alınabilir**). | `AverageCostCalculator` (`ProductionReceipt`), Üretim maliyet portu, fire raporu |

**KLN-D23 teyidi (4. tur madde 1; 6810239322):** Yeni karar değildir. "Gelmeyen etiket varken transfer kapatılamaz" KLN-D23 (1),
"kayıp kesinleşirse açıklama + ikinci kişi onayı ile fire" KLN-D23 (2)(c) ve (3), "mal geri dönerse kaynak depoya ters
hareketle iade" KLN-D23 (2)(b) ile aynıdır. Cevaptaki "**teslim farkı kaydı**" adı, KLN-D23 (2)(c)'deki kayıp/fire kaydının
adıdır; KLN-D21'deki otomatik eksik teslim firesinden ayrıdır (KLN-D23 (4): bekleyen etikete otomatik fire uygulanmaz)
(**yazarın okumasıdır, geri alınabilir**).
"Ters hareketle iade"nin KLN-D23 (2)(b)'deki ters transferle yapılması **yazarın okumasıdır, geri alınabilir**.

## Uygulama notları

- **KLN-D25 — B-22b.** Sevk iptali yeni durum + migration gerektirir (K3). İptal, dispatch hareketini ters hareketle geri
  alır; ters hareketin türü B-22b PR'ının seçimidir. e-İrsaliye bağlı sevkte iptal, ters e-İrsaliye sonucu sürümlü
  sözleşmeyle gelene **ve "fiilen yola çıkmış" bilgisinin kaynağı Sinan kararıyla seçilene** kadar kapalı kalır (kapalı
  başarısızlık; KLN-D25 (6)). PR'da `Modül bağlantıları` satırı yazılır.
- **KLN-D26 — F-017 dilimi.** Önce spec + dilim planı (docs PR), sonra kod. Stok tarafı: tahsis kaynağına "alış satırı"
  türü, miktar (kg/ton) ile ayırma, aynı miktarın iki siparişe verilmemesi için DB kısıtı ve concurrency testi
  (DEC-STK-004), mal kabulde talebe bağlama ve etiket ataması. Alış tarafı: ayrılabilir açık miktar portu. Birim
  çevrimi SAT-D298'e göre merkezi servisle yapılır; ekran yeniden hesaplamaz.
- **SAT-D308 — Üretim maliyet portu dilimi.** Anahtar kapalı yolu calculator'da tek yerde hesaplanır; golden örnek
  ChatGPT şeridinden veya `NEEDS-SINAN`; K3 review'unda doğrulanır.

| Karar | İş | Şerit / Kapı |
|---|---|---|
| KLN-D25 | Ters e-İrsaliye kaydıyla sevk iptali + ters hareket (çıkış bilgisi kaynağı seçilene kadar kapalı); yola çıkmışsa teslim + ters transfer | Stok; e-İrsaliye ters kayıt sözleşmesine bağımlı; K3 (migration) |
| KLN-D26 | Gelecek tedarikten miktar bazında ayırma, mal kabulde talebe bağlama | Stok + Alış (port); cross-module K3 |
| SAT-D308 | Anahtar kapalıyken fire → ayrı fire/dönem gideri | Stok + Üretim; formül K3; hesap eşlemesi `NEEDS-SPEC` |

## Sınırlar

- **Kod, veri ve migration değişmez.** Bu belge kod, manifest, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; izin veya grant açmaz.
- **Kapatma yoktur.** `KARAR_SICILI`/`ACIK_KARARLAR` değişmez; `OPEN-*` kapatma insan kapısıdır.
- **Seçilmeyenler:** "fiilen yola çıkmış" bilgisinin kaynağı (KLN-D25 (6); seçilene kadar e-İrsaliye bağlı sevkte iptal
  kapalı); sevk iptal edilince Lojistik transfer çıkış belgesine ne olacağı (11-11 belgesi KLN-D20 (7), Seçilmeyenler); mal kabulde gelen miktar ayrılandan az/fazla
  olursa veya alış siparişi kapatılır/iptal edilirse ayırmanın akıbeti (KLN-D26 (6)); fire/dönem giderinin hesap
  eşlemesi (`NEEDS-SPEC`). Bunlar spec/dilim planında `NEEDS-SINAN` olarak sorulur; bu belge tahminle seçmez.
- "Yazarın okumasıdır (geri alınabilir)" diye işaretli noktalar Sinan'ın sözü değildir; Sinan farklı isterse ayrı karar
  gerekir.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma) değişmez.
