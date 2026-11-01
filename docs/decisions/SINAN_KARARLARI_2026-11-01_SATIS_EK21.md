# Sinan Kararları — 2026-11-01 (Satış: tekliften sipariş carisi, risk istisnası, e-belge yetkisi, karma vade, taksit, alıcı durum listesi)

Bu belge önceki karar dosyalarını silmez, SAT-D152…SAT-D163'ü ekler. 1 Kasım 2026'daki satış modülü genel taramasının
(backend, ekran, test denetimi) açtığı açık soruları cevaplar. EK18 (SAT-D124, SAT-D128, SAT-D138), EK19 (SAT-D140), EK20
(SAT-D141…D151), EK15/EK17 (SAT-D102, SAT-D119) ve DEC-FIN-009 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Tekliften dönen sipariş:** SAT-D152, EK18 SAT-D128'in tekliften dönen siparişe uygulanışını daraltır. SAT-D128 cari
  değişikliğine fiyatsız ve sevki başlamamış siparişte izin veriyordu. Tekliften dönen siparişte cari **hiç değişmez**.
- **Tekliften dönen siparişte kod değişikliği:** SAT-D152, PR #864'ün başlık revizyonu kodunu da değiştirir; bugün tekliften
  dönen siparişte de SAT-D128 kuralı uygulanıyor.
- **Risk istisnası türleri:** SAT-D154, 21 Ekim kararları No 32'deki **tarih aralıklı (müşteri bazlı) istisna türünü
  kaldırır**; yalnız tek sevk (belge bazlı) istisna kalır (soru 9). `SATIS_HAZIRLIK_PLANI_v1` API-07 satırındaki "veya tarih
  aralığı (≤ 30 gün)" ifadesi bu kararla geçersizdir.
- **DEC-SAL-002'nin okunuşu:** SAT-D161, kilitli DEC-SAL-002'deki "risk istisnası değişirse yeni revizyon" ifadesinin tek
  sevk istisnasını kapsamadığını Sinan'ın açık cevabıyla belirler (soru 13).
