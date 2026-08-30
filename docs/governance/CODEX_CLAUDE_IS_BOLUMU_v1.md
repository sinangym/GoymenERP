# Göymen ERP — Codex / Claude Çift Ajan İş Bölümü Protokolü v1.0

- Durum: `ÖNERİ / K3 / SİNAN REVIEW VE MERGE SONRASI BAĞLAYICI`
- Taban: `main@871101455dc2ba343d1c94982bfe9d1fa547411b`
- İlişki: `CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md` protokolünü daraltır;
  yerine geçmez.
- Kaynak önceliği değişmez: `KARAR_SICILI.md` >
  `GOYMEN_ERP_MASTER_RAPOR_V3.md` > feature spec/ADR > StokERP (yalnız referans).

Bu belge yeni iş kuralı, yeni yetki veya yeni merge yetkisi üretmez. `AGENTS.md`
mimari değişmezleri ve PR kabul kapısı aynen geçerlidir. Kurallar çelişirse daha
sıkı güvenlik, sahiplik ve merge kapısı uygulanır.

Bu dosyanın `codex/*` dalında bulunması protokolü etkinleştirmez. Protokol ancak
Sinan'ın exact-head incelemesi ve manuel merge'i sonrasında bağlayıcı olur.

## 1. Neden bu protokol

### 1.1 Bilgilendirici başlangıç fotoğrafı

2026-08-31 tarihinde `main@8711014` üzerinde yeniden doğrulanan göstergeler:

| Gösterge | Değer |
|---|---|
| Commit sayısı | 79; conventional prefix dağılımı 17 docs, 8 fix, 8 feat, 5 test, 1 ci; 40 diğer/merge |
| Değişen dosya dokunuşları | 278 docs, 220 backend, 77 test, 19 frontend |
| Frontend production kaynak satırı | 586 (test hariç TS/TSX/CSS); başlangıçtaki 345 ölçümü yeniden üretilemedi |
| Kataloglanmış ekran | 159 kanonik, 72 kaynak; kanonik ürün ekranı uygulaması henüz yok |
| Açık PR | 14 |
| Açık karar | 21 `OPEN-*` |
| Task manifest | A–E `done`, F/G/H/I `ready`, J/K/L/M/N/O `blocked` |

Bu tablo bağlayıcı truth-state değildir; her döngü başında canlı GitHub ve repo
kaynakları yeniden okunur.

Platform temeli ve test disiplini güçlüdür. Darboğaz, çalışan kullanıcı davranışı
yerine readiness/contract/evidence çıktılarının birikmesi ve farklı ajanların aynı
yüzeyi farklı varsayımlarla ele almasıdır.

Protokol üç hedefi birlikte uygular:

1. Her dosyada tek yazarlı sahiplik.
2. API sözleşmesinde makine-okunur drift kontrolü.
3. Yatay platform işi yerine kullanıcıya görünür dikey dilim.

## 2. Rol tanımları

### 2.1 Codex — Platform ve domain sahibi

Birincil sorumluluk:

- `src/backend/Modules/**`
- `src/backend/BuildingBlocks/**`
- modül Domain/Application/Infrastructure davranışı
- EF Core mapping ve migration'ları
- ilgili unit ve architecture testleri
- `docs/adr/**`, `docs/domain/**`, `docs/security/**`
- açıkça `owner: CODEX-*` olarak atanmış `docs/features/**`
- `tools/**`, `tests/task_manifest_validator/**`,
  `tests/dotnet_bootstrap_integrity/**`

Branch öneki: `codex/`.

### 2.2 Claude — API uç yüzeyi ve atanmış frontend feature sahibi

İlgili exact-surface HANDOFF sonrasında birincil sorumluluk:

- atanmış frontend feature dizinleri; ilk hedef
  `src/frontend/goymen-erp-web/src/features/cari/**`
- `src/backend/GoymenERP.Api/Endpoints/**`
- endpoint DTO binding ve HTTP response mapping
- devraldığı feature endpoint davranışına ait API integration testleri
- frontend generated client ve codegen config'i
- `docs/ui/assets/**`, `docs/ui/prototypes/**` ve atanmış ekran kartları
- açıkça `owner: CLAUDE-*` olarak atanmış UI/API feature belgeleri

