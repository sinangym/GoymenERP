# R02.1-B — ek kaynak-only release manifest v2

V2; frontend kaynak klasörlerini, committed OpenAPI kimliğini ve DbContext/migration
başlarını sürüm zarfına ekler. Bunlar **kaynak kimlikleridir**: çalışan ekran,
capability/grant, uygulanmış migration, uyumlu ortam, build, restore, UAT veya
production kanıtı değildir. Sonuç `UNVERIFIED`, `deployable=false` kalır.

## V1 ve teslim sınırı

Yeni üretici `tools/generate_release_manifest_v2.py` dosyasıdır. Kabul edilmiş v1
üreticisi, v1 şeması ve testleri değiştirilmez; mevcut CI `release-manifest` işi
hâlâ v1 üretir/doğrular. V1 CI sonucu bu yeni v2 üreticisi veya testlerinin koştuğunu
kanıtlamaz. `.github`, OpenAPI, migration veya ortak yardımcı değişikliği yoktur.
R05.1 `ffb15ead` kaynağı v1-only kalır; v2 tüketimine bu dilimle açılmaz.

Taban: `main@b6ed7ca5496f8f34314a0a4b2d3f13467bb5ffe7`.
Rapor girdisi: paket v2 `CILT_01.md:2394-3423`, bölüm SHA256
`b5e577adcb7786ac2aac4e83d2cec0926e7f9c3db577336af0019b4b4cb00d75`.
Rapordaki mevcut v1 dosyalarını dönüştüren hunklar uygulanmadı; mevcut koordinatörün
additive NEW4 claim'i ile ayrı v2 şema/üretici/test/belge yazıldı.
[DEC-TEC-008](../decisions/KARAR_SICILI.md) ortam ayrımını korur; bu kaynak zarfı
R02.2 ortam kurulumu veya D-ENVIRONMENT/IdP/veri sahibi aktivasyonu değildir.

## Exact-source bağı

V2, sabit SHA256 ile doğruladığı kabul edilmiş v1 Python kaynağının mevcut snapshot,
şema, component/config-key/feature ve recovery proof fonksiyonlarını kullanır.
V1 globals veya guard'ları patch edilmez. SHA256'lar Git blob baytlarınadır;
değişen/eksik bağımlılıkta otomatik fallback veya paket kurulumu yapılmaz.

V1 snapshot'ının girdi listesi ek v2 dosyalarını ve OpenAPI'yi içermez. Bu yüzden v2:

1. Aynı exact commit/tree'den kendi şema, üretici, test, belge ve OpenAPI blob'larını
   ayrıca alır; düzenli Git blob'u, canonical/case-collision olmayan yol ve tam
   kapsama şartı uygular.
2. Executed v2 üreticisinin ve çalışma alanı girdilerinin committed baytlarla aynı
   olduğunu doğrular. V1'in temiz checkout, doğru origin, current HEAD,
   no-replace ve gizli index bayrağı reddini korur; çıktı öncesinde yeniden kontrol eder.
3. `v2SourceInputs` içinde dört yeni dosya ve beş v1 dependency/test kaydının
   path/Git blob/SHA256 kimliğini taşır. OpenAPI ayrıca `apiContract` içindedir.

Bu mekanik doğrulama, exact commit'in insan/CI/K3 tarafından **kabul edildiğini**
kanıtlamaz. Attestation, build, SQL, browser ve admission makbuzları ayrı kalır.
Kaynak dışında environment değeri, secret, gerçek şirket veya müşteri verisi okunmaz.

## Üç ek kimlik

