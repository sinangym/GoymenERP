# `claude/*` push'larını soru sormadan geçirmek — hook notu v1

- **Tarih:** 2026-11-07
- **Durum:** Öneri notu. `.claude/**` değişikliği yok. **Karar Sinan'da.**
- **Hazırlayan:** Claude şeridi · **Kaynak:** `origin/main` `11299b14f`; PR #969 review'ları

**Kısaca (Sinan için):** Ajanlar her `git push`'ta onay soruyor. #969 bunu ayar kuralıyla açmaya
çalıştı, ama ayar kuralındaki `*` boşluğu ve `:` işaretini de kapsıyor. Bu yüzden
`git push origin claude/x:main` gibi main'e yazan komutlar da geçerdi. #969 sonunda genel "sor"
kuralını korudu. Bu not daha dar bir yolu anlatıyor: küçük bir kontrol betiği (hook) yalnız
`git push origin claude/<dal>` biçimine birebir uyan komutu geçirir, geri kalan her şeyde soru sorar.
Asıl sunucu tarafı güvence ise main'i GitHub'da korumaya almaktır. Bu, Sinan'ın ayrı kararıdır.

## Neden ayar kuralı yetmedi

`Bash(git push origin claude/*)` kuralı şunları da geçirirdi: `claude/x:main`, `+main`, `--force`,
`--delete main`, `claude/x:codex/y`, `--tags`. API'ye göre `main` korumalı değil (`protected: false`,
ruleset yok), yani GitHub da bu push'ları reddetmez.

## Öneri: PreToolUse hook

`.claude/hooks/` altında bir betik, `.claude/settings.json`'da `PreToolUse` / `matcher: "Bash"` ile
bağlanır. Betik `tool_input.command` değerini okur ve şunu yapar:

- Komut **tam olarak** `^git push( -u)? origin claude/[A-Za-z0-9._/-]+$` ise ve `..` içermiyorsa
  → `{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"allow"}}`
- `git push` ile başlayan başka her komut → `"permissionDecision":"ask"`
- Başka komutlar → çıktı yok (exit 0); normal izin akışı devam eder.

Komutun herhangi bir yerinde satır sonu, `;`, `&`, `|`, `` ` ``, `$(`, `<`, `>`, tırnak, `:`, `+`
ya da `-u` dışında bir bayrak varsa "allow" verilmez.

**Önemli kısıt (Claude Code belgesi, PreToolUse):** *"Deny and ask rules are still evaluated
regardless of what the hook returns"*. Yani hook'un "allow" demesi, ayardaki `ask: Bash(git push:*)`
kuralını **aşmaz**. Kural kaldıkça hook işe yaramaz. Dolayısıyla genel `ask` kuralı kaldırılmalı ve
"diğer her push'ta sor" görevini hook üstlenmeli. `deny` kuralları (`--force`, `main` ve diğerleri)
aynen kalır; hook bunları geçemez.

**Bunun bedeli:** Genel `ask` kuralı, hangi izin modunda olunursa olunsun her push'ta soru sordurur.
Hook ise belgeye göre zaman aşımına uğrarsa ya da hata verirse çağrıyı **durdurmaz**: komut normal
akışa düşer. Bu durumda listede olmayan bir push, varsayılan modda yine soru sordurur, ama başka bir
modda (ör. auto) soru sorulmadan geçebilir. Seçenekler:

- (a) Bugünkü gibi kalmak (her push'ta soru, en güvenli seçenek)
- (b) Hook + genel `ask`'ın kaldırılması (soru yalnız dar biçim dışında, hook arızasında boşluk var)
- (c) (b) + main koruması: hook arızalansa bile GitHub main'e doğrudan yazmayı reddeder

## Riskler

- Betik bir güvenlik yüzeyine dönüşür. Ham komut metnini ayrıştırması gerekir; kabuk ayrıştırmasına
  güvenmez, beyaz liste regex'i dışında hiçbir şeyi kabul etmez.
- Betik yalnız Python standart kütüphanesini kullanmalı ve kısa olmalı; zaman aşımı, hata ve
  geçersiz JSON durumlarında "allow" vermemeli.
- `claude/` öneki yalnız ad kontrolüdür. Başka bir şeridin `claude/*` dalının üzerine yazmayı
  engellemez; force olmayan push bunu zaten reddeder.

## Test durumları (betikle birlikte unittest)

- **allow:** `git push origin claude/a`, `git push -u origin claude/ci-sure_v1.2/x`
- **ask:** `git push origin claude/x:main`, `… claude/x:codex/y`, `git push origin +main`,
  `git push --delete origin main`, `git push origin --tags`, `git push origin claude/a claude/b`,
  `git push origin claude/../main`, `git push origin claude/x && git push origin main`,
  `git push origin claude/x; rm -rf .`, `git push origin claude/x | cat`, `` git push origin claude/`id` ``,
  `git push origin claude/$(id)`, satır sonu içeren komut, `git push upstream claude/x`,
  `git  push origin claude/x` (çift boşluk), `git push`
- **deny (ayar kuralı, hook'tan bağımsız):** `git push --force …`, `git push origin main`
- **çıktı yok:** `git status`

## Yönetişim

`.claude/settings.json` ve `.claude/hooks/**` değişikliği governance PR'ıdır: **iki bağımsız
exact-head review** gerekir ve AGENTS.md (25 Ekim istisnası) gereği `.claude/settings.json` için
**Sinan'ın Issue #66'da onayı** şarttır. `main` için branch protection veya ruleset kurmak (doğrudan
push'u, force'u ve silmeyi reddetmek) bir Settings işlemidir, **insan kapısıdır**. Bu, istemci
tarafındaki her kuraldan daha güçlü, gerçek bir sunucu tarafı güvencedir. Önerilen sıra: önce main
koruması (Sinan), sonra hook (c seçeneği).