Branch öneki: `claude/`.

Claude bu repository'de dış bir GitHub yazarıdır. Claude'a ait branch veya durable
HANDOFF görülmeden Codex, Claude yüzeyine onun adına production yazısı yapmaz.

### 2.3 CODEX-PLATFORM ortak güvenlik istisnası

Aşağıdaki yüzeyler sıradan API yüzeyi değildir; platform güvenliği ve composition
root sınırıdır:

- `src/backend/GoymenERP.Api/Errors/**`
- `src/backend/GoymenERP.Api/Multitenancy/**`
- `src/backend/GoymenERP.Api/Observability/**`
- `src/backend/GoymenERP.Api/Security/**`
- `src/backend/GoymenERP.Api/Program.cs`
- `src/backend/GoymenERP.Api/GoymenERP.Api.csproj`
- backend OpenAPI build/generator config'i ve kanonik OpenAPI baseline'ı
- ortak API test factory'si ve gerçek SQL runner/harness altyapısı

Bu yüzeylerde birincil yazar CODEX-PLATFORM'dur. Her davranış değişikliği en az K3
olarak sınıflanır; Claude API/UX contract review'u, Sinan güvenlik/merge onayı
zorunludur.

### 2.4 Sinan — karar, kilit ve merge sahibi

Aşağıdaki yüzeylerin onay ve merge sahibi yalnız Sinan'dır:

- `AGENTS.md`
- `docs/governance/**`
- `docs/decisions/**`
- `docs/master/**`
- `docs/backlog/task_manifest.json`
- `.github/**`
- CODEOWNERS, branch protection ve merge politikası

Ajanlar bu yüzeyler için yalnız ayrı branch'te Draft K3 öneri PR'ı açabilir. Öneri
PR'ı yazma yetkisi, merge veya aktivasyon yetkisi değildir.

## 3. Kesin yüzey sahipliği

Bir dosyanın aynı anda yalnız bir yazarı olabilir. Sahibi olmayan ajan değişiklik
gereksinimini issue/HANDOFF olarak sahibine iletir.

| Yüzey | Birincil yazar | Zorunlu karşı kontrol |
|---|---|---|
| `src/backend/Modules/**` | Codex | Claude API contract review |
| `src/backend/BuildingBlocks/**` | Codex | Claude contract review; güvenlikte Sinan |
| `src/backend/GoymenERP.Api/Endpoints/**` | Claude | Codex domain/security review |
| atanmış frontend feature/client/generated yüzeyi | Claude | Codex contract/security review |
| `App.tsx`, shared router/layout, global CompanyContext/query/cache | HANDOFF'a kadar CODEX-PLATFORM | Claude + Sinan K3 review |
| frontend codegen/package config'i | Claude Draft önerisi | Codex + Sinan tool/dependency review |
| API platform istisnaları (2.3) | CODEX-PLATFORM | Claude + Sinan |
| modül unit testleri | production owner | karşı ajan review |
| feature endpoint integration testleri | test edilen feature contract owner | karşı ajan review |
| platform/security API integration testleri | CODEX-PLATFORM | Claude + Sinan |
| ortak SQL/test harness | CODEX-PLATFORM | Claude + Sinan |
| `docs/adr/**`, `docs/domain/**`, `docs/security/**` | Codex | kapsamına göre Claude/Sinan |
| `docs/features/**` | belgede kayıtlı owner | karşı ajan review |
| UI asset/prototype/atanmış ekran kartı | Claude | Codex contract review |
| kanonik ekran ID/statü/katalog ve UI golden gate semantiği | Claude Draft önerisi | Codex + Sinan K3 review |
| `tests/ui_catalog_validator/**` fixture/evidence | Claude | Codex review |
| UI validator/gate kuralı veya golden kapsamı | CODEX-PLATFORM Draft önerisi | Claude + Sinan K3 review |
| Bölüm 2.4 yüzeyleri | yalnız Draft öneri; merge Sinan | Sinan exact-head review |

PR açılmadan önce:

```text
git diff --name-only origin/main
```

