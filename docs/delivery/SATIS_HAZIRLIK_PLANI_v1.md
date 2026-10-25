# Satış Modülü Hazırlık Planı v1

Tarih: 2026-10-25 · Hazırlayan: Claude (cloud oturumu) · Durum: **ÖNERİ** (plan; iş kararı değildir)

Kaynaklar: `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` §7.2, §8.1, §11; `docs/decisions/KARAR_SICILI.md`
`DEC-SAL-001…008`; `SINAN_KARARLARI_2026-10-21.md` delegasyon satırları; **`SINAN_KARARLARI_2026-10-25_SATIS.md`
(SAT-D1…D11)**; `src/backend/Modules/Sales/README.md`; `ui/sources/dispatch-invoice/dispatch-invoice-design-spec-v1.0.md`;
`backlog/DELIVERY_BACKLOG_v1.json` W2-SATIS, W2-IRSALIYE, W2-ACIKKALEM, W5-RAPOR.

Bu plan kodun 2026-10-25 `main` durumuna göre yazıldı. İnceleme raporu:
<https://claude.ai/artifact/Y1dKjxbnQ3G7BcwBCYMpJi>.

Şerit kısaltmaları: **CX** Codex (backend), **CL** Claude (frontend), **GP** ChatGPT (spec/kabul), **SN** Sinan (karar).
Seviye: K1/K2/K3 (`GOYMEN_ERP_HIZLI_TESLIM_v4.md`). 2026-10-25'te Sinan, bu plan için Claude'un backend işlerine de
başlamasını istedi; Claude'un yazdığı backend PR'ları yine bağımsız review ve koordinatör merge'ü bekler.

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
| TEK-01 | `BuildingBlocks` içinde ortak `Money` (tutar + para birimi + ölçek) ve `ExchangeRate` değer nesneleri; Ö2 ölçekleri (tutar 2, birim fiyat ≤ 6) ve SAT-D11 yuvarlaması (`AwayFromZero`) tek yerde | AGENTS.md "Money VO kullan"; Satış fiyatları bugün metin | CX | K3 | – |
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
| API-01 | `GET drafts/{id}/force-close-impact` | Zorla kapatma etki önizlemesi (iptalle aynı 6 kategori + token) | `sales.orders.force-close` | No 30, DEC-SAL-005 | K3 |
| API-02 | `POST drafts/{id}/force-close` | Teslim başlamış onaylı siparişin kalanını "iptal edilen kalan" olarak kapat; gerekçe zorunlu; açık/okunamaz kategori varsa red | `sales.orders.force-close` | No 30 | K3 |
| API-03 | `GET drafts/{id}/totals` | Satır tutarı, KDV, tevkifat, toplam (SAT-D11) | `sales.orders.price.read` | SAT-D11, No 15 | K3 |
| API-04 | `POST drafts/{id}/header-revision` | Cari hesap, para birimi, vade, satış sorumlusu, teslim bilgisi revizyonu → Rev+1, yeniden onay | `sales.orders.header.write` (yeni) | DEC-SAL-002, SAT-D3 | K3 |
| API-05 | `POST drafts/{id}/line-revision` | Satır ekle/çıkar, ürün değiştir → Rev+1, yeniden onay | `sales.orders.quantity.write` | DEC-SAL-002, SAT-D3 | K3 |
| API-06 | `GET drafts/{id}/risk-check` | Brüt risk (açık alacak + faturalanmamış sevk + sevk edilmemiş onaylı kalan), limit, sonuç | `sales.drafts.read` | DEC-FIN-007/009 | K3 |
| API-07 | `POST drafts/{id}/risk-exceptions` · `POST …/risk-exceptions/{no}/approval|rejection` | Sevkiyat başı veya tarih aralığı (≤ 30 gün) risk istisnası | `sales.orders.risk-exception.request` / `.approve` (yeni) | No 31, 32 | K3 |
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
| MOD-07 | Belgeler | Sipariş için Belgeler sekmesi; sipariş PDF'i ve fatura dışa aktarım dosyası | CL + CX | mevcut `SalesDocumentOwnerLinkPort` | K2 |
| MOD-08 | Kimlik | Satış sorumlusu için kullanıcı okuma portu | CX | `IUserDirectoryReadPortV1` | K2 |
| MOD-09 | Platform | Outbox/inbox (TEK-04) | CX | ERP-000F | K3 |
| MOD-10 | Work | Talep, teklif, fatura onayları ve risk istisnası için onay kutusu adaptörleri | CX | mevcut Work native approval sözleşmesi | K2 |
| MOD-11 | Muhasebe | Fatura vergi kontrolü (dispatch spec: muhasebe kontrol eder) | CX | Faz 6 sonrası | K3 |

