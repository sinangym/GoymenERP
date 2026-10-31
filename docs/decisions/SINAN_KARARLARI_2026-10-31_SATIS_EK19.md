# Sinan Kararları — 2026-10-31 (Satış: vergi masterı kapsamı, tevkifat eşiği, senaryo listesi, karma vade, kabul bekleyen sipariş)

Bu belge önceki karar dosyalarını silmez; SAT-D133…SAT-D140'ı ekler. PR #866 (MOD-03 KDV/tevkifat masterı), PR #860 (Cari
e-belge), PR #864 (vade modeli) ve PR #865 (S6 raporlar) uygulamalarının ve review'larının açtığı soruları cevaplar.
SAT-D75, SAT-D76 ve SAT-D84 (EK12), SAT-D119/D120 (EK17), SAT-D129 (EK18) ve DEC-SAL-008 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Ürün tarafı:** SAT-D134 ve SAT-D139, EK12 SAT-D73 ve SAT-D82'deki "KDV sınıfı ürün kartında seçilir" kuralını, SAT-D74'teki
  "ürünün tevkifat kodu" girdisini ve EK13 SAT-D93'teki ürün girdilerini (SAT-D82 KDV sınıfı, SAT-D74 tevkifat kodu) değiştirir:
  ürün kartında vergi sınıfı veya istisna tutulmaz, yalnız kalem tipi (demir / hurda / hizmet) tutulur; KDV ve tevkifatın ürün
  tarafı bu tipe göre Ayarlar'daki varsayılandan gelir. SAT-D73'ün "oran Ayarlar'da sürümlü tabloda" kısmı geçerlidir.
