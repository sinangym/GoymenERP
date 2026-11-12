# F-017 — Gelecek tedarikten satış siparişine ayırma: spec ve dilim planı v1

Tarih: 2026-11-12 · Hazırlayan: Claude (cloud oturumu, Alış şeridi) · Durum: **ÖNERİ** (plan; iş kararı değildir)
Karar: `docs/decisions/SINAN_KARARLARI_2026-11-12_ALIS_EK17.md` ALS-D82 (#66 6819147253 madde 2), ALS-D83/ALS-D84 (#66 6819640816
madde 3–4), ALS-D85..ALS-D90 (#66 6820601088 madde 1, 3, 6–9), ALS-D91 (#66 6821262098 madde 7), ALS-D92 (#66 6822140358
madde 1), ALS-D93 (#66 6822767886 madde 3);
ALS-D10/D12 (API-11 satır kapanışı); master INV-006; Stok KLN-D26 (#1108); ALS-D30 (EK3); ALS-D22/D25;
SAT-D298 (birim çevrim kaynağı ürün kartı katsayısı).
Bağlam: `docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md` MOD-14, MOD-16, A3-0; Fable analizi F-017
(`docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md`). Stok karşı belgesi: PR #1116
`docs/delivery/STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md`.

Bu belge yalnız plan ve sözleşme taslağıdır. Kod yoktur. Stok tarafındaki model Stok şeridinin işidir (#1116). Buradaki Stok
maddeleri **öneri**dir; Stok şeridi kendi kapılarıyla değiştirebilir. Arayüz adları öneridir. Problem kodu adları da öneridir;
mevcut önek düzenine (`purchasing_…`, `inventory_future_allocation_…`) uyar ve ilgili dilimde kesinleşir.

Revizyon 2 (2026-11-12): Stok şeridi PORT-REVIEW (#1110 review 5871928090) bulguları işlendi: `companyId` parametresi
kaldırıldı, birim kuralı SAT-D298'e bağlandı, sipariş sürümü yalnız gözlem oldu, modüller arası yarış için ortak satır kilidi
eklendi (§3.4), F17-1 K3 oldu, genel/teknik satır kuralı yazıldı, termin dilimleri ayrı okumaya alındı, kabul geri alma
maddesi NEEDS-SINAN'a taşındı, kabul replay çiti çevirmeyi kapsar.

Revizyon 3 (2026-11-12): Sinan S1/S2 cevapları işlendi (ALS-D83 bağ düşürme §3.5; ALS-D84 kabulde sıra §3.3). Satır/sipariş
iptali yeni NEEDS-SINAN S4 oldu.

Revizyon 4 (2026-11-12): Sinan 11.–13. tur cevapları işlendi: iptal akışı ALS-D85 (§3.5), kabul geri alma ALS-D86 ve
az/fazla gelme ALS-D87 (§3.3), ayıran kişi ALS-D88, kabulde bağlama hatası ALS-D89 (§3.3), süre yok ALS-D90. §5'te açık iş
kararı kalmadı.

Revizyon 5 (2026-11-12): #1110 DOCS review A 6820967459 ve B 6821005445 bulguları ve Sinan 18. tur m.7 (ALS-D91) işlendi:
ALS-D87 "satır kapanırsa" API-11 kapanışına bağlandı (§3.6), "bağları düşür ve iptal et" sözleşmesi görülen küme, host
orkestrasyonu, replay, izin ve neden kodu/metin ayrımıyla yazıldı (§3.5), kabul geri alma nedene göre ayrı bölüm ve dilim oldu
(§3.7, F17-6), "aktif ayırma" tanımlandı, iş/teknik hata ayrımı, sıra önceliği, uyarı neden kodları ve termin uyarısının sahibi
yazıldı.

Revizyon 6 (2026-11-12): #1110 DOCS review A 6821502877 ve B 6821523835 bulguları işlendi: ayırma oluşturma yolu kabul
çevirmesi ve iptal koruması gelmeden açılmaz, API-44/API-60 için sıra kuralı (§4); ayrılabilir formülü INV-006 ile hizalandı,
kapalı satırın ayrılabilir miktarı 0 (§2); §3.7'de "kapalı" tanımlandı ve Sinan metninin ötesindeki okumalar etiketlendi;
Cancelled/API-11 kapalı satır kesişimi fail-closed; API-11 izinleri, replay sırası, problem kodu adları, iade işaretinin yeri ve
API-44 ters + yeni çevirme sırası yazıldı.

Revizyon 7 (2026-11-12): #1110 DOCS review A 6822067504 ve B 6822096001 bulguları ve Sinan 19. tur m.1 (ALS-D92) işlendi:
Cancelled sipariş / API-11 kapalı satırda hatalı kabul geri alma ve işaretli iade miktarı açar, sipariş/satır **canlanır**
(yazar okuması "açılmaz" ve geçici 409 kaldırıldı); canlanmanın teknik biçimi §3.8'de K3 olarak yazıldı. F17-2b K3 oldu ve
#1116 §10 hizalama kuralı eklendi; §3.2 portuna tüketilmiş ayırma sinyali eklendi ve 409 korumasının sahibi yazıldı;
`kabulNet`/`kesinRed/iadeEtkisi` port formülüne onları ilk yazan dilimde girer; problem kodları önek düzenine getirildi.

Revizyon 8 (2026-11-12): #1110 DOCS review A 6822493749 ve B 6822468969 bulguları ve Sinan 21. tur m.3 (ALS-D93) işlendi:
§3.8 pay aritmetiği koordinatör notundaki "iptal/kapanış payının düşmesi" ifadesine bağlandı (2X çift sayım kapandı); canlanmanın
kod/DB etkisi (iptal kısıtı, iptal kaydı geçmişi, `CancelRemainder`) API-44/API-60 K3 dilimine yazıldı; aynı host işleminde Alış
yazısı → Stok okuması sırası yazıldı; replay sürüm kontrolünden önce; sürüm çakışması mevcut `version_conflict` kodu; bırakılmış
ayırmaların canlanmada geri gelmemesi ve "canlandı" bildirimi ALS-D93'e bağlandı; F17-1 anındaki port formülü yazıldı.

Revizyon 9 (2026-11-12): #1110 DOCS review B 6823061324 ve A 6823074629 bulguları işlendi (yalnız teknik biçim; yeni politika
yok): §3.7 host işlemi tutarlılık kontrolü host'un Alış sonucuna bağlandı (`Allocatable` + beklenen sürüm + beklenen miktar;
başka her cevap, bayat `NotAllocatable` dahil, işlemin tamamını geri alır; Stok iş reddi de Alış yazısını geri alır); §3.1'e
işlem içi tutarlılık kontrolü istisnası; §3.8 canlanma tek düzeltmedeki **net** açılan miktara bağlandı (net 0 ise durum
değişmez); iptal geçmişi deposu iki seçenekte de zorunlu ve forward-fix `Down` davranışı yazıldı; alıntı ve satır atıfları
düzeltildi. #1116 hizalaması (koordinatör kararı (d), #66 6824928883 F-017 (a)–(d) koordinatör kaydı; merged #1116 (merge `1af6ffea`) §3.2 adım 0, §3.7 ve KLN-D28 (7)'de yazar okuması): kabul geri almada `Consumed → Active` yeniden
açma önce satış satırı kilidini, sonra alış satırlarını artan `lineId` ile alır; kilit sonrası satış satırı yeniden okunur,
iptal/kapalıysa aktif gelecek oluşmaz (§3.4, §3.7, §6). Aynı kararın (a) satır kaldırmanın Stok portuna 0'a azaltma olarak
gelmesi (kapsam: satırda sevk, yükleme talimatı veya fiziksel ayırma varsa Satış 422 döner ve Stok portu çağrılmaz; #66 6824928883 (a)), (b) fiziksel tahsise dokunan bırakmaya ara 409 KLN-D32 (3), (c) terminal ret kaydı ve replay'in Satış revizyon
komutunda olması Satış/Stok tarafıdır; bu belgede onlarla çelişen metin yoktur. Revizyon 10'da metin merged #1116 (main `1af6ffea`) ile yeniden karşılaştırıldı.

Revizyon 10 (2026-11-12): #1110 merge sonrası DOCS review A r9 6824530422 ve B r9 6824580734 P3'leri işlendi (yalnız
teknik biçim; yeni politika yok): kilit sırası kaynağı düzeltildi (koordinatör kararı (d), #1116'da yazar okuması; review
6824169369 atfı kaldırıldı); §3.7 adım 1'e çoklu satış satırı sırası ve "küme oku → kilitle → kilit altında yeniden oku"
protokolü; §3.7 tutarlılık kontrolünün dalları sayısal karşılaştırmayla yazıldı; "Sinan lafzı" etiketi yazar türetmesi
olarak düzeltildi; net = 0 düzeltme için kabul kapısı değişikliği Kod/DB listesine girdi, net satır başına tanımlandı,
beklenen değer "net son durumdaki kalan" oldu; §3.7'ye geri alma sonuçlarının terminal / terminal değil sınıfı eklendi;
satır kilidi meşgul kodu hizalaması §4'e yazıldı (§3.4 metni değişmedi; ad F17-2'de kesinleşir).

Revizyon 11 (2026-11-12): #1127 merge sonrası DOCS review A 6824921197 ve B 6824916506 P3'leri işlendi (yalnız teknik
biçim; yeni politika yok): koordinatör kararı (d) atfı #66 6824928883 kaydına bağlandı; (a) özetine kapsam eklendi; §3.7 ile
§3.8 arasındaki net 0 çelişkisi giderildi (net 0'da açılan miktar yoktur, §3.7 port türüne bakmaz; beklenen değer yalnız
net > 0'da verilir); §3.7'ye karışık durum kuralı (önce tüm satırlar değerlendirilir, sonra terminal olmayan kazanır) ve
sınıflandırılmamış durumlar eklendi; açılan miktar yokken `Unreadable` / `NotFound` / `Line` null 503 ile fail-closed
yazıldı; kabul kapısı "net = 0" ve satır bazlı olarak düzeltildi (rev10'daki "net ≤ 0" ifadesi "net = 0" oldu; net < 0
ayrı kuraldır); rev10 özetindeki §3.4 atfı düzeltildi.

Revizyon 12 (2026-11-12): #1137 merge sonrası DOCS review A 6825893552 ve B 6825964824 P3'leri ve koordinatörün
değerlendirme modeli (#66 6825926365) işlendi (yalnız teknik biçim; yeni politika yok): §3.7 karışık durum kuralı
6825926365 modeliyle değiştirildi (önce bütün kilitler; sabit sırada değerlendirme; ilk terminal olmayan sonuç denemeyi
bitirir ve kendi durum/kodunu döndürür; hepsi terminalse sabit sıradaki ilk terminal kaydedilir); "port cevabının türüne
bakmaz" ifadesi `NotAllocatable` / `Allocatable(R)` ayrımıyla sınırlandı ve 503 istisnası §3.8'e de yazıldı; terminal sınıf
"Stok iş reddi; bugün yalnız `inventory_future_allocation_already_fulfilled`" olarak tekleştirildi; "net < 0 zaten
`AcceptanceExceedsRemaining`" cümlesi kalanı 0 olan satırla sınırlandı (Approved satırda kalan içinde net < 0 meşrudur,
`PurchaseOrder.cs:337-341`); §4 #1116 hizalama maddesi #1126 (main `732ba997`) sonrasına güncellendi; §6'ya karışık çok
satırlı, açılan miktar yokken fail-closed, satır bazlı net 0 / net > 0 kapısı ve 6825926365 golden testleri eklendi.
Rev12 r2 (#1142 review A 6826271131, B 6826280925): model Alış akışına uyarlandı (bugün tek terminal kod
`inventory_future_allocation_already_fulfilled`, bu akışta kaydedilen 422 yok; golden (iv)/(v) buna göre; farklı terminal
kodlar bugün ulaşılamaz); Alış domain retlerinin Stok değerlendirmesinden önce ev iskeleti gereği terminal kaydedildiği yazar
okuması olarak yazıldı (r3'te düzeltildi); sınırlı deneme tükenmesi kilit evresine alındı ve kodu adlandırıldı; 5. adım "terminal olmayan sonuç
yoksa ve en az bir terminal varsa" oldu; model etiketi "koordinatör okuması" oldu; §6'ya port istisnası, zaman aşımı ve deneme
tükenmesi testleri eklendi; Kod/DB kapı parantezi §3.8 ile hizalandı (`RequireNotCancelled` önceliği).
Rev12 r3 (#1142 review A 6826710253, B 6826712066): Alış retlerinin sınıflaması ev iskeletinin catch zinciriyle yeniden yazıldı
(sürüm/durum retleri kaydedilen 409, kullanıcının düzeltebileceği kural retleri kaydedilmeyen 422 `validation_failed`; kod
bazında eşleme F17-6'da); r2'deki "mevcut davranış" iddiası kaldırıldı; deneme tükenmesinin kilit altı yeniden okumayla tespit
edildiği düzeltildi; Kod/DB kabul kapısı değişikliğine participant kapıları eklendi.

## 1. Bugün (main `58b96aa2`)

| Parça | Durum | Kanıt |
|---|---|---|
| Onaylı satırın kalan taahhüdü (tek satır) | Var: `IPurchaseSupplyReferencePortV1.GetApprovedLineAsync` (committed − accepted, gelecek arz; Draft/Cancelled = NotApproved; şirket sunucu bağlamından) | `…Purchasing.Application/Contracts/IPurchaseSupplyReferencePortV1.cs`; `…Purchasing.Infrastructure/Persistence/PurchaseSupplyReferencePort.cs:22-30` |
| Onaylı açık satır listesi (operasyon listesi) | Var: `IPurchaseOpenLineReadPortV1` (şirket geneli, sayfalı; ürün süzgeci yok) | `IPurchaseOpenLineReadPortV1.cs` |
| Satış siparişi için alınan satırlar | Var: `IPurchaseSalesLinkedSupplyReadPortV1` (talep satırı kaynağı = satış siparişi; iptal önizlemesi için) | `IPurchaseSalesLinkedSupplyReadPortV1.cs` |
| Mal kabulünde Stok'a giden kayıt | Var: aynı host işleminde `InventoryGoodsReceiptRequestV1` (satınalma siparişi + satır kimliği, etiket, lot) | `…Inventory.Application/InventoryGoodsReceiptContractsV1.cs` |
| Fiziksel stoktan tahsis | Var: `DemandKind = SalesOrderLine` | `…Inventory.Infrastructure/Persistence/InventoryStore.Allocations.cs` |
| Modüller arası satır kilidi | **Yok** (kodda uygulama kilidi kullanımı yok) | §3.4 |
| Gelecek tedarikten ayırma | **Yok** | F-017 |
| İptal | Yalnız onaylı siparişin **tüm** kalanı (satır/miktar düzeyinde iptal yok); tek Alış store kaydı | `PurchaseOrder.cs:362-394` (`Cancel`); `PurchasingService.OrderCancellation.cs:54` |
| Satır kapanışı (eksik teslim, yeniden açma) | **Yok** (plan API-11, ALS-D10/D12) | plan API-11 |
| Kabul düzeltmesi / tedarikçiye iade | **Yok** (plan API-44 ters + yeni, API-60 iade); `RecordAcceptance` yalnız ekler | `PurchaseOrder.cs:319` (`RecordAcceptance`); plan API-44, API-60 |

## 2. Kavramlar

- **Ayrılabilir miktar (Alış'ın söylediği):** onaylı **genel** satırın kalanıdır ve master INV-006'nın genişlemesidir:
  `committed − kabulNet − cancelled − closed − kesinRed/iadeEtkisi`. INV-006 `Onaylı − İptal − KabulNet − KesinRed/İadeEtkisi`
  der; `closed` terimi INV-006'da yoktur ve yazar okumasıdır (plan API-11 "iptalden ayrı eksen"). `kesinRed/iadeEtkisi`
  INV-006'nın `KesinRed/İadeEtkisi` terimidir: kesin red ve işaretsiz iade ile kalıcı olarak düşen miktarın tamamı (yalnız iade
  değil). Bugün kodda API-11 kapanışı, kesin red ve iade yoktur; `closed` ve `kesinRed/iadeEtkisi` sıfırdır ve formül
  `committed − accepted − cancelled` olur.
- **Port formülüne giriş (tek kural):** her terim porta onu ilk yazan dilimde girer, F-017 dilimlerinden önce gelse bile:
  `closed` API-11'de, `kabulNet` düşüşü ve `kesinRed/iadeEtkisi` API-44/API-60'ta (ve kesin red ekleyen kabul diliminde). Alan
  ekleme sözleşme sürüm etkisidir; port contract testi o dilimde genişler. F17-6 bu terimleri yazmaz, yalnız ayırma tarafından
  doğrular. F17-1 portu, main'de F17-1 merge anında bulunan bütün terimleri kapsar (API-11 veya API-44/API-60 F17-1'den önce
  gelirse onların terimleri F17-1'in ilk sürümüne girer). Teknik dilimler genel satırın içindedir; iki kez sayılmaz (mevcut kural).
- **Ayrılamayan satır:** Draft ve Cancelled siparişin satırı ve API-11 ile kapalı satır ayrılamaz; ayrılabilir miktarı 0'dır.
  Kapalı satır yeniden açılınca (§3.6) veya Cancelled sipariş / API-11 kapalı satır canlanınca (§3.8, ALS-D92) açılan kalan
  yeniden ayrılabilir olur.
- **"Kapalı" sözcüğü:** kodda kapalı sipariş durumu yoktur (`PurchaseOrderStatus` Draft/Approved/Cancelled,
  `PurchaseOrder.cs:6-16`). Bu belgede (a) **tam kabul** = Approved + satır kalanı 0, (b) **API-11 kapalı** = satır API-11 ile
  kapatılmış, (c) **Cancelled** = sipariş iptal edilmiş demektir. Yeni durum eklenmez. (b) ve (c) canlanmayla (§3.8) açığa
  döner.
- **Aktif ayırma:** Stok'ta durumu `Active` olan gelecek ayırma. `Consumed` (kabulde fiziksel tahsise dönmüş) ve `Released`
  (bırakılmış) ayırmalar aktif değildir. Bu belgede "bağlı ayırma", "ayırması olan satır" hep aktif ayırma demektir.
- **Ayrılmış miktar (Stok'un tuttuğu):** bir satınalma genel satırına bağlı aktif ayırmaların toplamı.
- **Değişmez (ALS-D82):** bir satınalma satırı için ayrılmış ≤ ayrılabilir. Aynı miktar iki satış siparişine verilemez. Değişmezin
  sahibi Stok'tur. Alış ayrılmış miktarı hesaplamaz ve saklamaz. Değişmezin iki modül arasında korunması §3.4 kilidiyle olur.
- **Satış talebi bağlantısı (Alış'ın bildiği):** satırın talep satırı bir satış siparişinden geldiyse (`SourceKind = Sales`), satır
  o siparişin niyetini taşır. Bu bir **niyet**tir, ayırma değildir. Ayırma kaydı her zaman Stok'ta açıkça kurulur. Alış, satış
  kaynaklı bir satır onaylanınca kendiliğinden ayırma oluşturmaz (okuma, geri alınabilir).
- **Genel / teknik satır (yazarın önerisi, geri alınabilir; #1116 S9):** ayırma **genel satıra** yapılır, teknik dilime yapılmaz.
  Ayrılabilir miktar genel satır düzeyindedir. Kabul teknik dilimden gelirse Alış kabul payı o dilimin bağlı olduğu genel satır
  kimliğini bildirir (§3.3); Stok çevirmeyi genel satırdaki ayırmalar üzerinden yapar. Teknik dilimin kendi kimliği Stok'ta
  yalnız izlenebilirlik alanıdır. Koordinatör notuna göre S9 teknik konudur: bu kural şerit kararıdır, geri alınabilir.

## 3. Port sözleşmesi taslağı (Alış ↔ Stok ortak yazar)

### 3.1 Alış → Stok: `IPurchaseFutureSupplyReadPortV1` (Alış sahibi; A3-0'ın yerine geçer)

A3-0'daki "`IPurchaseSupplyReferencePortV1` v2" ihtiyacını karşılar. v1 port değişmez; yeni port ayrı sürümlü arayüzdür.

```csharp
public interface IPurchaseFutureSupplyReadPortV1
{
    // Stok'un ayırma kaydında, §3.4 kilidi alındıktan sonra aynı işlemde okuduğu tek genel satır.
    // lineId teknik dilim kimliği ise sonuç NotAllocatable (Status = "technical-line") döner; genel satır kimliği ParentLineId'dedir.
    Task<PurchaseFutureSupplyAnswerV1> GetAllocatableLineAsync(Guid orderId, Guid lineId, CancellationToken ct = default);

    // Ayırma ekranının seçenek listesi: bir ürün için ayrılabilir genel satırlar, sipariş tarihi/satır no sırasıyla.
    Task<PurchaseFutureSupplyPageV1> ListAllocatableLinesAsync(Guid productId, int pageSize, string? cursor, CancellationToken ct = default);
}

public enum PurchaseFutureSupplyOutcomeV1 { Allocatable = 1, NotAllocatable = 2, NotFound = 3 }

public sealed record PurchaseFutureSupplyAnswerV1(
    PurchaseFutureSupplyOutcomeV1 Outcome, PurchaseFutureSupplyLineV1? Line, string? Status, Guid? ParentLineId);

public sealed record PurchaseFutureSupplyLineV1(
    Guid CompanyId, Guid OrderId, Guid LineId, int LineNo,
    Guid SupplierPartyId, Guid CompanyAccountId,
    Guid ProductId, string ProductVersion, Guid UnitId, string Kind,
    string CommittedText, string AcceptedText, string CancelledText,
    string AllocatableText,                                 // F17-1 anındaki §2 formülü (bugün committed − accepted − cancelled; INV-006 genişlemesi, canlanan miktar dahil); satırın birimi (UnitId), Ö2 tam ondalık metin
    Guid? SalesSourceOrderId, Guid? SalesSourceOrderLineId, // satış talebi niyeti (§2), yoksa null
    long ObservedOrderVersion);                             // yalnız gözlem; kilit/expected-version değildir

public sealed record PurchaseFutureSupplyPageV1(IReadOnlyList<PurchaseFutureSupplyLineV1> Items, string? NextCursor);
```

Kurallar:

- **Şirket parametre değildir.** Şirket sunucu bağlamından ve tenant filtresinden gelir (`PurchaseSupplyReferencePort.cs:22-30`
  deseni). Bağlamda şirket yoksa port `NotFound` döner (fail-closed). Başka şirketin siparişi `NotFound`'dur; 403 dönmez.
  `CompanyId` alanı yalnız yanıtın hangi bağlamda okunduğunu gösterir.
- Fiyat, iskonto, döviz, ödeme alanı yoktur (T28).
- Okuma hiçbir şey oluşturmaz. Port yetki taşımaz; tüketici kendi yetkisini ölçer (LOG-4 D6).
- **Birim (SAT-D298):** `AllocatableText` ve diğer miktarlar satınalma satırının kendi birimindedir (`UnitId`, Settings birim
  kimliği). Alış çevirmez. Karşılaştırma satınalma satırı biriminde yapılır: Stok kullanıcının kg/ton girdisini merkezi birim
  çevrim servisiyle (kaynak ürün kartı katsayısı) satırın birimine çevirir. Katsayı yoksa çevrim `NoSource`'tur ve ayırma
  reddedilir; tahmin veya ekran katsayısı yoktur (#1116 §3.2 adım 3, S10).
- **`ObservedOrderVersion` yalnız gözlemdir.** Stok bunu kayıtta saklayabilir; hiçbir modül onu modüller arası kilit veya
  expected-version olarak kullanmaz. Eşzamanlılık koruması §3.4'tedir. İstisna değil, ayrı bir şey: aynı host işlemi içindeki
  tutarlılık kontrolü (§3.7) expected-version veya kilit değildir. Host, aynı işlemde az önce yazdığı Alış sonucundan beklenen
  sürümü bilir; Stok yalnız porttan okuduğunun bu yazının kendisi olduğunu doğrular. Modüller arası eşzamanlılık yine §3.4
  kilidiyle korunur.
- **Termin dilimleri bu portta yoktur.** API-30 dilimleri (#1101) ayrı bir okuma olarak eklenir (F17-1b:
  `GetLineScheduleAsync(orderId, lineId)` → dilim kimliği, teslim tarihi, miktar, kalan). Böylece F17-1 ve F17-2 #1101'i
  beklemez. Ayırma kararı dilime bağlı değildir; dilim yalnız ekran bilgisidir.

### 3.2 Stok → Alış: `IInventoryFutureAllocationReadPortV1` (Stok sahibi; #1116 F017-b)

ALS-D30 "Alış okuma portu ayrılan miktarı da gösterir" ve §3.5 (ALS-D83) için. Alış Stok'a referans vermez; host adaptörü çevirir
(mevcut `PurchasingSalesLinkedSupplyHostPorts` deseni).

```csharp
// Bir satınalma genel satırına bağlı, aktif ayırmaların toplamı (satırın birimiyle, tam ondalık metin). Şirket bağlamdan.
Task<FutureAllocationSumV1> GetAllocatedOnPurchaseLineAsync(Guid orderId, Guid lineId, CancellationToken ct = default);
// Present(text) / None / Unreadable. Unreadable hiçbir zaman "sıfır" sayılmaz (fail-closed).

// Satırda kabulde tüketilmiş (Consumed; fiziksel tahsise dönmüş) gelecek ayırma var mı. Şirket bağlamdan.
Task<FutureAllocationPresenceV1> GetConsumedPresenceOnPurchaseLineAsync(Guid orderId, Guid lineId, CancellationToken ct = default);
// Present / None / Unreadable. Unreadable "yok" sayılmaz; çağıran yazmadan reddeder (fail-closed).
```

- Tüketilmiş sinyali kabul geri alma/iade korumasının (§4) ve §3.7 Stok geri almasının girdisidir. Aktif toplam tüketilmiş
  ayırmayı göstermez; ikisi ayrı okunur.

### 3.3 Mal kabulü (aynı host işlemi)

- Alış'ın kabul payı (`IPurchaseAcceptanceParticipantV1.StageAsync`) ve Stok'un mal girişi (`InventoryGoodsReceiptRequestV1`)
  zaten aynı host işlemindedir ve ikisi de satınalma satırı kimliğini taşır.
- **Teknik dilim eşlemesi:** kabul teknik dilim satırına yapılırsa Alış kabul sonucu (`PurchaseAcceptanceStageResultV1`) dilimin
  genel satır kimliğini de taşır (F17-1'de eklenen alan; v1 sözleşmesi sürüm artırılarak genişletilir). Host, Stok'a bu genel
  satır kimliğini verir. Kabul genel satıra yapılırsa iki kimlik aynıdır.
- Stok aynı işlemde genel satırın ayırmalarını kabul edilen miktar kadar fiziksel tahsise çevirir ve etiketi satış sipariş satırına
  atar (ALS-D82 "etiket ataması o zaman yapılır"). Replay çiti çevirmeyi kapsar.
- **#1116 §5 hizalaması:** #1116 §5 "mal kabulde yeni Alış alanı gerekmez" der. Bu yalnız genel satıra yapılan kabul için
  doğrudur. Teknik dilime kabul için genel satır kimliği alanı (yukarıda) ve kullanıcı sırası için host komutundaki
  `AllocationOrder` gerekir. İki belge F17-1/F17-3 PR'larında aynı metne getirilir.
- **Bağlama hatası (ALS-D89):** çevirme **iş nedeniyle** yapılamazsa (etiket blokta (S8), birim çevrimi `NoSource`, satış satırı
  onaysız veya iptal) mal kabulü durmaz; mal stoğa girer, ilgili ayırma Stok'ta bırakılır (`reasonCode =
  acceptance-link-failed`) ve satışa uyarı olayı yazılır. Kabul geri alınmaz. **Teknik hata** (veritabanı hatası, kilit/eşzamanlılık
  çakışması, zaman aşımı) iş nedeni değildir: host işleminin tamamı geri alınır, kabul de yazılmaz (yazar okuması, #1116 §3.5 ile
  aynı; geri alınabilir).
- **Az/fazla gelme (ALS-D87):** az gelirse gelen miktar sıraya göre ayırmalara çevrilir, kalan ayırma satırın gelecek kısmında
  aktif kalır. "Satır kapanırsa" API-11 eksik teslim kapanışıdır (ALS-D10/D12): kapanışta kalan aktif ayırmalar bırakılır ve
  satış uyarılır (§3.6). Sipariş kalanının iptali bu madde değildir; o ALS-D85 akışıdır (§3.5). Fazla gelen kısım (ayrılmış
  toplamı aşan kabul miktarı) serbest stoğa girer, ayırmaya bağlanmaz. **Kabul kuralı değişmez:** kalan taahhüdü aşan kabul bugün
  `AcceptanceExceedsRemaining` ile reddedilir (`PurchaseOrder.cs:339`); F-017 bunu gevşetmez. Toleranslı fazla teslim ayrı
  iştir (No 40/ALS-D32, API-40); geliştirici tolerans eklemez.
- **Kabul geri alma/iade (ALS-D86, ALS-D91):** §3.7.
- **Sıra (ALS-D84):** satıra birden çok ayırma varken kabul edilen miktar varsayılan olarak **satış sipariş satırının termini en
  yakın** ayırmaya önce gider. Öncelik sırası #1116 ile aynıdır: kabul anında verilen `AllocationOrder` > Stok'taki kalıcı
  `ManualSequence` (#1116) > satış sipariş satırı termini > ayırmanın oluşturma zamanı. Son iki eşitlik kuralı tekniktir, geri
  alınabilir. Kabul eden kullanıcı sırayı **değiştirebilir veya ayırma seçebilir**: host
  mal kabul komutuna isteğe bağlı `AllocationOrder` (genel satır başına sıralı ayırma kimlikleri) eklenir ve Stok'a iletilir. Boşsa
  varsayılan sıra uygulanır. Listedeki her kimlik o genel satırın aktif ayırması olmalıdır; değilse 422, hiçbir şey yazılmaz.
  Listede olmayan ayırmalar listedekilerden sonra varsayılan sırayla gelir. Seçilen sıra kabul yük hash'ine girer (replay).
  Termin satış sipariş satırı portundan okunur; Alış satış terminini saklamaz. Ekran varsayılan sırayı gösterir, kullanıcı
  sürükleyerek veya seçerek değiştirir (F17-5/kabul ekranı).
- **Replay çiti çevirmeyi kapsar:** kabulün mevcut idempotency kaydı (anahtar hash + yük hash) çevirmeyi de kapsar. Aynı anahtarla
  tekrar gelen kabul yeniden çevirme yapmaz; saklanan sonucu döndürür. Katılımcının saklanan sonuç özeti çevirme sonucunu içerir
  (tüketilen ayırma kimlikleri ve miktarları, oluşan fiziksel tahsis kimlikleri). Aynı anahtar + farklı istek (yük hash'i) = 409;
  problem kodu mevcut kabul kodudur (`purchasing_acceptance_idempotency_conflict`, `IPurchaseAcceptanceParticipantV1.cs:116`).
- **Alış tarafı:** kabul sonucu satırın kabul sonrası kalanını zaten döndürür (`RemainingQuantity`). Satış talebi niyeti (§2)
  gerektiğinde `GetAllocatableLineAsync` ile okunur.

### 3.4 Modüller arası yarış: ortak satır kilidi (P1)

Sorun: Stok "Alış'tan oku → ayrılmış toplamı kontrol et → yaz" yapar. Bu arada Alış aynı satırın kalanını (kabul, kalan iptali,
ileride azaltma) değiştirirse ayrılmış > ayrılabilir olabilir (check-then-write yarışı). Stok'un kendi defter satırı concurrency
token'ı yalnız Stok'un iki ayırmasını sıralar; Alış yazısını görmez.

Öneri (teknik; geri alınabilir):

- Host düzeyinde `(şirket, satınalma genel satırı)` anahtarlı **işlem kapsamlı özel uygulama kilidi** (SQL Server
  `sp_getapplock`, `@LockOwner = 'Transaction'`, `@LockMode = 'Exclusive'`). Kaynak adı:
  `goymen:purchase-line:{companyId:N}:{lineId:N}`. Şirket kimliği sunucu bağlamından alınır.
- Arayüz host/BuildingBlocks'ta tek yerde durur (öneri ad: `IPurchaseLineSerializationLockV1.AcquireAsync(Guid lineId, ct)`);
  iki modül de host adaptörü üzerinden çağırır, birbirine referans vermez.
- **Kilidi kim alır:** (1) Stok ayırma oluşturma (Alış okumasından **önce**), (2) Stok ayırma bırakma, (3) Alış kabul payı
  (kabul edilen her genel satır için; aynı işlemdeki Stok çevirmesi aynı sahipte olduğu için yeniden kilitlemez), (4) Alış sipariş
  kalanı iptali ve "bağları düşür ve iptal et" (etkilenen her genel satır), (5) ileride Alış satır azaltma (F17-4), (6) API-11
  satır kapanışı ve yeniden açma (§3.6), (7) kabul düzeltmesi ve tedarikçiye iade (§3.7; Alış payı ve Stok geri alması aynı
  işlemde, aynı sahip).
- **Sıra:** birden çok satır kilitlenecekse `lineId` artan sırayla alınır (kilitlenme önlemi). Satış satırı kilidi gereken
  yollarda önce satış satırları (birden çoksa artan satış satırı kimliğiyle, hepsi alış kilitlerinden önce; satış ve alış
  kilitleri iç içe geçmez), sonra alış satırları artan `lineId` ile alınır (#1116 §3.2 adım 0, §4.3): Stok ayırma oluşturma,
  Satış satır iptali/azaltması ve kabul geri almada tüketilmiş ayırmanın `Consumed → Active` yeniden açılması (koordinatör kararı (d), #66 6824928883 F-017 (a)–(d) koordinatör kaydı; merged #1116 (merge `1af6ffea`) §3.2 adım 0, §3.7 ve KLN-D28 (7)'de yazar okuması). Satış satırı kilidinin arayüzü de host/BuildingBlocks'tadır; modüller birbirine
  referans vermez.
- **Zaman aşımı:** kısa (öneri 5 sn). Alınamazsa işlem yazmadan 409 `purchasing_line_busy` ile biter; replay değilse istemci yeniler.
  Kilit dönüş kodu < 0 ise asla devam edilmez (fail-closed).
- Kilit işlem bitince (commit/rollback) kendiliğinden bırakılır. Uzun süreli kilit yoktur.
- Kilit alındıktan sonra Stok Alış'ı aynı işlemde okur; ayrılmış + yeni ≤ ayrılabilir değilse 409
  `inventory_future_allocation_insufficient_future_supply` (Stok'un kodu, #1116 §7 ile aynı).
  Alış tarafı kilit altında §3.2 portuyla ayrılmış toplamı okur; yeni kalan ayrılmışın altına düşecekse reddeder (§3.5).
- Gerçek SQL testi: iki eşzamanlı işlem (Stok ayırma + Alış kalan iptali) aynı satırda; biri bekler veya 409 alır, sonuçta
  ayrılmış ≤ ayrılabilir. Kilit kaynağı adı contract testine bağlanır.

### 3.5 Satınalma tarafından bağ düşürme (ALS-D83)

- Satınalma kullanıcısı, bir satınalma genel satırına bağlı satış ayırmasını **Alış ekranından** düşürebilir. Bu, ayırmanın Stok'ta
  bırakılmasıdır (`Released`); Alış Stok tablosuna yazmaz. Alış komutu host adaptörü üzerinden Stok'un yazma portunu çağırır
  (öneri: `IInventoryFutureAllocationReleasePortV1.ReleaseAsync(allocationId, expectedAllocationVersion, reasonCode, reasonText,
  origin = "purchasing")`, Stok sahibi; #1116 §3.3 bırakma akışının aynısı). Şirket sunucu bağlamından gelir.
- **Satınalma miktarı azalmaz.** Committed, accepted, cancelled değişmez; ayrılabilir miktar bağ kadar yeniden serbest kalır.
  Serbest miktar başka satış siparişine ayrılabilir veya stok için alınır (ayrı işlem gerekmez; ayrılmamış miktar zaten stok
  içindir).
- Neden zorunludur. Sessiz bırakma yoktur (DEC-STK-005): Stok `inventory.future-allocation.released.v1` olayını `origin =
  purchasing` ile yazar; satış ekranı bunu inbox/dedup ile tüketip satış sipariş satırında "kaynak düştü" uyarısı gösterir.
- Komut §3.4 kilidini alır, idempotency anahtarı ve audit (önce/sonra, neden, aktör) taşır. İzin adı önerisi
  `purchasing.future-allocation.release` (ad kaydı teknik; grant insan kapısı).
- **Neden kodu ve gerekçe metni ayrıdır.** `reasonCode` makine kodudur ve satışa giden uyarının türünü belirler; `reasonText`
  kullanıcının yazdığı gerekçedir (audit'e girer, uyarıda gösterilir). Neden kodu listesi (tek yer; Stok olayında taşınır):

  | `reasonCode` | Nerede | Satışa uyarı |
  |---|---|---|
  | `purchasing-link-dropped` | Satınalma tarafı bağ düşürme (bu bölüm, ALS-D83) | "kaynak düştü" |
  | `source-cancelled` | Bağları düşür ve iptal et (ALS-D85) | "kaynak iptal edildi" |
  | `purchase-line-closed` | API-11 satır kapanışı (§3.6, ALS-D87) | "kaynak eksik teslimle kapandı" |
  | `acceptance-link-failed` | Kabulde iş nedenli bağlama hatası (§3.3, ALS-D89) | "kabulde bağlanamadı" |
  | `acceptance-reversed-no-supply` | Kabul geri alma, yeniden açılacak gelecek miktar yok (§3.7, ALS-D86/D91) | "kabul geri alındı, kaynak yok" |

  Stok'un kendi bırakma nedenleri (#1116) bu listeye eklenmez; satış ekranı ikisini birlikte gösterir.
- **Satır azaltma** (ileride F17-4): yeni kalan, ayrılmış toplamın altına düşecekse Alış işlemi reddeder ve kullanıcıya önce bağ
  düşürmesini söyler (ALS-D83 okuması: miktar bağ yüzünden kendiliğinden azalmaz; bağ önce açıkça düşürülür).
- **Sipariş kalanı iptali (ALS-D85):** kodda satır düzeyinde iptal yoktur; iptal onaylı siparişin tüm kalanıdır (§1). Ayırması
  olan bir satır varsa sipariş doğrudan iptal edilmez: düz iptal komutu (API-10) kilit altında aktif ayırma görürse 409
  `purchasing_line_has_allocations` ile reddeder ve bağlı aktif ayırmaları (ayırma kimliği, sürüm, satış sipariş satırı, miktar)
  döner. Ekran bunları listeler ve "bağları düşür ve iptal et" seçeneği sunar.
- **"Bağları düşür ve iptal et" komutu (ALS-D85):**
  - İstek: sipariş kimliği, `ExpectedVersion`, zorunlu `reasonText`, idempotency anahtarı ve kullanıcının gördüğü küme
    `SeenAllocations = [(allocationId, expectedAllocationVersion)]`.
  - Host komutu etkilenen genel satırların §3.4 kilitlerini artan sırayla alır, kilit altında her satırın aktif ayırmalarını
    §3.2 portundan okur. Aktif küme `SeenAllocations` ile birebir aynı değilse (yeni ayırma açılmış, biri kabulde tüketilmiş veya
    bırakılmış, sürüm değişmiş) 409 `purchasing_future_allocation_set_changed` döner ve hiçbir şey yazılmaz. Kullanıcının görmediği ayırma
    sessizce bırakılmaz.
  - Küme aynıysa aynı işlemde önce her ayırma için Stok bırakma portu (`reasonCode = source-cancelled`, `reasonText` kullanıcının
    gerekçesi) çağrılır, sonra Alış iptali yazılır. Yalnız aktif ayırmalar bırakılır; tüketilmiş ayırmalara ve fiziksel tahsislere
    dokunulmaz. Biri başarısızsa işlemin tamamı geri alınır.
  - **Host orkestrasyonu:** bugünkü `CancelOrderAsync` tek Alış store kaydıdır (`PurchasingService.OrderCancellation.cs:54`).
    Birleşik komut, mal kabulündeki gibi host düzeyinde tek işlem açar: Alış iptal payı kabul katılımcısı desenine benzer bir
    katılımcı (`IPurchaseCancellationParticipantV1.StageAsync`, öneri) olarak, Stok bırakmaları host adaptörüyle aynı
    `DbTransaction` içinde yazılır.
  - **Sıra:** idempotency replay kontrolü `ExpectedVersion` ve `SeenAllocations` karşılaştırmasından **önce** yapılır. Başarılı
    komuttan sonra aktif küme boşaldığı için aynı anahtarla gelen tekrar saklanan sonucu alır, 409 almaz.
  - **Replay:** idempotency kaydının sahibi host komutudur (kabuldeki gibi tek kayıt). İstek hash'ine sipariş kimliği,
    `ExpectedVersion`, `reasonText` ve sıralı `SeenAllocations` girer. Saklanan sonuç bırakılan ayırma kimliklerini ve yeni sipariş
    sürümünü içerir. Aynı anahtar + farklı istek = 409 (ad mevcut desene uyar, öneri
    `purchasing_order_cancellation_idempotency_conflict`; F17-4'te kesinleşir).
  - **İzin (teknik):** komut hem `purchasing.orders.cancel` hem `purchasing.future-allocation.release` ister; biri yoksa 403.
    Grant insan kapısıdır.
  - Gerekçe, aktör, önce/sonra ve bırakılan ayırmalar audit'e yazılır.
- **Süre ve termin uyarısı (ALS-D90):** ayırmanın son tarihi yoktur; süre ileride ayarlardan tanımlanabilir (Sinan metni), bugün
  ayar yoktur. Satınalma satırının termini geçince satış ve satınalma kullanıcısı uyarılır, ayırma aktif kalır. Uyarı **okuma
  anında hesaplanır** (olay yok, zamanlanmış iş yok): Alış satır termini (F17-1b) bugünden önceyse ve satırda aktif ayırma varsa
  ekran "termin geçti" gösterir. Satınalma tarafı F17-4 ekranında, satış tarafı F17-5 kaynak görünümünde gösterir (yazar
  önerisi, geri alınabilir).
- **Kim ayırır (ALS-D88):** ayırma izni olan kullanıcı (satış, satınalma veya depo) tek başına ayırır; ikinci onay yoktur. İzin
  adı kodlanır; grant insan kapısıdır.

### 3.6 Satır kapanışı (API-11) ve ayırma (ALS-D87, ALS-D10/D12)

- API-11 eksik teslim kapanışı satırın kalanını kapatır (iptalden ayrı eksen). Kapanış Alış'ta olur; Stok bunu kendiliğinden
  görmez. Bu yüzden bırakmayı Alış komutu yapar: kapanış komutu §3.4 kilidini alır ve aynı host işleminde satırın aktif
  ayırmalarını Stok bırakma portuyla `reasonCode = purchase-line-closed` (gerekçe metni kapanış gerekçesi) ile bırakır. Biri
  başarısızsa kapanış da yazılmaz. #1116 §3.5'teki `purchase-line-closed` bu çağrıdır.
- ALS-D10 tolerans içi kapanışta sistem önerir ve sorar; ALS-D12 tolerans dışını satın alma sorumlusu gerekçeyle kapatır. Onay
  ekranı satırın bağlı aktif ayırmalarını (satış sipariş satırı, miktar) gösterir; kullanıcı bunların bırakılacağını görerek
  onaylar (UX şerit kararı, geri alınabilir). İstek "bağları düşür ve iptal et"teki gibi `SeenAllocations` taşır; farklıysa 409
  `purchasing_future_allocation_set_changed`. Replay kontrolü küme ve sürüm karşılaştırmasından önce yapılır (§3.5).
- **İzin (teknik):** satırda aktif ayırma yoksa kapanış yalnız `purchasing.orders.close` ister. Aktif ayırma varsa
  `purchasing.orders.close` + `purchasing.future-allocation.release` ister; biri yoksa 403 (§3.5 ile aynı bileşim). Grant insan
  kapısıdır.
- **Yeniden açma:** kapanan satır yeniden açılınca (ALS-D10) bırakılmış ayırmalar geri gelmez. Açılan kalan yeniden ayrılabilir
  olur; satış isterse yeni ayırma yapar (**yazar okuması, kapalı yön**; #1116 §3.6 ile aynı etiket; canlanmadaki karşılığı Sinan
  kararıdır, ALS-D93, §3.8).
- Dilim (tek kural): API-11 F17-4'ten önce merge edilirse kapanışta bırakmayı F17-4 ekler; F17-4'ten sonra merge edilirse
  bırakma API-11 diliminin içindedir (§4). Ayırma oluşturma yolu F17-4'ten önce açılmadığı için (§4 kapı kuralı) arada bırakılacak
  ayırma oluşmaz.

### 3.7 Kabul geri alma ve tedarikçiye iade (ALS-D86, ALS-D91)

Bugün kodda kabul geri alma/iade yoktur (§1). Akışın kendisi plan API-44 (kabul düzeltmesi: ters + yeni) ve API-60 (tedarikçiye
iade) dilimleridir. Bu bölüm o dilimlerin F-017 kurallarıdır; uygulama F17-6'dır (§4).

- **Gelecek miktar ne zaman açılır (ALS-D91, ALS-D92):** Sinan metni (aynen, EK17 ALS-D91): "Hatalı kabul geri alınırsa
  miktar siparişe geri açılır. Tedarikçiye iadede kullanıcı 'yerine yenisi gelecek' işaretlerse açılır, işaretlemezse sipariş
  kapalı kalır." Bu metin **tam kabul** edilmiş, kalanı açık (Approved), **Cancelled** ve **API-11 kapalı** satırda koşulsuz
  uygulanır (ALS-D92: "Açılır, sipariş canlanır."). Cancelled ve API-11 kapalı durumdan açığa dönüş §3.8'dedir.
- **INV-006 eşlemesi (yazar okuması, geri alınabilir):** master INV-006 `Onaylı − İptal − KabulNet − KesinRed/İadeEtkisi`.
  - Hatalı kabulün geri alınması (API-44 ters kayıt): `KabulNet` düşer, kalan aynı miktar kadar artar.
  - İade, işaretli: `KabulNet` düşer, `KesinRed/İadeEtkisi`'ne yazılmaz; kalan artar.
  - İade, işaretsiz: `KabulNet` düşer ve aynı miktar `KesinRed/İadeEtkisi`'ne yazılır; kalan artmaz. "Sipariş kapalı kalır" =
    satır kalanı değişmez (tam kabulde 0 kalır, Cancelled/API-11 kapalıda canlanma olmaz); yeni durum yoktur.
  - Kalanı 0 olan (tam kabul) satırda açılan miktar satırı yeniden açık yapar; yeni durum gerekmez, satır kalanı pozitif olur.
- **Cancelled ve API-11 kapalı satır (ALS-D92):** geri alınan / işaretli iade edilen miktar açılır ve sipariş/satır canlanır
  (§3.8). Açılan miktar gelecek miktardır; aşağıdaki Stok kuralları tam kabul satırıyla aynı uygulanır.
- **"Yerine yenisi gelecek" işaretinin yeri:** API-60 isteğinde zorunlu alan (`ReplacementExpected`, bool; öneri). Audit'e ve
  idempotency istek hash'ine girer; iade yazıldıktan sonra değiştirilemez (düzeltme ters iade + yeni iade ile).
- **Host işlemi sırası (teknik, kabul akışı §3.3 ile aynı yön):**
  1. §3.4 kilitleri. Tüketilmiş ayırma `Consumed → Active` yeniden açılabilecekse kilitlenecek satış satırları, kilitten önce
     Stok'un bu alış satırlarındaki tüketilmiş kayıtlarından okunan kümeden çıkar. Sıra: satış satırları (birden çoksa artan
     satış satırı kimliğiyle, hepsi alış kilitlerinden önce, iç içe geçmeden), sonra alış satırları artan `lineId` (§3.4 Sıra).
     Protokol: küme oku → kilitle → kilit altında kümeyi yeniden oku; küme büyüdüyse kilitler bırakılıp yeniden denenir
     (sınırlı deneme, sonra 409; #1116 §3.2 adım 0 ve §3.7 ile aynı, yazar okuması, geri alınabilir).
  2. Alış geri alma/iade payını ve varsa canlanmayı (§3.8) yazar ve aynı `DbTransaction` içinde flush eder. Alış sonucu host'a
     döner: yeni sipariş sürümü, canlandı mı, satırın yeni kalanı (beklenen ayrılabilir miktar).
  3. Stok, Alış portunu (§3.1 `AllocatableText`) ve kendi tüketilmiş kaydını (§3.2) aynı işlemde okur. Host 2. adımdaki Alış
     sonucundan beklenen durumu Stok adımına verir.
  4. Stok tahsis/etiket geri alması ve `Consumed → Active` / bırakma.
- **Tutarlılık kontrolü (host'un bildiğine dayanır, port cevabına değil):** kontrol yalnız Stok'un yeniden açacağı veya
  bırakacağı bir ayırma varken yapılır. "Açılan miktar" host'un Alış sonucundaki bu işlemle açılan net miktardır (§3.8, satır
  başına); satırın kalanının pozitif olması tek başına açılma demek değildir (ör. Approved satırda başka kabul edilmemiş miktar
  varken işaretsiz iade). Host'un Alış sonucuna göre açılan miktar varsa Stok porttan
  `Outcome = Allocatable` **ve** `ObservedOrderVersion` = beklenen sürüm **ve** `AllocatableText` = beklenen miktar okumalıdır
  (`AllocatableText` decimal olarak ayrıştırılıp sayısal karşılaştırılır, metin karşılaştırması yapılmaz).
  Başka her cevap (`NotAllocatable`, `NotFound`, `Line` null, sürüm veya miktar farklı, `Unreadable`) bayat okumadır ve işlemin
  tamamı yazmadan geri alınır; ayırma bırakılmaz, `acceptance-reversed-no-supply` yazılmaz. Böylece port işlem dışı bir
  bağlantıdan Cancelled siparişi bayat `NotAllocatable` okursa satış bağı kaybolmaz. Host'un Alış sonucuna göre açılan miktar
  yoksa (işaretsiz iade, canlanmasız durum, net 0 düzeltme) Stok `NotAllocatable` / `Allocatable(R)` ayrımına bakmaz (ikisi de
  meşrudur) ve bırakma kuralını uygular; ancak `Unreadable`, `NotFound` veya `Line` null okunursa yine
  bayat okuma gibi işlemin tamamı yazmadan geri alınır ve 503 döner, kaydedilmez (fail-closed; yazar okuması, geri alınabilir);
  aynı düzeltmedeki yeni kabulün yeniden tükettiği ayırmalar bırakılmaz (§3.8 net açılan miktar). Ayrım port cevabından değil
  host'un Alış sonucundan yapılır. Bu kontrol §3.1'deki işlem içi tutarlılık kontrolüdür, expected-version değildir.
- **Stok iş reddi Alış yazısını da geri alır:** 4. adımda Stok bir iş kuralıyla reddederse (sevk edilmiş/tüketilmiş etiket; bugün
  bu sınıfta tek kod 409 `inventory_future_allocation_already_fulfilled`, sınıfa eklenecek kodlar F17-6'da kesinleşir) host işleminin tamamı geri alınır; Alış geri alma/iade payı ve canlanma da
  yazılmaz. Bu, kabul akışındaki ALS-D89'dan farklıdır (orada iş nedenli çevirme hatasında kabul tamamlanır): geri almada
  önce satış/sevk tarafında ters işlem gerekir.
- **Sonuç sınıfı ve replay (teknik; `k3-command-skeleton` terminal ret deseni, #1116 §5.1 (c) satış tarafındaki aynası):**
  - Bayat okuma, `Unreadable` ve satır kilidi meşgul terminal değildir: işlem geri alınır, idempotency kaydı yazılmaz, istemci
    yeniden denenebilir hata alır (bayat okuma ve `Unreadable` için 503, kilit meşgul için §3.4 409'u); aynı anahtar yeniden
    yürütülür.
  - Stok iş reddi (bugün yalnız 409 `inventory_future_allocation_already_fulfilled`) terminaldir: geri alınan işlemden sonra ret kaydını
    API-44/API-60 host komutunun kendisi yazar, Stok hiçbir şey yazmaz; aynı anahtarla tekrar saklanan 409'u Stok'u yeniden
    çağırmadan döndürür.
  - Değerlendirme modeli ve karışık durum (koordinatör okuması #66 6825926365; teknik, geri alınabilir):
    1. Kilit evresi: önce bütün kilitler alınır (host işlemi sırası adım 1: kilit öncesi küme okuması, satış satırları, sonra
       alış satırları artan `lineId`; §3.4). Bir kilit meşgulse deneme o kilidin kendi 409'uyla biter (satış satırı kilidi
       için Satış tarafının kodu, alış satırı için `purchasing_line_busy`). Kilit altında küme büyüdüğü için sınırlı deneme
       tükenirse deneme 409 `inventory_concurrent_change` ile biter (ad öneridir, F17-6'da kesinleşir). Bu evrede biten
       denemede Alış yazısı ve port okuması yapılmaz (tükenmeyi kilit altındaki küme yeniden okuması tespit eder, host işlemi
       sırası adım 1), hiçbir şey kaydedilmez, aynı anahtar yeniden yürütülür.
    2. Kilitler alındıktan sonra Alış yazısı (host işlemi sırası adım 2) yapılır, ardından satırlar sabit sırayla
       değerlendirilir (satış satırı, sonra artan `lineId`). Bu akışta bugün tek terminal sonuç Stok iş reddi 409
       `inventory_future_allocation_already_fulfilled`'dır; toplanır, değerlendirmeyi durdurmaz. Bu akışta kaydedilen bir
       422 yoktur (`AllocationOrder` 422'si kabul akışınındır, §3.3).
       Alış retleri Alış yazısında, Stok değerlendirmesinden önce oluşur; Stok portu çağrılmaz, satır değerlendirmesine ve
       karışık duruma girmez. Sınıflama ev iskeletinin catch zinciridir (`k3-command-skeleton` adım 10 ve "rule refusal 422
       not recorded"): sürüm çakışması ve durum retleri kaydedilen terminal 409 olur (ör. 409 `version_conflict`); kullanıcının
       yeni girdiyle düzeltebileceği kural retleri 422 `validation_failed` olur ve kaydedilmez. Bugün tek modüllü Alış
       komutlarında yalnız `version_conflict` ve açıkça eşlenmiş durum kodları kaydedilir, eşlenmemiş kural retleri kaydedilmeyen
       422'dir (`PurchasingService.cs:169-182`). API-44/60'ta kod bazında eşleme F17-6'da kesinleşir (yazar okuması, teknik,
       geri alınabilir idempotency sınıflaması).
    3. İlk terminal olmayan sonuç (503 bayat okuma / `Unreadable`, port istisnası, zaman aşımı, 409
       `inventory_concurrent_change`) değerlendirmeyi bitirir: işlem geri alınır, istemciye o sonucun durum ve kodu döner
       (ör. 503 veya 409 `inventory_concurrent_change`), hiçbir şey kaydedilmez, aynı anahtar yeniden yürütülür. Daha önce
       toplanan terminal sonuç döndürülmez ve kaydedilmez.
    4. Kilit meşgul ile 503 aynı denemede oluşamaz (meşgul kilit okumadan önce denemeyi bitirir).
    5. Terminal olmayan sonuç yoksa ve en az bir terminal varsa sabit sıradaki ilk terminal kaydedilir ve döndürülür; aynı
       anahtar saklanan sonucu portu çağırmadan döndürür. Bugün tek terminal kod olduğundan "farklı terminal kodlar" durumuna
       ulaşılamaz; bu kural ikinci bir terminal kod eklenirse geçerlidir.
  - Sınıflandırılmamış durumlar (port istisnası, zaman aşımı, `inventory_concurrent_change`) terminal değildir ve yukarıdaki
    3. adıma girer (sınırlı deneme tükenmesi 1. adımdadır): işlem geri alınır, idempotency kaydı yazılmaz, aynı anahtar
    yeniden yürütülür. Stok tarafında yalnız yukarıda adı geçen Stok iş reddi terminaldir. Kodlar F17-6'da kesinleşir.
- **Stok tarafı (aynı host işlemi, §3.4 kilidi altında, yukarıdaki sırayla):** geri alınan etiketlere bağlı fiziksel tahsis ve
  etiket ataması geri alınır.
  - Gelecek miktar varsa ilgili ayırma aynı ayırma kimliğiyle `Consumed` → `Active` döner (sürüm artar), ayrılmış + yeniden
    açılan ≤ ayrılabilir kontrolüyle.
  - Gelecek miktar yoksa veya yetmiyorsa karşılanamayan kısım bırakılır (`reasonCode = acceptance-reversed-no-supply`) ve satış
    uyarılır. Kısmi yeniden açma Sinan metninin (var/yok) genellemesidir: **yazar okuması, geri alınabilir**; #1116 ile aynı metne
    getirilir.
- **Fail-closed teknik kurallar (yazar okuması, geri alınabilir):**
  - Etiket satışa sevk edildiyse veya tüketildiyse o etiket için geri alma 409 `inventory_future_allocation_already_fulfilled` ile reddedilir;
    önce satış/sevk tarafında ters işlem gerekir.
  - Satış tarafı fiziksel tahsisi bıraktıysa veya satış satırı iptal edildiyse yeniden açılacak ayırma yoktur: yalnız etiket
    geri alınır, ayırma açılmaz, uyarı gerekmez. Satış satırı kilit alındıktan sonra yeniden okunur; satır iptal veya kapalıysa
    yeniden açma aktif gelecek ayırma oluşturmaz (koordinatör kararı (d), #66 6824928883 F-017 (a)–(d) koordinatör kaydı; merged #1116 (merge `1af6ffea`) §3.2 adım 0, §3.7 ve KLN-D28 (7)'de yazar okuması). "Aynı kimlikle yeniden açılır" yalnız fiziksel tahsis hâlâ aktifse geçerlidir.
  - Kısmi geri almada önce geri alınan etiketlerin bağlı olduğu tahsisler geri alınır. Etiket ayrımı yoksa ALS-D84 sırasının
    tersi uygulanır: termini en uzak satış önce geri alınır, termini en yakın olan korunur. Yeniden açma da aynı sırayla yapılır.
- **API-44 ters + yeni çevirme sırası:** aynı düzeltmede ters kayıt bir ayırmayı yeniden açarsa yeni kabul önce o ayırmaları
  (aynı ayırma kimliği) tüketir, artan miktar ALS-D84 sırasına göre gider. Böylece düzeltme malın gideceği satışı değiştirmez
  (yazar okuması, geri alınabilir).
- Geri alma ve iade komutları §3.4 kilit listesindedir (7). Replay kaydı akışın kendi host komutundadır; çevirme geri alması
  sonuç özetine girer.

### 3.8 Canlanma: Cancelled sipariş ve API-11 kapalı satırın açığa dönüşü (ALS-D92)

Sinan metni (aynen, #66 6822140358 m.1): "Açılır, sipariş canlanır." Koordinatör kaydındaki açıklama: miktar siparişe geri
açılır; iptal/kapalı satır yeniden açık olur ve gelecek tedarikten ayrılabilir. Aynı notun teknik biçim listesi: "Cancelled/API-11
kapalı durumdan açığa dönüş, iptal/kapanış payının düşmesi, audit, expected version, §3.4 kilidi". Önceden bırakılmış ayırmalar için
Sinan metni (aynen, #66 6822767886 m.3, ALS-D93): "Geri gelmez, yeniden ayrılır (Önerilen)". Aşağısı canlanmanın teknik biçimidir
(**K3**, §4 canlanma satırı); yeni iş politikası seçmez.

- **Tetikleyici:** yalnız Cancelled siparişin veya API-11 kapalı satırın üzerinde (a) hatalı kabulün geri alınması (API-44 ters
  kayıt) veya (b) `ReplacementExpected = true` iade (API-60). İşaretsiz iade ve kesin red canlandırmaz. Başka yol (elle
  "iptali geri al") yoktur; API-11'in kendi yeniden açması (ALS-D10, §3.6) ayrı akıştır.
- **Net açılan miktar (yazar okuması, teknik tanım: Sinan cevabı "Açılır, sipariş canlanır." miktarın açılmasına bağlıdır;
  miktar açılmazsa canlanma yoktur):** API-44 her düzeltmeyi "ters + yeni" olarak
  kaydeder. Canlanma tek düzeltmedeki **net** açılan miktara göre belirlenir: net = ters kayıt miktarı − aynı düzeltmedeki yeni
  kabul miktarı. Net **genel satır başına** hesaplanır; birden çok satırı etkileyen düzeltmede her satır kendi neti ile
  değerlendirilir, sipariş canlanması net > 0 olan en az bir satır varsa olur (yazar okuması, geri alınabilir).
  - Net > 0: canlanma olur, satır kalanı net miktardır (aşağıdaki X = net). §3.7 tutarlılık kontrolünde beklenen değer net
    değişim sonrası kalandır (Approved satırda da). Yeni > 0 iken ALS-D82 yeniden açma kontrolü ve
    `acceptance-reversed-no-supply` bırakması da net son duruma göre yapılır.
  - Net = 0 (ör. yalnız lot/etiket düzelten, miktarı değişmeyen düzeltme: ters X + yeni X): sipariş ve satır durumu
    **değişmez**. Cancelled sipariş Cancelled, API-11 kapalı satır kapalı kalır; canlanma olayı ve "canlandı" bildirimi
    üretilmez; iptal/kapanış kaydına dokunulmaz. Net < 0 (yeni > ters) yeni kabul gibi kalan taahhüde karşı ölçülür
    (`PurchaseOrder.cs:337-341`): kalanı 0 olan satırda (Cancelled, API-11 kapalı veya tam kabul) `AcceptanceExceedsRemaining`
    ile reddedilir (Cancelled siparişte bugün önce `RequireNotCancelled` reddeder; aşağıdaki Kod/DB kabul kapısı); Approved
    satırda kalan içinde kalan net < 0 meşrudur.
  - Net = 0'da Stok tarafındaki "ters + yeni" çevirmesi (aynı ayırma kimliğiyle `Consumed → Active → Consumed`) işlem içi geçici durumdur;
    `acceptance-reversed-no-supply` yazılmaz. Net 0'da açılan miktar yoktur; §3.7 tutarlılık kontrolü `NotAllocatable` /
    `Allocatable(R)` ayrımına bakmaz, ancak `Unreadable`, `NotFound` veya `Line` null okunursa işlem geri alınır ve 503 döner,
    kaydedilmez (§3.7 "açılan miktar yoksa" dalı).
- **Açılan miktar:** yalnız geri alınan / işaretli iade edilen net miktar (X). Formül gereği kalan X olur: `kabulNet` X düşer,
  `cancelled` ve `closed` payları **değişmez**. Önceden iptal veya kapanışla düşen miktar canlanmaz (Sinan metni "miktar"; geri
  alınmayan iptal payı bu işlemin konusu değildir). İptal/kapanış payı geri alınan miktar kadar artırılmaz; rev6'daki "paya yaz"
  okuması kaldırıldı.
  - **Koordinatör notundaki "iptal/kapanış payının düşmesi" (yazar okuması; miktar kuralı, Sinan sorusu "o satırdaki hatalı
    bir kabul geri alınırsa …", #66 6822140358 m.1):** rev6'da geri alınan miktar iptal/kapanış payına yazılacaktı (+X); canlanmada bu yazılmaz, yani pay rev6'ya
    göre X kadar "düşmüş" olur ve kayıtlı paydaki net değişim 0'dır. Pay ayrıca −X düşürülmez: düşürülürse `kabulNet −X` ile
    birlikte kalan **2X** olur, sipariş gelmeyecek miktar kadar fazla arz gösterir ve ALS-D82 değişmezi bozulur. Önceden iptal
    edilen veya kapanan miktar canlanmaz.
  - Önceden iptal edilen miktarın da canlanması istenirse bu yeni politikadır, NEEDS-SINAN'dır; şerit seçemez. API-44/API-60 K3
    review'ı notu farklı okursa #66'ya NEEDS-SINAN yazılır.
- **Durum geçişi (yeni durum yok):**
  - Cancelled sipariş: sipariş `Cancelled → Approved` döner. Satırların iptal payları kayıtlı kalır; yalnız canlanan satırın
    kalanı X'tir, diğer satırların kalanı 0'dır. `Cancel` değişmezi ("her satırın kalanı iptal payıdır") canlanmadan sonra
    geçerli değildir; yerine "kalan = committed − kabulNet − cancelled − closed − kesinRed/iadeEtkisi ≥ 0" korunur. Sipariş
    yeniden iptal edilirse yalnız yeni kalan (X) iptal payına eklenir.
  - API-11 kapalı satır: satırın kapalı işareti kalkar, kapanış payı ve gerekçesi kayıtlı kalır, satır kalanı X ile açıktır.
    Satır sonra yeniden API-11 ile kapatılırsa yalnız yeni kalan kapanış payına eklenir.
  - Canlanan satır tam kabul edilirse (a) tam kabul olur; kapalı veya iptal durumuna kendiliğinden dönmez.
  - **Kod/DB etkisi (API-44/API-60, K3; AGENTS.md migration ve rollback kapısı):** bugünkü kod canlanmaya izin vermez ve bunu
    dilim değiştirir:
    - `CK_PurchaseOrders_Cancellation` (`PurchasingDbContext.cs:87-90`) Status ≠ Cancelled iken iptal alanlarının NULL olmasını
      ister. İptal aktörü, zamanı ve gerekçesi **silinmez** (ters kayıt ilkesi, audit izi). İkinci iptali tek sütun takımı
      tutamayacağı için **iki seçenekte de bir iptal geçmişi deposu zorunludur**: dilim ya kısıtı canlanmış siparişi kapsayacak
      biçimde genişletir ve ek iptalleri geçmiş tablosunda tutar, ya da tüm iptal kayıtlarını ayrı bir iptal geçmişi tablosuna
      taşır (forward-fix migration; biçimi dilim seçer). Status = Cancelled iken sipariş sütunlarında hangi kaydın durduğunu
      (ilk iptal mi, son iptal mi) dilim açıkça yazar. Alanları NULL'layan çözüm kabul edilmez.
    - Tek `PurchaseOrder.Cancellation` kaydı (`PurchaseOrder.cs:85`; `Cancel` `:362`, atama `:394`) iptal geçmişine dönüşür: ilk iptal kaydı (aktör,
      zaman, gerekçe) korunur; canlanmadan sonraki ikinci iptal ayrı kayıttır.
    - `PurchaseOrderLine.CancelRemainder` (`PurchaseOrderLine.cs:103-111`) bugün iptal payı sıfır değilse reddeder ve payı
      **atar**; tekrar iptalde yeni kalanı payına **ekler** biçimine geçer.
    - `PurchaseOrderStatus.Cancelled` belgesindeki "Terminal" (`PurchaseOrder.cs:13`) "yalnız §3.8 canlanmasıyla Approved'a
      döner" olur; `Cancel` değişmezi yukarıdaki "kalan ≥ 0" ile değişir.
    - Kabul kapısı: `RecordAcceptance` bugün `RequireNotCancelled()` ve `RequireStatus(Approved)` ister
      (`PurchaseOrder.cs:319-324`; "Terminal" belgesi :13 "no acceptance … follows"). API-44 ters + yeni düzeltmesini tek net
      işlem olarak değerlendirir. Kapı satır bazlıdır: her genel satır kendi netiyle değerlendirilir. Net = 0 olan satırın
      düzeltmesi Cancelled siparişte ve API-11 kapalı satırda durum değiştirmeden yazılır; net > 0 ise önce canlanma, sonra
      yeni kabul; net < 0 kalan taahhüde karşı ölçülür (`PurchaseOrder.cs:337-341`): kalanı 0 olan satırda (Cancelled, API-11
      kapalı veya tam kabul) reddedilir, Approved satırda kalan içinde meşrudur. Bugünkü kodda Cancelled siparişte önce
      `RequireNotCancelled` (`PurchaseOrder.cs:323`, `purchasing_order_cancelled`) reddeder; Cancelled siparişte
      `AcceptanceExceedsRemaining` ancak bu kapı değişikliğinden sonra devreye girer. HTTP kabul yolunda participant kapıları
      domain'den önce çalışır (`PurchaseAcceptanceParticipant.cs:163-172`: sürüm, `Approved` durumu, kalan); kapı değişikliği
      bu kapıları da kapsar, aksi halde net 0 düzeltmesi Cancelled siparişte participant'ta 409 ile reddedilir. Kapı, participant
      kapıları ve "Terminal" belgesi buna göre güncellenir; düzeltme dışı yeni kabul
      Cancelled/kapalıda reddedilmeye devam eder. Test: net 0 düzeltme Cancelled siparişte yazılır, düzeltme dışı kabul bugünkü kodla reddedilir.
    - Geri dönüş: migration forward-fix'tir (ev deseni, ör. `AddPurchaseOrderLinePrice` `THROW 51169`). `Down`, canlanmış
      sipariş veya iptal geçmişi satırı varken yeni ve başka migration'da kullanılmayan bir `THROW 51xxx` ile reddeder; veri
      yokken kısıtı eski haline getirir ve geçmiş deposunu kaldırır. Veri oluştuktan sonra geri alma yeni bir forward-fix
      migration ile yapılır. Dilim PR'ı migration, kısıt, iptal geçmişi ve `Down` (veri varken red, yokken geri dönüş) için
      gerçek SQL kabulünü getirir.
    - API-11 kapalı satır için aynı kural: kapanış payı ve ilk kapanış gerekçesi korunur; yeniden kapanışın gerekçesinin nerede
      tutulduğunu (kapanış geçmişi) API-11 tasarımı yazar.
- **Expected version ve kilit:** canlanma API-44/API-60 komutunun parçasıdır, ayrı komut değildir. Komut sipariş
  `ExpectedVersion` taşır; canlanma sipariş sürümünü artırır. §3.4 satır kilidi (7) altında yapılır; kilit altında sipariş
  durumu ve satırın kapalı işareti yeniden okunur, istek okunandan farklı bir durumu varsayıyorsa 409 ve yazma yok. Kod: Alış
  komutlarının mevcut sürüm çakışma kodu `version_conflict` (`PurchasingContractsV1.cs:520`; iptal komutu da kullanır) yeniden
  kullanılır (öneri; dilimde kesinleşir).
- **Sıra:** idempotency replay kontrolü `ExpectedVersion` ve durum karşılaştırmasından **önce** yapılır (§3.5 ile aynı; ADR-011
  replay first, `PurchasingService.OrderCancellation.cs`). Başarılı canlanma sipariş sürümünü artırdığı için aynı anahtarla gelen
  tekrar saklanan sonucu alır, 409 almaz.
- **Audit ve olay:** önce/sonra sipariş durumu, satır kalanı, kapalı işareti, tetikleyen kayıt (ters kabul / iade kimliği),
  aktör ve gerekçe audit'e yazılır. Canlanma olayı (öneri `purchasing.order.revived.v1`) aynı işlemde outbox'a yazılır;
  tüketicileri (Satış bağlı arz görünümü, Finans taahhüt görünümü) kendi şeritlerinde değerlendirir. Bu belge onların davranışını
  seçmez.
- **İdempotency:** canlanma, akışın host komutunun idempotency kaydına ve sonuç özetine girer; aynı anahtarla tekrar ikinci
  canlanma yapmaz.
- **İzin:** canlanma ayrı izin istemez; API-44/API-60 komutunun izni yeterlidir (ad kaydı teknik, grant insan kapısı).
- **Ayırma etkisi (ALS-D93; Stok tarafı #1116 §3.7 F017-e ile aynı metin):** port Cancelled ve API-11 kapalı satıra 0 döner;
  canlanmadan sonra §2 formülü X'i ayrılabilir gösterir.
  - İptal veya kapanışta bırakılmış ayırmalar **geri gelmez**; canlanan miktar serbest açık miktar olur, gerekiyorsa kullanıcı
    yeniden ayırır (ALS-D93, Sinan kararı).
  - Geri alınan kabulün kendi tüketilmiş ayırması bırakılmış değildir: §3.7'deki gibi aynı kimlikle `Consumed → Active` döner,
    ayrılmış + yeniden açılan ≤ ayrılabilir kontrolüyle; sığmayan kısım `acceptance-reversed-no-supply` ile bırakılır. X'in
    bundan arta kalanı serbesttir. (**Yazar okuması:** ALS-D93'teki "eski ayırmalar" #1116 §9.1 aday 2 sorusundaki "iptal veya
    API-11 kapanışında bırakılan ayırmalar"dır; tüketilmiş ayırma ALS-D86/ALS-D91 kuralına bağlıdır. K3 review farklı okursa
    NEEDS-SINAN.)
  - Sıra §3.7 host işlemi sırasıdır: önce Alış canlanmayı yazar ve flush eder, Stok sonra Alış portunu ve kendi tüketilmiş
    kaydını aynı işlemde okur; tutarlılık kontrolü host'un Alış sonucuna dayanır (§3.7).
  - **"Canlandı" bildirimi (ALS-D93):** sistem canlanmayı kullanıcıya bildirir. Bildirim canlanma olayından üretilir; gösterildiği
    yer Alış sipariş ekranı ve satış kaynak görünümüdür (F17-5) (UX şerit önerisi, geri alınabilir). Bildirim yeni ayırma yapmaz.
- **Kabul ölçütleri:** §6.

## 4. Dilim planı

Sıra: #1101 kuyruğu bitmeden kod yok (koordinatör, 2026-11-12).

**Kapı kuralı (değişmezin her merge'de korunması):**
- F17-2 Stok ayırma kaydını, değişmezi ve bırakmayı getirir; ama **ayırma oluşturma yolu** (endpoint ve izin kaydı) F17-2'de
  kayıtlı değildir; yalnız servis ve testler vardır. Oluşturma yolu F17-3 (kabulde çevirme) ve F17-4 (iptal koruması) merge
  edildikten sonra **F17-2b** (K3: yeni izin kaydı ve stok taahhüdü yazan public endpoint) ile açılır. Böylece kabul çevirmesi
  veya iptal koruması yokken aktif ayırma oluşmaz; aynı ton hem ayırmada hem serbest stokta sayılmaz ve iptal ayırmayı iptal
  arzın üstünde bırakmaz.
- **#1116 hizalaması:** #1116 §10 F017-b bu kapıya göre bölünür: oluşturma endpoint'i, OpenAPI ve izin kaydı F017-b'den çıkıp
  F17-2b'ye (Stok adıyla F017-b2) geçer; F017-d (frontend) F17-2b'ye bağlanır. İki belge F17-2 PR'ında aynı metne getirilir;
  hizalama yapılmadan F17-2 merge edilmez. #1116 bu belgeden önce merge edildi (main `1af6ffea`); hizalamayı F17-2 PR'ı yapar ve
  F17-2 review'ı denetler. Hizalama ayrıca şunları kapsar: problem kodlarının önekli adları (#1126, main `732ba997`, Stok
  belgesini bu belgenin adlarına getirdi: satır kilidi meşgul `purchasing_line_busy`, Stok iş reddi
  `inventory_future_allocation_already_fulfilled`; F17-2 iki belgenin hâlâ aynı adı taşıdığını doğrular), karar
  aralığının ALS-D82..ALS-D93 olması, #1116 §3.6/§9.1 aday 2'nin ALS-D93 ile karara bağlanması.
- **F17-6 / F017-e hizalaması:** #1116 §3.7'deki "iptal/kapanış payının düşmesi" ifadesi ve Stok tarafı maddeleri, F17-6
  (≈ F017-e) PR'ında bu belgenin §3.7 host işlemi sırası ve §3.8 pay aritmetiği / ayırma etkisi metnine getirilir.
- **Kabul geri alma/iade koruması:** API-44/API-60 F17-6'dan önce merge edilirse, aktif ayırması (§3.2 toplam) veya tüketilmiş
  ayırması (§3.2 `GetConsumedPresenceOnPurchaseLineAsync`) olan satırda kabul geri alma/iade fail-closed 409
  `purchasing_line_has_allocations` ile reddedilir; Unreadable da reddedilir. Korumanın sahibi API-44/API-60 ile F17-2b'den
  hangisi **sonra** merge edilirse odur (önce gelen API-44/API-60 ise F17-2b korumayı onların host adaptörüne ekler; F17-2b
  önce ise API-44/API-60 korumayla gelir). F17-6 bu reddi kaldırır ve §3.7 kurallarını getirir.
- **Canlanma (§3.8):** API-44/API-60'ın zorunlu parçasıdır (K3). Cancelled sipariş için her iki dilimde; API-11 kapalı satır
  için API-11 main'deyse. API-11 API-44/API-60'tan sonra gelirse kapalı satır canlanmasını API-11 dilimi ekler. Ayırma tarafı
  (canlanan miktarın ayrılabilirliği, tüketilmiş ayırmanın yeniden açılması) F17-6'dadır.
- API-11 için tek kural §3.6'dadır; port formül terimleri için tek kural §2'dedir.

| Dilim | Şerit | İçerik | Kapı | Bağımlılık |
|---|---|---|---|---|
| F17-0 | Alış | Bu belge + EK17 (docs) | K1 | — |
| F17-1 | Alış (+host) | `IPurchaseFutureSupplyReadPortV1` (tek satır + ürün listesi, satış niyeti, teknik→genel eşleme) + Infrastructure okuması; `IPurchaseLineSerializationLockV1` host uygulaması; Alış kabul/iptal yollarında kilit; kabul sonucuna genel satır kimliği; birim/servis/contract testleri + gerçek SQL kabulü (eşzamanlılık dahil); A3-0'ı kapatır | **K3** (yeni modüller arası sözleşme + kilit; migration yok, gerekirse ürün+durum indeksi) | — |
| F17-1b | Alış | Termin dilimi okuması `GetLineScheduleAsync` | K2 | #1101 |
| F17-2 | Stok | Gelecek ayırma kaydı (#1116 F017-b), değişmez (§3.4 kilidi + aynı işlemde F17-1 okuması), bırakma; oluşturma endpoint'i yok (kapı kuralı) | K3 | F17-1 |
| F17-3 | Stok (+host) | Mal kabulünde gelecek ayırma → fiziksel tahsis + etiket ataması (aynı host işlemi, replay çiti, ALS-D84 sırası ve `AllocationOrder` §3.3) (#1116 F017-c) | K3 | F17-2 |
| F17-4 | Alış (+host) | Bağ düşürme komutu ve ekranı (§3.5, Stok bırakma portu üzerinden); sipariş kalanı iptali/satır azaltmada ayrılmış miktar kontrolü (§3.2 portu, §3.4 kilidi); ALS-D85 "bağları düşür ve iptal et" host komutu (görülen küme, replay, iki izin); termin geçti uyarısı (Alış ekranı); API-11 F17-4'ten önce merge edildiyse kapanışta bırakma (§3.6) | K3 | F17-2 |
| F17-2b | Stok | Ayırma oluşturma endpoint'i, OpenAPI ve izin kaydı (grant insan kapısı); API-44/API-60 main'deyse kabul geri alma/iade koruması (§4) (#1116 F017-b2) | K3 | F17-3, F17-4 |
| API-11 | Alış (+host) | Satır kapanışı/yeniden açma; port formülüne `closed` (§2); F17-4'ten sonra merge edilirse kapanışta `purchase-line-closed` bırakması, iki izin ve §3.4 kilidi (§3.6); API-44/API-60'tan sonra merge edilirse kapalı satır canlanması (§3.8) ve kapanış gerekçesi geçmişi | K3 | plan A3 |
| API-44 / API-60 | Alış (+host) | Kabul düzeltmesi / iade (plan); port formülüne `kabulNet` düşüşü ve `kesinRed/iadeEtkisi` (§2); canlanma (§3.8: durum geçişi, payların korunması, expected version, replay önce, §3.4 kilidi, audit, olay); canlanmanın migration + kısıt etkisi (`CK_PurchaseOrders_Cancellation`, iptal kaydı geçmişi, `CancelRemainder` ekleme) ve gerçek SQL kabulü; F17-2b'den sonra merge edilirse §4 koruması | K3 | plan A3/A5 |
| F17-5 | Satış/UI | Satış kaynak planında "alıştan" seçimi (MOD-16); uyarı olaylarının gösterimi (§3.5 neden kodları), termin geçti uyarısı | Satış şeridi | F17-2b |
| F17-6 | Alış + Stok (+host) | Kabul geri alma/iade F-017 kuralları (§3.7, ALS-D86/D91): gelecek miktarın nedene göre açılması, Stok tahsis/etiket geri alması, yeniden açma/bırakma, fail-closed kurallar, canlanan miktarın ayrılabilirliği (§3.8 ayırma etkisi, ALS-D93), §3.7 host işlemi sırası, `ReplacementExpected`, ters + yeni sırası, §2 terimlerinin ayırma tarafından doğrulanması (yazmaz), §4 korumasının kaldırılması, §3.4 kilidi | K3 | F17-3, API-44, API-60 |

## 5. NEEDS-SINAN (hepsi cevaplandı)

Not: §3.7 Cancelled/API-11 kapalı satır kesişimi rev6'da açık sorudu; Sinan 19. tur m.1 (ALS-D92, #66 6822140358) ile
cevaplandı: açılır, sipariş canlanır (§3.8). Canlanmada önceden bırakılmış ayırmalar (#1116 §9.1 aday 2) Sinan 21. tur m.3
(ALS-D93, #66 6822767886) ile cevaplandı: geri gelmez, yeniden ayrılır. Açık iş kararı kalmadı.

Ortak sorular #1116 §9 ile tek cevapla kapanır: S1 + S4 = #1116 S2, S2 = #1116 S4, S3 = #1116 S12, genel/teknik satır = #1116 S9.

- **S1 — Cevaplandı (ALS-D83):** satınalma tarafı bağı düşürür, satınalma miktarı azalmaz; serbest miktar başka satışa ayrılır veya
  stoğa alınır. Uygulama §3.5.
- **S2 — Cevaplandı (ALS-D84):** termini en yakın satış önce; sıra elle değiştirilebilir/seçilebilir. Uygulama §3.3.
- **S3 — Cevaplandı (ALS-D86):** kabul geri alınırsa tahsis ve etiket geri alınır; gelecek miktar varsa ayırma yeniden açılır,
  yoksa bırakılır ve satış uyarılır. 18. tur m.7 (ALS-D91) gelecek miktarı nedene bağladı. Uygulama §3.7, F17-6.
- **S4 — Cevaplandı (ALS-D85):** iptal doğrudan yapılmaz; bağlı satışlar listelenir, "bağları düşür ve iptal et" gerekçeli
  tek onayla aynı işlemde yapılır, satışa "kaynak iptal edildi" uyarısı gider. Uygulama §3.5.
- **#1116 ortak sorular:** S1/S3 az/fazla gelme ALS-D87, S5 ayıran kişi ALS-D88, S7/S8 bağlama hatası ALS-D89, S11 süre
  ALS-D90. S6 (izin adı) ve S9 (teknik/genel eşleme) teknik konudur; S10 SAT-D298 ile kilitlidir.
- **Stok tarafı:** yukarıdaki cevaplar Stok modelini de bağlar; ayrıntı #1116'da Stok şeridinindir. KLN-D26 (6) ile tutarlıdır.

## 6. Kabul ölçütleri (F-017 testi)

- Aynı satınalma satırı miktarı iki satış siparişine verilemez. İki eşzamanlı ayırma isteğinden toplamı aşan ikincisi reddedilir.
- Stok ayırması ile Alış kabul/kalan iptali aynı satırda eşzamanlı olduğunda sonuç her zaman ayrılmış ≤ ayrılabilir (§3.4).
- Draft ve Cancelled siparişin satırı ve API-11 ile kapalı satır ayrılamaz (ayrılabilir 0); işaretsiz iade edilen miktar
  ayrılabilir görünmez. Teknik dilim satırına doğrudan ayırma yapılamaz. Başka şirketin satırı ve
  bağlamda şirket yokken okuma `NotFound`'dur.
- Ürün kartında katsayı yoksa kg/ton girdisi satır birimine çevrilemez ve ayırma reddedilir (tahmin yok).
- Mal kabulünde ayrılan miktar fiziksel tahsise döner ve etiket satış sipariş satırına atanır; aynı kabul isteğinin tekrarı ikinci
  çevirme yapmaz.
- Kısmi kabulde varsayılan sıra termini en yakın satış; kullanıcının verdiği `AllocationOrder` varsayılanı geçer; geçersiz kimlik
  422 ve yazma yok (ALS-D84).
- Satınalma tarafı bağı düşürünce ayırma Stok'ta `Released` olur, satınalma committed/accepted/cancelled değişmez, ayrılabilir miktar
  aynı kadar artar ve satışa uyarı olayı yazılır (ALS-D83).
- Ayırması olan siparişin düz kalan iptali 409 `purchasing_line_has_allocations` ile reddedilir ve bağlı aktif ayırmaları döner.
  "Bağları düşür ve iptal et" gerekçesiz reddedilir; görülen küme kilit altındaki aktif kümeden farklıysa 409
  `purchasing_future_allocation_set_changed` ve yazma yok; aynıysa bağ düşürme ve iptal tek işlemde olur, biri başarısızsa hiçbiri yazılmaz;
  tüketilmiş ayırmalara dokunulmaz; iki izinden biri yoksa 403 (ALS-D85).
- Hatalı kabul geri alınınca kalan açılır; "yerine yenisi gelecek" işaretli iadede açılır, işaretsiz iadede açılmaz. Açılan
  gelecek miktar varsa ve fiziksel tahsis aktifse ayırma aynı kimlikle yeniden açılır; yoksa bırakılır ve satışa uyarı yazılır.
  Sevk edilmiş/tüketilmiş etiketin geri alınması 409 (ALS-D86, ALS-D91).
- Cancelled siparişte / API-11 kapalı satırda hatalı kabul geri alınınca veya işaretli iade yapılınca yalnız o miktar açılır:
  sipariş `Approved`'a döner (API-11'de kapalı işaret kalkar), iptal/kapanış payları değişmez, sipariş sürümü artar, audit ve
  canlanma olayı yazılır; port açılan miktarı ayrılabilir gösterir; bırakılmış ayırmalar geri gelmez ve "canlandı" bildirimi
  görünür (ALS-D93). İşaretsiz iade canlandırmaz. Kalan 2X olmaz (pay ayrıca düşürülmez). İlk iptal kaydı (aktör, zaman,
  gerekçe) canlanmadan ve ikinci iptalden sonra korunur; ikinci iptal yalnız yeni kalanı paya ekler. Eski `ExpectedVersion` ile
  409 `version_conflict` ve yazma yok; aynı anahtarla tekrar saklanan sonucu alır, 409 almaz ve ikinci canlanma yapmaz (ALS-D92).
- Cancelled siparişte tüketilmiş ayırmalı satırda hatalı kabul geri alınınca ayırma aynı kimlikle `Active` döner;
  `acceptance-reversed-no-supply` yazılmaz (Alış yazısı Stok okumasından önce, §3.7 host işlemi sırası). Host'un Alış sonucuna
  göre açılan miktar varken port `Allocatable` + beklenen sürüm + beklenen miktar dışında bir cevap verirse (bayat
  `NotAllocatable`, `Line` null, farklı sürüm, `Unreadable`) işlemin tamamı yazmadan geri alınır; ayırma bırakılmaz,
  `acceptance-reversed-no-supply` yazılmaz. Stok iş reddi (bugün yalnız 409 `inventory_future_allocation_already_fulfilled`) Alış geri
  alma/iade payını ve canlanmayı da geri alır.
- Kabul geri almada `Consumed → Active` yeniden açma önce satış satırı kilidini, sonra alış satırlarını artan `lineId` ile alır;
  kilit sonrası satış satırı iptal/kapalı okunursa aktif gelecek ayırma oluşmaz, yalnız etiket geri alınır (gerçek SQL: satış
  satırı iptali ile eşzamanlı geri alma kilitlenmeden biter).
- Birden çok satış satırının tüketilmiş ayırmasını yeniden açan geri almada satış satırları artan kimlikle, alış kilitlerinden
  önce alınır; kilit altında küme büyüdüyse yeniden denenir (gerçek SQL). Bayat port okuması veya `Unreadable` 503 döner ve
  idempotency kaydı yazılmaz, aynı anahtar yeniden yürütür; Stok iş reddi 409'u API-44/API-60 komutu kaydeder, aynı anahtar
  Stok'u çağırmadan 409'u replay eder.
- Karışık çok satırlı geri alma: bir satırda Stok iş reddi 409, başka satırda `Unreadable` → 503, idempotency kaydı yok; aynı
  anahtar yeniden yürütür.
- Açılan miktar yokken (işaretsiz iade, net 0 düzeltme) port `Unreadable`, `NotFound` veya `Line` null okursa işlemin tamamı
  geri alınır, 503 döner, kaydedilmez; `NotAllocatable` ve `Allocatable(R)` ikisi de kabul edilir.
- Satır bazlı kabul kapısı: aynı API-44 düzeltmesinde bir satır net 0, başka satır net > 0 ise net 0 satırın durumu değişmez,
  net > 0 satır canlanır; kalanı 0 olan satırda net < 0 `AcceptanceExceedsRemaining`, Approved satırda kalan içinde net < 0
  yazılır.
- Değerlendirme modeli golden'ları (#66 6825926365): (i) kilit meşgul → o kilidin 409'u, port okunmaz, kayıt yok;
  (ii) terminal + 503 → 503, kayıt yok; (iii) terminal + 409 `inventory_concurrent_change` → o 409, kayıt yok; (iv) iki
  satırda da 409 `inventory_future_allocation_already_fulfilled` → sabit sıradaki ilk satırın reddi kaydedilir (farklı terminal
  kodlar bugün ulaşılamaz; ikinci terminal kod eklenirse test o kodla genişletilir); (v) replay zinciri: meşgul → 409
  kaydedilmez; kilit boşalınca aynı anahtar → 409 `inventory_future_allocation_already_fulfilled` kaydedilir; sonraki çağrı
  saklanan 409'u portu çağırmadan döndürür.
- Terminal olmayan diğer sonuçlar: port istisnası ve port zaman aşımı → işlem geri alınır, kayıt yok, aynı anahtar yeniden
  yürütür; kilit altında küme büyümesiyle sınırlı deneme tükenince 409 `inventory_concurrent_change`, Alış yazısı ve port
  okuması yok, kayıt yok. Alış sürüm çakışması (409 `version_conflict`) Stok portu çağrılmadan terminal kaydedilir ve replay
  edilir; kullanıcının düzeltebileceği Alış kural reddi 422 `validation_failed` döner ve kaydedilmez.
- Cancelled siparişte / API-11 kapalı satırda miktarı değişmeyen API-44 düzeltmesi (ters X + yeni X, net 0): sipariş Cancelled,
  satır kapalı kalır; `Approved`'a dönüş, canlanma olayı ve "canlandı" bildirimi yok; iptal/kapanış kaydı değişmez; Stok'ta
  ayırma aynı kimlikle `Consumed` kalır ve `acceptance-reversed-no-supply` yazılmaz. Net > 0 ise yalnız net miktar açılır.
- Canlanma migration'ının `Down`'ı canlanmış sipariş veya iptal geçmişi satırı varken `THROW 51xxx` ile reddeder, veri yokken
  kısıtı geri getirir (gerçek SQL).
- F17-2b merge edilmeden ayırma oluşturma endpoint'i yoktur; F17-6 öncesi aktif veya tüketilmiş ayırmalı satırda (veya
  tüketilmiş sinyali Unreadable iken) kabul geri alma/iade 409.
- Aynı anahtarla tekrarlanan "bağları düşür ve iptal et" veya kapanış saklanan sonucu döndürür, küme farkı 409'u almaz.
- Az kabulde kalan ayırma aktif kalır. API-11 ile kapanan satırın aktif ayırmaları aynı işlemde bırakılır ve uyarılır; satır
  yeniden açılınca bırakılan ayırma geri gelmez. Fazla kabul edilen miktar serbest stoktur. Kalanı aşan kabul
  `AcceptanceExceedsRemaining` ile reddedilmeye devam eder (ALS-D87).
- İş nedenli çevirme hatasında (bloklu etiket dahil) kabul tamamlanır, mal stoğa girer, ayırma bırakılır ve uyarı yazılır. Teknik
  hatada işlemin tamamı geri alınır, kabul yazılmaz (ALS-D89).
- Termini geçen satınalma satırındaki ayırma aktif kalır ve okuma anında "termin geçti" uyarısı görünür (ALS-D90).
- Ayrılabilir miktar her zaman Alış'ın kalanıdır; tüketici yeniden hesaplamaz.
