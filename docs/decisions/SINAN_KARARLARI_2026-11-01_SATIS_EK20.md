# Sinan Kararları — 2026-11-01 (Satış: persona grant'ları, yeni personalar, özel alıcı vergisi, vergi kodları, kalem tipinin yeri)

Bu belge önceki karar dosyalarını silmez, SAT-D141…SAT-D151'i ekler. Birleşen PR'lar şu izinleri bilerek hiçbir role
vermedi: #864 (API-04 başlık revizyonu, vade), #865 (S6 raporlar), #866 (MOD-03 vergi masterı) ve #860 (Cari e-belge). Bu
belge, bu izinlerin hangi personaya verileceğini cevaplar. Ayrıca EK19'un açık bıraktığı kalem tipi yerini ve SAT-D140'ın
karara bağlamadığı özel alıcı vergisini de cevaplar. EK19 (SAT-D133…D140), `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md`,
DEC-SAL-002 ve DEC-FIN-001 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Yeni personalar:** SAT-D141 ve SAT-D142, `PERSONA_GRANT_MATRISI_KARTI_v0`'daki dört personaya (Admin, Sevkiyatçı, Satış
  Destek, Müdür) iki yeni persona ekler: **Satış Uzmanı** ve **Muhasebe**.
- **Vergi ayarlarının sahibi:** SAT-D145 ve SAT-D146, EK12 SAT-D83'ü değiştirir. SAT-D83'e göre vergi ayarlarını
  Muhasebe/Finans ve Admin doğrudan düzenliyordu. Bundan sonra **Müdür ve Admin doğrudan düzenler**; **Muhasebe'nin
  değişikliği müdür sınıfı bir onaya gider**.
- **Vergi kodu ve eşiğin girilmesi:** SAT-D148, EK19 SAT-D135'teki "değer boş başlar ve Sinan veya mali müşavir girer"
  kuralını değiştirir. Bundan sonra Claude öneri hazırlar, mali müşavir onaylar, **Müdür veya Admin girer**.
- **Özel alıcı:** SAT-D147, PR #866'daki ara davranışı değiştirir. Bugün "ihraç kayıtlı" ve "istisna belgeli" alıcıda vergi
  `Unresolved` / `buyer_situation_undecided` döner. Bundan sonra bu alıcılarda **KDV %0 uygulanır, istisna kodu listeden
  seçilir, tevkifat uygulanmaz**.
- **Kalem tipinin yeri:** SAT-D149, EK19 "Açık kalanlar"daki kalem tipi sorusunu kapatır ve PR #866'nın şirket bazlı kalem
  tipi deposunu değiştirir: kalem tipi **ortak ürün kartında tek değer** olur. SAT-D133 değişmez; vergi oran ve kod
  tabloları şirket bazında kalır.

