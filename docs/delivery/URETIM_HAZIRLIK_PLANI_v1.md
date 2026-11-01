# Üretim Modülü Hazırlık Planı v1

Tarih: 2026-11-01 · Hazırlayan: Claude (cloud oturumu) · Durum: **ÖNERİ** (plan; iş kararı değildir)

Kaynaklar: `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` §7.7, §19.2, §27.7, §28.2–28.9, §29.5;
`docs/decisions/KARAR_SICILI.md` `DEC-PRDCT-001…009`, `DEC-ORG-001/003`, `DEC-STK-002/003/006/008`, `DEC-LBL-*`,
`DEC-SAL-004/005`, `DEC-SET-001`; `ACIK_KARARLAR.md` `OPEN-004…008`; `ERTELENEN_KAPSAM.md`;
`SINAN_KARARLARI_2026-08-31.md` (OPEN-005/007), `SINAN_KARARLARI_2026-10-21.md` §3.1 (No 83, 85, 86) ve §3.5
(No 70, 71/73, 90, 103–106, 124); `src/backend/Modules/Manufacturing/README.md`;
`docs/backlog/DELIVERY_BACKLOG_v1.json` W3-URETIM, W3-ETIKET, W3-TAHSIS, W3-KALITE;
`docs/ui/CANONICAL_SCREEN_STATUS.csv` (PR-01…17, Q-01…06).

Bu plan kodun 2026-11-01 `main` durumuna (`d7e19e4`) göre yazıldı. Biçim `SATIS_HAZIRLIK_PLANI_v1.md` ile aynıdır.

Şerit kısaltmaları: **CL** Claude, **GP** ChatGPT (spec/kabul/golden), **SN** Sinan (karar). Seviye: K1/K2/K3
(`GOYMEN_ERP_HIZLI_TESLIM_v4.md`).

---

## 0. Şerit kaydı

2026-11-01'de Sinan, bu oturumda Üretim modülünün **bütün geliştirme sürecinin Claude'da** olmasını istedi
("Bütün geliştirme süreci claude da olacak şekilde"). Satış'taki `SINAN_KARARLARI_2026-10-25_SATIS.md` §0 emsaliyle
bu, Üretim planındaki backend, migration, OpenAPI, formül hesaplayıcı ve ekran işlerinin hepsini Claude'un yazması
anlamına gelir.

Değişmeyenler:

- Review ve merge kapıları aynen geçerlidir. Yazar kendi PR'ını review etmez. K3 işlerde iki bağımsız exact-head
  review gerekir; reviewer ayrı bir oturum veya ajandır.
- Para, stok, üretim, formül, tolerans, yetki ve migration politikası tahminle etkinleştirilmez. Bunlar §8'deki
  `NEEDS-SINAN` sorularıyla ilerler.
- İnsan kapıları değişmez: grant ve rol ataması, `production.execution.enabled` bayrağının açılması, deployment,
  secret ve `OPEN-*` kapatma.
- Bu kayıt plan dokümanındadır. `docs/decisions/` altında resmî bir `SINAN_KARARLARI_2026-11-01_URETIM.md` dosyası
  açılması ayrı bir governance PR'ıdır (U0).

---

## 1. "Hazır" tanımı

Üretim modülü aşağıdakilerin hepsi sağlanınca hazırdır:

1. Beş motor (Dilme/Sarım, Soğuk Hadde, Boru/Profil, Boyama, Boy Kesme) gerçek parametre profilleriyle hesap
   yapıyor; hiçbir parametre kodda sabit değil (DEC-PRDCT-001/004, DEC-SET-001, OPEN-007).
2. Plan durum makinesi `DURUM_MAKINELERI.md`'deki gibi çalışıyor: Taslak → Kontrolde → Onaylı → Kuyrukta → Üretimde →
   Kısmi/Tamamlandı → İptal/Ters.
3. Reçete, formül, tolerans ve parametre sürümü plan onayında snapshot oluyor. Yeni sürüm geçmişi değiştirmiyor
   (DEC-PRDCT-008, AT-049).
4. Girdi tahsisi, fiilî tüketim, çıktı etiketi ve soy ağacı Stok'ta tek kez kaydediliyor (AT-041, 042, 050).
5. Kütle dengesi yedi kategoride ve rulo başına ayrı uzlaşıyor. Tolerans dışı fark onaysız kapanmıyor
   (DEC-PRDCT-006, AT-043…048).
