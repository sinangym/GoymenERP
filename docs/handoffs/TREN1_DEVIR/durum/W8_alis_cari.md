# TREN-1 W8 — Alış / Cari

## ALC-S14 — B-16, ALS-D24/D28

V2 tedarikçi portu gerçek Cari `Tolling` bayrağını ve şirket/cari/hesap sürümlerini
korur; V1 şekli değişmez. Ödeme vadesi kullanıcının ALS-D24/D28 kararıyla siparişe
özgüdür; Cari varsayılanı üretilmez. Rol matrisi, aktör/şirket erişim kaybı ve gerçek
SQL owner okumasında taze rol/yabancı hesap ayrımı için test kaynakları eklendi.
Yeni migration veya izin yok. Host entegrasyonunda
`IPurchasingPartyAccountPortV2 → CounterpartyPurchasingPartyAccountPortV1` kaydı
eklenecek. Bu checkpoint'in derlemesi ortak .NET sırasını bekliyor; runtime
testler **NOT_RUN**, entegrasyon doğrulanmadan dilim DONE sayılmadı.

Bu son paket mevcut 174 dilim kapsamındadır; yeni dalga veya iş politikası açmaz.
ALC-S6 **PARTIAL**: tek-kabul ve çok-kabul owner zincirleri yazıldı. Batch genel
satır başına tek net etki ve sipariş başına tek sürüm uygular. Bildirim alıcı owner
bağlantısı, gerçek host sağlayıcıları ve tümleşik kabul tamamlanmadan DONE sayılmaz.

## Kabul düzeltmesi ve iade sahibi

- ALS-D86/D91/D92/D93, ALIS_F017 bölüm3.7/3.8: özgün brüt kabul payı değişmez;
  geri alma ve varsa yeni kabul tek net işlem/sürümde kaydedilir. Net sıfır düzeltme
  kapalı/iptal siparişi geçici olarak canlandırmaz. Normal yeni kabul kapısı korunur.
- Cancelled ve API-11 kapalı satır yalnız pozitif net açılan pay kadar canlanır.
  Eski iptal/kapanış payı ayrıca düşülmez; iki kat arz oluşmaz. İlk ve sonraki iptaller,
  kapanış bilgileri ve yeniden iptalin yalnız yeni kalanı tüketmesi korunur.
- Yenisi beklenmeyen tedarikçi iadesi kabulü azaltır ve kesin iade payına eklenir;
  açık tedarik oluşturmaz. Gelecek tedarik, açık alış, Satış bağlantılı tedarik ve termin
  okuma kaynakları bu payı da kapsar. Teslim edilmiş kesin iade payının termini açılmaz.
- Borrowed transaction participant commit yapmaz. Şirket, özgün kabul/satır ilişkisi,
  expected version, satır kilidi, ortak idempotency denemesi ve immutable sonuç korunur.
  Aynı anahtar daha sonraki iptal/sürümden etkilenmeden ilk yanıtı döner.
- Yeni Audit kaydı Audit sahibinin transaction portundan aynı işlemde yazılır;
  yeni bir modüller arası Audit tablosu yazarı veya filtre bypass'ı eklenmedi.
- ALC-S5 bağı: iade önerisinin sipariş sürümü/geri alma miktarı kalıcıdır. Yeni iade
  denemeleri ilk yanıt görüntüsünü saklar; sonradan reddedilen önerinin ilk anahtarına
  ilk Pending yanıtı döner. Eski NULL görüntülü denemelerin açık legacy fallback'i
  raporda kalır; eski kayıtlar tahmini yanıtla geriye dönük doldurulmaz.

## Migration ve K3 notları

- EF10.0.8 üretimi `20261116213347_AddPurchaseReceiptAdjustments`: immutable düzeltme
  ve iptal geçmişi, şirket/sipariş/satır/kabul birleşik ilişkisi, yeni miktar payları ve
  kısıtlar. Down ilk koruması **51949**. Eski Cancelled header gerçekleri geçmişe
  aktarılır; bu veri mutasyonu gerekçeli **K3 review bekliyor** olarak taşınır.
- EF10.0.8 üretimi `20261116215806_AddPurchaseSupplierReturnResponseSnapshots`:
  ilk iade yanıtı görüntüsü ve JSON kısıtı; Down ilk koruması **51953**.
- Yeni ham SQL/bypass guard girdileri yalnız yeşil sonuç için izin listesine eklenmedi.
  Ortak bağlantı/transaction katılımı ve iki Down/veri koruma SQL'i K3'te incelenir.
- EF10.0.8 üretimi `20261116222957_AddPurchaseReceiptAdjustmentBatches`: batch başlığı,
  değişmez detay/deneme bağlantıları, tek sürüm indeksleri ve outbox rowversion.
  Down ilk koruması **51956**, batch veya herhangi bir outbox cursor geçmişi varken
  veri/tekrar yayım kaybını reddeder. Boş depoda generated geri alma korunur.
