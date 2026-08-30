# Göymen ERP — Supply-Chain Trust Envanteri v1

- Durum: **karar-nötr envanter / trusted release NO-GO**
- Snapshot tarihi: 2026-08-30
- İncelenen kaynak: `origin/main@83197c50f515db959638fadd7cc11ad6eb7a4fc8`
- Takip: GitHub Issue `#58` / `P2-3`; repository enforcement bağımlılığı Issue `#87`
- Sahiplik: writer `CODEX-PLATFORM`, bağımsız reviewer `CHATGPT-CARI`, merge için insan
  security review

Bu belge mevcut dış kimlikleri ve trust boşluklarını görünür kılar. Workflow, paket,
container, repository ayarı veya task durumu değiştirmez; action SHA'sı, image digest'i,
provider, ürün, threshold ya da production davranışı seçmez. Snapshot SHA'dan sonraki her
değişiklik yeniden envanter ve exact-head review gerektirir.

## 1. Bağlayıcı sınır

`ERP-000O_CI_CD_DELIVERY_READINESS.md`, trusted release için action/reusable workflow
kaynaklarının immutable revision'a; SDK, tool, package ve base image girdilerinin ise
lock/digest'e bağlanmasını ister. Bugünkü repository bu hedefin tamamını sağlamaz.

Bu envanter şu ayrımı kullanır:

- **Repository-sabit:** exact source commit içindeki byte veya sürüm kaydı sabittir.
- **Artifact-doğrulanabilir:** indirilen byte için repository'de cryptographic digest ya da
  registry integrity değeri vardır.
- **Mutable dış kimlik:** aynı metinsel ref daha sonra başka byte'a çözülebilir.
- **Bilinmeyen:** release kanıt zarfında gerçek resolved kimlik kaydedilmiyor.

Repository-sabit olmak tek başına artifact provenance'ı değildir. Örneğin
`dotnet-ef=10.0.8` kaydı exact commit'te sabit olsa da indirilen `.nupkg` için repository'de
checksum, imza sonucu veya attestation bulunmaz.

## 2. Snapshot dosya kimlikleri

Aşağıdaki SHA-256 değerleri yalnız bu envanter snapshot'ını yeniden üretmek içindir;
signing/attestation veya bağımsız trust root değildir.

| Girdi | SHA-256 |
|---|---|
| `.github/workflows/ci.yml` | `543894E463755DA0251231A41BA230E2B05B24D2BB887D2BB5E9A497C5930A50` |
| `.github/workflows/ui-catalog-validation.yml` | `DD5A103B8A112BDEA11FF6A67D578CD482EA393FEA8D0B9120EC981209E8C4BB` |
| `global.json` | `A647FBC942871CEF23CE38EF75C362BD02269B0BB634AA348A4537CADC926C3F` |
| `Directory.Build.props` | `5F331370A343903CD6349064FAE749D40C101C87F30C4DA9351FE2BB285968BE` |
| `NuGet.Config` | `CB93C65E28718AA9075FCE221E9F9CEA9E72D3FE1B30462383448A591F5B3AA1` |
| `dotnet-tools.json` | `BDD198CFEB9F036E9B75C18C2E72F786524973CFD5B55807C060DD2DA0308DAB` |
| `infra/docker-compose.yml` | `F73244215985185C4D31BECBCA3EFEE4FEA189672567AFD94C3C3E6714C0E0CC` |
| `src/backend/GoymenERP.Api/Dockerfile` | `C0F60E2548005472B51181038E573B8F19C1297147B296C7495545305612EF20` |
| `src/frontend/goymen-erp-web/Dockerfile` | `8F8EF81BEBDF488362D562C000F01CBDF9D5DFCEACF89051BEC04ABD7440C428` |
| `src/frontend/goymen-erp-web/package.json` | `3E5D040E378D193B2F595D894AD8E70724E3631071C1C9328A6BA108A59FBFD3` |
| `src/frontend/goymen-erp-web/package-lock.json` | `521D32C0AC100F1109FE011709C5086D90E32F5868A7A795B9409955F6122B9C` |

