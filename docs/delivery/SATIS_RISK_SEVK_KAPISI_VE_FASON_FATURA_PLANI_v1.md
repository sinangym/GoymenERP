# Satış — Kredi limiti uyarısı, sevk anı risk portu ve fason hizmet faturası: dilim planı v1

Tarih: 2026-11-12 · Yazan: Satış şeridi (Claude) · Taban: `origin/main` @ `149f516ae` · Durum: **ÖNERİ** (plan; iş
kararı değildir, karar sicilini değiştirmez).

**Kaynaklar (öncelik sırası AGENTS.md):** `KARAR_SICILI.md` DEC-FIN-007 (satır 83), DEC-FIN-009 (satır 85); `ACIK_KARARLAR.md`
OPEN-003/004/010; EK21 SAT-D153…D162; EK55 SAT-D281; EK56 SAT-D283…D287; EK7 SAT-D46…D53, SAT-D80; EK25 SAT-D175/D176; EK29
SAT-D195; EK31 SAT-D201; URT-D16; Finans devri **PR #1117** (main'de, `1a994cbe`): `SINAN_KARARLARI_2026-11-12_FINANS_KREDI_LIMITI.md`
FNS-D27/D27a/D30/D31 ve `docs/delivery/FINANS_F018_KREDI_LIMITI_PLANI_v1.md` §2 L1, §3. Fason fatura kuralı: Issue #66 yorumu
`6820601088` (11. tur) madde 4; karar dosyası bu PR'da: `SINAN_KARARLARI_2026-11-12_SATIS_EK60.md` SAT-D309…D313 (F0).
#1117 `1a994cbe` ile main'dedir.

Kısaltmalar: `SA` = `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Application`, `SD` = `…Sales.Domain`, `SI` =
`…Sales.Infrastructure`, `SR` = `src/backend/Modules/Sales/README.md`, `LR` = `src/backend/Modules/Logistics/README.md`.

---

## 1. Bugünkü durum (main `149f516ae`)

### 1a. Siparişte limit aşımı uyarısı (FNS-D27 (3), DEC-FIN-009 "uyarı" kısmı)

