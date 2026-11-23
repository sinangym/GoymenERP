# Codex devam promptu — Tren-1 kapanışı, Tren-2 (Work + Yapay Zekâ) ve sonrası

> Bu dosya 23 Kasım 2026'da yazıldı. **Bu dosya bir başlangıç haritasıdır; güncel gerçek depodadır.** Her adımdan önce
> aşağıdaki "Güncel durumu okuma" bölümündeki komutlarla dalların, PR'ların ve karar dosyalarının son hâlini oku. Bu
> dosyayla depo çelişirse depo, depodaki tarihli `SINAN_KARARLARI_*.md` ise her şeyden önce gelir.

## 0. Rolün ve sınırların

Sen GöymenERP'nin geliştirmesini sürdüren ajansın. Repo: `sinangym/GoymenERP`. Çalışma dili: kullanıcıyla kısa Türkçe;
kod ve teknik isimler İngilizce, kullanıcı metinleri Türkçe ve ayrı dosyada.

Her işe başlamadan oku (bu sırayla):
1. `AGENTS.md` (repo kuralları, merge kapıları, Faz 0 main tazelik kontrolü, insan kapıları, yasaklar)
2. `docs/decisions/SINAN_KARARLARI_*.md` — en yenileri, özellikle `SINAN_KARARLARI_2026-11-22_EK_SORULAR.md`
   (E-01…E-94; SAT-D462…D477, KIM-D57/D58, FNS-D113…D127, ALS-D125…D128, KLN-D120, YOL-D22…D43, AI-D35…D50,
   ROL-D21…D25, GOV-D14…D19), `SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR.md`, `…_ROL_YETKI.md`, `…_AI.md`
3. `docs/decisions/KARAR_SICILI.md`, `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`
4. `docs/governance/` altındaki hızlı teslim (v4/v3/v2) ve orkestratör dosyaları
5. `docs/delivery/WORK_AI_TAMAMLAMA_PLANI_v1.md` (Work+AI paket planı, THROW aralıkları, sıcak dosyalar)
6. `docs/delivery/WORK_AI_ORTAK_KURALLAR.md` (tren-2 dalında; paket oturumu kuralları)
7. `.claude/skills/*/SKILL.md` — özellikle: k3-command-skeleton, ef-migration-forward-fix, recovery-inventory-repin,
   architecture-guard-repin, openapi-regenerate, permission-capability-registration, frontend-screen-states,
   test-naming-and-groups, decision-docs-validation, main-merge-resync, review-signatures

Değişmeyen sınırlar (AGENTS.md "insan kapıları" ve "yasaklar"):
- Rol/grant ataması, Critical işaretleme, deployment/secret/environment, workflow etkinleştirme, production aktivasyonu
  ve `OPEN-*` kapatma **her zaman Sinan'ın somut kararıdır**. İzin kaydetmek serbest; bir role/seed'e vermek değil.
- Para/stok/üretim/formül/yetki/saklama politikası **tahminle seçilmez**: kararlı değilse fail-closed bırak ve
  Sinan'a önerili soru sor (`NEEDS-SINAN`). Soruyu sormadan önce kilitli kararları ara (aynı konuda karar varsa sorma).
- Test atlama/silme/karantina/iddia gevşetme yok. CI'ı tetiklemek için boş commit, re-run, kapat-aç yok.
- Kendi yazdığın işe K3 review imzası atamazsın; review bağımsız oturumlarla yapılır.
- GitHub metinlerine model kimliği, oturum kimliği, yerel yol, e-posta yazma.
- Yapay zekâ özellikleri kapalı bayrakla teslim edilir (Ai:Enabled=false); gerçek sağlayıcı/anahtar yok (YOL-D21/D43).

## 1. Güncel durumu okuma (her oturumun başında)

