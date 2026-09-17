# Göymen ERP — Bütünleşik Geliştirme ve Uygulama Raporu v2.0

**Tarih:** 17 Eylül 2026. **Business:** mevcut CHATGPT-BUSINESS. **Uygulama koordinasyonu:** mevcut GOYMEN-KOORDINATOR.

Bu belge, kullanıcının istediği yeniden analiz edilmiş işlevsel/teknik yol haritası ve uygulayıcı rehberidir. Kullanıcı raporun GitHub'a iletilmesini ve hazır işlerin Koordinatör tarafından başlatılmasını istedi. Teknik örnekler üretim kodu veya tamamlanmış kabul kanıtı değildir. K-1(a) ve ayrıntılı maliyet ertelemesi doğrudan kullanıcı kararıdır; diğer açık politikalar bu belgeyle seçilmez.

Son kullanıcı kararı > geçerli tarihli kararlar/sicil > Master > ilgili kabul/spec. Rapor mevcut master, beş-modül matrisi, kabul sicili veya PR sahipliğinin yerine geçen ikinci otorite değildir. T00–T34 yalnız rapor içi plan satırlarıdır; uygulamada mevcut görev/kabul kimliklerine eşlenir. Eski sohbet raporlarının teknik yol haritası güncellenir; eski dosyalar silinmez.

## 1. Yönetim sonucu ve yeni kararlar

Mevcut modüler monolit, .NET 10/C#, React/TypeScript ve SQL Server/EF Core uygulaması korunur. Yeni frontend, ORM, mikroservis veya AI orkestratörü kurulmaz. SQL Server'ın bugün kullanılması, eski PoC/production kararlarının hepsinin kapanması demek değildir.

Ana teslim: **Ürün, Cari, Organizasyon, Sipariş/Satış, Finans, Stok/Etiket/Tahsis, Satınalma, Üretim, Sevkiyat, Genel Muhasebe**; gerekli fiyat/belge/kalite/onay/rapor/AI altyapısıyla gerçek kullanıcı işlerini tamamlar. Çekirdek modüller aynı hedef sürümde; geliştirme bağımlılık sırasıyla. İlk dar ekran tam modül değildir. İK, Bakım, CAPA, Sabit Kıymet, CRM, ileri Proje/Maliyet, genel Hukuk/Uyum ve İSG tam uygulamaları ertelenir; kaynak bağları korunur.

### K-1(a): karar verildi, uygulama ve kabul ayrı

[Asıl soru](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5683890499) için **(a)**: Ürün'ün ayrı, dar operasyonel seçim okuma izni **ve** tüketen operasyonun kendi izni gerekir. Varsayılan kapalı; otomatik kullanıcı/rol ataması yok. İzin adı ve eşleme mevcut Product + Identity/merkezi sahiplerinde. Dar izin genel Product yönetimi, geniş master listesi, fiyat/maliyet, audit veya geçmiş yetkisi değildir. UUID/kod tahmini de aynı sınıra tabidir.

Yetki her okumada ve asıl komutta yeniden doğrulanır. Yetki yokken varlık/alan hatası/uygunluk açıklanmaz. Aktör/şirket/izin sürümü değişirse eski cevap yeni bağlama taşınmaz. Bu karar aile, U-08 birim, gerçek para/profil ve production grant seçimi değildir. [Karar devri](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5719251344); [tarihli kayıt](../decisions/SINAN_KARARLARI_2026-09-17_K1_VE_MALIYET.md).

### Ayrıntılı maliyet: ana sistemden sonra

**Şimdi:** gerçek miktar/ölçüm kaynağı, lot, alış/fatura tutarı ve parası, onaylı kur referansı, gerçek sarf/süre, WIP ve çıktı ilişkisi. Fiyatlandırma, fatura/açık kalem, tahsilat/ödeme, temel Finans ve Muhasebe ertelenmez.

**Sonra:** enerji/işçilik/genel gider dağıtımı, standart–gerçek sapma, ortak/yan ürün/hurda maliyeti, gelişmiş kârlılık, stok değerleme ve tam üretim maliyeti motoru. Hareketli ortalama/FIFO seçilmez; önceki teknik öneri kesin karar değildir.

Bilinmeyen maliyet **null + değerlenmedi/bekliyor** olur, sıfır veya hesaplanmış kâr olmaz. Değerleme gerektiren fiş/rapor ayrı bekler; sahte tam mali kapanış yok. Ana operasyon kabulü ile tam değerlenmiş mali dönem kapanışı ayrıdır; ertelenen kapsam görünür onaylı istisnadır.

## 2. Yeniden incelemede yapılan düzeltmeler

| Önceki risk/eksik | v2.0 düzeltmesi |
|---|---|
| K-1 sürekli cevapsızdı | (a) kaydedildi; #265'te mevcut yazara dar düzeltme şimdi verilebilir. |
| T25 kritik yola ayrıntılı maliyeti bağlıyordu | Ham veri mevcut paketlere; ayrıntılı T25 ana sistem sonrasına. |
| Beş yeni modül arasında fiyat/fatura/ödeme araçları kaybolabiliyordu | T28–T34 ile fiyat, belge, tam Finance, kalite, onay, işletim bağları. |
| Sözleşme hazır = kullanıcı işi hazır sanılabiliyordu | Tasarlandı/uygulandı/doğrulandı/bağımlılık bekliyor/ertelendi ayrımı. |
| Tek durum alanı ticari/fiziksel/finansal durumu karıştırıyordu | Onay, ticaret, gerçekleşme, belge ve kapanış eksenleri ayrı. |
| Yeni tahsis satırına rowversion yeterli sanılabiliyordu | Ortak kaynak VE ihtiyaç bakiyesi atomik korunur; insert yarışı test edilir. |
| Her entegrasyon event ile çözülecek sanılabiliyordu | Stok/tahsis/sevk aynı kritik sınır; dış etkiler outbox/inbox. |
| Bütün örnek kod ikinci ERP'ye dönüşebilirdi | Çalışan kaynak kataloğu + küçük sözde kod + sayısal kabul; kör kopyalama yok. |
| RAM boşalınca bütün işler açılır sanılabiliyordu | RAM, CI, ortam, iş kararı ve sahiplik ayrı engel. |
| Çok PR/oturum ilerleme sayılabiliyordu | Tam gerçek kullanıcı yolu ve açık bağımlılık yaşı izlenir. |

## 3. Kaynak fotoğrafı ve mevcut durum

Taban `main@eacbfe720aa426f4d6a71d5806603c461b30d9e2`. 17 Eylül son okuması: #261 `edbdf8d9`, #262 `dfe0e674`, #264 `1658165b`, #265 `b46d0ea0`, #266 `9a4f01a0`, #267 `c585096c`, #268 `716b120d`, #269 `b3f96b97`, #270 `5d9668f6`, #271 `9fb59ae8`, #272 `74d14e5c`, #273 `21e3f408`, #274 `f6f73d8f`. Kısaltmalar tarihsel fotoğraf; görevde tam head/base yeniden okunur.

