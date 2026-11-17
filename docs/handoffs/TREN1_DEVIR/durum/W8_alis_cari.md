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
