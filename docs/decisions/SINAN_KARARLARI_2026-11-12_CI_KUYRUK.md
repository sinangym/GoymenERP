# Sinan Kararları — 2026-11-12 (CI kuyruğu)

Bu belge `SINAN_KARARLARI_2026-10-26_PUBLIC_CI.md` ve `SINAN_KARARLARI_2026-11-08_CI_HIZLANDIRMA.md`
dosyalarını silmez; CIH-D5, CIH-D6 ve CIH-D7 ekler. CIH-D1…CIH-D4 aynen geçerlidir. 8 Kasım
belgesinin "yol-bazlı (paths-filter) atlama … ayrı bir karar/PR konusudur (`NEEDS-SINAN`)" notu
bu belgeyle, **yalnız aşağıda yazılan ölçüde** karşılanır.

**Kaynak:** Sinan'ın 12 Kasım 2026 tarihli talimatı (oturum `session_015Usc2nAnMTMtt1JTQNgn2D`,
sohbet mesajı). Sinan'ın sözü, yazıldığı haliyle:

> Githubta proje herkese açık olan planında koşuyor bu nedemek ve sınırsızmı araştır, birde bunu
> hızlandırmanın yolu nedir ona göre çözüm yolunu bul ve uygula sorunu çözelim

Talimat çözümün bulunup uygulanmasını ister; hangi mekanizmanın seçileceğini söylemez. Aşağıdaki
mekanizma ajan önerisidir, "Uygulama ayrıntısı (geri alınabilir)" olarak işaretlidir ve K3
review'dan (iki bağımsız exact-head review + fresh yeşil CI) geçmeden merge edilmez. Aynı
oturumdaki sınır da geçerlidir: "Ücretli GitHub Actions/bulut CI açma; bütçe veya kullanım
limitlerini değiştirme."

## Ölçüm (12 Kasım 2026, GitHub Actions kayıtlarından)

| Konu | Ölçülen |
|---|---|
| Public depo | Standart GitHub-hosted runner dakikası ücretsiz ve sınırsız. Sınırlı olan, hesabın **aynı anda koşan iş sayısıdır** (hesap planına göre; hesabın planı doğrudan doğrulanmadı). |
| Gözlenen eşzamanlılık | Dakikada ≈4 iş başlıyor, ortalama iş ≈5,6 dk → aynı anda ≈20 iş. |
| Bir CI koşusu | 22 iş, ≈117–131 runner-dakika; runner boşsa ≈25 dk. |
| Kapasite / talep | Saatte ≈9–10 tam CI koşusu kapasite; saatte ≈20 CI koşusu talep. |
| Sonuç | Son 15 başarılı koşu 91–181 dk sürdü (ortanca 144); işler ortanca 52–60 dk sıra bekledi. |
| Talebin kaynağı | Son 60 CI koşusunun 41'i yalnız doküman değişikliğiyle tetiklendi (25 PR, 16 `main` push'u). `main`'deki 16 doküman koşusunun hepsi bir sonraki merge tarafından iptal edildi. |
| Bu filtrenin etkisi | Aynı 60 koşunun 38'i hiç başlamazdı. `main`'de 10 Kasım'dan beri 88 commit'in 45'i CI başlatmazdı. Talep saatte ≈7 koşuya iner. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| CIH-D5 | Hiçbir CI işinin okumadığı düzyazı CI başlatmaz | **Uygulama ayrıntısı (geri alınabilir).** `ci.yml` ve `cari-c02-link-acceptance.yml`, `pull_request` ve `main` push'unda aynı `paths` listesini taşır: her şey CI başlatır; yalnız `docs/decisions/`, `docs/delivery/`, `docs/handoffs/` ve `docs/operations/` altındaki `*.md` dosyaları başlatmaz. CI doğrulayıcılarının okuduğu dört Markdown dosyası adıyla geri alınır: `KARAR_SICILI.md`, `ACIK_KARARLAR.md`, `SINAN_KARARLARI_2026-08-31.md`, `DECISION_PACK.md`. `.md` olmayan her dosya (`KARAR_SICILI.csv`, kanıt JSON'ları dahil) CI başlatır. Değişiklikte bu düzyazıdan başka tek dosya varsa CI bütün işleriyle koşar. Hiçbir iş, adım veya `needs` değişmedi. | `.github/workflows/ci.yml`, `.github/workflows/cari-c02-link-acceptance.yml`, `tests/ci_safe_trx_summary/test_workflow_path_filters.py` |
| CIH-D6 | C006 koşularında eskiyen PR koşusu | **Uygulama ayrıntısı (geri alınabilir).** `cari-c02-link-acceptance.yml` bir `concurrency` grubu alır: aynı PR'a gelen yeni push eski koşunun yerine geçer. `main` koşuları hiç iptal edilmez. | `.github/workflows/cari-c02-link-acceptance.yml` |
| CIH-D7 | Değişmeyenler | CIH-D4 aynen: yalnız `ubuntu-latest`/`windows-latest`, self-hosted yok, secret/environment/`pull_request_target`/deploy yok, artifact ≤ 7 gün. Ücretli plan, bütçe veya limit değişikliği yok. 22 Ekim kuralı aynen: yalnız doküman değiştiren PR'da CI şartı yoktur; `docs/decisions/**` değişikliğinde yerel hafif kontroller ve kanıtı zorunludur. Faz 0 `main` tazelik kontrolü aynen. | `AGENTS.md`, tüm `.github/**` |

Filtrenin koruması `test_workflow_path_filters.py` içindedir: listeyi iki workflow'da ve iki olayda
sabitler, depodaki `docs/` dosyalarına karşı yalnız incelenmiş düzyazının atlandığını doğrular ve
depodaki kod atlanan bir dosyaya başvurmaya başlarsa kırmızıya düşer.

## Sınırlar

- **Tüm ağaca bakan kurallar.** `validate_dotnet_bootstrap_integrity.py` ve iki envanter üreticisi
  yol adlarına ve symlink'lere bütün ağaçta bakar. Dört klasörde adı `.md` ile biten bir symlink
  veya kural dışı bir yol kendi PR'ında değil, sonraki ilk CI koşusunda yakalanır (o koşu
  fail-closed kırmızı olur). `docs/decisions/**` için 22 Ekim kuralı aynı doğrulayıcıyı yerelde
  zaten zorunlu tutar.
