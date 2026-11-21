# Stok / Lojistik / Ürün — dalga 7

Kaynak tabanı `7529fe1e8`; ekip dalı `codex/tren-1-stok_lojistik-w7`.
DONE yalnız kaynak kapsamıdır. Birim, gerçek SQL, HTTP, Vitest, browser ve CI koşumları **NOT_RUN**;
test projesi derlemek bir kabul testi koşumu değildir. Merkezi plan ve üretici pinleri entegrasyon sahibindedir.

## Dilimler

| Dilim | Durum | Commit | Sonuç / kalan |
|---|---|---|---|
| STK-S17 kabul çevrimi yarış düzeltmesi | DONE — kaynak | `eb5d1096ce48f9a08b45cde14fa6390ff9e4d618` | Tek ve toplu kabul, bütün ilgili Sales kilitlerini önce alır. Altı gerçek SQL fact ve host/servis test kaynakları yazıldı. SQL/HTTP/CI NOT_RUN. |
| URT-S4 / URT-S5 Inventory destek kaynağı | DONE — dar owner kaynağı | `14b63931a54742c292b22185e15d1a8cb3cdb67c` | Tek Free pozisyon / sürüm / sahiplik kanıtından sonraki ret ölçümünü taşır; Manufacturing tüketicisi ve host map'i ayrı ekiptedir. |
| STK-S18 | DONE — F017-d kaynak kapsamı | `1b9205deba476a049d69e065981222ab63374ca4` + W6 `44fba107c` | Mevcut kabul ekranında gerçek Inventory owner toplamı; W6 ayırma liste/oluştur/bırak ve entegre Sales bağlantısını tamamlar. Geçmiş çevrim defteri veya tüm batch ekranı bu DONE kapsamına eklenmedi. |
| STK-S8 | PARTIAL — sevk iptal ekranı kaynağı | `20d5abd2495b941de8c2bd5ecd0bb54129930390` | İptal portunun gerçek üreticisi ve depo kantar ayarı kaynağı yok. Kontrol kapalı yayımlanır; kullanılabilir iş akışı sayılmaz. Plan/sevk/teslim/fazla-kayıp onay ekranları da eksik. |
| STK-S19 / ALC-S6 | BLOCKED_DECISION — uygulanmadı | Yeni commit yok | Yeniden açılacak tüketilmiş miktarın güncel talep sınırını aşması halinde kısmi açma / tüm komutu reddetme seçimi bekleniyor. Politika seçilmedi. |

## STK-S17

- Hedef istemciden genel satır tahminiyle üretilmez. Purchasing katılımcısının
  `ResolveReceiptTargetAsync` okuması aynı bağlantı ve transaction'da şirket, sipariş, kabul satırı,
  genel satır ve gözlenen sipariş sürümünü verir. Teknik satır genel sahibine çözülür.
- Tek kabulde Inventory'nin aynı anahtar / aktör / şirket / kabul kimliği replay okuması önce gelir.
  Orijinal fiziksel yük ve Purchasing request hash çitleri korunur. Sonraki sipariş iptali veya yeni
  ayırma kümesi geçmiş kabulün replay'ini yeni iş kurallarıyla engellemez.
- Yeni kabulde: active küme ön okuması → farklı Sales satırları artan sırayla → genel purchase
  satırı → hedefi ve active kümeyi yeniden okuma. Yeni allocation kimliği veya kilitlenmemiş Sales
  satırı görünürse transaction geri alınır; bütün hazırlık yeni transaction'da tekrar yapılır.
  Teknik hazırlık sınırı üçtür. İş callback'i, kabul / etiket stage'i ve beforeCommit yeniden çağrılmaz.
- Bütün batch etiketleri aynı sipariş satırındadır; bütün kilitler ilk etiket stage edilmeden alınır.
  Batch submit / approval replay sahipliği mevcut Purchasing batch store'da kalır. Pending ve reject
  yolları kabul host'unu çağırmaz; takip edilen EF nesnesi olmayan domain snapshot'ı korunur.
- Fiziksel ve gelecek ayırma yazarları aynı Sales serialization resource'u kullanır. Artık kabul
  çevrimi de o resource altında kaldığından iki ayrı cap SELECT'i arasına çevrim commit'i giremez.
  Çözüm RCSI varsayımına dayanmaz; iki SELECT'in kendiliğinden tek snapshot olduğu iddia edilmez.
