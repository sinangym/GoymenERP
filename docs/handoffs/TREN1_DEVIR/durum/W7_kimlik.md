# W7 Kimlik — kaynak teslimi

Bu raporda DONE kaynak kapsamını belirtir. Gerçek SQL, test koşumu, tarayıcı, CI, K3 review ve insan grant/aktivasyon kapıları
ayrı kanıttır; kaynak veya derleme bunların yerine geçmez.

| Dilim | Kaynak durumu | Kararlar | Commit |
|---|---|---|---|
| KIM-SC32-a | DONE | FNS-D31, FNS-D70, FNS-D68, KLN-D109 | `9498911fe5c7160409a9e73eff77ebc7df3bd78c` |
| KIM-SC32-b | DONE | SAT-D346, SAT-D358, SAT-D364(1), KLN-D109 | `fae3f57bab00173ac0048a010171e58062a44252` |
| KIM-S5 | PARTIAL | KIM-D18, KIM-D20, KIM-D21; ID-3b | `2b7ecf9ed28ec124af3a5b747d96e5208963f1b2` |

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

## SC32 entegrasyon notu

Merkezi `KALAN_PLAN.json`, OpenAPI JSON/expectation, Shell authority hash, recovery/release, architecture fingerprint ve
ilişkisel/test grubu envanteri değiştirilmedi; kök entegrasyonda üretilecek. İki migration'ın Down kaynakları birer Sql
operasyonu içerir. Yeni tenant bypass/raw SQL üretim yüzeyi açılmadı; owner transaction bağlantısı K3 incelemesinde kalır.
Diğer W6 Kimlik PARTIAL kayıtları korunur; bu rapor onları DONE ilan etmez.
SC32 kapsamında yeni iş kararı seçilmedi. Yeni izinlerin gerçek grant/rol eşlemesi ve K3 kabulü insan kapısındadır.


## KIM-S5 — PARTIAL

- Kaynak commit: `2b7ecf9ed28ec124af3a5b747d96e5208963f1b2` (`KIM-S5 PARTIAL: wire company setup owners (KIM-D18 D20 D21)`).
- Organization `Company` ayrı `LifecycleState`/`OpenedByUserId` taşır. Yeni açık factory `SetupPending` üretir;
  operasyonel `IsActive` ayrı kalır. Mevcut şirketler migration ile Active kalır; seed/rol/grant yeniden yazılmaz.
  Şirket açan kimliği değişmez, lifecycle yalnız Pending→Active geçer.
- Gerçek `OrganizationCompanyLifecycleReader`, `ICompanyLifecycleReadPortV1` ve `IOutboxCompanyDirectoryV1` sağlar.
  Directory kurulum olaylarının işlenebilmesi için Pending şirketleri de içerir; üyelik veya grant sağlamaz.
  `organization.company.setup_pending` şirket sayısı metriği directory okumasında güncellenir.
- İki sahip modülde ayrı, şirket filtreli, immutable `CompanySetupInboxReceipts`; Organization'da kendi açılış outbox'ı,
  Identity outbox'ında rowversion cursor. Kaynaklar yalnız ambient şirketi okur; sabit MIN_ACTIVE_ROWVERSION horizon'u
  açık transaction'ın önünden cursor geçmesini engeller. Kayıt ve okuma sağlayıcıları gerçektir; açma producer command'i eksiktir.
- Identity açılış inbox'ı ve kurucu ilk Admin komutu mevcut `IdentityAdminLock`/kilitli aktif-süresiz Admin sayımını kullanır.
  Yapılandırılmış Admin rolü alınır; rol adı veya kişi kimliği koddan seçilmez. CompanyOpener yalnız opener'ın kendi
  süresiz atamasıdır; owner-local proof + aynı transaction'daki receipt/assignment olmadan store reddeder.
  CompanyOpener ile FounderDirect aynı 0→1 kilidini paylaşır, şirket başına deterministik tek first-admin olayı üretir.
- Kullanıcı ve Admin rolü aktifliği kontrol edilir. Pasif opener retry/dead-letter/alarm yoluna hata verir; şirketi açmaz.
  Rol değişimi/outbox/terminal cevap, Audit owner portu ve fail-closed permission security event'inden önce commit edilmez.
  Her denemede collector ve owner context temizlenir; failure tam transaction rollback olur; aynı teslim/key tekrar denenebilir.
- `POST /api/v1/identity/company-setup/first-admin` dar KIM-D20 komutudur. Pending şirkette yalnız aktif bootstrap kurucusu
  üyelik istisnasından geçer; hedef rol sunucudaki Admin rolüdür. Expected şirket sürümü, gerekçe, idempotency,
  correlation/causation, aktif hedef kullanıcı ve no-Admin sayımı vardır. Active şirkette replay de normal üyelik ve taze
  `identity.roles.manage` ister. Yeni geniş bir founder bypass açılmadı.