- Purchasing outbox sabit `MIN_ACTIVE_ROWVERSION` sorgusuyla yalnız commit edilmiş
  güvenli sırayı okur; şirket filtresi korunur. Erken commit olan sonraki mesajın
  eski açık transaction mesajını atlatmaması için gerçek SQL yarış kaynağı eklendi.
  Ham ufuk okuması gerekçeli **K3 review bekliyor**; dispatcher etkinleştirilmedi.
- Grant/rol, production, OPEN durumları, Finance STOP ve altyapı değişmedi.

## Doğrulama

API startup derlemesi ve EF üretimleri exit0. Purchasing.UnitTests derlemesi exit0,
sıfır hata/uyarı. Api.IntegrationTests derlemesi de exit0, sıfır hata/uyarı.
Kaynaklar: net sıfır/pozitif canlanma, kapanış/iptal geçmişi, kesin iade,
değişmez replay, aynı transaction Audit rollback, shared terminal deneme, model
append-only/şirket ilişkisi, gerçek SQL Down51949/51953 ve ilk Pending replay.

Tam test, gerçek SQL/HTTP, Vitest, browser ve CI koşumları **NOT_RUN**; mevcut
kırmızıların geçtiği veya bu kaynakların kabul aldığı iddia edilmez.

## Çoklu kabul düzeltmesi kaynak checkpoint'i

- ALS-D86/D91/D92/D93: bütün özgün paylar, kimlikler ve sonuç miktarları önce
  doğrulanır. Bir erken geri alma ile sonraki yeniden kabul geçici canlanma üretmez.
  Net sıfır batch Cancelled durumunu ve sıfır tahsis edilebilir miktarı korur.
- Tüm batch sonucu, sıralı detaylar ve genel satır toplamları değişmez saklanır;
  sonraki tekil düzeltme/version artışından sonra replay ilk sonucu döndürür.
  Eksik başlık/çocuk geçmişi başarıya çevrilmez. Bütün owner yazıları ve Audit aynı
  ödünç transaction'a katılır; participant commit yapmaz.
- Yalnız gerçekten canlanan genel satır için `purchasing.order-line.revived.v1`
  outbox olayı kaydedilir. Satış ilişkileri ve alış oluşturan/onaylayan alıcıları
  bildirimin owner portlarına ayrıca bağlanacaktır; kişi varsayımı yapılmaz.
- Son batch kaynaklarında Purchasing.UnitTests ve Api.IntegrationTests (API dahil)
  derlemeleri exit 0, sıfır hata/uyarı. Unit/model/SQL kaynakları yazıldı;
  test koşumu yapılmadı. Pin/ilişkisel envanter son entegrasyonda üretilecektir.

## Canlanma bildirimi kaynak bağlantısı

- Kullanıcının "Satış ve satın alma sorumlularına" ve "Siparişi oluşturan ve
  onaylayan kişi olabilir" yanıtı kapsamında, alış owner portu gerçek `CreatedBy`
  ve `ApprovedBy` kimliklerini aynı şirket/sipariş/genel satır üzerinden okur.
  Eksik eski onaylayan veya kullanıcı kimliğine çevrilemeyen aktör uydurulmaz;
  eksik alıcı kümesi tamamlanmış teslimat sayılmaz. Grant/rol yapılmaz.
- Geciken bildirim, olaydan sonra oluşan satış bağlantılarını almamalıdır.
  Inventory geçmiş bağlantı okuyucusu artık hostun açık Serializable transaction'ına
  katılabilir; host satış/sipariş kilitleri altında gerçek bağlantıları yakalar.
  Purchasing canlanma olayı bu sıralı, tekilleştirilmiş kimlikleri değişmez taşır.
  Eksik/yabancı/eski sürümlü görüntü canlanmada 503'tür; gerçek boş liste ayrı
  tutulur. Net sıfır veya canlanmayan düzeltmede bu bildirim kanıtı aranmaz.
- Yeni borrowed-context construction yalnız sahibi Inventory'nin kendi tablolarını
  okur, commit yapmaz. Gerekçe **K3 review bekliyor**; guard izin listesine sessizce
  eklenmedi. Eski `SalesTargets = null` olaylar güncel ilişkilerden tamamlanmaz.
- Alıcı erişim çiti, kayıtlı aktörler/legacy durumu, immutable snapshot reddi,
  borrowed transaction'daki henüz commit edilmemiş link okuması ve rollback için
  unit/gerçek SQL test kaynakları yazıldı. Runtime testler **NOT_RUN**.
- Purchasing.UnitTests ve Api.IntegrationTests (API dahil) kaynak derlemeleri
  exit 0, sıfır hata/uyarı. Hostun snapshot
  üretimi ile Work inbox/kişisel ekran zinciri birleşene kadar ALC-S6 **PARTIAL**.

