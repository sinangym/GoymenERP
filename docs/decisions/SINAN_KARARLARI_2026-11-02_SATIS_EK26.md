# Sinan Kararları — 2026-11-02 (Satış: sipariş vergisi — satır vergisi seçimi, tevkifatın uygulanacağı alıcı, risk tutarı, eski siparişler)

Bu belge önceki karar dosyalarını silmez; SAT-D180…SAT-D184'ü ekler. PR #885'in (siparişe vergi: alıcı durumu, satır vergi
anlık görüntüsü, toplamlar) yazarının sorduğu üç soruyu cevaplar. SAT-D73…D76, SAT-D82, SAT-D93, SAT-D97, SAT-D133…D135,
SAT-D139, SAT-D140, SAT-D146…D148 ve SAT-D158 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Risk tutarı:** SAT-D46 risk kalemlerini "KDV dahil" tutarla alıyordu. SAT-D183'e göre tevkifatlı siparişte riske
  müşterinin bize **ödeyeceği** tutar girer: KDV dahil tutardan tevkif edilen KDV düşülür. Tevkifatsız siparişte iki tutar
  aynıdır, SAT-D46 aynen kalır.
- **Satır vergisi seçimi:** SAT-D93/D97'deki satır vergisi değişikliği artık Ayarlar'daki KDV oranı ve tevkifat oranı
  listelerinden de seçim yapabilir (SAT-D181). Elle oran yazılmaz.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
