# Sinan Kararları — 2026-11-02 Alış EK1 (yeniden analiz soruları)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda ("Proje modülleri alış analizi"), Alış hazırlık
planının yeniden analizi (`docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md` §13) sırasında sorulan seçenekli sorulara verdiği
**doğrudan cevaplar**. İki tur, yedi soru. 1. tur (üç soru) yeniden analizden çıktı; 2. tur (dört soru) Sinan'ın aynı
gün paylaştığı hubvogal (şu an kullanılan sistem) alış sipariş listesi ve sipariş ekleme ekranı görüntülerinden
çıktı. Serbest metin cevaplar aynen aktarılmıştır. Claude yalnız soruyu ve seçenekleri hazırlamıştır.

## 0. Yetki ve sınır

`SINAN_KARARLARI_2026-11-02_ALIS.md` §0 aynen geçerlidir: bu kararlar yapılandırılabilir ve fail-closed uygulanır;
hiçbir `OPEN-*` kapanmaz; persona eşlemesi grant değildir; sayısal değerler kod sabiti olmaz.

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D14 | Çok günlü kabulü kapsayan tedarikçi faturasında ERP kontrol kuru | **Her kabul kendi günü.** Her kabul payı kendi kabul gününden önceki iş günü TCMB kuruyla (ALS-D6) kontrol edilir; Satış'taki "her sevk kendi günü" mantığıyla aynı | ALS-D6 tek kabul için yazılmıştı | Plan §13 B-4, N-22, SAT-D90 |
| ALS-D15 | Tedarikçi faturasındaki kur ERP kontrol kurundan farklıysa | **Müdür onayı.** Kuruş yuvarlaması dışında her kur farkı, fiyat farkı gibi (ALS-D8) Satın alma müdürü onayı olmadan kesinleşmez | Satış'ta kur farkı yalnız işaretlenir (SAT-D172) | N-23, API-52/53 |
| ALS-D16 | Üretim planından açılan satın alma talebi | **Normal talep gibi müdür onayı bekler.** Talep havuzunda "Üretim" kaynağıyla bekler; plan onayı üretimin kararıdır, ne kadar ve kimden alınacağı satın almanın | URETIM_ALIS_DEVIR A-2 onayı belirtmiyordu | N-24, ALS-D2, MOD-02 |
| ALS-D17 | Hubvogal'deki "Rezerve Sipariş" onay durumu | Cevap aynen: **"Bu durum bizdeki talep ve onaylanmış talep ve tedarikçinin onay durumları kalsın"** | Hubvogal: Onaylanan Sipariş / Rezerve Sipariş | ALS-D2, ALS-D13, MIG-01 |
| ALS-D18 | Fiyatsız sipariş onayı | **Evet, fiyat sonra girilir.** Fiyat satırda boş kalabilir; sipariş onaylanır, mal kabul yapılır. Fiyat girilene kadar sipariş "fiyat bekliyor" görünür ve faturayla eşleşmez. Sonradan fiyat girmek Rev+1 + onay ister (ALS-D9) | Hubvogal'de onaylı siparişlerin çoğu 0 fiyatlı | ALS-D5, ALS-D9, API-20, API-51 |
| ALS-D19 | Satır birimi | **Ürüne göre değişsin** (önerilen "kg esas, adet/metre bilgi" seçilmedi). Bazı ürünler adet veya metre ile sipariş edilir ve faturalanır | Ö2: yalnız kg profili onaylı; hubvogal satırında kg + adet + metre birlikte | Ö2, `PurchaseUnitVerifier`, Ürün alış birimi (DEC-PRD-006) |
| ALS-D20 | "Tedarik No" ve "Sipariş Pozisyon" | Cevap aynen: **"Tedarik no, tedarikçinin verdiği sipariş numarası, sipariş pozisyonu hizmet mi ürün(ticari mi), iade mi , teşvikli mi , ihraç kayıtlı mı gibi"** | Hubvogal sipariş başlığı alanları | ALS-D13, API-70, N-20 |

## 1a. Bilinçli değişiklikler

- **ALS-D6 ile ilişki (ALS-D14/D15):** ALS-D6 "Satışla aynı… faturada sayı olarak sabit" der. Alışta resmî faturayı ve
  kurunu tedarikçi düzenler; Sinan 1. turda bu çerçeveyle sorulan iki soruya cevap verdi. Buna göre alışta ERP'nin
  ALS-D6/D14 kuru **kontrol kuru** olarak gelen faturaya sayı olarak yazılır ve sabit kalır; resmî kur tedarikçinin
  belgesindeki değerdir ve ayrıca kaydedilir; ikisi farklıysa ALS-D15 onayı gerekir. Finans'a giden TL karşılığının
  hangi kurla hesaplanacağı bu kararlarla seçilmedi (plan N-25).

## 2. Notlar (geri alınabilir)

- **ALS-D17 yorumu:** Göymen ERP'de ayrı bir "Rezerve" durumu açılmaz. Hubvogal'in iki onay durumu, Göymen'in mevcut
  eksenleriyle karşılanır: talep (Gönderildi / Onaylı, ALS-D2), sipariş onayı (ALS-D3) ve tedarikçi teyidi (ALS-D13).
  Göçte (MIG-01) hubvogal "Rezerve Sipariş" kayıtlarının hangi Göymen durumuna taşınacağı kayıt örnekleriyle ayrıca
  doğrulanır; tahminle eşlenmez.
- **ALS-D18 yorumu:** fiyatsız onaylı sipariş gelecek arza girer (miktar bellidir). Toplam ve KDV "hesaplanamıyor"
  gösterilir (Satış'taki vergi masterı boşken davranışla aynı). Fiyatsız satırın kabulü serbesttir; faturaya hazır
  listesine fiyat girilene kadar düşmez.
- **ALS-D19 yorumu:** Ayarlar'da bugün yalnız kg birim profili onaylıdır (Ö2). Adet ve metre profilleri Ayarlar'da
  onaylanana kadar alış satırı kg dışı birimle açılamaz (fail-closed, `purchasing_unit_unsupported`). Metre için
  ondalık hassasiyeti ve kg ↔ adet ↔ metre çevriminin ürün masterından mı geleceği ayrı karardır (plan N-28). Kabul
  toleransı (No 40) kg için tanımlıdır; adet/metre için tolerans ayrıca sorulur.
- **ALS-D20 yorumu:** "Tedarik No" sipariş başlığında tedarikçinin sipariş numarası alanıdır (ALS-D13 teyidiyle
  birlikte girilir). "Sipariş pozisyonu" sipariş başlığında **işlem türü** listesidir (ör. ticari ürün, hizmet, iade,
  teşvikli, ihraç kayıtlı). Liste Ayarlar'da tutulur; vergi etkisi (ihraç kayıtlı ve teşvikli alımlarda KDV
  istisnası/tecili) mali müşavir teyidiyle vergi masterına bağlanır (plan N-20); teyide kadar bu türler vergi
  hesaplamaz ("hesaplanamıyor").

- ALS-D15'te "kuruş yuvarlaması" sınırı, ALS-D8'deki fiyat farkı toleransıyla aynı Ayarlar kaydında tutulur
  (başlangıç 0); ayrı bir sabit yazılmaz.
- ALS-D6 ve ALS-D14 yalnız ERP'nin **kontrol** kurunu tanımlar; resmî faturanın kuru tedarikçinin belgesindeki
  değerdir ve ERP onu değiştirmez (DEC-FIN-003).
