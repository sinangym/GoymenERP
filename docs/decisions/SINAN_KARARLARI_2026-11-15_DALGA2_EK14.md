# Sinan Kararları — 2026-11-15 (Dalga 2 ek 14: termin tamponunun tek kaynağı, soy ağacı geçmişi ve pay gösterimi, idempotency temizlik işinin teknik değerleri)

Bu belge önceki karar dosyalarını silmez; URT-D92…URT-D94 ve KLN-D101 kararlarını ekler. URT-D62, URT-D89, URT-D91,
KLN-D37 ve izlenebilirlik kararları (`SINAN_KARARLARI_2026-11-14_IZLENEBILIRLIK.md`) aynen geçerlidir; bu belge onların
açık bıraktığı noktaları tamamlar. URT-D89 ve URT-D91 henüz merge edilmemiş karar PR'larındadır; bu belge onlara o
PR'lardaki metinle atıf yapar.

**Kaynak:** Sinan, 15 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 35). Sorular, ek 13'ün
Sınırlar bölümündeki termin tamponu maddesinden ve tren-1 dalga 3 raporlarındaki `NEEDS-SINAN` maddelerinden geldi
(Üretim URT-S20 soy ağacı; Platform PLT-S14 idempotency temizliği). "(Önerilen)" işaretleri soranındır. Dört sorunun
dördünde de önerilen seçenek seçildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 35.1 | Termin tamponu: URT-D89 tamponu iş merkezi başına (saat/gün, Üretim Müdürü) bağladı; SC34'te seçilen URT-D91 seçeneğinin açıklamasında ise "şirket başına gün, Admin" geçiyordu. Şirket geneli ayrı bir tampon da olsun mu? | **Yalnız iş merkezi (Önerilen)** | "Tek tampon URT-D89'daki iş merkezi tamponudur; URT-D91'in 'başlangıç 0, hazır değer yok' kuralı ona uygulanır. Ek tablo yok, çift sayım riski yok." Seçilmeyen: "Şirket geneli de olsun" — Admin şirket başına gün cinsinden ek tampon girer; iş merkezi tamponunun üstüne eklenir. Ek Ayarlar kaydı + migration. |
| 35.2 | Üretim soy ağacı: soy kenarları yeni migration'la yazılmaya başladı. Migration'dan önce beyan edilmiş çıktıların kenarı yok. Ne yapalım? | **Geriye doldurma yok (Önerilen)** | "Eski çıktılarda soy sekmesi 'Bu kayıt soy izi tutulmadan önce oluşturuldu' der. Canlı öncesi olduğumuz için kayıp veri yok; tahmini kenar üretilmez." Seçilmeyen: "Stok hareketlerinden doldur" — bir kerelik migration eski emirlerin girdi/çıktı hareketlerinden kenarları çıkarır. Çok girdili emirlerde kg payı tahmin olur; migration geri alma planı gerekir. |
| 35.3 | Soy ağacında kg payları (bir çıktının hangi girdiden yüzde kaç geldiği) ekranda nasıl gösterilsin? Şu an yuvarlamasız dönüyor. | **% 2 ondalık, kg 3 ondalık (Önerilen)** | "Ekranda yüzde 2 ondalık ve kg 3 ondalık gösterilir; toplam 100'e zorla tamamlanmaz. API ham değeri de döner, hesap hep ham değerle yapılır." Seçilmeyen: "Kalan son satıra yazılsın" — aynı yuvarlama, ama yüzdeler toplamı tam 100,00 olsun diye fark son girdiye eklenir. |
| 35.4 | İdempotency kayıtlarını temizleyen iş (PLT-S14) OPEN-021 kapalı olduğu için çalışmıyor. Teknik değerleri (parti boyu, aralık, zaman aşımı) şimdi yazalım mı? | **Varsayılan yaz, iş kapalı (Önerilen)** | "Parti 500, aralık 1 dk, komut zaman aşımı 30 sn; ayar dosyasından değişir. İş OPEN-021 kararı gelene kadar kapalı kalır, saklama süresi seçilmez." Seçilmeyen: "Hiç değer yazma" — değerler OPEN-021 kararıyla birlikte belirlenir; o zamana kadar kod değer eksik diye başlamaz. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URT-D92 | Termin tamponunun tek kaynağı (35.1; URT-D89/URT-D91'i netleştirir) | Termin hesabında tek güvenlik tamponu URT-D89'daki **iş merkezi başına** (saat/gün) tampondur; şirket geneli ayrı bir tampon açılmaz. URT-D91'in "başlangıç değeri 0, hazır değer (seed) yok, değişiklik denetime yazılır" kuralı bu iş merkezi tamponuna uygulanır. Persona dağılımı URT-D89'daki gibi Üretim Müdürü'dür. Ek 13 Sınırlar bölümündeki ilgili `NEEDS-SINAN` bu kararla kapanır. | URT-S10/URT-SC33/URT-SC34 termin önerisi, Ayarlar iş merkezi kaydı (KIM-SC33-b) |
| URT-D93 | Soy izi öncesi çıktılar (35.2) | Soy kenarı migration'ından önce beyan edilmiş üretim çıktıları için **geriye dönük kenar üretilmez** (backfill yok). Bu çıktılarda soy okuması boş kenar listesiyle birlikte "soy izi tutulmadan önce oluşturuldu" durumunu açıkça döner; ekran bunu Türkçe metinle gösterir. Tahmini kg payı üretilmez. Uygulama ayrıntısı (geri alınabilir): durumun alan/kod adı ve "migration öncesi" ayrımının nasıl tespit edildiği. | URT-S21 çıktı beyanı, URT-S22 API-28/UI-18, STK-S33 iz okuması |
| URT-D94 | Soy ağacı pay gösterimi (35.3) | Soy ağacında kg payı ekranda **yüzde 2 ondalık, kg 3 ondalık** gösterilir; yüzdelerin toplamı 100'e zorla tamamlanmaz (kalan son satıra eklenmez). API ham (yuvarlamasız) değeri de döner; türetilen her hesap ham değerle yapılır, ekranda yeniden hesap yapılmaz. Uygulama ayrıntısı (geri alınabilir): yuvarlama modu ekran biçimlendiricisinde mevcut sayı biçimi kuralıyla aynıdır. | URT-S22 API-28/UI-18, STK-S33 iz ekranı |
| KLN-D101 | İdempotency temizlik işinin teknik değerleri (35.4; KLN-D37'yi tamamlar) | PLT-S14 temizlik işinin teknik varsayılanları yazılır: parti boyu **500**, tarama aralığı **1 dakika**, komut zaman aşımı **30 saniye**; değerler ayar dosyasından değişir. İş **kapalı** kalır; OPEN-021 kapatılıp etkinleştirme kararı verilene kadar çalışmaz. Saklama süresi bu belgede seçilmez (KLN-D37'deki 90 gün varsayılanı değişmez); etkinleştirme insan kapısındadır. | Platform idempotency temizlik işi (PLT-S14) |

## Sınırlar

- URT-D92 yalnız tamponun nerede ve kaç tane olduğunu netleştirir; URT-D89'daki kapasite girişi, "kapasite yoksa
  hesaplanamadı" kuralı ve URT-D91'in başlangıç değeri değişmez.
- URT-D93 canlı öncesi duruma dayanır; canlıya alınmış veride kenarsız çıktı bulunursa geriye doldurma ayrı bir karar
  ister.
- KLN-D101 etkinleştirme kararı değildir; PLT-S14'teki "sistem işi en yüksek yetki şartını karşılar mı, legal hold veya
  ikinci onay gerekir mi" sorusu `NEEDS-SINAN` olarak kalır.
- Platform AI/E8/OPEN-020, yedek saklama (OPEN-014/OPEN-017) ve diğer raporlardaki `NEEDS-SINAN` maddeleri bu belgede
  seçilmedi; `NEEDS-SINAN` olarak kalır.
- Bu belge yeni rol açmaz ve hiçbir kişiye izin vermez. Persona dağılımları yukarıda yazıldığı gibidir; gerçek grant
  insan kapısındadır.
- `OPEN-*` kararları bu belgeyle kapanmaz.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