---

## 6. Ekranlar (UI)

| ID | Ekran / iş | Şerit | Sev. | Bağımlılık |
|---|---|---|---|---|
| UI-10 | Sipariş 360'ı altı kilitli sekmeye böl | CL | K2 | – |
| UI-11 | Sözleşme/Belgeler sekmesi (`DocumentsPanel`) | CL | K2 | MOD-07 |
| UI-12 | Zorla kapatma paneli | CL | K2 | API-01/02 |
| UI-13 | Tahsis panelinde etiket seçici | CL | K2 | Stok okuma ucu |
| UI-14 | Yeni sipariş formunda fiyat girişi | CL | K2 | TEK-03 |
| UI-15 | Toplamlar kartı, risk kartı, durum eksenleri | CL | K2 | API-03, 06, 08 |
| UI-20 | S-01 Talep/Teklif Havuzu, "Siparişe dönüştür" | CL | K2 | API-20…24 |
| UI-50 | S-05 Fiyat-İskonto-Marj-Risk Kontrolü | CL | K2 | GP görsel referans, API-50 |
| UI-51 | S-06 Satış Onayları | CL | K2 | MOD-10 |
| UI-60…65 | S-07 Sevkiyat/İrsaliyeler, S-08 Faturaya Hazır, S-09 Faturalar, Faturalar sekmesi, Kontrol Merkezi | CL | K2 | API-30…37 |
| UI-70 | İade görünümü (sipariş ve fatura üzerinde; ayrı menü yok) | CL | K2 | API-10, API-40 |
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
| **S0** | Karar kaydı (bu PR), TEK-09…12, UI-10, TEK-13'ün frontend kısmı | – |
| **S1** | TEK-01…03, API-01/02 + UI-12 (zorla kapatma), API-10 + UI-70 (iade görünümü), UI-11 | – |
| **S2** | MOD-03 + API-50…52, API-03 (toplamlar), API-08 (durum eksenleri), MOD-02 (fiyat kademesi), API-12 iskonto limiti kontrolü | Ayarlar politika kaydı |
| **S3** | TEK-04 (outbox), MOD-01 + API-06/07 (risk), API-04/05 (başlık/satır revizyonu), MOD-08 | ERP-000F |
| **S4** | API-20…24 + UI-20 (talep/teklif), MOD-10 | S2 |
| **S5** | MOD-06, API-30…37, MOD-04, UI-60…65 (irsaliye & fatura, manuel akış) | S2, S3 |
| **S6** | API-40 (iade kaydı), API-11 + MOD-05 (kaynak stratejisi), UI-50/51, API-41/42 + UI-80 | S5 |

---

## 8. Bu oturumda başlatılan işler

- Karar kaydı `SINAN_KARARLARI_2026-10-25_SATIS.md` ve bu plan.
- S0 frontend: TEK-09, TEK-10, TEK-12 (frontend), TEK-06'nın UI etiketi, UI-10 sekmeler.
- S1 backend: API-01/02 zorla kapatma (Claude yazar; K3 iki bağımsız review gerekir).

Her iş ayrı PR'dır; durumları PR açıklamalarında izlenir.