Main'de Organization/Identity/Audit/Idempotency/Counterparty/Product çekirdeği; Sales/Finance/Settings sözleşmeleri ve frontend hazırlıkları var. Arayüz bulunması gerçek kayıt uygulaması değildir; Settings geçici sağlayıcı gerçek veri döndürmez. Açık PR'lardaki geliştirme main'e alınmış sayılmaz. Yeni beş modül bu raporla uygulanmış kabul edilmez.

[Koordinatör 5716360050](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5716360050) CI/Chromium/K-1 engellerini kaydetti. K-1 şimdi kararla açıldı; CI/Chromium ve diğer açık veri/politika şartları kalkmadı. Bellek temizliği veya SSMS kapanması kod/test/merge kanıtı değildir.

İnceleme: kritik kaynak dosyaları, PR/karar/kabul kayıtları ve bu konuşmadaki önceki iki rapor. Tüm testler yeniden koşulmadı; depo-geneli satır satır güvenlik denetimi iddiası yok. `GOYMEN_ERP_BUTUNLESIK_DETAYLI_SISTEM_RAPORU_v1_0.docx` ilgili bölümleri ayrıca okundu. Önceki v2.1 dosyasının konuşmada görünür alıntıları kullanıldı; özgün dosyasının tamamı bu tur yeniden elde edildi denmiyor.

## 4. Kod yönü: korunacak temel, gerçek boşluklar

Korunacaklar: mevcut stack/katmanlar; Party–şirket hesabı ve Product master–origin ayrımı; atomik audit/terminal; frozen command/replay; context fence; unavailable/empty ayrımı; gerçek SQL ve bağımsız inceleme.

Tamamlanacaklar: gerçek Settings/family/aktiflik kaynakları; Sales kalıcı komut ve taslak sonrası tam akış; Finance kaynak/kayıt/ters/dağıtım ve araçları; normal host DI+endpoint+capability/nav+veri+yetkili aktör+browser; ilk gerçek outbox tüketicisi. Uzun sayfaları ve saf tekrarları davranışı koruyan küçük işlerle sadeleştir; yeni genel form motoru veya toplu yeniden yazım yok.

### Claude'un önce okuyacağı kaynak kataloğu

| Kaynak (main; PR ayrıca belirtilir) | Kullanılacak fikir / sınır |
|---|---|
| `src/backend/GoymenERP.Api/ProductRuntime/ProductSqlCommandRunner.cs` | Tek commit, sahip katılımı, audit/terminal, belirsiz sonuç; Product global yetkisini Stock'a kopyalama. |
| `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/ProductCommandGateway.cs` | Frozen intent, anahtar+payload, güncel yetkili replay. |
| `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyDbContext.cs` | Şirket hesabı unique/FK/rowversion/write guard; yabancı tablolara serbest yazma değil. |
| `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Application/Contracts/V1/ISettingsPrerequisiteProvider.cs` | Sürüm+tarihli profil; eksik profile default yok. |
| `src/frontend/goymen-erp-web/src/product/commandAttempt.ts` | Aynı girişim, unknown recovery; her click'te yeni key yok. |
| `src/frontend/goymen-erp-web/src/shell/contextFence.ts` | Tek bağlam nesli ve geç cevabı reddetme. |
| `src/frontend/goymen-erp-web/src/finance/port.ts` | Ekran/transport ayrımı; yayımlanmamış URL icat etme. |
| `tests/GoymenERP.Api.IntegrationTests/ProductGlobalRuntimeSqlServerRelationalAcceptanceTests.cs` | Gerçek DB snapshot/yarış/replay; sentetik aktör production seed değil. |
| `tests/GoymenERP.ArchitectureTests/ModuleDependencyGuardTests.cs` | Sınırları doğrula; testi geçirmek için keyfî allowlist yok. |
| #265 `ProductSalesEligibilityService.cs` | Mevcut hata/family/unit kanalı korunur; K-1(b) yerine (a). |
| #274 `ProductCreatePage.tsx` | Referans beklerken seçimi koru, placeholder≠valid; #270 ile birleşik doğrulama gerekir. |

## 5. Ortak işletme modeli

Satış talebi/teklif→sipariş snapshot'ı→kaynak/tahsis→gerekiyorsa alış→kabul/etiket→üretim/kalite→yükleme/sevk→irsaliye/fatura→açık kalem/tahsilat→muhasebe. Hazır stok, stok için üretim, müşteri malı, hizmet ve dış fason uygun alt yolları kullanır.

Her olayın tek sahibi ve kaynak kimliği vardır. Grup görünümü hukuki şirketleri birleştirmez. İşlem şirketi, mal sahibi, gözeten taraf ve fiziksel konum ayrıdır. Gerçek kg, teorik kg, belge kg ve fiyat miktarı ayrı. Hizmetin fiziksel stok kg'si yok; sarf onaylı kendi birim profiliyle izlenir. Aynı grupta olma her şirketin malını kullanma yetkisi değildir.

Onay, ticari durum, fiziksel gerçekleşme, belge ve finansal kapanış ayrı. Plan onayı tüketim değildir; yükleme talimatı kesin sevk değildir; tahsis fiziksel hareket değildir; fatura dağıtımı yeni banka olayı değildir. Cari ekranı Finance/Inventory bilgilerini gösterir, ikinci bakiye motoru kurmaz.

## 6. Ortak platform: ihtiyaçla birlikte

| Yetenek | İlk gerçek kullanım | Koruma |
|---|---|---|
| Identity/Organization | Aktör/şirket/lokasyon/iş merkezi, K-1 | UI yetki değildir; default-deny. |
| Product/Settings | Birim/para/politika/formül/paket sürümü | Örnek değer gerçek master değil. |
| Audit/Idempotency | Kaynak, önce/sonra, correlation, girişim/sonuç | Unknown ve geçmiş korunur. |
| Onay/Revizyon | Sürüm ve etki önizlemesine bağlı onay | İçerik değişirse eski onay geçmez. |
| Belge/Ek | Dosya sürümü, kayıt ilişkisi, şablon/tarama | Hash erişim hakkı değildir; dosya ticari olay değil. |
| Olay/Worker | Outbox/inbox ve istisna/yeniden deneme | Çift teslim çift iş etkisi değildir. |
| Fiyat/Ticari belge | Birim/para/tarih/kademe/iskonto/vergi | Onaylı işlem snapshot'ı değişmez. |
| Rapor | Kaynak, filtre, veri tazeliği, drill-down | Gecikmiş read model transaction otoritesi değildir. |
| AI/OCR | Bağlamsal yardım/taslak, yerel-first | Ham DB, otomatik grant ve kesin işlem yok. |
| Import/Cihaz | Önizleme/hata/mükerrer/onay; barkod/yazıcı/kantar | Retry ve yeniden baskı ikinci hareket üretmez. |
| İşletim | Sağlık/log/restore/export | Secret, keyfî purge ve otomatik production yok. |

Settings ekranından Organization yönetmek ikinci şirket/depo masterı değildir. Ortak paket ilk tüketicisiyle; tüm ERP şemasını tek seferde üretmek yok.

## 7. Modül sahipliği ve veri tasarımı

Yeni klasör önerileri: backend `Modules/Inventory`, `Purchasing`, `Manufacturing`, `Logistics`, `Accounting`; frontend'de karşılıkları. Kesin adlar ilk dar implementation işinde mevcut sicil/guard ile eşlenir. Domain iş değişmezlerini, Application komut/sorgu/sözleşmeleri, Infrastructure kalıcılığı taşır. Merkezi API bunları bağlar; her modül kendi tablosuna yazar.

