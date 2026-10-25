# Sinan Kararları — 2026-10-25 (Claude şeridine merge yetkisi)

Bu belge 31 Ağustos, 21 Ekim ve 22 Ekim kararlarını silmez. Merge'i yalnız `CODEX-ORCHESTRATOR`'a
bırakan satırı, aşağıdaki dar kapsamda genişletir.

**Kaynak:** Sinan'ın 25 Ekim 2026 Claude Code cloud oturumundaki doğrudan mesajı
(oturum `session_01SPADrzecggx3apH2gfNW7b`), kelimesi kelimesine:

> "Merge'i CODEX-ORCHESTRATOR review'dan sonra yapacak, burda yapılanlar için codex ORCHESTRATOR u
> beklemeden sen mergeleri ve gereken herşeyi yapıp sadece codex-ORCHESTRATOR'a yaptığının bilgisini
> gönderebilirsin, o konuşmaya yaptıklarını yapıştırıp mesaj ile gönderebilirsin, böylelikle beklemeden
> devam et ve benden onay izin almadan devam et çünkü ben bi kaç saat olmayacağım sen bu süreçte yazılım
> geliştirme ve planı ilerletmeye devam edip merge gerektikçe yapabilirsin, sırayla birleşmeleri ve
> mergeleri benden onay almadan yapabilirsin, kuraldada sadece codex-ORCHESTRATOR merge yaparı
> değiştirebilirsin ve mergeleri sende yapabilirsin"

## M1. Yetki

1. Claude şeridi (cloud oturumu), kendi açtığı veya sürdürdüğü PR'ları `CODEX-ORCHESTRATOR`'ı
   beklemeden **squash merge** edebilir. Merge'ler bağımlılık sırasıyla yapılır; yığılı PR'ın
   base'i önce `main`'e alınır.
2. Merge sonrası `CODEX-ORCHESTRATOR` bilgilendirilir: Issue #66'ya tek satır makbuz
   (PR, merge SHA, review bağlantıları). Bilgilendirme onay beklemez.
3. Bu yetki Sinan'ın onayını beklemeden kullanılır; Sinan Issue #66'ya yazacağı tek satırla veya
   oturumda doğrudan geri alabilir.

## M2. Değişmeyen kapılar

Merge, `CODEX-ORCHESTRATOR` için geçerli kapıların aynısını ister. Yalnız merge'i yapan kişi değişir:

- **Fresh zorunlu CI:** merge edilecek exact head'de yeşil olmalı. Yalnız doküman/governance
  PR'ında CI şartı yoktur; `git diff --check` ve AGENTS.md'deki hafif yerel kontroller yeter.
- **Bağımsız exact-head review:** K1/K2'de bir, K3'te iki.
  - Yazarın bağlamını taşımayan ayrı bir ajan oturumunun review'u bağımsız review sayılır.
  - Review notu PR'a yazılır.
  - Açık P0/P1 olmamalı.
- **K3 kanıtı:** gerçek SQL, browser veya failure kanıtı gerekir.

**İnsan kapıları aynen kalır:** deployment, environment, secret, Settings → Actions etkinleştirme,
grant/rol atama/Critical, production aktivasyonu ve `OPEN-*` kapatma her zaman somut kullanıcı
kararı ister. Merge yetkisi bunlardan hiçbirini vermez.
