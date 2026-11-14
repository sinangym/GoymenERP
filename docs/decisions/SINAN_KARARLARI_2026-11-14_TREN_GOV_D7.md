# Sinan Kararları — 2026-11-14 (Tren etkisi: mekanik merge'de makine kanıtı, yığın birleştirme)

Bu belge önceki karar dosyalarını silmez; **GOV-D7**'yi ekler. GOV-D1 (`SINAN_KARARLARI_2026-11-07_K3_DELTA.md`),
GOV-D2…GOV-D4 (`SINAN_KARARLARI_2026-11-07_MUHASEBE_FISI_VE_RECOVERY.md`), GOV-D5
(`SINAN_KARARLARI_2026-11-12_CI_KUYRUK.md`) ve GOV-D6 (`SINAN_KARARLARI_2026-11-13_EK1.md`) geçerlidir. GOV-D7 yalnız
aşağıda yazılan mekanik birleştirme ve yığın hizalaması için ayrı `DELTA-OK` şartını kaldırır; bu şartların dışında
GOV-D1/GOV-D6 ve Faz 0 aynen uygulanır. Güvenlik değişmezleri (`AGENTS.md` "Mimari değişmezler", "Yasaklar", K3
kapsamı, insan kapıları) değişmez.

**Kaynak:** Issue #66 yorumu
[6876636485](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6876636485) ("SINAN-CEVAP (11)",
2026-11-14 11:32:18 UTC; Claude oturumu, serbest metin). Yorum depo sahibinin hesabından yazıldı; cevabın sahibi
yorumda Sinan diye yazılıdır. Bu belgenin yazımı sırasında yorum düzenlenmemişti.

Soranın önerisi (Claude), aynen:

> **Mekanik merge'de DELTA-OK:** Merge mekanikse (generator ve testler çıkış kodu 0, CI yeşil, patch-id eşit), ayrı
> DELTA-OK aranmasın mı? Bu, turların yarısını kaldırır ama bugünkü GOV-D1/GOV-D6 kararını değiştirir.

Sinan'ın cevabı, aynen (yazım hataları dahil):

> Bütün eksiklerive darboğaları planla ve her biri yapıldıkça tamamlanma yüzdeki ve kalanları söyelyerek eksiksiz
> ilerleyelim tren etkisini kaldıralım veya aşmanın alternatif yollarını bularak hızlanalım önerilerinide uygula daha
> derin ve etkili çözümleride araştır, ve hızlanmak için lüzumsuz yâda fuzuli güvenlik kontrolü varsa bunlar geçelim
> bittiğinde zaten sistemi baştan sona analiz ve güvenlik için test edeceksin,