### 7.1 Inventory / Etiket / Tahsis

Önerilen kayıtlar: StockMovement(+Line), StockBalance, MaterialLot, MaterialLabel, MaterialGenealogyLink, StockCommitment, SupplyReference, Transfer, InventoryCount. Değişmez hareket fiziksel izdir; bakiye hızlı okuma ve yarış korumasıdır, hareketlerle uzlaştırılır.

Etiket ürün kartı değildir. Bölünme/birleşmede yeni fiziksel kimlik; yeniden baskıda aynı kimlik ve baskı audit'i. Heat/coil/MTC ve çoklu kaynak ilişkisi korunur. Farklı sahiplik izsiz birleşmez. Blokaj ayrı boyuttur; kullanıcı dili serbest stok/satışa tahsisli/üretime ayrılmış olarak anlaşılır kalır.

Tahsis ihtiyaç–kaynak miktar bağıdır. Etiket/havuz/onaylı alış/üretim gelecek arzı desteklenir. Gelecek arz fiziksel veya sevk edilebilir değildir. Gerçekleşmede beklenen kalan ile fiziksel yeni pay birlikte güncellenir. Havuz tahsisinin etikete bağlanması ikinci rezervasyon değildir. Yükleme alt payı tekrar düşülmez. Sonradan bloklanan tahsis kaybolmaz; gerçekleştirilebilirliği bloke olur. ReservedUntil ve otomatik serbest bırakma davranışı son kayıtlı kullanıcı kararına göre; eski kaynakta geçen öneri süre sessizce etkinleştirilmez.

Kaynak VE ihtiyaç kalanları aynı tutarlılık sınırında korunur. Sadece tahsis satırında rowversion insert yarışını çözmez. Ortak kaynak satırına koşullu sürümlü güncelleme/kilit; olmayan bucket için unique insert yarışı; birden fazla kaynağa deterministik kilit sırası düşünülür. Teknik seçim gerçek SQL'le kanıtlanır. Kısmi öneri açık kullanıcı kabulü olmadan tam istek yerine yazılmaz.

Transfer kaynak çıkış/yolda/hedef kabul; eksik hedef kabul fark kuyruğu. Sayım referans anı ve aradaki hareketler korunur. İade, sayım farkı ve ters kayıt kaynakları silmez. Negatif stok veya sınırsız manuel kaynak varsayımı yok.

### 7.2 Purchasing / satınalma

PurchaseRequest, SupplierOffer, PurchaseOrder(+Line), DeliverySchedule, ReceiptMatch, InvoiceMatch. Talep→teklif→sipariş→termin→mal/hizmet kabulü→fatura kontrolü→Finance. Teklif zorunluluğu alım politikasına göre; rapor yeni genel onay zorunluluğu seçmez.

Genel alım teknik satırlara bölünebilir; genel kalan+teknik dilimler ilk taahhüdü aşmaz. Onaylı alış gelecek arz; fiilî mal Inventory'de kabul edilir. Hizmet stok yaratmaz; sarf onaylı birimiyle. Siparişsiz geliş gözlem/karantina olarak kaydedilebilir, otomatik serbest stok veya borç değildir. Alış modülü ikinci tahsis motoru kurmaz.

Üçlü kontrol sipariş–kabul–fatura paylarını ayrı izler. Fiyat, birim, miktar, para, vergi ve şirket farkları görünür. Mal iadesi fatura düzeltmesi değildir. Tedarikçi revizyonu eski siparişi sessiz değiştirmez; AI yalnız fark taslağı.

### 7.3 Manufacturing / üretim

RecipeVersion, RouteVersion, ProductionPlan, ProductionOrder, InputIssue, ProductionRun, OutputDeclaration, WipPosition, DowntimeEvent. Siparişten/hedef miktardan/hammaddeden plan; stok için üretim; uygun rota. Reçete, plan, emir ve gerçekleşme ayrı. Plan onayı tüketim değildir; gerçek üretime giriş depo–WIP hareketidir. Kısmi üretim, kalan hedef, kullanılmayan girdi iadesi ve yeniden işleme desteklenir.

| Motor | Zorunlu özel davranış |
|---|---|
| Dilme/Sarım | Fiziksel en/trim/bant dizilimi; eşzamanlı bant metrajı hat süresini çoğaltmaz; bobin kg/çap/makine sınırı; her çıktı ana ruloya bağlı. |
| Soğuk hadde | Giriş/çıkış kalınlığı, paso/metraj/kayıp; inceltme kg üretmez; her paso yeni alış değildir. |
| Boru/Profil | Doğrulanmış kg/metre önceliği; bant kaynak geçişi, kalıp/hat; gerçek adet/metre/kg/paket. |
| Boyama | Çelik ve boya/sarf dengesi ayrı; renk/kat/film/yüzey, boya lotu, temizlik/yeniden boyama; kaplama negatif fire değildir. |
| Boy kesme | Rulodan sac ile profil/borudan parça ayrı; kerf, baş/son trim, kullanılabilir artık/hurda; fiziksel boya sığmayan desen kabul edilmez. |

Hepsi sürümlü deterministik calculator kullanır: girdi/profil/formül/politika snapshot'ı, birim/rounding, yetkili override ve sonuç. LLM hesap motoru değildir. Eski iş emri yeni masterla yeniden yazılmaz; gerçekleşmede kaynak/yetki/durum tekrar doğrulanır.

Ortak kuyruk/kapasite ilk motorla başlar: vardiya, kurulum, kalıp, bakım blokları, kesişen duruşlar; mola iki kez düşülmez. Miktar, termin, sıra etkisi birlikte. İyi ürün, ikinci kalite, kullanılabilir artık, hurda, proses kaybı ve WIP ayrı. Açıklanamayan fark otomatik fire olmaz. Ayrıntılı maliyet yok; ham miktar/süre/masraf kaynağı var.

### 7.4 Logistics / sevkiyat

LoadingInstruction, VehicleVisit, LoadScan, WeighingTicket, Shipment(+Line), DeliveryConfirmation. İhtiyaç→yükleme planı→araç/sürücü→gerçek etiket/adet/kg→tartım→kesin sevk→teslimat. Plaka/sürücü bilgileri yetki ve audit kapsamında.

Talimat, yüklenen, sevk edilen, teslim edilen paylar ayrı. Araç toplam tartısı ile pakete dağıtılan ölçüm niteliği belirtilir. Yanlış ürün/sahip/tahsis, kalite bloku veya fazla miktar engellenir. Farklı şirket/müşterilerin aynı araçta olması belge ve transaction'larını birleştirmez.

Kesin sevkte stok çıkışı+tahsis tüketimi+sevk payı birlikte. Belge/fatura ikinci çıkış yapmaz. Dış depodan müşteriye gerçek konumdan çıkış; merkezde hayalî hareket yok. Eksik/hasarlı/ret/iade kaynak sevke bağlı. Ticari fazla teslim, kantar ve üretim toleransları farklı; kısmi teslimde her seferinde yeniden tolerans verilmez.

### 7.5 Finance / tam kapsam

