# Sinan Kararları — 2026-11-14 (tren-1 dalga 1: ortalama maliyet, sevk planı, irsaliyesiz sevk, toplu kabul kg farkı)

Bu belge önceki karar dosyalarını silmez; SAT-D347, SAT-D348, SAT-D349 ve KLN-D45 ekler. SAT-D272, SAT-D311,
SAT-D313 ve SAT-D262/D265 geçerliliğini korur; bu kararlar onların açık bıraktığı uygulama noktalarını kapatır.
KLN-D45, KLN-D43'ün (toplu mal kabul, `SINAN_KARARLARI_2026-11-14_IZLENEBILIRLIK.md`, PR #1158) açık bıraktığı
kg toleransı davranışını kapatır.

**Kaynak:** Sinan, 2026-11-14, oturum içinde önerili seçeneklerle soruldu; Issue #66 yorumu
[6885327056](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6885327056) (SINAN-CEVAP 16).
Dört cevabın dördü de önerilen seçenektir. "(Önerilen)" işaretleri soranındır. Sorular, tren-1 dalga 1'de
Satış takımının `NEEDS-SINAN` satırlarından ve KLN-D43'ün açık noktasından doğdu.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 16.1 | Satış marjı için şirket geneli ortalama alış maliyeti (SAT-D272): bir depo okunamazsa ya da maliyet kaynağı yoksa ne olsun? | **Okunamaz → marj yok (Önerilen)** | Tek bir depo okunamazsa marj gösterilmez ve "maliyet okunamadı" yazar. Ağırlık eldeki kg'dır. Maliyet kaynağı olmayan depo (stok 0) ağırlığa girmez. Para tarafında güvenli: yanlış marj hiç gösterilmez. — Seçilmeyenler: *Okunanların ortalaması* (okunabilen depoların kg ağırlıklı ortalaması gösterilir ve yanında "X depo dahil edilemedi" uyarısı çıkar; marj hep görünür ama eksik veriyle hesaplanmış olabilir); *Son alış fiyatı* (ortalama yerine şirketteki son alış birim fiyatı kullanılır; basit ama SAT-D272'deki ortalama kararından sapar). |
| 16.2 | Siparişin risk tutarı sevk planına göre hesaplanacaktı (SAT-D313), ama Lojistik'te henüz sevk planı miktarı yok. Ne yapalım? | **Şimdilik onaylı − sevk (Önerilen)** | Kalan tutar = onaylı miktar − kesin sevk. Lojistik plan miktarı gelince plan esas alınır. Plan üstü sevk uyarı verir ve onaya düşer. Fiyat tabanı siparişin geçerli revizyonudur. — Seçilmeyenler: *Plan miktarı hemen eklensin* (Lojistik'e yeni bir "sevk planı miktarı" alanı ve okuma noktası açılır, risk hesabı ona bağlanır; daha fazla iş, Lojistik dilimine bağımlı); *Plan üstü sevk serbest* (kalan tutar onaylı − sevk olarak kalır; plan üstü sevk onay istemeden geçer). |
| 16.3 | Kredi riskindeki "faturalanmamış sevk" kalemine, onaylı irsaliyesi olmayan kesin sevk de sayılsın mı? | **Evet, sayılsın (Önerilen)** | Mal fiziksel olarak çıktıysa (kesin sevk) irsaliye onayı beklenmeden riske eklenir. Risk tarafında ihtiyatlı; kodun bugünkü davranışı da bu. — Seçilmeyen: *Hayır, irsaliye onayından sonra* (yalnız onaylı irsaliyesi olan sevk riske girer; irsaliye onayı gecikirse risk eksik görünür). |
| 16.4 | Toplu mal kabulde etiketlerin toplam kg'ı sipariş kg'ından farklı çıkarsa ne olsun (KLN-D43)? | **Uyar; tolerans dışı onaya (Önerilen)** | Tolerans yüzdesi Ayarlar'dan girilir. İçindeki fark uyarıyla kabul edilir, dışındaki fark onaylayana düşer. Tolerans girilmemişse her fark onaya düşer. — Seçilmeyenler: *Yalnız uyar, hep kabul* (her fark uyarı gösterir ama kabul durmaz; hızlı ama fazla veya eksik teslim gözden kaçabilir); *Tolerans dışında dur* (tolerans dışı fark kabulü engeller; sipariş revize edilmeden kabul yapılamaz). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D347 | Şirket geneli ortalama maliyette eksik depo | Şirket geneli ağırlıklı hareketli ortalama alış maliyeti (SAT-D272) tek bir depo okunamazsa **okunamaz** döner; Satış marjı gösterilmez ve "maliyet okunamadı" yazar. Ağırlık depodaki eldeki kg'dır. Maliyet kaynağı olmayan (stoğu 0) depo ağırlığa girmez. Toplama kuralı Stok'un kendi sözleşmesindedir (SAT-D262); Satış depo okumalarını kendisi birleştirmez. Uygulama ayrıntısı (geri alınabilir): hiçbir depoda kaynak yoksa sonuç "kaynak yok" olarak döner. | Stok şirket geneli ortalama maliyet okuma portu (yeni), Satış marj önizlemesi (UI-50 S-05) |
| SAT-D348 | Sevk planı miktarı yokken risk kalanı | Lojistik sevk planı miktarı bulunana kadar siparişin risk kalanı = onaylı miktar − kesin sevk; plan miktarı geldiğinde plan esas alınır. Plan üstü sevk uyarı verir ve onaya düşer. Kalanın fiyat tabanı siparişin geçerli revizyonudur. | Satış risk kontrolü (S0), Lojistik sevk planı (sonraki dilim) |
| SAT-D349 | İrsaliyesiz kesin sevk | Onaylı irsaliyesi olmayan kesin sevk, irsaliye onayı beklenmeden "faturalanmamış sevk" risk kalemine sayılır. | Satış risk kontrolü (S0 c) |
| KLN-D45 | Toplu mal kabulde kg farkı | Etiketlerin toplam kg'ı sipariş kg'ından farklıysa: tolerans yüzdesi Ayarlar'dan girilir; tolerans içindeki fark uyarıyla kabul edilir, dışındaki fark onaylayana düşer; tolerans girilmemişse her fark onaya düşer. Uygulama ayrıntısı (geri alınabilir): onay mevcut hazırlayan ≠ onaylayan kuralıyla yapılır; yeni izin adı kayıt edilebilir, grant/rol atanmaz. | Alış toplu mal kabul (KLN-D43) |

## Sınırlar

- Tolerans **değeri** (yüzde) seçilmedi; Ayarlar'da girilir. Girilmemişse her fark onaya düşer.
- Marj formülünün etkinleştirilmesi SAT-D265 (5) gereği golden'a bağlıdır; bu belge onu değiştirmez.
- Lojistik sevk planı miktarının kendisi (alan, okuma noktası) bu belgeyle açılmaz; ayrı dilimdir.
- `OPEN-*` kapatılmaz; `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` değişmez.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` her zaman somut kullanıcı kararı ister.
