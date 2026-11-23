# kimlik — dalga 5 durum raporu

Dal: `claude/tren-1-kimlik` (push edildi), taban 318c0200, head a134ac646.

## Merge edilen dilimler (çakışmasız, --no-ff)
- KIM-S19 (PARTIAL) — şirket politika sicili, Finans açılış dönemi okuyucu adapter'ı, termin tamponu, satış fason vergi varsayılanı.
- KIM-S18 (PARTIAL) — Ayarlar referans listeleri (masraf/hizmet tipi, boya rengi, hurda türü) + ekran.
- KIM-S15 (PARTIAL) — üretim profili tek akış (URN-D6); KIM-S18 adına AddReferenceListEntries migration'ı da burada üretildi.
- KIM-S7 (PARTIAL) — recover-founder reactivate/reset-password (ID-2r), elle yazılmış SecurityEvents migration'ı.
- KIM-SC34 (DONE) — yer tutucu VKN düzeltmesi staging + onaylı uygulama (KIM-D41/D48).

Atlanan dilim: yok. Hızlı incelemede kural ihlali (başka modül tablosu, grant, rol, Critical, yasak dosya) bulunmadı.

## Derleme (lead, flock, -p:UseSharedCompilation=false, -p:OpenApiGenerateDocumentsOnBuild=false)
- GoymenERP.Api: exit 0. Api.IntegrationTests: exit 0. Settings/SecurityEvents/ArchitectureTests/Manufacturing unit: exit 0.
- Identity.UnitTests ve Organization.UnitTests ilk derlemede hata verdi; düzeltme commit'i a134ac646
  (SecurityEventV1Validator.Validate; IDesignTimeModel için Metadata using). Sonra exit 0.
- KIM-S18'in "Build failed" bildirimi birleşik dalda tekrarlanmadı.
- OpenAPI build-time adımı kapatıldı: ShellCapabilityRegistryAuthorities tabanda zaten bayat (entegrasyonda yeniden türetilmeli).
- Frontend `tsc -b`: exit 0.
- Test koşulmadı. KIM-S7 migration/snapshot'ı elle yazıldı: `SecurityEventsPersistenceModelTests.Migrations_are_ordered_and_the_snapshot_matches_the_model` entegrasyonda mutlaka koşulmalı.
- bin/obj silme komutu izin sınıflandırıcısı tarafından reddedildi (lead ve S7/S15 worker worktree'lerinde); gitignored, commit'te yok.

## regen_needed (birleşim)
- openapi (settings company-policies 6 uç, reference-lists 12 uç, manufacturing approve RetiredApproval 409, organization tax-id corrections GET/apply)
- recovery-inventory + release-manifest (yeni migration'lar: Settings AddCompanyPolicyVersions, AddReferenceListEntries, AdoptManufacturingProfileSingleFlow; SecurityEvents AddFounderRecoverySecurityEvents; Organization StageCompanyTaxIdCorrections; ClearPlaceholderCompanyVkn gövdesi değişti)
- architecture-guard: PlatformTenantQueryFilterBypassGuardTests (SettingsDbContext + partial'lar, SettingsModuleExtensions, OrganizationDbContext, IdentityModuleExtensions, SqlLocalCredentialStore/CommandStore, FounderRecoveryService/CompanyLock; ReviewedMigrationMutationSources: AddCompanyPolicyVersions Sql=1, AddReferenceListEntries, AdoptManufacturingProfileSingleFlow, AddFounderRecoverySecurityEvents Sql=1, StageCompanyTaxIdCorrections Sql=2, ClearPlaceholderCompanyVkn girdisi güncelle/kaldır)
- relational-inventory:platform (S15 ve S7 birer SQL fact ekledi) + test grup/shard/RCSI sayıları
- shell-capability-registry (Settings provider 1.6; tabanda zaten bayat)

## NEEDS-SINAN
- N-12 numara serisi biçimi/sıfırlama/boşluk politikası (ISettingsNumberSeriesProviderV1 yalnız taslak).
- ALS-D105: satınalma tarafı fason vergi varsayılanını kim yazar (yazma kapalı).
- FNS-D97: kredi hesap eşleme amaç ayrıntısı ve yazma yolu/yetkisi.
- settings.finance-opening-period.manage kime verilir (grant yok).
- URT-D96: şirket termin tamponu üst sınırı (iş günü).
- Satış fason vergi varsayılanı rotası (purchasing.orders.approve) ve termin tamponu rotası (manufacturing.plans.approve) yetki teyidi.
- settings.reference-lists.manage → Satınalma Müdürü/Admin grant'i (insan kapısı).
- settings.production-reference-lists.manage (boya rengi/hurda) kim düzenler (B-14).
- Operasyon tipi listesi ne zaman açılır (Satış fasonu operasyon tipidir).
- ALS-D79 ortak masraf yapısı ayrıntısı (NEEDS-SPEC).
- URN-D5 üretim profili version-0 aktarımı: hangi şirketler, hangi izin.
- recover-founder reactivate operatörün yazdığı parolayı saklasın mı (şu an evet, MustChangePassword=true)?
- identity.founder.* olayları için saklama sınıfı No134 teyidi.
- Platform Audit sözleşmesinde System aktör/operatör beyanı yok; recover-founder şirket Audit satırı yazmıyor — sözleşmeyi kim değiştirir?
- Tam kilitlenme: KIM-D16 başka yönetici varken reset reddi; o kişi yoksa onaylı yol?
- Bayat VKN düzeltmesi için ret/kapatma yolu.
- ClearPlaceholderCompanyVkn eski Up'ını koşmuş tren veritabanları: backfill audit mı, yeniden kurulum mu?
- Yer tutucu VKN temizliğinin onay kapılı (organization.company.tax-id.approve) olması KIM-D41/D48 okuması olarak teyit.

## Mesajlar
- Gönderilen (worker): kimlik__finans__acilis-donemi-adapter.md (S19), kimlik__platform__founder-recovery.md (S7); S18 alis_cari__kimlik__KIM-S18-masraf-tipi-listesi.md'yi yanıtladı.
- Lead yanıtları: uretim__kimlik__sirket-varsayilan-guvenlik-payi-portu.md, alis_cari__kimlik__ALC-SC47-legal-name-permissions.md, koordinator__kimlik__SC36-kararlari.md, koordinator__kimlik__SC38-kararlari.md.

## Sonraki dalga bilmeli
- URN-D23 (Ayarlar kg/m içe aktarım sınırı ≤ platform genel sınırı, backend doğrulaması) uygulanmadı.
- Üretim şirket varsayılan tamponu: port iş günü cinsinden (Hours/Days birimi yok); Manufacturing'in istediği birim hizalaması açık.
- KIM-S19 real-SQL kabul testleri (Down guard 51853, CHECK'ler) ve KIM-SC34 51877 real-SQL Down testi borç (K3 kanıt açığı).
- KIM-S19 Ayarlar ekranı (açılış dönemi, termin tamponu, vergi varsayılanı) frontend'i yok.
- KIM-S7 Audit satırı yazımı platform Audit sözleşmesi değişikliğine bağlı.
- ErpPermissions'ta alis_cari ALC-SC47 ile metin çakışması olası; iki tarafın adları korunmalı.
- KIM-SC34: legacy upgrade testi hâlâ GIN Version 1 bekliyor; doğrulanmadı.
