# Göymen ERP boşluk analizi v1 (22 Kasım 2026)

Bu belge bir **plan önerisidir**, karar değildir. Kararlar yalnız Sinan'ın cevabıyla tarihli `SINAN_KARARLARI_*.md`
dosyasına girer. Burada önerilen hiçbir işletme politikası (fiyat tabanı, kredi limiti, emniyet stoku, tevkifat yorumu,
hedge) tahminle etkinleştirilmez; yapılandırılabilir bırakılır ve gerekiyorsa `OPEN-*` / `NEEDS-SINAN` olarak işaretlenir.

## 1. Yöntem

Dört kaynak karşılaştırıldı:

1. **Bizim ERP'nin kod envanteri** (repo head 3dae29911): 17 modül, yaklaşık 3.000 backend dosyası, 447 HTTP rotası,
   286 yetki sabiti, yaklaşık 350 React ekranı. "Var" kodu bulunan demektir. README'lerin çoğu çalışma ortamı/tarayıcı
   kabulünü `NOT_RUN` sayar; yani "var" ≠ "üretimde kanıtlı".
2. **Metal servis merkezi ERP'leri:** Epicor, Infor, SAP Mill Products, D365 tabanlı (Crowe, Sikich), Invera STRATIX,
   Enmark, Aptean, unitop, IFS. Satıcı ifadeleri iddia düzeyindedir.
3. **Türkiye mevzuatı** (Kasım 2026): VUK tebliğleri (509, 565, 588, 589), KDV Genel Uygulama Tebliği, 7571 sayılı
   Kanun, KVKK kurul kararları. Birincil metinler satır satır okunamadı; **mali müşavirle teyit edilmeli**.
4. **Karar destek ve kârlılık literatürü:** hakemli çalışmalar, analist kıyasları ve satıcı vakaları.

**Kanıt düzeyleri:**

| Düzey | Anlamı |
|---|---|
| A | Hakemli çalışma veya bağımsız denetim |
| B | Analist veya danışmanlık kıyası (McKinsey, Gartner, APQC) |
| C | Satıcı vakası veya iddiası |
| D | Spekülatif veya yeni fikir |
| M | Mevzuat (teyit gerekir) |

## 2. Kısa sonuç

**Güçlü olduğumuz yerler.** Bunlar profesyonel ERP'lerde çoğu zaman ek ürün veya özelleştirme isteyen alanlar:

- **Parti/etiket soy ağacı ve döküm no:** Döküm no bölme/birleştirmede çocuk etikete geçiyor (`InventoryStore.Genealogy`).
- **Adet/kg çift birim:** teorik ve tartılan kg; bu trende ekleniyor.
- **Altı deterministik üretim motoru ve kütle dengesi:** dilme, boy kesme, sarma, boru/profil, soğuk hadde, boya.
- **Fason:** satış, alış, lojistik ve stok tarafında.
- **Kantar farkı onayı.**
- **Çok şirket:** her tabloda şirket filtresi.
- **Maker-checker, idempotency, audit ve outbox:** kritik her komutta.
- **Finans:** çek/senet portföyü, teminat, kredi ve risk paneli.

**En büyük boşluklar** (etki × kanıt sırasıyla):

