# Stok / Lojistik / Ürün — dalga 6

Kaynak tabanı `f3224bcee`; ekip dalı `codex/tren-1-stok_lojistik-w6`.
Bu rapordaki DONE kaynak kapsamını anlatır. Birim, SQL, HTTP, Vitest, browser, barkod tarayıcı ve CI
koşumları **NOT_RUN**; üretim veya gerçek kullanıcı kabulü yapılmadı. KALAN_PLAN ve merkezi pinler entegrasyon sahibinde.

## Dilimler

| Dilim | Durum | Commit / mevcut kaynak | Sonuç ve açık kalan |
|---|---|---|---|
| STK-S1 | DONE — kaynakta doğrulandı | Mevcut `LoadingShareRow.RecordedBy`, `LogisticsSalesOrderDeliveryReadPort`, `AddLoadingShareRecordedBy` | `ShippedBy` benzersiz, Ordinal sıralı sevk edenlerden geliyor; eksik tarihsel aktör null kalıyor. Yeni veya mükerrer kod yazılmadı. Gerçek SQL NOT_RUN. |
| STK-S17 talep bırakma önkoşulu | DONE | `71bff4ffa`, `8af98eb70` | Talep azaltmada gereken gelecek miktar, en uzak alış termininden başlayarak kısmi bırakılıyor; açık seçim sürümleri doğrulanıyor. Fiziksel bırakma gerekiyorsa 409; okunamayan kaynak varsa terminal ret yerine 503. Boş tahsis kümesinde çevrim kaynağı gerekmeyen no-op korunuyor. Host transaction geri alma SQL fact'i ve hesaplayıcı testleri yazıldı. |
| STK-S17 kullanıcı uçları | DONE — entegre güvenlik kapısıyla | `7a13d2f12` | Oluşturma / bırakma / talep satırı listesi, şirket filtresi ve taze yetki çiti. Metadata aktörü, izni ve rolleri sunucudan geliyor; başarı ve ret replay'i yeniden yetkilendiriliyor. İki izin yalnız kayıtlı. **SAT-S8 + kabulde çevirme + Alış iptal koruması aynı entegre head'de bulunmalı.** |
| STK-S26 | DONE — kaynak | `d06ae6181` | Mevcut etiket kimliğinden logosuz, Noto Sans gömülü PDF; mevcut PDFsharp barkod motoru; mevcut Documents deposuna kayıt başarılı olmadan dosya dönmez. Etiket ayrıntısında oluştur / aynı anahtarla yeniden dene / indir. PDF, HTTP ve frontend test kaynakları var. Görsel, tarayıcı ve gerçek belge deposu kabulü NOT_RUN. |
| STK-S8 | PARTIAL | `3fe9809a0`, `207bdab47` | Şirket filtreli son 25 transfer, gerçek Organization depo adları, durumlar, kayıttaki sevk zamanı ve etiket bağlantılı detay okuması eklendi. Stok pozisyonlarından giriş var. **Plan / sevk / sevk iptali / teslim alma / fazla-kayıp onay mutation ekranları tamamlanmadı.** Mevcut servis ve endpoint boşlukları iş kuralı kararı gibi gösterilmedi. |
| STK-S18 | PARTIAL | `44fba107c` | Talep satırı ayırma listesi, Settings birim adları, yalnız onaylı Purchasing genel satırlarından kaynak seçimi, Cari sahibinden tedarikçi adları, oluşturma ve gerekçeli tam bırakma ekranı. Bilinmeyen sonuç aynı anahtar ve yükle tekrar gider. **Sales satır Link'i ortak entegrasyonda; mal kabul ekranındaki çevrim/ayırma gösterimi henüz eklenmedi.** |
| STK-SC36-c | PARTIAL | `ead16d046` | SAT-D416/D418: yalnız Active ürüne yeni ambalaj standardı; eski self-approval seçeneği maker≠checker kuralını aşamıyor. **TK22 müşteri istisnası için Cari Customer rolü kanıtı mevcut display portunda yok; yeni Cari-read izni eklenmedi.** |
| STK-S3 | PARTIAL — W5 kaynağı | W5 `48707ad49`; URT-S18 sahibi `52d6cca76` | Bu dalgada Lojistik kalıcılığı / migration / fason risk kapısı / sahiplik consumer'ı bağlanmadı. Üretim sahibinin tamamlanma + müşteri + çıktı etiketi cevabı diğer ekipte hazır. Sırf port hazır diye fason teslim açılmadı. |
| STK-SC36-a | PARTIAL — W5 kaynağı | W5 `989287366` | Hesaplayıcılar henüz kalıcı maliyet akışına bağlanmadı. EK24 geç fatura farkı / satır iskontosu ve SAT-D419 Finans eşleme tüketimi W7 entegrasyon işi. |
| STK-SC36-b | PARTIAL — W5 kaynağı | W5 `44d6ebc7e` | KLN-D107/D108 önceki kaynak korunuyor; ilk mal kabul kur portu hâlâ kuru. EK24 otomatik kabul kuru ve kg ağırlıklı birleşen etiket kuru bağlanmadı. |
| STK-S11 | BLOCKED_SOURCE | Mevcut Product calculator ve Settings `ISettingsUnitIdentityCatalogV1` | Gerçek Settings birim kimliği bugün yalnız kg. `Code` görüntü etiketidir; ton/metre/adet semantiği koddan türetilmedi. Owner'ın kaynaklı typed birim semantiği gereklidir; kaynak yokken mevcut fail-closed korunuyor. |

