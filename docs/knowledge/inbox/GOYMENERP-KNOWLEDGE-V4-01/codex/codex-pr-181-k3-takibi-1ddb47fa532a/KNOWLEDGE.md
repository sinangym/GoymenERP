# PR #181 K3 Takibi — Codex Kaynak Bilgisi

Bu paket, görünen `PR 181 K3 Takibi` konuşmasındaki kullanıcı kararlarını, izleme kabul ölçütlerini ve 4 Eylül 2026 tarihinde canlı GitHub kaynağından yeniden doğrulanan durumu taşır. Kapsam `visible_only`, paket durumu `partial`dır; görünmeyen eski konuşma turları ve diğer sağlayıcılardaki ayrı konuşmalar bu pakette temsil edilmez.

## Normatif kullanıcı talebi

- PR #181 yalnız salt okunur izlenmelidir; PR, branch, yorum, review, dosya ve workflow üzerinde değişiklik yapılmamalıdır.
- Her turda canlı `main`, exact PR head/base, merge-base, ahead/behind ayrışması, Draft/state/mergeability ve changed-file kapsamı canlı kaynaktan doğrulanmalıdır.
- Yeni conversation yorumları, formal review'lar, review thread'leri ve P0/P1 bulguları izlenmelidir.
- İki bağımsız yeni exact-head K3 review tamamlanmadan inceleme kapısı kapanmış sayılmamalıdır.
- Standard CI, UI catalog golden ve C-01 real-browser E2E sonuçları birbirinden ayrı doğrulanmalıdır.
- `G-PRD-CI-01`, yalnız shared workflow aşağıdaki komutları görünür runner step/job olarak çalıştırıp nonzero exit'i job failure'a aktardığında kapanabilir:
  - `node docs/acceptance/PRODUCT_CORE_JIT_VALIDATOR_v1.mjs`
  - `node --test docs/acceptance/PRODUCT_CORE_JIT_VALIDATOR_MUTATION_TEST_v1.mjs`
- Genel CI yeşili bu özel CI kapısının kapanış kanıtı değildir.
- PR head veya canlı `main` değişirse önceki exact-SHA review, CI ve mutation kanıtı güncel-main entegrasyonu açısından stale kabul edilmelidir.
- Product schema, API, migration, UI veya production implementation için GO varsayılmamalıdır.

## Canlı kaynak durumu

4 Eylül 2026 doğrulamasında:

- canonical `main`: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`
- PR #181 exact head: `cc2ea1ee4b29b05f98d06158165c9bbf9f4b2220`
- kayıtlı base ve merge-base: `9a7f9b0e2bc2c7664094f6cf4d0000b913169145`
- ayrışma: `ahead=1`, `behind=18`, `diverged`
- PR: open, Draft, `merged=false`, `mergeable=true`
- changed files: 12/12 izinli; yalnız `docs/features/**`, `docs/acceptance/**`, `docs/formulas/**`
- formal review: 0; iki bağımsız güncel-main uyumlu exact-head K3 review eksik
- exact-head CI `33578332007`: success
- exact-head UI catalog golden `33578332169`: success
- exact-head C-01 real-browser E2E `33578331972`: success
- üç workflow'da iki Product validator komutu için görünür runner step/job bulunmuyor; `G-PRD-CI-01` açık

Üç başarılı regression koşusu exact PR head için geçerli olsa da head canlı `main`in 18 commit gerisindedir. Bu nedenle sonuçlar güncel-main entegrasyon kanıtı değildir; restack sonrasında yeni exact-head kanıt gerekir.

## Yorumlama düzeltmesi

No-change durumunda bildirim göndermeme kuralı yalnız salt-okunur izleme turlarına aittir. Kullanıcının yapıştırdığı operasyon talimatını gerçekleştirme isteği, değişiklik yok gerekçesiyle `SKIP_COMPLETION` sonucuna dönüştürülemez.

## Entegrasyon sınırı

Bu kaynak paketi davranış, schema, API, migration, UI veya workflow değiştirmez. Final Master V4 Candidate ancak kullanıcı tarafından doğrulanmış tam kaynak envanteri, gerekli üç sağlayıcı kaynağı ve entegrasyon kapıları tamamlandığında değerlendirilebilir.
