# F-029 — Derlenmiş politika katalogları: envanter ve hedef spec (v1, yalnız doküman)

**Kaynak:**
- Fable 01, F-029 (`docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md`). Metin aynen:
  "Politika katalogları ve şirket kimlikleri derlenmiş kodda; şirket üstü kiracı düzeyi yok … Kendi kullanımınız için
  sorun değil. Lisanslı ürüne geçişte ciddi iş; yeni tablolarda bu ayrımı zorlaştıracak kısayollardan kaçının. Şimdi
  kiracı katmanı eklemeyi önermem".
- Koordinatör görevi, 2026-11-12 08:56Z: önce envanter ve spec yazılacak. Ardından şirket bazlı, sürümlü bir
  ayar/katalog tablosu kurulacak. Değerleri yalnız yetkili kişi değiştirecek ve her değişiklik denetim kaydı
  bırakacak. Mevcut davranış varsayılan olarak korunacak, yeni değer tahminle seçilmeyecek. Diğer modüllerin
  katalogları için ilgili şeride port/sözleşme önerilecek.
- Taban: `origin/main` `58b96aa`.

Bu belge **kod, migration, seed, izin adı, grant veya değer değiştirmez**. Hiçbir `OPEN-*` kararını kapatmaz,
yeni bir politika değeri de seçmez. §6'daki sorular Sinan kararlarıyla (URN-D2–D6) cevaplanmıştır.
Kiracı (şirket üstü) katmanı F-029 önerisine uygun olarak **kapsam dışıdır** (§5.6).

## 1. Özet

- `GoymenERP.Modules.Settings.Application` içinde **11 derlenmiş katalog** var (`Approved*Catalog`). Kod yorumlarına
  göre hepsi "sürümlü, kod gibi incelenmiş kaynak veri"dir. Çalışma zamanında hiçbir şey bunları değiştirmez; yeni bir
  değer, kod gibi incelenen yeni bir sürümdür. Kalıcı bir sağlayıcı, aynı portun arkasında bunların yerini alır.
- Kalıcı durum:
  - **5 kataloğun** kalıcı zaman çizelgesi tablosu zaten var. Bunlarda derlenmiş katalog yalnız taban (sürüm 0)
    olarak duruyor: vergi kalem tipi varsayılanı, vergi alıcı durumu, üretim parametre profili, üretim toleransı ve
    döviz/vergi ana tabloları.
  - **1 katalog kısmen kalıcı:** satış politikasında yalnız iskonto limiti tablodan geliyor; teslim toleransı ve onay
    eşiği hâlâ derlenmiş katalogdan okunuyor.
  - **5 katalog yalnız derlenmiş:** birim profili, birim kimliği (birim profilinden türetilir), para profili, teklif
    politikası, e-Fatura senaryosu.
- **Sabit şirket kimlikleri** (GDC/GSP/GIN için `10000000-…-000000000001/2/3`) `ApprovedUnitProfileCatalog` içinde
  tanımlıdır ve 9 Settings kataloğunda, 5 Ürün fixture deposunda ve `GoymenERP.Api/Shell/C01E2eFixtureWriterActivation.cs`
  içinde kullanılır.
- Settings'te prepare → approve → publish yaşam döngüsünü izin adlarıyla uygulayan bir örnek zaten var: üretim
  parametre ve tolerans profilleri. Hedef tasarım bu örneği temel alır; URN-D3 gereği ayrı onay adımı olmadan (prepare → publish).

## 2. Envanter — Settings (Ayarlar modülü, Codex backend şeridi)

Dosyaların hepsi `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Application/` altındadır.

