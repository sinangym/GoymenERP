# Sinan Kararları — 2026-11-02 Alış EK7 (tedarikçi performansı, bülten kuru, avans/gümrük, birim çevrimi)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda ("Proje modülleri alış analizi"), Alış planında
açık kalan sorulara (N-18, N-36, N-27 kalan, N-28 kalan) verdiği **doğrudan cevaplar**. Sorular ve cevaplar aynen
aktarılmıştır; serbest metinler yazım düzeltmesi yapılmadan alınmıştır. Claude yalnız soruları ve seçenekleri
hazırlamıştır.

Numaralama: ALS-D47 paralel PR #907'deki EK6'dadır (talebin geri çekilmesi: oluşturan veya gönderen). Bu dosya
ALS-D48'den başlar; EK6 bu dosyanın parçası değildir.

## 0. Yetki ve sınır

`SINAN_KARARLARI_2026-11-02_ALIS.md` §0 (ALS-D45 ile daraltılmış hâliyle) geçerlidir. Bu kararlar rol, grant, formül
golden veya vergi kuralı tanımlamaz; para/kur/miktar hesapları ortak backend calculator'da kalır. Birim çevrim
katsayıları Ürün modülünün verisidir (DEC-PRD-006); Alış Ürün dosyalarını değiştirmez, görev notu bırakır
(`docs/handoffs/ALIS_URUN_DEVIR_v1.md`). İnsan kapıları (grant/rol, production aktivasyonu, deployment, secret,
`OPEN-*` kapatma) değişmez.

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D48 | Tedarikçi performans raporu ölçütleri | Soru aynen: **"Tedarikçi performans raporunda (A6) hangi ölçütler olsun?"** (çoklu seçim; seçenekler: Zamanında teslim / Miktar sapması / Kalite reddi / Fiyat sapması). Seçilen aynen: **"Zamanında teslim, Miktar sapması, Fiyat sapması"**. **Kalite reddi seçilmedi** | Plan API-91 dört ölçütü öneriyordu; N-18 açıktı | N-18, plan API-91, UI-13, A6 |
| ALS-D49 | Dövizli siparişte hubvogal "bülten kuru" | Soru aynen: **"Dövizli siparişte hubvogal'deki 'bülten kuru' ne işe yarasın?"** Cevap aynen: **"Dövizli siparişte sevk tarihinin kurunu baz alacağız yada elle değiştirebileceğiz, hubvogaldeli bülten kuru ifadesine gerek yok"**. Netleştirme sorusu aynen: **"Dövizli alışta 'sevk tarihinin kuru' için netleştirme: daha önce (ALS-D6) alış faturasının kuru 'mal kabul (irsaliye) gününden önceki iş günü TCMB kuru' demiştik. Yeni cevabınız bunu nasıl etkiliyor?"** Seçilen: **"Aynı şey (Önerilen)"**; seçenek açıklaması aynen: "Sevk tarihi = irsaliye/kabul tarihi. Fatura kuru o günün (bir önceki iş günü) TCMB kuru; yetkili gerekçeyle elle değiştirebilir. ALS-D6 aynen kalır." | EK2 §2: bülten kuru fiyat referansı sayılmıştı; kontrol kuru olarak kullanımı N-36 ile açıktı | N-36, ALS-D6, ALS-D14, ALS-D15, EK2 §2, MIG-01 |
| ALS-D50 | Tedarikçiye avans ve gümrük/ithalat masrafları | Soru aynen: **"Tedarikçiye avans ve gümrük/ithalat masrafları Alış'ta nasıl olsun?"** Seçilen: **"İkisi de sonra (Önerilen)"**; seçenek açıklaması aynen: "Avans Finans konuşmasında ödeme olarak yapılır; gümrük/ithalat masrafları şimdilik ALS-D23 masraf satırı ile siparişe/kabule eklenir. Ayrı ithalat dosyası yok." | ALS-D23 masraf satırını kapatmış, avans ve gümrük/ithalat açık kalmıştı (N-27 kalan) | N-27, ALS-D23, ALS-D27, API-23, ALS-D44 (Finans konuşması) |
| ALS-D51 | kg ↔ adet ↔ metre çevrimi ve fiyatın birimi | Soru aynen: **"kg ↔ adet ↔ metre çevrimi nereden gelsin?"** Cevap aynen: **"Ürün kartından gelir, ürün özelliğine göre adet girilirse metre ve kg hesaplanır yada kg girilirse adet ve metre hesaplanır ama birim fiyatın hangi miktar üzerinden çarpılacağı ürün kartından varsayılan gelir ama sipariş sırasında değiştirilebilir, siparişe görede sevkiyatı gerçekleştirilen irsaliye faturalanır veya faturası kontrol edilir"** | ALS-D19 satır birimini ürüne bağlamıştı; ALS-D43 ondalık/toleransı kapatmıştı; çevrimin kaynağı açıktı (N-28 kalan) | N-28, ALS-D19, ALS-D43, DEC-PRD-006, plan A2a (API-20), A3 |
| ALS-D52 | Faturanın hangi miktar üzerinden kesildiği / kontrol edildiği | ALS-D51 cevabının son cümlesi aynen: **"siparişe görede sevkiyatı gerçekleştirilen irsaliye faturalanır veya faturası kontrol edilir"** (ayrı satır olarak kaydedildi; ayrı soru sorulmadı) | ALS-D7 (Mikro'dan içe aktar + eşleştir), ALS-D32 (fazla teslim aynı fiyatla) | ALS-D7, ALS-D8, ALS-D32, API-51/52 |

## 2. Yorumlar (geri alınabilir; karar değildir)

- **ALS-D48:** API-91 üç ölçütle yazılır: zamanında teslim (termin ↔ kabul tarihi), miktar sapması (sipariş ↔ kabul
  miktarı, ALS-D43 tolerans profiline göre) ve fiyat sapması (sipariş fiyatı ↔ gelen fatura fiyatı, API-52 üçlü
  kontrolündeki fiyat farkı). Kalite reddi raporda **yoktur** (Sinan seçmedi). Ölçütlerin ağırlıkları/puanlaması seçilmedi; rapor ham ölçütleri gösterir.
- **ALS-D49:** Göymen ERP'de **bülten kuru kavramı yoktur**: sipariş, kabul veya faturada bülten kuru alanı açılmaz;
  MIG-01 göçünde de hubvogal bülten kuru alanı taşınmaz (kontrol kuru olarak da taşınmaz). Dövizli alışta kur ALS-D6
  (kabul gününden önceki iş günü TCMB kuru; yetkili gerekçeyle düzeltir) ve ALS-D14/D15 (çok günlü kabul, kur farkı
  onayı) ile aynen kalır. EK2 §2'deki "bülten kuru ve tarihi fiyat referansıdır" yorumu bülten **kuru** için bu
  kararla geçersizdir (EK2 dosyası değiştirilmez; daha yeni karar geçerlidir). Bülten **no/tarihi** ve "Bülten Hazır"
  miktarı bu soruya konu değildi; ALS-D22/D25 aynen kalır.
- **ALS-D50:** Avans Alış'ta kayıt türü değildir; Finans konuşmasında ödeme olarak yapılır (ALS-D44). Gümrük/ithalat
  masrafları API-23 masraf satırının masraf tipleridir (API-98 listesi; ALS-D27 ile başka cari, ör. gümrük müşaviri,
  seçilebilir). Ayrı ithalat dosyası/maliyet dağıtımı ilk sürümde yoktur.
- **ALS-D51:** Satırda kullanıcı kg, adet veya metreden **birini** girer; diğer ikisi ürün kartındaki çevrimle
  ortak calculator'da hesaplanır (ekranda yeniden yazılmaz). Ürün kartı ayrıca **fiyat birimini** (birim fiyatın
  çarpıldığı miktar) varsayılan olarak verir; sipariş satırında değiştirilebilir ve değişiklik satırda saklanır.
  Satırın tutarı fiyat birimindeki miktar × birim fiyattır. Bu, ALS-D19'un "satır birimi ürüne göre" kuralını
  "giriş birimi + fiyat birimi" olarak ayrıntılandırır; A2a fiyat modeli baştan fiyat birimini satırda taşıyacak
  biçimde kurulur. Ürün kartında çevrimi olmayan üründe yalnız girilen birim tutulur, diğerleri "hesaplanamıyor"
  kalır (fail-closed; tahmini katsayı yok). Fiyat birimini kimlerin değiştirebileceği ve değişikliğin onay
  gerektirip gerektirmediği seçilmedi; ilk sürümde sipariş düzenleme yetkisi ve sipariş onayı (ALS-D3) yeter.
