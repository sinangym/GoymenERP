# Sinan Kararları — 2026-11-02 (Satış: sipariş vergisi — satır vergisi seçimi, tevkifatın uygulanacağı alıcı, risk tutarı, eski siparişler)

Bu belge önceki karar dosyalarını silmez; SAT-D180…SAT-D184'ü ekler. PR #885'in (siparişe vergi: alıcı durumu, satır vergi
anlık görüntüsü, toplamlar) yazarının sorduğu üç soruyu cevaplar. SAT-D73…D76, SAT-D82, SAT-D93, SAT-D97, SAT-D133…D135,
SAT-D139, SAT-D140, SAT-D145…D148 ve SAT-D158 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Risk tutarı:** SAT-D46 risk kalemlerini "KDV dahil" tutarla alıyordu. SAT-D183'e göre tevkifatlı siparişte riske
  müşterinin bize **ödeyeceği** tutar girer: KDV dahil tutardan tevkif edilen KDV düşülür. Tevkifatsız siparişte iki tutar
  aynıdır, SAT-D46 aynen kalır.
- **Satır vergisi seçimi:** SAT-D93/D97'deki satır vergisi değişikliği artık Ayarlar'daki KDV oranı ve tevkifat oranı
  listelerinden de seçim yapabilir (SAT-D181). Elle oran yazılmaz.
