# Sinan Kararları — 2026-11-02 Alış EK9 (koordinatör yetkisi, modül önceliği, borç Finans'ta, fiyat birimi, min-stok, fatura→Finans portu, Ayarlar istisnası, işlem sınırı, Lojistik)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda (Alış şeridi), başka bir Claude oturumunun
(koordinatör) onun adına ilettiği talimatlara, EK7'de açık kalan fiyat birimi sorusuna (ALS-D51 NEEDS-SINAN), plan
§14.1 **MINSTOK** kapsam sorusuna ve gelen fatura borcunun Finans'a iletim yoluna (A5, MOD-04) ve ardından sorulan üç takip sorusuna (Alış
politika kayıtlarının yeri, fatura→Finans işlem sınırı, Lojistik'in ertelenmesi; PR #924 review bulguları üzerine)
verdiği **doğrudan cevaplar**. Sorular, bütün seçenekler ve seçenek açıklamaları aynen aktarılmıştır; "(Önerilen)" işareti Claude'un
soruyu sorarken koyduğu işarettir. Serbest metin cevap yazım düzeltmesi yapılmadan alınmıştır. Claude yalnız soruları
ve seçenekleri hazırlamıştır; seçimi Sinan yapmıştır.

Numaralama: ALS-D47 (talebin geri çekilmesi: oluşturan veya gönderen), paralel PR #907 ile eklenecek EK6'dadır;
ALS-D48…D52 EK7'de, ALS-D53…D56 EK8'dedir. Bu dosya ALS-D57…D65'i içerir; EK6 bu dosyanın parçası değildir.

## 0. Yetki ve sınır

- `SINAN_KARARLARI_2026-11-02_ALIS.md` §0 (ALS-D45 ile daraltılmış hâliyle) ve EK8 §0'daki sürekli talimat
  geçerlidir. Seçilen seçenekler doğrudan kullanıcı cevabıdır.
- **Değişmeyen insan kapıları:** production aktivasyonu, secret/deployment, grant/rol/Critical ve `OPEN-*` kapatma
  insanda kalır (AGENTS.md). ALS-D57 bu kapıların hiçbirini koordinatöre veya Claude'a devretmez.
- Bu dosya hiçbir `OPEN-*` kimliğini kapatmaz, grant/rol vermez, persona kartını değiştirmez.

### 0.1 Koordinatör oturumunun ilettiği talimatlar (bağlam; ALS-D57 ile Sinan onayladı)

Başka bir Claude oturumu (koordinatör) Sinan adına aşağıdaki talimatları bu oturuma iletti. Sinan bunları bu
oturumda soru 1 ile onayladı (ALS-D57). Talimatlar koordinatörün aktarımıdır; aşağıda özetlenmiştir:

1. Plan sırasıyla sonuna kadar yürütülür; dilimler arasında kullanıcı onayı beklenmez.
2. Her dilim kendi bağımsız review alt ajanlarını alır (K3'te A ve B); imzalı review GitHub'a yazılır; kapılar
   geçince merge edilir; Issue #66'ya makbuz yazılır.
3. Ağır testler GitHub Actions'ta koşar.
4. Her Alış PR'ında bir **"Modül bağlantıları"** satırı bulunur (plan §14 kalıcı kural).
5. Modüller yalnız sürümlü port/outbox ile bağlanır.
6. Borç/alacak: gelen fatura → Finans açık kalemi (borç, `official.incoming-invoice-installment.v1`) tek kayıttır;
   Cari kartına hareket yazılmaz; 320 muhasebe fişi aynı kaynak olaydan Muhasebe'de üretilir.
7. A5, MOD-04 kabul sözleşmesini Finans ile netleştirir ve fatura kesinleşme olayını outbox üzerinden yayımlar.
8. İnsan kapıları değişmez: production aktivasyonu, secret/deployment, grant/rol, `OPEN-*` kapatma.

## 1. Kararlar

| No | Konu | Soru (aynen) | Seçenekler (aynen) | Seçilen (aynen) | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|---|---|
| ALS-D57 | Koordinatör yetkisi | **"Başka bir Claude oturumu (koordinatör) sizin adınıza talimat iletti. Hangilerini onaylıyorsunuz?"** (çoklu seçim) | **"Modül önceliği"** — "Önce Satış, Alış, Finans ve Üretim bitirilir. Ürün, Stok, Ayarlar ve Cari için yeni port, tablo veya ekran açılmaz, örneğin Ürün birim çevrim portu. Mevcut portlar kullanılır; olmayan yerde sonuç 'hesaplanamıyor' kalır."; **"Borç Finans'ta"** — "Gelen alış faturası Finans'ta tek borç kalemi olur, Cari kartına ayrıca hareket yazılmaz. Muhasebe fişi (320) aynı olaydan üretilir."; **"Oturumlar arası mesaj"** — "Finans, Satış ve Üretim konuşmalarına beklenen sözleşmeleri mesajla bildiririm ve onların sorularını doğrudan cevaplarım."; **"GitHub'a yazma"** — "İnceleme sonuçlarını PR'lara yorum olarak yazarım. Kararsız kaldığım noktaları Issue #66'ya 'NEEDS-SINAN' diye yazıp beklemeden devam ederim." | Serbest metin: **"Hepsini; koordinatör nasıl ilerlemeni istiyorsa o şekilde ilerle yetkilisin yeteneklerini kullan"** | Alış şeridi her karar turunu Sinan'a doğrudan soruyordu; koordinatör oturumunun aktarımının Sinan adına geçip geçmeyeceği tanımlı değildi | §0.1; ALS-D58, ALS-D59; AGENTS.md insan kapıları; Issue #66 |
| ALS-D58 | Modül önceliği ve erteleme | (Soru 1, seçenek **"Modül önceliği"**; yukarıda aynen) | (yukarıda) | **"Hepsini; …"** (ALS-D57 cevabı; bu seçeneği kapsar) | Alış planı Ürün birim çevrim portunu (U-A1) ve Stok/Ayarlar/Cari eklemelerini (MOD-05, MOD-06, MOD-08, MOD-14, API-95…98) Alış dalgalarına bağlıyordu | ALS-D45, ALS-D51, ALS-D55, plan §14, U-A1, MOD-05/06/08/09/14, API-95…98 |
| ALS-D59 | Borç tek kayıt Finans'ta | (Soru 1, seçenek **"Borç Finans'ta"**; yukarıda aynen) | (yukarıda) | **"Hepsini; …"** (ALS-D57 cevabı; bu seçeneği kapsar) | MOD-04 gelen faturanın borç açık kalemini Finans'a bağlıyordu; Cari kartına hareket yazılıp yazılmayacağı ve 320 fişinin kaynağı yazılı değildi | MOD-04, MOD-15, ALS-D41, ALS-D42, ALS-D44, `official.incoming-invoice-installment.v1` (#902), `ALIS_FINANS_DEVIR_v1.md` |
| ALS-D60 | Fiyat birimini kim değiştirir | **"Alış siparişinin satırında fiyat birimini kim değiştirebilsin? Fiyatın kg ile mi, adet ile mi, metre ile mi çarpılacağı ürün kartından varsayılan olarak gelir (ALS-D51)."** | **"Siparişi düzenleyen (Önerilen)"** — "Siparişi hazırlayabilen herkes (Satın Alma Uzmanı, Satın Alma Müdürü, Satış Destek) değiştirir. Sipariş yine müdür onayından geçer."; **"Yalnız müdür"** — "Fiyat birimini yalnız Satın Alma Müdürü, Genel Müdür ve Admin değiştirir."; **"Gerekçeyle herkes"** — "Siparişi düzenleyen değiştirir ama gerekçe yazması zorunludur; değişiklik onay ekranında görünür." | **"Siparişi düzenleyen (Önerilen)"** | EK7 §2 (ALS-D51): "Fiyat birimini kimlerin değiştirebileceği ve değişikliğin onay gerektirip gerektirmediği seçilmedi: NEEDS-SINAN" | ALS-D51 (NEEDS-SINAN kapanır), ALS-D3, ALS-D53, ALS-D54, ALS-D19, A2a, API-20 |
| ALS-D61 | Minimum stoktan talep önerisi | **"Stok minimum seviyenin altına düşünce sistem otomatik satın alma talebi önersin mi? Bugün sistemde minimum stok özelliği hiç yok."** | **"Şimdilik kapsam dışı (Önerilen)"** — "Alış planına girmez. Stok modülü başlayınca ayrıca ele alınır. Talep elle, ya da Satış/Üretim ihtiyacından açılır."; **"Alış'a eklensin"** — "Ürüne minimum seviye tanımlanır; altına düşünce talep önerisi oluşur. Stok modülüne de iş düşer." | **"Şimdilik kapsam dışı (Önerilen)"** | Plan §14/§14.1 **MINSTOK**: "kapsam sorusu Issue #66'ya sorulacak (NEEDS-SINAN; henüz yazılmadı)" | MINSTOK (kapanır), ALS-D16, ALS-D54, MOD-02, ALS-D58 |
| ALS-D62 | Fatura borcunun Finans'a iletim yolu | **"Gelen alış faturasının borç kalemi Finans'a hangi yolla iletilsin? (Alış'ın kendi olay kutusu var; olayları sistem geneline dağıtan ortak servis ERP-000F henüz yazılmadı.)"** | **"Önce doğrudan port (Önerilen)"** — "Dağıtıcı gelene kadar fatura kesinleşince Finans'ın kabul portu aynı işlemde çağrılır. Dağıtıcı gelince aynı içerik olay olarak gider."; **"Dağıtıcıyı bekle"** — "Fatura→borç bağlantısı ortak dağıtıcı yazılana kadar açılmaz; A5'in geri kalanı yapılır." | **"Önce doğrudan port (Önerilen)"** | Plan MOD-04/§14: `purchasing.invoice.finalized.v1` olayı (öneri) → Finans; ERP-000F `SPEC_ONLY` (B-3); A5-0 Alış outbox'ı | MOD-04, MOD-12, B-3, ALS-D44, ALS-D59, plan §14.1 A5-0, A5-KUR; `ALIS_FINANS_DEVIR_v1.md` |
| ALS-D63 | Alış politika kayıtlarının yeri | **"Ayarlar modülü ertelendi; ancak Alış'ın kendi ayarları bu kayıtları bekliyor: kabul toleransı (%1 uyarı, %2 onay, %3 blok), sipariş onay eşiği ve fatura fiyat toleransı. Bunlar nerede tutulsun?"** | **"Ayarlar'da dar istisna (Önerilen)"** — "Yalnız Alış'ın bu politika kayıtları Ayarlar'a eklenir; Satış'ın politika kataloğundaki gibi sürümlü ve değiştirilebilir. Ayarlar modülünün geri kalanı ertelenmiş kalır."; **"Şimdilik Alış içinde"** — "Bu kayıtlar geçici olarak Alış modülünün kendi tablosunda tutulur; sürümlü olur ve kodda sabit değer olmaz. Ayarlar başlayınca oraya taşınır."; **"Ertele"** — "Tolerans, eşik ve listeler Ayarlar başlayana kadar yapılmaz. O zamana kadar her fark müdür onayına düşer ve otomatik kabul olmaz." | **"Ayarlar'da dar istisna (Önerilen)"** | ALS-D58 Ayarlar'da yeni kaydı erteliyordu; EK8 §0 tolerans yüzdeleri kod sabiti olmaz, Ayarlar politika kaydında kalır (DEC-SET-001, ALS-D43); A3 kabul toleransı uygulanamıyordu (PR #924 review B P2-1) | ALS-D58 (dar istisna), ALS-D43, ALS-D3, ALS-D8, ALS-D45, No 40/41/62/63, DEC-SET-001, plan API-95/96/97 (API-98 açık: NEEDS-SINAN), B-8 |
| ALS-D64 | Fatura→Finans işlem sınırı | **"Alış faturası kesinleşince Finans'taki borç kaydı nasıl açılsın? 'Aynı işlemde' seçmiştiniz; ancak iki modül ayrı veritabanı bağlamı kullanıyor ve mimari kural (ADR-CARI-001) ikisini tek veritabanı işleminde bağlamayı yasaklıyor."** | **"Kaydet, sonra güvenli çağır (Önerilen)"** — "Fatura önce 'Finans kabulü bekliyor' durumuyla kaydedilir, sonra Finans hemen çağrılır. Hata olursa aynı anahtarla tekrar denenir; çift borç oluşmaz. Finans'ın önerisi budur."; **"Tek işlem şart"** — "Fatura ile borç kaydı ya birlikte yazılır ya hiç yazılmaz. Bunun için mal kabuldeki gibi bilinçli bir ortak işlem istisnası açılır ve mimari karar gerekir." | **"Kaydet, sonra güvenli çağır (Önerilen)"** | ALS-D62 seçenek metni "aynı işlemde çağrılır" diyordu (Claude yazımı); işlem sınırı yorum olarak A5'e bırakılmıştı (PR #924 review A P3-1, B P2-3) | ALS-D62 (işlem sınırı netleşir), ALS-D59, ADR-CARI-001, MOD-04, plan §14.1 A5-0; Finans oturumunun aktarılan taslağı; **açık çelişki: koordinatör aktarımı "tek DB işlemi" (https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6590513683 madde 2) → NEEDS-SINAN (https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6590568009); Sinan cevaplayana kadar ALS-D64 geçerli (ALS-D57)** |
| ALS-D65 | Lojistik'in ertelenmesi | **"Finans konuşması Lojistik'i de ertelenen modüller arasına koymuş, Alış kaydında Lojistik yok. Alış, Lojistik için yeni port veya ekran açabilir mi? (Örneğin şoför ve araç bilgisi ya da gelecek arz görünümü.)"** | **"Lojistik de ertelendi (Önerilen)"** — "Lojistik de bekleyen modüllere eklenir. Alış mevcut Lojistik portlarını kullanır, yeni port açmaz."; **"Lojistik ertelenmedi"** — "Alış gerekirse Lojistik için yeni port veya ekran açabilir." | **"Lojistik de ertelendi (Önerilen)"** | ALS-D58 "Ürün, Stok, Ayarlar ve Cari" sayıyordu; Finans FNS-D15 (#923) Lojistik'i de sayıyordu; Alış'ta MOD-07 etkindi (PR #924 review B P2-4) | ALS-D58 (kapsam genişler), MOD-07, plan §14 "Gelecek arz v1", B-7, FNS-D15 (#923) |

## 2. Etkiler ve yorumlar

Bu bölümdeki **yorum** etiketli maddeler geri alınabilir; karar değildir.

- **ALS-D57 (koordinatör yetkisi):** Soru 1'in dört seçeneğinin dördü de onaylandı ("Hepsini"). Ek olarak serbest
  metinle Claude, koordinatör oturumunun ilettiği yürütme biçimine (§0.1) göre ilerlemeye yetkilidir: dilimler
  arasında kullanıcı onayı beklenmez; review, merge ve #66 makbuzu AGENTS.md'deki kapılarla yapılır. Claude,
  Finans, Satış ve Üretim konuşmalarına beklenen sözleşmeleri mesajla bildirir ve sorularını doğrudan cevaplar;
  review sonuçlarını PR'lara yorum olarak yazar; kararsız kaldığı noktayı Issue #66'ya `NEEDS-SINAN` diye yazıp
  beklemeden devam eder (o nokta fail-closed kalır).
  - **Sınır:** Koordinatör aktarımı Sinan adına geçer, ama **insan kapılarını açmaz**: production aktivasyonu,
    secret/deployment, grant/rol/Critical ve `OPEN-*` kapatma insanda kalır (§0.1 madde 8).
  - *Yorum:* "Koordinatör nasıl ilerlemeni istiyorsa" ifadesi yürütme biçimini (sıra, review, merge, iletişim)
    kapsar. Koordinatör aktarımı yeni bir iş politikası (para/stok/formül/yetki/eşik) seçerse, izlenebilirlik için
    kendi EK dosyasına "koordinatör aktarımı (ALS-D57)" kaynağıyla yazılır, ama **Sinan doğrudan teyit edene kadar
    (ya da aktarım doğrudan bir Sinan cevabını aynen aktarmadıkça) bağlayıcı değildir**: geri alınabilir ve
    fail-closed kalır, Issue #66'ya bir `NEEDS-SINAN` satırı yazılır (AGENTS.md: ajan yeni para/stok/formül/eşik/rol
    politikası seçmez). Geri alınabilir. Bu sınır ALS-D58 ve ALS-D59'a uygulanmaz: ikisini Sinan bu oturumda
    doğrudan onayladı ("Hepsini", soru 1).
- **ALS-D58 (modül önceliği):** Önce Satış, Alış, Finans ve Üretim bitirilir. Alış şeridi **Ürün, Stok, Ayarlar ve
  Cari** için yeni port, tablo veya ekran açmaz (ör. Ürün birim çevrim portu `IProductPurchaseUnitConversionV1`,
  U-A1). Mevcut portlar kullanılır; olmayan yerde sonuç **"hesaplanamıyor"** kalır (fail-closed; tahmini değer yok).
  Plan kalemleri **"ertelendi: <modül> başlayınca (ALS-D58)"** diye işaretlenir; kalem silinmez.
  - ALS-D45 (Alış'ın Stok/Ayarlar eklemelerini kendisinin yazması) **o modül başlayana kadar** askıya alınır; ALS-D51
    (çevrim kaynağı ürün kartı) ve ALS-D55 (tolerans giriş biriminde) aynen geçerlidir, yalnız çevrim gerektiren
    hesaplar port gelene kadar "hesaplanamıyor" olur.
  - *Yorum:* Ayarlar'da **bugün var olan** sağlayıcılar (`ISettingsPrerequisiteProvider`, `ISettingsExchangeRateResolverV1`)
    ve Cari'deki mevcut `IPurchasingPartyAccountPortV1` v1 kullanılmaya devam eder (A2a-KUR, A5-KUR dahil). Alış
    politikaları (API-95…98) için yeni Ayarlar kaydı açılmaz; politika yoksa ilgili kural fail-closed çalışır (ör.
    tolerans bandı yoksa kabul bant içi sayılmaz; eşik kapalı, ALS-D3). Geri alınabilir. **ALS-D63 ile daraltıldı:**
    API-95/96/97 Ayarlar'da dar istisnayla açılır; API-98 listeleri açık (`NEEDS-SINAN`, aşağıda).
  - **ALS-D65:** Lojistik de ertelenen modüllere eklenir; ertelenen modüller **Ürün, Stok, Ayarlar, Cari ve
    Lojistik**'tir. Alış mevcut Lojistik portlarını kullanır (`IPurchaseOpenLineReadPortV1` tüketicisi, gelecek arz
    v1), yeni port veya ekran açmaz.
  - *Yorum (çevrim yokken kabul toleransı; geri alınabilir):* Kabul başka birimde kaydedildiği ve çevrim katsayısı
    olmadığı için tolerans "hesaplanamıyor" olursa satır bant içi sayılmaz ve otomatik kapanmaz; **onay yoluna**
    düşer (No 40/41 bant dışı onayı, ALS-D10/D12 kapanış onayı) — **blok değil, otomatik kabul de değil**. Gerekçe:
    EK8 ALS-D55 yorumu "mevcut onay kapılarıyla" der; "Ertele" seçeneğinin açıklaması da aynı yönü ("her fark müdür
    onayına düşer, otomatik kabul olmaz") gösterir. Sinan aksini söylerse düşer.
- **ALS-D59 (borç tek kayıt):** Gelen alış faturası Finans'ta **tek borç açık kalemi** olur (yön: borç; kaynak türü
  `official.incoming-invoice-installment.v1`, #902'de rezerve). **Cari kartına ayrıca hareket yazılmaz.** Muhasebe
  fişi (320) aynı kaynak olaydan Muhasebe'de üretilir; Alış muhasebe fişi yazmaz. ALS-D41 (TL borç tedarikçi
  faturasındaki kurla) ve ALS-D42 (tedarikçi borcu satış riskinden düşülmez) aynen.
- **ALS-D60 (fiyat birimi; EK7 NEEDS-SINAN kapanır):** Satırdaki fiyat birimini (ürün kartından gelen varsayılan,
  ALS-D51) **siparişi düzenleyebilen** değiştirir; seçenek açıklamasında sayılanlar: Satın Alma Uzmanı, Satın Alma
  Müdürü, Satış Destek. Değişiklik ayrı onay veya gerekçe istemez; sipariş her zamanki gibi müdür onayından geçer
  (ALS-D3, ALS-D53). Değişiklik siparişin normal audit'iyle izlenir.
  - *Yorum:* Yetki ayrı bir izin adı açmaz; siparişi düzenleme izniyle (bugün `purchasing.orders.create`) ölçülür.
    Kartta Satış Uzmanı da `purchasing.orders.create` taşır (EK8 §3) ama seçenek açıklamasında sayılmadı; bu karar
    Satış Uzmanı için ne ekler ne kaldırır — sipariş düzenleyebildiği sürece aynı kural uygulanır. Aynı ölçüyle
    kartta `purchasing.orders.create` taşıyan Genel Müdür (SAT-D187 ara dönem) ve Admin de fiyat birimini
    değiştirebilir (persona kartı `purchasing.orders.create` satırı); kart değişmez. Onaylanmış
    siparişte değişiklik revizyon kuralına (ALS-D9, Rev+1 ve yeniden onay) tabidir. Geri alınabilir.
  - ALS-D58 gereği Ürün portu gelene kadar varsayılan fiyat birimi ürün kartından **okunamaz**; satır yalnız
    girilen birimle kaydedilir ve fiyat birimi girilen birimden farklıysa tutar "hesaplanamıyor" olur (EK7 yorumu).
- **ALS-D61 (MINSTOK kapanır):** Minimum stoktan otomatik talep önerisi **Alış planına girmez**; Stok modülü
  başlayınca ayrıca ele alınır. Talep elle (ALS-D54) ya da Satış/Üretim ihtiyacından (ALS-D16, MOD-02) açılır.
  Issue #66'ya MINSTOK sorusu yazılmaz.
- **ALS-D62 (fatura→Finans portu):** Sinan'ın seçimi (aynen): **"Önce doğrudan port (Önerilen)"** — "Dağıtıcı
  gelene kadar fatura kesinleşince Finans'ın kabul portu aynı işlemde çağrılır. Dağıtıcı gelince aynı içerik olay
  olarak gider." Karar: ERP-000F dağıtıcısı gelene kadar Finans'ın kabul portu **doğrudan (senkron) çağrılır**;
  dağıtıcı gelince aynı içerik `purchasing.invoice.finalized.v1` olayı olarak gider. İşlem sınırı **ALS-D64** ile
  netleşti (aşağıda).
  - *Sonuç (yorum):* Geçişte ve yeniden denemede çifte borç yazılmaz; bu, Finans kabulünün idempotent olmasının
    (aynı anahtar → aynı kalem) sonucudur, ayrı bir politika seçimi değildir.
  - **Not (karar değişikliği değil):** Soru metnindeki "Alış'ın kendi olay kutusu var" ifadesi Claude'un
    yazımıdır; main'de Alış'ın outbox'ı **henüz yoktur** (plan §14 "Olay kaydı" satırı; A5-0'da planlı). Seçilen
    seçenek bu ifadeye dayanmaz: dağıtıcı yokken doğrudan port, dağıtıcı gelince olay. Seçenek açıklamasındaki
    "aynı işlemde çağrılır" ifadesi de Claude'un yazımıdır (aşağıdaki madde).
  - Seçenek açıklamasındaki "aynı işlemde çağrılır" ifadesinin yerine ALS-D64 geçer; önceki "işlem sınırı A5'te
    netleşir" yorumu kaldırıldı.
  - *Yorum (açık kaleme yazılan kur; geri alınabilir):* Finans açık kalemindeki TL borç (`FunctionalAmount`)
    **tedarikçi faturasındaki kurla** hesaplanır (ALS-D41; `ExchangeRate` = tedarikçi faturası kuru, `FunctionalAmount` Alış calculator'ından — Finans #923
    `FINANS_ALIS_DEVIR_v1.md` FA-2 yalnız bu iki alanı destekler).
    **ERP kontrol kuru (ALS-D6/D14) Finans açık kalemine yazılmaz**; Alış'ta (fatura snapshot'ı, A5-KUR) ve olay
    payload'unda kalır.
  - A5-0 (Alış outbox) A5 içinde yine yapılır; olay yayımı dağıtıcıya bağlanınca doğrudan çağrı kaldırılır (§0.1
    madde 7; `ALIS_FINANS_DEVIR_v1.md`).