6. Düzeltme kaydı silmiyor; ters/düzeltme zinciri kuruyor (AT-052).
7. Kuyruk, kontrol merkezi ve takvim aynı veri kaynağını kullanıyor (§7.7).
8. Satış (kaynak planı, bağlı üretim), Work (onaylar) ve fason akışı gerçek portlarla bağlı. Dış etkiler outbox ile
   gidiyor.
9. PR-01…PR-17 ekranları ve Q-01…Q-04 çekirdek kalite ekranları yayında. Her ekranda yükleniyor, boş, hata, yasak,
   çakışma ve bilinmeyen sonuç durumları var.
10. AT-038, 039, 041…052, 061…063 gerçek SQL ve tarayıcıda geçiyor. `GOLDEN_TEST_SABLONU.csv` motorların golden
    testleriyle eşleşiyor.

---

## 2. Bugünkü durum

### 2.1 Var olan ve çalışan

| Alan | Durum | Kanıt |
|---|---|---|
| Domain | `ProductionPlan` (Draft/Approved), `ProductionOrder` (Open/Closed), giriş/iade/çıktı/WIP/proses kaybı olguları, rulo başına kütle dengesi, kapanış değerlendirmesi | `Manufacturing.Domain` |
| Motorlar | Altı hesaplayıcı: GDC-SLT/1, WND/1, CR/1, TUB/1, PNT/1, CUT/1, ayrıca GDC-BAL/1 ve plan bileşeni GDC-PLAN-CALC/1. Tamamı decimal, yuvarlamasız ve golden testli | `Domain/Engines/` |
| HTTP | 19 uç (`/api/v1/manufacturing`): plan kaydet/önizle/onayla/revize/liste/detay, emir aç/liste/detay, beş olgu, kapanış, kütle dengesi | `ManufacturingEndpoints.cs` |
| Yetki | 8 izin (`manufacturing.plans.*`, `orders.*`, `facts.record`, `orders.close-exception.approve`) | `ErpPermissions.cs` |
| Stok | Girdi çıkışı, iade ve çıktı etiketi tek SQL transaction'ında Stok'a işleniyor (Issue/Return/Receipt) | `InventoryManufacturingRealizationParticipant.cs` |
| Snapshot | Ürün teknik ölçüleri (kalınlık/en/boy) ve ticari bağlam (müşteri/fason sahibi) plan girdisinde | `PlanTechnicalParameterSnapshotV1`, `PlanCommercialContextSnapshotV1` |
| Kapanış | Ayarlar tolerans profili okunuyor. Tolerans dışı kapanış gerekçe + ayrı izinle yapılıyor | `CloseOrderAsync`, `OrderClosureDecisions` |
| Ekranlar | 5 rota: `/uretim/planlar`, `/uretim/plan/yeni`, `/uretim/plan/:id`, `/uretim/emirler`, `/uretim/emir/:id`; canlı önizleme (AT-061/062), stok satırından plan (AT-063) | `src/manufacturing/` |
| Testler | 331 backend birim, ~80 SQL kabul (17 dosya), 249 frontend birim, ~23 tarayıcı senaryosu, OpenAPI sözleşme testi | `tests/` |

### 2.2 Çalışıyor görünen ama üretimde kullanılamayan

| Konu | Neden | Etki |
|---|---|---|
| Motor parametreleri | Yoğunluk, hat hızı, kerf, net en, iç/dış çap, katı madde, çevre ve birim kg için kaynak yok (OPEN-007). Yalnız ürün ölçüleri geliyor | Motorların çoğu `Unavailable` döner. Dilme spec'li plan hiç onaylanamaz (`manufacturing_plan_parameters_unavailable`) |
| Tolerans profili | `settings.ManufacturingTolerancePolicies` tablosuna yazan uç yok. Yaşam döngüsü politikası (prepare/approve/publish) yalnız testte | Üretimde kapanış her zaman 503 veya "profil yok" döner. Yalnız test fikstürü %5 verir |
| Maliyet | Her çıktı `NotCalculated` | DEC-PRDCT-009 gereği bilinçli; ham veri de toplanmıyor |

