# Sinan Kararları — 2026-11-12 (CI kuyruğu)

Bu belge `SINAN_KARARLARI_2026-10-26_PUBLIC_CI.md`, `SINAN_KARARLARI_2026-11-08_CI_HIZLANDIRMA.md`
ve `SINAN_KARARLARI_2026-11-07_MUHASEBE_FISI_VE_RECOVERY.md` dosyalarını silmez; CIH-D5…CIH-D10 ve
GOV-D5 ekler. CIH-D1…CIH-D4 ve GOV-D1…GOV-D4 aynen geçerlidir; GOV-D5, GOV-D2/GOV-D3'ün Faz 0
kontrolüne yalnız aşağıda yazılan (c) kademesini ekler. 8 Kasım belgesinin "yol-bazlı
(paths-filter) atlama … ayrı bir karar/PR konusudur (`NEEDS-SINAN`)" notu bu belgeyle, **yalnız
aşağıda yazılan ölçüde** karşılanır.

**Kaynak:** Sinan'ın 12 Kasım 2026 tarihli iki sohbet mesajı (oturum
`session_015Usc2nAnMTMtt1JTQNgn2D`). Sözleri, yazıldığı haliyle:

> Githubta proje herkese açık olan planında koşuyor bu nedemek ve sınırsızmı araştır, birde bunu
> hızlandırmanın yolu nedir ona göre çözüm yolunu bul ve uygula sorunu çözelim

> Evet yapılacak herşeyi yap ki hızlanalım, önerilerin hepsini mantıklı şekilde uygula

İlk mesaj çözümün bulunup uygulanmasını ister (CIH-D5…CIH-D7). İkinci mesaj, ajanın sorduğu dört
soruya verilen tek cevaptır (aşağıdaki tablo). İki mesaj da hangi mekanizmanın seçileceğini
söylemez. Mekanizmalar ajan önerisidir, "Uygulama ayrıntısı (geri alınabilir)" olarak işaretlidir
ve K3 review'dan (iki bağımsız exact-head review + fresh yeşil CI) geçmeden merge edilmez. Aynı
oturumdaki sınır da geçerlidir: "Ücretli GitHub Actions/bulut CI açma; bütçe veya kullanım
limitlerini değiştirme."

## Soru ve cevaplar

Sorular ajanın sorduğu haliyledir. Her soru evet/hayır sorusuydu; "Önerim evet" notu ajanındır.
Dördüne birden verilen cevap: "Evet yapılacak herşeyi yap ki hızlanalım, önerilerin hepsini
mantıklı şekilde uygula".

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Faz 0'da `main` yalnız doküman tarafında ilerlediyse yeniden merge ve yeni CI şartı kalksın mı? Kalan en büyük kuyruk kaynağı bu. | Evet | Şart kalkar → GOV-D5. Seçilmeyen: hayır (her `main` ilerlemesinde yeniden merge ve yeni CI). |
| 2 | C-01'in "dal `main`'i içermeli" kapısı gevşetilsin mi? | Evet | Kapı gevşer → CIH-D9. Seçilmeyen: hayır (kapı katı kalır). |
| 3 | 11 Kasım'dan kalan yaklaşık 20 bekleyen eski koşu iptal edilsin mi? | Evet | İptal denenir → CIH-D10 (GitHub reddetti; aşağıda). Seçilmeyen: hayır (dokunulmaz). |
| 4 | Yukarıdaki `!cancelled()` değişikliği ayrı bir PR olarak hazırlansın mı? Önerim evet, #1149 merge edildikten sonra. | Evet | Değişiklik yapılır → CIH-D8. Seçilmeyen: hayır (toplayıcılar `always()` kalır). Soru ayrı PR diyordu; neden aynı PR'da geldiği "Sınırlar"da yazılıdır. |

## Ölçüm (12 Kasım 2026, GitHub Actions kayıtlarından)