**Kaynak:** Sinan'ın 1 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği seçmeli
soru cevapları. Beş tur, on altı soru. Son iki tur, docs review'larının (#869) açtığı altı teyit sorusudur. Sinan'a her
seçeneğin adı **ve açıklaması** birlikte gösterildi. Altı soruda (1, 2, 3, 5, 6, 9) Sinan kendi metnini yazdı ya da seçeneğe
ek yaptı; bu metinler aynen verilir.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Satış raporlarını (açık sipariş tonajı, fiyat sapması, kayıp fırsat) hangi personalar görsün? Not: rapordaki tutarları yalnız fiyat görme yetkisi olan görür; Sevkiyatçı yalnız miktarları görür. | Kendi metni: "Satışçı, müdür, admin, satış destek görsün, muhasebe ekibi hepsini görsün; sevkiyatçı birim fiyatsız ve tutarsız görsün" | — (seçenekler, çoklu: "Admin" — "En yetkili kullanıcı."; "Müdür" — "Siparişleri onaylayan yönetici."; "Satış Destek" — "Sipariş giren ve düzenleyen kişi."; "Sevkiyatçı" — "Yalnız miktarları görür, tutarları göremez.") |
| 2 | Sipariş başlığını kimler değiştirebilsin (müşteri, para birimi, vade, satış sorumlusu, teslimat adresi)? Not: müşteri, para birimi ya da vade değişirse sipariş yeniden Müdür onayına gider; yalnız sorumlu ya da adres değişirse onaya gitmez. | Kendi metni: "Satış destek,  admin, müdür değiştirebilsin; yetkisi olanlar onaylayabilsin" | — (seçenekler, çoklu: "Admin"; "Müdür"; "Satış Destek" — "Siparişi giren kişi; değişiklik yine Müdür onayına gider.") |
| 3 | Vergi ayarlarını (KDV/tevkifat varsayılanları, istisna ve tevkifat kodları, VUK 232 eşiği) ve ürün kartındaki ürün türünü (demir/hurda/hizmet) kimler yönetsin? | Kendi metni: "Müdürler ve admin" | — (seçenekler: "Yalnız Admin (Önerilen)" — "Vergi verisini tek elden yönetmek en güvenlisi."; "Admin + Müdür" — "Müdür de vergi ayarlarını ve ürün türünü değiştirebilir."; "Admin vergiyi, Müdür ürün türünü") |
| 4 | İhracat kayıtlı satış ve muafiyet belgeli alıcılarda KDV/tevkifat nasıl olsun? Şu an bu siparişlerde vergi hesaplanmıyor ve sipariş 'karar bekliyor' durumunda kalıyor. | KDV %0 + istisna kodu | "Belge varsa KDV sıfır olur, istisna kodu listeden seçilir, tevkifat uygulanmaz." (seçilmeyenler: "Şimdilik bekletilsin (Önerilen)" — "Bu alıcılar nadirse mali müşavirle kural netleşene kadar sistem fatura kesmesin."; "Normal KDV, tevkifat yok") |
| 5 | 'Satışçı' dediğin kişi, mevcut 'Satış Destek' ile aynı persona mı, yoksa ayrı bir persona mı? (Satış Destek şu an sipariş girip düzenleyebiliyor ama onaylayamıyor.) | Kendi metni (seçenek "Ayrı persona, Satış Destek gibi" + ek): "Ayrı persona, satış destek gibi sonra ayrıştırılabilir, adı satış uzmanı olabilir" | "Yeni bir persona, yetkileri Satış Destek'le aynı başlar; sonra ayrıştırılabilir." (seçilmeyenler: "Aynı persona"; "Ayrı persona, daha dar" — "Yalnız kendi siparişlerini ve müşterilerini görür/girer.") |
| 6 | 'Muhasebe ekibi' için yeni bir persona açılsın mı? Satış modalında ne yapabilsin? | Kendi metni (seçenek "Yeni persona: tüm satışı görsün, yazmasın (Önerilen)" + ek): "Yeni persona: tüm satışı görsün yazamasın(önerilen) , ama fatura modülü gelince fatura kesme yetkisi de olacak ve şuanda gerçek kesilen fatura ile erp den oluşturulanı eşleştirme yapabilsin, birde eğer mantıklıysa sadece vergi ayarlarını değiştirebilsin ama değişiklikten dolayı onaya gitsin" | "Siparişleri, fiyatları, raporları ve vergi ayarlarını görür; sipariş girmez, onaylamaz." (seçilmeyenler: "Yeni persona: görsün + fatura kessin"; "Yeni persona: görsün + vergi ayarlarını yönetsin") |
| 7 | Vergi kodları (istisna kodları, tevkifat kodları) ve VUK 232 eşik tutarı sisteme nasıl girilsin? | Ben hazırlayayım, müşavir onaylasın (Önerilen) | "GİB'in standart listesini öneri olarak ben hazırlarım; mali müşavir onaylayınca Müdür/Admin Ayarlar ekranından girer." (seçilmeyenler: "Müşavir bilgiyi versin, Müdür/Admin girsin"; "Bu iş sonra") |
| 8 | Ürün türü (demir/hurda/hizmet) her şirkette ayrı mı tutulsun, yoksa ortak ürün kartında tek bir değer mi olsun? Şu an şirket başına tutuluyor. | Ortak kartta tek değer (Önerilen) | "Aynı ürün üç şirkette de aynı türde olur, bir kez girilir." (seçilmeyen: "Şirket başına ayrı" — "Şu anki gibi kalsın; her şirket kendi ürün türünü tutsun.") |
| 9 | Muhasebe vergi ayarını değiştirdiğinde değişiklik onaya gidecek. Bu onayı kim versin? (Değişikliği yapan kişi kendi değişikliğini onaylayamaz.) | Kendi metni: "Müdür, admin, muhasebe müdürü, satış müdürü, gibi tüm müdürler" | — (seçenekler: "Müdür veya Admin (Önerilen)"; "Yalnız Admin"; "Başka bir muhasebeci de onaylayabilsin") |
| 10 | Müdür veya Admin vergi ayarını kendisi değiştirirse ne olsun? | Doğrudan geçerli olsun (Önerilen) | "Senin önceki kararına göre Müdür ve Admin yönetir; onaya gitmez." (seçilmeyen: "Onlarınki de onaya gitsin") |
| 11 | (Teyit) Yeni Satış Uzmanı personası da sipariş başlığını (müşteri, para birimi, vade, sorumlu, adres) değiştirebilsin mi? İlk soruda Satış Destek, Admin ve Müdür demiştin; Satış Uzmanı'nı da 'Satış Destek gibi' tanımladın. | Evet, Satış Destek gibi (Önerilen) | "Satış Uzmanı Satış Destek'in tüm yetkilerini alır, başlık değiştirme dahil; değişiklik yine onaya gider." (seçilmeyen: "Hayır, başlığı değiştiremesin") |
| 12 | (Teyit) Muhasebe için 'gerçek kesilen fatura ile ERP'den oluşturulanı şu anda eşleştirme' dedin. ERP'de henüz fatura kesme yok. Şu an Muhasebe neyi neyle eşleştirsin? | Dış faturayı sipariş/sevkle eşleştirsin (Önerilen) | "Başka sistemde kesilen faturanın numarası ve tutarı ERP'deki sipariş/irsaliyeye bağlanır; fark varsa görünür." (seçilmeyenler: "Mevcut resmi fatura içe aktarmayı kullansın" — "Finans'taki resmi fatura içe aktarma ekranı (FIN-F03) Muhasebe'ye açılır; eşleştirme oradan yapılır."; "Fatura modülü gelince") |
| 13 | (Teyit) İhraç kayıtlı ya da istisna belgeli seçilen siparişte belge bilgisi (no, tarih) girilmemişse ne olsun? | Fatura beklesin (Önerilen) | "KDV %0 görünür ama belge girilene kadar fatura kesilmez." (seçilmeyenler: "Normal vergiye dönsün"; "Sipariş onaylanamasın") |
| 14 | (Teyit) Ürün türü ortak kartta tek değer olacak, yani bir şirkette yapılan değişiklik üç şirketi birden etkiler. Bunu kim değiştirebilsin? | Herhangi bir şirkette yetkili Müdür/Admin (Önerilen) | "Üç şirketten birinde Müdür/Admin olan değiştirebilir; değişiklik kaydedilir." (seçilmeyenler: "Yalnız üç şirkette de yetkili olan"; "Yalnız Admin") |
| 15 | (Teyit) Satış siparişlerinde ve raporlarda birim fiyat ile tutarları kimler görebilsin? (Sevkiyatçı göremez, Muhasebe hepsini görür demiştin.) | Admin, Müdür, Satış Destek, Uzman, Muhasebe (Önerilen) | "Sevkiyatçı dışındaki herkes fiyat ve tutarı görür." (seçilmeyen: "Admin, Müdür, Muhasebe" — "Satış Destek ve Satış Uzmanı sipariş girer ama fiyat ve tutarı göremez.") |
| 16 | (Teyit) Sipariş satırına fiyat girme/değiştirme yetkisi kimde olsun? (Yetki matrisinde bu konu şu an 'koşullu' olarak açık duruyor.) | Satış Destek, Uzman, Müdür, Admin (Önerilen) | "Siparişi giren fiyatı da girer; fiyat taban/iskonto kuralları ve onay yine çalışır." (seçilmeyenler: "Yalnız Müdür ve Admin" — "Satış Destek ve Satış Uzmanı siparişi fiyatsız girer, fiyatı Müdür/Admin girer."; "Şimdilik karar verme" — "Fiyat yazma izni hiçbir role verilmez, sonra konuşuruz.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D141 | Yeni persona: Satış Uzmanı | `PERSONA_GRANT_MATRISI_KARTI_v0`'a **Satış Uzmanı** personası eklenir. Başlangıçta **Satış Destek'in bütün grant'larını** alır: sipariş girer ve düzenler, onaylayamaz, sipariş başlığını da değiştirir (soru 11). İleride ayrıştırılabilir. Uygulama yorumu (geri alınabilir): "Satış Destek gibi" ifadesi kartta Satış Destek'e verilen her izni kapsar, alış siparişi izinleri dahil | Persona grant matrisi |
| SAT-D142 | Yeni persona: Muhasebe | `PERSONA_GRANT_MATRISI_KARTI_v0`'a **Muhasebe** personası eklenir. Satış modülünde **her şeyi görür**: siparişler, fiyatlar ve tutarlar (`sales.orders.price.read` dahil), raporlar ve vergi ayarları. **Sipariş yazmaz ve onaylamaz**. **Şimdi** dış sistemde kesilen faturanın numarasını ve tutarını ERP'deki sipariş veya irsaliyeye bağlayarak **eşleştirir**; fark varsa görünür (soru 12; DEC-FIN-001: resmî fatura dış sistemdedir). **Fatura modülü geldiğinde** (API-32…37) faturayı **keser**; uygulama yorumu (geri alınabilir): ERP'de "fatura kesme" DEC-FIN-001 gereği ERP fatura taslağını hazırlamaktır (API-32) ve SAT-D88'deki hazırlayan ≠ onaylayan kuralına tabidir. Vergi ayarlarını yalnız SAT-D146 onay akışıyla değiştirir | Persona grant matrisi, yeni eşleştirme dilimi, fatura dalgası |
| SAT-D143 | Satış raporlarını görme | `sales.reports.read` şu personalara verilir: **Admin, Müdür, Satış Destek, Satış Uzmanı, Muhasebe ve Sevkiyatçı**. Sevkiyatçı `sales.orders.price.read` almaz; bu yüzden raporlarda **yalnız miktarları** görür, birim fiyat ve tutar gizlenir. Fiyat sapması raporu fiyat görme izni olmadan açılmaz (403). Diğer personaların fiyat görme izni SAT-D150'dedir | Persona grant matrisi, API-41/42 |
| SAT-D144 | Sipariş başlığını değiştirme | `sales.orders.header.write` şu personalara verilir: **Satış Destek, Satış Uzmanı, Admin, Müdür** (soru 2 ve 11). Başlık değişikliği yeniden onay gerektiriyorsa (cari, para birimi, vade; DEC-SAL-002) onayı mevcut sipariş onay iznine sahip olanlar verir | Persona grant matrisi, API-04 |
| SAT-D145 | Vergi ayarları ve kalem tipi yönetimi | `settings.tax-master.manage` ve `product.item-type.manage` **Müdür ve Admin**'e verilir; değişiklikleri **doğrudan geçerli olur**, onaya gitmez. Uygulama yorumu (geri alınabilir): "Müdürler" ifadesi karttaki Müdür personası olarak okunur | Persona grant matrisi, MOD-03 |
| SAT-D146 | Muhasebe'nin vergi ayarı değişikliği | Muhasebe vergi ayarlarını (KDV/tevkifat varsayılanları, istisna ve tevkifat kodları, VUK 232 eşiği) **önerebilir**. Öneri **onaya gider** ve onaylanana kadar yürürlüğe girmez. Onayı **müdür sınıfındaki** bir persona verir; Sinan'ın metni: "Müdür, admin, muhasebe müdürü, satış müdürü, gibi tüm müdürler". Değişikliği yapan kendi değişikliğini onaylayamaz (maker ≠ checker). Muhasebe kalem tipini değiştirmez. Sinan'ın 6. sorudaki "eğer mantıklıysa" koşulu, 9. soruda onay akışının ayrıntısını cevaplamasıyla akışın kendisine dönüşmüştür | MOD-03 vergi masterı (yeni öneri/onay akışı), persona grant matrisi |
| SAT-D147 | İhraç kayıtlı / istisna belgeli alıcı | SAT-D140'a göre siparişte "ihraç kayıtlı" ya da "istisna belgeli" seçilirse **KDV %0** olur, **istisna kodu listeden seçilir** (SAT-D93, SAT-D133 kod listesi) ve **tevkifat uygulanmaz**. Belge bilgisi (no, tarih) siparişte istenir. Belge bilgisi veya istisna kodu yoksa KDV %0 görünür ama **fatura kesilmez** ("kaynak yok"; soru 13, SAT-D135 ile aynı bekleme). Satır SAT-D93/D140'taki gibi tek tek değiştirilebilir. Kural hesaplayıcıda sabit kodlanmaz, Ayarlar verisi olarak tutulur (SAT-D93, SAT-D139). İhraç kayıtlı satışın e-Fatura gösterimi mali müşavir onayıyla belirlenir (SAT-D148): fatura tipi, istisna kodu ve KDV'nin hesaplanıp tecil edilip edilmeyeceği | MOD-03 vergi çözücü (PR #866'daki `buyer_situation_undecided` kaldırılır), sipariş başlığı, API-32 |
| SAT-D148 | Vergi kodları ve VUK 232 eşiği | İstisna ve tevkifat kod listesi ile VUK 232 eşik tutarı için **Claude, GİB'in standart kod listelerinden bir öneri belgesi hazırlar**. **Mali müşavir onaylar**. Onaylanan değerleri **Müdür veya Admin** Ayarlar ekranından girer (Muhasebe SAT-D146 akışıyla önerebilir). Onaylanmamış hiçbir kod veya tutar sisteme girilmez. SAT-D139 varsayılan oranları bu kapının dışındadır | Öneri belgesi (docs), Ayarlar vergi masterı |
| SAT-D150 | Fiyat ve tutar görme | `sales.orders.price.read` şu personalara verilir: **Admin, Müdür, Satış Destek, Satış Uzmanı, Muhasebe** (soru 15). **Sevkiyatçı almaz**; siparişlerde ve raporlarda birim fiyat ve tutar ona gizlenir (soru 1, PRS-02) | Persona grant matrisi |
| SAT-D151 | Fiyat girme / değiştirme | `sales.orders.price.write` şu personalara verilir: **Satış Destek, Satış Uzmanı, Müdür, Admin** (soru 16). Persona kartındaki "koşullu" satır bu kararla kapanır. Fiyat taban, iskonto limiti ve onay kuralları (SAT-D1, SAT-D2, SAT-D61…D68; API-12) aynen çalışır | Persona grant matrisi |
| SAT-D149 | Kalem tipinin yeri | Kalem tipi (demir/hurda/hizmet) **ortak ürün kartında tek değer** olarak tutulur: aynı ürün üç şirkette de aynı tiptedir ve bir kez girilir. Bunu **üç şirketten herhangi birinde** `product.item-type.manage` iznine sahip Müdür veya Admin değiştirebilir; değişiklik audit'e yazılır (soru 14). EK19 "Açık kalanlar"daki soru bununla kapanır. SAT-D133 (oran ve kod tabloları şirket bazında) ile SAT-D139 (varsayılanlar) değişmez | Ürün kartı (PR #866 deposunun taşınması), vergi çözücü |

