# Recovery ve parmak izi pin'lerinin PR'ları kilitlemesi — analiz ve öneri v1

- **Tarih:** 2026-11-07
- **Durum:** Öneri. Kod değişikliği yok. **Karar Sinan'da.**
- **Hazırlayan:** Kalan Modüller şeridi (Claude)
- **Kaynak commit:** `origin/main` `1e694aa3e` (#929)

**Kısaca (Sinan için):** Bütün backend'in tek bir "parmak izi" değeri her merge'de değiştiği için açık
PR'ların hepsi her seferinde yeniden hazırlanmak zorunda kalıyor. Öneri: bu tek değeri, yalnız riskli
dosyaları (veritabanı, ham SQL, build ayarları) kapsayan dar bir değerle değiştirmek. Ama önce "merge
eden herkes, PR'ın güncel main'i içerdiğini yazılı olarak kontrol eder" kuralını getirmek; çünkü bugünkü
değer bu güvenceyi de dolaylı olarak sağlıyor.

## 1. Sorun

Main'e bir backend PR'ı girince, açık her backend PR'ı main'i alıp pin değerlerini yeniden üretmek,
CI'ı yeniden koşturmak ve (K3'te) yeniden review almak zorunda kalıyor. Ölçüm (`1e694aa3e`): 20 Ekim'den
beri main'e giren 75 commit'in 21'i `src/backend/`'e dokunuyor ve bu 21'in **hepsi**
`tools/generate_recovery_surface_inventory.py`'yi de değiştirmek zorunda kalmış (commit commit kontrol
edildi). Aynı aralıkta commit gövdelerinde 30 "re-pin" satırı var. Sonuç: PR'lar fiilen tek sıra
halinde merge ediliyor. Desen `docs/governance/ORTAK_DESENLER_v1.md` P1/P2'de de kayıtlı.

## 2. Pin'ler neden var ve hangisi gerçekten "insan onayı" taşıyor

**(a)** = ağaçtan mekanik türetilen özet (değeri bir araç basar, insan kopyalar). **(b)** = incelenmiş
izin listesi (bir satırın eklenmesi, reviewer'ın bilerek "evet" demesini gerektirir).

| Pin | Ne yakalar | Sınıf | Çakışma |
|---|---|---|---|
| `EXPECTED_SOURCE_SURFACE_FILE_COUNT` / `_FINGERPRINT` | `src/backend/**` ve build dosyalarının her baytı. Generator gerekçesi (satır 57–60): bağlı dosya, source generator, global alias veya isim gölgeleyen dosya, sözcüksel envanteri atlatmasın. | (a) | **Her backend PR'ı** |
| `EXPECTED_MIGRATION_*`, `EXPECTED_DB_CONTEXT_*`, `EXPECTED_DURABLE_STORE_COUNT` | İncelenmemiş migration, DbContext veya depo | (b) | Migration ekleyen PR'lar |
| `REVIEWED_OPERATIVE_METHOD_FINGERPRINTS`, `REVIEWED_LIVE_SOURCE_SHA256`, `EXPECTED_AUDIT_LIVE_SOURCE_*` | İncelenmiş store metodu, dosyası veya Audit kodunun sessizce değişmesi | (b), dosya/modül başına | Aynı store'a dokunan PR'lar |
| `PlatformTenantQueryFilterBypassGuardTests.cs` sözlükleri | Şirket filtresini atlayan ham SQL, DbContext, `migrationBuilder.Sql`; dosya yolu ve API sayısı reviewer gerekçesi ister | (b), dosya başına | Aynı sözlükteki komşu satırlar |
| `tools/relational_inventory/<modül>.txt` + `ReviewedRelationalInventoryFingerprints` | SQL kabul testlerinin sessizce düşmemesi | (b) liste, modül başına (a) hash; pin satırları tek dosyada ama ayrı paragraflarda | Düşük |
| `ShellCapabilityRegistryAuthorities.cs` | Kabul edilmiş shell bileşimleri | (a) değer, (b) sağlayıcı sürümü | Sağlayıcı sürümü artınca dokuz satırın hepsi |

**Gözlem 1.** İnsan kararı taşıyan pin'ler (b) satırları. Bunlar dosya veya modül başına tutuluyor ve
nadiren çakışıyor. Her PR'ı kilitleyen ise (a) türü global bayt özeti. Bu değer her backend PR'ında
değiştiği için diff'te sinyal taşımıyor; reviewer yalnız aracın bastığı yeni değeri görüyor.

**Gözlem 2 (tazelik).** `ci.yml`'deki derleme ve test işleri (`backend-build`, `backend-test-group`,
`relational-*`, `contract-drift`, `frontend`, `containers`) checkout'ta `ref` vermiyor. Yani CI anındaki
`refs/pull/N/merge` ağacını, main ile birleşmiş hali test ediyorlar. Yalnız `recovery-surface-inventory`
ve `release-manifest` işleri PR head'ini checkout ediyor. Boşluk şu: CI koştuktan sonra main ilerlerse,
o birleşik sonuç bayatlıyor. Bu durumda tek başına yeşil olan iki PR, birlikte hiç test edilmemiş bir
main üretebilir. Global pin bu boşluğu bugün dolaylı olarak kapatıyor, çünkü main ilerleyince pin tutmuyor
ve PR'ın güncellenmesi zorunlu oluyor. Pin kaldırılırsa bu güvence **ayrı bir kuralla** korunmalı.

