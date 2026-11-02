# Sinan Kararları — 2026-11-02 (Satış: eski Müdür yetkilerinin dağılımı, cari yönetimi, önce-fatura bakiyesi, taslak açma, fason fiyatı)

Bu belge önceki karar dosyalarını silmez; SAT-D191…SAT-D195'i ekler. EK28'in (SAT-D186…D190) persona kartına işlenmesi
sırasında (PR #901) açık kalan hücreleri ve fason sipariş PR'ının (#898) K3 review'unun sorduğu fason fiyatı sorusunu
cevaplar. SAT-D99, SAT-D175…D178, SAT-D187, SAT-D188 ve SAT-D190 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Cari yönetimi:** cari kartı açma/değiştirme ve cari hesabını aktif/pasif yapma yetkisi yalnız **Muhasebe Müdürü**'ne
  verilir (SAT-D192); Satış Müdürü bu yetkiyi almaz. Genel Müdür ve Admin SAT-D187/D190 gereği yapabilir. (Persona
  kartında bu hücreler eski Müdür için `?` idi; yetki burada ilk kez verilir.)
- **Fason fiyatı (SAT-D195):** EK25 SAT-D177/D178'e göre fason hizmet fiyatı listeden gelebiliyor ya da elle yazılıyordu ve
  listede fiyat/taban varsa istisna kuralı uygulanıyordu. SAT-D195'e göre fasonda **fiyat listesi kullanılmaz**: fiyat her
  zaman kg başına elle yazılır ve onaya gider.
- **Modül okumaları:** EK28 SAT-D188'deki "okuma yetkileri iki müdüre de verilir" ifadesi satış ve muhasebe okumalarıyla
  sınırlıdır; öteki modüllerin okumaları (ör. `purchasing.orders.read`) SAT-D191 gereği o modülün müdürüne gider.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: dört seçmeli soru (sorular 1–4) ve iki seçmeli soruya verdiği serbest metin cevaplar (soru 5 ve 6). Soru 6, docs review'unun "soru 5'in cevabı iki seçenekten birini seçmiyor" bulgusu üzerine soruldu. Sinan'a her
seçeneğin adı **ve açıklaması** birlikte gösterildi. Serbest metinler aynen aktarılmıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Eski 'Müdür' rolünün satış ve muhasebe dışı modüllerdeki yetkileri ne olsun? (Stok etiket/bakiye okuma, üretim, sevkiyat, muhasebe dönemi/yevmiye, finans, alış siparişi açma gibi.) | O modülün müdürüne (Önerilen) | "Her yetki kendi modülünün müdürüne gider (üretim → Üretim Müdürü, muhasebe dönemi → Muhasebe Müdürü, alış → Satın Alma Müdürü...). Rol tanımlanana kadar Genel Müdür ve Admin yapar." (seçilmeyen: "Yalnız Genel Müdür" — "Bu yetkiler modül müdürlerine verilmez; yalnız Genel Müdür ve Admin yapar.") |
| 2 | Cari kartı açma/değiştirme ve cari hesabını aktif/pasif yapma yetkisi (eski Müdür'de vardı) hangi müdürlerde olsun? | Yalnız Muhasebe Müdürü | "Cari kartını Muhasebe Müdürü (ve Genel Müdür, Admin) yönetir." (seçilmeyenler: "Satış ve Muhasebe Müdürü (Önerilen)" — "İki müdür de cari açıp düzenleyebilir; Genel Müdür ve Admin de."; "Yalnız Satış Müdürü" — "Cari kartını Satış Müdürü (ve Genel Müdür, Admin) yönetir.") |
| 3 | 'Faturalanmış ama sevk edilmemiş' bakiyeyi (önce fatura kesilen satışlar) hangi müdür görüp yönetsin? | İki müdür de (Önerilen) | "Satış Müdürü ve Muhasebe Müdürü görür ve yönetir; Genel Müdür ve Admin de." (seçilmeyenler: "Satış Müdürü"; "Muhasebe Müdürü") |
| 4 | Satış Müdürü ve Genel Müdür kendileri satış siparişi taslak açabilsin mi? | Satış Müdürü ve Genel Müdür açabilsin (Önerilen) | "Siparişi açan onaylayamaz kuralı korunur; kendi açtığını başka biri onaylar." (seçilmeyen: "Açamasınlar" — "Müdürler yalnız onaylar; taslakı satışçılar açar.") |
| 5 | Fason fiyatı için 'listede fiyatı/tabanı yoksa elle yazılan fiyat serbest' demiştin. Fiyat listesinde miktar kademesi olabiliyor (ör. hizmetin fiyatı yalnız 1000 kg üstü için tanımlı). 250 kg'lık bir fason siparişte durum ne olsun? | Kendi metni: "Bütün fiyatlar onaya gidiyor zaten" | — (seçenekler: "O miktara bak (Önerilen)" — "250 kg için listede fiyat yoksa fiyat serbesttir. Miktar sonradan 1000 kg üstüne çıkarsa liste fiyatı devreye girer ve altına inen fiyat onaya gider."; "Listede hiç olmamalı" — "Hizmetin listede herhangi bir kademede fiyatı varsa fiyat serbest değildir; 250 kg'da da onaya gider. Serbestlik yalnız listede hiç fiyatı olmayan hizmet içindir.") |
| 6 | Fason hizmetin fiyat listesinde fiyatı yalnız 1000 kg üstü için var (40 TL, taban 35 TL). 250 kg'lık bir fason siparişte satışçı elle 10 TL yazıyor. Sipariş her durumda normal onaydan geçiyor. Ek olarak iskonto/taban fiyat istisna onayı da istensin mi? | Kendi metni: "Fason için fiyat olmasın elle yazılsın her kg için buda onaya gitsin" | — (seçenekler: "İstenmesin (250 kg'a bak)" — "250 kg için listede fiyat yok, bu yüzden yalnız normal sipariş onayı yeterli. Miktar 1000 kg üstüne çıkarsa istisna onayı da istenir."; "İstensin (listede bir yerde var)" — "Hizmetin listede herhangi bir kademede fiyatı olduğu için 10 TL tabandan düşük sayılır; normal onaya ek olarak istisna onayı da istenir.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D191 | Eski Müdür'ün öteki modül yetkileri | Eski "Müdür" personasının satış ve muhasebe dışındaki yetkileri **o modülün müdürüne** gider (soru 1): üretim → Üretim Müdürü, muhasebe dönemi ve yevmiye → Muhasebe Müdürü, alış → Satın Alma Müdürü vb. Rol tanımlanana (persona kartına işlenene) kadar bu yetkileri Genel Müdür ve Admin yapar (soru 1; SAT-D187). Uygulama ayrıntısı (geri alınabilir): stok okumaları Stok, sevkiyat Lojistik, finans Finans modülünün müdürüne aittir; bir modülün müdürü yoksa yetki Genel Müdür ve Admin'de kalır; bütün personalara verilen temel izinler (ör. `identity.access.request`, organizasyon/kimlik/cari/ürün seçimi okumaları) müdürlere de verilir. Bir modülün okuması (ör. `purchasing.orders.read`) o modülün müdürüne ve Genel Müdür'e gider, öteki modül müdürlerine gitmez | Persona kartı (PR #901), Alış/Üretim/Stok/Lojistik/Finans şeritleri |
| SAT-D192 | Cari yönetimi | Cari kartı açma/değiştirme ve cari hesabını aktif/pasif yapma yetkisi **yalnız Muhasebe Müdürü**'ndedir (soru 2); Genel Müdür ve Admin de yapabilir (SAT-D187/D190). Satış Müdürü bu yetkiyi almaz | Persona kartı: `counterparty.parties.create`, `.update.basic`, `counterparty.accounts.create`, `.status.update` |
| SAT-D193 | Önce-fatura bakiyesi | SAT-D99'daki "faturalanmış, sevk edilmemiş" bakiyeyi **Satış Müdürü ve Muhasebe Müdürü** görür ve yönetir; Genel Müdür ve Admin de (soru 3). Bakiyenin kapatma yolu SAT-D99 gereği mali müşavirle belirlenir | Persona kartı, fatura dalgası (API-32…37) |
| SAT-D194 | Müdürlerin taslak açması | **Satış Müdürü ve Genel Müdür** satış siparişi taslağı açabilir (soru 4). Siparişi açan kişi onu onaylayamaz (hazırlayan ≠ onaylayan); kendi açtığını başka bir yetkili onaylar | Persona kartı: `sales.drafts.create` |
| SAT-D195 | Fason fiyatı | Fason hizmet satırında **fiyat listesi kullanılmaz**; fiyat her siparişte **kg başına elle yazılır** ve **onaya gider** (soru 6, Sinan'ın metni; soru 5: "Bütün fiyatlar onaya gidiyor zaten"). Uygulama ayrıntısı (geri alınabilir): onay, fason siparişin normal iç onayıdır (SAT-D175); liste olmadığı için ayrıca iskonto/taban istisna onayı yoktur; elle fiyat yazmak SAT-D151'deki fiyat yazma iznini ister ve kimin ne zaman hangi değeri yazdığı audit'e girer; fiyat birimi kg'dır, kg dışı birimle fason hizmet satırı kabul edilmez (fail-closed) | Fason sipariş (PR #898), EK25 SAT-D177/D178 |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Persona tanımı karardır; gerçek kişilere ve rollere
  atama (grant) her zaman insan kapısındadır.
