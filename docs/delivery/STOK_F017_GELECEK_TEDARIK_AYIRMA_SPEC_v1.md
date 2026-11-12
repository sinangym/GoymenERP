# F-017 — Gelecek tedarikten ayırma: Stok tarafı spec ve dilim planı v1

Tarih: 2026-11-12 · Hazırlayan: Claude (Stok / Kalan Modüller şeridi) · Durum: **TASLAK** (plan; iş kararı değildir)
Main: `58b96aa28`. Bu belge yalnız dokümandır: kod, migration, OpenAPI, manifest, izin veya grant değiştirmez.

Revizyon 2 (2026-11-12): §9 soruları Sinan #66 5. tur (madde 3–4) ve 11.–13. tur (yorum 6820601088, madde 1, 3, 6–9)
cevaplarıyla kapandı; karar metni KLN-D26 (6) (PR #1108) ve Alış tarafında ALS-D83…ALS-D90 (PR #1110). §3.5, §4.3, §6, §8 ve
§10 buna göre güncellendi. S6 (izin adı) ve S9 (genel/teknik satır) teknik tercihtir, gerekçesi §9'dadır; S10 SAT-D298 ile kilitlidir.

**Kaynaklar (öncelik sırasıyla):**

- **KLN-D26** — `docs/decisions/SINAN_KARARLARI_2026-11-12_STOK_TRANSFER_IPTAL_VE_GELECEK_TEDARIK.md` (PR #1108 dalı;
  merge bekliyor). Sinan, #66 yorum 6819147253 madde 2: "Açık satınalma siparişi satırından satış siparişine **miktar
  bazında (kg/ton)** ayırma yapılabilir; aynı miktar iki siparişe verilemez. Mal kabulde ilgili satış talebine bağlanır,
  etiket ataması o zaman yapılır."
- **ALS-D82** — Alış şeridinin aynı kayda verdiği numara (`SINAN_KARARLARI_2026-11-12_ALIS_EK17.md`, PR #1110) ve
  Alış spec'i `docs/delivery/ALIS_F017_GELECEK_TEDARIK_AYIRMA_v1.md` (PR #1110). Port sözleşmesi o belgenin §3'ündedir;
  bu belge onu kopyalamaz, atıf yapar ve Stok tarafını hizalar (aşağıda §5).
- `KARAR_SICILI`: DEC-STK-002 (gelecek arz serbest stoğa karışmaz), DEC-STK-003 (tahsis alış satırından da yapılır;
  fiziksel hareket değildir), DEC-STK-004 (atomik, idempotent, serbest miktar aşılmaz; DB kısıtı + concurrency testi
  zorunlu), DEC-STK-006 (etki türleri ayrı; sahip, neden, kaynak, süre ayrı saklanır), DEC-SAL-004 (satır karma kaynakla
  karşılanabilir; plandaki stok kısmı tahsisten türetilir, ikinci ayırma yapısı yoktur).
- ALS-D30 (`SINAN_KARARLARI_2026-11-02_ALIS_EK3.md`): "aynı ton ikinci kez bağlanamaz; mal gelince fiziksel tahsise döner".
- SAT-D298 (`SINAN_KARARLARI_2026-11-11_SATIS_EK59_ORTALAMA_MALIYET.md`): birim çevrim kaynağı ürün kartındaki katsayıdır;
  katsayı yoksa `NoSource`, tahmini katsayı yok; ekran yeniden hesaplamaz.
- Fable analizi F-017 satırı (`docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md:409`) ve
  `docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md` B-20 (`:261`) / B-23 (`:264`).

## 1. Kapsam

**İçinde (Stok):**

1. Onaylı satış sipariş satırı için, onaylı ve açık bir satınalma sipariş satırından **miktar bazında gelecek ayırma**
   oluşturma ve bırakma (iptal).
2. "Aynı miktar iki siparişe verilemez" değişmezi: bir satınalma satırına bağlı aktif gelecek ayırmaların toplamı, Alış'ın
   o satır için verdiği ayrılabilir miktarı aşamaz. DB kısıtı + concurrency ile.
3. Mal kabulde, aynı host işleminde, kabul edilen miktar kadar gelecek ayırmayı **fiziksel tahsise çevirme** ve etiketi
   satış sipariş satırına **o anda atama** (KLN-D26 (4)).
4. Stok'un Alış'a "bu satırda ne kadar ayrılmış" bilgisini veren okuma portu (PR #1110 §3.2).

**Dışında:**

- Alış'ın ayrılabilir miktar portu ve Alış tarafı davranışı (Alış şeridi, PR #1110 §3.1, F17-1).
- Satış kaynak planında "alıştan" seçimi (MOD-16, Satış şeridi; PR #1110 F17-5).
- Üretim planı ve "ürün seviyesinde gelecek arz" kaynakları (DEC-STK-003'ün diğer kaynakları; ayrı iş).
- Satınalma tarafının bağ düşürme ve "bağları düşür ve iptal et" komutları (Alış şeridi, PR #1110 §3.5, ALS-D83/ALS-D85).
  Stok bunlar için bırakma portunu sağlar (§5).
- Uyarıların bildirim kanalı (e-posta/anlık bildirim); bu belge yalnız olay ve okuma alanını tanımlar.

## 2. Bugünkü durum (kod atıflı)

| Konu | Durum | Kanıt |
|---|---|---|
| Tahsis talep türleri | Yalnız `sales.order-line` ve `manufacturing.plan-input` | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventoryAllocationContractsV1.cs:10-19` |
| Tahsis domain modeli | Tek bir **fiziksel** etiket + pozisyon üzerinde; `Create` yalnız pozisyonla çalışır | `…Inventory.Domain/Allocations/StockAllocation.cs:23-66` |
| Serbest miktar | `Unreserved = serbest fiziki − aktif tahsis` | `…Inventory.Domain/Allocations/StockAllocation.cs:76-77` |
| Tahsis satırı | `LabelId`, `LocationId`, `OwnerId`, `CustodianId` zorunlu; gelecek kaynak için yer yok | `…Inventory.Infrastructure/Persistence/InventoryRows.cs:191-221` |
| DB kısıtları | `CK_Allocations_Identity` etiket/konum boş olamaz; `CK_Allocations_State` `[Block] = 1` (Free); `CK_Allocations_Demand` yalnız iki tür | `…Inventory.Infrastructure/Persistence/InventoryDbContext.cs:442-481` |
| Fiziksel tahsis oluşturma | Etiket sürümü beklenir (`:55-58`), tek Free satır aranır (`:65-72`), aktif tahsis düşülüp serbest miktar kontrol edilir (`:83-93`), etiket sürümü artırılarak aynı etikete eşzamanlı tahsis sıralanır (`:129`), komut + audit aynı save'de (`:132-136`), unique/concurrency hatasında replay veya 409 (`:142-153`) | `…Inventory.Infrastructure/Persistence/InventoryStore.Allocations.cs` |
| Eşzamanlılık çapası | Etiketin `Version` alanı concurrency token, ayrıca `RowVersion` | `…InventoryDbContext.cs:206-207` |
| Host doğrulaması | Satış satırı `ISalesOrderLineReferencePortV1` ile onaylı mı okunur; izin önce ve sonra ölçülür | `src/backend/GoymenERP.Api/Endpoints/InventoryPhysicalAllocationHostPorts.cs:47-51`, `:137-171`, `:186-223` |
| Mevcut izin | `inventory.physical-allocation.write` | `…Inventory.Application/InventoryOpeningContractsV1.cs:17` |
| HTTP | `POST /physical-allocations`, `/release`, `/change` | `src/backend/GoymenERP.Api/Endpoints/InventoryEndpoints.cs:1115`, `:1196`, `:1279`, `:1695` |
| Mal kabul | Alış kabul payı + Stok girişi **aynı host işleminde**; Stok tarafı lot, etiket, giriş hareketi, bakiye, komut, audit yazar | `src/backend/GoymenERP.Api/PurchasingRuntime/PurchaseGoodsReceiptHost.cs:32-43`; `…Inventory.Application/InventoryGoodsReceiptContractsV1.cs:14-31`, `:62-85`; `…Inventory.Infrastructure/Persistence/InventoryGoodsReceiptParticipant.cs:92`, `:144-149` |
| Kabul etiketi | Bir kabul = bir etiket; ilk blok bugün sabit `"Free"` | `…Purchasing.Application/Contracts/PurchaseGoodsReceiptContractsV1.cs:45` |
| Outbox | Stok'un kendi outbox tablosu var | `…InventoryDbContext.cs:75`, `:599` |
| Alış tek satır portu | `IPurchaseSupplyReferencePortV1.GetApprovedLineAsync` — onaylı satırın `CommittedText`, `AcceptedText`, `RemainingText`, birim kimliği, sipariş `Version`; şirket bağlamdan okunur, başka şirket `NotFound` | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Application/Contracts/IPurchaseSupplyReferencePortV1.cs:14-17`, `:39-55`; uygulama `…Purchasing.Infrastructure/Persistence/PurchaseSupplyReferencePort.cs:22-66`; kayıt `…Purchasing.Infrastructure/PurchasingModuleExtensions.cs:58` |
| Portun tüketicisi | **Yok** (Purchasing dışında referans yok) | `git grep IPurchaseSupplyReferencePortV1 -- src` |
| Açık satır listesi | `IPurchaseOpenLineReadPortV1` — şirket geneli, sayfalı, ürün süzgeci yok | `…Purchasing.Application/Contracts/IPurchaseOpenLineReadPortV1.cs:18-21` |
| Satınalma satırı miktarları | `Committed = Quantity − Technical`, `Remaining = Committed − Accepted − Cancelled`; teknik dilim genel satırdan oyulur | `…Purchasing.Domain/Orders/PurchaseOrderLine.cs:70`, `:76`, `:78-112` |
| Fazla kabul | Alış kabul payı kalanı aşamaz (`AcceptanceExceedsRemaining`) | `…Purchasing.Domain/Orders/PurchaseOrder.cs:337-341` |
| Sipariş durumları | `Draft`, `Approved`, `Cancelled` (kapalı/“Closed” durumu yok) | `…Purchasing.Domain/Orders/PurchaseOrder.cs:6-16` |

Özet: gelecek tedarikten ayırma **yok**; tahsis yalnız mal geldikten sonra fiziksel etiket üzerinde yapılabiliyor (F-017).

## 3. Hedef davranış

### 3.1 Kavramlar

- **Ayrılabilir miktar (Alış'ın):** onaylı satınalma satırının kalanı (`Committed − Accepted − Cancelled`). Stok bunu
  hesaplamaz, yalnız Alış portundan okur (PR #1110 §2, §3.1).
- **Ayrılmış miktar (Stok'un):** bir satınalma satırına bağlı aktif gelecek ayırmaların kalan toplamı.
- **Değişmez:** her `(CompanyId, PurchaseOrderId, PurchaseOrderLineId)` için `ayrılmış ≤ ayrılabilir`. Sahibi Stok'tur.
- Gelecek ayırma **fiziksel hareket değildir**; stok bakiyesi, serbest fiziki ve maliyet değişmez (DEC-STK-003,
  KLN-D26 (3)). Fiziksel stok listesinde ayrı kaynak türü olarak görünür, serbest stoğa karışmaz (DEC-STK-002).

### 3.2 Ayırma oluştur

Girdi (öneri): `AllocationId`, `SalesOrderId`, `SalesOrderLineId`, `PurchaseOrderId`, `PurchaseOrderLineId`, miktar
(kg veya ton olarak girilir), `Reason`, `ExpectedPurchaseOrderVersion` (bilgi amaçlı, §5 notu).

Akış (host + Stok yazıcısı; fiziksel tahsisteki desenle aynı):

1. İzin ölçülür (§6). Satış satırı `ISalesOrderLineReferencePortV1` ile okunur; onaylı değilse 422.
2. Alış satırı Alış portuyla okunur (PR #1110 `GetAllocatableLineAsync`). `NotFound` → 404, `NotAllocatable`
   (Draft/Cancelled) → 422. Ürün kimliği satış satırınınkiyle aynı değilse 422.
3. Miktar, merkezi birim çevrim servisiyle satınalma satırının birimine çevrilir (SAT-D298). Çevrim `NoSource` ise 422;
   ekran ve Stok kendi katsayısını üretmez.
4. Stok, aynı işlemde **satınalma satırı defter satırını** (§4.1) kilitler/günceller: ayrılabilir anlık değeri Alış'tan
   okunanla yazılır, yeni ayrılmış toplam kontrol edilir; aşarsa 409 `…insufficient_future_supply`.
5. Ayırma satırı, komut satırı (idempotency), audit ve outbox olayı **tek save**'de yazılır.

Çakışmada mevcut serbest miktara göre kısmi ayırma **önerilebilir** (DEC-STK-004); otomatik kısmi kayıt yapılmaz.

### 3.3 Ayırma bırak (iptal)

`ExpectedAllocationVersion` + `Reason` ile. Aktif değilse 409. Bırakma silme değildir: satır `Released` olur, defter
toplamı düşer, audit + outbox yazılır. Sessiz bırakma yoktur (DEC-STK-005).

### 3.4 Miktar değiştirme

Fiziksel tahsisteki `Resize` gibi bir değişiklik komutu bu ilk dilimde **önerilmez**; bırak + yeniden oluştur yeterlidir.
Gerekirse ayrı dilim (geri alınabilir teknik tercih).

### 3.5 Mal kabulde talebe bağlama ve etiket ataması

- Mal kabul zaten tek host işlemidir (`PurchaseGoodsReceiptHost.cs:32-43`). Stok katılımcısı aynı işlemde, kabul edilen
  satınalma satırına bağlı aktif gelecek ayırmaları bulur.
- Kabul edilen miktar kadar gelecek ayırma **tüketilir** (`Consumed`) ve yeni etiket üzerinde, aynı satış sipariş satırına
  **fiziksel tahsis** (`sales.order-line`) oluşturulur. Etiket ataması budur (KLN-D26 (4), ALS-D30 "fiziksel tahsise
  döner").
- Gelecek ayırma ile oluşan fiziksel tahsis birbirine kimlikle bağlanır (izlenebilirlik).
- **Sıra (KLN-D26 (6)(b), ALS-D84):** bir kabulde birden çok aktif ayırma varsa kabul edilen miktar, satış sipariş satırı
  termini en yakın olandan başlayarak dağıtılır; eşit terminde ilk oluşturulan önce. Kullanıcı sırayı ayırma üzerinde
  elle değiştirebilir (`ManualSequence`, boşsa termin sırası) veya kabul anında ayırma seçebilir; seçim Alış kabul payından
  gelir (PR #1110 §3.3). Alan adı **yazarın önerisi, geri alınabilir**.
- **Az gelirse (KLN-D26 (6)(c)):** gelen miktar sırayla çevrilir; çevrilemeyen kalan ayırmada `Active` kalır
  (`RemainingQuantity` düşer). Satınalma satırının gelecek kısmı kalmazsa (kalan sıfır, satır iptal) kalan ayırma bırakılır
  (`Released`, neden `purchase-line-closed`) ve `inventory.future-allocation.released.v1` olayı satış uyarısını taşır.
- **Fazla gelirse:** ayırmaları aşan kısım etikette serbest kalır, ek tahsis yapılmaz. Alış'ın kalanı aşan kabul reddi
  (`PurchaseOrder.cs:337-341`) değişmez; bu belge onu gevşetmez.
- **Bağlama başarısızsa (KLN-D26 (6)(g)):** iş nedenli bağlama hatası (etiket bloklu, birim çevrimi `NoSource`, satış
  satırı artık onaylı değil) mal kabulü durdurmaz: kabul ve etiket yazılır, ilgili ayırma aynı işlemde bırakılır (neden
  kodu ile) ve satış uyarılır. Bloklu etiket (B-18) de bu kurala girer; blok kalkınca otomatik yeniden bağlama yapılmaz.
  Teknik hata (DB hatası, eşzamanlılık çakışması) tek host işlemini bütünüyle geri alır; bu bir politika değil
  atomikliktir (**yazarın okuması, geri alınabilir**).
- **Kabul geri alma / iade (KLN-D26 (6)(e), ALS-D86):** fiziksel tahsis ve etiket ataması geri alınır. Satınalma satırında
  hâlâ gelecek miktar varsa tüketilen ayırma aynı satırda yeniden `Active` olur (kalan geri eklenir, sürüm artar, audit);
  gelecek miktar yoksa ayırma bırakılır ve satış uyarılır. Kabul geri alma komutunun kendisi Alış/Stok mevcut akışına
  bağlıdır; F017-c yalnız ayırma tarafını yazar.
- Hepsi aynı host işlemindedir; tek başarı veya tek geri alma.

## 4. Veri modeli (öneri, F017-b)

### 4.1 Tablolar

Mevcut `inventory.Allocations` tablosu fiziksel etikete bağlıdır (`CK_Allocations_Identity`, `[Block] = 1`). Onu
gevşetmek fiziksel tahsis kısıtlarını zayıflatır. Öneri: **ayrı tablolar** (geri alınabilir teknik tercih):

- `inventory.FutureSupplyAllocations` — bir satır = bir gelecek ayırma.
  `Id`, `CompanyId`, `SourceKind` (`purchasing.order-line`), `PurchaseOrderId`, `PurchaseOrderLineId`, `PurchaseUnitId`,
  `ProductId`, `ProductVersion`, `DemandKind` (`sales.order-line`), `DemandId`, `DemandLineId`, `AllocatedQuantity`,
  `RemainingQuantity` (satınalma satırı biriminde, tam ondalık), `ManualSequence` (boş olabilir, §3.5), `Reason`,
  `Status` (Active/Consumed/Released; kabul geri almada Consumed → Active, §3.5),
  `Version`, `RowVersion`, `ObservedPurchaseOrderVersion`, oluşturma/güncelleme/bırakma zamanları ve nedeni.
- `inventory.FutureSupplyLines` — satınalma satırı başına **tek** defter satırı; eşzamanlılık çapası.
  `CompanyId`, `PurchaseOrderId`, `PurchaseOrderLineId`, `PurchaseUnitId`, `AllocatableSnapshot`, `ActiveAllocated`,
  `Version` (concurrency token), `RowVersion`.
- `inventory.FutureSupplyConversions` (veya fiziksel tahsis satırında nullable kaynak kolonu) — mal kabulde tüketilen
  gelecek ayırma ↔ oluşan fiziksel tahsis bağı. Seçim F017-c PR'ınındır.

### 4.2 DB kısıtları

- `UNIQUE (CompanyId, PurchaseOrderId, PurchaseOrderLineId)` — defter satırı tektir.
- `CHECK (ActiveAllocated >= 0 AND ActiveAllocated <= AllocatableSnapshot)` — **"aynı miktar iki kez" DB kısıtı**.
- Ayırma satırında: `AllocatedQuantity > 0`, `0 <= RemainingQuantity <= AllocatedQuantity`, durum ile kalan tutarlılığı
  (`StockAllocation` kuralının aynısı, `StockAllocation.cs:28-31`), boş GUID yok, `Reason` boş değil, `SourceKind` ve
  `DemandKind` sabit listede.
- Şirket bileşik anahtarı (`CompanyId, Id`) ve tenant filtresi.
- Migration geri alınabilir: `Down`, veri varken `THROW` ile reddeder (forward-fix deseni).

### 4.3 Eşzamanlılık

- Her oluştur/bırak/çevir, defter satırının `Version` alanını artırır. İki eşzamanlı ayırmadan biri
  `DbUpdateConcurrencyException` alır → replay değilse 409 `inventory_concurrent_change` (fiziksel tahsisteki
  `InventoryStore.Allocations.cs:142-153` deseni).
- Defter satırı ilk ayırmada oluşturulur; iki eşzamanlı ilk ayırma unique ihlaliyle (2601/2627) sıralanır.
- **Modüller arası yarış:** Alış satırın kalanını (kabul, iptal, ileride azaltma) Stok'tan bağımsız değiştirebilir.
  Stok kendi işleminde `AllocatableSnapshot`'ı tazeler; ama Alış tarafında kalan, ayrılmışın altına inebilir. Bu
  durumun teknik koruması (ör. iki modülün aynı host işleminde `(şirket, satınalma satırı)` anahtarlı uygulama kilidi
  alması; PR #1110 §3.4 `IPurchaseLineSerializationLockV1`) F017-a'da Alış şeridiyle birlikte yazılır. İş davranışı
  cevaplıdır: satınalma tarafı bağı açıkça düşürür, satınalma miktarı bağ yüzünden azalmaz (ALS-D83); ayırması olan
  satır/sipariş doğrudan iptal edilmez, önce bağlı satışlar gösterilir, "bağları düşür ve iptal et" gerekçeli tek onayla
  aynı işlemde yapılır (KLN-D26 (6)(d), ALS-D85). Buna rağmen okumada `ayrılmış > ayrılabilir` görülürse Stok yeni
  ayırmayı reddeder ve satırı "karşılıksız" diye raporlar; kendiliğinden bırakmaz (DEC-STK-005).

## 5. Modül sınırı ve port sözleşmesi

- **Alış → Stok:** ayrılabilir açık miktar Alış'ın sürümlü portundan gelir. Sözleşme **PR #1110 §3.1
  `IPurchaseFutureSupplyReadPortV1`**'dir (Alış sahibi; Alış ve Stok ortak yazar). Bu belge alanları kopyalamaz.
  Stok tarafının ihtiyaç listesi:
  - tek satır okuma: şirket, sipariş, satır, ürün + sürüm, birim kimliği, satır türü, ayrılabilir tam ondalık metin,
    sonuç (`Allocatable` / `NotAllocatable` / `NotFound`), sipariş sürümü;
  - ürün bazlı sayfalı liste (ekran seçenekleri);
  - fiyat/döviz alanı yok (T28); başka şirket `NotFound`, 403 değil; port yetki taşımaz, tüketici kendi iznini ölçer.
- **Stok → Alış:** PR #1110 §3.2 `IInventoryFutureAllocationReadPortV1` (Stok sahibi). Bir satınalma satırının aktif
  ayrılmış toplamını satınalma satırı biriminde döner: `Present(text)` / `None` / `Unreadable`; `Unreadable` asla sıfır
  sayılmaz. Stok bu portu F017-b'de yazar.
- **Yazma yok:** Stok Alış tablosuna yazmaz, Alış Stok tablosuna yazmaz. Modüller birbirine referans vermez; host
  adaptörü çevirir (mevcut `InventoryPhysicalAllocationHostPorts.cs` deseni).
- **Stok bırakma portu (Stok sahibi):** `IInventoryFutureAllocationReleasePortV1` (PR #1110 §3.5 önerisi); Alış'ın bağ
  düşürme ve "bağları düşür ve iptal et" komutları bunu aynı host işleminde `origin = purchasing` ve zorunlu nedenle çağırır.
  Bırakma §3.3 akışının aynısıdır (sürüm, audit, outbox). Stok bu portu F017-b'de yazar.
- **Termin:** Alış portu satır termini döner (PR #1110 termin okuması); Stok onu §3.5 sırası ve §9 S11 uyarısı için okur,
  saklamaz.
- Mal kabul tarafında **yeni Alış alanı gerekmez**: `InventoryGoodsReceiptRequestV1` zaten satınalma sipariş ve satır
  kimliğini taşır (`InventoryGoodsReceiptContractsV1.cs:62-66`).
- Sürüm: v1 portlar değişmez; yeni port ayrı sürümlü arayüzdür. Contract testleri iki tarafta da (§8).
- PR #1110 §3 ile hizalama notları bu belgenin konusu değildir; şerit raporunda "#1110 §3 review notları" olarak
  ayrıca verilir (ör. birim, `companyId` parametresi, teknik/genel satır, kapı seviyesi).

## 6. CompanyId, izin, audit, idempotency, outbox

- **CompanyId:** her tabloda zorunlu; şirket sunucu bağlamından gelir, istemciden alınmaz. Tenant filtresi + bileşik
  anahtar. Başka şirketin satış/satınalma satırı 404 (varlık sızdırılmaz).
- **İzin adı (yalnız ad önerisi):** `inventory.future-allocation.write` (oluştur/bırak) ve okuma için
  `inventory.future-allocation.read`. Ad kaydı ayrı PR'da yapılır; **grant/rol/Critical insan kapısıdır**, bu iş açmaz.
  Ayırmayı izinli tek kişi yapar, ikinci onay yoktur (KLN-D26 (6)(f)); satış, satınalma veya depo kullanıcısına izin
  verilmesi grant kararıdır. Ayrı ad gerekçesi §9 S6.
- **Expected version:** bırakmada `ExpectedAllocationVersion`; defter satırında concurrency token. Alış sipariş sürümü
  yalnız gözlem olarak saklanır; Stok onu kendi kilidi saymaz.
- **Idempotency:** `Idempotency-Key` zorunlu; anahtar hash + payload hash (fiziksel tahsisteki `AllocationKeyHash` /
  `AllocationPayloadHash` deseni). Aynı anahtar + aynı yük → replay (`IsReplay`, `OriginalCorrelationId`); farklı yük →
  409 `idempotency_conflict`.
- **Audit:** her oluştur/bırak/çevir için önce/sonra görüntüsü ve neden; correlation/causation.
- **Outbox:** `inventory.future-allocation.created.v1`, `.released.v1`, `.converted.v1` (öneri). Satış ve Alış ekranları
  bunları inbox/dedup ile tüketebilir. Olay şeması sürümlü contract testine bağlanır.
- **Log/metric/trace:** ret kodları sayılır; concurrency çakışması ayrı metrik.

## 7. Hata, boş ve çakışma durumları

| Durum | HTTP / kod (öneri) |
|---|---|
| Doğrulama (miktar ≤ 0, eksik alan, neden boş) | 400 `validation_failed` |
| Satış veya satınalma satırı yok / başka şirket | 404 `inventory_future_allocation_invalid_reference` |
| Satış satırı onaylı değil; satınalma Draft/Cancelled; ürün uyuşmuyor | 422 `inventory_future_allocation_invalid_reference` |
| Birim çevrimi yok (`NoSource`) | 422 `inventory_future_allocation_unit_conversion_unavailable` |
| Ayrılabilir miktar yetersiz | 409 `inventory_future_allocation_insufficient_future_supply` (+ kısmi öneri) |
| Eşzamanlı değişiklik | 409 `inventory_concurrent_change` |
| Ayırma sürüm çakışması / aktif değil | 409 `inventory_allocation_version_conflict` / `inventory_allocation_state_conflict` |
| Idempotency çakışması | 409 `idempotency_conflict` |
| Alış portu okunamıyor | 503 (kapalı başarısızlık; asla "sıfır ayrılmış" sayılmaz) |
| Yetki yok | 401 / 403 |

Ekran (F017-d): yükleniyor, veri yok ("Bu ürün için açık satınalma satırı yok"), süzgeçle boş, yetkisiz, bulunamadı,
çakışma (güncel ayrılabilir miktarla yenile), çevrimdışı, servis yok, bayat veri durumları. Ekran miktar ve birim
çevirisini kendisi hesaplamaz; backend'in döndüğünü gösterir.

## 8. Testler

- **Unit (Domain):** gelecek ayırma yaşam döngüsü (oluştur/bırak/tüket), tutarsız kayıt reddi, kalan ≤ ayrılan,
  sürüm artışı.
- **Unit (Application/Infrastructure):** idempotent replay, farklı yükle aynı anahtar, ret kodları, `NoSource`,
  `Unreadable` portun sıfır sayılmaması.
- **Gerçek SQL (SQL Server relational shard):** check kısıtı `ActiveAllocated <= AllocatableSnapshot` ihlali DB'de
  reddedilir; unique defter satırı; migration `Down` veri varken reddeder; tenant filtresi başka şirketi göstermez.
- **Concurrency (F-017 kabul testi):** aynı satınalma satırına, toplamı ayrılabiliri aşan iki eşzamanlı ayırmadan
  biri reddedilir; ilk ayırmada eşzamanlı defter satırı oluşturma; bırak ile oluştur yarışı.
- **Mal kabul (F017-c):** kabul aynı işlemde gelecek ayırmayı tüketir, fiziksel tahsis oluşturur, etiket satış satırına
  bağlanır; replay aynı sonucu döner. Ayrıca: termin sırası ve elle sıra; az gelmede kalan açık; satır kapanınca bırakma +
  olay; fazla kısım serbest; bloklu etiket ve `NoSource`'ta kabul yazılır, ayırma bırakılır, olay yazılır; teknik hatada
  kabul, etiket ve tahsis birlikte geri alınır; kabul geri almada ayırma yeniden açılır veya bırakılır.
- **Termin uyarısı:** termini geçmiş satınalma satırına bağlı aktif ayırma okumada `PurchaseDueDatePassed` işaretiyle
  döner; ayırma bırakılmaz.
- **Contract:** Alış portu ve Stok portu için sürümlü contract testleri (alan adları, fiyat alanı yokluğu, `NotFound`);
  outbox olay şeması; OpenAPI drift.
- **Yetki:** izin yokken 403; şirket değişince 404; izin önce/sonra ölçümü.
- **Frontend (F017-d):** decoder kesin anahtar, durum blokları, a11y.

## 9. Sorular ve cevaplar

Politika soruları Sinan tarafından cevaplandı; karar metni KLN-D26 (6) (PR #1108), Alış eşleri ALS-D83…ALS-D90 (PR #1110).
Sinan'ın metinleri #66'dadır (5. tur 6819640816; 11.–13. tur 6820601088). Uygulama yeri parantezde.

- **S1 — Az gelirse:** gelen kadar ayrılır, kalan satınalma satırının gelecek kısmında açık kalır; satır kapanırsa kalan
  bırakılır ve satış uyarılır (12. tur m.6; ALS-D87; §3.5).
- **S2 — Satınalma satırı/sipariş iptali, bağ düşürme:** satınalma tarafı bağı düşürür, miktar azalmaz (5. tur m.3; ALS-D83).
  Ayırması olan satır/sipariş iptalinde önce bağlı satışlar gösterilir; "bağları düşür ve iptal et" gerekçeli tek onayla aynı
  işlemde, satışa "kaynak iptal edildi" uyarısı (11. tur m.1; ALS-D85; §4.3, §5 bırakma portu).
- **S3 — Fazla gelirse:** fazlası serbest stoğa girer (12. tur m.6; ALS-D87; §3.5).
- **S4 — Kısmi kabulde sıra:** termini en yakın satış önce; sıra elle değiştirilebilir/seçilebilir (5. tur m.4; ALS-D84; §3.5).
- **S5 — Kim yapar:** izinli tek kişi (satış, satınalma veya depo), ikinci onay yok; grant insan kapısı (12. tur m.7;
  ALS-D88; §6).
- **S6 — İzin adı (teknik, yazarın tercihi, geri alınabilir):** yeni `inventory.future-allocation.write` /
  `.read`. Gerekçe: gelecek ayırma fiziksel tahsisten ayrı etki türüdür (DEC-STK-006) ve S5 cevabı satınalma kullanıcısına
  da ayırma yetkisi verilebileceğini söyler; mevcut `inventory.physical-allocation.write` yeniden kullanılırsa satınalma
  kullanıcısı fiziksel etiket tahsisi yetkisini de alır. Ayrı ad grant'ı daraltır. Alış'ın bağ düşürme izni
  `purchasing.future-allocation.release` ayrıdır (PR #1110). Ad kaydı F017-b'de, grant insan kapısı.
- **S7 — Kabulde bağlama başarısızsa:** kabul durmaz, mal stoğa girer, ayırma bırakılır, satış uyarılır (12. tur m.8;
  ALS-D89; §3.5).
- **S8 — Bloklu etiket:** S7 ile aynı kural (12. tur m.8; ALS-D89; §3.5).
- **S9 — Genel/teknik satır (teknik, yazarın tercihi, geri alınabilir):** ayırma **genel satıra** yapılır; teknik dilime
  doğrudan ayırma yoktur (port `technical-line` → 422). Kabul teknik dilimden gelirse Alış kabul payı bağlı genel satır
  kimliğini bildirir ve Stok çevirmeyi genel satırdaki ayırmalar üzerinden yapar. Gerekçe: ayrılabilir miktar
  (`Committed − Accepted − Cancelled`) genel satır düzeyindedir (`PurchaseOrderLine.cs:70-78`); PR #1110 §2 aynı kuralı yazar.
- **S10 — Birim:** SAT-D298 ile kilitli; katsayı yoksa `NoSource`, ayırma yapılmaz (§3.2 adım 3).
- **S11 — Süre:** ayırmanın son tarihi yoktur (`ReservedUntil` alanı açılmaz); satınalma termini geçerse satış ve satınalma
  kullanıcısı uyarılır, ayırma açık kalır (13. tur m.9; ALS-D90; §8 termin uyarısı). Uyarı okuma anında hesaplanır; ayrı
  zamanlanmış iş bu belgede yoktur.
- **S12 — Kabul geri alma/iade:** tahsis ve etiket ataması geri alınır; gelecek miktar varsa ayırma yeniden açılır, yoksa
  bırakılır ve satış uyarılır (11. tur m.3; ALS-D86; §3.5).

## 10. Dilim planı

Sıra: koordinatörün #1101 kuyruğu bitmeden kod yok. PR #1110 §4 ile aynı kimlik eşlemesi: F017-a ≈ F17-1 (+ ortak
sözleşme), F017-b ≈ F17-2, F017-c ≈ F17-3, F017-d ≈ F17-5'in Stok ekranı.

| Dilim | Şerit | İçerik | Dosya alanı | K | Bağımlılık |
|---|---|---|---|---|---|
| F017-0 | Stok (docs) | Bu belge | `docs/delivery/STOK_F017_…` | K1 | KLN-D26 merge (#1108) |
| F017-a | Alış + Stok | Port sözleşmesi: Alış `IPurchaseFutureSupplyReadPortV1` (Alış yazar) + Stok'un `IInventoryFutureAllocationReadPortV1` arayüzü; ortak contract testi; modüller arası yarış koruması tasarımı | `src/backend/Modules/Purchasing/**/Contracts/`, `src/backend/Modules/Inventory/**/Application/`, `tests/GoymenERP.*` contract | **K3** (cross-module contract) | PR #1110 merge |
| F017-b | Stok (Codex backend) | Gelecek ayırma domain + `FutureSupplyAllocations` / `FutureSupplyLines` + migration + check/unique kısıtları; oluştur/bırak yazıcısı, host servisi, endpoint, OpenAPI; Stok okuma portu; outbox; izin adı kaydı (grant yok) | `src/backend/Modules/Inventory/**`, `src/backend/GoymenERP.Api/Endpoints/Inventory*`, `contracts/openapi/`, recovery/relational envanterleri, `tests/GoymenERP.*` | **K3** (migration + kısıt + izin) | F017-a; bırakma portu (§5) F017-b'de; production aktivasyonu ve grant insan kapısı |
| F017-c | Stok (Codex backend) | Mal kabulde aynı host işleminde gelecek ayırmayı tüketme + fiziksel tahsis + bağ satırı; replay; geri alma testleri | `…Inventory.Infrastructure/Persistence/InventoryGoodsReceiptParticipant.cs`, `src/backend/GoymenERP.Api/PurchasingRuntime/`, migration (bağ tablosu) | **K3** (migration + cross-module işlem) | F017-b; kabul geri alma akışı (S12) |
| F017-d | Claude (frontend) | Satış satırı ekranından "açık satınalmadan ayır" ve ayırma listesi/bırakma; kabul ekranında ayırma bilgisi; generated client tüketimi; durum blokları | `src/frontend/goymen-erp-web/**` (Stok/Satış klasörleri) | K2 | F017-b OpenAPI; izin capability kaydı |

Her K3 dilimde: iki bağımsız exact-head review, gerçek SQL kanıtı, fresh CI, merge öncesi main tazelik kontrolü
(AGENTS "Merge öncesi main tazelik kontrolü"). Production aktivasyonu, grant ve `OPEN-*` kapatma insan kapısıdır.

## 11. Sınırlar

- Bu belge karar değildir; `KARAR_SICILI`, `ACIK_KARARLAR` ve karar dosyaları değişmez.
- "Öneri" ve "yazarın önerisi, geri alınabilir" diye işaretli teknik noktalar (ayrı tablo, izin adı, olay adları, hata
  kodları) review'da değişebilir.
- Para, stok veya maliyet politikası seçilmez; §9 politika cevapları Sinan'ındır (KLN-D26 (6)). Yeni politika sorusu çıkarsa
  Issue #66'ya `NEEDS-SINAN` olarak yazılır.