| Konu | Ölçülen |
|---|---|
| Public depo | Standart GitHub-hosted runner dakikası ücretsiz ve sınırsız. Sınırlı olan, hesabın **aynı anda koşan iş sayısıdır** (hesap planına göre; hesabın planı doğrudan doğrulanmadı). |
| Gözlenen eşzamanlılık | Dakikada ≈4 iş başlıyor, ortalama iş ≈5,6 dk → aynı anda ≈20 iş. |
| Bir CI koşusu | 22 iş, ≈117–131 runner-dakika; runner boşsa ≈25 dk. |
| Kapasite / talep | Saatte ≈9–10 tam CI koşusu kapasite; saatte ≈20 CI koşusu talep. |
| Sonuç | Son 15 başarılı koşu 91–181 dk sürdü (ortanca 144); işler ortanca 52–60 dk sıra bekledi. |
| Talebin kaynağı | Son 60 CI koşusunun 41'i yalnız doküman değişikliğiyle tetiklendi (25 PR, 16 `main` push'u). `main`'deki 16 doküman koşusunun hepsi bir sonraki merge tarafından iptal edildi. |
| CIH-D5 filtresinin etkisi | Aynı 60 koşunun 38'i hiç başlamazdı. `main`'de 10 Kasım'dan beri 88 commit'in 45'i CI başlatmazdı. Talep saatte ≈7 koşuya iner. |
| İptal edilen koşunun kuyruğu | Koşu 43280123897 18:38:20Z'de iptal edildi; `always()` taşıyan üç toplayıcı işi runner bekleyip 18:47:55Z'de bitti. Yerine gelen koşu 43281915847'nin işleri 18:47:56Z'de sıraya girdi: ≈9,5 dk gecikme (CIH-D8). |
| Faz 0 (c) komutları | `main`'in ilk-ebeveyn geçmişinde 680 karşılaştırma (115 tek commit, 115 uca kadar aralık, 2–5 commit'lik 450 pencere): komutların sonucu CIH-D5 filtresinin sonucuyla 680/680 aynı. Tek commit'lerin 46'sında, pencerelerin 70'inde (c) geçerliydi (GOV-D5). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| CIH-D5 | Hiçbir CI işinin okumadığı düzyazı CI başlatmaz | **Uygulama ayrıntısı (geri alınabilir).** `ci.yml` ve `cari-c02-link-acceptance.yml`, `pull_request` ve `main` push'unda aynı `paths` listesini taşır: her şey CI başlatır; yalnız `docs/decisions/`, `docs/delivery/`, `docs/handoffs/` ve `docs/operations/` altındaki `*.md` dosyaları başlatmaz. CI doğrulayıcılarının okuduğu dört Markdown dosyası adıyla geri alınır: `KARAR_SICILI.md`, `ACIK_KARARLAR.md`, `SINAN_KARARLARI_2026-08-31.md`, `DECISION_PACK.md`. `.md` olmayan her dosya (`KARAR_SICILI.csv`, kanıt JSON'ları dahil) CI başlatır. Değişiklikte bu düzyazıdan başka tek dosya varsa CI bütün işleriyle koşar. Hiçbir iş, adım veya `needs` değişmedi. | `.github/workflows/ci.yml`, `.github/workflows/cari-c02-link-acceptance.yml`, `tests/ci_safe_trx_summary/test_workflow_path_filters.py` |
| CIH-D6 | C006 koşularında eskiyen PR koşusu | **Uygulama ayrıntısı (geri alınabilir).** `cari-c02-link-acceptance.yml` bir `concurrency` grubu alır: aynı PR'a gelen yeni push eski koşunun yerine geçer. `main` koşuları hiç iptal edilmez. | `.github/workflows/cari-c02-link-acceptance.yml` |
| CIH-D7 | Değişmeyenler | CIH-D4 aynen: yalnız `ubuntu-latest`/`windows-latest`, self-hosted yok, secret/environment/`pull_request_target`/deploy yok, artifact ≤ 7 gün. Ücretli plan, bütçe veya limit değişikliği yok. 22 Ekim kuralı aynen: yalnız doküman değiştiren PR'da CI şartı yoktur; `docs/decisions/**` değişikliğinde yerel hafif kontroller ve kanıtı zorunludur. Faz 0 `main` tazelik kontrolü yalnız GOV-D5'in (c) kademesiyle genişler; ata kontrolü ile (a)/(b) aynen. | `AGENTS.md`, tüm `.github/**` |
| CIH-D8 | İptal edilen koşuda toplayıcı işler | Sinan'ın cevabı (soru 4): değişiklik yapılsın. **Uygulama ayrıntısı (geri alınabilir).** `ci.yml`'deki üç toplayıcı iş (`backend`, `frontend`, `relational`) iş düzeyinde `if: ${{ always() }}` yerine `if: ${{ !cancelled() }}` taşır. Gereken bir iş başarısız olduğunda, zaman aşımına uğradığında veya matrix fail-fast ile iptal edildiğinde toplayıcı yine koşar ve kırmızıya düşer (fail-closed aynen). Yalnız koşunun tamamı iptal edildiğinde (aynı PR'a yeni push, `main`'de sonraki merge) koşmaz; yerine gelen koşu toplayıcıları beklemez. Adım düzeyindeki `always()` satırları ve başka hiçbir iş değişmedi. Devre dışı yerel CI şablonu aynı iş gövdelerini bayt bayt taşıdığı için aynı üç satırla güncellendi; şablon devre dışı kalır. | `.github/workflows/ci.yml`, `.github/workflows/local-ci-bootstrap.yml.disabled`, `tests/GoymenERP.ArchitectureTests/PlatformTenantQueryFilterBypassGuardTests.cs`, `tests/ci_safe_trx_summary/test_summarize_trx.py`, `tests/dotnet_bootstrap_integrity/test_validate_dotnet_bootstrap_integrity.py` |
| CIH-D9 | C-01 "dal `main`'i içermeli" kapısı | Sinan'ın cevabı (soru 2): kapı gevşetilsin. **Uygulama ayrıntısı (geri alınabilir).** Önce eski katı kontrol koşar (`git merge-base --is-ancestor`). Geçmezse kapı yalnız şu durumda açılır: `main`, dalın ayrıldığı noktadan beri yalnız bu workflow'un kendi `pull_request` tetikleyicisinin zaten yok saydığı yollarda (`docs/**`, `AGENTS.md`, `.claude/**`) ilerlemiştir. Başka her fark ve her git hatası koşuyu durdurur. Tetikleyici, işler ve diğer adımlar değişmedi. | `.github/workflows/c01-real-browser-e2e.yml`, `tests/c01_e2e_contract/test_validate_c01_e2e_contract.py` |
| GOV-D5 | Faz 0 — `main` yalnız CI'ın okumadığı düzyazıda ilerlediyse | Sinan'ın cevabı (soru 1): bu durumda yeniden merge ve yeni CI şartı kalksın. **Uygulama ayrıntısı (geri alınabilir):** "doküman tarafı" dar okunur ve CIH-D5'in CI başlatmayan düzyazı listesidir. `docs/` altındaki diğer dosyalar (governance, master, backlog, dört geri alınan dosya, `.csv`/`.json`) CI başlatır ve bu kademeye girmez. Sıra: ata kontrolü; exit ≠ 0 ise önce (c) denenir. `AGENTS.md`'deki iki komut da exit 0 verirse merge edilebilir: mekanik merge commit'i, yeni CI koşusu ve `DELTA-OK` aranmaz; aynı head'deki fresh yeşil CI ve review'lar geçerli kalır. Makbuza main SHA'sı, `(c)`, iki komut ve exit code'ları yazılır. Komutlardan biri exit ≠ 0 verirse (git hatası dahil) (c) uygulanmaz; (a)/(b) aynen. Review sayısı, fresh yeşil CI, yazar ≠ reviewer ve K3 şartları değişmez. | `AGENTS.md`, `.claude/skills/review-signatures/SKILL.md`, `tests/ci_safe_trx_summary/test_workflow_path_filters.py` |
| CIH-D10 | 11 Kasım'dan kalan bekleyen koşular | Sinan'ın cevabı (soru 3): iptal edilsin. **Sonuç: denendi, GitHub hepsini reddetti; hiçbir koşu iptal edilmedi.** Ayrıntı aşağıda. Yalnız eskidiği kanıtlanan koşulara dokunuldu; açık bir PR'ın güncel head'indeki veya `main` ucundaki hiçbir koşuya dokunulmadı. | Yalnız GitHub Actions kayıtları; depoda dosya yok |

Filtrenin koruması `test_workflow_path_filters.py` içindedir: listeyi iki workflow'da ve iki olayda
sabitler, depodaki `docs/` dosyalarına karşı yalnız incelenmiş düzyazının atlandığını doğrular ve
depodaki kod atlanan bir dosyaya başvurmaya başlarsa kırmızıya düşer. Aynı dosya GOV-D5 komutlarını
`AGENTS.md`'den okur: iki listenin filtreyle aynı olduğunu sabitler ve komutları yazıldığı haliyle
geçici bir git deposunda çalıştırır (yalnız düzyazı → exit 0; başka tek dosya, taşıma veya karışık
değişiklik → exit 1; bilinmeyen head veya eksik `origin/main` → hata). CIH-D9 kapısı için
`test_validate_c01_e2e_contract.py` aynı iki şeyi yapar: istisna listesinin tetikleyicinin
`paths-ignore` listesiyle aynı olduğunu sabitler ve kapıyı geçici bir git deposunda çalıştırır.

### CIH-D10 — ne yapıldı, ne oldu

- **Kapsam (24 koşu, hepsi 11 Kasım 2026 20:18–20:53Z arasında oluştu).** Her biri tek tek okundu:
  `status: queued`, `run_attempt: 1`, `created_at` = `updated_at`, iş sayısı 0.
  - Eski `main` commit'i `b49ad45e` (#1075'in squash'ı): 43095209680, 43095209696, 43095209818,
    43095209940.
  - PR #1086 (11 Kasım 20:59Z'de merge edildi): 43097414055, 43097414069, 43097414212.
  - PR #1077 (açık; koşular yerine yenisi gelmiş eski head `f386f718` üzerinde, okunduğu sırada
    güncel head `c91b144a`): 43097479350, 43097479379, 43097479385, 43097479473.
  - PR #1087 (11 Kasım 20:53Z'de merge edildi; üç eski head): 43097629621, 43097629660,
    43097629664, 43097810941, 43097810942, 43097811105, 43098349264, 43098349328, 43098349356.
  - Eski `main` commit'i `aa1730ca` (#1087'nin squash'ı): 43098615961, 43098616023, 43098616086,
    43098616212.
  - İki eski `main` commit'i `origin/main`'in (`740c126f`) atasıdır ve ucu değildir.