Kaynak tahsilat/ödeme, açık kalem/dağıtım, banka/kasa, ödeme planı ve araçları, çek/senet, kayıtlı kredi kartı/DBS akışları, risk/limit/teminat, nakit takip ve raporlar tek banka taslağına daralmaz. Kaynak onayı, dağıtım, araç durumu ve banka eşleşmesi ayrı. Cari gösterir; ikinci motor kurmaz.

Fatura/taksit açık kalem; sipariş/irsaliye ayrı risk/taahhüt. Çoklu/kısmi dağıtım ikinci para hareketi değildir. Çek nominali nakit tahsil değildir; karşılıksız/iade/ciro/ödeme ile nominal, risk ve gerçek banka etkisi ayrı. Bilinmeyen ödeyen tahminle cariye atanmaz. İlk aşama manuel/dosya; otomatik banka/POS yeni kapsamı yok.

Kur yönü/tarihi/kaynağı ve kaynak-hedef para miktarları açık. Sipariş iptalindeki avans gibi açık politika genel onaydan türetilmez. İleri nakit tamponu/13 haftalık model, çek kırdırarak USD/kâr koruma ve yaklaşık 15 günlük üretim çevrimi optimizasyonu ertelemede kalır.

### 7.6 Accounting / temel muhasebe

Account, AccountingPeriod, PostingRuleVersion, Journal(+Line), SourcePosting, ReconciliationItem. Şirket hesap planı, kaynak-bağlı taslak/kesin/manuel fiş, ters kayıt, defter/mizan, dönem ve dış kayıt mutabakatı. Fiş ikinci banka veya stok olayı yaratmaz.

Denge, şirket, boyut, kaynak kimliği, iş/evrak/muhasebe tarihleri, para/kur ve kural sürümü korunur. Kaynak/iş amacı benzersizliği mesaj kimliğinden ayrıca korunur. Kural sürümü değişince ikinci asıl fiş yok; düzeltme yeni ve bağlı. Eksik eşleme istisna kuyruğu, rastgele hesap değil.

Resmî yasal defter/e-belge dış sistem sınırı korunur. İlk sürüm canlı Mikro/banka/e-belge entegrasyonu gerektirmez; manuel/dosya resmî referans mutabakatı olabilir. Değerleme bekleyen fiş/rapor eksik kapsamını gösterir; operasyonel tamamlık ile tam değerlenmiş mali kapanış ayrı.

## 8. Fiyat, ticari belge ve onay boşlukları

Fiyat motoru şirket/kapsam, ürün/cari grubu/özel cari, para, fiyat birimi, etkin tarih, miktar kademesi ve sıralı hareketleri kullanır. Teklif/sipariş onayında fiyat/iskonto/vergi/kur sürümleri dondurulur. Adet fiyatı gerçek kg değişince yeniden hesaplanmaz; hizmet ücreti malın kg fiyatına gizlice karışmaz.

Brüt/net, iskonto, vade/fiyat farkı, masraf/nakliye ve vergi ayrı. Vergi oranı/istisna bu raporla seçilmez; onaylı Settings kaydı ve hukuki doğrulama ilgili sahibinde. Kaynakta mevcut minimum fiyat/iskonto uygulanır. Güvenilir maliyet olmadan otomatik marj kuralı eklenmez; zorunlu ama hesaplanamayan kontrol görünür, yetkisizce atlanamaz.

İrsaliye/fatura kaydı dosya kasası değildir. Dosya hash'i tek başına ticari mükerrerlik anahtarı olamaz. Şirket, yön, düzenleyici, belge türü/dış kimlik ve kaynak gerçekleşme payları kontrol edilir. Yeni PDF ikinci borç/alacak değildir; aynı numaranın farklı düzenleyicilerdeki anlamı kaynak kuralıyla. Kısmi/kümülatif faturalamada pay iki kez kullanılamaz.

Revizyon öncesi etki listesi: tahsis/yükleme, sevk/kabul, üretim, fatura, avans/dağıtım, muhasebe. Kesin alt kaydın geçmişi silinmez. Kısmi ters miktar/tutar kaynak kalanıyla sınırlı. Onay işlem sürümüne bağlı; değişmiş içeriğe eski onay yok. Açık karar yalnız etkilenen yolu bekletir.

## 9. Kalite, fason, belge ve AI

Temel kalite ilk kabul ve üretimle: kontrol planı, ölçüm/tolerans kaynağı, MTC, karantina, serbestlik, ikinci kalite, ret/yeniden işleme. CAPA ertelenebilir, temel kalite/soy ağacı ertelenmez. Blok tahsisi silmez, kullanılabilirliği değiştirir; kaynaktan etkilenen müşteriye izlenir.

Müşteri malı sahibinde kalır. Fason gönderim/dönüş/hizmet kabulü/mal bedeli ayrı. Karma kampanyada ölçülmeyen kesin karışım yüzdesi üretilmez. Sahiplik devri ayrı izinli belge. Stok için üretim satışa zorunlu bağlanmaz.

Belge yüklemede tarama/güvenlik durumu ve bağlantı yetkisi; sürümlü şirket şablonu; baskı olayı fiziksel hareketten ayrı. Cevabı kaybolan baskı işi, yeni stok yaratmadan kurtarılır. Gönderildi ve teslim edildi ayrı. Import batch/row kimliği, eşleme/önizleme/hata/mükerrer/onay/kontrollü yazma.

AI her ilgili sayfada bağlamsal yardım ve taslak; yerel-first. Dosya/prompt içeriği güvenilir sistem talimatı değildir. Yalnız yetkili şemalı araç, parametre önizlemesi ve kullanıcı onayı; ham DB ve otomatik yetki yok. Kesinti normal ERP'yi durdurmaz. Bulut veri aktarımı/model/credential bu raporla açılmaz.

## 10. Transaction, event ve kurtarma

**Birlikte kesinleşme:** stok çıkışı+tahsis tüketimi+sevk gibi invariants tek kritik işlem. Aynı ilişkisel DB'de modül context'leri aynı DbConnection/DbTransaction'a kontrollü sahip katılımıyla bağlanabilir [T1]. Başkasının tablosuna ham yazma değil. EF retry stratejisiyle transaction uyumu ayrıca doğrulanır; yazıcı/dış ağ çağrısında DB kilidi tutulmaz.

**Ayrı şirket/DB:** tek yerel transaction varmış gibi davranma. İleride şirket ayrıştırılabilirliği için sabit kimlik/şirket export'u; ayrı DB/dış sistemde idempotent olay ve görünür telafi gerekir. Şirketler arası mal/para politikasını teknik ekip seçmez.

**Outbox:** kaynak ve olay aynı commit. Olay kimlik/şirket/kaynak/aggregate sürümü-sırası/correlation/causation/tarih/schemaVersion taşır; büyük dosya/secret gömülmez. Dispatcher lease/retry/backoff/kalıcı hata kuyruğu.

**Inbox:** dedup marker+tüketici etkisi aynı commit; unique constraint eşzamanlı çift mesajı korur. Mesaj tekilliği ve iş kaynağı tekilliği ayrı. Aggregate version ile gerekirse sıralama/eski mesaj denetimi. Retention bilinmeden dedup/olay geçmişi silinmez.