## Mal kabul maliyeti owner kanıtı

- `IPurchaseReceiptCostProofV1`, hostun kabul yazdığı transaction'a katılarak gerçek
  kabul kimliği, miktarı, ilk sonuç sürümü/tarihi, ürün sürümü ve alış fiyatını okur.
  İskontolu maliyet ile vade farkı içeren net fiyat ayrı kalır. Alış faturası yokken
  fatura kuru eksikliği sipariş veya giriş günü kuruyla gizlenmez.
- Yeni Purchasing context construction yalnız kendi tablolarını okur ve commit
  yapmaz; **K3 review bekliyor**. Foreign şirket/satır ve bilinmeyen kabul bulunamaz.
- Owner kaynağı API/Integration derlemesinde exit 0. İki yeni gerçek SQL fact'i
  aynı transaction'daki henüz commit edilmemiş kabul, rollback, şirket ayrımı,
  iskontolu fiyat ve eksik fatura kurunu kapsar; bu son test kaynaklarının
  derlemesi sıradaki ortak derlemede, runtime koşumu **NOT_RUN**.
- Inventory host bağlayıcısı ve gerçek gelen fatura sağlayıcısı tamamlanana kadar
  uçtan uca maliyet kapsamı **PARTIAL**.

## KIM-S19 / N-12 — alış siparişinde gerçek numara owner bağlantısı

- KIM-D53 ve ALS-D36'nın yerini alan kullanıcı cevabı doğrultusunda yeni alış siparişi,
  Settings'in şirket bazında açık girilmiş önek/yıl/sıra biçiminden numara alır.
  Eksik biçim/provider 503 ile kapanır; varsayılan önek, otomatik kurulum veya geçmişe
  numara doldurma yapılmaz. Bu mevcut KIM-S19 diliminin tüketicisidir, yeni dilim değildir.
- Settings numara owner'ı, siparişle aynı açık Serializable transaction'a katılır.
  Numara/biçim sürümü/sıra siparişte immutable tutulur; numara geçmişi kendi sahibindedir.
  Sipariş/numara/iki denetim kaydı birlikte commit veya rollback olur. Commit öncesi
  aktör, şirket ve güncel yetki tekrar doğrulanır. Aynı anahtar yeni numara tüketmez.
- İptal aynı transaction içinde numara boşluğu geçmişi ekler; sonraki sipariş numarayı
  yeniden kullanmaz. Eski sipariş numarasız kalabilir. UI kayıtlı numarayı gösterir;
  üretmez, legacy eksikliği ayrı Türkçe metinle gösterir. Strict decoder kaynak testleri var.
- **51957** additive nullable kolonlar/unique/check migration'ının ilk Down korumasıdır.
  Model ve gerçek SQL kaynakları annual reset, replay, eksik biçim, iptal/tekrar kullanmama,
  save sonrası yetki kaybında tüm transaction rollback ve ilk sıra numarasının korunmasını kapsar.
- Direct-store fixture gerçek Settings ve Audit owner'larını kullanır; yalnız fixture
  kimlik/yetkisi sentetiktir. HTTP hostunun gerçek authorization servisleri değiştirilmedi.
  Genuine Product→alış→kabul yolunda numara biçimi gerçek HTTP komutuyla açıkça kaydedilir.
- EF CLI 10.0.8 `20261117002600_AddPurchaseOrderDocumentNumbers` migration/snapshot dosyalarını üretti.
  Up yalnız nullable kolon/index/CHECK DDL'idir; Down ilk işlem 51957 ile kayıtlı numara geçmişini korur.
  API, Purchasing.UnitTests, Api.IntegrationTests ve TypeScript kaynak derlemeleri exit 0, hata/uyarı 0; runtime
  Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**. Inventory ortak receipt fixture'ının aynı
  gerçek numara test owner'ına bağlanması ve merkezi pin/kontrat üretimi entegrasyondadır.

### STK-SC36-b tedarik kaynağı — FNS-D103

- Gerçek mal kabul maliyet kanıtı aynı ödünç transaction içinden onaylı sipariş kurunu da
  ayrı tipli alan olarak okur. Fatura kuru bulunmadığında sipariş kuru fatura kuru yerine geçmez.
- SQL kaynakları onaylı sipariş kuru varken faturanın hâlâ kayıtsız kalmasını, kur belirtilmeyen
  siparişi, şirket izolasyonunu ve read-only rollback'i kapsar. Bu ek kaynak henüz derlenmedi;
  SQL/runtime **NOT_RUN**. Stok ilk giriş kuru sahibinin bağlantısı ayrı dilimdedir.

## ALC-SC47 / ALS-D112-D115 — cari revizyonu