seçmeli soru cevapları: iki tur, beş soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi. Birinci turdaki
1. soruya Sinan kendi metniyle cevap verip görüş sordu ("sence nasıl olur"); ikinci turdaki 4. soru bu metne verilen öneriyi
sordu ve Sinan öneriyi seçti. Sinan'ın metni aynen aktarılmıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Satışçı bir satırın vergisini gerekçeyle değiştirdiğinde ne seçebilsin? Bugün yalnız kalem tipi varsayılanı, alıcı durumu ve istisna kodu seçilebiliyor; başka KDV oranı (ör. %1, %10) ya da tevkifat kodu seçilemiyor. | Kendi metni: "Bütün ürün satışlarında %20 kdv ve 5/10 tevkifat varsayılan gelsin ama ellede değiştirilebilir olsun (%1-2-3-4-5-6-7-8-9-10-11-12-13-14-15-16-18-19-20 ) gibi sence nasıl olur" | — (seçenekler: "Ayarlar listesinden oran/kod (Önerilen)" — "Ayarlar'da KDV oranı listesi (ör. %1, %10, %20) ve tevkifat kodu listesi tutulur; satışçı yalnız bu listelerden gerekçeyle seçer. Elle oran yazılamaz."; "Şimdiki gibi kalsın"; "Yalnız Muhasebe değiştirsin") |
| 2 | Tevkifatlı bir siparişte risk kontrolüne hangi tutar girsin? Örnek: 100.000 TL + %20 KDV = 120.000 TL; 5/10 tevkifat varsa müşteri bize 110.000 TL öder, 10.000 TL KDV'yi kendisi vergi dairesine yatırır. | Ödenecek tutar (Önerilen) | "Riske 110.000 TL girer: müşterinin bize gerçekten borçlanacağı tutar." (seçilmeyen: "KDV dahil brüt" — "Riske 120.000 TL girer: tevkifat düşülmez (daha ihtiyatlı).") |
| 3 | Bu değişiklikten önce açılmış siparişlerde alıcı vergi durumu yok (canlı veri yok, yalnız deneme kayıtları). Ne yapılsın? | Otomatik 'Normal' olsun (Önerilen) | "Eski siparişlere alıcı durumu 'Normal' yazılır ve satır vergileri Ayarlar'dan çözülür." (seçilmeyen: "Boş kalsın") |
| 4 | Önerim şu: Ürün satışlarında varsayılan %20 KDV + 5/10 tevkifat olsun; bunu Ayarlar'da kalem tipinin varsayılanı olarak siz/müşavir girersiniz. Satışçı bir satırda gerekçe yazarak başka oran seçebilir, ama yalnız Ayarlar'daki listelerden: KDV oranları (%0, %1, %10, %20) ve tevkifat oranları (2/10 … 10/10). İhracat ve istisna belgeli alıcıda tevkifat zaten uygulanmaz (SAT-D147). Nasıl olsun? | Önerdiğin gibi (Önerilen) | "Varsayılan %20 + 5/10 Ayarlar'dan gelir. Satır değişikliği yalnız Ayarlar'daki yasal oran/tevkifat listelerinden, gerekçeyle yapılır. Listeyi müşavir onayıyla siz güncellersiniz." (seçilmeyenler: "%1–%20 arası her oran"; "Elle oran yazılsın") |
| 5 | 5/10 tevkifat hangi alıcılarda uygulansın? (Demir-çelikte tevkifat genelde KDV mükellefi alıcıya yapılan satışta uygulanır; nihai tüketiciye uygulanmaz.) | Müşavire göre Ayarlar (Önerilen) | "Hangi alıcı durumunda tevkifat uygulanacağını müşavir belirler ve Ayarlar'a girilir; sistem tahmin etmez." (seçilmeyen: "Normal alıcının hepsinde") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D180 | Ürün satışında varsayılan vergi | Ürün satışlarında varsayılan **%20 KDV + 5/10 tevkifat** olur (soru 1 ve 4). Bu değerler kodda sabitlenmez; Ayarlar'da kalem tipinin efektif tarihli varsayılanı olarak (SAT-D139) Sinan veya mali müşavir tarafından girilir. Girilene kadar SAT-D148 gereği vergi "hesaplanamıyor" kalır | Ayarlar vergi masterı, sipariş satırı vergisi |
| SAT-D181 | Satır vergisinin değiştirilmesi | Satışçı bir satırın vergisini **gerekçe yazarak** değiştirebilir, ama yalnız Ayarlar'daki listelerden seçer: **KDV oranı listesi** (%0, %1, %10, %20) ve **tevkifat oranı listesi** (2/10 … 10/10). Elle oran yazılmaz (soru 4). Listeler müşavir onayıyla güncellenir; güncelleme Ayarlar'daki vergi ayarı değişikliği kurallarına tabidir (SAT-D146: öneri ve müdür sınıfı onayı). Sinan'ın 1. sorudaki "%1…%20" metni bu öneriyle yerini aldı; yasal olmayan oran listede bulunmaz | Ayarlar vergi masterı (oran/tevkifat listeleri), sipariş satırı vergi değişikliği (PR #885) |
| SAT-D182 | Tevkifatın uygulanacağı alıcı | Hangi alıcı durumunda tevkifat uygulanacağını **mali müşavir belirler** ve Ayarlar'a girilir; sistem tahmin etmez (soru 5). İhracat kayıtlı ve istisna belgeli alıcıda tevkifat uygulanmaz (SAT-D147). Değer girilene kadar o alıcı durumunda tevkifat "hesaplanamıyor" kalır (fail-closed) | Ayarlar vergi masterı (alıcı durumu × kalem tipi) |
| SAT-D183 | Tevkifatlı siparişte risk tutarı | Riske müşterinin bize **ödeyeceği tutar** girer: KDV dahil tutardan tevkif edilen KDV düşülür. Örnek: 100.000 TL + %20 KDV = 120.000 TL; 5/10 tevkifatta riske 110.000 TL girer (soru 2). Tutar Satış'ın toplam hesaplayıcısından gelir; ekranda yeniden hesaplanmaz | API-06 risk kontrolü, API-03 toplamlar |
| SAT-D184 | Eski siparişlerin alıcı durumu | Bu değişiklikten önce açılmış siparişlere alıcı vergi durumu olarak **"Normal"** yazılır ve satır vergileri Ayarlar'dan çözülür (soru 3). Uygulama ayrıntısı (geri alınabilir): yazım migration ile bir kez yapılır ve audit'e girer; canlı veri olmadığı için yalnız deneme kayıtlarını etkiler | PR #885 migration |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz. Vergi oranları ve
  kodları bu belgeyle Ayarlar'a **girilmez**; SAT-D148 gereği Sinan veya mali müşavir girer.
