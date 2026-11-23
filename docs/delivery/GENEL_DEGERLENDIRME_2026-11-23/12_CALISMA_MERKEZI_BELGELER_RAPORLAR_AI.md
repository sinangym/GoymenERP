# Ana Sayfa, Çalışma Merkezi, Belgeler, Raporlar, Arama ve AI — durum ve yapılacaklar

> Kapsam: Ana Sayfa ve Çalışma Merkezi (Work), Belgeler (belge kasası ve PDF üretimi), Raporlar, genel arama,
> AI Merkezi ve bütün modüllerin kullandığı ortak ekran şablonu. HEAD `ddc6cba`, 23 Kasım 2026.
> Yalnız kod okunarak hazırlandı; derleme ve test koşulmadı. Bu belge karar veya onay değildir.

Seviye ölçeği: **0** yok, **1** kod yazılmış, **1-kısmi** yalnız bir bölümü var (arka uç var ekran yok, ya da akışın
bir parçası). "Test kaynağı" sütunu testin var olduğunu söyler, geçtiğini söylemez.

## 0. Genel kısa sonuç

- Bu beş alan, modülleri "tek sistem" yapan katmandır ve bugün en zayıf katmandır. Altyapı sağlam, kullanıcıya
  görünen kısım dardır.
- **Ana sayfa bir pano değildir.** Şirket adını, "Onaylarım" ve "Bildirimlerim" bağlantılarını gösterir; gösterge,
  uyarı ve hızlı işlem yoktur.
- **Çalışma Merkezi** bugün salt okunur bir onay listesidir. Sistemde sayılan 63 onay/karar işleminden yalnız 4 türü
  bu listeye bağlıdır. Görev, hatırlatma, vekâlet ve süre takibi yoktur. Bildirim yalnız iki olay için yazılmıştır
  ve olay dağıtıcısı kapalı olduğu için hiç üretilmez.
- **Belge kasası** en olgun parçadır: tek dosya, sürüm, çok kayda bağlama, virüs taraması, 14 kayıt türü. Ancak
  merkezi arşiv ekranı, OCR ve e-posta yoktur; PDF yalnız üç belge için üretilir (satış siparişi, sevk irsaliyesi,
  stok etiketi).
- **Rapor merkezi yoktur.** Üç satış raporu, bir üretim raporu (ekransız), mizan (menüde yok) ve cari ekstre vardır.
  Ekrandan Excel/CSV dışa aktarım hiçbir listede yoktur.
- **Arama** çalışır durumda ama dört kayıt türüyle sınırlıdır (cari, ürün, satış siparişi, stok etiketi).
- **AI kapalıdır ve açılsa da iş görmez:** hiçbir modül AI'a okuma aracı kaydetmemiştir, sağlayıcı sahtedir, dış
  bağlantı taşıyıcısı çıkış yapmaz. Her sayfada panel düğmesi vardır; panel "AI kullanılamıyor" der.
- Bu alanlardaki izinlerin hiçbiri (`documents.*`, `ai.panel.use`, `sales.reports.read`, `manufacturing.reports.read`)
  başlangıçtaki beş rolün hiçbirinde yoktur (`src/backend/GoymenERP.Api/Seed/identity-seed.json`).
- 22 Kasım tarihli Work/AI planı bazı işleri "kısmen bitti" sayar (12 yeni onay türü, not/görev, 2 AI aracı, karar
  dizini); bunların kodu bu depoda (main, `ddc6cba`) **yoktur**. Ayrıntı §8.

---

# A. Ana Sayfa ve Çalışma Merkezi

## A.1 Kısa sonuç

Kullanıcı bugün ana sayfadan onay listesine ve bildirim listesine gidebilir; onay listesinde dört türdeki bekleyen
kaydı görür ve kaydın kendi ekranına geçip kararı orada verir. Yapılamayanlar: rol bazlı pano, gösterge, uyarı,
görev atama, hatırlatma, vekâlet, süre aşımı ve yükseltme, kutudan karar verme. En önemli eksikler: (1) 59 onay
işlemi ortak kutuda görünmüyor, (2) olay dağıtıcısı kapalı olduğundan bildirimler ve modüller arası birçok bağ
çalışmıyor, (3) menüde "Çalışma Merkezi" başlığı yok, (4) görev/vekâlet/süre hiç yazılmamış.

## A.2 Hedef

- Ana Sayfa: rol ve şirkete göre satış, tedarik, stok, üretim, sevkiyat, finans ve risk özeti; göstergeden listeye
  ve kayda inme; bugün/hafta/dönem karşılaştırması; AI günlük özeti; favoriler (master §7.1, §27.1 D-01, D-03, D-04, D-07).
- Çalışma Merkezi: bana atanan, rol kuyruğu, onay bekleyen, bilgi bekleyen ve geciken işler; görevin kaynağı, süresi,
  sorumlusu, gözlemcisi; tek tıkla ilgili ekrana geçiş (master §7.1, D-02, D-05, D-06).
- Onay motoru: onay, ret, düzeltmeye iade, bilgi iste, koşullu onay; vekâlet, zaman aşımı ve yükseltme kayıtlı (master §12.3).
- Menüde 2. sıra "Çalışma Merkezi (görevler/onay)" (KLN-D1, `SINAN_KARARLARI_2026-11-03_MENU.md`).
- Tek iş kutusu: bütün onay türleri (hedef 64), önce 12 tür; süre sayacı, bir hatırlatma sonra yükseltme, vekâlet,
  görev ve kayda bağlı not, okundu bilgisi, yönetim ekranı, kutudan karar (AI-D23, AI-D31, YOL-D17, YOL-D18;
  `SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR.md`). Bildirimler "şimdi" ve "özet" olarak iki sınıf, yalnız uygulama içi (AI-D29).

## A.3 Bugün var olan

