# Work

Bildirim deposu `KLN-D114/D115` kapsamında kişisel uygulama içi bildirimleri,
olay tekilleştirme kaydını ve pasifleştirme geçmişini ayrı saklar. Varsayılan 90
gün Ayarlar owner'ından okunur; kullanıcı bu süreyi değiştirebilir. Kalıcı silme
yoktur; özgün olay ve denetim kayıtları korunur.

`AddPersonalNotifications` migration'ı üç tenant filtreli tablo ekler. Down guard
**51955**, teslimat, inbox tekilleştirme veya pasifleştirme geçmişi varken geri
almayı reddeder; boş depoda üretilen geri alma çalışır. Forward-fix gerekir.

Kaynak abonelikleri yalnız kayıt edilir. Dispatcher ve production etkinleştirme
insan kapısındadır. Satış sorumlusu ve alış oluşturan/onaylayan kimlikleri ilgili
modüllerin owner portlarından gelir; rol veya kullanıcı tahmini yapılmaz.

## İş kutusu (W1-A; AI-D31, AI-D32, YOL-D17, YOL-D18)

`GET /api/v1/work/inbox?view=awaiting-my-decision` "benden karar bekleyen" görünümünü verir.
Sahip modüller `IWorkInboxSourceV1` okuma portunu kendi tablolarından açar (12 tür: satış taban
fiyat / iskonto / risk istisnası; lojistik irsaliye, plan üstü sevk, tartım farkı; üretim planı;
alış siparişi, gelen fatura farkı, mal kabul partisi; cari kredi limiti, elle banka ödemesi).
Work hiçbir sahip tablosunu okumaz/yazmaz; her okumada sahibe yeniden sorar.

- Kullanıcı yalnız şirket genelinde onay iznine sahip olduğu türleri görür (Work sorar, sahip
  yeniden kontrol eder). Kaydı hazırlayan kendi kaydını görmez (maker ≠ checker).
- Açıklama kartı modelsizdir: kural kodu, eşik/gerçekleşen/sapma sahip modülün sakladığı veya
  kendi hesaplayıcısıyla ürettiği değerlerdir; Work ve ekran hesaplamaz, yapay zekâ çağrısı yoktur.
- Süre sınıfı sahibin bildirimidir (saat / iş günü); uzunluklar Ayarlar'da versiyonlu
  `work.approval-duration-hours` ve `work.approval-duration-business-days` kayıtlarıdır. Kayıt
  yoksa "süre tanımlı değil" gösterilir (varsayılan yok). İş günü hesabı Türkiye saatinde
  Pazartesi–Cuma sayar; resmî tatil takvimi henüz yoktur (ASSUMPTION-W1A-1, yalnız gösterim).
- Karar kutudan verilmez; kalem sahip ekranına bağlanır.

Ölçüm: `work.ApprovalInboxEvents` (yalnız ekleme, kişi kolonu yok → kişisel performans raporu
yok). `Arrived` = sahibin "bekliyor" zamanı; `Opened` / `OwnerScreenOpened` =
`POST /api/v1/work/inbox/events`; `ResolvedObserved` = tam bir okumada artık listelenmeyen
döngünün gözlem zamanı (kesin karar zamanı için sahip olay bağlaması sonraki dilim). Toplam
bekleme = `Arrived` → `ResolvedObserved`; aktif inceleme = ilk `Opened` → `ResolvedObserved`.

`AddWorkApprovalInboxEvents` Down guard **52080**: olay varken geri alma reddedilir (forward fix).
Ayarlar'daki `AddWorkApprovalDurationPolicies` Down guard **52084** (Work aralığı; anahtarlar
Work'e aittir): süre kaydı varken reddeder.