- **ALS-D52:** Fatura, sipariş satırına bağlı **gerçekleşen sevkiyatın irsaliyesindeki** (kabul) miktar üzerinden
  kesilir veya kontrol edilir; sipariş miktarı üzerinden değil. API-52 üçlü kontrol bu miktarı satırın fiyat
  birimiyle karşılaştırır; fazla teslim ALS-D32 ile aynı fiyattan. Bu, ALS-D7/D32'yi tekrarlar ve birim boyutunu
  ekler; yeni bir onay veya tolerans kuralı getirmez.
- **Kabul toleransının hangi birimde ölçüleceği** (giriş birimi mi, fiyat birimi mi, kantar kg'ı mı) bu cevaplarla
  seçilmedi; ALS-D43 her birimde aynı yüzdeyi verir. A3'ten önce sorulur (plan N-38).

## 3. Ürün şeridine görev notu

Çevrim katsayıları ve varsayılan fiyat birimi Ürün kartının verisidir (DEC-PRD-006: sabit, formül bağlı ve fiilî
dönüşüm sınıfları). Alış Ürün dosyalarını değiştirmez; istek `docs/handoffs/ALIS_URUN_DEVIR_v1.md` notundadır.
Ürün portu gelene kadar Alış satırı yalnız girilen birimle çalışır (ALS-D19/D43 fail-closed davranışı).

## 4. Değişmeyenler

- ALS-D6, ALS-D14, ALS-D15, ALS-D41 (kur kuralları) aynen.
- ALS-D23, ALS-D27, ALS-D29 (masraf satırı; fason masraf değildir) aynen.
- ALS-D19 (satır birimi ürüne göre), ALS-D43 (metre 2 ondalık, adet tam sayı, tolerans kg ile aynı) aynen;
  ALS-D51 yalnız çevrimin kaynağını ve fiyat birimini ekler.
- ALS-D22/D25 (U.S/Bülten Hazır gelecek maldır) aynen.
- N-20 (mali müşavir teyidi) ve N-19 (hubvogal erişimi, bağımlılık) açık kalır.