- **GitHub'ın dosya sayısı sınırı.** GitHub yol filtresini değişikliğin sınırlı sayıdaki dosyası
  üzerinden değerlendirir. Yüzlerce düzyazı dosyasıyla birlikte kod değiştiren tek bir değişiklik
  CI başlatmayabilir. Kod PR'ı exact-head fresh yeşil CI olmadan merge edilemediği için bu durum
  merge kapısında görünür.
- **Zorunlu durum kontrolü varsayımı.** Bu depo bugün branch protection'da zorunlu durum kontrolü
  kullanmıyor. Açılırsa (insan kapısı) atlanan koşu "Pending" kalır; önce bu filtre gözden
  geçirilir.
- **Nokta ile başlayan yollar.** GitHub belgesi `**` için "sıfır veya daha fazla herhangi bir
  karakter" der; `.github/…` gibi yolların eşleştiği ayrıca yazılı değildir. Ölçüldü: PR #1149'un
  ilk head'i (`db06cade2498b43eeeaeacc461089a01e5bc0e86`) yalnız `.github/workflows/` altındaki iki
  dosyayı değiştiriyordu; hem `CI` (koşu 43280123897) hem C006 (koşu 43280124024) `pull_request`
  olayıyla başladı. `'**'` nokta ile başlayan yolları da eşler; ek desen gerekmez.
- **Karara bağlanmayan (`NEEDS-SINAN`):** Faz 0'da `main` yalnız atlanan düzyazıda ilerlediğinde
  mekanik merge commit'i ve yeni CI koşusu şartının kaldırılması. Kalan en büyük kuyruk kaynağı
  budur; merge kapısını değiştirdiği için bu belge onu **değiştirmez**.
- `c01-real-browser-e2e.yml` (`main` push'u, CIH-D3) ve `ui-catalog-validation.yml` değişmedi.
- Kuyrukta bekleyen eski koşular iptal edilmedi; başka şeritlerin koşularına dokunulmadı.
- Deployment, environment, secret, workflow'ların Settings → Actions'ta etkinleştirilmesi,
  grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`
  insan kapıları değişmez.