- **Müşavir onayının kapsamı:** SAT-D148'de müşavir onayı istisna/tevkifat kodları ve VUK 232 eşiği içindi. SAT-D181 ile
  KDV oranı ve tevkifat oranı listeleri de müşavir onayıyla güncellenir (soru 4).

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
seçmeli soru cevapları: üç tur, yedi soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi. Birinci turdaki
1. soruya Sinan kendi metniyle cevap verip görüş sordu ("sence nasıl olur"); ikinci turdaki 4. soru bu metne verilen öneriyi
sordu ve Sinan öneriyi seçti. Üçüncü tur (soru 6 ve 7), docs review'unun SAT-D139 ve SAT-D158 ile olası çelişki
bulguları üzerine soruldu. Sinan'ın metni aynen aktarılmıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Satışçı bir satırın vergisini gerekçeyle değiştirdiğinde ne seçebilsin? Bugün yalnız kalem tipi varsayılanı, alıcı durumu ve istisna kodu seçilebiliyor; başka KDV oranı (ör. %1, %10) ya da tevkifat kodu seçilemiyor. | Kendi metni: "Bütün ürün satışlarında %20 kdv ve 5/10 tevkifat varsayılan gelsin ama ellede değiştirilebilir olsun (%1-2-3-4-5-6-7-8-9-10-11-12-13-14-15-16-18-19-20 ) gibi sence nasıl olur" | — (seçenekler: "Ayarlar listesinden oran/kod (Önerilen)" — "Ayarlar'da KDV oranı listesi (ör. %1, %10, %20) ve tevkifat kodu listesi tutulur; satışçı yalnız bu listelerden gerekçeyle seçer. Elle oran yazılamaz."; "Şimdiki gibi kalsın"; "Yalnız Muhasebe değiştirsin") |
| 2 | Tevkifatlı bir siparişte risk kontrolüne hangi tutar girsin? Örnek: 100.000 TL + %20 KDV = 120.000 TL; 5/10 tevkifat varsa müşteri bize 110.000 TL öder, 10.000 TL KDV'yi kendisi vergi dairesine yatırır. | Ödenecek tutar (Önerilen) | "Riske 110.000 TL girer: müşterinin bize gerçekten borçlanacağı tutar." (seçilmeyen: "KDV dahil brüt" — "Riske 120.000 TL girer: tevkifat düşülmez (daha ihtiyatlı).") |
| 3 | Bu değişiklikten önce açılmış siparişlerde alıcı vergi durumu yok (canlı veri yok, yalnız deneme kayıtları). Ne yapılsın? | Otomatik 'Normal' olsun (Önerilen) | "Eski siparişlere alıcı durumu 'Normal' yazılır ve satır vergileri Ayarlar'dan çözülür." (seçilmeyen: "Boş kalsın") |
| 4 | Önerim şu: Ürün satışlarında varsayılan %20 KDV + 5/10 tevkifat olsun; bunu Ayarlar'da kalem tipinin varsayılanı olarak siz/müşavir girersiniz. Satışçı bir satırda gerekçe yazarak başka oran seçebilir, ama yalnız Ayarlar'daki listelerden: KDV oranları (%0, %1, %10, %20) ve tevkifat oranları (2/10 … 10/10). İhracat ve istisna belgeli alıcıda tevkifat zaten uygulanmaz (SAT-D147). Nasıl olsun? | Önerdiğin gibi (Önerilen) | "Varsayılan %20 + 5/10 Ayarlar'dan gelir. Satır değişikliği yalnız Ayarlar'daki yasal oran/tevkifat listelerinden, gerekçeyle yapılır. Listeyi müşavir onayıyla siz güncellersiniz." (seçilmeyenler: "%1–%20 arası her oran"; "Elle oran yazılsın") |
| 5 | 5/10 tevkifat hangi alıcılarda uygulansın? (Demir-çelikte tevkifat genelde KDV mükellefi alıcıya yapılan satışta uygulanır; nihai tüketiciye uygulanmaz.) | Müşavire göre Ayarlar (Önerilen) | "Hangi alıcı durumunda tevkifat uygulanacağını müşavir belirler ve Ayarlar'a girilir; sistem tahmin etmez." (seçilmeyen: "Normal alıcının hepsinde") |
| 6 | 'Bütün ürün satışlarında %20 KDV + 5/10 tevkifat' dedin. Daha önceki kararında hurda için 'KDV yok, tevkifat yok', hizmet için '%20 KDV, tevkifat yok' demiştin. Hangisi geçerli? | Yalnız demir için (Önerilen) | "%20 + 5/10 demir ürünleri içindir; hurda (KDV yok, tevkifat yok) ve hizmet (%20, tevkifat yok) varsayılanları önceki karardaki gibi kalır." (seçilmeyenler: "Hurda da %20 + 5/10"; "Hepsi %20 + 5/10") |
| 7 | Tevkifatın hangi alıcıda uygulanacağını müşavir belirleyecek. Müşavir Ayarlar'a girene kadar başlangıç değeri ne olsun? Bugün sistemde 'Normal alıcıda tevkifat uygulanır; nihai tüketici, tevkifata tabi olmayan, ihraç kayıtlı ve istisna belgeli alıcıda uygulanmaz' diye başlangıç değeri var. | Bugünkü başlangıç kalsın (Önerilen) | "Normal alıcıda tevkifat uygulanır, diğerlerinde uygulanmaz; müşavir isterse Ayarlar'dan değiştirir." (seçilmeyen: "Müşavir girene kadar bekle" — "Normal alıcıda tevkifat müşavir girene kadar 'hesaplanamıyor' görünür; diğerlerinde zaten uygulanmaz.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D180 | Demir satışında varsayılan vergi | **Demir** kalem tipindeki ürün satışlarında varsayılan **%20 KDV + 5/10 tevkifat** olur (soru 1, 4 ve 6). Bu, SAT-D139'daki demir varsayılanının aynısıdır; **hurda** (KDV yok, tevkifat yok) ve **hizmet** (%20 KDV, tevkifat yok) varsayılanları değişmez (soru 6). Değerler kodda sabitlenmez; Ayarlar'da kalem tipinin efektif tarihli varsayılanıdır (No 15, SAT-D82, SAT-D139). Ayarlar'da değer yokken vergi "hesaplanamıyor" kalır | Ayarlar vergi masterı, sipariş satırı vergisi |
| SAT-D181 | Satır vergisinin değiştirilmesi | Satışçı bir satırın vergisini **gerekçe yazarak** değiştirebilir, ama yalnız Ayarlar'daki listelerden seçer: **KDV oranı listesi** (%0, %1, %10, %20) ve **tevkifat oranı listesi** (2/10 … 10/10). Elle oran yazılmaz (soru 4). Listeler müşavir onayıyla güncellenir (soru 4). Uygulama ayrıntısı (geri alınabilir): onaylanan değeri Müdür veya Admin doğrudan girer (SAT-D145), Muhasebe önerir ve müdür sınıfı onaylar (SAT-D146); listelerdeki değerler Sinan'a gösterilen yasal oranlardır, içerik Ayarlar verisidir ve kodda sabitlenmez; %0 KDV seçimi istisna kodu ister (SAT-D93, SAT-D147); her tevkifat oranı faturada SAT-D148 kod listesindeki bir GİB koduyla eşlenir. Sinan'ın 1. sorudaki "%1…%20" metni 4. sorudaki öneriyle yerini aldı; yasal olmayan oran listede bulunmaz | Ayarlar vergi masterı (oran/tevkifat listeleri), sipariş satırı vergi değişikliği (PR #885) |
| SAT-D182 | Tevkifatın uygulanacağı alıcı | Hangi alıcı durumunda tevkifat uygulanacağını **mali müşavir belirler** ve Ayarlar'da değiştirilir; sistem tahmin etmez (soru 5). Müşavir değiştirene kadar SAT-D158'in sürüm-0 başlangıç değeri **aynen kalır**: Normal alıcıda tevkifat uygulanır; nihai tüketici, tevkifata tabi olmayan, ihraç kayıtlı ve istisna belgeli alıcıda uygulanmaz (soru 7; SAT-D147) | Ayarlar alıcı vergi durumu listesi (SAT-D158) |
| SAT-D183 | Tevkifatlı siparişte risk tutarı | Riske müşterinin bize **ödeyeceği tutar** girer: KDV dahil tutardan tevkif edilen KDV düşülür. Örnek: 100.000 TL + %20 KDV = 120.000 TL; 5/10 tevkifatta riske 110.000 TL girer (soru 2). Tutar Satış'ın toplam hesaplayıcısından gelir; ekranda yeniden hesaplanmaz. Uygulama ayrıntısı (geri alınabilir): riskin öteki bileşenleri (açık alacak; faturalanmamış sevk) de müşterinin ödeyeceği tutar bazındadır, böylece toplam tek bazda kalır (DEC-FIN-007); bir satırın tevkifatı çözülemezse o bileşen "hesaplanamıyor" kalır, brüt tutar yerine konmaz (SAT-D46'daki kural) | API-06 risk kontrolü, API-03 toplamlar |
| SAT-D184 | Eski siparişlerin alıcı durumu | Bu değişiklikten önce açılmış siparişlere alıcı vergi durumu olarak **"Normal"** yazılır ve satır vergileri Ayarlar'dan çözülür (soru 3). Uygulama ayrıntısı (geri alınabilir): yazım migration ile bir kez yapılır ve audit'e girer; canlı veri olmadığı için yalnız deneme kayıtlarını etkiler | PR #885 migration |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz. Vergi oranları ve
  kodları bu belgeyle Ayarlar'a **girilmez**; müşavirin onayladığı değerleri Müdür veya Admin girer (SAT-D145, SAT-D148).
