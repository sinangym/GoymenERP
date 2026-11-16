# tren-1 W8 üretim

| Dilim | Durum | Commit | Sonuç ve kalan |
|---|---|---|---|
| URT-S5 AT044 | DONE — kaynak/derleme; entegrasyon bekliyor | `2120b758e7f41fb5aeb7c75bcf1d2327e72b3937` | URT-D37/URT-D111: Slitting ilk kayıt, önizleme ve revizyonunda HamKg etiketin tüm pozisyonlarındaki güncel fiziksel toplamını aşamaz. Taslakta serbest miktar ve tek pozisyon şartı uygulanmaz; onaydaki Inventory ayırma kontrolü korunur. |
| URT-SC33 manuel sıra | DONE — kaynak/derleme; entegrasyon bekliyor | `eb09811b3`, `4a0ab4db4`, `47ed3462b`, `d8a03e90d`, `59f30eb7e` | URT-D97/D106/D110: mevcut elle sıra termin/puan değişiminde korunur; yeni üye ilk daha geç otomatik anahtarın önüne girer. B20→A10 ve C15 sonucu C→B→A. Kalıcı snapshot, ayrı yetkili komut, gerçek kuyruk/termin tüketimi, UI ve test kaynakları hazır. |
| URT-S4 | DONE — kaynak; runtime ayrı | W7 `921eb9300` ve W8 `2120b758e` | Gerçek owner availability, taze ret snapshot'ı, iptal edilmiş emir geçmişi, etiket seçimi/revizyon/yeniden onay ekranı ve atomik allocation nesli kaynaktadır. W8 AT044 güncel fiziksel toplam sınırını tamamlar. Aynı iptal edilmiş emrin iki yeniden onay yarışı, stale Free geri alma ve yabancı şirket ayrıntı sızdırmama SQL/HTTP kabul kaynakları W7'de yazılmıştır. |
| URT-S8 | PARTIAL — maliyet generation host entegrasyonu sürüyor | `446fe0550`, `226ca89bf`, `11f05270e`, `5c08db493` | URT-D32/D36/D56/D77/D81: gerekçeli versioned yeniden açma, Accounting taze dönem çiti, eski kapanış başlığını ve tüm kapanış kararlarını saklayan append-only model/store, EF migration/Down51951, ayrı kayıt izni, endpoint, strict UI ve test kaynakları hazır ve derlenmiştir. Gerçek Inventory maliyet nesli ters kaydı/yeni kapanış kaydı, host ve maliyet geçmişi çıktısı bağlanmadan kullanıcı capability açılmaz. |
| URT-SC32 / URT-SC36 | PARTIAL | mevcut hesaplayıcılar; owner entegrasyonu sürüyor | Hareket anındaki TL/USD damgalı sarf/iade maliyeti ve issue bağı, gerçek Settings hurda-maliyet politikası ve kur öncelik kaynağı bağlanmalıdır. Geçmiş NotCalculated maliyet güncel ortalamadan veya kurdan doldurulmaz; iade için FIFO/ortalama seçilmez. |
| ALC-S6 / STK-S19 API44 batch desteği | PARTIAL — atanmış host/API/UI kapsamı | `692602dfd`, `81a0bac0f`, `aa36031ca`, `05814e6d0` | Tek expected version/gerekçe ve final order version; genel satır neti sahibin Supplies kaydından gelir. Tüm Sales kilitleri ardından tüm Purchase kilitleri, tam küme yeniden okuması; tüm satırların salt okunur ortak stok bütçesi ön kontrolü; tüm tersler sonra yeni kabuller. Gerçek batch owner factory/merkezi DI ve final birleşik derleme beklenir. |

## Doğrulama

- `2120b758e7f41fb5aeb7c75bcf1d2327e72b3937`: Manufacturing.UnitTests derlemesi **exit 0, 0 uyarı / 0 hata**. Tek MSBuild node, shared compilation ve OpenAPI build üretimi kapalı.
- AT044 için unit, iki gerçek SQL HTTP fact ve frontend ret/düzeltme test kaynakları yazıldı. **Test runtime, gerçek SQL, HTTP runtime, Vitest ve browser NOT_RUN.** Yeni SQL HTTP kaynakları sonraki S8 checkpoint'inde derlendi; frontend TSC henüz NOT_RUN.
- AT044 migration, THROW, izin, grant, rol veya pin değişikliği içermez.
- W7 merkezi adayına bu commit dahil değildir; sonraki dalgada lider entegrasyonu beklenir. Bu rapor yalnız üretim ekip dalındadır.

