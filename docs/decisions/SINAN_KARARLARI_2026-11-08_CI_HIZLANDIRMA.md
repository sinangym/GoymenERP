# Sinan Kararları — 2026-11-08 (CI hızlandırma)

Bu belge `SINAN_KARARLARI_2026-10-26_PUBLIC_CI.md` dosyasını silmez; onun "cache, Packages,
secret, environment, `pull_request_target` ve deploy yüzeyi açılmaz" delegasyonunu **yalnız
aşağıda yazılan ölçüde** genişletir. Diğer her madde (yalnız GitHub-hosted runner, self-hosted
yok, artifact yalnız secret içermeyen kanıt ve saklama ≤ 7 gün) aynen geçerlidir.

**Kaynak:** Sinan'ın 8 Kasım 2026 tarihli kararı; `CI-HIZ` şeridi görevinde koordinatör
tarafından aktarıldı (görev bildirimi, oturum `session_015Usc2mi9Td14Cg4rL82Ec1`). Sinan'ın
sözü, görevde aktarıldığı haliyle:

> Sinan: depo public, GitHub-hosted runner'larda kaynak sorunu yok; "açılması gereken her şeyi
> açabiliriz, CI'ı çok hızlandır".

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| CIH-D1 | Cache/paralellik açma izni | **KILITLI (Sinan).** CI süresini kısaltmak için cache, matrix ve paralellik açılabilir. Bu belge 26 Ekim "cache açılmaz" delegasyonunu bu amaçla genişletir. Cache yalnız ölçülmüş kazanç varsa eklenir. | `.github/workflows/ci.yml` |
| CIH-D2 | Backend `api` test grubu 3 dilime bölünür | **Uygulama ayrıntısı (geri alınabilir).** `Backend test group (api-1/2/3)`; her dilim ayrı kanıt üretir. Aynı testler koşar: bölüşüm sınıf düzeyinde, deterministik ve disjoint; aggregate bölüşümü birleşik keşiften yeniden hesaplar ve eksik/çakışan dilimde fail-closed olur. Hiçbir doğrulama zayıflatılmaz, atlanan test yoktur. | `tools/verify_backend_test_groups.py`, `.github/workflows/ci.yml` |
| CIH-D3 | Yalnız doküman değişen PR'da C-01 tarayıcı koşusu | **Uygulama ayrıntısı (geri alınabilir).** `c01-real-browser-e2e.yml` `pull_request` tetikleyicisi `docs/**`, `AGENTS.md`, `.claude/**` için `paths-ignore` alır. `main` push'u ve `workflow_dispatch` aynen kalır. AGENTS.md "SCOPE" listesindeki hiçbir yol atlanmaz. | `.github/workflows/c01-real-browser-e2e.yml` |
| CIH-D4 | Değişmeyenler | `runs-on` yalnız `ubuntu-latest`/`windows-latest`; self-hosted yok; secret, environment, `pull_request_target`, deploy yüzeyi yok; artifact saklama ≤ 7 gün; zorunlu kontroller atlanmaz veya zayıflatılmaz. | Tüm `.github/**` |

## Sınırlar

- Bu karar `NuGet`/`npm` cache'ini **eklemez**: restore ~10 sn sürer, kazanç ölçülmeden cache
  açılmaz (CIH-D1).
- `containers` işinin `needs` listesi ve yol-bazlı (paths-filter) atlama bu PR'da **değişmedi**;
  mevcut testler `containers`'ın tüm kapılara bağlı olmasını ve zorunlu kontrollerin atlanmamasını
  kilitler. Değiştirmek ayrı bir karar/PR konusudur (`NEEDS-SINAN`, Issue #66).
- Deployment, environment, secret, workflow'ların Settings → Actions'ta etkinleştirilmesi,
  grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`
  insan kapıları değişmez.
