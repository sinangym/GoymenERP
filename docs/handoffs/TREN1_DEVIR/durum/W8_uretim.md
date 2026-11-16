# tren-1 W8 üretim

| Dilim | Durum | Commit | Sonuç ve kalan |
|---|---|---|---|
| URT-S5 AT044 | DONE — kaynak/derleme; entegrasyon bekliyor | `2120b758e7f41fb5aeb7c75bcf1d2327e72b3937` | URT-D37/URT-D111: Slitting ilk kayıt, önizleme ve revizyonunda HamKg etiketin tüm pozisyonlarındaki güncel fiziksel toplamını aşamaz. Taslakta serbest miktar ve tek pozisyon şartı uygulanmaz; onaydaki Inventory ayırma kontrolü korunur. |
| URT-SC33 manuel sıra | DONE — kaynak/derleme; entegrasyon bekliyor | `eb09811b3`, `4a0ab4db4`, `47ed3462b`, `d8a03e90d`, `59f30eb7e` | URT-D97/D106/D110: mevcut elle sıra termin/puan değişiminde korunur; yeni üye ilk daha geç otomatik anahtarın önüne girer. B20→A10 ve C15 sonucu C→B→A. Kalıcı snapshot, ayrı yetkili komut, gerçek kuyruk/termin tüketimi, UI ve test kaynakları hazır. |
| URT-S4 | DONE — kaynak; runtime ayrı | W7 `921eb9300` ve W8 `2120b758e` | Gerçek owner availability, taze ret snapshot'ı, iptal edilmiş emir geçmişi, etiket seçimi/revizyon/yeniden onay ekranı ve atomik allocation nesli kaynaktadır. W8 AT044 güncel fiziksel toplam sınırını tamamlar. Aynı iptal edilmiş emrin iki yeniden onay yarışı, stale Free geri alma ve yabancı şirket ayrıntı sızdırmama SQL/HTTP kabul kaynakları W7'de yazılmıştır. |
| URT-S8 | PARTIAL — maliyet generation host entegrasyonu sürüyor | `446fe0550`, `226ca89bf`, `11f05270e`, `5c08db493` | URT-D32/D36/D56/D77/D81: gerekçeli versioned yeniden açma, Accounting taze dönem çiti, eski kapanış başlığını ve tüm kapanış kararlarını saklayan append-only model/store, EF migration/Down51951, ayrı kayıt izni, endpoint, strict UI ve test kaynakları hazır ve derlenmiştir. Gerçek Inventory maliyet nesli ters kaydı/yeni kapanış kaydı, host ve maliyet geçmişi çıktısı bağlanmadan kullanıcı capability açılmaz. |
| URT-SC32 / URT-SC36 | PARTIAL | mevcut hesaplayıcılar; owner entegrasyonu sürüyor | Hareket anındaki TL/USD damgalı sarf/iade maliyeti ve issue bağı, gerçek Settings hurda-maliyet politikası ve kur öncelik kaynağı bağlanmalıdır. Geçmiş NotCalculated maliyet güncel ortalamadan veya kurdan doldurulmaz; iade için FIFO/ortalama seçilmez. |

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
