# Alış (Satın Alma) Modülü Hazırlık Planı v1

Tarih: 2026-11-02 · Hazırlayan: Claude (cloud oturumu, Alış şeridi) · Durum: **ÖNERİ** (plan; iş kararı değildir) · Kararlar: **`SINAN_KARARLARI_2026-11-02_ALIS.md` (ALS-D1…D13)**, **`_ALIS_EK1.md` (ALS-D14…D20)**, **`_ALIS_EK2.md` (ALS-D21…D28)**, **`_ALIS_EK3.md` (ALS-D29…D43)**

Kaynaklar: `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` §7.3, §8.2, §10.3, §25.5, §27.3, §28.12, §28.16, §29.3
(AT-025…028); `docs/decisions/KARAR_SICILI.md` `DEC-PUR-001`, `DEC-CAR-001`, `DEC-STK-002/003/006/008/009`,
`DEC-FIN-003/005/010`, `DEC-SAL-004/005/007`; `ACIK_KARARLAR.md` `OPEN-003`, `OPEN-004`, `OPEN-012`;
`SINAN_KARARLARI_2026-08-31.md` §4; `SINAN_KARARLARI_2026-10-21.md` delegasyon No 20, 25, 26, 36, 40, 41, 50, 55,
62, 63, 70, 71, 73, 112; `SINAN_KARARLARI_2026-10-27_NO161_TALEP_RED_SIPARIS_IPTAL.md`;
`SINAN_KARARLARI_2026-11-01_URETIM.md` URT-D15/D17; `SINAN_KARARLARI_2026-11-02_URETIM_EK1.md` URT-D20/D22;
`docs/handoffs/URETIM_ALIS_DEVIR_v1.md` (A-1…A-3); `src/backend/Modules/Purchasing/README.md`;
`docs/ui/screens/purchasing/MASTER3-P-01…12.md`; `docs/ui/screens/logistics/DISPATCH-INVOICE-PDH-01`, `PDW-01`,
`PDW-02`; `docs/backlog/DELIVERY_BACKLOG_v1.json` W2-SATINALMA, W2-IRSALIYE.

Bu plan kodun 2026-11-02 `main` durumuna (`71361df`) göre yazıldı. Biçim `SATIS_HAZIRLIK_PLANI_v1.md` ve
`URETIM_HAZIRLIK_PLANI_v1.md` ile aynıdır. Yöntem iki konuşmadan alındı: "Proje modülleri" (Satış şeridi) ve
"Proje modülleri geliştirme analizi" (Üretim şeridi). Eski StokERP alış işleyişi yalnız iş bilgisi ve ekran
kolaylığı referansıdır (§2.4).

Şerit kısaltmaları: **CL** Claude, **GP** ChatGPT (spec/kabul/golden), **SN** Sinan (karar). Seviye: K1/K2/K3
(`GOYMEN_ERP_HIZLI_TESLIM_v4.md`).

---

## 0. Şerit kaydı

- URT-D20 (2026-11-02): "Alış tarafını başka bir konuşma yapar." Bu oturum o konuşmadır.
- **ALS-D1 (2026-11-02):** Satış ve Üretim emsaliyle Alış'ın bütün geliştirmesi (backend, migration, OpenAPI,
  ekranlar) Claude'dadır. Kayıt: `SINAN_KARARLARI_2026-11-02_ALIS.md` §0.
- Modül sınırları: Satış dosyalarını "proje modülleri" konuşması, Üretim ve Stok dosyalarını Üretim şeridi değiştirir.
  Bu şerit onların dosyalarını değiştirmez; gereken işi görev notuyla bırakır: `ALIS_SATIS_DEVIR_v1.md` (TEK-03/04
  geçişi, MOD-03), `ALIS_URETIM_DEVIR_v1.md` (MOD-01/02/06/14), `ALIS_FINANS_DEVIR_v1.md` (MOD-04),
  `ALIS_LOJISTIK_DEVIR_v1.md` (MOD-07). Notlar ilgili dalga başlarken yazılır.
  Alış portları karşı taraf gelene kadar fail-closed çalışır.
- Değişmeyenler: review ve merge kapıları aynen geçerlidir; yazar kendi PR'ını review etmez; K3'te iki bağımsız
  exact-head review gerekir. Para, stok, formül, tolerans, yetki ve migration politikası tahminle etkinleştirilmez.
  İnsan kapıları (grant/rol, production aktivasyonu, deployment, secret, `OPEN-*` kapatma) değişmez.

---

## 1. "Hazır" tanımı

Alış modülü aşağıdakilerin hepsi sağlanınca hazırdır:

1. Akış uçtan uca çalışır: talep → (teklif) → sipariş → onay → termin/yolda → mal/hizmet kabulü → faturaya hazır →
   gelen fatura + üçlü kontrol → açık kalem (Finans) (master §8.2). Ödeme ve banka Finans'tadır.
2. Sipariş, kabul, gelen fatura ve ödeme planı ayrı kayıtlardır; "yolda" tek metin değil, termin + sevk bildirimi +
   beklenen kabul miktarıyla izlenir (§7.3).
3. Fiyat, iskonto, KDV, tevkifat, kur ve toplam yalnız backend calculator'dan gelir; Satış'ın ortak `Money`,
   `ExchangeRate` ve Ayarlar vergi masterı kullanılır, ikinci hesap yazılmaz.
4. Kabul toleransı (AT-026), fiyat/miktar farkı onayı (AT-027) ve iade bağı (AT-028) gerçek SQL ve tarayıcıda geçer.
   AT-025 (satış/alış belgeleri karışmaz) korunur.
5. Talep reddi ve onaylı sipariş kalanının iptali No.161 kararıyla çalışır; iptal ve kapatma fiziksel stok veya para
   olayı üretmez; gelecek arz formülü `INV-006` (`Onaylı − İptal − KabulNet − KesinRed/İade`) değişmez.
6. Hizmet satırı ve giden fason hizmet siparişi çalışır; Üretim'e `IPurchaseSubcontractOrderReadPortV1` yayımlanır
   (A-1). Talep satırı kaynağını taşır (A-2). Satış'ın "bağlı satın alma" kategorisi gerçek kaynaktan okunur (A-3).
7. P-01…P-12 ekranları ve Alış İrsaliye & Fatura merkezi yayında; her birinde yükleniyor, boş, hata, yasak, çakışma
   ve bilinmeyen sonuç durumları var.
8. Diğer modüllere giden etkiler outbox ile; aynı kabul veya fatura iki kez hareket üretmez.
9. Alış'ı etkileyen açık kararlar ya kapatılmış ya da yapılandırılabilir ve fail-closed.

---

## 2. Bugünkü durum

### 2.1 Var olan ve çalışan

