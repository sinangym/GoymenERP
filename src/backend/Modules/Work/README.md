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
- Süre sınıfı WP1'den beri sahibin bildirimi değildir; Work'ün sürümlü onay kural tablosundan gelir (aşağıda).
  Uzunluklar Ayarlar'da versiyonlu `work.approval-duration-hours` ve `work.approval-duration-business-days`
  kayıtlarıdır; kayıt yoksa YOL-D18 karar varsayılanı (4 saat / 2 iş günü) sürüm 0 olarak geçerlidir (WP1 D3).
  İş günü hesabı iş saat diliminde (P7 / SAT-D10, Europe/Istanbul; sabit +3 yok, WP1 D6) Pazartesi–Cuma sayar ve YOL-D22 gereği şirketin Ayarlar'da yıllık, sürümlü kaydettiği resmî
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

## WP1: sürümlü onay kural tablosu, tek kutu (YOL-D17, YOL-D18, ROL-D10, YOL-D23)

- `work.ApprovalRuleVersions` (yalnız ekleme): tür → sahibin onay izni (kayıt anındaki), süre sınıfı, yükseltme izni;
  kaydeden, zaman, gerekçe, korelasyon. Kayıt yoksa karar varsayılanı (sürüm 0): irsaliye, plan üstü sevk, tartım farkı,
  üretim planı saat ölçekli; diğerleri iş günü (`WorkApprovalRuleDefaultsV1`). Yükseltme izni varsayılanda tanımsızdır
  (yükseltme yapılmaz; WP2 okur).
- `GET /api/v1/work/approval-rules`, `PUT /api/v1/work/approval-rules/{kind}`: `settings.work-approval-duration.manage`
  (şirket geneli; yeni izin yok, role atama yok). Komut iskeleti: izin çiti (önce ve kayıttan hemen önce), beklenen sürüm
  (`"0"` = varsayılan), Idempotency-Key ile tekrar, kalıcı ret (sürüm çakışması, değişmeyen kural), denetim, tek kayıt.
- Sahibin onay izni kayıttan sonra değişirse satır `ApproverPermissionChanged` olur ve kutu o türü okunamaz gösterir.
- Dört yerel tür (resmî fatura içe alma, sayım, hurda, sipariş taslağı) `NativeApprovalWorkInboxSourceV1` ile kutu
  kaynağıdır; B-25 kuyruğu ekranda ikinci liste değildir (uç nokta geriye uyum için durur).
- "Onaylayamaz" kümesi sahip modülün kuralıdır: `WorkInboxItemV1.PreparedBy` + `ExcludedDeciders` (ör. gelen fatura
  farkında kaydeden, eşleştiren ve eşleşen siparişlerin onaylayanı, ROL-D10). Kimlik karşılaştırması Guid biçiminden bağımsızdır.
- Kapsamlı onaycı (D11): sahip `AcceptsScopedApprovers` derse tesis/lokasyon/iş merkezi kapsamlı izin sahibi yalnız kapsamındaki
  kayıtları görür; kapsamlı okuma ölçümde döngü kapatmaz. Bugün hiçbir sahip komutu kapsamlı onayı kabul etmiyor.

`AddWorkApprovalRuleVersions` Down guard **52100**: kural sürümü varken geri alma reddedilir (forward fix).

## WP2: tek hatırlatma, yükseltme, sahipsiz onay, "şimdi" bildirimi, tek bildirim deposu (YOL-D17, YOL-D25, YOL-D28, AI-D29, D9, D10)

- **Tek depo (D9):** `work.Notifications` kaldırıldı; satırları kimlikleriyle `work.InboxNotifications`'a taşındı (tür 1→4
  "tahsis serbest", 2→5 "tedarik yeniden açıldı", "özet" sınıfı); pasifleştirme satırları aynı kimliği gösterir. Eski
  `GET /api/v1/work/notifications` sözleşmesi aynı kalır, artık tek depodan okur.
- **Takip döngüsü:** `WorkApprovalFollowUpMaintenanceV1`, `IWorkInboxFollowUpSourceV1` uygulayan kaynaklar için kullanıcısız
  çalışır: yeni bekleyen onayda uygun onaylayıcılara "şimdi" bildirimi (yapan ve sahibin dışladıkları hariç); süre dolunca
  **bir** hatırlatma; bir sınıf süresi daha sonra kural tablosunun yükseltme izni sahiplerine yükseltme (sürüm 0:
  `work.approvals.escalate`; kimsede yoksa yalnız hatırlatma gider, YOL-D28); uygun onaylayıcı yoksa "sahipsiz onay".
  Otomatik onay/ret yoktur. Her adım `work.ApprovalFollowUps`'ta döngü+adım başına tek satırdır.
- **D10:** sahip artık listelemediği ölçülmüş döngünün kararını kendi kaydından okur (`ReadDecisionAsync`); karar zamanı
  `ApprovalInboxEvents` tür 5 (`Decided`) olarak sahibin zamanıyla yazılır. Ölçüm artık kutu açılmasını beklemez.
- **Özet saatleri (YOL-D25):** Ayarlar anahtarları `work.digest-time-morning|midday|evening` (gece yarısından dakika; sürüm 0 =
  09:00/13:00/17:00), yalnız iş günü (hafta sonu + kayıtlı tatil/arife düşülür), iş saat dilimi (D6).
- **Bayrak:** döngü yalnız `Work:CollaborationScheduler:Enabled` ve `Work:CollaborationScheduler:ApprovalFollowUpEnabled` ikisi
  de true iken çalışır (varsayılan kapalı; açmak insan kapısı).