çıktısı sahiplik allowlist'iyle karşılaştırılır. Karşı yüzey dosyası varsa PR
bölünür. Test sahipliği yalnız klasör adına göre değil, kanıtladığı production
contract sahibine göre belirlenir.

`docs/features/**` altında açık owner başlığı bulunmayan dosya fail-closed olarak
read-only kabul edilir. Owner ancak kabul edilmiş issue/HANDOFF, mevcut kanonik
contract veya belge başlığıyla atanır. Owner ekleme/değiştirme K3'tür ve Sinan
review'u ister; ajan dosya adına bakarak sahip uyduramaz.

### 3.1 Geçişteki mevcut PR'lar

Bu protokol merge edilmeden açılmış PR'lar eski sahiplik sözleşmesiyle tamamlanır
veya kapatılır; ortasında sessizce yeni ajana devredilmez. Özellikle:

- mevcut `parallel/chatgpt/*` ve ChatGPT-CARI işleri, iki ayrı durable handoff olmadan
  yeni sahiplerin işi sayılmaz: Counterparty `Modules/**` ve domain contractları için
  `CHATGPT-CARI → Codex`; endpoint/HTTP mapping, frontend ve UI feature yüzeyi için
  `CHATGPT-CARI → Claude`;
- PR #114 mevcut CODEX-PLATFORM sahipliğiyle sonuçlandırılır;
- eski PR'a yalnız P0/P1 kapatma, current-main sync ve kanıt güncellemesi yapılır;
- yeni feature davranışı yeni sahiplikte yeni branch/PR ister.

## 4. API sözleşmesi ve contract drift

İki ajanın ortak sınırı sürümlü API sözleşmesidir.

Hedef durum:

1. Backend build dış DB, seed ve OIDC erişimi gerektirmeden deterministik geçici
   `artifacts/openapi.generated.json` üretir. `artifacts/**` build artifact'ıdır ve
   kanonik baseline değildir.
2. Normalize edilmiş ve commit edilen kanonik baseline
   `contracts/openapi/goymen-erp-v1.json` olur.
3. Frontend TypeScript client/tiplerini elle çoğaltmaz; kanonik OpenAPI'den üretir.
4. Üretilmiş frontend çıktı yolu:
   `src/frontend/goymen-erp-web/src/api/generated/`.
5. Generated frontend çıktısı commit edilir.
6. CI temiz checkout'ta geçici OpenAPI ve frontend çıktısını iki kez üretir; iki
   üretimin hash'i aynı değilse veya normalize baseline/generated TS diff'i varsa
   `contract-drift` job'ı kırmızı olur.
7. Contract değişikliğini isteyen taraf karşı owner'a issue ve exact contract
   handoff'u açar.

Mevcut `main`de OpenAPI/Swagger/NSwag paketi, generator config'i, generated client
ve `contract-drift` job'ı yoktur. Paket/generator/runtime seçimi tahminle yapılmaz.
Önce decision-neutral teknik PoC ve ayrı K3 `.github/**` önerisi hazırlanır; CI
entegrasyonunu yalnız Sinan merge eder.

PoC kabulü en az şunları kanıtlar:

- build-time generation DB, seed, secret ve OIDC metadata erişimi olmadan çalışır;
- iki ardışık üretim aynı normalize hash'i verir;
- endpointler typed response ve sürümlü schema üretir;
- bearer security, `X-Company-Id`, `Idempotency-Key`, correlation/replay header'ları
  ve ProblemDetails code sözleşmesi makine-okunur matriste kaybolmaz;
- generator ve codegen tool sürümleri pinlidir;
- PR #91 sonuçlanmadan aynı `.github/workflows/ci.yml` yüzeyine paralel yazılmaz.

OpenAPI çıktısı endpoint yetkisi, CompanyContext, audit veya idempotency davranışının
kanıtı değildir; bunlar ayrı çalıştırılabilir testlerle doğrulanır.

## 5. Döngü tanımı

Bir ürün döngüsü ancak aşağıdakilerden en az biriyle `DONE` sayılır:

- yeni veya düzeltilmiş endpoint gerçek istek alıp doğrulanmış cevap döndürür;
- yeni ekran gerçek API verisiyle render olur;
- yeni davranışı önce kırmızı, sonra yeşil yapan test vardır;
- kullanıcı tarafından kapanan `OPEN-*` kararının kod karşılığı uygulanır.