- Onaylı alış siparişinde gerçek Cari sahibinden yeniden seçilen tedarikçi/şirket hesabı,
  gerekçe, expected version ve mevcut sipariş revizyon izniyle taslak revizyon açılır.
  Herhangi bir kabul geçmişi (geri alınmış kabul dahil) veya fatura bağlantısı varsa reddedilir.
  Satır kimlikleri/miktarları korunur; eski fiyatlar ve her onay append-only geçmişte kalır.
  Güncel fiyatlar yeniden girilir; mevcut `purchasing.orders.approve` ve farklı hazırlayan/onaylayan
  çitiyle yeniden onaylanır. İlk oluşturan değiştirilmez; güncel hazırlayan ayrı tutulur.
- Fatura bağlantısı Purchasing'in kendi şirket filtreli immutable tablosundadır. Gelen alış
  faturası üreticisi, aynı sipariş satırı kilitlerinde bu tabloyu doldurmalıdır; yalnız tablo
  eklemek gelen fatura akışını tamamlamaz. Bu owner bağlantısı nedeniyle dilim **PARTIAL** kalır.
- Para birimi/profil görüntüsü olmayan eski sipariş, gerekli fiyatların girilemeyeceği taslağa
  taşınmaz; onayı ve carisi korunarak 503 döner. Tarihsel para birimi/profil uydurulmaz.
- Kayıt, idempotency ve audit tek transaction'dadır; şirket/yetki ve yeni Cari uygunluğu
  save öncesi/sonrası tekrar doğrulanır. Fiyat yanıtı için güncel fiyat okuma yetkisi tekrar
  okunur. Satır kapanışı ile cari/fiyat/kur revizyonları gerçek sürüm sırasıyla yeniden kurulur.
- EF CLI `20261117072202_AddPurchaseSupplierRevisions` migration/designer/snapshot üretti.
  Up yalnız DDL; Down ilk işlemde geçmiş veya fatura bağı varken **THROW 51977** ile reddeder.
  Veritabanına uygulanmadı. Yeni bypass, grant veya rol yok; Shell sözleşmesi 1.9, mevcut
  izinleri tüketen `purchasing.orders.supplier-revise` capability'si eklendi.
- Gerçek cari arama/seçim, gerekçe, fiyat/iskonto/vade farkı girişi ve geçmiş ekranı yazıldı.
  UI hesap yapmaz; bilinmeyen sonuçta aynı gövde/anahtar oturumda korunur. Yetki, boş,
  çakışma, çevrimdışı, 503 ve bozuk kaynak/bekleyen kayıt durumları ayrı gösterilir.
- Birim/model, HTTP, dört gerçek SQL fact ve Vitest kaynakları hazır. Son kaynakların
  API (Integration bağımlılığı), Purchasing.UnitTests, Api.IntegrationTests ve TypeScript
  derlemeleri exit 0; .NET uyarı/hata 0. Backend komutları `--no-restore -m:1 -nr:false
  -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false` ile çalıştı.
  OpenAPI üretimi bu derlemelere dahil değildir. `git diff --check` exit 0.
  Önceki mal kabul kur kanıtının test namespace düzeltmesi `404186d8a` ile kaydedildi.
  Runtime Unit/SQL/HTTP/Vitest/browser/CI
  **NOT_RUN**. OpenAPI, relational envanter ve bütün pinler merkezi entegrasyon sırasındadır.

## FIN-S13 / ALC-SC47 — gerçek gelen fatura çekirdeği ön koşulu

- ALS-D7/D8/D14/D15/D41/D44/D59/D64: resmi kaynak kimliği/hash/tarih, satır tutarları,
  tedarikçi ve ödeme planı immutable fatura geçmişine alındı. İki yöndeki fiyat, miktar
  ve kur farkları kayıtlı Ayarlar sürümüyle karşılaştırılır; hazırlayan kendi farkını onaylamaz.
- Eşleştirme gerçek kabul/sipariş bağını şirket filtresi ve bütün general-line kimliklerinin
  tek global kilit sırası altında kaydeder; cari revizyonu bu gerçek bağı görür.
  Kabul payı SQL ölçeğine tam sığmıyorsa reddedilir, sessiz yuvarlama yapılmaz.
- Kesinleştirme bir adet `purchasing.invoice.finalized.v1` outbox kaydı ve sabit Finans
  kabul kimliği üretir. Finans cevabı ayrı revizyondur; kesinleşmiş payload değişmez.
  Alış yazısı kendi transaction'ında tamamlanır; borrowed/ambient transaction reddedilir.
- EF CLI `20261117084001_AddPurchaseIncomingInvoices` migration/designer/snapshot üretti.
  Up yalnız DDL; Down önce resmi kimlik/geçmiş/kabul bağı/terminal sonuç varsa **51980**
  ile reddeder. DB'ye uygulanmadı. Model, birim ve altı gerçek SQL test kaynağı yazıldı.
- API, Purchasing.UnitTests ve Api.IntegrationTests kaynak derlemeleri exit 0,
  uyarı/hata 0. İncelemede bulunan transaction sahipliği, miktar ölçeği ve global kilit
  sırası düzeltildi. Runtime Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**.
