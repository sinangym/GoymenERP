# Göymen ERP fikir havuzu ve uyarlama planı v1 (22 Kasım 2026)

Bu belge bir **plan önerisidir**, karar değildir. `ERP_BOSLUK_ANALIZI_v1.md`'yi genişletir. Kararlar yalnız Sinan'ın cevabıyla
tarihli `SINAN_KARARLARI_*.md` dosyasına girer. Hiçbir işletme politikası tahminle etkinleştirilmez: oran, eşik, tarife,
maliyet dağıtım yöntemi, transfer fiyatı, iskonto, hedge. Bunlar yapılandırılabilir bırakılır ve `NEEDS-SINAN` / `OPEN-*`
olarak işaretlenir. Türkiye maddeleri mali müşavir veya YMM teyidi ister.

Mevcut kararlarla bağ:

- **YOL-D1:** sıradaki tren Dalga 0–1, AI treni paralel.
- **YOL-D2:** roller taslağı.
- **YOL-D3:** muhasebe ilk yıl Mikro'da.
- **SAT-D446–SAT-D450:** fiyat yapısı.
- **AI-D10–AI-D21:** sistem içi AI.
- **DEC-DOC-007:** WhatsApp API yok.
- **SAT-D302:** ek masraf v1'de ortalamaya girmez.

## 1. Kaynaklar ve kanıt düzeyleri

Yedi ajan araştırması, aynen saklandı (`docs/delivery/arastirma/`):

| Ek | Konu |
|---|---|
| `2026-11-22_goymen_erp_envanteri.md` | Bizim ERP'nin modül modül kod envanteri |
| `2026-11-22_metal_sektoru_ve_turkiye.md` | Metal servis merkezi ERP'leri ve temel Türkiye maddeleri |
| `2026-11-22_karar_destek_karlilik.md` | Karar destek ve kârlılık kaldıraçları |
| `2026-11-22_sektorler_arasi_erp.md` | Metal dışı 10 sektörün ERP modül ve yöntemleri, bize uyarlama |
| `2026-11-22_turkiye_yukumlulukler.md` | Tüm sektörlerde Türkiye yükümlülükleri ve mali müşavir soru listesi |
| `2026-11-22_kanitlanmamis_is_erp_fikirleri.md` | Ana akım dışı iş ve ERP fikirleri, pilot tasarımları |
| `2026-11-22_kanitlanmamis_ai_yontemleri.md` | Erken aşama AI yöntemleri ve hafıza mimarisi |

**Kanıt düzeyleri:**

| Düzey | Anlamı |
|---|---|
| A | Hakemli çalışma veya bağımsız denetim |
| B | Analist kıyası veya bağımsız benchmark |
| C | Satıcı vakası, iddia veya yazarın kendi ölçümü |
| D | Spekülatif |
| M | Mevzuat (teyit gerekir) |

**Sınırlama:** Ajanlar sayfaların çoğunu tam okuyamadı; bulgular arama özetlerine dayanıyor. Kritik bir rakam veya tarih
uygulamaya girmeden birincil kaynaktan doğrulanır.

## 2. Ana sonuçlar

1. **En güçlü ve sektöre en yakın kanıt:** ABD'de bir B2B alüminyum satıcısında yapılan saha deneyi (Marketing Science 2024,
   A). Satışçıya modelin fiyatı yalnız **gösterilmiş**, zorunlu tutulmamış; işlenen tekliflerin kârı yaklaşık %11 artmış.
   Karmaşık tekliflerde insan daha iyi kalmış. "Tek backend hesaplayıcı öneri verir, insan karar verir" mimarimize birebir
   uyuyor.
2. **Elimizdeki altyapı, başka ERP'lerin pahalıya kurduğu şeyleri ucuza veriyor:**
   - Soy ağacı ve döküm no mirası; geri çağırma raporu, sertifika, CBAM emisyon verisi ve İDİS bunun üzerine kurulur.
   - Audit/outbox olay günlüğü; süreç madenciliği bunun üzerine kurulur.
   - Deterministik hesaplayıcı; "bu sayıyı açıkla" izi bunun üzerine kurulur.
   - Çok şirket yapısı; grup içi ticaret otomasyonu bunun üzerine kurulur.
