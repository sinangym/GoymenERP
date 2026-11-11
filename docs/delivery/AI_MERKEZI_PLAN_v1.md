# AI Merkezi — Var/Eksik Tablosu ve Dilim Planı v1

Tarih: 2026-11-03 · Şerit: AI Merkezi (Claude, oturum `session_015Usc2puiKz7TEEqG49ym5e`) · Taban: `main@2318317`

Kaynaklar: `AGENTS.md`, `KARAR_SICILI.md` (DEC-AI-001…009), `SINAN_KARARLARI_2026-11-03_AI.md` (AI-D1…D6),
`SINAN_KARARLARI_2026-10-21.md` (No 156/157/159), `SINAN_KARARLARI_2026-10-22.md` §1.3 (W05.1 → W05.2 → W06 açık),
`docs/security/AI_VERI_GUVENLIK.md`, `docs/security/AI_YETKI_SOZLESMESI.md`, master rapor v3 §6 ve §25.1–25.3,
`docs/features/ERP-000J_AI_GATEWAY_CONTRACT_v1.md`, `docs/ui/UI_DESIGN_SYSTEM.md` §7. Birleşmemiş önceki çalışmalar
referans olarak okundu: `origin/docs/ai-merkezi-master-rapor-v1` (AI Merkezi master v1.5 + kodlama tasarımı v1.3) ve
`origin/codex/w051-ai-contract-20261022` (W05.1 sözleşme tipleri + önerilen ADR-008).

## 1. Hedef (Sinan, 3 Kasım)

AI dört iş yapar (AI-D1): **sistemi kullanır** (kullanıcı onayıyla işlem), **iş yükünü azaltır** (taslak, OCR,
özet), **sistemi tanır** (ekran, kural ve kaydı açıklar), **genel sorulara cevap verir**. İki kaynak vardır
(AI-D2): **yerel sistem AI'sı** ve **API ile bağlı dış AI**. Yerleşim (master §6.1): sayfa içi mikro AI, her
sayfada **sağ AI paneli**, **AI Merkezi** (günlük özet, öneri, geri bildirim).

Değişmezler: AI DB'ye veya ham SQL'e erişmez, yalnız modüllerin şemalı araçlarını kullanır · yazma =
önizleme + kullanıcı onayı + idempotency, AI kendi onay veremez · AI kapalıyken ERP normal çalışır (DEC-AI-001) ·
her AI çağrısı kaydedilir (DEC-AI-005) · dış AI'a hassas veri yalnız takma adla ve yöneticinin açtığı veri
türünde gider (AI-D4) · secret repoya girmez · dış AI'ın canlıda açılması, veri türü izni ve rol/yetki Sinan'da.

## 1a. Durum güncellemesi (2026-11-11, main@0a789a2)

| Dilim | Durum | Kanıt |
|---|---|---|
| AI-0 | Merge edildi | Bu plan + `SINAN_KARARLARI_2026-11-03_AI.md` |
| AI-1 (W05.1) | Merge edildi | #945; ADR-008 (yalnız sözleşme katmanı kabul), `BuildingBlocks/Ai/*`, `GoymenERP.Ai.UnitTests` |
| AI-4a (dış AI süzgeci) | Merge edildi; sertleştirme PR #1077 (açık) | `AiExternalEgressV1.cs` (#970) |
| AI-5 (sağ panel) | Merge edildi; panel düğmesi her sayfada görünür, açılınca her durumda "kullanılamıyor" gösterir; çalışan AI yolu yok | #947; `src/frontend/goymen-erp-web/src/ai/` |
| AI-2 | AI-2a (yazma tarafı) PR #1093 açık. E1–E7 ve E9 BS-11/BS-15 ile kabul; açık olanlar: E8 (insan kapısı) ve ADR-SEC-001'in K3 kabulü | `docs/adr/ADR-SEC-001-durable-security-event-channel.md` |
| AI-3 | Bekliyor: AI-2 (ERP-000J §18) | — |
| AI-4b (Ayarlar AI politikası) | Bekliyor: Ayarlar ADR-SET-002 runtime (Ayarlar şeridi) | — |
| AI-6…AI-10 | Bekliyor: AI-3 ve modül portları (§5) | — |

