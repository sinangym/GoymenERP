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
  ondalık metin, > 0, yuvarlanmaz), durum (`Proposed`, `Approved`, `Rejected`, `Superseded`), öneren, karar veren, ret
  gerekçesi ve sürüm.
- **Akış (FNS-D27a):**
  1. Öner: yeni limit, değişiklik veya kaldırma.
  2. Başka bir kullanıcı onaylar ya da gerekçe yazarak reddeder.
  3. Onaylanan kayıt öncekini `Superseded` yapar. Bir cari hesapta aynı anda tek canlı onaylı kayıt olur (filtreli tekil
     index) ve tek bekleyen öneri olur.
  4. Maker≠checker kuralı serviste ve DB check'inde (`BIN2`) uygulanır.
- **Komut kuralları:** expected version, Idempotency-Key, deneme ve audit kaydı, correlation (`k3-command-skeleton`).
- **Uçlar:** `/api/v1/finance/credit-limits` altında öner, onay, ret, cari hesabın geçerli limiti ve tarihçesi, liste.
- **İzinler:** `finance.credit-limits.read`, `.propose`, `.approve`. `All` ve `CompanyWideOnly` kümelerinde yer alırlar.
  Critical değildir, hiçbir role verilmez.
- **Migration:** Down, kayıt varken `THROW 51xxx` ile reddeder (forward fix). Numara, merge anında main ve açık PR'lara
  bakılarak seçilir.
- **Port:** `FinanceSalesPartyRiskReadPortV1.CreditLimit` şöyle cevap verir:
  - onaylı kayıt varsa `Available`: tek tutar, `IncludesTax: null`;
  - kayıt okunamıyorsa `Unavailable`;
  - **onaylı kayıt yoksa** `NoSource` kalır ve yeni neden kodu `finance_credit_limit_not_set` kullanılır. Sıfır veya
    "limitsiz" asla dönmez. FNS-D30 gereği bu durumda sevk durur ve tek sevk istisnasıyla geçer; bu karar Satış/Lojistik
    tarafındadır, port yalnız "kayıt yok" bilgisini verir.

### L2 — Müşteri teminatı (API-85b, K3)

