# Sinan Kararları — 2026-11-02 (Satış: belge tarihinden önceki sevklerde istisna onayı)

Bu belge önceki karar dosyalarını silmez; SAT-D186'yı ekler. EK27'nin "Açık kalanlar" bölümündeki ve Issue #66'daki
müşavir sorusunu (belge tarihinden önce yapılmış sevklerin faturasında istisna) Sinan'ın kararıyla kapatır. SAT-D140,
SAT-D146, SAT-D147, SAT-D148 ve SAT-D185 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevabıyla):**
- **EK27 "Açık kalanlar":** EK27'de bu soru mali müşavire bırakılmıştı ve cevap gelene kadar fatura bekliyordu. SAT-D186'ya
  göre karar **muhasebe müdürünün onayıdır**: onay verilirse fatura istisnayla kesilebilir; onaya kadar bugünkü bekleme
  aynen sürer.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) yazdığı
mesaj. Koordinatörün durum özetindeki müşavir sorusuna (aşağıda) serbest metinle cevap verdi; seçmeli soru sorulmadı.
Mesaj, sorunun tekrarıyla birlikte aynen aktarılmıştır.

## Soru ve cevap

| # | Soru (koordinatörün özetinden) | Cevap (Sinan'ın mesajı, aynen) |
|---|---|---|
| 1 | Müşavire iletmen gereken bir konu var: ihraç kayıtlı ya da istisna belgeli alıcının belgesi sonradan gelirse, belge tarihinden önceki sevklerin faturasında KDV %0 istisnası uygulanabilir mi? | "Şu an sana sorum yok. Müşavire iletmen gereken bir konu var: ihraç kayıtlı ya da istisna belgeli alıcının belgesi sonradan gelirse, belge tarihinden önceki sevklerin faturasında KDV %0 istisnası uygulanabilir mi? Muhasebe müdürü onaylasın, onaylayana kadar sipariş alınabilsin ama sevk çıksın fatura kesilemesin" |

## Karar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D186 | Belge tarihinden önceki sevklerde istisna | İhraç kayıtlı ya da istisna belgeli alıcının belgesi sonradan geldiğinde, belge tarihinden **önce** yapılmış sevklerin faturasında KDV %0 istisnasının uygulanmasını **muhasebe müdürü onaylar**. Onaylanana kadar **sipariş alınabilir ve sevk çıkabilir**, ama o sevklerin **faturası kesilemez** (Sinan'ın mesajı). Uygulama ayrıntısı (geri alınabilir): ayrı bir "muhasebe müdürü" personası yokken onayı SAT-D146'daki ara okumayla **müdür sınıfı** (Müdür ve Admin) verir; Sinan'ın SAT-D146 metninde muhasebe müdürü bu sınıfın içindedir. Onay sipariş bazında verilir, gerekçe ve audit ile kaydedilir, hazırlayan ≠ onaylayan korunur; onay yalnız o siparişin belge tarihinden önceki sevklerini kapsar. Onay verilmezse o sevkler istisnayla faturalanmaz; satır vergisi SAT-D181'e göre gerekçeyle değiştirilene kadar fatura kesilmez. Belge tarihinden sonraki sevkler SAT-D147/D185 gereği onay beklemeden faturalanabilir | Fatura kapısı (API-32), sipariş başlığı, onay listesi (MOD-10), persona kartı |

## Sınırlar

- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz; onay izninin
  kime verileceği persona kartında ve canlıya geçişte insan kapısındadır.
