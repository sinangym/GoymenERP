# Güvenlik takip sözleşmeleri (WP-C10)

| Dosya | Ne | Kim üretir / okur |
|---|---|---|
| `dependency-scan-report-v1.schema.json` | Bağımlılık güvenlik uyarıları raporu (ekosistem, paket, kurulu/düzeltilmiş sürüm, danışma kimliği, tarayıcının önem etiketi, başlık; üretim zamanı ve kaynak commit). | WP16 deterministik taraması üretir; `security.tech-watch.summary.v1` yalnız okur (`Ai:SecurityWatch:DependencyScanReportPath`, varsayılan yol yok → `not-configured`). |
| `emergency-step-list-v1.schema.json` | Önceden onaylı acil adım listesi — yalnız şema. | Liste AI-D18 gereği ayrı karardır: bugün `steps` boş, `executable` false; hiçbir kod adım yürütmez. |
| `samples/dependency-scan-report-v1.sample.json` | SENTETİK örnek rapor (testler kullanır). | — |

Araçlar: `security.events.summary.v1` (SecurityEvents sahibi) ve `security.tech-watch.summary.v1` (Ai); ikisi de
mevcut `platform.security-events.read` iznini şirket çapında ister, veri sınıfı Confidential, `Ai:AllowedTools` boş
kalır. Yeni izin yok, hiçbir role verilmez. Aktör kimliği yanıt başına takma adla maskelenir.
