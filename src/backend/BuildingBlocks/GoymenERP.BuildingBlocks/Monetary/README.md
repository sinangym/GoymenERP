# BuildingBlocks / Monetary

Ortak para değer nesneleri (TEK-01): `Money`, `ExchangeRate`. Hiçbiri ölçek, kur ya da kaynak seçmez;
değerler Ayarlar/Finans sahiplerinden çağıran tarafından getirilir.

## Önceki iş günü kuru (SAT-D54, SAT-D55, DEC-FIN-010)

`PreviousBusinessDayExchangeRate.Resolve(request, bulletinDays)` saf ve deterministiktir: saat, saat dilimi,
ağ, veritabanı, tatil, hafta günü ya da geriye bakış ayarı yoktur.

- **Kontrol günü D**: çağıranın Europe/Istanbul iş tarihinden (SAT-D10) türettiği `DateOnly`. Çözümleyici
  bir an (`DateTimeOffset`) almaz; UTC takvim günü D yerine kullanılmaz.
- **Yürüyüş**: D−1'den başlayıp birer takvim günü geri gider. D'nin kendi bülteni hiçbir zaman kullanılmaz.
  - Yayımlandı → o günün döviz **satış** kuru (`ExchangeRate`, kaynak para → hedef para). İstenen çift o
    günde yoksa `exchange_rate_currency_missing`; daha eski güne geçilmez.
  - Bülten yok → atlanır (`SkippedDays` artar).
  - Kayıt yok ya da `Unconfirmed` → `exchange_rate_day_unconfirmed`; sessizce eski kura kayılmaz.
  - Günün iş günü olup olmadığına yalnız **resmî** kayıt karar verir. O günün elle kaydı bunu değiştirmeye
    kalkarsa → `exchange_rate_manual_conflict` (aşağıda).
- **Sınır yok**: yürüyüş yalnız kesin "bülten yok" kaydı olan günlerden geçebildiği için kendiliğinden
  sınırlıdır; ayrı bir geriye bakış ayarı tutulmaz.
- **Sonuç**: kontrol günü, kaynak (TCMB), istenen çift (kaynak/hedef para birimi), atlanan resmî "bülten yok"
  gün sayısı; çözüldüyse bülten tarihi, kur ve kurun kaynağı (`RateProvenance`: resmî/elle), çözülmediyse kod
  ve yürüyüşü durduran gün (`StoppedAt`).

### Kur kaydı sözleşmesi (`ExchangeRateBulletinDay`)

Kur kaydı için henüz bir sahip modül ve tablo yok; çözümleyici yalnız şu sözleşmeyi bekler. Çağıran
sahibin deposundaki günleri verir. Gelecekteki içe aktarma bu sözleşmeye uymak zorundadır:

- Kayıtlar **yalnız TCMB'nin kendi bültenlerinden** gelir; üçüncü taraf arşivleri (yayım olmayan günü son
  bültenle dolduran seriler) kabul edilmez.
- **Her takvim günü** (hafta sonları ve tatiller dahil) için resmî kayıt yazılır. Hafta sonu ve tatil kodda
  bilinmez: içe aktarma o günleri TCMB'nin cevabına göre işaretler. Arife yalnız TCMB o gün yayımladıysa
  yayımlandı olur (SAT-D55).
- `Published`: en az bir satış kuru; çift başına tek kur; kur **bir** kaynak para birimi içindir (bültende
  100 birim için verilen satır kaydeden tarafından bire indirgenir). Boş çift ya da sıfır kur reddedilir.
- **Ters çift türetilmez.** TCMB yalnız yabancı para → TRY satış kuru yayımlar; TRY→USD ya da USD→EUR gibi
  çiftler bu çözümleyiciden her zaman `exchange_rate_currency_missing` döner, 1/kur ya da çapraz kur
  hesaplanmaz. SAT-D53'teki limit para birimi toplamı (ör. TL kalemini USD limitine çevirmek) için çevrim
  formülü (bölme mi, çapraz kur mu) risk diliminde ayrıca karar ister.
- `ConfirmedNoBulletin`: yalnız **kesin** bir cevapla yazılır (TCMB'nin resmî kaynağı o tarihli bültenin
  bulunmadığını bildirir). Kur taşımaz.
- `Unconfirmed`: diğer her cevap (ağ hatası, zaman aşımı, beklenmeyen yanıt). Kayıtsız günle aynıdır:
  yürüyüşü durdurur.
- Bir kaynağın bir günü için en çok **bir resmî ve bir elle** kayıt olur.

### Elle değişiklik (DEC-FIN-010)

`ExchangeRateBulletinDayProvenance.Manual` kaydı, DEC-FIN-010'un izin verdiği manuel kur değişikliğidir;
resmî kaydın yanında durur, onu silmez. `ExchangeRateManualEntry` işlemi yapan kullanıcıyı (repodaki diğer
modüller gibi metin `ActorId`, en çok `MaximumActorIdLength` = 128) ve gerekçeyi (kırpılır, en çok
`MaximumReasonLength` = 1000) taşır; ikisi de zorunludur. Yetki ve audit sahibin deposunun/komutunun işidir;
o yetkinin kime verileceği insan kapısındadır, manuel istisna sınırı OPEN-012'de açıktır. Giriş komutu bu
dilimde yoktur.

Elle kayıt **yalnız kur değerini** değiştirebilir; bir günün iş günü olup olmadığını değiştiremez (SAT-D55:
iş gününü yalnız TCMB'nin yayımı belirler).

- **Elle kur, resmî kaydı yayımlandı olan günde**: taşıdığı çiftler için o günün kurudur
  (`RateProvenance = Manual`); taşımadığı çiftler için resmî kur geçerlidir.
- **Elle kur, resmî kaydı "bülten yok", `Unconfirmed` olan ya da resmî kaydı olmayan günde**: o günü kur günü
  yapmaz; yürüyüş `exchange_rate_manual_conflict` ile o günde durur.
- **Elle "bülten yok"** (hangi resmî kaydın yanında olursa olsun): gün atlanmaz; yürüyüş
  `exchange_rate_manual_conflict` ile durur. "Bülten yok"u yalnız resmî kesin cevap yazar. Bu tür bir kayıt,
  saklanmış bir satır sessizce yok sayılmasın diye modellenmiştir.
- D ve sonraki günlerin elle kayıtları yürüyüşe girmez.

### Kapsam dışı

- TCMB'den çekme (içe aktarma) bir dış bağlantıdır ve insan kapısındadır; burada ağ erişimi yoktur.
- Kesin alma saati ve manuel istisna sınırı OPEN-012'de açıktır; çözümleyici yalnız önceki günlerin kaydını
  okuduğundan alma saati yalnız içe aktarmanın zamanlamasını etkiler.
- Risk kontrolüne bağlanması ayrı dilimdedir.