## Uygulama notları

- SAT-D141…D146 ve SAT-D150/D151'deki grant'lar ve personalar Sinan'ın somut grant kararıdır. Bu grant'lar ayrı bir PR'da uygulanır; o PR
  önce `PERSONA_GRANT_MATRISI_KARTI_v0`'ı günceller ve yalnız bu belgede yazan grant'ları açar. Bu belge kendi başına hiçbir
  grant açmaz.
- SAT-D146 için ayrı izin adları gerekir; böylece Muhasebe'nin "görme" izni onay akışını atlatamaz. Uygulama ayrıntısı
  (geri alınabilir):
  - Okuma için ayrı bir izin olur; Muhasebe, Müdür ve Admin bunu alır.
  - Öneri için ayrı bir izin olur; yalnız Muhasebe bunu alır.
  - Öneriyi onaylamak için ayrı bir izin olur; Müdür ve Admin bunu alır.
  - `settings.tax-master.manage` (doğrudan değiştirme) yalnız Müdür ve Admin'de kalır.
  - Kalem tipinde kimin yalnız okuyacağı sorulmadı. Karar verilene kadar kalem tipini yalnız `product.item-type.manage`
    sahipleri (Müdür, Admin) okur (bkz. Açık kalanlar).
- SAT-D146'daki onay izni, "muhasebe müdürü" ve "satış müdürü" için karar verilene kadar yalnız Müdür ve Admin
  personalarına verilir. İş unvanı kendiliğinden persona ataması yapmaz.