| Yetenek | Arka uç | Ekran | Menüden erişim | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Ana sayfa | — | Var | Var ("Ana sayfa") | `shell/*.test.tsx` | 1-kısmi | Yalnız şirket adı ve üç bağlantı; dosyanın kendi notu: "sahte gösterge yok". `src/frontend/goymen-erp-web/src/shell/ShellHome.tsx` |
| Rol bazlı pano, gösterge, uyarı, hızlı işlem | Yok | Yok | — | — | 0 | Sözleşmede `dashboard`, `summary`, `kpi` içeren yol 0 (`contracts/openapi/goymen-erp-v1.json`) |
| Onaylarım (liste + ayrıntı), 4 tür | Var | Var | Yalnız ana sayfa bağlantısı | Var | 1 | `GoymenERP.Api/Endpoints/WorkApprovalEndpoints.cs`, `WorkRuntime/*PendingApprovalAdapterV1.cs`, `work/ApprovalInboxPages.tsx`; test `NativeApprovalReadQueryV1Tests.cs` |
| Tek liste kuyruk (bütün kaynaklar, eskiden yeniye) | Var | Var | Onaylarım içinden | Var | 1 | `WorkQueueEndpoints.cs`, `work/WorkQueuePage.tsx`; test `NativeApprovalQueueReadQueryV1Tests.cs` |
| Kutudan karar verme | Yok | Yok | — | — | 0 | Kutu yalnız listeler; karar kaydın kendi ekranında verilir (`NativeApprovalQueueContractsV1.cs` açıklaması) |
| Kişisel bildirim listesi | Var | Var | Yalnız ana sayfa bağlantısı | Var | 1-kısmi | Yalnız 2 olay türü (tahsis çözüldü, tedarik yeniden açıldı): `Work.Domain/WorkNotificationsV1.cs`, `work/NotificationsPage.tsx`. Dağıtıcı kapalıyken bildirim oluşmaz |
| Bildirim saklama süresi ayarı | Var | Var | Ayarlar | Var | 1 | "Bildirim etkinlik süresi" (`settings.notification-retention.manage`) |
| Olay dağıtıcısı (modüller arası olay taşıyıcı) | Var | — | — | Var | 1, **kapalı** | `Work.Infrastructure/Outbox/WorkOutboxDispatcher.cs`; `Work:OutboxDispatcher:Enabled` hiçbir ayar dosyasında yok |
| Görev (atama, sorumlu, gözlemci) | Yok | Yok | — | — | 0 | `WorkTask`, `AssignedTo` araması 0. `WorkDesiredStateStoreV2` var ama Work dışında kullanan yok |
| Hatırlatma, süre aşımı, yükseltme | Yok | Yok | — | — | 0 | `Reminder`, `Hatırlat` arka uçta 0; `Escalat` yalnız ilgisiz 1 dosya |
| Vekâlet | Yok | Yok | — | — | 0 | `Vekalet`/`Vekâlet` 0; `Delegat` eşleşmeleri yalnız C# `delegate` |
| Favoriler, kaydedilmiş görünümler | Yok | Yok | — | — | 0 | `Favorite`, `Favori`, `SavedView` 0 |
| Üst çubukta bildirim zili, sayaç | Yok | Yok | — | — | 0 | `shell/AppShell.tsx` üst çubuğu: şirket seçici, arama, AI düğmesi, kullanıcı |

**Onay kutusunun kapsamı (sayım).** Sözleşmedeki yazma işlemlerinden yolunda `approv` veya `decision` geçenler
sayıldı (onay matrisi ve eşik ayarı hariç): **63 işlem**. Dağılım: Finans 18, Satış 12, Muhasebe 9, Satın Alma 6,
Stok 5, Lojistik 4, Üretim 2, Cari 2, Ayarlar 2, Kimlik 1, Organizasyon 1, Ürün 1. Ortak kutuya bağlı olanlar
(`Program.cs` satır 200–203) dört türdür:

| Bağlı tür | Sahip modül | Karar nerede verilir |
|---|---|---|
| Resmî fatura aktarımı onayı | Finans | Finans onay ekranı |
| Satış sipariş taslağı iç onayı | Satış | Satış sipariş ayrıntısı / Satış Onayları |
| Stok sayım farkı onayı | Stok | Etiket ayrıntı ekranı |
| Stok hurda/fire onayı | Stok | Etiket ayrıntı ekranı |

Kutuda olmayan ve günlük işi durdurabilen örnekler: iskonto ve taban fiyat istisnası, risk istisnası, irsaliye onayı,
plan üstü sevk, tartım farkı, üretim planı onayı, alış talebi ve siparişi onayı, mal kabul partisi, fatura farkı,
kredi limiti, elle banka ödemesi, masraf ödemesi, muhasebe fişi ve dönem onayları.

**Olay dağıtıcısı.** Kayıtlı 7 olay kaynağı (Organizasyon, Kimlik, Stok, Satın Alma, Finans, Satış, Lojistik) ve 13
tüketici vardır (`GoymenERP.Api/Shell/TrainCompletionHostComposition.cs`, `OrganizationRuntime/CompanySetupMessagingHost.cs`,
`LogisticsRuntime/LogisticsRiskGateHost.cs`, `SalesRuntime/SalesRiskExceptionUsageInboxHost.cs`). Dağıtıcı
varsayılan kapalıdır; açıldığında beş sayı (parti boyu, deneme sayısı, ilk ve en uzun bekleme, yoklama aralığı)
elle verilmezse uygulama başlamaz (`WorkOutboxDispatcherOptions.cs`). Açılması insan kapısıdır.

## A.4 Kullanılabilirlik engelleri

- **Menü:** "Çalışma Merkezi" grubu çizilmiyor (`shell/navigation.ts` `NAV_GROUPS` içinde yok). Onaylarım ve
  Bildirimlerim yalnız ana sayfadaki bağlantıdan açılır.
- **Yetki:** Kutunun kendi izni yoktur; kullanıcı ancak dört sahip modülün onay izninden birine sahipse görür. Bu
  izinler başlangıç rollerinde yoktur.
- **Kapalı anahtar:** Olay dağıtıcısı kapalı. Kapalıyken bildirim üretilmez; ayrıca muhasebeye fatura fişi, stok
  maliyet farkı, kredi hareket önerisi, şirket açılışı gibi 13 tüketicinin işi de akmaz (ayrıntı ilgili modül raporlarında).
- **İnsan kapısı:** Dağıtıcının açılması ve beş çalışma değerinin seçilmesi.

## A.5 Eksikler

**İsteklerine göre:** rol bazlı pano ve göstergeler; yönetim özeti ve operasyon nabzı; 59 onay işleminin kutuya
bağlanması; kutudan karar; görev ve kayda bağlı not; süre sayacı (4 saat / 2 iş günü), hatırlatma, yükseltme;
vekâlet; "sahipsiz onay" görünümü; iki sınıflı bildirim; Work yönetim ekranı; favoriler.

**Bir ERP'de olması gerekene göre:** kullanıcı izinliyken onayların beklememesi için vekâlet üç şirketli bir grupta
ilk günden gerekir. Üst çubukta okunmamış sayısı gösteren bildirim zili. Onay geçmişi (kim, ne zaman, hangi gerekçe)
için tek bakış ekranı. E-posta ile bildirim kararla kapalı başlıyor; saha ve depo kullanıcıları için sonradan gerekecek.