### 2.3 Hiç olmayan

Hammadde girdi tahsisi, soy ağacı kenarı, kuyruk/takvim/kontrol merkezi, hat/kalıp/kapasite masterı, reçete masterı,
Soğuk Hadde/Boru/Boya için kayıtlı spec, plan iptali, olgu düzeltmesi, outbox olayı, Satış bağlantısı, Work onay
adaptörü, fason akışı, kalite/karantina, raporlar. PR-01, 06, 07, 08, 10, 11, 12, 16, 17 ve Q-01…06 ekranları
yok. Üretim ekran spec'i yok (`docs/ui/screens/` altında üretim klasörü yok; hepsi
`DECISION_ONLY_REFERENCE_MISSING`).

---

## 3. Fazla ve temizlenecek (FZL)

| ID | İş | Neden | Sev. |
|---|---|---|---|
| FZL-01 | Rotasız eski olgu metotlarını kaldır: `IssueInputAsync`, `ReturnUnusedInputAsync`, `DeclareOutputAsync` (Stok'suz yol). Testleri Stok'lu yola taşı | İki yol; yalnız testler çağırıyor; yanlışlıkla Stok'suz olgu yazma riski | K2 |
| FZL-02 | README'yi kodla eşitle: "No HTTP / No Inventory / No tolerance" bölümleri yanlış; proses kaybı, çıktı referansları, kesim satırları, snapshot'lar, kapanış kararı ve istisna izni belgelenmemiş | Belge kodla çelişiyor | K1 |
| FZL-03 | Eski doc comment'ler: `ManufacturingPlanPreviewV1.cs:62` ("Product not wired"), `ClosureTolerancePolicySnapshotV1` ("runtime adapter outside") | Aynı | K1 |
| FZL-04 | Frontend `as CapabilityId` dönüşümleri ve `SHARED_MOUNT_HANDOFF.md` (rotalar yayında; not kaldırılabilir diyor) | Aynı | K1 |
| FZL-05 | Kendi `Kilograms` tipi → BuildingBlocks `Quantity` (Satış TEK-02 ile üç kopya birleşti; Üretim dışarıda kaldı). Ö2 kuralı (≤ 3 ondalık, yuvarlamadan red) korunur | Kural kayması riski | K3 (sözleşme) |

---

## 4. Kod ve teknoloji düzeltmeleri (TEK)

| ID | İş | Neden | Sev. | Bağımlılık |
|---|---|---|---|---|
| TEK-01 | Büyük dosyaları böl: `ManufacturingApiV1.cs` (2.662), `ManufacturingStore.cs` (1.634), `OrderDetailPage.tsx` (1.372), `contract.ts` (1.051), `planCommandApi.ts` (817), `ManufacturingCommandServiceTests.cs` (3.070) | Okunabilirlik, çakışma riski | K1 | – |
| TEK-02 | Ham `<button>` (31 adet) → `ui/Button`; tablolar → `ui/DataTable`; alanlar → `ui/Field`; durum rozeti → `ui/StatusIndicator`; çakışma → `ui/ConflictDiff`. Modül bugün `ui/` bileşenlerinin hiçbirini kullanmıyor | Tutarlı görünüm, odak, erişilebilirlik | K1 | – |
| TEK-03 | Bileşenlerdeki sabit Türkçe metni (~197 satır) `manufacturingText.ts` ve kardeş text dosyalarına taşı | Kaynak dosyalı metin kuralı | K1 | – |
| TEK-04 | GUID yerine ad: emir/plan detayında iş merkezi adı, etiket kodu, ürün kodu/adı, sahip/gözetim cari adı, kişi adı (`IUserDirectoryReadPortV1`). Liste sayfalarındaki "…son 8 karakter" gösterimi | Kullanıcı kimlik okuyamaz | K2 | Mevcut okuma portları |
| TEK-05 | Ortak frontend parçalarını shell'e çıkar: tek `createPendingAttemptStore<TReq,TRes>()` ve tek komut deneme reducer'ı (`orderAttempt` + `factAttempt` + `pendingPlanStore` birleşir). Üretim ilk tüketici olur, Satış sonra taşınır | Satış'ta 7, Satın Alma'da 3, Üretim'de 2 kopya aynı mantık | K2 | – |
| TEK-06 | Ortak SQL test altyapısı (`DropDatabaseAsync`, `WithDatabaseAsync`, `GrantAsync` 34/18/12 kopya). Üretim SQL testleri ilk tüketici | Test yazma hızı | K1 | – |
| TEK-07 | Ortak audit şirket guard'ı (11 modülde kopya; Satış ve Satın Alma sürümleri farklılaşmış). Üretim'inki ortak sürüme yönlenir | Güvenlik kuralı tek yerde | K3 | – |
| TEK-08 | `listFormAuditBaseline.ts`'teki üretim istisnalarını kapat (`table-name` 12, `nested-main` 7) | Erişilebilirlik borcu | K1 | TEK-02 |
| TEK-09 | Üretilmiş OpenAPI istemcisine geçiş (#122; `api.ts:4` notu). Sözleşme testi korunur | Elle yazılmış decoder sapabilir | K2 | – |
| TEK-10 | Golden kataloğu: `GOLDEN_TEST_SABLONU.csv`'ye motorların mevcut golden testlerini işle (GP doğrular); `ACCEPTANCE_MATRIX.csv`'de AT-041…063 için modül = Üretim ve kanıt sütunu | Katalog boş; testler yalnız kodda | K1 | – |
| TEK-11 | Karar uyumu denetimi: No 83 (kesin rasyonel aritmetik, süre dakika 2 ondalık), No 85 (kesit sınırı kg'da 3 ondalığa aşağı), No 86 (`D-M025-KERF=A`: PerPiece/BetweenPieces, baş/kuyruk trim, global varsayılan yok) motorlarda karşılanıyor mu; eksikse yeni motor sürümü | 2026-10-21 kararları motorlardan sonra geldi | K3 (formül) | – |

---

## 5. Eksik API uçları

Tüm yeni uçlar `/api/v1/manufacturing/` altında; değişiklik yapanlar `Idempotency-Key` + gövdede
`expectedVersion`; katı JSON; şirket bağlamı; yetki her adımda yeniden ölçülür; audit aynı transaction'da; OpenAPI'ye
ve sözleşme testine eklenir. Satış'taki komut iskeleti (`SalesDraftService.Cancellation.cs` deseni) örnek alınır.

### 5.1 Parametre, reçete ve tolerans (motorları canlandıran iş)

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-01 | Ayarlar: `POST/GET settings/manufacturing-parameter-profiles` + `…/{id}/approval`, `…/publication` | Proses/hat/malzeme bazında sürümlü parametre profili: yoğunluk, hat hızı, kerf ve kesim tipi, net en/kenar kaybı, iç/maks dış çap, eşzamanlı profil adedi, katı madde, fire katsayısı, metre toleransı. Değerler kodda yok; boşsa fail-closed | `settings.manufacturing-parameters.prepare/approve/publish` (yeni) | DEC-SET-001, OPEN-007, No 86 | K3 |
| API-02 | Ayarlar: `POST/GET settings/manufacturing-tolerance-profiles` + approval/publication | Var olan tablo ve yaşam döngüsü politikasına yazma yolu. Hat başına varsayılan ±%2, planda yetkili override (OPEN-005) | `settings.manufacturing-tolerance-profiles.prepare/approve/publish` (politikada adları var) | OPEN-005, DEC-SET-001 | K3 |
| API-03 | `POST plans` / `plans/{id}/approval` genişlemesi | Onayda parametre + tolerans + reçete sürümü snapshot'ı; planda yetkili override snapshot'la saklanır | mevcut | DEC-PRDCT-008 | K3 |
| API-04 | `GET/POST recipes` · `POST recipes/{id}/revision|approval` | Ürün × proses reçetesi: girdi ürün ailesi, çıktı ürün(ler)i, proses sırası, parametre profili bağı. Hazırlayan ≠ onaylayan (No 90, 103) | `manufacturing.recipes.prepare/approve` (yeni) | DEC-PRDCT-008, No 90 | K3 |

### 5.2 Plan yaşam döngüsü

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-10 | `POST plans/{id}/submission` · `…/rejection` | Taslak → Kontrolde → Onaylı/Red. Hazırlayan ≠ onaylayan | mevcut `plans.write` / `plans.approve` | DURUM_MAKINELERI | K3 |
| API-11 | `POST plans/{id}/cancellation` | İptal (gerekçe zorunlu); emir açılmış planda red veya ters zinciri | `manufacturing.plans.cancel` (yeni) | Silme yerine iptal | K3 |
| API-12 | Soğuk Hadde, Boru/Profil, Boyama spec'leri (`PlanColdRollLines`, `PlanTubeLines`, `PlanPaintLines`) plan kaydı ve revizyonunda | Bugün yalnız Dilme ve Boy Kesme spec'i kaydediliyor | mevcut | DEC-PRDCT-001 | K3 (migration) |
| API-13 | `GET plans/{id}/revisions/{rev}/diff` | Revizyon farkı (PR-09) | `plans.read` | – | K2 |
| API-14 | `GET plans/{id}/readiness` | Kaynak hazır mı: tahsis, parametre, tolerans, reçete; hazır değilse sebepler | `plans.read` | AT-041 | K2 |

### 5.3 Stok bağları ve gerçekleştirme

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-20 | `POST plans/{id}/input-allocations` · `DELETE`→ ters kayıt | Plan girdisini Stok'ta "üretim girdi tahsisi" olarak tut (merkezi tahsis motoru; ayrı rezerv tablosu yok) | `manufacturing.plans.allocate` (yeni) | DEC-STK-003/006, AT-041 | K3 |
| API-21 | Girdi çıkışında tahsis kontrolü | Tüketim tahsisi ve fiziki mevcudu aşamaz | mevcut | AT-042 | K3 |
| API-22 | Çıktı beyanında soy ağacı kenarı | Her çıktı etiketi tüm girdi etiketlerine bağlanır (Stok'taki `IInventoryLabelGenealogyWriterV1`) | mevcut | DEC-PRDCT-003, AT-050 | K3 |
| API-23 | `POST orders/{id}/start` | Başlatma: kaynak ve ön koşul kontrolü; kısmi hammaddeyle başlama (asgari parti yok, plan override; eksik kaynak uyarısı, yetkili onay, üretilebilir süre ve tahmini bitiş) | `manufacturing.orders.start` (yeni) | DEC-PRDCT-002, OPEN-006 | K3 |
| API-24 | `POST orders/{id}/input-substitutions` | Plan sürerken hammadde ekle/değiştir: olay, sebep, yeniden denge (PR-13) | `facts.record` | §7.7 | K3 |
| API-25 | `POST orders/{id}/facts/{factId}/correction` | Düzeltme: önceki olgu silinmez; ters olgu + yeni olgu zinciri, Stok'ta karşı hareket | `manufacturing.facts.correct` (yeni) | AT-052 | K3 |
| API-26 | `POST orders/{id}/quick-completion` | "Hızlı Tamamla": plan değerleriyle olguları tek komutta önerir, kullanıcı onaylar | `facts.record` | §7.7 | K3 |
| API-27 | Çıktı sınıfına göre etiket durumu (ikinci kalite, artık, hurda, WIP kendi kalite/üretim durumuyla) | Satılabilirlik ve depo ayrı | mevcut | DEC-PRDCT-007 | K3 |
| API-28 | `GET orders/{id}/lineage` | Emirden girdi/çıktı soy ağacı okuması | `orders.read` | AT-050 | K2 |

### 5.4 Kuyruk, kapasite ve zaman kayıtları

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-30 | Organizasyon: hat/kalıp/vardiya/kapasite masterı (iş merkezi genişlemesi) | PR-17 | `organization.work-centers.write` | DEC-ORG-001/003 | K3 |
| API-31 | `GET queue` · `POST queue/sequence` | Aktif kuyruk ve sıra değişikliği (AI yalnız öneri) | `manufacturing.queue.read` / `.apply` (yeni) | No 105, §7.7 | K3 |
| API-32 | `GET schedule?from&to&workCenter` | Takvim/Gantt; kuyrukla aynı kaynak | `manufacturing.queue.read` | §7.7 | K2 |
| API-33 | `POST orders/{id}/time-records` | Kurulum, duruş, çalışma, bakım ham olayları | `manufacturing.time-records.record` (yeni) | No 105, ERTELENEN (ham olay çekirdekte) | K3 |
| API-34 | `GET control-center` | Kontrol Merkezi: darboğaz, istisna, gecikme | `orders.read` | §7.7 | K2 |

### 5.5 Raporlar

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-40 | `GET reports/line-efficiency` | Hat verimi (fiilî/plan kg) ve kütle dengesi sapması (PR-01 KPI) | `manufacturing.reports.read` (yeni) | No 124 | K2 |
| API-41 | `GET reports/plan-vs-actual` · `GET reports/fire` | Plan–gerçek, fire/ikinci kalite dağılımı (PR-15) | `manufacturing.reports.read` | §28.9 | K2 |
| API-42 | Maliyet ham verisi okuması (lot maliyeti, süre, hat, duruş, fire, fason, sipariş bağı) | Maliyet modülü için veri; tutar hesaplanmaz | `manufacturing.reports.read` | DEC-PRDCT-009 | K2 |

---

## 6. Diğer modüllerle eksik ilişkiler (MOD)

| ID | Modül | Eksik | Sözleşme | Sev. |
|---|---|---|---|---|
| MOD-01 | Ayarlar | Parametre ve tolerans profili yazma yolu (API-01/02) | `ISettingsManufacturingParameterProviderV1` (yeni), mevcut tolerans provider | K3 |
| MOD-02 | Ürün | Reçete–ürün bağı, birim kg/profil ağırlığı önceliği (manuel doğrulanmış → ürün/paket → geometrik; §28.2) | `IProductManufacturingTechnicalSnapshotV1` v2 | K3 |
| MOD-03 | Stok | Üretim girdi tahsisi, soy ağacı kenarı, çıktı etiket durumu, düzeltme karşı hareketi | `IInventoryManufacturingRealizationParticipantV1` v2 + tahsis motoru | K3 |
| MOD-04 | Satış | Kaynak planı (sipariş satırı → üretim planı) ve iptal/zorla kapatma için bağlı üretim okuması (bugün "kaynak yok") | Satış API-11, `ISalesOrderLinkedSupplyReadPortV1` (Üretim tarafı) | K3 |
| MOD-05 | Work | Plan onayı, tolerans dışı kapanış, kısmi başlatma onayı için onay kutusu adaptörleri | Work native approval sözleşmesi | K2 |
| MOD-06 | Organizasyon | Hat/kalıp/vardiya/kapasite (API-30) | `IOrganizationWorkCenterPlanningReferenceV1` v2 | K3 |
| MOD-07 | Satın Alma / Lojistik | Dış fason: P-xx fason siparişinden başlar, iki bacaklı transfer, fason lokasyondan doğrudan satış sevki | DEC-STK-008, No 71/73 | K3 |
| MOD-08 | Platform | Outbox: `manufacturing.output.declared.v1`, `manufacturing.order.closed.v1`, `manufacturing.plan.approved.v1` | ERP-000F | K3 |
| MOD-09 | Kalite | Karantina bloğu, kalite kararı → yeniden işlemeyi Üretim yürütür (No 70) | W3-KALITE | K3 |
| MOD-10 | Belgeler | Emir/plan için belge sekmesi, iş emri PDF'i (Satış sipariş PDF'i deseni) | mevcut Documents owner-link | K2 |
| MOD-11 | Kimlik | Kişi adları (TEK-04) | `IUserDirectoryReadPortV1` (hazır) | K2 |

---

## 7. Ekranlar (UI)

Ekran spec'i yok. Her ekran dalgasından önce GP görsel referans hazırlar; yoksa Satış ekranlarının düzeni temel
alınır ve `CANONICAL_SCREEN_STATUS.csv` güncellenir.

| ID | Ekran | Bugün | Sev. | Bağımlılık |
|---|---|---|---|---|
| UI-01 | PR-01 Üretim Ana Bakış (KPI, darboğaz, istisna) | Yok | K2 | API-34, API-40 |
| UI-02 | PR-02 Planlar | Var (liste + detay) | K1 | TEK-02…04 |
| UI-03 | PR-03 Ortak plan kabuğu (bağlam → kaynak → hesap → çıktı → denge → onay) | Kısmen (`PlanCreatePage`) | K2 | API-03, API-14 |
| UI-04…08 | PR-04 Boy Kesme, PR-05 Dilme/Sarım, PR-06 Boru/Profil, PR-07 Boyama, PR-08 Soğuk Hadde planlayıcıları | 04/05 kısmen; 06/07/08 yok | K2 | API-01, API-12 |
| UI-09 | PR-09 Plan detayı / revizyon farkı | Kısmen (geçmiş var, fark yok) | K2 | API-13 |
| UI-10 | PR-10 Aktif Üretim Kuyruğu | Yok | K2 | API-31 |
| UI-11 | PR-11 Takvim/Gantt | Yok | K2 | API-32 |
| UI-12 | PR-12 Başlatma | Yok | K2 | API-23 |
| UI-13 | PR-13 Hammadde ekle/değiştir | Yok | K2 | API-24 |
| UI-14 | PR-14 Gerçekleştirme (Hızlı Tamamla / Fiilî / İstisna) | Kısmen (olgu formları) | K2 | API-25, API-26 |
| UI-15 | PR-15 Gerçekleşmeler (plan–gerçek, ters işlem) | Yok | K2 | API-41 |
| UI-16 | PR-16 Reçete/Formül/Tolerans masterı | Yok | K2 | API-01, 02, 04 |
| UI-17 | PR-17 Hat/Kalıp/Kapasite | Yok | K2 | API-30 |
| UI-18 | Emir 360 sekmeleri: Özet, Girdiler, Çıktılar, Denge, Soy ağacı, Zaman, Belgeler | Tek sayfa | K2 | API-28, MOD-10 |
| UI-19 | Durum eksenleri kartı (plan, emir, kalite, stok etkisi) | Yalnız Açık/Kapalı rozeti | K2 | API-10 |
| UI-20 | Q-01…Q-04 Giriş/Proses/Nihai kalite, Karantina | Yok | K2 | MOD-09 |

Personalar (No 90, 105; grant değildir): Teknik ofis/Üretim planlama reçete ve plan hazırlar, kuyruğu uygular; Üretim
müdürü reçeteyi, planı ve tolerans dışı kapanışı onaylar; Hat operatörü/Vardiya amiri olgu ve zaman kaydı girer.

| ID | İş | Şerit | Sev. |
|---|---|---|---|
| PRS-01 | Persona grant matrisi kartına üretim satırları (yeni izinler dahil) | GP | K3 (yetki) |

---

## 8. Karar bekleyen sorular (NEEDS-SINAN)

Kod bu sorular için yapılandırılabilir ve fail-closed yazılır; cevap gelene kadar değer seçilmez.

| No | Soru | İlgili | Engellediği |
|---|---|---|---|
| N-01 | Parametre ilk değerleri ve veri sahibi: yoğunluk, hat hızları, kerf, net en/kenar kaybı, iç/maks dış çap, boya katı madde ve fire katsayısı. Göymen verisi mi, web kaynağı + golden mı? | OPEN-007 | Motorların canlı hesabı |
| N-02 | Hat başına tolerans ilk profilleri: ±%2 varsayılan her hat için yayınlansın mı? | OPEN-005 | Kapanış |
| N-03 | Bir onaylı plandan birden fazla emir açılabilir mi? | #66 5728934213 | API-10, UI-03 |
| N-04 | "Kontrolde" adımı her plan için zorunlu mu, yoksa tutar/proses eşiğine bağlı mı? | DURUM_MAKINELERI | API-10 |
| N-05 | İkinci kalite, geri kazanılabilir artık ve hurdanın stok/depo etkisi | Master §19.2 #12, DEC-PRDCT-007 | API-27 |
| N-06 | Yuvarlama politikası §28.1: No 83/85 dışındaki aşamalar (WND-010 kırılımı, WND-008 kg dalı, BAL-005 dağıtım) | OPEN-007 | Motor sürüm 2 |
| N-07 | Soğuk haddede giriş ve hedef ürünün aynı çelik kalitesinde olması şart mı? | README MFG-7 | API-12 |
| N-08 | Kalite çekirdeği (Q-01…04) Üretim'in ilk sürümüne mi, sonraki dalgaya mı? | W3-KALITE | U6 |
| N-09 | Şerit kaydının (§0) `docs/decisions/` altına resmî karar dosyası olarak işlenmesi | §0 | Yok (governance) |

---

## 9. Satış'tan yeniden kullanılacaklar

| Satış'ta | Üretim'de kullanım |
|---|---|
| Komut iskeleti (sürüm → yetki → replay → domain → audit) | API-10, 11, 20…26 |
| Onay deseni (taban fiyat / iskonto istisnası; hazırlayan ≠ onaylayan) | Plan onayı, tolerans dışı kapanış, kısmi başlatma |
| İptal/zorla kapatma etki önizlemesi | API-11 plan iptali |
| Revizyon geçmişi ve fark | API-13, UI-09 |
| `SalesOrderStatusAxes` | UI-19 |
| `SalesOrderTabs` | UI-18 |
| `SalesOrderPdfAction` + `DocumentsWorkspace` | MOD-10 |
| Satış politika kaydı (MOD-03 dilim 1) | API-01/02 |
| Rapor uçları (S6) | API-40…42 |

Kopyalama yerine önce ortak katmana çıkarılır (TEK-05, 06, 07); Üretim ilk tüketici olur.

---

## 10. Sıralı yol haritası

| Dalga | İçerik | Ön şart |
|---|---|---|
| **U0** | Bu plan; §0 karar dosyası (N-09); FZL-01…04; TEK-01…03, TEK-08, TEK-10; TEK-05 ve TEK-06 (ortak altyapı) | – |
| **U1** | API-01/02 + MOD-01 (parametre ve tolerans profili), API-03 (onay snapshot'ı), API-04 + MOD-02 (reçete), UI-16; TEK-11 karar uyumu; FZL-05 | N-01, N-02 cevabı (değer olmadan da yapı kurulur, fail-closed) |
| **U2** | API-10…14 (plan yaşam döngüsü, üç yeni spec, fark, hazırlık), UI-03…09, UI-19, TEK-04, PRS-01 | U1 |
| **U3** | API-20…28 + MOD-03 (tahsis, soy ağacı, başlatma, ikame, düzeltme, hızlı tamamla, etiket durumu), UI-12…14, UI-18, MOD-08 (outbox) | U2, ERP-000F |
| **U4** | API-30…34 + MOD-06 (hat/kapasite, kuyruk, takvim, zaman kayıtları, kontrol merkezi), UI-01, UI-10, UI-11, UI-17 | U3 |
| **U5** | MOD-04 (Satış kaynak planı ve bağlı üretim), MOD-05 (Work), MOD-07 (fason), MOD-10 (belgeler), API-40…42 + UI-15 | U3 |
| **U6** | MOD-09 + UI-20 (kalite/karantina, AT-051, yeniden işleme), TEK-07, TEK-09, kabul taraması AT-038…063; `production.execution.enabled` açılışı insan kararıdır | U4, U5, N-08 |

---

## 11. Test planı

| Katman | Ne | Nerede |
|---|---|---|
| Birim | Domain kuralları, motor golden testleri (master örneği ± 0,0005 ve 40 haneli referans), yeni motor sürümleri | `tests/GoymenERP.Manufacturing.UnitTests` |
| Gerçek SQL | Şirket izolasyonu, idempotency replay, sürüm çakışması, Stok ile tek transaction, soy ağacı, tahsis aşımı reddi | `tests/GoymenERP.Api.IntegrationTests` (TEK-06 ortak altyapısıyla) |
| Sözleşme | OpenAPI drift ve frontend `openapiContract.test.ts` her yeni uçta | `tests/openapi_contract_drift`, `src/manufacturing` |
| Tarayıcı | Plan → onay → tahsis → başlat → olgular → kapanış → soy ağacı; F5, şirket değişimi, cevabı kaybolan istek | `tests/e2e/manufacturing-mfg` |
| Erişilebilirlik | axe taraması, `listFormAudit` istisnası kalmaması | frontend |

Her PR'da: karar referansı, CompanyId ve yetki testi, audit/idempotency/concurrency etkisi, UI durumları, migration
geri dönüş planı.

---

## 12. Durum (2026-11-01)

Bu bölüm iş takibidir; karar değildir.

| İş | PR | Durum |
|---|---|---|
| Bu plan | – | taslak PR |

Sıradaki işler: U0 (FZL-01…04, TEK-01…03, TEK-05/06), N-01…N-09 soruları Issue #66'ya.
