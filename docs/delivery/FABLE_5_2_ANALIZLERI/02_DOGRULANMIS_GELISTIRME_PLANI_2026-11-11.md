# GöymenERP — 2. Aşama: Doğrulanmış Bulgular ve İlk Üç Paket

> **Belge türü:** Fable 5.2 analizi — 2. aşama (1. aşama bulgularının yeniden doğrulanması ve ilk üç geliştirme paketi, 11 Kasım 2026).
> **İncelenen sürüm:** `main` = `9091e7d4ca553389fc152bde2cf9e4903a538545`. Satır numaraları bu sürüme aittir.
> **Önceki belge:** [01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md](01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md). Çelişkide bu belge geçerlidir.
> **Durum:** Bilgi amaçlı plandır. Karar, onay, görev ataması ya da `OPEN-*` kapatma değildir. `KARAR_SICILI.md`, tarihli `SINAN_KARARLARI_*` dosyaları ve feature spec'ler bu belgenin üstündedir. Production, migration, seed, gerçek grant, finans aktivasyonu, merge veya deployment onayı değildir. Hangi paketin uygulanacağını kullanıcı seçer.
> **Bekleyen kullanıcı teyitleri:** Paket 2 için Stok & Lojistik dalgasının açık sayılması; Paket 3 için kuralın Alış planındaki A2a-5'ten öne alınması. Paket 1 bunlardan bağımsızdır.
> **Kayıt:** Plan sohbette teslim edildi; aynı gün kullanıcının isteğiyle gövdesi değiştirilmeden depoya işlendi.

İlk rapordaki önemli bulguları `main` `9091e7d` üzerinde kaynak kod ve kararlarla yeniden karşılaştırdım. Sonuç: ilk üç paket sırasıyla **hazırlık denetimi kapsamı**, **Lojistik'in sevk edeni bildirmesi** ve **alış siparişinde hazırlayan ≠ onaylayan**. Canlıya geçişin asıl engelleri (rol atama, giriş, kurulum yolu, fatura kesinleştirme) hâlâ sizin kararınızı bekliyor; bu yüzden paket olarak değil, soru olarak duruyor.

Bu aşamada hiçbir dosya değişmedi; build, test, uygulama, commit ve GitHub yazımı yapılmadı.

## 1. İlk raporun yeniden doğrulanması