## 3. GitHub Actions ve runner kimlikleri

| Kullanım | Mevcut kimlik | Sınıf | Dış trust sahibi | Güncelleme / rollback / evidence boşluğu |
|---|---|---|---|---|
| Source checkout; beş job | `actions/checkout@v7` | Mutable major tag | GitHub Actions publisher | Resolved commit kaydedilmiyor; allowlist ve SHA pin zorlaması yok; rollback exact önceki action commit'ine bağlı değil. |
| .NET setup; backend ve relational | `actions/setup-dotnet@v6` | Mutable major tag | GitHub Actions publisher | Resolved action commit ve action artifact provenance'ı release zarfında yok. |
| Node setup; frontend | `actions/setup-node@v7` | Mutable major tag | GitHub Actions publisher | Resolved action commit kaydedilmiyor; cache trust sınırı ayrıca sürümlü değil. |
| Python setup; UI validation | `actions/setup-python@v6` | Mutable major tag | GitHub Actions publisher | Resolved action commit kaydedilmiyor. |
| Beş job runner'ı | `ubuntu-latest` | Mutable hosted-runner etiketi | GitHub-hosted runner hizmeti | OS image release'i ve ön-yüklü tool envanteri release kanıtında yok; önceki substrate'a deterministik rollback yok. |
| Bootstrap Python | runner içindeki `python3` | Bilinmeyen resolved sürüm | GitHub-hosted runner hizmeti | `ci.yml` bootstrap validation'dan önce Python setup yapmaz; gerçek interpreter kimliği kaydedilmez. |
| UI Python | `python-version: "3.13"` | Mutable patch kanalı | Python upstream + setup action | Exact patch ve dağıtım digest'i release kanıtında yok. |

Canlı read-only repository ayarı snapshot'ında Actions etkin, `allowed_actions=all` ve
`sha_pinning_required=false` idi. Bunlar bu belgeyle değişmedi. Issue `#87` çözülmeden
required review/check, admin, force-push/delete ve exact-head CI enforcement'ı varmış gibi
davranılamaz. İki workflow da token sınırını explicit `permissions: contents: read` olarak
daraltır; bu olumlu sınır mutable action/runner kimliklerini sabitlemez.

## 4. SDK ve build toolchain kimlikleri

| Yüzey | Mevcut kimlik | Sınıf | Trust sahibi | Güncelleme / rollback / evidence boşluğu |
|---|---|---|---|---|
| Workflow .NET kanalı | `dotnet-version: 10.0.x` | Mutable patch kanalı | Microsoft .NET + setup action | Kurulan exact SDK sürümü ve download digest'i kalıcı release zarfında yok. |
| Repository SDK politikası | `10.0.300`; `rollForward=latestFeature`; `allowPrerelease=false` | Policy repository-sabit, resolved SDK mutable | Repository owner + Microsoft .NET | Exact 10.0.300 yazılı olsa da `latestFeature` daha yeni feature band'e roll-forward edebilir; resolved SDK kanıtı yok. |
| EF CLI tool manifesti | `dotnet-ef=10.0.8`; `rollForward=false` | Direct sürüm repository-sabit; artifact doğrulanmamış | Repository owner + NuGet publisher | Root manifest validator ile byte/hash kilitli; `.nupkg` checksum/signature/provenance ve tool SBOM'u repository'de yok. |
| Frontend workflow runtime | `node-version: 24` | Mutable major kanal | Node.js upstream + setup action | Exact patch/binary digest ve rollback kimliği yok. |
| Frontend package manager | Node 24 dağıtımı/image'ı ile gelen `npm`; exact CLI sürümü kaydedilmiyor | Bilinmeyen resolved sürüm | Node.js/npm upstream + setup action veya image publisher | `packageManager`, `devEngines.packageManager`, `engines`, `.npm-version`, `.nvmrc` ve `.node-version` yok; npm sürümü Node patch/image tag ile değişebilir ve release zarfında kaydedilmez. |
| UI validator runtime | Python `3.13` | Mutable patch kanal | Python upstream + setup action | Exact patch/binary digest yok. |

