# W8 Finance / Inventory ilk birim koşusu düzeltmeleri

Durum: SOURCE_DONE; birleşik kaynakta derleme ve test tekrarı bekliyor.
Taban `f62f1b080099fa156aee7c5918f72fa59bc3046d`; GOV-D9. İncelenen ilk gerçek
TRX kaynak head'i `a8f4440ec88da785ee08ff5d2fa4500e72bfb4e8`; Finance 15 ve Inventory
6 başarısız örnek. İlk başarısızlık kanıtları değiştirilmedi. Yeni dalga/dilim açılmadı.

## Gerçek backend regresyonu

Finance kasa pasifleştirme/etkinleştirme öneri ve onay wrapper'ı, mevcut zorunlu kimlik
kontrolünden önce Treasury transaction kuruyordu. Geçersiz accountId/sürüm, DB erişilemezse
ValidationFailed yerine Unavailable dönüyordu. Mevcut StartAsync yetki/komut meta verisi
önceliği korunarak IdentityRefusal transaction öncesinde de uygulanır. Transaction içindeki
fresh authority ve kimlik kontrolleri korunur. Sekiz mevcut invalid-input örneği, erişilemez
DB ve okunmaması gereken bakiye okuyucusuyla bu erken yolu doğrular; beklentileri gevşetilmedi.

## Test hizalamaları

- Kasa etkinleştirme para birimi reddinde dış giriş, transaction içi giriş ve Settings
  dönüşü olmak üzere üç mevcut authority fence'i beklenir; kaynak davranışı değiştirilmedi.
- Banka migration sırası son mevcut CollateralBankProof'u açık kimlikle pinler. Belge
  numaralama testi kendi migration'ını tekil kesin kimlikle envanterde bulur; global son
  migration olduğu varsayılmaz. Pending model ve mevcut Down/nullable/DDL kontrolleri kalır.
- Resmî fatura onay replay'i, deserialize edilen listelerin referansı yerine bütün iç içe
  wire değerini karşılaştırır. Çift yazmama ve komut sayısı kontrolleri korunur.
- Finance ödeme adayı ve Inventory ayırma bırakma testleri, şirket değişimini taklit etmek
  için yalnız testte adversarial ICompanyContext kullanır. Üretim CompanyContext.Set'in
  değiştirilemezliği gevşetilmedi; 403/Denied beklentileri aynı kaldı.
- Shell eski 19 kontrol yerine mevcut 20 kontrolü sayar; resmî fatura geri çekme kontrolünün
  üst kayıt ekranı/izin ilişkisi de açıkça doğrulanır.
- Inventory maliyet okuma sözleşmelerinin mevcut nullable CostCurrencyId alanı kesin
  property envanterine eklenir; alan veya davranış kaynakta değiştirilmedi.
- Stok hareketinin mevcut Kind 16 mal kabul ters kaydı, iki model constraint beklentisine
  yansır; constraint kaynaktan kaldırılmaz veya genişletilmez.
- Fiziksel kısa stok null testi, eklenen Exception overload'u yerine amaçlanan nonnull
  shortfall overload'unu `stockShortfall:` adıyla seçer. ArgumentNullException beklentisi kalır.

`git diff --check`: exit 0. Bu düzeltme head'inde .NET derleme/test, SQL, Node, HTTP,
tarayıcı, CI: NOT_RUN; ağır işlem hakkı entegrasyon sahibinde. Test silme/skip/karantina,
model/migration, iş formülü/izin politikası veya provider aktivasyonu yapılmadı. Tek backend
kasa dosyası değiştiğinden entegrasyon sahibi guard/recovery fingerprint'ini gerçek
kaynak üzerinden yenilemelidir. Bu yazar desteğidir, bağımsız exact-head/K3 onayı değildir.