Açık sorular tek belgede: `docs/delivery/AI_KARAR_PAKETI_2026-11.md` (A–E). Fable 5.2 analizi bu modülü
F-030 olarak işaretler (OPEN-020'ye bağlı, "Sonra" grubu); rol sorusu F-001 ile birlikte sorulur.

## 2. Var / eksik tablosu (main@2318317)

| Alan | Var | Eksik | Dilim |
|---|---|---|---|
| Kararlar | DEC-AI-001…009; No 156 (önizleme 15 dk, A fazı salt okunur), No 157 (araç çağrısı/red kaydı 1 yıl), No 159 (soru/cevap maskeli 90 gün); AI-D1…D6 | OPEN-020 (sağlayıcı, sözleşme, KVKK, yerel model/GPU) açık | — / NEEDS-SINAN |
| ADR-008 (AI gateway) | Codex dalında "Önerildi" taslağı (main'de yok) | Main'de fiziksel ADR-008 | AI-1 |
| Sözleşme tipleri | Codex W05.1 dalında `BuildingBlocks/Ai/*` (araç tanımı, risk sınıfı, veri sınıfı, kanonik JSON, önizleme hash'i, audit DTO, ~30 test) | Main'de yok; dal 22 Ekim tabanlı, cherry-pick + uzlaştırma gerekir | AI-1 |
| AI modülü (backend) | — | `Modules/Ai` (Domain/Application/Infrastructure), endpoint'ler, DI, araç kataloğu (default-deny), politika sırası | AI-3 |
| Model sağlayıcı | — | Sağlayıcı portu; sahte (fake) sağlayıcı; yerel model adaptörü; dış AI HTTP adaptörü (kapalı) | AI-3 / AI-8 |
| Veri sınıfı + maskeleme | Audit'te maskeleme alanları kararı (No 135: VKN/TCKN, IBAN, telefon, e-posta, adres) | Alan bazında veri sınıfı etiketi, takma ad (pseudonym) üretici ve geri çözücü, dış AI süzgeci | AI-4 |
| AI ayarları | Ayarlar modülünde tipli politika sağlayıcıları; ADR-SET-002 politika kaydı **taslak** (runtime yok) | `ai.externalProvider.enabled` (varsayılan kapalı), izinli veri türleri listesi (varsayılan boş) | AI-4 |
| Denetim / AI çağrı kaydı | `AuditEventDraft` içinde `AiInvolvement` (None/Assisted/Proposed/Executed) ve `AiContextJson`; `SourceChannels.AiAssisted`; `AuditActorTypes.Ai` | Kalıcı güvenlik olayı kanalı (No 134: depo kararı var, runtime yok; No 157: AI araç çağrıları ve redler buraya, 1 yıl) — ERP-000J §18 gereği gateway runtime bu kanal olmadan başlamaz | AI-2 (W05.2) |
| Shell capability | `GET /api/v1/shell/capabilities`, `IShellCapabilityProvider`, frontend `CapabilityRoute` | AI capability bildirimi (önerilen adlar `ai.panel.use`, `ai.center.view` — ASSUMPTION, katalog kaydı, grant yok) | AI-3 / AI-5 |
| Sağ AI paneli (frontend) | Shell 240 px sol menü + içerik; tasarım sisteminde 320–380 px sağ panel tarifi (UI_DESIGN_SYSTEM §7) | `AppShell` sağ panel yuvası, `AiContextPanel`, kapalı/yetkisiz/hata/yükleniyor durumları, şirket değişince oturum sıfırlama | AI-5 |
| "Sistemi tanır" bilgisi | Master rapor, karar sicili, ekran kataloğu, durum makineleri (docs/) | Ekran/alan/kural açıklama bilgisinin sürümlü ve yetki süzgeçli okunması (`platform.help.explain` aracı) | AI-6 |
| Modül araçları (READ) | Modüllerin public query portları; global arama `IGlobalSearchSourceV1` | Modül başına READ adaptörleri — şeritlerden ihtiyaç listesi istendi (§5) | AI-6 |
| AI Merkezi (AI-01/02/03) | Ekran kataloğunda `DECISION_ONLY_REFERENCE_MISSING` | Günlük özet, öneri kartları (master §6.3 alanları), geri bildirim (No 159), araç/politika görünümü | AI-7 |
| Belge okuma (OCR) | Documents modülü: append-only dosya kasası, PDF/PNG/JPEG, tarayıcı, yetkili içerik okuma | OCR/çıkarım portu, taslak ekranı (kaynak sayfa/alan, güven, düzeltme — DEC-AI-008) | AI-9 |
| DRAFT araçları | — | Belgeden/metinden form taslağı (kayıt yazmaz) | AI-9 |
| WRITE araçları | Modüllerde idempotent komutlar, `CommandExecution` | Önizleme → onay → sahip modül komutu; No 156: B fazında ikinci onay + adım yükseltme (kapalı) | AI-10 |
| Test | Modül unit testleri, SQL kabul testleri, vitest, Playwright | `GoymenERP.Ai.UnitTests`, AI SQL kabul testi, panel vitest'leri, red-team/golden seti | her dilim |

## 3. Uyarlanan önceki tasarım kararları

- W05.1 sözleşme tipleri tek kanon olur; master v1.5'teki eksik alanlar (kapsam, dış çıkış, kill switch,
  bayt/kardinalite sınırı) ona eklenir.
- Master v1.5'teki "Çok gizli asla dışarı gitmez / maskeleme sınıf düşürmez" kuralı AI-D4 ile güncellenir:
  ham hassas değer hiç gitmez; takma ad yalnız izinli veri türünde gider; Sinan'ın listesinde olmayan
  "Çok gizli" kalemler hiç gitmez.
- Önizleme geçerliliği 24 saat değil **15 dakika** (No 156). Saklama No 157/159'a göre.
- Model tutar yazmaz: sayısal değerler araç sonucundan sunucuda yerleştirilir (master v1.5 `measure` ilkesi,
  DEC-AI-007).
- Ağır iş akışı/kurtarma/workflow katmanı ertelenir; önce READ + sahte sağlayıcı + panel kurulur.

## 4. Dilimler

| Dilim | K | Kapsam | Kabul |
|---|---|---|---|
| AI-0 | docs | Bu plan + `SINAN_KARARLARI_2026-11-03_AI.md` | İki review, validator'lar |
| AI-1 (W05.1) | K3 | ADR-008 main'e; W05.1 sözleşme tipleri (araç tanımı, risk/veri sınıfı, kanonik JSON, önizleme hash'i, audit DTO) + `GoymenERP.Ai.UnitTests`; runtime yok | Tip testleri; çözüm/test grubu/mimari parmak izi uzlaşması |
| AI-2 (W05.2) | K3 | Önce ERP-000L §6.2/§16 açık maddeleri (sahip, şema, yazma hatası davranışı, saklama, hedef) kendi K3 ADR'ı veya NEEDS-SINAN ile karara bağlanır; kod ondan sonra. Öneri: append-only, şirket kapsamlı kanal, yazılamazsa çağrı reddedilir (fail-closed); No 134 olay türleri + AI araç çağrısı/red (No 157, 1 yıl). Öneri sahiplik: platform (ERP-000L); AI şeridi geliştirir, platform sahibi review'a çağrılır | Gerçek SQL: yazılamazsa çağrı reddedilir; değiştirilemez; şirket sızıntısı yok |
| AI-3 | K3 | `Modules/Ai` runtime: default-deny araç kataloğu, politika sırası (ERP-000J §8), sahte sağlayıcı, `GET /api/v1/ai/capabilities`, `POST /api/v1/ai/ask` (yalnız READ), AI capability katalog kaydı (önerilen adlar, grant yok) | Kapalıyken disabled ve ERP etkisiz; tanımsız araç reddi; CompanyContext yoksa sağlayıcı çağrılmaz; her çağrı + red güvenlik olayı kanalında |
| AI-4 | K3 | Veri sınıfı etiket tablosu (alan bazlı, review'a), takma ad üretici/çözücü, dış AI süzgeci; Ayarlar'da AI politikası (dış AI kapalı, izinli tür boş) | Ham hassas değer dış adaptör girdisinde hiç görünmez; izin yoksa tür gitmez; "Çok gizli" ve eşlenmemiş "Gizli" her durumda reddedilir |
| AI-5 | K2 | Frontend: sağ AI paneli (her sayfa), capability kapısı, durumlar, şirket değişimi sıfırlama, güvenli metin gösterimi | vitest + a11y; capability yokken panel kapalı, sahte cevap yok |
| AI-6 | K3 | READ araçları: ekran/alan/kural açıklama, global arama sarmalayıcı, şeritlerden gelen ilk 3'lük listeler (başka modül portu = modüller arası sözleşme) | Yetkisiz kayıt cevaba/sayıya/kaynağa sızmaz; kaynak açılırken yeniden yetki |
| AI-7 (W06) | K2 (migration varsa K3) | AI Merkezi AI-01 (günlük özet, öneri kartı), AI-02 geri bildirim (No 159: maskeli, 90 gün, kullanıcı kimliği ayrık), AI-03 araç/politika görünümü | Her özet cümlesinin kaynak kaydı; geri bildirim maskeli |
| AI-8 | K3 | Dış AI HTTP adaptörü (kapalı, anahtar yalnız ortam değişkeni), yerel model adaptörü portu | Ayar kapalıyken dışarı sıfır istek; AI-D5 kanıtsızsa açılamaz |
| AI-9 | K3 | Belge okuma (OCR) portu + DRAFT araçları + taslak ekranı | OCR kesin kayıt yazmaz; kaynak sayfa/alan, güven, kullanıcı düzeltmesi bağlı |
| AI-10 | K3 | WRITE: önizleme (15 dk) → açık onay → sahip modül komutu (idempotent); ikinci onay + adım yükseltme; varsayılan kapalı | AI kendi onaylayamaz; payload/sürüm değişince eski onay reddedilir; aynı anahtar tek etki |

Sıra W05.1 → W05.2 → W06 kararına (2026-10-22 §1.3) uyar: AI-0 → AI-1 → AI-2 → AI-3 → AI-4 ve AI-5 (paralel) →
AI-6 → AI-7 → AI-8 → AI-9 → AI-10. Her dilim ayrı PR'dır. AI-8, AI-4 süzgeci main'e girmeden merge edilmez.

## 5. Modül şeritlerinden gelen araç ihtiyaçları (3 Kasım; 11 Kasım AI-6 güncellemesi)

3 Kasım'da beş şeride aynı soru gönderildi; cevaplar aşağıdadır. Adlar şeritlerin önerisidir (AI-6'da
katalogda sürümlenir). İzin adları mevcut `ErpPermissions`/manifest adları ya da sahip şeridin açık PR'ında planlanan adlardır ("bekleyen" diye işaretli); AI şeridi yeni ad açmaz. Tutar/fiyat
maskesi sahip serviste uygulanır (ör. `sales.orders.price.read` yoksa Withheld; Finans `…amount.read` yoksa
Redacted); AI bu maskeyi aynen taşır, tutarı yeniden hesaplamaz veya toplamaz.

| Modül | İlk 3 (READ/SIMULATE) | Dayanak | İzin | Hassas |
|---|---|---|---|---|
| Satış | `sales.order.status.explain`, `sales.open_orders.daily_summary`, `sales.customer.risk.explain` | `GET /api/v1/sales/drafts/{id}/status`, `…/{id}`, `…/{id}/line-pricing`; `GET /api/v1/sales/reports/open-orders`; `GET …/{id}/risk-check` | `sales.drafts.read`, `sales.reports.read` (+ `sales.orders.price.read`) | fiyat, iskonto, risk limiti/teminat/açık alacak |
| Alış | `purchasing.request.explain`, `purchasing.approvals.pending`, `purchasing.order.cancel-impact` (SIMULATE, #932) | `IPurchasingService` (`GetRequestDetailAsync`, `GetRequestListAsync`, `GetOrderListAsync`); `GET …/orders/{id}/cancel-impact` (`GetOrderCancelImpactAsync`) | `purchasing.orders.read` (talep okuma ayrı ad yok, ASSUMPTION-PUR4-1), `purchasing.orders.cancel` (main'de) | sipariş fiyat/tutar/KDV/kur (Price; `PricePending` maskesi main'de, ayrıntı §5a) |
| Finans | `finance.party-balance.explain`, `finance.party-ledger.explain`, `finance.open-items.overdue-summary` | `IPartyLedgerService.ReadAuthorizedAsync`, `SummarizeAuthorizedAsync` (main'de); `IFinanceOpenItemReadServiceV1.ListAuthorizedAsync`/`DetailAuthorizedAsync` | `finance.party-ledger.read` (+ `.amount.read`), `finance.open-items.read` (+ `.amount.read`) | tüm tutarlar, cari kimliği, IBAN, çek no (Finans önerisi: IBAN dış AI'a hiç açılmasın) |
| Üretim | `manufacturing.plan.explain`, `manufacturing.plan.simulate` (SIMULATE), `manufacturing.order.variance.explain` | `GET plans/{id}`; `POST plans/preview` (calculator, kayıt yok); `GET orders/{id}` | `manufacturing.plans.read`, `manufacturing.plans.write`, `manufacturing.orders.read` | yok (ticari bağlam müşteri adı taşıyabilir: dışarı aktarılmaz) |
| Kalan Modüller | Liste K0 dalgasından sonra gelecek | Belgeler: `IDocumentContentReadPortV1` istendi (K6, AI-9 için) | `documents.files.read` + sahip kontrolü | şirket belgeleri |

### 5a. AI-6 veri sınıfı girdileri (11 Kasım, şeritlerin önerisi — karar değil)

Kaynak: Satış, Alış, Finans ve Üretim şeritlerinin 11 Kasım cevapları (taban `main@0a789a2`…`3f60f58`). Tabloda
"(planlanan)" işaretli olanlar dışındaki dayanaklar main'de mevcuttur; AI için yeni port veya izin adı açılmaz. Üretim ayrı bir AI okuma portu planlamaz;
AI host mevcut HTTP uçlarını/servislerini kullanır.

| Modül | Dönüş tipi / sonuç | Şirket içi (dış AI açılırsa takma adsız gidebilir; dış AI canlıya alma AI-D6 insan kapısı) | Hassas tür (AI-D4: yalnız takma ad + yöneticinin açtığı tür) | Dış AI'a hiç gitmez (cevapsızken) |
|---|---|---|---|---|
| Satış | `SalesOrderDraftStatusResponse`, `SalesOpenOrderReportResponse` (grup düzeyinde `SalesOpenOrderGroupView.Complete=false` = kısmi; `TooManyOrders` grubunda toplamlar null), `SalesOrderDraftRiskCheckResponse` | durum, kimlikler, miktarlar, risk sonucu/gerekçe kodları | fiyat/iskonto/tutar (Price, FinanceAmount; Withheld maskesi aynen taşınır), risk tutarları (Balance), belge no (CompanyDocument) | müşteri adı, cari kimliği (§D) |
| Alış | `PurchaseRequestDetailView`, `PurchaseRequestListResponse` (Submitted), `PurchaseOrderListResponse` (Draft), `PurchaseOrderCancelImpactView` | kimlikler, durum, sürüm, tarihler, miktarlar, Cancellable/Blocker/HasAcceptance | sipariş fiyat/tutar/KDV/kur/ödeme planı (Price; `purchasing.orders.price.read` yoksa sahip serviste gizli, `PricePending` dahil) | ret/geri çekme gerekçesi serbest metni, SupplierPartyId/CompanyAccountId (§D), aktör kimlikleri |
| Finans | `PartyBalanceResult`, `PartyLedgerResult` (`TooManyEntries` > 1000 satır, kesilmez), `ReadResult<FinanceOpenItemPageV1>` | Kind, Side, Status, Direction, tarihler, CurrencyId, Version, SourceKind, DocumentId/InstrumentId | bütün tutar/bakiye alanları (Balance, FinanceAmount; `.amount.read` yoksa Redacted, Money ve Status null) | PartyId/CompanyAccountId (§D); IBAN bu portlarda yok |
| Üretim | `PlanViewV1`, `PlanPreviewViewV1` (kayıt yok), `OrderViewV1` (rulo başına kütle dengesi) | kimlikler, iş merkezi, proses, durum, sürüm, kg/mm/adet, etiket/rulo, fire, WIP, parametre snapshot'ı, tolerans | yok | ticari bağlam (commercial party/account), U2-b `salesOrderLink` (planlanan, main'de yok), aktör kimlikleri |

Kur (→ Price) ve belge no (→ CompanyDocument) eşlemeleri şeritlerin önerisidir; AI-D4 listesinde ayrı kalem değildir.

Uygulama kuralları (mevcut kararlardan türer, yeni politika değildir):

- **Cari kimliği:** Finans kimlikleri "Şirket içi" önerir, Satış "Gizli" der. AI-D4'e göre cari kimliği Sinan'ın
  listesinde olmayan "Gizli" kalemdir; karar paketi §D cevaplanana kadar dış AI'a gönderilmez (`AiEgressRulesV1`:
  eşlenmemiş Gizli → Deny). AI şeridi yeniden sınıflamaz.