- Preflight retleri callback öncesinde dönerse servis aynı izin / aktör / şirketi taze ölçer.
  Cancellation aynen geçer. Başarılı beforeCommit yalnız bir kez çağrılır.
- Altı gerçek SQL fact kaynağı: başka purchase'tan gelecek ayırma cap yarışı; fiziksel ayırma cap
  yarışı; büyüyen kümede farklı transaction ve callback tekliği; sürekli büyümede sınırlı ret;
  stage hatasında allocation / etiket / audit / outbox rollback, aynı anahtarla başarı ve sonradan
  sipariş iptali sonrası replay; cancellation ve owner şirket filtresi. Ürün / birim / açık talep
  snapshot'ları ile test yetki çitleri sentetiktir; üretim yetkisi veya uçtan uca kabul sayılmaz.
- `Purchasing.UnitTests` ve `Api.IntegrationTests` kaynak derlemeleri **exit 0, sıfır uyarı / hata**.
  Test koşumları **NOT_RUN**. `git diff --check` exit 0.

## Üretim plan tahsisi kısa düşüm ayrıntısı

- `InventoryManufacturingPlanAllocationShortfallV1`, yalnız owner'ın doğruladığı tek Free pozisyon,
  etiket sahipliği ve sürümünden sonra oluşan `AllocationInsufficientFreeStock` / 409 ret anını taşır.
  İstenen, fiziksel, ayrılmış, kullanılabilir ve eksik kg canonical metindir; mevcut
  `StockAllocation.Unreserved` hesabı kullanılır. Bu veri allocation başarısı veya kalıcı deneme kaydı değildir.
- Pozisyon eksikliği, stale version, sahiplik veya teknik hatada yeni ayrıntı null kalır. Fiziksel hareketin
  mevcut `StockShortfall` alanı değiştirilmedi. Bir önceki satır tahsisiyle birlikte ret gelirse bütün işlem geri alınır.
- Unit ve iki gerçek SQL fact kaynağı yazıldı. Manufacturing kamuya yalnız ürün/etiket/sürüm ve
  istenen/kullanılabilir/eksik miktarı aktarır; host map'i ortak entegrasyon işidir.
- `1b9205deb` checkpoint'inde Inventory.UnitTests / Purchasing.UnitTests / Api.IntegrationTests derlemeleri
  **exit 0, sıfır uyarı / hata**; bu destek kaynağı da aynı checkpoint'tedir. Bütün test koşumları **NOT_RUN**.

## STK-S18 kabul ekranı bilgisi

- Purchasing sahibi kabul satırını kendi genel satırına çözer; bu yönlendirme kimliği JSON'a yayımlanmaz.
  Host mevcut `IInventoryFutureAllocationReadPortV1.GetAllocatedOnPurchaseLineAsync` sahibinden aktif toplamı alır.
  Ek Cari/Inventory grant'i veya rol ataması yapılmadı.
- `FutureSupply` cevabında şirket/sipariş/genel satır/birim kimliği ve aktif miktar doğrulanır. None ancak
  gerçek owner cevabıysa sıfırdır; okunamayan veya bozuk kaynak 503'tür. Ekran katsayı/toplam hesaplamaz.
  Okuma bilgilendirmedir; ortak transaction/snapshot veya kabul garantisi gibi sunulmaz.
- Frontend kesin alan ve kimlik decoder'ı, yükleme/hata/boş durumları ve Türkçe metinler eklendi;
  mevcut kabul cevabındaki bağlanamayan ayırma uyarıları korunur. Unit/HTTP/gerçek SQL fact ve Vitest kaynakları yazıldı.
- F017-d §10 yalnız kabul ekranında ayırma bilgisi ister. Geçmiş çevrim defteri ve batch tüm ekranı ayrıca
  tamamlanmış sayılmaz. Kaynak kapsamı tamam; gerçek kullanıcı kabulü değildir.
- `tsc -b` **exit 0**. `1b9205deb` checkpoint'inde Inventory.UnitTests / Purchasing.UnitTests /
  Api.IntegrationTests derlemeleri **exit 0, sıfır uyarı / hata**.
  Vitest, HTTP, gerçek SQL ve browser koşumları **NOT_RUN**.

## STK-S8 sevk iptal ekranı ve gerçek kaynak boşluğu

