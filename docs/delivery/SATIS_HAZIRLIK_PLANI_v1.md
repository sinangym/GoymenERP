# Satış Modülü Hazırlık Planı v1

Tarih: 2026-10-25 · Hazırlayan: Claude (cloud oturumu) · Durum: **ÖNERİ** (plan; iş kararı değildir)

Kaynaklar: `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` §7.2, §8.1, §11; `docs/decisions/KARAR_SICILI.md`
`DEC-SAL-001…008`; `SINAN_KARARLARI_2026-10-21.md` delegasyon satırları; **`SINAN_KARARLARI_2026-10-25_SATIS.md`
(SAT-D1…D12)** ve `SINAN_KARARLARI_2026-10-26_SATIS_EK.md` (SAT-D13…D19); `src/backend/Modules/Sales/README.md`; `docs/ui/sources/dispatch-invoice/dispatch-invoice-design-spec-v1.0.md`;
`docs/backlog/DELIVERY_BACKLOG_v1.json` W2-SATIS, W2-IRSALIYE, W2-ACIKKALEM, W5-RAPOR.

Bu plan kodun 2026-10-25 `main` durumuna göre yazıldı. İnceleme raporu:
<https://claude.ai/artifact/Y1dKjxbnQ3G7BcwBCYMpJi>.

Şerit kısaltmaları: **CX** Codex (backend), **CL** Claude (frontend), **GP** ChatGPT (spec/kabul), **SN** Sinan (karar).
Seviye: K1/K2/K3 (`GOYMEN_ERP_HIZLI_TESLIM_v4.md`). 2026-10-25'te Sinan, bu plan için Claude'un backend işlerine de
başlamasını istedi (karar kaydı §0); Claude'un yazdığı backend PR'ları yine bağımsız review bekler, merge
`SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md` kapılarıyla yapılır.

---

## 1. "Hazır" tanımı

Satış modülü aşağıdakilerin hepsi sağlanınca hazırdır:

1. S-01…S-11 ekranları ve Sipariş 360'ın altı kilitli sekmesi yayında; her birinde yükleniyor, boş, hata, yasak,
   çakışma ve bilinmeyen sonuç durumları var.
2. AT-007, AT-018, AT-021…025, AT-028, AT-055 gerçek SQL ve tarayıcıda geçiyor.
3. Fiyat, iskonto, KDV, tevkifat ve toplam yalnız backend calculator'dan geliyor (SAT-D11).
4. `DEC-SAL-001`'in yedi durum ekseni ayrı; operasyonel ve finansal kapanış ayrı.
5. Diğer modüllere giden etkiler outbox ile; aynı olay iki kez hareket üretmiyor.
6. Gerçek sahip modüllerle uçtan uca: talep → teklif → sipariş → tahsis → irsaliye → fatura taslağı → resmî belge
   eşleşmesi → açık kalem.
7. Satış'ı etkileyen açık kararlar ya kapatılmış ya da yapılandırılabilir ve fail-closed.

---

## 2. Kod ve teknoloji düzeltmeleri (TEK)