```bash
git fetch origin
git log --oneline -5 origin/main
git log --oneline -15 origin/claude/tren-2
git branch -r | grep -E 'claude/(wp|wpa|wpb|wpc|k3fix|tren)' | sort
git log --oneline origin/main..origin/claude/tren-2 | wc -l
```
- GitHub MCP/CLI ile açık PR'ları listele (`claude/tren-2` için PR açılmış mı, review/CI durumu).
- Issue #1189: merge makbuzları, `NEEDS-SINAN` / `SINAN-CEVAP` satırları buraya yazılır (Issue #66 doldu).
- `docs/decisions/SINAN_KARARLARI_2026-11-22_EK_SORULAR.md` dosyasının son E- numarasına bak; bu dosyadan sonra yeni
  kararlar eklendiyse onları uygula.

## 2. Tren-1 — TAMAMLANDI

- PR #1201 (`claude/tren-1`) main'e squash: `9e6107ff3` (23 Kasım 2026). İki bağımsız exact-head K3 APPROVE
  (review 6020705503, 6020757132), CI 45140110590 yeşil, Faz 0 tazelik exit 0, #1189 makbuzu yazıldı.
- PR #1207 (karar kayıtları E-80…E-82) main'e: `ddc6cba4e`.
- İçerik: adet/kg paketi (AK-1…AK-8) + formül golden'ları (GOV-D19: golden ile kod aynı trende), K3 tam kapsam
  düzeltmeleri, 22–23 Kasım kararları.
- Tren-1'de öğrenilen ders: inceleme turlarının her biri yeni bulgu çıkardı. Reviewer'a **en baştan tam kapsam**
  (tüm farkı satır satır okuma) şartı ver; onay şartı: tam kapsam + aynı head'de yeşil CI + açık P0/P1/P2 yok.

## 3. Tren-2 — DEVAM EDİYOR (Work + Yapay Zekâ)

**Amaç (Sinan, 22 Kasım):** Work ve yapay zekâ kararlardaki kapsamın tamamıyla kodda tamamlansın; tren-2 bittiğinde
yalnız birleştirme testleri kalsın. Etkinleştirme (sağlayıcı, bütçe, rol ataması, hukuk onayı, canlı) insan kapısında.

**Entegrasyon dalı:** `claude/tren-2`. Paketler kendi dallarında yazılır, tren-2'ye merge edilir; pin'li dosyalar
yalnız tren-2 head'inde en sonda üretilir (GOV-D8/D9).

### 3.1 Tren-2'ye birleşmiş / birleşmekte olan paketler
Durumu `git log origin/claude/tren-2` ve `git branch -r --merged origin/claude/tren-2` ile doğrula.
- Dalga 1: W1-A (iş kutusu), W1-B (komut paleti), W1-C (görev/not/bildirim), W1-D (rol taslağı)
- WP1 kural tablosu + tek kutu · WP2 hatırlatma/yükseltme/tek bildirim deposu (Work şeması) · WP4–WP7 kalan onay
  türleri (Satış, Finans/Muhasebe, Stok/Lojistik/Üretim, Satınalma/Ayarlar/Kimlik/Cari/Ürün) · WP9 not/görev (+mal
  kabul, YOL-D42) · WP11 "bu sayıyı açıkla" · WP12 banka ekstresi + cari IBAN listesi (FNS-D113…D122) · WP13 belge
  PDF'leri + imzalı sözleşme (SAT-D470/D471) · WP14 rapor merkezi (OTIF, yaşlandırma, grup içi, denetçi paketi) ·
  WP15 mükerrer fatura + yasal süre takvimi · WP16 güvenlik taraması + rol matrisi
- AI: Y-A1 karar dizini · WP-A1 ağ geçidi + bütçe defteri · WP-A2 değerlendirme koşucusu (%10 insan örneklemi) ·
  WP-A3a/b/c okuma araçları · WP-A4 bilgi kaynakları (grup genelinde onay) · WP-A5 yönetim ekranı · WP-A6 dış metin
  karantinası + araç kartı pin'i · WP-B3 belgeden taslak · WP-C1 onaylı yazma hattı (ikinci onay + şifre) · WP-C6
  kişisel hafıza (1 yıl, 200 kayıt) · WP-C10 güvenlik olayı aracı
- Doküman dalı `claude/tren2-dokumanlar`: rol taslağı v1.4.1 (ROL-D24/D25 önerileri), hafıza bilgilendirme metni
  taslağı (AI-D50), kredi grubu alan listesi v1.2 ONAYLANDI (FNS-D124…D127). **Tren-2'ye henüz merge edilmediyse et.**