Yalnız `docs/**` değiştiren çalışma `DÖNGÜ` değil `NOT` kaydıdır. Güvenlik açığı,
stale kanıt veya bağımsız kritik bakım yine yapılabilir; ancak ürün ilerlemesi gibi
sayılmaz.

Arka arkaya iki ürün turu davranış üretmediyse üçüncü ürün turu başlamaz. Ajan,
kilit `OPEN-*` veya sahiplik kapısını Sinan'a tek paragrafla bildirir; buna rağmen
bağımsız P0/P1 güvenlik işi, acceptance/golden/ADR güvenlik hazırlığı ve mevcut PR
incelemesi durdurulmaz; bunlar ürün döngüsü olarak sayılmaz.

## 6. Kapı katmanlaması

### K1 — düşük risk

Yalnız mevcut otonom protokolün otomatik merge allowlist'ine bütünüyle uyan:

- governance/decision/master/task-manifest/acceptance truth içermeyen docs,
- davranış değiştirmeyen tooling,
- test kapsamını gevşetmeyen fixture veya generated artifact

K1 olabilir.

Kapı: current-main tabanında exact-head runner-backed standart CI yeşil, diff allowlist
birebir ve açık blocker yok. Mevcut otonom
protokoldeki security, cross-module, finance/stock/production, test-gate ve sahiplik
istisnaları K1'i otomatik olarak K3'e yükseltir. K1 sınıfı kendi başına merge yetkisi
üretmez; güncel dış yazma yetkisi ayrıca bulunmalıdır.

Geçiş döneminde ve `main` branch protection/ruleset ile korunmadığı sürece K1 için
otomatik merge kapalıdır. Sinan'ın ayrı aktivasyon kararı ve teknik koruma kanıtı
olmadan bu belge K1 auto-merge'i açmaz.

### K2 — normal dikey feature

Kapsam: endpoint, ekran, domain davranışı ve bunların kanıtları; migration, yeni
permission veya cross-module güvenlik sözleşmesi içermez.

Kapı:

- standart CI,
- contract-drift,
- karşı ajan review'u,
- `AGENTS.md` PR kabul kapısının uygulanabilir bütün maddeleri,
- yeni davranış için kırmızıdan yeşile çalışan test.

K2 Draft kalır ve exact-head karşı ajan review'u ister. Bu belge insan/merge kapısını
azaltmaz; güncel kullanıcı ve repository merge politikası ne gerektiriyorsa uygulanır.
Sinan'ın ayrı aktivasyon kararı bulunmadan K2 manuel insan review'u olmadan merge
edilmez.

### K3 — yüksek risk

Kapsam:

- DB schema/migration/constraint,
- permission/security/CompanyId,
- audit/idempotency/outbox/inbox,
- finans/stok/üretim formülü,
- cross-module contract,
- API platform istisnaları,
- `.github/**`, governance, decisions, master, task manifest ve `AGENTS.md`,
- production/deployment/backup/restore.

Kapı: mevcut tam set, gerçek SQL Server relational acceptance, karşı owner review'u
ve Sinan exact-head onayı. K3 otomatik merge edilmez.

PR başlığı `[K1]`, `[K2]` veya `[K3]` taşır. Yanlış sınıflandırma blocker'dır.

## 7. Çapraz review şablonu

K2 ve K3 review çıktısı:

```text
KATMAN: K1 | K2 | K3 (doğru mu?)
YÜZEY İHLALİ: yok | <dosya listesi>
CONTRACT DRIFT: yok | <alan/tip farkı>
DOĞRULANMAYAN İDDİA: yok | <kanıtsız iddia>
COMPANYID/YETKI/AUDIT/IDEMPOTENCY REGRESYONU: yok | <bulgu>
EKSİK UI DURUMU: yok | error/empty/loading/conflict
KARAR: ONAY | BLOCKER
```

Kurallar:

