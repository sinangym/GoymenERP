# Stok & Tahsis (Inventory) — durum ve yapılacaklar

> Kapsam: `src/backend/Modules/Inventory`, `src/backend/GoymenERP.Api/Endpoints/Inventory*`, `InventoryRuntime/`,
> `src/frontend/goymen-erp-web/src/inventory`. HEAD `ddc6cba`, 23 Kasım 2026. Salt okunur kod incelemesidir; derleme
> ve test koşulmadı. Bu belge karar veya onay değildir.

## 1. Kısa sonuç

Stok modülü projenin en dolu modülüdür ve çekirdeği sağlamdır: her fiziksel parça (rulo, paket) bir **etiket**
olarak tutulur, her giriş/çıkış değişmez bir **stok hareketi** olarak yazılır, bakiye bu hareketlerden türetilir.
Mal kabul, üretim tüketimi/çıktısı, sevk, müşteri iadesi, tedarikçi iadesi ve fason teslim stok hareketini tek bir
yoldan, kaynak belgeyle aynı veritabanı işleminde üretir; aynı belge aynı türde ikinci hareketi üretemez
(veritabanı tekil kısıtı).

Bugün ekrandan yapılabilenler (yetki verilirse): stok listesi ve filtreleme, etiket kartı, hareket geçmişi, ileri/geri
iz, etiket PDF'i (barkodlu), açılış sayımı girişi, tek etiket sayımı ve farkın onayı, hurda/fire çıkışı ve onayı,
bölme/birleştirme, sınıflandırma, satış siparişine fiziksel tahsis ve gelecek tedarikten ayırma, depolar arası
transferin planı–sevki–kabulü–kayıp/fazla onayları, eksik uyarısı.

Yapılamayanlar ve en önemli eksikler:

1. **Hiçbir stok izni hiçbir rolde yok.** 19 stok izninin tamamı yalnız kayıtlıdır; başlangıç rollerinde yoktur.
   Bugün hiçbir kullanıcı stok ekranını açamaz.
2. **Açılış stoku yalnız elle, satır satır girilir.** Toplu içe aktarma (Excel/CSV) yoktur; adet (paket/levha
   sayısı) alanı yoktur; müşteri malı açılışı ekranda yoktur; kaydedilmiş açılış sayımlarının listesi yoktur.
3. **Açılış maliyetinin ekranı yok.** Arka uç var (giren ≠ onaylayan), ön yüzde hiç kullanılmıyor. Girilmezse açılış
   stokundan yapılan satışın maliyeti ve kârı "yok" okunur.
4. **Kalite/sistem blokajı komutu yok.** Stok "bloklu" olarak girebilir ama bloke etme ve serbest bırakma komutu,
   ekranı ve izni yoktur.
5. **Şirketler arası mal hareketi tanımlı değil.** Transfer yalnız aynı şirketin depoları arasındadır. GDC→GSP gibi
   bir geçiş bugün ancak bir şirkette satış + sevk, diğerinde alış + mal kabul olarak kaydedilebilir; iki kayıt
   birbirine bağlanmaz.
6. Kabul edilmiş transferin ters transferi ve tartı farkı incelemesi arka uçta var, ekranı yok. Toplu (depo
   bazında) sayım, stok değer raporu ve dışa aktarım yok.

## 2. Hedef: bu modülden ne isteniyor

| Hedef | Kaynak |
|---|---|
| Ekranın adı "Stok / Malzeme Pozisyonu"; dört eksen: mülkiyet, gözetim, fiziksel konum, ticari bağ. Müşteri malı ve dış fason ayrı veri kaynağı değil, aynı satır modelinin görünümleri | Master §7.6; DEC-STK-001 |
| Miktar kartları: fiziki, satış tahsisi, üretim girdi tahsisi, yükleme için tutulan, kalite/sistem blokajı, serbest fiziki, gelecek arz, projeksiyon | Master §7.6, §28.10 (INV-001…010) |
| Stok gerçeğinin sahibi stok olay defteri; ekranlar türetilmiş pozisyon gösterir. Tahsis stok hareketi değildir; merkezi, atomik, yarış kontrollü, tekrar güvenli tahsis motoru | Master §9.2; DEC-STK-003/004 |
| Fiziksel stok ile alış siparişi, yolda, üretim bekleniyor, kalite bekliyor aynı listede ayrı kaynak türleriyle | Master §25.5; DEC-STK-002 |
| Stok satırı bağlamsal işlem merkezi: üretim planla, satışa tahsis et, yükleme talimatı, transfer/fason, sayım, kalite blokajı, soy ağacı | Master §25.4 |
| Her fiziksel paket ayrı etiket; bölme/birleştirmede yeni etiket ve korunan soy ağacı; basım öncesi önizleme, yeniden basım nedeni ve denetim kaydı | DEC-LBL-001…005 |
| Sayım farkı sebep ve yetki ister; geçmiş hareket silinmez | DEC-STK-007; AT-037 |
| Transfer ve dış fason iki bacaklı (çıkış, yolda, hedef kabul); mülkiyet değişmez | DEC-STK-008; 13–14 Ekim transfer kararları |
| Sevk iptali, eksik teslimde otomatik fire, fazla teslimde ikinci kişi onayı, kabul edilmiş transferin ters transferle geri alınması, bekleyen etikette teslim/ters transfer/kayıp | 11–12 Kasım kararları (KLN-D20…D27) |
| Hareketli ortalama maliyet depo ve şirket bazında; açılış maliyeti elle girilir, ikinci kişi onaylar; stok eksiye düşmez, denemeler kaydedilir | 9–13 Kasım kararları (SAT-D257, SAT-D304, SAT-D318/D323) |
| Açık alış siparişinden satış siparişine miktar bazında ayırma; mal kabulde fiziksel tahsise dönüşme | KLN-D26; `docs/delivery/STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md` |
| Rezervasyon etiket bazında; benzer ürün önerisi ölçü toleransına göre ve yapay zekâ ile; asgari stok yerine tarihli eksik uyarısı; iz ekranını stok gören herkes görür | 14 Kasım izlenebilirlik kararları (KLN-D39…D44) |
| Adet takibi (paket/levha sayısı) etikette ve hareketlerde | KLN-D119 |
| Ekran kataloğu: ST-01…ST-08, AL-01…AL-06 | Master §27.6 |
| Kabul senaryoları AT-029…AT-040 | Master §29.4 |
| Stok değerleme yöntemi ertelendi; lot maliyet ham verisi korunacak | OPEN-008 (kısmen açık) |