**Belirsiz commit:** catch→retry yeterli değil. Commit başladıktan sonra cevap kaybı veya rollback teyitsizliği UNKNOWN; aynı key/intent ve güncel authority ile sonuç aranır. Yeni key ile aynı işi yaratma, tüm hataları retry veya ayrı transaction'da sahte success yok.

## 11. API sözleşmesi rehberi

Yeni uçlar öneridir, mevcut yayımlanmış API değildir. Kesin path/DTO/permission ilk dar implementation ve OpenAPI'de kilitlenir; önceki sözleşme geriye dönük bozulmaz.

| Alan | Önerilen uç aileleri | Kapanış |
|---|---|---|
| Inventory | `/api/v1/inventory/positions`, `/labels/{id}`, `/labels/{id}/genealogy`, `/receipts`, `/receipts/{id}/post` | Gerçek kabul/etiket/stok |
| Tahsis | `/api/v1/inventory/allocations`, `/allocations/{id}/release` | Kaynak ve ihtiyaç korunur |
| Purchasing | `/api/v1/purchasing/orders`, `/orders/{id}/approve`, `/invoice-matches` | Taahhüt/kabul/fatura payları |
| Manufacturing | `/api/v1/manufacturing/plan-previews`, `/orders`, `/orders/{id}/input-issues`, `/orders/{id}/realizations` | Önizleme tüketim değildir |
| Logistics | `/api/v1/logistics/loading-instructions`, `/loadings/{id}/scans`, `/shipments/{id}/dispatch` | Tek çıkış/teslim |
| Accounting | `/api/v1/accounting/journal-drafts`, `/journals/{id}/post`, `/journals/{id}/reverse`, `/trial-balance` | Kaynak-bağlı dengeli fiş |

GET yalnız okuma; önerilen oluşturma/onay/gerçekleşme uçları command olarak POST. Aktör/şirket sunucuda; origin başlığı grant değil. expectedVersion kayıpsız; para/miktar kayıpsız decimal metni ve ayrı birim/para kimliği. Örnek scale master değil; iş tarihi UTC olay zamanından ayrı.

400 biçim/alan, 401 kimlik, 403 yetki, 404 kapsamda bulunamama, 409 sürüm/anahtar/yarış çakışması, 422 uygulanamaz iş kuralı, 503 sağlayıcı. Kesin mapping mevcut platform/owner'a uyar. 404/422 ile varlık sızdırma yok. 5xx/bozuk başarı unknown olabilir.

Cursor/sort/filter ve query key aktör/oturum+company+generation+kaynak/filtreyle bağlı. Liste continuation ağ hatası yetki ihlaliyle aynı değil. Önceki endpoint retry/status alanı yeni API'ye varsayımla taşınmaz. Bounded page/sort; gerçek SLO ölçüm/onayla.

## 12. Önceden kod yazmak yerine uygulayıcı reçeteleri

Bütün eksikleri kapsayan ikinci bir örnek ERP yazılmayacak. Eksik aile/izin/endpoint/şema kararlarıyla binlerce satır hazırlamak daha fazla inceleme ve uyarlama yükü yaratır. Hız için çalışan kaynak kataloğu, somut görev ve kabul örnekleri daha değerlidir. Aşağıdaki dar sözde kodlar mimari niyeti gösterir; derlenmiş/test edilmiş C#/TS veya mevcut metot iddiası değildir. `Require`, `Reserve` gibi adlar uygulayıcı tarafından gerçek owner sözleşmesine uyarlanır. Production kodu yalnız Koordinatör sahiplik ayırınca normal kod+test PR'ında yazılır.

### R1 — K-1 yetkisi (T00)

```text
Consumer kendi işlem iznini kontrol eder
Product sunucudan actor/company ve dar selection-read yetkisini alır
Yetki yoksa ürün tablosunu okumadan reddeder
Scope = actor + company + authorityRevision
Stored Product okunur; await sonucu/hatası verilmeden scope yeniden doğrulanır
Lifecycle, role, expectedVersion ve kabul edilmiş family/unit kontrol edilir
Profil/family yoksa olumlu seçim üretilmez
Sonuç yalnız aynı yetkili scope altında ve gereken dar alanlarla döner
Asıl iş komutu kendi tutarlılık sınırında yeniden doğrular
```

Kabul: consumer=false/product=false → red; true/false → red ve Product read=0; false/true → consumer red; true/true fakat profil unavailable → no-success; true/true ve kayıtlı koşullar doğru → yalnız seçim sonucu. Await sırasında grant/actor/company değişimi → veri ifşası yok. Dar izin genel Product kartı/edit yetkisi değildir.

### R2 — atomik tahsis (T11)

```text
AuthorizeAndBind → FreezeIntent → ResolveSameKey
Begin owner-coordinated transaction
  source + demand kilit/sürüm sırası deterministik
  güncel sahiplik, kalite, kaynak ve ihtiyaç kalanını doğrula
  quantity > 0; uygun unit; quantity <= sourceFree ve demandRemaining
  ortak balance koşullu güncellenir; etkilenen satır 1 olmalı
  commitment + audit + terminal (+ gerekiyorsa outbox) birlikte yazılır
  final authority/policy kontrolü → tek commit
Unique/sürüm yarışında bütün girişim rollback; yalnız teyitli güvenli durumda yeniden çöz
Commit belirsizse aynı key/intent UNKNOWN kalır
```

Yalnız `SELECT SUM` ardından insert fazla tahsisi önlemez. Unique constraint miktar toplamının yerini tutmaz. Ortak kaynak sayacı/kilit veya eşdeğer kanıtlı tasarım gerekir. EF concurrency update/delete ile insert unique yarışını aynı exception saymaz [T2].

### R3 — aynı girişim/ekran (T03/T05/T07/T12)

```text
İlk geçerli submit payload+origin+actor/session snapshot'ı ve tek key üretir
Aynı içerik çift click/retry → aynı key
Timeout/5xx/bozuk başarı → UNKNOWN, otomatik yeni kayıt/auto-retry yok
Kullanıcı retry → aynı snapshot/key, sunucu güncel yetkiyle sonucu çözer
Replay yetki reddi önceki UNKNOWN işlemin rollback kanıtı değildir
Başarı doğru bağlamda gerçekten gösterilince işlem kullanıcı açısından kapanır
Eski unresolved girişim yeni aktör/şirkete taşınmaz
```

### R4 — outbox/inbox ve fiş tekilliği (T14/T23)

```text
Producer transaction: source state + event(messageId, sourceId, operation, version)
Dispatcher lease ile gönderir; ack kaybında aynı mesaj yeniden gelebilir
Consumer transaction:
  unique(company, consumer, messageId) inbox marker'ı alınır
  duplicate → yeni iş etkisi yok
  ayrıca sourceId+postingPurpose asıl fiş tekilliği kontrol edilir
  consumer etkisi + inbox sonucu birlikte commit
Yeni postingRuleVersion aynı kaynağa ikinci asıl fiş yaratmaz
Ters/düzeltme ayrı kimlik ve original source/fiş bağı taşır
```

### R5 — referans bekleme/eski cevap (T02/T06)

