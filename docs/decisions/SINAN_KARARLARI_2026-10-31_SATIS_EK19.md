# Sinan Kararları — 2026-10-31 (Satış: vergi masterı kapsamı, tevkifat eşiği, senaryo listesi, karma vade, kabul bekleyen sipariş)

Bu belge önceki karar dosyalarını silmez; SAT-D133…SAT-D138'u ekler. PR #866 (MOD-03 KDV/tevkifat masterı), PR #860 (Cari
e-belge), PR #864 (vade modeli) ve PR #865 (S6 raporlar) uygulamalarının ve review'larının açtığı soruları cevaplar.
SAT-D73…SAT-D76 ve SAT-D84 (EK12), SAT-D93 (EK13), SAT-D119/D120 (EK17), SAT-D129 (EK18) ve DEC-SAL-008 geçerlidir.

**Kaynak:** Sinan'ın 31 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) iki tur seçmeli
soru cevabı. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi; bir soruda Sinan kendi metnini yazdı, o metin aynen
verilir.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| KDV ve tevkifat oran tablosu (Ayarlar) şirket bazında mı tutulsun, yoksa 3 şirkette ortak mı? (Oranlar vergi mevzuatından gelir.) | Her şirket ayrı | "Her şirketin kendi tablosu olur; bir şirkette girilen oran diğerine yansımaz (şu anki kod)." (seçilmeyen: "3 şirkette ortak (Önerilen)" — "Oranlar mevzuat olduğu için bir kez girilir, üç şirket aynı tabloyu kullanır. Üç kez aynı değeri girme ve tutarsızlık riski olmaz.") |
| Ürünün KDV sınıfı ve tevkifat sınıfı (örn. demir-çelik) nerede tutulsun? | Kendi metni: "Bunu anlamadım ama tam olarak bence her şirkette ayrı , ve kdv tevkifat tablosu ürünlerin içine değil ayrı bir tabloya yada sayfada mu tutulacak rapor kaydı" | — (seçenekler: "Ürün kartında tek (Önerilen)" — "Ürünün vergi sınıfı ürüne ait bir özelliktir; bir kez girilir, üç şirkette aynı geçerli olur."; "Her şirkette ayrı" — "Aynı ürün için her şirket kendi vergi sınıfını girer (şu anki kod).") |
| Tevkifatın tutar eşiği (VUK 232: belirli tutarın altındaki faturada tevkifat uygulanmaz; 2026 için araştırmada 12.000 TL) ne zaman devreye girsin? | Şimdi, değer Ayarlar'dan | "Eşik tarihli bir Ayarlar değeri olarak eklenir; sen veya mali müşavir girene kadar boş kalır ve fatura 'kaynak yok' uyarısıyla bekler." (seçilmeyen: "Fatura dalgasında") |
| e-Fatura senaryo listesinde standart dört kod (TEMELFATURA, TİCARİFATURA, e-Arşiv, İHRACAT) yeterli mi, yoksa başkaları da eklensin mi? | Bu dördü yeterli | "Şimdilik dört standart kod kalır; gerektiğinde Ayarlar'dan eklenir." (seçilmeyenler: "KAMU eklensin", "YOLCUBERABERFATURA eklensin") |
| İçeride onaylanmış ama MÜŞTERİ henüz kabul etmemiş sipariş (şu an sevk ve fatura yapılamıyor), 'açık sipariş tonajı' raporunda nasıl görünsün? | Ayrı gösterilsin | "Açık tonaja eklenmez; 'müşteri onayı bekliyor' olarak ayrı bir sütun/satırda görünür. Böylece hem gerçek açık miktar hem bekleyen teklif hacmi bilinir." (seçilmeyenler: "Hiç görünmesin", "Açık sayılsın" — "İç onay yeterli; müşteri kabulü beklenirken de açık tonaja eklenir (şu anki rapor).") |
| Taksitli vadede taksitler farklı vade tiplerinde olabilsin mi? (Örn. 1. taksit nokta vade, 2. taksit sevk vadesi — 'karma' plan.) | Evet, karma olabilsin | "Her taksit kendi tipini (nokta/sevk) ve gün sayısını taşır; kilitli DEC-SAL-008'deki 'karma' model korunur." (seçilmeyen: "Hayır, hepsi aynı tip") |

Ürün vergi sınıfı sorusundaki serbest metinde Sinan'ın sorduğu ("KDV/tevkifat tablosu ürünlerin içine değil ayrı bir tabloya
mı tutulacak") aynı oturumda şöyle cevaplandı: oran tablosu ürünlerin içinde değil, Ayarlar'da ayrı bir sayfada tutulur;
ürün kartında yalnız ürünün sınıfı seçilir, oran o sınıfa göre ayrı tablodan okunur.

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D133 | Vergi masterının kapsamı | KDV ve tevkifat oran/kod tabloları (Ayarlar) **şirket bazında** tutulur; bir şirkette girilen değer diğerine yansımaz. SAT-D73'teki "oran Ayarlar'da sürümlü tabloda" kuralı her şirket için ayrı uygulanır | Ayarlar vergi masterı (PR #866) |
| SAT-D134 | Ürün vergi sınıfı | Ürünün KDV sınıfı ve tevkifat sınıfı **şirket bazında** tutulur. Oran tabloları ürün kartının içinde değil, Ayarlar'da ayrı sayfada (SAT-D133) tutulur; ürün kartında yalnız sınıf seçilir | Ürün vergi sınıflandırması (PR #866) |
| SAT-D135 | Tevkifat tutar eşiği | VUK 232 tevkifat tutar eşiği **şimdi**, şirket bazında efektif tarihli bir Ayarlar değeri olarak eklenir; değer boş başlar ve Sinan veya mali müşavir girer. Değer yokken eşik "kaynak yok" döner ve fatura (API-32) bu uyarıyla bekler; hiçbir eşik tahmin edilmez | Ayarlar vergi masterı (PR #866), fatura taslağı |
| SAT-D136 | e-Fatura senaryo listesi | SAT-D120'deki standart dört kod (TEMELFATURA, TİCARİFATURA, e-Arşiv senaryosu, İHRACAT) şimdilik yeterlidir; başka kod (KAMU, YOLCUBERABERFATURA vb.) eklenmez, gerektiğinde Ayarlar'dan eklenir | Ayarlar senaryo kod listesi (PR #860) |
| SAT-D137 | Kabul bekleyen sipariş raporda | Açık sipariş tonajı raporunda (KPI No 124) içeride onaylanmış ama müşteri kabulü bekleyen miktar **açık tonaja eklenmez**; "müşteri onayı bekliyor" olarak ayrı gösterilir. Açık miktar, müşterinin kabul ettiği son onaylı revizyona göre ölçülür (uygulama ayrıntısı, geri alınabilir: satır portu / fatura kapısıyla aynı kabul kuralı) | API-41 (PR #865) |
| SAT-D138 | Karma vade | Taksitli vadede her taksit kendi vade tipini (nokta vade veya sevk vadesi, SAT-D129) ve gün sayısını taşır; farklı tipler bir arada olabilir (kilitli DEC-SAL-008'deki "karma" model korunur) | Vade modeli (PR #864) |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
- Vergi oranları, kodlar ve tevkifat eşiği değerleri mali müşavir/YMM teyidiyle girilir; bu belge hiçbir değer koymaz.
