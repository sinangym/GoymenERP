# Sinan Kararları — 2026-10-26 (Public depo ve GitHub-hosted CI)

Bu belge `CI_SIFIR_MALIYET_MODELI_20261021.md` dosyasını silmez; onun "yalnız self-hosted
runner, artifact yok" maddesinin yerine geçer. Bütçe kuralı (`$0`) aynen kalır.

**Kaynak:** Sinan'ın 26 Ekim 2026 Claude Code cloud oturumundaki doğrudan cevapları
(oturum `session_01CnTLoXcwGRZAnYfGgbm9wK`). Soru ve cevaplar kelimesi kelimesine:

> Sinan: "Önce bütün Git geçmişini parola, anahtar ve kişisel veri için tarayıp size bir rapor
> sunarım. Temiz çıkarsa: Depoyu public yapmayı onaylıyorum ve Ben iş akışlarını GitHub'ın bulut
> makinelerine geçiren PR'ı hazırla"
>
> Soru: "Depo public olunca commit'lerdeki e-postanız ve adınız herkese görünecek. Bu kabul mü?"
> — Cevap: "Kabul, devam (Önerilen)"
>
> Soru: "Prototipteki \"ABC Metal\" / \"Ahmet Kılıç\" iletişim bilgileri gerçek bir firma/kişiye
> mi ait?" — Cevap: "Uydurma, sorun yok"

Aynı gün Satış şeridi oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Sinan'ın seçtiği seçenek,
açıklamasıyla kelimesi kelimesine (alıntı: #819 review #1):

> Soru: "CI'ı GitHub'ın ücretsiz makinelerine taşıyalım mı?" — Seçilen: **"Evet, hepsini taşı
> (Önerilen)"** — "Bütün işler GitHub makinelerinde koşar … İşler arası kanıt, bugünkü yerel klasör
> yerine GitHub artifact ile taşınır. AGENTS.md'deki 'yalnız self-hosted, artifact yok' kuralını
> kaldıran bir karar kaydı yazarım; .github değişikliği iki bağımsız review ile gelir."

## Geçmiş taraması (26 Ekim 2026)

Kapsam: bütün dallar (860), PR referansları (821), 4.977 commit, 14.801 dosya sürümü.

- Gizli anahtar, token, özel anahtar veya gerçek bağlantı dizesi bulunmadı. Parolaya benzeyen
  74 değer test sabiti ya da tek kullanımlık CI SQL konteyner parolasıdır.
- Kod/test içindeki T.C. kimlik, IBAN ve telefon değerleri bilinen test/örnek değerlerdir.
- Kişisel veri: commit yazar bilgisindeki e-posta ve ad; Sinan kabul etti.

## Kararlar

1. **KILITLI — Depo public olur.** Görünürlük değişikliğini Sinan GitHub Settings'ten yapar.
2. **KILITLI — CI yalnız GitHub-hosted runner'larda koşar** (`ubuntu-latest`, Windows işi için
   `windows-latest`). Public depoda bu dakikalar ücretsizdir; `$0` kuralı korunur.
3. **KILITLI — Public depoda self-hosted runner kullanılmaz.** Fork PR'ı sahibinin bilgisayarında
   kod çalıştırabilir. **Asıl koruma insan adımlarıdır** (aşağıda (a) ve (b)): bir fork PR'ının
   `pull_request` koşusu fork'un kendi workflow dosyalarını kullanır; repodaki test bunu
   engelleyemez. `test_all_workflow_jobs_use_github_hosted_runners` yalnız **base repo
   hijyenidir**: main'deki workflow'lara self-hosted etiket girmesini önler.
   `tools/ci/self_hosted_admission.py` yalnız runner'ın job-started hook'u olarak kuruluysa korur.
4. **KILITLI — Job'lar arası CI kanıtı GitHub Actions artifact'ıyla taşınır** (yukarıdaki
   "Evet, hepsini taşı" seçimi). **DELEGE (daraltıcı türetme, Sinan geri alabilir):** artifact
   yalnız secret içermeyen JSON özetleri ve kanıtlardır, saklama ≤ 7 gün; cache, Packages,
   secret, environment, `pull_request_target` ve deploy yüzeyi açılmaz. Aktif workflow'larda bu
   yüzeyleri ve 7 günden uzun saklamayı bir test reddeder.
5. Sıra: PR hazırlanır → Sinan depoyu public yapar → PR'ın CI'ı hosted runner'da koşar → iki
   bağımsız exact-head K3 review → koordinatör merge. Public yapılınca, bu PR merge edilene kadar
   self-hosted job'lar admission kontrolünde reddedilir (beklenen ve güvenli durum).
6. Tetikleyiciler: `c01-real-browser-e2e.yml` ve `cari-c02-link-acceptance.yml` #789 öncesindeki
   gibi yeniden `pull_request` + `push: main` ile koşar (#789'un geri alınmasıyla). #789 bunları
   self-hosted döneminde yalnız `workflow_dispatch`'e daraltmıştı; hosted runner'da dakikalar
   ücretsiz ve işler paralel koşar.

Bu PR'ın dışında kalan, Sinan'ın Settings'ten yapacağı ayarlar (insan kapısı). (a) ve (b) bu PR
merge edilsin edilmesin şimdi gereklidir:

- (a) Settings → Actions → Runners: kullanılmayan self-hosted runner kaydının kaldırılması.
- (b) Settings → Actions → General → "Fork pull request workflows from outside collaborators":
  **"Require approval for all outside collaborators"**. Dışarıdan gelen her PR'ın CI'ı onayla
  başlar.
- (c) Merge sonrası zorunlu check'ler ve branch protection kurallarının public depoya göre
  gözden geçirilmesi.

Değişmeyen insan kapıları: deployment, environment, secret, grant/rol/Critical, production
aktivasyonu ve `OPEN-*` kapatma.