`tools/validate_dotnet_bootstrap_integrity.py`, `global.json` semantiğini exact policy olarak;
`Directory.Build.props`, `NuGet.Config` ve `dotnet-tools.json` dosyalarını byte uzunluğu ve
SHA-256 ile fail-closed doğrular. Aynı validator nested/inherited bootstrap override'larını
da reddeder. Bu olumlu sınır dış action/runner/SDK artifact provenance'ını, NuGet transitive
lock'unu veya container digest'lerini doğrulamaz.

## 5. Container ve service image kimlikleri

| Kullanım | Mevcut kimlik | Sınıf | Dış trust sahibi | Güncelleme / rollback / evidence boşluğu |
|---|---|---|---|---|
| Relational CI service ve local compose | `mcr.microsoft.com/mssql/server:2022-latest` | Mutable tag | Microsoft Container Registry | Aynı tag farklı image'a çözülebilir; digest, platform manifesti, SBOM ve önceki approved digest yok. |
| Backend build stage | `mcr.microsoft.com/dotnet/sdk:10.0` | Mutable tag | Microsoft Container Registry | SDK image digest'i ve OS/base-layer kimliği yok. |
| Backend runtime stage | `mcr.microsoft.com/dotnet/aspnet:10.0` | Mutable tag | Microsoft Container Registry | Runtime digest'i, image SBOM'u ve signed provenance verification yok. |
| Frontend build stage | `node:24-alpine` | Mutable tag | Docker Official Images / Node maintainers | Node patch ve Alpine base aynı ref altında değişebilir; digest yok. |
| Frontend runtime stage | `nginx:1.28-alpine` | Mutable tag | Docker Official Images / NGINX maintainers | Nginx patch/build ve Alpine base digest'i yok. |

CI application image'larını build eder ancak registry'ye yüklemez, digest'i kalıcı artifact
olarak kaydetmez ve image scan/SBOM/attestation üretmez. Compose çıktısı development
modelidir; production artifact veya rollback kanıtı değildir.

## 6. NuGet ve .NET package yüzeyi

`NuGet.Config` bütün paketler için yalnız
`https://api.nuget.org/v3/index.json` kaynağını ve `packageSourceMapping` içindeki `*`
eşlemesini tanımlar. Exact-case root dosyası bootstrap validator tarafından byte/hash ile
kilitlidir. `trustedSigners` veya `signatureValidationMode` politikası yoktur.

Repository'deki unique direct `PackageReference` kimlikleri şunlardır:

| Paket | Direct sürüm |
|---|---|
| `coverlet.collector` | `6.0.4` |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | `10.0.8` |
| `Microsoft.AspNetCore.Mvc.Testing` | `10.0.8` |
| `Microsoft.Data.SqlClient` | `6.1.1` |
| `Microsoft.EntityFrameworkCore.Design` | `10.0.8` |
| `Microsoft.EntityFrameworkCore.InMemory` | `10.0.8` |
| `Microsoft.EntityFrameworkCore.SqlServer` | `10.0.8` |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | `10.0.8` |
| `Microsoft.NET.Test.Sdk` | `17.14.1` |
| `xunit` | `2.9.3` |
| `xunit.runner.visualstudio` | `3.1.4` |

Direct sürümler source commit'te sabittir; fakat `packages.lock.json`, central package lock,
repo-saklı transitive graph/checksum, SBOM, package signature sonucu ve mirror snapshot'ı
yoktur. Bu nedenle aynı source SHA için tam NuGet çözümünün ve indirilen byte'ların
repository kanıtıyla tekrar üretildiği söylenemez. CI'daki `dotnet list ... --vulnerable`
kontrolü canlı advisory/feed sonucuna bağlıdır; sonucu kalıcı provenance artifactına
bağlanmaz ve pozitif/negatif fail-closed gate testi ayrıca Issue `#58` kapsamında açıktır.

## 7. npm yüzeyi

