# W8 — backend kanıt boyutu düzeltmesi

## Doğrulanmış başlangıç

- Aday kaynak: `79d275bdddc4ea6b91bc3ac99034b0f1e8172d29`.
- CI: [4889 / 44633143123](https://github.com/sinangym/GoymenERP/actions/runs/44633143123).
- CI birleşik ağacı: `169cceb37e9709544e7c79c1f97ca6f7a99c5a0a`.
- SQL A/B/C ve RCSI kabulü, beş backend test grubu, frontend test/audit/build,
  OpenAPI, recovery ve release kontrolleri başarılıdır. Son backend birleştiricisi
  başarısızdır; bağımlı container işi atlanmıştır. Koşu bütünü PASS değildir.
- SQL kanıtları A: 642, B: 634, C: 508 yürütüm bildirir. Normal backend kanıtı
  19.264 başarılı yürütüm ve ayrı SQL kabulüne bağlı 1.775 SQL atlaması içerir;
  birleştiricinin toplamı 21.039'dur. Bunlar tek bir toplam PASS sayısı olarak
  birleştirilmez.
- Aynı adayın masaüstü hedefli doğrulaması tamamlandı: 95 SQL hedefi/95 yürütüm,
  sıfır hata/atlama; diğer 8.900 backend yürütümü başarılıdır. Yerel SQL major 17,
  CI major 16'dır; yerel kanıt tam CI kabulünün yerine geçmez.

## Kök neden ve dar düzeltme

`proof-modules.json` 20 projenin tam kanıtını 2.665.705 baytta taşıyor. Üretici
bu dosyayı yayımlarken boyut denetlemiyordu; tüketici 2.097.152 bayt sınırında
`Backend group proof is missing or oversized` hatası veriyordu.

Özgün dosya SHA-256:
`b740dc3d40f8b9bb3f0eca1eb96a728e3cd3fcb78b3938cfb7d5025555f93b36`.

Ortak sınır, bu kanıtı alan en küçük ikilik MiB kademesi olan 4.194.304 bayta
çıkarıldı. Üretici UTF-8 JSON baytlarını ölçer; sınır aşılırsa geçici/final kanıt
yayımlamadan reddeder. Tüketici dosya metadatasını kontrol eder, en fazla
`limit + 1` bayt okur ve gerçek okuma boyutunu da denetler. Hata grup, boyut ve
sınırı belirtir. Sınırın kendisi kabul edilir; bir bayt fazlası reddedilir.

Şema, kaynak/merge SHA, proje kapsamı, native test kimliği, keşif/koşum adedi,
SQL atlamalarının ayrı gerçek kabulü, eksik/çift JSON alanı ve hash kontrolleri
korundu. Workflow, paket, runtime uygulama, migration veya işletme politikası
değişmedi. İki mevcut mimari parmak izi ev üreticisiyle yeniden ölçüldü;
bağımsız K3 incelemesi bekliyor.

## Yerel doğrulama

| Kontrol | Sonuç |
|---|---|
| `python -I -S -B -m unittest discover -s tests/backend_test_group_verifier -p "test_*.py"` | 41 test, exit 0, skip 0 |
| `dotnet test tests/GoymenERP.ArchitectureTests/GoymenERP.ArchitectureTests.csproj -noAutoResponse --configuration Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false` | 405 test, exit 0, skip 0 |
| `python -I -S -B tools/validate_delivery_backlog.py` | exit 0 |
| `python -I -S -B -m unittest discover -s tests/delivery_backlog_validator -p "test_*.py"` | 64 test, exit 0, skip 0 |
| `python -I -S -B tools/validate_dotnet_bootstrap_integrity.py` | exit 0 |
| `python -I -S -B -m unittest discover -s tests/dotnet_bootstrap_integrity -p "test_*.py"` | 56 test, exit 0; Windows'a özgü 9 koşullu skip |
| `python -I -S -B -m unittest discover -s tests/rcsi_relational_acceptance_verifier -p "test_*.py"` | 104 test, exit 0, skip 0 |
| `python -I -S -B -m unittest discover -s tests/ci_safe_trx_summary -p "test_*.py"` | 103 test, exit 0, skip 0 |
| `git diff --check` | exit 0 |

Üç regresyon; 2 MiB üzerindeki eksiksiz üretici/tüketici akışını, yanlış kaynak ve
eksilen testin reddini, iki tarafta tam sınır/bir bayt aşımı ve metadata kontrolünden
sonra büyüyen dosyanın sınırlı okunmasını kapsar.

CI 4889'un beş backend ve üç SQL artefaktı değiştirilmeden tekrar okundu. Eski
okuyucu aynı boyut hatasıyla exit 1; düzeltilen okuyucu 21.039 yürütümü doğrulayarak
exit 0 verdi. Bu tanısal karşılaştırmadır; eski kırmızı koşuyu yeşil yapmaz ve yeni
adayın taze CI kanıtı değildir. Yeni head için CI ve iki tam bağımsız K3 review
halen gereklidir. Yazar merge yapmaz.
