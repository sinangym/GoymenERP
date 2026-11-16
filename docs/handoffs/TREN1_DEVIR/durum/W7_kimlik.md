# W7 Kimlik — kaynak teslimi

Bu raporda DONE kaynak kapsamını belirtir. Gerçek SQL, test koşumu, tarayıcı, CI, K3 review ve insan grant/aktivasyon kapıları
ayrı kanıttır; kaynak veya derleme bunların yerine geçmez.

| Dilim | Kaynak durumu | Kararlar | Commit |
|---|---|---|---|
| KIM-SC32-a | DONE | FNS-D31, FNS-D70, FNS-D68, KLN-D109 | Dilim commit'i; sonraki rapor güncellemesinde SHA |
| KIM-SC32-b | PARTIAL | SAT-D346, SAT-D358, SAT-D364, KLN-D109 | Henüz yok |

## KIM-SC32-a

- Settings sahibi: şirket anahtarlı `CollateralTypes` ve değişmez `CollateralTypeVersions`; altı açık teminat sınıfı,
  sabit kimlik/kod/sınıf, gerekçeli ad/etkinlik/oran sürümleri. Çek/senet eklenmez. Katalog boş başlar; açık oluşturma
  komutunun ilk oranı 100'dür. Sonraki oranlar Finance'in 64 karakterlik exact metin sözleşmesinde 0–100, yuvarlama yok.
- `ISettingsCollateralTypeCatalogV1` ve host `SettingsFinanceCollateralTypeCatalogReader`, Finance'in gerçek katalog
  portuna bağlıdır. Pasif tür oranları mevcut teminat için okunur; başka şirket veya bozuk/okunamayan kaynak fail-closed.
- `/api/v1/settings/collateral-types` GET/POST ve `/{entryId}/versions` POST; şirket sunucudan, ilk ve taze yetki çiti,
  expected version, gerekçe, idempotency terminal cevabı/replay, correlation/causation ve Audit owner portu.
  Audit owner aynı açık transaction'da Settings kaydından önce yazar; legacy interceptor'a pending taslak bırakılmaz.
  Hata/iptal/revocation business ve audit'i geri alır; owner hatası version conflict diye saklanmaz.
- Yeni izin yalnız kayıt: `settings.collateral-types.manage`. Grant/rol/Critical/production/OPEN/Finance STOP değişmedi.
- `/ayarlar/teminat-turleri`: gerçek API, Shell bildirimi/mount/nav, sıkı decoder, Türkçe metin, boş/yükleme/yetkisiz/
  bulunamadı/bozuk yanıt/503/çevrimdışı durumu, aynı anahtarlı belirsiz sonuç tekrarı ve çakışmada taze okuma.
- Mevcut policy/mapping/reference-list ve yeni collateral row'larına şirket-yazma ve insert-only guard eklendi;
  meşru append/unchanged/ikinci sürüm ve foreign insert/modify/delete/unresolved-company test kaynakları var.
- EF-generated `20261116181354_AddCollateralTypeCatalog`; iki tablo, FK/index/CHECK, seed/default yok.
  Down ilk işlem **THROW 51941**, herhangi bir katalog satırı varken forward fix; DB'ye uygulanmadı.

Kanıt: Api, Api.IntegrationTests, Settings.UnitTests derleme exit 0, 0 hata/uyarı; `tsc -b` exit 0.
İlk derlemede kullanılmayan local ve OpenAPI build bayrağı, ilk tsc'de Button/nav typing hataları düzeltildi.
Model/unit/gerçek-SQL/audit-owner rollback+retry+replay/Down/decoder/UI test kaynakları eklendi.
Tüm testler **NOT_RUN**; gerçek SQL/host/Vitest/browser/CI **NOT_RUN**. Merkezi pin/OpenAPI/inventory üretimi entegrasyondadır.

## Kalan

KIM-SC32-b ayrı dilim olarak sürüyor. Diğer W6 Kimlik PARTIAL kayıtları korunur; bu rapor onları DONE ilan etmez.
Yeni iş kararı veya NEEDS-SINAN üretilmedi. Yeni izinlerin gerçek grant/rol eşlemesi ve K3 kabulü insan kapısındadır.
