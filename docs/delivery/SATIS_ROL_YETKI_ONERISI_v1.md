# Satış İzinleri — Rol Öneri Tablosu v1

- **Durum:** ÖNERİ. Bu belge karar değildir; yeni karar kimliği üretmez, `KARAR_SICILI.md`'yi ve
  `docs/decisions/**`'ı değiştirmez.
- **İnsan kapısı:** Hiçbir izin bu belgeyle bir role veya kullanıcıya verilmez. Grant, rol tanımı,
  rol seed'i ve Critical sınıfı AGENTS.md "Değişmeyen insan kapıları" gereği Sinan'ın somut kararını
  ister. Bu PR kod, manifest, seed veya grant değiştirmez.
- **Kaynak:** Sinan'ın #66 onayı —
  <https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6726883926>
  ("sales.orders.close, sales.invoices.write, sales.orders.over-delivery.approve ve fatura
  kesinleştirme izinleri için yönetici / satış müdürü / satış personeli öneri tablosu docs'a konur").
- **İzin adlarının kaynağı:** `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs`
  (Sales owner adları, `SalesPermissionManifestV1` ile aynı dizgeler). Hepsi kayıtlı, hiçbiri
  verilmiş değil; hepsi yalnız şirket geneli (`CompanyWideOnly`) verilebilir.
- **Mevcut persona kartı:** `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md`. Bu tablo kartın
  yerine geçmez; kartta "ad bekliyor" olarak duran satırları kayıtlı adlara eşlemeyi **önerir**.

## Rol eşlemesi (öneri)

Görevdeki üç rol, mevcut kararlardaki personalara şöyle eşlenir:

| Bu tablodaki rol | Kararlardaki persona | Dayanak |
|---|---|---|
| **Yönetici** | Genel Müdür ve Admin | SAT-D187 (Admin müdürlerin yaptığı her şeyi yapar), SAT-D190 (Genel Müdür bütün modüllerde müdür onaylarını yapar; Admin'in sistem yönetimi hariç) |
| **Satış Müdürü** | Satış Müdürü | SAT-D187/SAT-D188 (satış yetkileri Satış Müdürü'ne; vergi/muhasebe yetkileri Muhasebe Müdürü'ne) |
| **Satış Personeli** | Satış Uzmanı (Satış Destek ayrı not) | SAT-D141 (Satış Uzmanı = Satış Destek'in grant'ları + SAT-D168'deki üç ek yetki) |

Not: Fatura izinlerinin kararlı sahibi **Muhasebe** tarafıdır (Muhasebe, Muhasebe Müdürü). Bu üç rolün
dışında kaldığı için tabloda ayrı sütun yoktur; gerekçe sütununda belirtilir.

## Görev ayrılığı dayanakları

| İlke | Kaynak | Uygulandığı yer |
|---|---|---|
| Hazırlayan ≠ onaylayan; tek onaycı başlangıç | `SINAN_KARARLARI_2026-10-21.md` P6 | Bütün onay izinleri (istisnalar: SAT-D44 süresi geçmiş teklif, SAT-D45 listesiz satır) |
| Sevki yapan ya da talebi açan kişi kendi fazla teslimini onaylayamaz, kendi satırını eksikle kapatamaz | SAT-D216 (EK37) | `sales.orders.over-delivery.approve`, `sales.orders.lines.close` (komutta `sales_over_delivery_self_approval`, `sales_line_closure_self_closure`) |
| Sevkiyatçı bilinmezken hiçbir fazla teslim onaylanmaz | SAT-D222 (EK41) | `sales.orders.over-delivery.approve` (409 `sales_over_delivery_shipper_unverified`) |
| Fazla teslim onayı onay anındaki miktarı kapsar; artarsa yeniden onay | SAT-D213 (EK37) | `sales.orders.over-delivery.approve` |
| Fatura taslağını hazırlayan veya gönderen onaylayamaz | SAT-D88, SAT-D204 (EK33) | `sales.invoices.approve` (domain `RequireNotPreparer`, `CK_Invoices_MakerChecker`) |
| Tolerans aşan eşleştirme farkını hazırlayan onaylayamaz | SAT-D106, SAT-D204 | `sales.invoices.match` + `sales.invoices.approve` |
| Faturayı kesinleştiren kendi muhasebe fişini onaylayamaz | KLN-D17 (`SINAN_KARARLARI_2026-11-07_MUHASEBE_FISI_VE_RECOVERY.md`) | `sales.invoices.finalize` sonrası Muhasebe fiş onayı |
| Onaylı sipariş iptalinde ikinci onay yok | SAT-D4 | `sales.orders.cancel` |

## Öneri tablosu

Sütun anlamları: **Ver** = önerilir · **Verme** = önerilmez · **Koşullu** = gerekçedeki koşulla.
"Karar" sütunu: **KARARLI** = persona kümesi bir SAT-D kararında açıkça yazılı; tablo yalnız kayıtlı
ada eşler. **ÖNERİ** = kararlarda bu ad için persona kümesi yok; öneri, karar değil.

"K3 / kritik" sütunu: izin adlarının hiçbiri kayıtta **Critical** değildir. Grant'ın kendisi her durumda
permission/rol K3 kapsamı ve insan kapısıdır; sütun ayrıca işlemin para/stok etkisini belirtir.

### İstenen dört izin

| İzin (kod adı) | Ne yapar | K3 / kritik | Yönetici | Satış Müdürü | Satış Personeli | Karar | Gerekçe |
|---|---|---|---|---|---|---|---|
| `sales.orders.close` | "Kapanmaya hazır" siparişi kullanıcı işlemiyle kapatır; gerekçe zorunlu değil | Critical değil; stok/para hareketi yazmaz, siparişin yaşam döngüsünü bitirir | **Ver** | **Ver** | **Ver** | KARARLI | SAT-D168 (Satış Uzmanı, Müdür, Admin) + SAT-D188 (Müdür → Satış Müdürü) + SAT-D190 (Genel Müdür); SAT-D32, SAT-D215. SAT-D221 gereği sahibi olmayan eksenler gelene kadar komut zaten 409 ile kapalıdır. |
| `sales.orders.over-delivery.approve` | Toleransı aşıp onay bandına giren fazla teslimi onaylar | Critical değil; **stok ve fatura edilebilir miktarı etkiler** (onaylanan fazlalık faturalanabilir hale gelir) | **Ver** | **Ver** | **Koşullu** | KARARLI | SAT-D168/SAT-D30 kümesi aynı üç rol. Koşul: Satış Uzmanı **kendi sevki veya kendi açtığı talep için onay veremez** (SAT-D216, komutta zorlanır); sevkiyatçı doğrulanamıyorsa kimse onaylayamaz (SAT-D222). Sevkiyatçı personası verilmez (kartta `—`). |
| `sales.invoices.write` | Onaylı sevk paylarından satış faturası taslağı hazırlar (API-32) | Critical değil; taslak para hareketi yazmaz, ama faturalamanın ilk adımıdır | **Ver** | **Verme** | **Verme** | KARARLI | SAT-D202: faturayı Muhasebe hazırlar/keser; Muhasebe Müdürü, Genel Müdür, Admin de keser; **Satış (Satış Uzmanı, Satış Müdürü) görür ama kesemez**. Satış tarafının görmesi `sales.invoices.read` ve ayrı fatura okumasıyla karşılanır. |
| `sales.invoices.finalize` | Eşleştirilmiş faturayı kesinleştirir ve Finans'a devrini (`sales.invoice.finalized.v1`) yeniden tetikler (API-36) | Critical olarak **işaretlenmemiş** (Critical sınıfı insan kapısıdır); **para etkili**: Finans'ta alacak açık kalemi ve Muhasebe'de 120 taslak fişi doğurur (SAT-D208, KLN-D10) | **Ver** | **Verme** | **Verme** | ÖNERİ | Kararlarda API-36 için ayrı persona kümesi yok. En yakın dayanak SAT-D202'nin "kesme" kümesidir (Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin; Satış kesemez); öneri bu kümeyi uygular. Kesinleştiren kişi kendi 120 fişini onaylayamaz (KLN-D17). Kesinleştirmenin hazırlayan/onaylayandan ayrı kişi olması **seçilmedi** (bkz. Sinan'ın seçeceği noktalar). |

### İlgili diğer Satış yazma/onay izinleri

| İzin (kod adı) | Ne yapar | K3 / kritik | Yönetici | Satış Müdürü | Satış Personeli | Karar | Gerekçe |
|---|---|---|---|---|---|---|---|
| `sales.orders.lines.close` | Bir sipariş satırını eksik teslimle kapatır (onay ve uyarı bandı) | Critical değil; kalan miktarı kapatır, kapatılan satıra sevk yapılamaz | **Ver** | **Ver** | **Koşullu** | KARARLI | SAT-D168/SAT-D17, SAT-D214. Koşul: kendi sevkinin/talebinin satırını kapatamaz (SAT-D216, komutta zorlanır). |
| `sales.orders.force-close` | Onaylı siparişi etki önizlemesinden sonra gerekçeyle zorla kapatır | Critical değil; teslim edilmemiş kalanı iptal eder, bağlı işler önizlemede açık görünür | **Ver** | **Ver** | **Verme** | ÖNERİ | No 30 / SAT-D12 adı ve kuralı kararlı, persona kümesi değil. Kart SAT-D168'i bu ada **eşlemez**. Öneri: iptale (`sales.orders.cancel`, SAT-D4 "Müdür") benzer olduğu için müdür seviyesinde tutulur. |
| `sales.orders.cancel` | Onaylı siparişi etki önizlemesinden sonra iptal eder | Critical değil; ikinci onay yok (SAT-D4) | **Ver** | **Ver** | **Verme** | KARARLI (kart) | SAT-D4 (Müdür tek adımda iptal) + SAT-D188; kartta Satış Müdürü `+`, Satış Uzmanı `?`. Satış Uzmanı hücresi kartta açık; öneri **Verme**. |
| `sales.orders.approve` | Sipariş taslağını iç onaydan geçirir/reddeder | Critical değil; P6 hazırlayan ≠ onaylayan | **Ver** | **Ver** | **Verme** | KARARLI | SAT-D6 (tek onaycı Müdür) + SAT-D188; SAT-D141 (Satış Uzmanı onaylayamaz); kartta kalın `—`. |
| `sales.orders.discount-exception.approve` | Limit aşan / listesiz iskontolu satırı gerekçeyle onaylar | Critical değil; **fiyat/para etkili** | **Ver** | **Koşullu** | **Verme** | KARARLI | SAT-D37 + SAT-D188. Koşul: Satış Müdürü'nün kendi limitini aşan iskontosunu başka bir onay yetkilisi onaylar (SAT-D67). |
| `sales.orders.price-floor-exception.approve` | Taban fiyat altındaki satırı gerekçeyle onaylar | Critical değil; **fiyat/para etkili** | **Ver** | **Ver** | **Verme** | KARARLI | SAT-D1 + SAT-D188; kartta Satış Uzmanı `—`. |
| `sales.invoices.approve` | Gönderilmiş fatura taslağını onaylar/reddeder; tolerans aşan eşleştirme farkını onaylar | Critical değil; hazırlayan/gönderen onaylayamaz | **Ver** | **Verme** | **Verme** | KARARLI | SAT-D88, SAT-D106, SAT-D204: Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin; Satış Müdürü kalın `—`. |
| `sales.invoices.match` | Onaylı faturayı resmî belgeyle (no, ETTN, tutar) eşleştirir | Critical değil; para/stok/Finans verisi yazmaz (EK20) | **Ver** | **Verme** | **Verme** | ÖNERİ (yorum) | SAT-D142 eşleştirmeyi Muhasebe'ye verir; SAT-D89/SAT-D229 tolerans. Ad kararlarda personaya bağlanmamış; öneri Muhasebe tarafı + Genel Müdür/Admin (SAT-D187/D190 yorumu). Tolerans aşan farkın onayı ayrıca `sales.invoices.approve` ister. |

Bu tabloya alınmayanlar (yalnız okuma veya kapsam dışı): `sales.drafts.read`, `sales.orders.price.read`,
`sales.invoices.read`, `sales.reports.read`, `sales.requests.read`, `sales.quotes.read`. Taslak/teklif/talep
yazma ve revizyon izinleri (`sales.drafts.create`, `sales.quotes.write`, `sales.requests.write`,
`sales.orders.quantity.write`, `sales.orders.header.write`, `sales.orders.price.write`,
`sales.orders.delivery-schedule.write`, `sales.orders.customer-acceptance.record`,
`sales.requests.approve`, `sales.quotes.expired-conversion.approve`) kartta ayrıca izlenir; bu belge
istenen dört izin ve onların görev ayrılığı komşularıyla sınırlıdır.

## Sinan'ın seçeceği noktalar

1. **Rol eşlemesi:** "Yönetici" = Genel Müdür + Admin, "Satış Personeli" = Satış Uzmanı eşlemesi doğru mu?
   Satış Destek'e SAT-D168 izinleri (kapatma, fazla teslim onayı) verilmeyecek mi (kartta `—`)?
2. **`sales.invoices.finalize` kimde olsun?** Öneri: SAT-D202 "kesme" kümesi (Muhasebe, Muhasebe Müdürü,
   Genel Müdür, Admin); Satış Müdürü ve Satış Uzmanı verilmez. Onaylanırsa kart satırı bu ada eşlenir.
3. **Kesinleştirmede görev ayrılığı:** Faturayı hazırlayan veya onaylayan kişi aynı faturayı kesinleştirebilsin
   mi? Kararlarda seçilmedi; bugün yalnız KLN-D17 (kendi 120 fişini onaylayamaz) var.
4. **`sales.invoices.finalize` Critical olsun mu?** Para etkili ilk adım olduğu için grant'ın iki kişiyle
   verilmesi (Critical) istenirse bu Sinan'ın kararıdır; kod bugün Critical işaretlemez.
5. **`sales.orders.force-close` kimde olsun?** Öneri: Satış Müdürü + Genel Müdür + Admin; Satış Uzmanı verilmez.
6. **`sales.orders.cancel` Satış Uzmanı hücresi:** kartta `?`; öneri **Verme**.
7. **`sales.invoices.match` kimde olsun?** Öneri: Muhasebe tarafı + Genel Müdür + Admin; Satış verilmez.
8. **Gerçek kullanıcı atamaları:** hangi kişinin hangi role bağlanacağı ve grant'ların ne zaman açılacağı
   (insan kapısı; bu belge hiçbir grant açmaz).
