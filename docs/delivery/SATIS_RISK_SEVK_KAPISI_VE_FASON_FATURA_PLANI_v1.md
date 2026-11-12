# Satış — Kredi limiti uyarısı, sevk anı risk portu ve fason hizmet faturası: dilim planı v1

Tarih: 2026-11-12 · Yazan: Satış şeridi (Claude) · Taban: `origin/main` @ `149f516ae` · Durum: **ÖNERİ** (plan; iş
kararı değildir, karar sicilini değiştirmez).

**Kaynaklar (öncelik sırası AGENTS.md):** `KARAR_SICILI.md` DEC-FIN-007 (satır 83), DEC-FIN-009 (satır 85); `ACIK_KARARLAR.md`
OPEN-003/004/010; EK21 SAT-D153…D162; EK55 SAT-D281; EK56 SAT-D283…D287; EK7 SAT-D46…D53, SAT-D80; EK25 SAT-D175/D176; EK29
SAT-D195; EK31 SAT-D201; URT-D16; Finans devri **PR #1117** (main'de, `1a994cbe`): `SINAN_KARARLARI_2026-11-12_FINANS_KREDI_LIMITI.md`
FNS-D27/D27a/D30/D31 ve `docs/delivery/FINANS_F018_KREDI_LIMITI_PLANI_v1.md` §2 L1, §3. Fason fatura kuralı: Issue #66 yorumu
`6820601088` (11. tur) madde 4; karar dosyası bu PR'da: `SINAN_KARARLARI_2026-11-12_SATIS_EK60.md` SAT-D309…D315 (F0).
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
    `RiskCheck.cs:340` `ApprovedRemainderAsync`, kesin sevk okuması `:412-430`) ile
    faturalanmamış sevk (`ISalesInvoiceableShipmentReadPortV1` onaylı irsaliye payı) aynı Lojistik olgusuna dayanmalıdır.
    Test: kesin sevk var, irsaliye onaysız → tutar tam olarak bir bileşende.
  - (d) Pay anahtarı taşımayan faturalar (review B P2-1, A P3-1):
    - **F2 fason hizmet faturası:** "faturalandı" miktar karşılaştırmasıyla **ölçülmez**. Sevk payındaki miktar beyan/mal
      miktarıdır (`ISalesDispatchReadPortsV1.cs:97-101`), fatura miktarı tartı net kg payıdır (SAT-D309/D312); iki taban
      farklıdır. Fason faturası kendi anahtarına (F2 "ayrı anahtar": talimat + tartım + fason sipariş) bağlıdır ve kesilince
      o `(fason orderId, loadingInstructionId)` altındaki sevk paylarını **tamamen** kapatır; net ile beyan farkı artık
      bırakmaz, negatif fark doğurmaz. Faturalanmamış fason sevkin tutarı da aynı tabanda, net kg payı × onaylı kg fiyatı
      olarak `SalesInvoiceCalculator`'dan gelir. Çok siparişli talimatta pay hesaplanamıyorsa (paylaştırma artığı kuralı F2
      açık noktası) bileşen `Unavailable` olur, sıfır sayılmaz.
    - **SAT-D94 (2) önce-fatura/depo teslim:** "faturalandı" **sipariş satırı başına**, faturalanan miktar sevk edilen
      miktarla karşılaştırılarak ölçülür (`ISalesDispatchReadPortsV1.cs:102-106`: "invoiced … measured by the invoice owner
      per order line"). Kesilmiş ama sevk edilmemiş kısım, satır başına `max(0, faturalanan − sevk edilen)`, sevk planı
      bileşeninden (c) düşülür; çünkü aynı tutar SAT-D311 satırında veya `OpenReceivables`'tadır. Düşüm en çok plan
      kadardır; bileşen sıfırın altına inmez (aşağıdaki SAT-D313 taban formülü).
    - LOJ-D14 fason teslimi kapıya aldığı için bu sınır kapı sonucunu doğrudan etkiler. Testler: "F2 fason faturası kesilince
      fason payı tam olarak bir bileşende", "fason net < Σ beyan ve net > Σ beyan: faturadan sonra faturalanmamış fason
      sevk 0", "çok siparişli talimatta pay yoksa bileşen `Unavailable`", "önce-fatura: kesilmiş, sevk edilmemiş tutar tam
      olarak bir bileşende".
- **SAT-D313 (Sinan, #66 `6822385722` + `6822674443`; koordinatör sorusunda `6822767886` m.1: "Sevk plan miktarı x onaylı
  fiyat"):** risk onaylı siparişin tamamından sayılmaz; **sevk planına açılan miktar × onaylı birim fiyat** ile başlar (satış ve fason aynı; fason tartı öncesi de böyle), irsaliye/fatura kesilene kadar.
  Tutar durumuna göre görünür: sevk planına alınan → sevk olan (faturalanmamış sevk) → faturası kesilen (SAT-D311 satırı,
  Finans işleyince açık bakiye). Plana alınmamış sipariş kalanı riske girmez (`6822674443`; koordinatör notu `6822810785`: plansız fason boşluğu bu
  cevapla kapandı). Bu, bugünkü "onaylı kalan
  sipariş" bileşenini (`SalesDraftService.RiskCheck.cs:340` `ApprovedRemainderAsync`, DEC-FIN-007 ilk aşaması) değiştirir; S0 K3 PR'ı uygular, Finans'a
  bildirilir; DEC-FIN-007 sicil daraltması ayrı, incelenmiş bir sicil PR'ındadır (`6822810785`).
  - *Satır başına taban formülü (yazar önerisi, geri alınabilir; S0 spec ve ChatGPT formül golden'ı sabitler, para
    hesabı yalnız backend hesaplayıcısında, SAT-D11/D46/D90):* planlanmış sevk edilmemiş = max(0, plan − sevk edilen);
    faturalanmış sevk edilmemiş = max(0, faturalanan − sevk edilen); plan bileşeni miktarı = max(0, planlanmış sevk
    edilmemiş − faturalanmış sevk edilmemiş). Golden: plan 100, sevk 40 → 60; plan 100, sevk 40, fatura 70 → 30; plan 30,
    sevk 0, fatura 100 → 0 (düşüm planı aşmaz); plan 50, sevk 60 → 0. *Açık:* plan miktarını aşan sevk (fazlası
    faturalanmamış sevkte sayılır mı, plan ayrıca güncellenir mi) ve fiyat revizyonunda "onaylı fiyat" esası (eski mi
    yeni onaylı fiyat mı); para etkisi olduğu için gerekirse `NEEDS-SINAN`. Test: plan → sevk → fatura geçişinin her adımında tutar tam olarak bir durumda; plana alınmamış kalan
  riskte yok. *Açık (S0 spec önerir):* sevk planı miktarının kaynağı (Lojistik okuma portu, mevcut/yeni; #1115 `26ad52f7` spec
  04:183 `ILogisticsShipmentPlanQuantityReadPortV1` / `logistics.shipment-plan-quantity.v1` önerir; spec main'de, port
  kodda yok), fasonda plan
  kg'ı, plan iptali/azaltmasında düşme, durum adları; para etkisi varsa `NEEDS-SINAN`. Plansız fason sipariş plana alınana kadar riske
  girmez (`6822810785`).
- **SAT-D175 ve ara davranış (review B P2-2, A P3-3):** SAT-D175 fason siparişin risk kontrolünü normal siparişle aynı
  tutar; SAT-D313 de satış ve fason için aynı kuralı koyar, çelişki yoktur. Bugün main'deki `ListOpenApprovedOrdersAsync`
  (`SI/Persistence/SalesStore.cs:1945`) fason siparişi ayırmaz; "onaylı kalan sipariş" onun onaylı miktar × fiyatını sayar.
  S0 K3 PR'ı merge olana kadar bu statüko değişmez (zararsız: "faturalanmamış sevk" `NoSource` olduğu için toplam risk hiç
  `Within` dönmez). S0 bu bileşeni sevk planı bileşeniyle değiştirir. Sevk planı kaynağı okunamazsa bileşen `Unavailable`
  olur, sıfır veya tahmin sayılmaz; risk `Within` dönmez ve kapı yalnız `Valid` istisnayla geçer (DEC-FIN-009, SAT-D310).
  Test: "sevk planı kaynağı yok → risk `Unavailable`, fason dahil asla `Within`".
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
  (None/Pending/Lapsed/Rejected/Withdrawn) → durur. Port hatası/zaman aşımı → durur (503, terminal değil; istisna da okunamaz; DEC-FIN-009,
  Lojistik L-7). Lojistik'e kapı kuralı değişikliği bildirilir (L-2 metni `SourceUnavailable`'ı durduruyordu).