## 3. Bugün var olan

Ölçüler: 59 stok adresi (61 işlem) `contracts/openapi/goymen-erp-v1.json` içinde; 46 veritabanı göçü; 25 ekran/panel
dosyası; 16 stok hareketi türü (`Domain/Movements/StockMovement.cs`). Test kaynağı: 124 dosya ve 1207 test
`tests/GoymenERP.Inventory.UnitTests`; `tests/GoymenERP.Api.IntegrationTests` altında adı Inventory geçen 127 dosya;
gerçek SQL kabul listesinde 366 satır (`tools/relational_inventory/inventory.txt`); ön yüzde 60 test dosyası.

"Menü" sütununda **M** = yan menüde girişi var, **B** = başka bir ekranın içinden açılır, **—** = ulaşılamaz.
Bütün satırlar için ortak engel §4'tedir (izin hiçbir rolde yok).

### 3.1 Stok defteri, pozisyon, etiket

| Yetenek | Arka uç | Ekran | Menü | Seviye | Not / kanıt |
|---|---|---|---|---|---|
| Değişmez stok hareketi ve türetilmiş bakiye | Var | — | — | 1 | `Domain/Movements/StockMovement.cs`, `Domain/Balances/StockLedger.cs` |
| Aynı kaynak belge aynı türde ikinci hareket üretemez | Var | — | — | 1 | Tekil kısıt: `InventoryDbContext.cs` satır 586–591 (şirket, tür, kaynak, satır) |
| Dört eksen: şirket, mal sahibi, gözeten, lokasyon | Var | Var | M | 1 | `Domain/Stock/StockPosition.cs`; liste sütunları "Mal sahibi", "Malı tutan", "Lokasyon" |
| Stok / Malzeme Pozisyonu listesi (ST-01) | Var | Var | M | 1 | `InventoryStockPositionPage.tsx`; filtre: etiket kodu, ürün, lokasyon, sahip, sınıf, ürün ailesi, kalınlık/en |
| Miktar sütunları: fiziksel, bloklu, tahsisli, kullanılabilir | Var | Var | M | 1-kısmi | Tahsis tek sütun; satış/üretim/yükleme ayrımı yok. Ürün bazında toplam yok |
| Gelecek arz (açık alış siparişi) aynı ekranda | Var | Var | M | 1-kısmi | `PurchaseSupplySection.tsx`. Yalnız alış siparişi; "üretim bekleniyor", "yolda", "kalite bekliyor" yok |
| Etiket kartı (ST-02) | Var | Var | B | 1 | `InventoryLabelDetailPage.tsx`: kimlik, sahiplik/konum, hareketler, soy ağacı, belgeler |
| Hareket zaman çizgisi (ST-04) | Var | Var | B | 1 | `InventoryMovementTimelinePage.tsx`; yalnız sayfalama, tarih/tür filtresi yok |
| Etiket PDF'i, barkodlu | Var | Var | B | 1-kısmi | `InventoryLabelPdfComposer.cs`, `BarcodeLabelPdfGenerator.cs` (Code39). Etikette yalnız kod, döküm ve rulo no basılır; ürün adı, ölçü, kg yok |
| Etiket şablonu (logo, antet) | Var | Var | M | 1 | `documents.templates.inventory.*`; Ayarlar altında |
| İleri/geri iz ve etiket arama (tedarikçi etiket no dahil) | Var | Var | B | 1 | `InventoryLabelTraceService.cs`, `InventoryLabelTracePage.tsx` |
| Bölme / birleştirme, soy ağacı | Var | Var | B | 1 | `Domain/Labels/InventoryLabelTransformation.cs`, `LabelTransformationPanel.tsx` |
| Ana rulo adayları (üretim planı için) | Var | Üretimde | B | 1 | `InventoryParentRollService.cs`; tüketen `manufacturing/planReferences.ts` |
| Sınıflandırma: standart, ikinci kalite, hurda | Var | Var | B | 1 | `InventoryLabelClassificationCommandService.cs`. Yalnız tanımlayıcı; stok ve satılabilirlik etkisi yok |
| Adet takibi | Var | Kısmi | B | 1-kısmi | Mal kabul, sevk, transfer, sayım, hurda, tahsis taşıyor; açılış sayımı taşımıyor |
| Genel aramada etiket | Var | Var | — | 1 | `Search/InventoryMaterialLabelGlobalSearchSource.cs` |
| Depo/lokasyon görünümü (ST-03) | Yok | Yok | — | 0 | Yalnız ST-01'de lokasyon filtresi. Raf/göz yok |
| Kalite/sistem blokajı (ST-06) | Kısmi | Yok | — | 0 | Hareket türleri `Block`/`Release` tanımlı; komut, adres ve izin yok |

