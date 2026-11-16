# Sinan Kararları — 2026-11-15 Dalga 2 soruları (ortalama maliyet toplamı, sevk planı, iade taksiti, masraf para profili, sevk riski, kredi limiti onayı, belge erişimi, unvan değişikliği, ebat kapsamı, kesit türleri)

Bu belge önceki karar dosyalarını silmez; `KLN-D55`, `SAT-D353`, `SAT-D354`, `FNS-D54`–`FNS-D56`, `KLN-D56`,
`ALS-D98`, `URN-D10` ve `URN-D11` kararlarını ekler. `SINAN_KARARLARI_2026-11-15_MALIYET_VE_RISK.md` (`KLN-D53`,
`URT-D67`, `FNS-D53`, `KLN-D54`), `SINAN_KARARLARI_2026-11-12_URUN.md`, `SINAN_KARARLARI_2026-11-12_URUN_EK1.md`
(`URN-D1`–`URN-D9`) ve `SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` (BS-9) aynen geçerlidir; bu belge BS-9'u
aşağıda yazılan iki noktada netleştirir. Aynı günün SINAN-CEVAP 18 (`SINAN_KARARLARI_2026-11-15_GIRIS_VE_ARTIK.md`,
#1161) ve SINAN-CEVAP 19 (`SINAN_KARARLARI_2026-11-15_DALGA1_EK.md`, #1162) kararlarıyla ilişki, çelişkiler ve
tekrarlar "Önceki kararlarla ilişki" bölümünde tek tek yazılıdır; orada "önceki karar geçerli" denen maddede bu
belgedeki karar uygulanmaz.

**Kaynak:** Sinan, 2026-11-15, Claude şeridi oturumunda soru-cevap aracıyla. Sorular tren-1 dalga 1 takım
raporlarındaki ve takımlar arası mesajlardaki NEEDS-SINAN maddelerindendir. Makbuz: Issue #66 yorumu 6894839302. "(Önerilen)" işaretleri soranındır.
Masraf ödemesi sorusunun ilk hâline Sinan'ın cevabı kelimesi kelimesine: **"Bunu anlamadım bunu mantıklı
araştırırsan aynı olması gerekmiyormu ben mi yanlış anladım"**; soru açıklanarak yeniden soruldu ve aşağıdaki seçim
yapıldı. Bu turda kullanılabilir artık (BS-5) etiketinin bölünme/birleşme mirası da soruldu ve cevap gelmedi; ancak
bu soru aynı gün SINAN-CEVAP 18'de KLN-D46 ile cevaplanmıştı. Madde açık değildir (bkz. "Önceki kararlarla ilişki").

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Stok (STK-S35): ürünün şirket geneli ortalama maliyeti depoların ortalamasından hesaplanırken bir depo okunamıyorsa veya o depoda kayıt yoksa ne yapılsın? | Kg ağırlıklı, okunamazsa yok (Önerilen) | Depoların ortalaması eldeki kg ile ağırlıklandırılır. Stoğu ve kaydı olmayan depo hesaba girmez. Tek bir depo okunamazsa şirket geneli değer "okunamadı" gösterilir, eksik hesap yapılmaz. Seçilmeyenler: "Kg ağırlıklı, okunamayanı atla" (okunamayan depolar çıkarılır, "eksik veri" uyarısı); "Şirket geneli olmasın" (yalnız depo bazında, STK-S35 iptal). |
| 2 | Satış (SAT-D313): siparişte "sevk planı miktarı" Lojistik'te hangi kayıttan okunsun? | Yükleme talimatı payı (Önerilen) | Siparişin yükleme talimatlarına ayrılmış miktarı plan miktarı sayılır. Lojistik'te bu kayıt zaten var; yeni tablo gerekmez. Plan üstü sevk engellenir. Seçilmeyenler: "Ayrı sevk planı kaydı" (yeni Lojistik belgesi, tablo, migration, ekran); "Şimdilik plan yok" (kalan = onaylı − kesin sevk sürer). |
| 3 | Finans (FIN-S10): faturanın birden çok taksiti açıkken KISMİ müşteri iadesi kabul edilince iade tutarı hangi taksitten düşülsün? | En yakın vadeden (Önerilen) | İade tutarı önce vadesi en yakın açık taksitten düşülür, artarsa sıradakine geçer. Seçilmeyenler: "En uzak vadeden"; "Oransal dağıt" (açık taksitlere kalan tutarları oranında). |
| 4 | Masraf ödemesi: para profili bir işlemde hangi para birimi ve kaç ondalık girilebileceğini söyler (TL, 2 ondalık); değerler banka ödemesiyle aynı olmalı. Soru yalnız Ayarlar listesinde ayrı satır olup olmayacağı (Alış siparişinde ALS-D69 ile aynı değerlerle ayrı satır açılmıştı). | Ayrı satır, aynı değer (Önerilen) | Ayarlar'da "masraf ödemesi" satırı açılır, değerleri banka ödemesiyle aynıdır (TL, 2 ondalık). İleride biri değişirse diğerini etkilemez; Alış/Satış'taki düzenle aynı. Seçilmeyen: "Tek satır, banka ödemesininki" (masraf kalıcı olarak manuel banka ödemesinin satırını kullanır). |
| 5 | Satış/Finans risk: onaylı irsaliyesi henüz olmayan ama kesinleşmiş sevk, müşteri riskinde "faturalanmamış sevk" tutarına sayılsın mı? | Evet, kesin sevk sayılır (Önerilen) | Mal çıktığı an risk doğar; irsaliye onayı beklenmeden kesin sevk miktarı × fiyat riske eklenir. Seçilmeyen: "Hayır, irsaliye onayından sonra". |
| 6 | Finans (OPEN-009): müşteri kredi limiti belli bir tutarın üzerindeyse ikinci bir kişinin onayı gereksin mi? | Şimdilik tek onay (Önerilen) | Yetkili kişi limiti tek adımda belirler; her değişiklik denetim kaydına girer. İkinci onay ve eşik sonra eklenebilir. Seçilmeyenler: "Eşik üstü ikinci onay" (şirket bazında eşik, ikinci yetkili); "Her limit ikinci onay". |
| 7 | Belgeler (B-26): bir dosya birden çok kayıtla bağlantılıysa erişim kuralı ne olsun? | Önerilen kural (Önerilen) | Görme: bağlı kayıtlardan en az birini görebilen dosyayı görür. Yeni sürüm yüklemek: tüm bağlı kayıtları görebilmeli. Başka kayda bağlamak: kaynak ve hedef kaydı görebilmeli. İlk bağlantı birincil sayılır. Seçilmeyenler: "Daha sıkı: hepsini görmeli"; "Yalnız birincil kayıt". |
| 8 | Cari (CARI-DEC-004): carinin resmi/hukuki unvanı değiştirilirken hazırlayan-onaylayan düzeni nasıl olsun? | Tek kişi, mevcut yetki | Cari düzenleme yetkisi olan kişi unvanı doğrudan değiştirir; değişiklik denetim kaydına girer, onay yok. Seçilmeyenler: "İki yeni yetki, iki kişi (Önerilen)"; "İki kişi ve Critical". |
| 9 | Ürün (BS-9): ürün kartındaki ebatlar üç şirkette ortak mı, her şirkete ayrı mı? | Ortak, tek satır (Önerilen) | Ürün kartı şirketler arası ortak olduğu için ebatlar da tek; yetkisi olan herhangi bir şirketten düzenlenir ve her değişiklik denetim kaydına girer. Seçilmeyen: "Şirket bazında". |
| 10 | Ürün (BS-9): kesit türleri Sac = kalınlık/en/boy, Rulo = kalınlık/en, Yuvarlak boru = dış çap/et kalınlığı (kg/m isteğe bağlı), Profil = en/yükseklik/et kalınlığı (kg/m zorunlu). Yeterli mi? | Şimdilik bu dördü (Önerilen) | Dört tür ve ölçüler aynen kalır; yeni tür gerektiğinde ayrı dilimle eklenir. Seçilmeyen: "Köşebent/lama/dolu da ekle". |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KLN-D55 | Şirket geneli ortalama maliyetin toplanması | Şirket geneli ortalama maliyet, depo ortalamalarının **eldeki kg ile ağırlıklı** ortalamasıdır. Stoğu ve kaynağı olmayan depo hesaba girmez. Bir depo bile okunamazsa sonuç "okunamadı" olur; eksik depolarla hesap yapılmaz (fail-closed). Hiç kaynak yoksa sonuç "kaynak yok"tur. Depo ortalaması KLN-D53'teki hareket anı hareketli ortalamadır. | Stok STK-S35 (şirket geneli okuma portu), Satış marj okuması |
| SAT-D353 | Sevk planı miktarının kaynağı (SAT-D313) | Siparişin sevk planı miktarı, Lojistik'teki **yükleme talimatlarına ayrılmış miktardır**; yeni plan kaydı açılmaz. Plan üstü sevk engellenir. Okuma, Lojistik'in sürümlü portu üzerinden yapılır (Satış Lojistik tablosuna erişmez). SAT-D351 ve SAT-D348 ile ilişkisi SINAN-CEVAP 42.16 ile cevaplandı (EK20 SAT-D406, #1186). | Satış SAT-D313 kalan hesabı, Lojistik okuma portu |
| SAT-D354 | Kesin sevkin riske girmesi (plan S0(c)) | Onaylı irsaliyesi olmayan **kesin sevk**, müşteri riskinde "faturalanmamış sevk" tutarına sayılır (kesin sevk miktarı × fiyat). | Satış risk okuması, Finans kredi riski |
| FNS-D54 | Kısmi iadede taksit sırası | Birden çok açık taksitte kısmi müşteri iadesi, **vadesi en yakın açık taksitten** başlayarak düşülür; artan tutar sıradaki vadeye geçer. `finance_customer_return_installment_order_not_decided` reddi bu kuralla yer değiştirir. FNS-D43/FNS-D44 ile çelişkisi SINAN-CEVAP 42.14 ile cevaplandı (EK20 FNS-D81, #1186). | Finans FIN-S10 (iade kabulü) |
| FNS-D55 | Masraf ödemesinin para profili | Ayarlar para profili kataloğuna **masraf ödemesi için ayrı satır** eklenir; değerleri manuel banka ödemesiyle aynıdır (GDC/GSP/GIN şirket profilleri ayrı, TRY 2 ondalık). Geçici `finance.manual-bank-payment.v1` bağlaması bu satıra taşınır; saklı satırlar profil kimliği ve sürümünü taşıdığından geçmiş kayıtlar değişmez. Anahtar adı uygulama ayrıntısıdır (geri alınabilir). | Ayarlar para kataloğu, Finans `ExpensePaymentBinding` |
| FNS-D56 | Kredi limitinde ikinci onay (OPEN-009) | **Şimdilik tek onay**: yetkili kişi kredi limitini tek adımda belirler veya kaldırır; her değişiklik denetim kaydına girer. Eşik ve ikinci onay sonra ayrı kararla eklenebilir. Bu karar OPEN-009'un yalnız kredi limiti onay adımına cevap verir; OPEN-009 (tutar/rol/para/onay matrisi) kapanmaz ve `OPEN-*` kapatma insan kapısıdır. FNS-D45 ile ilişkisi: "Önceki kararlarla ilişki". | Finans FIN-S1/FIN-S15 kredi limiti |
| KLN-D56 | Çok bağlantılı belge erişimi (B-26, DEC-DOC-002) | Görme: bağlı kayıtlardan **en az birini** görebilen dosyayı görür. Yeni sürüm: **tüm** bağlı kayıtlar görülebilmeli. Bağlama: kaynak dosya en az bir bağlantısından görülebilmeli ve hedef kayıt görülebilmeli. İlk bağlantı birincildir. | Belgeler PLT-S16 |
| ALS-D98 | Cari resmi unvan değişikliği (CARI-DEC-004) | Unvan değişikliği **tek kişiyle, mevcut cari düzenleme yetkisiyle** yapılır; onay adımı ve yeni yetki yoktur. Değişiklik eski ve yeni değerle denetim kaydına girer; geçmiş belgelerdeki unvan değişmez. ALS-D95 ve 19.22 (ayrı izin) ile çelişkisi SINAN-CEVAP 42.15 ve 44.2 ile cevaplandı (EK20 ALS-D108 ve ALS-D110, #1186). | Cari (Counterparty) |
| URN-D10 | Ebatların kapsamı (BS-9) | Ürün kartındaki ebatlar **şirketler arası ortaktır, tek satırdır**; yetkisi olan herhangi bir şirketten düzenlenir ve her değişiklik denetim kaydına girer. | Ürün ebat paneli |
| URN-D11 | Kesit türleri (BS-9) | Kesit türleri **şimdilik dört**: Sac (kalınlık/en/boy), Rulo (kalınlık/en), Yuvarlak boru (dış çap/et kalınlığı, kg/m isteğe bağlı), Profil (en/yükseklik/et kalınlığı, kg/m zorunlu; KLN-D54 tablosu). Yeni tür ayrı dilimle eklenir. "Şimdilik dört" sınırı sonradan KLN-D87 ile kalkmıştır ("Önceki kararlarla ilişki"). | Ürün STK-S37 |

## Önceki kararlarla ilişki

Bu bölüm yeni politika seçmez; yalnız bu belgedeki kararların önceki ve sonraki kararlarla ilişkisini yazar. Bazı
kaynaklar henüz merge edilmemiş karar PR'larındadır; onlara o PR'daki metinle atıf yapılır.

| Bu belgedeki karar | Önceki karar | İlişki | Durum |
|---|---|---|---|
| KLN-D55 | SAT-D350 (SINAN-CEVAP 19) | Tekrar: aynı kural (kg ağırlıklı, okunamazsa "okunamaz", stoksuz depo girmez). | SAT-D350'yi teyit eder; iki kimlik aynı kuraldır, numara değiştirilmez. Ek olarak "hiç kaynak yoksa 'kaynak yok'" ve depo ortalamasının KLN-D53 olduğu yazılır. Çelişki yok. |
| SAT-D354 | SAT-D352 (SINAN-CEVAP 19) | Tekrar: onaylı irsaliyesi olmayan kesin sevk riske sayılır. | SAT-D352'yi teyit eder. Çelişki yok. |
| KLN-D56 | KLN-D49 (SINAN-CEVAP 19) | Tekrar: çok bağlantılı belge erişimi (görme, yeni sürüm, bağlama, ilk bağlantı birincil). | KLN-D49'u teyit eder; KLN-D49'daki DEC-DOC-002 ve şirket sınırı notları aynen geçerlidir. Çelişki yok. |
| URN-D10 | KLN-D47 (SINAN-CEVAP 19) | Tekrar: ebatlar ortak, tek satır, denetimli. | KLN-D47'yi teyit eder. Çelişki yok. |
| FNS-D55 | FNS-D48 (SINAN-CEVAP 19) | Tekrar: masraf ödemesi ayrı para profili satırı. | FNS-D48'i teyit eder; anahtar adı FNS-D48'de `finance.expense-payment.v1` olarak yazılıdır. Çelişki yok. |
| FNS-D54 | FNS-D43 (SINAN-CEVAP 17), FNS-D44 (SINAN-CEVAP 19.5/19.6) | Çelişki: FNS-D43/FNS-D44'te taksiti kullanıcı seçer, ekran en yakın vadeden önceden doldurur; FNS-D54 sırayı sistemin otomatik uygulamasını söyler. | SINAN-CEVAP 42.14 ile çözüldü (EK20 FNS-D81, #1186). |
| ALS-D98 | ALS-D95 (SINAN-CEVAP 17), 19.21/19.22 (SINAN-CEVAP 19), ALS-D97 | Çelişki: ALS-D95 ve 19.22 resmi unvan için ayrı izin açar ("Yeni izin, tek adım"); ALS-D98 mevcut cari düzenleme yetkisini yeterli sayar. Onaysız, tek adım ve denetim iki tarafta aynıdır. | SINAN-CEVAP 42.15 ve 44.2 ile cevaplandı (EK20 ALS-D108 ve ALS-D110, #1186). |
| FNS-D56 | FNS-D45 (SINAN-CEVAP 19) | Çelişki: FNS-D45 öneren ≠ onaylayan; FNS-D56 tek adım. | Sonraki turda çözüldü: SINAN-CEVAP 25.13 "İkisi de tek adım" ve FNS-D57 (3) (`SINAN_KARARLARI_2026-11-15_DALGA2_EK4.md`, #1168): FNS-D56 geçerlidir, FNS-D45'teki kredi limiti akışı uygulanmaz. OPEN-009 her iki durumda da açık kalır. |
| SAT-D353 | SAT-D351 (SINAN-CEVAP 19.2), SAT-D348 (SINAN-CEVAP 16.2) | Çelişki: SAT-D351'de kalan şimdilik "onaylı − kesin sevk"tir ve plan bazlı kalan ile plan üstü sevk ayrıca sorulacaktır; SAT-D348'de plan üstü sevk uyarı verir ve onaya düşer; SAT-D353 plan miktarını yükleme talimatı payı sayar ve plan üstü sevki engeller. | Kısmen sonraki turda: SAT-D367 (SINAN-CEVAP 33.15, `SINAN_KARARLARI_2026-11-15_DALGA2_EK12.md`, #1176) plan miktarının kaynağını "onaylanmış, henüz sevk edilmemiş açık sevk talimatları" olarak seçer. Plan bazlı kalanın ne zaman SAT-D351'in yerine geçeceği ve plan üstü sevkin engellenmesi mi yoksa uyarı + onay mı olacağı çözülmedi. Kalanı SINAN-CEVAP 42.16 ile çözüldü (EK20 SAT-D406, #1186). |
| URN-D11 | KLN-D48 (SINAN-CEVAP 19) | Çelişki: KLN-D48 yeni aileler ekler; URN-D11 "şimdilik dört" der. | Sonraki turlarda çözüldü: önce EK6 (#1170, merge edildi) URN-D11'i geçerli saydı; sonra KLN-D87 (SINAN-CEVAP 33.18, #1176) KLN-D48'deki bütün aileleri ilk dilime aldı ve EK6'daki satırın yerine geçti. URN-D11'deki dört türün ölçü alanları aynen geçerlidir. |
| (Giriş, BS-5 notu) | KLN-D46 (SINAN-CEVAP 18, #1161) | Bu turdaki "cevap gelmedi" notu, aynı gün verilmiş KLN-D46 cevabını görmüyordu. | Çözüldü: KLN-D46 geçerlidir; KLN-D63 (SINAN-CEVAP 27.1, EK6, #1170, merge edildi) ve KLN-D86 (#1176) bunu teyit eder. BS-5 mirası açık değildir. |

## Sınırlar

- Açık kalanlar (NEEDS-SINAN):
  - yeni yetkilerin hangi rol/persona'ya verileceği (`product.dimension.manage`, `documents.files.link`,
    `settings.sales-delivery-tolerances.manage`, `settings.sales-approval-thresholds.manage`). URT-D66 (5)'e göre
    ayrı termin izni yoktur; bu listeye girmez;
  - satış politikası baseline aktarımının üretimde şirket başına sırası ve yetkilisi.
- SINAN-CEVAP 42 ve 44 ile cevaplananlar (`SINAN_KARARLARI_2026-11-16_DALGA4_EK20.md`, #1186): FNS-D54 ↔
  FNS-D43/FNS-D44 (42.14, FNS-D81), ALS-D98 ↔ ALS-D95/19.22 (42.15 ve 44.2, ALS-D108 ve ALS-D110), SAT-D353 ↔
  SAT-D351/SAT-D348 (42.16, SAT-D406).
- BS-5 mirası açık değildir (KLN-D46, KLN-D63). OPEN-009 açık kalır.
- Bu belge yetki vermez. İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme,
  grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` her zaman somut kullanıcı
  kararı ister.