| Alan | Durum | Kanıt |
|---|---|---|
| Domain | `PurchaseRequest` (Draft → Submitted), `PurchaseOrder` (Draft → Approved). Genel/teknik satır kırılımı (1.000 → 925 + 75), kabul payları, kalan = taahhüt − kabul, her değişiklikte `expectedVersion` | `Purchasing.Domain` |
| Çoklu kaynak | Bir talep satırı birden çok siparişe bölünür; onaylı siparişlerin toplamı talep miktarını aşamaz (Σ-pay, onay anında SQL içinde ölçülür) | `PurchasingStore`, Sinan #66 5738113623 |
| Miktar | Ortak `Quantity` (kg ≤ 3 ondalık, adet tam; fazla hane reddedilir). Birim Ayarlar kg profilinden doğrulanır | `PurchaseUnitVerifier`, Ö2, TEK-02 |
| Sahip kontrolleri | Cari tedarikçi rolü (`not_supplier`), Ürün alış uygunluğu (host'ta uygulanmış), birim profili; hepsi fail-closed | `PurchasingService` |
| Mal kabul | Sipariş satırına bağlı kg kabul: kabul miktarı, ölçülen kg, lokasyon, lot, etiket, heat/coil, belgeler; Stok'a tek transaction'da giriş; kabul–irsaliye kg karşılaştırma görünümü | `PurchasingService.GoodsReceipt*`, `InventoryGoodsReceiptParticipant` |
| HTTP | 14 uç (`/api/v1/purchasing`): talep oluştur/liste/detay/gönder; sipariş oluştur/liste/detay/onay; mal kabul kaydet/seçenekler/liste/detay/karşılaştırma; ürün adayı | `PurchasingEndpoints.cs`, `PurchasingProductCandidateEndpoints.cs` |
| Yetki | 4 izin: `purchasing.orders.create/.read/.approve`, `purchasing.goods-receipts.record` | `ErpPermissions.cs` |
| Portlar | `IPurchaseSupplyReferencePortV1` (gelecek arz), `IPurchaseOpenLineReadPortV1` (Lojistik L-09), kabul katılımcısı | `Contracts/` |
| Frontend | Talep liste/yeni/detay, sipariş liste/yeni/detay (onay dahil), mal kabul paneli, detay ve karşılaştırma | `src/frontend/.../purchasing/` (≈50 dosya, 20 test) |
| Test | 17 birim test dosyası; adında Purchasing geçen 12 entegrasyon dosyası (gerçek SQL dahil); PUR01 Playwright | `tests/` |

### 2.2 Çalışıyor görünen ama üretimde kullanılamayan

| Konu | Neden | Etki |
|---|---|---|
| Sipariş | Fiyat, para birimi, kur, iskonto, KDV, vade, termin yok (T28) | Sipariş tedarikçiye gönderilecek ticari belge değil; fatura eşleştirmesi yapılamaz |
| Onaylı sipariş | İptal, kapatma, revizyon, satır ekleme yok | Hatalı veya eksik teslim kalan sipariş sonsuza dek "açık arz" görünür |
| Talep | Onay/ret yok (No.161 kararı var, kod yok); kaynak alanı yok (A-2) | Talep havuzu yalnız kayıt listesi |
| Mal kabul toleransı | Kabul, kalanı aşamaz; fazla/eksik teslim bandı (No 40/41/62/63, OPEN-004 %1/%2/%3) uygulanmıyor | AT-026 geçmez; fazla gelen malı almak mümkün değil |
| Kalite serbest bırakma | Kabul `InitialBlock` taşıyor ama "kabul edilen mal kaliteden geçene kadar bloklu mu" kararı açık (#66 5743483383) | Kabul edilen malın kullanılabilirliği belirsiz |
| README | "Mal kabul yok, P-04 staged, ürün uygunluk portu yok" diyor; üçü de artık var | Belge kodla çelişiyor |

### 2.3 Hiç olmayan

Teklif / tedarikçi karşılaştırma (P-02), fiyat-vergi-kur, sipariş onay politikası ve tutar eşiği, termin dilimleri ve
"yolda" izleme, revizyon, iptal/kapatma, hizmet satırı ve hizmet kabulü, giden fason hizmet siparişi (A-1), talep
kaynağı (A-2), gelen fatura ve üçlü kontrol, fiyat farkı onayı, tedarikçiye iade ve iade faturası, red/iade/düzeltme
görünümü, sipariş PDF'i, tedarikçi performansı, raporlar, outbox olayları, Work onay kutusu adaptörü. Alış'a ait
`SISTEM_OF_RECORD_MATRISI.md` satırı ve `DURUM_MAKINELERI.md` makinesi yok.

Satış ile hacim karşılaştırması: modül .cs dosyası 135 / 46, migration 20 / 2, uç ≈56 / 14, birim test dosyası 58 / 17.

### 2.4 StokERP karşılaştırması

StokERP'de satın alma modülü **yoktu**. Talep, sipariş, onay, fatura, iade belgesi, fiyat, kur ve KDV yoktu; alış
yalnız bir irsaliye girişi satırıydı (`TicariHareketler`, `IslemGrubu='Alış'`). Bu yüzden Alış, Üretim'den farklı
olarak StokERP'den akış almaz; yalnız mal kabul ekranının kolaylıklarını ve kantar deneyimini alır. Süreç kuralları
master rapordan ve Satış emsalinden gelir.

**Alınacaklar (ekran kolaylığı ve iş bilgisi):**

| StokERP | Göymen karşılığı |
|---|---|
| `frmIrsaliyeGiris`: barkod/etiket okutarak satır ekleme, Enter yeni satır, Excel'den yapıştır, "Kaydet" + "Kaydet ve Yeni" | UI-08 mal kabul formu |
| Teorik kg (irsaliye) ile fiilî kg (kantar) farkının anlık, renkli ve yüzdeli gösterimi | UI-08 tolerans bandı göstergesi (API-40) |
| Kantar farkı listesi (firma ve onay filtreli, onay düğmesi) | UI-09 kontrol merkezi, API-55 |
| Yerel taslak + "geri yükle?" | UI-08 (mevcut `pending*` deseni) |
| AI/OCR ile irsaliye okuma → kullanıcı onay modalı (doğrudan kayıt yok) | Belgeler/AI şeridi; UI-08'e öneri olarak bağlanır (sonraki dalga) |
| Kırmızı "REVİZYON MODU" bandı, iptal/revizyonda zorunlu sebep ve denetim kaydı | UI-06, API-12, API-44 |
| Fason yönleri: "Fason Hizmet Alma" (bizim mal fasoncuya) ve "Fason Hizmet Verme" (müşteri malı bize) | API-70…72 (yalnız giden yön; gelen yön Satış S-3) |
| Kantar toleransı varsayılanı ±250 kg (irsaliye toplamı üzerinden, mutlak kg) | Alınmaz: Göymen'de yüzde bandı (OPEN-004 %1/%2/%3) ve satır bazı; değerler Ayarlar'da |

**Alınmayacaklar (StokERP'nin hataları):** tedarikçiye iade kaydının stoğu sessizce düşürmemesi (`Alış`+`Çıkış` görünümde
0 etki); kantar farkının yalnız irsaliye toplamında izlenmesi (satır sapmaları birbirini kapatıyordu); kantar farkı
onayının stok etkisine bağlı olmaması; tek ve mutlak kg tolerans; kodda sabit 7 günlük kilit ve Türkçe işlem adları;
serbest metin firma adı ve irsaliye no ile eşleştirme (`CompanyId` ve idempotency yok); rulo bölmede yuvarlama farkının
dağıtılmaması; fason yönünün işlem tipi seçimine bırakılıp mülkiyetin karışabilmesi.

### 2.5 Satış aynası: aynı olan, ters olan, Alış'a özgü olan

Sinan'ın 2026-11-02 notu: "alış modülü, sipariş oluşturma tarafı ve diğer sayfa gereksinimleri çoğunlukla satış
modülüne benziyor, satışta olanların tersi gibi çalışıyor". Değerlendirme: Alış'ın yaklaşık üçte ikisi Satış'la aynı
kalıptır ve Satış'ta çalışan bileşenlerle kurulur; kalan kısım ya yönü ters çevrilmiş ya da Alış'a özgüdür. Ters ve
özgü kısımlar Satış'tan kopyalanırsa yanlış çalışır; bunlar ayrıca tasarlanır.

**Aynı kalıp (Satış'taki bileşen/servis örnek alınır):**

| Satış'ta | Alış'ta | İş |
|---|---|---|
| Talep listesi/oluştur/detay (`SalesRequest*`) | İhtiyaç havuzu P-01 (talep zaten var; onay/ret eklenir) | API-01…03, UI-01 |
| Sipariş formu, satır fiyatı (`SalesDraftCreatePage`, `SalesDraftLinePricing`, `linePricing.ts`) | Alış sipariş formu, satır fiyatı + iskonto (ALS-D5) | API-20, UI-03 |
| Toplamlar kartı (`SalesOrderTotalsCard`, `SalesDraftService.Totals`) | Aynı calculator, aynı kart | API-20, UI-03 |
| Vade editörü (`SalesDueTermsEditor`) | Alış vadesi ve ödeme planı (ALS-D24, ALS-D28) | API-22 |
| Başlık/miktar/ticari revizyon + geçmiş (`SalesDraft*RevisionForm`, `SalesDraftRevisionHistory`) | Rev+1 ve yeniden onay (ALS-D9) | API-12/13, UI-06 |
| Termin planı (`SalesDraftDeliveryScheduleForm`, `deliverySchedule.ts`) | Termin dilimleri (P-05, P-06) | API-30, UI-07 |
| İptal ve zorla kapatma, etki önizlemesi (`SalesDraftCancellation`, `SalesDraftForceClose`) | No.161 kalan iptali + satır kapatma/yeniden açma (ALS-D10) | API-10/11, UI-05 |
| Durum eksenleri, sekmeler (`SalesOrderStatusAxes`, `SalesOrderTabs`) | Alış eksenleri: onay, kabul, faturalama, iade | API-14, UI-04 |
| Sipariş PDF'i, belgeler (`SalesOrderPdfAction`, `SalesOrderDocumentsWorkspace`) | Tedarikçiye sipariş PDF'i, P-05 belgeler sekmesi | API-16, MOD-11 |
| Onay deseni (taban fiyat/iskonto istisnası; hazırlayan ≠ onaylayan) | Talep, sipariş, fiyat farkı onayı (ALS-D2/D3/D8) | API-01, 15, 53 |
| Kur kaydı (SAT-D169…D172) | Aynı kural, kabul gününe göre (ALS-D6) | API-21 |
| Açık sipariş raporu, raporlar sayfası | Açık alış siparişleri, fiyat sapması | API-90…92, UI-13 |
| Bekleyen deneme depoları (`pending*.ts`, `orderAttempt.ts`) | Aynı "cevabı kaybolan istek" deseni | TEK-04 |

**Ters yönde çalışan (yön değişir; kopyalanmaz):**

| Konu | Satış | Alış |
|---|---|---|
| Stok etkisi | Tahsis + sevk = **çıkış** (`SalesDraftAllocationPanel`) | Gelecek arz + mal kabul = **giriş** (`GoodsReceiptPanel`, mevcut); tahsis paneli yok |
| Teslim farkı | Sevk toleransı (SAT-D5), müşteri kapısında red | Kabul toleransı + kantar fişi + iki kademeli istisna (No 40/41/62/63) |
| Fatura | ERP taslak hazırlar → Mikro'ya **dışa** aktarılır → resmî belgeyle eşleşir (SAT-D7) | Resmî fatura tedarikçiden gelir → Mikro'dan **içe** aktarılır → kabul paylarıyla eşleşir → üçlü kontrol (ALS-D7) |
| Fiyat farkı | Fatura–sipariş sapma raporu | Fark onaysız faturaya geçmez; her fark müdür onayı (ALS-D8, AT-027) |
| İade | Müşteri iade faturası keser; Satış kaydeder (SAT-D8) | **Göymen** iade faturası keser (No 20) → bu belge Satış'ın fatura taslağı akışına benzer (taslak → dışa aktar → eşleştir) |
| Açık kalem | Alacak (müşteri) | Borç (tedarikçi) → ödeme planı Finans'ta |
| Tevkifat | Müşteri (alıcı) tevkif eder | **Göymen** (alıcı) tevkif eder: ör. hurda alımı, fason hizmet. Vergi masterındaki "alıcı durumu" Göymen'in kendisidir; satıcının muafiyet durumu ayrıca okunur — mali müşavir teyidi (N-20) |
| Kur günü | Sevk günü | Kabul günü (ALS-D6) |
| Kaynak bağı | Sipariş satırı kaynağını seçer: stok/alış/üretim (API-11) | Talep satırı kaynağını taşır: satış/üretim/stok/manuel (A-2); aynı bağın iki ucu (MOD-03) |
| Teklif | Bizden çıkar, tek müşteri; "siparişe dönüştür" | Birden çok tedarikçiden toplanır ve karşılaştırılır; seçilen siparişe dönüşür (A6, ALS-D11) |
| Fiyatsız görünüm | Sevkiyatçı | Depo/kabul personeli (PRS-02) |

**Satış'ta olup Alış'ta karşılığı olmayan:** fiyat kademe zinciri ve taban fiyat (ALS-D5 elle fiyat), rol bazlı
iskonto limiti, müşteri risk/limit kontrolü ve risk istisnası, müşteri kabulü/reddi (yerine tedarikçi sipariş
teyidi: N-21), kayıp/fırsat nedenleri.

**Alış'a özgü (Satış'ta yok):** genel/teknik satır kırılımı (P-12), Σ-pay ile çoklu kaynak, kantar ve kabul
toleransı istisnaları, siparişsiz kabul istisnası (DEC-PUR-001), sevk bildirimi/"yolda", hizmet kabulü (stok hareketi
yok), giden fason hizmet siparişi (A-1), tedarikçi performansı.

**Uygulama yolu:** Satış dosyaları bu şeritte değiştirilmez (URT-D19). Aynı kalıptaki bileşenler kopyalanmaz;
yön bilgisinden bağımsız kısımları (durum eksenleri, sekmeler, toplamlar kartı, revizyon geçmişi, PDF eylemi, vade
editörü, bekleyen deneme deposu, iptal önizleme paneli) yeni ortak klasöre (`src/frontend/goymen-erp-web/src/commercial/`,
backend'de BuildingBlocks altında ticari belge yardımcıları) **Alış için yazılır**; Satış'ın bunlara geçişi
`docs/handoffs/ALIS_SATIS_DEVIR_v1.md` görev notuyla Satış şeridine bırakılır (TEK-03, TEK-04).

---

## 3. Fazla ve temizlenecek (FZL)

| ID | İş | Neden | Sev. |
|---|---|---|---|
| FZL-01 | README'yi kodla eşitle (mal kabul, P-04, ürün uygunluk portu, "What this module is not" listesi) | Belge kodla çelişiyor | K1 |
| FZL-02 | `PurchaseOrder.FromRequest` (tam miktar eski biçim) — `CreateForRequest` + kaynak satır bağı varken ikinci yol | İki yol, Σ-pay kuralını atlama riski | K2 |
| FZL-03 | Frontend `as CapabilityId` dönüşümleri, `SHARED_MOUNT_HANDOFF.md` durum notu, eski "staged/not live" yorumları | Belge ve kod çelişkisi | K1 |
| FZL-04 | Alış audit şirket guard'ı Satış sürümünden ayrılmış ("aktif şirket zorunlu" kontrolü aynı yerde değil; Üretim analizi). Ortak guard'a geçiş ve açığın doğrulanması | Olası şirket izolasyonu farkı | K3 |

---

## 4. Kod ve teknoloji düzeltmeleri (TEK)

| ID | İş | Neden | Sev. | Bağımlılık |
|---|---|---|---|---|
| TEK-01 | Sipariş satırına ortak `Money` (birim fiyat + para birimi) ve satır tutarı; ölçek Ayarlar para profilinden | Fiyatsız sipariş fatura eşleştiremez | K3 | – |
| TEK-02 | Alış idempotency'sini (`PurchaseAttempts`) Satış'taki ADR-SAL-001 sonucuyla hizala; yeni komutlar tek yolu kullanır | İki yol | K2 | – |
| TEK-03 | Ortak komut iskeletini (sürüm → yetki → replay → domain → audit), replay kaydını ve SQL test yardımcılarını BuildingBlocks altında **yeni** ortak katmana yaz; Alış ilk tüketici. Satış dosyaları değişmez; Satış'ın geçişi `ALIS_SATIS_DEVIR_v1.md` notuyla Satış şeridine bırakılır | Kopya kod | K3 (ortak sözleşme) | – |
| TEK-04 | Yönden bağımsız frontend bileşenlerini (bekleyen deneme deposu, sekmeler, durum eksenleri, toplamlar kartı, revizyon geçmişi, PDF eylemi, vade editörü, iptal önizleme paneli) **yeni** `src/commercial/` klasörüne Alış için yaz; Satış'ın geçişi aynı görev notuyla | Kopya kod | K1 | – |
| TEK-05 | Frontend OpenAPI sözleşme testi (Satış TEK-08 / Üretim emsali) | Elle decoder sapması | K2 | – |
| TEK-06 | Sabit Türkçe metinler `purchasingText.ts`'e; ham `<button>` → tasarım sistemi `Button` | Kaynak dosyalı metin kuralı | K1 | – |
| TEK-07 | Büyük dosyaları böl: `PurchasingStore.cs` (902), `PurchasingService.cs` (896) | Çakışma riski | K1 | – |
| TEK-08 | Detayda GUID yerine tedarikçi, ürün ve birim adı | Okunabilirlik | K2 | – |
| TEK-09 | Durum makinesi genişletmesi: talep Draft → Submitted → Approved/Rejected; sipariş Draft → Approved → Kısmi → Tamamlandı/Kapatıldı/İptal (eksenler §5.2) | Bugün yalnız iki durum | K3 | N-02, N-03 |

---

## 5. Eksik API uçları

Tüm yeni uçlar `/api/v1/purchasing/` altında; değişiklik yapanlar `Idempotency-Key` + gövdede `expectedVersion`; katı
JSON; şirket bağlamı; yetki her adımda yeniden ölçülür; audit aynı transaction'da; OpenAPI'ye ve sözleşme testine
eklenir. İzin adları öneridir; sahip modül koyar (No P5).

### 5.1 Talep (P-01 İhtiyaç Havuzu)

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-01 | `POST requests/{id}/approval` · `/rejection` | Talep onayı/reddi; ret yalnız Submitted/Approved iken ve bağlı onaylı sipariş yokken; gerekçe zorunlu | `purchasing.requests.approve` (yeni) | No.161 §1, N-02 | K3 |
| API-02 | `POST requests` genişlemesi | Satır kaynağı (stok / satış / üretim / manuel) + kaynak kimliği, istenen tarih | mevcut | A-2 | K3 |
| API-03 | `GET requests/pool` | İhtiyaç havuzu: onaylı, siparişe bağlanmamış kalanlar; kaynak ve tarih filtresi | `purchasing.orders.read` | §7.3 | K2 |

### 5.2 Sipariş yaşam döngüsü

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-10 | `GET orders/{id}/cancel-impact` · `POST orders/{id}/cancellation` | Onaylı siparişin kabul edilmemiş kalanının tamamını iptal (kısmi kabul olsa da); etki önizlemesi; gerekçe | `purchasing.orders.cancel` (yeni) | No.161 §2 | K3 |
| API-11 | `POST orders/{id}/lines/{lineId}/closure` · `/reopening` | Eksik teslimde satır kalanını kapat ("eksik teslimle kapandı"), iptalden ayrı eksen; tolerans bandı içinde sistem önerir ve sorar (ALS-D10), band dışını Satın alma sorumlusu gerekçeyle kapatır (ALS-D12); kapanan satır yeniden açılabilir | `purchasing.orders.close` (yeni) | ALS-D10, ALS-D12 | K3 |
| API-12 | `POST orders/{id}/header-revision` · `/line-revision` | Tedarikçi, para birimi, vade, termin; satır ekle/sil/ürün/miktar/fiyat → Rev+1 ve yeniden onay | `purchasing.orders.revise` (yeni) | N-10 | K3 |
| API-13 | `GET orders/{id}/revisions` | Revizyon geçmişi ve fark (P-05) | `purchasing.orders.read` | – | K2 |
| API-14 | `GET orders/{id}/status` | Eksenler: yaşam döngüsü, onay, kabul (tolerans dahil), faturalama, iade, finansal kapanış | `purchasing.orders.read` | §7.3 | K3 |
| API-15 | `POST orders/{id}/approval` genişlemesi | Tutar eşiği / onay politikası Ayarlar'dan; hazırlayan ≠ onaylayan | mevcut | N-03 | K3 |
| API-16 | `GET orders/{id}/pdf` | Tedarikçiye gidecek fiyatlı sipariş PDF'i (Satış MOD-07 deseni, TASLAK filigranı) | `purchasing.orders.read` | ALS-D34 | K2 |
| API-18 | `POST orders/{id}/supplier-confirmation` | Tedarikçi sipariş teyidi (isteğe bağlı; teyit belgesi eklenir; listede "teyit bekliyor"; yeni kolon/migration) | `purchasing.orders.create` | ALS-D13, master P-05 | K3 |
| API-17 | `POST orders/{id}/breakdown` | Domain'de var olan genel/teknik kırılımı HTTP'ye aç (P-04 ürün kırılımı; görsel MASTER3-P-12) | `purchasing.orders.revise` | §13 | K3 |

### 5.3 Fiyat, vergi, kur

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-20 | Sipariş satırında birim fiyat, para birimi, iskonto; `GET orders/{id}/totals` | Satır tutarı, KDV, tevkifat, toplam (ortak calculator); vade farkı iki biçimde: peşin + fark veya tek vadeli fiyat (ALS-D38); fiyatsız satır (ALS-D18) | TEK-01, ALS-D5, ALS-D38 | K3 |
| API-21 | Kur: siparişte bilgi amaçlı; faturada tek kur, Satış kur kaydı ile aynı kaynak | N-06, DEC-FIN-010, No 26 | K3 |
| API-22 | Vade ve ödeme planı siparişte (taksit sayısı, her taksitin tarihi ve tutarı); Cari'den öneri gelmez; taksit toplamı ödenecek tutara eşit (EK2 §2 yorumu) | ALS-D24, ALS-D28 | K3 |
| API-23 | Masraf ve başlık iskontosu satırı: siparişe veya kabule tip + tutar + KDV (nakliye, komisyon, finansman; fason işlemler masraf satırı değildir → ALS-D29); satırda **cari** seçilebilir (sipariş tedarikçisi veya başka firma, ör. nakliyeci); o carinin gelen faturasıyla eşleşir (API-51/52); stok miktarını değiştirmez | ALS-D23, ALS-D27 | K3 |

### 5.4 Termin ve "yolda" (P-05, P-06)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-30 | `POST orders/{id}/lines/{lineId}/schedule` | Miktar bazlı termin dilimleri (toplam = satır miktarı; yeni tablo + kısıt); hubvogal'deki bağlantının "sıra"ları (ALS-D21) | §7.3, ALS-D33 | K3 |
| API-31 | `POST orders/{id}/shipment-notices` | Tedarikçi sevk bildirimi (araç, plaka, şoför, irsaliye no, kg) → "yolda"; taşıma durumu (master §7.3); ayrıca **tedarikçide hazır** miktarı (U.S Hazır / Bülten Hazır) ve düzeltmesi (ALS-D22); şoför ad/telefon/TC/plaka (ALS-D39: yetkili, maskeli, audit'li; 10 yıl saklama, imha ayrı insan kapısı) | §7.3, ALS-D22, ALS-D33, ALS-D39 | K3 |
| API-32 | `GET expected-deliveries` | Beklenen teslimatlar; geciken termin | ST-08, L-09 | K2 |

### 5.5 Mal ve hizmet kabulü

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-40 | Mal kabulüne tolerans | Fazla/eksik teslim bandı: uyarı → devam; onay bandı → `purchasing.delivery-variance.approve` tek onay; blok → ret veya iki kademeli istisna (Depo → Satın alma sorumlusu); kantar fiş no zorunlu; politika ürün/işlem bazlı (`PurchaseDelivery`), Ayarlar'dan sürümlü. Formüller master §28.12 WGT-001/002, GR-001…003 (büyük sapma); stok gerçek kg ile girer (No 40). Production etkisi W2-IRSALIYE kapısı `inventory.receiptQuantityPolicy.enabled = false` arkasında; AT-055 | No 40, 41, 62, 63; OPEN-003/004 | K3 |
| API-41 | `POST orders/{id}/lines/{lineId}/service-acceptances` | Hizmet kabulü: stok hareketi yok; kabul kanıtı + faturaya hazır | §7.3 | K3 |
| API-42 | `POST goods-receipts/without-order` | Siparişsiz kabul yalnız yetkili istisna | DEC-PUR-001, ALS-D35 | K3 |
| API-43 | Kalite serbest bırakma bağı | Kabul ve serbest bırakma ayrı kavram kalır (DEC-PUR-001); varsayılan: kabul anında serbest (`InitialBlock = "Free"`, kodda zaten böyle; §13 B-6), kalite kullanıcısı sonradan bloklayabilir | ALS-D4 | K3 |
| API-44 | `POST goods-receipts/{id}/correction` | Kabul düzeltmesi: ters + yeni, silme yok | AGENTS.md | K3 |

### 5.6 Gelen fatura ve üçlü kontrol (P-08, P-09; PDH-01, PDW-02)

Satış'taki SAT-D7 deseni: resmî belge dış sistemde; ERP eşleştirir. Finans'taki resmî fatura içe aktarma yeniden
kullanılır; Alış ikinci bir resmî fatura kaydı tutmaz. **Ön şartlar §13 B-1 (Finans'ta yön) ve B-2 (fatura çekirdeğinin
sahibi) çözülmeden bu bölüm başlamaz.**

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-50 | `GET invoice-candidates` | Faturaya hazır kabul payları (tedarikçi + para birimi gruplaması) | §7.3 | K3 |
| API-51 | `POST incoming-invoices` · `GET …` · `GET …/{id}` | Gelen fatura kaydı: kabul payları ve aynı cariye ait masraf satırları (API-23) seçilir; fatura sipariş para biriminde; tek kur | No 25, 26, N-07 | K3 |
| API-52 | `GET incoming-invoices/{id}/three-way-check` | Üçlü kontrol: sipariş fiyatı/miktarı ↔ kabul ↔ fatura; fark türleri | AT-027 | K3 |
| API-53 | `POST incoming-invoices/{id}/variance-approval` | Fiyat/miktar farkı onayı (onaysız faturaya geçmez); fark tolerans politikası Ayarlar'dan | AT-027, N-08 | K3 |
| API-54 | `POST incoming-invoices/{id}/finalization` | Kesinleştir → `purchasing.invoice.finalized.v1` → Finans borç açık kalemi | DEC-FIN-005 | K3 |
| API-55 | `GET control-center` | Eşleşmeyen, farklı, geciken kayıtlar (PDH-01) | dispatch spec | K2 |

### 5.7 Red, iade, düzeltme

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-60 | `POST supplier-returns` · `GET …/{id}` | Tedarikçiye iade: kaynak sipariş + kabul (+ fatura) bağı zorunlu; bloklu stoktan çıkabilir; fiziksel çıkış Stok/Lojistik'te | No 20, 36, 70; AT-028 | K3 |
| API-61 | İade faturası | Göymen'in kestiği ayrı iade faturası; taslak → dışa aktar → eşleştir (Satış manuel akışı) | No 20, DEC-FIN-003 | K3 |
| API-62 | `GET orders/{id}/returns` | Red/İade/Düzeltme ilişki görünümü (DCR-01) | §7.3 | K2 |

### 5.8 Fason (A-1)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-70 | Hizmet satırı (`Service` kalem tipi, hizmet birimi veya işlenecek kg) | Hizmet alışı ve fason için ortak satır türü | URT-D15/D17, ALS-D29, ALS-D40 | K3 |
| API-71 | Fason hizmet alış siparişi: tedarikçi = Cari fason rolü; gönderilecek malzeme ve beklenen çıktı ürünleri | A-1, DEC-CAR-001, DEC-STK-008 | K3 |
| API-72 | `IPurchaseSubcontractOrderReadPortV1` | Üretim'e: onaylı mı, fason firması, hizmet satırları, malzeme, açık/kapalı | A-1 | K3 |

### 5.9 Teklif (P-02) — A6 dalgasında (ALS-D11)

| ID | Uç | Amaç | Sev. |
|---|---|---|---|
| API-80 | `POST rfqs` · `POST rfqs/{id}/offers` | Talep için birden çok tedarikçiden teklif (yeni tablolar) | K3 |
| API-81 | `GET rfqs/{id}/comparison` · `POST rfqs/{id}/award` | Karşılaştırma ve seçilen teklifi siparişe dönüştürme (fiyat snapshot'ı) | K3 |

### 5.10 Raporlar ve performans

| ID | Uç | Amaç | Sev. |
|---|---|---|---|
| API-90 | `GET reports/open-orders` | Açık alış siparişi tonajı ve tutarı | K2 |
| API-91 | `GET reports/supplier-performance` | Zamanında teslim, miktar sapması, kalite reddi, fiyat sapması (N-18) | K2 |
| API-92 | `GET reports/price-variance` | Fatura–sipariş fiyat sapması | K2 |

### 5.11 Ayarlar sahibinde

| ID | Politika | Karar | Sev. |
|---|---|---|---|
| API-95 | Alış teslim toleransı (`PurchaseDelivery`; uyarı %1 / onay %2 / blok %3 başlangıç, ürün/işlem bazında) | No 40, OPEN-004 | K3 |
| API-96 | Alış onay tutar eşiği | N-03 | K3 |
| API-97 | Fatura fiyat farkı toleransı | N-08 | K3 |
| API-98 | Masraf tipi, hizmet tipi ve sipariş pozisyonu (işlem türü) listeleri; Takas/Temel yok; "Satış Fason" Alış listesinde yer almaz (gelen fason Satış'ta, ALS-D29) | ALS-D20, ALS-D23, ALS-D29, ALS-D37 | K2 (migration gerekirse K3) |

Satış politika kaydı (`ApprovedSalesPolicyCatalog`) ve üretim tolerans yaşam döngüsü örnek alınır.

---

## 6. Diğer modüllerle eksik ilişkiler (MOD)

| ID | Modül | Eksik | Kim yapar | Sözleşme | Sev. |
|---|---|---|---|---|---|
| MOD-01 | Üretim | Fason hizmet siparişi okuma portu; transferin fason siparişine bağlanması | Alış (port) + Üretim şeridi (tüketici) | `IPurchaseSubcontractOrderReadPortV1` (A-1) | K3 |
| MOD-02 | Üretim | Eksik hammadde için "satın alma talebi oluştur" çağrısı | Alış (alan) + Üretim (eylem) | mevcut talep ucu + kaynak alanı (A-2) | K3 |
| MOD-03 | Satış | İptal/zorla kapatma önizlemesinde "bağlı satın alma"; kaynak planında "satın alma" seçeneği | Alış (veri sağlayan port) + Satış şeridi (`ISalesOrderLinkedSupplyReadPortV1` sözleşmesinin sahibi ve tüketici) | Alış, Satış'ın tanımladığı sözleşmeye bir adaptör yazar; sözleşme dosyasına dokunmaz (A-3, Satış MOD-05, API-11) | K3 |
| MOD-04 | Finans | Gelen faturanın borç açık kalemi; resmî gelen fatura içe aktarma ve eşleştirme okuması | Alış + Finans sahibi | `purchasing.invoice.finalized.v1` + gelen yönlü içe aktarma | K3 |
| MOD-05 | Ayarlar | Alış politikaları (API-95…97); vergi masterı ve para profili okuması | Alış | mevcut `ISettingsPrerequisiteProvider` genişlemesi | K3 |
| MOD-06 | Stok | Kabul toleransının stok girişine etkisi, kalite blok bağı, tedarikçiye iade çıkışı (bloklu stoktan dahil) | Alış + Üretim/Stok şeridi | mevcut kabul katılımcısı genişlemesi + iade çıkış portu | K3 |
| MOD-07 | Lojistik | L-09 kapı kabulü ve L-10 mal kabulü ile Alış kabulünün tek kayıt olması; kantar oturumu; iade sevki | Alış + Lojistik | mevcut `IPurchaseOpenLineReadPortV1`, `PurchaseDelivery` politikası | K3 |
| MOD-08 | Cari | Tedarikçi ve fason rolü, tedarikçi vade/ödeme koşulu okuması | Alış | mevcut `IPurchasingPartyAccountPortV1` genişlemesi | K2 |
| MOD-09 | Ürün | Tedarikçiye özel alış fiyatı/fiyat listesi (varsa), hizmet ürünleri | Alış + Ürün | N-05 | K2 |
| MOD-10 | Work | Talep, sipariş, fark ve istisna onayları için onay kutusu adaptörü | Alış | mevcut Work onay sözleşmesi | K3 (yetki + modüller arası) |
| MOD-11 | Belgeler | Sipariş PDF'i, sözleşme ve belgeler sekmesi (P-05; görsel MASTER3-P-06) | Alış | `PurchasingDocumentOwnerLinkPort` (mevcut) | K2 |
| MOD-12 | Platform | Outbox/inbox (ERP-000F); Satış'ın "fatura olayını iletecek altyapı" işiyle ortak | Satış şeridiyle sıralı | ERP-000F | K3 |
| MOD-13 | Muhasebe | Gelen fatura vergi kontrolü | Faz 6 sonrası | – | K3 |
| MOD-14 | Stok | Gelecek arz görünümüne (ST-08, master §25.5) termin dilimi, **tedarikçide hazır** (ALS-D22 + ALS-D25: gelecek arz içinde ayrı satır, fiziksel stok değil; sevk ve fiziksel tahsis dışı; satışa gelecek mal olarak ayırma ve "aynı ton bir kez" kuralı Stok tahsis motorunda, ALS-D30), yoldaki miktar, iptal ve kesin red/iade etkisi | Alış (port genişlemesi) + Üretim/Stok şeridi (görünüm) | `IPurchaseSupplyReferencePortV1` v2 | K3 |
| MOD-16 | Satış | Kaynak planında ve sipariş ekranında "tedarikçide hazır" dahil alış gelecek arzını görme (ALS-D22: "satış… görmeli"); gelecek mal olarak ayırma (ALS-D30) | Satış şeridi (görev notu `ALIS_SATIS_DEVIR_v1.md`) | `IPurchaseSupplyReferencePortV1` v2 (MOD-14 ile aynı port) | K3 |
| MOD-15 | Finans | **Açık kalemde yön (alacak/borç) yok** (§13 B-1) | Finans sahibi (görev notu `ALIS_FINANS_DEVIR_v1.md`) | `FinanceOpenItems` yön alanı; Satış risk okumasında borç kalemlerinin süzülmesi; gelen fatura içe aktarma türü | K3 (migration + para + modüller arası sözleşme; netleştirme yok → ALS-D42) |

---

## 7. Ekranlar (UI)

**Ekran kimlikleri.** Bu planda P-xx kimlikleri **master §27.3 kanonik** kimlikleridir. Görsel kaynak dosyaları
(`MASTER3-P-*`) kendi DOCX numaralarını taşır; eşleme `docs/ui/SCREEN_CATALOG.md` `canonical_ids` sütunundadır:

| Kanonik (master §27.3) | Ad | Görsel kaynak |
|---|---|---|
| P-01 | Talepler / İhtiyaç Havuzu | – (referans yok) |
| P-02 | Teklif ve Tedarikçi Karşılaştırma | – (referans yok) |
| P-03 | Satın Alma Siparişleri (liste) | MASTER3-P-01 |
| P-04 | Yeni/Düzenle Sipariş | MASTER3-P-08, MASTER3-P-12 (ürün kırılımı) |
| P-05 | Sipariş 360 / Termin Dilimleri (teyit, yolda, kabul, fatura) | MASTER3-P-02, P-03, P-06, P-07, P-09 |
| P-06 | Beklenen Teslimatlar | MASTER3-P-03, P-09 |
| P-07 | Mal/Hizmet Kabulü | MASTER3-P-04, P-10; PDW-01 |
| P-08 | Gelen Faturalar | MASTER3-P-05, P-11; PDH-01 |
| P-09 | Üçlü Kontrol | MASTER3-P-05, P-11; PDW-02 |
| P-10 | Red / İade / Düzeltme | MASTER3-P-07; DCR-01 |
| P-11 | Satın Alma Onayları | – (referans yok) |
| P-12 | Tedarikçi Performansı | – (referans yok) |

| ID | Ekran / iş | Sev. | Bağımlılık |
|---|---|---|---|
| UI-01 | P-01 İhtiyaç Havuzu: kaynak filtresi, onay/ret, "siparişe dönüştür" | K2 | API-01…03 |
| UI-02 | P-02 Teklif / Tedarikçi karşılaştırma (A6, ALS-D11) | K2 | API-80/81 |
| UI-03 | P-04 sipariş formunda fiyat, iskonto, para birimi, vade, termin; toplamlar kartı | K2 | API-20…22 |
| UI-04 | P-05 Sipariş 360 sekmeleri (Satış `SalesOrderTabs` deseni): Satırlar · Tedarik & Termin · Mal Kabul · Faturalar · Belgeler · Revizyon | K2 | API-13, 14 |
| UI-05 | İptal / satır kapatma panelleri (etki önizlemeli) | K2 | API-10, 11 |
| UI-06 | P-04 düzenleme: revizyon, satır ekle, ürün kırılımı; P-05 revizyon geçmişi; tedarikçi teyidi | K2 | API-12, 13, 17, 18 |
| UI-07 | P-05 termin dilimleri ve sevk bildirimi; P-06 Beklenen Teslimatlar | K2 | API-30…32 |
| UI-08 | P-07 Mal/Hizmet Kabulü (PDW-01): tolerans bandı, kantar fişi, istisna talebi | K2 | API-40…44 |
| UI-09 | P-08 Gelen Faturalar (PDH-01 merkez, faturaya hazır, içe aktarma/eşleştirme) ve P-09 Üçlü Kontrol (PDW-02) | K2 | API-50…55 |
| UI-10 | P-10 Red/İade/Düzeltme (DCR-01) ve tedarikçi iadesi | K2 | API-60…62 |
| UI-11 | Fason hizmet siparişi formu (hizmet satırı + gönderilecek malzeme + beklenen çıktı) | K2 | API-70…72 |
| UI-12 | P-11 Satın Alma Onayları (Work filtresi) | K2 | MOD-10 |
| UI-13 | P-12 Tedarikçi Performansı ve raporlar | K2 | API-90…92 |

Persona önerileri (grant değildir; karta giriş yalnız Sinan'ın cümlesiyle): Satın alma uzmanı talep/sipariş girer,
Satın alma müdürü onaylar; Depo kabul kaydeder; Muhasebe gelen faturayı eşleştirir. **PRS-01** persona kartı satırları
(GP, K3), **PRS-02** depocunun fiyatsız sipariş görünümü (K3).

---

## 8. Kararlar

2026-11-02'de dört turda on beş soru soruldu; cevaplar `SINAN_KARARLARI_2026-11-02_ALIS.md` (ALS-D1…D13). Sonraki iki
karar dosyası: `_ALIS_EK1.md` (ALS-D14…D20, §13.4) ve `_ALIS_EK2.md` (ALS-D21…D28, §13.7); kalan sorular `_ALIS_EK3.md` (ALS-D29…D43, §13.8). Sonraki
turlar `_ALIS_EKn.md` olur. Zaten karara bağlanmış konular yeniden sorulmaz: tolerans bantları ve onaycılar (No 40,
41, 62, 63), iade faturası (No 20), fatura para birimi (No 26; kur günü ALS-D6 ile kabul günü), talep reddi ve sipariş iptali (No.161), siparişsiz
kabulün istisna olması (DEC-PUR-001), kantar kg'ının elle girilmesi (OPEN-003), fason yönü (URT-D15/D17).

| No | Soru | Cevap |
|---|---|---|
| N-01 | Şerit | ALS-D1 (hepsi Claude'da) |
| N-02 | Talep onayı | ALS-D2 (Satın alma müdürü onaylar) |
| N-03 | Sipariş onayı ve tutar eşiği | ALS-D3 (tek onay, eşik kapalı) |
| N-04 | Teklif / tedarikçi karşılaştırma | ALS-D11 (A6 dalgası) |
| N-05 | Fiyat biçimi | ALS-D5 (elle, iskontolu; StokERP ve hubvogal referans) |
| N-06 | Alış faturası kuru | ALS-D6 (Satış kuralı, kabul gününe göre) |
| N-07 | Gelen fatura girişi | ALS-D7 (Mikro'dan içe aktar + eşleştir) |
| N-08 | Fatura farkı | ALS-D8 (her fark, ucuz gelse de, müdür onayı; tolerans 0; No 25 bilinçli olarak değişti) |
| N-10 | Revizyon | ALS-D9 (Rev+1 ve yeniden onay) |
| N-11 | Eksik teslimde kapanış | ALS-D10 (bant içi otomatik öneri + sor; kapanan satır yeniden açılabilir), ALS-D12 (band dışı: Satın alma sorumlusu tek başına) |
| N-21 | Tedarikçi sipariş teyidi | ALS-D13 (isteğe bağlı) |
| N-13 | Kabul ve kalite | ALS-D4 (hemen kullanılabilir) |
| N-15 | Vade ve ödeme planı | ALS-D24 (siparişte, her siparişte farklı olabilir; taksitli ödeme planı) |
| N-22…N-24 | Kontrol kuru günü, kur farkı, Üretim talebi | ALS-D14, D15, D16 (§13.4) |
| N-27 | Ek alış maliyetleri | ALS-D23 (siparişe/kabule masraf satırı); avans açık kalır |

**Açık kalanlar (2026-11-02 sonu):**

| No | Soru | Durum |
|---|---|---|
| N-18 | Tedarikçi performansı ölçütleri | A6'dan önce sorulur |
| N-20 | Alışta tevkifat, satıcı muafiyeti, ihraç kayıtlı/teşvikli KDV, vade farkının matraha girişi, "ödenecek tutar" tanımı | Mali müşavir; teyide kadar ilgili satırlar "hesaplanamıyor" |
| N-36 | Bülten kuru dövizli siparişte kontrol kuru olarak kullanılır mı? | A5'ten önce sorulur |
| – | Sipariş numarası algoritmasının parçaları (ALS-D36) | MIG-01 alan eşlemesinde hubvogal kayıtlarından çıkarılır |
| – | N-19: hubvogal erişimi göç (MIG-01) için yeniden gerekecek (karar değil, bağımlılık) | MIG-01'den önce |
| N-27 (kalan) | Tedarikçiye avans; gümrük/ithalat masrafları | A5'ten önce sorulur |
| N-28 (kalan) | kg ↔ adet ↔ metre çevrimi ürün masterından mı gelir | A3'ten önce (Ürün şeridiyle) |

Bunlar dışındaki N soruları cevaplandı (ALS-D1…D43).

---

## 9. Satış ve Üretim'den yeniden kullanılacaklar

| Kaynak | Alış'ta kullanım |
|---|---|
| Ortak `Money`, `ExchangeRate`, `PreviousBusinessDayExchangeRate`, `ExchangeRateBulletinDay` (BuildingBlocks) | TEK-01, API-20/21 |
| Ayarlar vergi masterı ve `TaxMasterCalculatorV1` | API-20 |
| Satış politika kaydı, üretim tolerans profili yaşam döngüsü | API-95…97 |
| İptal/zorla kapatma etki önizlemesi | API-10, 11 |
| Revizyon geçmişi ve fark | API-12, 13 |
| `SalesOrderStatusAxes`, `SalesOrderTabs`, `DocumentsWorkspace`, PDF eylemi | UI-04, API-16 |
| Satış fatura dalgası (taslak → dışa aktar → dış eşleştir → kesinleştir, kontrol merkezi) — **henüz yazılmadı, §13 B-2** | API-50…55, API-61 |
| Onay deseni (hazırlayan ≠ onaylayan) | API-01, 15, 40, 53 |

Ayrıntılı eşleme ve ters/özgü kısımlar §2.5'tedir. Kopyalama yerine ortak katmana yazılır (TEK-03, TEK-04); Satış
dosyaları bu şeritte değiştirilmez, Satış'ın geçişi görev notuyla bırakılır.

---

## 10. Sıralı yol haritası

| Dalga | İçerik | Ön şart |
|---|---|---|
| **A0** | Bu plan ve karar dosyaları; `SISTEM_OF_RECORD_MATRISI.md` ve `DURUM_MAKINELERI.md` Alış satırları; FZL-01…03; TEK-05…08; hubvogal alan eşlemesi (MIG-01 hazırlığı) | – |
| **A1** | PRS-01 (persona kartı satırları, §13.3); TEK-09 durum makinesi; API-01 (talep onay/ret), API-02 (kaynak alanı, A-2), API-03 + UI-01; API-10 + UI-05 (No.161 iptali); FZL-04 | ALS-D2, ALS-D16, ALS-D17 |
| **A2a** | TEK-01…03; API-20…22 + UI-03 (fiyat, fiyatsız onay, vergi, kur, vade + ödeme planı, işlem türü); API-23 (masraf ve iskonto satırı); API-98 (Ayarlar listeleri); API-15 + API-96 (onay eşiği); PRS-02 (depocunun fiyatsız görünümü). Fiyat modeli baştan satır birimine bağlı kurulur (ALS-D19; A3'te migration gerekmesin) | ALS-D3, D5, D6, D18, D20, D21, D23, D24, D27, D28; Ayarlar merge sırası (B-8); ALS-D38 (vade farkı, API-20); ALS-D29 (fason masraf değil) |
| **A2b** | API-12/13/17/18 + UI-06 (revizyon, kırılım, teyit, Tedarik No); API-14 + UI-04 (sekmeler, eksenler); API-16 (PDF) | A2a; ALS-D9, D13, D20, D34 |
| **A3** | API-95 + API-40 (kabul toleransı, kantar fiş no), API-41 (hizmet kabulü), API-42…44 + UI-08; API-11 (satır kapatma, tolerans profiline bağlı); API-30…32 + UI-07 (termin, tedarikçide hazır, yolda); MOD-06, MOD-07, MOD-14 (görev notu `ALIS_URETIM_DEVIR_v1.md`: Stok'ta gelecek arz içinde "tedarikçide hazır" satırı), MOD-16 (Satış görev notu); adet/metre birimleri (ALS-D19, Ayarlar onayından sonra) | API-95 Ayarlar kaydı (B-8); ALS-D33, D43, D30, D39 (API-31) |
| **A4** | API-70…72 + UI-11 + MOD-01 (fason, A-1); MOD-02 (Üretim talebi, ALS-D16); MOD-03 (Satış bağlı satın alma, A-3) | A1, A2a; MOD-03 için Satış S6 (API-11, MOD-05) sözleşmesi |
| **A5** | MOD-12 (outbox); API-50…55 + API-97 + UI-09 (gelen fatura, üçlü kontrol, kur farkı); MOD-04; API-60…62 + UI-10 (iade) | A2b, A3; MOD-15 (B-1), B-2 kararı, ERP-000F (B-3); ALS-D7/D8/D14/D15, ALS-D41, ALS-D42; iade için Stok iade çıkış portu (MOD-06) |
| **A6** | API-80/81 + UI-02 (teklif, ALS-D11); API-90…92 + UI-13 (raporlar, performans); MOD-10 + UI-12 (onaylar); MIG-01 (hubvogal göçü); kabul taraması AT-025…028; production aktivasyonu insan kararıdır | A5; N-19 (hubvogal erişimi) |

Bu tablo §13.3 düzeltmelerini ve §13.7 kararlarını içerir (2026-11-02, v1.2).

---

## 11. Test planı

| Katman | Ne | Nerede |
|---|---|---|
| Birim | Durum geçişleri, Σ-pay, tolerans bantları, fiyat/vergi hesabı (ortak calculator golden), iptal kalanı formülü (`INV-006`) | `tests/GoymenERP.Purchasing.UnitTests` |
| Gerçek SQL | Şirket izolasyonu, replay, sürüm çakışması, Stok ile tek transaction, iptal sonrası gelecek arzın düşmesi, fatura kesinleşmesinin tek açık kalem üretmesi | `tests/GoymenERP.Api.IntegrationTests` |
| Sözleşme | OpenAPI drift, frontend `openapiContract.test.ts`, port sözleşme testleri (A-1/A-3) | `tests/openapi_contract_drift`, `src/purchasing` |
| Tarayıcı | Talep → onay → sipariş → onay → kabul (tolerans bandı) → fatura → üçlü kontrol → iade; F5, şirket değişimi, cevabı kaybolan istek | `tests/e2e/purchasing-pur01` genişlemesi |
| Erişilebilirlik | axe taraması | frontend |

Her PR'da: karar referansı, CompanyId ve yetki testi, audit/idempotency/concurrency etkisi, UI durumları, migration
geri dönüş planı.

---

## 12. Durum (2026-11-02)

Bu bölüm iş takibidir; karar değildir.

| İş | PR | Durum |
|---|---|---|
| Bu plan + karar dosyası ALS-D1…D13 | #884 | merge edildi (`ea3f0a19`) |
| §13 yeniden analiz (v1.1) + EK1 ALS-D14…D20 | #887 | merge edildi (`4475e89b`) |
| Hubvogal incelemesi (§13.6/§13.7) + EK2 ALS-D21…D28 | #889 | merge edildi (`f7ec453e`) |
| Kalan sorular (§13.8) + EK3 ALS-D29…D43 | #891 | taslak PR |
| A0 belgeleri: FZL-01 (Purchasing README kodla eşit), `SISTEM_OF_RECORD_MATRISI.md` ve `DURUM_MAKINELERI.md` Satın Alma satırları | – | taslak PR |

Sıradaki: A0 (belge ve temizlik), ardından A1 (PRS-01, talep onay/ret, kaynak alanı, No.161 iptali). Açık sorular §8
sonundaki tabloda.

---

## 13. Yeniden analiz (v1.1, 2026-11-02)

Plan merge edildikten sonra varsayımları kodla tek tek doğrulandı ve Sinan'ın paylaştığı hubvogal ekranları incelendi.
Bu bölüm planın önceki bölümlerini düzeltir; çelişkide plan içinde bu bölüm geçerlidir. **Karar dosyalarını
geçersiz kılmaz;** karar gerektiren her nokta soru olarak yazılmıştır.

### 13.1 Planı değiştiren bulgular

| No | Bulgu | Kanıt | Etki | Yapılacak |
|---|---|---|---|---|
| B-1 | Finans açık kaleminde **yön yok** (alacak/borç ayrımı tutulmuyor). Satış risk okuması kalemleri örtük olarak müşteri alacağı kabul ediyor | `FinanceOpenItemRow` (yön alanı yok); `FinanceSalesPartyRiskReadPortV1` yalnız `ApprovedOfficialInvoiceInstallment` türünü toplar, başka tür görürse `Unavailable` döner | Aynı firma hem müşteri hem tedarikçi olabilir (DEC-CAR-001). Alış faturası aynı türle yazılırsa borç alacak sayılır; yeni türle yazılırsa o firmanın Satış risk okuması tamamen kapanır. İkisi de zararlı | **MOD-15** (K3) A5'in ön şartı; Finans dosyaları bu şeritte değiştirilmez, görev notu `ALIS_FINANS_DEVIR_v1.md`; netleştirme yok → ALS-D42 |
| B-2 | Satış'ın fatura dalgası **henüz yazılmadı** (Satış sırası #11–12) | Satış'ta fatura servisi yok; yalnız `sales.invoices.read` izni ve faturalanabilir sevk okuma portu var | Plan §9'daki "Satış fatura dalgası yeniden kullanılır" şimdilik geçersiz; iki şerit aynı çekirdeği ayrı yazarsa çakışır | Fatura çekirdeği (dosya içe aktarma, eşleştirme, kesinleştirme, kontrol merkezi) **tek** yazılır; hangi şeridin önce yazacağı `ALIS_SATIS_DEVIR_v1.md` notuyla Satış şeridiyle kararlaştırılır |
| B-3 | Platform outbox dağıtıcısı (ERP-000F) **yok**; manifestte `ready`, backlog'da şerit `codex`. Work ve Stok kendi outbox tablolarını tutuyor, dağıtan yok | `task_manifest.json` ERP-000F; `WorkOutboxRow`, `InventoryOutboxRow` | `purchasing.invoice.finalized.v1` Finans'a ulaşamaz | MOD-12 değişmez; ERP-000F A5'ten önce yapılmalı. Codex şeridi yapmazsa Claude şeritleri (Satış/Alış) arasında kim yapacağı Issue #66'ya `NEEDS-SINAN` olarak sorulur |
| B-4 | Alışta resmî faturayı ve kurunu **tedarikçi** düzenler; bir fatura farklı günlerdeki kabulleri kapsayabilir | Satış'ta kur ERP'den çıkar (SAT-D90/D172); alışta fatura dışarıdan gelir | ALS-D6 tek başına uygulanamıyordu | Sinan'a soruldu: ALS-D14 (her kabul payı kendi günüyle kontrol), ALS-D15 (kur farkı müdür onayı). ERP kuru alışta **kontrol kuru** olarak faturaya sabit yazılır; bu, EK1 §1a'da bilinçli değişiklik olarak kayıtlı. Finans'a giden TL karşılığı tedarikçi faturasındaki kurla → **ALS-D41**. Not: çok günlü faturada tek resmî kur ile kabul başına kontrol kurları çoğu zaman farklı çıkar; ALS-D15 onayı bu durumda sık tetiklenir (Satış'taki SAT-D101 "kur gününe göre ayrı fatura" çözümü tedarikçiye dayatılamaz) |
| B-5 | Hubvogal Göymen'in **şu an kullandığı** sistem: Sinan'ın 2026-11-02 ekran görüntülerinde 157 sayfa canlı alış siparişi var (Ekim 2026 tarihli, "Sevkiyat Sürüyor"). Göç stratejisi "açık satın alma siparişleri taşınır" diyor (madde 5); StokERP'de sipariş yoktu | Ekran görüntüleri (§13.6); `MIGRASYON_STRATEJISI.md` | Planda göç işi yoktu | Yeni **MIG-01** (aşağıda) |
| B-6 | Kabul anında serbest bırakma **zaten kodda**: `PurchaseGoodsReceiptApiV1.InitialBlock = "Free"` | `PurchaseGoodsReceiptContractsV1.cs` | API-43 küçülür | API-43 yalnız "kalite kullanıcısı sonradan bloklar → iade yolu" bağıdır (MOD-06 içinde) |
| B-7 | Kantar tartımı Lojistik'te yalnız **satış yükleme talimatına** bağlı (AT-053). Alış kabulünde ölçülen kg zaten var (`MeasuredQuantityKg`) | `RecordWeighingRequest`; `PurchaseGoodsReceiptContractsV1` | Eksik olan yalnız kantar fiş numarası | İlk sürüm: Alış kabulüne kantar fiş no alanı (OPEN-003 elle giriş). Lojistik tartım kaydına bağ MOD-07'de sonra |
| B-8 | Alış teslim toleransı için Ayarlar'da **politika kaydı yok**; Satış toleransı Satış politika kataloğunda, Üretim'inki ayrı tabloda | `ISettingsSalesPolicyProviderV1`, `ManufacturingTolerancePolicyRow` | API-95 yeni Ayarlar kaydı ister (migration); Üretim ve Satış şeritleri de Ayarlar'da çalışıyor | API-95 K3; A2a'daki API-96 de Ayarlar'a yazar. Merge sırası iki şeritle notla ayarlanır |

**MIG-01 — hubvogal'den göç (K3).** Kapsam: açık alış siparişleri (kısmi kabul edilmiş miktarlar dahil), kabul edilip
faturalanmamış paylar, açık tedarikçi borcu (`MIGRASYON_STRATEJISI.md` madde 5, 7–8). Tedarikçi kartları Cari'nin göç
sırasıdır (madde 2); Alış yalnız Cari'deki tedarikçiye bağlanır. Adımlar: alan eşleme (A0) → deneme taşıma →
mutabakat raporu (sipariş sayısı, kalan kg, tutar) → canlı taşıma insan kararıyla. Canlıya alma günü hubvogal'de alış
kapanır; o günden sonra alış irsaliyesinin Mikro'ya aktarımı Göymen'den yapılır (ALS-D31; aktarım işi A5'te
fatura/Mikro tarafıyla birlikte). Seviye K3: veritabanına, paraya ve
gelecek arza (`INV-006`) dokunur. Ön şart: N-19 (hubvogal erişimi) ve ALS-D17 durum eşlemesinin kayıt örnekleriyle
doğrulanması.

### 13.2 Kapsam netleştirmeleri

- **İşletme alımları kapsam dışıdır.** Sarf, bakım, yedek parça, genel gider ve sabit kıymet alımları master §7.4 ve
  §27.4'e göre ayrı modüldür (IA-01, IA-02). N-17 yalnız ticari hizmet alışını sorar.
- **Üretim'in açtığı talepler** normal talep gibi müdür onayı bekler (ALS-D16; ALS-D2'ye istisna yoktur).
- **Ek alış maliyetleri ve avans** (nakliye, gümrük, ithalat masrafı; tedarikçiye ön ödeme) master raporda ve planda
  yoktu. Hubvogal siparişinde "Lojistik Firması" alanı olması nakliyenin siparişle ilişkilendirildiğini gösteriyor.
  Sorular **N-27**.

### 13.3 Dalga düzeltmeleri (§10'a işlendi)

| Değişiklik | Neden |
|---|---|
| PRS-01 A6'dan A1 başına | A1'den itibaren her dilim yeni izin ekliyor; kart gecikirse ekranlar gerçek kullanıcıyla denenemez. Grant insan kapısıdır |
| A2 → A2a (fiyat, vergi, kur, vade, onay eşiği, işlem türü) + A2b (revizyon, kırılım, teyit, eksenler, PDF) | A2 çok büyüktü; fiyat olmadan revizyonun fiyat kısmı yazılamaz |
| PRS-02 A6'dan A2a'ya | Fiyat A2a'da geliyor; depo/kabul personeli o andan itibaren fiyatı görmemeli |
| A5 ön şartlarına MOD-15, B-2 kararı, ERP-000F (N-25/N-26 → ALS-D41/D42) | B-1…B-4 |
| MIG-01 A6'ya; alan eşlemesi A0'a | B-5 |
| Adet/metre birimleri A3'e (Ayarlar onayından sonra) | ALS-D19 |

### 13.4 Sorular

Cevaplananlar (`SINAN_KARARLARI_2026-11-02_ALIS_EK1.md`): N-22 → ALS-D14 (her kabul payı kendi günü); N-23 → ALS-D15
(kur farkı müdür onayı); N-24 → ALS-D16 (Üretim talebi de onay bekler); hubvogal soruları → ALS-D17…D20.

Sonuç: API-52 üçlü kontrole **kur farkı** türü eklenir; API-53 fiyat, miktar ve kur farkını aynı onay akışında toplar;
MOD-02'de Üretim'in çağrısı talebi Gönderildi durumunda açar; API-20 fiyatsız satırı kabul eder (ALS-D18); sipariş
başlığına Tedarik No ve işlem türü (sipariş pozisyonu) eklenir (ALS-D20).

Yeni açık sorular:

| No | Soru |
|---|---|
| N-25 | Gelen faturanın Finans'taki TL karşılığı hangi kurla: tedarikçinin resmî kuru mu, ERP kontrol kuru mu? (onaylı farkta) → **ALS-D41** |
| N-26 | Aynı firmanın tedarikçi borcu Satış risk hesabından düşülsün mü (netleştirme)? Önerilecek seçenek: düşülmez, borç kalemleri risk okumasından süzülür. → ALS-D42: düşülmez |
| N-27 | Ek alış maliyetleri: ALS-D23 ile cevaplandı (masraf satırı). Açık kalan: tedarikçiye avans ve gümrük/ithalat masrafları |
| N-28 | Adet ve metre birimleri: metre kaç ondalık; kg ↔ adet ↔ metre çevrimi ürün masterından mı gelir; adet/metre için kabul toleransı → **ALS-D43** |
| N-29 | Sipariş numarası: hubvogal biçimi (`0213-01-72-261026-024`) korunsun mu, yeni numara + eski numara referansı mı? → **ALS-D36** |

### 13.5 Büyüklük ve darboğazlar

Plan yaklaşık 50 API, 15 MOD ve 13 ekran işi içerir; Satış'taki tempo (dilim başına bir PR, K3'te iki review) ile
kabaca **40–50 PR** eder. Darboğazlar kod değil, başka şeritlere bağlı kararlardır:

| Dalga | Dış bağımlılık |
|---|---|
| A0, A1 | Yok; hemen yürür |
| A2a, A3 | Ayarlar'da merge sırası (API-96, API-95; B-8) |
| A4 | MOD-03 için Satış S6 sözleşmesi; MOD-01/02 için Üretim şeridi |
| A5 | MOD-15 (Finans), B-2 (fatura çekirdeği sahibi), ERP-000F |
| A6 | Hubvogal erişimi (MIG-01) |

### 13.6 Hubvogal incelemesi (2026-11-02)

Kaynak: önce Sinan'ın paylaştığı iki ekran görüntüsü, sonra aynı gün VPN'li bir ofis bilgisayarı üzerinden doğrudan okuma.
Uygulama kendini "HubVision / Hades Steel ERP" olarak tanıtır (ASP.NET MVC). Yalnız sayfalar okundu (22 sayfa: alış
sipariş listesi, ekleme, bir sipariş detayı, alış irsaliyeleri, alış raporları, tanım listeleri); **hiçbir kayıt
değiştirilmedi**, oturum kapatıldı, uzak bilgisayardaki geçici dosyalar silindi. Erişim bilgisi repoya yazılmaz.
Hubvogal yalnız referanstır; kör kopya yoktur.

**Akış.** Hubvogal'de alış = sipariş ("bağlantı") → sevk "sıra"ları → alış irsaliyesi (stoğa giriş, depo + lokasyon +
etiket) → irsaliyenin Mikro'ya aktarılması ("Cariye işlendi"). **Alış faturası ekranı yoktur**; faturalar Mikro'dan
okunan raporda (Fatura Listesi: Borç/Alacak, evrak cinsi) görünür. Bu, ALS-D7 (Mikro'dan içe aktar + eşleştir) ile
uyumludur; okunan sayfalarda sipariş–kabul–fatura üçlü kontrolüne (P-09) karşılık gelen bir ekran görülmedi.

| Hubvogal'de | Göymen karşılığı |
|---|---|
| **Sipariş listesi:** Tedarik No, Sipariş No, Eklenme Tarihi, Firma (bizim şirket), Cari, Sipariş Tipi, Personel, Onay Durumu (Rezerve / Onaylanan), Sevk Durumu (Sevkiyat Sürüyor / Tamamlanmış), Toplam (KG), Toplam (tutar, $ veya ₺) | P-03; şirket bağlamı `CompanyId`'den; "sil" yerine iptal (No.161); onay durumları ALS-D17 |
| **Başlık:** tedarikçi (yanında cari bakiye: Kur, Toplam Borç, Toplam Alacak), bizim şirket (üç Göymen şirketi ve bir deneme şirketi), teslim/fatura adresleri (bizim + tedarikçi), Sipariş Geçerlilik Tarihi, Tedarik No, Lojistik Firması, Vade, Kur + Kur Tutarı (elle), Sipariş Pozisyonu, **Nokta Vade (Ödeme Planı, 1–12 taksit)**, ön sipariş bağı | UI-03; Tedarik No ve işlem türü (ALS-D20); geçerlilik tarihi; taşıyıcı; vade + ödeme planı (ALS-D24, API-22); sipariş kuru bilgi amaçlı (API-21); cari bakiye özeti Finans okumasından (MOD-15 sonrası) |
| **Sipariş Pozisyonu** listesi: Hizmet, Ticari, İade, Alış Fason, Satış Fason, Takas, Teşvikli, Temel, İhracat, İhraç Kayıtlı; masterda "Fason Durumu" bayrağı ve TR/EN ad | ALS-D20 işlem türü listesi (Ayarlar). "Alış Fason" = A-1 fason hizmet siparişi; "Satış Fason" Satış şeridinin S-3'ü (Alış listesine alınmaz); "İade" ayrı tedarikçi iadesi belgesine (API-60, No 20) yönlendirir, siparişte ikinci bir iade yolu açmaz; "Takas" ve "Temel" alınmaz → ALS-D37 |
| **Hizmet Tipleri:** rulo boy açma, rulo dilme, profil fason, boy açma + dilme | API-70 hizmet satırı tipleri (Ayarlar listesi) |
| **Satır:** malzeme **veya** hizmet tanımı + hizmet tipi; birim (KG / Adet / Metre); sipariş miktarı; birim fiyat; KDV oranı; **tevkifat oranı, tevkif edilen KDV, gerçek KDV ("G. KDV" = KDV − tevkifat)**; KDV dahil fiyat; **vade farkı** (% veya tutar); iskonto (% veya tutar); stok işlem tipi (kusur/kalite sınıfı: dalga, kırık, kenar bozuk, çapak, 2. kalite…); satır notu | API-20: tutar, KDV, tevkifat ve "gerçek KDV" ortak calculator'dan (N-20 mali müşavir); vade farkı → ALS-D38; birim ALS-D19; kusur sınıfı ürün/etiket niteliğidir (Ürün/Stok; Alış yalnız taşır) |
| Detayda her birim için **Sipariş / Net / Kalan**; fiyat görünümü "siparişe göre / cariye göre", TL ve USD, KDV hariç / dahil / G.KDV dahil | API-14 eksenleri; UI-04 |
| **Bağlantı ve sıra:** raporlarda Bağlantı Miktarı, Net Miktar, Bağlantıdan Kalan, Bağlantı No; sevkler "Sıra No / Sıra Tarih" ile düşer | ALS-D21: bağlantı = onaylı sipariş satırı, fiyat sabit; sıralar termin dilimleri ve kabuller (API-30, kabul payları) |
| **U.S Hazır** (üretici stokta hazır) ve **Bülten Hazır** miktarları; bülten no / yurtiçi-yurtdışı bülten tarihi / bülten kuru | ALS-D22 + ALS-D25: tedarikçide hazır durumu (API-31); mal ve fatura üreticide olduğundan gelecek arzdır, fiziksel stok değil (MOD-14, MOD-16). Bülten kuru ve tarihi fiyat referansıdır (EK2 §2 yorumu; kontrol kuru olarak kullanımı N-36) |
| **Sevk/irsaliye:** slot no, brüt/net sevk miktarı, sevk plan tarihi, plaka, dorse, şoför adı/telefon/TC, nakliye firması; irsaliye durumu Sevkiyatta / Onaylı / Zarflandı / Cariye İşlendi; Mikro şubesi | API-31 sevk bildirimi + P-07 kabul; brüt/net = kantar (B-7); şoför ad/telefon/TC/plaka tutulur, 10 yıl saklama → ALS-D39 (yetkili, maskeli, audit'li; imha ayrı insan kapısı). Hubvogal alış irsaliyesini de Mikro'ya aktarıyor; birlikte kullanım yok; canlıya alma gününden sonra Mikro aktarımı Göymen'den → ALS-D31 |
| **Stoğa giriş:** depo (Göymen depoları + tedarikçi depoları: İSDEMİR, ERDEMİR, TOSYALI…), lokasyon (ham / işlenmiş / hurda, fason, U.S hazır, bülten, konsinye fason), iç/dış lokasyon, menşei (üretici), alt kalite (çelik kalite kodu), etiket | Mevcut kabul (Stok katılımcısı); tedarikçi deposundaki **bizim** mal dış lokasyondur (OPEN-002 KAPANDI, DEC-ORG-002); U.S/bülten hazır mal ise ALS-D25 gereği henüz bizim değildir; menşei ve alt kalite etiket niteliği |
| **Masraflar ve iskontolar** siparişe ve irsaliyeye eklenir: Nakliye; Fason boy kesme / dilme / profil / dilme+profil / dilme+boy kesme / firkete büküm; Kredi kartı komisyonu; Finansman bedeli (tutar + KDV). "Maliyet Tipleri" ve hiyerarşik "Masraf Tipleri" ayrı iki liste | ALS-D23, ALS-D27, API-23, API-98; "Fason – …" masraf tipleri masraf satırı olmaz (ALS-D29) |
| Sipariş içinden yeni ürün kartı açma (kategori, marka, ölçü, min/max fiyat) | Yapılmaz: ürün kartı Ürün modülünün işidir (modül sınırı); sipariş formu yalnız mevcut ürünü seçer |
| Bağlı satış siparişleri ve rezerve stok listesi; cari limit / limit boşluğu | MOD-03 (A-3); limit Finans/Cari okuması |
| Raporlar: Sevkiyat Raporu – Alış, Sipariş Raporu – Alış (Gerçekleşen Tutar, Sipariş Kalan Tutar, Sevkiyat Kalan Tutar; ürün kategori grubu filtresi) | API-90…92; açık sipariş raporuna kalan tutar ve kategori grubu eklenir |
| "Araç Yükleme Talimatı" ISO 9001 formu | Lojistik belgesi (Satış/Lojistik'te var); Alış'ta karşılığı yok |
| Alış formunda "Satış Temsilcisi Notu" etiketi | Hubvogal alış formu satış formundan türetilmiş; §2.5 "Satış aynası" gözlemini doğruluyor |

**MIG-01 için alan notları:** sipariş no biçiminin parçaları (`0213-01-72-261026-024`; numaralar taşınmaz, yalnız algoritma çıkarılır, ALS-D36), pozisyon ve hizmet tipleri, ödeme
planı taksitleri, bağlantı/sıra kalanları, U.S/bülten hazır miktarları, tedarikçi depoları ve menşei listesi taşınacak
alanlardır. Hubvogal'de onaylı siparişlerin çoğu fiyatsızdır (ALS-D18).

### 13.7 Hubvogal sonrası kararlar ve yeni sorular

Cevaplananlar (`SINAN_KARARLARI_2026-11-02_ALIS_EK2.md`): ALS-D21 (bağlantı fiyatı sabit), ALS-D22 (tedarikçide hazır
durumu; satış ve üretim görür; düzeltilebilir), ALS-D23 (masraf satırı), ALS-D24 (vade ve ödeme planı siparişte),
ALS-D25 (U.S/bülten hazır gelecek maldır, stok değil), ALS-D26 (fason/hizmet aynı iş olabilir; kayıt yolu açık),
ALS-D27 (masraf satırında farklı cari), ALS-D28 (taksit toplamı ödenecek tutara eşit; Cari önerisi yok).

Önceki review notlarından kapatılanlar (#887 P3): §8 karar özeti; N-15 (satır vadesi → ALS-D24 sipariş başlığında
ödeme planı); N-26 varsayılanının cevapsız uygulanmaması; fiyat modelinin satır birimine bağlanması (A2a); "iade"
işlem türü ile API-60 ilişkisi (yukarıdaki tablo); kontrol kurunun kabul payı başına saklanması (ALS-D14, API-52).

Yeni açık sorular:

| No | Soru |
|---|---|
| N-30 | Satış siparişi "tedarikçide hazır" (ALS-D22) miktar üzerine rezerve edilebilir mi, yoksa yalnız görünür mü? (Satış/Stok şeritleriyle) → **ALS-D30** |
| N-31 | Sipariş pozisyonundaki "Takas" ve "Temel" ne anlama geliyor, Göymen'de karşılığı ne? → **ALS-D37** |
| N-32 | Satırdaki vade farkı (% veya tutar): alışta kullanılıyor mu, fiyata mı eklenir, ayrı satır mı? → **ALS-D38** |
| N-33 | Sevk bildiriminde şoför TC kimlik no gibi kişisel veriler tutulacak mı (saklama politikası)? → **ALS-D39** |
| N-34 | (ALS-D25 ile cevaplandı: U.S/bülten hazır gelecek maldır) |
| N-35 | Fason işlem (kesme, dilme, profil, büküm) ayrı fason hizmet siparişiyle mi (malzeme fasoncuya gider), alış siparişine masraf satırıyla mı (tedarikçi işleyip getirir) kaydedilir? ALS-D26'da soru anlaşılmadı; örnekle yeniden sorulur → **ALS-D29** |
| N-36 | Bülten kuru dövizli siparişte kontrol kuru olarak kullanılır mı? |
| N-37 | Geçiş döneminde (hubvogal ve Göymen ERP birlikteyken) alış irsaliyesi Mikro'ya hangi sistemden aktarılır? → **ALS-D31** |

### 13.8 Kalan soruların cevapları (EK3)

`SINAN_KARARLARI_2026-11-02_ALIS_EK3.md` (ALS-D29…D43). Plana etkileri:

| Karar | Etki |
|---|---|
| ALS-D29 fason = yalnız sahipliği değişmeyen mal | N-35 kapandı. Giden fason yalnız A-1 fason hizmet siparişi (API-70…72); masraf satırı (API-23) fason için kullanılmaz; tedarikçinin işleyip sattığı mal normal alıştır. Gelen fason Satış'ta |
| ALS-D30 tedarikçide hazır mal satışa ayrılabilir | N-30 kapandı. MOD-16 (Satış kaynak planı) ve MOD-14 (Stok tahsis motoru) görev notlarına "gelecek mal olarak ayırma, aynı ton bir kez" eklenir (notları Alış şeridi A3 başında yazar: `ALIS_SATIS_DEVIR_v1.md`, `ALIS_URETIM_DEVIR_v1.md`); Alış okuma portu ayrılan miktarı da gösterir |
| ALS-D31 birlikte kullanım yok | N-37 kapandı. MIG-01: canlıya alma günü açık siparişler taşınır, hubvogal'de alış kapanır, Mikro aktarımı Göymen'den |
| ALS-D32 fazla teslim aynı fiyatla faturaya | N-09 kapandı. API-52 üçlü kontrolde fazla teslim payı sipariş fiyatıyla karşılaştırılır |
| ALS-D33 termin dilimleri ilk sürümde | N-12 kapandı; API-30 A3'te kalır |
| ALS-D34 fiyatlı sipariş PDF'i | N-14 kapandı; API-16 A2b, PRS-02 ile depocu fiyatsız görür |
| ALS-D35 siparişsiz kabulü Satın alma müdürü onaylar | N-16 kapandı; API-42 A3 |
| ALS-D36 aynı algoritma, yeni başlangıç | N-29 kapandı; numara serisi Ayarlar'da, parçaların anlamı MIG-01'de doğrulanır |
| ALS-D37 Takas ve Temel alınmaz | N-31 kapandı; API-98 pozisyon listesi: Ticari, Hizmet, İade, Alış Fason, Satış Fason, Teşvikli, İhracat, İhraç Kayıtlı |
| ALS-D38 vade farkı: peşin + fark veya tek vadeli fiyat | N-32 kapandı; API-20 iki giriş biçimi; vergi etkisi N-20 |
| ALS-D39 şoför ad/telefon/TC/plaka, 10 yıl | N-33 kapandı; API-31 kişisel veri alanları yetkili, maskeli, audit'li; imha işlemi ayrı insan kapısı |
| ALS-D40 fason dışı hizmet alışı Alış'ta | N-17 kapandı; API-41/API-70 hizmet satırı |
| ALS-D41 TL borç tedarikçi faturasındaki kurla | N-25 kapandı; MOD-04 (görev notu `ALIS_FINANS_DEVIR_v1.md`, Alış şeridi A3 başında yazar ki Finans şeridi A5'e kadar bitirebilsin) |
| ALS-D42 tedarikçi borcu satış riskinden düşülmez | N-26 kapandı; MOD-15 (aynı Finans görev notu; A5'ten önce yazılır) |
| ALS-D43 metre 2 ondalık, tolerans kg ile aynı | N-28 kapandı; adet/metre A3'te Ayarlar profiliyle |