**Ekran:** Onay ve bildirim listelerinde yükleniyor, boş, yetkisiz, bulunamadı ve hata durumları ortak blokla
çiziliyor. Arama, süzgeç (yalnız kaynak süzgeci var), toplu seçim ve dışa aktarım yok. Bildirim listesi satır
kimliğini (sipariş satırı GUID'i) gösteriyor, okunur belge numarası göstermiyor (`work/NotificationsPage.tsx`).

## A.6 Bağlantılar

- **Alır:** Finans, Stok ve Satış'tan bekleyen onay listesi (okuma bağdaştırıcıları, `WorkRuntime/`). Satış'tan satır
  sorumlusu, Satın Alma'dan siparişi oluşturan/onaylayan, Kimlik'ten şirket üyeliği, Ayarlar'dan saklama süresi
  (`WorkRuntime/WorkNotificationHostV1.cs`). Stok ve Satın Alma olayları (dağıtıcı üzerinden).
- **Verir:** Bütün modüllere olay taşıma altyapısı (kaynak ve tüketici kaydı, `WorkModuleExtensions.cs`).
- **Kopuk halkalar:** Dağıtıcı kapalı olduğu için olayla kurulan bütün bağlar beklemede. 59 onay işlemi kutuya bağlı
  değil; her biri yalnız kendi modül ekranında görünür, yani onaycı hangi ekrana bakacağını bilmek zorunda.
  Çift kayıt riski görülmedi: kutu veri tutmaz, her okumada sahibine sorar.

---

# B. Belgeler ve dijital arşiv

## B.1 Kısa sonuç

Kullanıcı bugün dokuz ekranda bir kayda dosya ekleyebilir, sürümünü yükleyebilir, indirebilir ve var olan bir
dosyayı ikinci bir kayda kopyalamadan bağlayabilir. Şirket logosu ve anteti yüklenir; satış, stok ve lojistik için
belge şablonu hazırlanıp yayımlanır. Satış siparişi, sevk irsaliyesi ve stok etiketi PDF olarak üretilir.
Yapılamayanlar: bütün belgeleri tek ekranda arama/gezme (merkezi arşiv), belge okuma (OCR), e-posta, paylaşım,
teklif/fatura/alış siparişi/ekstre PDF'i. En önemli eksikler: merkezi arşiv ekranı, eksik PDF'ler, canlı ortamda
depolama ve tarama kurulumu, izinlerin hiçbir rolde olmaması.

## B.2 Hedef

- Bir dosya tek kez saklanır, çok kayda bağlanır; hash, tür, sürüm, yükleyen, şirket, bağlı kayıt; eski sürüm
  ezilmez (master §7.9, §25.1).
- Mantıksal arşiv görünümü: Şirket → Yıl → Modül → Belge Türü → Cari/Kayıt (§7.9, ekran B-06, B-07).
- Türkçe karakterli dosya adı standardı; gömülü font (§7.9).
- Dışarıdan yüklenen ve sistemin ürettiği bütün dosyalar kasaya girer; çıktı PDF, tablo XLSX (§25.7).
- Belge gelen kutusu, OCR, sınıflandırma, mükerrer kontrol, belgeden kayda akış (§8.4, ekranlar B-01…B-05).
- E-posta taslağı ve gönderim, paylaşım geçmişi (§7.9, B-09, B-10).
- Belgeler hem ayrı menü hem her kaydın içinde sekme (KLN-D4).
- Kendi sunucuda saklama, Windows Defender ile tarama, 10 yıl silme yok, en çok 20 MB
  (30 Eylül 2026 kararları, `src/backend/Modules/Documents/README.md`).

## B.3 Bugün var olan

| Yetenek | Arka uç | Ekran | Menüden erişim | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Kasa: yükleme, listeleme, indirme | Var | Var (panel) | Kayıt ekranı içinden | Var | 1 | `Endpoints/DocumentEndpoints.cs`, `Modules/Documents/.../DocumentService.cs`, `documents/DocumentsPanel.tsx`; `DocumentVaultSqlServerRelationalAcceptanceTests*.cs`, `tests/GoymenERP.Documents.UnitTests` |
| Sürüm (yeni sürüm, sürüm listesi, eski sürümü indirme) | Var | Var | Aynı | Var | 1 | `POST/GET /documents/{id}/versions` |
| Aynı dosyayı ikinci kayda bağlama | Var | Var | Aynı | Var | 1 | `POST /documents/{id}/links`, `GET /documents/link-candidates` |
| Mükerrer uyarısı (aynı içerik) | Var | Var | Aynı | Var | 1 | Uyarır, reddetmez |
| İçerik kuralı (PDF/PNG/JPEG; banka ekstresi türleri yalnız izin veren kayıtta) | Var | Var | — | Var | 1 | `Documents.Domain/DocumentContentPolicy.cs` |
| Virüs taraması | Var | — | — | Var | 1, kurulum ister | Canlıda yalnız Windows Defender; geliştirmede yalnız deneme imzası (`Scanning/`) |
| Depolama (sunucu klasörü, değişmez, hash ile) | Var | — | — | Var | 1, kurulum ister | `Storage/ServerDirectoryDocumentObjectStore.cs`; bulut yok |
| Silme / yeniden adlandırma | Yok (kasıtlı) | — | — | — | — | 10 yıl saklama kararı; arşivleme ve yasal bekletme yazılmadı |
| Merkezi arşiv ekranı (B-06), Belge 360 (B-07) | Yok | Yok | Yok | — | 0 | Listeleme yalnız `ownerKind`+`ownerId` ile; `/belgeler` rotası yok; menüde "Belgeler" grubu yok |
| Belge gelen kutusu, sınıflandırma (B-01, B-03, B-05) | Yok | Yok | — | — | 0 | — |
| OCR / belge okuma | Yok | Yok | — | — | 0 | `ocr`, `Tesseract` araması arka uç ve ön yüzde 0 |
| E-posta taslağı/gönderim, paylaşım | Yok | Yok | — | — | 0 | `Smtp`, `IEmailSender`, `MailKit` 0; `navigator.share` 0 |
| Şirket logosu ve anteti | Var | Var | Ayarlar | Var | 1 | `Endpoints/DocumentBrandingEndpoints.cs`, `documents/DocumentBrandingPage.tsx` |
| Belge şablonu hazırla/yayımla (Satış, Stok, Lojistik) | Var | Var | Ayarlar | Var | 1 | Üç modül; Satın Alma, Finans, Muhasebe şablonu yok |
| PDF üretim motoru (Türkçe font gömülü, dosya adı standardı) | Var | — | — | Var | 1 | `src/backend/DocumentGeneration/GoymenERP.DocumentGeneration.Pdf/`; `tests/GoymenERP.DocumentGeneration.Pdf.Tests` |

**Üretilen PDF'ler.** PDF motorunu kullanan üç uç vardır (`DocumentPdfGenerator`/`BarcodeLabelPdfGenerator` araması):

| Belge | Durum | Kanıt |
|---|---|---|
| Satış siparişi | Var (ekranda düğme) | `Endpoints/SalesEndpoints.OrderPdf.cs`, `sales/SalesOrderPdfAction.tsx` |
| Sevk irsaliyesi | Var (ekranda düğme) | `Endpoints/LogisticsDispatchPdfEndpoints.cs`, `logistics/DispatchNotePdfButton.tsx` |
| Stok etiketi (barkodlu) | Var (ekranda düğme) | `Endpoints/InventoryEndpoints.LabelPdf.cs`, `inventory/LabelPrintPanel.tsx` |
| Satış teklifi, satış faturası, alış siparişi, alış talebi, cari ekstre, tahsilat/ödeme makbuzu, üretim emri, kantar fişi, mizan | **Yok** | Sözleşmede `pdf` geçen yalnız 4 yol var, hepsi yukarıdaki üç belgeye ait |

Resmî satış faturasında PDF üretilmez; dışarıda kesilen faturanın PDF'i dosya olarak eklenir
(`finance/OfficialSalesInvoicesPage.tsx`).

**Belge eklenebilen kayıt türleri.** Arka uçta 14 tür tanımlı ve bağlıdır
(`Documents.Application/Contracts/DocumentOwnerLinkContractsV1.cs`, `DocumentsRuntime/DocumentsRuntimeExtensions.cs`):

| Kayıt türü | Ekranda "belge" paneli |
|---|---|
| Cari kart | Var: `cari/CariDetailPage.tsx`; ayrıca satış siparişi ve cari bakiye devri ekranlarında |
| Satış siparişi | Var: `sales/SalesOrderDocumentsWorkspace.tsx` |
| Alış siparişi | Var: `purchasing/PurchaseOrderDetailPage.tsx` |
| Alış faturası içe aktarımı | Var: `purchasing/InvoiceImportPanel.tsx` |
| Stok etiketi | Var: `inventory/InventoryLabelDetailPage.tsx` |
| Sevk irsaliyesi | Var: `logistics/DispatchKilogramsPanel.tsx` |
| Resmî satış faturası | Var: `finance/OfficialSalesInvoicesPage.tsx` |
| Döviz bozdurma | Var: `finance/CashExchangeBookValuePage.tsx` |
| Şirket logosu/anteti | Var: `documents/DocumentBrandingPage.tsx` |
| Üretim planı, üretim emri | **Yok** (yalnız arka uç) |
| Elle tahsilat, elle banka ödemesi, müşteri çeki/senedi | **Yok** (yalnız arka uç) |

Son beş tür için ön yüzde ilgili tür adı araması 0 sonuç verdi. Belge eklenemeyen önemli kayıtlar (arka uçta da
yok): ürün kartı, satış teklifi ve talebi, satış faturası, alış talebi, alış faturası, mal kabul, müşteri iadesi,
muhasebe fişi, kredi ve teminat kayıtları, kantar tartımı.

## B.4 Kullanılabilirlik engelleri

- **Yetki:** `documents.files.read`, `.upload`, `.link` ve logo/şablon izinleri hiçbir başlangıç rolünde yok
  (`ErpPermissions.cs` açıklaması: "kayıtlı, verilmemiş"). Rol atanmadan paneller boş ya da yetkisiz görünür.
- **Kurulum:** Geliştirme/Test dışındaki ortamda belge klasörü ve Defender yolu birlikte verilmezse yükleme ve
  indirme 503 döner; yalnız biri verilirse ya da sunucu Windows değilse uygulama başlamaz (`Modules/Documents/README.md`).
  Depodaki API imajı Linux tabanlıdır (`GoymenERP.Api/Dockerfile`), dolayısıyla test ortamı "Testing" kipinde
  kalmadıkça canlı tarayıcı kullanılamaz. Test kipinde depolama geçici klasördür ve tarayıcı yalnız deneme imzasını tanır.
- **Menü:** "Belgeler" başlığı çizilmiyor; logo ve şablon ekranları Ayarlar altında.
- **Açık kararlar:** arşivleme ve yasal bekletme, dönemler arası saklama (OPEN-018, OPEN-021).

## B.5 Eksikler

**İsteklerine göre:** merkezi arşiv ve Belge 360; gelen kutusu, OCR, sınıflandırma ve belgeden kayda akış (§8.4);
e-posta ve paylaşım; eksik PDF'ler (teklif, fatura, alış siparişi, ekstre; YOL-D11 ile sözleşme PDF'i); beş kayıt
türünde panel; indirme işleminin denetim kaydı (README: "indirme henüz denetlenmiyor"); XLSX çıktı.