1. Her çalışma iddiası test adı veya CI job linki taşır.
2. Dosya, route ve sembol adları repoda fiilen doğrulanır.
3. Yeşil CI yeni davranış testinin yerine geçmez.
4. Mock/InMemory relational kanıt sayılmaz.
5. Skip edilmiş test PASS sayılmaz ve sayısı açıklanır.
6. Kaynakta olmayan iş kuralı, eşik, oran, rol veya politika kodlanmaz.

## 8. Dikey dilim zorunluluğu

Yeni ürün işi şu hattın ilgili bütün adımlarını tamamlar:

```text
DB/domain → application → endpoint → OpenAPI/TS contract → ekran → integration → UI testi
```

Bir adım mevcut ve yeterliyse yeniden yazılmaz; exact contract ve testle yeniden
kullanılır. Migration gerekmeyen dilimde yapay DB değişikliği üretilmez.

İlk görünür dilim Cari'dir. Bunun nedeni Cari Domain/Application/Infrastructure ve
`/api/v1/counterparties` GET/POST uçlarının zaten `main`de bulunması; katalog ve UI
contractlarının mevcut olmasıdır. İlk hedef backend'i yeniden yazmak değil, mevcut
güvenli contractı frontend'e bağlamaktır.

## 9. İlk dört ürün döngüsü

### Döngü A — ortak kabuğun kontrollü açılışı

| Sahip | İş |
|---|---|
| Sinan | ERP-000K'nin L/O dependency ve `ERP-000K-SHELL-CONTRACT` kapılarını çözer; yalnız status değiştirmez, kabul edilmiş dependency/contract kararı üretir |
| Claude | Shared-shell exact HANDOFF ve kabul edilmiş K contractı sonrasında mevcut Organization API şirket seçicisini generation-fence'li CompanyContext mimarisine refactor eder; routing/layout ve error/empty/loading/conflict bileşenlerini ekler |
| Codex | DB/seed/OIDC bağımsız OpenAPI üretim PoC'si, kanonik baseline ve deterministik drift tool'u; `.github/**` değişikliği yalnız K3 öneri olarak |

Çıkış: uygulama kabuğu gerçek şirket API'siyle çalışır ve contract generation PoC'si
tekrarlanabilir. ERP-000K kanonik kapıları kapanmadan task `done` sayılmaz.

### Döngü B — Cari listesi (`CARI-ROUTE-01`)

Mevcut `GET /api/v1/counterparties` yeniden oluşturulmaz. Issue #94
`CARI-READ-PAGE-001` keyset pagination sözleşmesi zorunludur; tam liste/unpaged
frontend fallback yasaktır. PR #111'in kabul kapıları kapanmadan bu zincir tamamlandı
sayılmaz.

| Sahip | İş |
|---|---|
| Codex | Application/query katmanında kabul edilmiş keyset cursor, limit ve ABAC/CompanyId sözleşmesini önce failing unit/relational testle uygular veya doğrular |
| Claude | HANDOFF sonrası endpoint binding'i kabul edilmiş paging contractına bağlar; generated client, Cari liste ekranı ve API/UI integration testini ekler |

Çıkış: Cari listesi gerçek API verisiyle render olur; yetkisiz şirket verisi gerçek
SQL/API testiyle sızmaz; loading/empty/error/forbidden durumları görünürdür.

### Döngü C — Cari oluşturma

Mevcut `POST /api/v1/counterparties`, idempotency ve audit yolu yeniden yazılmaz.

| Sahip | İş |
|---|---|
| Codex | Mevcut create/idempotency/audit/concurrency davranışında eksik kabul varsa önce failing unit/relational kanıt, sonra dar düzeltme |
| Claude | Cari formu, client-side idempotency key yaşam döngüsü, ProblemDetails ve replay/conflict gösterimi, UI/integration testi |

Çıkış: kullanıcı cariyi gerçek API ile oluşturur; aynı komut ikinci iş kaydı üretmez;
replay/conflict kullanıcıya güvenli Türkçe durumla gösterilir.

### Döngü D — açık PR geçiş temizliği

Başlangıç fotoğrafında 14 açık PR vardır.

| Sahip | İş |
|---|---|
| Sinan | PR'ları `merge / close / K3 human review` kovalarına ayırır |
| Codex/Claude | Yalnız kendi yeni sahiplik yüzeyindeki PR'ı current main'e taşır; eski PR'ı ortasında sahip değiştirmez |