- Mevcut seçilmiş transfer kimliği / gözlenen sürümü, UTC anı ve gerekçe ile mevcut cancel-dispatch
  endpoint'i tüketilir. Pencere, tartı veya e-İrsaliye kuralı ekranda hesaplanmaz. Mevcut
  `inventory.stock-transfer.cancel` izni kullanılır; yeni izin/grant/rol yoktur.
- Bekleyen yük ve anahtar sunucunun gerçek oturum kimliği + aktör + şirket + transfer bağlamıyla
  sessionStorage'a kaydedilmeden HTTP gönderilmez. F5, yetki yenileme veya yeniden açmada aynı
  yük/anahtar yalnız kullanıcı isteğiyle tekrar gider. 401/403 shell boundary'ye bildirilir;
  503, ağ kesintisi, bozuk cevap veya bozuk replay header'ı yeni anahtara dönüşmez.
- Stok kaydının silinmesi başarısız olursa da aynı anahtar korunur. Geç eski oturum cevabı yeni
  oturumun ekranını yenilemez. Okuma endpoint'i veya GUID girme formu, sahip onayı yerine kullanılmadı.
- Kritik composition boşluğu: `IInventoryTransferDispatchCancelGatePortV1` ve
  `IInventoryTransferSourceScaleSettingPortV1` gerçek üreticileri/host kayıtları yoktur.
  `InventoryTransferCommandService.CancelDispatchAsync` eksik gate için 503 verir. Bu nedenle
  capability `ImplementationAvailable:false` kalır; yazılmış ekran kullanılabilir sayılmaz.
- Var olan `LogisticsTransferOutgoingDocumentContractsV1` yalnız documentId ile proof okur;
  Draft/CurrentEffective/Cancelled, şirket/transfer/satır/sürüm ve isteğe bağlı e-İrsaliye numarası taşır.
  GİB'e gönderim anı, link anı, çıkış tartısı/araç çıkışı veya ileriye dönük no-exit/no-send garantisi yoktur.
  `ILogisticsInstructionService.CancelDispatchNoteAsync` yükleme talimatının başka yaşam döngüsüdür;
  transfer kapısı yerine bağlanmadı. Numaranın boşluğu GİB'e gönderilmedi kanıtı sayılmadı.
- LOJ-D10/D11 depo kantar ayrımını ve ilk kapanış olayını kilitler; Lojistik EK2 Sınırlar araç çıkışı
  ekran/olay/port biçimini ayrıntılandırmaz. Adapter için sahip kontrollü taze transfer fact okuması ve
  sonraki çıkış/gönderimi de engelleyen sürümlü sahip onayı gereklidir. Koşulsuz başarılı adapter yazılmadı.
- Inventory shell provider **1.9**, toplam 12 declaration; yeni cancel child mevcut parent izinlerine yalnız
  mevcut cancel adını ekler ve false kalır. Bu son provider değişikliğinin .NET derlemesi **NOT_RUN**.
  Önceki checkpoint'in derlemesi bu son değişikliğin kanıtı olarak gösterilmez.
- `tsc -b` **exit 0**; yeni Vitest test kaynakları var. Vitest/browser/HTTP/SQL/CI **NOT_RUN**.

## Merkezi üretim / entegrasyon

- Receipt single / batch mutation 409 metadata'sına mevcut `sales_order_line_busy` eklendi;
  OpenAPI JSON ve sözleşme sabitleri üretilecek.
- Yeni SQL fact tam adları merkezi ilişkisel envantere sıralı eklenmeli; backend test grupları,
  recovery / release ve değişen guard kaynak fingerprint'leri üreticiyle yeniden üretilecek.
- Receipt options `FutureSupply` read modeli OpenAPI JSON/sözleşme üretimine alınmalı. Üretim shortfall
  owner DTO'sunun kamuya hangi alanları açtığı Manufacturing consumer + host entegrasyonunda sabitlenmeli.
- Inventory provider 1.9 / false cancel declaration için ShellCapabilityRegistryAuthorities üreticiyle yeniden
  üretilmeli. Mevcut açık owner kapısını kapatmadan availability true yapılmamalı.
- Yeni migration, THROW, izin, grant, rol, SQL üretim yüzeyi veya bypass allowlist girdisi yok.
- STK-S19 / ALC-S6 karar boşluğu önceki NEEDS-SINAN kaydıyla bekliyor; yeni soru yazılmadı.