3. **İlk analizde eksik kalan Türkiye maddeleri:**
   - AB karbon sınır vergisi (CBAM). Kesin dönem 1.1.2026'da başladı, ilk beyan 30.09.2027.
   - 3 şirket arasında transfer fiyatlandırması ve adat.
   - İmzalı sipariş formunda damga vergisi.
   - İhracat dosyası.
   - Tevkifat zinciri: tüccar olarak satışta da 5/10, bu yüzden sürekli KDV iade alacağı.
   - TTK 1530 ödeme süresi ve temerrüt faizi.
   - e-Tebligat (VUK 596).
   - Tehlikeli atık (boya hattı).
   - KVKK yurt dışı aktarım bildirimi (AI ve bulut).
4. **Düzeltmeler:**
   - Belge saklama VUK'ta 5 yıl, TTK'da 10 yıl. ERP 10 yılı esas alır; kasamız zaten 10 yıl saklıyor.
   - Metal hurda teslimi KDV istisnasıdır; 7/10 tevkifat yalnız istisnadan vazgeçilirse uygulanır.
5. **AI'da kanıt yönü:** iş akışı önce gelir, ajan sonra; doğrulamayı araç yapar, modelin kendi eleştirisine güvenilmez.
   Hafıza çift zamanlı ve kaynaklı tutulur. Dış metin karantinaya alınır (CaMeL kalıbı). Her değişiklik değerlendirme
   CI'ından geçer. Şirket verisiyle model eğitimi, tartışan ajan sürüleri, ekrana tıklayan ajan ve üçüncü taraf MCP yoktur.

## 3. Birleşik yol haritası

Dalga numaraları `ERP_BOSLUK_ANALIZI_v1.md` §9 ile aynıdır. **Yeni** sütunu bu araştırmayla eklenenleri gösterir.
Efor: S = 1 dilim, M = 2–4 dilim, L = tren ölçeği.

### Dalga 0–1 (sıradaki tren, YOL-D1)

| # | Kalem | Yeni | Kaynak | Kanıt | Efor | Kapı / bağımlılık |
|---|---|---|---|---|---|---|
| 1.1 | İşletme rolleri taslağı (YOL-D2) | | Envanter | — | S | Onay Sinan'da |
| 1.2 | **İstisna öncelikli ana sayfa:** her istisnanın sahibi, SLA'sı ve kapanış neden kodu (gerçek / yanlış alarm / eşik yanlış). İlk üç tür: kantar farkı, taban fiyat altı, sertifikası eksik sevk | ✓ (genişletme) | İş fikirleri A1 | C | M | Eşikler politika |
| 1.3 | Tek iş kutusu: tüm onay türleri, vekâlet ve SLA. **İstisna bazlı inceleme:** temiz kayıt onaysız akar, imzada anlam kodu tutulur | ✓ (genişletme) | Sektörler K3 | C | M | Eşikler politika |
| 1.4 | **"Bu sayıyı açıkla":** hesaplayıcı yanıtında iz (girdi, formül sürümü, yuvarlama, kaynak belge); UI yalnız gösterir. İlk hedefler satır marjı, üretim maliyeti, kütle dengesi | ✓ | İş fikirleri A2 | B/C | M | Maliyet görme yetkisi |
| 1.5 | Banka ekstresi içe alma ve eşleştirme | | v1 | M/B | M | — |
| 1.6 | Fatura, alış siparişi ve teklif PDF'leri; zorunlu TTK alanları (unvan, MERSİS, sicil no, web) | ✓ (TTK) | Türkiye §6 | M | S | — |
| 1.7 | Rapor merkezi, genel dışa aktarım ve kayıtlı görünüm | | v1 | B | M | — |
| 1.8 | Raporlar: yaşlanan stok, **OTIF ve mükemmel sipariş**, **tek tık geri çağırma / izleme** (döküm no → müşteri / irsaliye ve geriye), grup içi işlem dökümü | ✓ | Sektörler H1, G2 | B / C | S–M | — |
| 1.9 | Kural tabanlı mükerrer fatura kontrolü | | v1 | B | S | — |
| 1.10 | **Uyum takvimi ve süre sayaçları:** e-Arşiv iptali / e-Fatura reddi 8 gün, fatura 7 gün, iş kazası bildirimi 3 iş günü, ihraç kayıtlı teslim 3 ay, e-Tebligat kontrolü | ✓ | Türkiye | M | S | Süreler teyitle |
| 1.11 | **Komut paleti (Ctrl+K):** yalnız yetkili komutlar listelenir | ✓ | İş fikirleri A6 | A (hız) | S | Yetki filtresi |

### AI treni (paralel, AI-11..AI-29'a eklenecekler)