- **Tutarlar:** Finans'ın "Gizli mi Çok gizli mi" sorusunu AI-D4 cevaplar: bakiye ve finans tutarı "Gizli" sınıfı +
  Balance/FinanceAmount türüdür ("Çok gizli" etiketlenirse kod her durumda reddeder), yalnız takma adla ve tür izni açıksa gider; izin varsayılan kapalıdır. Yeni soru açılmaz.
- **Aktör kimlikleri ve serbest metin** (PreparedBy/ApprovedBy/…, ret gerekçesi): kişisel veri içerebilir; OPEN-020
  KVKK zemini kapanana kadar gönderilmez (`ASSUMPTION-AI-ACTOR-1`: AI-D4 kişisel veri kuralının ihtiyatlı,
  fail-closed uygulaması; Sinan'ın listesinde ayrı kalem değil, OPEN-020 kapanınca yeniden değerlendirilir).
- **Maske:** Withheld/Redacted/`PricePending` sahip serviste uygulanır; AI maskeyi aynen taşır, açmaya çalışmaz.
- **Hesap yok:** AI toplam, fark, aging veya "vadesi geçmiş" özeti hesaplamaz (formül tek backend calculator).
  Finans'ta ayrı vadesi geçmiş özeti ucu yoktur (aging API-90…93 Finans backlog'u); o zamana kadar
  `finance.open-items.overdue-summary` yalnız sunucunun döndürdüğü kalemleri listeler.
