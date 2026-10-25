# Sinan Kararları — 2026-10-25 (Claude şeridine merge yetkisi)

Bu belge 31 Ağustos, 21 Ekim ve 22 Ekim kararlarını silmez. Yalnız "merge'i `CODEX-ORCHESTRATOR`
yapar / yazar ≠ koordinatör" satırlarına aşağıdaki dar istisnayı ekler. Diğer bütün koşulları aynen
yürürlükte bırakır.

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

1. **Kapsam:** yalnız Claude şeridinin açtığı PR'lar: PR'ı bir Claude oturumu açmıştır ve dal `claude/*`'dır.
   - Codex/ChatGPT şeridinin açtığı bir PR'a Claude commit itmiş olsa bile o PR bu kapsama girmez.
   - Claude bu PR'ları `CODEX-ORCHESTRATOR`'ı beklemeden **squash merge** edebilir; kendi açtığı
     governance/karar PR'ları da buna dahildir.
   - Merge'ler bağımlılık sırasıyla yapılır; yığılı PR'ın base'i önce `main`'e alınır.
2. **Bilgilendirme:** merge sonrası `CODEX-ORCHESTRATOR` bilgilendirilir.
   - Issue #66'ya tek satır makbuz yazılır: PR, merge SHA, review bağlantıları.
   - Codex oturumu erişilebilirse aynı özet oraya mesaj olarak gönderilir; erişilemezse Sinan'a
     yapıştırılacak metin verilir.
   - Bilgilendirme onay beklemez.
3. **Geri alma:** yetki Sinan'ın onayını beklemeden kullanılır. Sinan Issue #66'ya yazacağı tek
   satırla veya oturumda doğrudan geri alabilir.

## M2. Değişmeyen kapılar

Merge'i yapan değişir, kapılar değişmez. AGENTS.md ve önceki kararlardaki **bütün** merge koşulları
aynen uygulanır. Özellikle:

- **K1/K2:**
  - fresh zorunlu CI;
  - exact-head bağımsız review;
  - yeni davranış testi;
  - açık P0/P1 yok.
- **K3:** K1/K2 koşullarına ek olarak:
  - kilitli kararları uygular;
  - açık işletme politikasını seçmez;
  - iki bağımsız exact-head review;
  - ilgili gerçek SQL/browser/failure kanıtı.
- **Governance/karar/`.claude/**`/`.github/**` PR'ı (22 Ekim §0):**
  - iki bağımsız exact-head review;
  - yalnız doküman/ayar dosyası değiştiriyorsa CI şartı yoktur;
  - `docs/decisions/**`, `docs/backlog/**`, `.github/**` için yerel hafif kontroller kanıtıyla
    çalıştırılır;
  - `.github/**`'da `runs-on` self-hosted ve secret/deploy doğrulaması yapılır;
  - **`.claude/settings.json` istisnası aynen geçerlidir:** Sinan'ın Issue #66'daki açık tek satırlık
    onayı olmadan merge edilmez.
- Kanıt (review notları, CI, yerel komutlar) GitHub'a yazılmadan merge yapılmaz.

**ASSUMPTION-MERGE-1:** "bağımsız review"u Claude, yazarın bağlamını taşımayan ayrı bir ajan oturumuyla
sağlar. Bunun dayanakları:
- Sinan'ın "beklemeden … gereken herşeyi yap" talimatı;
- v4'ün `karşı-ajan` review'u tam zamanında beklemenin bu talimatla çelişmesi.

Review metni PR'a yazılır. Sinan bu yorumu tek satırla daraltabilir; örneğin "en az bir review Codex
şeridinden olsun".

Bu varsayımda ChatGPT hakemliği yoktur. Bu yüzden Issue #66'ya `NEEDS-SINAN` satırı olarak yazılır ve
Sinan döndüğünde teyit edilir; teyide kadar varsayım geri alınabilir durumda yürürlüktedir.

## M3. İnsan kapıları

Aşağıdakiler her zaman somut kullanıcı kararı ister; merge yetkisi bunlardan hiçbirini vermez:

- deployment, environment, secret;
- Settings → Actions etkinleştirme;
- grant/rol atama/Critical;
- production aktivasyonu;
- gerçek sağlayıcı, bütçe/ücretli servis, `config.toml`;
- `.claude/settings.json`;
- `OPEN-*` kapatma.