| ID | İş | Neden | Şerit | Sev. | Bağımlılık |
|---|---|---|---|---|---|
| TEK-01 | `BuildingBlocks` içinde ortak `Money` (tutar + para birimi + ölçek) ve `ExchangeRate` değer nesneleri; ölçek VO'da taşınır ama değeri Ayarlar para profilinden okunur (kodda sabit ölçek yok; Ö2 başlangıç profili tutar 2, birim fiyat ≤ 6); SAT-D11 yuvarlaması (`AwayFromZero`) tek yerde | AGENTS.md "Money VO kullan"; Satış fiyatları bugün metin | CX | K3 | – |
| TEK-02 | Ortak `Quantity` (kg ≤ 3 ondalık, adet tam) `BuildingBlocks`'a; Satış, Satın Alma, Lojistik kopyaları ona yönlenir | Üç kopya kural kayması riski | CX | K3 | – |
| TEK-03 | Satış fiyat alanlarını TEK-01'e taşı (wire metin olarak kalır, domain `Money` kullanır) | Toplam/KDV hesabının ön şartı | CX | K3 | TEK-01 |
| TEK-04 | Platform outbox/inbox (ERP-000F, manifestte `ready`, P0) | Satış dahil hiçbir modül olay yayınlayamıyor | CX | K3 | ERP-000E |
| TEK-05 | Satış idempotency'sinin (`sales.DraftAttempts`) platform Idempotency modülüyle ilişkisini ADR ile netleştir; yeni Satış komutları tek yolu kullanır | İki farklı idempotency yolu | CX | K2 | – |
| TEK-06 | Adlandırma: UI'da "Satış Siparişleri" (yapıldı, bkz. §8); API v2'de `/orders` takma yolu ve domain'de `SalesOrder` adı ayrı K3 sözleşme işi | Onaylı sipariş "Draft" adını taşıyor | CL / CX | K1 / K3 | – |
| TEK-07 | Büyük dosyaları böl: `SalesOrderDraft.cs` (1.731), `SalesStore.cs` (1.648), `SalesDraftService.cs` (1.597), `contract.ts` (1.590), `SalesDraftCreatePage.tsx` (1.175) | Okunabilirlik, çakışma riski | CX / CL | K1 | – |
| TEK-08 | Frontend OpenAPI sözleşme testi (Üretim'deki `openapiContract.test.ts` gibi), sonra üretilmiş istemci | Elle yazılmış decoder ile backend sapabilir | CL | K2 | – |
| TEK-09 | Sabit Türkçe metinleri `salesText.ts`'e taşı | Kaynak dosyalı metin kuralı | CL | K1 | – |
| TEK-10 | Panellerdeki ham `<button>`'ları tasarım sistemi `Button`'a çevir | Tutarlı görünüm ve odak durumları | CL | K1 | – |
| TEK-11 | Detayda GUID yerine cari hesap, para birimi ve birim adı | Kullanıcı kimlik okuyamaz | CL | K2 | – |
| TEK-12 | Eski "not live" yorumları, `SHARED_MOUNT_HANDOFF.md` durumu, `as unknown as CapabilityId` dönüşümü, README "Deliberate limits" | Belgeler kodla çelişiyor | CL / CX | K1 | – |
| TEK-13 | Test borcu: kalan değişikliği gerçek SQL; müşteri kabulü birim; taban fiyat ve ticari revizyon bileşen testleri; axe erişilebilirlik taraması; gerçek sahiplerle olumlu oluşturma | Kanıt eksikleri | CX / CL | K2 | – |

---

## 3. Eksik API uçları

Tüm yeni uçlar `/api/v1/sales/` altında; değişiklik yapanlar `Idempotency-Key` + gövdede `expectedVersion`; katı JSON;
şirket bağlamı; yetki her adımda yeniden ölçülür; audit aynı transaction'da; OpenAPI'ye ve sözleşme testine eklenir.

### 3.1 Sipariş (mevcut kayıt üzerinde)

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-01 | `GET drafts/{id}/force-close-impact` | Zorla kapatma etki önizlemesi (iptalle aynı 6 kategori + token) | `sales.orders.force-close` (yeni) | No 30, DEC-SAL-005 | K3 |
| API-02 | `POST drafts/{id}/force-close` | Teslim başlamış onaylı siparişin kalanını "iptal edilen kalan" olarak kapat; gerekçe zorunlu; No 30'daki herhangi bir açık kategori (aktif tahsis, bağlı satın alma/üretim, irsaliyesiz yükleme, eşleşmemiş avans/dekont) veya okunamayan kaynak varsa red; yalnız faturalanmamış irsaliye engellemez, uyarı olarak gösterilir (SAT-D12) | `sales.orders.force-close` (yeni) | No 30, SAT-D12 | K3 |
| API-03 | `GET drafts/{id}/totals` | Satır tutarı, KDV, tevkifat, toplam (SAT-D11) | `sales.orders.price.read` | SAT-D11, No 15 | K3 |
| API-04 | `POST drafts/{id}/header-revision` | Cari hesap, para birimi, vade, satış sorumlusu, teslim bilgisi revizyonu → Rev+1, yeniden onay | `sales.orders.header.write` (yeni) | DEC-SAL-002, SAT-D3 | K3 |
| API-05 | `POST drafts/{id}/line-revision` | Satır ekle/çıkar, ürün değiştir → Rev+1, yeniden onay | `sales.orders.quantity.write` | DEC-SAL-002, SAT-D3 | K3 |
| API-06 | `GET drafts/{id}/risk-check` | Brüt risk (açık alacak + faturalanmamış sevk + sevk edilmemiş onaylı kalan), limit, sonuç | `sales.drafts.read` | DEC-FIN-007/009 | K3 |
| API-07 | `POST drafts/{id}/risk-exceptions` · `POST …/risk-exceptions/{no}/approval|rejection` | Tek sevk risk istisnası: yalnız bir sevki serbest bırakır, onaylandığı iş tarihinin sonuna kadar geçerlidir (SAT-D154, SAT-D160 — `SINAN_KARARLARI_2026-11-01_SATIS_EK21.md`; Satış tarafı ve okuma portu SAT-D281 — `SINAN_KARARLARI_2026-11-10_SATIS_EK55.md`) | `sales.orders.risk-exception.request` / `.approve` (yeni) | No 31, 32 | K3 |
| API-08 | `GET drafts/{id}/status` | Yedi eksen: yaşam döngüsü, iç onay, müşteri onayı, kaynak, gerçekleşme (SAT-D5 toleransıyla), faturalama, finansal kapanış | `sales.drafts.read` | DEC-SAL-001, SAT-D5 | K3 |
| API-09 | `GET drafts/{id}/invoices` | Sipariş 360 "Faturalar" sekmesi | `sales.drafts.read` | – | K2 |
| API-10 | `GET drafts/{id}/customer-returns` | Siparişe bağlı müşteri iadeleri (Lojistik portu hazır) | `sales.drafts.read` | No 21, 38 | K2 |
| API-11 | `GET drafts/{id}/source-plan` · `POST drafts/{id}/source-plan` | Satır kaynak stratejisi (stok / satın alma / üretim / fason); sistem önerir, kullanıcı karar verir | `sales.orders.source-plan.write` (yeni) | DEC-SAL-004 | K3 |
| API-12 | `POST drafts/{id}/discount-exception-approval` | Rol limitini aşan iskontonun Müdür onayı (taban fiyat istisnasıyla aynı desen; hazırlayan ≠ onaylayan) | `sales.orders.discount-exception.approve` (yeni) | SAT-D2 | K3 |

### 3.2 Talep ve teklif (S-01)

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-20 | `POST requests` · `GET requests` · `GET requests/{id}` | Satış talebi | `sales.requests.write` / `.read` | – | K2 |
| API-21 | `POST requests/{id}/approval|rejection` | Talep onayı; onaylı siparişi olan talep reddedilemez | `sales.requests.approve` | No 160, 161 | K3 |
| API-22 | `POST quotes` · `GET quotes` · `GET quotes/{id}` · `POST quotes/{id}/revision` | Teklif; bir teklif aynı müşterinin birden fazla talebini karşılar | `sales.quotes.write` / `.read` | No 163 | K2 |
| API-23 | `POST quotes/{id}/conversion` | "Siparişe dönüştür": satırlar fiyat/miktar snapshot'ı ve teklif referansıyla sipariş olur; teklif başına tek sipariş | `sales.quotes.write` + `sales.drafts.create` | No 162 | K3 |
| API-24 | `POST quotes/{id}/loss` | Kayıp/fırsat nedeni | `sales.quotes.write` | Master §7.2 | K2 |

### 3.3 İrsaliye ve fatura (S-07…S-09)

İlk sürüm akışı SAT-D7: ERP taslak hazırlar, dışa aktarır, resmî belge dış programda kesilir, sonra eşleştirilir.
Finance'ta mevcut **resmî fatura içe aktarma** (`FinanceOfficialInvoiceImport*`) ve **açık kalem kabulü**
(`FinanceOpenItemSourceAdmissionService`) yeniden kullanılır; Satış ikinci bir resmî fatura kaydı tutmaz.

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-30 | `GET dispatches` | Satış menüsü altında sevkiyat/irsaliye listesi (Lojistik okuma portu, yalnız satış yönü) | `sales.drafts.read` | AT-025 | K2 |
| API-31 | `GET invoice-candidates` | Faturaya hazır sevk payları, DEC-SAL-006 gruplama anahtarına göre | `sales.invoices.read` (yeni) | DEC-SAL-006, AT-055 | K3 |
| API-32 | `POST invoices` · `GET invoices` · `GET invoices/{id}` | Fatura taslağı: seçili sevk payları, tek kur, sipariş para birimi | `sales.invoices.write` (yeni) | No 25, 26, SAT-D11 | K3 |
| API-33 | `POST invoices/{id}/submission|approval|rejection` | Fatura taslağı onayı | `sales.invoices.approve` (yeni) | – | K3 |
| API-34 | `POST invoices/{id}/export` | Excel/XML dışa aktarım (Belge kasasına yazılır) | `sales.invoices.export` (yeni) | SAT-D7 | K2 |
| API-35 | `POST invoices/{id}/external-match` | Finance'a içe aktarılan resmî belgeyle eşleştir (numara, tarih, tutar kontrolü) | `sales.invoices.match` (yeni) | DEC-FIN-003 | K3 |
| API-36 | `POST invoices/{id}/finalization` | Kesinleştir → `sales.invoice.finalized.v1` olayı → Finance açık kalem | `sales.invoices.approve` | DEC-FIN-005 | K3 |
| API-37 | `GET control-center` | Kontrol Merkezi: eşleşmeyen, gecikmiş, sapmalı kayıtlar | `sales.invoices.read` | dispatch spec | K2 |

### 3.4 İade ve raporlar

| ID | Uç | Amaç | Yetki | Karar | Sev. |
|---|---|---|---|---|---|
| API-40 | `POST customer-returns` · `GET customer-returns/{id}` | Müşterinin iade faturası kaydı; orijinal satış faturası + sevk bağı zorunlu; ayrı menü yok (SAT-D8) | `sales.returns.write` (yeni) | No 21, 38, AT-028 | K3 |
| API-41 | `GET reports/open-orders` | Açık sipariş tonajı (KPI No 124) | `sales.reports.read` (yeni) | No 124 | K2 |
| API-42 | `GET reports/lost-opportunities` · `GET reports/price-variance` | Kayıp/fırsat ve fatura–sipariş fiyat sapması (No 25) | `sales.reports.read` | No 25 | K2 |

### 3.5 Ayarlar sahibinde (Satış'ın okuyacağı politika uçları)

| ID | Uç | Amaç | Karar | Sev. |
|---|---|---|---|---|
| API-50 | Ayarlar: satış iskonto limiti politikası (rol × yüzde, sürümlü) | SAT-D2 | K3 |
| API-51 | Ayarlar: satış teslim toleransı politikası (%1/%2/%3 başlangıç, ürün/işlem bazında) | SAT-D5 | K3 |
| API-52 | Ayarlar: satış onay tutar eşiği (başlangıçta kapalı) | SAT-D6 | K3 |

Ayarlar'da üretim toleransı için kurulmuş sürümlü politika kaydı (`AddManufacturingTolerancePolicyRegistry`) örnek
alınır.

---

## 4. Alan bazında hazır olmayanları hazırlama planı

| Alan | Bugün | Hedef | İş paketleri |
|---|---|---|---|
| Olumlu oluşturma kanıtı | Sahte portlarla | Gerçek Cari/Ürün/Ayarlar ile | TEK-13 |
| Müşteri kabulü testi | Yalnız tarayıcı | Birim + SQL | TEK-13 |
| Taban fiyat istisnası | Onaycısız fail-closed | Müdür onaylar (SAT-D1) | PRS-01, TEK-13 |
| Kısmi teslim kalanı | SQL testi yok | SQL kabul | TEK-13 |
| Fiziksel tahsis paneli | Elle etiket id | Etiket seçici | UI-13 |
| Vade, sorumlu, kaynak stratejisi, paket | 422 ile red | Sahiplerinden referans | API-04, API-11, MOD-02, MOD-05, MOD-08 |
| KDV, toplam, kur | Yok | Backend calculator | TEK-01, TEK-03, API-03, MOD-03 |
| İskonto limiti | Yok | Rol bazlı limit (SAT-D2) | API-50, API-12 |
| Liste fiyatı seçimi | Kullanıcı elle seçer | Kademe zinciri (SAT-D9), Europe/Istanbul (SAT-D10) | MOD-02 |
| Risk/limit | Yok | Uyarı + sevkte sert kontrol | API-06, API-07, MOD-01 |
| Yaşam döngüsü, zorla kapatma | Taslak/İptal | Yedi eksen + zorla kapat | API-01, API-02, API-08, API-51 |
| Teklif/talep | Yok | S-01 | API-20…24, UI-20 |
| S-05 kontrol | Yok | Fiyat-iskonto-marj-risk ekranı | UI-50 (görsel referans GP) |
| S-06 onaylar | Work kutusunda | Satış filtresi | UI-51 |
| S-07 sevkiyat | Lojistik'te | Satış altında satış yönlü liste | API-30, UI-60 |
| S-08/S-09 fatura | Yok | Manuel akış (SAT-D7) | API-31…37, MOD-04, UI-61…65 |
| İade | Yok | Sipariş/fatura üzerinde görünüm (SAT-D8) | API-10, API-40, UI-70 |
| Sözleşme/belgeler | Port var, ekran yok | Belgeler sekmesi + S-11 | UI-11, UI-71 |
| Raporlar | Yok | Açık sipariş, kayıp, sapma | API-41, API-42, UI-80 |

---

## 5. Diğer modüllerle eksik ilişkiler (MOD)

| ID | Modül | Eksik | Kim yapar | Sözleşme | Sev. |
|---|---|---|---|---|---|
| MOD-01 | Cari / Cari Finans | Risk girdileri okuma portu (açık alacak, limit, teminat); `OPEN-010` haircut başlangıçta 0 | CX (Cari/Finans sahibi) | `ISalesPartyRiskReadPortV1` | K3 |
| MOD-02 | Ürün | Fiyat kademe zinciri çözümü (anlaşma > cari özel > cari grubu > şirket), Europe/Istanbul geçerlilik, paket standardı snapshot'ı | CX (Ürün sahibi) | `IProductQuantityPriceReferenceReaderV1` v2 | K3 |
| MOD-03 | Ayarlar | KDV/tevkifat masterı; iskonto, tolerans, onay eşiği politikaları | CX (Ayarlar sahibi) | `ISettingsPrerequisiteProvider` genişlemesi | K3 |
| MOD-04 | Finans | Satış faturası kesinleşince açık kalem; resmî fatura içe aktarmayla eşleştirme okuma portu | CX (Finans sahibi) | `sales.invoice.finalized.v1` olayı + `IFinanceOfficialInvoiceMatchPortV1` | K3 |
| MOD-05 | Satın Alma / Üretim | Kaynak stratejisi bağlantısı; iptal ve zorla kapatma önizlemesi için bağlı kayıt okuma (bugün "kaynak yok") | CX | `ISalesOrderLinkedSupplyReadPortV1` | K3 |
| MOD-06 | Lojistik | Müşteri iadesi portu Satış'ta tüketilmiyor; faturaya hazır sevk payı okuma | CX | mevcut `ISalesOrderCustomerReturnReadPortV1`, yeni `ISalesInvoiceableShipmentReadPortV1` | K3 |
| MOD-07 | Belgeler | Sipariş için Belgeler sekmesi; sipariş PDF'i ve fatura dışa aktarım dosyası | CL + CX | mevcut `SalesDocumentOwnerLinkPort` | K2 · **sipariş PDF'i hazır:** `GET /sales/drafts/{id}/pdf` güncel revizyonu teknik şablonla (DEC-DOC-010) basar; fiyatlar yalnız `sales.orders.price.read` ile, yoksa fiyatsız sürüm; onaysız revizyon her sayfada "TASLAK" filigranıyla (SAT-D56); saatler İstanbul saatiyle (SAT-D10); toplam/KDV yazılmaz (API-03); detayda "PDF indir". Fatura dışa aktarım dosyası S5'te |
| MOD-08 | Kimlik | Satış sorumlusu için kullanıcı okuma portu | CX | `IUserDirectoryReadPortV1` | K2 · **port hazır:** Kimlik'te şirket kapsamlı, salt okunur `IUserDirectoryReadPortV1` (kimlikten ad çözme + şirketin aktif kişilerini arama); Satış, zenginleştirilmiş sipariş detayında onaya gönderen / karar veren / müşteri kabulünü kaydeden kimliklerinin yanına `userDisplays` ekler. Satış sorumlusu alanı (API-04) ve seçici bu portu kullanacak; kimin sorumlu olabileceği filtresi NEEDS-SINAN |
| MOD-09 | Platform | Outbox/inbox (TEK-04) | CX | ERP-000F | K3 |
| MOD-10 | Work | Talep, teklif, fatura onayları ve risk istisnası için onay kutusu adaptörleri | CX | mevcut Work native approval sözleşmesi | K2 |
| MOD-11 | Muhasebe | Fatura vergi kontrolü (dispatch spec: muhasebe kontrol eder) | CX | Faz 6 sonrası | K3 |
| MOD-12 | Stok | Tahsis paneli için şirket kapsamlı etiket okuma ucu (etiket kimliği yerine seçici) | CX | mevcut Inventory etiket kayıtları | K2 · **karşılandı:** mevcut ST-01 `GET /api/v1/inventory/positions` (`q`, `productId`, `limit`; şirket kapsamlı, `labels.read` + `balances.read`); yeni uç yazılmaz (PR #816) |

---

## 6. Ekranlar (UI)

| ID | Ekran / iş | Şerit | Sev. | Bağımlılık |
|---|---|---|---|---|
| UI-10 | Sipariş 360'ı altı kilitli sekmeye böl | CL | K2 | – |
| UI-11 | Sözleşme/Belgeler sekmesi (`DocumentsPanel`) | CL | K2 | MOD-07 |
| UI-12 | Zorla kapatma paneli | CL | K2 | API-01/02 |
| UI-13 | Tahsis panelinde etiket seçici | CL | K2 | MOD-12 (karşılandı: ST-01) |
| UI-14 | Yeni sipariş formunda fiyat girişi | CL | K2 | TEK-03 |
| UI-15 | Toplamlar kartı, risk kartı, durum eksenleri | CL | K2 | API-03, 06, 08 |
| UI-20 | S-01 Talep/Teklif Havuzu, "Siparişe dönüştür" | CL | K2 | API-20…24 |
| UI-50 | S-05 Fiyat-İskonto-Marj-Risk Kontrolü | CL | K2 | GP görsel referans, API-50 |
| UI-51 | S-06 Satış Onayları | CL | K2 | MOD-10 |
| UI-60…65 | S-07 Sevkiyat/İrsaliyeler, S-08 Faturaya Hazır, S-09 Faturalar, Faturalar sekmesi, Kontrol Merkezi | CL | K2 | API-30…37 |
| UI-70 | İade görünümü (sipariş ve fatura üzerinde; ayrı menü yok); UI-70a okuma görünümü API-10, UI-70b iade kaydı API-40 | CL | K2 | API-10 (70a), API-40 (70b) |
| UI-71 | S-11 İletişim/Sözleşmeler | CL | K2 | MOD-07 |
| UI-80 | Satış raporları | CL | K2 | API-41/42 |

Personalar (SAT-D1, D3, D4, D6 — grant değildir): Satış Destek sipariş girer/düzenler ve revizyon açar; Müdür
siparişi, taban fiyat istisnasını, iskonto limiti aşımını ve kalan değişikliğini onaylar, siparişi iptal eder.
Sevkiyatçı siparişi fiyatsız görür (fiyatsız okuma görünümü ayrıca gerekir: PRS-02).

| ID | İş | Şerit | Sev. |
|---|---|---|---|
| PRS-01 | Persona grant matrisi kartına SAT-D1/D3/D4/D6 satırlarını işle | GP | K3 (yetki) |
| PRS-02 | Sevkiyatçı için fiyatsız sipariş okuması (detay ve listede fiyat alanları gizli) | CX + CL | K3 |

---

## 7. Sıralı yol haritası

| Dalga | İçerik | Ön şart |
|---|---|---|
| **S0** | Karar kaydı (bu PR), TEK-06 (UI etiketi), TEK-08, TEK-09…12, UI-10, TEK-13 (frontend ve kalan değişikliği SQL kabulü) | – |
| **S1** | TEK-01…03, TEK-05 (idempotency ADR), API-01/02 + UI-12 (zorla kapatma), API-10 + UI-70a (iade görünümü), MOD-07 + UI-11 + UI-71 (belgeler, S-11), MOD-12 + UI-13, PRS-01/02, TEK-13 kalan backend kanıtı (gerçek sahiplerle olumlu oluşturma) | – |
| **S2** | MOD-03 + API-50…52, API-03 (toplamlar), API-08 (durum eksenleri) + API-09 (Faturalar sekmesi), MOD-02 (fiyat kademesi), API-12 iskonto limiti kontrolü, UI-14, UI-15 (toplamlar ve durum eksenleri kartı), TEK-07 (dosya bölme) | Ayarlar politika kaydı, TEK-03 |
| **S3** | TEK-04 (outbox, = MOD-09), MOD-01 + API-06/07 (risk) + UI-15 risk kartı, API-04/05 (başlık/satır revizyonu), MOD-08, MOD-10'un risk onayı adaptörü (API-07 onay yönlendirmesi için), TEK-06 (API v2 adı) | ERP-000E (TEK-04 = ERP-000F bu dalgada) |
| **S4** | API-20…24 + UI-20 (talep/teklif), MOD-10 | S2 |
| **S5** | MOD-06, API-30…37, MOD-04, UI-60…65 (irsaliye & fatura, manuel akış) | S2, S3 |
| **S6** | API-40 + UI-70b (iade kaydı), API-11 + MOD-05 (kaynak stratejisi), UI-50/51, API-41/42 + UI-80, MOD-11 | S5 |

---

## 8. Bu oturumda başlatılan işler

- Karar kaydı `SINAN_KARARLARI_2026-10-25_SATIS.md` ve bu plan.
- S0 frontend: TEK-09, TEK-10, TEK-12 (frontend), TEK-06'nın UI etiketi, UI-10 sekmeler.
- S1 backend: API-01/02 zorla kapatma (Claude yazar; K3 iki bağımsız review gerekir).

Her iş ayrı PR'dır; güncel durum §9'da ve PR açıklamalarında izlenir.

---

## 9. Durum (2026-11-11)

Bu bölüm yalnız iş takibidir; karar değildir. Kararlar `SINAN_KARARLARI_*_SATIS*.md` dosyalarındadır (SAT-D1…D299,
EK59'a kadar). Durumlar 2026-11-11 `origin/main` (`0a789a2d`) git geçmişinden doğrulandı; PR numarasının yanında
squash merge SHA'sı (8 hane) var. 2026-11-11 itibarıyla açık Satış PR'ı yok. Engeller
`docs/delivery/FABLE_5_2_ANALIZLERI/` (#1061) bulgu kimlikleriyle (F-0xx) yazıldı.

Kısaltma: ✅ merge edildi · ◐ kod merge edildi ama başka modül veya karar yüzünden uçtan uca çalışmıyor · ☐ başlamadı.

### 9.1 Teknik işler (TEK) ve kayıtlar

| İş | PR · merge | Durum |
|---|---|---|
| Karar kaydı SAT-D1…D12 + bu plan; plan durumu 2026-10-26 | #799, #818 · `cc7a7ceb`, `72d274ed` | ✅ |
| S0 temizlik: TEK-06 UI etiketi, TEK-09, TEK-10, TEK-12 | #805 · `f87ca960` | ✅ |
| TEK-01…03 ortak Money/ExchangeRate/Quantity, Satış fiyatları Money | #823 · `4a79e37d` | ✅ |
| TEK-04 outbox (= MOD-09) | #986 · `cef78270` (Satış outbox tablosu), #999 · `9529be5e` (Work dağıtıcı çekirdeği) | ◐ `sales.invoice.finalized.v1` yazılıyor; Satış olaylarını okuyan dağıtıcı bağlantısı yok |
| TEK-05 idempotency ADR | #826 · `b5dd0b16` | ✅ |
| TEK-06 API v2 adı (`/orders`, `SalesOrder`) | – | ☐ |
| TEK-07 büyük dosyaları bölme | – | ☐ (dosyalar büyüdü: `SalesStore.cs` 2.499, `SalesOrderDraft.cs` 2.061 satır) |
| TEK-08 + TEK-13 test borcu | #808 · `2c5bf214`, #1020 · `e147ca77` | ✅ |
| TEK-11 detayda birim adları | #810 · `60142c56` | ✅ |
| PRS-01 persona grant kartı (Satış satırları) | #871 · `641af4a3`; rol taslağı SAT-D230 #1001 · `b809bb0c` | ◐ kart ve taslak hazır; gerçek atama yok — Ayarlar/Kimlik rol ekranı bekleniyor (F-001; insan kapısı) |
| PRS-02 Sevkiyatçıya fiyatsız okuma | tek PR'a bağlı değil (#842, #850, #865, #1046) | ✅ liste fiyat taşımaz; detay, PDF, toplamlar, raporlar ve fatura listesi fiyat okuması yoksa `Withheld`/403 |

### 9.2 API uçları

| İş | PR · merge | Durum |
|---|---|---|
| API-01/02 + UI-12 zorla kapatma | #807 · `8c3b18ed` | ✅ |
| API-03 toplamlar | #850 · `4c3075f4`; vergi #885 · `511382ae` | ✅ |
| API-04 başlık revizyonu | #864 · `411311e8` | ✅ |
| API-05 satır ekle/çıkar/ürün değiştir | #926 · `76225bdb` | ✅ |
| API-06 risk kontrolü (+ MOD-01) | #839 · `a63d1def`, #854 · `edc61c3f` | ◐ Finans portu her zaman "kaynak yok" döner; kredi limiti/teminat kaynağı yok (F-018, Finans/Cari, OPEN-010) |
| API-07 tek sevk risk istisnası | #1054 · `9b1467a9`, #1057 · `9091e7d4` | ◐ istisna kaydı ve onayı çalışır; sevkte tüketilmesi Lojistik'i bekler (F-006 ikinci dilim), iş değeri F-018 sonrası |
| API-08 durum eksenleri | #832 · `f1b9ef8b` | ✅ |
| API-09 Faturalar sekmesi | #982 · `d5eaf744` | ✅ |
| API-10 siparişte müşteri iadeleri | #817 · `5ee11b08` | ✅ |
| API-11 kaynak planı (S-2) + MOD-05 | #1040 · `c8e090c9`; bağlı tedarik portu #931 · `30392fb9`, Alış adaptörü #998 · `1009fe39` | ◐ Alış bağlı; Üretim adaptörü yok (Üretim tarafı "kaynak yok") |
| API-12 iskonto limiti aşımı onayı | #834 · `865a892b`; limit grupları #852 · `4a87b8fc` | ✅ |
| API-20/21 talep | #824 · `8b57c9ef` | ✅ |
| API-22…24 teklif, kayıp, dönüşüm | #828 · `861e5238`; takip #849 · `945d8ebe`, #984 · `5f9a30e6` | ✅ |
| API-30/31 sevkiyat listesi, faturaya hazır adaylar (+ MOD-06) | #856 · `693ceb81` | ✅ |
| API-32 fatura taslağı ve hesaplayıcı | #975 · `bab70cb4` | ✅ |
| API-33 fatura onay/red/iptal | #980 · `5b5baa23` | ✅ |
| API-34 Excel dışa aktarım | #1047 · `b6099323`, #1052 · `fb2b635c` | ✅ |
| API-35 resmî belge eşleştirme | #981 · `7664278b`; dövizli tolerans #1021 · `e3643fc9`; KDV/tevkifat ayrı #1046 · `78adcd68` | ◐ resmî fatura okuma portunu uygulayan modül yok → 503; resmî belge verisinin sahibi Sinan kararı (F-005) |
| API-36 kesinleştirme → Finans açık kalem (+ MOD-04) | #986 · `cef78270`; Finans kabul portu #929 · `1e694aa3` | ◐ eşleşme olmadan kesinleşme yok; F-005'e bağlı |
| API-37 Kontrol Merkezi | #983 · `40aae54a`; takip #992 · `45f654bf`, #1042 · `efe38033` | ✅ |
| API-40 müşteri iade faturası | #1059 · `36885556`; ret/iptal #1063 · `5e6ab310` | ◐ kayıt, gönderme, onay, ret, iptal çalışır; kesinleştirme 503 (`sales_customer_return_finance_port_unavailable`) — Finans API-11 ters kayıt portu yok |
| API-41/42 raporlar | #865 · `d7e19e4a` | ✅ |
| API-50…52 Ayarlar politikaları (+ MOD-03 dilim 1) | #813 · `118254b0` | ✅ |

### 9.3 Ekranlar ve görev notları (S-)

| İş | PR · merge | Durum |
|---|---|---|
| UI-10 altı sekme + UI-11 Belgeler sekmesi | #806 · `5be1a9c4` | ✅ |
| UI-13 etiket seçici (+ MOD-12 = ST-01) | #816 · `e2e981d2`, #822 · `78cdf905` | ✅ |
| UI-14 yeni siparişte fiyat girişi | #831 · `9fc3a41a` | ✅ |
| UI-15 toplamlar, durum eksenleri, risk kartı | #850, #836 · `ae0432e9`, #872 · `40f64ba8` | ✅ (risk kartı F-018 nedeniyle "kaynak yok" gösterir) |
| UI-20 S-01 talep/teklif havuzu | #830 · `a543c7ce` | ✅ |
| UI-50 S-05 kontrol + marj | #1016 · `e6e23b60`, #1045 · `ee6a8d15` | ◐ marj maliyeti Stok defterini bekler: ortalama maliyet çekirdeği var (#1044 · `184fac03`, #1062 · `6986296d`), defter yazıcısı ve Satış bağlantısı yok (F-011, Stok) |
| UI-51 S-06 onaylar + Work "Satış" filtresi (SAT-D243) | #1016, #1024 · `f21174e1` | ✅ |
| UI-60/61 S-07 sevkiyat, S-08 faturaya hazır | #856 · `693ceb81` | ✅ |
| UI-62 taslak oluşturma, UI-64 taslak komutları | #1018 · `64922021`, #1025 · `d3a5c421` | ✅ |
| UI-63 S-09 fatura listesi | #1017 · `f5cfb63e` | ✅ |
| UI-64b eşleştirme formu, kesinleştirme ekranı | #1015 · `e817700b`, #990 · `1256156c` | ◐ ekran hazır; arka uç F-005 nedeniyle 503 |
| UI-65 Kontrol Merkezi | #1014 · `0a5baaf0`, #1036 · `401607af` | ✅ |
| UI-70a iade görünümü | #829 · `af15f6b4` | ✅ |
| UI-70b iade kaydı ekranları | #1059 · `36885556`, #1063 · `5e6ab310` | ◐ kesinleştirme API-40 ile aynı nedenle kapalı |
| UI-71 S-11 Sözleşme/Belgeler çalışma alanı | #825 · `e3928d49` | ✅ (belgeler) |
| S-11 Satış sözleşmesi kaydı (SAT-D28) | – | ☐ SAT-D28 kaydı Satış'ta ayrı kayıt olarak tanımlar; ödeme planı ve iskonto kuralları ayrı politika kararıdır. Anlaşma fiyat kademesi (SAT-D58) bu kaydı bekler |
| UI-80 raporlar | #865 · `d7e19e4a` | ✅ |
| MOD-07 sipariş PDF'i | #842 · `c625ca04`, #1038 · `89e3a272` | ✅ |
| MOD-08 kullanıcı dizini, sorumlu seçici | #827 · `dd5f20d7`, #844 · `13062ce2` | ✅ |
| MOD-02 fiyat kademesi | #843 · `c86416e7` | ✅ |
| MOD-10 Work onay adaptörleri | – | ☐ Work modülünde Satış'a özgü backend adaptörü yok; S-06 ve Work filtresi ekran tarafında (#1024) |
| MOD-11 Muhasebe vergi kontrolü | – | ☐ Faz 6 sonrası (plan §7) |
| S-1 bağlı tedarik okuma | #931 · `30392fb9` | ✅ (Üretim adaptörü yok) |
| S-2 sipariş kaynak planı | #1040 · `c8e090c9` | ✅ |
| S-3 fason hizmet siparişi | #898 · `8156dc78` | ✅ |
| SAT-D17 satır eksikle kapatma, SAT-D30 fazla teslim onayı, SAT-D32 kullanıcı kapatması | #985 · `990ce28e`, #987 · `2f391006`, #963 · `2f2be741`, #952 · `ca36d045` | ◐ Lojistik sevk edeni (`ShippedBy`) bildirmediği için onaylar her zaman reddedilir (F-006, Lojistik; migration gerekir) |
| Döviz kuru ve iş günü | #906 · `f4936c1d`, #914 · `438e87db` | ◐ kur ekranı yok, kaynak kapalı; pratikte yalnız TRY (F-025, OPEN-012) |

Notlar:

- **Rol ve yetki:** izinler kayıtlı; çoğu hiçbir rolde değil. Rol tanımı ve atama Ayarlar rol ekranından yapılacak
  (SAT-D282, SAT-D287); ekran yok (F-001). Atama insan kapısıdır.
- **Satış → finans zinciri:** taslak, onay ve dışa aktarım çalışır; resmî belge eşleşmesi, kesinleşme ve alacak
  F-005'teki Sinan kararını bekler. İade faturası kesinleşmesi ayrıca Finans API-11 ters kayıt portunu bekler
  (SAT-D291). EK57'nin üç açık sorusu EK58'de (#1058 · `084ceb0f`, SAT-D292…D294) cevaplandı.
- **Yeni `NEEDS-SINAN`:** reddedilen veya iptal edilen müşteri iadesinin müşteri belge numarası yeni bir iade
  kaydında yeniden kullanılabilir mi? Cevaplanana kadar kural seçilmez.
- **API-10 sınırı** (2026-10-26 notu) aynen geçerli: Lojistik portu satır başına yalnız onaylı toplamı verir.

Sıradaki işler (Satış dışı sahiplerde): F-006 Lojistik `ShippedBy` (SAT-D17/D30/D32 ve API-07 tüketimi açılır),
F-005 resmî fatura verisi kararı ve portu, Finans API-11 ters kayıt portu (iade kesinleşmesi), F-011 Stok maliyet
defteri yazıcısı ve Satış marj bağlantısı, F-018 limit/teminat kaynağı (OPEN-010), F-025 kur ekranı (OPEN-012),
F-001 rol ekranı. Satış şeridinde kalan: S-11 sözleşme kaydı (SAT-D28), TEK-06 API v2 adı, TEK-07 dosya bölme,
TEK-04 için Satış olaylarının dağıtıcıya bağlanması, MOD-10.