```text
cacheKey = actor/session + company + generation + resource + filters
placeholder yalnız aynı bağlamda önceki seçimi gösterebilir
selectionValid yalnız exact settled owner response ile oluşur
Geç A cevabı B state/cache/accessibility ağacına yazılmaz
unavailable/malformed != empty; listed != eligible
İlk veya tek aday otomatik default ya da write yetkisi değildir
```

### R6 — maliyet ertelenirken veri koruma (T15–T19/T24)

```text
OperationalFact = sourceEventId, company, lot/input/output, quantity+unit,
  actual/estimated niteliği, zaman, recipe/formulaVersion,
  sourceInvoiceRef, sourceAmount+currency (bilinmiyorsa null)
CostResult = NOT_CALCULATED / WAITING_POLICY / KNOWN
amount yalnız KNOWN ise anlamlı; bilinmeyen sonuç sıfır değildir
Source fact korunur; ileri maliyet ayrı sürümlü türev kayıttır
Maliyet hesaplanmadı diye gerçek para/malzeme olayı kaybolmaz
```

## 13. Sayısal kabul örnekleri

Sayısal girdiler sentetiktir; fiyat/tolerans/yoğunluk/ilk aile/production seed kararı değildir. Uygulamada gerçek CASE/AT kimlikleriyle eşlenir.

| Örnek | Beklenen |
|---|---|
| 100 ton alışın 40 tonu kabul | 40 fiziksel+60 gelecek; 140 olmaz. 60 tahsisin toplamı aynı, kaynak payları aktarılır. |
| 10 ton stok, 6 tahsis, bunun içinde 4 yükleme | Serbest 4; yükleme ikinci kez düşmez. |
| 10 ton kaynağa eşzamanlı iki 6 ton talep | En fazla biri tam kabul; diğeri açık çakışma/öneri; toplam 12 olmaz. |
| 1.000 ton genel alımdan 75 teknik kırılım | 925 genel+75 teknik; aynı pay tekrar kırılmaz. |
| 100 adet × 250 TL/adet, %4 iskonto | Vergi öncesi 24.000 TL; gerçek kg değişince bedel değişmez. Vergi oranı seçilmez. |
| 500.000 TL tahsilat, 200.000+150.000 dağıtım | Banka bir kez 500.000; dağıtılmamış 150.000. |
| Aynı ticari belge farklı PDF ile tekrar | Dosya farklı olabilir; ikinci borç/alacak yok. |
| Aynı fiş kaynağı farklı messageId ile gelir | İş kaynağı tekilliği ikinci asıl fişi engeller. |

Üretim örneği: 10.000 kg rulo → dilme 9.700 bant+200 hurda+100 kayıp → profil 9.500 iyi ürün+150 hurda+50 kayıp. 9.500+350+150=10.000. Mamulün 6.000 kg'si tahsis, bunun 4.000 kg'si yükleme; serbest 3.500. 3.800 kg sevk sonrası fiziksel 5.700, aktif tahsis 2.200; yüklenmiş ama sevk edilmemiş 200 ayrıca görünür. Fatura ikinci çıkış yapmaz. Bu oranlar maliyet dağıtım politikası değildir.

## 14. Görev planı — önceki T numaraları korunarak

T satırları tek büyük PR değil, kullanıcı sonucu hedefleridir. Hazır olması için gerçek owner/politika/API/veri ve öncül doğrulanır. Aynı kapsamın mevcut PR'ı varsa ikincisi açılmaz.

| Paket | Kapsam / sonuç | Öncül / sınır |
|---|---|---|
| T00 YENİ | K-1(a), #265 dar kod+yetki testleri | Karar verildi; Product/Identity sahibi; K3. Yerel geliştirme CI yokken yapılabilir. |
| T01 | CI/tarayıcı çalıştırılabilirliği, exact-head teslim sırası | Billing/sudo/production ayrı izin; gate bypass yok. |
| T02 | Gerçek birim/para/referans/politika sağlayıcıları | #273/mevcut portlar; ilk profil/aile tahmin edilmez. |
| T03 | Sales kalıcı taslak/liste/detay, satır/termin/onay/revizyon | #262, T00/T02, Product; T28/T33 ile ticari kapanış. |
| T04 | Finance banka kaynağı ve kalıcı taslak | #261/#266 ve gerçek owner; taslak para hareketi değildir. |
| T05 | Finance kesinleştirme/ters kayıt | T04, yetki/dönem/kur, audit/replay; kalan araçlar T29. |
| T06 | Product/Cari/Org tam kullanıcı dilimleri | Mevcut CASE/PR; geçmiş/aktiflik/birim/aile ve Cari tam kapsam daralmaz. |
| T07 | Inventory ilk gerçek giriş/hareket/bakiye/etiket görünümü | Product/Organization/owner; kontrollü açılış-kabul, sentetik seed production değil. |
| T08 | Alış talep/teklif/sipariş/termin/genel-teknik kırılım | Cari/Product/Settings, T28/T33. |
| T09 | Alıştan kısmi kabul→stok/etiket | T07–T08, kalan pay ve hareket birlikte. |
| T10 | Etiket bölme/birleştirme/soy ağacı/baskı | T09, gerçek paket; baskı T31. |
| T11 | Merkezi fiziksel/gelecek arz tahsisi | Gerçek stok+talep; source/demand yarışları ve alt yükleme payı. |
| T12 | Hazır stoktan yükleme/tartım/sevk/teslim | T11/T30/T31–T33; tek fiziksel çıkış. |
| T13 | Transfer/sayım/iade/blokaj/ters hareket | T07/09; sayım anı/arada hareket ve kısmi düzeltme. |
| T14 | Outbox/inbox ilk gerçek tüketici | İlk belge/muhasebe ihtiyacıyla; tüketicisiz genel platform değil. |
| T15 | Ortak plan/emir/WIP ve Dilme/Sarım | Inventory/T11/onaylı hesap; gerçek etiketli girdi/çıktı. |
| T16 | Soğuk hadde | T15 kabuğu, paso/ölçüm/kayıp; ileri maliyet yok. |
| T17 | Boru/Profil | T15, doğrulanmış bant/profil/birim; gerçek adet/kg/paket. |
| T18 | Boyama | T15, sarf/renk/kat; çelik/kaplama ayrımı. |
| T19 | Boy kesme | T15, onaylı kerf/desen/makine; artık/hurda. |
| T20 | Ortak kapasite/vardiya/aktif üretim kuyruğu | İlk motorla artımlı, bütün motorların sonuna ertelenmez. |
| T21 | Müşteri malı/dış fason/kısmi dönüş/doğrudan sevk | Inventory/hizmet/üretim/Logistics/sahiplik. |
| T22 | Hesap planı/eşleme/dönem | Erken başlar; gerçek hesap/politika; boş hazırlık ekranı değil. |
| T23 | Kaynaktan taslak/kesin/ters fiş | Finance/ticari belge+T14/T22; business dedup ve denge. |
| T24 | Fatura–açık kalem–para–muhasebe uzlaştırması | T05/T23/T29/T32; ölçülmemiş değerleme ayrı. |
| T25 REVİZE | Ayrıntılı değerleme/üretim maliyet motoru | **ERTELENDİ: ana sistemden sonra.** Ham veri T07–T24'te. |
| T26 | Defter/mizan/dönem/açılış/devir | T22–T24; deferred maliyet görünür; tam mali kapanış iddiası yok. |
| T27 | Tam kullanıcı kabulü ve iki geçiş/restore provası | Tüm çekirdek; gerekli insan kabulü; T25 görünür ertelenmiş istisna. |
| T28 YENİ | Fiyat/kademe/iskonto/kur/vergi snapshot'ı | T02; T03/T08 ticari onayının gerçek öncülü. |
| T29 YENİ | Finance araç/ödeme/dağıtım/risk/limit/teminat tamamlama | T04/T05; yalnız banka tahsilatıyla Finance kapanmaz. |
| T30 YENİ | Temel kalite/MTC/blokaj/serbestlik/yeniden işlem | T09 ve ilk motorla; ileri CAPA değil. |
| T31 YENİ | Belge, şablon/baskı, import/export, cihaz, AI/OCR | Mevcut ERP-000G/J/DocumentGeneration sahibi; ilk tüketiciyle. |
| T32 YENİ | İrsaliye/fatura kaydı ve pay eşlemesi | T08/09/12+T28/T31; canlı dış entegrasyon değil. |
| T33 YENİ | Sürümlü onay/revizyon/iptal-ters etki alanı | İlk kesin işlemle; avans/negatif kalan politikası ayrı. |
| T34 YENİ | Gözlemlenebilirlik/performans/işletim/şirket export | Her dilimle, en son log ekranı ekleme değil. |

