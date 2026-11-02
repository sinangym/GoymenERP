# Sinan Kararları — 2026-11-02 Alış EK3 (kalan sorular)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda ("Proje modülleri alış analizi"), Alış hazırlık
planındaki açık sorulara (`docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md` §8, §13.4, §13.7) verdiği **doğrudan cevaplar**.
Dört tur, on altı soru. Serbest metin cevaplar aynen aktarılmıştır; seçenek seçilen cevaplarda "Seçilen" sütunu seçeneğin
adı ve açıklamasıdır. Claude yalnız soruyu ve seçenekleri hazırlamıştır.

## 0. Yetki ve sınır

`SINAN_KARARLARI_2026-11-02_ALIS.md` §0 aynen geçerlidir: kararlar yapılandırılabilir ve fail-closed uygulanır;
hiçbir `OPEN-*` kapanmaz; persona eşlemesi grant değildir; sayısal değerler kod sabiti olmaz. Başka modüle düşen
işler o şeritlere görev notuyla bırakılır. Saklama/imha politikası (ALS-D39) AGENTS.md gereği ayrı politikadır; bu
karar süreyi seçer, imha işleminin canlıda çalıştırılması ayrı insan kapısıdır.

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D29 | Fason işlemin kaydı (örnekli soru: A — kendi rulomuzu fasoncuya gönderip dilimletmek; B — tedarikçinin kendi rulosunu dilip bize satması) | Cevap aynen: **"Bunu anlamadım, a seçeneği evet fason sipariş olacak ama b seçeneği müşteri bize direk mal satmış oluyor, ben alacağım mal için fason yapmam, ben kendi malımı fasona gönderebilirim sahipliği değişmez, yada bana dışardan fason için malzeme gelir o da müşterinin malıdır benim malıma karışmaz ben müşteriye hizmetini yapıp hizmet faturası keserim"** | ALS-D26 (soru anlaşılmamıştı), N-35 | A-1, API-70…72, URT-D15/D17, DEC-STK-008 |
| ALS-D30 | Tedarikçide hazır mal üzerine satış siparişinin ayrılması | Seçenek: **Evet, gelecek mal olarak ayrılır.** Seçenek açıklaması: satış siparişi kaynak planında "alıştan" seçilir ve miktar bu hazır mala bağlanır; aynı ton ikinci kez bağlanamaz; mal gelince fiziksel tahsise döner | N-30 | ALS-D22, ALS-D25, DEC-SAL-004, MOD-16 |
| ALS-D31 | Hubvogal ile birlikte kullanım ve Mikro'ya irsaliye aktarımı | Seçenek: **Birlikte kullanım yok.** Seçenek açıklaması: Alış modülü canlıya alındığı gün açık siparişler Göymen'e taşınır ve hubvogal'de alış kapanır; Mikro aktarımı o günden sonra Göymen'den yapılır | N-37 | MIG-01 |
| ALS-D32 | Fazla teslimin faturalanması | Seçenek: **Aynı fiyatla faturaya girer.** Seçenek açıklaması: fazla miktar siparişe "fazla teslim" olarak bağlıdır; tedarikçi faturasında sipariş fiyatından kabul edilir; fiyat farklıysa normal fiyat farkı onayı (ALS-D8) | N-09, No 40 | API-40, API-51/52 |
| ALS-D33 | Termin dilimleri | Seçenek: **Evet, ilk sürümde.** Seçenek açıklaması: satırda miktar bazlı termin dilimleri; geciken dilim beklenen teslimatlarda işaretlenir; gelecek arz tarihe göre hesaplanır | N-12 | API-30, API-32, ALS-D21 |
| ALS-D34 | Sipariş PDF'i | Seçenek: **Evet, fiyatlı.** Seçenek açıklaması: onaylı siparişin fiyatlı PDF'i tedarikçiye gönderilir; onaysız sürümde "TASLAK" filigranı (Satış'taki gibi) | N-14 | API-16, MOD-11 |
| ALS-D35 | Siparişsiz kabul istisnasının onayı | Seçenek: **Satın alma müdürü.** Seçenek açıklaması: depo kabulü ister, Satın alma müdürü onaylar; onaydan sonra sipariş açılıp kabule bağlanır | N-16, DEC-PUR-001 | API-42 |
| ALS-D36 | Sipariş numarası | Cevap aynen: **"Aynı algoritma ile yeniden başlanabilir hubvogali korumamıza gerek yok"** | N-29 | MIG-01 |
| ALS-D37 | "Takas" ve "Temel" pozisyonları | Cevap aynen: **"Temel, ticari olan yani ürün alış satışı içindi ; takas, mal karşılığı mal ama buna gerek yok, takas olanlarda alışta satışta ayrı siparişler açılabilir"** | N-31 | ALS-D20, API-98 |
| ALS-D38 | Satırda vade farkı | Cevap aynen: **"Satırda ayrıda yazılabilir yani peşin+vade farkı olarak yada tek fiyatta yazılabilir vadeli alış fiyatı olarak"** | N-32 | API-20 |
| ALS-D39 | Sevk bildiriminde şoför bilgileri ve saklama | Seçenek: **TC dahil hepsi** (ad, telefon, TC kimlik no, plaka). Saklama süresi seçeneği: **10 yıl (ticari defter süresi)**; seçenek açıklaması: irsaliye eki olarak ticari belgelerle aynı süre saklanır | N-33 | API-31 |
| ALS-D40 | Fason dışı hizmet alışları (vinç kiralama, dış işçilik, analiz/test) | Seçenek: **Evet, hizmet satırıyla.** Seçenek açıklaması: ticari faaliyete bağlı hizmetler alış siparişinde hizmet satırı olarak alınır; stok hareketi yok; hizmet kabulü ile faturaya hazır olur | N-17 | API-41, API-70 |
| ALS-D41 | TL borç kuru (kur farkı onaylandığında) | Seçenek: **Tedarikçi faturasındaki kur.** Seçenek açıklaması: resmî belge tedarikçinin faturası; Mikro ve muhasebe o kuru kullanır; ERP kontrol kuru yalnız farkı yakalamak içindir | N-25 | ALS-D14/D15, MOD-04 |
| ALS-D42 | Aynı firmanın tedarikçi borcunun satış riskinden düşülmesi | Seçenek: **Düşülmesin.** Seçenek açıklaması: satış riski müşteri alacağı üzerinden hesaplanır; tedarikçi borcu ayrı izlenir; mahsup Finans'ta ayrı işlemle | N-26 | MOD-15 |
| ALS-D43 | Adet ve metre birimleri | Seçenek: **Metre 2 ondalık, tolerans kg ile aynı.** Seçenek açıklaması: metre 2 ondalık (cm), adet tam sayı; %1 uyarı / %2 onay / %3 blok toleransı adet ve metreye de uygulanır; değerler Ayarlar'da ürün bazında değişir | N-28, ALS-D19 | Ö2, API-95, OPEN-004 |

Not: on altı soru on beş satırdadır; ALS-D39 3. turdaki şoför bilgisi sorusu ile 4. turdaki saklama süresi sorusunu birleştirir. Turlar: D29–D32, D33–D36, D37–D40, D41–D43 + saklama.

## 1a. Bilinçli değişiklikler

- **ALS-D26 / N-35 kapandı (ALS-D29):** fason yalnız sahipliği değişmeyen malın işlenmesidir. Giden fason: Göymen'in
  kendi malı fasoncuya gider, mülkiyet Göymen'de kalır → **fason hizmet alış siparişi** (A-1). Gelen fason: müşterinin
  malı Göymen'e gelir, Göymen hizmet faturası keser → **Satış** şeridi (URETIM_SATIS_DEVIR S-3). Tedarikçinin kendi
  malını işleyip satması **fason değildir, normal mal alışıdır**. Bu nedenle fason işlemler masraf satırıyla
  kaydedilmez.
- **ALS-D43, ALS-D19'un açık ayrıntısını kapatır;** adet/metre profilleri Ayarlar'da onaylanana kadar fail-closed
  kuralı (EK1 §2) aynen geçerlidir. Ö2'nin kg kuralı değişmez.
- **ALS-D37:** "Takas" pozisyonu Göymen'e alınmaz; takas işlemleri alışta ve satışta ayrı siparişlerle yapılır.
  "Temel" pozisyonu "Ticari" ile aynıdır, ayrı alınmaz.

## 2. Notlar (geri alınabilir)

- **ALS-D29 göç notu:** hubvogal'deki "Fason – …" masraf tipleri Göymen'e masraf tipi olarak taşınmaz; MIG-01 alan
  eşlemesinde ilgili fason hizmet tipine bağlanması önerilir (göç tercihi, kayıt örnekleriyle doğrulanır).

- **ALS-D36 yorumu:** hubvogal numara biçimi (`0213-01-72-261026-024`) aynı algoritmayla Göymen'de yeniden başlar;
  eski numaralar taşınmaz. Biçimin parçalarının anlamı (ilk blok cari kodu mu, "01" ve "72" ne) MIG-01 alan
  eşlemesinde hubvogal kayıtlarından çıkarılır ve numara serisi Ayarlar'da şirket bazlı tanımlanır; anlamı
  doğrulanmadan seri canlıya alınmaz.
- **ALS-D38 yorumu:** satırda iki giriş biçimi de desteklenir: (a) peşin birim fiyat + vade farkı (% veya tutar),
  (b) tek vadeli birim fiyat. Fatura ve üçlü kontrolde karşılaştırma vade farkı dahil birim fiyatla yapılır. Vade
  farkının KDV matrahına girişi ortak calculator'da ve mali müşavir teyidiyle (N-20) belirlenir; teyide kadar
  vade farkı girilen satır vergi hesaplamaz ("hesaplanamıyor").
- **ALS-D39 notu:** TC kimlik no kişisel veridir (KVKK). Alan erişimi yetkiyle sınırlanır, listelerde maskelenir,
  audit'e yazılır. 10 yıllık saklama ve sonrasındaki imha/anonimleştirme ayrı saklama politikasıdır; imha işleminin
  canlıda çalıştırılması insan kapısıdır. Hukuk/KVKK danışmanının aykırı görüşü olursa bu karar yeniden sorulur.
- **ALS-D30 notu:** gelecek mal üzerine ayırma Satış ve Stok'un işidir (MOD-16, MOD-14); Alış yalnız tedarikçide hazır
  miktarı ve onun ayrılan kısmını gösteren okuma portunu yayımlar. Aynı tonun iki kez ayrılmaması kuralı ayırmanın
  sahibinde (Stok tahsis motoru) uygulanır.
- **ALS-D42:** Finans görev notunda (MOD-15) karar olarak geçer; netleştirme yapılmaz.