| Bulgu | Yeniden kontrolün sonucu | Durum |
|---|---|---|
| F-022 Hazırlık denetimi kapsamı | Denetim 14 veritabanı bağlamını sayıyor; Ayarlar, Belgeler ve Fiyat Listesi bağlamları listede yok. Work modülü host'a hiç bağlı değil, dördüncü eksik değil. | Doğrulandı, kapsam düzeltildi |
| F-006 Sevk eden bilgisi | Lojistik `ShippedBy` alanını hiç doldurmuyor; pay kaydında aktör sütunu da yok. İlk raporda migration ihtiyacı yazmıyordu. | Doğrulandı, efor düzeltildi |
| F-015 Alış sipariş onayı | ALS-D3 "hazırlayan ≠ onaylayan" diyor; `PurchaseOrder.Approve` aktör almıyor. Modül README'si de "Still absent" diye yazıyor. İlk raporda "olası boşluk"tu. | Doğrudan doğrulandı |
| F-008 Üretim çıktısı soy ağacı | Çıktı için soy ağacı kaydı yazılmıyor. Mevcut model yalnız Böl/Birleştir (`[Kind] IN (1, 2)`); migration ve model seçimi gerekir. | Doğrulandı, efor büyütüldü |
| F-011 Ortalama maliyet | Hesaplayıcı, defter tablosu ve okuma bağlantısı var. Deftere yazan kod, tüketici ve Alış fiyat okuma bağlantısı yok. | Düzeltildi |
| F-005 Fatura kesinleştirme | Resmî fatura sağlayıcısı bağlı değil; uç 503 dönüyor. Veri sahibi kararı yok. | Doğrulandı, karar bekliyor |
| F-001 Rol atama | İzin adları ve tablolar var, yazma servisi yok. "Mevcut kararlarla yapılır" ifadem yanlıştı: kimin atayacağı kararsız. | Düzeltildi, karar bekliyor |
| F-021 Outbox | Dağıtıcı çekirdeği merge edilmiş (#999) ama Work modülü host'ta kayıtlı değil; kaynak ve tüketici yok. | Düzeltildi |
| F-028 Muhasebe bağlantısı | Kabul bağlantısı merge edilmiş (#1003); yalnız host adaptörü eksik ve F-005'e bağlı. | Düzeltildi |
| F-019 Müşteri iade faturası | Taslak PR #1059'da sürüyor. | Plandan çıkarıldı |
| F-009 Üretim iptal/düzeltme | Plan ret/iptal var (#1023, #1030); üretim emri tarafı yeniden okunmadı. | Kısmen doğrulandı |

**Kanıtı zayıf kalanlar (plana alınmadı):** F-007 ve F-010 (Üretim kapıları, kapasite/takvim), F-013 (depo transferi kaydının çağıranı), F-017 (Alış tedarik referansının tüketicisi), F-020 (araç tartımı), F-014 ve F-023…F-027. Bunlar bu aşamada yeniden okunmadı.

## 2. Gerçek öncelikler ve geliştirme sırası

**Karar bekleyenler (iş değeri en yüksek, geliştirme başlayamıyor):**

1. **Rol atama (F-001):** Atamayı ekrandan operatör mü yapacak, yoksa ekran yalnız talep üretip atamayı siz mi yapacaksınız? Plan §2.2 ile kurulum rehberi burada çelişiyor. İlk Admin'in nasıl oluşacağı da açık.
2. **Giriş (OPEN-013):** OIDC sağlayıcısı seçilmeden ön yüz girişi yazılamaz.
3. **Fatura kesinleştirme (F-005):** Resmî fatura verisinin sahibi kim olacak?
4. **Ürün çalışma zamanı (B-06):** Açılış koşulu sizde.

**Geliştirme sırası (kararı tam olanlar önce):**

| Sıra | Paket | Neden burada | Büyüklük | Ön koşul |
|---|---|---|---|---|
| 1 | Hazırlık denetimi kapsamı (F-022) | Eksik migration'lı veritabanı "hazır" görünüyor; yalnız host'a dokunur | Küçük | Yok |
| 2 | Lojistik sevk edeni bildirir (F-006) | Fazla teslim onayı ve eksikle satır kapatma bugün hiç çalışamıyor | Orta, K3 | Lojistik dalgası teyidi |
| 3 | Alış siparişi: hazırlayan ≠ onaylayan (F-015) | Onay yetkisi dağıtılmadan önce kapanması gereken kontrol | Orta, K3 | Alış planı notu |
| 4 | Üretim çıktısı soy ağacı (F-008, plan K0-4) | İzlenebilirlik; önce model seçimi gerekir | Orta–büyük, K3 | Tasarım kararı |
| 5 | Maliyet zinciri (F-011) | Alış fiyat bağlantısı, defter yazıcısı, Satış marjı | Büyük, K3 | Birim çevrimi ve para birimi kararları |
| 6 | Work modülünün host'a bağlanması (F-021) | Olay dağıtımının ön koşulu | Orta | Yok |
| 7 | Muhasebe host adaptörü (F-028) | Fiş üretiminin son halkası | Orta | F-005 kararı |

## 3. Paket 1 — Hazırlık denetimi kapsamı

**Amaç:** `/health/ready` ve `ValidateOnly` kapısı, host'ta kayıtlı ve migration taşıyan her bağlamı denetlesin.

**Dosyalar ve değişiklikler:**

| Dosya | Değişiklik |
|---|---|
| `src/backend/GoymenERP.Api/Startup/DatabaseSchemaCompatibilityHealthCheck.cs` | `Contexts` dizisine `SettingsDbContext`, `DocumentsDbContext`, `ProductPriceListDbContext` eklenir. Atlama koşulu, Ürün çalışma zamanı kapalıyken `ProductPriceListDbContext`'i de atlar. |
| `tests/GoymenERP.Api.IntegrationTests/SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests.cs` | Mevcut "geride/ileride migration geçmişi" testi (satır 100) `[settings]` ve `[documents]` geçmiş tabloları için genellenir. |
| `tools/relational_inventory/platform.txt` | Yeni test adları eklenir. |

Üç bağlam da `public` ve host bu projelere zaten referans veriyor; yeni proje referansı gerekmez.

**Korunacak kurallar:**
- Her hata "hazır değil" sonucuna gider; yanıt bağlam adı ya da sır sızdırmaz.
- `ValidateOnly` hiçbir şeyi onarmaz ya da yazmaz.
- Ürün çalışma zamanı kapalıyken Ürün bağlamları atlanır (B-06 kapısı değişmez).
- Work bağlamı eklenmez; modül host'a bağlandığında ayrı iş olur.

**Testler:**
- Ayarlar geçmişinden son satır silinince `/health/ready` 503 döner ve satır geri gelmez.
- Geleceğe ait satır eklenince 503 döner.
- Aynı iki durum Belgeler için sınanır.
- Uyumlu veritabanı testi (satır 17) yeşil kalır.
- Fiyat listesi geçmişi, SQL test host'u Ürün çalışma zamanını açıyorsa aynı şekilde sınanır. Bunu doğrulayamadım; açmıyorsa atlama koşulu birim düzeyinde sınanır.

**Kabul kriterleri:**
- Üç bağlamdan birinin geçmişi kaynakla uyuşmadığında hazırlık 503 verir.
- `DatabaseStartupModeTests` ve `DatabaseReadinessGateMiddlewareTests` değişmeden geçer.
- Migration, uç ve izin değişikliği yoktur.

**Bağımlılık ve risk:** Bağımlılık yok. `ValidateOnly` kipinde denetim her iş isteğinde çalıştığı için üç ek bağlantı ve geçmiş okuması gelir; süre ölçülmeli.

## 4. Paket 2 — Lojistik sevk edeni bildirir

**Amaç:** Lojistik her sevk kaydında sevki yapanı saklasın ve Satış'a bildirsin. Böylece SAT-D216 kontrolü, SAT-D222'nin dediği gibi Satış kodu değişmeden devreye girer.

**Dosyalar** (kök: `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/`):

| Dosya | Değişiklik |
|---|---|
| `LogisticsRows.cs` | `LoadingShareRow`'a boş bırakılabilir `RecordedBy` (en çok 128 karakter). |
| `LogisticsDbContext.cs` | `ConfigureShares` içinde uzunluk ve "doluysa boş metin olamaz" kısıtı. |
| `Migrations/` (yeni dosya) ve `LogisticsDbContextModelSnapshot.cs` | Sütunu ekleyen migration. `Down`, aktör taşıyan pay varken `THROW` ile reddeder. |
| `LogisticsStore.cs` | Tek pay ekleme noktasında (satır 386–400) `RecordedBy = actorId`; aktör 278. satırda zaten elde. |
| `LogisticsSalesOrderDeliveryReadPort.cs` | Sipariş satırı ve birim başına, sevk türündeki payların aktörleri ayrık ve sıralı döner. Sayılan sevk paylarından biri aktörsüzse `ShippedBy` boş kalır. Tekli ve toplu okuma aynı kuralı uygular. |

Değişmeyenler: `ISalesOrderDeliveryReadPortV1` (alan zaten var) ve Satış kodunun tamamı.

**Korunacak kurallar:**
- SAT-D216: sevki yapan kendi işlemini onaylayamaz.
- SAT-D222 ve plan B-33: boş, eksik ya da okunamayan değer onayı reddeder. Geçmiş paylar için geriye dönük doldurma yapılmaz.
- SAT-D209: yeni bağlantı, tablo ya da ekran açılmaz. Mevcut tabloya sütun eklemeyi bu sınırın içinde yorumluyorum; itirazınız varsa paket durur.
- `sales.orders.over-delivery.approve` hiçbir role verilmez.
- Şirket filtresi yeni sorguda da geçerlidir.

**Testler:**
- Birim (`tests/GoymenERP.Logistics.UnitTests/LogisticsSalesOrderDeliveryReadPortTests.cs`): sevk edeni adlandırır; aktörsüz eski pay boş döner; iki sevk eden ikisini de döner; başka şirketin payı görünmez.
- Model (`LogisticsPersistenceModelTests.cs`): sütun ve kısıt.
- Gerçek SQL (`tests/GoymenERP.Api.IntegrationTests/SalesOrderOverDeliveryApprovalSqlServerRelationalAcceptanceTests.cs`): 146. satırdaki test bugünkü davranışı sabitliyor ve bilinçli olarak değiştirilir. Yeni hali üç durumu sınar: sevk eden onaylarsa ret; başka yetkili onaylarsa kabul; aktörsüz eski pay varsa `sales_over_delivery_shipper_unverified`.
- Tekrar gönderimde `Idempotency-Replayed`, yetkisiz kullanıcıda 403 ve migration `Down` reddi.

**Kabul kriterleri:**
- Gerçek Lojistik deposuyla sevk edilen satırda fazla teslim onayı başka bir yetkiliyle tamamlanır.
- Sevk edenin kendi onayı reddedilir.
- Sahte bağlantıyla çalışan mevcut Satış testleri değişmeden geçer.

**Bağımlılık ve onay:** K3 (migration, modüller arası sözleşme davranışı, onay kuralı); iki bağımsız inceleme gerekir. B-33 "Stok & Lojistik dalgası başlayınca" diyor; dalganın açık sayıldığını sizin teyit etmeniz gerekir. `THROW` numarası uygulama anında seçilir: `main`'de en yüksek 51254, #1059 51257 kullanıyor.

## 5. Paket 3 — Alış siparişinde hazırlayan ≠ onaylayan

**Amaç:** ALS-D3'ün kural yarısını uygulamak. Siparişi oluşturan kişi, onay yetkisi olsa bile kendi siparişini onaylayamaz. Talep tarafında aynı kural (ALS-D2) alan adı ve veritabanı kısıtıyla zaten var; sipariş onu örnek alır.

**Dosyalar** (kök: `src/backend/Modules/Purchasing/`):

| Dosya | Değişiklik |
|---|---|
| `GoymenERP.Modules.Purchasing.Domain/Orders/PurchaseOrder.cs` | Hazırlayan ve onay bilgisi eklenir. `Approve` (satır 186) onaylayanı alır; hazırlayanla aynıysa ya da hazırlayan bilinmiyorsa reddeder. Karşılaştırma talepteki gibi `Ordinal`. |
| `GoymenERP.Modules.Purchasing.Domain/Identity.cs` | İki yeni kural kodu; talepteki `RequestSameActor` ve `RequestMakerUnknown` karşılıkları. |
| `GoymenERP.Modules.Purchasing.Application/PurchasingService.cs` | `ApproveOrderAsync` (346–380) aktörü geçirir. Ret, `PurchasingService.RequestDecisions.cs` 91–105'teki gibi kalıcı 409 olarak kaydedilir. |
| `GoymenERP.Modules.Purchasing.Application/Contracts/PurchasingContractsV1.cs` | İki yeni problem kodu. |
| `…Infrastructure/Persistence/PurchasingRows.cs`, `PurchasingDbContext.cs` | Sipariş satırına `ApprovedBy` ve `ApprovedAtUtc`. `RequestMakerCheckerCheck` (satır 52–60) örneğiyle ikili harmanlamalı kısıt. |
| `…Infrastructure/Persistence/PurchasingStore.cs` | Oluşturmada hazırlayan (satır 303), onayda onaylayan yazılır. `RestoreOrder` (726; onay 759) onaylayanı kayıtlı olmayan eski siparişi reddetmeden yükler. |
| `…Infrastructure/Persistence/Migrations/` (yeni dosya) | İki sütun ve kısıt; `Down`, onaylayan kaydı varken `THROW` ile reddeder. |
| `src/backend/GoymenERP.Api/Endpoints/PurchasingEndpoints.cs` | Onay ucunun (satır 333) çakışma kodlarına iki yeni kod; OpenAPI yeniden üretilir. |
| `src/frontend/goymen-erp-web/src/purchasing/purchasingText.ts` | İki kodun Türkçe metni. |

İlk raporda sipariş satırında `ApprovedBy` olduğunu varsaymıştım; o alan talep satırında. Siparişte yalnız `CreatedBy` var, bu yüzden migration gerekiyor.

**Korunacak kurallar:**
- ALS-D3: tek onay; tutar eşiği kapalı kalır. Eşik (API-96) bu pakete girmez, Alış planındaki A2a-5'te kalır.
- ALS-D53: izin dağıtımı yapılmaz.
- Sipariş yanıtının biçimi değişmez; onaylayanı ekranda göstermek ayrı karardır.
- Geçmiş onaylar için onaylayan boş kalır; başka modülün denetim kaydından doldurulmaz.
- Sürüm kontrolü, tekrar gönderim, denetim kaydı ve `RequireSharesWithinNeedAsync` aynen kalır.

**Testler:**
- Birim (yeni `PurchaseOrderApprovalTests.cs` önerisi): oluşturan reddedilir, başkası onaylar, hazırlayan bilinmiyorsa ret.
- Gerçek SQL (yeni `PurchasingSqlServerRelationalAcceptanceTests.OrderApproval.cs` önerisi):
  - Oluşturan 409 alır ve tekrar gönderim aynı sonucu döner.
  - Yalnız harf büyüklüğü farklı aktörde alan adı ve veritabanı aynı kararı verir.
  - Eski onaylı sipariş okunur ve iptal edilebilir.
  - Yetkisiz 403, sürüm çakışması 409, başka şirket görünmez.
  - `Down` reddeder.
- Ön yüz: yeni kodların metni ve çakışma durumu.

**Kabul kriterleri:**
- Siparişi oluşturan hiçbir yoldan kendi siparişini onaylayamaz.
- Mevcut onaylı siparişler çalışmaya devam eder.
- Hiçbir test kuralı atlayarak yeşile dönmez.

**Bağımlılık ve risk:** K3. Asıl iş yükü test tarafında: bugün birçok test siparişi oluşturan kullanıcıyla onaylıyor, hepsine ikinci bir onaylayan gerekir. Alış planı bu kuralı eşikle birlikte A2a-5'e koymuş; kuralın öne alınması plana not düşülmesini ve Alış arka uç şeridiyle çakışmamayı gerektirir.

## 6. Üç pakette ortak yükümlülükler

- **Yeniden sabitleme:** Depodaki `recovery-inventory-repin`, `architecture-guard-repin`, `ef-migration-forward-fix`, `test-naming-and-groups` ve Paket 3'te `openapi-regenerate` adımları uygulanır; sabit değerler elle yazılmaz.
- **Doğrulama komutları (öneri, çalıştırılmadı):** `dotnet build GoymenERP.slnx`, ardından ilgili test projesinde `dotnet test --filter "FullyQualifiedName~<sınıf>"`. Gerçek SQL testleri yerel bir test veritabanı ister; mevcut SQL ortamınıza dokunmadan önce size sorulmalı.
- **Kapsam dışı:** İzin dağıtımı, `OPEN-*` kapatma, production, deployment ve Finance STOP.

**Opus çalışma kuralı:**

> "Uygulamaya başlamadan önce mevcut HEAD ve çalışma ağacını raporun başlangıç sürümüyle karşılaştır. İlgili kod değişmişse yalnız etkilenen bulguları yeniden doğrula. Fable raporunu tartışılmaz gerçek kabul etme. Yalnız kullanıcı tarafından uygulama için seçilen paketi gerçekleştir; diğer paketlere kendiliğinden geçme. Bu rapor production, migration, seed, gerçek grant, finans aktivasyonu, merge veya deployment onayı değildir."

## 7. Sınırlar

- Hiçbir test çalıştırılmadı; "yeşil kalır" ifadeleri kod okumasına dayanır.
- Satır numaraları `9091e7d` içindir.
- Paket 3'teki yeni kod adları ve test dosyası adları öneridir.
- 7 Kasım sonrası bazı karar ekleri tam okunmadı; ALS-D3'ü değiştiren bir karar bulmadım ama bu, yok demek değildir.

Sizden iki teyit gerekiyor: Paket 2 için Stok & Lojistik dalgasının açık sayılması, Paket 3 için kuralın A2a-5'ten öne alınması. Paket 1 bunlardan bağımsız, hemen devredilebilir.