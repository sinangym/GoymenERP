# Sinan Kararları — 2026-10-27 (Satış: müşteri risk kontrolü)

Bu belge önceki Satış karar dosyalarını (`SINAN_KARARLARI_2026-10-25_SATIS.md` … `SINAN_KARARLARI_2026-10-27_SATIS_EK6.md`)
silmez; onlara SAT-D46…SAT-D53'ü ekler. Kararlar DEC-FIN-007 (brüt risk), DEC-FIN-009 (siparişte uyarı/onay, sevkiyatta
kesin kontrol) ve DEC-FIN-010'un (kur) müşteri risk kontrolündeki uygulamasıdır; onları değiştirmez. SAT-D51 ve SAT-D52
Finans modülünü de etkiler.

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli
soru cevapları (üç tur; üçüncü tur PR #840 review'larının kur sorusu üzerine). Sorular MOD-01 + API-06 (PR [#839](https://github.com/sinangym/GoymenERP/pull/839)) uygulamasında
çıkan NEEDS-SINAN maddeleriydi. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi. İki soruda Sinan kendi metnini
yazdı; o metin aynen verilir.

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| Risk hesabında onaylanmış ama henüz sevk edilmemiş sipariş tutarı KDV dahil mi sayılsın? (Açık alacaklar faturadan geldiği için KDV dahil.) | KDV dahil | "Sipariş kalanı da KDV dahil tutarla alınır; açık alacakla aynı bazda toplanır (KDV hesabı gelince çalışır)." |
| Risk kalemleri farklı para birimindeyse (örn. TL alacak + USD sipariş) hangi kurla çevrilsin? | Bugünün TCMB döviz satış | "Kontrolün yapıldığı günün TCMB döviz satış kuru." (seçilmeyenler: "Bugünün TCMB döviz alış", "Kalemin kendi tarihi", "Farklı para birimi reddedilsin") |
| Kur kuralında daha önce kilitlediğin karar (DEC-FIN-010): 'bir önceki iş gününün TCMB döviz satış kuru'. Risk kontrolünde de bu mu kullanılsın? | Evet, önceki iş günü | "Genel kur kuralı (DEC-FIN-010) geçerli: kontrol gününde geçerli olan, bir önceki iş gününün TCMB döviz satış kuru. Sabah ve tatilde de kur vardır." (seçilmeyen: "Hayır, o gün açıklanan") |
| Risk toplamı hangi para biriminde hesaplansın? | Kendi metni: "Bence her ikiside ayrı toplanmalı, burda finansal ve muhasebesek açıdan hangisi mantıklı" | — (seçenekler: "Şirketin yerel para birimi (TL)", "Limitin para birimi", "Siparişin para birimi") |
| Sipariş aşamasında limit kontrolü neye bakılsın? | Mevcut risk + bu sipariş | "Müşterinin mevcut riskine bu siparişin tutarı da eklenir; toplam limiti aşarsa uyarı/istisna gerekir." |
| Risk tam limite eşitse sonuç ne olsun? | Limit içinde | "Limite eşit risk kabul edilir; yalnız limiti geçen 'aşım' sayılır (şu anki durum)." |
| İç onayı alınmış ama müşteri kabulü henüz gelmemiş siparişler riske dahil edilsin mi? | Evet, dahil edilsin | "İç onaylı her sipariş riske girer; müşteri kabulü beklenmez (şu anki ihtiyatlı durum)." |
| Müşterinin kredi limiti ve teminatları hangi modülde tutulsun? | Kendi metni: "Hangisin olması mantıklıysa" | — (seçenekler: "Cari kartında", "Finans modülünde") |
| Müşterinin henüz bir faturaya eşleştirilmemiş ödemeleri açık alacaktan düşülsün mü? | Kendi metni: "Bence düşülmesi doğru, eşleştirme daha sonra vade takibi için lazım aslında" | — (seçenekler: "Düşülmesin", "Düşülsün") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D46 | Risk vergi bazı | Brüt riskte onaylı sevk edilmemiş sipariş kalanı **KDV dahil** tutarla alınır ve KDV dahil açık alacakla aynı bazda toplanır. KDV dahil sipariş tutarı Satış'ın toplam hesabından (API-03) gelir; o hesap yokken bu bileşen "hesaplanamıyor" kalır, KDV hariç tutar yerine konmaz | API-06 (#839), API-03 |
| SAT-D47 | Risk kur çevrimi | Farklı para birimli risk kalemleri **DEC-FIN-010'daki kurla**, yani kontrol gününde geçerli olan **bir önceki iş gününün TCMB döviz satış kuruyla** çevrilir (Sinan'ın üçüncü tur cevabı; ilk turdaki "bugünün" ifadesi bu anlamdadır). Kesin saat ve tatil takvimi OPEN-012'de açık kalır. Kur kaydı henüz yoktur; kur bulunamazsa çevrim yapılmaz ve sonuç "hesaplanamıyor" olur (fail-closed) | API-06 (#839), API-07 |
| SAT-D53 | Risk toplamının para birimi | Sinan "her ikisi ayrı toplanmalı" dedi ve hangisinin finansal/muhasebesel açıdan mantıklı olduğunu koordinatöre (Satış şeridi Claude oturumu) sordu. Koordinatör seçimi (geri alınabilir, `DELEGE` düzeyi, kilitli değil): risk **iki toplamla** gösterilir — (1) **şirketin yerel para birimi (TL)** toplamı: muhasebe ve raporlama yasal defter para biriminde tutulduğu için; (2) **kredi limitinin para birimi** toplamı: limit karşılaştırması limitin kendi para biriminde yapılır, böylece limit kur farkıyla kaymaz. Limit/aşım sonucu (2)'ye göre verilir; limit TL ise iki toplam aynıdır. Çevrim SAT-D47 kuruyla yapılır. Sinan tek satırla değiştirebilir | API-06 (#839), API-07, UI-15 risk kartı |
| SAT-D48 | Siparişteki limit kontrolü | Sipariş aşamasındaki kontrol **müşterinin mevcut riski + bu siparişin tutarı** ile limiti karşılaştırır; toplam limiti aşarsa DEC-FIN-009'daki uyarı/istisna yolu gerekir. Bu siparişin tutarı SAT-D46'ya göre KDV dahildir. DEC-FIN-007'deki çift sayım yasağı gereği, zaten onaylı olup revize edilen siparişte mevcut riskteki eski kalanı yerine yeni tutar konur (yalnız fark eklenir) | API-06 (#839), API-07 |
| SAT-D49 | Limite eşitlik | Risk limite **eşitse limit içindedir**; yalnız limiti geçen tutar aşımdır | API-06 (#839) |
| SAT-D50 | Müşteri kabulü bekleyen sipariş | İç onayı alınmış her sipariş, müşteri kabulü beklenmeden onaylı sipariş kalanı olarak riske girer | API-06 (#839) |
| SAT-D51 | Kredi limiti ve teminatın sahibi | Sinan seçimi koordinatöre (Satış şeridi Claude oturumu) bıraktı ("Hangisin olması mantıklıysa"). Koordinatör seçimi (geri alınabilir, `DELEGE` düzeyi, kilitli değil): **Finans modülü** kaydı yazar ve sahiplenir; Cari kartı (Counterparty) limit ve teminatı **yalnız okur ve gösterir**. Gerekçe: risk (DEC-FIN-007), DBS (DEC-FIN-008) ve teminat olarak çek/senet (DEC-FIN-016) Finans kararlarıdır; master rapor §7.11/§8 Finans & Hazine satırları "kredi, limit, teminat"ı Finans'a koyar. Master raporun nesne tablosundaki Cari satırı (limit, teminat) ve Cari 360 "Risk/Limit/Teminat" sekmesi bu seçimle **görüntüleme** olarak okunur. Modüller arası sözleşme sonucudur (K3). Limit/teminat değerleri ve teminat politikası (OPEN-010 haircut dahil) bu kararla seçilmez; Sinan tek satırla değiştirebilir | MOD-01 (#839), W4-RISK, Cari kartı |
| SAT-D52 | Eşleştirilmemiş müşteri ödemeleri | Müşterinin henüz bir faturaya eşleştirilmemiş ödemeleri **açık alacaktan düşülür**. Eşleştirme ayrıca yapılmaya devam eder, çünkü vade takibi için gereklidir; düşüm eşleştirmeyi gereksiz kılmaz. DEC-FIN-008 (DBS) ve DEC-FIN-016 (çek/senet) gereği banka ödemesi gelmemiş DBS veya tahsil edilmemiş çek ödeme sayılmaz | MOD-01 (#839) |

## Açık kalanlar

- Kredi limiti ve teminat değerleri, teminat politikası ve OPEN-010 (haircut) açık kalır; bu belge onları seçmez.
- Sevkiyattaki kesin kontrolde (DEC-FIN-009) "bu sevkiyatın tutarı"nın nasıl ekleneceği sorulmadı.
- **OPEN-012** (kurun kesin saati ve tatil takvimi) açık kalır.
- **SAT-D52'nin sınırı:** eşleştirilmemiş ödemenin açık alacaktan büyük olması halinde fazlanın sevk/sipariş bileşenlerini de
  azaltıp azaltmayacağı ve farklı para birimli ödemenin nasıl düşüleceği sorulmadı. Karar verilene kadar düşüm, aynı para
  birimindeki açık alacakla sınırlıdır ve sıfırın altına inmez (fail-closed).

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol veya grant açılmaz; OPEN-010 ve OPEN-012 kapanmaz.
