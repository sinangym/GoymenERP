# Sinan Kararları — 2026-11-12 (Finans: müşteri kredi limiti ve teminatı F-018, limitsiz müşteri sevki, teminat türleri ve oranı, limitin yetkili tarafından tek adımda tanımlanması; resmî no/ETTN CSV sütununun dilimi FIN-F03; kasa sorumlu kişisi)

Bu belge önceki karar dosyalarını silmez; FNS-D27…FNS-D32'yi ekler. FNS-D1…FNS-D26 geçerli kalır. Ayrıca şunlar aynen
geçerlidir: DEC-FIN-007 (brüt risk), DEC-FIN-009 (siparişte uyarı/onay, sevkte kesin kontrol, auditli istisna),
SAT-D46…SAT-D53, SAT-D80, SAT-D283…SAT-D287 (tek sevk risk istisnası) ve SAT-D51 (limit ve teminatın sahibi Finans,
Cari kartı okur ve gösterir). SAT-D284 (2) yalnız FNS-D30'da yazılan durumlar için genişler. Bu belge `KARAR_SICILI`
satırı eklemez veya değiştirmez. `OPEN-010`'un "gerçek haircut oranları" kısmı FNS-D31 ve #66 `6821262098` madde 6 ile
cevaplanmıştır; Sinan bu kısmın kapanmasını seçmiştir. `ACIK_KARARLAR.md` satırı burada değiştirilmez, ayrı review'lu
değişikliktir (bkz. Sınırlar).

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
- Koordinatör oturumunda aynı sorular yeniden soruldu ve Issue #66'ya yazıldı:
  [`6819640816`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819640816) ("SINAN-KARARI 2026-11-12
  (5. tur)", 09:16:55Z) madde 1 ve
  [`6819835966`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819835966) ("SINAN-KARARI 2026-11-12
  (6. tur)", 09:26:29Z) madde 4. İki kaynak birbiriyle uyumludur. 6. tur teminat cevabı daha ayrıntılıdır ve FNS-D31
  ona göre yazılmıştır.
- Issue #66 yorumu
  [`6819908312`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819908312) ("SINAN-KARARI 2026-11-12
  (7. tur)", 09:30:03Z) madde 2, aynen: "**Gerek yok**; alan eklenmez (kasa işlemini yapan kişi zaten kayıtlı)."
  FNS-D32'yi teyit eder.
- Issue #66 yorumu
  [`6821262098`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821262098) ("SINAN-KARARI 2026-11-12
  (17.–18. tur)", 10:35:27Z) madde 1, 5 ve 6. Koordinatör oturumunda `AskUserQuestion` ile alındı; bu PR'ın review
  bulgularına (DOCS-REVIEW A `6821060368`, B `6821069797`) dayanır. FNS-D27a, FNS-D30 ve FNS-D31 (3) bu yoruma göre
  yazılmıştır; önceki taslaktaki "öner → başka kullanıcı onaylar" ve "azaltım uygulanmaz" ifadelerinin yerine geçer.
  Aynı yorumun öteki maddeleri (N-6, ters e-İrsaliye, ilk Admin, kabul geri alma, pilot ürün verisi) bu belgenin konusu
  değildir.
- Yorumlarda seçilmeyen seçeneklerin metni yoktur. Bu belge onları uydurmaz.

## Soru ve cevaplar (aynen)

| # | Soru | Cevap (Issue #66, aynen) | Kaynak |
|---|---|---|---|
| 1 | Kredi limiti (F-018 / OPEN-010) | "Limit ve teminat cari kartında, şirket bazında tutulur. **Siparişte uyarı** (kayıt alınır); **sevkte limit aşımı durdurur**, yetkili kişi açıklamayla istisna onayı verirse sevk devam eder. (Önerilen seçenek.)" | `6818788693` madde 1 |
| 2 | FIN-F03 CSV (FNS-D19; NEEDS-SINAN 6816881596) | "Resmî no / ETTN sütunu **CSV v2 / dosya aktarımı (BS-4) diliminde** eklenir; mevcut CSV v1 değişmez. Finans D5 ekran/API ayağıyla şimdi ilerler." | `6819201876` madde 3 |
| 3 | Döviz kuru (F-025), yalnız Finans'a düşen kısım | "Her iş günü **TCMB kuru otomatik** çekilir; yetkili kişi elle girer/düzeltir (denetim kaydıyla). Kur yoksa dövizli işlem durur. Kurlar ekranı eklenir. (Önerilen seçenek.)" | `6818788693` madde 3 |
| 4 | Limiti hiç tanımlanmamış müşteride sevk (`6819494783` madde 3) | "Burda aslında cevap vermiştim, yetkili kişiden tek seferlik onay istesin, ve onayı veren kişiye cari kartına öneri olarak kısayol ile limit tanımlayabilmesi önerilsin" | Finans oturumu, `AskUserQuestion` |
| 5 | Teminat ve azaltım oranı (OPEN-010; `6819494783` madde 2) | "Fatura tutarı teminattan düşülsün ve teminatlı alacak riskli alacak olarak görünsün, çek ler teminat sayılmayacak, teminat mektubu, ipotek, kredi kartı, vinov, dbs gibi banka garantileri teminat sayılacak" | Finans oturumu, `AskUserQuestion` |
| 4a | Limiti tanımlanmamış müşteri (5. tur) | "Sevk **durur**, uyarı çıkar; yetkili kişi **istisna onayı** verirse sevk devam eder. Limit sonradan cari kartta girilir. (Önerilen seçenek.)" | `6819640816` madde 1 |
| 5a | Teminat (6. tur) | "Banka garantililer, dbs, teminat mektubu, kredi kartı, vinov, ipotek teminat sayılsın ve fatura tutarı bu teminatta düşülsün, varolan teminat , teminat doluluğu, riskteki tutar ayrı ayrı görülsün, çek ve senet  bir teminat değildir bunlar teminatta görünmeyecek direk riskte görünecek, çek ve senet için ayrı bir risk gösterimi yapalım hatta evraklı riski diye" | `6819835966` madde 4 |
| 6 | Kasaya "sorumlu kişi" alanı (API-30; `6819494783` madde 1) | "Şimdilik eklenmesin (Önerilen)" | Finans oturumu, `AskUserQuestion` |
| 7 | N-5 — risk okunamazsa sevk (#1119) | "**Cevap: Evet, istisnayla geçer.** (Yetkili onaylarsa risk okunamasa da tek sevk çıkar.)" | `6821262098` madde 1 |
| 8 | Kredi limiti tanımı (#1117 FNS-D27a/FNS-D30 (2)) | "**Cevap: Yetkili tek adımda.** Limit onay yetkisi olan kişi limiti doğrudan tanımlar ve değiştirir; kim, ne zaman, eski/yeni değer denetim izinde kalır. Yetkisi olmayan kullanıcı yalnız öneri açar." | `6821262098` madde 5 |
| 9 | Teminat oranı (OPEN-010, #1117 FNS-D31) | "**Cevap: Türe göre oran, %100.** Ayarlarda teminat türü başına oran alanı olur, başlangıç değeri %100 (tam tutar). İleride oran ayardan değiştirilebilir. Sinan OPEN-010'un "gerçek haircut oranları" kısmının bu cevapla kapanmasını seçti ("OPEN-010 kapanır" seçeneği); "risk motoru aktivasyon politikası" kısmı bu cevabın konusu değildir. `ACIK_KARARLAR.md` güncellemesi ayrı review'lu değişikliktir." | `6821262098` madde 6 |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D27 | Müşteri kredi limiti ve teminatı (F-018) | (1) Limit ve teminat **şirket bazında** tutulur; kullanıcı onları **cari kartında** görür ve yönetir. (2) Kaydın sahibi SAT-D51 gereği Finans modülüdür. Cari kartı (Counterparty) Finans'ın okuma portundan okur ve gösterir; ekran Finans'a ait bir panel olarak cari kartında yer alır. Counterparty tablosuna limit yazılmaz (AGENTS.md: modül başka modül tablosuna yazmaz). (3) **Siparişte** limit aşımı **uyarıdır**: sipariş kaydedilir ("kayıt alınır") ve uyarı siparişin denetim kaydına geçer. (4) **Sevkte** limit aşımı sevki **durdurur**. Yetkili kişi açıklama yazarak istisna onayı verirse sevk devam eder. Bu, mevcut tek sevk risk istisnasıdır (SAT-D283…SAT-D287): yükleme talimatına bağlıdır, tek sevkte tüketilir, sevk iptal edilirse düşer, onaylandığı iş gününün sonuna kadar geçerlidir (SAT-D160). Audit, idempotency ve expected version mevcut API-07 komutlarındadır. (5) Limit/aşım kararı mevcut Satış hesaplayıcısıyla verilir: DEC-FIN-007 brüt riski, limit para birimindeki toplam (SAT-D53/SAT-D80), eşit değer limit içidir (SAT-D49). Finans yalnız girdileri (açık alacak, limit, teminat) verir; hesabı tekrarlamaz | API-85, `FinanceSalesPartyRiskReadPortV1` (`NoSource` kalkar), Satış risk kontrolü (API-06), Lojistik sevk kapısı, Cari kartı paneli (UI) |
| FNS-D27a | Limitin tanımlanması (#66 `6821262098` m.5) ve uygulama ayrıntısı | (1) **Yetkili tek adımda:** limit onay yetkisi olan kişi limiti **doğrudan** tanımlar, değiştirir ve kaldırır; ikinci onay beklenmez. Kim, ne zaman, eski ve yeni değer denetim izinde kalır. (2) Yetkisi olmayan kullanıcı yalnız **öneri** açar; öneri, limit onay yetkisi olan bir kişinin onayı veya gerekçeli reddiyle sonuçlanır. Öneren kendi önerisine karar veremez (uygulama ayrıntısı, geri alınabilir). (3) Uygulama ayrıntısı (geri alınabilir): limit cari hesabın (companyAccountId) **tek bir tutar ve tek bir para birimidir**; Satış hesaplayıcısı `Amounts.Count != 1` olan limiti (tutar yok veya birden fazla tutar) `credit_limit_not_defined` sayar. Kayıt ekle-yalnız tarihçeyle tutulur; geçerli değer en son geçerli kayıttır; kaldırma ayrı bir kayıt türüdür. Tutarlar tam ondalık metindir, yuvarlanmaz | API-85 |
| FNS-D28 | Resmî no / ETTN dosya sütunu (FIN-F03) | (1) Resmî fatura numarası ve ETTN sütunu **CSV v2 / BS-4 dosya aktarımı diliminde** eklenir. (2) Mevcut CSV v1 **değişmez**. (3) FNS-D19 çift alacak reddinin (D5) bu turdaki uygulaması yalnız **ekran/API** ayağıdır ve **şimdi** ilerler. Yalnız Satış sözleşme alanlarına dayanan kısmı, o alanlar (#1103) main'e girdikten sonra bağlanır | F-005 D5, BS-4 dosya aktarımı dilimi |
| FNS-D29 | Dövizli Finans işleminde kur yokluğu | Kur kaydı, TCMB çekimi ve Kurlar ekranı Ayarlar modülünündür. Finans'ta bir işlem için Ayarlar kur çözümleyicisinden (`ISettingsExchangeRateResolverV1`) kur gerekiyorsa ve cevap `Unavailable` ise işlem **durur** (fail-closed). Finans kur tahmin etmez veya varsayılan kur kullanmaz. Hangi Finans işleminin kur gerektirdiği her dilimin kendi spec'indedir | İlgili Finans dilimleri (kur gerektirenler) |
| FNS-D30 | Limiti tanımlanmamış müşteride ve risk okunamadığında sevk | (1) Geçerli limiti olmayan müşteride sevk **durur** ve uyarı çıkar; yetkili kişinin **tek seferlik onayı** ile devam eder. Bu onay mevcut tek sevk risk istisnasıdır (SAT-D283…SAT-D287, SAT-D160): yükleme talimatına bağlıdır, tek sevkte tüketilir, iş günü sonunda düşer. "Limitsiz" veya "sıfır limit" sayılmaz. SAT-D284 (2)'deki "yalnız riski limiti aşan sipariş için istisna istenebilir" kuralı bu durum için genişler: risk sonucu `Unavailable` olsa da istisna talebi açılır. (2) Onayı veren kişiye, cari kartında o müşteri için **limit tanımlama kısayolu** önerilir. Kısayolu kullanan kişinin limit onay yetkisi varsa limit FNS-D27a (1) gereği tek adımda geçerli olur; yoksa kısayol yalnız öneri açar (FNS-D27a (2)). Kısayol yetkiyle kapılanır: kişinin limit öneri veya onay yetkisi yoksa gösterilmez. Kullanmak zorunlu değildir (Finans oturumu cevabı; #66 `6821262098` m.5). (3) **Risk okunamazsa** (Finans, kur veya fatura kaynağına ulaşılamazsa) da sevk aynı tek sevk istisnasıyla geçebilir (#66 `6821262098` m.1, N-5). (4) Lojistik, Finans neden kodunu görmez; durumları Satış'ın sevk anı portundaki tutar içermeyen **neden kategorisinden** ayırır (#1119: `OverLimit` / `CreditLimitNotSet` / `SourceUnavailable`) | Lojistik sevk kapısı, Satış risk sonucu/istisna (SAT-D284), Cari kartı paneli (UI), `FinanceSalesPartyRiskReadPortV1` |
| FNS-D31 | Müşteri teminatı ve evraklı risk (OPEN-010'un teminat kısmı) | (1) **Teminat sayılır:** banka garantili ödemeler, DBS, teminat mektubu, kredi kartı, Vinov, ipotek. Teminat türü listesi Ayarlar'da **yapılandırılabilir** tutulur. (2) **Çek ve senet teminat değildir:** teminat alanında görünmez, doğrudan riske girer ve ayrı **"evraklı risk"** olarak gösterilir. (3) Fatura tutarı teminattan düşülür. **Teminat oranı türe göredir:** Ayarlar'da teminat türü başına oran alanı olur, başlangıç değeri **%100** (tam tutar); oran ileride ayardan değiştirilebilir (#66 `6821262098` m.6). Bu, 2026-10-21 K6 (azaltım kapsam önceliği: tür+banka+vade > tür+banka > tür > şirket geneli) ve K7 (risk yüzde alanları 0–100) ile tutarlıdır: m.6 yalnız "tür" kapsamını ve başlangıç değerini seçer; K6/K7 geçerli kalır. (4) Ekranda **mevcut teminat**, **teminat doluluğu** (teminattan düşülen fatura tutarı) ve **riskteki tutar** ayrı ayrı görünür. (5) Teminat brüt riski düşürmez (DEC-FIN-007 notu): teminatla karşılanan alacak da riskli alacak olarak görünür | API-85b, `FinanceSalesPartyRiskReadPortV1.Collateral`, evraklı risk okuma ucu, Cari kartı paneli (UI), `ACIK_KARARLAR.md` OPEN-010 |
| FNS-D32 | Kasa "sorumlu kişi" alanı (API-30) | Eklenmez (Finans oturumu "Şimdilik eklenmesin"; #66 `6819908312` m.2 "Gerek yok"). API-30 (#1109) bugünkü kapsamıyla ilerler; gerekirse ayrı dilimde sorulur | API-30 |

## Kapatılan açık sorular

- `6816881596` (FIN-F03 CSV sütunu) → FNS-D28.
- `6819494783` madde 1 (kasa sorumlu kişisi) → FNS-D32; madde 2 (teminat ve azaltım oranı) → FNS-D31 (ve `6821262098`
  m.6); madde 3 (limiti tanımlanmamış müşteri) → FNS-D30.

## Sınırlar

- **OPEN-010:** "Gerçek haircut oranları" kısmı `6821262098` madde 6 ile cevaplanmıştır (türe göre oran, başlangıç
  %100) ve Sinan bu kısmın kapanmasını seçmiştir. Teminat türleri ve çek/senet dışlaması `6819835966` madde 4'tedir.
  `ACIK_KARARLAR.md` satırı bu PR'da değiştirilmez; kapatma, review'da doğrulanan ayrı bir değişikliktir. "Risk motoru
  aktivasyon politikası" bu cevabın konusu değildir ve açık kalır.
- **FNS-D31 okumaları (geri alınabilir):**
  - "Teminat doluluğu": teminattan düşülmüş açık fatura tutarıdır. "Riskteki tutar": Satış risk hesaplayıcısının brüt
    riskidir. Ekranda hesaplanmaz, sunucudan gelir.
  - "Fatura tutarı bu teminattan düşülsün" ifadesi teminatın kullanımı/doluluğu olarak okunur. Teminat limit kontrolünde
    brüt riski düşürmez, sevk kararı brüt riskle verilir. Sinan başka bir şey kastettiyse tek satırla düzeltir.
  - "Evraklı risk": müşteriden alınıp henüz tahsil edilmemiş çek ve senetlerin toplamıdır. DEC-FIN-016 gereği borç
    tahsilata kadar açık kalemde durur; bu yüzden evraklı risk brüt riske **ek bir toplam değildir**, risk içindeki bir
    dökümdür (çift sayım yoktur). Çek/senet portföyünün sahibi ve kaynağı L2 spec'inde netleşir (API-84 ilişkisiyle
    birlikte).
  - Çek ve senedin teminat sayılmaması sunucuda zorlanır: yapılandırılabilir tür listesine teminat türü olarak
    eklenemezler.
  - Teminat oranı (FNS-D31 (3)) sayılan teminat tutarını belirler; (5) gereği teminat brüt riski düşürmez. Oranın
    uygulandığı yer (Finans portu veya okuma ucu) L2 spec'inde yazılır.
  - "Kredi kartı" müşterinin kredi kartıyla verdiği teminat/provizyon olarak okunur. "Vinov" adı geçen banka garantili
    finansman ürünüdür. Tür listesi yapılandırılabilir olduğundan bu okumalar kodda sabitlenmez.
- **Limiti tanımlanmamış müşteri** `NEEDS-SINAN`'ı
  ([`6819494783`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6819494783) madde 3) FNS-D30 ile
  kapandı. "Yetkili kişi" için yeni rol seçilmez: istisnayı onaylama yetkisi mevcut `sales.orders.risk-exception.approve`
  iznidir. Limit onay yetkisi için izin adı L1'de önerilir (`finance.credit-limits.approve`); kime verileceği grant
  kararıdır.
- "Cari kartında tutulur" ifadesi bu belgede **ekran yeri** olarak okunur; kaydın sahibi SAT-D51 gereği Finans'tır. Sinan
  bununla kaydın sahibini Cari'ye taşımayı kastettiyse SAT-D51'i tek satırla değiştirir; o durumda FNS-D27 (2)
  güncellenir.
- Siparişte uyarı ve uyarının denetim kaydı (FNS-D27 (3)), istisna talebinin `Unavailable` durumuna açılması, neden
  kategorisi ve sevk kapısının uygulanması Satış ve Lojistik şeritlerinin işidir. Finans bunları uygulamaz. Siparişte
  `Unavailable` için uyarı kaydı (#1119 N-4) bu belgenin konusu değildir.
- Limit/teminat izin adları kayıtlı olur, hiçbir role verilmez. Critical olup olmadıkları seçilmemiştir; Critical
  işaretlenmezler (insan kapısı). Deployment, environment, secret, Settings'te
  etkinleştirme, grant/rol/Critical, production aktivasyonu ve `OPEN-*` kapatma kapıları değişmez.