## 3. Seçenekler

| Seçenek | Kazanç | Kayıp / risk | Kapı |
|---|---|---|---|
| **A. Modül başına pin dosyaları** (guard'da `partial` deseni zaten var) | Çakışma yalnız aynı modüle dokunan PR'lar arasında; güvenlik kaybı yok | Ortak `GoymenERP.Api` parçası yine sık çakışır; global toplam kalırsa kazanç olmaz | K3, iki review |
| **B. Merge sonucunda zorunlu CI** (merge queue) | Tazelik tam çözülür | Merge queue büyük ihtimalle kullanılamaz, çünkü depo kullanıcı hesabına ait; `merge_group` ve Settings ayarı gerekir | `.github` K3 + Settings **insan kapısı** |
| **C. Merge sonrası bot commit'i** | Şeritler pin'le uğraşmaz | `contents: write` token gerekir; CI testleri bunu reddediyor. `GITHUB_TOKEN` commit'i CI tetiklemez, PAT ise secret demek. Bot review'suz main'e yazar. | Secret = **insan kapısı**; **önerilmez** |
| **D. Global bayt pin'ini dar riskli-yüzey pin'iyle değiştirmek** (§4 Faz 1) | Sıradan PR pin'e dokunmaz; (b) pin'lerin hepsi kalır | Tazelik yan faydası kaybolur (Gözlem 2); dar kümenin dışında kalan girdiler var (§4 tablo) | K3 iki review + Soru 1 |
| **E. `merge=union`** (sıralı test listeleri) | Yerel çakışma azalır | Sıra veya silinen satır bozulabilir; doğrulayıcı fail-closed reddeder. GitHub'ın bunu uygulayıp uygulamadığı doğrulanmadı. Hash dosyalarında **asla** kullanılmaz. | K1/K2, düşük değer |

## 4. Öneri — fazlı

**Faz 0 — süreç kuralı (governance PR, iki review).** Merge eden **herkes** (koordinatör ve Claude
şeridi) expected-head squash'tan hemen önce `git fetch origin main && git merge-base --is-ancestor origin/main <head>`
çalıştırır ve kullandığı main SHA'sını ve exit code'u PR'a yazar. Fresh zorunlu CI **aynı head** üzerinde
yeşil olmalıdır. Ancestor kontrolü tek başına yetmez: birleşik ağacın test edildiğini, CI'ın o head'de
koşmuş olması gösterir. Bunun kalıcı kural olması `AGENTS.md` / `review-signatures` güncellemesi demektir.
Bu da `docs/governance/**` PR'ı ve iki review ister; yani "kapısız" bir adım değildir.

**Faz 1 — K3 kod PR'ı, iki review; ancak Faz 0 merge edildikten sonra.**
1. `EXPECTED_SOURCE_SURFACE_*` kaldırılır. Yerine dar pin gelir: aşağıdaki dosya kümesinin (yol + bayt)
   hash'i. Küme miras ilişkisine göre değil, token ve dosya sınıfına göre **üst küme** olarak tanımlanır:
   - `REPOSITORY_BUILD_CONTROL_PATHS`'in tamamı (`.editorconfig`, `.globalconfig`, `*.rsp`,
     `Directory.Build.*`, `Directory.Packages.*`, `Directory.Solution.*`, `NuGet.Config`, `global.json`),
   - `src/backend/**/*.{csproj,props,targets,rsp}`,
   - canlı kodunda `DbContext`, `ModelBuilder`, `MigrationBuilder`, `Migration`, `IDocumentObjectStoreV1`,
     `SqlConnection`, `SqlCommand`, `Microsoft.Data.SqlClient` veya `ToTable` geçen her `src/backend/**/*.cs`,
   - `using X =` içeren her dosya (global ve dosya-yerel alias),
   - incelenmiş bir tipin (DbContext, store) `partial` bildirimini taşıyan her dosya.