| # | Boşluk | Neden önemli |
|---|---|---|
| 1 | **Kimse işletme yetkisine sahip değil.** Seed'de 5 sistem rolü var, işletme rolü yok; README'ler 49 kez "hiçbir role verilmedi" diyor. | Sistem bugün kullanılamaz; rol önerisi (`SATIS_ROL_YETKI_ONERISI_v1.md`) karar bekliyor. İnsan kapısı. |
| 2 | **e-Belge entegrasyonu yok:** e-Fatura, e-Arşiv, e-İrsaliye dışarıda kesilip elle işleniyor (DEC-INT-001). | En büyük çift giriş yükü. GTİP 72/73 e-İrsaliyesi demir-çelik için sektörel zorunluluk (M). |
| 3 | **Banka ekstresi içe alma bağlı değil:** CSV eşleme var, Finans tüketicisi yok. | Tahsilat ve ödeme eşleştirme tamamen elle. |
| 4 | **Ana sayfa, KPI ve rapor merkezi yok:** yalnız 3 satış raporu ve 1 üretim raporu var. | Yönetici ve rol çalışma alanı yok; karar desteği zayıf. |
| 5 | **Cep marjı / hizmet maliyeti yok:** satır marjı var, ama navlun, işleme, vade, iade ve fatura altı indirim müşteriye düşülmüyor. | Kârlılıkta kanıtı en güçlü kaldıraç (A/B). |
| 6 | **Kalite modülü yok:** kalite blokesi, uygunsuzluk, MTC/sertifika kaydı yok. | Metal servis merkezinde "kritik" sayılıyor (W3-KALITE). |
| 7 | **Raf/bin konumu ve el terminali yok.** | Depoda arama süresi ve yanlış etiket riski. |
| 8 | **KDV beyanı, tevkifat iade takibi, kur farkı değerlemesi ve masraf merkezi yok.** | Muhasebe ayrı yazılımda (Mikro) kalıyor; çift iş. |
| 9 | **Onay motoru dar:** iş kutusunda yalnız 4 onay türü var; vekâlet, eskalasyon ve SLA yok. | Onay bekleyen işler kaybolur. |
| 10 | **PDF eksikleri:** fatura, alış siparişi ve teklif PDF'i yok; e-posta gönderimi yok. | Müşteriye ve tedarikçiye belge elle gidiyor. |

## 3. Modül bazlı karşılaştırma

Durum: **Var** / **Kısmi** / **Yok**. Öncelik: **P1** sonraki 1–2 tren, **P2** 3–5 tren, **P3** sonrası.

