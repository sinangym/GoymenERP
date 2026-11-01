# Sinan Kararları — 2026-11-01 (Satış: persona grant'ları, yeni personalar, özel alıcı vergisi, vergi kodları, kalem tipinin yeri)

Bu belge önceki karar dosyalarını silmez; SAT-D141…SAT-D149'u ekler. Birleşen PR #864 (API-04 başlık revizyonu, vade),
#865 (S6 raporlar), #866 (MOD-03 vergi masterı) ve #860'ın (Cari e-belge) bilerek hiçbir role vermediği izinlerin hangi
personaya verileceğini, EK19'un açık bıraktığı kalem tipi yerini ve SAT-D140'ın karara bağlamadığı özel alıcı vergisini cevaplar.
EK19 (SAT-D133…D140), `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` ve DEC-SAL-002 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Yeni personalar:** SAT-D141 ve SAT-D142, `PERSONA_GRANT_MATRISI_KARTI_v0`'daki dört personaya (Admin, Sevkiyatçı, Satış
  Destek, Müdür) iki yeni persona ekler: **Satış Uzmanı** ve **Muhasebe**.
- **Özel alıcı:** SAT-D147, PR #866'daki ara davranışı ("ihraç kayıtlı" ve "istisna belgeli" alıcıda vergi `Unresolved` /
  `buyer_situation_undecided`) değiştirir: bu alıcılarda **KDV %0 + listeden seçilen istisna kodu, tevkifat yok** uygulanır.
- **Kalem tipinin yeri:** SAT-D149, EK19 "Açık kalanlar"daki kalem tipi sorusunu kapatır ve PR #866'nın şirket bazlı kalem
  tipi deposunu değiştirir: kalem tipi **ortak ürün kartında tek değer** olur. SAT-D133 (vergi oran/kod tabloları şirket
  bazında) değişmez.