## Kalıcı manuel kuyruk

- `eb09811b3`: domain sırası, immutable ve şirket filtreli snapshot modeli, gerçek store/DI, komut/HTTP endpoint ve ekran bağlantısı. Kuyruk ve termin önerisi aynı gerçekleşen sırayı tüketir. Taslak hedefin okuma önizlemi snapshot'a yazılmaz.
- `4a0ab4db4`: bilinmeyen sonucun tam istek/anahtar/gerekçesi session storage'dan yeniden yüklenir; salt okunur dondurulmuş alanlar görünür kalır.
- `47ed3462b`: eşzamanlı aynı revision yazmasında tek snapshot/audit ve kaybedenin geri alınması için SQL kaynak; SQL'in zaten geri aldığı deadlock işleminde cleanup özgün geçici reddi örtmez.
- `d8a03e90de5b03663deca192f93706743f7dcacf`: EF üretimi `20261116210842_AddManufacturingManualQueues`, Designer ve snapshot birlikte. Up yalnız DDL; Down ilk işlemde iki history tablosu boş değilse **THROW 51946** ile reddeder. Boş veri geri alınabilir. Guard'ın model/unit ve gerçek SQL kabul kaynakları eklendi.
- `59f30eb7ec53022f47289d71ac0a8a9abece0769`: scope değişiminde kaldırılmış eski ekranın geç yanıtı bekleyen isteği temizlemez veya başka scope'u yenilemez; UI kabul kaynağı eklendi.

`POST /api/v1/manufacturing/plans/{planId}/work-center-queue/reorder` hem `manufacturing.plans.read` hem
`manufacturing.plans.reorder` ister. Request: `expectedVersion`, `expectedQueueRevision`, `movingPlanId`, nullable
`beforePlanId` (kuyruk sonu), 1–256 karakter `reason`; idempotency anahtarı header'dadır. Aynı kuyruktaki gerçek üyeler
taşınır. Plan ve bağımsız kuyruk revision çiti, son yetki çiti, audit ve değişmez ADR-011 cevap kaydı birlikte uygulanır.
Yeni izin açılmaz, grant/rol verilmez; shell provider **1.12**, mevcut reorder capability gerçek endpoint ve ekrana bağlıdır.

Manuel komut kendi **Serializable** işlemini açar. Diğer onay/iptal/kapama/revizyon/yeniden onay gibi kuyruk üyeliğini
etkileyen store yazmaları, mevcut manual snapshot'ı aynı Manufacturing owner veya mevcut Inventory host işlemi içinde
yeniler. Üyelik kaydı hata verirse iş yazması da commit edilmez. Termin/puan değişimi mevcut üyelerin sırasını değiştirmez;
ayrılan üyeler sonraki snapshot'tan çıkar, önceki history korunur. Yeni DB context construction veya tenant bypass eklenmez.

## W8 doğrulama ve merkezi entegrasyon

- `47ed3462b`: API startup derlemesi **exit 0, 0 uyarı / 0 hata**, OpenAPI build üretimi kapalı; EF üretimi **exit 0**.
- `5c08db493` kaynak checkpoint'inde queue dahil Manufacturing.UnitTests ve Api.IntegrationTests **exit 0, 0 uyarı / 0 hata**. Son UI değişiklikleri için merkezi TSC **NOT_RUN**.
- Manuel kuyruk için beş domain, üç application yetki/replay, iki persistence/migration unit fact; beş gerçek SQL fact ve bir HTTP SQL fact; üç API decoder/storage ve beş UI test kaynağı yazıldı. **Test runtime, gerçek SQL/HTTP, Vitest, browser ve CI NOT_RUN**.
- Migration veritabanına uygulanmadı. `51946`, diğer ekiplerle koordineli ayrıldı; üretim kaynak taramasında çakışma yoktu.
- W7 tamamlanmış `df1b7055f9397cd5206b57df439986d1e8344b4e` treni ekip dalına temiz `--no-ff` merge edildi. W8 commitleri henüz merkezi trende değildir; lider entegrasyonu beklenir.
- Merkezi üretilecekler: shell registry provider 1.12 pin'i; yeni POST ve queue response `queueRevision`/`hasManualOrder` OpenAPI sözleşmesi; recovery/release migration ve store envanteri; DbContext/migration fingerprint ve **ReviewedMigrationMutationSources Sql=1 (Down 51946)**; yeni SQL fact'lerin sıralı ilişkisel envanter ve grup pinleri. Bu merkezi dosyalar ekip dalında değiştirilmedi.
- Manufacturing dışındaki W5 shell-count hizalamaları yalnız lider için raporlandı; Logistics/Purchasing test dosyalarına dokunulmadı.

