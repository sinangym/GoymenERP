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
| SAT-D165 | Müşteri reddinden sonra sipariş PDF'i | Güncel revizyonu müşteri tarafından reddedilmiş bir siparişte sipariş PDF'i **müşterinin kabul ettiği son revizyonu** basar: o revizyonun satırları, miktarları ve fiyatları. Reddedilen revizyon PDF'te bir notla belirtilir (soru 1). Uygulama ayrıntısı (geri alınabilir): notun içeriği yalnız hangi revizyonun müşteri tarafından reddedildiği ve siparişin hangi revizyonla sürdüğüdür (ör. "Rev.2 müşteri tarafından reddedildi; sipariş Rev.1 ile sürer"). İç kayıttaki serbest metin ret gerekçesi müşteriye giden PDF'e **basılmaz**; basılması ayrı bir Sinan kararı ister. Dosya adı ve belgede gösterilen revizyon numarası kabul edilen revizyonu gösterir. Ret sonrasında açılan yeni bir revizyon müşteri kabulü bekliyorsa güncel revizyon artık reddedilmiş değildir; PDF bugünkü gibi güncel revizyonu basar | Sipariş PDF'i (MOD-07, #842), PR #874 |
| SAT-D166 | Müşteri reddinden sonra risk | Müşteri bir revizyonu reddettiğinde risk hesabındaki onaylı sipariş kalanı **müşterinin kabul ettiği son revizyona göre** ölçülür (soru 2). Reddedilen revizyon riske girmez; risk, SAT-D48'in kabul beklenirken koyduğu yeni tutar yerine kabul edilen revizyonun kalanına döner. Kabul **bekleyen** revizyon için (ret sonrasında açılan yeni revizyon dahil) SAT-D50 aynen geçerlidir. Uygulama ayrıntısı (geri alınabilir): ret komutu yeni bir sipariş aşaması limit kontrolü tetiklemez; kabul edilen revizyonun tutarı daha önce kontrol edilmişti, sevkteki kesin kontrol (DEC-FIN-009) değişmeden çalışır | API-06 risk kontrolü, PR #874 |

## Kapsam notu

- Ret yalnız müşterinin daha önce bir revizyonu kabul ettiği siparişte kaydedilebilir (SAT-D113: Rev.0'ın davranışı değişmez; PR #874 bu durumda reddi reddeder). Bu yüzden SAT-D165 ve SAT-D166'nın uygulandığı her siparişte kabul edilmiş bir revizyon vardır. Ret kaydı olmayan siparişte PDF ve risk bugünkü gibi çalışır.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