| # | Kalem | AI dilimi | Kanıt |
|---|---|---|---|
| A.1 | Değerlendirme CI'ı: önce deterministik kontrol, sonra farklı aileden LLM hakem; hafıza kategorileri (LongMemEval); pass^k | AI-11, AI-27 | A |
| A.2 | **Çift zamanlı olgu deposu** (SQL Server): her olgu kaynaklı, geçerlilik ve kayıt zamanlı. İşlemler EKLE / DESTEKLE / YERİNE GEÇ / GEÇERSİZ KIL; fiziksel silme yok. Çelişki kaydı açıksa AI cevap vermez. ERP doğrusu hafızaya kopyalanmaz. Ayrıntı: AI eki §2 | AI-12, AI-17, AI-28 | C (kalıp) |
| A.3 | Dış metne karantina (CaMeL / çift LLM) + köken ve bütünlük etiketi | AI-15, AI-22 | B− |
| A.4 | İş akışı önce + araçla doğrulanan düzeltme (toplam, şema, calculator) | AI-13..AI-16 | A |
| A.5 | Hafıza ve araç zehirlemesine karşı: şirket kapsamına onaysız yazma yok; araç açıklaması hash pin'li; üçüncü taraf MCP yok | AI-12, AI-13, AI-29 | A |
| A.6 | Onay/red gerekçelerinden kural önerisi (ExpeL); yayına yönetici onayıyla girer | AI-19 | A |
| A.7 | Tarif kütüphanesi: araç dizisi + şema + araç sürümüne bağ | AI-25 | A |
| A.8 | Kalibre güven ve otomasyon yanlılığına dayanıklı onay ekranı: önce kanıt, öneri isteğe bağlı | AI-14, AI-27 | A/B |
| A.9 | Çevrimdışı istem optimizasyonu (GEPA/DSPy), CI kapılı | AI-27 | A |
| A.10 | Yerel tahmin modelleri **gölge modda:** tahsilat gecikmesi ve teklif kazanma (TabPFN); kesikli talepte Croston'a karşı zaman serisi modelleri | AI-16, AI-18 | A / B |
| A.11 | **WhatsApp veya e-posta metnini yapıştır → taslak sipariş** (DEC-DOC-007 ile uyumlu) | AI-15 | C |
| A.12 | **Marj köprüsü (PVM):** fiyat, hacim, karışım, kg maliyeti, kur ve navlun etkisi deterministik hesaplanır; AI yalnız anlatır | AI-16 | B (yöntem) |

### Dalga 2: Türkiye uyumu ve e-belge (mali müşavir teyidiyle)

| # | Kalem | Yeni | Kanıt | Efor |
|---|---|---|---|---|
| 2.1 | e-Fatura / e-Arşiv / e-İrsaliye entegratör adaptörü (OPEN-016); ihracat e-faturası senaryosu | ✓ (ihracat) | M | L |
| 2.2 | Ürün kartında GTİP ve tevkifat kategorisi; **tüccar zinciri** (satışta da 5/10); hurda istisnası veya 7/10 seçimi; **KDV iade dosyası** (listeler otomatik) | ✓ (zincir, iade dosyası) | M | M |
| 2.3 | **İhracat dosyası:** sipariş, e-fatura, GÇB, DAB/IBKB, kur farkı faturası, iade ve süre sayaçları | ✓ | M | M |
| 2.4 | **Grup içi ticaret otomasyonu:** A'nın satışı B'de alış taslağı açar (outbox üzerinden); grup içi mutabakat; transfer fiyatının emsalle karşılaştırılması. Fiyat politikası Sinan ve mali müşavirde | ✓ | C + M | M/L |
| 2.5 | **Damga vergisi:** imzalı sipariş formu ve sözleşme sicili, hesaplayıcı (binde 9,48, tavan parametre) | ✓ | M | S |
| 2.6 | **TTK 1530:** ödeme süresi ve KOBİ bayrağı, vade aşımı faizi hesaplayıcısı (oran parametre); adat | ✓ | M | S |
| 2.7 | KVKK: yurt dışı aktarım envanteri, 5 iş günü bildirim, 72 saatlik ihlal kaydı; 10 yıllık saklama matrisi ve hukuki saklama | ✓ | M | S |

### Dalga 3: kalite, izlenebilirlik ve CBAM