## Yeniden açma kaynak checkpoint'i

- `446fe0550`: gerekçeli Closed→Open domain geçişi; önceki kapanış ve yeniden açan/gerekçe geçmişi; her kapanışın bağımsız karar anahtarı; şirket filtreli immutable history; aynı owner transaction zorunluluğu; Accounting close-business-day okuması ve son taze yetki/dönem çiti. URT-D81, eski artı kg kapanışlarının da önce kontrollü yeniden açılmasını seçmiştir; eski URT-D52 açık notu yeni bir engel sayılmaz.
- `226ca89bf`: ilk API derlemesinde yakalanan yerel değişken adı çakışması düzeltildi. Bu head'de **API build exit 0, 0 uyarı / 0 hata**; yalnız mevcut project reference için Unit restore yapıldı, paket sürümü değişmedi.
- EF ile `20261116214107_AddManufacturingOrderReopenings` üretildi. Up yalnız DDL; Down ilk SQL işleminde history veya aynı emir için çoklu kapanış varsa **THROW 51951** ile reddeder. Designer ve snapshot üreticiden gelir; migration veritabanına uygulanmadı.
- Yeni izin `manufacturing.orders.reopen` yalnız merkez kayıt + company-wide kapsam manifestine eklendi; grant, rol ve Critical sınıfı eklenmedi. Shell provider **1.13**, **20** declaration; gerçek Inventory participant bağlanana kadar yalnız reopen kontrolü staged'dır. Önceki 16/all-available ve reorder-staged integration beklentileri gerçek kaynağa hizalandı.
- `11f05270e` migration ve kabul kaynaklarını içerir; `5c08db493` SQL test kaynağındaki eksik process namespace'ini tamamlar. Yeni domain/application/persistence, store transaction rollback, ikinci kapanış geçmişi, Down koruma ve frontend unknown/reload test kaynakları yazıldı. **Manufacturing.UnitTests ve Api.IntegrationTests build exit 0, 0 uyarı / 0 hata**. Runtime, SQL/HTTP, Vitest, browser ve CI **NOT_RUN**.
- Merkezi shell/OpenAPI/recovery/guard/relational/grup pinleri liderdedir; bu ekip onları değiştirmedi. `ReviewedMigrationMutationSources` için yeni migration'ın **Sql=1**, gerekçe **Down 51951 veri anlamı/history koruması** olarak kayda alınmalıdır.

## Maliyet kaynağı ve açık sarf bağı

- `d1e5cf2a9`: kullanılmayan girdi iadesine isteğe bağlı açık `SourceIssueMovementId` eklendi. Alan yokken eski canonical request/hash korunur; boş kimlik reddedilir, açık kimlik istek kimliğinin parçasıdır. Ekran yalnız seçili ana etiketin gerçek sarf hareketlerini gösterir ve belirsiz sonuçta aynı içerik/anahtarı korur. Inventory owner alanı henüz bu checkpoint'te olmadığından host açık bağı sessizce atmak yerine 503 döndürür; gerçek owner map bu geçici çitin yerini almalıdır.
- `fc0161ccf`: Manufacturing-owned gerçek veri birleştirme kaynağı; kendi emrinin issue/return/output olgu kimliklerini ve kg'larını hareket anı damgalarıyla tam eşler. Etiket kimliği ile output fact kimliği ayrıdır. İade özgün sarf bağı yoksa hesaplayıcıda okunamaz kalır; FIFO veya güncel ortalama seçilmez.
- Kaynak mevcut tek maliyet hesaplayıcılarını tüketir. Hurda tartımının Europe/Istanbul ayı için gerçek versioned `MonthlyScrapPrice`, ayrı gerçek Settings politika sürümü ve output'un domain net kg'sı girdidir. OrderVersion, owner hareket sürümü, politika sürümü, fiyat kimlik/sürüm/değerleri ve deterministik girdi fingerprint'i provenance olarak taşınır.
- Bu source gerçek Inventory/Settings host adaptörleri bağlanmadan DI'a başarı kaynağı olarak eklenmedi. Sarf/iade/çıktı maliyet generation ve kapanış/yeniden açma ters kayıtları bitmeden URT-SC32, URT-SC36 ve URT-S8 **PARTIAL** kalır. Mevcut kapanışta tüm mamulün hâlâ stokta olması şartı yoktur; yeni bir tam-stok şartı seçilmedi.
- Kimlik/versiyon/kg uyuşmazlığı, ay sınırı/fiyat sürümü, gerçek hesaplayıcı toplamı ve eksik eski damganın okunamaz kalması için unit kaynakları yazıldı. Son kaynakların derlemesi, test runtime, SQL, HTTP runtime, Vitest ve browser **NOT_RUN**.