**Bir ERP'de olması gerekene göre:** Müşteriye gönderilen her belgenin (teklif, sipariş teyidi, irsaliye, ekstre,
mutabakat mektubu) antetli PDF'i. Kalite sertifikası (üretici analiz belgesi) için ürün/parti/etiket düzeyinde belge
türü ve sevkte müşteriye birlikte verme; bugün belge türü alanı (sertifika, sözleşme, dekont gibi sınıflama) yok,
yalnız dosya türü var. Tarayıcıdan toplu yükleme. Belge geçerlilik tarihi ve süresi dolan belge uyarısı (imza
sirküleri, teminat mektubu). Canlı ortam Linux olacaksa Defender dışında bir tarayıcı seçeneği.

**Ekran:** Panel yükleniyor, boş, yetkisiz, taranmamış, tür uyumsuz, çok büyük ve çakışma durumlarını ayrı ayrı
gösteriyor (`documents/documentsText.ts`). Belgelerde arama, süzgeç ve önizleme sınırlı (`DocumentPreview.tsx`
var); sürükle-bırak ve çoklu dosya doğrulanmadı.

## B.6 Bağlantılar

- **Alır:** Her kayıt türü için sahip modülün "bu kullanıcı bu kaydı okuyabilir mi" cevabı (sahip bağlantı portu,
  `DocumentsRuntime/*OwnerLinkPort*.cs`, `FinanceRuntime/FinanceCashExchangeBookValueRuntime.cs`,
  `PurchasingRuntime/PurchaseInvoiceImportHost.cs`). Belge, bağlı olduğu kayıttan daha görünür olamaz.
- **Verir:** Satış ve Stok'a üretilen PDF'i kasaya yazma portu (`DocumentService.GeneratedStore.cs`); Satış, Stok ve
  Lojistik PDF'lerine yayımlanmış logo/şablon (`DocumentsRuntime/DocumentBrandingPdfRuntime.cs`).
- **Kopuk halkalar:** Sevk irsaliyesi PDF'i kasaya yazılmıyor: üretilen belge kabul eden türler yalnız satış
  siparişi ve stok etiketidir (`AcceptsGeneratedDocuments` iki portta) ve `LogisticsDispatchPdfEndpoints.cs` içinde
  kasaya yazma çağrısı (`IGeneratedDocumentStorePortV1`) yok. §25.7 "sistemin ürettiği bütün dosyalar
  kasaya girer" hedefiyle uyumsuz. Veri sahipliği ihlali görülmedi.

---

# C. Raporlar

## C.1 Kısa sonuç

Rapor merkezi yoktur. Bugün ekrandan açılabilen raporlar: üç satış raporu, cari ekstre, açık kalemler, stok
pozisyonu, denetim kaydı. Mizan ekranı yazılmış ama menüde yoktur. Yönetimin istediği "satış, stok, nakit, üretim
tek gerçeklikten" görünümü için ortak ekran yoktur; her modül kendi listesini gösterir. Ekrandan Excel/CSV alma
hiçbir listede yoktur.

## C.2 Hedef

- Kurumsal Rapor Merkezi: satış, satın alma, stok, üretim, lojistik, cari, finans ve zincir raporları; her raporda
  veri tarihi, para birimi/kur, süzgeç, sürüm (master §7.12; ekranlar R-01…R-05).
- Yönetim: tek gerçeklik, göstergeden işleme inme, erken uyarı (§2.2).
- Liste dışa aktarımı aynı süzgeç ve sütun sırasıyla; büyük dışa aktarım kuyrukta (§5.3, §13.5).
- Menüde 12. sıra "Raporlar" (KLN-D1).

## C.3 Bugün var olan