### 3.2 Açılış stoku ve maliyet

| Yetenek | Arka uç | Ekran | Menü | Seviye | Not / kanıt |
|---|---|---|---|---|---|
| Açılış sayımı: bir lokasyonun sayım föyü, çok satırlı, tek işlemde | Var | Var | M | 1-kısmi | `InventoryOpeningCommandService.cs`, `InventoryOpeningCreatePage.tsx`; Ayarlar › "İlk kurulum ve veri aktarımı". Satırlar elle |
| Açılışta müşteri malı (sahip = cari) | Var | Yok | — | 1-kısmi | Sözleşmede `OpeningCountPositionContextV1`; ekranda alan yok |
| Açılış sayımlarının listesi | Yok | Yok | — | 0 | Yalnız tek kayıt okuma; ekran tarayıcıda saklanan "son kayıt"ı açar |
| Toplu içe aktarma | Yok | Yok | — | 0 | Modülde `Import`, `Csv`, `Excel` araması 0 sonuç |
| Açılış maliyeti (giren ≠ onaylayan) | Var | Yok | — | 1-kısmi | `InventoryOpeningCostCommandService.cs`; ön yüzde `opening-cost` araması 0 sonuç. Bekleyenleri listeleyen adres de yok |
| Hareketli ortalama maliyet (şirket + depo + ürün) | Var | — | — | 1 | `Domain/Costing/AverageCostCalculator.cs`, `InventoryAverageCostLedgerWriter.cs` |
| Tam (lot) maliyet havuzu ve soy payı | Var | — | — | 1 | `Domain/Costing/ExactAverageCostPool.cs`, `InventoryCostLineageShares.cs` |
| Geç gelen alış faturası farkı | Var | Muhasebede | — | 1 | `InventoryPurchaseInvoiceCostStoreV1*.cs` |
| Eksi stok engeli ve deneme kaydı | Var | Var | B | 1 | `InventoryNegativeStockAttempts.cs`, `NegativeStockAttemptPage.tsx` |
| Stok değer raporu | Yok | Yok | — | 0 | Kullanıcıya ortalama maliyeti ya da stok değerini gösteren stok adresi yok |

### 3.3 Tahsis

| Yetenek | Arka uç | Ekran | Menü | Seviye | Not / kanıt |
|---|---|---|---|---|---|
| Satış siparişine fiziksel tahsis (etiket bazında): oluştur, değiştir, taşı, bırak | Var | Var | B | 1 | `InventoryStore.Allocations.cs`, `InventoryStore.AllocationChanges.cs`; `sales/SalesDraftAllocationPanel.tsx`, `LabelAllocationPanel.tsx` |
| Yarış kontrolü | Var | — | — | 1 | Etiket sürümü her tahsiste artar; ikinci kullanıcı 409 alır (`InventoryStore.Allocations.cs` satır 43, 114, 127) |
| Tahsis önerisi | Var | Var | B | 1-kısmi | `InventoryAllocationSuggestionReader.cs`. Benzer ürün toleransı ve yapay zekâ aracı bağlanmamış (§4) |
| Üretim planı girdi tahsisi | Var | Üretimde | B | 1 | `InventoryManufacturingRealizationParticipant.PlanAllocations.cs`; plan onayıyla aynı işlemde |
| Fason hizmet siparişi tahsisi ve kullanılmayanı bırakma | Var | Var | M | 1 | `SubcontractAllocationReleasePage.tsx` |
| Gelecek tedarikten ayırma, alış siparişi iptalinde bırakma | Var | Var | B | 1 | `InventoryFutureAllocationEngine.cs`, `InventoryFutureAllocationPage.tsx`; satış siparişi satırından açılır |
| Mal kabulde gelecek ayırmanın fiziksel tahsise dönüşmesi | Var | — | — | 1 | `InventoryGoodsReceiptFutureSupplyConversion.cs` |
| Tarihli eksik uyarısı | Var | Var | B | 1-kısmi | `InventoryShortageWarningService.cs`; benzer stok kaynağı bağlanmamış |
| Yükleme için tutma (ayrı etki türü) | Yok | Yok | — | 0 | `LoadingHold` araması 0 sonuç; sevk tahsisi doğrudan tüketir |
| Şirket genelinde aktif tahsisler listesi (AL-03), talep havuzu (AL-01), çakışma/öncelik (AL-06) | Yok | Yok | — | 0 | Tahsis okuması yalnız sipariş satırı veya etiket bazında |
| Tahsis süresi dolunca uyarı | Yok | Yok | — | 0 | `ReservedUntil` alanı saklanıyor; `Expire` araması 0 sonuç |