| # | Katalog | Karar kaynağı | İçerik (değerler burada tekrarlanmaz; kaynak kod tek kaynaktır) | Port | Kalıcı durum | Sınıf |
|---|---|---|---|---|---|---|
| S1 | `ApprovedUnitProfileCatalog` | T02-a | Şirket başına birim profili (yalnız `kg`), miktar ölçeği; sabit şirket kimlikleri burada tanımlı | `ISettingsPrerequisiteProvider` | Yok | **A — yalnız derlenmiş** |
| S2 | `ApprovedSettingsUnitIdentityCatalog` | T02-a (S1'den türetilir) | Birim kimliği/kodu, boyut, açık kilogram kimliği | `ISettingsUnitIdentityCatalogV1` | Yok | **A** (S1 ile birlikte taşınır) |
| S3 | `ApprovedMoneyProfileCatalog` | T02-b, ALS-D69 | Para birimi × işlem; işlem başına birim fiyat ölçeği | `ISettingsPrerequisiteProvider`, `ISettingsMoneyCandidateReader` | Yok | **A** |
| S4 | `ApprovedSalesPolicyCatalog` | SAT-D5, SAT-D2, SAT-D6 | Teslim toleransı (D5), iskonto limiti grupları (D2), para birimi başına sipariş onay eşiği (D6) | `ISettingsSalesPolicyProviderV1` | Yalnız D2: `SalesDiscountLimit*` tabloları + `settings.sales-discount-limits.manage` | **B — kısmen kalıcı** (D5 ve D6 derlenmiş) |
| S5 | `ApprovedSalesQuotePolicyCatalog` | SAT-D25, SAT-D234, SAT-D39 | Kayıp nedeni listesi, varsayılan teklif geçerlilik süresi | `ISettingsSalesQuotePolicyProviderV1` | Yok | **A** |
| S6 | `ApprovedEInvoiceScenarioCatalog` | SAT-D102, SAT-D120 | Şirketin e-Fatura senaryo kod listesi ve varsayılan senaryo; varsayılan boştur | `ISettingsEInvoiceScenarioPolicyProviderV1` | Yok | **A** (boş başlangıç) |
| S7 | `ApprovedTaxItemTypeDefaultCatalog` | SAT-D139 | Kalem tipi → vergi varsayılanı tabanı | `SettingsTaxMasterV1` | Var: `TaxItemTypeDefault*` + `settings.tax-master.manage` | **C — taban + kalıcı** |
| S8 | `ApprovedTaxBuyerSituationCatalog` | SAT-D158 | Alıcı durumu tabanı | `SettingsTaxMasterV1` | Var: `TaxBuyerSituation*` | **C** |
| S9 | `ApprovedTaxRateListCatalog` | SAT-D181 | Boş | `SettingsTaxRateListsV1` | Tablo sonraki bir dilimde | **D — boş, tablo bekliyor** |
| S10 | `ApprovedManufacturingProfileCatalog` | URT-D6/7/9/10 | StokERP değerleri; GDC/GSP/GIN için aynı; çok sayıda Guid sabiti | `ManufacturingParameterProfilesV1` | Var: `ManufacturingParameterProfile*` + prepare/approve/publish izinleri | **C** |
| S11 | `ApprovedManufacturingToleranceCatalog` | — | Boş | `ISettingsManufacturingToleranceProviderV1` | Var: `ManufacturingTolerance*` + prepare/approve/publish izinleri | **C** (boş taban) |

Settings'te katalog sayılmayan kalıcı alanlar zaten tablodan gelir: döviz kurları (`ExchangeRate*`, kur
izinleri), vergi kod listeleri (`TaxCodeList*`) ve stopaj eşikleri (`TaxWithholdingThreshold*`). Bunlar F-029'un
hedef modelinin içinde zaten örnek olarak duruyor.

**Sınıflar:**
- **A:** Değer yalnız kaynak kodda. Değiştirmek için kod PR'ı gerekiyor. F-029'un asıl işi bunlardır.
- **B:** Bir kısmı tabloya taşınmış. Kalan alt politikalar A gibi ele alınır.
- **C:** Kalıcı tablo var; derlenmiş katalog bugün sürüm 0 tabanı/fallback. URN-D5 gereği taban, değer değişmeden
  ve denetim kaydıyla bir kez tabloya aktarılır; sonra tek kaynak tablodur (§6, N4).
- **D:** Boş katalog, tablo sonraki bir dilime planlanmış. Yeni iş yok; plan aynen sürer.

