# W7 Kimlik — kaynak teslimi

Bu raporda DONE kaynak kapsamını belirtir. Gerçek SQL, test koşumu, tarayıcı, CI, K3 review ve insan grant/aktivasyon kapıları
ayrı kanıttır; kaynak veya derleme bunların yerine geçmez.

| Dilim | Kaynak durumu | Kararlar | Commit |
|---|---|---|---|
| KIM-SC32-a | DONE | FNS-D31, FNS-D70, FNS-D68, KLN-D109 | `9498911fe5c7160409a9e73eff77ebc7df3bd78c` |
| KIM-SC32-b | DONE | SAT-D346, SAT-D358, SAT-D364(1), KLN-D109 | `fae3f57bab00173ac0048a010171e58062a44252` |

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

## KIM-SC32-b

- Şirkete ait ayrı `sales.contract-discount-limit` anahtarı ve nullable `PercentValue` decimal(7,4).
  Sipariş limit grupları kullanılmaz. Kayıt yoksa null/version 0; sıfır ayrı, açık bir insan kaydıdır. Seed/default/reset yok.
- Generic company policy komutunun metadata/yetki/reason/expected version/idempotency/terminal replay yolu yeniden
  kullanıldı. Yeni anahtarda Audit owner aynı transaction'a Settings save'den önce katılır; eski policy komutları legacy
  interceptor yolunu korur. Audit hatası terminal conflict/başarıya dönüştürülmez; rollback sonrası aynı key tekrar denenir.
- `ISettingsSalesContractDiscountLimitProviderV1` ve host `SettingsSalesContractDiscountLimitHost`, Sales'in gerçek
  `ISalesContractDiscountLimitSourceV1` portuna kayıtlıdır. Şirket erişimi tüketici okumasında zorunlu; edit izni tüketiciye
  dayatılmaz. Gerçek yokluk null, denied 403, okunamayan/bozuk kaynak 503; yoklukta Sales'in mevcut onay davranışı korunur.
- Yeni izin yalnız kayıt: `settings.sales-contract-discount-limit.manage`. Kişi/rol adı veya kullanıcı kimliği koddan
  seçilmedi, grant/rol/Critical kararı verilmedi. Shell provider 1.11, iki yeni ekranla toplam 24 bildirim.
- `/ayarlar/sozlesme-iskonto-siniri`: gerçek API, ilk değeri boş editör, sürüm geçmişi, sıkı decoder/Türkçe metin ve
  ortak ekran durumları; unknown outcome aynı key/body ile tekrar, version conflict taze okuma. Ekran hesap yapmaz.
- EF-generated `20261116182730_AddSalesContractDiscountLimit`: nullable kolon ve key/value CHECK, data/backfill yok.
  Down ilk işlem **THROW 51942**; yeni anahtar/değer veya önceki rate CHECK'e dönemeyen kayıt varken forward fix.
- SC32-a'nın rate CHECK'i yeni migration içinde ileri düzeltildi: SQL UNKNOWN/TRY_CONVERT overflow boşluğu kapandı.
  Binary collation altında tam metinsel sınır karşılaştırılır; 000100.000, 64 sıfır ve 38'den uzun kesir geçerli kalır;
  100.000…001 ve 64 haneli taşan tam sayı reddedilir. Eski migration, saklanan geçmiş ve audit içeriği yeniden yazılmadı.
- Yeni policy tipi mevcut immutable/company guard'a tabidir. Son model testleri iki migration'ın sırasını ve snapshot
  uyumunu, nullable/precision/no-default yapısını, ayrı permission kaydını, null/zero/denied/unavailable adapter ayrımını
  kapsar. SQL kaynakları business lifecycle, company, replay/version/no-op, CHECK/Down ve owner rollback/retry içerir.

Son kanıt: Api, Api.IntegrationTests ve Settings.UnitTests derlemeleri exit 0, 0 hata/uyarı; `tsc -b` exit 0.
EF CLI 10.0.8 iki migration'ı gerçek üreticiyle üretti; veritabanına uygulanmadı. Test runtime/gerçek SQL/Vitest/browser/
host/CI **NOT_RUN**. Kaynak ve derleme, K3 veya kullanıcı kabulü değildir.

## Entegrasyon ve kalan

Merkezi `KALAN_PLAN.json`, OpenAPI JSON/expectation, Shell authority hash, recovery/release, architecture fingerprint ve
ilişkisel/test grubu envanteri değiştirilmedi; kök entegrasyonda üretilecek. İki migration'ın Down kaynakları birer Sql
operasyonu içerir. Yeni tenant bypass/raw SQL üretim yüzeyi açılmadı; owner transaction bağlantısı K3 incelemesinde kalır.
Diğer W6 Kimlik PARTIAL kayıtları korunur; bu rapor onları DONE ilan etmez.
Yeni iş kararı veya NEEDS-SINAN üretilmedi. Yeni izinlerin gerçek grant/rol eşlemesi ve K3 kabulü insan kapısındadır.
