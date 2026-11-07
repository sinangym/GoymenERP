# Sinan Kararları — 2026-11-07 (dövizli faturada taksit TL'si, iptal siparişin sevki, eşleştirme toleransı)

Bu belge önceki karar dosyalarını silmez; SAT-D227, SAT-D228 ve SAT-D229'u ekler. Fatura dalgasında açık kalan üç soruyu
cevaplar. SAT-D11, SAT-D12, SAT-D89, SAT-D96, SAT-D124, SAT-D172, SAT-D209 ve EK42'deki SAT-D223…SAT-D226 geçerlidir
(SAT-D229, SAT-D89'un varsayılan değerini değiştirir; "Bilinçli değişiklikler"e bakın).

**Kaynak:** Sinan'ın 7 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: üç soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi; "(Önerilen)" işareti soranındır.
Soru 1'de ve soru 3'te Sinan serbest metinle cevap verdi (aşağıda "Yetki devri" ve soru 3'ün notu).

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Dövizli faturada taksit varsa (örn. 3 taksit) her taksitin TL karşılığı nasıl bulunsun? Finans her taksit için TL tutar istiyor; dövizli faturanın kesinleşmesi buna bağlı. | Serbest metin: "Bunu araştır muhasebesel nasıl oluyorsa" → Satış şeridinin araştırmasıyla seçilen: **Her taksit × kur, fark sonuncuya (Önerilen)** | "Her taksitin döviz tutarı fatura kuruyla çevrilip kuruşa yuvarlanır; taksitlerin TL toplamı fatura TL ödenecek tutarından farklıysa fark son taksite eklenir. Taksit yuvarlaması (SAT-D124) ile aynı mantık." (seçilmeyen: "Toplam TL'yi döviz oranıyla böl" — "TL ödenecek tutar taksitlerin döviz oranına göre bölünür, kalan fark son taksite eklenir. Taksit TL toplamı her zaman fatura toplamına eşittir; tek tek taksit TL'si döviz×kur'dan birkaç kuruş sapabilir.") |
| 2 | İptal edilmiş bir siparişin daha önce sevk edilmiş ama henüz faturalanmamış irsaliyesi faturalanabilsin mi? Şu an reddediliyor. | Evet, faturalanabilsin | "Sevk gerçekleştiği için sipariş iptal olsa da sevk edilen miktar faturalanır. Mal çıkmış ve faturasız kalmaz; iptal edilmiş siparişe fatura kesilmiş olur." (seçilmeyen: "Hayır, reddedilsin (Önerilen)" — "İptal edilen siparişin fiilen sevk edilmiş ama faturalanmamış sevki faturaya alınmaz; önce sipariş yeniden açılır ya da sevk düzeltilir. Güvenli, ama sevk edilmiş mal faturasız kalabilir.") |
| 3 | Resmî belge (e-Fatura) ile ERP faturası eşleştirilirken tutar farkı için tolerans kaydı Ayarlar'da henüz yok. Toleransa kadar ne olsun? | Serbest metin: "Birkaç kuruş fark kabul, 0,05₺ ye kadar ve ayarlardan ayarlanabilsin" | Seçilen yön: "Birkaç kuruş fark kabul" — "Belirli bir kuruş farkına kadar eşleşme kabul edilir. Bu eşiği senin belirlemen gerekir (örn. 0,05 TL)…" (açıklamanın aktarılan metni; eşik Sinan'ın cevabında 0,05 TL). (seçilmeyen: "Tam eşleşme, fark bekler (Önerilen)" — "Tolerans tanımlı olana kadar tutarlar kuruşu kuruşuna eşleşmelidir; fark varsa fatura Kontrol Merkezi'nde bekler. Daha sık bekleme olabilir ama yanlış eşleşme olmaz.") |

## Yetki devri (soru 1)

Sinan soru 1'e "Bunu araştır muhasebesel nasıl oluyorsa" diye cevap verdi. Bu, **yalnız bu soru için** açık bir yetki
devridir. Seçim, bu yetki devriyle **Satış şeridinin araştırmasına** dayanarak yapıldı; Sinan istediği an değiştirebilir.
Soru 2 ve soru 3 Sinan'ın kendi cevaplarıdır.

**Araştırma sonucu (dürüst özet):**

- Dövizli faturada muhasebe kaydı hem döviz hem TL tutarıyla tutulur; TL karşılığı fatura tarihindeki kurla alacak
  hesabına (120) yazılır. Tahsilatta döviz bakiyesi kapansa bile TL bakiyesi kalabilir; bu fark kur farkı olarak
  ayrı izlenir (fatura TL karşılığı alacağın TL bakiyesidir; taksitler bu bakiyenin parçalarıdır).
- Bu çerçeve, taksitlerin TL toplamının fatura TL ödenecek tutarına eşit olması gerektiğini destekler: alacak kalemi
  faturanın TL karşılığıyla açılır (SAT-D208, EK42/SAT-D225) ve tahsilatlar bu bakiyeyi düşürür. Taksit TL toplamı
  fatura TL ödenecekten saparsa 120 bakiyesi ile açık kalemler arasında kaynağı belirsiz bir fark oluşur.
- **Taksitli dövizli satışta kuruş yuvarlaması ve son taksitteki farkın nasıl dağıtılacağını açıkça düzenleyen resmî bir
  kural veya kaynak bulunamadı.** Bu yüzden seçim, bulunan çerçevenin (TL bakiye = fatura TL karşılığı) ve mevcut kararların
  (SAT-D124 taksit yuvarlaması, EK42 satır/grup farkı son kalemde) tutarlılığına dayanır; mali müşavir teyidi istenirse
  Sinan değiştirebilir.

Kaynaklar:

- https://www.parasut.com/blog/dovizli-fatura-hazirlarken-dikkat-edin
- https://www.parasut.com/blog/kur-farki-faturasi
- https://www.faturaport.com/blog/e-fatura/dovizli-fatura-nedir-nasil-kesilir-kur-ve-kdv-rehberi
- https://www.faturaport.com/blog/e-fatura/kur-farki-faturasi-nedir-nasil-kesilir-kdvsi-olur-mu
- https://www.muhasebetr.com/yazarlarimiz/ufuk/065/

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D227 | Dövizli faturada taksit TL'si | Dövizli faturada taksit varsa **her taksitin TL karşılığı** = taksidin fatura para birimindeki tutarı × faturanın kuru (SAT-D172, EK42/SAT-D226), SAT-D11'in yuvarlamasıyla (2 ondalık, yarı-yukarı, `AwayFromZero`) kuruşa yuvarlanır. Taksit TL'lerinin toplamı faturanın TL ödenecek tutarından (SAT-D225) farklıysa fark (artı veya eksi) **son taksite** eklenir; böylece **taksit TL toplamı fatura TL ödenecek tutarına eşittir**. Bu, SAT-D124'ün (taksit yuvarlaması) fatura para biriminde yapılan taksit bölüşümünün TL karşılığına uygulanmasıdır. "Toplam TL'yi döviz oranıyla böl" seçilmedi (soru 1, yetki devriyle) | Fatura (API-32/36), vade hesaplayıcısı, Finans açık kalem taksitleri (DEC-SAL-008) |
| SAT-D228 | İptal edilmiş siparişin sevk edilmiş, faturalanmamış irsaliyesi | Sipariş iptal edilmiş olsa bile **daha önce fiilen sevk edilmiş ve henüz faturalanmamış** irsaliye (sevk payı) **faturalanabilir**; sevk edilen miktar faturalanır, mal faturasız kalmaz (soru 2). Faturalama önceki kurallarla aynen çalışır: yalnız onaylı, iptal edilmemiş irsaliye ve henüz faturalanmamış miktar (SAT-D96'daki yeniden faturalanabilirlik dahil). Siparişin kendisi iptal kalır; fatura siparişi yeniden açmaz. İptalde faturalanmamış irsaliye etkisi önizlemede görünmeye devam eder (DEC-SAL-005) | Fatura taslağı (API-32), iptal etki önizlemesi |
| SAT-D229 | Resmî fatura eşleştirme toleransı | SAT-D89'daki eşleştirme toleransının **varsayılan değeri 0,05 TL** olur (soru 3): tutar, KDV ve tevkifatta resmî belge ile ERP faturası arasındaki fark **0,05 TL'ye kadar** (dahil) kabul edilir; fazlası onaya gider, resmî değer esastır (SAT-D89'un kalanı aynen). Bu eşik Satış'ın **kendi yapılandırma varsayılanıdır**; Ayarlar'da düzenlenebilir kayıt, **Ayarlar şeridi açılınca** bağlanır (aşağıda "Sınırlar", SAT-D209) | Fatura eşleştirme (API-35), Kontrol Merkezi |

## Bilinçli değişiklikler

- **SAT-D89:** eşleştirme toleransının varsayılanı **0'dan 0,05 TL'ye** çıkar (SAT-D229, soru 3). SAT-D89'un diğer
  kuralları (fatura no, ETTN, müşteri, para birimi tam eşleşir; fazlası onaya gider; resmî değer esastır) değişmez.
- **SAT-D124'ün kapsamına ek:** SAT-D124 taksit yuvarlamasını para biriminin ölçeğinde (TL'de kuruş) tanımlar; SAT-D227 bunun
  **dövizli faturadaki TL karşılığına** da aynı mantıkla uygulandığını ekler. SAT-D124'ün metni değişmez.
- **İptal edilmiş sipariş ve fatura (SAT-D228):** Mevcut bir Sinan kararıyla çelişmez. SAT-D12 (zorla kapatmada faturalanmamış
  irsaliye engellemez, uyarıdır; açık fatura işi olarak görünür), DEC-SAL-005 (iptalde faturalanmamış irsaliye etkisi
  gösterilir) ve SAT-D96 (resmî iptalde sevk payları yeniden faturalanabilir) sevkin faturalanmasını engellemez.
  "Şu an reddediliyor" ifadesi, bugünkü uygulamanın davranışıdır; bir karara dayanmadığı kod/spec taramasında görülmedi.
  Uygulama bu karara göre değişecektir.
- Başka hiçbir kararın anlamı değişmez. EK42 (SAT-D223…SAT-D226) aynen geçerlidir; SAT-D227 onun SAT-D225 ödenecek
  tutarına dayanır.

## Uygulama ayrıntısı (geri alınabilir)

- Taksit TL farkı son taksite eklenir; son taksit TL tutarının işaretini çevirecek veya tutarını aşacaksa fatura
  kesinleştirme reddedilmez, fark kurala göre eklenir ve durum Kontrol Merkezi'nde işaretlenir (EK42'deki işaret/aşma
  uygulama ayrıntısıyla aynı). Yeni politika seçilmez.
- İptal edilmiş siparişin sevkinden kesilen faturada siparişin iptal durumu faturada ve Kontrol Merkezi'nde görünür
  bilgidir; fatura için ek onay kapısı bu kararla getirilmez.
- Eşleştirme toleransı karşılaştırması sınır değeri dahil kabul eder (fark ≤ 0,05 TL).

## Sınırlar

- **Eşleştirme toleransının yeri (SAT-D209 ile ilişki):** Sinan "ayarlardan ayarlanabilsin" dedi; bu, Ayarlar modülünde
  yeni bir kayıt demektir. SAT-D209 (soru 2 ve 3: Ayarlar'da yeni bağlantı, tablo veya ekran açılmaz; ALS-D45/FNS-D1 küçük ek
  izni askıda) buna şimdilik engeldir ve **aşılmaz**. Bu yüzden karar şöyle kayda geçer: eşik **0,05 TL, Satış'ın kendi
  yapılandırma varsayılanı olarak**; **Ayarlar'da düzenlenebilir kayıt, Ayarlar şeridi açılınca** (SAT-D209'un "o modüllere
  başlanınca bağlantılar yeniden incelenir" maddesiyle) eklenir. O zamana kadar Ayarlar'da düzenleme yoktur; değer Satış'ın
  kendi yapılandırmasındadır (koda gömülü sabit değil, Satış modülünün kendi yapılandırma yüzeyi). Bu yeni bir `NEEDS-SINAN`
  açmaz.
- **Dövizli faturada tolerans para birimi — `NEEDS-SINAN`:** 0,05 TL'lik eşiğin dövizli faturada tutarın **TL karşılığı
  üzerinden mi** (EK42/SAT-D223…D225 TL tutarları) yoksa fatura para birimi tutarı üzerinden mi karşılaştırılacağı
  Sinan'ın cevabında yoktur ve burada **seçilmez**. Sinan'a sorulana kadar SAT-D229'un 0,05 TL varsayılanı dövizli faturaya
  uygulanmaz; dövizli faturada SAT-D89'un mevcut davranışı (eski varsayılan 0) olduğu gibi kalır. TL faturada (para birimi
  TL) 0,05 TL geçerlidir.
- Hiçbir `OPEN-*` kaydını kapatmaz.
- Muhasebe ve Finans sözleşmeleri bu belgeyle değişmez: taksit TL tutarları Finans açık kalem taksitlerine fatura olayıyla
  (SAT-D208, `sales.invoice.finalized.v1`) taşınır; alanların ayrıntısı Finans ile fatura dalgasında netleşir. Muhasebe
  fişindeki kuruş farkı politikası Muhasebe şeridine aittir (KLN-D14).
- Taksitli dövizli satışta tahsilat sırasında doğan **kur farkı** (fatura TL bakiyesi ile tahsilat TL tutarı arasındaki fark)
  bu belgede kararlaştırılmadı; Finans/Muhasebe şeridinin konusudur.
- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez.