| Rapor / yetenek | Arka uç | Ekran | Menü | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Açık siparişler | Var | Var | Satış › Satış raporları | Var | 1 | `GET /sales/reports/open-orders`, `sales/SalesReportsPage.tsx`, `SalesReportsPage.test.tsx` |
| Kayıp fırsatlar | Var | Var | Aynı | Var | 1 | `/sales/reports/lost-opportunities` |
| Fatura–sipariş fiyat sapması | Var | Var | Aynı | Var | 1 | `/sales/reports/price-variance` |
| Üretim: çıktısı girdisinden fazla kapanışlar | Var | **Yok** | Yok | — | 1-kısmi | `ManufacturingEndpoints.cs` satır 1097; ön yüzde bu yolu çağıran dosya yok |
| Mizan | Var | Var | **Yok** (etiket boş, "aşamalı") | Var | 1-kısmi | `accounting/TrialBalancePage.tsx`; `shell/navigation.ts` satır 333 |
| Cari ekstre | Var | Var | Cari ve Muhasebe | Var | 1 | `/finance/party-ledger`, `finance/PartyLedgerWorkspacePage.tsx` |
| Açık kalemler | Var | Var | Finans | Var | 1 | `finance/OpenItemReadPage.tsx` |
| Stok / malzeme pozisyonu | Var | Var | Stok & Lojistik | Var | 1 | `inventory/InventoryStockPositionPage.tsx` |
| Satış faturaları Excel dışa aktarımı (muhasebe için) | Var | **Yok** | Yok | Var | 1-kısmi | `SalesEndpoints.Invoices.cs` satır 133, `SalesRuntime/SalesInvoiceExportWorkbookComposer.cs`; ön yüzde `invoices/export` araması 0 |
| Genel liste dışa aktarımı (Excel/CSV) | Yok | Yok | — | — | 0 | Sözleşmede `export` geçen tek yol yukarıdaki; ön yüzde üretilen tek CSV ürün içe aktarma hata dosyası (`product/importContract.ts`) |
| Rapor merkezi, zamanlanmış rapor | Yok | Yok | Yok | — | 0 | `/raporlar` rotası yok; menüde grup yok |
| Yaşlandırma, tahsilat performansı, nakit akışı, stok yaşı/devir, kapasite, plan–gerçek, tedarikçi performansı, kârlılık | Yok | Yok | — | — | 0 | Sözleşmede `aging`, `statement`, `summary`, `dashboard` 0 yol |

**§2.2 yönetim görünümlerine uzaklık.** Satış tarafında üç rapor var. Stok için anlık pozisyon listesi var, yaş ve
devir yok. Nakit için açık kalem ve ekstre var, nakit akış tahmini ve banka/kasa özeti raporu yok. Üretim için
ekranda rapor yok. Dört alanı yan yana gösteren ya da bir göstergeden kayda inen ortak ekran yok.

## C.4 Engeller, eksikler, bağlantılar

- **Yetki:** `sales.reports.read` ve `manufacturing.reports.read` hiçbir başlangıç rolünde yok.
- **İsteklerine göre eksik:** rapor merkezi, alan raporlarının çoğu, zincir raporları (siparişten nakde, satın
  almadan ödemeye, stoktan kârlılığa), her raporda veri tarihi/kur/sürüm başlığı, dışa aktarım, zamanlanmış rapor.
  22 Kasım kararlarında anılan yaşlanan stok, zamanında tam teslim, geri çağırma, grup içi döküm ve denetçi paketi de yok.
- **Bir ERP'de gerekenler:** vadesi geçen alacak yaşlandırma (30/60/90), müşteri ve ürün bazında ciro ve marj,
  KDV ve BA/BS hazırlık dökümü, banka/kasa günlük durum, stok değerleme, hurda ve fire oranı, tezgâh verimi, üç şirket
  için birleşik (konsolide) görünüm. Bu sonuncusu şirketler arası okuma izni ister ve insan kapısıdır.
- **Ekran:** Satış raporları ekranı sayıyı kendisi hesaplamıyor, sunucudan alıyor; tarih süzgeci var, dışa aktarım yok.
- **Bağlantı:** Raporlar bugün tek modülün kendi verisini okur. Fiyat sapması raporu Satış içinde kalır; modüller
  arası rapor için ortak okuma katmanı (rapor projeksiyonu, §13.5) yazılmamış.

---

# D. Arama

## D.1 Kısa sonuç

Üst çubuktaki arama kutusu çalışır: en az iki harf yazılır, sonuçlar türe göre gruplanır, her türden en çok beş kayıt
gelir ve kayda tıklanınca kendi ekranı açılır. Dört kayıt türünü kapsar. Fatura, irsaliye, alış siparişi, üretim
emri, plaka, tahsilat gibi numaralar aranamaz.

## D.2 Hedef

Belge numarası, barkod, cari/ürün kodu ve plaka için kesin arama; ad ve açıklama için yazım hatasına dayanıklı arama;
şirket ve yetki kapsamlı birleşik arama görünümü; sonuç açılırken yetkinin yeniden denetlenmesi (master §13.5).
Üst çubukta "global arama/komut" (§5.1). Komut paleti (22 Kasım Dalga 1).

## D.3 Bugün var olan

| Yetenek | Arka uç | Ekran | Erişim | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Genel arama: cari, ürün, satış siparişi, stok etiketi | Var | Var | Üst çubuk + `/arama` | Var | 1 | `GoymenERP.Api/Search/GlobalSearchComposer.cs`, `GlobalSearchOwnerSources.cs`, `search/GlobalSearchBox.tsx`; `GlobalSearchComposerTests.cs`, `GlobalSearchEndpointTests.cs`, `GlobalSearchSqlServerRelationalAcceptanceTests.cs` |
| Yetki süzmesi | Var | Var | — | Var | 1 | Her tür, sahibinin kendi okuma izniyle aranır; yetkisi olmayan tür için grup hiç dönmez, sayı da verilmez. Bir kaynak cevap veremezse aramanın tamamı 503 olur |
| "Daha fazla" bağlantısı | Kısmi | Kısmi | — | — | 1-kısmi | Yalnız caride listeye aynı aramayla geçer; diğer üç türde "aramayı daraltın" (`search/searchCapabilities.ts`) |
| Diğer kayıt türleri | Yok | Yok | — | — | 0 | Tür listesi dört öğe (`search/searchContract.ts`) |
| Yazım hatasına dayanıklı arama, ortak arama görünümü | Yok | — | — | — | 0 | Arama her sahibin kendi liste aramasını çağırır |
| Komut paleti ("şu ekranı aç") | Yok | Yok | — | — | 0 | — |
| Etiket izleme araması (ayrı) | Var | Var | Stok | Var | 1 | `/inventory/label-trace/search`, `inventory/InventoryLabelTracePage.tsx` |

## D.4 Engeller ve eksikler

- **Yetki:** Aramanın kendi izni yok; kutu, kullanıcı dört liste ekranından en az birine yetkiliyse görünür. Rol
  atanmadan kutu çizilmez.
- **Eksik türler (öncelik sırasıyla öneri):** satış faturası ve irsaliye numarası, alış siparişi ve alış faturası,
  üretim emri, yükleme talimatı ve plaka, tahsilat/ödeme, satış teklifi/talebi, muhasebe fişi, belge adı.
- **Ekran:** Sonuç sayfası durum bloklarını kullanıyor; klavye kısayolu ve son aramalar yok.
- **Açık nokta:** Arama hız hedefi tanımlı değil (OPEN-015).

---

# E. AI Merkezi

## E.1 Kısa sonuç

AI bugün kullanılamaz. Her sayfada "AI asistan" düğmesi ve sağ panel vardır; panel sunucuya sorar ve AI kapalı
olduğu için "şu an kullanılamıyor" yazar. Arka uçta güvenlik iskeleti (izin, veri sınıfı tavanı, araç izin listesi,
giden metin süzgeci, iki dış sağlayıcı protokolü) yazılmıştır. Çalışan bir asistan için eksik olanlar: modüllerin
okuma araçları (bugün 0), doğal dille cevap üreten model bağlantısı, sağlayıcı ve anahtar, veri izni ayar ekranı.