- **Sonuç.** 24 koşunun her biri için bir kez iptal çağrısı yapıldı. GitHub hepsine aynı cevabı
  verdi: `409 Cannot cancel a workflow run that has not been queued yet.` Çağrılar yinelenmedi.
- **Etkisi yok.** Bu koşular hiç iş oluşturmadı; runner tutmuyor. Aynı `concurrency` grubundaki
  sonraki koşuları da engellemiyor: PR #1077'nin sonraki CI koşuları (43116482972, 43207827668,
  43266262770) 43097479379 bekler görünürken başarıyla bitti; `main`'de de sonraki koşular çalıştı.
  İptal edilebilselerdi de kuyruk kısalmazdı; bu yalnız liste temizliği olurdu.
- **Yapılmayan.** GitHub'ın zorla iptal uç noktası (`POST …/actions/runs/{run_id}/force-cancel`)
  bu oturumun araçlarında yoktur. GitHub belgesi, takılı kalan bir koşuda iptalin ve zorla iptalin
  işe yaramayabileceğini de söyler. Denenmedi.

## Sınırlar

- **Tüm ağaca bakan kurallar.** `validate_dotnet_bootstrap_integrity.py` ve iki envanter üreticisi
  yol adlarına ve symlink'lere bütün ağaçta bakar. Dört klasörde adı `.md` ile biten bir symlink
  veya kural dışı bir yol kendi PR'ında değil, sonraki ilk CI koşusunda yakalanır (o koşu
  fail-closed kırmızı olur). `docs/decisions/**` için 22 Ekim kuralı aynı doğrulayıcıyı yerelde
  zaten zorunlu tutar. GOV-D5'in (c) kademesi aynı sınırı taşır: atlanan düzyazıyı CI zaten
  görmediği için yeni bir açık eklemez.
