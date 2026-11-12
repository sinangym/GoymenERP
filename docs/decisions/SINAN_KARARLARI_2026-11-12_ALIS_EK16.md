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
- NEEDS-SINAN https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6812837765'in Q4 dışındaki soruları hâlâ
  açıktır. Kod bugün aşağıdaki emsal varsayılanlarla çalışır; cevap gelirse ayrı dilimle değiştirilir. Bunlar karar değildir:

  | Soru | Konu | Bugünkü varsayılan |
  |---|---|---|
  | Q1 | Teknik (fason/carve-out) satıra termin | Yalnız genel satır terminlenir; teknik satır 422 `purchasing_schedule_on_technical_line` |
  | Q2 | Geçmiş tarih / dilimlerin tarih sırası | Serbest (Satış emsali): geçmiş tarih ve sırasız dilim kabul edilir, sıra verildiği gibi kalır |
  | Q3 | Onayın her satırda termin şartı | Aramaz |
  | Q5 | Termin için ayrı yetki | Yok; `purchasing.orders.create` (ALS-D60 emsali) |

- **Teknik satıra kaydedilen kabul (bugün erişilemez):** ALS-D81 kilidi (`RequireReceivedPortionKept`) yalnız genel satırın kendi
  kabul payını sayar. Genel satırın termininde sayılan bir teknik satıra kabul kaydedilirse o kısım kilide girmez. Bugün teknik
  satır üreten bir production yolu yoktur (`BreakDown` yalnız `RestoreOrder` içinde çağrılır). Ayrıca `RestoreOrder` teknik
  satırları revizyonlardan önce oynatır; termin kaydından sonra yapılan bir kırılım yeniden kurulumda sürüm sırasını bozar.
  Kırılım komutu gelmeden önce o dilim iki noktayı kapatır: alt teknik satır kabulleri kilide sayılır ve `RestoreOrder`
  kırılımı revizyonlarla aynı sürüm sırasında oynatır.
