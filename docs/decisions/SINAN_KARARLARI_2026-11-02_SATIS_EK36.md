# Sinan Kararları — 2026-11-02 (satır revizyonu: yetki, bekleyen kaldırma, okuma tarihi)

Bu belge önceki karar dosyalarını silmez; SAT-D210…SAT-D212'yi ekler. EK24 SAT-D167 (satır ekleme, sevk edilmemiş satırı
kaldırma, ürün değiştirme; Rev+1 ve iç onay) uygulamasında (PR #926) açık kalan üç soruyu cevaplar. SAT-D151 (fiyat yazma
yetkisi) ve SAT-D181 (vergi listeleri) geçerlidir.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: iki tur, beş soru (2 ve 3 Kasım). İkinci tur, birinci turdaki 1. sorunun açıklamasındaki persona listesinin SAT-D3/SAT-D141 ile uyuşmadığı review'da görülünce sorulmuştur; 4. soru 1. sorunun persona kısmını düzeltir. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Siparişte satır ekleme / sevk edilmemiş satırı kaldırma / ürün değiştirme (yeni revizyon, onaya döner) işlemini kim yapabilsin? | Miktar değiştirenler (Önerilen) | "Bugün sipariş miktarını revize edebilen kişiler (Satış Uzmanı, Satış Müdürü, Genel Müdür, Admin) satır ekleme/çıkarma/ürün değişikliğini de yapar. Fiyat yazımı yine ayrı fiyat yetkisine bağlı." (seçilmeyen: "Ayrı yetki olsun" — "Satır ekleme/çıkarma için ayrı bir yetki olur; kime verileceğini ayrıca belirlersin.") |
| 2 | Bir satırı kaldıran revizyon onay beklerken (ya da reddedilirse), o satır son onaylı revizyona göre sevk edilebilsin mi? | Sevk edilmesin (Önerilen) | "Kaldırılması istenen satır, onay sonuçlanana kadar sevk edilmez. Reddedilirse satır geri döner ve sevk edilebilir. Güvenli taraf." (seçilmeyen: "Sevk edilebilsin" — "Onaylanana kadar son onaylı revizyon geçerlidir; satır sevk edilebilir, sevk edilirse kaldırma talebi reddolur.") |
| 3 | Revizyonla eklenen yeni satırın ürün uygunluğu, birimi ve vergisi hangi tarihe göre okunsun? | Revizyon günü (Önerilen) | "Satırın eklendiği günün ürün ve vergi bilgisi kullanılır; güncel bilgi." (seçilmeyen: "Siparişin tarihi" — "Siparişin ilk iş günündeki ürün ve vergi bilgisi kullanılır; siparişin tamamı aynı tarihe göre.") |
| 4 | Bir önceki soruda "miktarı revize edebilenler: Satış Uzmanı, Satış Müdürü, GM, Admin" yazmıştım; ama eski kararın (SAT-D3) miktar revizyonunu Satış Destek'in açması, Müdür'ün yalnız onaylaması yönündeydi. Miktar revizyonunu ve satır ekleme/çıkarmayı kim AÇABILSIN? | Destek+Uzman açar, müdür onaylar (Önerilen) | "Satış Destek ve Satış Uzmanı revizyonu açar; Satış Müdürü/GM/Admin onaylar (SAT-D3 aynen). Hazırlayan onaylayamaz." (seçilmeyenler: "Müdürler de açabilsin" — "Destek ve Uzman'a ek olarak Satış Müdürü, GM ve Admin de revizyon açabilir; ama kendi açtığını onaylayamaz."; "Destek açamasın" — "Yalnız Satış Uzmanı (ve üstü) açar; Satış Destek açamaz.") |
| 5 | Revizyonda ürünü değiştirilen satırın yeni ürün uygunluğu, birimi ve vergisi hangi tarihe göre okunsun? | Revizyon günü (Önerilen) | "Eklenen satır gibi: ürünün değiştirildiği günün bilgisi kullanılır." (seçilmeyen: "Siparişin tarihi" — "Siparişin ilk iş günündeki bilgiye göre okunur.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D210 | Satır revizyonu yetkisi | Satır ekleme, sevk edilmemiş satırı kaldırma ve ürün değiştirme, miktar revizyonuyla **aynı yetkidir**; ayrı yetki açılmaz (soru 1). Revizyonu **Satış Destek ve Satış Uzmanı açar**; **Satış Müdürü, Genel Müdür ve Admin onaylar**; hazırlayan onaylayamaz (soru 4; SAT-D3 aynen, SAT-D141). 1. sorunun açıklamasındaki persona listesi ("Satış Uzmanı, Satış Müdürü, GM, Admin") 4. soruyla düzeltilmiştir. Fiyat, iskonto ve vergi yazımı SAT-D151'deki ayrı fiyat yetkisine bağlı kalır. Uygulama ayrıntısı (geri alınabilir): mevcut `sales.orders.quantity.write` izni kullanılır (Satış hazırlık planı API-05); gerçek grant ataması insan kapısıdır | PR #926, persona kartı |
| SAT-D211 | Bekleyen kaldırmada sevk | Bir satırı kaldıran revizyon onay beklerken o satır **sevk edilmez**; revizyon **reddedilirse** satır geri döner ve sevk edilebilir (soru 2) | PR #926, sevk/yükleme okuması |
| SAT-D212 | Eklenen veya ürünü değişen satırın okuma tarihi | Revizyonla **eklenen** satırın ve **ürünü değiştirilen** satırın ürün uygunluğu, birimi ve vergisi **revizyon gününe** göre okunur (soru 3 ve 5). Uygulama ayrıntısı (geri alınabilir): ürünü ve miktarı değişmeyen satırlar kendi okundukları tarihin değerlerini korur (snapshot kuralı, SAT-D41); "revizyon günü" sunucunun bugünkü iş günüdür (SAT-D10/SAT-D71) | PR #926 |

## Sınırlar

- SAT-D211 yalnız **kaldırılan** satırın sevkini bağlar. Onay bekleyen revizyonda **eklenen** ya da **ürünü değişen** satırın sevki bu kararla bağlanmaz; SAT-D104/SAT-D111'deki "değişiklik onaydan sonra geçerli" ilkesi geçerlidir.

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez.