| Parça | Durum | Kaynak |
|---|---|---|
| Brüt risk hesaplayıcısı (DEC-FIN-007, SAT-D48/D49/D53/D80) | ✅ Var, tek deterministik hesap | `SD/Drafts/SalesCustomerRisk.cs:175` (`Evaluate`), karar `:303-311` |
| Risk okuma ucu (API-06) | ✅ `GET /api/v1/sales/drafts/{id}/risk-check`, yalnız okuma | `SA/SalesDraftService.RiskCheck.cs:46`, sözleşme `SA/Contracts/SalesDraftRiskCheckV1.cs:122` |
| Limit girdisi yalnız Finans portundan | ✅ Satış limit hesaplamaz/tutmaz (SAT-D51, FNS-D27 (2)/(5)) | `SA/Contracts/ISalesPartyRiskReadPortV1.cs:15`; Finans: `…/Finance/…Infrastructure/FinanceSalesPartyRiskReadPortV1.cs:30` |
| Finans limit cevabı | 🟡 Bugün hep `NoSource` / `finance_credit_limit_owner_missing` | `FinanceSalesPartyRiskReadPortV1.cs:38,59`; #1117 L1 sonrası `Available` veya `NoSource`+`finance_credit_limit_not_set` |
| NoSource sebep kodunun taşınması | ✅ Port sebebi aynen hesap sebebine geçer | `SA/SalesDraftService.RiskCheck.cs:328` |
| "Faturalanmamış sevk" bileşeni | ❌ Hep `NoSource` (`invoicing_owner_missing`) — fatura çekirdeği (API-32/33) main'de olduğu hâlde bağlanmadı | `SA/SalesDraftService.RiskCheck.cs:152`; `SR:252` |
| Sipariş kaydında/onayında uyarı **kaydı** | ❌ Yok. Kaydet/onaya gönder/onay akışı risk okumuyor; uyarı kayda geçmiyor | (grep: `EvaluateOrderRiskOutcomeAsync` yalnız API-07'de çağrılır, `SA/SalesRiskExceptionService.cs:287`) |

Sonuç: limit gelse bile bugünkü hesap **hiç `Within` dönemez** (`SR:893-894` aynı şeyi yazar). Bu, 1b'deki kapı için de ön şarttır.

### 1b. Sevk anı risk sonucu portu (FNS-D27 (4), FNS-D30, DEC-FIN-009 "sevkte kesin kontrol", SAT-D162)

| Parça | Durum | Kaynak |
|---|---|---|
| Tutarsız risk sonucu (yalnız `Within/Over/Unavailable`) | 🟡 İç arayüz var, **port değil**: komutun izniyle çalışır, Lojistik'e açık değil | `SA/Contracts/SalesShipmentRiskExceptionContractsV1.cs:252` (`ISalesOrderRiskOutcomeSource`), uygulama `SA/SalesDraftService.RiskCheck.cs:130` |
| Tek sevk istisnası (talep/onay/ret/geri çekme) | ✅ SAT-D153…D162, D283 (yükleme talimatı), D284 (`Within` ise ret), D286 | `SA/SalesRiskExceptionService.cs:97-102`; `SR:890` |
| İstisna okuma portu (Lojistik için) | ✅ `ISalesShipmentRiskExceptionReadPortV1` (`None/Pending/Valid/Lapsed/Rejected/Withdrawn`), tutarsız | `SA/Contracts/SalesShipmentRiskExceptionContractsV1.cs:259,297`; `SI/Persistence/SalesShipmentRiskExceptionReadPort.cs:16`; DI `SI/SalesModuleExtensions.cs:107` |
| Sevk anı **risk sonucu** portu (`Within/Over/Unavailable`) | ❌ Yok | FNS-D27 devir tablosu (#1117 plan §3) |
| Lojistik sevk kapısı | ❌ Yok; Lojistik iki Satış portunu da tüketmiyor; kesin sevk rotası yok (LOG-5) | `LR:33`, `LR:85` |
| Limitsiz müşteride istisna talebi | ✅ Bugün kabul edilir (`Unavailable` reddedilmez) | `SA/SalesRiskExceptionService.cs:97-99`, `SR:891-893` |

### 1c. Fason hizmet faturası (#66 `6820601088` m.4; URT-D16 "fatura hizmet bedelidir")

| Parça | Durum | Kaynak |
|---|---|---|
| Fason hizmet siparişi (işaret, yalnız hizmet satırı, kg başı elle fiyat, fiyatsız onaya gidemez) | ✅ SAT-D175/D176/D195/D201 | `SR:736-760` |
| Fason sipariş okuma portu (Üretim için) | ✅ `sales.subcontract-service-order.v1`, fiyatsız | `SA/Contracts/ISalesSubcontractServiceOrderReadPortV1.cs:28` |
| Fatura taslağı | 🟡 Var ama **yalnız Lojistik onaylı irsaliye paylarından**, payın tamamı (sevk miktarı) | `SR:613-615` (API-32) |
| Kantar kaydı | 🟡 Lojistik'te yükleme talimatı başına **bir** elle tartım (brüt/dara/net, ≤3 ondalık, yuvarlanmaz); stok miktarına bağlı değil | `LR:46-60` (AT-053, WGT-001/002) |
| "Müşteri teslim tartımı neti" kaynağı ve Satış'a portu | ❌ Yok | — |
| Fason siparişten net kg ile hizmet faturası | ❌ Yok | — |

---

## 2. Dilim planı

Sıra: S0 → S1 ‖ S2 → S3 (Lojistik) ; F0 → F1 → F2. Her backend dilimi `k3-command-skeleton`, `openapi-regenerate`,
`recovery-inventory-repin`, `test-naming-and-groups` kurallarına uyar. Grant yoktur (SAT-D287 yolu; insan kapısı).

### S0 — "Faturalanmamış sevk" bileşeninin bağlanması (K3: para/formül + modüller arası)
- **Ne:** `RiskCheck.cs:152`'deki sabit `NoSource` yerine DEC-FIN-007 ikinci aşaması: Lojistik'in onaylı sevk payları
  (`ISalesInvoiceableShipmentReadPortV1`, mevcut) − kesilmiş Satış faturasına bağlı paylar (pay anahtarı olmayan
  faturalar için (d)), KDV dahil (SAT-D46), çift sayımsız.
- **Önkoşul:** pay tutarının fatura hesaplayıcısından gelmesi (SAT-D11 ve AGENTS tek hesaplayıcı; SAT-D90 kur). "Finans işlemedi" tutarı **SAT-D311** ile
  cevaplandı (N-6): faturalanmamış sevk toplamında sayılır, ekranda ayrı risk türü/satırı olarak görünür, Finans işleyince
  açık bakiyeye geçer, çift sayım yok. Bu, risk kontrol sözleşmesine ayrı bileşen ekler (`SalesDraftRiskCheckV1`; sürüm
  etkisi S0'da yazılır) ve Finans "işlendi" olgusunu okur (#1117; port/olay adı Finans'ın).
- **Bileşen sınırları (K3 S0 PR'ından önce spec'te kesinleşir; review B P2-1):**
  - (a) Pay faturalanmamış sevkten **yalnız fatura kesildiği an** çıkar ve aynı anda SAT-D311 satırına girer. Taslak/onaylı
    (kesilmemiş), reddedilen veya iptal edilen faturanın payı faturalanmamış sevkte kalır ya da oraya döner. Test: taslak
    faturaya bağlı pay hâlâ faturalanmamış sevkte. SAT-D311 satırı faturalanmamış sevk toplamının alt satırıdır
    (SAT-D311: toplamda sayılır, ayrı satırda gösterilir).
  - (b) "Finans işledi" kümesi `OpenReceivables` ile **aynı Finans cevabından** (aynı anlık görüntü, ör. kabul edilmiş
    `AdmissionId` listesi) gelir; asenkron olay veya Satış'ın API-36 kabul defterinin ikinci kaydı (`SR:665`) kaynak
    sayılmaz, çünkü aradaki gecikme çift sayım veya boşluk üretir. Test: kabul anında tutar tam olarak bir bileşende.
  - (c) Sevk planı bileşeni (SAT-D313; bugün "onaylı kalan sipariş", `ISalesOrderDeliveryReadPortV1` kesin sevk toplamı,
    `RiskCheck.cs:412-430`) ile
    faturalanmamış sevk (`ISalesInvoiceableShipmentReadPortV1` onaylı irsaliye payı) aynı Lojistik olgusuna dayanmalıdır.
    Test: kesin sevk var, irsaliye onaysız → tutar tam olarak bir bileşende.
  - (d) Pay anahtarı taşımayan faturalarda (F2 fason hizmet faturası; SAT-D94 (2) önce-fatura/depo teslim) "faturalandı"
    **sipariş satırı başına**, faturalanan miktar sevk edilen miktarla karşılaştırılarak ölçülür
    (`ISalesDispatchReadPortsV1.cs:102-106`: "invoiced … measured by the invoice owner per order line"); tutar tam olarak
    bir bileşende kalır. LOJ-D14 fason teslimi kapıya aldığı için bu sınır kapı sonucunu doğrudan etkiler. Testler:
    "F2 fason faturası kesilince fason payı tam olarak bir bileşende", "önce-fatura: kesilmiş, sevk edilmemiş tutar tam
    olarak bir bileşende".
- **SAT-D313 (Sinan, #66 `6822385722` + `6822674443`):** risk onaylı siparişin tamamından sayılmaz; **sevk planına açılan
  miktar × birim fiyat** ile başlar (satış ve fason aynı; fason tartı öncesi de böyle), irsaliye/fatura kesilene kadar.
  Tutar durumuna göre görünür: sevk planına alınan → sevk olan (faturalanmamış sevk) → faturası kesilen (SAT-D311 satırı,
  Finans işleyince açık bakiye). Plana alınmamış sipariş kalanı riske girmez (yazar okuması). Bu, bugünkü "onaylı kalan
  sipariş" bileşenini (`RiskCheck.cs:412-430`, DEC-FIN-007 ilk aşaması) değiştirir; S0 K3 PR'ı uygular, Finans'a
  bildirilir. Test: plan → sevk → fatura geçişinin her adımında tutar tam olarak bir durumda; plana alınmamış kalan
  riskte yok. *Açık (S0 spec önerir):* sevk planı miktarının kaynağı (Lojistik okuma portu, mevcut/yeni), fasonda plan
  kg'ı, plan iptali/azaltmasında düşme, durum adları; para etkisi varsa `NEEDS-SINAN`.
- **Dosyalar:** `SA/SalesDraftService.RiskCheck.cs`, `SD/Drafts/SalesCustomerRisk.cs` (gerekirse yeni sebep kodu); frontend
  tüketicisi: sıkı decoder figür listesi ve `riskText.ts:54,73` etiketleri (yeni bileşen; S3 ile).
- **Testler:** hesap birim testleri (aşama sınırı, faturalanmış pay düşer, iptal edilen fatura geri döner), gerçek SQL
  risk olgusu (`Within`/`Over` ilk kez), Finans işleyene kadar ayrı satırda / işleyince açık bakiyede (aynı tutar iki
  bileşende yok), sözleşme testi yeni bileşenle güncellenir (`SalesDraftRiskCheckV1.cs:43` emsali).
- **Modül bağlantıları:** `ISalesInvoiceableShipmentReadPortV1` (Lojistik → Satış, tüketim ✅ mevcut), `ISalesPartyRiskReadPortV1` (Finans; "işlendi" kümesi aynı cevapta, 🟡 Finans'la sözleşme), API-36 `ISalesFinanceAdmissionPortV1` (mevcut, kaynak değil).

### S1 — Siparişte limit aşımı uyarısı ve kaydı (K3: modüller arası okuma + kayıt şeması/migration)
- **Ne (FNS-D27 (3)):** Onaya gönderme (ve/veya kaydet) anında aynı hesap (`EvaluateRiskAsync`) çalışır; sonuç `Over` ise
  **sipariş kaydı durmaz**, uyarı kayda geçer. Siparişte ayrı onay adımı açılmaz (#1117 plan §3 Satış satırı).
- **Kayıt:** Satış'ın kendi tablosunda ekle-yalnız uyarı kaydı + audit olayı (`sales.order.risk-warning.recorded.v1` adayı).
  Alanlar, tetik anı, görünürlük → §4a `ASSUMPTION-SAT-RISK-1…3` (geri alınabilir teknik/UX tercihleri).
- **`Unavailable`'da ne olur** (limit tanımsız, kaynak yok) → §4a `ASSUMPTION-SAT-RISK-4`.
- **Dosyalar:** `SA/SalesDraftService.*` (submit/approve yolu), yeni `SD/Drafts/SalesOrderRiskWarning.cs`, `SI/Persistence/*`,
  migration (forward-fix Down, `ef-migration-forward-fix`), `SR` bölümü, OpenAPI (sipariş detayında uyarı listesi).
- **Testler:** uyarı varken kayıt başarılı (davranış testi), `Within`'de kayıt yok, idempotent replay'de çift uyarı yok,
  şirket filtresi, tutar sızmaz (izinsiz okuyucuda `Withheld`, `SalesDraftRiskCheckV1.cs:118`), gerçek SQL + Down guard.
- **Modül bağlantıları:** `ISalesPartyRiskReadPortV1` (Finans → Satış, 🟡 limit #1117 L1 bekler); yeni bağ yok.

### S2 — Sevk anı risk sonucu portu `ISalesShipmentRiskGateReadPortV1` (K3: modüller arası sözleşme)
- **Sahip Satış, tüketici Lojistik** (FNS-D27 devri; SAT-D209/D281 (3): Satış Lojistik tablosuna yazmaz, Lojistik Finans okumaz).
- **Önerilen şekil (tutar yok, SAT-D162):**
  ```
  Task<SalesShipmentRiskGateReadV1> GetAsync(Guid orderId, Guid loadingInstructionId, CancellationToken ct);
  record SalesShipmentRiskGateReadV1(Guid OrderId, Guid LoadingInstructionId,
      SalesShipmentRiskResultV1 Result,            // Within | Over | Unavailable
      SalesShipmentRiskHoldReasonV1? HoldReason,   // null | OverLimit | CreditLimitNotSet | SourceUnavailable
      SalesShipmentRiskExceptionReadV1 Exception,  // mevcut port cevabı aynen (SAT-D160/D285)
      string ContractVersion);                     // "sales.shipment-risk-gate.v1"
  ```
- **Eşleme:** Finans limit `NoSource` + `finance_credit_limit_not_set` → `Unavailable` + `CreditLimitNotSet` (FNS-D30 (1):
  "limitsiz/sıfır sayılmaz"). Diğer `Unavailable` sebepleri → `SourceUnavailable`. Sebep **kategori**dir; tutar, limit,
  bakiye, ham sebep kodu listesi taşınmaz (SAT-D162). İstisna alanı ayrı portu tekrar okumak yerine aynı cevabı gömer
  (iki okuma arasında yarış olmasın); Lojistik L-2'de kabul etti, Lojistik ayrı istisna portu çağırmaz.
- **Karar Satış'ta değil:** port yalnız olguyu verir; "durdur/geçir" ve tüketim/iptal düşmesi Lojistik kapısının işidir
  (SAT-D285 (4)). Önerilen kapı kuralı (kararlardan türetilmiş): `Within` → geçer; `Over` veya `CreditLimitNotSet` →
  yalnız `Exception.Status = Valid` ile tek sevk geçer (FNS-D27 (4), FNS-D30 (1), SAT-D154/D160); `SourceUnavailable` de
  **SAT-D310** (N-5, Sinan: "Evet, istisnayla geçer") ile yalnız `Valid` istisnayla geçer; istisnanın diğer her durumu
  (None/Pending/Lapsed/Rejected/Withdrawn) → durur. Port hatası/zaman aşımı → durur (istisna da okunamaz; DEC-FIN-009,
  Lojistik L-7). Lojistik'e kapı kuralı değişikliği bildirilir (L-2 metni `SourceUnavailable`'ı durduruyordu).
- **Hesap:** `ISalesOrderRiskOutcomeSource` ile aynı deterministik değerlendirme (SAT-D284 ile tek kaynak); izin ölçümü
  port konvansiyonuna göre tüketicidedir (`ISalesPartyRiskReadPortV1.cs:9`). Lojistik L-3: kendi mevcut sevk kaydı iznini
  ölçer, yeni izin gerekmez; Satış okuması kullanıcı izni değil şirket bağlamlı sistem okumasıdır.
- **Okuma anı (L-1):** kapı yalnız kesin sevk kaydında (stok çıkışının geri dönmediği an) okur, her denemede yeniden okur,
  sonuç cache'lenmez; talimat/yükleme/tartım/irsaliye onayındaki okuma kapı sayılmaz.
- **Anahtar (L-4):** `(orderId, loadingInstructionId)`; bir talimat birden çok siparişe bağlanabilir (tek müşteri), Lojistik
  her sipariş için ayrı sorar; duran sipariş yalnız kendi satırlarının sevkini durdurur.
- **Tüketim/iptal (L-5, SAT-D285):** Lojistik kesin sevk commit olunca outbox olayı yayınlar (talimat, sipariş, sevk kimliği),
  sevk iptalinde ters olay; Satış istisnayı kendi kaydında tüketildi/düştü yapar (inbox/dedup). Satış Lojistik durumunu port
  ile sorgulamaz. Olay sözleşmesini Satış önerir, yayıncı Lojistik'tir; olay adı/sürümü S2 sonrası ayrı dilimde (S2b) yazılır.
- **Tek kullanım (review B P2-3):** v1 istisna cevabında `Consumed` yoktur ve L-5 asenkrondur; bu yüzden tek kullanım
  **Lojistik'in kesin sevk transaction'ında** eşzamanlı zorlanır (`ExceptionNo` başına tek tüketim, tekil kayıt). Satış'taki
  tüketim kaydı yalnız aynadır, kapı değildir. Satış gömülü cevaba `Consumed`/`Lapsed` eklerse bu v1 sözleşme değişikliği
  olarak listelenir (yeni sürüm).
- **Tüketiciler (LOJ-D14, #66 `6821949043`):** kapının iki tüketicisi vardır: Lojistik kesin sevk ve fason teslim
  (F-012). İkisi aynı port ve yukarıdaki aynı kuralı kullanır; fason teslim anahtarı `(fason orderId, loadingInstructionId)`.
  Tek kullanım iki yolu birlikte kapsar: bir `ExceptionNo` sevk veya fason teslimde toplam bir kez tüketilir. #1115 spec 04
  kapı satırı (`SourceUnavailable` + `Valid`) yukarıdaki kuraldan dar okunuyor (`Over`/`CreditLimitNotSet` + `Valid` de
  geçer); Lojistik'e bildirilir.
- **Hata (L-7):** port hatası veya zaman aşımı → sevk kaydı yazılmaz (503 benzeri ret); yeniden deneme kullanıcının aynı
  idempotency anahtarıyla; otomatik tekrar yok. `Unavailable` **sonucu** bir hata değildir: yalnız `Valid` istisna yoksa
  durur (SAT-D310).
- **Dosyalar:** `SA/Contracts/ISalesShipmentRiskGateReadPortV1.cs` (yeni), `SI/Persistence/SalesShipmentRiskGateReadPort.cs`,
  DI `SI/SalesModuleExtensions.cs`, `SR` bölümü; `FinanceSalesPartyRiskReadPortV1` sebep sabiti sözleşme testine.
- **Testler:** sürüm sabiti + şekil sözleşme testi (tutar alanı yok), her eşleme satırı (Within/Over/limit tanımsız/kaynak yok;
  kaynak yok + `Valid` istisna cevabı aynı çağrıda),
  başka şirketin siparişi → `Unavailable` (asla `Within`) **ve gömülü istisna `None`, sevk durur**
  (`SalesShipmentRiskExceptionReadPort.cs:10-12`), şirket bağlamı yoksa fırlatır (kapı fail-closed), gerçek SQL.
- **Modül bağlantıları:** yeni `ISalesShipmentRiskGateReadPortV1` (`sales.shipment-risk-gate.v1`, Satış → Lojistik sevk +
  fason teslim (LOJ-D14), ❌→🟡);
  `ISalesShipmentRiskExceptionReadPortV1` (✅, gömülür); `ISalesPartyRiskReadPortV1` (Finans, 🟡 #1117 L1).

### S3 — İstisna ekranı ve onaylayana limit önerisi kısayolu (K2, frontend)
- Satış S-04 istisna kartında "limit tanımlı değil" durumu ve onaylayana Cari kartı Finans paneline **öneri** kısayolu
  (FNS-D30 (2), FNS-D27a: limit onay yetkisi olan kişi limiti kısayoldan **tek adımda** tanımlar; yalnız öneri yetkisi
  olan öneri açar; yetkisi olmayana kısayol gösterilmez; #66 `6821262098` m.5). Sevkiyatçı yüzeyi Lojistik'tir
  (SAT-D162; yalnız "risk limiti aşıldı, sevk duruyor" + "istisna iste").
- **Frontend metinleri:** `riskText.ts:125` istisna notu FNS-D30 ve SAT-D310'a göre genişler (limit aşımı, limit tanımsız
  ve risk okunamadığında istisna istenebilir); S0'ın yeni risk bileşeni için decoder ve `riskText.ts:54,73` etiketleri.
- **Testler:** `frontend-screen-states` (loading/forbidden/503/stale), kısayol yalnız onay izni olana, tutar yok.
- **Modül bağlantıları:** Finans limit tanımlama/öneri ucu (#1117 L1/L3, ❌); Satış API-07 (✅).

### F0 — Karar dosyası (docs, governance)
- **Yapıldı (bu PR):** #66 `6820601088` m.4 → `docs/decisions/SINAN_KARARLARI_2026-11-12_SATIS_EK60.md`, **SAT-D309**
  (LOJ-D9 ile aynı cevap, #1115). SAT-D308 main'de (#1108, Stok) olduğu için 309 alındı.
  Aynı dosyaya #66 16.–17. tur cevaplarıyla **SAT-D310** (N-5), **SAT-D311** (N-6), **SAT-D312** (N-8) eklendi; #66 `6822385722`/`6822674443` ile **SAT-D313** (sevk planı esaslı risk tutarı).
- **Modül bağlantıları:** yok.

### F1 — Teslim tartımı net kg portu (K3: modüller arası sözleşme; sahibi Lojistik — SAT-D309/LOJ-D9)
- **Öneri:** Satış'ın beyan ettiği, sahibin cevapladığı `ISalesSubcontractDeliveryWeighingReadPortV1`
  (`sales.subcontract-delivery-weighing.v1`): girdi (şirket, talimat, tartım kimliği; #1115 spec 04 anahtarı
  `(CompanyId, LoadingInstructionId, VehicleWeighingId)`, L-8 ile aynı), cevap: talimatın toplam net kg'ı tam ondalık
  metin, sevk satırı başına beyan kg, düzeltme `Version`, ölçüm türü, durum (`Available/NoSource/Unavailable`; asla sıfır).
  Mevcut AT-053 tartımı talimat başınadır (`LR:52,56`); siparişe dağıtımı **SAT-D312** (N-8; Lojistik LOJ-D12/D13): sevk
  satırlarındaki beyan kg oranında; tartısız fason teslim yok. **Lojistik bölme yapmaz;** paylaştırma, artık ve para yalnız
  `SalesInvoiceCalculator`'dadır (AGENTS tek hesaplayıcı; #1115 spec 04 `4f96492a` ile hizalı).
- **Fason teslim kapısı (LOJ-D14):** fason teslim S2 portu ve kuralıyla aynen geçer, anahtar `(fason orderId,
  loadingInstructionId)`; kapı net kg okumasından önce çalışır (#1115 spec 04).
- **Fason satır bağı — öneri (F1 spec + K3 cross-module; karar değil):** Lojistik "tartısız fason teslim kaydedilemez"
  kapısı için satırın fason olduğunu `ISalesSubcontractServiceOrderReadPortV1`'den (`sales.subcontract-service-order.v1`)
  okuyabilir. Sözleşme değişmez; ancak main'deki port dokümanı host adapter'ı Üretim şeridine veriyor, Lojistik bağlantısı
  yeni bir host adapter ve sözleşme testi tüketicisi demektir. Kesin bağ F1 spec'inde.
- **Lojistik L-8 cevabı:** tartım kaydı bugün yalnız olguları taşır (tartı sırası, birinci/ikinci tartı, net, fark); "kimin
  kantarı" ve teslim/sevk ayrımı modellenmedi. Lojistik okuma portu önerisi: anahtar (loadingInstructionId, weighingId) →
  net kg + birim. **Teslim tartısı = talimatın tek tartı kaydı; kesin tanım fason teslim dilimi spec'inde netleşir.** Kesin
  alan adı burada verilmez; kantar sahipliği için yeni alan gerekirse `NEEDS-SINAN` (SAT-D209).
- **Testler:** sözleşme testi, şirket filtresi, tartım yokken `NoSource`, bozuk kayıt `Unavailable`, gerçek SQL (sahip şeritte).
- **Modül bağlantıları:** yeni port (Lojistik/Stok → Satış, ❌); `ISalesSubcontractServiceOrderReadPortV1` (Satış → Lojistik, yeni tüketici, ❌).

### F2 — Fason siparişten hizmet faturası (K3: para/formül)
- **Ne (#66 m.4, URT-D16, SAT-D195):** fatura **fason hizmet siparişinden** kesilir; tutar = teslim tartımı net kg × satırın kg
  başı onaylı fiyatı; fire/hurda faturalanmaz. Hesap `SalesInvoiceCalculator`'da (tek hesaplayıcı; ekranda hesap yok).
  Mevcut fatura taslağı (API-32) irsaliye payı anahtarlı olduğundan, fason satırı için kaynak türü genişler (ayrı anahtar,
  çift faturaya karşı tekil index; AT-055 emsali).
- **Kararlı:** paylaşım beyan kg oranında, tartısız satır faturalanmaz, kesilmiş faturada tartı düzeltmesi → fark faturası
  (SAT-D312). **Açık:** oran paydasının sevk satırındaki alanı ve paylaştırma artığı (F2 spec önerir, para etkisi →
  gerekirse `NEEDS-SINAN`), net kg ve kg payının faturaya hassasiyeti (oranla bölünen pay genelde sonlu ondalık değildir;
  F2 spec önerir, ChatGPT formül golden'ı sabitler, gerekirse `NEEDS-SINAN`), negatif fark faturasının yönü (iade/alacak
  belgesi; F2 spec önerir, gerekirse `NEEDS-SINAN`), birim, tevkifat kodu (SAT-D110: mali müşavir; Ayarlar). Fason faturası
  irsaliye payına bağlanmadığından S0 (d) sınırı uygulanır.
- **Testler:** golden (ChatGPT şeridi formül golden'ı), fire düşmez, aynı tartım iki kez faturalanamaz, fiyatsız/tartımsız
  satır 409 `invoice_not_ready`, iki sipariş tek tartı oran paylaşımı golden'ı, tartı düzeltmesi sonrası fark faturası,
  gerçek SQL, OpenAPI.
- **Modül bağlantıları:** F1 portu (❌), `ISalesSubcontractServiceOrderReadPortV1` (değişmez), `sales.invoice.finalized.v1`
  hedefi (Finans, `SATIS_MODUL_BAGLANTILARI_v1.md:87`, ❌).

---

## 3. Lojistik cevapları (2026-11-12, Lojistik şeridi önerisi, `ASSUMPTION`, geri alınabilir teknik)

Lojistik port önerisini kabul etti (Satış sahipli, tutar yok, Result + HoldReason + gömülü istisna). Para/limit politikası
seçilmedi; kurallar FNS-D27 (4)/FNS-D30/SAT-D154/D160'tır.

| # | Soru | Cevap |
|---|---|---|
| L-1 | Okuma anı | Yalnız kesin sevk kaydında; her denemede yeniden okunur, cache yok. Önceki aşamalarda okuma kapı değildir. |
| L-2 | İstisna gömülü mü | Gömülü; tek çağrı. `Within` geç; `Over`/`CreditLimitNotSet` yalnız `Valid` ile; diğer her durum dur. `SourceUnavailable` Lojistik cevabında duruyordu; **SAT-D310** ile `Valid` istisnayla geçer (Lojistik'e bildirilir). |
| L-3 | İzin | Yeni Lojistik izni yok; mevcut sevk kaydı izni. Satış okuması sistem okuması. Yeni ad gerekirse yalnız ad kodlanır, grant insan kapısı. |
| L-4 | Anahtar | `(loadingInstructionId, orderId)`; sipariş başına ayrı sorgu; duran sipariş diğerlerini etkilemez. Talimatta tek müşteri. |
| L-5 | Tüketim/iptal | Satış kendi istisna kaydında tutar; Lojistik sevk commit'inde outbox olayı, iptalde ters olay yayınlar (S2b). |
| L-6 | Ekran metni | Lojistik'te, resource dosyasında üç ayrı metin (aşıldı / limit tanımlı değil / risk okunamadı). Metin değişikliği kod değiştirmez (N-10). |
| L-7 | Port hatası | Fail-closed, sevk kaydı yazılmaz; kullanıcı aynı idempotency anahtarıyla yeniden dener, otomatik tekrar yok. Lojistik cevabı `Unavailable` sonucunu da durduruyordu; **SAT-D310** ile `Unavailable` sonucu yalnız `Valid` istisna yoksa durur, port hatası her zaman durur (Lojistik'e bildirilir). |
| L-8 | Fason tartım | Karar Lojistik'in değil; okuma portu anahtarı (loadingInstructionId, weighingId); teslim tartısı = talimatın tek tartı kaydı, fason teslim dilimi spec'inde netleşir (F1, N-7). |

## 4. Açık noktalar

Koordinatör ayıklaması (2026-11-12): geri alınabilir teknik/UX seçimleri gerekçeli `ASSUMPTION` olarak yazılır; yalnız para/risk
ve bölüşüm politikası Sinan'a gider (#66, tek satır, öneri ilk sırada).

### 4a. Gerekçeli teknik/UX tercihleri (`ASSUMPTION-SAT-RISK-*`, geri alınabilir; Sinan farklı isterse kod değişir, karar değişmez)

| Kimlik | Tercih | Gerekçe |
|---|---|---|
| N-1 → `ASSUMPTION-SAT-RISK-1` | Uyarı kaydı: sipariş + revizyon, kullanıcı, zaman (UTC + iş günü), sonuç (`Over`/`Unavailable`) ve sebep kategorisi, Finans port sürümü. **Tutar/limit/bakiye anlık görüntüsü yazılmaz.** | SAT-D162 tutarı sevk tarafında göstermez; Satış limit tutmaz (SAT-D51, FNS-D27 (5)). Tutar Finans'ta sürümlü okunur; kayıt yalnız "uyarı verildi" olgusudur. Saklama genel audit politikasına bağlıdır, yeni süre seçilmez. |
| N-2 → `ASSUMPTION-SAT-RISK-2` | Uyarı **onaya gönder** ve **onay/revizyon onayı** anında üretilir; taslak kaydında yalnız ekranda gösterilir, kayıt yazılmaz. Kullanıcıdan "okudum" onayı istenmez; sipariş durmaz. | FNS-D27 (3): "kayıt alınır, uyarı kayda geçer"; blok yok. Taslakta her kaydette kayıt üretmek gürültüdür; karar anları onay akışıdır. |
| N-3 → `ASSUMPTION-SAT-RISK-3` | Uyarıyı siparişi okuma izni olan herkes görür; uyarıda tutar yok, yalnız "limit aşıldı / limit tanımlı değil / risk okunamadı". Yeni izin adı yok, grant yok. | Tutar gösterilmediği için ayrı görünürlük izni gerekmez; rol/grant insan kapısı korunur. |
| N-4 → `ASSUMPTION-SAT-RISK-4` | Siparişte `Unavailable` (limit tanımsız veya kaynak yok) da uyarı kaydı olarak yazılır, sebep kategorisi ayrı; sipariş durmaz. | FNS-D30 "limitsiz/sıfır sayılmaz": sessiz geçmek limit varmış gibi davranmaktır. Kayıt yalnız olgu; para etkisi yok. |
| N-7 → kapandı | Net kg'ı Lojistik okuma portuyla sunar. | Sinan'ın cevabı değil, mimari değişmezden: bir modül başka modülün tablosuna yazmaz/okumaz (AGENTS.md); tartı Lojistik'te, fatura Satış'ta (SAT-D309) olduğundan olgu port ile okunur. Port anahtarı L-8. |
| N-9 → `ASSUMPTION-SAT-RISK-5` (daraltıldı) | Yalnız iki şey: satır tutarına kilitli SAT-D11 kuralı uygulanır; sipariş kg'ı ile fark için tolerans uyarısı/bloğu **yok** (`OPEN-004` etkinleşmez). Net kg ve kg payının hassasiyeti ile paylaştırma artığı burada seçilmez; F2 açık noktasıdır (F2 spec önerir, ChatGPT formül golden'ı sabitler, gerekirse `NEEDS-SINAN`). | Biri kilitli karar, diğeri açık kararı etkinleştirmeyen muhafazakâr varsayılandır; para/formül politikası tahminle seçilmez (AGENTS). |
| N-10 → `ASSUMPTION-SAT-RISK-6` | Sevkiyatçı ekranında üç ayrı metin (L-6), Lojistik resource dosyasında. | Metin UX'tir, kod değiştirmeden değişir; tutar göstermez (SAT-D162 korunur). |

### 4b. Sinan'a giden politika soruları (`NEEDS-SINAN`, #66) — cevaplandı

N-5/N-6/N-8 #66 [6821105608](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821105608) m.2–3 ve
[6821262098](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821262098) m.1–2 ile cevaplandı; karar metni
`SINAN_KARARLARI_2026-11-12_SATIS_EK60.md`: N-5 → **SAT-D310** (öneri "hayır" seçilmedi; istisnayla geçer), N-6 →
**SAT-D311** (ayrı risk satırı), N-8 → **SAT-D312** (oranla böl, tartı zorunlu, gerekçeli düzeltme + fark faturası).
Aşağıdaki tablo sorulan hâli kayıt için tutar.

| Kimlik | Soru (önerim ilk sırada) | Neden tahmin edilemez |
|---|---|---|
| N-5 | Risk okunamazsa (Finans/kur/fatura kaynağı yok) sevk tek sevk istisnasıyla geçebilsin mi? Öneri: **hayır, yalnız `Over` ve "limit tanımsız" istisnayla geçer; okunamayan risk durur.** Bugün istisna talebi `Unavailable`'da kabul ediliyor (`SalesRiskExceptionService.cs:97-99`). | DEC-FIN-009 fail-closed vs. istisna kapsamı; risk politikası |
| N-6 | Finans kabulü bekleyen kesilmiş fatura riskte nerede sayılsın? Öneri: **Finans kabul edene kadar "faturalanmamış sevk" bileşeninde kalsın** (iki bileşen arasında boşluk/çift sayım olmasın). | Risk formülü (`SATIS_MODUL_BAGLANTILARI_v1.md:45`) |
| N-8 | Bir teslim tartısı birden çok fason satırını/siparişini kapsarsa net kg nasıl bölüşülür? Öneri: **bir talimatta tek fason satırı olsun; çok satır gerekirse sevk payı kg oranında bölünsün.** Kısmi teslimde her teslim ayrı fatura; tartım düzeltmesi faturalanmışsa iade/ek faturayla. | Para/stok politikası |
| N-11 (Finans hizalama) | Koordinatör #1117 metnini (`credit_limit_not_defined` → `finance_credit_limit_not_set`) Finans'a iletti. | Sözleşme tutarlılığı |

## 5. Sınırlar
- Bu plan `OPEN-*` kapatmaz, rol/izin/grant seçmez, eşik/tolerans/yuvarlama koymaz. Satış limit hesaplamaz ve tutmaz (SAT-D51,
  FNS-D27 (5)). Lojistik kapı kuralı Lojistik şeridinindir (SAT-D285 (4)); burada yalnız port sözleşmesi önerilir.
- `ISalesShipmentRiskGateReadPortV1` adı ve şekli önerdir; Lojistik L-1…L-7'de kabul etti (§3); S2 PR'ı Satış kod kuyruğunda (#1103 sonrası) açılır.