- **Satış Uzmanı'nın ilk ayrışması:** SAT-D153 ile Satış Uzmanı, Satış Destek'te olmayan bir onay iznini ilk kez alır (EK20
  SAT-D141'deki "ileride ayrıştırılabilir").
- **Alıcı durum listesi:** SAT-D158, PR #866'daki kodda sabit beş durumluk alıcı durumu kümesini Ayarlar'da tarihli bir
  listeye çevirir; her durumun vergi etkisi veri olur.
- **Karma vadede tarih:** SAT-D156, PR #864'teki ara davranışı değiştirir. Bugün karma planda sevk vadeli bir taksitin sevk
  günü yoksa **bütün plan** çözümsüz döner. Bundan sonra plan **taksit taksit** çözülür.

**Kaynak:** Sinan'ın 1 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği seçmeli
soru cevapları: altı tur, on yedi soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi. İki soruda Sinan kendi
metnini yazdı (soru 2 ve 10), bir soruda "Bunu anlamadım" dedi (soru 6). Anlaşılmayan soru somut bir örnekle yeniden soruldu (soru
9). Metinler aynen verilir.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Tekliften dönüşen bir siparişte müşteri (cari) değiştirilebilsin mi? | Hayır, teklifin carisi kalsın (Önerilen) | "Başka müşteri için sipariş iptal edilir ve yeni teklif/sipariş açılır; teklif-sipariş izi bozulmaz." (seçilmeyenler: "Normal sipariş gibi" — "Fiyatlı satır yoksa değişir, yeniden onaya gider (bugünkü davranış)."; "Yalnız ilk onaydan önce" — "Sipariş hiç onaylanmadıysa değiştirilebilir, sonra değişmez.") |
| 2 | Risk limiti aşıldığında sevke izin veren 'risk istisnası' nasıl olsun ve kim onaylasın? (Şu an limit aşımı siparişi durdurmuyor; bu özellik yok.) | Kendi metni: "Satış uzmanı ve müdür" | — (seçenekler: "Sevk ya da 30 güne kadar, Müdür onaylar (Önerilen)" — "İstisna tek sevk için ya da en fazla 30 günlük tarih aralığı için verilir; Müdür onaylar, isteyen onaylayamaz."; "Yalnız tek sevk, Müdür onaylar" — "Her sevk için ayrı istisna istenir."; "Sipariş bazında, Muhasebe onaylar" — "İstisna bütün siparişe verilir; onayı Muhasebe verir.") |
| 3 | Cari kartındaki e-Fatura/e-Arşiv durumunu (GİB mükellef kontrolü) kimler güncelleyebilsin? | Muhasebe + Admin (Önerilen) | "GİB kontrolü muhasebenin işidir." (seçilmeyenler: "Muhasebe + Müdür + Admin" — "Müdür de güncelleyebilir."; "Satış Destek/Uzman + Müdür + Admin" — "Cariyi açan satış ekibi günceller.") |
| 4 | Karma vadeli bir planda sevk vadeli bir taksit varsa ama henüz sevk yapılmadıysa, planın vade tarihleri nasıl görünsün? | Taksit taksit (Önerilen) | "Nokta vadeli taksitlerin tarihi hesaplanır; sevk vadeli olanlar sevk yapılana kadar 'bekliyor' görünür." (seçilmeyen: "Bütün plan beklesin" — "Bir taksit belirsizse bütün planın tarihi 'bekliyor' görünür (bugünkü davranış).") |
| 5 | (Teyit) Risk istisnası için 'Satış uzmanı ve müdür' dedin. Bu şu anlama mı geliyor: Satış Uzmanı istisnayı ister, Müdür onaylar? | İkisi de onaylayabilir | "Satış Uzmanı da Müdür de istisnayı onaylayabilir (isteyen yine kendi talebini onaylayamaz)." (seçilmeyenler: "Uzman ister, Müdür onaylar (Önerilen)" — "Satış Uzmanı (ve Satış Destek) istisna talebi açar; Müdür onaylar; isteyen kendi talebini onaylayamaz."; "Uzman ve Müdür birlikte" — "Her istisna için iki onay gerekir: Satış Uzmanı ve Müdür.") |
| 6 | Risk istisnası ne kadar süre/kapsam için verilsin? | "Bunu anlamadım" | — (seçenekler: "Tek sevk veya 30 güne kadar (Önerilen)"; "Yalnız tek sevk"; "Sipariş bazında"). Soru 9 ile yeniden soruldu |
| 7 | Taksitli vadede tutar çok küçük olduğunda son taksit eksiye düşebiliyor (ör. 0,02 TL'yi 4 eşit taksite bölmek). Bu durumda ne olsun? | Bölme reddedilsin (Önerilen) | "Kullanıcıya 'tutar bu kadar taksite bölünemez' uyarısı çıkar; daha az taksit girer (bugünkü davranış)." (seçilmeyen: "Taksit sayısı otomatik azalılsın" (sic; ekranda böyle gösterildi)) |
| 8 | Siparişte seçilen alıcı vergi durumu listesi (normal, nihai tüketici, tevkifata tabi olmayan, ihraç kayıtlı, istisna belgeli) nerede tutulsun? | Ayarlar'da, tarihli liste (Önerilen) | "Vergi kodları gibi Ayarlar'da tutulur; Müdür/Admin yeni durum ekleyebilir." (seçilmeyen: "Sabit liste") |
| 9 | (Yeniden) Örnek: ABC firmasının risk limiti 1.000.000 TL ve doldu. ABC'ye bugün 3 kamyon yük çıkacak, gelecek hafta da 2 kamyon daha. Risk istisnası (limit aşılsa da sevke izin) onaylandığında neyi serbest bıraksın? | Yalnız o sevki (Önerilen) | "Bugünkü kamyon için bir onay alınır; gelecek haftaki sevk için yeniden onay gerekir. En sıkı kontrol." (seçilmeyenler: "Belirli tarihe kadar tüm sevkleri"; "O siparişin tüm sevklerini"; "Belirli bir tutara kadar") |
| 10 | (Teyit) Risk istisnasını kimler TALEP edebilsin? (Onayı Satış Uzmanı veya Müdür verecek; talep eden kendi talebini onaylayamaz.) | Kendi metni: "Sevkiyatçı, satış destek, satış uzmanı talep edebilsin" | — (seçenekler: "Satış Destek, Uzman, Müdür, Admin (Önerilen)" — "Sipariş girebilen herkes talep açabilir; onayı başka bir yetkili verir."; "Satış Destek ve Sevkiyatçı" — "Siparişi giren ve sevki hazırlayan talep eder; Uzman/Müdür onaylar."; "Yalnız Satış Destek") |
| 11 | (Teyit) Onaylanan ama kullanılmayan bir risk istisnası (sevk yapılmadı) kaç gün geçerli kalsın? | Aynı gün (Önerilen) | "İstisna onaylandığı günün sonuna kadar geçerlidir; ertesi gün yeniden onay gerekir." (seçilmeyenler: "3 gün"; "7 gün"; "Ayarlar'dan ben girerim" — "Süre Ayarlar'da değer olarak tutulur; ben girene kadar istisna verilemez.") |
| 12 | (Teyit) Risk istisnasını Admin de talep edebilsin ve onaylayabilsin mi? (Önceki cevaplarında Admin'i saymadın.) | Evet, ikisini de (Önerilen) | "Admin talep de edebilir, onay da verebilir; kendi talebini onaylayamaz." (seçilmeyenler: "Yalnız onaylasın"; "Hayır" — "Risk istisnasında Admin'in yetkisi olmaz.") |
| 13 | (Teyit) Kilitli bir kurala göre (DEC-SAL-002) 'risk istisnası değişirse sipariş yeni revizyona ve yeniden onaya gider'. Tek sevklik günlük risk istisnası da siparişi yeni revizyona sokmalı mı? | Hayır, sipariş revize olmaz (Önerilen) | "Tek sevk istisnası sevke bağlı ayrı bir onay kaydıdır; sipariş revizyonu ve sipariş yeniden onayı açmaz. Kuraldaki 'risk istisnası' siparişin kendisine yazılan sürekli istisnayı kasteder." (seçilmeyen: "Evet, her istisna revizyon açsın") |
| 14 | (Teyit) Sevkiyatçı sipariş detayını ve bakiyeleri göremiyor. Risk istisnası talebini nereden açsın? | Sevk/yükleme ekranından (Önerilen) | "Yükleme ekranında yalnız 'risk limiti aşıldı, sevk duruyor' yazar; tutar görünmez; 'istisna iste' düğmesi olur." (seçilmeyen: "Sevkiyatçı talep etmesin") |
| 15 | (Teyit) Alıcı vergi durumu listesine yeni bir durum eklemek veya bir durumun vergi etkisini değiştirmek vergi ayarı sayılsın mı? | Evet, vergi ayarı gibi (Önerilen) | "Müdür/Admin doğrudan değiştirir; Muhasebe önerir, müdür onaylar; yalnız müşavirin onayladığı kodlar kullanılabilir." (seçilmeyen: "Yalnız Admin") |
| 16 | (Teyit) Müdür de risk istisnası TALEP edebilsin mi? (Onaylayabiliyor; kendi talebini onaylayamaz.) | Evet (Önerilen) | "Müdür talep açabilir; onayı başka bir yetkili (Satış Uzmanı, başka Müdür veya Admin) verir." (seçilmeyen: "Hayır" — "Müdür yalnız onaylar, talep açmaz.") |
| 17 | (Teyit) Admin, Muhasebe'ye verilen iki işi de yapabilsin mi: vergi ayarı önermek ve dış faturayı sipariş/sevkle eşleştirmek? | Evet, ikisini de (Önerilen) | "Admin her şeyi yapabilen kullanıcı olduğu için bu ikisini de yapabilir (vergi ayarını zaten doğrudan değiştirebiliyor)." (seçilmeyenler: "Yalnız eşleştirme"; "Hayır, yalnız Muhasebe") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D152 | Tekliften dönen siparişin carisi | Tekliften dönen siparişte cari **değiştirilemez**; teklifin carisi kalır. Başka müşteri için sipariş iptal edilir ve yeni teklif veya sipariş açılır (soru 1). Para birimi değişikliği SAT-D128 kuralıyla kalır | API-04 başlık revizyonu (#864) |
| SAT-D153 | Risk istisnasını kim onaylar | Risk istisnasını **Satış Uzmanı, Müdür veya Admin** onaylayabilir; birinin onayı yeterlidir (Admin: soru 12). **İsteyen kendi talebini onaylayamaz** (soru 2 ve 5) | API-07, persona grant matrisi |
| SAT-D154 | Risk istisnasının kapsamı | Risk istisnası **yalnız tek bir sevki** serbest bırakır. Aynı müşterinin bir sonraki sevki için yeniden istisna gerekir (soru 9); aynı gün çıkan üç kamyon üç ayrı sevkse üç ayrı onay gerekir. İstisna, sevkiyatta yapılan kesin risk kontrolünü (DEC-FIN-009) yalnız o sevk için geçer; siparişteki uyarı/onay akışı değişmez. İstisna **auditlidir** (DEC-FIN-009): kim istedi, kim onayladı, hangi sevk, gerekçe ve zaman kaydedilir | API-07, Lojistik sevk kapısı |
| SAT-D155 | Cari e-belge durumunu güncelleme | `counterparty.accounts.e-document.update` iznini **Muhasebe ve Admin** alır (soru 3). GİB mükellef kontrolü muhasebenin işidir. e-Belge durumu VKN bazında üç şirkette ortak olduğu için (SAT-D119), bir şirkette bu izinle yapılan güncelleme üç şirkete birden yansır | Persona grant matrisi, Cari e-belge (#860) |
| SAT-D156 | Karma vadede tarih | Karma planda vade tarihleri **taksit taksit** çözülür. Nokta vadeli taksitin tarihi hesaplanır. Sevk vadeli taksit, sevk yapılana kadar "bekliyor" görünür (soru 4). Bir taksitin belirsizliği diğerlerini bekletmez | Vade hesaplayıcı (`SalesDueDateCalculator`, #864) |
| SAT-D157 | Eksi son taksit | Taksit bölmesinde son taksit eksiye düşüyorsa bölme **reddedilir**. Kullanıcı "tutar bu kadar taksite bölünemez" uyarısını görür ve daha az taksit girer (soru 7; bugünkü fail-closed davranış). SAT-D124'teki yuvarlama kuralı değişmez | Vade hesaplayıcı (#864) |
| SAT-D158 | Alıcı vergi durumu listesi | SAT-D140'taki alıcı vergi durumu listesi (normal, nihai tüketici, tevkifata tabi olmayan, ihraç kayıtlı, istisna belgeli) **Ayarlar'da efektif tarihli bir liste** olarak tutulur. Listeye durum eklemek veya bir durumun vergi etkisini değiştirmek **vergi ayarı** sayılır (soru 15): Müdür ve Admin doğrudan değiştirir (EK20 SAT-D145), Muhasebe önerir ve müdür sınıfı onaylar (SAT-D146); etkide yalnız mali müşavirin onayladığı kodlar kullanılabilir (SAT-D148). Sürüm-0 listesindeki beş durumun etkisi SAT-D74, SAT-D93 ve SAT-D147'ye göre tohumlanır. Her durumun vergi etkisi (SAT-D74, SAT-D93, SAT-D147) aynı listede veri olarak tutulur, hesaplayıcıda sabit kodlanmaz | Ayarlar vergi masterı, sipariş başlığı |
| SAT-D159 | Risk istisnasını kim ister | Risk istisnasını **Sevkiyatçı, Satış Destek, Satış Uzmanı, Müdür ve Admin** talep edebilir (soru 10, 12 ve 16). Sevkiyatçı talebi **sevk/yükleme ekranından** açar; o ekranda yalnız "risk limiti aşıldı, sevk duruyor" yazar, tutar görünmez (soru 14). Satış Uzmanı, Müdür ve Admin hem talep hem onay iznini alır; kimse kendi talebini onaylayamaz (SAT-D153) | API-07, persona grant matrisi |
| SAT-D160 | Risk istisnasının geçerliliği | Onaylanan risk istisnası **onaylandığı iş tarihinin (BusinessDate, Türkiye saati) sonuna kadar** geçerlidir; o tarih içinde sevk kesinleşmezse düşer ve ertesi gün yeniden onay gerekir (soru 11). Uygulama ayrıntısı (geri alınabilir): bağlı olduğu sevk iptal edilirse istisna da düşer. DEC-FIN-009'daki "istisna süreli" şartı bununla karşılanır | API-07, Lojistik sevk kapısı |
| SAT-D161 | Risk istisnası ve sipariş revizyonu | Tek sevk risk istisnası sevke bağlı **ayrı bir onay kaydıdır**; sipariş revizyonu (Rev+1) ve sipariş yeniden onayı **açmaz** (soru 13). Kilitli DEC-SAL-002'deki "risk istisnası" ifadesi siparişin kendisine yazılan sürekli istisnayı kasteder; bu okuma Sinan'ın açık cevabıdır | API-07, DEC-SAL-002 okunuşu |
| SAT-D162 | Sevkiyatçının talep yüzeyi | Sevkiyatçı risk istisnası talebini **sevk/yükleme ekranından** açar. O ekran yalnız "risk limiti aşıldı, sevk duruyor" durumunu ve "istisna iste" düğmesini gösterir; limit, bakiye ve tutar göstermez (soru 14; persona kartı: Sevkiyatçı Finance bakiyelerini göremez). Talep sipariş detayı izni (`sales.drafts.read`) gerektirmez | Lojistik yükleme ekranı, API-07 |
| SAT-D163 | Admin ve Muhasebe işleri | EK20'de yalnız Muhasebe'ye verilen iki işi **Admin de yapar**: vergi ayarı **önerisi** (SAT-D146 öneri izni) ve dış faturanın sipariş/irsaliyeyle **eşleştirilmesi** (SAT-D142) (soru 17). Admin'in öneri izni, onu SAT-D146'daki onay kuralından muaf tutmaz; kendi önerisini onaylayamaz. Admin vergi ayarını doğrudan da değiştirebilir (SAT-D145) | Persona grant matrisi |

## Uygulama notları

- SAT-D153 ve SAT-D159 için iki ayrı izin gerekir: istisnayı **isteme** (Sevkiyatçı, Satış Destek, Satış Uzmanı, Müdür, Admin) ve
  **onaylama** (Satış Uzmanı, Müdür, Admin). `SATIS_HAZIRLIK_PLANI_v1` API-07'deki adlar (`sales.orders.risk-exception.request`
  / `.approve`) kullanılır.
- SAT-D154'teki istisna belirli bir sevke (yükleme/irsaliye) bağlanır ve o sevk kesinleşince tükenir; kullanılmazsa
  SAT-D160'a göre iş tarihi sonunda düşer.
- SAT-D158'deki yeni durum eklenirken, o durumun KDV/tevkifat etkisi de girilmelidir. Etkisi girilmemiş bir durum siparişte
  seçilirse vergi "kaynak yok" ile bekler.

## Açık kalanlar

- Yok. Soru 6'daki belirsizlik soru 9 ile; istisnayı kimin isteyeceği, geçerlilik süresi, Admin, revizyon ilişkisi,
  Sevkiyatçının talep yüzeyi ve durum listesinin yetkisi soru 10–15 ile kapandı.

## Sınırlar

- Grant, rol ve Critical ataması insan kapısıdır. Bu belge, SAT-D153, SAT-D155, SAT-D158, SAT-D159 ve SAT-D163'teki atamalar için Sinan'ın somut
  kararıdır. Uygulama PR'ı bu satırların dışında grant, rol veya Critical açamaz. Gerçek kullanıcıya rol atanması ve
  production etkinleştirmesi insan kapısında kalır (SAT-D110).
- Bu kararlar merge kapılarını ve diğer insan kapılarını değiştirmez: deployment, environment, secret, Settings
  etkinleştirme, production, `OPEN-*` kapatma, `.claude/settings.json`.
