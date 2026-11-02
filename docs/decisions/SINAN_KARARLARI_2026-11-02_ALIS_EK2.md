# Sinan Kararları — 2026-11-02 Alış EK2 (hubvogal incelemesi soruları)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda ("Proje modülleri alış analizi"), şu an kullanılan
hubvogal sisteminin alış ekranları ve raporları incelendikten sonra sorulan seçenekli sorulara verdiği **doğrudan
cevaplar**. Bir tur, dört soru. Serbest metin cevaplar aynen aktarılmıştır. Claude yalnız soruyu ve seçenekleri
hazırlamıştır. İnceleme özeti: `docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md` §13.6.

## 0. Yetki ve sınır

`SINAN_KARARLARI_2026-11-02_ALIS.md` §0 aynen geçerlidir: kararlar yapılandırılabilir ve fail-closed uygulanır;
hiçbir `OPEN-*` kapanmaz; persona eşlemesi grant değildir; sayısal değerler kod sabiti olmaz. Stok ve Satış
modüllerine düşen işler o şeritlere görev notuyla bırakılır (URT-D19/D20).

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D21 | Bağlantı (çerçeve sipariş) fiyatı | **Fiyat bağlantıda sabit.** Hubvogal'deki "bağlantı" Göymen'deki onaylı sipariş satırıdır; her sevk/kabul ("sıra") aynı fiyattan bağlantının kalanından düşer. Fiyat değişirse revizyon (ALS-D9) | Hubvogal raporlarında Bağlantı Miktarı / Bağlantıdan Kalan / Sıra No | ALS-D9, `INV-006`, API-20 |
| ALS-D22 | Üretici stokta hazır ("U.S Hazır") ve "Bülten Hazır" | Cevap aynen: **"Evet, termin/yolda gibi durumlar olsun hatta burda , bülten yada us hazır malzeme sevk kabulüne başlandığında bu henüz satılamayacak şekilde stokta görünmeli ve satış ve üretimde bundan dolayı o ürünlerin gelebileceğini görmeli, ayrıca o us hazır bülten sevk kabülleri değiştirilebilirde olmalı"** | Planda yalnız termin dilimi ve sevk bildirimi ("yolda") vardı | API-30/31, MOD-14, ST-08, DEC-STK-002 |
| ALS-D23 | Ek alış maliyetleri (nakliye, fason kesme/dilme/profil/firkete büküm, kredi kartı komisyonu, finansman bedeli) | **Siparişe/kabule masraf satırı.** Masraf tipi listesi Ayarlar'da; siparişe veya kabule masraf satırı (tutar + KDV) eklenir, ilgili tedarikçi faturasıyla eşleşir. Maliyete dağıtım sonraki muhasebe aşamasıdır | Plan N-27 açıktı | N-27, API-20, API-51/52 |
| ALS-D24 | Vade ve ödeme planı | Cevap aynen: **"Siparişte , her siparişte farklı olabilir"** | Plan N-15 açıktı; hubvogal'de siparişte "Nokta Vade (Ödeme Planı)", 1–12 taksit | N-15, API-22, MOD-04 |

## 2. Notlar (geri alınabilir)

- **ALS-D22 yorumu:** "U.S Hazır" (üreticinin stokunda bizim için hazır) ve "Bülten Hazır" (üretici bülteninde hazır)
  sipariş satırında **tedarikçide hazır** miktarı olarak tutulur; sevk kabulü başladığında bu miktar Stok ekranında
  **ayrı bir "gelen / henüz satılamaz" kategorisinde** görünür. Bu kategori fiziksel stok değildir: sevk edilemez,
  fiziksel tahsise girmez (DEC-STK-002, master §25.5 "gelecek arz fiziksel stok değildir"); Satış ve Üretim onu
  gelecek arz olarak görür ve kaynak planında (DEC-SAL-004) kullanabilir. "Değiştirilebilir" sözü: hazır miktarı ve
  tarihi, kabul tamamlanana kadar gerekçe ve audit ile düzeltilebilir; düzeltme silme değil yeni sürümdür. Satış
  siparişinin bu miktar üzerine **rezerve** edilip edilemeyeceği ayrı karardır (plan N-30). Stok tarafındaki görünüm
  Üretim/Stok şeridinin işidir (görev notu); Alış yalnız miktarı ve durumu yayımlar (MOD-14).
- **ALS-D23 yorumu:** masraf satırı stok miktarını değiştirmez; KDV ve tevkifat ortak calculator'dan gelir. Masrafın
  fason hizmet siparişiyle (A-1) ilişkisi: üretime bağlı fason için A-1 hizmet siparişi esastır; masraf satırı yalnız
  sipariş dışı ek tutarlar içindir. Masraf tipi listesi hubvogal'deki listeyle başlar; değerler Ayarlar'da tutulur.
- **ALS-D24 yorumu:** vade ve ödeme planı (taksit sayısı, her taksitin tarihi ve tutarı) sipariş başlığında girilir
  ve her siparişte farklı olabilir. Cari'deki varsayılan vadenin forma öneri olarak gelip gelmeyeceği seçilmedi;
  ilk sürümde öneri yoktur, alan boş başlar. Taksit toplamı sipariş toplamına eşit olmalıdır (fiyatsız siparişte,
  ALS-D18, plan fiyat girilene kadar "hesaplanamıyor" kalır). Kesin ödeme planı gelen faturayla Finans'a gider (MOD-04).
