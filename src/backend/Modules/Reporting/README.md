# Reporting (rapor merkezi) — WP14

Dayanak: `DALGA_PLANI_v2.md` 1.15, YOL-D4 (b), YOL-D6, YOL-D13, ROL-D13, ROL-D15, EK-40, DEC-DOC-009.

Rapor merkezi **sayı saklamaz ve başka modülün tablosunu okumaz**. Her rapor, sahip modülün okuma portuna
host adaptörüyle (`GoymenERP.Api/ReportingRuntime`) bağlanır; hesap (yaş, yaş aralığı, OTIF, sayfa özeti)
`ReportingServiceV1` içinde, backend'de yapılır. Ekran yeniden hesaplamaz.

## Raporlar

| Rapor | Sahip | Gerekli izinler | Kaynak |
|---|---|---|---|
| `stock-aging` | Inventory | `inventory.labels.read` + `inventory.balances.read` | `IInventoryStockAgingReadPortV1` |
| `recall` | Inventory | aynı | `IInventoryLabelTraceV1` (STK-S33) |
| `party-aging` | Finance | `finance.open-items.read` + `finance.open-items.amount.read` | `IFinanceOpenItemReadServiceV1` |
| `otif` | Sales × Logistics | `sales.reports.read` | `ISalesOrderPromiseReadPortV1` (yeni) × `ISalesInvoiceableShipmentReadPortV1` |
| `intercompany` | Counterparty × Finance | `reporting.intercompany.read` + Finance open-item okuma izinleri | `ICounterpartyGroupCompanyLinkReadPortV1` (yeni) × `IFinanceOpenItemReadServiceV1` |

Ölçütler kararlardan gelir:
- **OTIF (YOL-D29):** satırın sipariş miktarının **tamamı** söz verilen termin gününde veya önce sevk edildiyse OTIF; kısmi
  sevk OTIF değil; tolerans yok. Söz verilen termin satırın istenen teslim tarihi, yoksa siparişinki. Miktar onaylı (gerekiyorsa
  müşterinin kabul ettiği) revizyonun miktarı; açık ve **kapanmış** siparişler sayılır, iptal edilenler sayılmaz; miktarı
  belirlenemeyen satır sayılır ama hesaba girmez. Sevk tarihi, onaylı irsaliye paylarının kayıtlı sevk anının Europe/Istanbul
  iş günüdür; başka birimdeki pay çevrilmez (503).
- **Alacak/borç yaşlandırma (YOL-D30):** gecikme vadeden; 0-30 / 31-60 / 61-90 / 90+; vadesi gelmemişler ve vadesi
  bilinmeyenler ayrı sütun; cari × cari hesap × para birimi; para birimleri çevrilmez, toplanmaz; 10 000 kalem üstü 503.
- **Grup şirketi (YOL-D31):** cari kartındaki cari ↔ şirket bağlantısı (`Party.GroupCompanyId`; Cari ekranında "Grup şirketi
  bağlantısı" paneli, izin `counterparty.parties.group-company-link.update`, kimseye verilmedi). VKN eşleşmesi grup şirketi
  saymaz. Döküm: bağlantılı carilerin (aktif şirketin kendisi hariç) Finance alt defter kalemleri, dönemde kabul edilenler.
- **Stok yaşlandırma (YOL-D39):** yaş etiket kökünün ilk giriş tarihinden; bölünen etiket kökün, birleşen en eski kökün
  tarihini alır; dilimler 0-30 / 31-60 / 61-90 / 90+.

İş günü Europe/Istanbul takvim günüdür (P7); saat dilimi okunamazsa 503.

## Dışa aktarım ve denetçi paketi

- `POST /api/v1/reporting/reports/{id}/exports` (CSV/XLSX, sayfalı, en çok 5000 satır/sayfa): rapor izinleri +
  `reporting.export`. Dosya çıkmadan önce aynı transaction'da bir `reporting.ExportRecords` satırı (parametreler,
  sütunlar, sayfa, satır sayısı, içerik SHA-256) ve `reporting.report.exported.v1` audit olayı yazılır. CSV/XLSX'te
  formül enjeksiyonu engellenir (`=`, `+`, `-`, `@` ile başlayan metin `'` ile başlar).
- `POST /api/v1/reporting/auditor-packages` (`reporting.auditor-package.export`, YOL-D13/ROL-D13): seçili dönem
  için ZIP — `manifest.json` + aktörün okuyabildiği bölümler (mizan: Accounting trial balance + `accounting.journals.read`;
  stok pozisyonları; dönem sonu itibarıyla alacak ve borç yaşlandırması; grup içi döküm). Okuyamadığı bölüm `forbidden`, bağlı olmayan bölüm (`cost`,
  `document-trail`) `not-connected` olarak listelenir; kesik liste `complete: false` taşır. Denetçiye sistem
  erişimi verilmez.

## Kayıtlı görünüm

Kişisel (yalnız sahibi görür/değiştirir), rapor başına en çok 50 etkin. Oluştur/güncelle/pasifleştir: Idempotency-Key,
beklenen sürüm, kalıcı ret (aynı anahtar aynı reddi döner), audit. Silme yoktur; pasifleştirme bir sürüm adımıdır.

## İzinler (kayıtlı, kimseye verilmedi)

`reporting.export`, `reporting.auditor-package.export`, `reporting.intercompany.read` — şirket geneli, Critical değil
(Sinan kararı: raporlama Critical işaretlenmez).
Rol ataması insan kapısıdır (ROL-D15 önerisi: Genel Müdür, Sistem Yöneticisi, Muhasebe Müdürü).

## Migration

`AddReportingCenter` (şema `reporting`: `SavedViews`, `ExportRecords`, `CommandAttempts`). Down guard **52270**:
herhangi bir satır varken geri alma reddedilir (forward fix); boş depoda geri alınır. WP14 aralığı 52270–52279.
Counterparty'deki `AddPartyGroupCompanyLink` (WP14) Down guard **52271**: bağlantılı cari varken reddeder.
