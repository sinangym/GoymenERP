# W8 OpenAPI kapanışı

## Kaynak ve kapsam

- Başlangıç: `3e85cbbb1bbee5efc5ec4dbe9afcfa6669677034`. Bu commit'teki gerçek API üreticisinin canonical çıktısı 826 operasyon ve 1721 şemadır. `contracts/openapi/goymen-erp-v1.json` SHA-256: `432c013a9dd5b992d91536c89aca1ab8805563524dc1b9cb5238a84bbb71056a`.
- Yalnız `OpenApiContractGenerationTests.cs`, Product partial'ındaki kesin şema beklentileri ve bu rapor değiştirilir. API/endpoint/metadata, baseline JSON, permission, Shell, recovery, mimari guard ve merkezi envanter bu şeridin yazma kapsamında değildir.
- Depo dışında tek seferlik deterministik yardımcı, sabit başlangıç test kaynağı ve gerçek canonical artifact'tan C# literal beklentileri üretir. Aynı girdilerle tekrar üretimde iki test dosyasının SHA-256 değerleri değişmedi. Testler çalışma zamanında baseline dosyasından beklenti yüklemez; sabit beklenen operasyon, kod ve şema kümelerini karşılaştırır.

## Hizalama

- Önceki 611 operasyonun ardından 215 yeni operasyon vardır. 195 yeni operationId doğrudan backend kaynak literal'ıyla; kalan 20, gerçek Sales lifecycle/resmî iptal ve document template döngülerinin ad üretimiyle doğrulandı. 813 genel ve 13 Product operasyonu ayrı doğrulama yollarını korur.
- Ortak transformer'ın şirket, antiforgery, authentication ve lifecycle katkıları korunur. İşe özgü problem kodları gerçek üretilmiş endpoint metadata'sından sabit literal olarak eşlenir; yeni iş politikası veya hata anlamı türetilmez. Önceden listelenen hiçbir RequiredCompany, OptionalCompany veya Idempotent operasyon kaldırılmadı; 196 required company ve 97 idempotent operasyon eklendi.
- Genel success köklerinden erişilen 1214 şema, request köklerinden erişilen 526 şema, 9 problem şeması ve Product'ın 28 success şeması exact root descriptor, required alan kümesi ve property descriptor'larıyla sabitlenir. Request closure mevcut root kontrollerine ek olarak iç içe sözleşmeleri de kapsar. Parametre, yanıt media/status, replay ve kodlu/kodsuz/karma problem ayrımları korunur.
- Mevcut privacy/determinism/no-dependency-contact, runtime endpoint parity, authorization, şirket header'ı, idempotency, replay, antiforgery, malformed/unsupported body ve özel schema assertion metinleri korunur. Product'ın ayrı authority/problem/strict request/optional alan kontrolleri değişmez. `DescribeSchema` yalnız yeni erişilen boş JSON-value şeması ve tür belirtmeyen `anyOf`/`allOf` için exact descriptor desteği kazanır; hiçbir şema veya assertion atlanmaz.
- Native approval polymorphism ve ManufacturingStockShortfall özel kontrolleri korunur. API üretimi veya sözleşme testi somut kaynak kusuru gösterirse beklenti gevşetilmez; owner'a bildirilir.

## Doğrulama durumu

- Deterministik yardımcı tekrar üretimi: byte/hash eşitliği doğrulandı. `git diff --check`: exit0.
- Gerçek API üreticisi: `dotnet build src/backend/GoymenERP.Api/GoymenERP.Api.csproj -noAutoResponse --configuration Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=true -v:q` — ilk koşu exit0, 0 uyarı/hata.
- Canonical karşılaştırma: `python -I -S -B tools/validate_openapi_contract.py --baseline contracts/openapi/goymen-erp-v1.json --generated artifacts/openapi.json` — exit0; 9746060 byte, yukarıdaki SHA-256 ile byte düzeyinde eşit. Baseline değiştirilmedi.
- Integration kaynak derlemesi: `dotnet build tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj -noAutoResponse --configuration Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false -v:q` — ilk koşu exit0, 0 uyarı/hata.
- Dar sözleşme koşumu: `dotnet test tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj -noAutoResponse --configuration Debug --no-build --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false --filter FullyQualifiedName~OpenApiContractGenerationTests` — ilk koşu exit0; **10/10 PASS, failed0, skip0**. TRX ve ayrı native exit/log kayıtları saklandı; yeniden deneme gerekmedi. Deterministik Production/Development üretimi, sıfır dış bağımlılık bağlantısı, gizlilik, exact endpoint parity ve mevcut HTTP sınırları gerçek testlerle geçti.
- Koşulan kaynak hash'leri: ana test `07e9943bbdb236661bb86f0d28aef1f7436d2abfcab36d943ab687b19fbdf047`; Product partial `4e2164a4bc42bb99bc613d027eab12e5218ee846bfb818a02219286d804a2e78`. Koşu boyunca kaynaklar sabitti; daha sonra yalnız bu rapor tamamlandı.
- Ayrı yerel bootstrap doğrulayıcısı mevcut frontend `node_modules` reparse bağlantısı nedeniyle exit1 verdi. Bağlantı değiştirilmedi; bu kontrol PASS sayılmadı. Bu yerel çevre sonucu, yukarıdaki gerçek API/OpenAPI koşumunun yerine kullanılmaz.
- Dar OpenAPI beklenti kapanışı **DONE**. Gerçek SQL, browser, Vitest, diğer tam testler, CI ve K3 incelemesi bu sonuçla tamamlanmış sayılmaz; bu görevde çalıştırılmadı. Ağır süreç lease'i koordinatöre geri verildi.
