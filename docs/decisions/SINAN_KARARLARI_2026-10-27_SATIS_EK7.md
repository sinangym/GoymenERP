# Sinan Kararları — 2026-10-27 (Satış: müşteri risk kontrolü)

Bu belge önceki Satış karar dosyalarını (`SINAN_KARARLARI_2026-10-25_SATIS.md` … `SINAN_KARARLARI_2026-10-27_SATIS_EK6.md`)
silmez; onlara SAT-D46…SAT-D52'yi ekler. Kararlar DEC-FIN-007 (brüt risk) ve DEC-FIN-009'un (siparişte uyarı/onay,
sevkiyatta kesin kontrol) Satış tarafındaki uygulamasıdır; onları değiştirmez.

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli
soru cevapları (iki tur). Sorular MOD-01 + API-06 (PR [#839](https://github.com/sinangym/GoymenERP/pull/839)) uygulamasında
çıkan NEEDS-SINAN maddeleriydi. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi. İki soruda Sinan kendi metnini
yazdı; o metin aynen verilir.

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| Risk hesabında onaylanmış ama henüz sevk edilmemiş sipariş tutarı KDV dahil mi sayılsın? (Açık alacaklar faturadan geldiği için KDV dahil.) | KDV dahil | "Sipariş kalanı da KDV dahil tutarla alınır; açık alacakla aynı bazda toplanır (KDV hesabı gelince çalışır)." |
| Risk kalemleri farklı para birimindeyse (örn. TL alacak + USD sipariş) hangi kurla çevrilsin? | Bugünün TCMB döviz satış | "Kontrolün yapıldığı günün TCMB döviz satış kuru." |
| Sipariş aşamasında limit kontrolü neye bakılsın? | Mevcut risk + bu sipariş | "Müşterinin mevcut riskine bu siparişin tutarı da eklenir; toplam limiti aşarsa uyarı/istisna gerekir." |
| Risk tam limite eşitse sonuç ne olsun? | Limit içinde | "Limite eşit risk kabul edilir; yalnız limiti geçen 'aşım' sayılır (şu anki durum)." |
| İç onayı alınmış ama müşteri kabulü henüz gelmemiş siparişler riske dahil edilsin mi? | Evet, dahil edilsin | "İç onaylı her sipariş riske girer; müşteri kabulü beklenmez (şu anki ihtiyatlı durum)." |
| Müşterinin kredi limiti ve teminatları hangi modülde tutulsun? | Kendi metni: "Hangisin olması mantıklıysa" | — (seçenekler: "Cari kartında", "Finans modülünde") |
| Müşterinin henüz bir faturaya eşleştirilmemiş ödemeleri açık alacaktan düşülsün mü? | Kendi metni: "Bence düşülmesi doğru, eşleştirme daha sonra vade takibi için lazım aslında" | — (seçenekler: "Düşülmesin", "Düşülsün") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D46 | Risk vergi bazı | Brüt riskte onaylı sevk edilmemiş sipariş kalanı **KDV dahil** tutarla alınır ve KDV dahil açık alacakla aynı bazda toplanır. KDV dahil sipariş tutarı Satış'ın toplam hesabından (API-03) gelir; o hesap yokken bu bileşen "hesaplanamıyor" kalır, KDV hariç tutar yerine konmaz | API-06 (#839), API-03 |
| SAT-D47 | Risk kur çevrimi | Farklı para birimli risk kalemleri, kontrolün yapıldığı günün **TCMB döviz satış** kuruyla siparişin para birimine çevrilir. Kur kaynağı Finans/Ayarlar'daki kur kaydıdır; o günün kuru yoksa çevrim yapılmaz ve sonuç "hesaplanamıyor" olur (fail-closed) | API-06 (#839), API-07 |
| SAT-D48 | Siparişteki limit kontrolü | Sipariş aşamasındaki kontrol **müşterinin mevcut riski + bu siparişin tutarı** ile limiti karşılaştırır; toplam limiti aşarsa DEC-FIN-009'daki uyarı/istisna yolu gerekir | API-06 (#839), API-07 |
| SAT-D49 | Limite eşitlik | Risk limite **eşitse limit içindedir**; yalnız limiti geçen tutar aşımdır | API-06 (#839) |
| SAT-D50 | Müşteri kabulü bekleyen sipariş | İç onayı alınmış her sipariş, müşteri kabulü beklenmeden onaylı sipariş kalanı olarak riske girer | API-06 (#839) |
| SAT-D51 | Kredi limiti ve teminatın sahibi | Sinan seçimi koordinatöre bıraktı. **Finans modülü** seçildi: risk (DEC-FIN-007), DBS (DEC-FIN-008) ve teminat olarak çek/senet (DEC-FIN-016) zaten Finans kararlarıdır; Cari kartında limit ve teminat yalnız **görüntülenir**. Geri alınabilir bir sahiplik seçimidir; limit/teminat değerleri ve teminat politikası (OPEN-010 haircut dahil) bu kararla seçilmez | MOD-01 (#839), W4-RISK, Cari kartı |
| SAT-D52 | Eşleştirilmemiş müşteri ödemeleri | Müşterinin henüz bir faturaya eşleştirilmemiş ödemeleri **açık alacaktan düşülür**. Eşleştirme ayrıca yapılmaya devam eder, çünkü vade takibi için gereklidir; düşüm eşleştirmeyi gereksiz kılmaz. DEC-FIN-008 gereği banka ödemesi gelmemiş DBS/çek tahsilat sayılmaz | MOD-01 (#839) |

## Açık kalanlar

- Kredi limiti ve teminat değerleri, teminat politikası ve OPEN-010 (haircut) açık kalır; bu belge onları seçmez.
- Sevkiyattaki kesin kontrolde (DEC-FIN-009) "bu sevkiyatın tutarı"nın nasıl ekleneceği sorulmadı.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol veya grant açılmaz; OPEN-010 kapanmaz.
