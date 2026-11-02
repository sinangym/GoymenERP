# Üretim → Satış görev notu v1

Tarih: 2026-11-02 · Yazan: Üretim şeridi (Claude) · Alıcı: "proje modülleri" konuşması (Satış şeridi)
Karar: `docs/decisions/SINAN_KARARLARI_2026-11-02_URETIM_EK1.md` URT-D19 — Satış dosyalarını yalnız Satış şeridi
değiştirir. Bu not iş tanımıdır; Satış'ın kendi karar, review ve merge kapıları geçerlidir.

Üretim tarafındaki karşılıklar Üretim şeridince ayrı PR'larla yazılır ve aşağıda "Üretim tarafı" olarak işaretlidir.
Arayüz adları öneridir; Satış şeridi kendi adlandırmasını seçebilir, Üretim tarafı ona uyar.

## S-1 · Sipariş iptali ve zorla kapatmada "bağlı üretim" (Satış planı MOD-05)

- **Bugün:** `SalesCancellationImpactSourcesV1.LinkedProduction = "manufacturing.linked-orders"` kategorisi
  `SalesDraftService.Cancellation.cs` ve `SalesDraftService.ForceClosure.cs` içinde `_ => NoSource` dalına düşüyor;
  engellemiyor. `LinkedPurchasing` da aynı.
- **İstenen (Satış):** Satış'ta bir okuma portu (öneri: `ISalesOrderLinkedSupplyReadPortV1`) ve iki servisteki
  `LinkedProduction` dalının bu porttan beslenmesi. Kurallar No 30 / SAT-D12 ile aynı: açık bağlı üretim veya
  okunamayan kaynak → red; port yoksa bugünkü `NoSource` davranışı korunur.
- **Üretim tarafı:** `IManufacturingSalesOrderLinkReadPortV1` — şirket + sipariş kimliği (+ isteğe bağlı satır
  kimlikleri) için açık (kapanmamış, iptal edilmemiş) bağlı plan/emir sayısı ve kimlikleri; okunamazsa açıkça
  "okunamadı". Host'ta Satış portuna bağlayan adaptör Satış şeridinin onayıyla eklenir.

## S-2 · Kaynak planı (Satış planı API-11, DEC-SAL-004)

- **İstenen (Satış):** sipariş satırında kaynak stratejisi (stok / satın alma / üretim / fason) ekranı ve kaydı.
- **Üretim tarafı:** plan satırı ↔ sipariş satırı bağı Üretim'de tutulur (Üretim planı API-16, URT-D13). Üretim,
  bağı kurarken mevcut `ISalesOrderLineReferencePortV1.GetApprovedLineAsync` ile satırı okur ve snapshot'lar;
  Satış'ta yeni yazma gerekmez. Satış'ın kaynak planı ekranı "üretim" seçildiğinde S-1 portundan durum okuyabilir.

## S-3 · Fason hizmet satış siparişi (URT-D15, URT-D16, SAT-D110)

- **Bugün:** Satış sipariş satırında satır türü / kalem tipi alanı yok. Ürün'de `ProductItemTypesV1.Service` ve
  Ayarlar'da kalem tipi varsayılan vergisi var.
- **İstenen (Satış):**
  1. Sipariş satırında hizmet (fason işçilik) satırı: kalem tipi `Service` olan ürün; miktar işlenecek kg veya
     hizmet birimi; vergi Ayarlar `ISettingsLineTaxResolverV1` ile (fason tevkifat oranı mali müşavir onayı
     bekliyor, SAT-D110 — o zamana kadar fail-closed).
  2. Siparişin "fason" olduğunu ve hangi müşterinin malının işleneceğini belirten başlık bilgisi.
  3. Bir okuma portu (öneri: `ISalesSubcontractServiceOrderReadPortV1`): şirket + sipariş kimliği → onaylı mı,
     müşteri (cari hesap) kimliği, hizmet satırları (ürün, miktar, birim), açık/kapalı.
- **Üretim tarafı:** müşteri malıyla plan, yalnız bu porttan onaylı ve aynı müşteriye ait bir fason siparişi
  okunduğunda kaydedilir ve onaylanır (URT-D22). Çıktı, fire ve hurda müşterinin malı kalır (URT-D16).
  Faturalama Satış'ın mevcut fatura akışıdır; Üretim fatura üretmez.

- **Satış tarafı durumu (PR #898):** `ISalesSubcontractServiceOrderReadPortV1` (`sales.subcontract-service-order.v1`) Satış
  modülünde kayıtlı; Üretim tüketici adaptörünü kendi şeridinde bağlar. Fason siparişin carisi, bağlı üretim planı / müşteri malı
  tahsisi kancası `ISalesSubcontractOrderBindingReadPortV1` bağlanana kadar **değiştirilemez** (503, fail-closed) — takip: Üretim
  host adaptörü. Müşteri malı eşleşme anahtarı Stok şeridiyle koordinasyon konusudur (Stok'ta "müşteri adına" anahtarı tanımlanınca
  sabitlenir; o zamana kadar port cariyi — PartyId + CompanyAccountId — yayımlar).

## Sıra önerisi

S-3 (fason) Üretim'in U5 dalgasını, S-1 iptal güvenliğini etkiler. S-2 Satış S6 dalgasıyla aynı yerdedir.
Üretim tarafı portları Satış beklemeden yazılır ve Satış portu gelene kadar fail-closed çalışır.