| Alan | Anlamı ve sınırı |
|---|---|
| `frontendFeatures` | Web `src/` altındaki kaynak klasörlerinin isim/tür/dosya sayısı/parmak izi. `assets` ve `test` hariç; kök App/main dosyaları web component hash'inde kalır. MODULE/SHELL/DESIGN_SYSTEM yalnız kaynak sınıflamasıdır, route veya kullanıcı capability'si değildir. |
| `apiContract` | Tam committed OpenAPI blob'u, OpenAPI/belge sürümü ve `paths` içindeki tekil operationId'lerin sayısı/özeti. Özet, sıralı kimliklerin araya LF konarak, son LF eklenmeden SHA256'sıdır. Tam blob hash'i belgenin tamamını bağlar. Bu, backend startup veya OpenAPI üretimi değildir. |
| `schemaHeads` | Her inventoried DbContext/migration dizini için dosya sayısı ve en son canonical migration dosya adı. Her migration tam bir kez kapsanır. Bu liste gerçek DB history'sini, hedef migration emrini, uygulanmış şemayı veya uyumluluğu göstermez. |

Migration/Designer bağında mevcut C# lexer önce yorumları ayıklar, sonra literal ve
statik ölü kodu maskeler. `[DbContext]` gerçek migration sınıfının header'ından
okunur; yorum/string içindeki metin veya başka sınıftaki öznitelik bağ oluşturmaz.
Ana dosya/Designer arasında eksik, çift, çelişen veya çözülemeyen bağ reddedilir.
Bare canonical context adı ya da doğrulanan context namespace'ine tam nitelikli
başvuru desteklenir. Yeni alias/öznitelik biçimi sessizce tahmin edilmez.

OpenAPI strict JSON olarak ayrıştırılır; yinelenen anahtar, NaN/Infinity, eksik/çift
operationId, desteklenmeyen path üyesi ve unresolved `$ref` reddedilir. Kaynak JSON
kimliği çıkarılırken gerçek sayısal örnekler hesaplanmaz veya yuvarlanmaz. Sabit
rapor sayımları (ör. 236 operasyon, 18 context, 96 migration) v2 algoritmasına
kopyalanmaz; gerçek kaynak/accepted inventory esas alınır.

## Yalnız ileride yetkili slotta kullanım

Bu teslim kaynak-öncedir. Aşağıdaki komutlar bu oturumda çalıştırılmadı ve bir
test/CI/ortam slotu açmaz. Exact commit, bu v2 NEW4 kaynaklarını içeren temiz HEAD
olmalıdır; eski b6ed7ca5 tek başına bu yeni üretici için gerekli dosyaları içermez.

```text
python -I -S -B tools/generate_release_manifest_v2.py build --repository-root <checkout> --source-commit <exact-HEAD>
python -I -S -B tools/generate_release_manifest_v2.py validate --repository-root <checkout> --source-commit <exact-HEAD> --manifest <manifest-v2.json>
python -I -S -B -m unittest discover -s tests/release_manifest_v2 -p test_*.py -v
```

Üretici kanonik UTF-8 JSON'u stdout'a verir; `validate` manifesti aynı exact source
üzerinden yeniden üretip karşılaştırır. Başarılı özet de `UNVERIFIED` ve
`deployable=false` taşır. Exit 2 input/source/contract reddidir; teşhisler payload veya
kullanıcı yolunu yazdırmaz. Argüman/manifest input'ları için key/depth/size bütçeleri
vardır; bunlar ayrıştırma kaynak sınırlarıdır, ERP veri/saklama politikası değildir.

Yetkili koşum, v1 proof/recovery inventory üretimini de çalıştırır ve maliyetli
olabilir. Mevcut tek ağır-iş/test slotu korunur. Test kaynağındaki gerçek committed
source vakaları da bu nedenle çalışma kanıtı sayılmaz; kaynak kodunun yazılması
koştuğu anlamına gelmez.

## Bu teslimin kanıt durumu

Yalnız statik Python AST, JSON/schema yapısı, kaynak pinleri ve exact4 diff
kontrolleri yapılır. Unit/CLI/generator/build/restore/SQL/browser/scanner/CI
**NOT_RUN**. İki bağımsız exact-head K3 incelemesi, insan ve fresh zorunlu CI kapıları
açıktır. Push/PR/merge, source admission, R02.2 ortamı, production veya 18/128 artışı
bu kaynak commit'iyle verilmez. Finance STOP, AI A-contract-only,
permission/grant/Critical ve #759 sahiplik sınırları korunur.