## API44 birlikte düzeltme desteği

- `692602dfd`: public sürümlü batch sözleşmesi, strict giriş doğrulama, aynı permission/captured authority çitli servis ve HTTP endpoint. Kullanıcıca düzeltilebilir **422 NOT_STORED** olarak tekli ve batch helper'da hizalandı; terminal iş 409'u, transient/concurrent/busy/503/cancel hallerinden ayrılır. `PurchasingCommands.IsKnown` batch komutunu da kaydeder.
- `81a0bac0f` ve `aa36031ca`: bir genel satırın tüm yeni kabul alış miktarı tek ortak replacement bütçesidir; her satır için tekrar kullanılan ayrı bütçe değildir. Inventory ön kontrolü hiçbir entity track etmez, Stage/savepoint/SaveChanges/audit üretmez. Tüm owner okumaları tamamlanınca ortak talep tutuşu, genel satır tedariki ve tam stok pozisyonu gölgeleriyle mevcut exact-ratio kernel değerlendirilir. Sonraki geçici owner hatası önceki terminal iş reddini bastırır.
- Host tek tx içinde önce Purchasing bütün net batch'ini flush eder; aynı tx supply okuması son sürüm ve gerçek miktarla eşleşir. Tüm ters hareketler tüm yeni kabullerden önce yazılır. Yeniden tüketim önceliği aynı genel satırın bütün yeniden açılmış allocation kimlikleridir. Çocuk idempotency anahtarları adjustment kimliğiyle ayrılır; replay bugünkü mutable targets/versions okunmadan değişmez owner cevaplarından kurulur.
- Strict decoder ve mevcut kabul çalışma alanına bağlı batch ekranı; ayrı alış miktarı/ölçülmüş kg, ortak gerekçe, satır bazlı yeni kabul; loading/read states ve unknown durumda tam body/key kilidi kaynakta vardır. Ekran net miktar, fiyat veya maliyet hesaplamaz; sahibin Supplies olgularını gösterir.
- Unit ve sentetik kilit/HTTP/UI kaynaklarına ek olarak SQL'de yazmasız ön kontrol, sonraki owner unavailable önceliği ve gerçek birleşik host commit/replay/ilk Inventory yazısından sonraki taze yetki reddiyle tüm owner rollback + aynı anahtar retry fact kaynakları yazıldı. Bu fact'lerin tamamı **NOT_RUN**; gerçek factory/DI bağlanması ve birleşik kaynak derlemesi beklenir. Yeni schema, migration, THROW, izin/grant veya merkezi pin değişikliği yoktur.

## Son kaynak derleme checkpoint'i

- `5c9d1381d`: **API, Purchasing.UnitTests, Manufacturing.UnitTests ve Api.IntegrationTests derlemeleri exit 0, 0 uyarı / 0 hata**. API'de OpenAPI build üretimi kapalı, tek MSBuild node ve shared compilation kapalıdır. Başlangıçtaki Purchasing test assets eksikliği yalnız mevcut proje restore edilerek çözüldü; paket veya sürüm değiştirilmedi.
- Aynı checkpoint gerçek `ManufacturingSettingsScrapCostPolicyReader` adaptörünü içerir: captured actor/company/member çiti, owner CompanyId ve Version>=0 doğrulaması, gerçek VersionText; owner hatasında consumer varsayılanı yoktur. Gerçek Settings provider altyapısı ve Program kaydı merkezi entegrasyondadır.
- İlk gerçek TypeScript derlemesi üç test assertion tip hatası gösterdi. `16f53bd4c` assertion'ları projenin kurulu tipleriyle uyumlu hale getirir; bu checkpoint'te **TypeScript `tsc -b` exit 0**. Eşleşen kilit dosyasının kurulu bağımlılıkları kullanıldı; yeni paket kurulmadı.
- Yeni unit/SQL/HTTP/UI test kaynakları derlenmiştir. **Test runtime, gerçek SQL/HTTP, Vitest, browser ve CI NOT_RUN**. Batch owner implementation/factory ve maliyet owner/generation bağı bu derleme kanıtıyla tamamlanmış sayılmaz.

