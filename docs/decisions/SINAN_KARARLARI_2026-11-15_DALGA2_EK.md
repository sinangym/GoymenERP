# Sinan Kararları — 2026-11-15 Dalga 2 ek (artık etiketi mirası, yeni izinlerin personaları, başlangıç politika aktarımı, sipariş limit uyarısı)

Bu belge önceki karar dosyalarını silmez; `KLN-D57`, `KLN-D58`, `URN-D12`, `SAT-D355`–`SAT-D357` kararlarını ekler.
`SINAN_KARARLARI_2026-11-15_DALGA2_SORULARI.md` (`KLN-D55`, `SAT-D353`, `SAT-D354`, `FNS-D54`–`FNS-D56`, `KLN-D56`,
`ALS-D98`, `URN-D10`, `URN-D11`), `SINAN_KARARLARI_2026-11-15_DALGA1_EK.md` (`URT-D66` dahil) ve
`docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md`'deki önceki persona kararları (`SAT-D69`, `SAT-D145`, `SAT-D187`,
`SAT-D191`) aynen geçerlidir. Bu belge `SINAN_KARARLARI_2026-11-15_DALGA2_SORULARI.md` "Sınırlar" bölümündeki açık
maddeleri cevaplar.

**Kaynak:** Sinan, 2026-11-15, Claude şeridi oturumunda soru-cevap aracıyla, iki turda. Sorular dalga 2 karar
belgesinin açık maddelerinden ve satış takımının dalga 2 raporundaki NEEDS-SINAN maddelerinden (SAT-S4,
ASSUMPTION-SAT-RISK-1…3) gelir. "(Önerilen)" işaretleri soranındır. Ölçü/dosya sorusunda Sinan serbest metin yazdı;
kelimesi kelimesine: **"Ölçü müdürü kavramı eğer ürün stok tarafı ile ilgili bir müdür varsa o olsun, çok müdürlük
açmayalım, yada ayrı açalım ama tek bir kişiye bunuda verebilelim, dosya herkes"**.