### 3.4 Transfer, sayım, hurda

| Yetenek | Arka uç | Ekran | Menü | Seviye | Not / kanıt |
|---|---|---|---|---|---|
| Transfer planı (aynı şirket, bütün etiket) | Var | Var | M | 1 | `InventoryTransferCommandService.cs`, `TransferPlanPanel.tsx` |
| Sevk (çıkış belgesi kanıtıyla) | Var | Var | B | 1 | `TransferDispatchPanel.tsx`; Lojistik çıkış belgesine bağlı |
| Sevk iptali | Var | Var | B | 1 | `InventoryStore.TransferCancel.cs`, `TransferCancelPanel.tsx` |
| Hedefte kabul, kabul belgesi | Var | Var | B | 1 | `InventoryTransferReceiptCommandService.cs`, `TransferReceiptPanel.tsx` |
| Eksik teslimde otomatik fire | Var | Var | B | 1 | Hareket türü `TransferReceiptShortage` |
| Fazla teslim: ikinci kişi onayı, yeniden talep | Var | Var | M | 1 | "Transfer fazla kabul onayları" |
| Yolda kayıp: talep, onay, geri çekme | Var | Var | M | 1 | "Transfer kayıp talepleri / onayları" |
| Kabul edilmiş transferin ters transferi | Var | Yok | — | 1-kısmi | `InventoryTransferReverseCommandService.cs`; ön yüzde `/reverse` araması 0 sonuç |
| Tartı farkı incelemesi | Var | Yok | — | 1-kısmi | `InventoryTransferScaleCheckReviewQueryService.cs`; ön yüzde 0 sonuç |
| Etiket sayımı, farkın onayı | Var | Var | B | 1 | `Domain/Counts/StockCount.cs`, `StockCountPanel.tsx`; bekleyenler İş Merkezi'ne düşer |
| Depo bazında toplu sayım | Yok | Yok | — | 0 | `CountSession`, `CycleCount` araması 0 sonuç |
| Hurda/fire çıkışı, onay | Var | Var | B | 1 | `Domain/Scraps/StockScrap.cs`, `StockScrapPanel.tsx` |
| Şirketler arası transfer | Yok | Yok | — | 0 | `Intercompany`, `CrossCompany`, `IntraGroup` araması 0 sonuç |

## 4. Kullanılabilirlik engelleri

**Rol ve yetki.** `src/backend/GoymenERP.Api/Seed/identity-seed.json` beş rol içerir; hiçbirinde `inventory.` ile
başlayan izin yoktur. 19 stok izninin tamamı `ErpPermissions.cs` içinde kayıtlıdır ama hiçbir role verilmemiştir:
etiket ve bakiye okuma, açılış stoku, fiziksel tahsis, gelecek ayırma okuma/yazma, bölme-birleştirme,
sınıflandırma, sayım kayıt/onay, hurda kayıt/onay, transfer yazma/iptal/fazla onay/kayıp kayıt/kayıp onay, açılış
maliyeti giriş/onay. 22 Kasım rol kararı sayım, hurda ve transfer onaylarını Operasyon Müdürü'ne verir; rol
tanımı ve ataması insan kapısıdır.

**İkinci kişi kuralı.** Sayım farkı, hurda, fazla teslim, yolda kayıp ve açılış maliyeti onayında kaydı giren kişi
onaylayamaz. Her şirkette bu işler için en az iki yetkili kullanıcı gerekir.

**Menü.** Yan menüde stok için altı giriş vardır: Stok / Malzeme Pozisyonu, Kullanılmayan fason ayırmayı bırak,
Depo transferi planlama, Transfer fazla kabul onayları, Transfer kayıp talepleri, Transfer kayıp onayları
(`shell/navigation.ts`); açılış stoku Ayarlar altındadır. Hareketler, iz, eksik uyarıları, eksi stok denemeleri ve
transfer listesi yalnız stok listesindeki bağlantılardan açılır.

**Kurulum verisi.** Stok girmeden önce şunlar gerekir: şirketin lokasyonları (Organizasyon), ürün kartları ve
sürümleri (Ürün), müşteri malı için cari hesap (Cari). Açılış sayımı harici lokasyonu reddeder
(`inventory_location_external`); fasoncuda ya da harici depoda duran mevcut malın açılışı bu yolla girilemez.