| Alan | Bizde | Profesyonel ERP / sektör pratiği | Öneri | Öncelik | Kanıt |
|---|---|---|---|---|---|
| Rol ve yetki | Kısmi: yetki altyapısı tam, işletme rolü yok | Hazır rol şablonları + rol bazlı çalışma alanı | Rol önerisini karara bağla; rol başına çalışma alanı | **P1 (insan kapısı)** | — |
| Organizasyon | Var: şirket, saha, depo, iş merkezi | + raf/bin, vardiya | Raf/bin konumu (stokla birlikte) | P2 | C |
| Ürün | Var: kart, kod grubu, fiyat listesi, paket standardı | + GTİP, tevkifat kategorisi, boyut öznitelikleri, tedarikçi fiyatı | Karta **GTİP** ve **tevkifat kategorisi** ekle | **P1** | M |
| Cari | Var: cari 360, kimlik, birleştirme | + IBAN, e-mutabakat, portal | IBAN ve mutabakat mektubu | P2 | M (gönüllü pratik) |
| Satış | Var: en derin modül, 73 ekran | + baz fiyat + tarihli ekstra + sevk tarihli sürşarj; taban/hedef fiyat bandı; kazanma olasılığı | Fiyat ekstraları ve sürşarj tablosu (tek hesaplayıcı); teklif kayıp kaydı zaten var, analiz eklenir | **P1** | B/C |
| Satınalma | Var: talep, sipariş, kabul, iade, fatura eşleştirme, UBL-TR içe alma | + RFQ ve teklif karşılaştırma, tedarikçi karnesi, PPV | RFQ + tedarikçi karnesi (W2-SATINALMA) | P2 | B |
| Stok | Var: etiket, soy ağacı, sayım, hurda, transfer, ortalama ve tam maliyet havuzu | + kalite blokesi/karantina, min stok, yaşlanan stok, raf, el terminali | Yaşlanan/atıl stok raporu; kalite blokesi; min stok (K-7) | **P1** (rapor) / P2 | A/B |
| Lojistik | Var: yükleme, kantar, irsaliye, sevk | + araç/şoför/kapı, e-İrsaliye gönderimi, yük optimizasyonu | Araç/şoför ana verisi (L-04); e-İrsaliye entegrasyonu | **P1** (e-İrsaliye) | M |
| Üretim | Var: 6 motor, plan, iş emri, kütle dengesi, maliyet | + kesim optimizasyonu (artığı kullanan), sonlu kapasite, OEE, hat başı ekranı | Artık parçayı kullanan kesim önerisi; hat başı tablet ekranı | P2 | A (yöntem) |
| Kalite | Yok | Sertifika (EN 10204 3.1) kaydı ve sevkte otomatik gönderim, kalite blokesi, NCR | Alım kabulünde sertifika belgesi + etikete bağlama + soy ağacıyla miras + sevkte PDF eki | **P1** | C (sektör pratiği) |
| Finans | Var: banka/kasa, tahsilat/ödeme, açık kalem, cari defter, çek/senet, teminat, kredi | + banka ekstresi içe alma ve otomatik eşleştirme, 13 haftalık nakit planı, tahsilat önceliği | MT940/CSV ekstre içe alma + eşleştirme önerisi; nakit planı (OPEN-011) | **P1** | M/B |
| Muhasebe | Var: dönem, yevmiye, mizan, kaynak fiş, maliyet önerileri | + KDV beyan hazırlığı, tevkifat iade takibi, kur farkı değerlemesi, masraf merkezi, e-Defter | Tevkifat iade alacağı takibi; kur farkı değerlemesi; masraf merkezi boyutu | P2 | M |
| Belgeler | Var: kasa, SHA-256, 10 yıl | + OCR, gelen belge kutusu, sınıflandırma | AI treniyle gelen belge okuma (W2-BELGE) | P2 | C |
| İş kutusu | Kısmi: 4 onay türü, bildirim | Ortak onay motoru, vekâlet, eskalasyon, SLA | Tüm onay türleri tek iş kutusunda (AI-D20 ile aynı yön) | **P1** | B |
| Arama | Kısmi: 4 tür | Tüm belge türleri, son kullanılanlar | Fatura, irsaliye, alış siparişi ve teklifi ekle | P2 | D |
| Rapor/BI | Kısmi: 4 rapor | Rapor merkezi, kayıtlı görünüm, zamanlanmış dışa aktarım, detaya inme | Rapor merkezi (W5-RAPOR) + genel liste dışa aktarımı | **P1** | B |
| AI | İskelet (kapalı) | Semantik katman + şemalı araçlar, belge okuma, teklif taslağı | AI treni planı (AI-11..AI-29) | Sonraki tren | A/B/C |
| Bakım (CMMS) | Yok (ertelendi) | Hat duruşu = teslim kaybı | Basit arıza/duruş kaydı (OEE için veri) | P3 | C |
| İK/bordro | Yok (ertelendi) | Türkiye'de genelde yerel bordro paketi | Dışarıda kalsın; personel ana verisi yeter | P3 | — |
| Sabit kıymet, bütçe | Yok | Standart | Bütçe W4-NAKIT ile | P3 | — |
| Portal | Yok | Sertifika indirme, sipariş durumu | Kalite ve rapor merkezinden sonra | P3 | C |

## 4. Metal sektörüne özgü işlevler

| İşlev | Bizde | Boşluk | Öneri |
|---|---|---|---|
| Döküm/parti izlenebilirliği | Var: döküm no soy ağacıyla geçiyor | Sertifika belgesi ve test değerleri bağlı değil | Kalite dilimi: kabulde MTC yükle → etikete bağla → çocuk etikete miras → sevkte otomatik ek |
| Çift birim (adet/kg) | Bu trende ekleniyor | Metre birimi ayrı karar (SAT-D443) | Metre ve ondalıklı birim sonraki karar |
| Dilme/boy kesme soy ağacı | Var | Yan kesim hurdasının geri kazanım değeri kısmen var (aylık hurda fiyatı) | Hurda değerini çıktı satırında göstermek |
| Artık parça | Kısmi: üretimde artık sınıflandırması var | Artığı yeni siparişle eşleştiren öneri yok | Artık eşleştirme önerisi (kesim stoğu problemi, A) |
| Fason iki yön | Var | Fason fire mutabakat raporu | Rapor merkezi içinde |
| Fiyat: baz + ekstra + sürşarj | Kısmi: satır bazında elle yüzde/tutar sürşarj | Tarihli ekstra tablosu ve endeks bazlı sürşarj yok | Ekstra/sürşarj tablosu, tek backend hesaplayıcı, golden ile |
| Kantar ve tartı | Kısmi: tartım elle yazılıyor (OPEN-003) | Cihaz bağlantısı yok | Kantar cihaz adaptörü (insan kapısı: donanım) |
| Etiket/barkod | Kısmi: PDF + Code39 | ZPL ve doğrudan yazıcı yok; el terminali yok | ZPL çıktısı + el terminali ekranı (PWA) |

