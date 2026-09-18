# ADR-INV-001 — Malzeme etiketini hangi şirket kapsar?

- Durum: **Öneri** (dar ortak öneri; yazar ÜRÜN şeridi, merkezi/tenant görüşü CARİ'den bekleniyor)
- Tarih: 2026-09-18
- Karar sahibi: Önerilen seçenek (A) ADR-002 içinde kalıyor; bu yüzden Inventory sahibi ile merkezi tenant sahibinin teknik kararı. Seçenek B veya C seçilirse karar insan kapısıdır (NEEDS-SINAN; açılışı koordinatörde).
- İlgili kaynaklar: ADR-002; ADR-006; ADR-009; ADR-011; plan v2 (#275 `a5b8143b`) §5, §7.1, §10, T07/T13; Sinan Ö1/Ö3/Ö6 (#66 5722637822); Cari emsali `docs/features/CARI_CORE_DOMAIN_MODEL_PROPOSAL_v1.md` (§2.1, §3); INV-1 (#281, exact `69957289`) README `dependency_wait` madde 1.

Bu belge kod, DbContext, migration, izin ya da grant getirmez. INV-2 bu karar main'e girmeden kalıcılık yazmaz.

## Bağlam

INV-1 (#281) etiketi fiziksel kimlik olarak modelliyor. Bir stok pozisyonu dört ayrı olgu taşıyor (plan v2 §5): **işlem şirketi** (`CompanyId`), **sahip** (şirket veya karşı taraf), **gözeten taraf** (şirket veya karşı taraf) ve **fiziksel konum**. Ledger iki kuralı koruyor: bir etiketin sıfır olmayan bakiyeleri tek bir (şirket, sahip, gözeten, konum, ürün) yerinde durur, ve bir etiket aynı anda iki şirkette stok taşıyamaz (`inventory_label_in_two_positions`). `MaterialLabel`'ın kendisi INV-1'de `CompanyId` taşımıyor. INV-2'nin DbContext'i ADR-002'nin global filtresini kurmadan önce etiketin kapsamı belirlenmeli.

Sabit girdiler (bu belge bunları seçmiyor):
- **ADR-002:** her tenant satırında zorunlu `CompanyId`, EF global filtre, eklenen/değiştirilen kayıtta `CompanyContext` eşleşmesi, `(CompanyId, Code)` unique ve bileşik FK. Başka şirketin kimliğine erişim genel 404 alır. Grup raporları filtreyi atlamaz; ayrı ve yetkili bir çapraz-şirket okuma modeli gerekir.
- **Organization:** Site, Location ve WorkCenter şirket kapsamlı (`CompanyId` + filtre).
- **Product master grup seviyesinde:** `product.Products`'ta `CompanyId` yok. Etiketin ürün referansı şirketler arasında aynı kalır.
- **Cari emsali:** grup seviyesinde `Party` (filtresiz, doğrudan koleksiyon olarak açılmıyor) + şirket başına `CompanyPartyAccount` (filtreli). Görünürlük bağ üzerinden çözülür. Ö6: grup yetkisi her şirketin bakiyesini görme hakkı değildir.
- **Plan v2 §5:** grup görünümü hukuki şirketleri birleştirmez; aynı grupta olmak her şirketin malını kullanma yetkisi değildir.
- **Plan v2 §10:** ayrı şirketler arasında tek yerel transaction varmış gibi davranılmaz. **Şirketler arası mal/para politikasını teknik ekip seçmez.**
- **Plan v2 §7.1:** bölünme ve birleşmede yeni fiziksel kimlik oluşur; yeniden baskıda kimlik aynı kalır. Heat/coil/MTC ve çoklu kaynak ilişkisi korunur. Farklı sahiplik izsiz birleşmez.
- **Ö3:** zorunlu belge eksikse fiziksel geliş kaydedilir, belge/kalite "bekliyor" olur ve üretim/sevk serbestliği açılmaz.

## Seçenekler

Her seçenek aynı dört başlıkla karşılaştırılıyor: CompanyId/filtre, şirketler arası transferin etikete etkisi, sorgu/görünürlük ve INV-2 DbContext etkisi.

### A — Etiket kaydı, stoğu tutan işlem şirketinin tenant satırıdır (ADR-002 içinde)

- **CompanyId / filtre:** `MaterialLabel.CompanyId` zorunlu ve kayıt boyunca değişmez. Bu, etiketin bakiyelerinin durduğu işlem şirketidir. Hareket, bakiye, belge eki ve baskı audit'i de aynı `CompanyId` ile tenant satırıdır; hepsi global filtre altında. Pozisyonun şirketi etiketin şirketiyle aynı olmak zorunda (INV-1'in "tek fiziksel yer" kuralının kalıcı karşılığı). Sahip ve gözeten o şirketin kaydının içinde ayrı olgular olarak kalır. Örneğin GDC'de tutulan müşteri malı: `CompanyId = GDC`, sahip = müşteri (karşı taraf), gözeten = GDC.
- **Transfer (T13):** A şirketinden B şirketine giden bobin, A'nın kaydında çıkışla kapanır (kimlik ve geçmiş korunur). B'nin kabulü **B'de yeni bir etiket kaydı** oluşturur. Bu kayıt kaynağa yalnız opak bir köken referansı olarak bağlanır: (kaynak şirket, kaynak etiket kimliği, kaynak etiket kodu). B bu referansla A'nın satırını okuyamaz. İki şirket arasında tek transaction yok (§10): A çıkışı + outbox, B tarafında inbox + yolda beklenti + kabul (ADR-006). Etiket kodu yalnız `(CompanyId, Code)` içinde tekil olduğu için B, fiziksel etikette basılı kodu aynen kullanabilir; yeniden baskı zorunlu değil. Grup genelinde bir tekillik indeksi olmadığından, "bu kod başka şirkette var mı?" sorusu bir hata üzerinden sızamaz.
- **Görünürlük:** her şirket yalnız kendi etiketlerini, hareketlerini ve belgelerini görür. A'nın MTC'si, tedarikçisi ya da geçmişi B'ye kendiliğinden gitmez. Şirketler arası soy ağacı (B'deki bobinden A'nın kabul belgesine) ancak ADR-002'nin öngördüğü ayrı ve yetkili çapraz-şirket okuma modeliyle görülür. Bu model ayrı bir iştir, filtre atlatması değildir ve grup yetkisi kendiliğinden bu okumayı vermez (Ö6 ile aynı ilke).
- **INV-2 DbContext:** Organization ve Counterparty ile aynı desen:
  - her tenant varlıkta `HasQueryFilter(x => x.CompanyId == companyContext.CompanyId)`;
  - senkron ve asenkron `SaveChanges` üzerinde yazma koruması (etkin `CompanyContext` şart ve kayıt ile eşleşmeli; `CompanyId` değiştirilemez);
  - `(CompanyId, Code)` unique;
  - konum için `(CompanyId, LocationId)` bileşik FK: bir etiket başka şirketin konumuna bağlanamaz;
  - ürün için grup seviyesindeki `ProductId`.

  Yeni istisna, allowlist ya da `IgnoreQueryFilters` yok.

### B — Grup seviyesinde fiziksel etiket kimliği + şirket başına kayıt (Party emsali) — **ADR-002 İSTİSNASI GEREKİR**

- **CompanyId / filtre:** `CompanyId` taşımayan ve filtresiz bir `MaterialLabelIdentity` tablosu (kimlik, grup genelinde tekil kod, değişmez fiziksel alanlar). Yanında şirket başına filtreli bir kayıt tablosu (`LabelCompanyRecord`: `CompanyId`, kimlik, bakiye, belge, blokaj). Tenant filtresi dışında kalan **operasyonel** bir tablo ve grup genelinde tekil bir kod indeksi ADR-002'nin kapsamı dışında kalır. Party istisnası master veri içindi; bu, operasyonel fiziksel veri için yeni bir istisna olur.
- **Transfer:** kimlik aynı kalır, B'de yeni bir şirket kaydı açılır. Şirketler arası soy ağacı kimlik üzerinden doğrudan izlenebilir.
- **Görünürlük ve risk:** hangi alanın grup tablosunda durduğu kalıcı bir sızıntı yüzeyidir (heat, tedarikçi, kalite, ağırlık). Grup genelinde tekil kod, başka şirkette o kodun var olduğunu çakışma hatasıyla ele verebilir; bu yüzden kodlar yalnız sunucu tarafından üretilmek zorunda kalır. "Aynı grupta olmak başka şirketin malını görme yetkisi değildir" (§5) kuralı tablo tasarımıyla değil, disiplinle korunmuş olur.
- **INV-2 DbContext:** filtresiz bir varlık, tenant filtre guard'ına yeni bir allowlist girdisi ve yazma korumasına özel dal gerekir. **Seçilirse karar insan kapısıdır (NEEDS-SINAN).**

### C — Etiket, sahip şirkette kalır; başka şirketin konumu ya da gözetimi referansla tutulur — **ADR-002 İSTİSNASI GEREKİR**

- **CompanyId / filtre:** etiket hep hukuki sahip şirketin satırıdır. Başka bir grup şirketinin deposunda dururken konum o şirketin `Location`'ıdır ve bu, şirketler arası bir referans demektir. ADR-002'nin bileşik FK kuralı bunu engeller; kuralın gevşetilmesi ya da konumun FK'siz opak bir kimlik olarak tutulması gerekir.
- **Transfer:** sahiplik değişmezse kayıt taşınmaz, yalnız konum değişir. Sahiplik değişirse A'dakine benzer bir kapanış ve açılış gerekir.
- **Görünürlük ve risk:** malı fiilen tutan şirket (gözeten) bu stoğu kendi filtresi altında göremez, ama sayım ve sevk ondan beklenir. Tersinden bakınca, sahip şirketin kaydında başka şirketin konum kimlikleri durur.
- **INV-2 DbContext:** şirketler arası FK istisnası ya da FK'siz konum referansı gerekir. **Seçilirse karar insan kapısıdır (NEEDS-SINAN).** Önerilmiyor.

## Karar (öneri)

**Seçenek A önerilir.** ADR-002 içinde kalıyor, INV-1'in tek-yer kuralının doğrudan kalıcı karşılığı ve Organization ile Counterparty'nin mevcut filtre desenini kullanıyor. Bu yüzden karar ÜRÜN (Inventory sahibi) ve CARİ'nin (merkezi/tenant görüşü) teknik kararıdır. B ve C yalnız insan kapısıyla seçilebilir.

A'nın INV-2'ye bağladığı maddeler:
1. `MaterialLabel.CompanyId` zorunlu ve değişmez; etiketin bütün pozisyonları aynı `CompanyId`'yi taşır (INV-1'deki "iki şirkette stok yok" kuralı veri modelinde de korunur).
2. Etiket, hareket, bakiye, belge eki ve baskı audit'i tenant satırıdır; global filtre ve yazma koruması Organization/Counterparty deseniyle kurulur. `IgnoreQueryFilters` ve allowlist yoktur.
3. Kod tekilliği `(CompanyId, Code)`; grup genelinde tekillik yoktur.
4. Konum `(CompanyId, LocationId)` bileşik FK ile bağlanır; ürün grup seviyesindeki `ProductId`'dir.
5. Başka şirkete ait kimlikle yapılan sorgu ya da komut genel 404 alır (ADR-002 test ailesi, gerçek SQL ile).

### Bu belgenin seçmediği ve A ile açık kalanlar (Business / sahip kararları; INV-2'yi engellemez)
INV-2 tek şirket içindeki kabul/stok kaydıdır. Aşağıdakiler şirketler arası transferde (T13) ya da sonraki işlerde gerekir. §10 gereği teknik ekip bunları seçmez:
- **Şirketler arası mal politikası:** transfer satış mı, emanet mi, iç devir mi; sahipliğin ve riskin hangi anda geçtiği; yoldaki malın hangi şirketin stoğu sayıldığı.
- **Kaynak şirketten hedefe hangi bilgilerin taşınacağı:** heat/coil numarası, MTC ve belgeler, kalite sonucu, soy ağacı özeti. Teknik varsayılan fail-closed: politika onaylanana kadar yalnız ürün, miktar ve kaynak referansı taşınır. Hedefteki belge durumu Ö3'e göre "bekliyor" başlar ve üretim/sevk serbestliği belge hedefte tamamlanana kadar açılmaz.
- **Grup içi gözetim (transfer olmadan):** bir grup şirketinin malının başka bir grup şirketinin deposunda durması. A'da bunun için ya sahip şirkete ait bir dış-saha `Location`'ı (Organization sahibi) ya da transfer gerekir. Diğer grup şirketinin sahip/gözeten olarak nasıl temsil edileceği (şirket mi, Cari karşı taraf mı) Cari ve Organization sahiplerinin kararıdır.
- **Fiziksel etiketin transferde yeniden basılıp basılmayacağı:** A bunu zorunlu kılmıyor (kod şirket içinde tekil); bu operasyonel bir tercih.
- **Şirketler arası soy ağacı okuma modeli:** kimin göreceği ve hangi yetkiyle görüleceği.

### INV-2'nin diğer iki maddesi (yalnız referans; kodları INV-2'de)
- **kg ölçeğinin Settings profiline bağlanması:** onaylı profil (#279) ve host kaydı (#284) main'de. INV-2, ürünün stok birimini `SettingsUnitIdentitiesV1.Kilogram`'a ve kg hassasiyetini profilin `Scale` değerine bağlar (INV-1'de sabit 3).
- **ORG P3-1 (#281 review):** kaynak + satır tekilliği (domain'de mi, unique indekste mi) ve stok taşıyan bir etiketin yeniden girişinin reddi. INV-1'de tekrar tanıma yalnız hareket kimliğiyle yapılıyor.

## Sonuçlar ve riskler

- **Artı:** ADR-002'ye yeni istisna yok; mevcut guard, write guard ve SQL kabul desenleri aynen kullanılır. Bir şirketin etiketi, belgesi ve geçmişi başka şirkete ancak açık bir transfer olayı ya da yetkili bir okuma modeliyle geçer.
- **Eksi:** transfer edilen bobinin iki kaydı olur (A'da kapanmış, B'de açık). Şirketler arası iz ancak köken referansı ve ayrı okuma modeliyle birleştirilir. B tek başına A'daki geçmişi göremez; bu kasıtlıdır.
- **Risk:** köken referansı (kaynak şirket ve etiket kimliği) B'de bir bilgi olarak durur. Ancak B bu kimlikle A'da bir şey okuyamaz (genel 404). Referansın hangi alanları taşıyacağı yukarıdaki veri taşıma politikasına bağlıdır.
- **Risk:** bir grup şirketinin yanlış `CompanyContext` ile etiket yaratması. Yazma koruması ve bileşik FK ile engellenir; INV-2'de gerçek SQL kabulüyle kanıtlanmalı.

## Geri alma/yeniden değerlendirme koşulu

Business, şirketler arası transferde tek fiziksel kimliğin bütün şirketlerde **aynı kayıt** olarak görünmesini zorunlu kılarsa (B), ya da grup içi gözetimde gözeten şirketin sahip şirketin kaydını görmesini isterse (C), bu belge insan kapısıyla yeniden açılır. A'nın kayıtları B'ye taşınabilir: B'deki etiket kayıtları, köken referanslarıyla birleştirilerek bir grup kimliğine bağlanabilir.

## Test ve kanıt (INV-2'de istenecek; bu belgede kod yok)

- Unit: etiket ve pozisyon şirket eşitliği; `CompanyId` değişmezliği; transferde yeni kayıt ve köken referansı (T13'te).
- Mimari: yeni tenant varlıkların filtre guard'ında allowlist'siz geçmesi.
- **Gerçek SQL** (`SqlServerRelationalAcceptanceTests` ailesi): write guard, `(CompanyId, Code)` unique, `(CompanyId, LocationId)` bileşik FK hatası 547, başka şirketin etiket kimliğine HTTP 404/403. Unit ve bileşen kanıtı gerçek SQL kanıtının yerine geçmez.