**Bağlanmamış kaynaklar (sahte değil, bilinçli kapalı).** `InventoryModuleExtensions.cs` içinde varsayılanı
"kapalı/tanımsız" olan ve çalışma zamanında değiştirilmeyen üç kaynak:

- Benzer ürün ölçü toleransı (`UnconfiguredInventorySimilarProductToleranceSourceV1`): Ayarlar'a bağlı değil;
  benzer ürün önerisi ve ikame tahsis çalışmaz.
- Tahsis önerisinde yapay zekâ aracı (`DisabledInventoryAllocationAiSuggestionToolV1`).
- Eksik uyarısında benzer stok (`InventoryNotConfiguredShortageSimilarStockSourceV1`).

**Ayar anahtarı.** Liste sayfalama imza anahtarı `Inventory:StockPositionCursorSigningKeyBase64` ortam ayarından
okunur (`Program.cs` satır 309–318); test ortamında tanımlanması gerekir.

**İnsan kapıları.** İzinlerin rollere verilmesi; transferde e-İrsaliye gönderim/iptal kanıtının hangi programdan
alınacağı; maliyet tarafındaki açık sorular (§7).

## 5. Eksikler

### 5.1 İsteklerinize göre

1. **Kalite/sistem blokajı** (Master §7.6, ST-06, AT-035; DEC-STK-006). Bloke etme ve serbest bırakma komutu yok.
   Mal kabul ve üretim çıktısı stoku "bloklu" girebilir; böyle giren stok bugün yalnız hurda çıkışıyla ya da
   iadeyle sistemden çıkabilir, serbest bırakılamaz.
2. **Miktar kartlarının ayrımı.** Satış tahsisi, üretim girdi tahsisi ve yükleme tutma tek "Tahsisli kg"
   sütununda toplanır. "Yükleme için tutulan" ayrı bir etki olarak yoktur (AT-036).
3. **Gelecek arzın diğer türleri.** "Üretim bekleniyor", "yolda" ve "kalite bekliyor" stok ekranında yok;
   projeksiyon (ileri tarihli kullanılabilir miktar, INV-009) yok.
4. **Satır işlemleri.** Master §25.4'teki sekiz işlemden "transfer veya dış fason başlat" ve "kalite blokajı
   başlat" stok satırından açılmaz (`stockRowActions.ts` kendi notunda bunu söyler). Transfer ayrı ekrandan
   planlanır.
5. **Tahsis ekranları.** Talep havuzu, şirket genelinde aktif tahsisler, çakışma/öncelik ekranları yok. Tahsis
   süresi dolduğunda uyarı (DEC-STK-005) yok.
6. **Etiket baskısı.** Basım öncesi düzenlenebilir önizleme ve yeniden basım nedeni (DEC-LBL-004) doğrulanamadı;
   PDF yalnız kod, döküm ve rulo numarası basar.
7. **Benzer ürün önerisi ve yapay zekâ** (KLN-D39/D40): kod var, kaynakları bağlı değil.
8. **Adet takibi açılışta yok** (KLN-D119). Kural "etiket adedi ilk stoktan itibaren taşır ya da hiç taşımaz"
   olduğundan, açılışla giren paket ve levhalar kalıcı olarak yalnız kg ile izlenir.
9. **Ters transfer ve tartı farkı incelemesi** (KLN-D22): ekranı yok.
10. **Transferin kapsamı.** Yalnız bütün etiket taşınır; kısmi miktar için önce bölme gerekir. Dış fasona
    gönderim ayrı bir akıştır (Lojistik fason teslim), transfer ekranından başlatılmaz.

### 5.2 Bir ERP'de olması gerekene göre

1. **Toplu açılış aktarımı.** Üç şirketin mevcut stoku (binlerce etiket olabilir) için Excel/CSV içe aktarma,
   ön doğrulama raporu ve hata satırlarının düzeltilmesi. Bugünkü form her satır için ürün arama, etiket kodu ve
   kg girişi ister.
2. **Şirketler arası hareket.** Aynı malın bir grup şirketinden diğerine geçişinde iki kaydı (satış/sevk ve
   alış/kabul) birbirine bağlayan ve etiketin kökenini taşıyan bir akış. Tasarım notu
   (`docs/adr/ADR-INV-001-label-company-scope.md`, durumu "Öneri") bunu "kaynak şirkette çıkış, hedef şirkette yeni
   etiket ve köken referansı" olarak tarif eder; kodu yoktur. 22 Kasım kararı grup içi tam otomasyonu erteler,
   döküm raporu ister.
3. **Depo bazında sayım.** Sayım föyü üretme, lokasyondaki bütün etiketleri sayma, bulunamayan ve fazladan çıkan
   parçaları toplu işleme, sayım sırasında hareket dondurma. Bugün her etiket ayrı sayılır; etiketsiz bulunan
   parça için yol yalnız açılış sayımıdır.
4. **Stok değer raporu ve dönem sonu.** Depo/ürün bazında miktar × ortalama maliyet, dönem sonu stok değeri,
   yaşlandırma (kaç gündür stokta), hareketsiz stok. Değerleme yöntemi açık karardır (OPEN-008).