Not: 23 Kasım öğleden sonra main + 12 paket dalının (WP2, WP4, WP5, WP6, WP7-2, WP9, WP12-3, WP14-3, WP-A2-2,
WP-A3c-2, WP-A4-2, WP-C6-3) tren-2'ye birleştirilmesi sürüyordu. Son durumunu `git log origin/claude/tren-2` ile kontrol
et; eksik kalan dal varsa birleştir (kurallar: §5.2).

### 3.2 Henüz yapılmamış paketler (tren-2 kapsamını tamamlamak için)
`docs/delivery/WORK_AI_TAMAMLAMA_PLANI_v1.md` §3'teki tanımlarla:
- **WP3** vekâlet (süreli, zincirsiz, yalnız izne sahip kişi; YOL-D17, ROL-D4; başta kapalı) — K3
- **WP8** kutudan karar (sahip komutu, expected version, idempotency, yetki yeniden kontrol) + Work yönetim ekranı
  (kurallar, vekâletler, geciken, sahipsiz). YOL-D41: açılış maliyeti ve kasa hesabı onayları ekransız, kutudan karar
- **WP10** istisna öncelikli ana sayfa + istisna bazlı inceleme altyapısı (eşik yokken kapalı; AI-D34 b)
- **WP-B1** ekran bağlamlı panel, kaynaklı cevap, "neden takıldı" zinciri
- **WP-B2** onay kartında özet ve risk işareti; öneri bütün türlerde kapalı başlar; "katılmıyorum" + neden kodu (AI-D26)
- **WP-B4** dar doğal dil sorgu (tanımlı ölçütler, sayı yalnız araçtan; marj köprüsü anlatımı)
- **WP-B5** genel sohbet modu (ERP araçları kapalı)
- **WP-C2** ilk komut ailesi + "ekranı aç ve doldur" (WP-C1 hattı üzerinden; önce C1 kalıcı defteri)
- **WP-C3** konuşarak ayar değişikliği ve yetki talebi taslağı (atama insanda)
- **WP-C5** zamanlı görevler ve rol başına sabah özeti; dört etki ayrımı (AI-D33); yetki kalkınca durur
- **WP-C7** e-posta: toplu onaylı giden kutusu, gelen yanıt okuma iskeleti (hesap bağlanana kadar kapalı)
- **WP-C8** salt okunur dış bağlantı iskeleti, 3–5 hassas olmayan araç, kullanıcı bazlı kimlik
- **WP-C9** öğrenme kaydı: kabul, düzeltme, ret, sonradan geri alma (WP-A5 kabul sayıları bunu bekliyor)
- **C1 kalıcı defter:** WP-C1 yazma hattı defteri hâlâ `UnconfiguredAiCommandLedger`; ayrı AI şeması/tablosu
  (Work değil), CompanyId filtreli, Version CAS'lı, olay tablosuyla aynı işlemde
- **YOL-D40:** bayatlayan SMM düzeltme önerisi Muhasebe'de "bayatladı" ile kapanır + denetim + hazırlayana bildirim.
  Gerekenler: Muhasebe migration (öneri kapanış tablosu + Muhasebe outbox), Work'te yeni bildirim türü + tüketici
  (Work şeması). WP2 tren-2'ye girdikten sonra yapılabilir. THROW 52140–52149.
- **Kredi grupları (FNS-D124…D127):** `FinanceCreditGroupGateV1` şu an `NoneApproved`; dört grubu onaylı aç ve
  `docs/features/FINANCE_CREDIT_GROUP_FIELDS_DRAFT_v1.md` v1.2 alan listesini uygula (leasing: anapara+faiz+KDV+dahil
  masraf; KDV eşleme anahtarı). "KDV anahtarı yokken fiş önerisi bekler" kuralı FNS-D97 çıkarımı — Sinan'a teyit sor.