W7'ye bırakılan, bu dalgada başlamayan işler: **STK-S13, STK-S19, STK-S21, STK-S29**.
Yeni tablo veya migration açılmadı; snapshot ve THROW numarası değişmedi. Parent entegrasyon tabanını
yeniledikten sonra bu dilimlere devam edilmesi istendi.

- S13: BS-10 boya ürünü üyelik tablosu henüz yok. KIM-S18 `ISettingsReferenceListReaderV1` / `PaintColor` kaynağı hazır.
  URT-D25 varsayılan rengin ürün bazlı mı şirket genelinde mi tutulacağını açıkça söylemiyor; bu alan etkinleştirilmedi.
- S19: kabul ters kaydı / tedarikçi iadesinde tüketilmiş gelecek ayırma ve fiziksel bağın geri alınması, yeniden açma ve
  yarış fact'leri henüz yazılmadı. Geçici koruma kaldırılmadı.
- S21: eksi stok kısa düşüm verisi mevcut, kalıcı deneme deposu / görünürlük / saklama akışı yok. Üretim ekibi bu tabloyu açmadı.
- S29: şirket logosu / antet yükleme ve maker≠publisher şablon yayımlama akışı yok; OPEN-019 kapatılmadı.

## Kontratlar ve merkezi entegrasyon

### SAT-S8 talep azaltma

`IInventoryFutureAllocationReleaseSessionV1.ReduceDemandAsync(request, metadata, ct)`:

- `InventoryFutureDemandReductionRequestV1`: Sales sipariş ve satır, ürün ve sürüm, talep birimi, yeni açık miktar metni,
  gerekçe ve isteğe bağlı açık seçimler.
- Seçim: `InventoryFutureDemandReleaseSelectionV1(AllocationId, ExpectedAllocationVersion, QuantityText, IsPhysical)`;
  miktar ayırmanın alış birimindedir. Kaynak ve çevrimler terminal seçim değerlendirmesinden önce okunur.
- Host bütün satış satırı kilitlerini artan sırayla önceden alır; port ilgili alış satırı kilitlerini artan sırayla alır.
  Bırakma çağıranın transaction'ında yapılır; commit / rollback host'undur.
- `IInventoryFutureSupplyScheduleSourceV1.GetLineScheduleAsync(orderId, lineId, ct)` →
  `InventoryFutureSupplyScheduleV1(IsReadable, PurchaseOrderId, PurchaseOrderLineId, ExpectedDeliveryDate)`.
  Tarih null olabilir; okunamayan cevap tarihsiz sayılmaz. Purchasing owner bridge entegrasyon sahibindedir.

### Kullanıcı uçları

- POST `/api/v1/inventory/future-allocations`.
- POST `/api/v1/inventory/future-allocations/{allocationId}/release`.
- GET `/api/v1/inventory/sales-orders/{salesOrderId}/lines/{salesOrderLineId}/future-allocations`.
- GET aynı talep satırı kökünde `/future-supply-options?cursor=...`; teknik sayfa boyutu 25.
  Kaynak liste fiyat, döviz, iskonto veya ödeme alanı taşımaz. Cari display'e yalnız owner listesinden alınan
  şirket ve Party kimlikleri gider; buradan rol veya yeni işlem uygunluğu türetilmez.
- GET `/api/v1/inventory/transfers`; teknik pencere 25, eksik depo adı GUID ile doldurulmaz; 503 döner.
- POST `/api/v1/inventory/labels/{labelId}/pdf`; mevcut labels/balances read. Başarı dosyayla birlikte `X-Document-Id` taşır.

### Frontend ve provider