| # | Kalem | Yeni | Kanıt | Efor |
|---|---|---|---|---|
| 3.1 | MTC **yapısal veri** olarak alınır (akma, çekme, uzama, kimyasal, kaplama), etikete bağlanır, çocuk etikete miras kalır, sevkte otomatik eklenir | (genişletme) | C | M |
| 3.2 | **Özelliğe göre etiket seçimi:** müşteri şartnamesine uyan etiketler listelenir, uymayanların gerekçesi gösterilir | ✓ | C | M |
| 3.3 | **CBAM emisyon pasaportu:** çelikhane emisyon beyanı (tCO2e/t, doğrulanmış mı, tesis) etikete bağlanır ve mirasla geçer; AB müşterisine veri paketi. ERP değer **üretmez**, beyanı taşır | ✓ | M | M |
| 3.4 | Kalite blokesi, NCR → **8D** ve çelikhaneye düzeltici faaliyet talebi (SCAR); etkilenen etiketler soy ağacından | ✓ (8D/SCAR) | B/C | M |
| 3.5 | Hafif PPAP: müşteri onaylı ürün kaydı (yalnız otomotiv müşterisi varsa) | ✓ | C | M |
| 3.6 | Tehlikeli atık kaydı (boya, tiner, yağ): atık kodu, depolama süresi, MoTAT ve beyan verisi | ✓ | M | S |

### Dalga 4: kârlılık ve ticari model

| # | Kalem | Yeni | Kanıt | Efor | Politika |
|---|---|---|---|---|---|
| 4.1 | Cep marjı / hizmet maliyeti + müşteri kârlılık eğrisi | | A/B | M | Dağıtım sürücüleri |
| 4.2 | **Prim (rebate):** çelikhane primi alacağı ve müşteri yıl sonu primi karşılığı; cep marjına ve tabana girer | ✓ | C | M | Oranlar, muhasebe |
| 4.3 | **Ardiye ve elleçleme faturalama:** müşteri malı ve fason stoğu için gün-ton ardiye, yükleme, paketleme ve kantar ücreti tahakkuku; aylık "faturalanmamış hizmet" listesi | ✓ | C | M | Tarifeler |
| 4.4 | Fiyat ekstra tablosu (SAT-D447–D449) + **gölge fiyat önerisi** (önce geriye dönük, gösterilmez; sonra öneriyi göster/gösterme deneyi). Fiyat rastgeleleştirilmez | ✓ (öneri) | **A** | M | Hedef marj |
| 4.5 | Teklif kazanma analizi + **teklif yanıt süresi** ölçümü (önce kendi verimizde ilişki var mı) | ✓ (süre) | A / B | S | — |
| 4.6 | **Çerçeve satış sözleşmesi ve çekiliş:** tonaj, fiyat, kümülatif sevk, kalan miktar; stok programı (rezerve) | ✓ | C | M | Min/maks |
| 4.7 | **Darboğaz dakikası başına katkı** ve hat saatlik verimi (yalnız rapor, teşvik yok) | ✓ | A (vaka) | M | — |
| 4.8 | **Karar günlüğü + ön-ölüm:** spekülatif alım ve yüksek limit kararları; sonucu ERP kendisi doldurur (endeks, marj); yön tahmini için Brier skoru | ✓ | D/A | S | — |
| 4.9 | Eş ürün / yan ürün maliyet dağıtımı (dilme genişlikleri; hurda NRV) | ✓ | B | M | **Yöntem kararı** |

### Dalga 5: depo, hat ve lojistik

| # | Kalem | Yeni | Kanıt | Efor |
|---|---|---|---|---|
| 5.1 | Raf/bin + el terminali PWA: **çevrimdışı kuyruk** (her öğe idempotency anahtarlı, karar sunucuda; stokta CRDT yok) | (genişletme) | A (kavram) | M |
| 5.2 | **Kalınlık × genişlik stok ızgarası:** serbest, rezerve, yolda ve fasonda kg; ızgaradan teklif satırı | ✓ | C | M |
| 5.3 | Hat başı ekranı + **dijital vardiya panosu:** dünkü fire, kütle dengesi sapması, kantar farkı, geciken iş emri, sahipli aksiyon | ✓ (pano) | A (nitel) | S–M |
| 5.4 | Kapı randevusu ve kantar/vinç kuyruğu (L-04 araç/şoför ile) | ✓ | C | M |
| 5.5 | Kabulde çapraz sevk uyarısı; çelikhaneden doğrudan sevk (back-to-back, tek hareket) | ✓ | C | S/M |
| 5.6 | Kesim/dilme planı: **sabit, golden-testli OR-Tools modeli**; AI yalnız isteği parametreye çevirir ve sonucu açıklar; artık eşleştirme; önce gölge mod | (genişletme) | A (yöntem) | L |
| 5.7 | SPC/Cpk: dilme genişliği, boy, soğuk hadde kalınlığı | ✓ | A (yöntem) | M |
| 5.8 | ZPL etiket, kantar cihaz adaptörü | | — | M |

