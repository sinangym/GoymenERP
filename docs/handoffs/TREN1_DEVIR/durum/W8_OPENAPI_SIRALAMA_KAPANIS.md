# W8 — OpenAPI şema sıralaması kapanışı

Taban: `94e0e88e98ca3374cb3149d943a9d4e9db5d6bd7`.
Kapsam: mevcut W8 / GOV-D9 / GOV-D10 sözleşme kapanışı; yeni dilim veya iş kuralı yok.

## Neden ve düzeltme

Windows üreticisinin `432c013a…` ve Linux CI üreticisinin `330d392b…` dosyaları
9.746.060 bayttı. Fark yalnız `components.schemas` anahtar sırasıydı. Eski dosyanın
şema anahtarlarını CI sırasına taşımak, JSON değerlerini değiştirmeden tam Linux
SHA-256 değerini verdi.

[ASP.NET Core 10.0.8 üreticisi](https://github.com/dotnet/aspnetcore/blob/v10.0.8/src/OpenApi/src/Services/OpenApiDocumentService.cs#L81-L87)
belge dönüştürücülerinden sonra şemaları karşılaştırıcı belirtilmeyen `OrderBy`
ile sıralar. Bu yüzden yalnız belge dönüştürücüsünde ordinal sözlük kurmak yeterli
değildir. [Microsoft.OpenApi 2.7.5](https://github.com/microsoft/OpenAPI.NET/blob/v2.7.5/src/Microsoft.OpenApi/Models/OpenApiComponents.cs#L78-L149)
kopya kurucusu ve sanal `SerializeAsV31` / `SerializeAsV3` sınırı kullanıldı.

`ErpCanonicalOpenApiComponents` serileştirme sırasında kopyanın şema sözlüğünü
`StringComparer.Ordinal` ile sıralar. Gerçek belge dönüştürücüsü bu sınıfı kullanır.
Kaynak belge ve uygulamanın kültürü değiştirilmez. Paket, SDK, iş akışı, doğrulayıcı,
endpoint, JSON alanı, problem kodu, güvenlik ve iş kuralı değiştirilmedi.

Yeni test, gerçek MSBuild çıktısının ordinal sırasını da ölçer; ardından OpenAPI
3.0 / 3.1 için `tr-TR`, `en-US` ve invariant kültürlerinde aynı baytları, eşit JSON
değerlerini, güvenlik/extension korunmasını ve kaynak nesnenin değişmediğini doğrular.
Mevcut tam sözleşme beklentileri ve Product özel kontrolleri korunmuştur.

## Gerçek üretici ve kanıt

Release API üreticisinin çıktısı doğrudan kanonik dosyaya kopyalandı; elle JSON
düzenlenmedi ve globalization ortam değişkeni kullanılmadı.

- Yeni SHA-256: `091cc64a9e71f90ddc12b7c2ab9784b3f22fd8e181234f7f7b50603dc30dec04`.
- Eski SHA-256: `432c013a9dd5b992d91536c89aca1ab8805563524dc1b9cb5238a84bbb71056a`.
- Boyut değişmedi: **9.746.060 bayt**; **826 operasyon / 1.721 şema**.
- Eski/yeni ayrıştırılmış JSON değerleri tamamen eşit; yeni şema sırası ordinal.
- Bootstrap doğrulayıcı, mevcut paket restore, Release API ve Integration derlemeleri:
  **exit 0**; iki derlemede **0 uyarı / 0 hata**.
- OpenAPI sözleşme testleri: **11 PASS / 0 FAIL / 0 SKIP**.
- Koordinatörün aynı filtreye eklettiği ayrı muhasebe teşhis testi: **1 FAIL**.
  Birleşik koşunun gerçek çıkışı **1**, toplamı **12 test**; bu koşu topluca PASS değildir.
  `The_actual_purchase_lineage_adapter_keeps_each_receipt_and_exact_candidate_under_the_original_envelope`
  testi `AccountingStoreException` beklerken, fixture içindeki ikinci
  `CompanyContext.Set` çağrısı `InvalidOperationException` üretir: “Bir istek içinde
  şirket bağlamı değiştirilemez.” Fixture koordinatör sahipliğinde; bu commit değiştirmez.
- Testlerden sonra bayt doğrulayıcı ve `git diff --check`: **exit 0**.

Temel komutlar (her ağır süreç seri, tek işlemci ve paylaşılan derleyici kapalı):

```text
python -I -S -B tools/validate_dotnet_bootstrap_integrity.py
dotnet restore tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj -noAutoResponse --configfile NuGet.Config --disable-parallel -m:1 -nr:false -p:UseSharedCompilation=false
dotnet build src/backend/GoymenERP.Api/GoymenERP.Api.csproj -noAutoResponse --configuration Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v:minimal
dotnet build tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj -noAutoResponse --configuration Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false -v:minimal
dotnet test tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj -noAutoResponse --configuration Release --no-build --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false --filter "FullyQualifiedName~OpenApiContractGenerationTests|FullyQualifiedName~AccountingPurchaseInvoiceCostSourceTests.The_actual_purchase_lineage_adapter_keeps_each_receipt_and_exact_candidate_under_the_original_envelope" --logger "trx;LogFileName=openapi-and-lineage.trx"
python -I -S -B tools/validate_openapi_contract.py --baseline contracts/openapi/goymen-erp-v1.json --generated artifacts/openapi.json
git diff --check
```

Native loglar, ilk başarısız sonuç ve TRX ayrı kanıt dizininde korunur. Bu yerel
hedef doğrulaması Linux CI / tam CI / gerçek SQL / bağımsız K3 incelemesi veya
production aktivasyonu yerine geçmez. Recovery ve mimari pin yenilemesi koordinatördedir.
