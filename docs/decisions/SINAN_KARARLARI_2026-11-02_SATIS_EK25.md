# Sinan Kararları — 2026-11-02 (Satış: fason hizmet siparişi ve kaynak planı)

Bu belge önceki karar dosyalarını silmez; SAT-D175…SAT-D179'u ekler. Üretim şeridinin görev notunu
(`docs/handoffs/URETIM_SATIS_DEVIR_v1.md`, S-1…S-3) Satış tarafında karara bağlar. URT-D13, URT-D15, URT-D16, URT-D19,
URT-D22, SAT-D110, DEC-SAL-004, DEC-SAL-005, No 30 / SAT-D12 ve iskonto/taban fiyat kararları (SAT-D1, SAT-D2, SAT-D34…D37)
geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Fason fiyatında taban ve iskonto:** SAT-D178'e göre serbestlik yalnız listede **ne fiyatı ne tabanı** olan fason hizmet
  satırı içindir. Listede fiyatı olan hizmette liste altı fiyat iskontodur ve SAT-D2, SAT-D35…D37 aynen uygulanır; tabanı
  olan hizmette taban altı fiyat SAT-D1 onayına gider. Bugün liste fiyatı referansı olmayan satırda iskonto ölçülemediği için
  onaya düşer (SAT-D36, fail-closed); bu davranış yalnız listede fiyatı ve tabanı olmayan fason hizmet satırı için
  uygulanmaz. Ürün satırlarında kurallar aynen kalır. "Serbest", herkesin fiyat yazabileceği anlamına gelmez: elle fiyat
  yazmak SAT-D151 gereği fiyat yazma iznini ister.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
seçmeli soru cevapları: iki tur, beş soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi. Bir cevap
Sinan'ın kendi metnidir ve aynen aktarılmıştır. İkinci tur (soru 5), 3. sorudaki serbest metnin taban fiyat ve iskonto
sınırı hakkında bir şey söylememesi üzerine soruldu.

