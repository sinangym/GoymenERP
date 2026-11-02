# Sinan Kararları — 2026-11-02 (satır revizyonu: yetki, bekleyen kaldırma, okuma tarihi)

Bu belge önceki karar dosyalarını silmez; SAT-D210…SAT-D212'yi ekler. EK24 SAT-D167 (satır ekleme, sevk edilmemiş satırı
kaldırma, ürün değiştirme; Rev+1 ve iç onay) uygulamasında (PR #926) açık kalan üç soruyu cevaplar. SAT-D151 (fiyat yazma
yetkisi) ve SAT-D181 (vergi listeleri) geçerlidir.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: bir tur, üç soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Siparişte satır ekleme / sevk edilmemiş satırı kaldırma / ürün değiştirme (yeni revizyon, onaya döner) işlemini kim yapabilsin? | Miktar değiştirenler (Önerilen) | "Bugün sipariş miktarını revize edebilen kişiler (Satış Uzmanı, Satış Müdürü, Genel Müdür, Admin) satır ekleme/çıkarma/ürün değişikliğini de yapar. Fiyat yazımı yine ayrı fiyat yetkisine bağlı." (seçilmeyen: "Ayrı yetki olsun" — "Satır ekleme/çıkarma için ayrı bir yetki olur; kime verileceğini ayrıca belirlersin.") |
| 2 | Bir satırı kaldıran revizyon onay beklerken (ya da reddedilirse), o satır son onaylı revizyona göre sevk edilebilsin mi? | Sevk edilmesin (Önerilen) | "Kaldırılması istenen satır, onay sonuçlanana kadar sevk edilmez. Reddedilirse satır geri döner ve sevk edilebilir. Güvenli taraf." (seçilmeyen: "Sevk edilebilsin" — "Onaylanana kadar son onaylı revizyon geçerlidir; satır sevk edilebilir, sevk edilirse kaldırma talebi reddolur.") |
| 3 | Revizyonla eklenen yeni satırın ürün uygunluğu, birimi ve vergisi hangi tarihe göre okunsun? | Revizyon günü (Önerilen) | "Satırın eklendiği günün ürün ve vergi bilgisi kullanılır; güncel bilgi." (seçilmeyen: "Siparişin tarihi" — "Siparişin ilk iş günündeki ürün ve vergi bilgisi kullanılır; siparişin tamamı aynı tarihe göre.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D210 | Satır revizyonu yetkisi | Satır ekleme, sevk edilmemiş satırı kaldırma ve ürün değiştirme, sipariş **miktarını revize edebilen** kişilerin yetkisidir; ayrı bir yetki adı açılmaz (soru 1). Fiyat, iskonto ve vergi yazımı SAT-D151'deki ayrı fiyat yetkisine bağlı kalır. Uygulama ayrıntısı (geri alınabilir): mevcut `sales.orders.quantity.write` izni kullanılır; gerçek grant ataması insan kapısıdır | PR #926 |
| SAT-D211 | Bekleyen kaldırmada sevk | Bir satırı kaldıran revizyon onay beklerken o satır **sevk edilmez**; revizyon **reddedilirse** satır geri döner ve sevk edilebilir (soru 2) | PR #926, sevk/yükleme okuması |
| SAT-D212 | Eklenen satırın okuma tarihi | Revizyonla eklenen yeni satırın ürün uygunluğu, birimi ve vergisi **revizyon gününe** (satırın eklendiği iş gününe) göre okunur (soru 3). Uygulama ayrıntısı (geri alınabilir): mevcut satırlar kendi okundukları tarihin değerlerini korur; "revizyon günü" sunucunun iş günüdür | PR #926 |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez.
