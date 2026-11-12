# F-017 — Gelecek tedarikten satış siparişine ayırma: spec ve dilim planı v1

Tarih: 2026-11-12 · Hazırlayan: Claude (cloud oturumu, Alış şeridi) · Durum: **ÖNERİ** (plan; iş kararı değildir)
Karar: `docs/decisions/SINAN_KARARLARI_2026-11-12_ALIS_EK17.md` ALS-D82 (#66 6819147253 madde 2), ALS-D83/ALS-D84 (#66 6819640816
madde 3–4); Stok KLN-D26 (#1108); ALS-D30 (EK3); ALS-D22/D25;
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

## 2. Kavramlar

- **Ayrılabilir miktar (Alış'ın söylediği):** onaylı **genel** satırın kalanı = committed − accepted − cancelled. Teknik dilimler genel
  satırın içindedir; iki kez sayılmaz (mevcut kural). Draft ve Cancelled siparişin satırı ayrılamaz.
- **Ayrılmış miktar (Stok'un tuttuğu):** bir satınalma genel satırına bağlı, henüz kabul edilmemiş satış ayırmalarının toplamı.
- **Değişmez (ALS-D82):** bir satınalma satırı için ayrılmış ≤ ayrılabilir. Aynı miktar iki satış siparişine verilemez. Değişmezin
  sahibi Stok'tur. Alış ayrılmış miktarı hesaplamaz ve saklamaz. Değişmezin iki modül arasında korunması §3.4 kilidiyle olur.
- **Satış talebi bağlantısı (Alış'ın bildiği):** satırın talep satırı bir satış siparişinden geldiyse (`SourceKind = Sales`), satır
  o siparişin niyetini taşır. Bu bir **niyet**tir, ayırma değildir. Ayırma kaydı her zaman Stok'ta açıkça kurulur. Alış, satış
  kaynaklı bir satır onaylanınca kendiliğinden ayırma oluşturmaz (okuma, geri alınabilir).
- **Genel / teknik satır (yazarın önerisi, geri alınabilir; #1116 S9):** ayırma **genel satıra** yapılır, teknik dilime yapılmaz.
  Ayrılabilir miktar genel satır düzeyindedir. Kabul teknik dilimden gelirse Alış kabul payı o dilimin bağlı olduğu genel satır
  kimliğini bildirir (§3.3); Stok çevirmeyi genel satırdaki ayırmalar üzerinden yapar. Teknik dilimin kendi kimliği Stok'ta
  yalnız izlenebilirlik alanıdır. Sinan S9'u farklı cevaplarsa bu madde değişir; cevaba kadar F17-2 bu kuralla çalışır.

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
    string AllocatableText,                                 // committed − accepted − cancelled; satırın birimi (UnitId), Ö2 tam ondalık metin
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
  atar (ALS-D82 "etiket ataması o zaman yapılır"). İşlem tek başarı ya da tek geri alma olur. Çevirmenin hatada davranışı #1116 S7.
- **Sıra (ALS-D84):** satıra birden çok ayırma varken kabul edilen miktar varsayılan olarak **satış sipariş satırının termini en
  yakın** ayırmaya önce gider; eşitse ayırma zamanı önce. Kabul eden kullanıcı sırayı **değiştirebilir veya ayırma seçebilir**: host
  mal kabul komutuna isteğe bağlı `AllocationOrder` (genel satır başına sıralı ayırma kimlikleri) eklenir ve Stok'a iletilir. Boşsa
  varsayılan sıra uygulanır. Listedeki her kimlik o genel satırın aktif ayırması olmalıdır; değilse 422, hiçbir şey yazılmaz.
  Listede olmayan ayırmalar listedekilerden sonra varsayılan sırayla gelir. Seçilen sıra kabul yük hash'ine girer (replay).
  Termin satış sipariş satırı portundan okunur; Alış satış terminini saklamaz. Ekran varsayılan sırayı gösterir, kullanıcı
  sürükleyerek veya seçerek değiştirir (F17-5/kabul ekranı).
- **Replay çiti çevirmeyi kapsar:** kabulün mevcut idempotency kaydı (anahtar hash + yük hash) çevirmeyi de kapsar. Aynı anahtarla
  tekrar gelen kabul yeniden çevirme yapmaz; saklanan sonucu döndürür. Katılımcının saklanan sonuç özeti çevirme sonucunu içerir
  (tüketilen ayırma kimlikleri ve miktarları, oluşan fiziksel tahsis kimlikleri). Aynı anahtar + farklı sonuç = 409
  `idempotency_conflict`.
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
  (kabul edilen her genel satır için; aynı işlemdeki Stok çevirmesi aynı sahipte olduğu için yeniden kilitlemez), (4) Alış kalan
  iptali ve sipariş iptali (etkilenen her genel satır), (5) ileride Alış satır azaltma (F17-4).
- **Sıra:** birden çok satır kilitlenecekse `lineId` artan sırayla alınır (kilitlenme önlemi).
- **Zaman aşımı:** kısa (öneri 5 sn). Alınamazsa işlem yazmadan 409 `purchase_line_busy` ile biter; replay değilse istemci yeniler.
  Kilit dönüş kodu < 0 ise asla devam edilmez (fail-closed).
- Kilit işlem bitince (commit/rollback) kendiliğinden bırakılır. Uzun süreli kilit yoktur.
- Kilit alındıktan sonra Stok Alış'ı aynı işlemde okur; ayrılmış + yeni ≤ ayrılabilir değilse 409 `insufficient_future_supply`.
  Alış tarafı kilit altında §3.2 portuyla ayrılmış toplamı okur; yeni kalan ayrılmışın altına düşecekse reddeder (§3.5).
- Gerçek SQL testi: iki eşzamanlı işlem (Stok ayırma + Alış kalan iptali) aynı satırda; biri bekler veya 409 alır, sonuçta
  ayrılmış ≤ ayrılabilir. Kilit kaynağı adı contract testine bağlanır.

### 3.5 Satınalma tarafından bağ düşürme (ALS-D83)

- Satınalma kullanıcısı, bir satınalma genel satırına bağlı satış ayırmasını **Alış ekranından** düşürebilir. Bu, ayırmanın Stok'ta
  bırakılmasıdır (`Released`); Alış Stok tablosuna yazmaz. Alış komutu host adaptörü üzerinden Stok'un yazma portunu çağırır
  (öneri: `IInventoryFutureAllocationReleasePortV1.ReleaseAsync(allocationId, expectedAllocationVersion, reason, origin =
  "purchasing")`, Stok sahibi; #1116 §3.3 bırakma akışının aynısı). Şirket sunucu bağlamından gelir.
- **Satınalma miktarı azalmaz.** Committed, accepted, cancelled değişmez; ayrılabilir miktar bağ kadar yeniden serbest kalır.
  Serbest miktar başka satış siparişine ayrılabilir veya stok için alınır (ayrı işlem gerekmez; ayrılmamış miktar zaten stok
  içindir).
- Neden zorunludur. Sessiz bırakma yoktur (DEC-STK-005): Stok `inventory.future-allocation.released.v1` olayını `origin =
  purchasing` ile yazar; satış ekranı bunu inbox/dedup ile tüketip satış sipariş satırında "kaynak düştü" uyarısı gösterir.
- Komut §3.4 kilidini alır, idempotency anahtarı ve audit (önce/sonra, neden, aktör) taşır. İzin adı önerisi
  `purchasing.future-allocation.release` (ad kaydı teknik; grant insan kapısı).
- **Satır azaltma** (ileride F17-4): yeni kalan, ayrılmış toplamın altına düşecekse Alış işlemi reddeder ve kullanıcıya önce bağ
  düşürmesini söyler (ALS-D83 okuması: miktar bağ yüzünden kendiliğinden azalmaz; bağ önce açıkça düşürülür).
- **Satır/sipariş iptali** bu bölümün konusu değildir (ALS-D83 "ayrı bir durum"): §5 S4. Cevaba kadar ayırması olan satırın kalan
  iptali ve siparişin iptali reddedilir (fail-closed); kullanıcı önce bağları düşürür.

## 4. Dilim planı

Sıra: #1101 kuyruğu bitmeden kod yok (koordinatör, 2026-11-12).

| Dilim | Şerit | İçerik | Kapı | Bağımlılık |
|---|---|---|---|---|
| F17-0 | Alış | Bu belge + EK17 (docs) | K1 | — |
| F17-1 | Alış (+host) | `IPurchaseFutureSupplyReadPortV1` (tek satır + ürün listesi, satış niyeti, teknik→genel eşleme) + Infrastructure okuması; `IPurchaseLineSerializationLockV1` host uygulaması; Alış kabul/iptal yollarında kilit; kabul sonucuna genel satır kimliği; birim/servis/contract testleri + gerçek SQL kabulü (eşzamanlılık dahil); A3-0'ı kapatır | **K3** (yeni modüller arası sözleşme + kilit; migration yok, gerekirse ürün+durum indeksi) | — |
| F17-1b | Alış | Termin dilimi okuması `GetLineScheduleAsync` | K2 | #1101 |
| F17-2 | Stok | Gelecek ayırma kaydı (#1116 F017-b), değişmez (§3.4 kilidi + aynı işlemde F17-1 okuması), bırakma | K3 | F17-1 |
| F17-3 | Stok (+host) | Mal kabulünde gelecek ayırma → fiziksel tahsis + etiket ataması (aynı host işlemi, replay çiti, ALS-D84 sırası ve `AllocationOrder` §3.3) (#1116 F017-c) | K3 | F17-2 |
| F17-4 | Alış (+host) | Bağ düşürme komutu ve ekranı (§3.5, Stok bırakma portu üzerinden); kalan iptali/sipariş iptali/satır azaltmada ayrılmış miktar kontrolü (§3.2 portu, §3.4 kilidi; iptal S4 cevabına kadar ret) | K3 | F17-2 |
| F17-5 | Satış/UI | Satış kaynak planında "alıştan" seçimi (MOD-16) | Satış şeridi | F17-2 |

## 5. NEEDS-SINAN (cevaba kadar ilgili davranış fail-closed)

Ortak sorular #1116 §9 ile tek cevapla kapanır: S1 = #1116 S2, S2 = #1116 S4, S3 = #1116 S12, genel/teknik satır = #1116 S9.

- **S1 — Cevaplandı (ALS-D83):** satınalma tarafı bağı düşürür, satınalma miktarı azalmaz; serbest miktar başka satışa ayrılır veya
  stoğa alınır. Uygulama §3.5.
- **S2 — Cevaplandı (ALS-D84):** termini en yakın satış önce; sıra elle değiştirilebilir/seçilebilir. Uygulama §3.3.
- **S3 — Kabul geri alınırsa / iade:** kabul reddi, düzeltme veya iade olursa fiziksel tahsis ve etiket ataması geri döner mi,
  gelecek ayırma yeniden açılır mı? Cevaba kadar otomatik geri dönüş yoktur; kabul geri alma yolu ayırma çevrilmiş satırda
  reddeder ve kullanıcıya önce tahsisin elle çözülmesi gerektiğini söyler.
- **S4 — Ayırması olan satınalma satırının veya siparişin iptali (ALS-D83 "ayrı bir durum"):** satış ayırması bağlı bir satırın
  kalanı iptal edilirse veya sipariş komple iptal edilirse ne olur?
  - **Önerilen:** iptal, bağlı aktif ayırma varken **reddedilir**. Ekran bağlı satış siparişlerini listeler ve "bağları düşür ve
    iptal et" seçeneği sunar. Kullanıcı tek bir açık onayla (neden zorunlu) önce bağları düşürür (§3.5), sonra iptal aynı işlemde
    yapılır. Satışa "kaynak iptal edildi" uyarısı düşer.
  - **Alternatif A:** iptal yapılır, bağlı ayırmalar Stok'ta otomatik bırakılır ve satış uyarılır. Bu yolda açık onay yoktur.
  - **Alternatif B:** iptal yapılır, ayırmalar "karşılıksız" işaretlenip açık kalır; satış başka kaynak bulana kadar görünür kalır.
  - Cevaba kadar ayırması olan satırda kalan iptali ve sipariş iptali reddedilir.
- **Stok tarafı açık kalanlar:** az/fazla gelme, blokta gelen etiket, süre ve izin soruları #1116 §9'da; KLN-D26 (6) ile tutarlıdır.

## 6. Kabul ölçütleri (F-017 testi)

- Aynı satınalma satırı miktarı iki satış siparişine verilemez. İki eşzamanlı ayırma isteğinden toplamı aşan ikincisi reddedilir.
- Stok ayırması ile Alış kabul/kalan iptali aynı satırda eşzamanlı olduğunda sonuç her zaman ayrılmış ≤ ayrılabilir (§3.4).
- Draft ve Cancelled siparişin satırı ayrılamaz. Teknik dilim satırına doğrudan ayırma yapılamaz. Başka şirketin satırı ve
  bağlamda şirket yokken okuma `NotFound`'dur.
- Ürün kartında katsayı yoksa kg/ton girdisi satır birimine çevrilemez ve ayırma reddedilir (tahmin yok).
- Mal kabulünde ayrılan miktar fiziksel tahsise döner ve etiket satış sipariş satırına atanır; aynı kabul isteğinin tekrarı ikinci
  çevirme yapmaz.
- Kısmi kabulde varsayılan sıra termini en yakın satış; kullanıcının verdiği `AllocationOrder` varsayılanı geçer; geçersiz kimlik
  422 ve yazma yok (ALS-D84).
- Satınalma tarafı bağı düşürünce ayırma Stok'ta `Released` olur, satınalma committed/accepted/cancelled değişmez, ayrılabilir miktar
  aynı kadar artar ve satışa uyarı olayı yazılır (ALS-D83).
- Ayırması olan satırın kalan iptali ve sipariş iptali S4 cevabına kadar reddedilir.
- Ayrılabilir miktar her zaman Alış'ın kalanıdır; tüketici yeniden hesaplamaz.
