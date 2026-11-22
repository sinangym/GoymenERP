# Reporting (rapor merkezi) — WP14

Dayanak: `DALGA_PLANI_v2.md` 1.15, YOL-D4 (b), YOL-D6, YOL-D13, ROL-D13, ROL-D15, EK-40, DEC-DOC-009.

Rapor merkezi **sayı saklamaz ve başka modülün tablosunu okumaz**. Her rapor, sahip modülün okuma portuna
host adaptörüyle (`GoymenERP.Api/ReportingRuntime`) bağlanır; hesap (yaş, yaş aralığı, OTIF, sayfa özeti)
`ReportingServiceV1` içinde, backend'de yapılır. Ekran yeniden hesaplamaz.

## Raporlar

| Rapor | Sahip | Gerekli izinler | Kaynak | Durum |
|---|---|---|---|---|
| `stock-aging` | Inventory | `inventory.labels.read` + `inventory.balances.read` | `IInventoryStockAgingReadPortV1` (yeni, Inventory'nin kendi tabloları) | Bağlı |
| `recall` | Inventory | aynı | `IInventoryLabelTraceV1` (STK-S33) | Bağlı |
| `otif` | Sales | `sales.reports.read` | — | **Bağlı değil** (503 `reporting_source_not_connected`) |
| `intercompany` | Accounting | `reporting.intercompany.read` (yeni) | — | **Bağlı değil** (EK-40 bacak işaretlemesi tanımsız) |

Kararlarda tanımı olmayan ölçüt tahmin edilmez; **zorunlu parametredir**, varsayılanı yoktur:
yaş aralığı sınırları (`bucketDays`), yaş başlangıcı (`ageBasis`: etiketin oluşturulması / ilk giriş hareketi),
OTIF toleransları (`onTimeToleranceDays`, `inFullTolerancePercent`). OTIF tanımı önerisi tek fonksiyonda
(`ReportingOtifCalculatorV1`): zamanında = ilk sevk ≤ istenen tarih + tolerans; tam = toplam sevk ≥ sipariş ×
(1 − %); OTIF = son tarihe kadar sevk edilen ≥ hedef. Tarih sevk (irsaliye) tarihidir.

İş günü Europe/Istanbul takvim günüdür (P7); saat dilimi okunamazsa 503.

## Dışa aktarım ve denetçi paketi

- `POST /api/v1/reporting/reports/{id}/exports` (CSV/XLSX, sayfalı, en çok 5000 satır/sayfa): rapor izinleri +
  `reporting.export`. Dosya çıkmadan önce aynı transaction'da bir `reporting.ExportRecords` satırı (parametreler,
  sütunlar, sayfa, satır sayısı, içerik SHA-256) ve `reporting.report.exported.v1` audit olayı yazılır. CSV/XLSX'te
  formül enjeksiyonu engellenir (`=`, `+`, `-`, `@` ile başlayan metin `'` ile başlar).
- `POST /api/v1/reporting/auditor-packages` (`reporting.auditor-package.export`, YOL-D13/ROL-D13): seçili dönem
  için ZIP — `manifest.json` + aktörün okuyabildiği bölümler (mizan: Accounting trial balance + `accounting.journals.read`;
  stok pozisyonları; grup içi döküm). Okuyamadığı bölüm `forbidden`, bağlı olmayan bölüm (`counterparty-balances`,
  `cost`, `document-trail`) `not-connected` olarak listelenir; kesik liste `complete: false` taşır. Denetçiye sistem
  erişimi verilmez.

## Kayıtlı görünüm

Kişisel (yalnız sahibi görür/değiştirir), rapor başına en çok 50 etkin. Oluştur/güncelle/pasifleştir: Idempotency-Key,
beklenen sürüm, kalıcı ret (aynı anahtar aynı reddi döner), audit. Silme yoktur; pasifleştirme bir sürüm adımıdır.

## İzinler (kayıtlı, kimseye verilmedi)

`reporting.export`, `reporting.auditor-package.export`, `reporting.intercompany.read` — şirket geneli, Critical değil.
Rol ataması insan kapısıdır (ROL-D15 önerisi: Genel Müdür, Sistem Yöneticisi, Muhasebe Müdürü).

## Migration

`AddReportingCenter` (şema `reporting`: `SavedViews`, `ExportRecords`, `CommandAttempts`). Down guard **52270**:
herhangi bir satır varken geri alma reddedilir (forward fix); boş depoda geri alınır. WP14 aralığı 52270–52279.
