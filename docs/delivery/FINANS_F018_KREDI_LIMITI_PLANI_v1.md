# Finans F-018 — Müşteri kredi limiti ve teminatı: spec ve dilim planı v1

**Kararlar:** FNS-D27/FNS-D27a (`docs/decisions/SINAN_KARARLARI_2026-11-12_FINANS_KREDI_LIMITI.md`), SAT-D51,
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
  - **onaylı kayıt yoksa** cevap §4 N-1'in cevabına bağlıdır. Cevaba kadar `NoSource` kalır (bugünkü davranış) ve yeni bir
    neden kodu kullanılır (`finance_credit_limit_not_set`). Sıfır veya "limitsiz" asla dönmez.

### L2 — Müşteri teminatı (API-85b, K3)

- Müşteriden alınan teminatların kaydı: tür (Ayarlar listesi), tutar, para birimi, geçerlilik tarihleri ve belge
  referansı. Akış L1 ile aynıdır (öner, onay, ret; kayıt ekle-yalnızdır, çözüm ters kayıtla yapılır).
- Port `Collateral` cevabı tür ve para birimine göre tam tutarlardır. **Azaltım uygulanmaz.** OPEN-010 açık; oran Ayarlar'da
  başlangıç `0` olacak.
- Teminat brüt riske girmez, ekranda gösterilir (DEC-FIN-007 notu).
- API-84 (genel teminatlar; verilen/alınan BTM) ile ilişkisi L2 spec'inde netleşir. Ortak tablo mu ayrı tablo mu,
  sonradan tersine dönmesi pahalı olduğu için L2 başlamadan koordinatöre yazılır.

### L3 — Cari kartı paneli için okuma ucu (K2)

- Cari kartı ekranı (Claude frontend), Finans'ın okuma ucundan limit, teminat ve açık alacağı gösterir. Düzenleme düğmeleri
  Finans uçlarına gider (FNS-D27 (2)).
- Brüt risk ve "limit boşluğu" sayısı ekranda hesaplanmaz. Gösterilecekse Satış risk hesaplayıcısının cevabından gelir.

## 3. Diğer şeritlere devir (Finans uygulamaz)

| Şerit | İş | Not |
|---|---|---|
| Satış | Siparişte limit aşımı **uyarısı ve kaydı** (FNS-D27 (3)). Uyarı sipariş kaydını durdurmaz. | DEC-FIN-009 "uyarı/onay" metninin uyarı kısmı. Siparişte ayrı onay adımı bu kararla açılmaz |
| Satış | Lojistik sevk kapısının okuyacağı **sevk anı risk sonucu** portu: sonuç (`Within`/`Over`/`Unavailable`), tutar yok (SAT-D162). | Hesap Satış'ta kalır (DEC-FIN-007 bileşenlerinin ikisi Satış verisidir). Finans portu girdi olmaya devam eder. Lojistik'e ayrı bir Finans portu açılmaz; Lojistik Finans tablosu da okumaz |
| Lojistik | Sevk kapısı: `Over` → sevk durur; geçerli istisna (`Valid`) tek sevki serbest bırakır; tüketim ve sevk iptalinde düşme (SAT-D285). `Unavailable` durumunda ne olacağı §4 N-1'e bağlıdır. | Modüller arası sözleşme, K3 |
| Frontend (Claude) | Cari kartında Finans paneli (limit/teminat öner-onay, tarihçe), Satış uyarı gösterimi, Lojistik "sevk durdu / istisna" durumu. | `frontend-screen-states` |
| Counterparty | Kod işi yok. Limit Counterparty tablosuna yazılmaz. | SAT-D51 |

## 4. Açık noktalar

| Kimlik | Soru | Neden tahmin edilemez |
|---|---|---|
| N-1 (`NEEDS-SINAN`) | Limiti tanımlanmamış müşteride sevk durur mu, limitsiz mi sayılır, yoksa sevk mi engellenir? Satış hesaplayıcısı bugün bu durumda `Unavailable` (`credit_limit_not_defined`) verir. | Sevk durdurma politikasıdır (stok/para). Fail-closed sevk kapısı, limit girilmemiş bütün müşterilerin sevkini durdurur |
| N-2 (`NEEDS-SINAN`, gerekirse) | "Cari kartında tutulur" kaydın sahibini Cari'ye taşımak mı demek? Bu plan ekran yeri olarak okur (SAT-D51 aynen). | Modül sahipliği kararıdır |
| N-3 | Limit değişikliğine tutar eşiğine bağlı ikinci onay gerekir mi? | OPEN-009 (onay matrisi tutarları); başlangıçta tek ikinci kişi onayıdır (FNS-D27a) |
| OPEN-010 | Teminat azaltım oranları | Açık; başlangıç `0`, Ayarlar'dan yapılandırılır |

## 5. Sıra

1. Bu karar ve plan dosyası (docs PR, iki DOCS review). Kuyruk sırası: #1095 ve #1102'den sonra.
2. N-1 ve OPEN-010 için Issue #66'ya `NEEDS-SINAN` yazıldı:
   [`6819494783`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819494783) (madde 2 ve 3).
3. L1, ayrı K3 PR. Port değişikliği Satış'a sözleşme değişikliği değildir: aynı sözleşmede `NoSource` yerine `Available`
   döner. Satış'a bilgi notu gider.
4. L2, API-84 ilişkisi netleştikten sonra.
5. Satış ve Lojistik devirleri koordinatör üzerinden gönderilir. Lojistik kapısı N-1 cevabını bekler.

## 6. Kabul ölçütleri (L1)

- Şirket filtresi: başka şirketin limiti görünmez, 404 kod taşımaz.
- Yetkisiz istek 403 alır. Öneren kendi önerisini onaylayamaz; servis ve DB ayrı ayrı test edilir.
- İdempotency: aynı anahtar ve aynı gövde replay olur, farklı gövde 409 alır. Expected version uyuşmazlığı 409 alır.
- Port: onaylı limit `Available` döner (tek tutar, tam metin). Bekleyen öneri limiti değiştirmez. Bozuk satır
  `Unavailable` döner. Kayıt yokken cevap §2 L1'deki gibidir.
- Satış risk kontrolü: limit girildikten sonra karar artık hep `Unavailable` değildir. Gerçek SQL ve gerçek host ile
  `Within`/`Over` olguları gösterilir (diğer bileşenler hazırsa; değilse ilgili neden kodu).
- Migration Down guard'ının gerçek SQL testi; recovery, mimari koruma ve OpenAPI pin'leri.
