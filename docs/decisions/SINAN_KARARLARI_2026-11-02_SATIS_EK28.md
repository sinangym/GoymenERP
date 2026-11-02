# Sinan Kararları — 2026-11-02 (Satış: belge tarihinden önceki sevklerde istisna onayı, müdür rolleri, satış ayarlarını değiştirme)

Bu belge önceki karar dosyalarını silmez; SAT-D186…SAT-D190'ı ekler. EK27'nin "Açık kalanlar" bölümündeki işletme sorusunu
ve EK20'nin "muhasebe müdürü ve satış müdürü ayrı personalar mı?" açık kalanını Sinan'ın cevaplarıyla karara bağlar.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Tek "Müdür" personası modül müdürlerine ayrılır (SAT-D187, SAT-D188):** bugüne kadar yetki verilen tek "Müdür" personası
  yerine her modülün kendi müdürü gelir (Satış Müdürü, Muhasebe Müdürü, Satın Alma Müdürü, Üretim Müdürü …). Bir kişiye
  birden fazla müdürlük verilebilir. Ayrıca **Genel Müdür** rolü kalır ve bütün müdür onaylarını yapabilir; Admin de
  hepsini yapabilir. Bu, önceki kararlardaki her "Müdür" hücresini değiştirir. Etkilenen kararlar:
  - persona kartının 19 Eylül §4 tanımı ("Müdür — siparişleri onaylayabilir");
  - Satış: SAT-D1, D2, D3, D4, D6, D24, D37, D44, D61, D62, D63, D67, D99, D110, D117 ve D143…D153, D158, D159, D168,
    D169, D179, D181;
  - Alış: ALS-D3 (alış siparişi onayı) ve ALS-D6 (kur düzeltmesi, "Satış'taki gibi").
  EK20'deki "müdür sınıfı = Müdür + Admin" ara okuması da kalkar.
- **Vergi ayarı önerisinin onayı daralır:** EK20 soru 9'da Sinan bu onayı "Müdür, admin, muhasebe müdürü, satış müdürü, gibi
  tüm müdürler" için yazmıştı. Soru 4'e göre vergi/muhasebe yetkileri artık Muhasebe Müdürü'ndedir; satış müdürü bu onayı
  vermez. Genel Müdür ve Admin verebilir (soru 9).
- **Kur düzeltmesi daralır:** EK24 SAT-D169'da Sinan "Muhasebe ve Müdürler" demişti. Soru 8'e göre müdürlerden yalnız
  Muhasebe Müdürü düzeltir; Muhasebe ve Admin aynen kalır. Genel Müdür de düzeltebilir (soru 9).
- **EK27 "Açık kalanlar":** belge tarihinden önceki sevklerde istisna için işletme kuralı artık muhasebe müdürünün onayıdır
  (SAT-D186). Genel hukuki soru (bu sevklere istisna uygulanabilir mi) mali müşavirde açık kalır; müşavirin görüşü
  onaylayanın dayanağıdır.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) yazdığı bir
