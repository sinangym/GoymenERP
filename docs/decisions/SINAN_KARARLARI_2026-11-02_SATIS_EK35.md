# Sinan Kararları — 2026-11-02 (satış faturasının alacak kaydı; modül sırası)

Bu belge önceki karar dosyalarını silmez; SAT-D208 ve SAT-D209'u ekler. İki karar önce koordinatör oturumu
(`session_015Usc2qbq4qsgayZcDY3UTb`) üzerinden Satış şeridine iletilmiş, Issue #66'ya devir notu olarak yazılmıştır
(https://github.com/sinangym/GoymenERP/issues/66). Bu dosya onları Sinan'ın kendi teyidiyle kayda geçirir.
DEC-FIN-001, DEC-FIN-004, DEC-FIN-006, FNS-D7 ve SAT-D172 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevabıyla):**
- **ALS-D45 ve FNS-D1:** Alış ve Finans şeritlerinin Ürün, Lojistik/Stok, Ayarlar ve Cari modüllerinde küçük ekler yapma izni, o modüllerin Claude geliştirmesi başlayana kadar **askıya alınır** (soru 3, SAT-D209).

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
teyit cevapları: iki tur, üç soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Koordinatör oturumu senin şu kararını iletti: "Satış faturası Finans'ta tek bir alacak kalemi açar, Cari kartına ayrı hareket yazılmaz; 120 Alıcılar muhasebe fişi aynı fatura olayından Muhasebe'de üretilir." Bunu karar dosyasına senin kararın olarak yazayım mı? | Evet, doğru (Önerilen) | "Karar dosyasına yazılır. 120 fişi mevcut kurallara göre taslak fiş olur, onaylayınca kesinleşir; resmî defter dış sistemde kalır." (seçilmeyen: "Hayır, farklı" — "Farklı bir şey kastettim; açıklamaya yazacağım.") |
| 2 | Yine koordinatör üzerinden: "Ürün, Lojistik/Stok, Ayarlar ve Cari modüllerinin Claude geliştirmesi henüz başlamadı; önce Satış, Alış, Finans, Üretim bitecek." Bu sürede o modüllerde yeni bağlantı/tablo/ekran açılmasın mı? | Açılmasın (Önerilen) | "O modüllerin mevcut bağlantıları kullanılır; olmayan şey güvenli şekilde kapalı kalır ve 'o modül başlayınca' diye ertelenir." (seçilmeyen: "Gerekirse açılabilsin" — "4 modülün ihtiyacı olan küçük bağlantılar o modüllerde açılabilir.") |
| 3 | "Ürün, Lojistik/Stok, Ayarlar, Cari'de yeni bağlantı/tablo/ekran açılmasın" kararın Alış ve Finans şeritlerini de bağlasın mı? Daha önceki iki kararın (ALS-D45, FNS-D1) Alış ve Finans'ın bu modüllere küçük ekler yapmasına izin veriyordu. | Hepsini bağlasın (Önerilen) | "Satış, Alış, Finans ve Üretim'in hiçbiri o 4 modülde yeni bağlantı/tablo/ekran açmaz; ALS-D45/FNS-D1'deki izin o modüllere başlanana kadar askıya alınır. Olmayan şey güvenli kapalı kalır." (seçilmeyen: "Yalnız Satış'ı" — "Alış ve Finans eski izinleriyle (ALS-D45/FNS-D1) küçük ekler yapmaya devam edebilir; yasak yalnız Satış şeridi için.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D208 | Satış faturasının alacak kaydı | Satış faturası Finans'ta **tek bir alacak açık kalemi** açar; **Cari kartına ayrı hareket yazılmaz**. **120 Alıcılar** muhasebe fişi **aynı fatura olayından** Muhasebe'de üretilir (soru 1). 120 fişi mevcut kurallara göre **taslak fiştir**, kullanıcı onaylayınca kesinleşir; resmî defter dış sistemde kalır (soru 1'in açıklaması; DEC-FIN-001/DEC-FIN-004). 120 eşlemesi FNS-D7'deki anlam eşlemesi olarak kalır. Uygulama ayrıntısı (geri alınabilir): kaynak olay Satış'ın outbox'ından yayınlanan sürümlü `sales.invoice.finalized.v1` olayıdır; Finans (API-10) ve Muhasebe bu olayı inbox/dedup ile tüketir; idempotency anahtarı fatura kimliği + sürümüdür. Olayın alan listesi Finans ile fatura dalgasında netleşir. ERP-000F dağıtıcısı gelene kadar ara dönemde Satış, kesinleşmeyi kendi işleminde "Finans kabulü bekliyor" durumuyla kaydeder ve Finans'ın sürümlü kabul portunu aynı anahtarla çağırır (SINAN 10-21 No 55, Finans hazırlık planı §4); bekleyen durum kalıcıdır ve teslim edilene kadar yeniden sürülür (arka plan taraması veya yetkili, audit'li "yeniden gönder"); Finans kabul etmeden alacak açılmış sayılmaz | Satış fatura dalgası (API-32…37), Finans API-10, Muhasebe fiş tüketicisi |
| SAT-D209 | Modül sırası | Ürün, Lojistik/Stok, Ayarlar ve Cari modüllerinin **Claude geliştirmesi** henüz başlamadı; önce Satış, Alış, Finans ve Üretim tamamlanır (soru 2'nin metni). Bu sürede o dört modülde **yeni bağlantı, tablo veya ekran açılmaz**; mevcut bağlantıları kullanılır, olmayan şey güvenli şekilde kapalı (fail-closed) kalır ve "ertelendi: <modül> başlayınca" diye işaretlenir (soru 2). O modüllere başlanınca bağlantılar yeniden incelenir. Bu kural **Satış, Alış, Finans ve Üretim şeritlerinin hepsini** bağlar; ALS-D45/FNS-D1'deki küçük ek izni o modüllere başlanana kadar askıdadır (soru 3) | Satış modül bağlantıları analizi, bütün şeritler |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`, dış kaynak ve e-Belge bağlantısının açılması) değiştirmez.
