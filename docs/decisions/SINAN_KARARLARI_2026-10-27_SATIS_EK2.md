# Sinan Kararları — 2026-10-27 (Satış etiket seçici ve planın tamamı)

Bu belge `SINAN_KARARLARI_2026-10-25_SATIS.md` ve `SINAN_KARARLARI_2026-10-26_SATIS_EK.md` dosyalarını
silmez; onlara SAT-D20 ve SAT-D21'i ekler.

**Kaynak:** Sinan'ın 26/27 Ekim 2026 (UTC 26 Ekim gece) Claude Code cloud oturumundaki doğrudan mesajı
(oturum `session_01SPADrzecggx3apH2gfNW7b`). Soru Issue #66'daki `NEEDS-SINAN` satırıydı
([6421712252](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6421712252)): etiket seçicide
karşı tarafa ait/gözetimdeki veya kullanılabilir miktarı 0 olan etiketler süzülsün mü, yoksa görünüp
"seçilemez" mi olsun. Cevap, kelimesi kelimesine:

> "Seçilemez görünsün, ama etiket geçmişine bakabilelim, birde bu miktar sıfır olanlar varsayılan
> filtrede görünmesin filtre kaldırılırsa gelsin ki sayfada anlamsız çok yer kaplamasın, ayrıca
> sales(satış) modülü için olan tüm planın tamamının kodlarını yazalım sen sırayla kuyrukta doğrulad ve
> merge edersin , yani tüm satış modülünün tamamı için oluşturulan planın tamamı eksiksiz bitecek şekilde
> devam edip eş zamanlı ajanlarla kodları ve yazılımı geliştirebilirsin"

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D20 | Tahsis paneli etiket seçicisinde tahsis edilemeyen etiketler (UI-13) | (1) Şirketin kendi malı ve kendi gözetiminde olmayan ya da başka bir nedenle tahsis edilemeyen etiketler listede **görünür ama seçilemez**; satırda nedeni yazar. (2) Her satırdan (seçilebilir olsun olmasın) **etiket geçmişine** gidilebilir (Stok'un mevcut etiket detayı/geçmişi görünümü; bu görünümün kendi okuma yetkisi geçerlidir). (3) **Kullanılabilir miktarı 0 olan etiketler varsayılan filtrede görünmez**; kullanıcı filtreyi kaldırırsa listelenir (yine seçilemez). Stok kayıt anında aynı kuralları uygulamaya devam eder; seçicideki süzme yalnız görünümdür, güvenlik kontrolü değildir | Satış UI-13 (`AllocationLabelPicker`); Stok ST-01 okuması (MOD-12) |
| SAT-D21 | Satış hazırlık planının tamamı | Satış hazırlık planı v1'in (`docs/delivery/SATIS_HAZIRLIK_PLANI_v1.md`) **bütün dalgaları (S0–S6) eksiksiz** kodlanır. Claude şeridi işleri eşzamanlı ajanlarla geliştirir; her PR'ı kuyrukta sırayla doğrular ve `SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md` M1/M2 kapılarıyla merge eder. Planda Codex (CX) şeridine yazılmış işler de bu talimat kapsamında Claude tarafından yapılabilir; Codex'in aynı işte açık PR'ı varsa çakışma önce Issue #66'da bildirilir | Plan v1 §7; merge yetkisi M1/M2 |

## Sınırlar

- SAT-D21 merge kapılarını (fresh CI, exact-head bağımsız review, K3'te iki review, açık P0/P1 yok)
  ve insan kapılarını (M3: deployment, environment, secret, Settings, grant/rol/Critical, production,
  `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez.
- Planın bir adımı açık bir işletme politikasına (fiyat, formül, eşik, para, stok, yetki, saklama)
  dayanıyorsa bu talimat o politikayı seçme yetkisi vermez: değer yapılandırılabilir ve fail-closed
  bırakılır, soru Issue #66'ya `NEEDS-SINAN` olarak yazılır ve sıradaki işe geçilir (AGENTS.md).
- SAT-D20 hiçbir rol veya grant açmaz; etiket geçmişi bağlantısı Stok'un mevcut yetkisiyle çalışır.
