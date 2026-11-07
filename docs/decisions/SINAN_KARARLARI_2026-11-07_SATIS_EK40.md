# Sinan Kararları — 2026-11-07 (kapanmaya hazır siparişte sahibi olmayan eksenler)

Bu belge önceki karar dosyalarını silmez; SAT-D221'i ekler. SAT-D32 (kullanıcının "kapanmaya hazır" siparişi kapatması) uygulamasında
(PR #952) açık kalan soruyu cevaplar. SAT-D12, SAT-D32, SAT-D168 ve EK37 geçerlidir.

**Kaynak:** Sinan'ın 7 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevap: bir tur, bir soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevap

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | "Kapanmaya hazır" siparişi kullanıcı kapatabilecek; ama hazır sayılmak için kaynak (üretim/tedarik), faturalama ve finansal kapanış da tamam olmalı. Bunların sahibi modüller henüz yok, bu yüzden şu an hiçbir sipariş kapatılamıyor. Ne yapalım? | Hepsi gelene kadar kapalı (Önerilen) | "Fatura ve finans eksenleri gelene kadar kullanıcı kapatması çalışmaz (409). Güvenli; faturası kesilmemiş sipariş kapanmaz. Gerekirse zorla kapatma kullanılır." (seçilmeyen: "Şimdilik mevcut eksenlerle" — "Onay, müşteri kabulü ve sevk tamamlanınca kapatılabilir; fatura/finans eksenleri gelince otomatik eklenir. Risk: faturası kesilmemiş sipariş kapanabilir.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D221 | Sahibi olmayan eksenler | "Kapanmaya hazır" değerlendirmesi SAT-D32'deki **bütün eksenleri** ister; kaynak, faturalama ve finansal kapanış eksenlerinin sahibi gelene kadar kullanıcı kapatması **çalışmaz** (409, fail-closed). Gerekirse zorla kapatma (SAT-D12) kullanılır (soru 1). Mevcut eksenlerle geçici kapatma seçilmedi | PR #952 |

## Sınırlar

- Bu karar merge kapılarını ve insan kapılarını değiştirmez.
