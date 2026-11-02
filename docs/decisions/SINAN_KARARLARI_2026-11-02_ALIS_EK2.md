# Sinan Kararları — 2026-11-02 Alış EK2 (hubvogal incelemesi soruları)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda ("Proje modülleri alış analizi"), şu an kullanılan
hubvogal sisteminin alış ekranları ve raporları incelendikten sonra sorulan seçenekli sorulara verdiği **doğrudan
cevaplar**. İki tur, sekiz soru (2. tur, 1. turun yorumları review'da politika seçimi sayılınca soruldu). Serbest
metin cevaplar aynen aktarılmıştır; seçenek seçilen cevaplarda "Seçilen" sütunu seçeneğin adı ve açıklamasıdır. Claude yalnız soruyu ve seçenekleri
hazırlamıştır. İnceleme özeti: `docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md` §13.6.

## 0. Yetki ve sınır

`SINAN_KARARLARI_2026-11-02_ALIS.md` §0 aynen geçerlidir: kararlar yapılandırılabilir ve fail-closed uygulanır;
hiçbir `OPEN-*` kapanmaz; persona eşlemesi grant değildir; sayısal değerler kod sabiti olmaz. Stok ve Satış
modüllerine düşen işler o şeritlere görev notuyla bırakılır (URT-D19/D20).

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D21 | Bağlantı (çerçeve sipariş) fiyatı | Seçenek: **Fiyat bağlantıda sabit.** Seçenek açıklaması: hubvogal'deki "bağlantı" Göymen'deki onaylı sipariş satırıdır; her sevk/kabul ("sıra") aynı fiyattan bağlantının kalanından düşer. Fiyat değişirse revizyon (ALS-D9) | Hubvogal raporlarında Bağlantı Miktarı / Bağlantıdan Kalan / Sıra No | ALS-D9, `INV-006`, API-20 |
| ALS-D22 | Üretici stokta hazır ("U.S Hazır") ve "Bülten Hazır" | Cevap aynen: **"Evet, termin/yolda gibi durumlar olsun hatta burda , bülten yada us hazır malzeme sevk kabulüne başlandığında bu henüz satılamayacak şekilde stokta görünmeli ve satış ve üretimde bundan dolayı o ürünlerin gelebileceğini görmeli, ayrıca o us hazır bülten sevk kabülleri değiştirilebilirde olmalı"** | Planda yalnız termin dilimi ve sevk bildirimi ("yolda") vardı | API-30/31, MOD-14, ST-08, DEC-STK-002 |
| ALS-D23 | Ek alış maliyetleri (nakliye, fason kesme/dilme/profil/firkete büküm, kredi kartı komisyonu, finansman bedeli) | Seçenek: **Siparişe/kabule masraf satırı.** Seçenek açıklaması: masraf tipi listesi Ayarlar'da; siparişe veya kabule masraf satırı (tutar + KDV) eklenir, ilgili tedarikçi faturasıyla eşleşir. Maliyete dağıtım sonraki muhasebe aşamasıdır | Plan N-27 açıktı | N-27, API-20, API-51/52 |
| ALS-D24 | Vade ve ödeme planı | Cevap aynen: **"Siparişte , her siparişte farklı olabilir"** | Plan N-15 açıktı; hubvogal'de siparişte "Nokta Vade (Ödeme Planı)", 1–12 taksit | N-15, API-22, MOD-04 |
| ALS-D25 | "U.S Hazır" / "Bülten Hazır" malın mülkiyeti ve faturası | Seçenek: **Hayır: henüz gelecek mal.** Seçenek açıklaması: mal ve fatura hâlâ üreticide; ERP'de gelecek arz olarak görünür, stok sayılmaz; kabul mal bize gelince yapılır | Hubvogal bunu üretici deposuna "stoğa giriş" olarak kaydediyor; ALS-D22'nin "stokta görünmeli" ifadesi iki türlü okunabiliyordu | ALS-D22, DEC-STK-002, master §25.5, MOD-14 |
| ALS-D26 | Fason işlemlerin kaydı (hubvogal'de "Fason – boy kesme…" masraf tipi ve "Rulo boy açma hizmeti…" hizmet tipi) | Cevap aynen: **"Bunu anlamadım, fason ile hizmet aynı farklı isimler kullanılmış olabilir"** | – | ALS-D23, API-70, A-1 |
| ALS-D27 | Masraf satırının carisi | Cevap aynen: **"Masraf ilgili firmanın ilgili siparişindeki faturaya ilgili cariden dolayıda eklenebilir, farklı cariden dolayıda masraf faturası almış olabilirim"** | – | ALS-D23, API-23, API-51/52 |
| ALS-D28 | Ödeme planı toplamı ve varsayılan vade | Cevap aynen: **"Ödenecek tutarla aynı olmalı ve vade tipi varsayılan değil her siparişte farklı olabilir"** | ALS-D24 | API-22 |

## 2. Notlar (geri alınabilir)

- **ALS-D22 + ALS-D25 birlikte:** "U.S Hazır" ve "Bülten Hazır" miktar sipariş satırında **tedarikçide hazır** miktarı
  olarak tutulur. Mal ve fatura hâlâ üreticide olduğu için (ALS-D25) bu miktar **fiziksel stok değildir**; gelecek
  arzdır (DEC-STK-002, master §25.5). ALS-D22'deki "henüz satılamayacak şekilde stokta görünmeli" sözü, Stok
  ekranında gelecek arz içinde ayrı bir "tedarikçide hazır" satırı olarak uygulanır: sevk edilemez, fiziksel tahsise
  girmez; Satış ve Üretim onu gelecek mal olarak görür. "Değiştirilebilir" sözü: hazır miktarı ve tarihi kabul
  tamamlanana kadar gerekçe ve audit ile düzeltilebilir; düzeltme silme değil yeni sürümdür. Mal kabulü mal Göymen'e
  gelince yapılır. Satış siparişinin bu miktar üzerine rezerve edilip edilemeyeceği ayrı karardır (plan N-30).
- **ALS-D26 yorumu:** Sinan'ın cevabına göre hubvogal'deki "Fason – boy kesme / dilme / profil…" masraf tipleri ile
  "Rulo boy açma / dilme / profil fason hizmeti" hizmet tipleri muhtemelen aynı işin iki adıdır. Göymen'de **tek bir
  işlem/hizmet tipi listesi** tutulur (Ayarlar). Bir fason işlemin ayrı hizmet siparişiyle mi (A-1: malzeme fasoncuya
  gider, kg dengesi Üretim'de) yoksa siparişe masraf satırıyla mı kaydedileceği **seçilmedi**; plan N-35'te örneklerle
  yeniden sorulur. Cevap gelene kadar iki yol da kapalı değildir, ama aynı fason işi için ikisi birden kullanılamaz.
- **ALS-D23 + ALS-D27 yorumu:** masraf satırı stok miktarını değiştirmez; KDV ve tevkifat ortak calculator'dan gelir.
  Masraf satırının **carisi** sipariş tedarikçisi olabilir veya farklı bir cari (ör. nakliye firması) seçilebilir;
  satır o carinin gelen faturasıyla eşleşir (API-51/52). Masraf tipi listesi hubvogal'deki listeyle başlar; değerler
  Ayarlar'da tutulur. Hubvogal'de "Maliyet Tipleri" ve hiyerarşik "Masraf Tipleri" iki ayrı listedir; Göymen'de birleşip
  birleşmeyeceği MIG-01 alan eşlemesinde netleşir.
- **ALS-D24 + ALS-D28 yorumu:** vade ve ödeme planı (taksit sayısı, her taksitin tarihi ve tutarı) sipariş başlığında
  girilir; Cari'deki vade forma öneri olarak **gelmez**, her sipariş kendi vadesini taşır. Taksit toplamı **ödenecek
  tutara** eşit olmalıdır. "Ödenecek tutar" = tedarikçiye ödenecek KDV dahil toplam − tevkif edilen KDV (tevkifatı
  Göymen öder) + aynı cariye ait masraf satırları; bu tanım geri alınabilir yorumdur ve mali müşavir teyidine
  (plan N-20) bağlıdır. Fiyatsız siparişte (ALS-D18) plan fiyat girilene kadar "hesaplanamıyor" kalır; revizyonda
  (ALS-D9) eşitlik yeniden kontrol edilir. Kesin ödeme planı gelen faturayla Finans'a gider (MOD-04).
- **Bülten kuru ve tarihi** (hubvogal sipariş detayı) fiyat referansıdır; ALS-D21 gereği bağlantı fiyatını kendiliğinden
  değiştirmez. Bülten kurunun dövizli siparişte kontrol kuru olarak kullanılıp kullanılmayacağı seçilmedi (plan N-36).
