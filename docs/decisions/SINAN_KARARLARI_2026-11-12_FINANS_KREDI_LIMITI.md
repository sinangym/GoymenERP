# Sinan Kararları — 2026-11-12 (Finans: müşteri kredi limiti ve teminatı F-018, limitsiz müşteri sevki, teminat türleri; resmî no/ETTN CSV sütununun dilimi FIN-F03; kasa sorumlu kişisi)

Bu belge önceki karar dosyalarını silmez; FNS-D27…FNS-D32'yi ekler. FNS-D1…FNS-D26 geçerli kalır. Ayrıca şunlar aynen
geçerlidir: DEC-FIN-007 (brüt risk), DEC-FIN-009 (siparişte uyarı/onay, sevkte kesin kontrol, auditli istisna),
SAT-D46…SAT-D53, SAT-D80, SAT-D283…SAT-D287 (tek sevk risk istisnası) ve SAT-D51 (limit ve teminatın sahibi Finans,
Cari kartı okur ve gösterir). Bu belge `KARAR_SICILI` satırı eklemez veya değiştirmez. `OPEN-010`'un teminat kısmı FNS-D31 ile
cevaplanmıştır; `ACIK_KARARLAR.md` satırı bu belgede değiştirilmez (bkz. Sınırlar).

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
- Finans şeridi oturumu `session_015Usc2qRaBDjAUVC69G8xez`, 2026-11-12: Sinan'a Issue #66
  [`6819494783`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819494783) `NEEDS-SINAN` yorumundaki üç
  soru `AskUserQuestion` ile soruldu. 2. ve 3. soruya serbest metinle, 1. soruya önerilen seçenekle cevap verdi
  (aşağıda aynen). Sunulan seçenekler: 3. soru "Sevk dursun, istisnayla geçsin" / "Limitsiz sayılsın" / "Limit girmek
  zorunlu olsun" / "Şimdi karar vermeyeceğim"; 2. soru "Şimdilik 0, açık kalsın (Önerilen)" / "Tam tutar düşülsün" /
  "Türe göre oran vereceğim"; 1. soru "Şimdilik eklenmesin (Önerilen)" / "Zorunlu alan olarak eklensin" / "İsteğe bağlı
  alan olsun".
- Yorumlarda seçilmeyen seçeneklerin metni yoktur. Bu belge onları uydurmaz.

## Soru ve cevaplar (aynen)

