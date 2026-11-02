# Alış → Satış görev notu v1

Tarih: 2026-11-02 · Yazan: Alış şeridi (Claude) · Alıcı: Satış modülünü yürüten konuşma ("Proje modülleri")
Kararlar: `docs/decisions/SINAN_KARARLARI_2026-11-02_ALIS_EK2.md` ALS-D22/D25 (tedarikçide hazır mal gelecek
arzdır, satış görmeli); `_ALIS_EK3.md` ALS-D30 (tedarikçide hazır mal satışa gelecek mal olarak ayrılabilir, aynı
ton bir kez); DEC-SAL-005 (iptal önizlemesinin altı kategorisi). Alış şeridi Satış dosyalarını değiştirmez. Bu not
iş tanımıdır; Satış'ın kendi karar, review ve merge kapıları geçerlidir. Arayüz adları öneridir. Bağlam:
`docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md` §6 MOD-03/MOD-16, §14; Satış planı
`docs/delivery/SATIS_HAZIRLIK_PLANI_v1.md` MOD-05, API-11, S6.

## S-A1 · Satış siparişine bağlı alış (MOD-03; Satış MOD-05, S6)

- **Bugün (main `e7a980c3`):** `SalesCancellationImpactSourcesV1.LinkedPurchasing` (`purchasing.linked-orders`)
  "kaynak yok" (NoSource) döner; `ISalesOrderLinkedSupplyReadPortV1` main'de yok. Alış siparişinde satış siparişi
  bağı da yok.
- **İstenen (Satış):** `ISalesOrderLinkedSupplyReadPortV1` sözleşmesinin sürümlü biçimi (Satış sahibi, S6 ile):
  girdi şirket + satış siparişi (ve/veya satır) kimliği; çıktı bağlı alış kayıtları (sipariş kimliği, satır, durum,
  açık miktar) ve Satış'ın mevcut `SalesCancellationImpactStatusesV1` sözlüğüyle durum (`None` / `Present` /
  `Unreadable` / `NoSource`). Okunamayan durum "yok" (`None`) sayılmaz.
- **Alış tarafı:** sözleşme merge edilince Alış, bu sözleşmeye bir **adaptör** yazar ve `purchasing.linked-orders`
  kategorisini gerçek cevaba çevirir (plan A4). Sözleşme dosyasına Alış dokunmaz; adaptör kaydı Api kompozisyonunda.
  Alış'ta satış siparişi bağı (talep kaynağı, API-02) bu adaptörün veri kaynağıdır.
- **Satış'ın sorusuna cevap (5l, `SATIS_MODUL_BAGLANTILARI_v1.md` / PR #919): "Alış tarafında okunacak veri modeli
  hazır mı? Hangi tablo/okuma servisi uygun?"** — **Hazır değil (❌).** Koddan (main `e7a980c3`):
  - `PurchaseRequest` (`Domain/Requests/PurchaseRequest.cs`) kaynak alanı taşımıyor; talepte satış siparişi/satırı
    kimliği yok. Bu yüzden bugün bir alış kaydını satış siparişine bağlayan veri yok.
  - Zincirin Alış içindeki yarısı var: `PurchaseOrder.SourceRequestId` ve `PurchaseOrderLine.SourceRequestLineId`
    siparişi/satırı talebe bağlar; satırda `RemainingQuantity` (= taahhüt − kabul) hesaplanır. Talepsiz girilen
    siparişin satış bağı yoktur (`SourceRequestId` boş). Sipariş durumu bugün yalnız
    `Draft` / `Approved`; iptal (API-10, No.161) ve satır kapatma (API-11) henüz yok.
  - Bağ API-02 ile gelir (plan A1): talep kaynağı = Satış + satış siparişi kimliği (+ satır). Ardından adaptör
    (plan A4, MOD-03) Alış'ın **kendi okuma modelini** bir Alış uygulama okuma servisi üzerinden okur: talep kaynağı
    → `SourceRequestId`/`SourceRequestLineId` ile siparişler ve satırlar → açık miktar = onaylı, iptal/kapalı
    olmayan satırın `RemainingQuantity`'si (genel satır ile teknik kırılım satırları toplanmaz; aynı miktar iki kez
    sayılmaz). Satış Alış tablolarını okumaz; adaptör Api kompozisyonunda kaydedilir.
  - Mevcut `IPurchaseOpenLineReadPortV1` (Lojistik için şirket genelinde onaylı açık satırları sayfalar) ve
    `IPurchaseSupplyReferencePortV1` (tek sipariş satırı) satış siparişine göre süzme yapmaz; adaptör bunları
    yeniden kullanmaz, aynı okuma modelinin yeni bir sorgusunu kullanır. Okunamayan durum (Alış çalışma zamanı
    kapalı, hata) `Unreadable` döner, `None` sayılmaz.

## S-A2 · Gelecek arzı görme ve gelecek mal olarak ayırma (MOD-16)

- **Bugün:** `IPurchaseSupplyReferencePortV1` (v1) kayıtlı ama tüketicisi yok; termin dilimi, tedarikçide hazır ve
  yolda miktarı taşımıyor.
- **Alış tarafı:** port v2 (plan §14.1 A3-0): termin dilimi, tedarikçide hazır, yolda, ayrılmış miktar; v1 kırılmaz.
- **Satış'ın sorusuna cevap (5m, PR #919):** port v2'yi Alış sağlar, Satış tüketir. Dilim: plan §14.1 **A3-0**
  (A3 kabul işinden önce). Biçim önerisi o dilimde yazılır ve Satış'a sözleşme incelemesi için gösterilir; bugün
  kesin alan listesi yoktur.
- **İstenen (Satış, isteğe bağlı):** kaynak planında (API-11) ve sipariş ekranında v2'yi okumak; "gelecek mal olarak
  ayırma" Stok tahsis motorundadır (ALS-D30, aynı ton bir kez) — Satış ayırmayı kendi tablosunda kopyalamaz.

## S-A3 · Ortak komut iskeleti ve ön yüz bileşenlerine geçiş (TEK-03/04) — ertelendi

Alış planı TEK-03 (BuildingBlocks altında ortak komut iskeleti) ve TEK-04 (`src/commercial/` ön yüz bileşenleri) önce
Alış için yazılır. Satış'ın bunlara geçişi ancak bu katmanlar main'e girdikten sonra ayrı bir notla istenir; bu notta
istek yoktur.
