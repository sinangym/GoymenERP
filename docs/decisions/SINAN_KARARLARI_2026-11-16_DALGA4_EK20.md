# Sinan Kararları — 2026-11-16 (Dalga 4 ek 20: açık NEEDS-SINAN maddelerinin cevapları — masraf türü, sipariş kuru, listeler, EN 10255 etiketi, ayrım bırakma uyarısı, termin izni, saklama, fason yuvarlama, döviz maliyeti, kantar kaydı, kuyruk önceliği, kısmi iade, resmi unvan, sevk planı, evraklı risk, hurda fiyatı, pasife alma uyarısı, USD kuru)

Bu belge önceki karar dosyalarını silmez. Şu kararları ekler: ALS-D107…ALS-D110, FNS-D80…FNS-D83, KIM-D51, URN-D24,
KLN-D110, SAT-D400…SAT-D406 ve URT-D103…URT-D105. Her karar, tamamladığı veya güncellediği önceki kimliği açıkça
yazar. Burada anılmayan önceki kararlar aynen geçerlidir.

Atıf yapılan kimliklerin bir kısmı henüz merge edilmemiş karar PR'larındadır. Bu belge onlara o PR'lardaki metinle atıf
yapar:

| Kimlik | Dosya | PR |
|---|---|---|
| SAT-D353, FNS-D54, ALS-D98 | `SINAN_KARARLARI_2026-11-15_DALGA2_SORULARI.md` | #1164 |
| FNS-D58, URT-D72, URT-D74 | `SINAN_KARARLARI_2026-11-15_DALGA2_EK4.md` | #1168 |
| FNS-D73 | `SINAN_KARARLARI_2026-11-15_DALGA2_EK12.md` | #1176 |
| URN-D17 (3) | `SINAN_KARARLARI_2026-11-15_DALGA2_EK7.md` | #1171 |
| ALS-D99 / ALS-D103 (düşürüldü), URT-D82 | `SINAN_KARARLARI_2026-11-15_DALGA2_EK11.md` | #1175 |
| KLN-D94 | `SINAN_KARARLARI_2026-11-15_DALGA2_EK13.md` | #1177 |
| SAT-D377, SAT-D385, SAT-D386, SAT-D389, URT-D97 | `SINAN_KARARLARI_2026-11-16_DALGA4_EK15.md` | #1179 |
| SAT-D397, URT-D101 | `SINAN_KARARLARI_2026-11-16_DALGA4_EK16.md` | #1180 |
| KIM-D49, FNS-D79, ALC-S8 sorusu | `SINAN_KARARLARI_2026-11-16_DALGA4_EK18.md` | #1182 |
| SAT-D398 | `SINAN_KARARLARI_2026-11-16_DALGA4_EK19.md` | #1185 |

Bu belge o PR'lardan sonra merge edilir. Çelişkide daha yeni olan bu belge geçerlidir.