- **C4** Dalga 4 golden'ını bekler — kapsam dışı.
- Küçük açıklar: plan üstü sevk reddi için özel Türkçe metin (WP6); cari bakiye devrinde replay'de
  `Idempotency-Original-Correlation-Id` (migration ister); farklı talimatlarda aynı ShareId yeniden kullanımında
  maliyet üretimi kilidi (global tekillik; karar + migration).

Paralellik: aynı anda en fazla 7–9 paket. Work şemasını aynı anda tek paket değiştirir. Her paket kendi THROW
aralığını kullanır (planın §4'ü; 52073 ve 52300 tren-1'de kullanıldı, çakıştırma).

### 3.3 Tren-2 kapanış adımları
1. Bütün paket dallarını tren-2'ye merge et; snapshot'ları EF ile modelden yeniden üret (§5.3).
2. Karar dizinini üret: `contracts/ai/decision-index-v1.json` (eski biçimde kalırsa Ai testleri "Decision index
   missing" ile kırmızı). Araç kartı pin'lerini (`contracts/ai/tool-catalog-v1.json` descriptionSha256) Ya1 katalog
   testinin çıktısından doldur.
3. Pin'leri sırayla üret: OpenAPI → shell authority → guard fingerprint + relational inventory → recovery + release
   (§5.4).
4. Yerel tam test: bütün `*.UnitTests`, ArchitectureTests 405+, Api.IntegrationTests, gerçek SQL (docker mssql 2022),
   frontend `tsc -b` + lint + vitest. Paketlerin yazıp koşmadığı çok sayıda SQL testi var: hepsini koş.
5. `claude/tren-2` → main PR (draft), tam CI.
6. İki bağımsız exact-head K3 review (tam kapsam). Bulguları düzeltme oturumlarına böl, yeni head, CI, delta review.
7. Main tazelik kontrolü (AGENTS Faz 0), squash merge, #1189 makbuzu.

## 4. Sinan'ın açık / bekleyen kararları
- Yapay zekâ aylık bütçesi: sağlayıcı karşılaştırması bitince (YOL-D21/D43).
- Hafıza bilgilendirme metni: hukuk onayı (AI-D50); onaylanınca sürüm numarası girilir.
- Rol ataması: rol taslağındaki öneriler (ROL-D24/D25) atama değildir; atama Sinan'ın rol ekranından.
- Leasing KDV "eşleme eksik" kuralının teyidi (FNS-D97 çıkarımı).
- Her yeni `NEEDS-SINAN` için: önce kilitli kararları ara; yoksa Sinan'a önerili seçenekli soru sor, cevabı
  `docs/decisions/SINAN_KARARLARI_2026-11-22_EK_SORULAR.md`'ye yeni E- satırı + karar satırı olarak işle (biçim:
  mevcut satırlar), hafif kontrolleri koş (`python3 -I tools/validate_delivery_backlog.py`,
  `python3 -I -m unittest discover -s tests/delivery_backlog_validator`,
  `python3 -I -m unittest discover -s tests/dotnet_bootstrap_integrity`, `git diff --check`).

## 5. Nasıl çalıştık (teknik reçeteler)

### 5.1 Ortam
- .NET 10: `export PATH=/root/.dotnet-sdk:$PATH DOTNET_ROOT=/root/.dotnet-sdk` (ortamına göre). Çözüm `GoymenERP.slnx`.
- Python yalnız `python3 -I`. Frontend: `src/frontend/goymen-erp-web` (`npx tsc -b`, `npm run lint`, `npx vitest run`).
- Gerçek SQL: docker `mcr.microsoft.com/mssql/server:2022-latest`, `GOYMEN_ERP_TEST_SQLSERVER_CONNECTION` ortam
  değişkeni (yerel geliştirici konteyneri; parolayı kendin belirle, commit etme).
- Build-time OpenAPI üretimi shell registry'yi açar; registry hatası alırsan
  `dotnet build tests/GoymenERP.Api.IntegrationTests -p:OpenApiGenerateDocumentsOnBuild=false` ile derle, shell drift
  testlerinin "Actual: sha256:…" değerleriyle `ShellCapabilityRegistryAuthorities.cs` sabitlerini yerinde güncelle.
