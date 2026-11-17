# W8 atıflı maliyet iptali merkez bağlantısı

- Owner `baf1d0add1447578d72a418c580fa9fb819fbde2`: mevcut ilk SMM önerisinin gerçek fatura payına bağlı iptal hazırlığı/onayı ve gerçek pay kadar ters kayıt; tüm toplu fiş terslenmez. Önceki kayıt ve diğer fatura payları korunur. Açık dönem/tarihi hazırlayan seçer, farklı muhasebe yetkilisi onaylar.
- Yeni teknik shell girişi `accounting.invoiced-shipment-costs.reverse-approve`, `/muhasebe/sevk-smm-ters-kayit-onay` yolunda aynı gerçek öneri ekranına bağlandı. Mevcut `AccountingJournalReversalPermissionNamesV1.Approve` kullanılır. Önerme ve onay izinleri ayrı kalır; yeni ERP izni, grant veya rol yoktur.
- Accounting provider1.10; yeni deklarasyon `ImplementationAvailable:false`. Eski Logistics sekiz staged deklarasyonu değişmedi. Ekran/route/nav/capability paritesi ve onaylayan girişin dar izni kaynak testlerinde korunur. Eski host exact-capability listesi de bütün 29 kaydı açıkça içerecek şekilde hizalandı; assertion gevşetilmedi.
- Gerçek EF `20261117223234_AddInventoryCostComponentCancellations`, iki append-only tablo, veri kayıplı Down51812 ve UPDATE51813/DELETE51814 korumaları merkezde. DB apply yoktur. Yeni ham guard yüzeyleri K3 review bekler; allowlist genişletilmedi.
- Owner API/Accounting.Unit/Integration/TSC kaynak derlemeleri exit0; ilk Integration eksik-using hatası korundu. Son merkez montajı, Settings replay düzeltmesi ve Sales gerçek faturalı miktar kaynağı birleşik derlemede ayrıca doğrulanacak.
- Altı yeni Accounting iptal ve üç Sales faturalı miktar SQL fact adı gerçek kaynaklardan sıralı envantere alındı. SQL veya başka runtime testi çalıştırılmadı.
- İptal ilk maliyet kaynağından önce gelirse tarihsel gerçek fatura kanıtını ve terminal iptal eşlemesini koruyan son dar kaynak bağı hâlâ owner çalışmasındadır. Bu checkpoint STK-SC36-a'nın tamamı veya kullanılabilir kabul değildir.

Unit/SQL/HTTP/Vitest/browser/CI yürütme ve production aktivasyonu **NOT_RUN**. Son K3/pin/hafif kontrol kapıları bekler.

## Son merkez kaynak derlemesi

`15a6342904570eb9e93cda2cdb0acac4ce10cb69` donuk kaynakta API, Settings.Unit, Finance.Unit, Inventory.Unit, Sales.Unit, Accounting.Unit, Integration ve TypeScript derlemeleri exit0/0 uyarı/hata. İlk hata kayıtları ve her native exit korundu; her süreç öncesi en az2GiB RAM ölçüldü. Kaynak incelemesindeki fresh replay çiti ve exact29 capability listesi düzeltmeleri dahildir. Sonraki tarihsel iptal delta kaynağı dahil değildir. Runtime/SQL/browser/CI NOT_RUN.