5. **Raf/göz adresi** ve depo içi yer değiştirme. Bugün en küçük birim lokasyondur.
6. **El terminali / barkod okutma akışı.** Barkod basılıyor; okutarak kabul, sayım, transfer ve yükleme yapan
   bir ekran yok (`Scanner`, `Handheld` araması anlamlı sonuç vermedi).
7. **Konsinye** (bizim malımız müşteride, satılınca faturalanır): `Consign`, `Konsinye` araması 0 sonuç. Müşteri
   malı ve fasoncudaki mal sahip/gözeten eksenleriyle temsil edilebiliyor.
8. **Müşteri malı için ardiye/elleçleme** ve fason stok mutabakat dökümü (müşteriye gönderilecek ekstre).
9. **Kalite belgesi (MTC) eksikken sevk/üretim engeli.** Etikette "belge bekliyor" durumu hesaplanıyor ve
   gösteriliyor; sevki ya da üretimi durdurduğu doğrulanamadı.
10. **İnşaat demiri bağ etiketi (İDİS)** ve emisyon pasaportu alanları: 22 Kasım kararıyla sonraki dalgalarda.

### 5.3 Ekran ve kullanım kolaylığı

- Stok ekranlarında hata, boş, yükleniyor, yetkisiz ve çakışma durumları ortak bileşenle gösteriliyor
  (`StateBlock`); bu yön iyi.
- Hiçbir stok ekranında dışa aktarım (Excel/CSV) ve yazdırma yok; etiket PDF'i tek etiket içindir, toplu etiket
  basımı yok.
- Stok listesi etiket satırıdır; ürün veya depo bazında özet/toplam satırı yok.
- Hareket zaman çizgisinde ve transfer listesinde filtre yok; transfer listesi sayfalanmıyor, sınırı aşınca
  "pencere dolu" uyarısı veriyor.
- Sayım, hurda, bölme, sınıflandırma tek etiket üzerinde çalışır; çoklu seçimle toplu işlem yok.
- Açılış sayımı kaydedildikten sonra düzeltme yolu yeni sayım ya da hurda/sayım farkıdır; yanlış girilen açılış
  satırını geri alan bir komut görülmedi.

## 6. Diğer modüllerle bağlantılar

Mekanizma: stok hareketi doğuran her dış modül işlemi, Stok'un "katılımcı" sınıfını kendi veritabanı işlemine
dahil eder; belge ve stok hareketi birlikte yazılır ya da birlikte geri alınır.

### 6.1 Stok kimden ne alır

| Kimden | Ne | Mekanizma (dosya) |
|---|---|---|
| Satın Alma | Mal kabul → giriş hareketi, yeni etiket ve lot; ilk maliyet | `InventoryGoodsReceiptParticipant.cs` ← `PurchasingRuntime/PurchaseGoodsReceiptHost.cs`, `PurchaseGoodsReceiptBatchHost.cs` |
| Satın Alma | Kabulün geri alınması; tedarikçi iadesi çıkışı (asıl alış satırı maliyetiyle) | `InventoryGoodsReceiptReversalParticipant.cs`, `InventorySupplierReturnExitParticipant.cs` ← `PurchaseReceiptReversalHost.cs`, `PurchaseSupplierReturnHost.cs` |
| Satın Alma | Açık sipariş kalanı (gelecek arz), alış faturası maliyeti | `PurchasingOwnerInventoryFutureSupplyScheduleSource.cs`, `InventoryPurchaseInvoiceCostRateHost.cs` |
| Üretim | Girdi tüketimi, kullanılmayan girdinin iadesi, çıktı girişi, çıktı tersi, plan girdi tahsisi | `InventoryManufacturingRealizationParticipant*.cs` ← `ManufacturingRuntime/ManufacturingInventoryInputIssueHost.cs`, `ManufacturingInventoryReturnOutputHost.cs`, `ManufacturingPlanAllocationHost.cs` |
| Üretim | Üretim maliyeti nesli | `InventoryManufacturingCostGenerationParticipant.cs` ← `ManufacturingCostGenerationHost.cs` |
| Lojistik | Kesin sevk → çıkış hareketi ve tahsis tüketimi | `InventoryDefiniteShipmentParticipant.cs` ← `LogisticsRuntime/LogisticsRealizationTransactionRunner.cs` |
| Lojistik | Müşteri iadesi girişi (ilk çıkış maliyetiyle) | `InventoryCustomerReturnParticipant.cs` ← `LogisticsCustomerReturnRuntime.cs` |
| Lojistik | Fason teslim; transfer çıkış belgesi kanıtı ve iptal kapısı | `InventorySubcontractDeliveryParticipant.cs` ← `LogisticsSubcontractDeliveryHost.cs`; `InventoryTransferHostPorts.cs` |
| Organizasyon | Lokasyon durumu, transfer hedefi | `InventoryOpeningHostPorts.cs`, `InventoryTransferHostPorts.cs` |
| Ürün | Stoklanabilirlik, birim dönüşümü, teorik kg/adet, benzerlik verisi | `ProductOwnerInventoryUnitConversionSource.cs`, `ProductOwnerInventoryKilogramsPerUnitSource.cs`, `InventoryProductSimilarityFactsHost.cs` |
| Cari | Mal sahibi hesabı ve adı | `InventoryStockOwnerDisplayHost.cs`, `InventoryOpeningHostPorts.cs` |
| Satış | Açık sipariş ihtiyacı (eksik uyarısı, gelecek ayırma) | `InventoryShortageDemandSource.cs`, `InventoryFutureDemandSource.cs` |