- SAT-D147'de istisna kodu listesi SAT-D148 onayı gelene kadar boş olabilir. O süre içinde bu siparişlerin faturası
  "kaynak yok" ile bekler; bu, SAT-D135'teki eşikle aynı fail-closed davranıştır.
- SAT-D149 için PR #866'nın şirket bazlı `product.ProductItemTypes` tablosu ortak ürün ana kaydına taşınır. Tablo main'de
  ama production verisi yoktur, bu yüzden geçiş ileri-düzeltme (forward-fix) migration'ıyla yapılır. Aynı ürün için
  şirketler arasında farklı tip kaydı varsa migration bunu sessizce seçmez, reddeder (fail-closed).
- SAT-D142'deki şimdiki eşleştirme, dış faturanın numarasını ve tutarını ERP siparişine veya irsaliyesine bağlar ve farkı
  gösterir. Satış modülünün işidir; açık kalem oluşturmaz ve Finance'ın resmî fatura verisine (FIN-F03, SAT-D85) yazmaz.
  İzin adı uygulama PR'ında manifest'e eklenir ve yalnız Muhasebe'ye verilir. Fatura dalgasında bu eşleştirme API-35 (resmî fatura ↔ ERP taslağı, SAT-D89/D106) ile birleştirilir;
  eşleştirme verisi kopya kayıt üretmez. Bu birleştirme bir uygulama yorumudur (geri alınabilir).