## E.2 Hedef

- Üç seviye: sayfa içi küçük yardımlar, sağ bağlam paneli, AI Merkezi/yönetim özeti (master §6.1; ekranlar AI-01…AI-03).
- Oku/açıkla, öner, taslak, önizle, onay sonrası uygula; kendi kendine onay ve yetki verme yasak (§6.2).
- AI veritabanına bağlanmaz; yalnız şemalı, yetki ve şirket denetimli araçları çağırır; her işlem kayıt altında (§6.4).
- Dört iş: sistemi kullanır (onayla), iş yükünü azaltır (taslak, OCR, özet), sistemi tanır, genel sorulara cevap
  verir; yerel AI ve dış AI birlikte (AI-D1, AI-D2). Hassas olmayan veri dışarı gidebilir; hassas veri yalnız takma
  adla ve yöneticinin açtığı türlerde; sağlayıcı veri saklamayan olmalı; canlıda açma Sinan'da (AI-D3…AI-D6,
  `SINAN_KARARLARI_2026-11-03_AI.md`).
- İlk tur yalın set: ağ geçidi ve doğal dil cevap, değerlendirme seti, bütçe tavanı, görev başına okuma araçları,
  kaynak zorunlu bilgi tabanı, onay kartında özet, belgeden taslak, dar doğal dil sorgu, tek yönetim ekranı (AI-D22).
  Onay, ret, ödeme, dönem kapanışı, rol atama ve politika yayımlama AI'a hiçbir koşulda açılmaz (AI-D25).

## E.3 Bugün var olan

| Yetenek | Arka uç | Ekran | Erişim | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Sağ AI paneli (her sayfada düğme) | — | Var | Üst çubuk | Var | 1 | `shell/AppShell.tsx`, `ai/AiPanel.tsx`; kapalıyken yalnız dürüst durum metni |
| Yetenek sorgusu ve soru ucu | Var | Var | — | Var | 1, **kapalı** | `Endpoints/AiEndpoints.cs` (`/ai/capabilities`, `/ai/ask`), `Ai/AiAskService.cs`; `AiAskServiceTests.cs`, `tests/GoymenERP.Ai.UnitTests` |
| Çalışma anahtarı | Var | — | — | — | Kapalı | `appsettings.json`: `Ai.Enabled=false`, `AllowedTools=[]`, `External.Providers=[]` |
| Model sağlayıcı | **Sahte** | — | — | Var | — | `Ai/AiModelProvider.cs` `FakeAiModelProvider`: cevap üretmez, yalnız kullanıcının seçtiği aracı çağırır; canlı ortamda reddedilir |
| Dış sağlayıcı bağdaştırıcıları (iki protokol) | Var | — | — | Var | 1-kısmi | `Ai/External/AiExternalProtocolAdapters.cs`; taşıyıcı `NoEgressAiExternalTransport` dışarı çıkmaz; anahtar ve sağlayıcı yok |
| Araç kataloğu (varsayılan kapalı izin listesi) | Var | — | — | Var | 1-kısmi | `Ai/AiToolCatalog.cs`; **kayıtlı araç 0**: `IAiOwnerReadToolV1` uygulayan sınıf araması üretim kodunda 0, yalnız testte |
| Taslak, önizleme ve yazma araçları | Yalnız arayüz | — | — | — | 0 | `BuildingBlocks/Ai/AiToolContractsV1.cs` tür tanımları; katalog bunları hiç kabul etmez |
| Giden metin süzgeci, takma ad | Var | — | — | Var | 1 | `BuildingBlocks/Ai/AiOutboundTextScreenV1.cs`, `AiExternalEgressV1.cs` |
| AI izin reddi güvenlik olayı | Var | — | — | — | 1 | `appsettings.json` `SecurityEvents.Retention.AiAuthorizationDenied` |
| AI Merkezi ekranı, günlük özet, öneri, geri bildirim | Yok | Yok | Yok | — | 0 | `DailySummary`, `feedback` araması 0; rota yok |
| AI yönetim/veri izni ayar ekranı | Yok | Yok | — | — | 0 | Değerler yalnız yapılandırma dosyasından, yeniden başlatmayla |
| Belge okuma (OCR), belgeden taslak | Yok | Yok | — | — | 0 | Bkz. B.3 |
| Karar/bilgi dizini, değerlendirme seti | Yok | — | — | — | 0 | `tests/` altında `ai_decision_index`, `ai_eval_set` klasörü yok |
| Sayfa içi küçük AI yardımları | Yok | Yok | — | — | 0 | Panel açık ekranın kaydını bilmez; kullanıcı araç seçip teknik bilgi (JSON) yazar (`ai/aiText.ts` `argumentsLabel`) |

## E.4 Açmak için gerekenler

Kararlar (insan kapısı): dış sağlayıcı seçimi ve sözleşmesi, veri saklamama kanıtı, KVKK aktarım zemini (OPEN-020);
yerel model ve OCR için donanım (OPEN-014); canlıda ilk açma; hangi veri türlerinin takma adla gidebileceği;
`ai.panel.use` izninin kimlere verileceği (karar: başlangıçta yönetici ve genel müdür; atama yapılmadı); aylık bütçe;
"marj, cari kimliği, sözleşme" gibi listede olmayan gizli kalemlerin durumu (AI-D4 açık notu).

İşler: (1) modül başına 3–6 okuma aracı, (2) doğal dil cevap ve kaynak gösterme, (3) çıkış yapan taşıyıcı ve anahtar
okuma, (4) Ayarlar'da AI politikası ekranı, (5) panelin açık ekranı tanıması, (6) değerlendirme seti ve koşucusu,
(7) AI Merkezi ekranı. Bunlar olmadan `Ai.Enabled=true` yapılsa bile panel "okunabilecek araç yok" der.

**"Her sayfada AI" hedefine uzaklık:** kabuk ve güvenlik kapıları hazır, içerik yok. Düğme her sayfada; cevap
hiçbir sayfada.

## E.5 Bağlantılar

- **Alır:** Kimlik'ten izin denetimi, Güvenlik Olayları'na ret kaydı. Modüllerden araç bekler; hiçbiri vermiyor.
- **Verir:** Hiçbir modüle bir şey vermez; kapalıyken ERP etkilenmez (tasarım gereği).
- **Kopuk halka:** AI ile Work (onay kartı özeti) ve Belgeler (belgeden taslak) arasında kod bağı yok.

---

# F. Ortak ekran şablonu ve tasarım sistemi

**Hedef (master §5, §26):** sabit kabuk; sayfa başlığı ve tek ana işlem; özet şeridi; süzgeç ve kayıtlı görünüm;
sağ bağlam paneli; sabit alt işlem çubuğu; yükleniyor/boş/hata/yetkisiz/çakışma durumları; sunucu tarafı sayfalama;
sütun seçme; toplu işlem; dışa aktarım; telefon ve tablet uyumu; klavye ve ekran okuyucu.

**Var olan ortak parçalar.**