`URT-D66` termin izni ayrıca sorulmadı: `URT-D66` (5) "ayrı termin izni yoktur" der; plan termini plan düzenleme,
emir termini emir düzenleme izniyle değişir. Önceki belgenin "Sınırlar" listesindeki "URT-D66 termin izni" maddesi bu
nedenle açık değildir.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Kullanılabilir artık (BS-5) etiketi taşıyan bir parti bölünür veya birleştirilirse yeni partiler bu etiketi miras alsın mı? | Miras alır (Önerilen) | Bölünen partinin çocukları etiketi alır. Birleştirmede sonuç ancak bütün kaynaklar artıksa artık sayılır, yoksa etiketsiz kalır. Seçilmeyenler: "Hiç miras yok" (bölme/birleştirme sonrası etiket düşer; gerekirse kullanıcı yeniden işaretler); "Bölmede var, birleştirmede yok" (bölünen çocuklar etiketi alır; birleştirilen parti her zaman etiketsiz başlar). |
| 2 | Satış teslim toleransı (`settings.sales-delivery-tolerances.manage`) ve satış onay eşikleri (`settings.sales-approval-thresholds.manage`) ayarlarını kimler değiştirebilsin? (Persona eşlemesi; gerçek grant'ı Sinan uygular.) | Yönetim Kurulu + Admin (Önerilen) | İskonto limitleriyle (SAT-D69) aynı: satış ekibi kendi kontrol eşiğini gevşetemez. Seçilmeyenler: "Satış Müdürü + Admin" (vergi/ürün tipi ayarlarındaki gibi, SAT-D145, müdür seviyesi yönetir); "Eşik YK, tolerans Müdür" (onay eşikleri Yönetim Kurulu + Admin; teslim toleransı Satış Müdürü + Admin). |
| 3 | Ürün ölçü tanımları (`product.dimension.manage`) ve belgeye dosya bağlama (`documents.files.link`) izinleri kimlere verilsin? | Serbest metin (yukarıda kelimesi kelimesine) | Seçenekler: "Ölçü Müdür+Admin, dosya herkes (Önerilen)" (ölçü tanımı ürün tipi gibi, SAT-D145, Müdür + Admin; dosya bağlama bütün iş personalarına, KLN-D56 zaten kaynağı ve hedefi okuyabilmeyi şart koşar); "İkisi de Müdür + Admin"; "Ölçü Admin, dosya herkes". Sinan'ın cevabı: ölçü izni ürün/stok tarafındaki mevcut müdüre; yeni müdür personası açılmaz; izin ayrı ad olarak kalır ve tek bir kişiye verilebilir; dosya bağlama herkese. |
| 4 | Üretimde derlenmiş başlangıç satış politikalarının (`CompiledBaselineV0`) kataloğa aktarımı şirket başına hangi sırayla ve kim tarafından yapılsın? (Aktivasyon her durumda insan adımıdır.) | Önce bir şirket, sonra diğerleri; Sinan (Önerilen) | Bir şirkette aktarılıp kontrol edilir, sonra diğer şirketler; her aktarımı Sinan başlatır. Seçilmeyenler: "Hepsi birlikte; Sinan" (bütün şirketler tek seferde); "Şirket başına Admin" (her şirketin Admin'i kendi şirketi için başlatır; sıra serbest). |
| 5 | Siparişte kredi limiti aşımı uyarısı kaydediliyor (siparişi durdurmaz). Bu uyarı kayıtları ne kadar saklansın ve içinde tutar yazsın mı? | Genel audit süresi, tutarsız (Önerilen) | Diğer audit kayıtlarıyla aynı süre saklanır; kayıtta yalnız uyarı türü ve sipariş sürümü olur, tutar olmaz (tutar finans kaynağından okunur). Seçilmeyenler: "Genel audit süresi, tutarlı" (o anki limit ve risk tutarı da saklanır); "Sipariş kapanınca silinir" (ayrı imha politikası gerekir). |
| 6 | Kredi limiti uyarısı hangi anlarda kaydedilsin? | Gönderme ve onayda (Önerilen) | Sipariş onaya gönderilirken ve onaylanırken kaydedilir; taslak düzenlemede yalnız ekranda görünür, kayıt oluşmaz. Seçilmeyenler: "Ayrıca sevkte"; "Her taslak değişikliğinde" (çok kayıt üretir). |
| 7 | Kaydedilen limit uyarılarını kimler görebilsin? | Siparişi gören herkes (Önerilen) | Mevcut sipariş okuma izni (`sales.drafts.read`) yeterli; yeni izin açılmaz. Seçilmeyen: "Ayrı risk okuma izni" (yeni izin, kimlere verileceği ayrıca kararlaştırılır). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KLN-D57 | Kullanılabilir artık (BS-5) etiketinin mirası | Etiketli parti **bölünürse** bütün çocuk partiler etiketi miras alır. Partiler **birleştirilirse** sonuç yalnız bütün kaynak partiler etiketliyse etiketli olur; tek bir kaynak etiketsizse sonuç etiketsizdir. Miras, bölme/birleştirme hareketiyle aynı kayıtta yazılır; ayrı hareket üretilmez. | Stok etiket bölme/birleştirme (BS-5) |
| SAT-D355 | Satış teslim toleransı ve onay eşiği ayarlarının personası | `settings.sales-delivery-tolerances.manage` ve `settings.sales-approval-thresholds.manage` **Yönetim Kurulu ve Admin** personalarına eşlenir (SAT-D69 iskonto limitleri emsali); hiçbir satış personası ve müdür personası bu izinleri almaz. Bu bir persona eşlemesidir; grant'ı Sinan uygular. | Persona grant matrisi, Satış ayarları |
| URN-D12 | Ürün ölçü tanımı izninin personası | `product.dimension.manage` ayrı izin adı olarak kalır ve ürün/stok tarafındaki **mevcut Stok Müdürü personasına** ve Admin'e eşlenir; yeni müdür personası açılmaz. Ara dönemde Stok Müdürü personasını taşıyan kişi SAT-D187'deki gibidir. İzin ayrı ad olduğundan tek bir kişiye de verilebilir. Bu bir persona eşlemesidir; grant'ı Sinan uygular. | Persona grant matrisi, Ürün ölçü paneli (URN-D10) |
| KLN-D58 | Belgeye dosya bağlama izninin personası | `documents.files.link` **bütün iş personalarına** eşlenir. Erişim sınırı KLN-D56'dır: bağlama için kaynak dosya en az bir bağlantısından ve hedef kayıt görülebilmelidir; izin bu kontrolü kaldırmaz. Bu bir persona eşlemesidir; grant'ı Sinan uygular. | Persona grant matrisi, Belgeler PLT-S16 |
| SAT-D356 | Başlangıç satış politikalarının kataloğa aktarımı | Üretimde `CompiledBaselineV0` → katalog aktarımı **önce tek bir şirkette** yapılır ve kontrol edilir, sonra diğer şirketlerde; her şirketin aktarımını **Sinan başlatır**. Hangi şirketin ilk olacağı aktarım anında Sinan tarafından seçilir. Aktarım bir production aktivasyonudur; bu karar ajanlara aktarım yetkisi vermez. | Satış politika katalogları (F029), production aktivasyon adımları |
| SAT-D357 | Sipariş kredi limiti uyarı kaydı (SAT-S4, FNS-D27(3)) | (1) Uyarı satırları ve audit olayları **genel audit kayıtlarıyla aynı saklama politikasına** bağlıdır; o politikanın süresi bu kararla belirlenmez. Satırda **tutar saklanmaz**; yalnız uyarı türü ve sipariş sürümü saklanır. (2) Uyarı **yalnız onaya gönderme ve onay** anlarında kaydedilir; taslak oluşturma/düzenlemede yalnız ekranda gösterilir; sevkte kaydedilmez. (3) Kaydedilen uyarılar mevcut **`sales.drafts.read`** izniyle okunur; yeni izin açılmaz. ASSUMPTION-SAT-RISK-1…3 bu kararla karara bağlanmıştır. Uyarı siparişi durdurmaz (FNS-D27). | Satış SAT-S4 (`sales.DraftRiskWarnings`, risk uyarısı okuma ucu) |

## Sınırlar

- Bu belge yetki vermez. `SAT-D355`, `URN-D12` ve `KLN-D58` persona eşlemesidir; gerçek grant'ları Sinan uygular.
- Genel audit saklama süresinin kendisi bu belgeyle belirlenmez; ayrı saklama politikasıdır.
- `SAT-D356` aktarımın sırasını ve sahibini söyler; aktarımı veya production aktivasyonunu başlatmaz.
- ASSUMPTION-SAT-RISK-4 (uyarı türü eşlemesi: aşım, limit tanımlı değil, kaynak okunamadı) geri alınabilir teknik
  ayrıntı olarak kalır.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` her zaman somut kullanıcı kararı ister.