İlk sıra: T00 ve T01; hazır mevcut beş-modül işleri. Sonra prerequisites ve T28/T33 ile T03/T08; T07–T12 stok zinciri. T14/T22/T31/T32 ilk gerçek tüketicileriyle paralel. Üretim T15–T21 ve kalite; Finance T29 ve Accounting T23/T24/T26; T27 birleşik kabul. Mevcut 'öncül main'e alınmadan bağımlı implementation yok' kuralı korunur; uzun unmerged feature zinciri izni değildir.

## 15. İlk uygulanabilir görev kartları

**T00 — şimdi:** #265 current head ve Product writer yeniden doğrulanır. Aynı yazara K-1(b) yerine (a), genel master grant ödünç almadan. Identity/shared katkı kendi sahibinde. R1 negatif/pozitif/await-revocation/varlık-sızmama kabulü. K-1 tekrar sorulmaz; family/profile/production açık. Sonuç kod+test+iki bağımsız K3 delta review+fresh CI; DI/veri yokken pozitif gerçek runtime kapanmış sayılmaz.

**T02 — gerçek prerequisite:** mevcut #273 portu yeniden yazılmaz. Sağlayıcı ve UI aynı kullanıcı hedefine. İlk/tek currency otomatik seçilmez; Code kimlik değildir. date/operation validity seçilmiş kimlikle owner resolver'da. Real profile yoksa tam eksik kimliği/kaynağı belirtilir; sentetik fixture testte. Company/actor değişimi ve malformed/empty/unavailable ayrı kabul.

**T09 — kısmi kabul:** onaylı alıştan gerçek etiketli stok. Company/owner/location/unit, alış kalan, duplicate belge payı/kalite kontrolü. Purchasing kabul payı, Inventory hareketi, audit/terminal birlikte. Normal, fazla kabul, çift submit, eşzamanlı kabul, yetki kaybı ve commit cevabı kaybı testleri. Host iş DB'si test kaynağı değil.

**T11 — tahsis:** gerçek satış/üretim ihtiyacına uygun mevcut/gelecek kaynak. Inventory tek yazar; Sales/Manufacturing kendi talep sözleşmesini sağlar. R2/§13 gerçek SQL'le. Release eski sürümle boş kaynak yaratamaz. Gizli/mock ekran kapanış değildir.

**T15 — ilk üretim:** gerçek ana rulo→plan→emir→girdi→kısmi/nihai dilme→çıktı etiketleri. Hesap/politika kaynağı kesinleşmeden formül aktive edilmez. StokERP yalnız doğrulanmış donör/golden; miktar dengesi/soy ağacı. Ham veri var; maliyet projesi yok. SQL/browser ayrı kanıt, tek ağır iş.

**T23 — ilk muhasebe:** onaylı kaynaktan fiş, yetkili kesinleştirme/ters. Inbox ve iş-kaynağı tekilliği ayrı; fiş kaynağı geriye dönük yaratmaz. Açık hesap/kur/dönem seçimi yok. Değerleme eksikse görünür istisna. Denge/duplicate/ters-replay/dönem-kapanma yarışı/restore sonrası tekrar testi.

Diğer T satırları açılırken aynı kısa kart: mevcut CASE/spec, tek kullanıcı sonucu, scope dışı, owner/worktree/full SHA, gerçek öncül, alan/null/unit/durum, read/write/dış etki, permission/replay/transaction, ayırt edici kabul, doğrulama komutları ve geri dönüş. Bu kart ilgili issue/PR'ın parçası; ayrı yeni manifesto değildir.

## 16. İnceleme ve doğrulama

Yerel hızlı test, gerçek SQL, API, component, browser ve fresh CI ayrı katmanlardır. Fixture/helper/birim testi production hazır veya gerçek kullanıcı kabulü değildir. Skip/environment-failure/not-run/failure ayrı; sahte yeşil yok. SQL'de kaynak yarışları, bütün rollback, belirsiz commit/retry, migration/invariant ve veri izi ölçülür.

Yazar bağımsız reviewer değildir. Mevcut K1/K2 ve K3 kapıları korunur; review sayısı artırılmaz/azaltılmaz. K3'te iki bağımsız exact-head, taze zorunlu CI, açık P0/P1 yok ve ilgili gerçek kanıt gerekir. Her işi büyük mutation kampanyasına çevirmek yerine değişen davranışı ayırt eden yeterli kanıt üretilir.

Main/head hareketinde kanıt geçerliliği yeniden değerlendirilir; eski sonucu final-head PASS sayma. İki ayrı dalın yeşili birleşik kabul değildir. Merkezi dosyalar tek yazarda; integration/rebase sonrası gerekli delta testi. Governance/CI/secret/production/human gate rapor onayıyla sessiz değişmez.

## 17. Beklemeyi azaltan iş düzeni

Engeller ayrı: kullanıcı kararı, birleşmemiş öncül, gerçek veri kaynağı, dosya sahibi, ortam, CI, bellek, uygulama hatası. Karar geldiğinde tekrar soru yok. Hazır bağımsız iş ilerler; gösteri için tekrar test/PR/değişiklik-yok mesajı yok.

CI yokken bağımsız kod/yerel test hazırlanabilir; merge iddiası yapılamaz. Bağımlı işin öncül-main şartı korunur. Yeni onaylı çekirdekte mevcut main'e gerçekten bağımsız, dar ve kaynakları yeterli bir iş varsa Koordinatör seçebilir; bütün mevcut modüllerin topyekûn bitmesi otomatik şart değildir. Eksik öncül sözleşmesinin kopyasını üretmek yasak kalır.

16 GB makinede tek ağır build/test/browser/docker-build yuvası; hafif okuma/kodlama/review paralel. Kuyrukta owner/exact head/kabul hedefi/bekleme nedeni; sürekli beş dakikalık poll yok. Worker sınırı işlevsel concurrency testlerini kapatmaz. Windows/WSL sayaçları ayrı; swap fiziksel available değil. Yeni eşik bu raporla seçilmez. Pasif kopyalar periyodik respawn edilmez; done/idle otomatik gereksiz değildir. Toplu stop, WSL shutdown veya model otomasyonu yok.