- **Hesap:** `ISalesOrderRiskOutcomeSource` ile aynı deterministik değerlendirme (SAT-D284 ile tek kaynak); izin ölçümü
  port konvansiyonuna göre tüketicidedir (`ISalesPartyRiskReadPortV1.cs:9`). Lojistik L-3: kendi mevcut sevk kaydı iznini
  ölçer, yeni izin gerekmez; Satış okuması kullanıcı izni değil şirket bağlamlı sistem okumasıdır.
- **Okuma anı (L-1):** kapı yalnız kesin sevk kaydında (stok çıkışının geri dönmediği an) okur, her yürütmede (yeni anahtar veya 503
  sonrası aynı anahtar) yeniden okur, terminal 409'un replay'i okumaz; sonuç cache'lenmez; talimat/yükleme/tartım/irsaliye onayındaki okuma kapı sayılmaz. **Yeniden deneme ayrımı (k3-command-skeleton):** 409 iş/istisna reddi terminaldir: kaydedilir, aynı anahtar aynı sonucu tekrar verir, kapıyı yeniden okumak yeni anahtar ister; 503, port hatası veya zaman aşımı terminal değildir: hiçbir şey kaydedilmez, aynı anahtar komutu yeniden yürütür.
- **Anahtar (L-4):** `(orderId, loadingInstructionId)`; bir talimat birden çok siparişe bağlanabilir (tek müşteri), Lojistik
  her sipariş için ayrı sorar; satış sevkinde duran sipariş yalnız kendi satırlarının sevkini durdurur (fason teslim istisnası aşağıda, Tüketiciler).