- Grant PR'ı `ErpPermissions` içindeki izin açıklamalarını da (ör. `SettingsTaxMasterManage`'ın "Muhasebe/Finans ve Admin" sahip notu) bu kararlara göre günceller.

## Açık kalanlar

- "Muhasebe müdürü" ve "satış müdürü" ayrı personalar mı, yoksa Müdür personasının iş unvanları mı? Karar verilene kadar
  bu unvanlar ek yetki almaz (bkz. Uygulama notları).
- Satış Uzmanı'nın Satış Destek'ten ne zaman ve nasıl ayrışacağı (ör. yalnız kendi müşterileri) sonraya bırakıldı.
- Kalem tipini (ürün kartında) `product.item-type.manage` sahipleri dışında kimin okuyacağı sorulmadı (NEEDS-SINAN).

## Sınırlar

- Grant, rol ve Critical ataması insan kapısıdır. Bu belge, SAT-D141…D146 ve SAT-D150/D151'deki atamalar için Sinan'ın somut kararıdır.
  Uygulama PR'ı bu satırların dışında grant, rol veya Critical açamaz. Persona/rol tanımı ve izin eşlemesi bir PR'da
  yapılabilir; gerçek kullanıcıya rol atanması ve production etkinleştirmesi insan kapısında kalır (SAT-D110).
- Bu kararlar merge kapılarını ve diğer insan kapılarını değiştirmez: deployment, environment, secret, Settings
  etkinleştirme, production, `OPEN-*` kapatma, `.claude/settings.json`.
- Vergi kodları ve VUK 232 eşik değeri mali müşavir/YMM onayı olmadan girilmez (SAT-D148). SAT-D139'daki varsayılan oranlar
  Sinan'ın açık cevabıdır ve bu kapının dışındadır.