- **PARTIAL:** gerçek belge/dosya içe aktarımı, Ayarlar tolerans ve kabul fiyat/kur
  karşılaştırma kaynakları, yetki çitli uygulama/HTTP/UI, ayrı Finans kabul/tekrar çağrısı
  ve masraf/hizmet satırının kendi gerçek owner bağlantıları henüz tamamlanmadı.
  Masraf satırı için yapay mal kabul üretilmez; ALC-S7/S8 bu checkpoint ile DONE değildir.
  Merkezi pin/ilişkisel envanter güncellemesi entegrasyon sırasındadır.

### FIN-S13 / ALC-SC47 — resmi fatura kuru ve ayrı Finans kabul bağı

- Gerçek kesinleşmiş alış faturalarının immutable revizyonları kabul bağı üzerinden okunur.
  Tek resmi kurla tamamı faturalanmış kabul bu kuru sağlar; eksik kapsama, kesinleşmemiş
  fatura veya bir kabulde farklı resmi kurlar açık bekleme nedenidir. Kur seçilmez veya
  ortalanmaz. Kısmi maliyetin kesinleştirilmesi kararı ayrıca bekler; bu okuma onu seçmez.
- Purchasing üreticisine özel Finans adaptörü gerçek açık-kalem kabul servisini çağırır.
  Sales'in yetki/adaptör kaydı değiştirilmez. Güncel şirket, aktör ve kesinleştirme yetkisi
  yeniden okunur; resmi tutarlar ve kesinleşmiş sürüm korunur. Farklı yetkilinin tekrarında
  taşıma anahtarı değişse de aynı kabul kimliği ikinci borç üretimini önler.
- Beş fatura izin adı yalnız kayıt olarak eklendi; grant, rol veya Critical değişikliği yok.
  Birim, DI/yetki ve beş gerçek SQL test kaynağı eklendi. API, Purchasing.UnitTests ve
  Api.IntegrationTests kaynak derlemeleri son durumda exit 0, uyarı/hata 0.
  Integration ilk derlemelerinde test yardımcısının ICompanyContext.Set ve IReadOnlySet
  oluşturma hataları düzeltildi. Runtime test/SQL/HTTP/Vitest/browser/CI **NOT_RUN**.
- **PARTIAL devam:** uygulama/HTTP/UI ve gerçek import akışı henüz bu adaptöre bağlı değil;
  Finans kabulü uçtan uca tamamlandı sayılmaz. Önceki checkpoint'in kalanları geçerlidir.

### FIN-S13 / ALC-SC47 — yetki çitli fatura komutları ve HTTP

- Gerçek Purchasing invoice store üzerinden okuma, fark onayı, kesinleştirme ve ayrı
  Finans kabulü tekrarı bağlandı. Komut iznine ek olarak güncel fiyat okuma yetkisi
  ölçülür; şirket/aktör/yetki yeniden doğrulanır. Hazırlayan kendi farkını onaylayamaz.
- Kesinleşmiş alış kaydı commit edildikten sonra Finans çağrılır. Bilinmeyen/ret cevap
  açık bekleyen durumu korur; olumlu cevap ayrı idempotent revision ile kaydedilir.
  Farklı aktörün tekrarında sabit kabul kimliği ve resmi payload korunur. Çağrı sırasında
  yetki kaybı başarı yanıtı vermez. Bozuk owner geçmişi 503; client şirketi ve kanonik
  olmayan sürüm reddedilir. Para/miktar HTTP'de tam string olarak taşınır.
- Birim, HTTP ve iki gerçek SQL fact kaynağı yazıldı. SQL fact'leri Purchasing'in
  commit sınırını gerçek SQL'de ölçmek üzere yazılmıştır; Finans tarafı sentetik probe'dur,
  gerçek Finans uçtan uca kabulü olarak sunulmaz. API, Purchasing.UnitTests ve
  Api.IntegrationTests kaynak derlemeleri exit 0, uyarı/hata 0; `git diff --check` exit 0.
  OpenAPI üretimi derlemede kapalıdır. Runtime Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**.
- **PARTIAL:** gerçek dosya importu, kabul/fiyat/kur/Ayarlar eşleştirme komutu, ekran
  girişleri ve masraf/hizmet owner bağı hâlâ açıktır. Yeni migration veya izin yoktur.
  OpenAPI/ilişkisel envanter/pin üretimi merkezi entegrasyonda yapılacaktır.

### FIN-S13 — gerçek kabul, fiyat ve toleransla eşleştirme

