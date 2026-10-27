# BuildingBlocks / Monetary

Ortak para değer nesneleri (TEK-01): `Money`, `ExchangeRate`. Hiçbiri ölçek, kur ya da kaynak seçmez;
değerler Ayarlar/Finans sahiplerinden çağıran tarafından getirilir.

## Önceki iş günü kuru (SAT-D54, SAT-D55, DEC-FIN-010)

`PreviousBusinessDayExchangeRate.Resolve(request, bulletinDays)` saf ve deterministiktir: saat, saat dilimi,
ağ, veritabanı, tatil ya da hafta günü bilgisi yoktur.

- **Kontrol günü D**: çağıranın Europe/Istanbul iş tarihinden (SAT-D10) türettiği `DateOnly`. Çözümleyici
  bir an (`DateTimeOffset`) almaz; UTC takvim günü D yerine kullanılmaz.
- **Yürüyüş**: D−1'den başlayıp birer takvim günü geri gider. D'nin kendi bülteni hiçbir zaman kullanılmaz.
  - `Published` → o günün döviz **satış** kuru (`ExchangeRate`, kaynak para → hedef para). İstenen çift o
    günde yoksa `exchange_rate_currency_missing`; daha eski güne düşülmez.
  - `ConfirmedNoBulletin` → atlanır (`SkippedDays` artar).
  - Kayıt yok (içe aktarma boşluğu/hatası) → `exchange_rate_day_unconfirmed`; sessizce eski kura kayılmaz.
  - Geriye bakış sınırı aşıldı → `exchange_rate_lookback_exceeded`.
- **Geriye bakış sınırı** (`LookbackCapDays`): Ayarlar'dan okunur; varsayılanı yoktur. Ayarlar kaydı yoksa
  çağıran çözümleyiciyi çağırmaz (`settings_provider_unavailable`, fail-closed). Değer tahmin edilmez.
- **Sonuç**: kontrol günü, kaynak (TCMB), atlanan gün sayısı; çözüldüyse bülten tarihi ve kur, çözülmediyse
  kod ve yürüyüşü durduran gün (`StoppedAt`).

### Kur kaydı sözleşmesi (`ExchangeRateBulletinDay`)

Kur kaydı için henüz bir sahip modül ve tablo yok; çözümleyici yalnız şu sözleşmeyi bekler. Çağıran
[D − sınır, D − 1] penceresindeki günleri sahibin deposundan okuyup verir.

- Bir kaynağın bir takvim günü için **tek** kayıt; durum `Published` ya da `ConfirmedNoBulletin`.
- `Published`: en az bir satış kuru; çift başına tek kur; kur **bir** kaynak para birimi içindir (bültende
  100 birim için verilen satır kaydeden tarafından bire indirgenir). Ters çift kendi yayımlanmış kurudur,
  1/kur olarak türetilmez.
- `ConfirmedNoBulletin`: yalnız resmî kaynaktan doğrulanmış "bülten yok" cevabıyla (ör. resmî 404, tatil)
  yazılır; kur taşımaz. Ağ/zaman aşımı hatası bu durum değildir; o gün kayıtsız kalır.
- **Hafta sonu ve tatiller** kodda bilinmez: içe aktarma Cumartesi/Pazar ve tatil günlerini de
  `ConfirmedNoBulletin` olarak işaretler. Arife yalnız TCMB o gün yayımladıysa `Published` olur (SAT-D55).

### Kapsam dışı

- TCMB'den çekme (içe aktarma) bir dış bağlantıdır ve insan kapısındadır; burada ağ erişimi yoktur.
- Kesin alma saati ve manuel istisna sınırı OPEN-012'de açıktır; çözümleyici yalnız önceki günlerin kaydını
  okuduğundan alma saati yalnız içe aktarmanın zamanlamasını etkiler.
- Risk kontrolüne bağlanması ayrı dilimdedir.
