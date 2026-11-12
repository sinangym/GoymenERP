# Sinan Kararları — 2026-11-12 Alış EK16 (API-30: kısmi mal kabulünden sonra termin)

Kaynak: Sinan Göymen'in koordinatör oturumunda AskUserQuestion ile verdiği kendi seçimi; koordinatör kaydı
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6816723313 madde 3 (NEEDS-SINAN
https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6812837765 Q4'ün cevabı). Bu dosya o kaydı numaralandırır; yeni
karar üretmez.

## 0. Yetki ve sınır

- EK13 §0 geçerlidir. **Değişmeyen insan kapıları:** production aktivasyonu, secret/deployment, grant/rol ve `OPEN-*` kapatma
  insanda kalır. Bu dosya hiçbir `OPEN-*` kapatmaz.

## 1. Karar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D81 | Kısmi mal kabulünden sonra sipariş satırı termini | "termin **yalnız kalan (teslim alınmamış) miktar için** değiştirilebilir. Teslim alınan kısım kilitlidir." | NEEDS-SINAN 6812837765 Q4: varsayılan "kabulden sonra yeniden termin serbest" | API-30, ALS-D33, ALS-D21 |

## 2. Uygulama okuması (geri alınabilir)

- Mal kabulü (kabul payı) bir termin dilimine bağlı kaydedilmez. Bu yüzden satırın kabul edilen miktarı, mevcut terminin
  dilimlerini dilim sırasına göre (1, 2, …) karşılıyor sayılır.
- Kabulün tamamen karşıladığı ve ardından kabul devam eden dilimler yeni terminde **aynı sırada, aynı kimlik, miktar ve
  tarihle** kalır.
- Kabulün bittiği dilim aynı sırada, aynı kimlik ve tarihle kalır. Miktarı teslim alınan kısmından az olamaz. Teslim
  alınmamış kalanı başka dilimlere/tarihlere taşınabilir ya da aynı tarihte artırılabilir.
- Bunların dışındaki her yeni termin `purchasing_schedule_received_portion_locked` koduyla reddedilir (422
  `validation_failed`, alan `slices`). Ret sonlu değildir; düzeltilmiş gövde yeni anahtarla kaydedilir.
- Kabul yoksa ya da kabulden önce hiç termin kaydedilmemişse kilitlenecek termin yoktur; termin serbesttir.
- Kural domain'dedir. Kayıtlı revizyonlar yeniden kurulurken (kabullerle birlikte sürüm sırasıyla) aynı kural tekrar uygulanır.

## 3. Açık kalanlar

- Kabulün belirli bir dilime bağlanması (sevk bildirimi/mal kabul dilim eşlemesi) API-31/API-32 dilimlerinde ele alınır.
  O zaman §2'deki sıra okuması gözden geçirilir.