- ALS-D8/D14/D15/D52/D63: istemci yalnız fatura satırı, alış siparişi, kabul kimliği ve
  pay miktarı seçer. Fiyat, kur, tedarikçi ve tolerans istemciden alınmaz. Mevcut
  Purchasing satır kilitleri altında gerçek kabul ve kayıtlı sipariş fiyatı okunur;
  şirketin yayımlanmış Ayarlar toleransı sürümüyle saklanır. Kontrol kuru mevcut
  Settings resolver'ından kabulün İstanbul iş gününe göre gelir; algoritma kopyalanmaz.
- Eksik fiyat/politika/kur başarı sayılmaz. Aynı kabulün faturalandırılmış miktarı
  tekrar kullanılamaz. Fiyat payları ortak exact calculator ile hesaplanır; tek fatura
  satırının toplamı bir kez kayıtlı para ölçeğine yuvarlanır. Yetki kaybı kaynak
  başarılı veya hatalı döndüğünde yeniden ölçülür.
- Resmî numarayla şirket filtresi korunan sayfalı liste ve satır bazında gerçek,
  faturalanmamış kabul adayları eklendi. Aday okuması rezervasyon değildir; komut
  seçimi kilit altında tekrar doğrular. Kullanıcıdan teknik kimlik ezberlemesi
  gerektirmeyen ekran girişinin owner kaynaklarıdır.
- Birim, HTTP ve dört gerçek SQL fact kaynağı eklendi. SQL kaynaklarında gerçek
  Purchasing kayıtları/fiyatları/işlem ve miktar bağları kullanılır; Settings yalnız
  sentetik sözleşme fixture'ıdır, uçtan uca Settings kabulü iddia edilmez.
- Resmî numarayla liste/detay ve gerçek kabul seçimi, fark onayı, kesinleştirme ve
  Finans aktarımı tekrar deneme ekranları eklendi. Bilinmeyen yazma sonucu aynı
  anahtar ve gövdeyle doğrulanır; şirket/aktör değişiminde eski veri gösterilmez.
  Tutar, kur, tolerans ve pay toplamı ekranda yeniden hesaplanmaz. Beş capability
  mevcut izinleri kullanarak kayıtlıdır; uygulama yayını kapalıdır (`staged`).
- API, Purchasing.UnitTests ve Api.IntegrationTests kaynak derlemeleri exit 0,
  uyarı/hata 0. Frontend `tsc -b` exit 0; sözleşme/HTTP/ekran
  Vitest kaynakları yazılmıştır. Runtime Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**.
  Yeni migration/izin yok. Dosya importu ve hizmet/masraf eşleştirme bağı
  tamamlanmadan dilim **PARTIAL** kalır. Bu checkpoint native Mikro/UBL içe
  aktarımının veya gerçek kullanıcı iş akışı kabulünün tamamlandığı anlamına gelmez.
- Son shell assertion güncellemesi (20 bildirim, 15 eski yayında + 5 fatura staged)
  merkezi Integration derlemesine dahildir; authority hash üreticisi entegrasyonda
  çalışacaktır. Kayıt veya yayına alma yerine test beklentisi gerçek metadata ile hizalanmıştır.

### ALC-S7 — hizmet ve kendi malı giden fason siparişi

- Kaynak commit: `b2e39a331` (ALS-D29, ALS-D40, KIM-D49; API70–72).
  **PARTIAL:** fiyatlı hizmet siparişi hazırlama → ayrı kişi onayı → gerçek hizmet kabulü →
  gerçek hizmet kabulüyle fatura eşleştirme kaynakları tamamdır. Fiyatsız siparişin
  sonradan fiyat tamamlama yolu aşağıda açık kalır; genel dilim DONE sayılmamıştır.
- Etkin hizmet tipi gerçek Settings listesinden, birim ve para profili gerçek owner'dan,
  tedarikçi ve Supplier/Tolling rolü gerçek Cari kaynağından okunur. Yapay katalog,
  numara, fiyat veya ürün varsayımı yoktur. Kendi malı fason için etiketin şirket mülkiyeti
  ve ürün/sürüm, beklenen çıktı için gerçek ürün kimliği doğrulanır. Müşteri malı
  fasonuyla karıştırılmaz. Fiziksel gönderim/dönüş ve STK-S3 bu siparişin hareketi değildir.
- Mevcut OrdersCreate/Read/Approve ve GoodsReceiptsRecord izinleri kullanılır. Şirket,
  aktör ve company-wide yetki await sonrasında ve commit öncesinde yeniden ölçülür.
  Hazırlayan kendi siparişini onaylayamaz. Expected version, aynı aktör/anahtar/içerik
  replay, correlation/causation ve Audit owner kaydı aynı owned Serializable işlemdedir.
  Ticari numara gerçek Settings PurchaseOrder serisinden aynı işlemde ayrılır.
- Gerçek hizmet kabulü append-only revision ve ayrı immutable kabul faktı olarak saklanır;
  mal kabulü/stok hareketi üretilmez. API72 `IPurchaseSubcontractOrderReadPortV1`
  yalnız trusted server şirketiyle fiyat içermeyen onay/açıklık/tedarikçi/hizmet/malzeme
  kaynağı verir. Ayrı `IPurchaseServiceAcceptanceReadPortV1` gerçek kabul kaynağını sunar.