`src/frontend/goymen-erp-web/package-lock.json` lockfile version `3` kullanır. Snapshot'ta
root dahil `100` package kaydı vardır; `99` dış kayıt `registry.npmjs.org` tarball URL'si ve
`99` integrity değeri taşır. `npm ci` lock ile kurulum yapar. Repository'de `.npmrc`, özel
registry allowlist'i, SBOM/license çıktısı veya package attestation verification politikası
yoktur.

Root dependency'lerin lock tarafından çözülen exact sürümleri:

| Paket | `package.json` isteği | Lock exact sürüm |
|---|---|---|
| `react` | `^19.2.8` | `19.2.8` |
| `react-dom` | `^19.2.8` | `19.2.8` |
| `@types/node` | `^24.13.3` | `24.13.3` |
| `@types/react` | `^19.2.18` | `19.2.18` |
| `@types/react-dom` | `^19.2.4` | `19.2.5` |
| `@vitejs/plugin-react` | `^6.1.0` | `6.1.0` |
| `oxlint` | `^1.79.0` | `1.80.0` |
| `typescript` | `~6.0.2` | `6.0.3` |
| `vite` | `^8.2.2` | `8.2.2` |
| `vitest` | `^4.1.11` | `4.1.11` |

Lock integrity, indirilen tarball byte'ı için olumlu bir kontrol sağlar; publisher hesabı,
release provenance'ı, lifecycle-script politikası, registry availability ve advisory
verisinin doğruluğunu tek başına kanıtlamaz. `npm audit --audit-level=high` canlı advisory
verisine bağlıdır; exact advisory snapshot'ı veya exception kaydı release zarfında yoktur.

## 8. Sahiplik ve kontrollü güncelleme sözleşmesi

| Aşama | Sorumlu | Zorunlu evidence | Failure / rollback davranışı |
|---|---|---|---|
| Envanter yenileme | `CODEX-PLATFORM` | Source SHA, changed refs, lock/image/action diff'i | Snapshot drift'i gizlenmez; trusted release NO-GO kalır. |
| Upstream kimlik inceleme | `CODEX-PLATFORM` + insan security reviewer | Official release/commit/digest, publisher ve security advisory referansları | Kimlik/provenance doğrulanamıyorsa update kabul edilmez. |
| Bağımsız exact-SHA review | `CHATGPT-CARI` ve insan security reviewer | PR exact head, diff, test ve provenance kanıtı | Eski head review'u yeniden kullanılamaz. |
| Merge | Repository owner; Issue `#87` enforcement'ı ayrıca gerekli | Fresh exact-head required checks ve resolved conversation | Zero-step, billing-blocked veya stale CI kabul kanıtı değildir; merge edilmez. |
| Rollback adayı | Aynı sahiplik zinciri | Önceki onaylı source/lock/action SHA/image digest ve compatibility kanıtı | Mutable tag'i geriye taşımak rollback sayılmaz; exact önceki kimliğe dönülür. |

Update PR'ı action, SDK, tool, package veya image değişikliğini ve üretilen lock/SBOM/
provenance farkını birlikte göstermelidir. Birden çok trust sınıfını tek PR'da gerekçesiz
karıştırmamalı; güvenlik gate'ini silerek kendi yeşil sonucunu üretmemelidir. Rollback,
repository revert'i ve önceki exact girdilerle yeniden doğrulamayı kapsar; kalıcı immutable
artifact registry henüz olmadığı için production artifact rollback'i **kanıtlanmış değildir**.

## 9. Fail-closed sıra

Issue `#58` ve `#87` kapanana kadar aşağıdaki sıra korunur:

Bu sıra bugünkü manuel **NO-MERGE / NO-PRODUCTION** governance sınırı ve ilerideki
enforcement gereksinimidir; mevcut workflow veya repository setting'leri tarafından tam
uygulanıyor sayılmaz.

1. Source SHA ve bu envanterdeki dosya/ref farkları yeniden çıkarılır.
2. Mutable veya kimliği bilinmeyen girdi trusted release için otomatik olarak reddedilir;
   yalnız validation/readiness çalışması yapılabilir.
3. Update ayrı branch/PR'da hazırlanır; workflow, pin, package veya image kararı bu envanter
   PR'ına eklenmez.
