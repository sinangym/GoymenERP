# F-017 — Gelecek tedarikten satış siparişine ayırma: spec ve dilim planı v1

Tarih: 2026-11-12 · Hazırlayan: Claude (cloud oturumu, Alış şeridi) · Durum: **ÖNERİ** (plan; iş kararı değildir)
Karar: `docs/decisions/SINAN_KARARLARI_2026-11-12_ALIS_EK17.md` ALS-D82 (#66 6819147253 madde 2), ALS-D83/ALS-D84 (#66 6819640816
madde 3–4), ALS-D85..ALS-D90 (#66 6820601088 madde 1, 3, 6–9), ALS-D91 (#66 6821262098 madde 7);
ALS-D10/D12 (API-11 satır kapanışı); master INV-006; Stok KLN-D26 (#1108); ALS-D30 (EK3); ALS-D22/D25;
SAT-D298 (birim çevrim kaynağı ürün kartı katsayısı).
Bağlam: `docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md` MOD-14, MOD-16, A3-0; Fable analizi F-017
(`docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md`). Stok karşı belgesi: PR #1116
`docs/delivery/STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md`.

Bu belge yalnız plan ve sözleşme taslağıdır. Kod yoktur. Stok tarafındaki model Stok şeridinin işidir (#1116). Buradaki Stok
maddeleri **öneri**dir; Stok şeridi kendi kapılarıyla değiştirebilir. Arayüz adları öneridir.

Revizyon 2 (2026-11-12): Stok şeridi PORT-REVIEW (#1110 review 5871928090) bulguları işlendi: `companyId` parametresi
kaldırıldı, birim kuralı SAT-D298'e bağlandı, sipariş sürümü yalnız gözlem oldu, modüller arası yarış için ortak satır kilidi
eklendi (§3.4), F17-1 K3 oldu, genel/teknik satır kuralı yazıldı, termin dilimleri ayrı okumaya alındı, kabul geri alma
maddesi NEEDS-SINAN'a taşındı, kabul replay çiti çevirmeyi kapsar.

Revizyon 3 (2026-11-12): Sinan S1/S2 cevapları işlendi (ALS-D83 bağ düşürme §3.5; ALS-D84 kabulde sıra §3.3). Satır/sipariş
iptali yeni NEEDS-SINAN S4 oldu.

Revizyon 4 (2026-11-12): Sinan 11.–13. tur cevapları işlendi: iptal akışı ALS-D85 (§3.5), kabul geri alma ALS-D86 ve
az/fazla gelme ALS-D87 (§3.3), ayıran kişi ALS-D88, kabulde bağlama hatası ALS-D89 (§3.3), süre yok ALS-D90. §5'te açık iş
kararı kalmadı.

Revizyon 5 (2026-11-12): #1110 DOCS review A 6820967459 ve B 6821005445 bulguları ve Sinan 18. tur m.7 (ALS-D91) işlendi:
ALS-D87 "satır kapanırsa" API-11 kapanışına bağlandı (§3.6), "bağları düşür ve iptal et" sözleşmesi görülen küme, host
orkestrasyonu, replay, izin ve neden kodu/metin ayrımıyla yazıldı (§3.5), kabul geri alma nedene göre ayrı bölüm ve dilim oldu
(§3.7, F17-6), "aktif ayırma" tanımlandı, iş/teknik hata ayrımı, sıra önceliği, uyarı neden kodları ve termin uyarısının sahibi
yazıldı.

Revizyon 6 (2026-11-12): #1110 DOCS review A 6821502877 ve B 6821523835 bulguları işlendi: ayırma oluşturma yolu kabul
çevirmesi ve iptal koruması gelmeden açılmaz, API-44/API-60 için sıra kuralı (§4); ayrılabilir formülü INV-006 ile hizalandı,
kapalı satırın ayrılabilir miktarı 0 (§2); §3.7'de "kapalı" tanımlandı ve Sinan metninin ötesindeki okumalar etiketlendi;
Cancelled/API-11 kapalı satır kesişimi fail-closed; API-11 izinleri, replay sırası, problem kodu adları, iade işaretinin yeri ve
API-44 ters + yeni çevirme sırası yazıldı.

## 1. Bugün (main `58b96aa2`)

| Parça | Durum | Kanıt |
|---|---|---|
| Onaylı satırın kalan taahhüdü (tek satır) | Var: `IPurchaseSupplyReferencePortV1.GetApprovedLineAsync` (committed − accepted, gelecek arz; Draft/Cancelled = NotApproved; şirket sunucu bağlamından) | `…Purchasing.Application/Contracts/IPurchaseSupplyReferencePortV1.cs`; `…Purchasing.Infrastructure/Persistence/PurchaseSupplyReferencePort.cs:22-30` |
| Onaylı açık satır listesi (operasyon listesi) | Var: `IPurchaseOpenLineReadPortV1` (şirket geneli, sayfalı; ürün süzgeci yok) | `IPurchaseOpenLineReadPortV1.cs` |
| Satış siparişi için alınan satırlar | Var: `IPurchaseSalesLinkedSupplyReadPortV1` (talep satırı kaynağı = satış siparişi; iptal önizlemesi için) | `IPurchaseSalesLinkedSupplyReadPortV1.cs` |
| Mal kabulünde Stok'a giden kayıt | Var: aynı host işleminde `InventoryGoodsReceiptRequestV1` (satınalma siparişi + satır kimliği, etiket, lot) | `…Inventory.Application/InventoryGoodsReceiptContractsV1.cs` |
| Fiziksel stoktan tahsis | Var: `DemandKind = SalesOrderLine` | `…Inventory.Infrastructure/Persistence/InventoryStore.Allocations.cs` |
| Modüller arası satır kilidi | **Yok** (kodda uygulama kilidi kullanımı yok) | §3.4 |
| Gelecek tedarikten ayırma | **Yok** | F-017 |
| İptal | Yalnız onaylı siparişin **tüm** kalanı (satır/miktar düzeyinde iptal yok); tek Alış store kaydı | `PurchaseOrder.cs:350-355`; `PurchasingService.OrderCancellation.cs:54` |
| Satır kapanışı (eksik teslim, yeniden açma) | **Yok** (plan API-11, ALS-D10/D12) | plan API-11 |
| Kabul düzeltmesi / tedarikçiye iade | **Yok** (plan API-44 ters + yeni, API-60 iade); `RecordAcceptance` yalnız ekler | `PurchaseOrder.cs:330-348`; plan API-44, API-60 |

## 2. Kavramlar

- **Ayrılabilir miktar (Alış'ın söylediği):** onaylı **genel** satırın kalanıdır ve master INV-006 ile aynıdır:
  `committed − kabulNet − cancelled − closed − iadeEtkisi`. Bugün kodda API-11 kapanışı ve iade yoktur; `closed` ve `iadeEtkisi`
  sıfırdır ve formül `committed − accepted − cancelled` olur. API-11 dilimi `closed`, API-44/API-60 dilimi `kabulNet` düşüşünü
  ve `iadeEtkisi`'ni porta ekler (alan ekleme, sözleşme sürüm etkisi; port contract testi o dilimde genişler). `closed` terimi
  yazar okumasıdır (plan API-11 "iptalden ayrı eksen"). Teknik dilimler genel satırın içindedir; iki kez sayılmaz (mevcut kural).
- **Ayrılamayan satır:** Draft ve Cancelled siparişin satırı ve API-11 ile kapalı satır ayrılamaz; ayrılabilir miktarı 0'dır.
  Kapalı satır yeniden açılınca (§3.6) kalanı yeniden ayrılabilir olur.
- **"Kapalı" sözcüğü:** kodda kapalı sipariş durumu yoktur (`PurchaseOrderStatus` Draft/Approved/Cancelled,
  `PurchaseOrder.cs:6-16`). Bu belgede (a) **tam kabul** = Approved + satır kalanı 0, (b) **API-11 kapalı** = satır API-11 ile
  kapatılmış, (c) **Cancelled** = sipariş iptal edilmiş demektir. Yeni durum eklenmez.
- **Aktif ayırma:** Stok'ta durumu `Active` olan gelecek ayırma. `Consumed` (kabulde fiziksel tahsise dönmüş) ve `Released`
  (bırakılmış) ayırmalar aktif değildir. Bu belgede "bağlı ayırma", "ayırması olan satır" hep aktif ayırma demektir.
- **Ayrılmış miktar (Stok'un tuttuğu):** bir satınalma genel satırına bağlı aktif ayırmaların toplamı.
- **Değişmez (ALS-D82):** bir satınalma satırı için ayrılmış ≤ ayrılabilir. Aynı miktar iki satış siparişine verilemez. Değişmezin
  sahibi Stok'tur. Alış ayrılmış miktarı hesaplamaz ve saklamaz. Değişmezin iki modül arasında korunması §3.4 kilidiyle olur.
- **Satış talebi bağlantısı (Alış'ın bildiği):** satırın talep satırı bir satış siparişinden geldiyse (`SourceKind = Sales`), satır
  o siparişin niyetini taşır. Bu bir **niyet**tir, ayırma değildir. Ayırma kaydı her zaman Stok'ta açıkça kurulur. Alış, satış
  kaynaklı bir satır onaylanınca kendiliğinden ayırma oluşturmaz (okuma, geri alınabilir).
- **Genel / teknik satır (yazarın önerisi, geri alınabilir; #1116 S9):** ayırma **genel satıra** yapılır, teknik dilime yapılmaz.
  Ayrılabilir miktar genel satır düzeyindedir. Kabul teknik dilimden gelirse Alış kabul payı o dilimin bağlı olduğu genel satır
  kimliğini bildirir (§3.3); Stok çevirmeyi genel satırdaki ayırmalar üzerinden yapar. Teknik dilimin kendi kimliği Stok'ta
  yalnız izlenebilirlik alanıdır. Koordinatör notuna göre S9 teknik konudur: bu kural şerit kararıdır, geri alınabilir.

## 3. Port sözleşmesi taslağı (Alış ↔ Stok ortak yazar)

### 3.1 Alış → Stok: `IPurchaseFutureSupplyReadPortV1` (Alış sahibi; A3-0'ın yerine geçer)

A3-0'daki "`IPurchaseSupplyReferencePortV1` v2" ihtiyacını karşılar. v1 port değişmez; yeni port ayrı sürümlü arayüzdür.

```csharp
public interface IPurchaseFutureSupplyReadPortV1
{
    // Stok'un ayırma kaydında, §3.4 kilidi alındıktan sonra aynı işlemde okuduğu tek genel satır.
    // lineId teknik dilim kimliği ise sonuç NotAllocatable (Status = "technical-line") döner; genel satır kimliği ParentLineId'dedir.
    Task<PurchaseFutureSupplyAnswerV1> GetAllocatableLineAsync(Guid orderId, Guid lineId, CancellationToken ct = default);

    // Ayırma ekranının seçenek listesi: bir ürün için ayrılabilir genel satırlar, sipariş tarihi/satır no sırasıyla.
    Task<PurchaseFutureSupplyPageV1> ListAllocatableLinesAsync(Guid productId, int pageSize, string? cursor, CancellationToken ct = default);
}

public enum PurchaseFutureSupplyOutcomeV1 { Allocatable = 1, NotAllocatable = 2, NotFound = 3 }

public sealed record PurchaseFutureSupplyAnswerV1(
    PurchaseFutureSupplyOutcomeV1 Outcome, PurchaseFutureSupplyLineV1? Line, string? Status, Guid? ParentLineId);

public sealed record PurchaseFutureSupplyLineV1(
    Guid CompanyId, Guid OrderId, Guid LineId, int LineNo,
    Guid SupplierPartyId, Guid CompanyAccountId,
    Guid ProductId, string ProductVersion, Guid UnitId, string Kind,
    string CommittedText, string AcceptedText, string CancelledText,
    string AllocatableText,                                 // bugün committed − accepted − cancelled (§2 INV-006 genişlemesi); satırın birimi (UnitId), Ö2 tam ondalık metin
    Guid? SalesSourceOrderId, Guid? SalesSourceOrderLineId, // satış talebi niyeti (§2), yoksa null
    long ObservedOrderVersion);                             // yalnız gözlem; kilit/expected-version değildir

public sealed record PurchaseFutureSupplyPageV1(IReadOnlyList<PurchaseFutureSupplyLineV1> Items, string? NextCursor);
```

Kurallar:

- **Şirket parametre değildir.** Şirket sunucu bağlamından ve tenant filtresinden gelir (`PurchaseSupplyReferencePort.cs:22-30`
  deseni). Bağlamda şirket yoksa port `NotFound` döner (fail-closed). Başka şirketin siparişi `NotFound`'dur; 403 dönmez.
  `CompanyId` alanı yalnız yanıtın hangi bağlamda okunduğunu gösterir.
- Fiyat, iskonto, döviz, ödeme alanı yoktur (T28).
- Okuma hiçbir şey oluşturmaz. Port yetki taşımaz; tüketici kendi yetkisini ölçer (LOG-4 D6).
- **Birim (SAT-D298):** `AllocatableText` ve diğer miktarlar satınalma satırının kendi birimindedir (`UnitId`, Settings birim
  kimliği). Alış çevirmez. Karşılaştırma satınalma satırı biriminde yapılır: Stok kullanıcının kg/ton girdisini merkezi birim
  çevrim servisiyle (kaynak ürün kartı katsayısı) satırın birimine çevirir. Katsayı yoksa çevrim `NoSource`'tur ve ayırma
  reddedilir; tahmin veya ekran katsayısı yoktur (#1116 §3.2 adım 3, S10).
- **`ObservedOrderVersion` yalnız gözlemdir.** Stok bunu kayıtta saklayabilir; hiçbir modül onu modüller arası kilit veya
  expected-version olarak kullanmaz. Eşzamanlılık koruması §3.4'tedir.
- **Termin dilimleri bu portta yoktur.** API-30 dilimleri (#1101) ayrı bir okuma olarak eklenir (F17-1b:
  `GetLineScheduleAsync(orderId, lineId)` → dilim kimliği, teslim tarihi, miktar, kalan). Böylece F17-1 ve F17-2 #1101'i
  beklemez. Ayırma kararı dilime bağlı değildir; dilim yalnız ekran bilgisidir.

### 3.2 Stok → Alış: `IInventoryFutureAllocationReadPortV1` (Stok sahibi; #1116 F017-b)

ALS-D30 "Alış okuma portu ayrılan miktarı da gösterir" ve §3.5 (ALS-D83) için. Alış Stok'a referans vermez; host adaptörü çevirir
(mevcut `PurchasingSalesLinkedSupplyHostPorts` deseni).

```csharp
// Bir satınalma genel satırına bağlı, aktif ayırmaların toplamı (satırın birimiyle, tam ondalık metin). Şirket bağlamdan.
Task<FutureAllocationSumV1> GetAllocatedOnPurchaseLineAsync(Guid orderId, Guid lineId, CancellationToken ct = default);
// Present(text) / None / Unreadable. Unreadable hiçbir zaman "sıfır" sayılmaz (fail-closed).
```

### 3.3 Mal kabulü (aynı host işlemi)

- Alış'ın kabul payı (`IPurchaseAcceptanceParticipantV1.StageAsync`) ve Stok'un mal girişi (`InventoryGoodsReceiptRequestV1`)
  zaten aynı host işlemindedir ve ikisi de satınalma satırı kimliğini taşır.
- **Teknik dilim eşlemesi:** kabul teknik dilim satırına yapılırsa Alış kabul sonucu (`PurchaseAcceptanceStageResultV1`) dilimin
  genel satır kimliğini de taşır (F17-1'de eklenen alan; v1 sözleşmesi sürüm artırılarak genişletilir). Host, Stok'a bu genel
  satır kimliğini verir. Kabul genel satıra yapılırsa iki kimlik aynıdır.
- Stok aynı işlemde genel satırın ayırmalarını kabul edilen miktar kadar fiziksel tahsise çevirir ve etiketi satış sipariş satırına
  atar (ALS-D82 "etiket ataması o zaman yapılır"). Replay çiti çevirmeyi kapsar.
- **#1116 §5 hizalaması:** #1116 §5 "mal kabulde yeni Alış alanı gerekmez" der. Bu yalnız genel satıra yapılan kabul için
  doğrudur. Teknik dilime kabul için genel satır kimliği alanı (yukarıda) ve kullanıcı sırası için host komutundaki
  `AllocationOrder` gerekir. İki belge F17-1/F17-3 PR'larında aynı metne getirilir.
- **Bağlama hatası (ALS-D89):** çevirme **iş nedeniyle** yapılamazsa (etiket blokta (S8), birim çevrimi `NoSource`, satış satırı
  onaysız veya iptal) mal kabulü durmaz; mal stoğa girer, ilgili ayırma Stok'ta bırakılır (`reasonCode =
  acceptance-link-failed`) ve satışa uyarı olayı yazılır. Kabul geri alınmaz. **Teknik hata** (veritabanı hatası, kilit/eşzamanlılık
  çakışması, zaman aşımı) iş nedeni değildir: host işleminin tamamı geri alınır, kabul de yazılmaz (yazar okuması, #1116 §3.5 ile
  aynı; geri alınabilir).
- **Az/fazla gelme (ALS-D87):** az gelirse gelen miktar sıraya göre ayırmalara çevrilir, kalan ayırma satırın gelecek kısmında
  aktif kalır. "Satır kapanırsa" API-11 eksik teslim kapanışıdır (ALS-D10/D12): kapanışta kalan aktif ayırmalar bırakılır ve
  satış uyarılır (§3.6). Sipariş kalanının iptali bu madde değildir; o ALS-D85 akışıdır (§3.5). Fazla gelen kısım (ayrılmış
  toplamı aşan kabul miktarı) serbest stoğa girer, ayırmaya bağlanmaz. **Kabul kuralı değişmez:** kalan taahhüdü aşan kabul bugün
  `AcceptanceExceedsRemaining` ile reddedilir (`PurchaseOrder.cs:337-341`); F-017 bunu gevşetmez. Toleranslı fazla teslim ayrı
  iştir (No 40/ALS-D32, API-40); geliştirici tolerans eklemez.
- **Kabul geri alma/iade (ALS-D86, ALS-D91):** §3.7.
- **Sıra (ALS-D84):** satıra birden çok ayırma varken kabul edilen miktar varsayılan olarak **satış sipariş satırının termini en
  yakın** ayırmaya önce gider. Öncelik sırası #1116 ile aynıdır: kabul anında verilen `AllocationOrder` > Stok'taki kalıcı
  `ManualSequence` (#1116) > satış sipariş satırı termini > ayırmanın oluşturma zamanı. Son iki eşitlik kuralı tekniktir, geri
  alınabilir. Kabul eden kullanıcı sırayı **değiştirebilir veya ayırma seçebilir**: host
  mal kabul komutuna isteğe bağlı `AllocationOrder` (genel satır başına sıralı ayırma kimlikleri) eklenir ve Stok'a iletilir. Boşsa
  varsayılan sıra uygulanır. Listedeki her kimlik o genel satırın aktif ayırması olmalıdır; değilse 422, hiçbir şey yazılmaz.
  Listede olmayan ayırmalar listedekilerden sonra varsayılan sırayla gelir. Seçilen sıra kabul yük hash'ine girer (replay).
  Termin satış sipariş satırı portundan okunur; Alış satış terminini saklamaz. Ekran varsayılan sırayı gösterir, kullanıcı
  sürükleyerek veya seçerek değiştirir (F17-5/kabul ekranı).
- **Replay çiti çevirmeyi kapsar:** kabulün mevcut idempotency kaydı (anahtar hash + yük hash) çevirmeyi de kapsar. Aynı anahtarla
  tekrar gelen kabul yeniden çevirme yapmaz; saklanan sonucu döndürür. Katılımcının saklanan sonuç özeti çevirme sonucunu içerir
  (tüketilen ayırma kimlikleri ve miktarları, oluşan fiziksel tahsis kimlikleri). Aynı anahtar + farklı istek (yük hash'i) = 409;
  problem kodu mevcut kabul kodudur (`purchasing_acceptance_idempotency_conflict`, `IPurchaseAcceptanceParticipantV1.cs:116`).
- **Alış tarafı:** kabul sonucu satırın kabul sonrası kalanını zaten döndürür (`RemainingQuantity`). Satış talebi niyeti (§2)
  gerektiğinde `GetAllocatableLineAsync` ile okunur.

### 3.4 Modüller arası yarış: ortak satır kilidi (P1)

Sorun: Stok "Alış'tan oku → ayrılmış toplamı kontrol et → yaz" yapar. Bu arada Alış aynı satırın kalanını (kabul, kalan iptali,
ileride azaltma) değiştirirse ayrılmış > ayrılabilir olabilir (check-then-write yarışı). Stok'un kendi defter satırı concurrency
token'ı yalnız Stok'un iki ayırmasını sıralar; Alış yazısını görmez.

Öneri (teknik; geri alınabilir):

- Host düzeyinde `(şirket, satınalma genel satırı)` anahtarlı **işlem kapsamlı özel uygulama kilidi** (SQL Server
  `sp_getapplock`, `@LockOwner = 'Transaction'`, `@LockMode = 'Exclusive'`). Kaynak adı:
  `goymen:purchase-line:{companyId:N}:{lineId:N}`. Şirket kimliği sunucu bağlamından alınır.
- Arayüz host/BuildingBlocks'ta tek yerde durur (öneri ad: `IPurchaseLineSerializationLockV1.AcquireAsync(Guid lineId, ct)`);
  iki modül de host adaptörü üzerinden çağırır, birbirine referans vermez.
- **Kilidi kim alır:** (1) Stok ayırma oluşturma (Alış okumasından **önce**), (2) Stok ayırma bırakma, (3) Alış kabul payı
  (kabul edilen her genel satır için; aynı işlemdeki Stok çevirmesi aynı sahipte olduğu için yeniden kilitlemez), (4) Alış sipariş
  kalanı iptali ve "bağları düşür ve iptal et" (etkilenen her genel satır), (5) ileride Alış satır azaltma (F17-4), (6) API-11
  satır kapanışı ve yeniden açma (§3.6), (7) kabul düzeltmesi ve tedarikçiye iade (§3.7; Alış payı ve Stok geri alması aynı
  işlemde, aynı sahip).
- **Sıra:** birden çok satır kilitlenecekse `lineId` artan sırayla alınır (kilitlenme önlemi).
- **Zaman aşımı:** kısa (öneri 5 sn). Alınamazsa işlem yazmadan 409 `purchase_line_busy` ile biter; replay değilse istemci yeniler.
  Kilit dönüş kodu < 0 ise asla devam edilmez (fail-closed).
- Kilit işlem bitince (commit/rollback) kendiliğinden bırakılır. Uzun süreli kilit yoktur.
- Kilit alındıktan sonra Stok Alış'ı aynı işlemde okur; ayrılmış + yeni ≤ ayrılabilir değilse 409
  `inventory_future_allocation_insufficient_future_supply` (Stok'un kodu, #1116 §7 ile aynı).
  Alış tarafı kilit altında §3.2 portuyla ayrılmış toplamı okur; yeni kalan ayrılmışın altına düşecekse reddeder (§3.5).
- Gerçek SQL testi: iki eşzamanlı işlem (Stok ayırma + Alış kalan iptali) aynı satırda; biri bekler veya 409 alır, sonuçta
  ayrılmış ≤ ayrılabilir. Kilit kaynağı adı contract testine bağlanır.

### 3.5 Satınalma tarafından bağ düşürme (ALS-D83)

- Satınalma kullanıcısı, bir satınalma genel satırına bağlı satış ayırmasını **Alış ekranından** düşürebilir. Bu, ayırmanın Stok'ta
  bırakılmasıdır (`Released`); Alış Stok tablosuna yazmaz. Alış komutu host adaptörü üzerinden Stok'un yazma portunu çağırır
  (öneri: `IInventoryFutureAllocationReleasePortV1.ReleaseAsync(allocationId, expectedAllocationVersion, reasonCode, reasonText,
  origin = "purchasing")`, Stok sahibi; #1116 §3.3 bırakma akışının aynısı). Şirket sunucu bağlamından gelir.
- **Satınalma miktarı azalmaz.** Committed, accepted, cancelled değişmez; ayrılabilir miktar bağ kadar yeniden serbest kalır.
  Serbest miktar başka satış siparişine ayrılabilir veya stok için alınır (ayrı işlem gerekmez; ayrılmamış miktar zaten stok
  içindir).
- Neden zorunludur. Sessiz bırakma yoktur (DEC-STK-005): Stok `inventory.future-allocation.released.v1` olayını `origin =
  purchasing` ile yazar; satış ekranı bunu inbox/dedup ile tüketip satış sipariş satırında "kaynak düştü" uyarısı gösterir.
- Komut §3.4 kilidini alır, idempotency anahtarı ve audit (önce/sonra, neden, aktör) taşır. İzin adı önerisi
  `purchasing.future-allocation.release` (ad kaydı teknik; grant insan kapısı).
- **Neden kodu ve gerekçe metni ayrıdır.** `reasonCode` makine kodudur ve satışa giden uyarının türünü belirler; `reasonText`
  kullanıcının yazdığı gerekçedir (audit'e girer, uyarıda gösterilir). Neden kodu listesi (tek yer; Stok olayında taşınır):

  | `reasonCode` | Nerede | Satışa uyarı |
  |---|---|---|
  | `purchasing-link-dropped` | Satınalma tarafı bağ düşürme (bu bölüm, ALS-D83) | "kaynak düştü" |
  | `source-cancelled` | Bağları düşür ve iptal et (ALS-D85) | "kaynak iptal edildi" |
  | `purchase-line-closed` | API-11 satır kapanışı (§3.6, ALS-D87) | "kaynak eksik teslimle kapandı" |
  | `acceptance-link-failed` | Kabulde iş nedenli bağlama hatası (§3.3, ALS-D89) | "kabulde bağlanamadı" |
  | `acceptance-reversed-no-supply` | Kabul geri alma, yeniden açılacak gelecek miktar yok (§3.7, ALS-D86/D91) | "kabul geri alındı, kaynak yok" |

  Stok'un kendi bırakma nedenleri (#1116) bu listeye eklenmez; satış ekranı ikisini birlikte gösterir.
- **Satır azaltma** (ileride F17-4): yeni kalan, ayrılmış toplamın altına düşecekse Alış işlemi reddeder ve kullanıcıya önce bağ
  düşürmesini söyler (ALS-D83 okuması: miktar bağ yüzünden kendiliğinden azalmaz; bağ önce açıkça düşürülür).
- **Sipariş kalanı iptali (ALS-D85):** kodda satır düzeyinde iptal yoktur; iptal onaylı siparişin tüm kalanıdır (§1). Ayırması
  olan bir satır varsa sipariş doğrudan iptal edilmez: düz iptal komutu (API-10) kilit altında aktif ayırma görürse 409
  `purchase_line_has_allocations` ile reddeder ve bağlı aktif ayırmaları (ayırma kimliği, sürüm, satış sipariş satırı, miktar)
  döner. Ekran bunları listeler ve "bağları düşür ve iptal et" seçeneği sunar.
- **"Bağları düşür ve iptal et" komutu (ALS-D85):**
  - İstek: sipariş kimliği, `ExpectedVersion`, zorunlu `reasonText`, idempotency anahtarı ve kullanıcının gördüğü küme
    `SeenAllocations = [(allocationId, expectedAllocationVersion)]`.
  - Host komutu etkilenen genel satırların §3.4 kilitlerini artan sırayla alır, kilit altında her satırın aktif ayırmalarını
    §3.2 portundan okur. Aktif küme `SeenAllocations` ile birebir aynı değilse (yeni ayırma açılmış, biri kabulde tüketilmiş veya
    bırakılmış, sürüm değişmiş) 409 `future_allocation_set_changed` döner ve hiçbir şey yazılmaz. Kullanıcının görmediği ayırma
    sessizce bırakılmaz.
  - Küme aynıysa aynı işlemde önce her ayırma için Stok bırakma portu (`reasonCode = source-cancelled`, `reasonText` kullanıcının
    gerekçesi) çağrılır, sonra Alış iptali yazılır. Yalnız aktif ayırmalar bırakılır; tüketilmiş ayırmalara ve fiziksel tahsislere
    dokunulmaz. Biri başarısızsa işlemin tamamı geri alınır.
  - **Host orkestrasyonu:** bugünkü `CancelOrderAsync` tek Alış store kaydıdır (`PurchasingService.OrderCancellation.cs:54`).
    Birleşik komut, mal kabulündeki gibi host düzeyinde tek işlem açar: Alış iptal payı kabul katılımcısı desenine benzer bir
    katılımcı (`IPurchaseCancellationParticipantV1.StageAsync`, öneri) olarak, Stok bırakmaları host adaptörüyle aynı
    `DbTransaction` içinde yazılır.
  - **Sıra:** idempotency replay kontrolü `ExpectedVersion` ve `SeenAllocations` karşılaştırmasından **önce** yapılır. Başarılı
    komuttan sonra aktif küme boşaldığı için aynı anahtarla gelen tekrar saklanan sonucu alır, 409 almaz.
  - **Replay:** idempotency kaydının sahibi host komutudur (kabuldeki gibi tek kayıt). İstek hash'ine sipariş kimliği,
    `ExpectedVersion`, `reasonText` ve sıralı `SeenAllocations` girer. Saklanan sonuç bırakılan ayırma kimliklerini ve yeni sipariş
    sürümünü içerir. Aynı anahtar + farklı istek = 409 (ad mevcut desene uyar, öneri
    `purchasing_cancellation_idempotency_conflict`; F17-4'te kesinleşir).
  - **İzin (teknik):** komut hem `purchasing.orders.cancel` hem `purchasing.future-allocation.release` ister; biri yoksa 403.
    Grant insan kapısıdır.
  - Gerekçe, aktör, önce/sonra ve bırakılan ayırmalar audit'e yazılır.
- **Süre ve termin uyarısı (ALS-D90):** ayırmanın son tarihi yoktur; süre ileride ayarlardan tanımlanabilir (Sinan metni), bugün
  ayar yoktur. Satınalma satırının termini geçince satış ve satınalma kullanıcısı uyarılır, ayırma aktif kalır. Uyarı **okuma
  anında hesaplanır** (olay yok, zamanlanmış iş yok): Alış satır termini (F17-1b) bugünden önceyse ve satırda aktif ayırma varsa
  ekran "termin geçti" gösterir. Satınalma tarafı F17-4 ekranında, satış tarafı F17-5 kaynak görünümünde gösterir (yazar
  önerisi, geri alınabilir).
- **Kim ayırır (ALS-D88):** ayırma izni olan kullanıcı (satış, satınalma veya depo) tek başına ayırır; ikinci onay yoktur. İzin
  adı kodlanır; grant insan kapısıdır.

### 3.6 Satır kapanışı (API-11) ve ayırma (ALS-D87, ALS-D10/D12)

- API-11 eksik teslim kapanışı satırın kalanını kapatır (iptalden ayrı eksen). Kapanış Alış'ta olur; Stok bunu kendiliğinden
  görmez. Bu yüzden bırakmayı Alış komutu yapar: kapanış komutu §3.4 kilidini alır ve aynı host işleminde satırın aktif
  ayırmalarını Stok bırakma portuyla `reasonCode = purchase-line-closed` (gerekçe metni kapanış gerekçesi) ile bırakır. Biri
  başarısızsa kapanış da yazılmaz. #1116 §3.5'teki `purchase-line-closed` bu çağrıdır.
- ALS-D10 tolerans içi kapanışta sistem önerir ve sorar; ALS-D12 tolerans dışını satın alma sorumlusu gerekçeyle kapatır. Onay
  ekranı satırın bağlı aktif ayırmalarını (satış sipariş satırı, miktar) gösterir; kullanıcı bunların bırakılacağını görerek
  onaylar (UX şerit kararı, geri alınabilir). İstek "bağları düşür ve iptal et"teki gibi `SeenAllocations` taşır; farklıysa 409
  `future_allocation_set_changed`. Replay kontrolü küme ve sürüm karşılaştırmasından önce yapılır (§3.5).
- **İzin (teknik):** satırda aktif ayırma yoksa kapanış yalnız `purchasing.orders.close` ister. Aktif ayırma varsa
  `purchasing.orders.close` + `purchasing.future-allocation.release` ister; biri yoksa 403 (§3.5 ile aynı bileşim). Grant insan
  kapısıdır.
- **Yeniden açma:** kapanan satır yeniden açılınca (ALS-D10) bırakılmış ayırmalar geri gelmez. Açılan kalan yeniden ayrılabilir
  olur; satış isterse yeni ayırma yapar.
- Dilim (tek kural): API-11 F17-4'ten önce merge edilirse kapanışta bırakmayı F17-4 ekler; F17-4'ten sonra merge edilirse
  bırakma API-11 diliminin içindedir (§4). Ayırma oluşturma yolu F17-4'ten önce açılmadığı için (§4 kapı kuralı) arada bırakılacak
  ayırma oluşmaz.

### 3.7 Kabul geri alma ve tedarikçiye iade (ALS-D86, ALS-D91)

Bugün kodda kabul geri alma/iade yoktur (§1). Akışın kendisi plan API-44 (kabul düzeltmesi: ters + yeni) ve API-60 (tedarikçiye
iade) dilimleridir. Bu bölüm o dilimlerin F-017 kurallarıdır; uygulama F17-6'dır (§4).

- **Gelecek miktar ne zaman açılır (ALS-D91):** Sinan metni: hatalı kabul geri alınırsa miktar siparişe geri açılır;
  tedarikçiye iadede "yerine yenisi gelecek" işaretlenirse açılır, işaretlenmezse sipariş kapalı kalır. Bu metin **tam kabul**
  edilmiş veya kalanı açık (Approved) satırda aynen uygulanır.
- **INV-006 eşlemesi (yazar okuması, geri alınabilir):** master INV-006 `Onaylı − İptal − KabulNet − KesinRed/İadeEtkisi`.
  - Hatalı kabulün geri alınması (API-44 ters kayıt): `KabulNet` düşer, kalan aynı miktar kadar artar.
  - İade, işaretli: `KabulNet` düşer, `İadeEtkisi`'ne yazılmaz; kalan artar.
  - İade, işaretsiz: `KabulNet` düşer ve aynı miktar `İadeEtkisi`'ne yazılır; kalan artmaz. "Sipariş kapalı kalır" = satır kalanı
    değişmez (tam kabulde 0 kalır); yeni durum yoktur.
  - Kalanı 0 olan (tam kabul) satırda açılan miktar satırı yeniden açık yapar; yeni durum gerekmez, satır kalanı pozitif olur.
- **Cancelled ve API-11 kapalı kesişimi (Sinan metni bu durumu seçmedi):** `Cancelled` terminaldir (`PurchaseOrder.cs:12-13`)
  ve API-11 kapalı satır ayrılamaz (§2). Yazar okuması (geri alınabilir): bu iki durumda gelecek miktar açılmaz; geri açılan
  miktar Alış'ta Cancelled'da iptal payına, API-11 kapalıda kapanış payına yazılır, satır kalanı 0 kalır (`Cancel` değişmezi
  "her satırın kalanı iptal payıdır" korunur). Payın yeri API-44/API-60 diliminin kararıdır; o dilim bu okumayı kabul etmezse
  #66'ya `NEEDS-SINAN` yazılır. **Cevaba veya o dilimin kararına kadar F17-6 bu iki durumda geri almayı ve işaretli iadeyi
  fail-closed 409 `purchase_line_not_open` ile reddeder.**
- **"Yerine yenisi gelecek" işaretinin yeri:** API-60 isteğinde zorunlu alan (`ReplacementExpected`, bool; öneri). Audit'e ve
  idempotency istek hash'ine girer; iade yazıldıktan sonra değiştirilemez (düzeltme ters iade + yeni iade ile).
- **Stok tarafı (aynı host işlemi, §3.4 kilidi altında):** geri alınan etiketlere bağlı fiziksel tahsis ve etiket ataması geri
  alınır.
  - Gelecek miktar varsa ilgili ayırma aynı ayırma kimliğiyle `Consumed` → `Active` döner (sürüm artar), ayrılmış + yeniden
    açılan ≤ ayrılabilir kontrolüyle.
  - Gelecek miktar yoksa veya yetmiyorsa karşılanamayan kısım bırakılır (`reasonCode = acceptance-reversed-no-supply`) ve satış
    uyarılır. Kısmi yeniden açma Sinan metninin (var/yok) genellemesidir: **yazar okuması, geri alınabilir**; #1116 ile aynı metne
    getirilir.
- **Fail-closed teknik kurallar (yazar okuması, geri alınabilir):**
  - Etiket satışa sevk edildiyse veya tüketildiyse o etiket için geri alma 409 `allocation_already_fulfilled` ile reddedilir;
    önce satış/sevk tarafında ters işlem gerekir.
  - Satış tarafı fiziksel tahsisi bıraktıysa veya satış satırı iptal edildiyse yeniden açılacak ayırma yoktur: yalnız etiket
    geri alınır, ayırma açılmaz, uyarı gerekmez. "Aynı kimlikle yeniden açılır" yalnız fiziksel tahsis hâlâ aktifse geçerlidir.
  - Kısmi geri almada önce geri alınan etiketlerin bağlı olduğu tahsisler geri alınır. Etiket ayrımı yoksa ALS-D84 sırasının
    tersi uygulanır: termini en uzak satış önce geri alınır, termini en yakın olan korunur. Yeniden açma da aynı sırayla yapılır.
- **API-44 ters + yeni çevirme sırası:** aynı düzeltmede ters kayıt bir ayırmayı yeniden açarsa yeni kabul önce o ayırmaları
  (aynı ayırma kimliği) tüketir, artan miktar ALS-D84 sırasına göre gider. Böylece düzeltme malın gideceği satışı değiştirmez
  (yazar okuması, geri alınabilir).
- Geri alma ve iade komutları §3.4 kilit listesindedir (7). Replay kaydı akışın kendi host komutundadır; çevirme geri alması
  sonuç özetine girer.

## 4. Dilim planı

Sıra: #1101 kuyruğu bitmeden kod yok (koordinatör, 2026-11-12).

**Kapı kuralı (değişmezin her merge'de korunması):**
- F17-2 Stok ayırma kaydını, değişmezi ve bırakmayı getirir; ama **ayırma oluşturma yolu** (endpoint ve izin kaydı) F17-2'de
  kayıtlı değildir; yalnız servis ve testler vardır. Oluşturma yolu F17-3 (kabulde çevirme) ve F17-4 (iptal koruması) merge
  edildikten sonra **F17-2b** ile açılır. Böylece kabul çevirmesi veya iptal koruması yokken aktif ayırma oluşmaz; aynı ton hem
  ayırmada hem serbest stokta sayılmaz ve iptal ayırmayı iptal arzın üstünde bırakmaz.
- API-44/API-60 F17-6'dan önce merge edilirse, aktif veya tüketilmiş gelecek ayırması olan satırda kabul geri alma/iade
  fail-closed 409 `purchase_line_has_allocations` ile reddedilir; F17-6 bu reddi kaldırır ve §3.7 kurallarını getirir.
- API-11 için tek kural §3.6'dadır.

| Dilim | Şerit | İçerik | Kapı | Bağımlılık |
|---|---|---|---|---|
| F17-0 | Alış | Bu belge + EK17 (docs) | K1 | — |
| F17-1 | Alış (+host) | `IPurchaseFutureSupplyReadPortV1` (tek satır + ürün listesi, satış niyeti, teknik→genel eşleme) + Infrastructure okuması; `IPurchaseLineSerializationLockV1` host uygulaması; Alış kabul/iptal yollarında kilit; kabul sonucuna genel satır kimliği; birim/servis/contract testleri + gerçek SQL kabulü (eşzamanlılık dahil); A3-0'ı kapatır | **K3** (yeni modüller arası sözleşme + kilit; migration yok, gerekirse ürün+durum indeksi) | — |
| F17-1b | Alış | Termin dilimi okuması `GetLineScheduleAsync` | K2 | #1101 |
| F17-2 | Stok | Gelecek ayırma kaydı (#1116 F017-b), değişmez (§3.4 kilidi + aynı işlemde F17-1 okuması), bırakma; oluşturma endpoint'i yok (kapı kuralı) | K3 | F17-1 |
| F17-3 | Stok (+host) | Mal kabulünde gelecek ayırma → fiziksel tahsis + etiket ataması (aynı host işlemi, replay çiti, ALS-D84 sırası ve `AllocationOrder` §3.3) (#1116 F017-c) | K3 | F17-2 |
| F17-4 | Alış (+host) | Bağ düşürme komutu ve ekranı (§3.5, Stok bırakma portu üzerinden); sipariş kalanı iptali/satır azaltmada ayrılmış miktar kontrolü (§3.2 portu, §3.4 kilidi); ALS-D85 "bağları düşür ve iptal et" host komutu (görülen küme, replay, iki izin); termin geçti uyarısı (Alış ekranı); API-11 F17-4'ten önce merge edildiyse kapanışta bırakma (§3.6) | K3 | F17-2 |
| F17-2b | Stok | Ayırma oluşturma endpoint'i ve izin kaydı (grant insan kapısı) | K2 | F17-3, F17-4 |
| API-11 | Alış (+host) | Satır kapanışı/yeniden açma; port formülüne `closed` (§2); F17-4'ten sonra merge edilirse kapanışta `purchase-line-closed` bırakması, iki izin ve §3.4 kilidi (§3.6) | K3 | plan A3 |
| F17-5 | Satış/UI | Satış kaynak planında "alıştan" seçimi (MOD-16); uyarı olaylarının gösterimi (§3.5 neden kodları), termin geçti uyarısı | Satış şeridi | F17-2b |
| F17-6 | Alış + Stok (+host) | Kabul geri alma/iade F-017 kuralları (§3.7, ALS-D86/D91): gelecek miktarın nedene göre açılması, Stok tahsis/etiket geri alması, yeniden açma/bırakma, fail-closed kurallar, Cancelled/API-11 kapalı kesişiminde 409, `ReplacementExpected`, ters + yeni sırası, port formülüne `iadeEtkisi` (§2), §3.4 kilidi | K3 | F17-3, API-44, API-60 |

## 5. NEEDS-SINAN (hepsi cevaplandı)

Not: §3.7 Cancelled/API-11 kapalı kesişimi Sinan metninin seçmediği bir durumdur; yazar okuması ve fail-closed 409 ile
yazıldı. API-44/API-60 dilimi okumayı kabul etmezse #66'ya `NEEDS-SINAN` yazılır.

Ortak sorular #1116 §9 ile tek cevapla kapanır: S1 + S4 = #1116 S2, S2 = #1116 S4, S3 = #1116 S12, genel/teknik satır = #1116 S9.

- **S1 — Cevaplandı (ALS-D83):** satınalma tarafı bağı düşürür, satınalma miktarı azalmaz; serbest miktar başka satışa ayrılır veya
  stoğa alınır. Uygulama §3.5.
- **S2 — Cevaplandı (ALS-D84):** termini en yakın satış önce; sıra elle değiştirilebilir/seçilebilir. Uygulama §3.3.
- **S3 — Cevaplandı (ALS-D86):** kabul geri alınırsa tahsis ve etiket geri alınır; gelecek miktar varsa ayırma yeniden açılır,
  yoksa bırakılır ve satış uyarılır. 18. tur m.7 (ALS-D91) gelecek miktarı nedene bağladı. Uygulama §3.7, F17-6.
- **S4 — Cevaplandı (ALS-D85):** iptal doğrudan yapılmaz; bağlı satışlar listelenir, "bağları düşür ve iptal et" gerekçeli
  tek onayla aynı işlemde yapılır, satışa "kaynak iptal edildi" uyarısı gider. Uygulama §3.5.
- **#1116 ortak sorular:** S1/S3 az/fazla gelme ALS-D87, S5 ayıran kişi ALS-D88, S7/S8 bağlama hatası ALS-D89, S11 süre
  ALS-D90. S6 (izin adı) ve S9 (teknik/genel eşleme) teknik konudur; S10 SAT-D298 ile kilitlidir.
- **Stok tarafı:** yukarıdaki cevaplar Stok modelini de bağlar; ayrıntı #1116'da Stok şeridinindir. KLN-D26 (6) ile tutarlıdır.

## 6. Kabul ölçütleri (F-017 testi)

- Aynı satınalma satırı miktarı iki satış siparişine verilemez. İki eşzamanlı ayırma isteğinden toplamı aşan ikincisi reddedilir.
- Stok ayırması ile Alış kabul/kalan iptali aynı satırda eşzamanlı olduğunda sonuç her zaman ayrılmış ≤ ayrılabilir (§3.4).
- Draft ve Cancelled siparişin satırı ve API-11 ile kapalı satır ayrılamaz (ayrılabilir 0); işaretsiz iade edilen miktar
  ayrılabilir görünmez. Teknik dilim satırına doğrudan ayırma yapılamaz. Başka şirketin satırı ve
  bağlamda şirket yokken okuma `NotFound`'dur.
- Ürün kartında katsayı yoksa kg/ton girdisi satır birimine çevrilemez ve ayırma reddedilir (tahmin yok).
- Mal kabulünde ayrılan miktar fiziksel tahsise döner ve etiket satış sipariş satırına atanır; aynı kabul isteğinin tekrarı ikinci
  çevirme yapmaz.
- Kısmi kabulde varsayılan sıra termini en yakın satış; kullanıcının verdiği `AllocationOrder` varsayılanı geçer; geçersiz kimlik
  422 ve yazma yok (ALS-D84).
- Satınalma tarafı bağı düşürünce ayırma Stok'ta `Released` olur, satınalma committed/accepted/cancelled değişmez, ayrılabilir miktar
  aynı kadar artar ve satışa uyarı olayı yazılır (ALS-D83).
- Ayırması olan siparişin düz kalan iptali 409 `purchase_line_has_allocations` ile reddedilir ve bağlı aktif ayırmaları döner.
  "Bağları düşür ve iptal et" gerekçesiz reddedilir; görülen küme kilit altındaki aktif kümeden farklıysa 409
  `future_allocation_set_changed` ve yazma yok; aynıysa bağ düşürme ve iptal tek işlemde olur, biri başarısızsa hiçbiri yazılmaz;
  tüketilmiş ayırmalara dokunulmaz; iki izinden biri yoksa 403 (ALS-D85).
- Hatalı kabul geri alınınca kalan açılır; "yerine yenisi gelecek" işaretli iadede açılır, işaretsiz iadede açılmaz. Açılan
  gelecek miktar varsa ve fiziksel tahsis aktifse ayırma aynı kimlikle yeniden açılır; yoksa bırakılır ve satışa uyarı yazılır.
  Sevk edilmiş/tüketilmiş etiketin geri alınması 409. Cancelled siparişte veya API-11 kapalı satırda geri alma/işaretli iade
  409 `purchase_line_not_open` (karar gelene kadar) (ALS-D86, ALS-D91).
- F17-2b merge edilmeden ayırma oluşturma endpoint'i yoktur; F17-6 öncesi ayırmalı satırda kabul geri alma/iade 409.
- Aynı anahtarla tekrarlanan "bağları düşür ve iptal et" veya kapanış saklanan sonucu döndürür, küme farkı 409'u almaz.
- Az kabulde kalan ayırma aktif kalır. API-11 ile kapanan satırın aktif ayırmaları aynı işlemde bırakılır ve uyarılır; satır
  yeniden açılınca bırakılan ayırma geri gelmez. Fazla kabul edilen miktar serbest stoktur. Kalanı aşan kabul
  `AcceptanceExceedsRemaining` ile reddedilmeye devam eder (ALS-D87).
- İş nedenli çevirme hatasında (bloklu etiket dahil) kabul tamamlanır, mal stoğa girer, ayırma bırakılır ve uyarı yazılır. Teknik
  hatada işlemin tamamı geri alınır, kabul yazılmaz (ALS-D89).
- Termini geçen satınalma satırındaki ayırma aktif kalır ve okuma anında "termin geçti" uyarısı görünür (ALS-D90).
- Ayrılabilir miktar her zaman Alış'ın kalanıdır; tüketici yeniden hesaplamaz.