- Fatura satırı `ItemType=Service`, `LinePurpose=service`, boş ProductId ve açık typed
  ServiceTypeId taşır. Nullable alan eski mal JSON'una yazılmaz. Aynı gerçek kabulün
  faturalanan miktarı tekrar kullanılamaz; aynı global sıralı alış satır kilitlerinde ve
  Serializable işlemde yeniden okunur. Yeni service invoice binding kendi composite
  FK'sına sahiptir; mal kabul FK'sı gevşetilmez. Hizmet, Inventory mal maliyeti bağına girmez.
  Finalized producer typed hizmet kimliğini korur; Accounting tüketici hizası merkezde
  ayrı owner tarafından alınacaktır.
- Numaralı liste/detay, gerçek kaynak seçimiyle çok satırlı form, onay ve kabul ekranı
  yazıldı. Fiyat backend'de esirgenir; fiyat/kur/tolerans hesapları UI'de kopyalanmaz.
  Şirket/aktör değişimi, yetkisiz, bulunamadı, boş kaynak, 503, bozuk yanıt ve bilinmeyen
  yazma sonucu ayrıdır. Bilinmeyen yazma aynı içerik/anahtarla doğrulanır.
- EF üretimi `20261117110054_AddPurchaseServiceOrders` exit 0. Up yalnız beş yeni tablo/
  indeks/FK DDL'sidir. Down ilk işlemde **THROW 51992** ile sipariş, revision, kabul,
  fatura bağı veya terminal geçmiş varken veri kaybını reddeder. DB uygulanmadı.
- Kaynak doğrulaması: API, Purchasing.UnitTests, Api.IntegrationTests ve frontend
  `tsc -b` exit 0; uyarı/hata 0. Unit/model, HTTP, gerçek SQL, transport/strict decoder
  ve DOM test kaynakları yazıldı. Runtime Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**.
  SQL hizmet siparişi/Audit/numara, rollback, replay, şirket, Down ve gerçek kabul
  eşleştirmesini ölçer; Settings hizmet kataloğu/Finans uçtan uca kabulü iddia edilmez.
- Merkezi mount gereksinimi: `AddPurchaseServiceOrderHostV1()` ve
  `MapPurchaseServiceOrderEndpointsV1()`; shell provider 1.11, beş capability ve üç
  frontend route kaynağı hazırdır. Merkezi nav/OpenAPI/pin/envanter üretimi kök owner'dadır.
- Açık somut eksik: ALS-D18 fiyatsız siparişi kabul eder. Matcher eksik fiyatı 503 tutar;
  hizmet siparişinde sonradan gerçek fiyat giriş/revizyon yolu henüz yoktur. Mevcut genel
  siparişte de yalnız tedarikçi revizyonuna özel fiyat komutu bulunur. Yeni onay/finans
  politikası seçilmedi; mevcut karar/spec fiyat lifecycle'ı ayrıca dar doğrulanacaktır.

#### ALC-S7 takip — ALS-D9/D18 fiyat tamamlama, ALS-D105/D106

- Kaynak commit: `7e3aad912`. Önceki checkpoint'te belirtilen fiyatsız fiyat tamamlama
  boşluğu mevcut ALS-D18 metniyle çözüldü: sonradan fiyat girişi yeni Draft revision
  oluşturur; yeni hazırlayan kendi revizyonunu onaylayamaz. Gerçek hizmet kabulü,
  miktarlar ve önceki fiyat/onay sürümleri değişmez. Tekrar onay olmadan fatura
  eşleştirme çalışmaz. Yapılmış fiziksel işlem fiyat revizyonunda yeniden yapılmaz.
- Mevcut OrdersRevise izni kullanılır; yeni izin/grant yoktur. Şirket/aktör/yetki,
  expected version, reason, idempotency ve aynı işlem Audit çitleri korunur. Backend,
  HTTP ve ekran komutu ile gerçek SQL kabulü→eksik fiyat503→revision→yeniden
  onay→eşleşme test kaynağı eklendi. Shell owner 1.12, toplam 26 bildirimdir;
  altıncı hizmet capability `purchasing.service-orders.prices-revise` mevcut izinlere bağlıdır.
- ALS-D106: giden kendi malı fasonun fiyat birimi actual UnitProfile.IsKilogram
  kanıtıyla kg olmak zorundadır; kod metni 'kg' diye birim anlamı türetilmez. UI de
  gerçek unit identity IsKilogram alanıyla seçim sunar; server profili tekrar okur.
