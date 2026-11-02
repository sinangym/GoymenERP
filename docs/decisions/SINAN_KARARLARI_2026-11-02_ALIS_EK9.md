# Sinan Kararları — 2026-11-02 Alış EK9 (koordinatör yetkisi, modül önceliği, borç Finans'ta, fiyat birimi, min-stok, fatura→Finans portu)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda (Alış şeridi), başka bir Claude oturumunun
(koordinatör) onun adına ilettiği talimatlara, EK7'de açık kalan fiyat birimi sorusuna (ALS-D51 NEEDS-SINAN), plan
§14.1 **MINSTOK** kapsam sorusuna ve gelen fatura borcunun Finans'a iletim yoluna (A5, MOD-04) verdiği **doğrudan
cevaplar**. Sorular, bütün seçenekler ve seçenek açıklamaları aynen aktarılmıştır; "(Önerilen)" işareti Claude'un
soruyu sorarken koyduğu işarettir. Serbest metin cevap yazım düzeltmesi yapılmadan alınmıştır. Claude yalnız soruları
ve seçenekleri hazırlamıştır; seçimi Sinan yapmıştır.

Numaralama: ALS-D47 (talebin geri çekilmesi: oluşturan veya gönderen), paralel PR #907 ile eklenecek EK6'dadır;
ALS-D48…D52 EK7'de, ALS-D53…D56 EK8'dedir. Bu dosya ALS-D57'den başlar; EK6 bu dosyanın parçası değildir.

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
    kapsar. Koordinatör aktarımı yeni bir iş politikası (para/stok/formül/yetki) seçerse, izlenebilirlik için ayrı
    bir EK dosyasına "koordinatör aktarımı (ALS-D57)" kaynağıyla yazılır. Geri alınabilir.
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
    tolerans bandı yoksa kabul bant içi sayılmaz; eşik kapalı, ALS-D3). Geri alınabilir.
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
    Satış Uzmanı için ne ekler ne kaldırır — sipariş düzenleyebildiği sürece aynı kural uygulanır. Onaylanmış
    siparişte değişiklik revizyon kuralına (ALS-D9, Rev+1 ve yeniden onay) tabidir. Geri alınabilir.
  - ALS-D58 gereği Ürün portu gelene kadar varsayılan fiyat birimi ürün kartından **okunamaz**; satır yalnız
    girilen birimle kaydedilir ve fiyat birimi girilen birimden farklıysa tutar "hesaplanamıyor" olur (EK7 yorumu).
- **ALS-D61 (MINSTOK kapanır):** Minimum stoktan otomatik talep önerisi **Alış planına girmez**; Stok modülü
  başlayınca ayrıca ele alınır. Talep elle (ALS-D54) ya da Satış/Üretim ihtiyacından (ALS-D16, MOD-02) açılır.
  Issue #66'ya MINSTOK sorusu yazılmaz.
- **ALS-D62 (fatura→Finans portu):** Sinan'ın seçimi (aynen): **"Önce doğrudan port (Önerilen)"** — "Dağıtıcı
  gelene kadar fatura kesinleşince Finans'ın kabul portu aynı işlemde çağrılır. Dağıtıcı gelince aynı içerik olay
  olarak gider." Karar: ERP-000F dağıtıcısı gelene kadar Finans'ın kabul portu **doğrudan (senkron) çağrılır**;
  dağıtıcı gelince aynı içerik `purchasing.invoice.finalized.v1` olayı olarak gider. Geçişte çifte borç yazılmaz.
  - **Not (karar değişikliği değil):** Soru metnindeki "Alış'ın kendi olay kutusu var" ifadesi Claude'un
    yazımıdır; main'de Alış'ın outbox'ı **henüz yoktur** (plan §14 "Olay kaydı" satırı; A5-0'da planlı). Seçilen
    seçenek bu ifadeye dayanmaz: dağıtıcı yokken doğrudan port, dağıtıcı gelince olay. Seçenek açıklamasındaki
    "aynı işlemde çağrılır" ifadesi de Claude'un yazımıdır (aşağıdaki yorum).
  - *Yorum (işlem sınırı; geri alınabilir):* Kesin işlem sınırı — Alış'ın veritabanı işlemiyle **aynı işlem** mi,
    yoksa **önce Alış commit'i, sonra idempotent çağrı** mı — bir mimari ayrıntıdır ve A5'te Finans ile
    netleştirilir. Bugünkü yön Finans'ın önerisidir (Finans PR #923, `docs/handoffs/FINANS_ALIS_DEVIR_v1.md` FA-2;
    koordinatör aktarımı): Finans kendi işleminde (ayrı DbContext) yazar; Alış kesinleştirmeyi **"Finans kabulü
    bekliyor"** durumuyla commit eder, sonra `IFinanceInvoiceOpenItemAdmissionPortV1.AdmitAsync`'i idempotency
    anahtarı `purchasing-invoice:{invoiceId}:{invoiceVersion}` ile çağırır; hata/zaman aşımında **aynı anahtarla**
    yeniden dener. Bu akış ERP-000F gelince outbox'a bire bir eşlenir. Bu yorum para politikasını değiştirmez ve A5
    kabul sözleşmesini kilitlemez; sözleşme A5'te Finans ile birlikte kesinleşir.
  - *Yorum (açık kaleme yazılan kur; geri alınabilir):* Finans açık kalemindeki TL borç (`FunctionalAmount`)
    **tedarikçi faturasındaki kurla** hesaplanır (ALS-D41; `ExchangeRate` = tedarikçi faturası kuru, Finans FA-2).
    **ERP kontrol kuru (ALS-D6/D14) Finans açık kalemine yazılmaz**; Alış'ta (fatura snapshot'ı, A5-KUR) ve olay
    payload'unda kalır.
  - A5-0 (Alış outbox) A5 içinde yine yapılır; olay yayımı dağıtıcıya bağlanınca doğrudan çağrı kaldırılır (§0.1
    madde 7; `ALIS_FINANS_DEVIR_v1.md`).

## 3. Değişmeyenler

- ALS-D3 (tek onay, eşik kapalı), ALS-D9 (revizyon), ALS-D19, ALS-D51, ALS-D52, ALS-D55 aynen; ALS-D60 yalnız fiyat
  birimini kimin değiştireceğini seçer.
- ALS-D41, ALS-D42, ALS-D44 aynen; ALS-D59 borcun tek kaydını ve Cari/Muhasebe'ye etkisini, ALS-D62 iletim yolunu
  ekler.
- ALS-D16 (Üretim talebi), ALS-D54 (talep açanlar) aynen; ALS-D61 yalnız min-stok önerisini kapsam dışı bırakır.
- Plan §12, API-01b ve TEK-09 bu dosyayla değişmez. Grant/rol ataması, production aktivasyonu, secret/deployment ve
  `OPEN-*` kapatma insan kapısıdır.
