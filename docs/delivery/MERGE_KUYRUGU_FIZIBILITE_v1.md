# GitHub Merge Queue fizibilitesi v1

- **Tarih:** 2026-11-07
- **Durum:** Fizibilite notu. Kod, workflow ve ayar değişikliği yok. **Karar Sinan'da, Faz 1'den sonra.**
- **Hazırlayan:** Claude şeridi
- **Kaynak commit:** `origin/main` `bd3bd63e0` (#945)
- **İlgili:** `RECOVERY_PIN_CAKISMA_ONERISI_v1.md` (Seçenek B), `SINAN_KARARLARI_2026-11-07_MUHASEBE_FISI_VE_RECOVERY.md` (GOV-D2…D4)

**Kısaca (Sinan için):** Merge queue, PR'ları sıraya koyar ve her birini "main + öndeki PR'lar" ile
birleştirilmiş halde test eder; test geçmeden main'e almaz. Bugün **kullanılamıyor**, çünkü depo bir
kullanıcı hesabına ait. GitHub bu özelliği public depolarda yalnız **organizasyon** hesaplarına açıyor.
Kullanmak için depoyu ücretsiz bir GitHub organizasyonuna taşımak gerekir. Bu büyük ve ayrı bir
karardır. Öneri: Faz 1 bitip etkisi görülene kadar karar verilmesin.

## 1. Kullanılabilirlik

- GitHub belgesi (`managing-a-merge-queue`, gated-features metni): *"Pull request merge queues are
  available in any public repository owned by an organization, or in private repositories owned by
  organizations using GitHub Enterprise Cloud."*
- API'ye göre `sinangym/GoymenERP` için `owner.type = User`, `visibility = public`. Yani **şu hâliyle
  uygun değil**.
- Tek yol, depoyu (ücretsiz) bir organizasyona transfer etmek. Bu bir Settings işlemidir, **insan
  kapısıdır**. Transfer depo adresini değiştirir (GitHub eski adresi yönlendirir), ajan oturumlarının
  ve entegrasyonların erişimini etkiler. Ayrıca bütün izinlerin ve Issue #66 akışının yeniden kontrol
  edilmesini gerektirir.
- Ayrıca `main` bugün korumalı değil: zorunlu check ve ruleset yok. Merge queue "Require merge queue"
  koruma kuralıyla açılır, yani branch protection ya da ruleset kurmak da gerekir (insan kapısı).

## 2. Gerekecek değişiklikler (organizasyona taşınırsa)

| Değişiklik | Ne | Kapı |
|---|---|---|
| `.github/**` | Zorunlu check üreten **her** workflow'a `merge_group:` tetikleyicisi eklenir (`ci.yml`, `c01-real-browser-e2e.yml`, `cari-c02-link-acceptance.yml`, `ui-catalog-validation.yml`). Belge: *"status checks will not be triggered when you add a pull request to a merge queue. The merge will fail as the required status check will not be reported."* `recovery-surface-inventory` ve `release-manifest` işleri `pull_request.head.sha \|\| github.sha` ile checkout yapıyor; `merge_group`'ta `github.sha` geçici kuyruk commit'idir. Bu doğru davranıştır, ama ilgili testler bu olayı da kabul edecek biçimde güncellenmelidir. | **K3, iki bağımsız review** (GitHub-hosted, secret/deploy yok kontrolü dahil) |
| Settings / ruleset | "Require merge queue", zorunlu check listesi, merge yöntemi **squash**, build concurrency (öneri: 1–2, çünkü 20 iş sınırı var; bkz. `CI_SURE_ANALIZI_v1.md`), check zaman aşımı (≥ 90 dk) | **İnsan kapısı (Sinan)** |
| AGENTS.md / `review-signatures` | Merge'ün "expected-head squash" yerine "kuyruğa ekle" olarak tarif edilmesi | Governance PR, iki review |

## 3. Riskler ve etkileşimler

- **Ajanların API ile merge etmesi:** Kuyruk açılınca doğrudan merge çağrısı reddedilir; ajanlar PR'ı
  kuyruğa ekler (GraphQL `enqueuePullRequest` ya da auto-merge). Bugünkü "expected-head" kontrolü
  kuyruğa ekleme anına taşınır. Kuyruktaki PR'a yeni commit gelirse GitHub PR'ı kuyruktan çıkarır,
  dolayısıyla exact-head güvencesi korunur. Ancak review imzası (REVIEW-READY/DELTA-OK) PR head'ine
  atılır, main'e giren commit ise kuyruğun ürettiği birleşik squash'tır. Bu fark yazılı kurala
  eklenmelidir.
- **Faz 0 ile ilişki:** Faz 0 (GOV-D3), merge eden kişiye `git merge-base --is-ancestor origin/main
  <head>` kontrolünü elle yaptırıyor. Merge queue birleşik sonucu her seferinde **kendisi test
  ettiği** için bu kontrolün yerini alabilir, hatta daha güçlüdür. İki PR'ın birlikte hiç test
  edilmemiş olması (öneri Gözlem 2) da ortadan kalkar.
- **Faz 1 ile ilişki:** Faz 1 (GOV-D2) global bayt pin'ini dar pin'le değiştiriyor. Global pin'in
  dolaylı "PR güncel mi" güvencesinin yerini Faz 0 alıyor; merge queue Faz 0'ın da yerini alabilir.
  Kuyruk varken bile (b) türü incelenmiş pin'ler ve dar pin kalır. Kuyruk yalnız tazeliği çözer,
  insan onayı taşıyan pin'lerin yerini tutmaz. Kuyruk, global pin'in "her merge'de bütün PR'lar
  bozulur" sorununu **tek başına çözmez**: pin değeri yine değişir, kuyruktaki ikinci PR kırmızı
  düşer. Bu yüzden önce Faz 1 gerekir.
- **Kuyruk ve kapasite:** Her kuyruk girişi CI'ın tamamını bir kez daha koşturur (bugün ~136
  iş-dakikası, ~53 dk). CI kısaltılmadan açılırsa merge başına bekleme ~1 saat olur ve PR koşularıyla
  20 iş sınırını paylaşır. `CI_SURE_ANALIZI_v1.md` #1–#2 önce yapılmalıdır.
- **GOV-D4:** Kuyruk yazma yetkili bir token veya bot gerektirmez; merge'ü GitHub kendisi yapar.
  Secret yüzeyi açılmaz.

## 4. Öneri

**Karar Faz 1'den sonra.** Sıra: (1) Faz 0 kuralı uygulanır, (2) Faz 1 dar pin merge edilir, (3) CI
süresi #1–#2 ile ~20 dakikaya iner, (4) PR'lar hâlâ main yüzünden sık sık yeniden hazırlanıyorsa,
Sinan "depoyu organizasyona taşıyıp merge queue açalım mı?" sorusunu o zaman değerlendirir. O güne
kadar bu belge yalnız bilgi amaçlıdır; hiçbir ayar veya workflow değişikliği önermez.