| Parça | Durum | Kanıt |
|---|---|---|
| Kabuk: sol menü (gruplu), şirket seçici, arama, AI düğmesi, kullanıcı | Var | `shell/AppShell.tsx`, `shell/navigation.ts` |
| Telefon menüsü (767 piksel altında çekmece), hareket azaltma | Var | `shell/AppShell.module.css` satır 353, 394 |
| Durum bloğu: 13 durum (yükleniyor, boş, süzgeçte boş, oturum yok, yetkisiz, bulunamadı, çakışma, çevrimdışı, ağ hatası, hizmet yok, bozuk cevap, eski cevap, yetkili şirket yok) | Var | `shell/uiState.ts`, `shell/StateBlock.tsx` |
| Ortak tablo, düğme, alan, durum göstergesi, çakışma farkı | Var | `ui/DataTable.tsx`, `Button.tsx`, `Field.tsx`, `StatusIndicator.tsx`, `ConflictDiff.tsx` |
| Renk ve yazı tipi sabitleri, kontrast testi | Var | `ui/tokens.css`, `ui/tokenContrast.test.ts` |
| Liste/form standardı (15 madde) ve otomatik denetim | Var | `ui/listFormStandard.ts`, `ui/listFormAudit.ts` |
| Ekmek kırıntısı | Yalnız yer ayrılmış | `AppShell.tsx` başlık notu; modül ayrıntı sayfalarında yalnız Cari'de izi var |
| Dönem seçici, bildirim zili, yardım, favoriler, menü sayaçları | Yok | `AppShell.tsx` üst çubuk içeriği |
| Özet şeridi, kayıtlı görünüm, sütun göster/gizle, toplu işlem çubuğu, dışa aktarım, paylaş | Yok (ortak parça olarak) | `ui/` klasöründe karşılığı yok |

**Modüller arası tutarlılık (ölçüm).** Durum bloğu bütün modüllerde kullanılıyor (ör. Satış 40, Finans 34, Ayarlar
23 dosya); bu en tutarlı kısım. Ortak tablo ise az kullanılıyor: ortak `DataTable` 16 dosyada, elle yazılmış tablo
151 dosyada. Modül bazında ortak tablo kullanan dosya sayısı: Satış 9, Finans 3, Stok 2, Satın Alma 1, Cari 1;
Üretim, Lojistik, Ürün, Muhasebe, Ayarlar, Organizasyon 0. Liste/form denetiminin kendi kaydı da bunu doğrular:
392 dosyada 163 bulgu, "yalnız azalır" taban listesi olarak tutuluyor (`ui/listFormAuditBaseline.ts`).

Örnek karşılaştırma (liste sayfaları):

| Sayfa | Ortak tablo | Arama | Sıralama | Seçim kutusu | Sayfalama |
|---|---|---|---|---|---|
| Satış siparişleri (`sales/SalesDraftListPage.tsx`) | Evet | Sınırlı | Yok | Yok | Var |
| Alış siparişleri (`purchasing/PurchaseOrderListPage.tsx`) | Evet | Var | Yok | Yok | Var |
| Üretim emirleri (`manufacturing/OrderListPage.tsx`) | Hayır | Yok | Yok | Yok | Var |
| Cari kartlar (`cari/CariListPage.tsx`) | Evet | Var | Var | Var | Var |
| Ürünler (`product/ProductListPage.tsx`) | Hayır | Var | Var | Var | Var |

Ayrıntı sayfaları: Satış sipariş ayrıntısı 1.468 satır, üretim emri ayrıntısı 1.772 satır tek dosyadır; alış
siparişinde sekme düzeni var, satış ve üretimde yok; belge paneli alış siparişi ve caride sayfanın içinde, satışta
ayrı çalışma alanında, üretimde hiç yok. Sabit alt işlem çubuğu ortak parça olarak yok.