**Kapsam:** "Fason hizmet siparişi" bu belgede **gelen fason**dur: müşterinin malını biz işleriz (URT-D16). SAT-D179'daki
kaynak stratejisi listesinde geçen "fason" ise **dışarıya gönderilen** fasondur (malımızı başka atölye işler, URT-D15;
DEC-SAL-004'teki "dış tedarik"). SAT-D175…D178 yalnız gelen fasonu kapsar.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Fason hizmet siparişi (müşterinin malini biz işleriz, işçilik faturalarız) nasıl bir süreçten geçsin? | Normal sipariş gibi (Önerilen) | "Aynı sipariş ekranında 'fason' işaretli sipariş: iç onay, risk kontrolü, müşteri kabulü, revizyon kuralları normal siparişle aynı." (seçilmeyenler: "Daha basit akış" — "Fason siparişte iç onay olur ama müşteri kabulü ve risk kontrolü aranmaz."; "Ayrı menü ve belge" — "Fason siparişleri ayrı bir 'Fason Siparişleri' menüsünde, ayrı belge olarak tutulur; kurallar normal siparişle aynı.") |
| 2 | Bir fason siparişinde hizmet satırının yanında normal mal satırı da olabilsin mi? (Ör. müşterinin profilini işleriz, ayrıca kendi stokumuzdan cıvata satarız.) | Yalnız hizmet satırı (Önerilen) | "Fason siparişte yalnız hizmet satırları olur; mal satışı ayrı normal siparişle yapılır. Fatura, stok ve tevkifat daha temiz ayrılır." (seçilmeyen: "Karma olabilsin" — "Aynı fason siparişinde hem hizmet hem mal satırı olabilir.") |
| 3 | Fason hizmet fiyatı (ör. kg başına işçilik) nereden gelsin? | Kendi metni: "Fiyat listesinden de gelebilir siparişte elle de yazılabilir" | — (seçenekler: "Fiyat listesinden (Önerilen)" — "Hizmet ürünü fiyat listesinde olur; taban fiyat ve iskonto kuralları ürünlerdeki gibi uygulanır."; "Siparişte elle" — "Her fason siparişinde fiyat elle girilir; taban fiyat ve iskonto sınırı uygulanmaz."; "Liste + elle değişir" — "Fiyat listeden gelir, satışçı değiştirebilir; taban altına inerse normal istisna onayı ister.") |
| 4 | Sipariş satırının kaynak stratejisini (stok / satın alma / üretim / fason / karma) kim belirlesin ve değiştirsin? Sistem öneri yapar. | Siparişi yazanlar (Önerilen) | "Satış Destek, Satış Uzmanı, Müdür, Admin." (seçilmeyenler: "Yalnız Müdür ve Admin" — "Satışçılar görür ama değiştiremez."; "Satış + Üretim müdürü" — "Siparişi yazanlar ve ayrıca Üretim müdürü değiştirebilir.") |
| 5 | Fason hizmet fiyatı elle yazıldığında ya da listeden gelip değiştirildiğinde taban fiyat ve iskonto sınırı uygulansın mı? | Listede varsa uygulansın (Önerilen) | "Hizmetin listede fiyatı/tabanı varsa, altına inen fiyat ürünlerdeki gibi istisna onayı ister. Listede yoksa elle yazılan fiyat serbesttir." (seçilmeyenler: "Hiç uygulanmasın" — "Fason fiyatı her zaman serbest; taban ve iskonto sınırı yok (yalnız kayıt altında)."; "Her zaman uygulansın" — "Listede taban yoksa elle fiyat yazılamaz; önce hizmetin tabanı Ayarlar'a girilmeli.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D175 | Fason siparişin akışı | Fason hizmet siparişi **normal satış siparişidir**: aynı sipariş ekranında "fason" olarak işaretlenir; iç onay, risk kontrolü, müşteri kabulü ve revizyon kuralları normal siparişle aynıdır (soru 1). Uygulama ayrıntısı (geri alınabilir): işlenecek mal siparişin carisine aittir; ayrı bir "malın sahibi" seçilmez (URT-D22: müşteri malı yalnız o müşterinin fason siparişine ayrılır). "Fason" işareti sipariş açılırken verilir; onaylı siparişte sonradan değiştirilmez; mal satırı olan siparişe işaret konamaz. Müşteri malının eşleşme anahtarı Stok'taki "müşteri adına" kaydının anahtarıyla aynıdır ve uygulama PR'ında adıyla sabitlenir. Fason siparişe bağlı üretim planı veya müşteri malı tahsisi varsa siparişin carisi değişmez (fail-closed; SAT-D128/D152'ye ek) | Sipariş başlığı, API-05/22, Üretim portu (görev notu S-3) |
| SAT-D176 | Fason siparişin satırları | Fason siparişte **yalnız hizmet satırı** olur (kalem tipi hizmet olan ürün, SAT-D149); mal satışı ayrı, normal siparişle yapılır (soru 2). Normal siparişte hizmet satırına ilişkin kural bu kararla değişmez. Uygulama ayrıntısı (geri alınabilir): onaylı fason siparişe SAT-D167 ile satır eklenirken ya da ürün değiştirilirken yalnız kalem tipi hizmet olan ürün seçilebilir | Sipariş satırı, SAT-D167 satır ekleme |
| SAT-D177 | Fason fiyatının kaynağı | Fason hizmet fiyatı **fiyat listesinden gelebilir ya da siparişte elle yazılabilir** (soru 3, Sinan'ın metni) | Fiyat çözümleme (MOD-02), sipariş satırı |
| SAT-D178 | Fason fiyatında taban ve iskonto sınırı | Hizmetin listede fiyatı veya tabanı varsa, altına inen fiyat ürünlerdeki gibi **istisna onayı** ister (SAT-D1/D2, SAT-D34…D37). Listede yoksa elle yazılan fiyat **serbesttir** (soru 5). Uygulama ayrıntısı (geri alınabilir): elle yazılan her fiyat, kimin hangi değeri yazdığıyla audit'e girer | İskonto/taban kontrolü, API-12 |
| SAT-D179 | Kaynak stratejisini kim belirler | Sipariş satırının kaynak stratejisini (stok / satın alma / üretim / fason / karma, DEC-SAL-004) **Satış Destek, Satış Uzmanı, Müdür ve Admin** belirler ve değiştirir; sistem öneri yapar (soru 4). Gerçek grant ataması insan kapısıdır; izin adı (`sales.orders.source-plan.write`, Satış planı API-11) uygulama PR'ında manifest'e eklenir ve hiçbir role atanmaz | API-11, persona kartı |

## Görev notunun Satış tarafındaki karşılığı

- **S-1 (sipariş iptali ve zorla kapatmada bağlı üretim, MOD-05):** yeni bir karar gerekmez. No 30 / SAT-D12 ve DEC-SAL-005
  aynen uygulanır: açık bağlı üretim veya okunamayan kaynak varsa iptal ve zorla kapatma reddedilir. Bugün Üretim portu
  bağlı olmadığı için bu kategori `NoSource` döner ve engellemez; port bağlanmadan önceki bu davranışın korunup
  korunmayacağı S-1 uygulama PR'ında ayrıca ele alınır (gerekirse NEEDS-SINAN).
- **Persona kartı:** SAT-D179'un `sales.orders.source-plan.write` satırı persona kartına ayrı bir PR'da eklenir.
- **S-2 (kaynak planı, API-11):** SAT-D179 ile karara bağlanmıştır.
- **S-3 (fason hizmet siparişi):** SAT-D175…D178 ile karara bağlanmıştır. Fason tevkifat oranı mali müşavir onayını bekler
  (SAT-D110); o zamana kadar vergi fail-closed kalır.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
