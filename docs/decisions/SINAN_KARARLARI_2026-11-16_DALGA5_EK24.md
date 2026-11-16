# Sinan Kararları — 2026-11-16 (Dalga 5 ek 24: W5 NEEDS-SINAN cevapları — banka bakiye kaynağı, alış iadesi istisnası, kantar kg dağıtımı, rezerv portu, iade tarihi, cari değişikliği, geç fatura farkı, mal kabul ve birleştirme kuru, satır iskontosu, alış fişi, kredi spec'i, numara serisi, kurucu parolası, paket standardı, yeni izinlerin Critical sınıfı, toplu kabul)

Bu belge önceki karar dosyalarını silmez. Şu kararları ekler: SAT-D408…SAT-D418, ALS-D115, FNS-D102…FNS-D107,
KIM-D52…KIM-D54 ve KLN-D112…KLN-D113. Her satır tamamladığı veya güncellediği önceki kararı adıyla söyler:

- FNS-D102 → FNS-D87 (EK21);
- SAT-D408 → SAT-D396 (EK16);
- SAT-D409 → SAT-D322 (3)-(4);
- KLN-D112 → KLN-D105 (EK15) ve SAT-S8 / STK-S17 dilim sırası;
- SAT-D410 → SAT-D371 ve SAT-D393 (EK15);
- SAT-D411 → SAT-D392 ve SAT-D393 (EK15);
- KIM-D52 → KIM-D9 (`SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md`);
- SAT-D412 → SAT-D294 ve SAT-D208 (müşteri iadesi Finans sunumu);
- ALS-D115 → ALS-D112 (EK21);
- SAT-D413 → SAT-D385 (EK15) ve SAT-D404 (EK20);
- FNS-D103 → FNS-D77 (EK15) ve FNS-D78 (EK16);
- FNS-D104 → FNS-D77 ve FNS-D78;
- SAT-D414 → SAT-D386 (EK15), SAT-D403 (EK20) ve SAT-D407 (EK22);
- FNS-D105 → KLN-D102 (EK15) alış tarafı;
- FNS-D106 → FNS-D93 ve FNS-D95 (EK22);
- FNS-D107 → FNS-D73 (EK12) ve FNS-D83 (EK20);
- SAT-D415 → SAT-D406 (1) (EK20);
- KIM-D53 → N-12 numara serisi sözleşmesi (KIM-S19);
- KIM-D54 → KIM-D22 ve KIM-D23;
- SAT-D416 → SAT-D397 (EK16) ve SAT-D401 (EK20);
- SAT-D417 → SAT-D406 (2) (EK20);
- SAT-D418 → SAT-D397 ve SAT-D401;
- KLN-D113 → aşağıdaki "Toplu kabul edilen öneriler" tablosu (TK-1…TK-44).

Bu kararların burada değinilmeyen kısımları aynen geçerlidir. Diğer önceki kararlar da aynen geçerlidir.

EK11, EK15, EK16, EK18, EK19, EK20, EK21, EK22 ve EK23 henüz merge edilmemiş karar PR'larındadır (#1175, #1179,
#1180, #1182, #1185, #1186, #1187, #1188, #1191). Bu belge onlara o PR'lardaki metinle atıf yapar. Çelişkide daha
yeni olan bu belge geçerlidir.

**Kaynak:** Sinan, 16 Kasım 2026, bu Claude Code oturumunda soru penceresiyle beş turda cevapladı:

- 1. tur (W5-1.1–W5-1.4): Issue #1189 yorumu
  https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6916648370
- 2. tur (W5-2.1–W5-2.4): Issue #1189 yorumu
  https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6916737452
- 3.–5. tur (W5-3.1–W5-3.16): Issue #1189 yorumu
  https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6916949682