- **Tüketim/iptal (L-5, SAT-D285):** Lojistik kesin sevk commit olunca outbox olayı yayınlar (talimat, sipariş, sevk kimliği),
  sevk iptalinde ters olay; Satış istisnayı kendi kaydında tüketildi/düştü yapar (inbox/dedup). Satış Lojistik durumunu port
  ile sorgulamaz. Olay sözleşmesini Satış önerir, yayıncı Lojistik'tir; olay adı/sürümü S2 sonrası ayrı dilimde (S2b) yazılır.
- **Tek kullanım (review B P2-3):** v1 istisna cevabında `Consumed` yoktur ve L-5 asenkrondur; bu yüzden tek kullanım
  **Lojistik'in kesin sevk transaction'ında** eşzamanlı zorlanır (`ExceptionNo` başına tek tüketim, tekil kayıt). Satış'taki
  tüketim kaydı yalnız aynadır, kapı değildir. Satış gömülü cevaba `Consumed`/`Lapsed` eklerse bu v1 sözleşme değişikliği
  olarak listelenir (yeni sürüm).
- **Tüketiciler (LOJ-D14, #66 `6821949043`):** kapının iki tüketicisi vardır: Lojistik kesin sevk ve fason teslim
  (F-012). İkisi aynı port, aynı sonuç eşlemesi ve aynı anahtarı kullanır; fason teslim anahtarı `(fason orderId,
  loadingInstructionId)`. Tek kullanım iki yolu birlikte kapsar: bir `ExceptionNo` sevk veya fason teslimde toplam bir kez
  tüketilir; `ExceptionNo` sipariş içinde tekil olduğundan tekil anahtar `(CompanyId, orderId, ExceptionNo)`'dur.
  **Fark (L-4'ten sapma):** #1115 `26ad52f7` spec 04:138-151'ye göre fason teslim kesin sevki talimat düzeyinde
  **hep-ya-hiç**tir: talimattaki herhangi bir fason siparişin kapısı durursa talimatın hiçbir satırı sevk edilmez; diğer
  siparişlerin `Valid` istisnası tüketilmez, aynı transaction'da yazılmış tüketim kayıtları geri alınır ve L-5 aynasına olay
  gitmez; terminal ret kaydı ayrı yazılır. Bu Lojistik yazar okumasıdır (LOJ-D12 "tartı talimat başına tek"; Sinan kararı
  değil); kısmi durdurma (L-4) yalnız satış sevkinde geçerlidir. Fasonda kısmi sevk istenirse yeni model/karar gerekir;
  o durumda Issue #66'ya `NEEDS-SINAN` yazılır (bugün açık soru yok). #1115 04:143-148 metni L-7'den sapar; #1122 ile hizalanıyor (koordinatör notu). Bu plan OPEN kapatmaz (yalnız referans; #1115 main'de
  merge `26ad52f7`).
- **Hata (L-7):** port hatası veya zaman aşımı → sevk kaydı yazılmaz (503 benzeri ret), ret kaydedilmez; yeniden deneme kullanıcının aynı
  idempotency anahtarıyla komutu yeniden yürütür; otomatik tekrar yok. Kapının 409 reddi ise terminaldir: kaydedilir, aynı
  anahtar aynı reddi döner, yeniden okuma yeni anahtar ister (Okuma anı). Karışık durumda (koordinatör kararı r2) bütün siparişler
  değerlendirilir (ilk 409'da kısa devre yok); bir 503 varsa 503 kazanır, hiçbir şey kaydedilmez. Kapsam: fason teslim zaten hep-ya-hiçtir; satış sevkinde tek 503 bütün talimatın sevk kaydını durdurur, L-4 bağımsızlığı yalnız 409'lar içindir. Testler: 409 sonrası aynı anahtar aynı ret;
  503 sonrası aynı anahtar yeniden yürür, kayıt yok; karışık 409 + 503 → 503, kayıt yok. `Unavailable` **sonucu** bir hata değildir: yalnız `Valid` istisna yoksa
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
  Aynı dosyaya #66 16.–17. tur cevaplarıyla **SAT-D310** (N-5), **SAT-D311** (N-6), **SAT-D312** (N-8) eklendi; #66 `6822385722`/`6822674443` ile **SAT-D313** (sevk planı esaslı risk tutarı; `6822767886` m.1 aynı yön), `6822767886` m.2 ile **SAT-D314** (iptal/azaltmada gelecek tedarik ayırması otomatik bırakılır), `6823243819` (23. tur) ile **SAT-D315** (bırakma sırası); `6823547103` (25. tur) SAT-D314/SAT-D315'i fiziksel fazlanın otomatik bırakılması ve ara dönem reddiyle günceller.
- **Modül bağlantıları:** yok.

### F1 — Teslim tartımı net kg portu (K3: modüller arası sözleşme; sahibi Lojistik — SAT-D309/LOJ-D9)
- **Öneri:** Satış'ın beyan ettiği, sahibin cevapladığı `ISalesSubcontractDeliveryWeighingReadPortV1`
  (`sales.subcontract-delivery-weighing.v1`): girdi (şirket, talimat, tartım kimliği; L-8 anahtarı
  `(LoadingInstructionId, WeighingId)`, şirket bağlamıyla; #1115 `4f96492a` aynı anahtarı `(CompanyId, LoadingInstructionId,
  VehicleWeighingId)` diye adlandırıyordu; #1115 `26ad52f7` spec 04:102-106 anahtarı seçmez; kesin ad F1 spec'inde), cevap: talimatın toplam net kg'ı tam ondalık
  metin, sevk satırı başına beyan kg, düzeltme `Version`, ölçüm türü, durum (`Available/NoSource/Unavailable`; asla sıfır).
  Mevcut AT-053 tartımı talimat başınadır (`LR:52,56`); siparişe dağıtımı **SAT-D312** (N-8; Lojistik LOJ-D12/D13): sevk
  satırlarındaki beyan kg oranında; tartısız fason teslim yok. **Lojistik bölme yapmaz;** paylaştırma, artık ve para yalnız
  `SalesInvoiceCalculator`'dadır (AGENTS tek hesaplayıcı; #1115 spec 04 `26ad52f7` ile hizalı).
- **Fason teslim kapısı (LOJ-D14):** fason teslim S2 portu ve kuralıyla aynen geçer, anahtar `(fason orderId,
  loadingInstructionId)`. Kapı kesin teslim transaction'ında okunur (L-1); net kg okuması ve tartı sırası kontrolleri kapıdan
  önce yapılır (#1115 `26ad52f7` spec 04:134-136). Fason teslim talimat düzeyinde hep-ya-hiçtir (S2 Tüketiciler). İki okuma aynı transaction'da fail-closed'dır; kesin sıra F1 spec'inde
  Lojistik metniyle tek ifadeye bağlanır.
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

### X1 — SAT-D314/SAT-D315: satır iptal/azaltmada gelecek tedarik ayırmasının bırakılması (K3: cross-module; #1116 F017-s ile)
- **Ne:** gelecek tedarikten ayrılmış satış satırı iptal edilir veya azaltılırsa Satış komutu aynı işlemde Stok bırakma
  portunu çağırır; artan gelecek ayırma kalmaz; audit'li. Satış, ayırma var diye "önce elle bırak" şartıyla reddetmez
  (seçilmeyen yön); ayırma yüzünden reddetmez, fail-closed, SAT-D104/D112 tabanı ve port hatası ayrı ret sebepleridir. Stok metni KLN-D28 (#1116), Stok modeli KLN-D26 (main `e165aaee`); X1 yalnız Satış tarafıdır.
- **Ölçü (yazar okuması, #1116 merge `1af6ffea` §3.2 adım 5 ile hizalı):** yeni açık miktar = yeni satır miktarı − sevk
  edilen/yüklenen toplam (SAT-D104/D112 tabanı); bırakılacak fazla = max(0, aktif fiziksel tahsis + aktif gelecek ayırma −
  yeni açık miktar) (koordinatörün 25. tur düzeltmesi, `6823547103`; SAT-D314 (1)). Golden: 100/40 sevk/60 gelecek → 80 ⇒
  20; fiziksel 10 + gelecek 10, 20 → 15 ⇒ gelecekten 5; 20 → 5 ⇒ gelecekten 10 + fiziksel 5 (ara dönemde ret). İptal bütün
  aktif gelecek ayırmayı bırakır. Satır kaldırma (koordinatör kararı r2; SAT-D167 metnini ve uygulama ayrıntısını değiştirmez): (a) satırda
  sevk/irsaliye, yükleme talimatı veya aktif fiziksel tahsis varsa → port çağrılmadan kalıcı Satış 422
  `sales_line_revision_line_shipped` (SAT-D167, EK24:52; README :792); (b) SAT-D167 anlamında sevk edilmemiş, yalnız gelecek
  ayırmalı satır kaldırılır: Stok portuna yeni miktar 0 ile azaltma, bütün gelecek ayırma bırakılır (#1116 §5.1 (c)).
  Normal yol (a)'dır; KLN-D32 (3) ara 409'u fiziksel bırakma gerektiren azaltmaya aittir. *Yarış penceresi (koordinatör kararı
  r3):* Satış (a) okumasını Stok kilitleri dışında yapar, fiziksel tahsis oluşturma satış satırı kilidini almaz (#1116 §3.2);
  okuma ile port çağrısı arasında fiziksel tahsis oluşursa Stok kilit altında görür ve 0'a azaltma fiziksel bırakma
  gerektirdiğinden ara 409 `inventory_future_allocation_physical_release_unavailable` döner (KLN-D32 (3)); bu 409 terminaldir
  ((c) aşağıda). *Hizalama notu (koordinatör kararı r3):* main'deki #1116 metni (spec §5.1 (c), §8 "fiziksel tahsisli → 409";
  KLN-D28 satır kaldırmada yalnız "sevkli → 422") bu ayrımdan sapar; Stok tarafı #1126 (açık) ile hizalanır. Hizalanana kadar
  Satış ön-kapısı (a) geçerlidir.
- **Sıra (SAT-D315, Sinan 23. tur):** birden çok alışa yayılan ayırmada en geç gelecek alıştan başlanır; fiziksel tahsis ve
  gelecek ayırma birlikteyse önce gelecek ayırma bırakılır; kullanıcının komuttaki açık seçimi varsayılandan üstündür.
  Geçici "seçim yoksa 409" yönü kalkmıştır. Stok metni KLN-D30/KLN-D31/KLN-D32 (#1116 merge `1af6ffea`); sıra Stok portunda hesaplanır,
  Satış yalnız açık seçimi iletir. "En geç gelen" tarihi Alış `GetLineScheduleAsync`'ten okunur (Stok yazar okuması; #1110 F17-1b, #1101;
  KLN-D30 (2), (4)): teslim alınmamış en geç `DeliveryDate`; dilimsiz satır tarihsiz (en geç); `Unreadable`/hata → komut
  yazılmaz, 503 fail-closed. F017-s termin okuması (#1110/#1101) main'e gelmeden sürülmez. Gelecek ayırma yetmezse fazla fiziksel tahsisten otomatik bırakılır (Sinan 25. tur,
  SAT-D315 (5)); bırakma yolu Satış/Stok fiziksel tahsis kuralıdır (KLN-D31 (4), #1116 §5.1 (b)). **Ara dönem:** Sinan:
  "Fiziksel bırakma ekranı hazır olana kadar böyle bir azaltma reddedilir". *Koordinatör notu:* Sinan'ın "ekran" sözü aynen korunur; KLN-D32 (3) bunu "fiziksel bırakma yolu … main'e gelene kadar" diye okur. *Koordinatör/yazar fail-closed türetmesi (KLN-D32 (3)):* ret tamdır (açık fiziksel
  seçim dahil), hiçbir şey yazılmaz, gelecek ayırma da bırakılmaz. Reddi Stok portu #1116 §3.2 adım 0 kilitleri altında
  tespit eder; problem kodu (#1116 KLN-D32 (3), spec §7/§8; öneri kodu `inventory_future_allocation_physical_release_unavailable`, F017-s'de kesinleşir).
- **Satış komut iskeleti (`k3-command-skeleton`; (a)/(b) yazar önerisi, (c) koordinatör kararı r2/r3):** (a) Satış Stok'a yeni açık miktarı, sevk tabanını ve
  açık seçimi iletir; bırakılacak miktarı ve sırayı Stok, #1116 §3.2 adım 0'daki alış satırı kilitleri altında hesaplar
  (RCSI anlık görüntüsünden değil); Satış miktar/sıra hesaplamaz. (b) Expected version + `Idempotency-Key`; replay ikinci
  Stok çağrısı yapmaz. (c) **Terminal, kayıtlı:** 409 (KLN-D32 (3) ara red, yarış penceresi dahil; KLN-D30 (3) bayat seçim
  `inventory_allocation_version_conflict`) ve 422 (KLN-D30 (3) fazlayı aşan seçim, öneri
  `inventory_future_allocation_release_selection_invalid`; kayıt/replay sınıfı *yazar okuması*: karar (c) gibi terminal ve
  kayıtlı). Bunlar Satış revizyon/iptal komutunun kendi terminal ret kaydıdır; Stok portu bunlar için hiçbir şey yazmaz,
  tüm komut geri alınır; aynı anahtar Stok'u çağırmadan aynı reddi
  döner, yeniden okuma (ör. fiziksel bırakma yolu geldikten sonra) yeni anahtar ister. **Terminal değil, kaydedilmez:** 503 (bırakma portu
  hatası/zaman aşımı, termin okuması `Unreadable`, Satış `revision_impact_unavailable`) ve satış satırı kilidi meşgul 409
  (#1116 spec §5.1 (c); kodu F017-s'te): aynı anahtar komutu yeniden yürütür. Aynı yürütmede terminal ret (409/422) ile
  503 birlikte doğarsa 503 geçer, hiçbir şey kaydedilmez. Stok'un problem kodu Satış
  cevabına **aynen taşınır** (#1116 spec §7 "Satış problemine taşınır"; çevrilmez); kesin ad F017-s'te.
- **Satış komutlarına eşleme:**
  - DEC-SAL-005 sipariş iptali (README :296-302): önizleme fiziksel tahsiste reddetmeye devam eder; gelecek ayırma
    onaylı iptalde bırakılır (KLN-D28 (9)). README :302 yarış beyanı değişir.
  - SAT-D104/D112 miktar revizyonu ve satır kaldırma (README :378, :400): "Satış hiçbir şey bırakmaz" cümlesi gelecek
    ayırma için değişir; taban reddi sürer. Bırakmanın gönderimde mi onayda mı yapılacağı ve SAT-D115 müşteri reddiyle
    geri dönüş açık noktadır (F017-s spec).
  - SAT-D17 satır kapatma ve zorla kapatma (README :850): SAT-D314 kapsamında değil; bugünkü kural sürer.
  - README metin değişiklikleri F017-s spec görevidir.
- **Bağımlılık:** Stok bırakma portu (#1116 F017-b; ad `IInventoryFutureAllocationReleasePortV1` #1116/#1110 önerisi,
  main'de yok), Satış tahsis okumasında gelecek ayırma türü ve alış beklenen geliş tarihi (#1116 F017-s), bırakma portunda
  komut seçimi alanı (SAT-D315 (3)). Port main'de yokken Satış
  komutuna bağlanmaz; dilim F017-s içinde ya da hemen ardından Satış şeridinde açılır. Satış ile Stok arasındaki ilk
  aynı-işlem yazmadır (host participant; emsal `InventoryManufacturingRealizationParticipant.StagePlanRemainderReleaseAsync`).
- **Fail-closed:** bırakma portu hata verir veya okunamazsa iptal/azaltma yazılmaz (aynı işlem); idempotent replay ikinci
  bırakma üretmez.
- **Testler:** iptalde ayırma `Released` (önceden tüketim varsa `Consumed`, #1116 §4.1); azaltmada yalnız açık miktarı aşan kısım bırakılır; kısmi sevkli satırda
  azaltma (100/40 sevk/60 ayırma → 80: 20 bırakılır); aşan kısım birden çok ayırmaya yayılıyor, seçim yok → en geç
  gelen alışın ayırması önce bırakılır (30 t → 20 t; 15 Aralık 10 t / 5 Ocak 20 t → 5 Ocak'tan 10 t); seçim varsa seçim
  uygulanır; fiziksel tahsis + gelecek ayırma birlikte → önce gelecek ayırma bırakılır (fiziksel 10 + gelecek 10, 20 → 15: gelecekten
  5, fiziksel 10 kalır); fiziksel bırakma gereken azaltma (20 → 5 veya açık fiziksel seçim) ara dönemde tamamen reddedilir,
  hiçbir şey yazılmaz; satır kaldırma: sevkli, yükleme talimatlı veya fiziksel tahsisli satır 422 `sales_line_revision_line_shipped` (Stok portu çağrılmaz), yalnız gelecek ayırmalı satır kaldırılır, bütün ayırmanın kalanı 0 (durum #1116 §4.1'e göre `Released`/`Consumed`);
  yarış: Satış okumasından sonra fiziksel tahsis oluşursa satır kaldırma ara 409, terminal kayıt, hiçbir şey yazılmaz; açık
  seçim fazlayı aşarsa 422, terminal kayıt; bayat seçim 409 (KLN-D30 (3)); termin okuması `Unreadable` → Satış komutu yazılmaz, 503; golden: dilimsiz alış satırı en geç sayılır; port hatasında satır değişmez; Stok reddi tüm komutu
  geri alır, denetimde yalnız Satış'ın terminal ret kaydı; ara 409 sonrası aynı anahtar → aynı 409, Stok çağrısı yok; 503
  sonrası aynı anahtar → komut yeniden yürür, kayıt yok; aynı yürütmede 409 + 503 veya 422 + 503 → 503, kayıt yok; satış satırı kilidi meşgul 409 → kayıt yok, aynı anahtar
  yeniden yürür; replay tek bırakma; şirket filtresi; gerçek SQL (iki modül tek işlem) ve
  gerçek-SQL yarış testleri "Satış azaltma/iptal × alış kabul dönüşümü" ve "Satış iptali/azaltması × oluşturma"
  (#1116 spec §8).
- **Modül bağlantıları:** Stok bırakma portu (Stok → Satış tüketimi, ❌ #1116), gelecek ayırma okuma türü (❌ #1116 F017-s).

---

## 3. Lojistik cevapları (2026-11-12, Lojistik şeridi önerisi, `ASSUMPTION`, geri alınabilir teknik)

Lojistik port önerisini kabul etti (Satış sahipli, tutar yok, Result + HoldReason + gömülü istisna). Para/limit politikası
seçilmedi; kurallar FNS-D27 (4)/FNS-D30/SAT-D154/D160'tır.

| # | Soru | Cevap |
|---|---|---|
| L-1 | Okuma anı | Yalnız kesin sevk kaydında; her yürütmede (yeni anahtar veya 503 sonrası aynı anahtar) yeniden okunur, terminal 409'un replay'i okumaz; cache yok. Önceki aşamalarda okuma kapı değildir. 409 ret terminaldir: kaydedilir, aynı anahtar aynı reddi döner. |
| L-2 | İstisna gömülü mü | Gömülü; tek çağrı. `Within` geç; `Over`/`CreditLimitNotSet` yalnız `Valid` ile; diğer her durum dur. `SourceUnavailable` Lojistik cevabında duruyordu; **SAT-D310** ile `Valid` istisnayla geçer (Lojistik'e bildirilir). |
| L-3 | İzin | Yeni Lojistik izni yok; mevcut sevk kaydı izni. Satış okuması sistem okuması. Yeni ad gerekirse yalnız ad kodlanır, grant insan kapısı. |
| L-4 | Anahtar | `(loadingInstructionId, orderId)`; sipariş başına ayrı sorgu; duran sipariş diğerlerini etkilemez. Talimatta tek müşteri. **Fason istisnası:** fason teslim talimat düzeyinde hep-ya-hiçtir (S2 Tüketiciler; #1115 `26ad52f7` spec 04:138-151). #1115 04:143-148 metni L-7'den sapar; #1122 ile hizalanıyor (koordinatör notu). |
| L-5 | Tüketim/iptal | Satış kendi istisna kaydında tutar; Lojistik sevk commit'inde outbox olayı, iptalde ters olay yayınlar (S2b). |
| L-6 | Ekran metni | Lojistik'te, resource dosyasında üç ayrı metin (aşıldı / limit tanımlı değil / risk okunamadı). Metin değişikliği kod değiştirmez (N-10). |
| L-7 | Port hatası | Fail-closed, sevk kaydı yazılmaz, ret kaydedilmez (terminal değil); kullanıcı aynı idempotency anahtarıyla yeniden dener ve komut yeniden yürür, otomatik tekrar yok. 409 iş/istisna reddi bundan ayrıdır ve terminaldir (L-1). Karışık durum (koordinatör kararı r2): talimattaki bütün siparişler değerlendirilir (ilk 409'da kısa devre yok); bir 503 varsa 503 kazanır, hiçbir şey kaydedilmez. Kapsam: fason teslim zaten hep-ya-hiçtir; satış sevkinde tek 503 bütün talimatın sevk kaydını durdurur, L-4 bağımsızlığı yalnız 409'lar içindir. Lojistik cevabı `Unavailable` sonucunu da durduruyordu; **SAT-D310** ile `Unavailable` sonucu yalnız `Valid` istisna yoksa durur, port hatası her zaman durur (Lojistik'e bildirilir). |
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