- **Alıcı tarafı:** SAT-D140, SAT-D74'teki "alıcının tevkifata tabi olması" ve SAT-D93'teki "müşteri durumu ve belgeleri"
  girdisinin **kaynağını** değiştirir: bu durum cari kartından otomatik okunmaz, **her siparişte sipariş bazında seçilir**.
  EK13 uygulama notundaki "müşteri durumu ve belgeler Cari kartında geçerlilik süreli tutulur" ifadesi bu karar için geçerli
  değildir. SAT-D74'ün ve SAT-D93'ün kuralı (alıcı tevkifata tabi değilse tevkifat uygulanmaz; belge/istisna KDV'yi değiştirir)
  sipariş bazında seçilen duruma göre uygulanır.

**Kaynak:** Sinan'ın 31 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) iki tur seçmeli
soru cevabı ve docs review'larının önerdiği iki teyit turu (beş soru). Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi;
üç soruda Sinan kendi metnini yazdı, o metin aynen verilir.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| KDV ve tevkifat oran tablosu (Ayarlar) şirket bazında mı tutulsun, yoksa 3 şirkette ortak mı? (Oranlar vergi mevzuatından gelir.) | Her şirket ayrı | "Her şirketin kendi tablosu olur; bir şirkette girilen oran diğerine yansımaz (şu anki kod)." (seçilmeyen: "3 şirkette ortak (Önerilen)" — "Oranlar mevzuat olduğu için bir kez girilir, üç şirket aynı tabloyu kullanır. Üç kez aynı değeri girme ve tutarsızlık riski olmaz.") |
| Ürünün KDV sınıfı ve tevkifat sınıfı (örn. demir-çelik) nerede tutulsun? | Kendi metni: "Bunu anlamadım ama tam olarak bence her şirkette ayrı , ve kdv tevkifat tablosu ürünlerin içine değil ayrı bir tabloya yada sayfada mu tutulacak rapor kaydı" | — (seçenekler: "Ürün kartında tek (Önerilen)" — "Ürünün vergi sınıfı ürüne ait bir özelliktir; bir kez girilir, üç şirkette aynı geçerli olur."; "Her şirkette ayrı" — "Aynı ürün için her şirket kendi vergi sınıfını girer (şu anki kod).") |
| Tevkifatın tutar eşiği (VUK 232: belirli tutarın altındaki faturada tevkifat uygulanmaz; 2026 için araştırmada 12.000 TL) ne zaman devreye girsin? | Şimdi, değer Ayarlar'dan | "Eşik tarihli bir Ayarlar değeri olarak eklenir; sen veya mali müşavir girene kadar boş kalır ve fatura 'kaynak yok' uyarısıyla bekler." (seçilmeyen: "Fatura dalgasında") |
| e-Fatura senaryo listesinde standart dört kod (TEMELFATURA, TİCARİFATURA, e-Arşiv, İHRACAT) yeterli mi, yoksa başkaları da eklensin mi? | Bu dördü yeterli | "Şimdilik dört standart kod kalır; gerektiğinde Ayarlar'dan eklenir." (seçilmeyenler: "KAMU eklensin", "YOLCUBERABERFATURA eklensin") |
| İçeride onaylanmış ama MÜŞTERİ henüz kabul etmemiş sipariş (şu an sevk ve fatura yapılamıyor), 'açık sipariş tonajı' raporunda nasıl görünsün? | Ayrı gösterilsin | "Açık tonaja eklenmez; 'müşteri onayı bekliyor' olarak ayrı bir sütun/satırda görünür. Böylece hem gerçek açık miktar hem bekleyen teklif hacmi bilinir." (seçilmeyenler: "Hiç görünmesin", "Açık sayılsın" — "İç onay yeterli; müşteri kabulü beklenirken de açık tonaja eklenir (şu anki rapor).") |
| Taksitli vadede taksitler farklı vade tiplerinde olabilsin mi? (Örn. 1. taksit nokta vade, 2. taksit sevk vadesi — 'karma' plan.) | Evet, karma olabilsin | "Her taksit kendi tipini (nokta/sevk) ve gün sayısını taşır; kilitli DEC-SAL-008'deki 'karma' model korunur." (seçilmeyen: "Hayır, hepsi aynı tip") |
| (Teyit) Aynı ürün (örn. 'S235 sac') üç şirkette de satılıyor. Bu ürünün vergi sınıfını (örn. 'KDV %20, demir-çelik tevkifatlı') kim, kaç kez seçsin? (Oranların kendisi zaten her şirkette ayrı tabloda.) | Kendi metni: "Bütün ürünlerde %20 kdv ve tevkifat varsayılan gelsin , hizmetlerde %20 kdv tevkifat yok varsayılan , bu durum bütün şirketlerde varsayılan olsun ama sipariş anında sipariş satır bazında değiştirilebilir olsun ve bunun ayarıda ayarlarda olsun" | — (seçenekler: "Her şirket kendi seçsin" — "Ürünün vergi sınıfı her şirkette ayrı seçilir; GSP'de başka, GDC'de başka olabilir. Üç kez girilir (şu anki kod; geçen cevabın bu yöndeydi)."; "Bir kez, üçünde ortak" — "Ürün kartında bir kez seçilir, üç şirkette aynı sınıf geçerli olur. Her şirket yine kendi oran tablosundan o sınıfın oranını okur.") |
| Ürünlerde varsayılan gelecek tevkifat oranı ne olsun? (Araştırmada demir-çelik için 5/10, hurda için 7/10 çıktı; mali müşavir teyidi gerekir.) | Kendi metni: "Demirde %20 kdv tevkifat  5/10, hurdada kdv 0 tevkifat ve kdv hiç yok" | — (seçenekler: "5/10 (demir-çelik)" — "Ürünlerde varsayılan tevkifat 5/10 gelir; Ayarlar'dan değiştirilebilir, satırda değiştirilebilir."; "Mali müşavir girsin (Önerilen)" — "Ayarlar'da 'tevkifat var' varsayılanı olur ama oran boş başlar; mali müşavir/sen girene kadar fatura 'kaynak yok' ile bekler.") |
| Varsayılanın dışında kalan ürünler için (örn. hurda farklı tevkifat, ya da KDV %1/%10 olan bir ürün) ürün kartında kalıcı bir istisna tanımlanabilsin mi? | Hayır, yalnız satırda | "Ürün kartında vergi bilgisi tutulmaz; varsayılan dışı her durum sipariş satırında elle değiştirilir." (seçilmeyen: "Evet, ürün kartında istisna (Önerilen)" — "Ürün kartında istenirse farklı KDV/tevkifat sınıfı seçilir; o ürün her siparişte bununla gelir. Seçilmezse varsayılan geçerlidir.") |
| Sistem, sipariş satırındaki bir kalemin DEMİR mi (KDV %20 + tevkifat 5/10), HURDA mı (KDV yok, tevkifat yok), HİZMET mi (KDV %20, tevkifat yok) olduğunu nereden bilsin? | Ürün kartındaki tipten | "Ürün kartında yalnız 'demir / hurda / hizmet' tipi seçilir (vergi oranı değil). Siparişte KDV ve tevkifat bu tipe göre Ayarlar'daki varsayılandan gelir; satırda değiştirilebilir." (seçilmeyen: "Satırda seçilsin" — "Ürün kartında hiçbir tip tutulmaz; satıcı her satırda demir/hurda/hizmet seçer, KDV/tevkifat ona göre gelir.") |
| Demirde varsayılan KDV %20 + tevkifat 5/10 geliyor. Müşteri nihai tüketici, tevkifata tabi olmayan alıcı, ihraç kayıtlı ya da istisna belgeli ise sistem bunu cari kartına bakarak otomatik düzeltsin mi? | Kendi metni: "Siparişte, sipariş bazında seçilebilir, çünkü müşterilerin siparişleri bazında farklı olabilir" | — (seçenekler: "Evet, otomatik düzeltsin (Önerilen)" — "Cari kartındaki müşteri durumu (nihai tüketici, belgeli, ihraç kayıtlı vb.) tevkifatı/KDV'yi otomatik değiştirir; satırda yine elle değiştirilebilir. Önceki kararlar (SAT-D74/D93) korunur."; "Hayır, yalnız satırda elle" — "Varsayılan her müşteride aynı gelir; müşteri durumuna göre gereken değişiklik satıcı tarafından satırda elle yapılır.") |

Ürün vergi sınıfı sorusundaki serbest metinde Sinan'ın sorduğu ("KDV/tevkifat tablosu ürünlerin içine değil ayrı bir tabloya
mı tutulacak") aynı oturumda şöyle cevaplandı: oran tablosu ürünlerin içinde değil, Ayarlar'da ayrı bir sayfada tutulur;
ürün kartında yalnız ürünün sınıfı seçilir, oran o sınıfa göre ayrı tablodan okunur. (Teyit turunda değişti: ürün kartında
sınıf değil yalnız kalem tipi seçilir — SAT-D134.) "Oran tabloları ayrı Ayarlar sayfasında" kısmı EK12 SAT-D82 ile de tutarlıdır.

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D133 | Vergi masterının kapsamı | KDV ve tevkifat oran/kod tabloları (Ayarlar) **şirket bazında** tutulur; bir şirkette girilen değer diğerine yansımaz. SAT-D73'teki "oran Ayarlar'da sürümlü tabloda" kuralı her şirket için ayrı uygulanır | Ayarlar vergi masterı (PR #866) |
| SAT-D134 | Ürün kartında kalem tipi | Ürün kartında **vergi sınıfı veya vergi istisnası tutulmaz**. Ürün kartında yalnız kalem tipi seçilir: **demir / hurda / hizmet**. KDV ve tevkifat siparişte bu tipe göre SAT-D139 varsayılanından gelir; varsayılan dışı her durum sipariş satırında değiştirilir (SAT-D93). İlk turdaki "her şirkette ayrı" cevabı Sinan'ın teyit turundaki cevabıyla bu şekle dönüşmüştür | Ürün kartı, vergi masterı (PR #866) |
| SAT-D135 | Tevkifat tutar eşiği | VUK 232 tevkifat tutar eşiği **şimdi**, şirket bazında efektif tarihli bir Ayarlar değeri olarak eklenir; değer boş başlar ve Sinan veya mali müşavir girer. Değer yokken eşik "kaynak yok" döner ve fatura (API-32) bu uyarıyla bekler; hiçbir eşik tahmin edilmez | Ayarlar vergi masterı (PR #866), fatura taslağı |
| SAT-D136 | e-Fatura senaryo listesi | SAT-D120'deki standart dört kod (TEMELFATURA, TİCARİFATURA, e-Arşiv senaryosu, İHRACAT) şimdilik yeterlidir; başka kod (KAMU, YOLCUBERABERFATURA vb.) eklenmez, gerektiğinde Ayarlar'dan eklenir | Ayarlar senaryo kod listesi (PR #860) |
| SAT-D137 | Kabul bekleyen sipariş raporda | Açık sipariş tonajı raporunda (KPI No 124) içeride onaylanmış ama müşteri kabulü bekleyen miktar **açık tonaja eklenmez**; "müşteri onayı bekliyor" olarak ayrı gösterilir. Uygulama ayrıntısı (geri alınabilir): müşteri kabulü bekleyen ve daha önce kabul edilmiş revizyonu olmayan sipariş tümüyle "müşteri onayı bekliyor" satırına gider; yeni revizyonu kabul bekleyen ama önceki revizyonu kabul edilmiş siparişte açık miktar son kabul edilen revizyona göre ölçülür ve fark "müşteri onayı bekliyor"da görünür (SAT-D113/D115 ile aynı kabul kuralı) | API-41 (PR #865) |
| SAT-D138 | Karma vade | Taksitli vadede her taksit kendi vade tipini (nokta vade veya sevk vadesi, SAT-D129) ve gün sayısını taşır; farklı tipler bir arada olabilir (kilitli DEC-SAL-008'deki "karma" model korunur) | Vade modeli (PR #864) |
| SAT-D139 | Kalem tipine göre varsayılan vergi | Ayarlar'da kalem tipine göre varsayılan KDV ve tevkifat **efektif tarihli Ayarlar verisi** olarak tutulur (hesaplayıcıda sabit değil; No 15, SAT-D82) ve **üç şirkette aynı** varsayılan uygulanır (her şirketin tablosunda aynı değer, SAT-D133; bir şirkette değiştirilen varsayılan diğerine yansımaz): **demir** → KDV %20, tevkifat 5/10; **hurda** → KDV yok, tevkifat yok; **hizmet** → KDV %20, tevkifat yok. Değerler Sinan'ın açık cevabıdır; varsayılan Ayarlar'dan değiştirilebilir ve sipariş satırında satır bazında değiştirilebilir | Ayarlar vergi masterı (PR #866), sipariş satırı vergi alanı |
| SAT-D140 | Alıcının vergi durumu sipariş bazında | Müşterinin vergi durumu (normal, nihai tüketici, tevkifata tabi olmayan alıcı, ihraç kayıtlı, istisna belgeli vb.) cari kartından otomatik okunmaz; **her siparişte sipariş bazında seçilir**, çünkü aynı müşterinin siparişleri farklı olabilir. Seçilen durum o siparişin satırlarındaki SAT-D139 varsayılanını SAT-D74/D93 kuralına göre değiştirir (ör. alıcı tevkifata tabi değilse tevkifat gelmez); satırlar yine tek tek değiştirilebilir. Uygulama ayrıntısı (geri alınabilir): yeni siparişte durum "normal" başlar; ihraç kayıtlı / istisna belgeli seçildiğinde belge bilgisi (no, tarih) siparişte istenir | Sipariş başlığı, sipariş satırı vergi alanı, API-03/API-32 |

## Uygulama notları

- SAT-D139'daki değerler araştırma notuyla (EK13: demir-çelik 5/10) uyumludur; hurdanın KDV'siz satışı için UBL-TR istisna
  kodu ve yasal dayanak (ör. KDVK 17/4-g) mali müşavir teyidiyle Ayarlar kod listesine girilir; kod yokken hurda
  satırının faturası "kaynak yok" ile bekler.
- SAT-D135 eşiği yalnız tevkifat uygulanabilen satır içeren faturayı bekletir.
- SAT-D133 kapsamına UBL-TR istisna/tevkifat kod listeleri de girer (şirket bazında); e-Fatura senaryo listesi (SAT-D120) PR
  #860'taki ayrı katalogdur.

## Açık kalanlar

- Kalem tipinin ürün kartında şirket bazında mı, ürün ana kaydında tek mi tutulacağı sorulmadı; ara durumda ürün kartının
  bugünkü kapsamında tutulur (geri alınabilir).

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
- SAT-D139'daki varsayılanlar dışında vergi oranları, kodlar ve tevkifat eşiği değerleri mali müşavir/YMM teyidiyle girilir.
