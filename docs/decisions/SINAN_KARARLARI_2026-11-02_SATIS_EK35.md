# Sinan Kararları — 2026-11-02 (satış faturasının alacak kaydı; modül sırası)

Bu belge önceki karar dosyalarını silmez; SAT-D208 ve SAT-D209'u ekler. İki karar önce koordinatör oturumu
(`session_015Usc2qbq4qsgayZcDY3UTb`) üzerinden Satış şeridine iletilmiş, Issue #66'ya devir notu olarak yazılmıştır
(https://github.com/sinangym/GoymenERP/issues/66). Bu dosya onları Sinan'ın kendi teyidiyle kayda geçirir.
DEC-FIN-001, DEC-FIN-004, DEC-FIN-006, FNS-D7 ve SAT-D172 geçerlidir.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
teyit cevapları: bir tur, iki soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Koordinatör oturumu senin şu kararını iletti: "Satış faturası Finans'ta tek bir alacak kalemi açar, Cari kartına ayrı hareket yazılmaz; 120 Alıcılar muhasebe fişi aynı fatura olayından Muhasebe'de üretilir." Bunu karar dosyasına senin kararın olarak yazayım mı? | Evet, doğru (Önerilen) | "Karar dosyasına yazılır. 120 fişi mevcut kurallara göre taslak fiş olur, onaylayınca kesinleşir; resmî defter dış sistemde kalır." (seçilmeyen: "Hayır, farklı" — "Farklı bir şey kastettim; açıklamaya yazacağım.") |
| 2 | Yine koordinatör üzerinden: "Ürün, Lojistik/Stok, Ayarlar ve Cari modüllerinin Claude geliştirmesi henüz başlamadı; önce Satış, Alış, Finans, Üretim bitecek." Bu sürede o modüllerde yeni bağlantı/tablo/ekran açılmasın mı? | Açılmasın (Önerilen) | "O modüllerin mevcut bağlantıları kullanılır; olmayan şey güvenli şekilde kapalı kalır ve 'o modül başlayınca' diye ertelenir." (seçilmeyen: "Gerekirse açılabilsin" — "4 modülün ihtiyacı olan küçük bağlantılar o modüllerde açılabilir.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D208 | Satış faturasının alacak kaydı | Satış faturası Finans'ta **tek bir alacak açık kalemi** açar; **Cari kartına ayrı hareket yazılmaz**. **120 Alıcılar** muhasebe fişi **aynı fatura olayından** Muhasebe'de üretilir (soru 1). 120 fişi mevcut kurallara göre **taslak fiştir**, kullanıcı onaylayınca kesinleşir; resmî defter dış sistemde kalır (soru 1'in açıklaması; DEC-FIN-001/DEC-FIN-004). 120 eşlemesi FNS-D7'deki anlam eşlemesi olarak kalır. Uygulama ayrıntısı (geri alınabilir): kaynak olay Satış'ın outbox'ından yayınlanan sürümlü `sales.invoice.finalized.v1` olayıdır; Finans (API-10) ve Muhasebe bu olayı inbox/dedup ile tüketir; idempotency anahtarı fatura kimliği + sürümüdür. Olayın alan listesi Finans ile fatura dalgasında netleşir | Satış fatura dalgası (API-32…37), Finans API-10, Muhasebe fiş tüketicisi |
| SAT-D209 | Modül sırası | Ürün, Lojistik/Stok, Ayarlar ve Cari modüllerinin **Claude geliştirmesi** henüz başlamadı; önce Satış, Alış, Finans ve Üretim tamamlanır (soru 2'nin metni). Bu sürede o dört modülde **yeni bağlantı, tablo veya ekran açılmaz**; mevcut bağlantıları kullanılır, olmayan şey güvenli şekilde kapalı (fail-closed) kalır ve "ertelendi: <modül> başlayınca" diye işaretlenir (soru 2). O modüllere başlanınca bağlantılar yeniden incelenir. Uygulama ayrıntısı (geri alınabilir): bu kayıt Satış şeridinin dosyasıdır ve Satış şeridini bağlar; ALS-D45 ve FNS-D1'in Alış ve Finans şeritlerine o modüllerde küçük ekler için verdiği izinler bu dosyayla değiştirilmez, çelişki olup olmadığı Issue #66'da `NEEDS-SINAN` olarak sorulur | Satış modül bağlantıları analizi, bütün şeritler |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`, dış kaynak ve e-Belge bağlantısının açılması) değiştirmez.
