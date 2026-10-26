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
   kod çalıştırabilir. `tools/ci/self_hosted_admission.py` public depoyu zaten reddeder; aktif
   workflow'larda self-hosted etiketi test ile yasaktır.
4. **KILITLI — Job'lar arası CI kanıtı yalnız GitHub Actions artifact'ıyla taşınır**
   (`retention-days: 7`, secret içermeyen JSON özetleri ve kanıtlar). Cache, Packages, secret,
   environment ve deploy yüzeyi açılmaz.
5. Sıra: PR hazırlanır → Sinan depoyu public yapar → PR'ın CI'ı hosted runner'da koşar → iki
   bağımsız exact-head K3 review → koordinatör merge. Public yapılınca, bu PR merge edilene kadar
   self-hosted job'lar admission kontrolünde reddedilir (beklenen ve güvenli durum).

Değişmeyen insan kapıları: deployment, environment, secret, grant/rol/Critical, production
aktivasyonu ve `OPEN-*` kapatma.