### Dalga 6: planlama, finans ve diğer

Bu dalgadakiler:

- Nakit planı (OPEN-011).
- Tahsilat önceliği.
- Kur farkı değerlemesi.
- Masraf merkezi.
- RFQ ve tedarikçi karnesi.
- **İthalat dosyası / landed cost:** hazırlık; ortalamaya katılması SAT-D302 ayarına bağlı.
- **Aylık S&OP:** önce süreç.
- **Açık alım bütçesi:** yalnız uyarı.
- **Enerji kWh/ton:** CBAM ile aynı veri.
- **Konsinye, iki yönlü.**
- **Konfigüratör / CPQ:** altı motor ve fiyat hesaplayıcısı üstünde, taslak teklif.
- Çok duraklı giden fason: yalnız gelen fason kararı genişletilirse.

## 4. 2–4 haftalık geri alınabilir pilotlar

Ortak kurallar:

- Her pilot özellik bayrağıyla kapalı başlar.
- Mümkün olan yerde salt okuma veya gölge mod kullanılır.
- Para veya stok kaydı üreten pilot politika kararı olmadan açılmaz.
- Başarı eşiği Sinan'la önceden yazılır.

| # | Pilot | Ölçüt (tanım; eşik Sinan'la) |
|---|---|---|
| P1 | İstisna öncelikli ana sayfa, 3 türle | Açık kalma süresinin medyanı; yanlış alarm kapanış oranı eğilimi; günlük liste ekranı açma sayısı |
| P2 | Hesap izi (marj, üretim maliyeti) | Rastgele 20 sayının izden aynen yeniden üretilmesi; "bu rakam nereden" sorusunun çözülme süresi |
| P3 | Süreç madenciliği + teklif yanıt süresi (çevrimdışı, rol bazlı, şirket başına) | En sık 5 varyant ve döngü; süre dilimine göre kazanma oranı; dokunuşsuz sipariş oranı; en az 3 somut iyileştirme önerisi |
| P4 | Gölge fiyat önerisi (yalnız geriye dönük) | Önerilen ile gerçekleşen fiyat farkının kazanılan ve kaybedilen tekliflerdeki dağılımı |
| P5 | Karar günlüğü + ön-ölüm | Kayıt oranı; 3. ayda Brier skoru; çıkarılan ders sayısı |
| P6 | Darboğaz katkısı ve cep marjı karşılaştırması | İki sıralamanın ilk ve son 10 müşterisinde örtüşme; rapor yüzünden değişen kararlar |
| P7 | Boş kapasite bülteni ve artık vitrini (önce grup içi) | Bültene atfedilen fason saat; vitrinden 4 haftada satılan kg payı; hurda fiyatına göre fark |
| P8 | Yapıştır → taslak sipariş (2 satışçı) | Giriş süresi; düzeltilen alan oranı; yanlış ürünle onaylanan sipariş (hedef 0) |
| P9 | Komut paleti | Haftada en az 5 kez kullanan kullanıcı oranı; yetkisiz komut sızıntısı testi = 0 |
| P10 | Geri çağırma tatbikatı | Döküm no'dan tüm müşteri ve irsaliye listesine ulaşma süresi |
| P11 | Emisyon beyanını döküm no'ya bağlama (satışta prim yok) | Beyanı olan kabul satırı payı; çocuk etikette eksiksiz miras oranı; müşteri veri talebi sayısı |
| P12 | Tahsilat hatırlatma metni A/B (norm oranı backend'den) | Ödemeye kadar geçen günün medyanı; şikâyet sayısı |

## 5. Uzak durulacaklar

| Konu | Neden |
|---|---|
| Tam olay kaynaklı yeniden yazım, composable/headless ERP, organizasyonun dijital ikizi | Fayda zaten outbox, audit ve modüler monolitte; maliyet yüksek, kanıt zayıf |
| Ekranda formül yazılan tablo arayüzü | Tabloların büyük kısmında hata çıkıyor (A); hesap UI'a kayar |
| Stok veya parada CRDT ile local-first | Çift hareket veya kayıp üretir |
| Çelik aboneliği, kendi açık pazaryerimiz, ters faktoringi başlatmak | Yaygın uygulama ve ağ etkisi yok; güç dengesi bizde değil |
| Müşteriye rastgele farklı fiyat, algoritmik rakip tepkisi | İlişki riski; Rekabet Kanunu (M) |
| Bireysel lider tablosu ve oyunlaştırma, çalışanlar arası tahmin pazarı | Kanıt zayıf; KVKK ve sosyal baskı; katılımcı az |
| Şirket verisiyle model eğitimi | AI-D5 yasaklıyor; RAG daha iyi (A) |
| LLM'nin kurduğu optimizasyon modelini çalıştırmak | Endüstriyel doğruluk düşük |
| Tartışan veya yazma yetkili ajan sürüleri, ekrana tıklayan ajan, üçüncü taraf MCP, bulut hafıza SaaS'ı | Hata modları, güvenlik, KVKK |
| Sesle veya görüntüyle doğrudan stok/sevk komutu | Sayı tanıma hatası; barkod, kantar ve ekran onayı tek doğru kaynak |
| WhatsApp'ta genel amaçlı asistan | Meta politikası (Ocak 2026) ve DEC-DOC-007 |
| Planogram, catch weight, tam APQP/Part 11, tam hakediş, tam proje MRP, RTLS | İş modelimize uymuyor veya zaten karşılanıyor |
| CBAM emisyonunu ERP'de hesaplamak | Doğrulama akredite üçüncü tarafın işi; biz beyanı taşırız |

## 6. Değişmezlerle eşleşme

| Değişmez | Çakışma riski olan kalemler | Güvenli biçim |
|---|---|---|
| Her kayıtta `CompanyId` | 1.8 grup içi döküm, 2.4 grup içi ticaret, P7 artık vitrini | Şirketler arası okuma ayrı yetkiyle (grant insan kapısı); yazma her şirkette ayrı ve outbox üzerinden |
| Tek deterministik hesaplayıcı | 1.4, 4.4, 4.7, 4.9, A.12, P12 | Bütün sayılar, oranlar ve izler backend'den gelir, golden testli |
| Kopya hareket yok | 5.5 back-to-back, konsinye, hurda geri alım | Fiziksel olay tek hareket |
| Silme yok, ters kayıt | Geri alma, hafıza | Gönderim öncesi bekleme penceresi; sonrası ters kayıt taslağı; hafızada geçersiz kılma |
| Politika tahminle etkinleşmez | 1.2 eşikleri, 4.2 / 4.3 / 4.6 / 4.9, P4 hedef marj | Gölge mod ve parametre; açılış Sinan kararıyla |
| AI'ya ham SQL yok | A.x, süreç madenciliği | Şemalı araçlar; madencilik çevrimdışı ve insan analizi |

## 7. Karar soruları (önerili)

| # | Soru | Öneri |
|---|---|---|
| F1 | Dalga 1'e eklenecek yeni kalemler: hesap izi, komut paleti, OTIF ve geri çağırma raporu, uyum takvimi, istisna bazlı inceleme | Hepsi; her biri S–M efor |
| F2 | AB'ye doğrudan veya müşteri üzerinden ihracat var mı? | Varsa CBAM pasaportu (3.3) ve ihracat dosyası (2.3) öne alınır |
| F3 | 3 şirket arasında düzenli satış var mı? | Varsa grup içi ticaret otomasyonu (2.4) Dalga 2'nin başına |
| F4 | Çelikhane primi veya müşteri yıl sonu primi, müşteri malı ardiye/elleçleme ücreti var mı? | Varsa 4.2 ve 4.3 cep marjıyla birlikte |
| F5 | Otomotiv tedarikçisi müşterimiz var mı (EDI çağrı, PPAP)? | Yoksa 3.5 ve EDI dışarıda kalır |
| F6 | Eş ürün maliyeti kg oranıyla mı, satış değeriyle mi dağıtılsın? | Golden'la ayrı karar; şimdilik açık |
| F7 | Mali müşavire soru listesi (Türkiye ekinde 18 soru) gönderilsin mi? | Evet; cevaplar Dalga 2 kapsamını kesinleştirir |
| F8 | Pilotlardan hangileri ilk sırada? | P2, P3, P4, P1: veri hazır ve salt okuma |