İlerleme: gerçek tamamlanan kullanıcı yolu, kaynak-sonuç uzlaşması, normal runtime kullanılabilirliği ve blocker yaşı. Ölçüm olmadan sabit yüzde veya kişi-gün sözü yok. Açık PR sayısı başarı değildir.

## 18. İşletim, geçiş ve gelecek modüller

SQL testleri açıkça izole ve silinebilir kaynakta; Windows `GoymenErp` korunur. Migration kendi sahibiyle ve gerçek veri etkisi/ileri-düzeltme planıyla. Dönem, gerçek grant ve production onayı insanda.

Geçiş sırası: organizasyon/yetki→ürün/cari/birim→lot/etiket/açılış stok→açık sipariş/tahsis→WIP/emir→sevk/kabul/fatura→açık kalem/araç/para→muhasebe/belge bağları. Eski hareket ve açılış bakiye iki kez sayılmaz. Placeholder şirket gerçek resmî kimlik olmadan resmî belge oluşturamaz.

En az iki tam geçiş/restore provası: DB+belge checksum, açık kaynak payları, dedup/girişim ve şirket export. RPO/RTO teknik varsayımı kabul edilmiş production SLO değildir. Yıl devrindeki açık kayıt kopyası yeni fiziksel/para etkisi üretmez. Retention/purge açık insan politikasıdır.

Bakım için ekipman/duruş, İK için aktör/vardiya, sabit kıymet için edinim kaynağı, CRM için cari/teklif, CAPA için ölçüm/uygunsuzluk, ileri maliyet için ham süre/tüketim, hukuk/İSG için kaynak olay kimliği korunur. Gelecek modül adına bugünkü zorunlu alanlar ve ekranlar gereksiz şişirilmez.

## 19. İç gözden geçirme sonucu ve başlangıç emri

Yeniden kontrolde beş mevcut modülün tam kapsamı korundu; fiyat/fatura/araç/kalite/onay/doküman açık bağlandı; maliyet ertelemesi temel Finance ve veri toplamayla uzlaştırıldı; K-1 karar/uygulama ayrıldı; beş üretim motoru korundu; gelecek arz/tahsis/yükleme çift etkisi ele alındı; transaction/event sınırları ayrıldı; örnekler gerçek master/kod diye sunulmadı.

Yayın doğrulaması uygulama kabulü değildir. Bu docs-only paket uygulama dosyası, grant, migration, CI/governance veya kabul statüsü değiştirmez. Kullanıcının doğrudan K-1 kararı mevcut sahibi tarafından uygulanabilir; teknik seçenekler dar PR'da review'a açıktır.

**Koordinatöre başlangıç emri:** K-1(a)'yı aynı #265 işine uygula; hazır mevcut işleri sürdür. Yeni çekirdek kapsamı mevcut kabul/backlog sahipliğine eşleştir, ilk bağımsız gerçek kullanıcı sonucunu seç. Yeni genel rapor yazdırma; yalnız ilgili bölüm ve çalışan referansları oku, somut eksiği Business'a ilet, kod+test+normal kullanıcı yolunu birlikte bitir. Daha doğru teknik düzeltme gerekiyorsa kanıtıyla uygula; raporu kör kopyalama. Tek Koordinatör, tek Business, modül başına tek aktif yazar. Son kullanıcı kararı dışındaki açık iş politikaları, güvenlik ve insan kapıları korunur.

## 20. Kaynaklar ve kanıt sınırı

- S1: `AGENTS.md`, `docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md`, `docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`. Tarihsel rol bölümünü son kullanıcı/gerçek sahiplik devriyle birlikte oku.
- S2: `docs/decisions/KARAR_SICILI.md`, `SINAN_KARARLARI_2026-08-31.md`, `ACIK_KARARLAR.md`, `ERTELENEN_KAPSAM.md`; `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`. Onaylı kapsam ve açık altlar.
- S3: Bu konuşmadaki işlevsel/teknik v1 raporları; yeni karar [5719251344](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5719251344).
- S4: `GOYMEN_ERP_BUTUNLESIK_DETAYLI_SISTEM_RAPORU_v1_0.docx` Library kaynağı §5.1, §6.3–6.4 ve ilişkili alanlar; fiyat/birim, belge, kaynak-dağıtım, erteleme. Repo'ya kopyalanmadı.
- S5: Mevcut matris [5653637829](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5653637829), teslim [5695973451](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5695973451), SQL sınırı [5696117248](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5696117248).
- S6: K-1 soru [5683890499](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5683890499); #265 `b46d0ea013f9778e0654ff8e0776e65874d94466`; #273 `21e3f4083af5c3e4c573e8a5bb5e886e93f6d0cf`; #274 `f6f73d8fcd8ae7e0ab9cb491c577dfad3e2158df`.
- S7: Sales para JIT [5715011050](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5715011050); bellek [5715604168](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5715604168); hazır iş [5716360050](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5716360050). Sonuncunun K-1 cevapsız kısmı S3 ile güncellendi; diğerleri ayrıca doğrulanır.
- T1: [Microsoft EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions): ilişkisel context'ler için ortak connection/transaction. Bu yeni modüllerin hepsinde uygulanmış olduğu iddiası değil.
- T2: [Microsoft EF Core concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency): update/delete sürüm çakışması ve insert unique yarışının ayrımı.

Kod referansları §4.3'te belirtilen main ve açık PR fotoğrafındadır. Yeni işte güncel kaynak ve full SHA ile yeniden bağlanırlar. İşlev tasarımı hiçbir zaman kod/SQL/browser kabulünün yerine geçmez.


## 21. 18 Eylül 2026 kullanıcı kabul eki — önceki öneri statülerini dar günceller

[Ö1–Ö8 kabul ve son aşama kararı](../decisions/SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md), bu raporun ilgili “karar bekliyor” cümlelerinden üstündür. Ö1–Ö7 kabul; Ö8 resmî belge kapalıyken ERP içi cari kaynak hareketleri/bakiye güncellemesi açık şartıyla kabul. `InvoiceCapable=false` iç finans kaydı için genel ret anahtarı değildir. Gerçek kişiler, ilk ürün verisi, saha sayıları ve kalan dar değerler ayrı kaynaktan tamamlanır; onaylanmış seçimler yeniden sorulmaz.

T25 ve diğer ileri kapsam **kalıcı olarak kapsamdan çıkarılmadı**: ayrıntılı maliyet, ileri nakit/13 hafta, çek kırdırma–USD/kâr koruma, 15 günlük çevrim ve sonraki tam İK/Bakım/CAPA/CRM/Sabit Kıymet modülleri son geliştirme aşamasında yapılır. Çekirdek ilk teslim, ileri kapsam son teslim evresidir; ham veri/temel kalite/Finance/Accounting şimdi korunur. Bu sıralama yeni formül/tolerans/grant veya bütün son evre ayrıntılarının bugünden onayı değildir. Eski metin ve T00–T34 numaraları değiştirilmeden korunmuştur.