mesaj ve dört turda sekiz seçmeli soruya verdiği cevaplar. Mesaj, koordinatörün durum özetindeki müşavir sorusuna serbest
metinle verilmiş bir cevaptır. Soru 6'nın cevabı da Sinan'ın kendi metnidir. İkisi aynen aktarılmıştır. Seçmeli sorularda
Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi. Sorular 6–9, docs review'larının "genel Müdür kalkınca
satış ve muhasebe dışındaki yetkiler ne olacak?" bulgusu üzerine soruldu.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | (Koordinatörün özetinden) Müşavire iletmen gereken bir konu var: ihraç kayıtlı ya da istisna belgeli alıcının belgesi sonradan gelirse, belge tarihinden önceki sevklerin faturasında KDV %0 istisnası uygulanabilir mi? | Sinan'ın mesajı, aynen: "Şu an sana sorum yok. Müşavire iletmen gereken bir konu var: ihraç kayıtlı ya da istisna belgeli alıcının belgesi sonradan gelirse, belge tarihinden önceki sevklerin faturasında KDV %0 istisnası uygulanabilir mi? Muhasebe müdürü onaylasın, onaylayana kadar sipariş alınabilsin ama sevk çıksın fatura kesilemesin" | — (serbest metin) |
| 2 | 'Muhasebe müdürü' ve 'satış müdürü' sistemde ayrı roller mi olsun, yoksa ikisi de 'Müdür' rolünün unvanı mı? (Şu an tek bir Müdür rolü var; muhasebe müdürünün onayı gereken yerde Müdür ve Admin onaylıyor.) | Ayrı roller olsun (Önerilen) | "Muhasebe Müdürü ve Satış Müdürü ayrı roller olur. Vergi ayarı ve istisna onayını Muhasebe Müdürü, satış onaylarını Satış Müdürü verir; Admin her ikisini yapabilir." (seçilmeyenler: "Tek Müdür rolü kalsın"; "Ayrı, ama Müdür de yapsın") |
| 3 | Satış ayarlarını (teklif kayıp nedenleri listesi, varsayılan teklif geçerlilik günü, teslim toleransı yüzdeleri) Ayarlar ekranından kim değiştirebilsin? | Müdür ve Admin (Önerilen) | "Değişiklik doğrudan geçerli olur; kim, ne zaman, neyi değiştirdi kayıt altında." (seçilmeyenler: "Yalnız Admin"; "Satış Uzmanı da") |
| 4 | Muhasebe Müdürü ve Satış Müdürü ayrı roller olacak. Bugünkü tek 'Müdür' rolüne verdiğin yetkiler ne olsun? (Ör. vergi ayarını doğrudan değiştirme, iskonto/taban fiyat onayı, risk istisnası onayı.) | İşe göre bölünsün (Önerilen) | "Vergi/muhasebe yetkileri (vergi ayarı, vergi onayı, istisna onayı) Muhasebe Müdürü'ne; satış yetkileri (iskonto, taban fiyat, risk istisnası, sipariş onayı) Satış Müdürü'ne geçer. Genel 'Müdür' rolü kalkar; Admin hepsini yapabilir." (seçilmeyenler: "Genel Müdür de kalsın"; "Müdür = Satış Müdürü") |
| 5 | Satış ayarlarını (kayıp nedenleri, geçerlilik günü, tolerans) 'Müdür ve Admin' değiştirsin dedin. Roller ayrılınca buradaki Müdür hangisi? | Satış Müdürü (Önerilen) | "Satış ayarlarını Satış Müdürü ve Admin değiştirir." (seçilmeyen: "İki müdür de") |
| 6 | Genel 'Müdür' rolü kalkınca satış ve muhasebe dışındaki Müdür yetkileri (alış siparişi onayı, üretim, muhasebe dönemi gibi) ne olsun? | Kendi metni: "Her müdürün yetkileri kendi modülünde kalsın gibi ayrıca bir kişiye birden fazla müdürlük verilebilsin gibi mi birde genel müdür bu bağlamda yine kalsın bence" | — (seçenekler: "Her modülün kendi müdürü (Önerilen)" — "Satın Alma Müdürü, Üretim Müdürü gibi roller o modüllerin kararlarında tanımlanır; tanımlanana kadar bu yetkileri Admin yapar."; "Genel Müdür bunlar için kalsın"; "Alış onayı Muhasebe Müdüründe") |
| 7 | İskonto limit gruplarında 'Müdür' adında bir grup var (o gruptaki kişi daha yüksek iskonto verebiliyor). Roller ayrılınca bu grup kimi kapsın? | Satış Müdürü (Önerilen) | "Grubun adı Satış Müdürü olur; limiti Satış Müdürü kullanır." (seçilmeyen: "İki müdür de") |
| 8 | Döviz kurunu gerekçeyle düzeltme için 'Muhasebe ve Müdürler' demiştin. Roller ayrılınca hangi müdür? | Muhasebe Müdürü (Önerilen) | "Kuru Muhasebe, Muhasebe Müdürü ve Admin düzeltir." (seçilmeyen: "İki müdür de") |
| 9 | Anladığım: her modülün kendi müdürü olacak (Satış, Muhasebe, Satın Alma, Üretim Müdürü...), yetkisi kendi modülünde kalacak, bir kişiye birden fazla müdürlük verilebilecek ve ayrıca bir 'Genel Müdür' rolü kalacak. Genel Müdür ne yapabilsin? | Tüm müdür onayları (Önerilen) | "Genel Müdür, bütün modüllerde müdürlerin yaptığı onay ve değişiklikleri yapabilir (Admin'in sistem yönetimi yetkileri hariç)." (seçilmeyenler: "Yalnız görür"; "Yalnız sipariş onayı") |