**Kaynak:** Sinan'ın 1 Kasım 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) üç tur seçmeli
soru cevabı (on soru). Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi; yedi soruda Sinan kendi metnini yazdı ya da
seçeneğe ek yaptı, o metin aynen verilir.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| Satış raporlarını (açık sipariş tonajı, fiyat sapması, kayıp fırsat) hangi personalar görsün? Not: rapordaki tutarları yalnız fiyat görme yetkisi olan görür; Sevkiyatçı yalnız miktarları görür. | Kendi metni: "Satışçı, müdür, admin, satış destek görsün, muhasebe ekibi hepsini görsün; sevkiyatçı birim fiyatsız ve tutarsız görsün" | — (seçenekler, çoklu: "Admin" — "En yetkili kullanıcı."; "Müdür" — "Siparişleri onaylayan yönetici."; "Satış Destek" — "Sipariş giren ve düzenleyen kişi."; "Sevkiyatçı" — "Yalnız miktarları görür, tutarları göremez.") |
| Sipariş başlığını kimler değiştirebilsin (müşteri, para birimi, vade, satış sorumlusu, teslimat adresi)? Not: müşteri, para birimi ya da vade değişirse sipariş yeniden Müdür onayına gider; yalnız sorumlu ya da adres değişirse onaya gitmez. | Kendi metni: "Satış destek, admin, müdür değiştirebilsin; yetkisi olanlar onaylayabilsin" | — (seçenekler, çoklu: "Admin"; "Müdür"; "Satış Destek" — "Siparişi giren kişi; değişiklik yine Müdür onayına gider.") |
| Vergi ayarlarını (KDV/tevkifat varsayılanları, istisna ve tevkifat kodları, VUK 232 eşiği) ve ürün kartındaki ürün türünü (demir/hurda/hizmet) kimler yönetsin? | Kendi metni: "Müdürler ve admin" | — (seçenekler: "Yalnız Admin (Önerilen)" — "Vergi verisini tek elden yönetmek en güvenlisi."; "Admin + Müdür" — "Müdür de vergi ayarlarını ve ürün türünü değiştirebilir."; "Admin vergiyi, Müdür ürün türünü") |
| İhracat kayıtlı satış ve muafiyet belgeli alıcılarda KDV/tevkifat nasıl olsun? Şu an bu siparişlerde vergi hesaplanmıyor ve sipariş 'karar bekliyor' durumunda kalıyor. | KDV %0 + istisna kodu | "Belge varsa KDV sıfır olur, istisna kodu listeden seçilir, tevkifat uygulanmaz." (seçilmeyenler: "Şimdilik bekletilsin (Önerilen)" — "Bu alıcılar nadirse mali müşavirle kural netleşene kadar sistem fatura kesmesin."; "Normal KDV, tevkifat yok") |
| 'Satışçı' dediğin kişi, mevcut 'Satış Destek' ile aynı persona mı, yoksa ayrı bir persona mı? (Satış Destek şu an sipariş girip düzenleyebiliyor ama onaylayamıyor.) | Kendi metni (seçenek "Ayrı persona, Satış Destek gibi" + ek): "Ayrı persona, satış destek gibi sonra ayrıştırılabilir, adı satış uzmanı olabilir" | "Yeni bir persona, yetkileri Satış Destek'le aynı başlar; sonra ayrıştırılabilir." (seçilmeyenler: "Aynı persona"; "Ayrı persona, daha dar" — "Yalnız kendi siparişlerini ve müşterilerini görür/girer.") |
| 'Muhasebe ekibi' için yeni bir persona açılsın mı? Satış modalında ne yapabilsin? | Kendi metni (seçenek "Yeni persona: tüm satışı görsün, yazmasın (Önerilen)" + ek): "Yeni persona: tüm satışı görsün yazamasın(önerilen) , ama fatura modülü gelince fatura kesme yetkisi de olacak ve şuanda gerçek kesilen fatura ile erp den oluşturulanı eşleştirme yapabilsin, birde eğer mantıklıysa sadece vergi ayarlarını değiştirebilsin ama değişiklikten dolayı onaya gitsin" | "Siparişleri, fiyatları, raporları ve vergi ayarlarını görür; sipariş girmez, onaylamaz." (seçilmeyenler: "Yeni persona: görsün + fatura kessin"; "Yeni persona: görsün + vergi ayarlarını yönetsin") |
| Vergi kodları (istisna kodları, tevkifat kodları) ve VUK 232 eşik tutarı sisteme nasıl girilsin? | Ben hazırlayayım, müşavir onaylasın (Önerilen) | "GİB'in standart listesini öneri olarak ben hazırlarım; mali müşavir onaylayınca Müdür/Admin Ayarlar ekranından girer." (seçilmeyenler: "Müşavir bilgiyi versin, Müdür/Admin girsin"; "Bu iş sonra") |
| Ürün türü (demir/hurda/hizmet) her şirkette ayrı mı tutulsun, yoksa ortak ürün kartında tek bir değer mi olsun? Şu an şirket başına tutuluyor. | Ortak kartta tek değer (Önerilen) | "Aynı ürün üç şirkette de aynı türde olur, bir kez girilir." (seçilmeyen: "Şirket başına ayrı" — "Şu anki gibi kalsın; her şirket kendi ürün türünü tutsun.") |
| Muhasebe vergi ayarını değiştirdiğinde değişiklik onaya gidecek. Bu onayı kim versin? (Değişikliği yapan kişi kendi değişikliğini onaylayamaz.) | Kendi metni: "Müdür, admin, muhasebe müdürü, satış müdürü, gibi tüm müdürler" | — (seçenekler: "Müdür veya Admin (Önerilen)"; "Yalnız Admin"; "Başka bir muhasebeci de onaylayabilsin") |
| Müdür veya Admin vergi ayarını kendisi değiştirirse ne olsun? | Doğrudan geçerli olsun (Önerilen) | "Senin önceki kararına göre Müdür ve Admin yönetir; onaya gitmez." (seçilmeyen: "Onlarınki de onaya gitsin") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D141 | Yeni persona: Satış Uzmanı | `PERSONA_GRANT_MATRISI_KARTI_v0`'a **Satış Uzmanı** personası eklenir. Başlangıç yetkileri **Satış Destek ile aynıdır** (sipariş girer/düzenler, onaylayamaz); ileride ayrıştırılabilir | Persona grant matrisi |
| SAT-D142 | Yeni persona: Muhasebe | `PERSONA_GRANT_MATRISI_KARTI_v0`'a **Muhasebe** personası eklenir. Satış modülünde **her şeyi görür** (siparişler, fiyatlar, tutarlar, raporlar, vergi ayarları), **sipariş yazmaz ve onaylamaz**. Fatura modülü (API-32…37) geldiğinde **fatura kesme** ve **gerçekte kesilmiş fatura ile ERP'de oluşan faturayı eşleştirme** yetkisi alır. Vergi ayarlarını yalnız SAT-D146 onay akışıyla değiştirebilir | Persona grant matrisi, fatura dalgası |
| SAT-D143 | Satış raporlarını görme | `sales.reports.read`: **Admin, Müdür, Satış Destek, Satış Uzmanı, Muhasebe**. Sevkiyatçı raporları **birim fiyatsız ve tutarsız** görür. Fiyat/tutar alanları mevcut fiyat görme izniyle (`sales.orders.price.read`) gizlenir; Sevkiyatçı bu izni almadığı için miktarları görür | Persona grant matrisi, API-41/42 |
| SAT-D144 | Sipariş başlığını değiştirme | `sales.orders.header.write`: **Satış Destek, Admin, Müdür**. Başlık değişikliğinin yeniden onayını (cari, para birimi, vade; DEC-SAL-002) onay yetkisi olanlar verir; mevcut sipariş onay izni değişmez | Persona grant matrisi, API-04 |
| SAT-D145 | Vergi ayarları ve kalem tipi yönetimi | `settings.tax-master.manage` ve `product.item-type.manage`: **Müdür ve Admin**; değişiklikleri **doğrudan geçerli olur**, onaya gitmez | Persona grant matrisi, MOD-03 |
| SAT-D146 | Muhasebe'nin vergi ayarı değişikliği | Muhasebe vergi ayarlarını (KDV/tevkifat varsayılanları, istisna/tevkifat kodları, VUK 232 eşiği) değiştirebilir, ama değişiklik **onaya gider** ve onaylanana kadar yürürlüğe girmez. Onayı **müdür sınıfındaki** bir persona verir (Sinan'ın metni: "Müdür, admin, muhasebe müdürü, satış müdürü, gibi tüm müdürler"). Değişikliği yapan kendi değişikliğini onaylayamaz (maker ≠ checker). Muhasebe kalem tipini değiştirmez | MOD-03 vergi masterı (yeni öneri/onay akışı), persona grant matrisi |
| SAT-D147 | İhraç kayıtlı / istisna belgeli alıcı | SAT-D140'ta siparişte "ihraç kayıtlı" ya da "istisna belgeli" seçilirse: **KDV %0**, **istisna kodu listeden seçilir** (SAT-D93, SAT-D133 kod listesi), **tevkifat uygulanmaz**. Belge bilgisi (no, tarih) siparişte istenir (SAT-D140 uygulama ayrıntısı). İstisna kodu seçilmeden fatura kesilmez ("kaynak yok", SAT-D135 ile aynı bekleme) | MOD-03 vergi çözücü (PR #866'daki `buyer_situation_undecided` kaldırılır), sipariş başlığı, API-32 |
| SAT-D148 | Vergi kodları ve VUK 232 eşiği | İstisna ve tevkifat kod listesi ile VUK 232 eşik tutarı için **Claude, GİB'in standart kod listelerinden bir öneri belgesi hazırlar**; **mali müşavir onaylar**; onaylanan değerleri **Müdür veya Admin** Ayarlar ekranından girer. Onaylanmamış hiçbir kod veya tutar sisteme girilmez | Öneri belgesi (docs), Ayarlar vergi masterı |
| SAT-D149 | Kalem tipinin yeri | Kalem tipi (demir/hurda/hizmet) **ortak ürün kartında tek değer** olarak tutulur: aynı ürün üç şirkette aynı tiptedir ve bir kez girilir. EK19 "Açık kalanlar"daki soru bununla kapanır. SAT-D133 (oran/kod tabloları şirket bazında) ve SAT-D139 (varsayılanlar) değişmez | Ürün kartı (PR #866 deposunun taşınması), vergi çözücü |

## Uygulama notları

- SAT-D141…D146 yeni grant'lar ve personalar Sinan'ın açık kararıdır; grant'lar ayrı bir PR'da
  `PERSONA_GRANT_MATRISI_KARTI_v0` güncellemesiyle ve mevcut grant mekanizmasıyla uygulanır. Bu belge kendi başına hiçbir
  grant açmaz.
- SAT-D146'daki "müdür sınıfı" için uygulama ayrıntısı (geri alınabilir): onay, bu akış için ayrı bir onay izniyle verilir;
  bu izin Müdür ve Admin personalarına verilir. "Muhasebe müdürü" ve "satış müdürü" ayrı personalar olarak açılırsa aynı izni
  alırlar (bkz. Açık kalanlar).
- SAT-D147'de istisna kodu listesi SAT-D148 onayı gelene kadar boş olabilir; o zaman bu siparişlerin faturası "kaynak yok"
  ile bekler. Bu, kod girilene kadar SAT-D135 eşiğiyle aynı fail-closed davranıştır.
- SAT-D149 için PR #866'nın `product.ProductItemTypes` tablosu (şirket bazlı) ortak ürün ana kaydına taşınır. Tablo main'de
  ama production verisi yoktur; geçiş ileri-düzeltme (forward-fix) migration'ıyla yapılır.

## Açık kalanlar

- "Muhasebe müdürü" ve "satış müdürü" ayrı personalar mı, yoksa Müdür personasının iş unvanları mı? Ara durumda Müdür
  personası sayılırlar (geri alınabilir).
- Satış Uzmanı'nın Satış Destek'ten ne zaman ve nasıl ayrışacağı (ör. yalnız kendi müşterileri) sonraya bırakıldı.

## Sınırlar

- Bu kararlar merge kapılarını ve diğer insan kapılarını (deployment, environment, secret, Settings etkinleştirme,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez.
- Vergi oranı, kodu ve eşik değerleri mali müşavir/YMM onayı olmadan girilmez (SAT-D148).
