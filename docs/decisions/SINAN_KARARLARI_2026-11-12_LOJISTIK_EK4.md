# Sinan Kararları — 2026-11-12 (Lojistik: fason teslim müşteri risk kapısına tabidir)

Bu belge önceki karar dosyalarını silmez; `LOJ-D14` ekler. `SINAN_KARARLARI_2026-11-12_LOJISTIK_F012_F020.md`
(`LOJ-D1…D8`), `..._LOJISTIK_EK2.md` (`LOJ-D9`, `LOJ-D10`) ve `..._LOJISTIK_EK3.md` (`LOJ-D11…D13`) aynen geçerlidir;
LOJ-D14 yalnız EK3'teki "N-5 notu (Lojistik kapsamı)" bölümünün açık bıraktığı soruyu (fason teslim risk kapısına tabi
mi) kapatır.

**Kaynak:** Sinan'ın 12 Kasım 2026 `AskUserQuestion` cevabı; koordinatör Issue #66 yorumu
[`6821949043`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821949043) (2026-11-12T11:09:16Z) olarak
kaydetti; yorum, Lojistik şeridinin yazdığı `NEEDS-SINAN`
[`6821846244`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821846244) sorusunun cevabıdır. Lojistik
şeridi yorumu `gh api` ile birincil kaynaktan okudu. Cevap kelimesi kelimesine aktarılmıştır; etiket ve soru metni
alıntı dışındadır.

**NEEDS-SINAN 6821846244 cevabı (fason teslim, risk kapısı):**

> Fason teslim (F-012, Lojistik) **satış sevkiyle aynı müşteri risk kapısına tabidir; istisnayla geçer** — limit aşılmışsa teslim durur, yetkilinin tek seferlik istisnasıyla geçer (SAT-D310 ve 17. tur m.1 istisnası aynen).

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| LOJ-D14 | Fason teslim müşteri risk kapısı | Fason teslim (F-012), satış sevkiyle **aynı müşteri risk kapısına tabidir** ve **istisnayla geçer**: limit aşılmışsa teslim durur, yetkilinin **tek seferlik istisnasıyla** geçer. Kapının durumları ve istisna, Satış'ın SAT-D310 kararı ve 17. tur m.1 istisnası **aynen** geçerlidir; Lojistik ayrı kapı, ayrı eşik veya ayrı istisna kaynağı icat etmez. | F-012 (spec 04), Satış sevk risk kapısı (SAT-D310, Satış #1119), LOJ-D12 / EK3 N-5 notu |

## Yazarın okumaları (geri alınabilir, karar değil)

- Kapının Lojistik'e bağlanma şekli (Satış'ın okuma portunu tüketme; port adı ve şekli Satış #1119 S2 önerisidir, port
  main'de yoktur): spec 04 "Fason teslimde müşteri riski kapısı" bölümünde. Kapı kuralı satış sevk kapısıyla aynı
  fonksiyondur (`Within` geçer; `Over`/`CreditLimitNotSet`/`SourceUnavailable` yalnız `Valid` istisnayla; diğer durumlar ve
  port hatası durur). Tek kullanım Lojistik'in kesin sevk transaction'ında `(CompanyId, orderId, ExceptionNo)` başına tekil kayıtla zorlanır (`ExceptionNo` yalnız sipariş
  içinde tekildir). Talimattaki bir fason siparişi durursa talimatın tamamı durur (kısmi sevk yok; yazar okuması). Tartı öncesi
  risk tutarı Sinan'ın #66 6822767886 m.1 kararıyla "Sevk plan miktarı x onaylı fiyat"tır; Satış hesaplar, Lojistik yalnız
  kapıyı uygular.
  Port main'e girene kadar fason teslim kesin sevki 503 `Unavailable` ile kapalıdır (fail-closed).
- İstisnanın izin adı, kaynağı ve ekranı Satış'ın kapsamıdır; grant/rol insan kapısıdır.

## Sınırlar

- Kapı eşiği, kredi limiti ve risk durumlarının anlamı Satış kararıdır; burada seçilmez.
- İnsan kapıları değişmez: deployment, environment, secret, Settings'te workflow etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma, `.claude/settings.json`. Hiçbir `OPEN-*` bu belgeyle kapatılmadı.
