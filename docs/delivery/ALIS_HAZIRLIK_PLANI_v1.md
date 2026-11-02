# Alış (Satın Alma) Modülü Hazırlık Planı v1

Tarih: 2026-11-02 · Hazırlayan: Claude (cloud oturumu, Alış şeridi) · Durum: **ÖNERİ** (plan; iş kararı değildir) · Kararlar: **`SINAN_KARARLARI_2026-11-02_ALIS.md` (ALS-D1…D11)**

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
  Bu şerit onların dosyalarını değiştirmez; gereken işi görev notuyla bırakır (`docs/handoffs/ALIS_*_DEVIR_v1.md`).
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
| Test | 17 birim, 15 entegrasyon (gerçek SQL dahil), PUR01 Playwright | `tests/` |

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
| TEK-03 | Ortak komut iskeletini (sürüm → yetki → replay → domain → audit), replay kaydını ve SQL test yardımcılarını paylaşılan katmana çıkar; Alış ilk tüketici (Üretim analizi: "Satınalma, Satış'ın aynası") | Kopya kod | K2 | – |
| TEK-04 | Frontend `pending*.ts` deposunu ve sekme/durum eksenleri bileşenlerini Satış'tan ortak pakete al | Aynı | K1 | – |
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
| API-11 | `POST orders/{id}/lines/{lineId}/closure` | Eksik teslimde satır kalanını kapat ("eksik teslimle kapandı"), iptalden ayrı eksen | `purchasing.orders.close` (yeni) | N-11 | K3 |
| API-12 | `POST orders/{id}/header-revision` · `/line-revision` | Tedarikçi, para birimi, vade, termin; satır ekle/sil/ürün/miktar/fiyat → Rev+1 ve yeniden onay | `purchasing.orders.revise` (yeni) | N-10 | K3 |
| API-13 | `GET orders/{id}/revisions` | Revizyon geçmişi ve fark (P-07) | `purchasing.orders.read` | – | K2 |
| API-14 | `GET orders/{id}/status` | Eksenler: yaşam döngüsü, onay, kabul (tolerans dahil), faturalama, iade, finansal kapanış | `purchasing.orders.read` | §7.3 | K3 |
| API-15 | `POST orders/{id}/approval` genişlemesi | Tutar eşiği / onay politikası Ayarlar'dan; hazırlayan ≠ onaylayan | mevcut | N-03 | K3 |
| API-16 | `GET orders/{id}/pdf` | Tedarikçiye gidecek sipariş PDF'i (Satış MOD-07 deseni, TASLAK filigranı) | `purchasing.orders.read` | N-14 | K2 |
| API-17 | `POST orders/{id}/breakdown` | Domain'de var olan genel/teknik kırılımı HTTP'ye aç (P-12 Ürün Kırılımı) | `purchasing.orders.revise` | §13 | K3 |

### 5.3 Fiyat, vergi, kur

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-20 | Sipariş satırında birim fiyat, para birimi, iskonto; `GET orders/{id}/totals` | Satır tutarı, KDV, tevkifat, toplam (ortak calculator) | TEK-01, N-05 | K3 |
| API-21 | Kur: siparişte bilgi amaçlı; faturada tek kur, Satış kur kaydı ile aynı kaynak | N-06, DEC-FIN-010, No 26 | K3 |
| API-22 | Vade / ödeme koşulu (Finans ödeme planına girdi) | N-15 | K3 |

### 5.4 Termin ve "yolda" (P-03 Tedarik & Termin, P-09)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-30 | `POST orders/{id}/lines/{lineId}/schedule` | Miktar bazlı termin dilimleri (toplam = satır miktarı) | §7.3, N-12 | K2 |
| API-31 | `POST orders/{id}/shipment-notices` | Tedarikçi sevk bildirimi (araç, irsaliye no, kg) → "yolda" | §7.3, N-12 | K2 |
| API-32 | `GET expected-deliveries` | Beklenen teslimatlar; geciken termin | ST-08, L-09 | K2 |

