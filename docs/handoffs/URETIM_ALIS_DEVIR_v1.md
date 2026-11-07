# Üretim → Alış (Satın Alma) görev notu v1

Tarih: 2026-11-02 · Yazan: Üretim şeridi (Claude) · Alıcı: Alış modülünü yürüten konuşma
Karar: `docs/decisions/SINAN_KARARLARI_2026-11-02_URETIM_EK1.md` URT-D20 — Alış tarafını başka bir konuşma yapar;
Üretim şeridi Alış dosyalarını değiştirmez. Bu not iş tanımıdır; Alış'ın kendi karar, review ve merge kapıları
geçerlidir. Arayüz adları öneridir.

## A-1 · Giden fason hizmet alış siparişi (URT-D15, URT-D17, DEC-STK-008, delegasyon No 71/73)

- **Bugün:** satın alma siparişi yalnız mal satırı taşıyor; hizmet / fason satırı, fason firması bilgisi ve
  gönderilecek malzeme listesi yok.
- **İstenen (Alış):**
  1. Fason hizmet alış siparişi: tedarikçi = fason firması (Cari'de fason rolü, `DEC-CAR-001`); hizmet satırı
     (kalem tipi `Service`, işlenecek kg veya hizmet birimi); gönderilecek malzeme ve beklenen çıktı ürünleri.
  2. Bir okuma portu (öneri: `IPurchaseSubcontractOrderReadPortV1`): şirket + sipariş kimliği → onaylı mı,
     fason firması, hizmet satırları, gönderilecek malzeme, açık/kapalı.
- **Üretim / Stok tarafı (Üretim şeridi):**
  - Malzemenin fason lokasyonuna gönderimi mevcut iki bacaklı Stok transferiyle (`IInventoryTransferWriterV1`)
    yapılır; mülkiyet şirkette kalır, gözetim fason firmasına geçer. Transfer bu fason siparişine bağlanır.
  - Dönüşte çıktı + fire, gönderilen kg ile URT-D6 toleransına göre dengelenir ve soy ağacı kurulur
    (Üretim planı API-38).
  - Bu adımlar yalnız A-1 portundan onaylı bir fason siparişi okunduğunda açılır; port yokken fail-closed.

## A-2 · İhtiyaç havuzunda üretim kaynağı (P-01)

- **Bugün:** satın alma talebinde kaynak / talep türü alanı yok.
- **İstenen (Alış):** talep satırında isteğe bağlı kaynak (stok / satış / üretim / manuel) ve kaynak kimliği.
- **Üretim tarafı:** onaylı planda hammaddesi eksik kalan girdi için "satın alma talebi oluştur" eylemi, Alış'ın
  mevcut talep oluşturma ucunu bu kaynakla çağırır. Alış alanı gelene kadar eylem gösterilmez.
- **Alış tarafı durumu (#928, 2026-11-03):** `POST /api/v1/purchasing/requests` talep satırında isteğe bağlı
  `source: {kind: "Production", documentId: <plan id>, documentLineId: <plan girdisi>}`. Diğer türler "Stock" ve
  "Sales"; kaynak yoksa null, yani Manuel; açıkça "Manual" yazmak 400. Kaynak oluşturmada yazılır, sonradan değişmez;
  onay akışı aynı (müdür onayı gerekir); ayrı izin yok. Kimlikler **opak**, Alış doğrulamaz (ALS-D58). Üretim eylemi
  (U5-c) plan ve girdinin geçerliliğini kendi tarafında doğrular. Plan girdisinin tekil kimliği etiket kimliğidir
  (`ParentRollId`).

## A-3 · Satış iptalinde "bağlı satın alma"

Satış'taki `LinkedPurchasing` kategorisi de bugün `NoSource`. Üretim'in işi değildir; Satış ve Alış şeritleri
arasında konuşulur (`URETIM_SATIS_DEVIR_v1.md` S-1 ile aynı desen).