- `OpenApiContractGenerationTests.BuildTimeContract…` yerelde makine adı yasaklı listeye takılabilir; farklı hostname
  ile koş (ör. `unshare --uts sh -c 'hostname x && dotnet test …'`).

### 5.2 Merge kuralları
- ErpPermissions, manifestler, Program.cs, README'ler, AiRuntimeServiceCollectionExtensions, appsettings,
  workRoutes/workInboxText/inboxKinds, `tools/relational_inventory/*.txt` (başlık `#` satırları başta, test adları
  LC_ALL=C ordinal, tekrar yok), `contracts/ai/tool-catalog-v1.json` (toolId birleşimi): **iki tarafı koru**.
- Pin'li dosyalarda taraf seçimi önemsiz; sonda yeniden üretilir.
- Mantıksal çakışmada iki niyeti koru, açıkla.

### 5.3 EF snapshot yeniden üretimi
`dotnet ef migrations add TmpSnapshotRegen --project <Infrastructure csproj> --startup-project
src/backend/GoymenERP.Api/GoymenERP.Api.csproj --context <XDbContext> --output-dir Persistence/Migrations --no-build`
→ Tmp migration'ın yalnız beklenen değişikliği içerdiğini kontrol et → Tmp dosyalarını sil, snapshot'ı tut.

### 5.4 Pin üretimi
- OpenAPI: `dotnet build src/backend/GoymenERP.Api/GoymenERP.Api.csproj --configuration Release` →
  `cp artifacts/openapi.json contracts/openapi/goymen-erp-v1.json` → `OpenApiContractGenerationTests.cs`
  beklentilerini (şema, problem kodları, IdempotentOperations…) güncelle.
- Guard: `python3 -I .claude/skills/architecture-guard-repin/scripts/guard_fingerprint.py <dosya>`; ArchitectureTests
  "Expected X, found Y" çıktısıyla `PlatformTenantQueryFilterBypassGuardTests.cs`'i güncelle; yeni migration `Sql`
  çağrıları için `ReviewedMigrationMutationSources` girdisi; relational inventory fingerprint'leri.
- Recovery: önce commit (temiz ağaç şart) →
  `python3 -I .claude/skills/recovery-inventory-repin/scripts/print_recovery_actuals.py` → DRIFT satırlarıyla
  `tools/generate_recovery_surface_inventory.py` sabitlerini, migration sayısını (şema const/min/max, recovery testi,
  release-manifest testi) güncelle → commit →
  `python3 -I -S -B tools/generate_recovery_surface_inventory.py` exit 0 → recovery/release unittest'leri ayrı
  worktree'de (uzun sürer).

### 5.5 Paket ve düzeltme oturumları
- Her paket kendi dalında; taban güncel tren-2. Pin'li dosyaları paket üretmez.
- K3 review oturumları: yazar değil, temiz bağlam, tam kapsam, exact head, GitHub'a COMMENT review olarak
  "K3-REVIEW-A/B APPROVE|REQUEST-CHANGES <sha>". Düzeltmeleri modül bazlı oturumlara böl (frontend / finans /
  stok-üretim / satınalma-satış), her oturum kendi dalında, sonra entegrasyon dalına merge.
- Doküman PR'ları: iki DOCS review + hafif kontroller, CI şartı yok (22 Ekim kuralı).

## 6. Sıradaki somut adımlar (bu dosya yazıldığı anda)
1. `origin/claude/tren-2` son hâlini oku; 12 paket dalı + `claude/tren2-dokumanlar` birleşmiş mi kontrol et, eksikleri
   birleştir, derle, test et.
2. §3.2'deki kalan paketleri (WP3, WP8, WP10, B1, B2, B4, B5, C2, C3, C5, C7, C8, C9, C1 kalıcı defter, YOL-D40,
   kredi grupları) tren-2 tabanından paralel başlat (en fazla 7–9 eşzamanlı).
3. Hepsi birleşince §3.3 kapanış adımları.
4. Tren-2 main'e girince Sinan'a haber ver; etkinleştirme kararlarını (bütçe, rol ataması, hukuk metni, canlı) ayrıca sor.