## 3. Envanter — diğer modüller (sahibi ilgili şerit)

| # | Modül / dosya | İçerik | Değerlendirme | Önerilen sahip |
|---|---|---|---|---|
| O1 | Accounting `ApprovedAccountingSourceCatalog` (Store + Rows, migration `20261002063616`, `ApprovedAccountingSourceCatalogLifecycleV1`) | Muhasebe fişi kaynak kataloğu | Zaten kalıcı ve yaşam döngülü. F-029 açısından **örnek**; ek iş yok | Codex (Muhasebe) |
| O2 | Product `ProductInitialTechnicalDefinitionV1`, `ProductFamilyDefinitionRulesV2` | Beş yassı aile, S235JR, kaplamasız; aile tanım kuralları | Bunlar politika değil, **kayıt/şema verisi** (ürün tanımının yapısı). Şirkete göre değişen bir eşik ya da tolerans içermez. Ebat/kalınlık kg/m tablosu ayrıca B-09'da kalıcı tablo olarak planlı. F-029 kapsamına alınmaz | Claude (Ürün) |
| O3 | Documents `DocumentContentPolicy` | PDF/PNG/JPEG imza baytları, bilinmeyen içerik tipleri | **Teknik güvenlik doğrulaması**, işletme politikası değil. Yapılandırılabilir yapmak saldırı yüzeyi açar. F-029 kapsamına alınmaz. İzinli dosya tipleri ya da boyut sınırı ileride işletme seçimi olursa ayrı karar gerekir | Belgeler şeridi |
| O4 | Finance `PartyLedgerPolicy`, `FinanceAllocationPolicy`, `FinanceOpenItemSourcePolicy`, `ReversalProposalPolicy`, `CustomerInstrumentRiskPolicy` | Cari defter, kapama/eşleştirme, açık kalem kaynağı, ters kayıt önerisi, müşteri enstrüman riski | İncelenen dosyalarda şirkete göre değişen bir değer kataloğu görülmedi. Bunlar **algoritmadır**: ölçek birleştirme, belirteç uzunluğu sınırı (`TokenMaximumLength = 128`) gibi. Risk limiti gibi bir eşik varsa Finans şeridi kendi envanteriyle doğrulamalıdır (öneri P4) | Codex (Finans) |
| O5 | Sabit şirket kimliği kullanan fixture'lar: `AccountingOpenPeriodFixtureStore`, 5 Ürün `*FixtureStore`, `C01E2eFixtureWriterActivation`; tohum: Organization migration `20260831142045_AddCompanyFiscalRuntimeSeed` | Test ve E2E fixture'ları, şirket tohumu | Fixture kullanımı test kapsamındadır, sorun değildir. Tohum migration'ı şirketleri yazan meşru kaynaktır. Asıl risk, üretim kodunun (S1–S11) bu sabitlere bağlı olmasıdır (§5.5) | Her fixture'ın kendi şeridi |

## 4. Korunacak davranış (sürüm 0)

- Her A/B kataloğunun bugünkü içeriği, kalıcı tabloya **değiştirilmeden** "sürüm 0, kaynak: derlenmiş katalog +
  karar kimliği" olarak aktarılır. Aktarım tohum değildir; denetim kaydı olan, idempotent ve tek seferlik bir
  aktarımdır (A/B: URN-D2 + bu spec; C: URN-D5). Aktarımdan sonra derlenmiş katalog kaynak olmaz.
- Aktarımdan sonra sağlayıcı yanıtları **bayt bayt aynı** kalır. Kabul testi: aynı şirket, işlem ve tarih için
  derlenmiş ve kalıcı sağlayıcı aynı profil kimliğini, sürümünü, ölçeği ve geçerlilik aralığını döner (§7).
