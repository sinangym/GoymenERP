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