### 5.5 Mal ve hizmet kabulü

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-40 | Mal kabulüne tolerans | Fazla/eksik teslim bandı: uyarı → devam; onay bandı → `purchasing.delivery-variance.approve` tek onay; blok → ret veya iki kademeli istisna (Depo → Satın alma sorumlusu); kantar fiş no zorunlu; politika ürün/işlem bazlı (`PurchaseDelivery`), Ayarlar'dan sürümlü | No 40, 41, 62, 63; OPEN-003/004 | K3 |
| API-41 | `POST orders/{id}/lines/{lineId}/service-acceptances` | Hizmet kabulü: stok hareketi yok; kabul kanıtı + faturaya hazır | §7.3 | K3 |
| API-42 | `POST goods-receipts/without-order` | Siparişsiz kabul yalnız yetkili istisna | DEC-PUR-001, N-16 | K3 |
| API-43 | Kalite serbest bırakma bağı | Kabul edilen mal kaliteden geçene kadar bloklu mu (`InitialBlock`) | N-13 (#66 5743483383) | K3 |
| API-44 | `POST goods-receipts/{id}/correction` | Kabul düzeltmesi: ters + yeni, silme yok | AGENTS.md | K3 |

### 5.6 Gelen fatura ve üçlü kontrol (P-05, P-11, PDW-02)

Satış'taki SAT-D7 deseni: resmî belge dış sistemde; ERP eşleştirir. Finans'taki resmî fatura içe aktarma yeniden
kullanılır; Alış ikinci bir resmî fatura kaydı tutmaz.

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-50 | `GET invoice-candidates` | Faturaya hazır kabul payları (tedarikçi + para birimi gruplaması) | §7.3 | K3 |
| API-51 | `POST incoming-invoices` · `GET …` · `GET …/{id}` | Gelen fatura kaydı: kabul payları seçilir; fatura sipariş para biriminde; tek kur | No 25, 26, N-07 | K3 |
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
| API-70 | Hizmet satırı (`Service` kalem tipi, hizmet birimi veya işlenecek kg) | Hizmet alışı ve fason için ortak satır türü | URT-D15/D17, N-17 | K3 |
| API-71 | Fason hizmet alış siparişi: tedarikçi = Cari fason rolü; gönderilecek malzeme ve beklenen çıktı ürünleri | A-1, DEC-CAR-001, DEC-STK-008 | K3 |
| API-72 | `IPurchaseSubcontractOrderReadPortV1` | Üretim'e: onaylı mı, fason firması, hizmet satırları, malzeme, açık/kapalı | A-1 | K3 |

### 5.9 Teklif (P-02) — ilk sürümde olup olmadığı N-04

| ID | Uç | Amaç | Sev. |
|---|---|---|---|
| API-80 | `POST rfqs` · `POST rfqs/{id}/offers` | Talep için birden çok tedarikçiden teklif | K2 |
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

Satış politika kaydı (`ApprovedSalesPolicyCatalog`) ve üretim tolerans yaşam döngüsü örnek alınır.

---

## 6. Diğer modüllerle eksik ilişkiler (MOD)

| ID | Modül | Eksik | Kim yapar | Sözleşme | Sev. |
|---|---|---|---|---|---|
| MOD-01 | Üretim | Fason hizmet siparişi okuma portu; transferin fason siparişine bağlanması | Alış (port) + Üretim şeridi (tüketici) | `IPurchaseSubcontractOrderReadPortV1` (A-1) | K3 |
| MOD-02 | Üretim | Eksik hammadde için "satın alma talebi oluştur" çağrısı | Alış (alan) + Üretim (eylem) | mevcut talep ucu + kaynak alanı (A-2) | K3 |
| MOD-03 | Satış | İptal/zorla kapatma önizlemesinde "bağlı satın alma"; kaynak planında "satın alma" seçeneği | Alış (port) + Satış şeridi (tüketici) | `ISalesOrderLinkedSupplyReadPortV1` için Alış tarafı okuma (A-3, Satış MOD-05, API-11) | K3 |
| MOD-04 | Finans | Gelen faturanın borç açık kalemi; resmî gelen fatura içe aktarma ve eşleştirme okuması | Alış + Finans sahibi | `purchasing.invoice.finalized.v1` + gelen yönlü içe aktarma | K3 |
| MOD-05 | Ayarlar | Alış politikaları (API-95…97); vergi masterı ve para profili okuması | Alış | mevcut `ISettingsPrerequisiteProvider` genişlemesi | K3 |
| MOD-06 | Stok | Kabul toleransının stok girişine etkisi, kalite blok bağı, tedarikçiye iade çıkışı (bloklu stoktan dahil) | Alış + Üretim/Stok şeridi | mevcut kabul katılımcısı genişlemesi + iade çıkış portu | K3 |
| MOD-07 | Lojistik | L-09 kapı kabulü ve L-10 mal kabulü ile Alış kabulünün tek kayıt olması; kantar oturumu; iade sevki | Alış + Lojistik | mevcut `IPurchaseOpenLineReadPortV1`, `PurchaseDelivery` politikası | K3 |
| MOD-08 | Cari | Tedarikçi ve fason rolü, tedarikçi vade/ödeme koşulu okuması | Alış | mevcut `IPurchasingPartyAccountPortV1` genişlemesi | K2 |
| MOD-09 | Ürün | Tedarikçiye özel alış fiyatı/fiyat listesi (varsa), hizmet ürünleri | Alış + Ürün | N-05 | K2 |
| MOD-10 | Work | Talep, sipariş, fark ve istisna onayları için onay kutusu adaptörü | Alış | mevcut Work onay sözleşmesi | K2 |
| MOD-11 | Belgeler | Sipariş PDF'i, sözleşme ve belgeler sekmesi (P-06) | Alış | `PurchasingDocumentOwnerLinkPort` (mevcut) | K2 |
| MOD-12 | Platform | Outbox/inbox (ERP-000F); Satış'ın "fatura olayını iletecek altyapı" işiyle ortak | Satış şeridiyle sıralı | ERP-000F | K3 |
| MOD-13 | Muhasebe | Gelen fatura vergi kontrolü | Faz 6 sonrası | – | K3 |

---

## 7. Ekranlar (UI)

| ID | Ekran / iş | Sev. | Bağımlılık |
|---|---|---|---|
| UI-01 | P-01 İhtiyaç Havuzu: kaynak filtresi, onay/ret, "siparişe dönüştür" | K2 | API-01…03 |
| UI-02 | P-02 Teklif / Tedarikçi karşılaştırma (N-04'e bağlı) | K2 | API-80/81 |
| UI-03 | Sipariş formunda fiyat, iskonto, para birimi, vade, termin; toplamlar kartı | K2 | API-20…22 |
| UI-04 | Sipariş 360 sekmeleri (Satış `SalesOrderTabs` deseni): Satırlar · Tedarik & Termin · Mal Kabul · Faturalar · Belgeler · Revizyon | K2 | API-13, 14 |
| UI-05 | İptal / satır kapatma panelleri (etki önizlemeli) | K2 | API-10, 11 |
| UI-06 | Revizyon, satır ekle (P-07, P-08), ürün kırılımı (P-12) | K2 | API-12, 17 |
| UI-07 | Termin bildirimi (P-09), beklenen teslimatlar | K2 | API-30…32 |
| UI-08 | Yeni mal/hizmet kabulü (P-10, PDW-01): tolerans bandı, kantar fişi, istisna talebi | K2 | API-40…44 |
| UI-09 | Alış İrsaliye & Fatura merkezi (PDH-01), faturaya hazır, gelen fatura girişi/eşleştirme (P-11, PDW-02), üçlü kontrol | K2 | API-50…55 |
| UI-10 | Red/İade/Düzeltme görünümü (DCR-01) ve tedarikçi iadesi | K2 | API-60…62 |
| UI-11 | Fason hizmet siparişi formu (hizmet satırı + gönderilecek malzeme + beklenen çıktı) | K2 | API-70…72 |
| UI-12 | Alış onayları (Work filtresi) | K2 | MOD-10 |
| UI-13 | Raporlar ve tedarikçi performansı | K2 | API-90…92 |

Persona önerileri (grant değildir; karta giriş yalnız Sinan'ın cümlesiyle): Satın alma uzmanı talep/sipariş girer,
Satın alma müdürü onaylar; Depo kabul kaydeder; Muhasebe gelen faturayı eşleştirir. **PRS-01** persona kartı satırları
(GP, K3), **PRS-02** depocunun fiyatsız sipariş görünümü (K3).

---

## 8. Kararlar

2026-11-02'de üç turda on iki soru soruldu; cevaplar `SINAN_KARARLARI_2026-11-02_ALIS.md` (ALS-D1…D11). Sonraki
turlar `_ALIS_EKn.md` olur. Zaten karara bağlanmış konular yeniden sorulmaz: tolerans bantları ve onaycılar (No 40,
41, 62, 63), iade faturası (No 20), fatura para birimi (No 26), talep reddi ve sipariş iptali (No.161), siparişsiz
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
| N-08 | Fatura farkı | ALS-D8 (her fark müdür onayı; tolerans 0) |
| N-10 | Revizyon | ALS-D9 (Rev+1 ve yeniden onay) |
| N-11 | Eksik teslimde kapanış | ALS-D10 (bant içi otomatik öneri + sor; kapanan satır yeniden açılabilir) |
| N-13 | Kabul ve kalite | ALS-D4 (hemen kullanılabilir) |

**Açık (sonraki tur):**

| No | Soru |
|---|---|
| N-09 | Fazla teslimde fazla kısmın faturalaması (No 40 fazla kısmı siparişe bağlar) |
| N-12 | Termin dilimleri ve sevk bildirimi ilk sürümde mi? |
| N-14 | Sipariş PDF'i tedarikçiye gönderilecek mi, fiyatlı mı? |
| N-15 | Vade ve ödeme koşulu siparişte mi, Cari'den mi gelir? |
| N-16 | Siparişsiz kabul istisnasını kim onaylar? |
| N-17 | Fason dışında hizmet alışı (nakliye, işçilik) bu modülde mi? |
| N-18 | Tedarikçi performansı ölçütleri |
| N-19 | Hubvogal erişimi: yerel ağdaki uygulamaya hangi bilgisayardan (VPN'li) bakılacak |

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
| Satış fatura dalgası (taslak → dışa aktar → dış eşleştir → kesinleştir, kontrol merkezi) | API-50…55, API-61 |
| Onay deseni (hazırlayan ≠ onaylayan) | API-01, 15, 40, 53 |

Kopyalama yerine önce ortak katmana çıkarılır (TEK-03, TEK-04); Satış dosyaları bu şeritte değiştirilmez, gerekirse
görev notu bırakılır.

---

## 10. Sıralı yol haritası

| Dalga | İçerik | Ön şart |
|---|---|---|
| **A0** | Bu plan ve karar dosyası; `SISTEM_OF_RECORD_MATRISI.md` ve `DURUM_MAKINELERI.md` Alış satırları; FZL-01…03; TEK-05…08 | – |
| **A1** | TEK-09 durum makinesi; API-01 (talep onay/ret), API-02 (kaynak alanı, A-2), API-03 + UI-01; API-10/11 + UI-05 (iptal, satır kapatma); FZL-04 | N-02, N-11 |
| **A2** | TEK-01…03; API-20…22 + UI-03 (fiyat, vergi, kur, vade); API-15 + API-96 (onay eşiği); API-12/13/17 + UI-06 (revizyon, kırılım); API-14 + UI-04 (sekmeler, eksenler); API-16 (PDF) | N-03, N-05, N-06, N-10 |
| **A3** | API-95 + API-40 (kabul toleransı), API-41 (hizmet kabulü), API-42…44 + UI-08; API-30…32 + UI-07 (termin, yolda); MOD-06, MOD-07 | OPEN-004 profili, N-12, N-13 |
| **A4** | API-70…72 + UI-11 + MOD-01 (fason, A-1); MOD-02 (Üretim talebi); MOD-03 (Satış bağlı satın alma, A-3) | A1, A2 |
| **A5** | MOD-12 (outbox, Satış ile sıralı); API-50…55 + API-97 + UI-09 (gelen fatura, üçlü kontrol); MOD-04; API-60…62 + UI-10 (iade) | A2, A3, N-07, N-08 |
| **A6** | API-80/81 + UI-02 (teklif, N-04'e bağlı); API-90…92 + UI-13 (raporlar, performans); MOD-10 + UI-12 (onaylar); PRS-01/02; kabul taraması AT-025…028; production aktivasyonu insan kararıdır | A5 |

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
| Bu plan + karar dosyası ALS-D1…D11 | #884 | taslak PR; iki docs review bekliyor |

Sıradaki: kalan sorular (N-09, N-12, N-14…N-19), A0 belge ve temizlik PR'ları, ardından A1 (talep onay/ret, kaynak
alanı, iptal ve satır kapatma).