- Eksik kaynak yine **istisnadır**. Hiçbir sağlayıcı null, boş katalog, sıfır ölçek ya da uygun dönem uyduramaz
  (Settings README'deki V1 kuralı).
- Hiçbir yeni değer tahminle girilmez. Boş olan katalog (S6, S9, S11) boş kalır.

## 5. Hedef tasarım (öneri; uygulama Settings sahibinin K3 dilimidir)

### 5.1 Tablo şekli

Her politika için ayrı ve tipli tablolar kullanılır; tek bir genel anahtar-değer tablosu kurulmaz. Bu, mevcut
`SalesDiscountLimit*`, `ManufacturingTolerance*` ve `TaxItemTypeDefault*` örneğiyle aynıdır. Gerekçe: para, ölçek ve
tolerans gibi değerlerin açık türleri (`ExactPositiveDecimalV1`, `Money`, `Quantity`) ve DB kısıtları
(CHECK, benzersizlik) korunur. Genel bir JSON değer sütunu bu korumayı kaybeder ve AGENTS.md "açık türlerle temsil"
kuralına aykırı olur.

Her politika tablosunda ortak çekirdek sütunlar bulunur:

| Sütun | Anlam |
|---|---|
| `CompanyId` | Zorunlu. Global sorgu filtresi geçerlidir; yalnız UI'da filtrelenmez |
| `PolicyKey` / iş anahtarı | Örnekler: para birimi, işlem, limit grubu |
| `ProfileVersion` | İş sürümü (1, 2, …). Yayımlanmış bir sürüm **değişmez**; değişiklik yeni sürümdür |
| `ValidFrom` / `ValidTo` | `DateOnly`, iş tarihi; UTC kayıt zamanı değildir |
| `Status` | Draft → Published → Retired (silme yoktur; ayrı onay adımı yok, URN-D3) |
| `DecisionReference` | Kaynak karar kimliği (SAT-D5 gibi) veya değişiklik gerekçesi |
| `StateVersion` | İyimser eşzamanlılık sürümü (`ProfileVersion`'dan ayrıdır) |
| `PreparedBy/PublishedBy` + zaman | Sunucunun çözdüğü aktör ve zaman; istek yükünden gelmez |
| `SourceKind` | `CompiledBaselineV0` veya `UserMaintained` |

Tipli değer sütunları politikaya özgüdür. Örneğin teslim toleransı için oran/miktar ve tür, onay eşiği için para
birimi ve tutar kullanılır.

### 5.2 Komutlar ve yetki

Kritik komut iskeleti uygulanır (`k3-command-skeleton`): authorization, expected version, idempotency key,
correlation/causation, audit ve tek kayıtta deneme + denetim. Önerilen eylemler, üretim profili örneğiyle aynıdır:
`settings.<politika>.read | prepare | publish` (URN-D3 gereği ayrı bir `approve` adımı yoktur).

- **URN-D3:** değeri ilgili alanın yetkili müdürü tek başına hazırlar ve yayımlar; ikinci onaylayıcı aranmaz. Her
  değişiklik denetim kaydı bırakır. Durum akışı Draft → Published → Retired olur. URN-D6 gereği S10/S11 de tek
  akışa (hazırla → yayımla) iner. Mevcut hazırla/onayla kayıtları tarihçe olarak kalır. Geçiş F029-C dilimiyle
  yapılır (ayrı K3).
- İzin adları kayıt altına alınır, ama **hiçbir role grant edilmez**. Grant/rol/Critical insan kapısıdır
  (`permission-capability-registration`).
- **URN-D4:** yayımlama geriye dönük değildir; `ValidFrom` bugünden önce olamaz (komut ve DB tarafında reddedilir).
- **URN-D2:** para profili ve birim profili de ekrandan yönetilir; bu iki profilde ekran, değişikliği kaydetmeden önce
  etkisini gösterir.
- Tüketiciler (Satış, Alış, Üretim, Finans) uygulanan profil kimliğini ve sürümünü kendi komut kayıtlarında snapshot
  olarak saklar. Bu zaten V1 kuralıdır. Değer değişince geçmiş ve onay bekleyen belgeler **yeniden hesaplanmaz**
  (URN-D4).

### 5.3 Okuma portları

Mevcut `ISettings*ProviderV1` arayüzleri **değişmeden** kalır. Kalıcı sağlayıcı aynı portun arkasında derlenmiş
kataloğun yerini alır. Bu, katalog yorumlarında zaten öngörülen yoldur. Sözleşme değişmediği için tüketici
modüllerde kod değişmez. Değişirse V2 port açılır ve V1 korunur.

### 5.4 Geçiş sırası (önerilen dilimler; her biri ayrı K3 PR)

1. **F029-S4b:** satış teslim toleransı (SAT-D5) ve onay eşiği (SAT-D6) kalıcı hale gelir. D2 tablosu örnek
   alınır; risk en düşük adımdır.
2. **F029-S5/S6:** teklif politikası ve e-Fatura senaryosu.
3. **F029-S3:** para profili. Birim fiyat ölçeği para hesabını etkiler; K3 kapsamındadır ve iki review gerekir.
4. **F029-S1/S2:** birim profili ve birim kimliği. Ürün B-07/B-08/B-09 tüketir; bunun için Ürün şeridiyle sözleşme
   gerekir (öneri P2).
5. **F029-C:** C sınıfı tabanların değer değişmeden, denetimli tek seferlik aktarımı (URN-D5); S10/S11'de
   hazırla/onayla/yayımla akışının hazırla → yayımla tek akışına indirilmesi (URN-D6).

### 5.5 Sabit şirket kimlikleri

- Üretim kodundaki derlenmiş kataloglar şirkete `GdcCompanyId` ve benzeri sabitlerle bağlıdır. Kalıcı tabloya
  geçince şirket, Organization'ın şirket kaydından gelir. Katalog satırı `CompanyId` ile anahtarlanır, kodda şirket
  kimliği kalmaz. Bu, AGENTS.md'deki "hard-coded CompanyId kullanma" kuralını karşılar.
- Geçişte sürüm 0 aktarımı, bugünkü üç şirketin kimliklerini **Organization kaydından okuyarak** eşler. Organization'da
  karşılığı olmayan bir sabit varsa aktarım durur ve hata verir.
- Test fixture'ları sabit kimlikleri kullanmaya devam edebilir; bu F-029 kapsamı dışındadır.

### 5.6 Kapsam dışı

- Kiracı (şirket üstü) katmanı. F-029 da şimdi önermiyor. Tasarım engel de çıkarmaz: tüm satırlar `CompanyId`
  taşır ve şirketler arası paylaşılan bir global satır kullanılmaz.
- Settings yönetim ekranları (Claude frontend şeridi; kalıcı sağlayıcı ve izinler merge olduktan sonra).
- Yeni bir politika değeri, eşik veya tolerans seçmek.

## 6. Açık noktalar ve kararlar

Sorular Issue #66'ya NEEDS-SINAN olarak yazıldı
([6819528621](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6819528621)); N1, N2, N3 ve N5 Sinan'ın 9.
tur kararıyla ([6820161788](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6820161788)), N4 10. tur
kararıyla ([6820237902](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6820237902)) cevaplandı ve
`docs/decisions/SINAN_KARARLARI_2026-11-12_URUN.md` içinde URN-D2–URN-D5 olarak kayıtlıdır. N6 sonradan sorulmuş
([6820502714](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6820502714)) ve 14.–15. tur kararıyla
([6820838938](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6820838938) m.6) URN-D6 olarak cevaplanmıştır.

| No | Soru | Durum |
|---|---|---|
| N1 | Hangi kataloglar kullanıcı tarafından yönetilebilir olsun? | **Karar (URN-D2):** hepsi ekrandan, para ve birim profili dahil. Para/birim profilinde ekran etkiyi değişiklikten önce gösterir. |
| N2 | Değişikliği kim yapar ve onaylar? | **Karar (URN-D3):** ilgili alanın yetkili müdürü tek başına değiştirir ve yayımlar; ikinci onay yok; denetim kaydı var. İzin adları kaydedilir, grant insan kapısıdır. |
| N3 | Yayımlanan bir değer geçmiş tarihten geçerli olabilir mi? | **Karar (URN-D4):** hayır; yalnız bugünden ileri. |
| N4 | C sınıfı tabanlar (S7, S8, S10, S11) tabloya tek seferlik "sürüm 0" olarak aktarılsın mı, yoksa derlenmiş fallback olarak mı kalsın? | **Karar (URN-D5):** değer değişmeden, denetim kaydıyla bir kez tabloya aktarılır; sonra tek kaynak tablodur. |
| N5 | Değer değişince bekleyen belgeler yeniden değerlendirilsin mi? | **Karar (URN-D4):** hayır; onay bekleyen belgeler eski değerle kalır. |
| N6 | URN-D3 mevcut üretim parametre/tolerans profillerinin hazırla/onayla ayrımına da uygulansın mı? | **Karar (URN-D6):** evet; üretim profilleri de tek akış (yetkili müdür hazırlar ve yayımlar, ikinci onay yok, denetim kaydı). Mevcut hazırla/onayla kayıtları geçmiş olarak kalır, geriye dönük işlem yok. |

Bütün sorular cevaplanmıştır. Bütün dilimler (F029-C dahil) URN-D2–D6 ile açılabilir; her dilim yine ayrı K3
PR'dır. F029-C, S10/S11'de değer aktarımına ek olarak akışı da teke indirebilir; mevcut `approve` izni ve kayıtları
forward-fix ile emekliye ayrılır, silinmez; grant değişikliği insan kapısıdır.

## 7. Port / sözleşme önerileri (ilgili şeritlere)

| No | Hedef şerit | Öneri |
|---|---|---|
| P1 | Codex — Settings (sahip) | §5.1–5.4'ün uygulanması. İlk dilim F029-S4b. Kabul testi: derlenmiş ve kalıcı sağlayıcı eşdeğerliği (aynı girdi → aynı profil kimliği, sürüm, değer, ölçek, geçerlilik), gerçek SQL ile; `CompanyId` yalıtım testi; eksik kaynakta istisna. Migration'lar forward-fix kuralıyla yazılır (`ef-migration-forward-fix`) |
| P2 | Codex — Settings ↔ Claude — Ürün | Birim profili kalıcı hale geldiğinde `ISettingsUnitIdentityCatalogV1` ve `ISettingsPrerequisiteProvider` imzaları aynı kalır. Ürün, birim dönüşüm hesaplayıcısında (B-07/B-08) Settings birim kimliğini snapshot'lar ve kendi tarafında sabit şirket veya birim kimliği tutmaz |
| P3 | Codex — Satış/Alış tüketicileri | Uygulanan politika profil kimliği ve sürümü belge satırında saklanır (mevcut V1 kuralı). F-029 geçişinde tüketici kodu değişmez. Değişirse V2 port açılır |
| P4 | Codex — Finans | O4 politikalarında şirkete göre değişen bir eşik (risk limiti, eşleştirme toleransı) varsa envantere eklenir ve aynı Settings modeliyle sunulur. Finans kendi tablosuna politika değeri yazmaz; Settings portu üzerinden okur (modül sınırı) |
| P5 | Codex — Muhasebe | `ApprovedAccountingSourceCatalog` kalıcı örnek olarak korunur. Ortak çekirdek sütunlarla (§5.1) uyum ileride değerlendirilir; zorunlu değildir |
| P6 | Belgeler şeridi | `DocumentContentPolicy` teknik doğrulama olarak kod içinde kalır. İzinli tip veya boyut sınırı bir işletme seçimi olursa ayrı karar ve ayrı sözleşme gerekir |

## 8. Doğrulama

- Bu PR yalnız doküman değiştirir. Yerel hafif kontroller PR gövdesinde kayıtlıdır.
- Envanter `origin/main` `58b96aa` üzerinde `grep` ile çıkarılmıştır:
  - `class Approved*Catalog`;
  - `GdcCompanyId|GspCompanyId|GinCompanyId`;
  - `"settings.*"` izin adları.
- Kod değişince envanter yeniden çıkarılmalıdır.
