# Platform — dalga 5 lead raporu

- Dal: `claude/tren-1-platform` @ `daade3796` (push edildi; base `318c02000`).
- Lead worktree: `tren/lead/platform-w5`. Dalga 4 lead worktree'si (`platform-w4`) dalı serbest bırakmak için detached HEAD'e alındı (dosya değişikliği yok).

## Birleştirilen dilimler
- **URN-SC34** (DONE, `dcd013132`, `--no-ff`): XLSX genişletilmiş başlık testi (yazıldı); URN-D22 doc yorumları; URN-D23 tavan: Product sahipli `IProductProfileWeightUploadCeilingSourceV1` + Api adaptörü `DocumentsProductProfileWeightUploadCeilingSource` (kasanın `DocumentsServiceBindings.MaximumUploadBytes` değerini okur, ikinci değer yok). Sınır = min(Ayarlar kaydı ya da 5 MiB, genel yükleme sınırı); okunamaz/≤0 → 503 `provider_unavailable`.
  - Kural incelemesi: başka modül tablosu yazımı yok, grant/rol/izin yok, yasak dosya yok, migration yok, OpenAPI değişmedi. Çakışma yok.

## Atlanan dilimler
- Yok.

## Derleme (GOV-D10, yalnız lead)
- `GoymenERP.Api`: 0 hata/0 uyarı (exit 0, `-p:OpenApiGenerateDocumentsOnBuild=false`). Varsayılan build derleme sonrası getdocument adımında düşüyor: pinli `ShellCapabilityRegistryAuthorities` base'deki diğer ekiplerin provider sürümlerini içermiyor (bilinen pin kayması, son eğitimde yeniden üretilecek; bu dalga shell'e dokunmadı).
- `GoymenERP.Product.UnitTests`: exit 0.
- Frontend `tsc -b`: exit 0 (symlink kaldırıldı). bin/obj silindi. Testler yazıldı, koşulmadı.
- Not: başka bir ajanın build'inden kalan sahipsiz VBCSCompiler süreci bir süre global kilidi tuttu (UseSharedCompilation=false olmayan bir ef/build çağrısı); boşta kalma süresi dolunca bıraktı.

## regen_needed (birleşik)
- recovery-inventory (ProductProfileWeightTableService.cs pin, yeni Api dosyası), release-manifest
- architecture-guard: PlatformTenantQueryFilterBypassGuardTests.cs (ProductPriceListRegistration.cs fingerprint)
- shell-capability-registry (base'den gelen kayma; Api host bu pin olmadan başlamaz)

## NEEDS-SINAN
- KLN-D110: uygulama içi bildirimler ne kadar süre saklanır (saklama politikası)? Platform uygulama içi bildirim deposu bundan önce kurulamaz.

## Mesajlar
- Gönderilen (işçi): `platform__kimlik__kgm-iceaktarim-tavan-urn-d23.md` (Ayarlar yazma komutu/ekranında URN-D23 tavanı), `platform__satis__kln-d110-uygulama-ici-bildirim.md` (taslak port adı, bildirim ayarlanabilir ve kapalı kalsın).
- Yanıtlanan: `koordinator__platform__SC38-kararlari.md`, `ust__platform__sc32.md`, `ust__platform__sc35.md` (son ikisi: henüz kodda yok, sonraki dalga).

## Sonraki dalga
- SC32: SAT-D366 denetim saklama 10 yıl (Ayarlar değeri, imha kapalı) — platform dilimi bekliyor.
- SC35: KLN-D101 temizlik işi varsayılanları (parti 500, 1 dk, 30 sn, ayar dosyası; OPEN-021'e kadar kapalı) — platform dilimi bekliyor.
- KLN-D110 uygulama içi bildirim deposu: ayrı platform K3 dilimi (tablo+migration+endpoint+shell ekranı); saklama kararı gelene kadar başlatılmamalı.
- Kullanıcı-yüklemesi kasa portu hâlâ yok; kg/m içe aktarım kasada 503 fail-closed (dalga 4 notu geçerli).
- Kimlik Ayarlar değerini eklediğinde GET görünümü yürürlükteki sınırı dönmeli; frontend `profileWeightApi.ts` sabit 5 MiB kontrolü kaldırılmalı (OpenAPI regen gerekir).