Sorular ve öneriler, Issue #1189'daki W5 `NEEDS-SINAN` listesinden geldi:
https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6916533983 . "(Önerilen)" işaretleri soranındır.
Serbest metin cevaplar aynen alıntılanmıştır. O listedeki "Cevaplı görünenler" bölümü (25 madde) bu belgede karar
olarak yazılmaz; her biri kendi karar dosyasındaki metinle geçerlidir ve dilim sahibince doğrulanır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| W5-1.1 | FNS-D87 (FIN-SC36): Banka hesabı eksiye düşmez kuralı fail-closed bağlı, ama gerçek banka bakiye kaynağı yok; banka ve masraf ödemesi kaydı 503 dönüyor. Bakiye kaynağı ne olsun? | Serbest metin: **"Şuan deneme olduğu için yapılamıyorsa bile bunu denemenin yolunu bulalım"** | Cevap seçeneklerden biri değildir; yöntem için takip sorusu (W5-2.1) soruldu. Seçilmeyen: "Ayrı dilim, 503 kalsın (Önerilen)" — Bakiye kaynağı ayrı dilimde gelir; o zamana kadar kayıt 503 döner. "Bakiye kontrolü olmadan kaydet" — Bakiye okunmadan banka ödemesi kaydedilir. "Elle girilen bakiye" — Banka bakiyesi kullanıcı tarafından elle girilir. |
| W5-2.1 | W5-1.1 takibi: Deneme ortamında bakiye kontrolü hangi yolla çalışsın? | **İç defterden hesapla (Önerilen)** | "Bakiye = onaylı açılış bakiyesi + sistemde kayıtlı banka hareketleri. Dış banka bağlantısı yok; gerçek entegrasyon gelince mutabakatta kullanılır." Seçilmeyen: "Yalnız deneme ortamına sahte kaynak" — Bakiye yalnız deneme ortamında sahte bir kaynaktan okunur. "Bakiye kontrolü deneme ortamında kapalı" — Deneme ortamında bakiye kontrolü yapılmaz. |
| W5-1.2 | ALC-S5 / SAT-D396: Tedarikçi iadesini giren kişi kendi elle maliyet istisnasını onaylayabilir veya reddedebilir mi? | **Hayır, başka kişi onaylar (Önerilen)** | "Giren ≠ onaylayan; `AllowSameActorCostExceptionDecision` false kalır." Seçilmeyen: "Evet, aynı kişi onaylayabilir" — İadeyi giren kişi kendi istisnasına karar verebilir. |
| W5-1.3 | SAT-S13 / SAT-D322 (3)-(4): Kantar farkı ≠ 0 iken bir ürünün kantar kg'ı birden çok fatura satırına nasıl bölünsün? | **Teorik kg oranında (Önerilen)** | "Fark satırların teorik kg'larıyla orantılı dağıtılır; yuvarlama artığı en ağır satıra eklenir." Seçilmeyen: "Tüm fark en ağır satıra" — Farkın tamamı teorik kg'ı en büyük satıra yazılır. "Karar gelene kadar dursun" — Fatura `weighing_kg_transfer_pending` ile fail-closed bekler. |
| W5-1.4 | SAT-S8 / STK-S17: Stok tarafındaki talep seviyesi rezerv düşürme portunu (`IInventoryFutureAllocationReleasePortV1`) kim, ne zaman yapsın? STK-S17 (W6) SAT-S8'e bağlı, bağımlılık döngüsel. | **Port dilimi W6 başına (Önerilen)** | "stok_lojistik W6'nın ilk işi olarak portu yapar; SAT-S8 X1 ona bağlanır." Seçilmeyen: "Satış takımı yapsın" — Port, Satış şeridince yazılır. "W7'ye ertele" — Port ve SAT-S8 X1 W7'ye kalır. |
| W5-2.2 | ALC-S5 / SAT-D371, SAT-D393: Onaylı maliyet istisnası Stok'un `cost_difference_unposted` (409) reddini aşsın mı? | **Hayır, fark işlenene kadar beklesin (Önerilen)** | "Bugünkü 409 kalır; iade, fark işlenene kadar bekler." Seçilmeyen: "Evet, onaylı istisna geçer" — Onaylı istisna 409 reddini aşar. |
| W5-2.3 | ALC-S5 / SAT-D392: Tedarikçi iadesinde "ilgili fatura" maliyeti hangi kaynaktan okunsun? Fatura maliyet portu yok, her iade istisna yoluna düşüyor. | **Fatura kaydı gelene kadar istisna (Önerilen)** | "Tedarikçi faturası kaydı gelene kadar onaylı elle istisna yolu (SAT-D393) sürer." Seçilmeyen: "Mal kabul maliyeti" — Fatura yerine mal kabuldeki maliyet okunur. "Sipariş fiyatı" — Fatura yerine sipariş satırı fiyatı okunur. |
| W5-2.4 | Kalan W5 NEEDS-SINAN listesi nasıl kapansın? | **Önerileri toplu kabul, kritikleri sor (Önerilen)** | "Para/stok/yetki etkili sorular tek tek sorulur; geri kalan, bugünkü fail-closed davranışı koruyan öneriler kabul edilir. Grant/rol/Critical ataması ve `OPEN-*` kapatma insan kapısında kalır." Seçilmeyen: "Hepsini tek tek sor" — Listedeki her soru ayrı sorulur. "Hepsini toplu kabul et (grant tablosu yine insan kapısında)" — Tüm öneriler, kritikler dahil, toplu kabul edilir. |
| W5-3.1 | FIN-SC47-KREDI, URT-S2, URT-S4, STK-SC36-c, KIM-S19: W5'te açılan yeni izinlerin Critical sınıfı ne olsun? | **Critical değil, grant'a kadar kapalı (Önerilen)** | "KIM-D9'a göre Critical değil; grant yapılana kadar uç 403 döner (fail-closed). Grant/rol ataması insan kapısında kalır." Seçilmeyen: "Kredi onayı Critical olsun" — `finance.credits.approve` Critical sınıfına alınır. "Onay izinlerinin hepsi Critical" — Yeni onay izinlerinin hepsi Critical olur. |
| W5-3.2 | SAT-S2: Müşteri iadesinin Finans'a giden BusinessDate'i ne olsun? Kesinleştirme günüyle ertesi gün yapılan tekrar 409 anahtar çakışması verir. | **İlk kesinleştirme günü sabit (Önerilen)** | "Tekrarda payload değişmesin diye ilk kesinleştirme günü sabitlenir; tekrarlarda değişmez." Seçilmeyen: "Müşteri belge tarihi" — Müşterinin iade belgesindeki tarih kullanılır. |
| W5-3.3 | ALC-SC47 / ALS-D112: Onaylı alış siparişinde cari değişince satır fiyatları, tedarikçi referansları, revizyon ne olur; mal kabul veya fatura varsa izin var mı? | **Mal kabul/fatura yoksa, yeniden onayla (Önerilen)** | "Mal kabul veya fatura olan siparişte cari değişikliği reddedilir. Yoksa taslak revizyon açılır; fiyat ve tedarikçi referansları yeniden girilip onaylanır." Seçilmeyen: "Hiç değiştirilemesin" — Onaylı siparişte cari hiç değişmez. "Kabul olsa da değişsin" — Mal kabul veya fatura olsa da cari değişebilir. |
| W5-3.4 | STK-SC36-a / SAT-D385: Pozisyonda stok yokken veya alacak pozisyon değerini aşınca geç fatura maliyet farkı nereye gitsin? | **Satılan mal maliyetine (Önerilen)** | "Emilemeyen kısım dönemin satılan mal maliyeti (SMM) farkı olarak Finans'a gider." Seçilmeyen: "Karar gelene kadar dursun" — `NoStockToAbsorb` / `DifferenceExceedsValue` ile hiçbir şey yazılmaz. "Ayrı maliyet farkı hesabı" — Emilemeyen fark ayrı bir maliyet farkı hesabında bekler. |
| W5-3.5 | STK-SC36-b / FNS-D77, FNS-D78: Onaylı kuru mal kabulde kim, ne zaman kaydetsin? | **Sistem, kabul anında otomatik (Önerilen)** | "Siparişin onaylı kuru, yoksa o günün TCMB döviz satış kuru alınır; ilk giriş kuru olarak saklanır." Seçilmeyen: "Depocu kabulde elle girer" — Kur mal kabul sırasında depocu tarafından yazılır. "Muhasebe sonradan girer" — Kur mal kabulden sonra muhasebe tarafından girilir. |
| W5-3.6 | STK-SC36-b: Birleştirilen etiketlerde ilk giriş kurunun soy kuralı ne olsun? | **Kg ağırlıklı ortalama (Önerilen)** | "Birleşen etiketlerin ilk giriş kurlarının kg ağırlıklı ortalaması alınır; biri okunamazsa 'hesaplanamadı'. Üretimde FNS-D78 aynen." Seçilmeyen: "En eski etiketin kuru" — Birleşen etiketlerden en eskisinin kuru alınır. "Okunamazsa hesaplanamadı" — Ortalama alınmaz; birleştirmede kur hep "hesaplanamadı" olur. |
| W5-3.7 | STK-SC36-a / SAT-D386, SAT-D403, SAT-D407: Belge altı iskonto alanının sahibi hangi modül olsun (Alış siparişi mi, fatura mı)? | Serbest metin: **"Toplam üzerinden değil satır bazlı yapılabilsin"** | Cevap seçeneklerden biri değildir: iskonto satır bazlı yapılır; belge altı (toplam üzerinden) iskonto alanı açılmaz. Seçilmeyen: "Alış sahibi, Stok okur (Önerilen)" — Belge altı iskonto alanı Alış siparişinde olur, Stok portla okur. "Yalnız faturada" — Belge altı iskonto yalnız faturada girilir. |
| W5-3.8 | FIN-S13 (320): Alış tarafı için olay ve kabul türü yok; alış fatura fişi ne zaman üretilsin? | **Alış faturası olayı gelince (Önerilen)** | "`purchasing.invoice.finalized` olayı W6'da Alış-Cari'ye eklenir; satış fişi kalıbı kullanılır. Olay gelene kadar alış fişi üretilmez." Seçilmeyen: "Mal kabulden üret" — Alış fişi mal kabul olayından üretilir. "Sonraya ertele" — Alış fişi dilimi belirsiz bir sonraki döneme kalır. |
| W5-3.9 | FIN-SC47-KREDI: Kredi gruplarının (nakdi, gayrinakdi, çek/senet + faktoring, leasing) grup bazındaki alanları ne olsun? | **Taslak spec, sen onayla (Önerilen)** | "Grup başına alan spec taslağı hazırlanır; Sinan onaylayana kadar ilgili grubun hareketleri kapalı kalır." Seçilmeyen: "Ortak alanlarla başla" — Bütün gruplar ortak alanlarla açılır. "Yalnız nakdi krediyle başla" — Önce yalnız nakdi kredi grubu açılır. |
| W5-3.10 | URT-SC36 / FNS-D73, FNS-D83: Siparişe bağlı üretim girdisinin sipariş kuru okunamazsa ilk mal kabul kuruna düşülsün mü? | **Hesaplanamadı, reddet (Önerilen)** | "Sipariş kuru okunamazsa maliyet 'hesaplanamadı' olur ve işlem reddedilir (fail-closed)." Seçilmeyen: "İlk mal kabul kuruna düş" — Sipariş kuru okunamazsa girdinin ilk mal kabul kuru kullanılır. |
| W5-3.11 | SAT-SC33 / SAT-D406: Risk hesabındaki ThisOrder deltası da sevk planına göre mi hesaplansın? | **Onaylı − sevk edilen (Önerilen)** | "ThisOrder deltası bugünkü gibi onaylı miktar − sevk edilen miktardır." Seçilmeyen: "Sevk planına göre kalan" — Delta, SAT-D406 (1)'deki sevk planına göre kalandan hesaplanır. |
| W5-3.12 | KIM-S19: N-12 numara serisinin biçimi, sıfırlama dönemi ve boşluk politikası ne olsun? | **Önek+yıl+sıra, yıllık sıfır (Önerilen)** | "Ör. SAT-2026-000123. Sıra her yıl sıfırlanır; iptal edilen numara boş kalır, yeniden kullanılmaz; boşluk raporlanır; biçim şirket ayarından değiştirilebilir." Seçilmeyen: "Sürekli sıra, sıfırlama yok" — Sıra hiç sıfırlanmaz. "Aylık sıfırlama" — Sıra her ay sıfırlanır. |
| W5-3.13 | KIM-S7: Kurucu kurtarma (recover-founder) operatörün yazdığı parolayı saklasın mı, yoksa yalnız aktifleştirip ayrı parola sıfırlama mı istesin? | **Geçici parola, ilk girişte değişim (Önerilen)** | "Operatörün yazdığı parola geçici parola olarak saklanır; ilk girişte değişim zorunludur (bugünkü davranış, KIM-D22/D23 ile uyumlu)." Seçilmeyen: "Yalnız aktifleştir, ayrı sıfırlama" — Kurtarma yalnız hesabı aktifleştirir; parola ayrı sıfırlama akışıyla belirlenir. |
| W5-3.14 | STK-SC36-c: Paket standardı sürümünü hazırlayan kişi aynı sürümü onaylayabilir mi? | **Hayır, başka kişi onaylar (Önerilen)** | "Maker ≠ checker; kod bugün kapalı." Seçilmeyen: "Evet, aynı kişi onaylayabilir" — Hazırlayan kendi sürümünü onaylayabilir. |
| W5-3.15 | SAT-SC33 / SAT-D406 (2): Plan üstü sevk uyarısı ve onayı hangi dilimde kodlansın? | **Lojistik sevk onayı dilimi, W6 (Önerilen)** | "Lojistik sevk onayı dilimine (W6) alınır; o zamana kadar Satış yalnız kalanı gösterir." Seçilmeyen: "Satış tarafında" — Uyarı ve onay Satış şeridinde kodlanır. |
| W5-3.16 | STK-SC36-c: Draft durumundaki ürüne paket standardı yazılabilir mi? | **Hayır, yalnız aktif ürün (Önerilen)** | "Paket standardı yalnız aktif ürüne yazılır; Draft ve pasif ürün reddedilir." Seçilmeyen: "Evet, draft da olur" — Draft ürüne de paket standardı yazılabilir. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D102 | Banka bakiye kaynağı (W5-1.1, W5-2.1; FNS-D87'yi tamamlar) | Serbest metin cevap (W5-1.1) ve takip cevabı (W5-2.1). FNS-D87'nin "banka hesabı eksiye düşmez" kontrolü için bakiye **iç defterden** hesaplanır: **bakiye = onaylı açılış bakiyesi + sistemde kayıtlı banka hareketleri**. Dış banka bağlantısı kurulmaz. Gerçek banka entegrasyonu gelince bu bakiye **mutabakatta** kullanılır. Bakiye okunamazsa FNS-D87'deki fail-closed ret aynen sürer. Hesap backend'dedir; ekran aynı sonucu gösterir. | Finans banka/masraf ödemesi kaydı (FIN-SC36), FIN01-02E e2e kayıt adımı |
| SAT-D408 | Alış iadesi istisnasında giren ≠ onaylayan (W5-1.2; SAT-D396'yı tamamlar) | Tedarikçi iadesini giren kişi kendi elle maliyet istisnasını **onaylayamaz ve reddedemez**; karar başka bir kişinin `purchasing.orders.approve` izniyle verilir. `AllowSameActorCostExceptionDecision` **false** kalır. | Alış iadesi istisna onayı (ALC-S5) |
| SAT-D409 | Kantar kg'ının fatura satırlarına dağıtımı (W5-1.3; SAT-D322 (3)-(4)'ü tamamlar) | Kantar farkı ≠ 0 iken bir ürünün kantar kg'ı birden çok fatura satırına, satırların **teorik kg'larıyla orantılı** dağıtılır. **Yuvarlama artığı en ağır satıra** eklenir. Hesap backend calculator'ındadır. SAT-D322'nin geri kalanı aynen geçerlidir. | Satış faturası kantar aktarımı (SAT-S13) |
| KLN-D112 | Talep seviyesi rezerv düşürme portunun dilimi (W5-1.4; KLN-D105'i tamamlar) | `IInventoryFutureAllocationReleasePortV1` talep seviyesi azaltma portu **stok_lojistik W6'nın ilk işi** olarak yapılır. SAT-S8 X1 bu porta bağlanır; STK-S17 ile SAT-S8 arasındaki döngüsel bağımlılık böyle çözülür. | Dilim planı (W6), Stok release portu, SAT-S8 |
| SAT-D410 | Onaylı istisna ve Stok 409 (W5-2.2; SAT-D371 ve SAT-D393'ü tamamlar) | Onaylı elle maliyet istisnası Stok'un **`cost_difference_unposted` (409)** reddini **aşmaz**; iade, fark işlenene kadar bekler (bugünkü davranış). | Alış iadesi, Stok maliyet kapısı (ALC-S5) |
| SAT-D411 | Alış iadesinde "ilgili fatura" maliyeti (W5-2.3; SAT-D392 ve SAT-D393'ü tamamlar) | Tedarikçi faturası kaydı ve fatura maliyet portu gelene kadar SAT-D392'deki fark okunamaz sayılır; iade SAT-D393'teki **onaylı elle istisna yolu** ile devam eder. Mal kabul maliyeti veya sipariş fiyatı yerine kullanılmaz. | Alış iadesi (ALC-S5) |
| KIM-D52 | W5 yeni izinlerinin Critical sınıfı (W5-3.1; KIM-D9'u uygular) | W5'te açılan yeni izinler (`counterparties.legal_name.propose`, `counterparties.legal_name.approve`, `finance.credits.read`, `finance.credits.propose`, `finance.credits.approve`, `settings.finance-opening-period.manage`, `settings.reference-lists.manage`, `settings.production-reference-lists.manage`, `manufacturing.facts.correct`, `manufacturing.orders.cancel`, `manufacturing.plans.reorder`, `product.package-standards.approve`) **Critical değildir** (KIM-D9). Grant yapılana kadar ilgili uç **403** döner (fail-closed). Bu belge persona, rol ataması veya grant **seçmez** (Sınırlar). | İzin kaydı (ErpPermissions Critical sınıflandırması) |
| SAT-D412 | Müşteri iadesinin Finans BusinessDate'i (W5-3.2) | Müşteri iadesinin Finans'a giden BusinessDate'i **ilk kesinleştirme günüdür** ve **tekrarlarda değişmez**; tekrar aynı payload ile gider, ertesi gün tekrarında 409 anahtar çakışması oluşmaz. Müşteri belge tarihi kullanılmaz. | Satış müşteri iadesi Finans sunumu (SAT-S2) |
| ALS-D115 | Onaylı alış siparişinde cari değişikliği (W5-3.3; ALS-D112'yi tamamlar) | Onaylı siparişte **mal kabul veya fatura varsa** cari değişikliği **reddedilir**. Yoksa sipariş **taslak revizyona** döner; satır fiyatları ve tedarikçi referansları yeni cari için **yeniden girilir** ve sipariş ALS-D112'deki gibi **yeniden onaylanır**. ALS-D112'nin geri kalanı (onay izni `purchasing.orders.approve`) aynen geçerlidir. | Alış siparişi cari değişikliği (ALC-SC47) |
| SAT-D413 | Emilemeyen geç fatura maliyet farkı (W5-3.4; SAT-D385 ve SAT-D404'ü tamamlar) | Geç gelen dövizli alış faturasının farkı pozisyonda stok yokken veya alacak fark pozisyon değerini aştığında, **emilemeyen kısım dönemin satılan mal maliyeti (SMM) farkı** olarak Finans'a gider. Emilebilen kısım SAT-D385/SAT-D404'teki gibi ortalamaya katılır. BS-1 aynen geçerlidir. | Stok maliyet farkı (STK-SC36-a), Finans fiş önerisi |
| FNS-D103 | Mal kabulde ilk giriş kuru (W5-3.5; FNS-D77 ve FNS-D78'i tamamlar) | İlk giriş kuru mal kabul anında **sistem tarafından otomatik** kaydedilir: siparişin **onaylı kuru**, sipariş kuru yoksa **o günün TCMB döviz satış kuru**. Bu kur malın **ilk giriş kuru** olarak saklanır ve FNS-D77/FNS-D78'deki çevrimde kullanılır. | Stok mal kabul, ilk giriş kuru portu (STK-SC36-b) |
| FNS-D104 | Birleştirilen etiketlerde ilk giriş kuru (W5-3.6; FNS-D77 ve FNS-D78'i tamamlar) | Birleştirmede yeni etiketin ilk giriş kuru, birleşen etiketlerin ilk giriş kurlarının **kg ağırlıklı ortalamasıdır**. Birleşen etiketlerden birinin kuru okunamazsa sonuç **"hesaplanamadı"**dır (fail-closed). Üretim çıktısında FNS-D78 aynen geçerlidir. Hesap backend calculator'ındadır. | Stok birleştirme (STK-SC36-b) |
| SAT-D414 | Satır bazlı iskonto (W5-3.7; SAT-D386, SAT-D403 ve SAT-D407'yi günceller) | Serbest metin cevap (Soru ve cevaplar, W5-3.7). İskonto **satır bazlı** yapılır. **Belge altı (toplam üzerinden) iskonto alanı açılmaz.** SAT-D386/SAT-D403'teki satır iskontosu kuralı aynen geçerlidir. Belge altı iskonto alanı olmadığından SAT-D403'ün belge altı kısmı ve SAT-D407'nin dağıtım/yuvarlama kuralı uygulanacak bir girdi bulmaz; belge altı allocator bağlanmaz. Alanın açılması yeni karar ister. | Alış siparişi satır iskontosu, Stok ortalama maliyeti (STK-SC36-a) |
| FNS-D105 | Alış fatura fişi (W5-3.8; KLN-D102'nin alış tarafı) | Alış fatura fişi (320) **`purchasing.invoice.finalized` olayı gelince** üretilir. Olay **W6'da Alış-Cari'ye** eklenir; fiş **satış fişi kalıbıyla** üretilir. Olay gelene kadar alış fişi üretilmez. | Alış-Cari olayı (W6), Finans fiş önerisi (FIN-S13) |
| FNS-D106 | Kredi grup alanları (W5-3.9; FNS-D93 ve FNS-D95'i tamamlar) | Kredi grupları (nakdi, gayrinakdi, çek/senet + faktoring, leasing) için **grup başına alan spec taslağı** hazırlanır ve **Sinan'ın onayına** sunulur. Onaylanana kadar ilgili grubun hareketleri **kapalı** kalır. | Kredi spec'i, FIN-SC47-KREDI |
| FNS-D107 | Siparişe bağlı üretim girdisinde sipariş kuru okunamazsa (W5-3.10; FNS-D73 ve FNS-D83'ü tamamlar) | Siparişe bağlı girdinin sipariş kuru (FNS-D83: siparişte yazan tür ve tarih) okunamazsa ilk mal kabul kuruna **düşülmez**; maliyet **"hesaplanamadı"** olur ve işlem **reddedilir** (fail-closed). | Üretim girdi maliyeti (URT-SC36) |
| SAT-D415 | Risk hesabında ThisOrder (W5-3.11; SAT-D406 (1)'i tamamlar) | Satış risk hesabındaki ThisOrder deltası **onaylı miktar − sevk edilen miktar** olarak kalır (bugünkü hesap). SAT-D406 (1)'deki sevk planına göre kalan bu deltaya uygulanmaz. | Satış risk kapısı (SAT-SC33) |
| KIM-D53 | N-12 numara serisi (W5-3.12) | Belge numarası **önek + yıl + sıra** biçimindedir (ör. `SAT-2026-000123`). Sıra **her yıl sıfırlanır**. İptal edilen numara **yeniden kullanılmaz** (boş kalır); **boşluklar raporlanır**. Biçim **şirket ayarından** değiştirilebilir. | Ayarlar numara serisi sözleşmesi (KIM-S19) |
| KIM-D54 | Kurucu kurtarma parolası (W5-3.13; KIM-D22 ve KIM-D23'ü tamamlar) | Kurucu kurtarmada operatörün yazdığı parola **geçici parola** olarak saklanır; **ilk girişte parola değişimi zorunludur** (bugünkü davranış). | Kimlik recover-founder (KIM-S7) |
| SAT-D416 | Paket standardında maker ≠ checker (W5-3.14; SAT-D397 ve SAT-D401'i tamamlar) | Paket standardı sürümünü hazırlayan kişi **aynı sürümü onaylayamaz**; onayı başka bir kişi verir. | Paket standardı onayı (STK-SC36-c) |
| SAT-D417 | Plan üstü sevk uyarısı ve onayının dilimi (W5-3.15; SAT-D406 (2)'yi tamamlar) | SAT-D406 (2)'deki plan üstü sevk uyarısı ve onayı **Lojistik sevk onayı diliminde (W6)** kodlanır. O zamana kadar Satış yalnız kalanı gösterir. | Dilim planı (W6), Lojistik sevk onayı |
| SAT-D418 | Draft ürüne paket standardı (W5-3.16; SAT-D397 ve SAT-D401'i tamamlar) | Paket standardı **yalnız aktif ürüne** yazılır; **Draft** ve pasif ürün reddedilir. | Paket standardı yazma (STK-SC36-c) |
| KLN-D113 | W5 tek satırlık önerilerinin toplu kabulü (W5-2.4) | Aşağıdaki "Toplu kabul edilen öneriler" tablosundaki **TK-1…TK-44** önerileri, tablodaki metinle karar olur. Kapsam: W5 `NEEDS-SINAN` listesindeki tek satırlık sorulardan W5-1.x, W5-2.x ve W5-3.x'te **tek tek sorulmayanlar**. Tek tek sorulan 22 soru bu listeye girmez; onların kararı yukarıdaki satırlardadır. Öneride "teknik ayar, ASSUMPTION olabilir" denen noktalar **uygulama ayrıntısıdır (geri alınabilir)**. Toplu kabul grant/rol/Critical ataması yapmaz ve hiçbir `OPEN-*` kaydını kapatmaz. | Tablodaki dilimler |

Uygulama ayrıntısı (geri alınabilir): FNS-D102'deki iç defter bakiyesinin hangi sorgu veya projeksiyonla
hesaplanacağı, SAT-D409'daki yuvarlama hassasiyetinin kod içindeki gösterimi (kg ondalığı mevcut kg türünden gelir),
KIM-D53'teki önek ve sıra uzunluğu başlangıç değerleri (ayar olarak) ve hata/durum kodlarının adları kodlayan
şeridin seçimidir.

## Toplu kabul edilen öneriler (KLN-D113)

| TK | Soru | Kabul edilen öneri | Dilim |
|---|---|---|---|
| TK-1 | İade önce Finans'a sunulup sonra Satış'ta mı commit edilsin (SAT-D294, kodlanan), yoksa önce Satış commit + outbox mı (SAT-D208)? | Kodlanan sıra kalır; çökme deterministik AcceptanceId ile tekrarda iyileşir. | SAT-S2 |
| TK-2 | SAT-D343 irsaliye toplam oranına ek olarak ürün başına ayrı fark üst sınırı olsun mu? | Yok; yalnız SAT-D343 toplam oranı uygulanır (bugünkü kod). | SAT-S13 |
| TK-3 | Fason ortak tartı paylaştırmasında teorik kg kaynağı Lojistik `LoadedKgAtWeighing` (WGT-002) mi? | Evet; okunamazsa paylaştırma yapılmaz (SAT-D376 yazar eki, fail-closed). | SAT-SC34 |
| TK-4 | Sipariş satırından farklı birimli plan satırında Lojistik mi sipariş biriminde cevaplasın, Satış mı çevirsin? | Lojistik sipariş biriminde cevaplar; o zamana kadar `plan_answer_invalid` ile ret. | SAT-SC33 |
| TK-5 | Release katılımcısında "bulunamadı / başka şirket" terminal (kayıtlı) red sayılsın mı? | Evet (teknik ayar, uygulama ayrıntısı). | SAT-S8 |
| TK-6 | Termin için shell capability de VEYA olsun mu (ShellCapabilityRegistryAuthorities yeniden üretimi gerekir)? | Evet, izin VEYA'sıyla hizalı (teknik ayar, uygulama ayrıntısı). | SAT-SC33 |
| TK-7 | Unvan onayında bayatlık kuralı: talepten sonra herhangi bir cari düzenlemesi onayı reddediyor, ret serbest. Daraltılsın mı? | Geniş kural kalır (fail-closed). | ALC-SC47 |
| TK-8 | Tahsis bağı aktifken düz satır kapatma da 409 `purchasing_line_has_allocations` dönüyor; "bağları düşür ve kapat" tek yol. F-017 §3.6 bunu mu istiyor? | Evet; ALS-D85'in iptal kuralı kapanışa da uygulanır. | ALC-S3 |
| TK-9 | KLN-D110 süpürmesinde bırakılan diğer tahsisler tetikleyen hatanın kodunu taşıyor; ayrı genel neden kodu açılsın mı? | Şimdilik tetikleyen kod + Türkçe ReasonText; yeni kod iki sahibin listesine birlikte eklenir. | ALC-SC34 |
| TK-10 | ALS-D83 reasonText uzunluk sınırı Alış'ın mı, Stok release portunun mu? | İkisinin küçüğü (teknik ayar, uygulama ayrıntısı). | ALC-S3 |
| TK-11 | Okunamayan maliyetle tedarikçi iadesi istisnası bir Alış komutu mu, düzeltilen maliyet bir toleransla sınırlı mı? | İzin SAT-D396 ile `purchasing.orders.approve`; tolerans seçilmez, karar gelene kadar SAT-D371 reddi sürer. | STK-SC36-a |
| TK-12 | Çok fason siparişli talimatta risk kapısı hep-ya-hiç mi, kısmi sevke izin var mı? | Hep-ya-hiç (bugünkü okuma). | STK-S3 |
| TK-13 | Satış fark faturası hazır olmadan çıkış sonrası tartı düzeltmesi (LOJ-D13) açılabilir mi? | Kapalı kalır; faturalanmış tartı düzeltilemez (SAT-D378 okuması). | STK-S3 |
| TK-14 | Kesin sevk iptali (STK-S5) ne zaman/hangi kurallarla gelir? `logistics.definite-shipment.cancelled.v1` olayının üreticisi yok, Satış tüketicisi hazır. | Üretici gelene kadar Satış tüketicisi pasif bekler. | SAT-SC33, SAT-S10 |
| TK-15 | KLN-D108 geri çekilen kayıp talebinden sonra yeni talep açılabilir mi? | Kod yeni numarayla izin veriyor; geri çekme KLN-D108'e göre talebi oluşturan izinle (`StockTransferLossRecord`). | STK-SC36-b |
| TK-16 | KLN-D107 yeniden talep kararında maker ≠ approver hem teslim alanı hem yenileyeni kapsar mı? | İkisini de kapsar (bugünkü `SelfApproval` reddi). | STK-SC36-b |
| TK-17 | Stoktaki Counterparty sahibinin OwnerId'si talimatın CustomerPartyId'siyle aynı Party kimliği mi? | Eşleşme kanıtlanana kadar Satış sahiplik kapısı tüm sevkleri korur. | STK-S3 |
| TK-18 | Fason teslimin Inventory tahsis talep türü (demand kind) adı ve sürümü ne olsun? | Teknik sözleşme adı (uygulama ayrıntısı); o zamana kadar yol kapalı. | STK-S3 |
| TK-19 | `inventory.future-allocation.released.v1` AcceptanceId ve ham bağ hatası kodunu taşısın mı? | Yeni sürümlü olayla (v2) eklenir; bugün Satış SalesOrderLineId + ReasonText ile uyarır. | ALC-SC34 |
| TK-20 | Paket standardı onayına geçmiş tarihli yürürlük yazılabilir mi? | Hayır; en erken bugünün iş günü (bugünkü kod). | STK-SC36-c |
| TK-21 | Saklanan orijinal .xlsx indirilebilir mi; indirilirse vault/malware taraması gerekir mi? | İndirme kapalı; yalnız metadata, SHA-256 ve sonuç raporu. | STK-SC36-c |
| TK-22 | Müşteri istisnasındaki cari Cari'de müşteri rolü taşımalı mı? | Müşteri rolü zorunlu (dar ve geri alınabilir). | STK-SC36-c |
| TK-23 | İlk onay Ayarlar anlık görüntüsü almadıysa yeniden onay reddediliyor. İstenen politika bu mu? | Evet (fail-closed). | URT-S4 |
| TK-24 | Termini geçmiş plan yeniden onayı engellesin mi? | Engeller; emir açılışıyla aynı kontrol. | URT-S4 |
| TK-25 | Emir iptaliyle serbest kalan plan yeniden onaylanana kadar iş merkezi kuyruğundan çıksın mı? | Evet; kapasite yükü şişmesin. | URT-S4 |
| TK-26 | Kuyruk sırasında hangi termin kullanılsın: planın emri varsa emir termini, yoksa planın istenen termini doğru mu? | Bugünkü kod gibi. | URT-SC33 |
| TK-27 | Kesirli gün tamponu (ör. 1,5 gün) reddedilsin mi, yuvarlansın mı? | `safety_margin_invalid` ile ret (bugünkü kod). | URT-SC33 |
| TK-28 | Plan onayı iş merkezi kapasite kaydını yakalasın mı (şimdi onaylı planlar da son Ayarlar sürümünü kullanıyor)? | Onayda sürüm kimliği yakalanır (KIM-D47 snapshot mantığı); uygulanana kadar bugünkü davranış. | URT-SC33, KIM-SC33-b |
| TK-29 | Ayarlar'da tanımlı ama kullanılamaz çıktı etiketi formatı çıktı beyanını `format_invalid` ile durduruyor. İstenen bu mu? | Evet; URT-D98 tanımlı formatta elle kodu yasaklar. | URT-SC36 |
| TK-30 | FNS-D97 kredi fiş önerisinin Ayarlar eşleme anahtarları ve ayrıntısı (kullanım / geri ödeme / faiz tahakkuku) ile yazma yolu ve yetkisi ne? | Üç amaçlı taslak kalır; tablo boş, port `MappingMissing` döner, öneri "eşleme eksik" bekler. | FIN-SC47-KREDI, KIM-S19 |
| TK-31 | B-04b portuna sistem (inbox) kabulünü hangi izin/politika ölçer? | `Accounting:InvoiceJournal:InboxAdmissionEnabled` varsayılan false; açılması insan kapısı. | FIN-S13 |
| TK-32 | Bankaya teminat verilen evrak için "teminatta" durumu yok (FNS-D82, OPEN-010). Kredi/teminat dilimiyle mi gelsin? | Evet; OPEN-010 kapanmadan durum açılmaz. | FIN-SC36 |
| TK-33 | Banka/kredi veren ana verisi yok; LenderName serbest metin. Ana veri açılsın mı? | Şimdilik serbest metin; ana veri ayrı dilim. | FIN-SC47-KREDI |
| TK-34 | İleri tarihli AsOfDate reddedilsin mi? | Reddedilir (ASSUMPTION-FIN-S9-ASOF); FNS-D86 AsOfDate'i şirket iş günü yapar. | FIN-S9 |
| TK-35 | Onaylı açılışın ters kaydı ayrı izin mi alsın? | FNS-D85 personalarıyla uyumlu olarak `finance.opening-balances.propose` / `.approve` kalır; yeni ad yok. | FIN-S9 |
| TK-36 | Alış fasonu KDV/tevkifat varsayılanını Ayarlar'da kim yazar, satırdaki kodu hangi izin değiştirir? | Yazma rotası kapalı kalır; satış tarafındaki SAT-D395 gibi `purchasing.orders.approve` en yakın okuma. | KIM-S19 |
| TK-37 | Şirket geneli termin tamponunun üst sınırı kaç iş günü? | Yalnız ≥ 0 zorunlu; üst sınır seçilmeden eklenmez. | KIM-S19 |
| TK-38 | URN-D5 üretim profilleri sürüm-0 aktarımı hangi şirketlere, hangi izinle çalışır? | Kurulmaz; değer değiştirmeyen idempotent aktarım tekniği uygulama ayrıntısı, kapsam Sinan'da. | KIM-S15 |
| TK-39 | KIM-D16'ya göre yönetebilen başka aktif kullanıcı varken o kişi ulaşılamazsa kurucu için onaylı yol var mı? | Yok; ret sürer (KIM-D23 yalnız "başka Admin yok" halini açar). | KIM-S7 |
| TK-40 | `identity.founder.recovered` / `password_reset` / `recovery_refused` olaylarının saklama sınıfı diğer kimlik olaylarıyla aynı sınıf mı? | Diğer kimlik olaylarıyla aynı; imha kapalı. | KIM-S7 |
| TK-41 | Bayatlamış staged VKN düzeltmesine reddetme komutu mu eklensin, yeni staging çalışmasıyla mı kapansın? | Ayrı reddetme komutu (denetimli, geri alınabilir); o zamana kadar bekler. | KIM-SC34 |
| TK-42 | Eski ClearPlaceholderCompanyVkn Up'ını çalıştırmış tren veritabanlarına geriye dönük denetim satırı mı yazılsın, ortamlar yeniden mi kurulsun? | Tren ortamları yeniden kurulur (main'de yok). | KIM-SC34 |
| TK-43 | ALS-D79 işlem türü listesi (satış fasonu dahil) ne zaman açılır, ortak masraf yapısı ayrıntısı (NEEDS-SPEC) ne? | İşlem türü listesi kapalı; masraf tipi listesi KIM-D49 ile boş açık. | KIM-S18 |
| TK-44 | Platform Audit sözleşmesinde System aktörü ve operatör beyanı alanları yok; recover-founder şirket Audit satırı yazamıyor. Sözleşmeyi kim değiştirir? | Ayrı platform K3 dilimi (cross-module contract); o zamana kadar yalnız kimlik olayları yazılır. | KIM-S7 |

## Sınırlar

- **Grant ve rol ataması:** KIM-D52'deki 12 iznin persona, rol ve grant ataması bu belgede yapılmaz; W5 listesindeki
  "önerilen persona" sütunu karar değildir. Grant yapılana kadar uçlar 403 döner. Ataması insan kapısıdır.
- **`OPEN-*` kayıtları:** OPEN-010 (TK-32) ve diğer `OPEN-*` kayıtları bu belgeyle kapanmaz.
- **Kredi spec'i (FNS-D106):** grup başına alan spec'i ayrıca Sinan'ın onayına sunulur; onaya kadar ilgili grubun
  hareketleri kapalıdır. Bu belge alanları seçmez.
- **Toplu kabulün sınırı (KLN-D113):** TK-1…TK-44 tablosunda bulunmayan, W5 listesine sonradan eklenen veya yeni
  ortaya çıkan noktalar karara bağlanmamıştır → `NEEDS-SINAN`. Toplu kabul edilen bir öneri ileride para, stok,
  üretim, formül, yetki, güvenlik, migration veya saklama politikası seçmeyi gerektirirse o nokta ayrıca sorulur.
- **"Cevaplı görünenler":** W5 listesindeki 25 madde bu belgede karar olarak yazılmaz; ilgili karar dosyasındaki
  metinle geçerlidir ve dilim sahibince doğrulanır.
- **SAT-D414:** belge altı iskonto alanının ileride açılması yeni karar ister.
- **SAT-D413:** SMM farkının hangi hesaba ve hangi fiş eşlemesiyle yazılacağı Finans eşleme ayarıdır; bu belge eşleme
  anahtarını seçmez → `NEEDS-SINAN`.
- İnsan kapıları değişmez:
  - deployment, environment ve secret;
  - Settings → Actions etkinleştirmesi;
  - grant/rol/Critical;
  - production aktivasyonu;
  - `OPEN-*` kapatma;
  - `.claude/settings.json`.