4. Resolved upstream commit/digest, publisher ve provenance bağımsız kaynaktan doğrulanır;
   doğrulanamayan download/build devam etmez.
5. Lock, digest, SBOM ve provenance çıktıları aynı source SHA'ya bağlanır; missing veya
   uyuşmayan tek kayıt sonucu fail eder.
6. Bootstrap validator repository-controlled ilk .NET komutundan önce çalışır. Tool restore
   ve bağımsız container runner preflight sırası P2-1/P2-2'nin ayrı değişikliğidir; bu belge
   o boşlukları kapanmış saymaz.
7. Full CI, gerçek SQL ve container kanıtları exact head üzerinde runner adımlarıyla geçer.
   `steps=[]`, `runner_id=0`, billing/spending-limit veya eski SHA sonucu yeşil sayılamaz.
8. Exact-head bağımsız security review ve insan merge kapısı geçmeden kritik değişiklik
   merge edilmez. Korumasız `main`, manuel merge'i güvenli enforcement'a dönüştürmez.
9. Merge sonrası yeni `main` aynı kontrolleri yeniden üretir. Beklenmeyen drift veya
   doğrulanamayan upstream kimlikte promotion/production activation kapalı kalır.
10. Geri dönüş yalnız önceki onaylı exact source/lock/action SHA/image digest'e ve kanıtlı
    compatibility'ye yapılır; mutable tag veya yeniden build edilmiş bilinmeyen artifact
    rollback kabul edilmez.

## 10. Açık boşluklar

- GitHub Action ref'leri immutable commit SHA'ya pinli değildir.
- Runner image/OS/tool substrate kimliği sürümlü ve doğrulanabilir değildir.
- SDK/runtime download ve container base image digest'leri kayıtlı değildir.
- NuGet transitive lock, package checksum/signature policy ve package SBOM yoktur.
- npm lock vardır; fakat publisher provenance, lifecycle-script, SBOM/license ve advisory
  snapshot/exception sözleşmesi yoktur; lock'u tüketen exact npm CLI/package-manager
  sürümü de repository policy'si veya release zarfıyla sabitlenmez.
- Container image digest, scan, SBOM, provenance, signing ve registry retention kanıtı yoktur.
- Dependency update botu/politikası, CODEOWNERS ve supply-chain exception yaşam döngüsü
  yoktur.
- Billing/spending-limit nedeniyle fresh remote CI başlatılamayabilir; zero-step run kabul
  değildir.
- Private repository'nin güncel planında branch protection/ruleset enforcement yoktur;
  Issue `#87` insan/hesap sahibi kapısıdır.

## 11. Bu belgede verilmeyen kararlar

Bu envanter özellikle şunları **seçmez veya onaylamaz**:

- action commit SHA'ları, image digest'leri veya yeni package sürümleri;
- GitHub planı, public/private repository değişimi veya ikinci reviewer kimliği;
- hosted/self-hosted runner, OS image, registry, artifact store veya package proxy/mirror;
- SBOM formatı, scanner, signing/attestation ürünü, algoritması, key veya trust root;
- vulnerability/license severity threshold'u, istisna süresi veya update cadence'i;
- cache, artifact/evidence retention, RPO/RTO, deployment strategy veya topology;
- production credential, environment, deployment, promotion veya activation;
- Issue `#58`/`#87`, `ERP-000O`, ADR, OPEN, task veya acceptance status kapanışı.

## 12. Sonuç

Mevcut repository npm lock integrity ve fail-closed .NET bootstrap policy doğrulaması gibi
değerli kısmi kontroller taşır. Buna karşın Actions, runner, SDK kanalları ve bütün container
base image'ları mutable; NuGet transitive graph ve indirilen artifact provenance'ı kilitsiz;
SBOM/signing/attestation/immutable artifact zinciri yoktur.

Bu nedenle P2-3 için **envanter hazır**, fakat supply-chain pinning ve trusted release
**tamamlanmış değildir**. Sonraki teknik pinning PR'ı ancak bu envanterin human security
review'undan sonra, Issue `#87` fail-closed enforcement gerçeğini koruyarak hazırlanabilir.
