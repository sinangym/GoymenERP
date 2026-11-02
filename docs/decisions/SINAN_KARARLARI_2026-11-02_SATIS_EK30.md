# Sinan Kararları — 2026-11-02 (Finans Uzmanı ve Finans Müdürü; Genel Müdür'ün finans onayı)

Bu belge önceki karar dosyalarını silmez; SAT-D196 ve SAT-D197'yi ekler. Persona kartı PR'ının (#901) docs review'larının
bulgusunu cevaplar: EK29 SAT-D191'in ara dönem kuralı Genel Müdür'e finans hücreleri veriyordu, oysa persona kartının
K4.2 sınırı (20 Eylül 2026 kullanıcı kararı: "onayın gerektirdiği okuma evet, genel Finance hayır") müdürlere genel
finans erişimi açmıyordu. EK28 SAT-D187/D190 ve EK29 SAT-D191 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **K4.2 sınırı Genel Müdür için daralır:** Genel Müdür finans işlemlerini **onaylayabilir** (SAT-D197). K4.2'nin "genel
  Finance hayır" sınırı Genel Müdür için yalnız onay ve onayın gerektirdiği okuma kadar kalkar; Genel Müdür finans işlemi
  oluşturmaz. Satış Müdürü ve Muhasebe Müdürü için K4.2 aynen kalır.
- **EK29 SAT-D191, finans için:** finans yetkileri artık "rol tanımlanana kadar Genel Müdür ve Admin" ara dönemine bağlı
  değildir; Finans Uzmanı ve Finans Müdürü tanımlanmıştır (SAT-D196).

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: iki tur, iki soru. Birinci sorunun cevabı Sinan'ın kendi metnidir ve aynen aktarılmıştır. Sinan'a her seçeneğin
adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Eylül'deki kararında müdürlere 'onay için gereken okuma evet, genel finans erişimi hayır' demiştin. Finans Müdürü rolü tanımlanana kadar banka hesabı okuma ve elle tahsilat taslağı gibi finans yetkileri Genel Müdür'de olsun mu? | Kendi metni: "Finans uzmanı ve finans müdürü personaları olsun, uzman işlemi oluştursun, müdür onaylasın" | — (seçenekler: "Hayır, kimsede olmasın (Önerilen)" — "Eylül kararı aynen kalır: Finans Müdürü tanımlanana kadar bu yetkiler yalnız Admin'de (ve finans personelinde) olur; Genel Müdür dahil müdürler genel finans erişimi almaz."; "Evet, Genel Müdür'de olsun" — "Finans Müdürü tanımlanana kadar Genel Müdür bu finans yetkilerini kullanır; Eylül kararı Genel Müdür için kalkar.") |
| 2 | Finans Uzmanı işlemi oluşturacak, Finans Müdürü onaylayacak. Genel Müdür (tüm müdür onaylarını yapabilen rol) finans işlemlerini de onaylayabilsin mi? | Evet, onaylayabilsin (Önerilen) | "Genel Müdür finans onaylarını da verebilir; finans işlemi oluşturamaz (bunu Finans Uzmanı yapar)." (seçilmeyen: "Hayır, yalnız Finans Müdürü" — "Finans onayını yalnız Finans Müdürü (ve Admin) verir; Genel Müdür'ün genel finans erişimi olmaz (Eylül kararı korunur).") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D196 | Finans Uzmanı ve Finans Müdürü | **Finans Uzmanı** ve **Finans Müdürü** ayrı personalardır (soru 1). Finans işlemini **Finans Uzmanı oluşturur**, **Finans Müdürü onaylar** (soru 1); hazırlayan ≠ onaylayan. Uygulama ayrıntısı (geri alınabilir): persona kartındaki Critical olmayan finans adları bu ayrıma göre dağıtılır — oluşturma/taslak kaydetme ve işlem için gereken okumalar Finans Uzmanı'na, onay ve onayın gerektirdiği okumalar Finans Müdürü'ne; Admin SAT-D187 gereği müdürün yaptığını yapabilir; Critical adlar insan kapısında kalır. Bir finans adı bu ikiye açıkça girmiyorsa persona kartında NEEDS-SINAN yazılır | Persona kartı (PR #901), Finans modülü |
| SAT-D197 | Genel Müdür'ün finans onayı | **Genel Müdür** finans işlemlerini **onaylayabilir**; finans işlemi **oluşturamaz** (soru 2). Uygulama ayrıntısı (geri alınabilir): Genel Müdür'e onay ve onayın gerektirdiği okuma verilir; banka hesabı okuması gibi oluşturma tarafındaki genel okumalar verilmez (K4.2'nin geri kalanı aynen) | Persona kartı (PR #901) |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Persona tanımı karardır; gerçek kişilere ve rollere
  atama (grant) her zaman insan kapısındadır. Critical finans adları bu kararla açılmaz.
