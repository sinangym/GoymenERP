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
  Pazartesi–Cuma sayar ve YOL-D22 gereği şirketin Ayarlar'da yıllık, sürümlü kaydettiği resmî
  tatil/arife listesini düşer (`settings.WorkHolidayCalendarVersions/Days`, aynı
  `settings.work-approval-duration.manage` izni). Bir yılın listesi yoksa yalnız hafta sonu düşülür
  ve kalemde "tatil takvimi tanımsız" uyarısı görünür; liste okunamazsa "okunamadı" uyarısı.
- Karar kutudan verilmez; kalem sahip ekranına bağlanır.

Ölçüm: `work.ApprovalInboxEvents` (yalnız ekleme, kişi kolonu yok → kişisel performans raporu
yok). `Arrived` = sahibin "bekliyor" zamanı; `Opened` / `OwnerScreenOpened` =
`POST /api/v1/work/inbox/events`; `ResolvedObserved` = tam bir okumada artık listelenmeyen
döngünün gözlem zamanı (kesin karar zamanı için sahip olay bağlaması sonraki dilim). Toplam
bekleme = `Arrived` → `ResolvedObserved`; aktif inceleme = ilk `Opened` → `ResolvedObserved`.

`AddWorkApprovalInboxEvents` Down guard **52080**: olay varken geri alma reddedilir (forward fix).
Ayarlar'daki `AddWorkApprovalDurationPolicies` Down guard **52084** (Work aralığı; anahtarlar ve
tatil takvimi Work'e aittir): süre veya tatil takvimi kaydı varken reddeder.
## W1-C: kayda bağlı not, bahsetme, görev ve iki sınıflı bildirim (AI-D20, AI-D23, AI-D29, YOL-D19)

Yapay zekâ yoktur. Kayıt türleri: satış siparişi taslağı, alış siparişi, stok etiketi, üretim planı, cari. Kaydın
varlığı ve okuma yetkisi sahip modülün kendi yetkili okumasından gelir (`IWorkRecordAccessPortV1`, host adaptörü
`WorkOwnerRecordAccessPortV1`); Work başka modül tablosunu okumaz. Bahsedilen kişinin okuma yetkisi Identity'nin
`IUserPermissionHolderReadPortV1` portuyla (sahibin okuma izni, şirket genelinde, şimdi) ölçülür; okuyamayan kişiye
bahsetme bütün notu 422 `work_mention_refused` ile reddeder ve kişinin var olup olmadığını söylemez.

- Not düzenlenmez; düzeltme `correctsNoteId` taşıyan yeni nottur. Silinmez; yazarı pasifleştirir (ayrı satır).
  Saklama YOL-D19: bağlı kayıt kadar, en az 10 yıl. Bu dilimde hiçbir imha/otomatik pasifleştirme yolu yoktur;
  saklama süresi parametresi imha politikası gelince Ayarlar'a bağlanır.
- Görev: kayda bağlı ya da bağımsız; atanan yalnız aynı şirketin etkin üyesi (bağlı görevde kaydı da okuyabilmeli);
  açık → tamamlandı (yalnız atanan) / iptal (yalnız atayan, gerekçeli); beklenen sürüm, idempotency anahtarı, audit.
- Bildirim sınıfları (AI-D29 varsayılanı): bahsetme ve süresi geçen görev "şimdi"; görev ataması "özet".
  Aynı alıcı + tür + kaynak için ikinci bildirim yoktur (benzersiz anahtar). Okundu bilgisi ayrı, ekleme-yalnız satır.
- Özet saatleri Ayarlar parametresidir; Settings anahtarı henüz yok, host okuyucusu `Unavailable` döner ve özet sınıfı
  bekletilir (erken gösterilmez). NEEDS-SINAN: varsayılan özet saatleri.
- Zamanlayıcı (`Work:CollaborationScheduler`, gecikme bildirimi + özet bırakma) varsayılan KAPALI; açılması insan kapısı.

| İzin | Anlam | Rol |
|---|---|---|
| `work.notes.write` | okuyabildiği kayda not, kendi notunu pasifleştirme | hiçbir role verilmedi |
| `work.tasks.assign` | görev oluşturma, kendi atadığını iptal | hiçbir role verilmedi |
| `work.tasks.complete` | kendisine atanan görevi tamamlama | hiçbir role verilmedi |

`AddWorkCollaboration` migration'ı dokuz tenant filtreli tablo ekler. Down guard **52090**: not, bahsetme,
pasifleştirme, görev, bildirim, okundu, özet veya komut denemesi varken geri almayı reddeder (forward fix); boş depo
geri alınır.

## WP-C6: kişisel asistan hafızası pilotu (AI-D15, AI-D22, AI-D30)

- Kayıt: kısa tercih veya not (`preference` | `note`, tek satır, en çok 500 karakter); kayda bağlı değildir, ERP verisinin
  kopyası değildir. Modele yalnız `WorkAssistantMemoryDataBlockV1` biçiminde, sabit başlıklı **veri** (JSON, HTML/ASCII dışı
  karakterler kaçışlı) olarak verilir; talimat değildir, yetki vermez.
- Sahip: kendi kayıtlarını görür, düzeltir (beklenen sürüm), siler (`/api/v1/work/assistant-memory/mine/**`). Görme ve silme
  her zaman açıktır; yazma `ai.panel.use` + açık pilot ister. Silinen kaydın metni kimseye (yönetici dahil) gösterilmez;
  fiziksel imha saklama politikasına bağlıdır (NEEDS-SINAN).
- Yönetici: `work.assistant-memory.oversee` ile bir kişinin hafızasını görür, kaydı veya kişinin bütün hafızasını kapatır /
  yeniden açar (`/oversight/**`). Her görüntüleme önce `AssistantMemoryOversightViews` satırı + `work.assistant-memory.viewed.v1`
  denetim olayı olarak kaydedilir, sonra sunulur; kişi kendi hafızasını kimin ne zaman gördüğünü listeler. Denetim metni kopyalamaz.
- Yazma şu dört koşulun hepsi sağlanmadan kapalıdır (`Work:AssistantMemory`, hepsi varsayılan boş): `PilotEnabled`,
  `RetentionDays` (AI-D30 "ayrı karar" — NEEDS-SINAN), `MaxEntriesPerUser` (parametre — NEEDS-SINAN değer),
  `InformationNoticeVersion` (hukukçu teyitli bilgilendirme metninin onaylı sürümü; metin şimdilik yer tutucu, onay insanda).

| İzin | Anlam | Rol |
|---|---|---|
| `work.assistant-memory.oversee` | başkasının hafızasını görme (denetimli) ve kapatma | hiçbir role verilmedi |

`AddWorkAssistantMemory` migration'ı üç tenant filtreli tablo ekler. Down guard **52210**: hafıza kaydı, kişi kapatması
veya görüntüleme kaydı varken geri almayı reddeder (forward fix); boş depo geri alınır.