### 6.2 Stok kime ne verir

| Kime | Ne | Mekanizma (dosya) |
|---|---|---|
| Satış | Kullanılabilir stok, tahsis ve öneri, satış marjı için maliyet | `/api/v1/inventory/positions`, `/physical-allocations`; `Endpoints/InventorySalesMarginCostReadPortV1.cs` |
| Üretim | Serbest miktar, ana rulo adayları, etiket durumu, maliyet okuma | `InventoryFreeQuantityReadPort.cs`, `InventoryParentRollService.cs`, `InventoryLabelDispositionReadPort.cs`, `InventoryManufacturingCostReadPort.cs` |
| Lojistik | Ürün fiziksel stoku, tahsis okuma, tartı farkı için stok | `InventoryProductPhysicalStockReadPort.cs`, `InventoryPhysicalAllocationHostPorts.cs` |
| Muhasebe | Sevk maliyeti, üretim tüketim maliyet izi → maliyet fişi önerileri | `InventoryShipmentCostReadPort.cs`, `InventoryManufacturingConsumedCostLineageReadPort.cs` ← `AccountingRuntime/AccountingInventoryCostSourceReaderV1.cs`; ekran `accounting/InventoryCostProposalsPage.tsx` |
| İş Merkezi | Bekleyen sayım ve hurda onayları; gelecek ayırma bırakıldı bildirimi | `Program.cs` satır 201–202; `Shell/TrainCompletionHostComposition.cs` satır 79–82 |
| Belgeler | Etikete bağlı belge ve etiket şablonu | `InventoryLabelDetailPage.tsx` içindeki `DocumentsPanel` |

### 6.3 Kopuk ya da eksik halkalar, çift kayıt riski

- **Çift hareket riski düşük.** Hareket yazan bütün yollar tek deftere gider ve kaynak belge başına tekil kısıtla
  korunur. Tahsis, bölme/birleştirme ve sınıflandırma hareket yazmaz.
- **Şirketler arası zincir kopuk.** Aynı mal iki şirkette birbirinden habersiz iki ayrı etiket olur; soy ağacı
  şirket sınırında kesilir.
- **Açılış stoku → maliyet → satış marjı / muhasebe zinciri ekranda kopuk.** Açılış maliyeti ekranı olmadığı için
  açılış stokunun maliyeti girilemez; bu stoktan yapılan sevkin maliyet önerisi "okunamadı" kalır.
- **Kalite → stok halkası yok.** Bloke stok oluşabiliyor, yönetilemiyor.
- **Stok olay bildirimleri.** Kesin sevk, müşteri iadesi ve tedarikçi iadesi için olay adları tanımlı
  (`Inventory*OutboxV1.cs`); dağıtıcıya yalnız iki olay kayıtlı (gelecek ayırma bırakıldı, üretim maliyet farkı
  önerildi). Diğerlerini dinleyen modül bulunamadı; bugün gerekli veriler doğrudan okuma kapılarıyla alınıyor.
- **Veri sahipliği ihlali görülmedi.** Stok başka modül tablosuna yazmıyor; izinli tek doğrudan bağımlılık denetim
  kaydıdır (`docs/architecture/MODULE_DEPENDENCY_ALLOWLIST.json`).

## 7. Açık kararlar

| Konu | Durum | Kullanımı engeller mi |
|---|---|---|
| Stok değerleme ve üretim maliyet yöntemi (OPEN-008) | Kısmen açık | Stok takibini engellemez; değer raporu ve dönem sonu için gerekir |
| Kantar/yükleme toleransı (OPEN-004), kütle dengesi (OPEN-005) | Geçici varsayım | Engellemez; saha doğrulaması bekler |
| Şirketler arası mal hareketinin biçimi (ADR-INV-001 "Öneri"; 22 Kasım: tam otomasyon ertelendi) | Karar yok | Üç şirket birlikte başlayacaksa ilk günden karşılaşılır |
| Kalite blokajını kim koyar, kim kaldırır | Karar yok (kod notu: "komut henüz yok") | Bloklu giren stok için engel |
| İlk alış maliyetinde stok/satılan mal payı, kısmi ilk fatura, kuruş artığı (`W8_KARAR_BEKLEYENLER.md`, STK-SC36-a) | Soruldu, cevaplar aktarılmış | Maliyet doğruluğunu etkiler |
| Transferde e-İrsaliye gönderim/iptal kanıtının kaynağı (STK-S8) | Soruldu | e-İrsaliyeli transfer iptalini etkiler |
| Ters transferde maliyet, düzeltilmiş fazla talebi, kayıp geri çekme izni | Modül notlarında açık | Engellemez |
| Hurda çıkışının değeri | OPEN-008'e bağlı | Engellemez |
| Eksi stok ihlali görülen kaydın davranışı | Modül notunda açık | Engellemez |
| İzinlerin rollere verilmesi | İnsan kapısı | **Evet** |