- **GitHub'ın dosya sayısı sınırı.** GitHub yol filtresini değişikliğin sınırlı sayıdaki dosyası
  üzerinden değerlendirir. Yüzlerce düzyazı dosyasıyla birlikte kod değiştiren tek bir değişiklik
  CI başlatmayabilir. Kod PR'ı exact-head fresh yeşil CI olmadan merge edilemediği için bu durum
  merge kapısında görünür. (c) komutları GitHub'ın filtresini değil git'i kullanır; bu sınır
  onlarda yoktur.
- **Zorunlu durum kontrolü varsayımı.** Bu depo bugün branch protection'da zorunlu durum kontrolü
  kullanmıyor. Açılırsa (insan kapısı) iki şey önce gözden geçirilir: atlanan koşu "Pending" kalır
  (CIH-D5), ve iptal edilen koşuda atlanan toplayıcı iş GitHub'da "geçti" sayılır (CIH-D8).
- **Nokta ile başlayan yollar.** GitHub belgesi `**` için "sıfır veya daha fazla herhangi bir
  karakter" der; `.github/…` gibi yolların eşleştiği ayrıca yazılı değildir. Ölçüldü: PR #1149'un
  ilk head'i (`db06cade2498b43eeeaeacc461089a01e5bc0e86`) yalnız `.github/workflows/` altındaki iki
  dosyayı değiştiriyordu; hem `CI` (koşu 43280123897) hem C006 (koşu 43280124024) `pull_request`
  olayıyla başladı. `'**'` nokta ile başlayan yolları da eşler; ek desen gerekmez.