## Maliyet nesli ve gerçek kapanış işlem bağı

- `574fd4982` — **URT-SC36 kaynak düzeltmesi**: URT-D75 (3) gereği fire maliyete dahilken hurda etiketi tartıldığı ayın fiyatıyla değer taşır. Önceki dağıtımda eksik hurda payı yuvarlama farkıyla sağlam ürüne geri yükleniyordu. Onaylı 40.000 TL örneği 37.500 ürün + 2.000 artık + 500 hurda olarak korunur; ayrı gider yolu değişmez. Golden ve mevcut yuvarlama test kaynakları hizalandı; test koşulmadı.
- `8271e8696` — **URT-S8 PARTIAL; gerçek owner entegrasyonu bekliyor**: `CostGenerations` şirket filtreli ve append-only nesil/revision geçmişidir. Fiziksel kapanış başına kimlik; okunamayan/bekleyen sonuçta tutar yok, sonraki gerçek tanıma aynı kimlikte yeni revision. Yeniden açma Inventory sahibi exact id/sürümünü tersler; eski sonuç silinmez. İkinci kapanış yeni nesildir; ayırma bırakma anahtarı yeni kapalı emir sürümüyle ayrılır, ilk kapanışın eski anahtarı korunur.
- `20261116232039_AddManufacturingCostGenerations` **dotnet ef ile üretildi, exit 0**. Up yalnız yeni tablo/index/FK/check DDL'idir. Down ilk işlemde **THROW 51960** ile herhangi bir geçmiş varken reddeder; boş geçmiş geri alınabilir. DB'ye uygulanmadı.
- Normal kapamanın aynı transaction'ında maliyet kaydı; gecikmiş maliyetin aynı kapanış için tanınması; yeniden açmada ters kayıt + Manufacturing reopen birlikte commit/rollback olur. Yeni izin yoktur. URT-D58 (EK4:93) ve URT-D67 (Maliyet ve Risk:40) gereği gecikmiş kaynak tekrar okunur; bu komut mevcut kapamanın teknik maliyet etkisini tamamladığı için mevcut `manufacturing.orders.close` yeniden doğrulanır. Mali cevap/geçmiş için ayrıca `manufacturing.reports.read` taze çiti vardır. Bağımsız SMM fiş hazırlama/onayı bu komutun işi değildir; muhasebe yetkilisinin açık dönem/tarih seçimi ve farklı kişi onayı Accounting sahibindedir.
- Yeni geçmiş ve tanıma endpoint'leri, kaydedilmiş ondalık metinleri gösteren strict decoder, kullanıcı gerekçesi ve belirsiz sonuçta session kapsamında aynı body/key koruması yazıldı. UI toplam veya kur hesaplamaz. Yeniden açma null owner cevabında fail-closed kalır.
- API, Manufacturing.UnitTests ve Api.IntegrationTests derlemeleri **exit 0, 0 uyarı / 0 hata**. TypeScript **exit 0** alındı; sonraki dar signed reversal decoder hizalaması son birleşik TSC ile yeniden doğrulanacaktır. Dört SQL/HTTP fact, model/append-only/guard, host contract, yetki/replay ve decoder test kaynakları yazıldı. **Test runtime, gerçek SQL/HTTP, Vitest, browser ve CI NOT_RUN**.
- Gerçek Inventory read/generation factory ve merkezi Program DI kaydı birleşik başta bağlanmadan PARTIAL kaldırılmaz. USD için gerçek issue stamp ve kullanıcıca seçilmiş hurda tartım günü TCMB döviz satış adaptörü kaynakta vardır; USD sonuç gösterimi ve isteğe bağlı özgün sarf hareketinin gerçek host forwarding'i takip kaynak işidir. USD kaynağı TL maliyetinin ön şartı değildir.
