# Sinan Kararları — 2026-11-02 (risk döviz çevriminde yuvarlama ve yuvarlamanın yeri)

Bu belge önceki karar dosyalarını silmez; SAT-D206 ve SAT-D207'yi ekler. EK31 SAT-D200 ve EK32 SAT-D203'te `NEEDS-SINAN` olarak açık
bırakılan döviz çevrim yuvarlamasını cevaplar. SAT-D11, SAT-D47, SAT-D54, SAT-D80, SAT-D200 ve SAT-D203 geçerlidir.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: iki tur, birer soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Risk hesabında döviz çevrimi yapılırken (ör. 100 USD → TL → EUR) yuvarlama nasıl olsun? Bu yalnız risk/limit kontrolü için, faturayı etkilemez. | Yalnız sonuç yuvarlansın (Önerilen) | "Ara TL değeri yuvarlanmadan taşınır, yalnız en sondaki limit para birimi tutarı kuruşa (2 ondalık, yarı-yukarı) yuvarlanır. En hassas sonuç." (seçilmeyen: "Her adımda kuruşa yuvarla" — "USD→TL sonucu kuruşa yuvarlanır, sonra TL→EUR sonucu tekrar kuruşa yuvarlanır. Faturadaki gibi görünür ama birkaç kuruş fark yapabilir.") |
| 2 | Risk hesabında birden fazla dövizli tutar varken yuvarlama nerede yapılsın? (Fark yalnız limit sınırında birkaç kuruş olabilir.) | Her tutar ayrı (Önerilen) | "Her çevrilen tutar ayrı ayrı kuruşa yuvarlanır, sonra toplanır. Bugünkü uygulama böyle; ekranda görünen satır tutarları toplamla birebir tutar." (seçilmeyen: "Yalnız toplam" — "Tüm tutarlar yuvarlanmadan toplanır, yalnız en sondaki toplam bir kez yuvarlanır. Daha hassas ama satır toplamı ekrandaki toplamdan bir kuruş farklı görünebilir.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D206 | Risk döviz çevriminde yuvarlama | Risk/limit kontrolü için yapılan döviz çevrimlerinde (SAT-D200 TL ÷ kur, SAT-D203 TL üzerinden çapraz çevrim) **ara TL değeri yuvarlanmaz**; yalnız **en sondaki limit para birimi tutarı** kuruşa (**2 ondalık, yarı-yukarı**) yuvarlanır (soru 1). Bu kural faturayı etkilemez (soru 1'in metni); fatura tutarları ve kurları SAT-D11/SAT-D172 ile kalır. Uygulama ayrıntısı (geri alınabilir): bölme işleminin kendi hassasiyeti uygulamanın ondalık türünün sınırındadır; "yuvarlanmaz" ara adımda bilinçli kuruş yuvarlaması yapılmaz demektir. "Kuruş (2 ondalık)" TL/USD/EUR içindir; ondalık sayısı farklı bir limit para biriminde (ör. JPY 0, KWD 3) son yuvarlama o para biriminin Ayarlar'daki onaylı ölçeğine yapılır (SAT-D75'teki "para biriminin ölçeği" okumasıyla aynı). Tutar başına mı toplam üzerinde mi yuvarlanacağı SAT-D207 ile belirlenmiştir | Sales risk (API-06/API-07), #914 |
| SAT-D207 | Yuvarlamanın yeri | Risk hesabında birden fazla dövizli tutar varken **her çevrilen tutar ayrı ayrı** SAT-D206'ya göre yuvarlanır, sonra yuvarlanmış tutarlar **toplanır**; toplam ayrıca yuvarlanmaz (soru 2). Bu bugünkü uygulamadır; ekranda görünen tutarlar toplamla birebir tutar | Sales risk (API-06/API-07), #914 |

## Sınırlar

- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`, dış kaynak bağlantısının açılması) değiştirmez.