- **(c) kademesinde çakışma.** PR ile `main` aynı düzyazı dosyasında çakışıyorsa GitHub squash'ı
  reddeder; (c) bunu aşmaz, `main` PR'a merge edilir.
- **Aynı PR.** Soru 4 ayrı bir PR öneriyordu. Bu oturum yalnız kendi dalına push edebilir ve o
  dalda PR #1149 açıktır; her push tam bir CI koşusu harcar. Bu yüzden CIH-D8, CIH-D9 ve GOV-D5
  aynı PR'a ayrı commit'ler olarak geldi (uygulama ayrıntısı; review ayırmayı isterse ayrılır).
- **Değişmeyen workflow'lar.** `ui-catalog-validation.yml` değişmedi. İstek üzerine çalışan
  `browser-acceptance.yml` ve `openapi-generate.yml` C-01 ile aynı katı kapıyı taşır ve değişmedi.
  `c01-real-browser-e2e.yml` tetikleyicisi (`main` push'u, CIH-D3) değişmedi.
- **Yerelde doğrulanamayan.** `PlatformTenantQueryFilterBypassGuardTests.cs` içindeki pin ve C-01
  workflow'unun kendisi yalnız CI'da doğrulanır (bu ortamda `dotnet` yok).
- Başka şeritlerin canlı koşularına dokunulmadı; ücretli plan, bütçe veya limit değişmedi.
- Deployment, environment, secret, workflow'ların Settings → Actions'ta etkinleştirilmesi,
  grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`
  insan kapıları değişmez.