- Müşteriden alınan teminatların kaydı: tür (Ayarlar'da yapılandırılabilir liste), tutar, para birimi, geçerlilik
  tarihleri ve belge referansı. FNS-D31: banka garantili ödemeler, DBS, teminat mektubu, kredi kartı, Vinov ve ipotek
  teminattır. **Çek ve senet teminat türü olarak kabul edilmez.** Akış L1 ile aynıdır (öner, onay, ret; kayıt
  ekle-yalnızdır, çözüm ters kayıtla yapılır).
- Port `Collateral` cevabı tür ve para birimine göre tam tutarlardır. **Azaltım uygulanmaz** (FNS-D31 (3)).
- Teminat brüt riski düşürmez (DEC-FIN-007 notu, FNS-D31 (5)). Ekranda **mevcut teminat**, **teminat doluluğu**
  (teminattan düşülen açık fatura) ve **riskteki tutar** ayrı ayrı görünür. Hiçbiri ekranda hesaplanmaz: ilk ikisi Finans
  okuma ucundan (L3), riskteki tutar Satış risk hesaplayıcısından gelir.
- **Evraklı risk** (FNS-D31 (2)): müşteriden alınıp tahsil edilmemiş çek ve senetlerin toplamı ayrı gösterilir ve
  doğrudan riske girer. Çek/senet portföyünün kaynağı ve DEC-FIN-007 bileşenleriyle eşleşmesi (çift sayım olmaması) L2
  spec'inde yazılır.
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
| Satış | Lojistik sevk kapısının okuyacağı **sevk anı risk sonucu** portu: sonuç (`Within`/`Over`/`Unavailable`), tutar yok (SAT-D162). | Hesap Satış'ta kalır (DEC-FIN-007 bileşenlerinin ikisi Satış verisidir). Finans portu girdi olmaya devam eder. Lojistik'e ayrı bir Finans portu açılmaz; Lojistik Finans tablosu da okumaz |
| Lojistik | Sevk kapısı: `Over` → sevk durur; geçerli istisna (`Valid`) tek sevki serbest bırakır; tüketim ve sevk iptalinde düşme (SAT-D285). Limiti tanımlanmamış müşteri (Finans portu `NoSource` + `finance_credit_limit_not_set`; Satış'ın `credit_limit_not_defined` kodu ise "limit tek tutar değil" demektir, bu durumu karşılamaz) için de sevk durur, aynı tek sevk istisnasıyla geçer (FNS-D30). | Modüller arası sözleşme, K3 |
| Frontend (Claude) | Cari kartında Finans paneli (limit/teminat öner-onay, tarihçe; mevcut teminat, teminat doluluğu, riskteki tutar ve evraklı risk ayrı ayrı), Satış uyarı gösterimi, Lojistik "sevk durdu / istisna" durumu. Limitsiz müşteri istisnasını onaylayan kişiye cari kartında limit **önerisi** kısayolu (FNS-D30 (2)); kısayol limiti doğrudan geçerli yapmaz. | `frontend-screen-states` |
| Üretim (bilgi; Finans işi yok) | Kapalı üretim emrinin yeniden açılmasında "muhasebe dönemi kapalı mı" okuması (URT-D32). **Yeni port açılmaz:** dönemin sahibi Muhasebe'dir ve main'de `IAccountingPeriodStatusPortV1` (`src/backend/Modules/Accounting/…/Contracts/IAccountingPeriodStatusPortV1.cs`, FIN-T05; Finans ve Lojistik aynı portu kullanır) bir tarih için `Open` / `NotOpen` / `None` / `Denied` döner. Hangi tarihin dönemi belirlediği Üretim'in kuralıdır. Port aktörün `accounting.periods.read` yetkisiyle okur; `Denied`, "dönem kapalı" diye gösterilmez. Üretim kullanıcısına bu yetkinin verilmesi grant kararıdır (insan kapısı). Port eksik kalırsa Üretim fail-closed kalır | Koordinatör talebi 2026-11-12; Üretim oturumu `session_015Usc2oQC2jEVhGTtMaXP58`. L dilimlerinde Finans kod işi yoktur |
| Counterparty | Kod işi yok. Limit Counterparty tablosuna yazılmaz. | SAT-D51 |

## 4. Açık noktalar

| Kimlik | Soru | Neden tahmin edilemez |
|---|---|---|
| N-1 | ~~Limiti tanımlanmamış müşteride sevk~~ **Cevaplandı: FNS-D30** (durur, tek seferlik onayla geçer, onaylayana limit önerisi kısayolu). | — |
| N-2 (`NEEDS-SINAN`, gerekirse) | "Cari kartında tutulur" kaydın sahibini Cari'ye taşımak mı demek? Bu plan ekran yeri olarak okur (SAT-D51 aynen). | Modül sahipliği kararıdır |
| N-3 | Limit değişikliğine tutar eşiğine bağlı ikinci onay gerekir mi? | OPEN-009 (onay matrisi tutarları); başlangıçta tek ikinci kişi onayıdır (FNS-D27a) |
| OPEN-010 | Teminat kısmı **FNS-D31** ile cevaplandı (#66 `6819835966` m.4: azaltım yok, çek/senet teminat değil). Karar dosyası kapatmayı **önerir**; `ACIK_KARARLAR.md` satırı ayrı ve review'da doğrulanır. Risk motoru aktivasyonu ayrı kalır | `OPEN-*` kapatma insan kapısıdır; dayanak Sinan yorumu |

## 5. Sıra

1. Bu karar ve plan dosyası (docs PR, iki DOCS review). Kuyruk sırası: #1095 ve #1102'den sonra.
2. N-1 ve OPEN-010 için Issue #66'ya `NEEDS-SINAN` yazıldı:
   [`6819494783`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819494783) (madde 2 ve 3).
3. L1, ayrı K3 PR. Port değişikliği Satış'a sözleşme değişikliği değildir: aynı sözleşmede `NoSource` yerine `Available`
   döner. Satış'a bilgi notu gider.
4. L2, API-84 ilişkisi netleştikten sonra.
5. Satış, Lojistik ve Frontend devirleri koordinatör üzerinden gönderilir (FNS-D30 ve FNS-D31 dahil).

## 6. Kabul ölçütleri (L1)

- Şirket filtresi: başka şirketin limiti görünmez, 404 kod taşımaz.
- Yetkisiz istek 403 alır. Öneren kendi önerisini onaylayamaz; servis ve DB ayrı ayrı test edilir.
- İdempotency: aynı anahtar ve aynı gövde replay olur, farklı gövde 409 alır. Expected version uyuşmazlığı 409 alır.
- Port: onaylı limit `Available` döner (tek tutar, tam metin). Bekleyen öneri limiti değiştirmez. Bozuk satır
  `Unavailable` döner. Kayıt yokken cevap §2 L1'deki gibidir.
- Satış risk kontrolü: limit girildikten sonra karar artık hep `Unavailable` değildir. Gerçek SQL ve gerçek host ile
  `Within`/`Over` olguları gösterilir (diğer bileşenler hazırsa; değilse ilgili neden kodu).
- Migration Down guard'ının gerçek SQL testi; recovery, mimari koruma ve OpenAPI pin'leri.
