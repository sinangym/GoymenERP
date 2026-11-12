# Sinan Kararları — 2026-11-12 (Finans: müşteri kredi limiti ve teminatı F-018; resmî no/ETTN CSV sütununun dilimi FIN-F03)

Bu belge önceki karar dosyalarını silmez; FNS-D27…FNS-D29'u ekler. FNS-D1…FNS-D26 geçerli kalır. Ayrıca şunlar aynen
geçerlidir: DEC-FIN-007 (brüt risk), DEC-FIN-009 (siparişte uyarı/onay, sevkte kesin kontrol, auditli istisna),
SAT-D46…SAT-D53, SAT-D80, SAT-D283…SAT-D287 (tek sevk risk istisnası) ve SAT-D51 (limit ve teminatın sahibi Finans,
Cari kartı okur ve gösterir). Bu belge `KARAR_SICILI` satırı eklemez veya değiştirmez. `OPEN-010` kapatılmaz: teminat
azaltım (haircut) oranı bu kararlarda yoktur.

**Kaynak:**

- Issue #66 yorumu
  [`6818788693`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6818788693) ("SINAN-KARARI 2026-11-12
  (2. tur)", 2026-11-12T08:34:35Z), madde 1. Koordinatör oturumunda `AskUserQuestion` ile alındı; Sinan önerilen
  seçeneği seçti. "(Önerilen)" işareti soranındır.
- Issue #66 yorumu
  [`6819201876`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819201876) ("SINAN-KARARI 2026-11-12
  (4. tur)", 2026-11-12T08:55:15Z), madde 3. Bu, Finans şeridinin `NEEDS-SINAN` sorusunun
  ([`6816881596`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6816881596)) cevabıdır. Sinan önerilen
  seçeneği seçti.
- Aynı yorumların öteki maddeleri (tartı farkı, döviz kuru, yedekleme, transfer, denetim kaydı, CI shard) bu belgenin
  konusu değildir. Döviz kuru (F-025) Ayarlar modülünündür (`src/backend/Modules/Settings/README.md`, "Exchange-rate
  record"). Finans'a düşen kısmı FNS-D29'da yazılıdır.
- Yorumlarda seçilmeyen seçeneklerin metni yoktur. Bu belge onları uydurmaz.

## Soru ve cevaplar (aynen)

| # | Soru | Cevap (Issue #66, aynen) | Kaynak |
|---|---|---|---|
| 1 | Kredi limiti (F-018 / OPEN-010) | "Limit ve teminat cari kartında, şirket bazında tutulur. **Siparişte uyarı** (kayıt alınır); **sevkte limit aşımı durdurur**, yetkili kişi açıklamayla istisna onayı verirse sevk devam eder. (Önerilen seçenek.)" | `6818788693` madde 1 |
| 2 | FIN-F03 CSV (FNS-D19; NEEDS-SINAN 6816881596) | "Resmî no / ETTN sütunu **CSV v2 / dosya aktarımı (BS-4) diliminde** eklenir; mevcut CSV v1 değişmez. Finans D5 ekran/API ayağıyla şimdi ilerler." | `6819201876` madde 3 |
| 3 | Döviz kuru (F-025), yalnız Finans'a düşen kısım | "Her iş günü **TCMB kuru otomatik** çekilir; yetkili kişi elle girer/düzeltir (denetim kaydıyla). Kur yoksa dövizli işlem durur. Kurlar ekranı eklenir. (Önerilen seçenek.)" | `6818788693` madde 3 |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D27 | Müşteri kredi limiti ve teminatı (F-018) | (1) Limit ve teminat **şirket bazında** tutulur; kullanıcı onları **cari kartında** görür ve yönetir. (2) Kaydın sahibi SAT-D51 gereği Finans modülüdür. Cari kartı (Counterparty) Finans'ın okuma portundan okur ve gösterir; ekran Finans'a ait bir panel olarak cari kartında yer alır. Counterparty tablosuna limit yazılmaz (AGENTS.md: modül başka modül tablosuna yazmaz). (3) **Siparişte** limit aşımı **uyarıdır**: sipariş kaydı alınır, uyarı kayda geçer. (4) **Sevkte** limit aşımı sevki **durdurur**. Yetkili kişi açıklama yazarak istisna onayı verirse sevk devam eder. Bu, mevcut tek sevk risk istisnasıdır (SAT-D283…SAT-D287): yükleme talimatına bağlıdır, tek sevkte tüketilir, sevk iptal edilirse düşer. (5) Limit/aşım kararı mevcut Satış hesaplayıcısıyla verilir: DEC-FIN-007 brüt riski, limit para birimindeki toplam (SAT-D53/SAT-D80), eşit değer limit içidir (SAT-D49). Finans yalnız girdileri (açık alacak, limit, teminat) verir; hesabı tekrarlamaz | API-85, `FinanceSalesPartyRiskReadPortV1` (`NoSource` kalkar), Satış risk kontrolü (API-06), Lojistik sevk kapısı, Cari kartı paneli (UI) |
| FNS-D27a | Uygulama ayrıntısı (geri alınabilir) | Limit, cari hesabın (companyAccountId) **tek bir tutar ve tek bir para birimidir**; Satış hesaplayıcısı birden fazla tutarı `credit_limit_not_defined` sayar. Limitin konması, değiştirilmesi ve kaldırılması **öner → başka kullanıcı onaylar** (maker≠checker, FS0/API-30 emsali) akışıyla yapılır. Kayıt ekle-yalnız tarihçeyle tutulur; geçerli değer en son onaylanmış kayıttır. Tutarlar tam ondalık metindir, yuvarlanmaz | API-85 |
| FNS-D28 | Resmî no / ETTN dosya sütunu (FIN-F03) | (1) Resmî fatura numarası ve ETTN sütunu **CSV v2 / BS-4 dosya aktarımı diliminde** eklenir. (2) Mevcut CSV v1 **değişmez**. (3) FNS-D19 çift alacak reddinin (D5) bu turdaki uygulaması yalnız **ekran/API** ayağıdır. Satış sözleşme alanları (#1103) merge edildikten sonra ilerler | F-005 D5, BS-4 dosya aktarımı dilimi |
| FNS-D29 | Dövizli Finans işleminde kur yokluğu | Kur kaydı, TCMB çekimi ve Kurlar ekranı Ayarlar modülünündür. Finans'ta bir işlem için Ayarlar kur çözümleyicisinden (`ISettingsExchangeRateResolverV1`) kur gerekiyorsa ve cevap `Unavailable` ise işlem **durur** (fail-closed). Finans kur tahmin etmez veya varsayılan kur kullanmaz. Hangi Finans işleminin kur gerektirdiği her dilimin kendi spec'indedir | İlgili Finans dilimleri (kur gerektirenler) |

## Kapatılan açık sorular

- `6816881596` (FIN-F03 CSV sütunu) → FNS-D28.

## Sınırlar

- **Teminat azaltım oranı (OPEN-010)** seçilmedi. Teminat, net risk girdisidir ve ekranda yalnız gösterilir; brüt riske
  girmez (DEC-FIN-007 notu). Oran tür/banka/vade bazında Ayarlar'dan yönetilecek ve başlangıçta `0` olacak
  (`ACIK_KARARLAR.md` OPEN-010 KISMEN). `OPEN-010` bu belgeyle kapatılmaz.
- **Limiti tanımlanmamış müşteri** için sevkin durup durmayacağı bu kararlarda yoktur. Bugün Satış hesaplayıcısı limitsiz
  müşteride kararı "hesaplanamadı" (`credit_limit_not_defined`) verir; sevk kapısı fail-closed tasarlanırsa sevk durur.
  Bunun için `NEEDS-SINAN` açıldı
  ([`6819494783`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819494783) madde 3); cevaba kadar
  tahminle etkinleştirilmez.
- "Cari kartında tutulur" ifadesi bu belgede **ekran yeri** olarak okunur; kaydın sahibi SAT-D51 gereği Finans'tır. Sinan
  bununla kaydın sahibini Cari'ye taşımayı kastettiyse SAT-D51'i tek satırla değiştirir; o durumda FNS-D27 (2)
  güncellenir.
- Siparişte uyarının kaydı ("kayıt alınır") ve sevk kapısının uygulanması Satış ve Lojistik şeritlerinin işidir. Finans
  bunları uygulamaz.
- Limit/teminat izin adları kayıtlı olur, hiçbir role verilmez. Deployment, environment, secret, Settings'te
  etkinleştirme, grant/rol/Critical, production aktivasyonu ve `OPEN-*` kapatma kapıları değişmez.