- ALS-D105 READ bağı: yalnız actual Settings Purchasing-side vergi varsayılanı okunur;
  şirket/side/version ve değer snapshot'ı satırda saklanır/gösterilir. Kaynak yoksa503;
  sıfır vergi, Sales varsayılanı veya mevzuat kodu uydurulmaz. İlk onay, güncel owner
  sürümüyle snapshot eşleşmesini ölçer. Fiyat-only revision tarihsel vergi kaynağını korur.
- **PARTIAL kalan:** ALS-D105 alış fasonu vergi varsayılanı yönetimi ve satır override
  yazma yetkileri için açık insan cevabı beklenir; bu yazmalar kapalıdır. Gerçek yönetim/
  override kaynağı olmadan tüm giden fason kullanıcı workflow'u tamamlandı sayılmaz.
- Bu son C# kaynak takibi **BUILD NOT_RUN**; önceki checkpoint'in derleme0 kanıtı bu
  yeni head'e taşınmaz. Erken fiyat UI takibi TSC0 verdi; son vergi DTO/UI ekleri sonrası
  **TSC NOT_RUN**. Kök owner merkezi import derlemelerine dahil edecektir. Unit/SQL/HTTP/
  Vitest/browser/CI runtime **NOT_RUN**. Yeni migration/THROW yok;51992 aynen kalır.

#### ALC-S8 checkpoint — gerçek ek masraf kaydı/okuması (PARTIAL)

- Kaynak commit: `60a2e3110ee9ef7065579fba9898a0259590e86c`. Dayanaklar
  ALS-D23/D27/D80/D107, KIM-D49 ve API23/API51'dir. Gerçek mal veya hizmet alış
  siparişi ve isteğe bağlı gerçek kabul/satır kimliğiyle değişmez masraf kaydı eklendi.
  İki kabul türü ayrı şirketli composite foreign key'lerle korunur; sahte mal kabulü,
  fiziksel miktar, stok hareketi veya ortalama maliyet etkisi üretilmez.
- Gerçek Cari Supplier hesabı siparişin tedarikçisi veya başka firma olabilir. Actual
  Settings masraf tipi, tarihli KDV listesi ve para profili sürümü saklanır. Etkin tip
  yoksa genel açıklama yeterlidir; etkin tip varsa açık seçim zorunludur. Eski tipsiz
  kayıtlar değişmez. KDV/brüt tutar ortak backend calculator'ından gelir.
- Kayıt yetkisi yeni insan yanıtıyla mevcut `purchasing.orders.revise` olarak
  kesinleşti: Issue #1189 aktarımı `6946921285`. Yeni izin/grant/rol yoktur. Fiyat
  okuma, şirket, aktör, fresh company-wide permission, expected kaynak sürümü,
  idempotency ve gerçek Audit aynı sahip olunan Serializable işlemde korunur.
  Kaynak çatışması terminal replay olur; teknik/yetki/Audit hataları tam rollback olur.
- EF `20261117114517_AddPurchaseAdditionalCosts` üretim exit0. Up yalnız iki yeni
  tablo/index/FK/check DDL'idir. Down ilk işlemde **THROW51998** ile masraf veya komut
  geçmişi varken reddeder; boş geçmiş geri alınabilir. DB uygulaması yapılmadı.
- API, Purchasing.UnitTests ve Api.IntegrationTests kaynak derlemeleri **exit0,
  0 uyarı/0 hata**; `npx tsc -b` exit0; `git diff --check` exit0. Unit/model, HTTP,
  dört gerçek-SQL fact, strict decoder/transport ve DOM test kaynakları yazıldı.
  Runtime Unit/SQL/HTTP/Vitest/browser/CI **NOT_RUN**; bağımsız K3 imzası yoktur.
- Module store/service DI hazırdır. Merkezi host mount ihtiyacı
  `MapPurchaseAdditionalCostEndpointsV1()`; Purchasing shell owner **1.13 / toplam28**
  bildirimdir: read + OrdersRevise çocuk kontrolü. Ekran ve mal/hizmet siparişinden
  bağlantılar hazırdır; merkezi Program/nav/OpenAPI/pin/envanter kök owner'dadır.
- **PARTIAL kalan:** Parçalı fatura eşleştirme insan cevabı `6947000365` ile
  kesinleşti; aynı caride toplam masrafı aşmadan farklı faturalara pay bağlanacaktır.
  Bu checkpoint kayıt/okuma kaynağıdır; gerçek parasal invoice binding ledger,
  aynı işlem toplam sınırı, fatura consumer/ekran ve test kaynakları takip işidir.
- S7 takip `7e3aad912` kaynakları da bu derlemelerde yer aldı; önceki BUILD/TSC
  NOT_RUN notu bu kaynak başında derleme0 ile tamamlandı. ALS-D105 yeni kesin yetki
  yanıtı `6947126872`: Satış `sales.orders.approve`, Alış `purchasing.orders.approve`.
  Bu checkpoint vergi yönetimi/override'ını henüz açmaz; ayrı kaynak takibi sürer.
