# F-017 — Gelecek tedarikten satış siparişine ayırma: spec ve dilim planı v1

Tarih: 2026-11-12 · Hazırlayan: Claude (cloud oturumu, Alış şeridi) · Durum: **ÖNERİ** (plan; iş kararı değildir)
Karar: `docs/decisions/SINAN_KARARLARI_2026-11-12_ALIS_EK17.md` ALS-D82 (#66 6819147253 madde 2); ALS-D30 (EK3); ALS-D22/D25.
Bağlam: `docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md` MOD-14, MOD-16, A3-0; Fable analizi F-017
(`docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md`).

Bu belge yalnız plan ve sözleşme taslağıdır. Kod yoktur. Stok tarafındaki model Stok şeridinin işidir. Buradaki Stok
maddeleri **öneri**dir; Stok şeridi kendi kapılarıyla değiştirebilir. Arayüz adları öneridir.

## 1. Bugün (main `58b96aa2`)

| Parça | Durum | Kanıt |
|---|---|---|
| Onaylı satırın kalan taahhüdü (tek satır) | Var: `IPurchaseSupplyReferencePortV1.GetApprovedLineAsync` (committed − accepted, gelecek arz; Draft/Cancelled = NotApproved) | `…Purchasing.Application/Contracts/IPurchaseSupplyReferencePortV1.cs` |
| Onaylı açık satır listesi (operasyon listesi) | Var: `IPurchaseOpenLineReadPortV1` (şirket geneli, sayfalı; ürün süzgeci yok) | `IPurchaseOpenLineReadPortV1.cs` |
| Satış siparişi için alınan satırlar | Var: `IPurchaseSalesLinkedSupplyReadPortV1` (talep satırı kaynağı = satış siparişi; iptal önizlemesi için) | `IPurchaseSalesLinkedSupplyReadPortV1.cs` |
| Mal kabulünde Stok'a giden kayıt | Var: aynı host işleminde `InventoryGoodsReceiptRequestV1` (satınalma siparişi + satır kimliği, etiket, lot) | `…Inventory.Application/InventoryGoodsReceiptContractsV1.cs` |
| Fiziksel stoktan tahsis | Var: `DemandKind = SalesOrderLine` | `…Inventory.Infrastructure/Persistence/InventoryStore.Allocations.cs` |
| Gelecek tedarikten ayırma | **Yok** | F-017 |

## 2. Kavramlar

- **Ayrılabilir miktar (Alış'ın söylediği):** onaylı satırın kalanı = committed − accepted − cancelled. Teknik dilimler genel satırın
  içindedir; iki kez sayılmaz (mevcut kural). Draft ve Cancelled siparişin satırı ayrılamaz.
- **Ayrılmış miktar (Stok'un tuttuğu):** bir satınalma satırına bağlı, henüz kabul edilmemiş satış ayırmalarının toplamı.
- **Değişmez (ALS-D82):** bir satınalma satırı için ayrılmış ≤ ayrılabilir. Aynı miktar iki satış siparişine verilemez. Değişmezin
  sahibi Stok'tur. Alış ayrılmış miktarı hesaplamaz ve saklamaz.
- **Satış talebi bağlantısı (Alış'ın bildiği):** satırın talep satırı bir satış siparişinden geldiyse (`SourceKind = Sales`), satır
  o siparişin niyetini taşır. Bu bir **niyet**tir, ayırma değildir. Ayırma kaydı her zaman Stok'ta açıkça kurulur. Alış, satış
  kaynaklı bir satır onaylanınca kendiliğinden ayırma oluşturmaz (okuma, geri alınabilir).

## 3. Port sözleşmesi taslağı (Alış ↔ Stok ortak yazar)

### 3.1 Alış → Stok: `IPurchaseFutureSupplyReadPortV1` (Alış sahibi; A3-0'ın yerine geçer)

A3-0'daki "`IPurchaseSupplyReferencePortV1` v2" ihtiyacını karşılar. v1 port değişmez; yeni port onu genişletir.

```csharp
public interface IPurchaseFutureSupplyReadPortV1
{
    // Stok'un ayırma kaydından hemen önce, aynı istekte okuduğu tek satır.
    Task<PurchaseFutureSupplyAnswerV1> GetAllocatableLineAsync(Guid companyId, Guid orderId, Guid lineId, CancellationToken ct = default);

    // Ayırma ekranının seçenek listesi: bir ürün için ayrılabilir satırlar, en yakın termin önce.
    Task<PurchaseFutureSupplyPageV1> ListAllocatableLinesAsync(Guid companyId, Guid productId, int pageSize, string? cursor, CancellationToken ct = default);
}

public enum PurchaseFutureSupplyOutcomeV1 { Allocatable = 1, NotAllocatable = 2, NotFound = 3 }

public sealed record PurchaseFutureSupplyAnswerV1(PurchaseFutureSupplyOutcomeV1 Outcome, PurchaseFutureSupplyLineV1? Line, string? Status);

public sealed record PurchaseFutureSupplyLineV1(
    Guid CompanyId, Guid OrderId, Guid LineId, int LineNo,
    Guid SupplierPartyId, Guid CompanyAccountId,
    Guid ProductId, string ProductVersion, Guid UnitId, string Kind,
    string CommittedText, string AcceptedText, string CancelledText,
    string AllocatableText,                       // committed − accepted − cancelled (Ö2 tam ondalık metin)
    IReadOnlyList<PurchaseFutureSupplySliceV1> Schedule, // API-30 termin dilimleri; termin yoksa boş
    Guid? SalesSourceOrderId, Guid? SalesSourceOrderLineId, // satış talebi niyeti (§2), yoksa null
    long Version);                                // sipariş sürümü: Stok kayıtta bunu saklar

public sealed record PurchaseFutureSupplySliceV1(Guid SliceId, DateOnly DeliveryDate, string QuantityText, string RemainingText);
```

Kurallar:

- Fiyat, iskonto, döviz, ödeme alanı yoktur (T28).
- Başka şirketin siparişi `NotFound`'dur. 403 dönmez.
- Okuma hiçbir şey oluşturmaz. Port yetki taşımaz; tüketici kendi yetkisini ölçer (LOG-4 D6).
- Birim Settings birim kimliğidir. kg/ton çevrimi Stok/Settings çevrimiyle yapılır; Alış metni satırın kendi birimindedir.
- Dilim `RemainingText`: kalan miktarın dilimlere dilim sırasıyla dağılımı (EK16 §2 okuması). Kabul belirli bir dilime bağlanınca
  (API-31/32) bu dağılım o kayıttan okunur.

### 3.2 Stok → Alış: `IInventoryFutureAllocationReadPortV1` (Stok sahibi; öneri)

ALS-D30 "Alış okuma portu ayrılan miktarı da gösterir" ve §5 S1 için. Alış Stok'a referans vermez; host adaptörü çevirir
(mevcut `PurchasingSalesLinkedSupplyHostPorts` deseni).

```csharp
// Bir satınalma satırına bağlı, henüz kabul edilmemiş ayırmaların toplamı (satırın birimiyle, tam ondalık metin).
Task<FutureAllocationSumV1> GetAllocatedOnPurchaseLineAsync(Guid companyId, Guid orderId, Guid lineId, CancellationToken ct = default);
// Present(text) / None / Unreadable. Unreadable hiçbir zaman "sıfır" sayılmaz (fail-closed).
```

### 3.3 Mal kabulü (aynı host işlemi)

- Alış'ın kabul payı (`IPurchaseAcceptanceParticipantV1.StageAsync`) ve Stok'un mal girişi (`InventoryGoodsReceiptRequestV1`)
  zaten aynı host işlemindedir ve ikisi de satınalma satırı kimliğini taşır. Yeni bir Alış alanı gerekmez.
- Stok aynı işlemde o satırın ayırmalarını kabul edilen miktar kadar fiziksel tahsise çevirir ve etiketi satış sipariş satırına
  atar (ALS-D82 "etiket ataması o zaman yapılır"). İşlem tek başarı ya da tek geri alma olur.
- **Alış tarafı:** kabul sonucu (`PurchaseAcceptanceStageResultV1`) satırın kabul sonrası ayrılabilir kalanını zaten döndürür
  (`RemainingQuantity`). Satış talebi niyeti (§2) gerektiğinde `GetAllocatableLineAsync` ile okunur.

## 4. Dilim planı

Sıra: #1101 kuyruğu bitmeden kod yok (koordinatör, 2026-11-12).

| Dilim | Şerit | İçerik | Kapı | Bağımlılık |
|---|---|---|---|---|
| F17-0 | Alış | Bu belge + EK17 (docs) | K1 | — |
| F17-1 | Alış | `IPurchaseFutureSupplyReadPortV1` + Infrastructure okuması (tek satır + ürün listesi, termin dilimleri, satış niyeti) + birim/servis testleri + gerçek SQL kabulü; A3-0'ı kapatır | K2 (migration yok; gerekirse ürün+durum indeksi K3) | #1101 (termin dilimleri) |
| F17-2 | Stok | Gelecek ayırma kaydı (`SourceKind = purchasing.order-line`), değişmez (aynı istekte F17-1 okuması + Stok'ta satınalma satırı anahtarında kilit), bırakma | K3 | F17-1 |
| F17-3 | Stok | Mal kabulünde gelecek ayırma → fiziksel tahsis + etiket ataması (aynı host işlemi) | K3 | F17-2 |
| F17-4 | Alış | Satır azaltma/iptalinde ayrılmış miktar kontrolü (§3.2 portu, host adaptörü) — §5 S1 cevabına göre | K2/K3 | F17-2, §5 S1 |
| F17-5 | Satış/UI | Satış kaynak planında "alıştan" seçimi (MOD-16) | Satış şeridi | F17-2 |

## 5. NEEDS-SINAN (cevaba kadar ilgili davranış fail-closed)

- **S1 — Ayrılmış miktarın altına inen satır azaltma/iptal:** satınalma satırı azaltılırsa veya kalan iptal edilirse ve yeni kalan
  ayrılmış miktarın altına düşerse ne olur? Önerilen: **Alış işlemi reddeder**; önce Stok'ta ayırma azaltılır/bırakılır.
  Alternatif: işlem yapılır, açıkta kalan ayırma Stok'ta "karşılıksız" işaretlenir ve satışa uyarı düşer. Cevaba kadar F17-4 açılmaz
  ve F17-2 ayırma kaydı, satır azaltma/iptal yolları korunana kadar etkinleştirilmez.
- **S2 — Kısmi kabulde ayırmaların sırası:** satıra birden çok satış ayırması varken kısmi kabul gelirse, kabul edilen miktar hangi
  ayırmaya önce gider? Önerilen: **satış siparişinin termini en yakın olan önce**, eşitse ayırma zamanı önce; depocu kabul ekranında
  değiştirebilir. Alternatif: ayırma zamanı sırası (ilk ayıran ilk alır).

## 6. Kabul ölçütleri (F-017 testi)

- Aynı satınalma satırı miktarı iki satış siparişine verilemez. İki eşzamanlı ayırma isteğinden toplamı aşan ikincisi reddedilir.
- Draft ve Cancelled siparişin satırı ayrılamaz. Başka şirketin satırı `NotFound`'dur.
- Mal kabulünde ayrılan miktar fiziksel tahsise döner ve etiket satış sipariş satırına atanır; kabul geri alınırsa (kabul reddi/iade)
  tahsis ve etiket ataması da geri döner.
- Ayrılabilir miktar her zaman Alış'ın kalanıdır; tüketici yeniden hesaplamaz.