| # | Soru | Cevap (Issue #66, aynen) | Kaynak |
|---|---|---|---|
| 1 | Kredi limiti (F-018 / OPEN-010) | "Limit ve teminat cari kartında, şirket bazında tutulur. **Siparişte uyarı** (kayıt alınır); **sevkte limit aşımı durdurur**, yetkili kişi açıklamayla istisna onayı verirse sevk devam eder. (Önerilen seçenek.)" | `6818788693` madde 1 |
| 2 | FIN-F03 CSV (FNS-D19; NEEDS-SINAN 6816881596) | "Resmî no / ETTN sütunu **CSV v2 / dosya aktarımı (BS-4) diliminde** eklenir; mevcut CSV v1 değişmez. Finans D5 ekran/API ayağıyla şimdi ilerler." | `6819201876` madde 3 |
| 3 | Döviz kuru (F-025), yalnız Finans'a düşen kısım | "Her iş günü **TCMB kuru otomatik** çekilir; yetkili kişi elle girer/düzeltir (denetim kaydıyla). Kur yoksa dövizli işlem durur. Kurlar ekranı eklenir. (Önerilen seçenek.)" | `6818788693` madde 3 |
| 4 | Limiti hiç tanımlanmamış müşteride sevk (`6819494783` madde 3) | "Burda aslında cevap vermiştim, yetkili kişiden tek seferlik onay istesin, ve onayı veren kişiye cari kartına öneri olarak kısayol ile limit tanımlayabilmesi önerilsin" | Finans oturumu, `AskUserQuestion` |
| 5 | Teminat ve azaltım oranı (OPEN-010; `6819494783` madde 2) | "Fatura tutarı teminattan düşülsün ve teminatlı alacak riskli alacak olarak görünsün, çek ler teminat sayılmayacak, teminat mektubu, ipotek, kredi kartı, vinov, dbs gibi banka garantileri teminat sayılacak" | Finans oturumu, `AskUserQuestion` |
| 6 | Kasaya "sorumlu kişi" alanı (API-30; `6819494783` madde 1) | "Şimdilik eklenmesin (Önerilen)" | Finans oturumu, `AskUserQuestion` |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D27 | Müşteri kredi limiti ve teminatı (F-018) | (1) Limit ve teminat **şirket bazında** tutulur; kullanıcı onları **cari kartında** görür ve yönetir. (2) Kaydın sahibi SAT-D51 gereği Finans modülüdür. Cari kartı (Counterparty) Finans'ın okuma portundan okur ve gösterir; ekran Finans'a ait bir panel olarak cari kartında yer alır. Counterparty tablosuna limit yazılmaz (AGENTS.md: modül başka modül tablosuna yazmaz). (3) **Siparişte** limit aşımı **uyarıdır**: sipariş kaydı alınır, uyarı kayda geçer. (4) **Sevkte** limit aşımı sevki **durdurur**. Yetkili kişi açıklama yazarak istisna onayı verirse sevk devam eder. Bu, mevcut tek sevk risk istisnasıdır (SAT-D283…SAT-D287): yükleme talimatına bağlıdır, tek sevkte tüketilir, sevk iptal edilirse düşer. (5) Limit/aşım kararı mevcut Satış hesaplayıcısıyla verilir: DEC-FIN-007 brüt riski, limit para birimindeki toplam (SAT-D53/SAT-D80), eşit değer limit içidir (SAT-D49). Finans yalnız girdileri (açık alacak, limit, teminat) verir; hesabı tekrarlamaz | API-85, `FinanceSalesPartyRiskReadPortV1` (`NoSource` kalkar), Satış risk kontrolü (API-06), Lojistik sevk kapısı, Cari kartı paneli (UI) |
| FNS-D27a | Uygulama ayrıntısı (geri alınabilir) | Limit, cari hesabın (companyAccountId) **tek bir tutar ve tek bir para birimidir**; Satış hesaplayıcısı birden fazla tutarı `credit_limit_not_defined` sayar. Limitin konması, değiştirilmesi ve kaldırılması **öner → başka kullanıcı onaylar** (maker≠checker, FS0/API-30 emsali) akışıyla yapılır. Kayıt ekle-yalnız tarihçeyle tutulur; geçerli değer en son onaylanmış kayıttır. Tutarlar tam ondalık metindir, yuvarlanmaz | API-85 |
| FNS-D28 | Resmî no / ETTN dosya sütunu (FIN-F03) | (1) Resmî fatura numarası ve ETTN sütunu **CSV v2 / BS-4 dosya aktarımı diliminde** eklenir. (2) Mevcut CSV v1 **değişmez**. (3) FNS-D19 çift alacak reddinin (D5) bu turdaki uygulaması yalnız **ekran/API** ayağıdır. Satış sözleşme alanları (#1103) merge edildikten sonra ilerler | F-005 D5, BS-4 dosya aktarımı dilimi |
| FNS-D29 | Dövizli Finans işleminde kur yokluğu | Kur kaydı, TCMB çekimi ve Kurlar ekranı Ayarlar modülünündür. Finans'ta bir işlem için Ayarlar kur çözümleyicisinden (`ISettingsExchangeRateResolverV1`) kur gerekiyorsa ve cevap `Unavailable` ise işlem **durur** (fail-closed). Finans kur tahmin etmez veya varsayılan kur kullanmaz. Hangi Finans işleminin kur gerektirdiği her dilimin kendi spec'indedir | İlgili Finans dilimleri (kur gerektirenler) |
| FNS-D30 | Limiti tanımlanmamış müşteride sevk | (1) Onaylı limiti olmayan müşteride sevk **durur**; yetkili kişinin **tek seferlik onayı** ile devam eder. Bu onay mevcut tek sevk risk istisnasıdır (SAT-D283…SAT-D287): yükleme talimatına bağlıdır, tek sevkte tüketilir. "Limitsiz" veya "sıfır limit" sayılmaz. (2) Onayı veren kişiye, cari kartında o müşteri için **limit tanımlama kısayolu öneri olarak** gösterilir. Kısayol yalnız limit **önerisi** açar; öneri FNS-D27a gereği başka bir kullanıcının onayıyla geçerli olur (maker≠checker değişmez). Öneriyi kullanmak zorunlu değildir | Lojistik sevk kapısı, Satış risk sonucu/istisna, Cari kartı paneli (UI), `FinanceSalesPartyRiskReadPortV1` |
| FNS-D31 | Müşteri teminatı (OPEN-010'un teminat kısmı) | (1) **Teminat sayılır:** teminat mektubu, ipotek, kredi kartı (teminatı), Vinov, DBS gibi **banka garantileri**. (2) **Çek teminat sayılmaz.** (3) Teminat tutarından açık fatura tutarı düşülür ("kalan teminat"); azaltım (haircut) oranı uygulanmaz, teminat tam tutarıyla girer. (4) Teminatla karşılanan alacak ekranda **riskli alacak olarak görünmeye devam eder**: teminat brüt riski düşürmez (DEC-FIN-007 notu ile uyumlu), "teminatlı alacak" ayrıca gösterilir | API-85b, `FinanceSalesPartyRiskReadPortV1.Collateral`, Cari kartı paneli (UI), `ACIK_KARARLAR.md` OPEN-010 |
| FNS-D32 | Kasa "sorumlu kişi" alanı (API-30) | Şimdilik eklenmez. API-30 (#1109) bugünkü kapsamıyla ilerler; gerekirse ayrı dilimde sorulur | API-30 |

## Kapatılan açık sorular

- `6816881596` (FIN-F03 CSV sütunu) → FNS-D28.

## Sınırlar

- **OPEN-010:** teminat kısmı FNS-D31 ile cevaplandı (azaltım yok, çek hariç, banka garantileri dahil). OPEN-010'un
  "risk motoru aktivasyon politikası" kısmı ve DBS'nin Satış/Finans tarafındaki ayrıntıları bu cevapta yoktur.
  `ACIK_KARARLAR.md` satırının güncellenmesi ayrı bir karar kaydı işidir; bu belge o satırı değiştirmez.
- **FNS-D31 okuması:** "teminatlı alacak riskli alacak olarak görünsün" ifadesi, teminatın brüt riski düşürmediği ve
  teminatla karşılanan alacağın riskli alacak toplamında kaldığı şeklinde okunur. Sinan teminatın limit kontrolünde
  (sevk durdurma) riski azaltmasını kastettiyse bunu tek satırla düzeltir; o zamana kadar sevk kararı brüt riskle verilir.
  "Kredi kartı" müşterinin kredi kartıyla verdiği teminat/provizyon, "Vinov" ise adı geçen banka garantili finansman
  ürünü olarak okunur; tür listesi Ayarlar'da yönetilir.
- **Limiti tanımlanmamış müşteri** `NEEDS-SINAN`'ı
  ([`6819494783`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819494783) madde 3) FNS-D30 ile
  kapandı. "Yetkili kişi" için yeni rol veya izin seçilmez: istisnayı onaylama yetkisi mevcut SAT-D283…SAT-D287
  izinleridir.
- "Cari kartında tutulur" ifadesi bu belgede **ekran yeri** olarak okunur; kaydın sahibi SAT-D51 gereği Finans'tır. Sinan
  bununla kaydın sahibini Cari'ye taşımayı kastettiyse SAT-D51'i tek satırla değiştirir; o durumda FNS-D27 (2)
  güncellenir.
- Siparişte uyarının kaydı ("kayıt alınır") ve sevk kapısının uygulanması Satış ve Lojistik şeritlerinin işidir. Finans
  bunları uygulamaz.
- Limit/teminat izin adları kayıtlı olur, hiçbir role verilmez. Deployment, environment, secret, Settings'te
  etkinleştirme, grant/rol/Critical, production aktivasyonu ve `OPEN-*` kapatma kapıları değişmez.