## 8. Yapılacaklar (öncelik sırasıyla)

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---|---|---|---|---|---|
| 1 | Stok iş rollerini tanımlayıp 19 izni rollere vermek (depo sorumlusu, Operasyon Müdürü, Genel Müdür yedek) | Bugün hiçbir kullanıcı stok ekranını açamaz | P0 | K | Rol/atama onayı (insan kapısı) |
| 2 | Toplu açılış stoku aktarımı: Excel/CSV, ön doğrulama, hata raporu; adet, müşteri malı ve harici lokasyon dahil | Üç şirketin mevcut stoku elle girilemez | P0 | O | Ürün kartları ve lokasyonlar kurulu; adet ve harici lokasyon için kural teyidi |
| 3 | Açılış sayımları listesi ve yanlış açılışı düzeltme yolu | Girilen açılışın kontrolü yapılamıyor | P0 | K | — |
| 4 | Açılış maliyeti ekranı (giriş, bekleyenler, onay); tercihen toplu giriş | Açılış stokunun maliyeti ve satış kârı "yok" kalır | P0 | K–O | İzinler (iş 1) |
| 5 | Şirketler arası mal hareketinin asgari biçimini kararlaştırıp uygulamak (en az: iki kaydı bağlayan referans ve köken etiketi) | Üç şirket aynı anda başlıyor | P0 (karar) / P1 (uygulama) | O–B | Sahip kararı |
| 6 | Kalite/sistem blokajı: bloke et, serbest bırak, neden, yetki, ekran | Bloklu stok yönetilemiyor; hedefte var | P1 | O | Yetki kararı |
| 7 | Ters transfer ve tartı farkı inceleme ekranları | Arka uç hazır, kullanılamıyor | P1 | K | — |
| 8 | Stok satırından transfer başlatma; kısmi miktar için bölme + transfer akışı | Hedefte var; günlük kullanım | P1 | K | — |
| 9 | Depo bazında toplu sayım (föy, toplu fark, bulunamayan/fazla parça) | Yıl sonu ve dönemsel sayım | P1 | O–B | — |
| 10 | Stok listesinde ürün/depo özeti, dışa aktarım; hareket ve transfer listesinde filtre ve sayfalama | Raporlama ve kullanım kolaylığı | P1 | K–O | — |
| 11 | Etiket PDF'ine ürün adı, ölçü, kg, mal sahibi; toplu basım; yeniden basım nedeni | Sahada okunabilir etiket | P1 | K | Etiket içeriği teyidi |
| 12 | Tahsisin türe göre ayrımı (satış, üretim, yükleme), yükleme tutma, şirket geneli tahsis listesi, süre uyarısı | Hedefin tamamı | P1 | O | — |
| 13 | Benzer ürün toleransını Ayarlar'a bağlamak; eksik uyarısında benzer stok | Kararı var, kaynağı bağlı değil | P1 | K | Tolerans değerleri |
| 14 | Stok değer raporu, yaşlandırma, dönem sonu | Muhasebe ve denetim | P1 | O | OPEN-008 |
| 15 | Gelecek arzda "üretim bekleniyor", "yolda"; projeksiyon | Hedefte var | P2 | O | — |
| 16 | Raf/göz adresi; el terminali ile okutmalı kabul, sayım, transfer | Depo verimliliği | P2 | B | — |
| 17 | Konsinye, ardiye, müşteri malı ekstresi; İDİS etiketi, emisyon alanları | Sonraki dalgalar | P2 | O–B | İlgili kararlar |

## 9. Doğrulanamayanlar

- Testlerin ve CI'ın bugünkü sonucu: koşulmadı; modül notları gerçek SQL testlerinin bir kısmını "yerelde
  koşulmadı" diye işaretler.
- "Belge bekliyor" durumundaki etiketin sevke ya da üretime girmesinin engellenip engellenmediği.
- Etiket baskısında yeniden basım nedeni ve baskı geçmişinin PDF adresince kaydedilip kaydedilmediği.
- Açılış sayımının canlı kullanım başladıktan sonra da serbestçe kullanılıp kullanılamayacağı (ek bir kilit
  görülmedi).
- Kesin sevk, müşteri iadesi ve tedarikçi iadesi olaylarının gerçekten yayımlanıp yayımlanmadığı.
- Stok ekranlarının yetki verildiğinde uçtan uca tarayıcıda çalıştığı; yalnız kaynak okundu.
- Muhasebedeki stok maliyeti önerileri ekranının menü ve yetki durumu (Muhasebe parçasının konusu).