- **Eksik risk kaynağı:** Satış risk kontrolünde kaynak yoksa (F-018/OPEN-010) araç "hesaplanamadı" der, "risk yok"
  demez.

Sonraki adaylar (AI-9/AI-10, önizleme + kullanıcı onayı + Idempotency-Key + `expectedVersion`):
Satış iptal/zorla kapatma (önizlemenin `impactToken`'ı zorunlu), teklif/sipariş taslağı; Alış talep taslağı,
talep geri çekme, kalan iptali; Üretim plan taslağı; Finans tahsilat dağıtım taslağı (ayrı simulate ucu yok).

**AI'a hiç açılmayacak komutlar (şeritlerin önerisi, DEC-AI-002):** her türlü onay/ret (sipariş, talep,
iskonto/taban istisnası, plan), plan iptali, emir açma, girdi/çıktı beyanı, kapanış ve istisna onayı,
ödeme/tahsilat kaydı.

**Yok (uydurulmaz, backlog):** tedarikçi performansı (Alış API-91), tedarikçi faturası OCR'ı (Alış A5 Mikro
içe aktarma), termin önerisi, teklif karşılaştırma; satır/miktar revizyonu önizlemesi (Satış); günlük hat
özeti ve kuyruk önerisi (Üretim U4); banka satırı eşleştirme (Finans F4), aging (API-90…93), kur/nakit/kredi
(F7); müşteri bazlı risk ucu.

## 6. NEEDS-SINAN (Issue #66)

Seçenekler, önerilen varsayılanlar ve cevapsızken davranış: `docs/delivery/AI_KARAR_PAKETI_2026-11.md`.

1. Dış AI sağlayıcısı ve hesabı — veri saklamama ve eğitimde kullanmama kanıtıyla (AI-D5, OPEN-020).
2. Yerel model ve OCR çalışma ortamı, GPU kapasitesi (OPEN-020 yerel PoC).
3. Ayarlar'daki AI politikasını hazırlama/onaylama/yayınlama ve AI panelini kullanma izinlerinin hangi
   personalara verileceği (rol/yetki kapısı).
4. AI_VERI_GUVENLIK'te Sinan'ın listesinde olmayan "Gizli" kalemler (marj, cari kimliği, sözleşme) dış AI'a
   takma adla gidebilir mi, hiç mi gitmez ("sözleşme"nin şirket belgesi sayılıp sayılmayacağı dahil).