Soru 4'teki "Genel 'Müdür' rolü kalkar" ifadesinin yerine soru 6 ve 9'daki son cevaplar geçer: genel Müdür kalır ve adı
**Genel Müdür**dür.

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D186 | Belge tarihinden önceki sevklerde istisna | İhraç kayıtlı ya da istisna belgeli alıcının belgesi sonradan geldiğinde, belge tarihinden **önce** yapılmış sevklerin faturasında KDV %0 istisnasının uygulanmasını **Muhasebe Müdürü** onaylar (soru 1); Genel Müdür (soru 9) ve Admin (soru 2) de onaylayabilir. Onaylanana kadar **sipariş alınabilir ve sevk çıkabilir**, ama o sevklerin **faturası kesilemez** (soru 1). Belge tarihinden sonraki sevkler SAT-D147/D185 gereği onay beklemeden faturalanabilir. Uygulama ayrıntısı (geri alınabilir): onay sipariş bazında verilir, yalnız o siparişin belge tarihinden önceki sevklerini kapsar, gerekçe ve audit ile kaydedilir; onayı isteyen kişi onaylayamaz. Onay verilmezse o sevklerin nasıl faturalanacağı ayrı karar ister; o zamana kadar fatura bekler (fail-closed) | Fatura kapısı (API-32), onay listesi (MOD-10), persona kartı |
| SAT-D187 | Müdür rolleri | Her modülün **kendi müdürü** olur ve yetkisi kendi modülünde kalır; **bir kişiye birden fazla müdürlük verilebilir** (soru 6). Satış Müdürü ve Muhasebe Müdürü ayrı rollerdir (soru 2). Ayrıca **Genel Müdür** rolü kalır ve bütün modüllerde müdürlerin yaptığı onay ve değişiklikleri yapabilir, Admin'in sistem yönetimi yetkileri hariç (soru 6 ve 9). **Admin** müdürlerin yaptığı her şeyi yapabilir (soru 2 ve 4). Satın Alma Müdürü, Üretim Müdürü gibi öteki modül müdürleri o modüllerin kararlarında tanımlanır; tanımlanana kadar o modüldeki müdür yetkilerini Genel Müdür ve Admin kullanır (uygulama ayrıntısı, geri alınabilir) | Persona kartı (`docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md`), Alış ve Üretim kararları |
| SAT-D188 | Müdür yetkilerinin bölünmesi | Önceki kararlarda "Müdür"e verilen yetkiler **işe göre bölünür** (soru 4): vergi/muhasebe yetkileri **Muhasebe Müdürü**'ne, satış yetkileri **Satış Müdürü**'ne geçer; öteki modüllerin yetkileri kendi modül müdürüne geçer (soru 6). Her hücrede Genel Müdür ve Admin de yetkilidir (soru 9). "Müdür" iskonto limit grubu **Satış Müdürü** grubudur (soru 7). Kur düzeltmesi **Muhasebe Müdürü**'ndedir (soru 8). Uygulama ayrıntısı (geri alınabilir), sınıflandırma: **muhasebe** — SAT-D145 vergi ayarları ve kalem tipi yönetimi, SAT-D146/D158 vergi ayarı önerisi ve alıcı durumu listesi, SAT-D148 değer girişi, SAT-D149 kalem tipi, SAT-D169 kur düzeltmesi, SAT-D181 oran listeleri, SAT-D186 istisna onayı; **satış** — SAT-D1 taban fiyat, SAT-D2/D37 iskonto ve iskonto istisnası onayı, SAT-D3 miktar revizyonu onayı, SAT-D4 iptal, SAT-D6 satış siparişi onayı, SAT-D24/D44 süresi geçmiş teklif onayı, SAT-D61…D63/D67/D117 iskonto limit grubu, SAT-D144 başlık, SAT-D151 fiyat yazma, SAT-D153 risk istisnası onayı, SAT-D159 risk istisnası talebi, SAT-D168 fazla teslim onayı ve kapatma, SAT-D179 kaynak planı; **iki müdüre de** — okuma yetkileri (SAT-D143 raporlar, SAT-D150 fiyat okuma, SAT-D110 faturaya hazır sevkiyatlar); **alış** — ALS-D3 alış siparişi onayı (Satın Alma Müdürü). Soru 4'teki "sipariş onayı" satış siparişi onayıdır. Bir hücre bu sınıflara açıkça girmiyorsa (ör. SAT-D99) persona kartı PR'ında NEEDS-SINAN yazılır | Persona kartı, izin manifestleri |
| SAT-D189 | Satış ayarlarını değiştirme | Teklif kayıp nedenleri listesi, varsayılan teklif geçerlilik günü ve teslim toleransı yüzdelerini **Satış Müdürü ve Admin** değiştirir (soru 3 ve 5); Genel Müdür de değiştirebilir (soru 9). Değişiklik doğrudan geçerli olur; kim, ne zaman, neyi değiştirdi kayıt altına alınır (soru 3). Kapsam bu üç ayarla sınırlıdır; iskonto limitleri ve limit grupları SAT-D65'teki gibi kalır | Ayarlar ekranları (SAT-D5, SAT-D24, SAT-D39), persona kartı |
| SAT-D190 | Genel Müdür | **Genel Müdür**, bütün modüllerde müdürlerin yaptığı onay ve değişiklikleri yapabilir; Admin'in sistem yönetimi yetkileri (kullanıcı, rol, sistem ayarı) bu kapsamda değildir (soru 9) | Persona kartı |

## Açık kalanlar

- Bu kararların persona kartına işlenmesi ayrı bir PR'dır; o PR'da önceki "Müdür" hücreleri SAT-D188'e göre yeniden
  dağıtılır. SAT-D99 gibi sınıfı açık olmayan hücreler orada NEEDS-SINAN olarak sorulur.
- Satın Alma, Üretim ve öteki modüllerin müdür rolleri o modüllerin karar dosyalarında tanımlanır (Alış ve Üretim şeritleri).
  ALS-D3 ve ALS-D6'nın yeni rollere göre güncellenmesi Alış şeridinin işidir.
- SAT-D186'nın genel hukuki sorusu mali müşavirde açıktır (SAT-D148).

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Persona tanımı karardır; personanın gerçek kişilere
  ve rollere atanması (grant) her zaman insan kapısındadır.
