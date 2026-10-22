# Göymen ERP — Hızlı Teslim Planı v4.0 (v3 üstüne tören azaltma, r2)

Tarih: 2026-10-21 · Kaynak: Sinan'ın 21 Ekim talimatı ("6 yavaşlatma nedenini ortadan
kaldıralım") · Makbuz: Sinan'ın bu PR'ı merge etmesi · Durum: insan review kapısında.

v4, v3'ün **rol dağılımını ve dilim sırasını korur**; yalnız tören seviyesini düşürür ve
karar akışını değiştirir. `AGENTS.md` mimari değişmezleri ve yasakları, `KARAR_SICILI`
önceliği, production aktivasyonunun insanda kalması, Finance STOP, AI `A-contract-only`,
K3 tanımı ve K3'teki iki bağımsız exact-head review aynen korunur. Çelişkide v4 süreç
tarafında, v2/v3 güvenlik tarafında üstündür.

**Yürürlük:** v4, `AGENTS.md` "Çalışma orkestrasyonu" bölümüne §5'teki işaretçi Sinan
tarafından merge edilene kadar yürürlükte değildir; o güne kadar v3, Issue #66'daki dilim
başına START-GO pratiği ve her push'ta exact-head review kuralı geçerlidir.

## 1. Bırakılanlar → yerine gelenler