2. Kabul ölçütüne negatif testler eklenir: incelenmiş bir context veya store için yeni `partial` dosyası
   açılırsa ya da yeni bir dosyada gizli `SqlClient` veya DbContext tanımlanırsa generator reddeder.
3. (b) pin'leri ve pin'den bağımsız kontroller aynen kalır: exact-commit blob okuma, executed-generator
   bayt eşliği, `_require_generator_index_flags_clean` (gizli Git index bayrakları),
   `_require_no_worktree_source_shadows`.
4. Tam bayt özeti envanter ve manifest çıktısına `sourceSurface` olarak yazılır. Bu **engelleyici değildir**;
   yalnız sonradan inceleme kanıtıdır.

| Generator'ın korktuğu | Faz 1'den sonra hangi kontrol yakalar |
|---|---|
| Bağlı dosya (`Compile Link`) | Guard, csproj öğelerini ve desteklenmeyen kaynak uzantılarını denetliyor (`ValidateNoUnsupportedBackendInputs`); csproj dar pin'de |
| Source generator | Guard `Analyzer`, `AdditionalFiles`, `CompilerVisibleProperty` ve benzeri öğeleri yasaklıyor (`ForbiddenGeneratedSourceItems`); build dosyaları dar pin'de |
| Global alias | `using X =` içeren her dosya dar pin'de; generator `extern alias` ve `global::` dışındaki alias niteleyicilerini reddediyor (satır 1137–1141) |
| İsim gölgeleme / gizli bypass | Guard her `src/backend/**/*.cs` dosyasını tarıyor (`ReadBackendSourceInventory`); hedef token'ları geçen dosyalar ve `partial` dosyaları dar pin'de |

**Kapsanmayan girdiler (açıkça):** `appsettings*.json`, `.resx` ve gömülü kaynaklar, `.cs` dışındaki
metin dosyaları. Bunlar derlenen kodun şeklini değiştirmez ama davranışı etkileyebilir; bugün de yalnız
global pin yakalıyor. Bunlar da dar kümeye alınabilir (az değişirler); Faz 1 PR'ında karar verilir.

**Faz 2 — K3.** (b) pin'leri modül dosyalarına bölünür; migration sayısı modül başına tutulur, toplamı CI
hesaplar. Shell registry'de bileşim hash'i testte türetilir, sağlayıcı sürümü pin olarak kalır.

**Faz 3 — yalnız Sinan isterse.** Settings'te "dal main ile güncel olmalı" ayarı veya merge sonucunda
zorunlu CI. Settings değişikliği insan kapısıdır, `.github` değişikliği K3. Seçenek C önerilmez.

## 5. NEEDS-SINAN soruları

1. **NEEDS-SINAN: Bütün backend'in tek bayt özetini sabitleme şartını, riskli dosyaları kapsayan dar bir
   özetle değiştirelim mi?** — Önerilen cevap: **Evet, ama Faz 0 kuralı yürürlüğe girdikten sonra.**
   Kaybedilenler şunlar: kendiliğinden gelen "PR main ile güncel" güvencesi (Faz 0 bunu yazılı kurala
   bağlıyor) ve dar kümenin dışındaki dosyalardaki değişikliklerin (ör. `appsettings`, `.resx`)
   otomatik yakalanması. Migration, veritabanı bağlamı, ham SQL, build ayarı ve incelenmiş metotlar
   yine insan onayı ister.
2. **NEEDS-SINAN: GitHub Settings'te "Require branches to be up to date before merging" açılsın mı?**
   — Önerilen cevap: **Şimdilik hayır.** CI zaten main ile birleşik hali test ediyor; eksik olan, CI'dan
   sonra main ilerlerse sonucun bayatlaması. Bunu, merge eden herkesin squash'tan hemen önce yaptığı yazılı
   "head main'i içeriyor + aynı head'de yeşil CI" kontrolü kapatır. Sorun sürerse ayarı siz açarsınız.
3. **NEEDS-SINAN: Main'e otomatik commit atan, yazma yetkili bir bot ister misiniz?** — Önerilen cevap:
   **Hayır.** Secret gerektirir, attığı commit CI'dan geçmeden ve review'suz main'e yazılır; Faz 0–2 aynı
   rahatlığı token olmadan sağlar.
