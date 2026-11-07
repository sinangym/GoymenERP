# CI süresi analizi v1 — neden 50–70 dakika, nasıl kısalır

- **Tarih:** 2026-11-07
- **Durum:** Analiz ve öneri. Kod ve workflow değişikliği yok. **Karar Sinan'da.**
- **Hazırlayan:** Claude şeridi
- **Kaynak commit:** `origin/main` `bd3bd63e0` (#945)
- **Veri:** GitHub Actions API (herkese açık), `CI` workflow'unun son 12 başarılı `main` koşusu
  (2–7 Kasım) ve 7 Kasım'daki 8 başarılı PR koşusu; iki işin tam günlüğü (koşu `42369831918`).

**Kısaca (Sinan için):** CI'ın yavaşlığının iki nedeni var. **(1)** Tek bir test, 512 farklı yetki
kombinasyonunun her biri için uygulamayı sıfırdan ayağa kaldırıyor ve tek başına **34 dakika**
sürüyor. Bütün CI bu testi bekliyor. Bu test aynı kontrolleri yapacak biçimde yeniden yazılırsa CI
yaklaşık **53 dakikadan 20 dakikaya** iner. **(2)** GitHub aynı anda en çok 20 iş çalıştırıyor. Bir
push 15 işi birden başlatıyor. Yoğun saatlerde PR'lar sırada **13–30 dakika** bekliyor. Main'e giren
her 10 PR'dan 7'si yalnız doküman değiştiriyor, ama yine de CI'ın tamamını koşturuyor. Bunları
atlamak kuyruğu belirgin biçimde rahatlatır.

## 1. Ölçüm

| | `main` (12 koşu) | PR (8 koşu) |
|---|---|---|
| Baştan sona süre (medyan) | **53 dk** (37–77) | **72 dk** (64–96) |
| Bir işin sırada bekleme süresi (medyan) | 0,1–2,4 dk | **8–30 dk** |
| Bir CI koşusunun toplam iş-dakikası | 136 dk (122–148) | aynı |

En uzun işler (çalışma süresi medyanı, `main`):

| İş | Süre | Ne yapıyor |
|---|---|---|
| Backend test group (api) | **47,7 dk** | 2.722 API testi, 53 parti halinde sırayla. **Parti 30 tek başına 34,4 dk** sürüyor. |
| Recovery surface inventory | 19,0 dk | 33 Python testi 981 sn sürüyor; en yavaş iki test 6,4 ve 4 dk |
| SQL Server shard C / A / B | 15,2 / 12,7 / 8,9 dk | Gerçek SQL konteyneriyle kabul testleri (konteyner açılışı ~25 sn) |
| Frontend | 9,8 dk | `npm test` tek işçiyle 9,1 dk sürüyor (bellek nedeniyle) |
| Release manifest | 9,2 dk | Python testleri ve iki üretim |
| RCSI, modules, backend-build, OpenAPI, container | 2–5 dk | build ~2 dk, restore 10–14 sn |

**Kritik yol:** `api` grubu (47,7) → `backend` toplayıcı (0,3) → `containers` (2,2). Diğer bütün
işler bu sürede biter. CI'ı kısaltmanın tek etkili yolu `api` grubunu kısaltmaktır.

**34 dakikalık parti:** `OrganizationShellCapabilityAcceptanceTests.ExistingPermissionAndNarrowScope_CombinationsRemainIndependent`.
Bu test 7 yetki biti × 4 kapsam = **512 durumu** deniyor ve her durumda `WithWebHostBuilder` ile yeni
bir test sunucusu kuruyor (durum başına ~4 sn). Parti sayısı ve yöntem sayısı günlükle birebir
uyuşuyor (19 yöntem, 532 durum). **Uyarı:** listeye bir yetki daha eklenirse durum sayısı 1.024'e,
süre ~68 dakikaya çıkar.

**Kuyruk:** GitHub Free planında aynı anda en çok **20** iş koşar (docs: Actions limits). Her push
`CI`'da 12 işi aynı anda başlatır. Buna C-01, Cari (Windows) ve UI catalog da eklenince 15 iş eder;
yani iki push kapasiteyi doldurur. 7 Kasım'da 15:35'e kadar 113 CI koşusu açıldı. 13:00 saatinde 19
koşu vardı: yaklaşık 2.600 iş-dakikası talep, saatlik kapasite ise 1.200 iş-dakikası. Son 5 günde
PR CI koşularının %54'ü (124/228), `main` koşularının %82'si (59/72) yeni bir push gelince iptal
edildi.

**Ek bulgu (güvence):** `ci.yml`'deki `cancel-in-progress: true` main'de de geçerli. Bu yüzden main'e
giren commit'lerin çoğunda merge sonrası CI hiç tamamlanmıyor. Son 5 günde yalnız 13 main commit'i
tam yeşil CI aldı.

**Ek bulgu (koruma):** API'ye göre `main` korumalı değil: zorunlu check yok, ruleset yok. "Zorunlu CI"
bugün teknik bir kural değil, merge edenlerin uyguladığı bir kural.

## 2. Öneriler (öncelik sırasıyla)

| # | Öneri | Tahmini kazanç | Risk | Kapı |
|---|---|---|---|---|
| 1 | **512 durumlu testi tek sunucuda koşturmak.** Yetki sağlayıcısı durum başına değiştirilir; 512 durumun hepsi ve beklentiler aynen kalır. Sunucu durum başına yeniden kurulmaz. | `api` 47,7 → ~13 dk. **CI ~53 → ~20 dk** (yeni kritik yol: recovery 19 dk). 34 iş-dk/koşu. | Düşük: yalnız test kodu değişir. Ortak sunucuda durumlar arasında durum sızmamalı; bunu review kontrol eder. | Test-only backend PR'ı (Codex şeridi). Yetki testi olduğu için iki review önerilir. |
| 2 | **Yalnız doküman değiştiren PR'larda ağır işleri atlamak.** İlk iş `git diff` ile değişen yolları bulur. Her zaman koşan tek bir "CI sonucu" işi, hangi kapının neden atlandığını yazar. `docs/decisions`/`docs/backlog` değişiminde backlog ve bootstrap doğrulayıcıları yine koşar. | Merge'lerin %69'u (80'de 55) yalnız doküman. Bu PR'larda push başına ~147 iş-dk tasarruf; kuyruk beklemesi belirgin düşer. AGENTS.md 22 Ekim kuralı doküman PR'larında zaten CI şartı aramıyor. | Orta: yanlış yol listesi bir kapıyı sessizce atlatabilir. Bunun için atlanabilir yollar dar bir **izin listesi** olur, gerisi tam CI koşar. Bugün zorunlu check olmadığı için merge engeli doğmaz; ileride check zorunlu yapılırsa "CI sonucu" işi zorunlu check olur, atlanan iş "skipped" olarak kalır. Üçüncü taraf `paths-filter` action'ı yerine düz `git diff` kullanılır. | `.github/**` = **K3, iki bağımsız review** |
| 3 | **Recovery envanteri testlerini hızlandırmak** (generator çıktısını testler arasında paylaşmak ya da test dosyasını 2–3 paralel işe bölmek). | 19 → ~8–10 dk. #1'den sonra CI ~15–16 dk'ya iner. | Düşük–orta: testler fail-closed mutasyon testleri; paylaşılan çıktı mutasyonu gizlememeli. | Recovery generator/test = iki review; iş bölünürse `.github` K3 |
| 4 | **Shard C'yi dengelemek** (4. shard ya da modül yer değiştirmesi). | 15 → ~11 dk (#1 ve #3'ten sonra kritik yol). +1 iş/push. | Düşük; toplayıcı doğrulayıcı shard listesine bağlı. | `.github` + verifier = K3 |
| 5 | **Frontend testlerini 2 parçaya bölmek** (`vitest --shard`). | 9,8 → ~5,5 dk. Kritik yolda değil; #1–#4'ten sonra anlamlı olur. | Düşük; +1 iş/push. | `.github` K3 |
| 6 | **Main push'larında iptali kapatmak** (`cancel-in-progress` yalnız PR'da). | Süre kazancı yok; **güvence kazancı**: her main commit'i tam CI alır. | Kuyruk yükü artar (son 5 günde +59 koşu ≈ +8.000 iş-dk). #1 ve #2'den sonra yapılması mantıklı. | `.github` K3 + **Sinan seçimi** |
| 7 | **Bir kez derleyip çıktıyı işlere artifact ile dağıtmak.** | Kritik yolda ~2 dk; push başına ~15 iş-dk. | Düşük teknik risk, ama 26 Ekim kararı artifact'ı "yalnız secret içermeyen JSON özetleri ve kanıtlar" ile sınırlıyor. | Karar genişletmesi = **Sinan** |
| — | **Cache (NuGet/npm)** | Ölçülen restore 10–14 sn, `npm ci` < 5 sn. **Kazanç ihmal edilebilir.** | Karar ve test bunu yasaklıyor (aşağıda). | Önerilmez |

