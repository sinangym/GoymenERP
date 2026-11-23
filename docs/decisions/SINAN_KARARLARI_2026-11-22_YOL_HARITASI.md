# Sinan Kararları — 22 Kasım 2026 (boşluk analizi sonrası yol haritası, roller, muhasebe, fiyat yapısı)

Bu belge önceki karar dosyalarını silmez; YOL-D1, YOL-D2, YOL-D3, SAT-D446 ve (aynı gün ek cevaplarla, SINAN-CEVAP 60)
SAT-D447–SAT-D450 ekler. `SINAN_KARARLARI_2026-11-22_AI.md`
(AI-D10..AI-D21), `SINAN_KARARLARI_2026-11-21_TREN_KAPANIS.md` ve DEC-INT-001 geçerliliğini korur; YOL-D3, DEC-INT-001'in
ilk sürümde muhasebe entegrasyonu olmaması ilkesini değiştirmez, yalnız muhasebenin hangi yazılımda tutulacağını söyler.

**Kaynak:** Sinan, 22 Kasım 2026, Claude Code oturumunda soru kutusuyla; dayanak `docs/delivery/ERP_BOSLUK_ANALIZI_v1.md`.
"(Önerilen)" işaretleri soranındır. Serbest metin cevaplar aynen alınmıştır. Bu dosya bir **ajan aktarımıdır**; Issue #1189
kaydı "SINAN-CEVAP — ajan aktarımı" olarak yazılır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Boşluk analizinden sonra: bu tren kapanınca sıradaki tren ne olsun? | **Önce Dalga 0–1, AI paralel (Önerilen)** | Roller, ana sayfa, tek iş kutusu, banka ekstresi, PDF'ler ve raporlar önce; AI treni bu veriye dayandığı için paralel ilerler. Seçilmeyenler: "Önce AI treni" (AI-11..AI-29 önce, Dalga 0–1 sonra); "Önce e-belge entegrasyonu" (e-Fatura, e-Arşiv, e-İrsaliye başa; entegratör seçimi ve mali müşavir teyidi gerekir). |
| 2 | Bugün hiçbir kullanıcının işletme yetkisi (satış, finans, stok…) yok. Roller nasıl belirlensin? | **Taslağı sen hazırla, ben onaylarım (Önerilen)** | Mevcut satış rol önerisi genişletilip tüm modüller için rol–yetki tablosu çıkarılır; atama ve onay Sinan'da kalır (insan kapısı). Seçilmeyen: "Rolleri ben yazacağım" (kişi ve rol listesini Sinan verir, ajan yetki eşlemesini hazırlar). |
| 3 | Muhasebe şimdilik Mikro'da mı kalsın? | **İlk yıl Mikro'da kalsın (Önerilen)** | ERP muhasebe fişini ve KDV/tevkifat hazırlık verisini Mikro'ya aktarır; e-Defter Mikro'da kalır. Seçilmeyen: "Tamamen ERP'ye geçsin" (KDV beyanı, e-Defter, kur farkı değerlemesi ve masraf merkezi ERP'de; iş yükü ve mevzuat riski daha büyük). |
| 4 | Satışta çelik fiyat ekstraları kullanılıyor mu (kalınlık, genişlik, kalite, kaplama ekstrası; endekse veya hurdaya bağlı sürşarj)? | Serbest metin: "Bilmiyorum ne için gerekli daha açıklayıcı ve nasıl olması gerektiğini önerileyerek sor" | Soru açıklamayla yeniden soruldu (5. soru). Seçenekler: "Evet, tablo halinde olsun"; "Hayır, elle fiyat yeterli"; "Bilmiyorum, sonra konuşalım". |
| 5 | Fiyat ekstrası ne demek (baz fiyat + ölçü/kalite/kaplama/miktar ekstraları + sevk tarihindeki endekse bağlı sürşarj; tabloyla teklif anında fiyat otomatik önerilir, satışçı değiştirebilir, taban altı mevcut onaya gider). Bizim satışımız nasıl fiyatlanıyor? | Serbest metin: "Fiyatı günlük elle vereceğim ama baz+ekstra+endeks sürşarj olabilir, hangilerinin baz hangilerinin ekstra veya sürşarjının ne olduğunu nasıl yöneteceğim bunuda ben ayarlar veya ürünlerden mi yönetmeliyim bunu bile ai ile yapabilmem lazım mesela yani herşeyi ai ile yapabilmem lazım ayrıca" | Seçenekler: "Baz + ekstra tablosu (Önerilen)" (fiyat listesine tarihli ekstra kuralları; teklif/siparişte otomatik öneri; sürşarj sonra); "Baz + ekstra + endeks sürşarjı" (endeks girilir, sürşarj sevk tarihindeki endeksle); "Her ürünün kendi fiyatı var" (ekstra tablosu gerekmez); "Fiyatı günlük elle veriyoruz" (her teklifte elle; bugünkü taban onayı yeterli). |

| 6 | Fiyat kuralları nerede yönetilsin? (Baz = o gün verilen ton fiyatı; ekstra = ölçü/kalite/kaplama için eklenen tutar; sürşarj = endekse bağlı ek.) | **Ekstra kuralı Ayarlar'da, baz fiyat listesinde (Önerilen)** | Örneğin "kalınlık < 2 mm → +1.500 TL/ton" bir kez Ayarlar'da tanımlanır, tüm ürünlere uyar; günlük baz fiyat fiyat listesinde güncellenir; AI ikisini de konuşarak taslaklar, Sinan onaylar. Seçilmeyenler: "Her ürün kartında ayrı ayrı" (esnek ama bakım yükü büyük); "İkisi birden" (genel kural Ayarlar'da, ürün kartında istisna). |
| 7 | Ekstralar hangi özelliklere bağlansın? (çoklu seçim) | **Kalınlık ve genişlik (Önerilen)**, **Kalite ve kaplama (Önerilen)** ve serbest metin: "Miktar ve işleme şirket içine göre karar veriliyor ama kalınlık genişlik kalite ve kaplama sektörel ekstraları var onun için bence bunlar zorunlu miktar işleme zaten şirket içi kar marjına göre değişken" | Kalınlık/genişlik: ölçü aralığına göre ekstra, kart ölçülerinden. Kalite/kaplama: S355, galvaniz, boyalı gibi, kartın kalite/kaplama bilgisinden. Seçilmeyenler: "Miktar" (az miktar ekstrası); "İşleme (dilme/kesim)" (ton veya kesim başına ücret). |
| 8 | Sürşarj hangi endekse bağlı olsun ve endeks değeri nereden gelsin? | Serbest metin: "Sürşarjı tam olarak anlayamadım" | Soru açıklamayla yeniden soruldu (9. soru). Seçenekler: "Endeksi elle gir, sözleşme bazında aç (Önerilen)"; "Şimdilik sürşarj yok"; "Otomatik dış kaynaktan". |
| 9 | Sürşarj ne işe yarar (uzun süreli anlaşmada sabit baza hammadde endeksindeki farkın eklenmesi/düşülmesi; günlük elle fiyatta genelde gerekmez). Bizde böyle bir durum var mı? | Serbest metin: "Sürşarj bizde yok zaten fiyat değişikliği siparişte gerekirse değiştirilir ve onaya gider, zaten siparişin geçmiş fiyat değişiklikleri ve sipariş formları sürümlü şekilde kayıt altında da olması gerekiyordu buna göre karar vermiştim daha öncd" | Seçenekler: "Uzun süreli anlaşma yok, şimdilik gerekmez (Önerilen)" (sürşarj alanı tasarımda yer alır ama kapalı); "Bazı müşterilerle sabit fiyatlı anlaşma var" (sözleşme bazında sürşarj); "Tedarikçiden sürşarjlı alıyoruz" (alış tarafında izlenir). |
| 10 | Sürşarj ve ekstralar hangi tarihte sabitlensin, belgede nasıl görünsün? | **Sipariş onayında sabit, ayrıntı ekranda (Önerilen)** | Fiyat onayda kilitlenir, müşteriye tek birim fiyat gider; baz/ekstra/sürşarj kırılımı içeride ve teklif PDF'inde görünür. Seçilmeyenler: "Sevk tarihinde sürşarj güncellensin" (sevk günü endeksiyle; sözleşme gerekir); "Faturada ayrı satır" (baz, ekstra, sürşarj ayrı kalemler). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| YOL-D1 | Sıradaki tren | Tren-1 kapanınca önce boşluk analizinin **Dalga 0–1**'i yapılır: işletme rolleri (YOL-D2), gerçek sorguya bağlı ana sayfa sayaçları, tek iş kutusu (tüm onay türleri), banka ekstresi içe alma + eşleştirme, fatura/alış siparişi/teklif PDF'leri, genel liste dışa aktarımı ve kayıtlı görünüm, yaşlanan stok raporu, kural tabanlı mükerrer fatura kontrolü. **AI treni (AI-11..AI-29) paralel** ilerler ve bu veriyi kullanır. e-Belge entegrasyonu Dalga 2'dedir. | Teslim planı, AI treni sırası |
| YOL-D2 | İşletme rolleri | Ajan tüm modüller için **rol–yetki taslağı** hazırlar (mevcut `SATIS_ROL_YETKI_ONERISI_v1.md` genişletilir). Rol tanımı, yetki ataması ve onayı **Sinan'dadır**; taslak hiçbir rolü veya grant'ı etkinleştirmez (grant/rol/Critical insan kapısı). | Identity seed önerisi, docs/delivery |
| YOL-D3 | Muhasebe yazılımı | İlk yıl resmî muhasebe ve **e-Defter Mikro'da kalır**. ERP muhasebe fişini ve KDV/tevkifat hazırlık verisini Mikro'ya aktaracak biçimde hazırlanır. Aktarım yöntemi, dosya biçimi ve kimlik bilgileri ayrı karardır (OPEN-016); DEC-INT-001 sürer. | Accounting, entegrasyon planı |
| SAT-D446 | Satış fiyat yapısı | Satış fiyatını Sinan **günlük elle** verir; fiyat yapısı **baz + ekstra + endeks sürşarjı** olabilir. Hangi kalemin baz, hangisinin ekstra, sürşarjın ne olduğu **yönetilebilir** olmalı ve bu yönetim **AI ile konuşarak** da yapılabilmelidir (AI-D16: taslak + önizleme + yetkili onayı). Ekstra/sürşarj kuralı tanımlanmamışsa bugünkü davranış (satırda elle fiyat, taban altı onayı) aynen sürer. Kural tablosunun yeri, alanları ve hesap formülü ayrı karar ve golden ister (bkz. Sınırlar). | Product fiyat listeleri, Settings, Sales fiyat hesaplayıcısı, AI treni |

| SAT-D447 | Fiyat kurallarının yeri | **Ekstra kuralları Ayarlar'da** şirket geneli, sürümlü ve tarihli tanımlanır (öznitelik aralığı → ton başı ekstra tutarı); **günlük baz fiyat fiyat listesinde** güncellenir. İkisi de AI ile konuşarak taslaklanabilir; yetkili onayıyla yürürlüğe girer (AI-D16). SAT-D446 (a) kapanır. | Settings, Product fiyat listeleri, AI treni |
| SAT-D448 | Ekstra öznitelikleri | Tablolu ekstralar yalnız **sektörel** olanlardır: **kalınlık, genişlik, kalite, kaplama** (zorunlu kapsam). **Miktar ve işleme** ekstra tablosuna girmez; şirket içi kâr marjına göre satışçının fiyatında kalır. SAT-D446 (b) kapanır. | Settings ekstra kuralları, Sales fiyat önerisi |
| SAT-D449 | Sabitleme ve görünüm | Baz + ekstra fiyat **sipariş onayında sabitlenir**; müşteriye tek birim fiyat gider; baz/ekstra kırılımı ekranda ve teklif PDF'inde görünür. Sonraki fiyat değişikliği mevcut sipariş revizyonu ve onayı yoluyla yapılır. SAT-D446 (d) kapanır. | Sales sipariş onayı, teklif PDF'i |
| SAT-D450 | Sürşarj | **Sürşarj yok;** sürşarj alanı veya endeks kurulmaz. Fiyat değişikliği gerekirse siparişte değiştirilir ve onaya gider; siparişin geçmiş fiyat değişiklikleri ve sipariş formları sürümlü kayıt altındadır (mevcut sipariş revizyon kararları). SAT-D446 (c) kapanır. | Sales, Settings |

## Sınırlar

- **NEEDS-SINAN (SAT-D446–SAT-D448 ayrıntısı):** ekstra tutarlarının ve aralık sınırlarının değerleri (Sinan veya yetkili
  AI taslağıyla girer); birden çok ekstra aynı anda uygulanınca toplama sırası ve yuvarlama; ekstra kuralı olmayan
  ürün/öznitelikte davranış (ajan önerisi: ekstra 0 değil "kural yok" gösterilir, fiyat elle girilir). Formül golden'ı
  SAT-D431 yöntemiyle hazırlanmadan önerilen fiyat hesabı etkinleştirilmez; bugünkü elle fiyat ve taban altı onayı sürer.
- Roller taslağı (YOL-D2) karar değildir; Sinan onaylayana kadar hiçbir rol veya grant eklenmez.
- e-Belge entegratörü, mali müşavir teyidi gereken Türkiye maddeleri (GTİP 72/73 "imal" yorumu, hurda tevkifat kodu,
  e-İrsaliye/e-Defter başlangıç tarihleri) ve kalite modülünün zamanı açıktır.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
