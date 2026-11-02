# Sinan Kararları — 2026-11-02 (Üretim EK2: başlangıç toleransının geçerliliği, profil personaları)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda, U1-a (#890, Ayarlar üretim parametre ve
tolerans profilleri) sonrasında sorulan seçenekli sorulara verdiği **doğrudan cevaplar**. Ana karar dosyaları:
`SINAN_KARARLARI_2026-11-01_URETIM.md` (URT-D1…D18), `SINAN_KARARLARI_2026-11-02_URETIM_EK1.md` (URT-D19…D22).
Ana dosyanın §0 yetki ve sınır kuralları aynen geçerlidir.

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| URT-D23 | Başlangıç tolerans değerlerinin (URT-D6 hat başı ±%2 kütle dengesi, URT-D7 %5 fire eşiği) devreye girmesi | **Doğrudan geçerli.** Sürüm-0 onaylı katalog değerleri emir kapanışında kullanılır; o kapsam için yayımlanmış bir sürüm varsa o geçerli olur. Kapanış kaydı hangi kaynak ve sürümle kapandığını snapshot olarak saklar | Kapanış yalnız elle yayımlanmış sürüm ≥ 1 kabul ediyordu; değerler `StartingValue` olarak duruyordu (#890) | `OrderClosureAssessment`, `ISettingsManufacturingToleranceProviderV1`, `ApprovedManufacturingProfileCatalog` |
| URT-D24 | Üretim parametre ve tolerans profillerinin personaları | **Teknik ofis / Üretim planlama hazırlar; Üretim müdürü onaylar ve yayımlar.** Hazırlayan ≠ onaylayan/yayımlayan. Reçete kartı düzeniyle aynı (delegasyon No 90). Persona eşlemesi grant değildir; gerçek kullanıcıya yetki ayrıca Sinan'ın onayıyla verilir | İzinler kayıtlı, hiçbir role verilmemiş (#890) | `settings.manufacturing-parameter-profiles.*`, `settings.manufacturing-tolerance-profiles.*` |

## 2. Uygulama

U1-b: kapanış sürüm-0 tolerans snapshot'ını kabul eder; tolerans hat (iş merkezi) kapsamına iner (URT-D6); açık uçlu
yayımlı sürümün yenisiyle değiştirilebilmesi; Üretim'in parametre profillerini okuması ve plan onayında snapshot'laması.