Hedef açık PR sayısı en çok 4'tür. Hedef, güvenlik/review kapısını atlayarak sayıyı
düşürme yetkisi vermez.

## 10. Sinan karar kuyruğu

Öncelik:

| Sıra | Karar | Kilitlediği iş |
|---|---|---|
| 1 | ERP-000K shell contract ve dependency alt kapıları | UI kabuğu ve domain mount'ları |
| 2 | OPEN-001 — şirket unvan/VKN/CompanyCode | canlı şirket masterı |
| 3 | OPEN-002 — depo/lokasyon sahipliği | stok başlangıcı |
| 4 | OPEN-013 — SSO sağlayıcı/MFA | production kimlik |
| 5 | OPEN-020 — bulut AI/KVKK/veri sınıfı | ERP-000J production |

Hedef ritim haftada üç `OPEN-*` kararıdır. Bu hedef kararları sessizce kapatma veya
ajanın Sinan yerine politika seçmesi anlamına gelmez.

## 11. Çatışma çözümü

- Aynı dosyaya iki aktif yazar varsa ikinci yazar durur; sahibi olan branch korunur.
- Farklı contract yorumu önce karar siciline bakılarak çözülür; kaynakta yoksa
  `OPEN-*` önerisi hazırlanır.
- İki karşı review aynı exact head'de anlaşamazsa Sinan'a taşınır; üçüncü tahmin turu
  yapılmaz.
- İkinci yüzey ihlalinde ilgili ürün döngüsü iptal edilir ve branch salt-okunur hale
  getirilir.

## 12. İlerleme kaydı

`docs/delivery/AUTONOMOUS_PROGRESS.md` için aynı anda tek coordinator/writer bulunur.
Dosya başka açık PR tarafından sahiplenilmişse ajan bu dosyaya yazmaz; Issue #66'ya
durable progress comment bırakır.

Yazılabildiğinde mevcut kayda şu alanlar eklenir:

```text
DAVRANIŞ DEĞİŞİKLİĞİ: evet: <endpoint/ekran/test> | hayır: <sebep>
AJAN: codex | claude
KATMAN: K1 | K2 | K3
```

Arka arkaya iki `hayır` ürün kaydında Bölüm 5 escalation kuralı uygulanır.

## 13. Aktivasyon kabul kapısı

Bu protokolün etkinleşmesi için:

1. Sinan exact-head inceleyip manuel merge eder.
2. Existing ChatGPT-CARI geçişi iki exact-surface HANDOFF ile kaydedilir: Counterparty
   module/domain yüzeyi Codex'e; endpoint/frontend/UI feature yüzeyi Claude'a. İlgili
   handoff yoksa eski hat kapanana kadar yeni sahip o Cari yüzeyine yazmaz.
3. Mevcut shared-shell sahibi CODEX-PLATFORM'dan Claude'a geçiş isteniyorsa ayrıca
   exact surface HANDOFF yapılır; aksi halde Claude yalnız Cari feature mount alanını
   yazar.
4. Kanonik UI katalog/status/route/golden-gate yazarlığı için ayrıca exact-surface
   activation HANDOFF yapılır; fixture üretme yetkisi gate semantiğini değiştirme
   yetkisi sayılmaz.
5. ERP-000K için yalnız status değişikliği değil, dependency/contract kararı alınır.
6. OpenAPI generator ve TypeScript codegen seçimi PoC/kanıtla onaylanır.
7. `.github/**` contract-drift önerisi ayrı K3 PR ve insan review'dan geçer.
8. PR #111'in gerçek SQL/P1 kapıları ve PR #114'ün named cross-owner review kapısı
   kapanmadan Cari error/query zincirinin tamamlandığı iddia edilmez.
9. Açık PR'lar Bölüm 3.1'e göre grandfathered olarak sınıflanır.
10. Branch protection/CODEOWNERS mevcut değilse bu belge uygulama disiplini sağlar;
   otomatik enforcement veya auto-merge varmış gibi iddia edilmez.

Aktivasyon bu kapıları, mevcut `AGENTS.md` veya otonom protokolün daha sıkı
güvenlik kurallarını gevşetmez.