Cevap A/B seçmez ve şartları tek tek saymaz. "önerilerinide uygula" yukarıdaki öneriyi kabul eder. Yorumun
"Uygulama" bölümü (madde 1–5) yorumda açıkça "Sinan'ın sözü değil, cevabın bu oturumca dar yorumudur" diye
işaretlidir; aşağıda **yazar okuması** ve "Uygulama ayrıntısı (geri alınabilir)" olarak kaydedilir.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 11.1 | "Merge mekanikse (generator ve testler çıkış kodu 0, CI yeşil, patch-id eşit), ayrı DELTA-OK aranmasın mı?" | Serbest metin (yukarıda aynen); öneriyle ilgili kısmı: "tren etkisini kaldıralım veya aşmanın alternatif yollarını bularak hızlanalım önerilerinide uygula" | Seçenek yoktu; öneri "aranmasın" yönündeydi. Karşı yön (bugünkü GOV-D1/GOV-D6: mekanik farkta bağımsız `DELTA-OK`) soru metnindedir |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| GOV-D7 | Mekanik merge'de ayrı `DELTA-OK` yerine makine kanıtı; yığın birleştirme; tren etkisi | **Sinan'ın sözü:** (1) Tren etkisi kaldırılır ya da aşmanın başka yolları bulunur; öneriler uygulanır; daha derin çözümler araştırılır. (2) Hızlanmak için lüzumsuz ya da fuzuli güvenlik kontrolü varsa geçilir. (3) İş bitince sistem baştan sona analiz ve güvenlik için test edilir; bu, bu oturumun işi olarak kayda geçer. (4) Öneri kabul edildi: **merge mekanikse (üretici ve testler exit 0, CI yeşil, patch-id eşit) ayrı `DELTA-OK` aranmaz.** **Uygulama ayrıntısı (geri alınabilir; yazar okuması):** (5) **Mekanik birleştirme şartları; hepsi sağlanmalıdır:** (a) `git rev-parse <head>^1` imzalı (review'lu) head'dir; `<head>^2` `origin/main` ya da yığında bir önceki PR'ın aynı şartları sağlayan head'idir. (b) `git show --remerge-diff <head>` boştur; ya da yalnız üretici sahipli dosyaları gösterir (recovery envanteri ve pin'leri, parmak izleri, OpenAPI sözleşmesi ve beklentileri, relational inventory, test grubu sayıları) ve bu dosyalar üreticinin taze çıktısıyla aynıdır. (c) Üretici ve recovery/release testleri exit 0'dır; PR'ın kendi değişikliği korunmuştur (üretici dosyaları dışında patch-id eşit). (d) Test edilen ağaçta zorunlu CI yeşildir. Bu makine kanıtı merge makbuzuna yazılır; herkes yeniden üretebilir. (6) **`src/**`, `tests/**` gövdesinde veya migration'da elle çakışma çözümü varsa (5) uygulanmaz; GOV-D1/GOV-D6 aynen geçerlidir.** (7) **Yığın birleştirme:** sıradaki PR'ın dalı önündeki onaylı PR'ların head'ini içerir; CI her yığın head'inde bir kez koşar. Öndeki PR squash edildikten sonra `git rev-parse origin/main^{tree}` önceki yığın head'inin ağacına eşitse sıradaki PR yeni CI turu beklenmeden merge edilir. Squash soy bağını kopardığı için dal `git merge -s ours origin/main` ile hizalanır; ağaç hash'i değişmez ve iki hash makbuza yazılır. Ağaçlar eşit değilse bugünkü Faz 0 kademeleri uygulanır. (8) Her merge'den sonra bütün açık PR'lar yeniden güncellenmez; yalnız sıradaki PR ya da yığın güncellenir. (9) **Değişmeyenler:** K3'te iki bağımsız tam review, K1/K2'de bir bağımsız review, governance/karar belgelerinde iki DOCS review ve yerel hafif kontroller; test edilen ağaçta yeşil CI; yeni davranış testi; açık P0/P1 yok; #66 makbuzu; yazar kendi işini onaylayamaz; ücretli CI açılmaz | `AGENTS.md` Faz 0 paragrafı; `.claude/skills/review-signatures/SKILL.md`; `.claude/skills/main-merge-resync/SKILL.md`; merge makbuzları |

## Uygulama notları (yazar okuması)

- (5) hem K1/K2 hem K3 PR'ına uygulanır; yorum seviyeye göre ayırmaz. (6)'daki el çözümü ve (5)'in herhangi bir
  şartının sağlanmaması GOV-D1/GOV-D6'ya döner (K3'te mekanik sınıf + bağımsız `DELTA-OK` ya da iki yeni tam review;
  K1/K2'de v4'e göre review).
- (5)(a) gereği yeniden üretilen dosyalar merge commit'inin içinde olmalıdır. Merge commit'inin üstüne ayrı bir
  "re-pin" commit'i gelirse yeni head'in `^1`'i imzalı head olmaz; o head için GOV-D1/GOV-D6 uygulanır.
- (7)'deki `git merge -s ours` commit'i ağaç değiştirmez; yeni CI istemek için yapılmaz, yalnız soy bağını kurar.
  `AGENTS.md`'deki "boş commit ile taze merge ref yasağı" CI tazeliği içindir ve bu hizalamaya karışmaz.
- Makbuz satırı: makine kanıtı (`^1`/`^2` hash'leri, remerge-diff sonucu, üretici/test exit kodları, patch-id
  karşılaştırması, CI run kimliği) ya da yığında iki ağaç hash'i.

## Sınırlar

- **Geri alma:** Sinan #66'ya tek satır yazarsa bu okuma durur; GOV-D1/GOV-D6 ve Faz 0 eskisi gibi uygulanır.
- **Değişmeyen bloklar:** `AGENTS.md`'deki Faz 0 (c) bloğu (`SKIPPED`/`READ_BY_CI`) ve `SCOPE` listesi değişmez;
  `tests/ci_safe_trx_summary/test_workflow_path_filters.py` (c) bloğunu `ci.yml` yol filtresine sabitler.
- **GOV-D1 ve GOV-D6 dosyaları değişmez.** GOV-D6 (4)'teki imzacı okuması da değişmez; GOV-D7 ayrı imzanın
  gerekmediği durumu tanımlar.
- **Açık kalanlar (`NEEDS-SINAN`):** "lüzumsuz ya da fuzuli güvenlik kontrolü" ifadesinin mekanik merge ve yığın
  hizalaması dışında hangi kontrolleri kapsadığı seçilmedi. Başka bir kontrolün kaldırılması ayrı önerilir ve
  kaydedilir; para/stok/üretim/formül/yetki/güvenlik/migration/saklama kapıları tahminle gevşetilmez.
- Finance STOP ve insan kapıları (deployment, environment, secret, Settings → Actions'ta etkinleştirme,
  grant/rol/Critical, production, `OPEN-*` kapatma, `.claude/settings.json`) değişmez. Hiçbir `OPEN-*` kapanmadı.
  `KARAR_SICILI` ve `ACIK_KARARLAR` değişmez.