**Kaynak:** Sinan, 16 Kasım 2026, Claude Code oturumunda soru penceresiyle cevapladı: SINAN-CEVAP 42 (Issue #66 yorumu
6911336951, 16 satır) SINAN-CEVAP 43 (Issue #66 yorumu 6911392211, 4 satır) ve SINAN-CEVAP 44 (Issue #66 yorumu 6911461584, 2 satır). Sorular, açık karar PR'larındaki
`NEEDS-SINAN` maddelerinden geldi. "(Önerilen)" işaretleri soranındır. Serbest metin cevaplar aynen alıntılanmıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 42.1 | ALC-S8 masraf türü: masraf türü seçimi zorunlu mu? | Serbest metin: "Şuan için tek bir masraf türü var genel açıklamaya yazılacak, ama tür seçimi eklenince zorunlu olacak" | Seçenek seçilmedi; cevap serbest metindir. |
| 42.2 | FNS-D79 kur: sipariş kuru ne zaman sabitlenir? | **Onaydan sonra kilitli (Önerilen)** | "Kur onayda sabitlenir; değişiklik sipariş revizyonu (yeniden onay) ister; kur zorunlu; kur bulunamazsa sipariş onaya gönderilemez." |
| 42.3 | KIM-D49 listeler: listeleri kim düzenler? | **Satınalma Müdürü + Admin (Önerilen)** | "Satınalma müdürü ve sistem yöneticisi denetimli düzenler; yeni izin adı gerekirse grant insan kapısında kalır." |
| 42.4 | EN 10255 etiketi: kg/m formül etiketi | **Kendi standardıyla (Önerilen)** | "Etiket: "Formülle hesaplandı (EN 10255, 7,85)"." |
| 42.5 | KLN-D94 uyarı kanalı: etiket bağı kurulamazsa uyarı hangi kanalla? | **Uygulama içi bildirim (Önerilen)** | "ERP içi bildirim + satış satırında "ayrım bırakıldı" işareti." Seçilmeyen: "Uygulama içi + e-posta" (sağlayıcı/secret insan kapısı). |
| 42.6 | KLN-D94 kapsam: "Kalan ayrımın tamamı" satırdaki diğer Aktif ayrımları da kapsar mı? | **Evet, satırdaki tüm Aktif ayrımlar (Önerilen)** | "Her ilgili satış satırı sorumlusu ayrı uyarılır." Seçilmeyen: "Yalnız ilgili ayrım". |
| 42.7 | SAT-D398 termin izni: `sales.orders.delivery-schedule.write` ne olsun? | **İkisi de yeterli (Önerilen)** | "Sipariş düzenleme izni VEYA mevcut termin izni yeter; izin silinmez." Seçilmeyenler: "Termin izni kaldırılsın"; "Yalnız termin izni". |
| 42.8 | SAT-D397 kapsam (37.10 yeniden): ambalaj standardı Excel içe aktarma dosyası ve sonuç raporu için "Açık kalsın, silme yok" geçerli mi? | **Evet, bunlar için de (Önerilen)** | "Süre seçilmez, silme yok, imha kapalı." Seçilmeyenler: "10 yıl + imha onayı"; "5 yıl + imha onayı". |
| 42.9 | SAT-D377 sıra: ortak tartıda paylaştırma/yuvarlama sırası | **Önce paylaştır, satırda yuvarla (Önerilen)** | "Ham değerle paylaştırılır, satır tam kg'a yuvarlanır, kalan son satıra." Seçilmeyen: "Önce toplamı yuvarla". |
| 42.10 | SAT-D386 iskonto: belge altı iskontosu da düşülsün mü? | Serbest metin: "İskonto hesaplanmış fiyat olmalı"; netleştirme: **Evet, tüm iskontolar (Önerilen)** | "Satır + belge altı iskontosu (satır tutarına oranla dağıtılarak) düşülmüş net birim fiyat." Seçilmeyen: "Yalnız satır iskontosu". |
| 42.11 | SAT-D385 / BS-1: fark hareketi ne zaman doğar? | **BS-1 kalır (Önerilen)** | "Fark yalnız fatura geldiğinde ortalamaya tek kez katılır; öncesinde hareket yok." Seçilmeyen: "BS-1 değişsin". |
| 42.12 | SAT-D389 / SAT-D322(1): kantarı fazla, stoğu yeten satış | **Kayıt var, süreç yok (Önerilen)** | "Sonuç kaydı tutulur, kantar farkı süreci başlamaz; SAT-D322(1) kalır." Seçilmeyen: "Süreç de başlasın". |
| 42.13 | URT-D97 öncelik puanı: kim girer? | **Plan onay izni olan (Önerilen)** | "Mevcut `manufacturing.plans.approve`; yeni izin yok." Seçilmeyenler: "Plan yazma izni"; "Yeni izin". |
| 42.14 | FNS-D54 kısmi iade: hangi taksitten düşülür? | **Kullanıcı seçer (Önerilen)** | "FNS-D44 kalır; ekran en yakın vadeyi önceden doldurur." Seçilmeyen: "Otomatik en yakın vade". |
| 42.15 | ALS-D98 resmi unvan: cari resmi unvan değişikliği kimde? | Serbest metin: "Muahsebe müdürü, genel müdür" | Seçenek seçilmedi. Seçilmeyenler: "Ayrı unvan izni (Önerilen)"; "Mevcut cari düzenleme izni". |
| 42.16 | SAT-D353 sevk planı: kalan ve plan üstü sevk | **Plana göre, üstü onaya (Önerilen)** | "Kalan plana göre (SAT-D367); plan üstü sevk uyarır ve onaya gider (SAT-D348)." Seçilmeyenler: "Plana göre, üstü engel"; "Onaylı eksi sevk". |
| 43.1 | FNS-D58 evraklı risk: bankaya tahsile/teminata verilmiş ama henüz tahsil edilmemiş müşteri çeki/senedi riske sayılsın mı? | **Evet, tahsil edilene kadar (Önerilen)** | "Durumu "tahsil edildi" veya "iade" olmayan her evrak riske sayılır; bankadaki evrak da." Seçilmeyen: "Hayır, bankadaki sayılmaz" (bankaya verilen evrak riskten çıkar). |
| 43.2 | URT-D72 (4) / URT-D74 (2) hurda fiyatı: yazar okumaları teyit edilsin mi? | **İkisi de teyit (Önerilen)** | "Düzeltmeyi fiyatı girme yetkisi olan yapar (URT-D57); hesaplayıcı da 0/negatif fiyatla maliyeti kesinleştirmez, fiyatsız ay beklemede kalır." Seçilmeyen: "Düzeltme ayrı yetkiyle" (düzeltme için ayrı üst yetki; sıfır fiyat kuralı teyit). |
| 43.3 | ALS-D99 pasife alma uyarısı: uyarıda hangi açık belgeler sayılsın? | **32.14 listesi kullanılsın (Önerilen)** | "32.14'te sayılan açık belge türleri uyarıda gösterilir; engellemez, gerekçe ister." Seçilmeyen: "Yalnız açık bakiye". |
| 43.4 | URT-D82 USD gösterim kuru: hangi TCMB kuru? | **Döviz satış (Önerilen)** | "TCMB günlük döviz satış kuru." Seçilmeyenler: "Döviz alış"; "Efektif satış". |
| 44.1 | FNS-D73 "siparişte belirtilen kur" | **Siparişteki tür+tarih (Önerilen)** | "Siparişte yazan kur türü ve kur tarihi kullanılır; siparişte yoksa sipariş tarihindeki TCMB döviz satış kuru (43.4 ile aynı tür)." Seçilmeyenler: "Siparişteki kur değeri" (sabit rakam; tür/tarih aranmaz); "Yalnız kur tarihi" (tür her zaman TCMB döviz satış). |
| 44.2 | ALS-D98 izin mekanizması (personalar: Muhasebe müdürü, genel müdür) | **Mevcut onay izni** | "Alış tarafındaki mevcut onay izni kullanılır; o izne sahip herkes bu işlemi de yapabilir." Seçilmeyen: "Ayrı izin adı (Önerilen)" (yeni izin adı, yalnız bu iki personaya). Grant ataması insanda kalır. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| ALS-D107 | Ek alış masraf türü (42.1; EK18'deki ALC-S8 `NEEDS-SINAN` sorusunu cevaplar, KIM-D49'u tamamlar) | Serbest metin cevap: "Şuan için tek bir masraf türü var genel açıklamaya yazılacak, ama tür seçimi eklenince zorunlu olacak". Bugün tek bir masraf türü vardır; ek alış masraf satırında masrafın ne olduğu satırın **genel açıklamasına** yazılır. Masraf tipi listesine (KIM-D49) tür eklendiğinde ek alış masraf satırında **tür seçimi zorunlu** olur. **Yorum (geri alınabilir):** listede tür yokken satır tür seçilmeden kaydedilir; listede en az bir aktif tür olduğunda tür seçilmeden kayıt reddedilir. Tür seçimi zorunlu olduktan sonra eski satırların geriye dönük tamamlanıp tamamlanmayacağı sorulmadı; eski satırlar değiştirilmez. Masraf tutarı ve dağıtımıyla ilgili önceki kararlar aynen geçerlidir. | Alış ek masraf satırı (ALC-S8), Ayarlar masraf tipi listesi (KIM-S18) |
| FNS-D80 | Dövizli alış siparişinde kurun kilitlenmesi (42.2; FNS-D79'u tamamlar) | (1) Dövizli alış siparişinde kur **zorunludur**. (2) Kur **onayda sabitlenir**; onaydan sonra kur ancak **sipariş revizyonuyla (yeniden onay)** değiştirilir. (3) Kur bulunamazsa (FNS-D79'daki varsayılan hesaplanamazsa ve elle girilmemişse) sipariş **onaya gönderilemez**. FNS-D79'un geri kalanı (otomatik varsayılan, onaydan önce hazırlayanın değiştirebilmesi, kurun sayı olarak yazılması) aynen geçerlidir. Hazırlayanın değişikliğinin gerekçe isteyip istemediği bu cevapta yoktur ve `NEEDS-SINAN` olarak kalır. | Alış siparişi (dövizli), onay akışı |
| KIM-D51 | Hizmet tipi ve masraf tipi listelerini düzenleyen personalar (42.3; KIM-D49'u tamamlar) | KIM-D49'daki hizmet tipi ve masraf tipi listelerine değer ekleme ve pasifleştirmeyi **Satınalma Müdürü** ve **Admin (Sistem Yöneticisi)** yapar. Her değişiklik denetime yazılır. Bu bir persona dağılımıdır; yeni izin adı gerekirse adı ve grant'i insan kapısında kalır. | Ayarlar hizmet/masraf tipi listeleri (KIM-S18) |
| URN-D24 | EN 10255 borularının kaynak etiketi (42.4; URN-D17 (3)'ü tamamlar) | EN 10219-2 kapsamında olmayan EN 10255 boruları için kg/m kaynak etiketi kendi standardıyla yazılır: **"Formülle hesaplandı (EN 10255, 7,85)"**. URN-D17 (3)'teki etiket biçimi EN 10219-2 kapsamındaki ürünler için aynen geçerlidir. | Ürün kg/m kaynak etiketi, KGM başlangıç tablosu |
| KLN-D110 | Ayrım bırakıldığında uyarı kanalı ve kapsamı (42.5, 42.6; KLN-D94'ü tamamlar) | (1) KLN-D94'te mal kabulde etiket bağı kurulamayıp ayrım bırakıldığında uyarı **uygulama içi bildirimle** verilir; satış satırında **"ayrım bırakıldı"** işareti gösterilir. E-posta kanalı açılmaz. (2) "Kalan ayrımın tamamı", o alış satırındaki **tüm Aktif ayrımları** kapsar; her ilgili satış satırının sorumlusu **ayrı ayrı** uyarılır. KLN-D94'ün geri kalanı (mal kabul geri alınmaz, bırakma denetime yazılır, yeniden ayırma yapılabilir) aynen geçerlidir. **Yorum (geri alınabilir):** uyarının alıcısı KLN-D94'teki gibi satış satırının sorumlusudur; alış sorumlusunun da uyarılması sorulmadı. | Lojistik mal kabul, Satış ayrım (tahsis) kaydı, bildirim |
| SAT-D400 | Termin değiştirme izni (42.7; SAT-D398'i tamamlar, EK19'daki `NEEDS-SINAN`'ı cevaplar) | Sipariş terminini **sipariş düzenleme izni olan VEYA mevcut `sales.orders.delivery-schedule.write` iznine sahip olan** kullanıcı değiştirebilir. `sales.orders.delivery-schedule.write` **silinmez**. SAT-D398 gibi bu karar da DEC-SAL-003'teki (`KARAR_SICILI.md`, KİLİTLİ) "planlama/sevkiyat rolü" hükmünü günceller; DEC-SAL-003'ün geri kalanı aynen geçerlidir. SAT-D398'deki "termin değişikliği onayı düşürmez, denetime yazılır" kuralı aynen geçerlidir. Yeni izin adı açılmaz; grant ataması insan kapısındadır. | Satış siparişi termin değişikliği, izin kontrolü |
| SAT-D401 | SAT-D397'nin kapsamının teyidi (42.8; SAT-D397'yi teyit eder) | SAT-D397'deki yazar okuması teyit edilmiştir: "Açık kalsın, silme yok" cevabı **ambalaj standardı Excel içe aktarma dosyası ve sonuç raporu** için geçerlidir. Saklama süresi seçilmez, bu dosyalar silinmez ve imha işlevi kapalı kalır. Saklama politikası ayrı kararla belirlenir; hiçbir `OPEN-*` kapanmaz. | Stok ambalaj içe aktarımı, dosya deposu (SAT-D383 dosyaları) |
| SAT-D402 | Ortak tartıda paylaştırma ve yuvarlama sırası (42.9; SAT-D377'yi tamamlar) | Ortak tartı birden çok fason satırına hizmet ediyorsa net kg önce **ham değerlerle** satırlara paylaştırılır (SAT-D376); sonra her satır **tam kg'a yuvarlanır** (SAT-D377, AwayFromZero). Yuvarlama farkı (kalan) **son satıra** yazılır; satırların toplamı yuvarlanmış toplamla aynı kalır. SAT-D377'deki fail-closed bekleyiş bu kararla kalkar. **Yorum (geri alınabilir):** "son satır" satırların belgedeki sırasına göre belirlenir; toplamın yuvarlanması SAT-D377'deki gibi AwayFromZero'dur. | Satış fason fatura hesaplayıcısı |
| SAT-D403 | Ortalamaya giren döviz birim maliyetinde iskonto (42.10; SAT-D386'yı tamamlar) | Serbest metin cevap: "İskonto hesaplanmış fiyat olmalı"; netleştirme: tüm iskontolar. Ortalamaya giren döviz birim maliyeti, **satır iskontosu ve belge altı iskontosu** düşülmüş, KDV hariç **net birim fiyattır** (NetUnitPrice). Belge altı iskontosu satırlara **satır tutarına oranla** dağıtılır. SAT-D386'daki alan seçimi bu kararla yapılmıştır. Dağıtımda yuvarlama ve kalan farkın yeri bu cevapta seçilmedi; deterministic backend calculator'ın formül golden'ında belirlenir ve belirlenene kadar `NEEDS-SINAN` olarak kalır. | Stok/Alış maliyet çözücüsü |
| SAT-D404 | Geç gelen dövizli alış faturası ve BS-1 (42.11; SAT-D385'i tamamlar) | **BS-1 aynen kalır.** Fatura gelene kadar TL maliyet `Unavailable`'dır; geçici kur ve düzeltme hareketi yoktur. Fark yalnız **fatura geldiğinde**, o andaki ortalamaya **tek kez** katılır (SAT-D385). Faturadan önce hareket üretilmez. | Stok ortalama maliyet defteri |
| SAT-D405 | Kantarı fazla çıkan, stoğu yeten satış (42.12; SAT-D389'u tamamlar, SAT-D322 (1)'i teyit eder) | SAT-D389'daki yazar okuması teyit edilmiştir: kantarı fazla çıkan ve fiziksel stoğu yeten satışta **sonuç kaydı tutulur**, ancak SAT-D319 (4)–(6) **kantar farkı süreci başlamaz**. SAT-D322 (1) aynen geçerlidir. SAT-D389'daki "kaydı olmayan irsaliye faturalanamaz" kuralı aynen geçerlidir. | Lojistik kantar farkı deposu, Satış fatura ön koşulu |
| URT-D103 | Kuyruk öncelik puanını giren izin (42.13; URT-D97'yi tamamlar) | URT-D97'deki öncelik puanını **mevcut `manufacturing.plans.approve` iznine** sahip kullanıcı girer. Yeni izin açılmaz. URT-D97'deki "karar gelene kadar bütün planlar varsayılan puanla sıralanır" bekleyişi bu kararla kalkar. Elle sıra taşımanın izni bu cevapta yoktur; EK16'daki URT-D101 (#1180) ile cevaplanmıştır: elle sıra taşıma mevcut `manufacturing.plans.approve` iznine bağlanır ve her taşıma denetime yazılır. | Üretim kuyruk/kapasite hesaplayıcısı, plan kaydı |
| FNS-D81 | Kısmi iadede taksit (42.14; FNS-D54 ile FNS-D44 arasındaki çelişkiyi çözer) | **FNS-D44 geçerlidir:** kısmi iadede taksiti **kullanıcı seçer**; ekran vadesi en yakın açık taksiti önceden doldurur. FNS-D54'teki otomatik sıra **uygulanmaz**. | Finans FIN-S10 (iade kabulü) |
| ALS-D108 | Cari resmi unvan değişikliğini yapan personalar (42.15; ALS-D98 ile ALS-D95/19.22 arasındaki çelişkiye cevap verir) | Serbest metin cevap: "Muahsebe müdürü, genel müdür". Cari resmi unvan değişikliğini **Muhasebe Müdürü** ve **Genel Müdür** personaları yapar. ALS-D98'deki "mevcut cari düzenleme yetkisiyle" kısmı bu kararla geçerli olmaz; onaysız, tek adım ve denetim kaydı (ALS-D95/ALS-D98) aynen geçerlidir. Yetkinin mekanizmasını ALS-D110 (44.2) belirler. Grant insan kapısındadır. | Cari (Counterparty) resmi unvan |
| SAT-D406 | Sevk planına göre kalan ve plan üstü sevk (42.16; SAT-D353'ü günceller, SAT-D351'in yerini alır) | (1) Siparişin kalan miktarı **sevk planına göre** hesaplanır; plan miktarı SAT-D367'deki gibi onaylanmış, henüz sevk edilmemiş açık sevk talimatlarıdır. Bu, SAT-D351'deki "onaylı − kesin sevk" geçici kalanının yerini alır. (2) Plan üstü sevk **engellenmez**; SAT-D348'deki gibi **uyarır ve onaya gider**. SAT-D353'teki "plan üstü sevk engellenir" kısmı uygulanmaz. Okuma Lojistik'in sürümlü portu üzerinden yapılır (SAT-D353). | Satış kalan hesabı (SAT-D313), sevk onayı, Lojistik okuma portu |
| FNS-D82 | Bankadaki evrakın evraklı riske sayılması (43.1; FNS-D58 (2)'yi tamamlar) | FNS-D58 (2)'deki yazar okuması teyit edilmiştir: bankaya tahsile veya teminata verilmiş ama henüz tahsil edilmemiş müşteri çeki/senedi, durumu "tahsil edildi" veya "iade" olmadığı sürece **evraklı riske sayılır**. FNS-D58'in geri kalanı aynen geçerlidir. | Finans evraklı risk okuma ucu, Cari kartı risk paneli |
| URT-D104 | Hurda fiyatı düzeltme yetkisi ve sıfır fiyat (43.2; URT-D72 (4) ve URT-D74 (2)'yi teyit eder) | (1) URT-D72 (4) teyit edilmiştir: aylık hurda fiyatının düzeltmesini **fiyatı girme yetkisi olan** yapar (URT-D57); ayrı yetki açılmaz. (2) URT-D74 (2) teyit edilmiştir: hesaplayıcı **0 veya negatif hurda fiyatıyla maliyeti kesinleştirmez**; fiyatsız ay beklemede kalır (URT-D47 (2), URT-D65). | Aylık hurda fiyatı, `ProductionReceiptCostCalculatorV1` girdi doğrulaması |
| ALS-D109 | Pasife alma uyarısında sayılan açık belgeler (43.3; ALS-D99'u tamamlar, EK11'deki ALS-D103 notundaki `NEEDS-SINAN`'ı cevaplar) | Cari pasife alınırken ALS-D99'daki uyarıda **32.14'te sayılan açık belge türleri** gösterilir: taslak dışı satış/alış siparişi, sevk/mal kabulü bitmemiş irsaliye ve kesilmemiş fatura. Uyarı **engellemez** ve **gerekçe ister** (ALS-D99 (1)). Düşürülmüş ALS-D103 geri gelmez: taslakların pasifleşmede iptali uygulanmaz. | Cari pasifleştirme (ALC-F1), Satış ve Alış açık belge kaynakları |
| FNS-D83 | Maliyet çevriminde "siparişte belirtilen kur" (44.1; FNS-D73'ü netleştirir, EK12'deki tür/tarih `NEEDS-SINAN`'ını cevaplar) | FNS-D73'teki "siparişte belirtilen kur", siparişte yazan **kur türü ve kur tarihidir**; çevrim bu tür ve tarihteki kurla yapılır. Siparişte kur türü veya tarihi yoksa **sipariş tarihindeki TCMB döviz satış kuru** kullanılır (URT-D105 ile aynı tür). FNS-D73'ün geri kalanı (fatura kuru bulunan girişte SAT-D295/BS-1) aynen geçerlidir. **Yorum (geri alınabilir):** siparişte yalnız biri (tür veya tarih) yazılıysa eksik olan bu varsayılandan tamamlanır (tür TCMB döviz satış, tarih sipariş tarihi). Siparişe hiç bağlanamayan çevrim (ör. siparişsiz giriş) bu cevapta yoktur; FNS-D73'teki gibi "hesaplanamadı" ile reddedilir ve `NEEDS-SINAN` olarak kalır. | Alış/Satış sipariş kuru okuma portları, Finans kur çevirici, üretim döviz değerlemesi (URT-D82) |
| ALS-D110 | Resmi unvan değişikliğinin izin mekanizması (44.2; ALS-D98'i ve ALS-D108'i netleştirir) | Cari resmi unvan değişikliği **alış tarafındaki mevcut onay izniyle** yapılır; o izne sahip herkes bu işlemi de yapabilir. **Yeni izin adı açılmaz**; ALS-D95/19.22'deki ayrı izin bu işlem için açılmaz. Muhasebe Müdürü ve Genel Müdür (ALS-D108) bu izinle işlemi yapar; iznin bu personalara grant'i insan kapısındadır. Onaysız, tek adım ve denetim kaydı aynen geçerlidir. Alış tarafında iki mevcut onay izni vardır (`purchasing.orders.approve`, `purchasing.requests.approve`); cevap hangisi olduğunu söylemez. Hangisinin kullanılacağı `NEEDS-SINAN`'dır; karar gelene kadar bu izin kontrolü etkinleştirilmez ve kod bugünkü davranışla devam eder. | Cari (Counterparty) resmi unvan, izin kontrolü |
| URT-D105 | Üretimde USD gösterim kuru (43.4; URT-D82'yi tamamlar) | URT-D82'deki TCMB kuru **TCMB günlük döviz satış kurudur**. URT-D82'nin geri kalanı (TL ana maliyet, USD yalnız üretim maliyetinde gösterim, kur okunamazsa "hesaplanamadı") aynen geçerlidir. OPEN-008 açık kalır. | Stok döviz ortalaması, üretim maliyeti gösterimi |

## Sınırlar

- Bu belge yeni izin adı açmaz, rol açmaz ve grant ataması yapmaz. KIM-D51 ve ALS-D108 persona dağılımıdır; ALS-D110 mevcut
  bir izni kullanır. Yeni izin adı gerekirse adı ve grant'i insan kapısındadır.
- Açık kalan `NEEDS-SINAN` maddeleri:
  - FNS-D80: hazırlayanın kur değişikliğinin gerekçe isteyip istemediği.
  - SAT-D403: belge altı iskonto dağıtımında yuvarlama ve kalan farkın yeri.
  - ALS-D110: alış tarafındaki iki mevcut onay izninden hangisinin kullanılacağı.
  - FNS-D83: siparişe hiç bağlanamayan çevrim (FNS-D73'teki gibi).
- SINAN-CEVAP 43 tolerans ve iskonto tavanı değerlerini ve teminat izinlerinin Critical sınıfını seçmedi. Bunlar
  `NEEDS-SINAN` olarak ve insan kapısında kalır.
- E-posta bildirim kanalı açılmaz; sağlayıcı ve secret insan kapısındadır.
- Saklama süresi ve imha politikası seçilmez.
- `OPEN-*` kararları bu belgeyle kapanmaz; OPEN-008 açık kalır.
- İnsan kapıları değişmez:
  - deployment, environment ve secret;
  - Settings → Actions etkinleştirmesi;
  - grant/rol/Critical;
  - production aktivasyonu;
  - `OPEN-*` kapatma;
  - `.claude/settings.json`.