- Normal şirket middleware'i Pending'i 409 ile kapatır. Aynı scoped lifecycle authority wrapper cached/fresh/snapshot/persona
  yollarını ve başlıksız komutların yetkisini kapatır; bilinmeyen/okunamayan lifecycle yetki sağlamaz.
  Work Pending'de yalnız iki exact owner source/event/version çiftini geçirir; diğer iş olayları ve cursor bekler.
  Program yalnız gerçek kayıtları bağlar; dispatcher aktivasyonu/bayrağı değişmez.
- `organization.companies.open` yalnız bilinen izin ve KIM-D21 gereği Critical kaydıdır. Açma yetkisinin şirket/grup kapsamı
  seçilmedi; command binding, grant, rol ataması ve açma endpoint'i yoktur.
- Ortak middleware 409'u gerçek `RequireCompanyContext` metadata'sından OpenAPI source transformer'ına yansır;
  mevcut code-less 409'lar coded/code-less şemalarını korur. Generated OpenAPI JSON/expectation/pin değiştirilmedi.

### Migration ve derleme kanıtı

- EF CLI 10.0.8 ile gerçek üretim: `20261116185533_AddCompanySetupInboxAndOutboxPosition` ve
  `20261116185536_AddCompanySetupLifecycleAndMessaging`; designer/snapshot üretici çıktısıdır.
- Up yalnız DDL. Identity approval CHECK CompanyOpener'ı kapsar; company-event position index ve owner inbox eklenir.
  Organization lifecycle kolonları, owner inbox/outbox ve CHECK/index eklenir; eski şirketlere Active uyumluluğu korunur.
- Down ilk SQL işlemi **51944** / **51945**: ilgili yeni lifecycle/history/approval/event varken reddeder, veri kaybetmez.
  Harmless eski/boş veri için guard geçişi ve korunmuş veri için ret gerçek SQL fact kaynağında yazılıdır.
- Son kaynakta API, Identity.UnitTests, Organization.UnitTests, Work.UnitTests ve Api.IntegrationTests derlemeleri
  **exit 0, 0 hata/uyarı**. Work projesinin ilk no-restore denemesinde eksik assets hatası alındı; mevcut paket restore'u ile
  derlendi, yeni paket/sürüm/lockfile değişmedi. UTF-8 taraması ve `git diff --check` temizdir.
- Unit/contract kaynakları: owner company/immutable/proof guardları, deterministic event kimliği, yanlış kaynak/şirket/sürüm/
  bozuk payload reddi, Pending/unknown/foreign lifecycle ile cached/fresh/snapshot/persona reddi, Work cursor tutma ve model/Down.
- Beş gerçek SQL fact kaynağı: çift teslim + yalnız Organization aktivasyonu; Audit append hatasında rollback/retry;
  opener∥founder ilk Admin yarışı; pasif opener Pending kalır; iki Down guard'ının harmless/protected veride sonucu.
- Test runtime, gerçek SQL, DB apply, host/browser/Vitest/CI **NOT_RUN**. S5 frontend değişikliği yok; önceki SC32 tsc kanıtı
  S5 kabulü değildir. Kaynak derlemesi K3 incelemesi veya kullanıcı kabulü değildir.

### Eksik ve sonraki sınır

- Şirket açma command/endpoint'i eski kaynakta yoktur; şirket yalnız bootstrap seed ile oluşturulabiliyordu.
  Açma izninin hangi mevcut şirket veya grup yetki kapsamından kullanılacağı sorusu köke iletildi; cevap bekleniyor.
  Bu cevap gelmeden yeni opening command/UI veya seed'e sessiz event/grant bağlanmadı.
- Kurulum listesi/kurucu recovery ekranı ve kullanıcı akışı eksiktir. Kaynak factory/inbox/port veya sentetik SQL fixture,
  gerçek bir kullanıcının şirket açıp kurulumunu tamamlayabildiği anlamına gelmez; bu yüzden **PARTIAL**.
- Yeni iki outbox source'undaki sabit horizon SQL'i için **K3 review bekliyor**:
  `IdentityCompanySetupOutboxSource.cs`, `OrganizationCompanySetupOutboxSource.cs`. Yeni bypass açılmadı;
  guard'ı geçirmek için allowlist eklenmedi. İki Down SQL guard'ı ve cross-module sözleşmeleri de K3 kapsamındadır.
- Root entegrasyonu: yeni endpoint/ortak409 OpenAPI üretimi, migration/recovery/release/architecture/test-group/relational
  envanterleri ve merkezi plan güncellemesi köktedir. Ortak middleware/Program/Work kaynak kapsamı bu dilim için izinliydi.
- Son kullanıcı cevapları ile gelen notification süresi/numaralama gibi yeni W7 sonrası işler bu commit'te uygulanmadı;
  bir sonraki dalga karar/spec kapsamında alınacaktır.