| Bırakılan | v4 |
|---|---|
| Dilim başına Sinan START-GO'su (Issue #66 pratiği; v3 metninde yok — v3 §5/§6.1 yalnız NEEDS-SINAN + günlük check-in tanımlar) | **Dalga START-GO:** v4 kabulünden sonra koordinatör, kararı sicilde KILITLI veya 2026-10-21 belgesinde cevaplı olan dilimler için START-GO'yu kendisi verir (Issue #66'ya 5 satır: dilim, owner, base, exact dosyalar, K sınıfı). Sinan'a yalnız P8 dışı (para/stok/üretim/formül/eşik/yetki/güvenlik/migration/saklama politikası, `OPEN-*` kapatma, production) sorulur. |
| Ayrı READ-ACK yorumu | ACK, PR açıklamasının ilk satırıdır (`READ-ACK <start-go-id> base <sha>`). |
| Her turda checkpoint yorumu | Yalnız **merge**, **bloke** ve **haftalık özet**te yorum. |
| K1/K2'de her push'ta tam yeniden review | **Review dondurma + delta review:** yazar `REVIEW-READY <sha>` yazar, review gelene kadar push yapmaz; bulgular yeni commit'le kapanır; reviewer `git diff <REVIEW-READY sha>..<yeni head>` deltasını okur ve `DELTA-OK <yeni-sha>` yazar. `DELTA-OK <sha>` o head için **exact-head imzasıdır**: koordinatörün expected-head'i = son DELTA-OK sha'sı; DELTA-OK'tan sonraki her push yeni `REVIEW-READY` ister; delta `origin/main` merge'i veya REVIEW-READY diff'i dışındaki dosyayı içeriyorsa delta modu düşer, tam review gerekir. |
| K3'te de aynı | **Değişmez:** K3'te delta review uygulanmaz; her yeni head için iki bağımsız tam exact-head review yeniden alınır. Review dondurma (aynı anda tek aday head) K3'te de uygulanır. |
| NEEDS-SINAN → bekleme | **P8 delegasyonu** (`SINAN_KARARLARI_2026-10-21.md` §1): bir karardan veya P1–P7'den doğrudan türeyen soruyu koordinatör `DELEGE-*` olarak cevaplar (GECICI_VARSAYIM; registry v1 değeri; Sinan geri alabilir), devam eder; sicile KILITLI yalnız Sinan'ın açık onayıyla — sessizlik onay değildir. Politika/`OPEN-*` sorusu `NEEDS-SINAN` kalır, haftalık listede Sinan'a gider. |
| Hosted CI / bütçe tartışması | `CI_SIFIR_MALIYET_MODELI_20261021.md`: self-hosted, artifact/cache yok, bütçe `$0`; `.github/**` PR'ında Sinan `MERGE-OK` makbuzu şart. |
| Her komut için izin onayı | Repo `.claude/settings.json` izin listesi (Ek A; koordinatör ayrı dar PR ile ekler); cloud oturumlarında Auto modu. |

## 2. Dur koşulları (değişmedi, 3 tane)

P0 güvenlik/veri bütünlüğü · dilimin tamamı P8 dışı bir karara bağlı · bir şeritte arka
arkaya 2 davranışsız tur.

## 3. Merge treni kuralı

- Aynı anda en fazla 5 aday PR; FIFO: `#765 (CI; Sinan MERGE-OK ile) → #749 → #752 → #750 →
  #748 → #755`, sonra TAKIP_TABLOSU sırası.
- Yeni PR açmadan önce yazarın açık aday PR'ı en fazla 1 (+1 remediation).
- Merge ön şartı: zorunlu job'lar aday head'de tam; K1/K2 tek review (`REVIEW-READY <sha>`
  veya sonraki `DELTA-OK <sha>` = aday head); K3 iki tam review; `.github/**` PR'ında Sinan
  `MERGE-OK <sha>`; P0/P1 yok. Koordinatör expected-head squash merge; expected-head ≠ son
  imzalı sha ise merge yok.

## 4. Gece slotu

22:00–07:00: ağır testler (SQL, browser, full) tek kilit altında sırayla; sonuç sabah kanıt
klasöründe. Gündüz yalnız hafif job'lar.

## 5. Bu belgenin kuralı ve AGENTS.md işaretçisi

v4 bir kez yazılır. Aynı governance PR'ında `AGENTS.md` "Çalışma orkestrasyonu" bölümüne, v3
satırının hemen altına şu paragraf eklenir (insan review, Sinan merge eder; otomatik merge
yok):

> `docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v4.md` merge edilmişse v3'ten **önce** okunur ve
> uygulanır. v4 (2026-10-21 kullanıcı kararı §2), v3'ün tören seviyesini ve karar akışını
> günceller (dalga START-GO, review dondurma + K1/K2 delta review `DELTA-OK`, P8 `DELEGE-*`
> delegasyonu, yorum disiplini); güvenlik değişmezleri, K3 tanımı ve K3'teki iki bağımsız
> exact-head review aynen korunur. K1/K2'de dondurulmuş `REVIEW-READY <sha>` üzerindeki tam
> karşı-ajan review + merge edilecek son head için `DELTA-OK <sha>` imzası "exact-head
> bağımsız review" şartını sağlar. Çelişkide v4 süreç tarafında, v2/v3 güvenlik tarafında
> üstündür.

## Ek A — `.claude/settings.json` önerilen izin listesi (koordinatör ayrı PR ile ekler)

Claude Code'un ajan oturumlarında her komut için izin sormasını keser. Cloud oturumlarında
ayrıca mod menüsünden **Auto** seçilir.

`deny` listesi **best-effort**tur, güvenlik sınırı değildir: listelenen `git`/`rm`
komutlarını ve `.env`, `*.pfx`, `secrets/` okumalarını (Read/Edit/Write araçları ve Bash'teki
`cat`/`head`/`tail`/`sed`/yönlendirmeler) kapatır; dosyayı kendisi açan bir Python/Node
betiğini kapsamaz. Bu yüzden genel yorumlayıcı izni (`Bash(python3:*)`) listede yoktur; yalnız
repo'nun kullandığı `python3 -I -S -B tools/...` ve `-m unittest discover` biçimleri vardır.
`main`'e doğrudan push `deny`'dadır; `main`'e giren tek yol koordinatörün expected-head squash
merge'idir (AGENTS.md: teknik koruma yokken lane self-merge kapalı). Gerçek secret koruması:
ajan çalışma ağacında ve runner'da `.env`/`*.pfx`/`secrets/` tutulmaz; gerçek değerler yalnız
runner ortam değişkenindedir. Secret/production yüzeyi kararı insanda kalır.

Kaynak incelemesi düzeltmesi: `git push` otomatik allow listesinde değildir; tüm standart
push çağrıları `ask` ile değerlendirilir. Böylece argüman sonundaki `--force` veya farklı
refspec yazımları otomatik izin kazanmaz. Hassas yollar için açık `Edit` deny kuralları da
vardır; yalnız `Read` deny'nin sürüme bağlı Write davranışına güvenilmez. Bunlar programın
her çağrı biçimini kapsayan bir işletim sistemi güvenlik sınırı değildir.

```json
{
  "permissions": {
    "allow": [
      "Read", "Glob", "Grep", "Edit", "Write",
      "Bash(git status:*)", "Bash(git log:*)", "Bash(git diff:*)", "Bash(git show:*)",
      "Bash(git branch:*)", "Bash(git fetch:*)", "Bash(git rev-parse:*)", "Bash(git ls-files:*)",
      "Bash(git add:*)", "Bash(git commit:*)",
      "Bash(git checkout -b:*)", "Bash(git switch -c:*)",
      "Bash(dotnet restore:*)", "Bash(dotnet build:*)", "Bash(dotnet test:*)",
      "Bash(dotnet format:*)", "Bash(dotnet ef migrations:*)",
      "Bash(npm ci:*)", "Bash(npm install:*)", "Bash(npm run:*)", "Bash(npm test:*)",
      "Bash(npm audit:*)", "Bash(npx vitest:*)", "Bash(npx playwright:*)",
      "Bash(npx tsc:*)", "Bash(npx eslint:*)",
      "Bash(python3 -I -S -B tools/*)", "Bash(python3 -I -S -B -m unittest discover *)",
      "Bash(ls:*)", "Bash(cat:*)", "Bash(head:*)", "Bash(tail:*)", "Bash(wc:*)",
      "Bash(find:*)", "Bash(rg:*)", "Bash(sha256sum:*)", "Bash(sed -n:*)", "Bash(mkdir:*)",
      "WebFetch(domain:github.com)", "WebFetch(domain:docs.github.com)",
      "WebFetch(domain:learn.microsoft.com)"
    ],
    "ask": ["Bash(git push:*)"],
    "deny": [
      "Bash(git push --force:*)", "Bash(git push -f:*)", "Bash(git push --force-with-lease:*)",
      "Bash(git push origin main:*)", "Bash(git push -u origin main:*)",
      "Bash(git push origin HEAD:main:*)", "Bash(git push -u origin HEAD:main:*)",
      "Bash(git reset --hard:*)", "Bash(git clean:*)", "Bash(git rebase:*)", "Bash(rm -rf:*)",
      "Read(.env)", "Read(**/.env)", "Read(**/*.pfx)", "Read(**/secrets/**)",
      "Edit(.env)", "Edit(**/.env)", "Edit(**/*.pfx)", "Edit(**/secrets/**)"
    ]
  }
}
```