## 5. Türkiye uyumu

Hepsi **M** düzeyindedir. Uygulamadan önce mali müşavirle teyit edilir. Tarihler ve kodlar tahminle kilitlenmez;
Ayarlar'da parametre olarak tutulur.

| Konu | Durum (bizde) | Gerekli |
|---|---|---|
| e-Fatura / e-Arşiv (589 sayılı tebliğ: 2026'dan itibaren tutardan bağımsız e-Arşiv) | Dışarıda kesiliyor, elle kayıt | Özel entegratör bağlantısı (OPEN-016) |
| e-İrsaliye, GTİP 72/73 sektörel zorunluluk (509 sayılı tebliğ) | Yalnız numara kaydı | Entegratör bağlantısı. **Açık:** dilme/kesme/kaplama "imal" mi; İDİS alanları (inşaat demiri varsa) |
| e-Defter | Yok (muhasebe Mikro'da) | Mikro'da kalacaksa yalnız fiş aktarımı |
| KDV tevkifatı: demir-çelik 5/10 (kod 627), hurda 7/10 | Tevkifat ana verisi var (Ayarlar) | Ürün kartında GTİP + tevkifat kategorisi, alıcı tipine göre otomatik seçim, **tevkifat KDV iade alacağı takibi**. Hurda kodu resmi listeden doğrulanmalı |
| Ba-Bs | — | Eylül 2024'te kaldırıldı; modül gerekmez |
| Enflasyon düzeltmesi | Yok | 2025–2027 yapılmıyor; altyapı ileride, varsayılan kapalı |
| Banka (MT940 / API) | CSV eşleme var, tüketici yok | Ekstre içe alma + eşleştirme |
| KVKK | Rol, log ve saklama altyapısı var | Aydınlatma metni, saklama/imha politikası (OPEN-018/021) |
| TCMB kuru | Adaptör var, kapalı | Kur tipi ve kesim saati kararı (OPEN-012) |

## 6. Kullanılabilirlik ve iş yükü

| Pratik | Bizde | Öneri | Kanıt |
|---|---|---|---|
| Rol bazlı çalışma alanı: sayaç + istisna listesi + iş listesi | Yok ("sahte gösterge yok") | Gerçek sorguya bağlı sayaçlar: "vadesi geçmiş", "sertifikası eksik", "onay bekleyen", "kantar farkı", "taban fiyat altı" | B (tasarım rehberi) |
| Tek iş kutusu | 4 onay türü | Tüm onay ve istisnalar tek kutuda, SLA ve vekâletle | B |
| Kayıtlı görünüm ve genel dışa aktarım | Yok | Her listede filtre kaydet + XLSX | B (NN/g) |
| Toplu işlem | Kısmi: toplu kabul ve içe almalar | Listelerde çoklu seçim | B |
| Belge PDF ve e-posta | Kısmi: sipariş, irsaliye, etiket | Fatura, alış siparişi, teklif PDF'i + e-posta (AI-D17 ile) | — |
| Barkod / el terminali | Yok | Depo ve sevk için telefon/tablet PWA ekranı | A (hata azaltımı; tam oran iddiası kaynaksız) |
| Hat başı ekranı | Yok | Büyük butonlu, tartı ve etiket bağlantılı | C |
| Akıllı varsayılan | Kısmi | Teklifte fiyat listesinden fiyat + işleme ücreti önerisi | C |
| Uyarı tasarımı | — | Az sayıda uyarı, eşiği yapılandırılabilir, sahibi ve SLA'sı belli; etkisi ölçülür (alarm yorgunluğu %90–96 red) | A |

## 7. Karar destek ve kârlılık

Sıra kanıt gücü ve uygulama riskiyle belirlendi. Hepsi aynı deterministik backend hesaplayıcısını kullanır; ekran veya
AI kendi sayısını üretmez.

| # | Kaldıraç | Ne yapar | Bizde gereken veri | Kanıt |
|---|---|---|---|---|
| 1 | **Cep marjı ve hizmet maliyeti** | Satır marjından navlun, işleme, vade maliyeti, iade ve fatura altı indirimi düşer; müşteri kârlılık ("balina") eğrisi | Fatura, sevk, üretim süreleri, tahsilat gecikmesi — çoğu bizde var | A/B |
| 2 | **Taban/hedef fiyat bandı + endeks bağlı sürşarj** | Bandın altı onaya gider (taban altı onayı zaten var) | Endeks fiyatı (HRC/LME/hurda) girişi | B/C (%1 fiyat ≈ %8 faaliyet kârı, model hesabı) |
| 3 | **Teklif kazanma/kaybetme analizi** | Kayıp nedeni + fiyat → kazanma olasılığı | Teklif kayıp nedeni zaten tutuluyor | A (yöntem) |
| 4 | **Mükerrer fatura kontrolü** (kural tabanlı) | Aynı tedarikçi + tutar + tarih + benzer numara | Alış faturası | B (APQC medyan %1,5 hatalı/mükerrer) |
| 5 | **Yaşlanan/atıl stok + istatistiksel temel tahmin** | Stok yaşı, dönüş hızı; Croston/üstel düzeltme; planlamacı düzeltmesi gerekçeli ve ölçülür | Stok hareketleri | A (M5: basit modele göre fark küçük) |
| 6 | **Tahsilat önceliklendirme** | Gecikme olasılığına göre tahsilat listesi; karar insanda | Açık kalem, ödeme geçmişi | A/B |
| 7 | **AI copilot (semantik katman) + e-postadan teklif taslağı** | Ham SQL değil, şemalı araç; kaynak göstermeyen yanıt engellenir | AI treni | A/C |

Politika gerektirenler (fiyat tabanı, kredi limiti, emniyet stoku hizmet seviyesi, hedge) yapılandırılabilir kalır ve karar
ister. "Piyasa zamanlaması" motoru ve dijital ikiz önerilmez (D).

## 8. AI treniyle bağlantı

`AI_TREN_PLANI_v2.md` bu analizin 4, 7, 9 ve 10. boşluklarını doğrudan kullanır. Önerilen sıra:

- **Önce veri ve deterministik hesap:** cep marjı, rapor merkezi, iş kutusu. AI bunların üzerine **şemalı okuma aracı**
  olarak gelir. Gerekçe: Spider 2.0 ve FinanceBench, ham SQL ve serbest metin yanıtın kurumsal veride güvenilmez
  olduğunu gösteriyor (A).
- **Belge okuma:** gelen alış faturası ve sipariş e-postası alan bazlı güven skoru ve aritmetik çapraz kontrolle okunur;
  güven düşükse insana gider.
- **Ajanlar dar ve geri alınabilir işlerle sınırlı kalır:** yalnız taslak hazırlarlar; kritik komut insan onayından
  geçer (Gartner: ajan projelerinin %40'tan fazlası iptal riski, B).

## 9. Önerilen yol haritası

| Dalga | İçerik | Bağımlılık / kapı |
|---|---|---|
| **0 — bu tren kapanınca** | (a) İşletme rolleri ve yetki dağıtımı; (b) gerçek sorgulu ana sayfa sayaçları; (c) tek iş kutusu: tüm onay türleri | (a) insan kapısı |
| **1** | Banka ekstresi içe alma + eşleştirme; fatura, alış siparişi ve teklif PDF'leri; genel liste dışa aktarımı + kayıtlı görünüm; yaşlanan stok raporu; mükerrer fatura kontrolü | — |
| **2** | Ürün kartında GTİP + tevkifat kategorisi, tevkifat iade takibi; e-Fatura/e-Arşiv/e-İrsaliye entegratör bağlantısı | Mali müşavir teyidi, OPEN-016 (sağlayıcı, kimlik bilgisi: insan kapısı) |
| **3** | Kalite: sertifika kaydı, etikete bağlama, miras, sevkte ek; kalite blokesi | W3-KALITE kararları |
| **4** | Cep marjı / hizmet maliyeti + müşteri kârlılık eğrisi; fiyat ekstra/sürşarj tablosu + taban bandı; teklif kazanma analizi | Golden (SAT-D431 yöntemi) |
| **5** | Raf/bin + el terminali PWA; ZPL etiket; hat başı ekranı; artık eşleştirme önerisi | Donanım kararları |
| **6** | Nakit planı, tahsilat önceliği, kur farkı değerlemesi, masraf merkezi, RFQ + tedarikçi karnesi | OPEN-011 |
| AI treni | AI-11..AI-29, Dalga 0–1 verisi üzerine | AI-D kararları |

## 10. Karar bekleyen sorular (önerili)

| # | Soru | Öneri |
|---|---|---|
| G1 | Sıradaki tren ne olsun: AI treni mi, yoksa önce Dalga 0–1 (roller, ana sayfa, iş kutusu, banka, PDF, raporlar) mi? | Önce Dalga 0–1, AI'yla paralel. AI bu veriye dayanır |
| G2 | İşletme rollerini kim, nasıl tanımlar? | Rol önerisi taslağını hazırlayayım; sen onayla (insan kapısı) |
| G3 | e-Belge entegrasyonu: hangi özel entegratör? | Sağlayıcıyı sen seç; biz adaptörü sağlayıcıdan bağımsız yazalım (OPEN-016) |
| G4 | Muhasebe Mikro'da mı kalsın? | İlk yılda kalsın; ERP fiş ve KDV hazırlık verisini aktarsın |
| G5 | Kalite/sertifika modülü ne zaman? | Dalga 3; müşteri sertifika istiyorsa öne alınsın |
| G6 | Fiyat ekstra ve sürşarj tablosu kullanılıyor mu (kalınlık/genişlik/kalite ekstrası, endeks)? | Kullanılıyorsa Dalga 4'te, golden'la |
| G7 | Dilme/kesme/kaplama e-İrsaliyede "imal" sayılıyor mu; inşaat demiri ticareti var mı? | Mali müşavire sor; ERP'de parametre olsun |
| G8 | Depoda el terminali ve kantar cihaz bağlantısı istiyor musun? | Evet, Dalga 5; donanım seçimi sende |

## 11. Seçili kaynaklar

- Metal ERP'leri: epicor.com (metal service centers), infor.com (metal fabrication), help.sap.com (Mill Products),
  sikich.com (D365 outside processing), crowe.com (metals), invera STRATIX/TOLL bültenleri, rolledalloys.com (alaşım
  sürşarjı), blog.projectmaterials.com (EN 10204).
- Türkiye: gib.gov.tr (e-İrsaliye GTİP 72/73), turmob.org.tr (589 sayılı tebliğ, 2026 hadleri), defterbeyan.gov.tr (KDV
  tevkifat kılavuzu), qnbesolutions.com.tr (Ba-Bs), ismmmo.org.tr (enflasyon düzeltmesi), roedl.com.tr (VERBİS).
- Karar destek: McKinsey "The power of pricing"; Kaplan ve Narayanan (2001) müşteri kârlılığı; M5 (IJF 2022);
  Fildes ve ark. (2009); Lessmann ve ark. (EJOR 2015); CSPUL derlemeleri (EJOR 2014, 2026); Spider 2.0 (ICLR 2025);
  FinanceBench; APQC mükerrer ödeme; AHRQ alarm yorgunluğu; Gartner ajan tahmini (2025).
