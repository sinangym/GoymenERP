# Finans F-018 — Müşteri kredi limiti ve teminatı: spec ve dilim planı v1

**Kararlar:** FNS-D27/FNS-D27a, FNS-D30, FNS-D31 (`docs/decisions/SINAN_KARARLARI_2026-11-12_FINANS_KREDI_LIMITI.md`), SAT-D51,
DEC-FIN-007, DEC-FIN-009, SAT-D46…SAT-D53, SAT-D80, SAT-D283…SAT-D287. Plan satırı: `FINANS_HAZIRLIK_PLANI_v1.md`
API-85 ve D-5 eksiği. **Sınıf:** K3, çünkü migration/constraint, yetki adı ve modüller arası sözleşme içerir.

## 1. Bugünkü durum (main 58b96aa28)

| Parça | Durum | Kaynak |
|---|---|---|
| Brüt risk hesaplayıcısı | Var (Satış): DEC-FIN-007'nin üç aşaması, limit para biriminde karar, eşit değer limit içi | `SalesCustomerRiskCalculator`, Satış README "Customer risk check" |
| Risk girdisi portu | Var: Satış port sözleşmesini tanımlar, Finans cevaplar. Açık alacak `Available`; **limit ve teminat `NoSource`** | `ISalesPartyRiskReadPortV1`, `FinanceSalesPartyRiskReadPortV1` |
| Sevk risk istisnası | Var (Satış): talep, onay, ret, geri çekme; yükleme talimatına bağlı; Lojistik için okuma portu | `ISalesShipmentRiskExceptionReadPortV1` (SAT-D283…SAT-D287) |
| Lojistik sevk kapısı | Yok: istisna portunu ve risk sonucunu henüz tüketmiyor | Lojistik README |
| Limit/teminat master'ı | **Yok**. Bu planın konusu | — |

Bugün risk kararı her zaman `Unavailable` döner. Bunun bir nedeni limit master'ının olmamasıdır.

## 2. Finans'ın işi (bu şerit)

### L1 — Kredi limiti master'ı (API-85a, K3)

- **Tablolar:** `finance.PartyCreditLimits` (cari hesap başına canlı kayıt) ve `finance.PartyCreditLimitCommands`
  (idempotency). Her satırda `CompanyId` ve şirket filtresi vardır.
- **Alanlar:** `PartyId`, `CompanyAccountId`, `CurrencyId` (Ayarlar para birimi adayları, API-30 emsali), `Amount` (tam
  ondalık metin, > 0, yuvarlanmaz; kaldırma kaydında boş), tür (`Set` veya `Remove`), durum (`Proposed`, `Approved`,
  `Rejected`, `Superseded`), öneren, karar veren, ret gerekçesi ve sürüm.