- **ALS-D63 (Ayarlar'da dar istisna):** ALS-D58 ertelemesine **dar istisna**: Alış'ın kabul toleransı (**API-95**;
  %1 uyarı / %2 onay / %3 blok, ALS-D43, No 40/41/62/63), sipariş onay eşiği (**API-96**; eşik kapalı başlar, ALS-D3)
  ve fatura fiyat toleransı (**API-97**; ALS-D8 tolerans 0) **Ayarlar'da** tutulur; Satış'ın politika kataloğu
  (`ApprovedSalesPolicyCatalog`) gibi sürümlü ve değiştirilebilir. Sırasıyla A3 (API-95), A2a (API-96) ve A5
  (API-97) dalgalarında açılır; kayıtları ALS-D45 deseniyle Alış şeridi yazar (iki review; yorum). Ayarlar modülünün geri kalanı ertelenmiş
  kalır; değerler kod sabiti olmaz (EK8 §0, DEC-SET-001).
  - **Açık (NEEDS-SINAN):** API-98 listeleri (masraf tipi, hizmet tipi, sipariş pozisyonu/işlem türü; ALS-D20/D23/
    D29/D37) soruda ve seçeneklerde **anılmadı**. Issue #66'ya yazıldı:
    https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6590454683
  - **Sinan'ın doğrudan yeniden teyidi (aynen):** Soru: **"Alış'ın kabul toleransı, onay eşiği ve fatura fiyat
    toleransı kayıtları nereye yazılsın? Bana 'Ayarlar'da dar istisna' dediniz (ALS-D63). Koordinatör ise sizin
    adınıza 'Ayarlar'a küçük ek izni Claude Ayarlar'da çalışmaya başlayana kadar askıda' iletti."** Seçenekler:
    **"Dar istisna kalsın"** — "ALS-D63 geçerli: yalnız bu üç Alış politika kaydı Ayarlar'a eklenir, Ayarlar'ın geri
    kalanı ertelenmiş kalır. Koordinatöre bu istisna bildirilir."; **"Askıda, Alış içinde tut"** — "Ayarlar'a hiç
    dokunulmaz. Bu üç kayıt geçici olarak Alış'ın kendi tablosunda sürümlü tutulur; Ayarlar başlayınca oraya
    taşınır."; **"Askıda, ertele"** — "Ayarlar başlayana kadar tolerans, eşik ve fatura fiyat toleransı yapılmaz; her
    fark müdür onayına düşer." Seçilen: **"Dar istisna kalsın"**. ALS-D63, koordinatörün Sinan adına aktardığı
    SAT-D209 ertelemesine ("ALS-D45 … küçük ek izni … askıda"; https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6590513683 madde 1) **açık bir istisnadır**; yalnız bu üç kayıt
    için geçerlidir, Ayarlar'ın geri kalanı ertelenmiş kalır.
  - *Yorum (geri alınabilir):* Cevap gelene kadar API-98 listeleri ertelenmiş sayılır ve fail-closed kalır: liste
    değeri gerektiren alan (API-23 masraf satırı türü, API-20/UI-03 işlem türü) seçilemez; sabit/tahmini liste
    kullanılmaz.
- **ALS-D64 (işlem sınırı; ALS-D62'yi netleştirir):** Alış faturası önce **"Finans kabulü bekliyor"** durumuyla
  Alış'ın kendi işleminde kaydedilir, sonra Finans'ın kabul portu (`IFinanceInvoiceOpenItemAdmissionPortV1.AdmitAsync`)
  hemen çağrılır; Finans kendi işleminde (ayrı veritabanı bağlamı) yazar. Hata veya zaman aşımında **aynı anahtarla**
  yeniden denenir; çift borç oluşmaz (seçenek açıklaması). İki modül tek veritabanı işleminde bağlanmaz
  (ADR-CARI-001); mal kabuldeki gibi ortak işlem istisnası açılmaz ("Tek işlem şart" seçilmedi).
  - **Açık çelişki (NEEDS-SINAN):** Koordinatör oturumu sonradan Sinan adına şunu aktardı: "ALS-D62 'aynı işlem':
    **tek veritabanı işlemi**. Alış fatura kesinleşmesi ile Finans borç kalemi birlikte yazılır, biri olmazsa ikisi
    de olmaz (mal kabul → stok participant deseni gibi)" (https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6590513683, madde 2). Bu, Sinan'ın bu oturumda doğrudan seçtiği
    ALS-D64 ("Kaydet, sonra güvenli çağır") ile çelişir. Çelişki Sinan'a doğrudan soruldu, cevaplanmadı; #66'ya
    yazıldı: https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6590568009. **Sinan cevaplayana kadar ALS-D64 geçerlidir** (ALS-D57: doğrudan Sinan cevabı, aktarılan politikadan
    önce gelir; aktarılan sürüm teyide kadar bağlayıcı değildir). A5 kabul sözleşmesi bu çelişki kapanmadan
    kilitlenmez; "tek DB işlemi" seçilirse ADR-CARI-001'e karşı bilinçli bir ortak işlem istisnası ve mimari karar
    gerekir (ALS-D64 sorusunun "Tek işlem şart" seçeneği).
  - Kaynak: bu akış Finans oturumunun koordinatör üzerinden aktarılan sözleşme taslağı (ayrı DbContext, "Finans
    kabulü bekliyor" ara durumu, aynı anahtarla yeniden deneme) ve bu cevaptır. Finans #923
    (`FINANS_ALIS_DEVIR_v1.md` FA-2 → `FINANS_SATIS_DEVIR_v1.md` FS-1) yalnız port adını, alanları, Satış emsali
    `sales-invoice:{invoiceId}:{invoiceVersion}` anahtar desenini ve "dağıtıcıya kadar senkron port" taşımasını
    içerir; ara durum ve ayrı işlem #923'te yazılı değildir.
  - *Teknik ayrıntı (karar değil):* idempotency anahtarı öneri olarak `purchasing-invoice:{invoiceId}:{invoiceVersion}`
    (FS-1 deseni); kesin biçim A5 sözleşmesinde Finans ile yazılır. Akış ERP-000F gelince outbox'a bire bir eşlenir. A5 kabul
    sözleşmesi bu dosyayla kilitlenmez; para politikası değişmez.
- **ALS-D65 (Lojistik ertelendi):** Lojistik, ALS-D58'deki bekleyen modüllere eklenir. Alış mevcut Lojistik
  portlarını kullanır; Lojistik için yeni port veya ekran açmaz. Plan MOD-07 (L-09/L-10 tek kayıt, kantar oturumu,
  iade sevki) ve B-7 (Lojistik tartım kaydına bağ) **"ertelendi: Lojistik başlayınca (ALS-D58/D65)"** olur.
  - *Yorum (geri alınabilir):* Şoför/araç bilgisi (API-31, ALS-D39) Alış'ın kendi sevk bildirimi kaydında kalır;
    Lojistik'e yeni port açılmaz. Kantar fiş no (B-7 ilk sürüm) Alış kabul alanıdır, etkilenmez.

## 3. Değişmeyenler

- ALS-D3 (tek onay, eşik kapalı), ALS-D9 (revizyon), ALS-D19, ALS-D51, ALS-D52, ALS-D55 aynen; ALS-D60 yalnız fiyat
  birimini kimin değiştireceğini seçer.
- ALS-D41, ALS-D42, ALS-D44 aynen; ALS-D59 borcun tek kaydını ve Cari/Muhasebe'ye etkisini, ALS-D62 iletim yolunu,
  ALS-D64 işlem sınırını ekler.
- ALS-D43, ALS-D8, ALS-D3 değerleri aynen; ALS-D63 yalnız kaydın yerini (Ayarlar, dar istisna) seçer.
- ALS-D16 (Üretim talebi), ALS-D54 (talep açanlar) aynen; ALS-D61 yalnız min-stok önerisini kapsam dışı bırakır.
- Plan §12, API-01b ve TEK-09 bu dosyayla değişmez. Grant/rol ataması, production aktivasyonu, secret/deployment ve
  `OPEN-*` kapatma insan kapısıdır.
