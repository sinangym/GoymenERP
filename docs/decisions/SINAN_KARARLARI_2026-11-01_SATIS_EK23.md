# Sinan Kararları — 2026-11-01 (Satış: müşteri reddinden sonra sipariş PDF'i ve risk tabanı)

Bu belge önceki karar dosyalarını silmez; SAT-D165 ve SAT-D166'yı ekler. PR #874'ün (SAT-D115 müşteri reddi komutu) K3
review'unun açtığı iki soruyu cevaplar. EK14 SAT-D113…D115, EK7 SAT-D48/D50 ve DEC-FIN-009 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Sipariş PDF'i:** SAT-D165, PR #842'deki sipariş PDF'inin davranışını değiştirir. Bugün PDF her zaman güncel revizyonu
  basıyor. Müşteri reddi olan siparişte bundan sonra müşterinin kabul ettiği revizyonu basar.
- **Risk tabanı:** SAT-D166, SAT-D50'nin müşteri reddindeki uygulanışını belirler. SAT-D50'ye göre iç onaylı her sipariş
  müşteri kabulü beklenmeden riske girer. Kabul **bekleyen** revizyon için bu kural aynen kalır. Müşterinin **reddettiği**
  revizyon ise riske girmez; risk, kabul edilen son revizyona göre ölçülür.

**Kaynak:** Sinan'ın 1 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği seçmeli
soru cevapları: bir tur, iki soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Müşteri siparişin yeni bir revizyonunu reddettiğinde, sipariş PDF'i neyi bassın? (Sevk ve fatura zaten müşterinin kabul ettiği son revizyonla devam ediyor.) | Kabul edilen revizyonu bassın (Önerilen) | "PDF, geçerli olan (müşterinin kabul ettiği) revizyonun miktar ve fiyatlarını gösterir; reddedilen revizyon bir notla belirtilir." (seçilmeyenler: "Reddedileni filigranla bassın" — "PDF son revizyonu basar ama üzerinde 'müşteri reddetti, geçersiz' filigranı olur."; "İkisini de ayrı seçenek olarak sunsun" — "Kullanıcı PDF'i hangi revizyon için alacağını seçer.") |
| 2 | Müşteri bir revizyonu reddettiğinde risk hesabı hangi revizyona göre yapılsın? | Kabul edilen son revizyona göre (Önerilen) | "Gerçekte sevk ve fatura edilecek miktar o olduğu için risk de ona göre hesaplanır." (seçilmeyen: "Hangisi büyükse ona göre" — "Reddedilen ve kabul edilen revizyondan riski büyük olan sayılır (ihtiyatlı).") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D165 | Müşteri reddinden sonra sipariş PDF'i | Güncel revizyonu müşteri tarafından reddedilmiş bir siparişte sipariş PDF'i **müşterinin kabul ettiği son revizyonu** basar: o revizyonun satırları, miktarları ve fiyatları. Reddedilen revizyon PDF'te bir notla belirtilir: hangi revizyon, ret gerekçesi, siparişin hangi revizyonla sürdüğü (soru 1). Uygulama ayrıntısı (geri alınabilir): dosya adı ve belgede gösterilen revizyon numarası kabul edilen revizyonu gösterir | Sipariş PDF'i (MOD-07, #842), PR #874 |
| SAT-D166 | Müşteri reddinden sonra risk | Müşteri bir revizyonu reddettiğinde risk hesabındaki onaylı sipariş kalanı **müşterinin kabul ettiği son revizyona göre** ölçülür (soru 2). Reddedilen revizyon riske girmez. Kabul **bekleyen** revizyon için SAT-D50 aynen geçerlidir | API-06 risk kontrolü, PR #874 |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