- **Akış (FNS-D27a, #66 `6821262098` m.5 "Yetkili tek adımda"):**
  1. **Doğrudan:** `.approve` yetkisi olan kişi limiti tanımlar, değiştirir veya kaldırır. Kayıt tek adımda `Approved`
     olur; ikinci onay beklenmez. Kim, ne zaman, eski ve yeni değer denetim izinde kalır.
  2. **Öneri:** yalnız `.propose` yetkisi olan kişi öneri açar (`Proposed`). `.approve` yetkisi olan bir kişi öneriyi
     onaylar ya da gerekçe yazarak reddeder. Öneren kendi önerisine karar veremez; serviste ve DB check'inde (`BIN2`)
     uygulanır (uygulama ayrıntısı, geri alınabilir).
  3. Geçerli olan kayıt öncekini `Superseded` yapar. Bir cari hesapta aynı anda tek canlı onaylı kayıt olur (filtreli
     tekil index) ve tek bekleyen öneri olur. Doğrudan değişiklik bekleyen öneriyi `Superseded` yapar.
  4. `Remove` onaylanınca cari hesabın geçerli limiti kalmaz; port yeniden `NoSource` + `finance_credit_limit_not_set`
     döner.
- **Komut kuralları:** expected version, Idempotency-Key, deneme ve audit kaydı, correlation (`k3-command-skeleton`).
- **Uçlar:** `/api/v1/finance/credit-limits` altında doğrudan tanım/değişiklik/kaldırma, öner, onay, ret, cari hesabın
  geçerli limiti ve tarihçesi, liste.
- **İzinler (öneri adları):** `finance.credit-limits.read`, `.propose`, `.approve`. `All` ve `CompanyWideOnly`
  kümelerinde yer alırlar ve hiçbir role verilmez. Critical olup olmadıkları seçilmemiştir; Critical işaretlenmezler
  (insan kapısı).
- **Migration:** Down, kayıt varken `THROW 51xxx` ile reddeder (forward fix). Numara, merge anında main ve açık PR'lara
  bakılarak seçilir.
- **Port:** `FinanceSalesPartyRiskReadPortV1.CreditLimit` şöyle cevap verir:
  - onaylı kayıt varsa `Available`: tek tutar, `IncludesTax: null`;
  - kayıt okunamıyorsa `Unavailable`;
  - **geçerli kayıt yoksa** (hiç tanımlanmamış veya kaldırılmış) `NoSource` kalır ve neden kodu
    `finance_credit_limit_not_set` olur. Sıfır veya "limitsiz" asla dönmez. FNS-D30 gereği bu durumda sevk durur ve tek
    sevk istisnasıyla geçer; bu karar Satış/Lojistik tarafındadır, port yalnız "kayıt yok" bilgisini verir.
  - **Anlam kayması:** bugün `NoSource` + `finance_credit_limit_owner_missing` "limitin sahibi henüz yok" demektir.
    L1'den sonra `NoSource` "bu cari hesapta limit tanımlı değil" demektir ve kod `finance_credit_limit_not_set` olur.
    Eski kodu tüketenler: `src/frontend/goymen-erp-web/src/sales/riskText.ts:104`,
    `src/frontend/goymen-erp-web/src/sales/SalesOrderRiskCard.test.tsx:115`, Satış README:251,
    `docs/handoffs/SATIS_MODUL_BAGLANTILARI_v1.md:89` ve Satış `examples/risk-check-response.json`. Bunların
    güncellenmesi §3 devir satırlarındadır.

### L2 — Müşteri teminatı (API-85b, K3)

- Müşteriden alınan teminatların kaydı: tür (Ayarlar'da yapılandırılabilir liste), tutar, para birimi, geçerlilik
  tarihleri ve belge referansı. FNS-D31: banka garantili ödemeler, DBS, teminat mektubu, kredi kartı, Vinov ve ipotek
  teminattır. **Çek ve senet teminat türü olarak kabul edilmez.** Kayıt ekle-yalnızdır, çözüm ters kayıtla
  yapılır. #66 `6821262098` m.5 yalnız limiti cevaplar; teminatın tanım akışı (tek adım mı, öneri-onay mı) L2 spec'inde
  yazılır, gerekirse `NEEDS-SINAN` olur. Çek ve senedin tür listesine eklenememesi sunucuda zorlanır; ekran
  listesinden geri eklenemez.
- **Teminat oranı (FNS-D31 (3), #66 `6821262098` m.6):** Ayarlar'da teminat türü başına oran alanı olur, başlangıç
  değeri %100 (tam tutar), aralık 0–100 (2026-10-21 K7). Port `Collateral` cevabı tür ve para birimine göre tutarlardır;
  oranın uygulandığı yer (port mu okuma ucu mu) ve K6 kapsam önceliğiyle ilişkisi L2 spec'inde yazılır. Oran ayarının
  sahibi Ayarlar'dır (§3).
- Teminat brüt riski düşürmez (DEC-FIN-007 notu, FNS-D31 (5)). Ekranda **mevcut teminat**, **teminat doluluğu**
  (teminattan düşülen açık fatura) ve **riskteki tutar** ayrı ayrı görünür. Hiçbiri ekranda hesaplanmaz: ilk ikisi Finans
  okuma ucundan (L3), riskteki tutar Satış risk hesaplayıcısından gelir.
- **Evraklı risk** (FNS-D31 (2)): müşteriden alınıp tahsil edilmemiş çek ve senetlerin toplamı ayrı gösterilir.
  DEC-FIN-016 gereği borç tahsilata kadar açık kalemde durduğu için evraklı risk brüt riske ek bir toplam değildir; risk
  içindeki bir dökümdür (çift sayım yoktur). Çek/senet portföyünün kaynağı L2 spec'inde yazılır.
- API-84 (genel teminatlar; verilen/alınan BTM) ile ilişkisi L2 spec'inde netleşir. Ortak tablo mu ayrı tablo mu,
  sonradan tersine dönmesi pahalı olduğu için L2 başlamadan koordinatöre yazılır.

### L3 — Cari kartı paneli için okuma ucu (K2)

- Cari kartı ekranı (Claude frontend), Finans'ın okuma ucundan limit, teminat ve açık alacağı gösterir. Düzenleme düğmeleri
  Finans uçlarına gider (FNS-D27 (2)).
- Mevcut teminat, teminat doluluğu ve evraklı risk (FNS-D31) bu uçtan gelir.
- Brüt risk ve "limit boşluğu" sayısı ekranda hesaplanmaz. Gösterilecekse Satış risk hesaplayıcısının cevabından gelir.

## 3. Diğer şeritlere devir (Finans uygulamaz)

| Şerit | İş | Not |
|---|---|---|
| Satış | Siparişte limit aşımı **uyarısı ve kaydı** (FNS-D27 (3)). Uyarı sipariş kaydını durdurmaz. | DEC-FIN-009 "uyarı/onay" metninin uyarı kısmı. Siparişte ayrı onay adımı bu kararla açılmaz |
| Satış | Lojistik sevk kapısının okuyacağı **sevk anı risk sonucu** portu: sonuç (`Within`/`Over`/`Unavailable`) ve tutar içermeyen **neden kategorisi** (#1119: `OverLimit` / `CreditLimitNotSet` / `SourceUnavailable`), tutar yok (SAT-D162). Finans `finance_credit_limit_not_set` → `CreditLimitNotSet`; okunamayan kaynak → `SourceUnavailable`. İstisna talebi `Unavailable` için de açılır (FNS-D30 (1): SAT-D284 (2) genişler; bugün `SalesRiskExceptionService` yalnız `Within`'i reddeder, kural ve metin buna göre yazılır). İstisna onay iş gününün sonunda düşer (SAT-D160). `finance_credit_limit_owner_missing` tüketicilerinin (README:251, `examples/risk-check-response.json`, `SATIS_MODUL_BAGLANTILARI_v1.md:89`) yeni koda geçmesi. | Hesap Satış'ta kalır (DEC-FIN-007 bileşenlerinin ikisi Satış verisidir). Finans portu girdi olmaya devam eder. Lojistik'e ayrı bir Finans portu açılmaz; Lojistik Finans tablosu ve Finans neden kodu görmez. Satış port sözleşmesine yeni alan eklenir: K3 |
| Lojistik | Sevk kapısı: `Over` → sevk durur; geçerli istisna (`Valid`) tek sevki serbest bırakır; tüketim ve sevk iptalinde düşme (SAT-D285). `Unavailable` (neden kategorisi `CreditLimitNotSet` veya `SourceUnavailable`) için de sevk durur ve aynı tek sevk istisnasıyla geçer (FNS-D30 (1) ve (3); #66 `6821262098` m.1). Satış'ın `credit_limit_not_defined` kodu "limit tek tutar değil" (`Amounts.Count != 1`) demektir; Lojistik bu kodu değil, Satış portunun neden kategorisini okur. | Modüller arası sözleşme, K3 |
| Frontend (Claude) | Cari kartında Finans paneli (limit/teminat tanım, öner-onay-ret, tarihçe; mevcut teminat, teminat doluluğu, riskteki tutar ve evraklı risk ayrı ayrı), Satış uyarı gösterimi, Lojistik "sevk durdu / istisna" durumu. Limitsiz müşteri istisnasını onaylayan kişiye cari kartında **limit tanımlama kısayolu** (FNS-D30 (2)): kişinin `.approve` yetkisi varsa limit tek adımda geçerli olur, yalnız `.propose` varsa öneri açılır, ikisi de yoksa kısayol gösterilmez (capability kapısı). `src/sales/riskText.ts:104` ve `SalesOrderRiskCard.test.tsx:115`'in `finance_credit_limit_not_set` koduna geçmesi. | `frontend-screen-states`, `permission-capability-registration` |
| Ayarlar | Teminat türü listesi (FNS-D31 (1); çek ve senet eklenemez, sunucuda zorlanır) ve tür başına **teminat oranı** alanı: başlangıç %100, aralık 0–100 (FNS-D31 (3), 2026-10-21 K7). | Sahiplik ve K6 kapsam önceliğiyle ilişki L2 spec'inde netleşir |
| Üretim (bilgi; Finans işi yok) | Kapalı üretim emrinin yeniden açılmasında "muhasebe dönemi kapalı mı" okuması (URT-D32, öneri aşamasında: #1114). **Yeni port açılmaz:** dönemin sahibi Muhasebe'dir ve main'de `IAccountingPeriodStatusPortV1` (`src/backend/Modules/Accounting/…/Contracts/IAccountingPeriodStatusPortV1.cs`, FIN-T05; port Lojistik için de tasarlandı, main'de Lojistik henüz tüketmiyor) bir tarih için `Open` / `NotOpen` / `None` / `Denied` döner. Hangi tarihin dönemi belirlediği Üretim'in kuralıdır. Port aktörün `accounting.periods.read` yetkisiyle okur; `Denied`, "dönem kapalı" diye gösterilmez. Üretim kullanıcısına bu yetkinin verilmesi grant kararıdır (insan kapısı). Port eksik kalırsa Üretim fail-closed kalır | Koordinatör talebi 2026-11-12; Üretim oturumu `session_015Usc2oQC2jEVhGTtMaXP58`. L dilimlerinde Finans kod işi yoktur |
| Counterparty | Kod işi yok. Limit Counterparty tablosuna yazılmaz. | SAT-D51 |

## 4. Açık noktalar

| Kimlik | Soru | Neden tahmin edilemez |
|---|---|---|
| N-1 | ~~Limiti tanımlanmamış müşteride sevk~~ **Cevaplandı: FNS-D30** (durur, tek seferlik onayla geçer, onaylayana limit tanımlama kısayolu; risk okunamazsa da aynı istisna, #66 `6821262098` m.1). | — |
| N-2 (`NEEDS-SINAN`, gerekirse) | "Cari kartında tutulur" kaydın sahibini Cari'ye taşımak mı demek? Bu plan ekran yeri olarak okur (SAT-D51 aynen). | Modül sahipliği kararıdır |
| N-3 | Limit değişikliğine tutar eşiğine bağlı ikinci onay gerekir mi? | OPEN-009 (onay matrisi tutarları); başlangıçta yetkili tek adımda tanımlar, yetkisiz yalnız öneri açar (FNS-D27a, #66 `6821262098` m.5) |
| OPEN-010 | "Gerçek haircut oranları" kısmı **FNS-D31 (3)** ile cevaplandı (#66 `6821262098` m.6: türe göre oran, başlangıç %100); Sinan bu kısmın kapanmasını seçti. Teminat türleri ve çek/senet dışlaması `6819835966` m.4. `ACIK_KARARLAR.md` satırı bu PR'da değişmez; ayrı ve review'da doğrulanır. "Risk motoru aktivasyon politikası" açık kalır | `OPEN-*` kapatma insan kapısıdır; dayanak Sinan yorumu |

## 5. Sıra

1. Bu karar ve plan dosyası (docs PR, iki DOCS review).
2. N-1 ve OPEN-010 için Issue #66'ya `NEEDS-SINAN` yazıldı:
   [`6819494783`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819494783) (madde 2 ve 3).
3. L1, ayrı K3 PR. Port sözleşmesinin şekli değişmez (aynı alanlarda `NoSource` yerine `Available` döner), ama
   `NoSource`'un anlamı ve neden kodu değişir (§2 L1 "Anlam kayması"). Bu bir sözleşme değişikliğidir: Satış ve Frontend
   tüketicileri aynı dalgada güncellenir, Satış'a devir notu gider.
4. L2, API-84 ilişkisi netleştikten sonra.
5. Satış, Lojistik ve Frontend devirleri koordinatör üzerinden gönderilir (FNS-D30 ve FNS-D31 dahil).

## 6. Kabul ölçütleri (L1)

- Şirket filtresi: başka şirketin limiti görünmez, 404 kod taşımaz.
- Yetkisiz istek 403 alır. `.approve` yetkisi olan kişi limiti tek adımda tanımlar, değiştirir ve kaldırır; denetim
  izinde kim, ne zaman, eski ve yeni değer görünür. Yalnız `.propose` yetkisi olan kişi doğrudan tanım yapamaz (403).
  Öneren kendi önerisine karar veremez; servis ve DB ayrı ayrı test edilir.
- Kaldırma: `Remove` onaylandıktan sonra port `NoSource` + `finance_credit_limit_not_set` döner.
- İdempotency: aynı anahtar ve aynı gövde replay olur, farklı gövde 409 alır. Expected version uyuşmazlığı 409 alır.
- Port: geçerli limit `Available` döner (tek tutar, tam metin). Bekleyen öneri limiti değiştirmez. Bozuk satır
  `Unavailable` döner. Kayıt yokken cevap §2 L1'deki gibidir.
- Satış risk kontrolü: limit girildikten sonra karar artık hep `Unavailable` değildir. Gerçek SQL ve gerçek host ile
  `Within`/`Over` olguları gösterilir (diğer bileşenler hazırsa; değilse ilgili neden kodu).
- Migration Down guard'ının gerçek SQL testi; recovery, mimari koruma ve OpenAPI pin'leri.