- **Görünüm:** `GET /api/v1/work/approval-follow-ups?view=escalated|orphaned` (`work.approvals.escalate`, şirket geneli);
  ekran `/onay-takibi`. Bugün yalnız Lojistik irsaliye kaynağı takip kaynağıdır; diğer kaynaklar eklenene kadar takip edilmez.
- `Wp2SingleNotificationStoreAndApprovalFollowUp`: Up kopya eksikse **52111** ile eski depoyu düşürmeden durur; Down guard
  **52110** (taşınmış/yeni bildirim, takip satırı veya karar olayı varken reddeder, forward fix). Ayarlar
  `Wp2WorkDigestTimes` Down guard **52112**.
## W1-C: kayda bağlı not, bahsetme, görev ve iki sınıflı bildirim (AI-D20, AI-D23, AI-D29, YOL-D19)

Yapay zekâ yoktur. Kayıt türleri: satış siparişi taslağı, alış siparişi, stok etiketi, üretim planı, cari (WP9 ile
yedi tür daha; aşağıya bakın). Kaydın
varlığı ve okuma yetkisi sahip modülün kendi yetkili okumasından gelir (`IWorkRecordAccessPortV1`, host adaptörü
`WorkOwnerRecordAccessPortV1`); Work başka modül tablosunu okumaz. Bahsedilen kişinin okuma yetkisi Identity'nin
`IUserPermissionHolderReadPortV1` portuyla (sahibin okuma izni, şirket genelinde, şimdi) ölçülür; okuyamayan kişiye
bahsetme bütün notu 422 `work_mention_refused` ile reddeder ve kişinin var olup olmadığını söylemez.

- Not düzenlenmez; düzeltme `correctsNoteId` taşıyan yeni nottur. Silinmez; yazarı pasifleştirir (ayrı satır).
  Saklama YOL-D19/YOL-D26: bağlı kayıt kadar, en az 10 yıl; kayıt imha edilince not da imha edilir. İmha yolu kodda
  vardır ama kayıt imha politikası etkinleşene kadar kapalıdır (WP9 bölümü).
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

## WP9: kayıt türleri, not okundu bilgisi, D7/D8, Work shell yetenekleri, YOL-D26

- **Kayıt türleri (6–12):** `sales-invoice` (Satış `ISalesInvoiceService`; okuma `sales.invoices.read` + fiyat okuma),
  `loading-instruction` (Lojistik `ILogisticsInstructionService`; irsaliyenin notu talimatındadır — Lojistik irsaliyeyi
  yalnız talimat üzerinden okur), `purchase-incoming-invoice` (Satınalma `IPurchaseIncomingInvoiceWorkflowV1`;
  `purchasing.invoices.read` + alış fiyatı okuma), `manufacturing-order` (`ManufacturingCommandService.GetOrderAsync`),
  `finance-manual-receipt` / `finance-manual-bank-payment` (Finans `DetailAuthorizedAsync`), `sales-quote`
  (`ISalesQuoteService`). Mal kabul ayrı tür değildir: Satınalma onu yalnız (sipariş, kabul) çiftiyle okur; notu alış
  siparişindedir (NEEDS-SINAN/Satınalma: kabul kimliğiyle okuma portu gelirse tür eklenir). Adlandırılmış kişinin okuma
  hakkı: sahibin okuma izinlerinin **hepsi** şirket genelinde, şimdi.
- **Okundu bilgisi:** `POST /api/v1/work/notes/{noteId}/read` (anahtar yok; (not, okuyan) başına ilk okuma kalır;
  yazar kendi notunu işaretleyemez → 409 `work_note_own_read`). Not görünümü `readByMeAtUtc` ve `reads` (okuyan, ad,
  zaman) taşır; kaydı okuyabilen herkes aynı listeyi görür.
- **D7:** `GET /api/v1/work/people` yalnız `work.tasks.assign` veya `work.notes.write` (şirket geneli, şimdi) sahibine
  cevap verir, diğerine kodsuz 403; satır yalnız `userId` + `displayName`.
- **D8:** stok etiketi varlığı Stok'un yetkili okuma portu `IInventoryLabelDetailsV1` ile (etiket + bakiye okuma);
  Work host artık Stok store'unu kullanmaz.
- **Shell:** `WorkShellCapabilityProvider` (WORK 1.0): `work.tasks.mine` (`/gorevlerim`, `work.tasks.complete`) ve
  kontrolü `work.tasks.assign`. Kişisel bildirim kutusu ve kayıt not paneli bilerek ilan edilmez (alıcıya özel /
  sahibin okumasının arkasında; registry izinsiz "kullanılabilir" yeteneği reddeder).
- **YOL-D26 imha yolu (KAPALI):** `IWorkNoteDisposalV1` kayda bağlı not ailesini (not, bahsetme, pasifleştirme,
  okundu, bahsetme bildirimi ve okundusu/özet bağı) tek işlemde siler ve sayılarla sistem audit'i yazar; görevlere
  dokunmaz. İki kapı: `Work:NoteDisposal:Enabled=true` (yok → kapalı) **ve** sahip modülün "kayıt imha edildi" cevabı
  (`IWorkRecordDisposalPortV1`; host bugün her kayıt için `Unavailable`). Bugün not silinmez, pasifleşir; açılış kayıt
  imha politikası kararıyla insan kapısıdır. Silme yalnız `WorkDbContext.OpenNoteDisposalWindow` içinde ve yalnız not
  ailesine açıktır.

`WidenWorkRecordTypesAndNoteReads` migration'ı üç kontrol kısıtını 1–12'ye genişletir ve `RecordNoteReads` tablosunu
ekler. Down guard **52170**: okundu satırı ya da 6–12 türüne bağlı not/görev/bildirim varken geri almayı reddeder
(forward fix).
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