**Cache neden önerilmez:** 26 Ekim kararı (madde 4, DELEGE) şöyle diyor: *"artifact yalnız secret
içermeyen JSON özetleri ve kanıtlardır, saklama ≤ 7 gün; cache, Packages, secret, environment,
`pull_request_target` ve deploy yüzeyi açılmaz."*
`tests/ci_safe_trx_summary/test_self_hosted_runner.py` aktif workflow'larda `actions/cache`'i
reddediyor. `tests/local_ci/test_local_check.py` de `setup-node`'un `cache` ayarını reddediyor.
Ölçülen kazanç saniyeler düzeyinde olduğu için kararı açmaya değmez.

**Ücretli seçenek (yalnız bilgi):** GitHub Pro planında eşzamanlı iş sınırı 40'tır. Ancak bu `$0`
kuralını değiştirir, dolayısıyla Sinan kararıdır. Destek talebiyle limit artışı da istenebilir.
Önerilmez; #1 ve #2 aynı etkiyi bedavaya sağlar.

## 3. Sinan'a sorular

1. **#1** (512 durumlu test) Codex şeridinin sıradaki işi olsun mu? — Önerilen: **Evet**. Tek başına
   CI'ı yaklaşık 30 dakika kısaltır ve güvenceden bir şey kaybettirmez.
2. **#2** (doküman PR'larında ağır işleri atlama) için `.github` PR'ı hazırlansın mı? — Önerilen:
   **Evet, #1'den sonra**. Yalnız izin listesindeki yollar atlanır.
3. **#6** main'de her commit tam CI alsın mı? — Önerilen: **Evet, #1 ve #2 merge edildikten sonra**.
4. **#7** (derleme çıktısını artifact ile taşıma) için artifact kuralı genişletilsin mi? — Önerilen:
   **Şimdilik hayır**; kazanç küçük.

Settings değişikliği (zorunlu check, branch protection) her zaman insan kapısıdır. Bu belge hiçbirini
önermez; bkz. `MERGE_KUYRUGU_FIZIBILITE_v1.md`.