**Telefon ve tablet.** Kabuk telefonda çekmeceye dönüyor. Modül stil dosyalarının azında ekran genişliği kuralı var
(ör. Satış 8 dosyanın 4'ünde, Finans 3'ün 1'inde; Lojistik, Muhasebe, Ayarlar, Organizasyon 0). §5.5'teki "telefonda
tablo yerine kart" düzeni ortak tabloda yok (`ui/dataTable.module.css` içinde ekran genişliği kuralı 0). Saha
kullanımı (depo, kantar, sevkiyat) için gerçek cihaz denemesi yapılmadı.

---

## 7. Açık kararlar (bu alanları etkileyen)

| Konu | Durum | Kullanımı engelliyor mu |
|---|---|---|
| Olay dağıtıcısının açılması ve beş çalışma değeri | İnsan kapısı | **Evet**: bildirim ve olayla kurulan modül bağları |
| Bu alanların izinlerinin rollere verilmesi | İnsan kapısı | **Evet** |
| Belge klasörü ve virüs tarayıcı kurulumu (sunucu işletim sistemi dahil) | Kurulum kararı | **Evet** (canlı kipte) |
| OPEN-020: AI sağlayıcısı, sözleşme, KVKK, canlıda açma | Kısmen açık | AI için evet; ERP için hayır |
| OPEN-014: sunucu ve GPU envanteri, yerel AI/OCR kapasitesi | Kısmen açık | OCR ve yerel AI için evet |
| OPEN-018 / OPEN-021: saklama, arşiv, yasal bekletme | Kısmen açık | Hayır (silme yok kuralı yeterli) |
| OPEN-015: hacim ve hız hedefi | Kısmen açık | Hayır |
| "Düşük riskli" onay türleri, saat ölçekli sürelerin kesin listesi, AI başarı eşikleri (22 Kasım NEEDS-SINAN) | Açık | Hayır; ilgili özellik kapalı başlar |
| AI Merkezi'nin menüdeki yeri | Açık (KLN-D1 notu) | Hayır |
| Şirketler arası birleşik rapor izni | Karar yok | Grup raporu için evet |

## 8. Yapılacaklar (dört alan ortak, öncelik sırasıyla)

Öncelik: P0 = üç firmanın test ortamında başlaması için şart; P1 = hedefin tamamı; P2 = sonra.
Büyüklük: K = gün, O = 1–2 hafta, B = daha fazla.

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---|---|---|---|---|---|
| 1 | Olay dağıtıcısını test ortamında aç; beş çalışma değerini belirle | Bildirimler ve olayla kurulan 13 modül bağı bugün akmıyor | P0 | K | Sinan kararı (değerler ve açma) |
| 2 | Belge, rapor, arama ve onay kutusu izinlerini iş rollerine ekle | İzinler hiçbir rolde yok; paneller ve raporlar görünmez | P0 | K | Rol/yetki kararı (insan kapısı) |
| 3 | Test ortamında belge depolama ve taramayı kur; sunucu Linux ise tarayıcı seçeneğini karara bağla | Canlı kipte yükleme/indirme 503 ya da açılmama | P0 | K–O | Sunucu kararı |
| 4 | Menüye "Çalışma Merkezi" grubunu ekle (Onaylarım, Kuyruk, Bildirimlerim); üst çubuğa bildirim zili | Bugün yalnız ana sayfadaki bağlantıdan ulaşılıyor | P0 | K | — |
| 5 | İlk 12 onay türünü kutuya bağla (iskonto, taban fiyat, risk, irsaliye, plan üstü sevk, tartım farkı, üretim planı, alış siparişi, fatura farkı, mal kabul, kredi limiti, banka ödemesi) | Onaycı bugün 59 işlemi kendi modül ekranında aramak zorunda | P0 | O | — |
| 6 | Ana sayfayı istisna öncelikli hale getir: bekleyen onay sayısı, geciken işler, modül kısayolları | Bütün olarak kullanım için giriş noktası boş | P0 | O | #5 |
| 7 | Eksik PDF'ler: satış teklifi, satış faturası bilgi çıktısı, alış siparişi, cari ekstre | Müşteri ve tedarikçiye belge gönderilemiyor | P0 | O | Şablon modülü kapsamı (Satın Alma, Finans) |
| 8 | Liste ekranlarına ortak Excel dışa aktarım; satış faturası dışa aktarımına ve üretim raporuna ekran; mizanı menüye al | Test döneminde eski sistemle karşılaştırma ve muhasebeye aktarım için gerekli | P0 | O | — |
| 9 | Aramaya fatura, irsaliye, alış siparişi, üretim emri ve plaka ekle | Belge numarasıyla kayıt bulma günlük iş | P1 | O | — |
| 10 | Kalan onay türlerini kutuya bağla; kutudan karar; sürümlü kural tablosu | Tek iş kutusu hedefi (AI-D23, AI-D31) | P1 | B | #5 |
| 11 | Süre sayacı, bir hatırlatma, yükseltme, sahipsiz onay; vekâlet | Onayın beklememesi (YOL-D17, YOL-D18) | P1 | B | #10; zamanlayıcı |
| 12 | Görev ve kayda bağlı not; Work yönetim ekranı | master §7.1, AI-D20 | P1 | B | — |
| 13 | Merkezi belge arşivi ve Belge 360; "Belgeler" menüsü; belge türü sınıflaması | KLN-D4, master B-06/B-07 | P1 | O–B | — |
| 14 | Belge panelini üretim planı/emri, tahsilat, banka ödemesi, çek/senet ekranlarına ekle; yeni kayıt türleri (ürün, teklif, fatura, mal kabul, muhasebe fişi) | Arka uçta 5 tür ekransız; önemli kayıtlar kapsam dışı | P1 | O | — |
| 15 | Sevk irsaliyesi ve diğer üretilen PDF'lerin kasaya yazılması; indirme denetim kaydı | §25.7; izlenebilirlik | P1 | K–O | — |
| 16 | Rapor merkezi ve ilk yönetim seti: alacak yaşlandırma, ciro/marj, stok yaşı ve değeri, nakit durumu, plan–gerçek, zamanında teslim | master §2.2, §7.12 | P1 | B | Ortak rapor okuma katmanı |
| 17 | Ortak ekran parçaları: sütun seç, kayıtlı görünüm, toplu işlem çubuğu, sabit alt çubuk, ekmek kırıntısı; elle yazılmış tabloları ortak tabloya taşı | 151 dosyada elle tablo; modüller arası farklı davranış | P1 | B | — |
| 18 | Telefon/tablet: ortak tabloda kart düzeni; depo, kantar, sevkiyat ekranlarının cihazda denenmesi | master §5.5 | P1 | O–B | #17 |
| 19 | AI: modül başına okuma araçları, doğal dil cevap, çıkışlı taşıyıcı, Ayarlar'da AI politikası, değerlendirme seti | AI bugün iş görmüyor | P1 | B | OPEN-020 kararları, anahtar, bütçe |
| 20 | AI: panelin açık ekranı tanıması, onay kartında özet, AI Merkezi ekranı | master §6, AI-D22, AI-D26 | P1 | B | #19, #10 |
| 21 | OCR ve belgeden taslak; belge gelen kutusu | master §8.4 | P2 | B | OPEN-014, #13, #19 |
| 22 | E-posta gönderme/alma, paylaşım, iletişim şablonları | master §7.9, AI-D27 | P2 | B | E-posta hesabı (insan kapısı) |
| 23 | Yazım hatasına dayanıklı ortak arama görünümü; komut paleti | master §13.5 | P2 | O–B | #9 |
| 24 | Zamanlanmış rapor, favoriler, şirketler arası birleşik rapor | master R-05, D-07 | P2 | O–B | #16; şirketler arası izin kararı |

## 9. Eski belgelerle koda göre çelişkiler

- `docs/delivery/WORK_AI_TAMAMLAMA_PLANI_v1.md` §1 ve §2: "Dalga 1: 1.2 (12 tür) bitti", "A.3 (2 araç)", karar
  dizini, `IWorkInboxSourceV1`, `work.InboxNotifications`, `GET /api/v1/work/people`, `work.tasks.*` izinleri anılıyor.
  Belge bunu "beş dal üzerinde" tespit ettiğini söyler. Bu depoda (yerelde yalnız `main` ve oturum dalı var) bu
  adların hiçbiri `src/` ve `contracts/` altında bulunmuyor; `tests/ai_decision_index` ve `tests/ai_eval_set` de yok.
  Main için doğru sayılar: kutuda 4 tür, AI aracı 0, görev/not yok.
- `docs/delivery/WORK_AI_ANALIZ_v1.md`: "64 onay akışının 4'ü" ve "kayıtlı araç sıfır" tespiti koda uyuyor
  (bu sayımda 63 bulundu; fark sayım ölçütünden kaynaklanıyor olabilir). "828 operasyon" sayısı da yaklaşık doğru:
  sözleşmede bugün 751 yol ve 829 işlem var.
- `src/backend/Modules/Documents/README.md` "üretilen belge kabul eden türler: satış siparişi, stok etiketi" der;
  kod aynı. Aynı README'nin giriş satırı kasayı "ilk sahip: Cari" diye tanıtır; bugün 14 tür bağlı.
- `shell/ShellHome.tsx` başlık notu "Onaylarım yalnız Finans onay yetkisi olana görünür" der; kod dört kaynağın
  herhangi birinin yetkisiyle gösteriyor (`work/workText.ts` `APPROVAL_SOURCES`).

## 10. Doğrulanamayanlar

- Testlerin geçip geçmediği (koşulmadı); yalnız test dosyalarının varlığı yazıldı.
- 22 Kasım planında anılan beş dalın içeriği: uzak depoda çok sayıda dal var, bu çalışmada açılmadı.
- Satış siparişi PDF'inin yayımlanmış logo/anteti gerçekten kullandığı: bağlantı kodu var
  (`DocumentBrandingPdfRuntime.cs`), çıktı görülmedi.
- Belge panelinde çoklu dosya ve sürükle-bırak; belge önizlemenin hangi türleri gösterdiği.
- Onay işlemi sayısı (63) yol adına göre sayıldı; adında "approval/decision" geçmeyen onay benzeri işlemler
  (ör. yayımlama, kesinleştirme, ret uçları) bu sayıya girmedi.
- Ortak tablo/elle tablo sayıları dosya sayısıdır; bir dosyada birden çok tablo olabilir.
- Telefon ve tablet görünümü gerçek cihazda ya da tarayıcıda denenmedi; yalnız stil dosyalarındaki kurallar sayıldı.
- Rol oluşturma ve izin atama ekranının bu izinleri atamaya yetip yetmediği (Kimlik raporunun konusu).