- Component: `InventoryFutureAllocationPage`, dosya `inventory/InventoryFutureAllocationPage.tsx`.
- Helper: `inventory/capabilities.ts` → `inventoryFutureAllocationPath(salesOrderId, salesOrderLineId)`.
- Route: `/stok/satis-siparisleri/:salesOrderId/satirlar/:salesOrderLineId/gelecek-ayirmalar`.
- Read capability: `inventory.future-allocation.read`; ayrı write control: `inventory.future-allocation.write`.
- Inventory shell provider **1.8**; read yalnız kendi iznini, write read+write çiftini ister. Ek Cari / Product / Inventory
  physical-stock izni eklenmedi. Sales Link'i, ortak shell mount entegrasyonu ve registry pinleri merkezi sahibindedir.

### Yalnız kayıt izinler

`inventory.future-allocation.read` ve `inventory.future-allocation.write`: ErpPermissions sabitleri, All,
CompanyWideOnly ve Inventory owner manifest'e eklendi. Critical'a, shipped seed'e, role veya gerçek kullanıcıya eklenmedi.
Mevcut registration testleri bu iki ismi de denetliyor. Başka yeni izin yok.

### S26 varsayımı

**ASSUMPTION-STK-BARCODE-1:** mevcut PDFsharp 6.2.4 `Code3of9Standard` (Code39) kullanımı geri alınabilir teknik baskı seçimidir.
[Sürümü sabit üretici kaynağı](https://raw.githubusercontent.com/empira/PDFsharp/v6.2.4/src/foundation/src/PDFsharp/src/PdfSharp.BarCodes/Drawing.BarCodes/Code3of9Standard.cs).
Yeni bağımlılık, checksum veya barkod motoru yazılmadı. Saklanan kod aynen kodlanır; küçük harf, Türkçe karakter,
sentinel veya desteklenmeyen karakter **422** verir; kod değiştirilmez. PDF içindeki Türkçe metin Noto Sans'tır.
Şirket logosu / antet veya yayımlanmış marka şablonu gibi gösterilmez.

## Kanıt durumu

- Ara kaynak derlemeleri **exit 0, 0 uyarı / 0 hata**: Inventory.UnitTests, Identity.UnitTests,
  Api.IntegrationTests ve DocumentGeneration.Pdf.Tests. OpenAPI build üretimi kapalıydı.
- Integration ilk turundaki yeni S8 SQL fact eksik namespace hatası `207bdab47` ile düzeltildi;
  tekrar build exit 0.
- Son S18 provider bildirimi ve `InventoryFutureSupplyOptionsHostTests` bu ara buildlerden sonra eklendi:
  **son head .NET build NOT_RUN**; entegre dalga derlemesinde mutlaka derlenecek.
- Son frontend kaynak + test kodu için `tsc -b`: **exit 0**.
- `git diff --check`: **exit 0**.
- Bütün test **koşumları NOT_RUN**. Test projelerinin derlenmesi, testin veya gerçek SQL'in geçtiği anlamına gelmez.
  PDF byte replay, Türkçe glyph, barkod görünümü/tarama, gerçek vault, HTTP ve React davranışları henüz koşulmadı.

## Yeniden üretilecekler

1. Inventory 1.8 shell registry authorities (üreticiyle, elle hash yok).
2. Yukarıdaki yeni endpointler ve UnitCodes / options kontratları için OpenAPI JSON ve generation test sabitleri.
3. Değişen kaynak yüzeyleri için recovery / release envanterleri; **yeni migration yok**.
4. İlgili mevcut guard kaynak fingerprint'leri; yeni tenant bypass veya ham SQL allowlist girdisi açılmadı.
5. Yeni SQL fact'leri ilişkisel envantere ASCII tam adla ve sıralı eklenmeli; backend test grubu pinleri üretilecek.
6. Product SC36-c maker/checker ve Active düzeltmesinin SQL fact'i final kabulde koşulmalı.

## Yeni NEEDS-SINAN

Yeni karar seçilmedi. S11 eksikliği owner veri/semantik kaynağıdır; TK22 Customer rolü şartı zaten kararlıdır.
S13 varsayılan renk alanı uygulanacağı zaman, URT-D25'in şirket genelinde mi ürün bazında mı bir varsayılan istediği
kaynak cevaptan bulunamazsa soru gerekir. Çözüm diye başka bir izni zorunlu kılan daraltma yapılmadı.

Kaynak kapsamıyla S1 + S17 + S26 DONE kabul edilirse ekip **34/45 (%75,6)**; **6 PARTIAL, 5 kalan**.
Kesin plan sayımını diğer ekiplerin entegrasyonu ve KALAN_PLAN sahibi yapacaktır.
