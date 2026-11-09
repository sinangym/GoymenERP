# Sinan Kararları — 2026-11-09 (Satış: S-2 sipariş kaynak planı — öneri, karma bölme, teslime kadar serbest, uyarılı değişiklik, tahsisten türeyen stok)

Bu belge önceki karar dosyalarını silmez; SAT-D251…SAT-D255'i ekler. Üretim → Satış görev notundaki S-2 işinin
(`docs/handoffs/URETIM_SATIS_DEVIR_v1.md` "S-2 · Kaynak planı"; Satış planı API-11) davranışını kayda geçirir.
DEC-SAL-004 ("Sipariş satırı fiziksel stok, onaylı alış, üretim planı, dış tedarik veya karma kaynakla karşılanabilir."
— "Sistem/AI önerir; yetkili kullanıcı değiştirir."), SAT-D179 (EK25: kaynak stratejisini Satış Destek, Satış Uzmanı,
Müdür ve Admin belirler; izin adı `sales.orders.source-plan.write`, hiçbir role atanmaz), SAT-D188 (EK28: SAT-D179
"satış" sınıfındadır), SAT-D167 (EK24), SAT-D219 (EK38), DEC-SAL-002, DEC-SAL-005, ALS-D30 ve Stok tahsis kararları
geçerlidir; bu belge onların metnini değiştirmez (bkz. "Bilinçli değişiklikler").

**Kaynak:**

- Sinan'ın 9 Kasım 2026'da Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine
  AskUserQuestion ile verdiği cevaplar. Seçenek adları ve açıklamaları soran ajana aittir; "(Önerilen)" işaretleri
  soranındır. Serbest metin cevaplar aşağıda **aynen** (yazım hatası düzeltilmeden) alıntılanmıştır.
- **Soru 5 (SAT-D255) Sinan'ın açık devriyle Claude'un seçtiği seçenektir.** Sinan önce "Bu aslında tahsisle aynı şey
  demek mi? Yoksa iki kere tahsise benzer bir yapı kurulması işlem karışıklığı veya operasyonel yorgunluk yaratır mı"
  diye sordu; Claude plan (niyet) ile tahsisin (belirli bobin/lotun fiziksel ayrılması) farkını açıklayıp ikinci soruyu
  sordu; Sinan "Bunu anlamadım, sende mantıklı şekilde düşün sistemin tamamını" cevabını verdi. Claude bu açık devir
  üzerine soranın önerdiği seçeneği ("Tahsis planı doldursun (Önerilen)") seçti. Bu, Sinan'ın seçenek seçimi değildir;
  v4 `DELEGE-*` kaydına benzer biçimde **devredilmiş seçim** olarak işaretlenir ve Sinan Issue #66'ya yazacağı tek
  satırla geri alabilir ya da başka seçeneği seçebilir. Seçilen seçenek yeni bir stok politikası açmaz: ikinci bir
  ayırma yapısını **engeller** ve mevcut tahsisi tek kaynak bırakır.

Bu belge Issue #66'ya makbuz yazmaz.

## Soru ve cevaplar

| # | Soru | Cevap (aynen) | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | "Siparişte 'kaynak planı': sistem her satır için kaynak (stok / üretim / satın alma / fason) önerecek. Öneri neye göre yapılsın?" | "Stok yetiyorsa stok (Önerilen)" (seçilmeyen: "Öneri yok, kullanıcı seçer") | "Serbest stok yeterliyse 'stok', değilse ürün tipine göre 'üretim' veya 'satın alma' önerilir; kullanıcı değiştirebilir." |
| 2 | "Bir satır 'karma' kaynaklıysa (ör. 10 tonun bir kısmı stoktan, bir kısmı üretimden) nasıl tutulsun?" | "Miktar olarak bölünsün (Önerilen)" (seçilmeyen: "Yalnız 'karma' etiketi") | "Ör. 6 ton stok + 4 ton üretim; parçaların toplamı satır miktarına eşit olmak zorunda." |
| 3 | "Kaynak planı siparişin hangi durumlarında değiştirilebilsin?" | Serbest metin: "Bence bu durum operasyonel süreci tıkamanak için teslime kadar serbest" (seçilmeyenler: "Taslakta serbest, onaylıda revizyon (Önerilen)", "Yalnız taslakta", "Teslime kadar serbest") | Serbest metin seçildi. Sunulan "Teslime kadar serbest" seçeneğinin açıklaması: "Onaylı siparişte de yeniden onay gerekmeden değiştirilebilir." Sinan'ın metni bu anlamla örtüşür. |
| 4 | "Satıra bağlı açık bir üretim planı veya alış siparişi varken kaynak değiştirilmek istenirse ne olsun?" | "Uyarıyla izin ver" (seçilmeyen: "Reddet (Önerilen)") | "Kullanıcı uyarılır, onaylarsa kaynak değişir; bağlı iş ayrıca ele alınır." |
| 5a | "Kaynak planında bir satır için 'stok' seçilince ne olsun?" | Sinan seçenek seçmedi, soru sordu: "Bu aslında tahsisle aynı şey demek mi? Yoksa iki kere tahsise benzer bir yapı kurulması işlem karışıklığı veya operasyonel yorgunluk yaratır mı" | — (Claude açıkladı: plan = niyet; tahsis = belirli bobin/lotun fiziksel ayrılması.) |
| 5b | "Çift iş olmaması için kaynak planındaki 'stok' ile tahsis nasıl bağlansın?" | "Bunu anlamadım, sende mantıklı şekilde düşün sistemin tamamını" (seçenekler: "Tahsis planı doldursun (Önerilen)", "Planda stok seç, tahsise yönlendir", "Ayrı kalsın") | **Sinan'ın açık devriyle Claude'un seçtiği seçenek:** "Tahsis planı doldursun (Önerilen)" — "Kullanıcı yalnız tahsis yapar; tahsis edilen miktar planda kendiliğinden 'stok' görünür. Planda 'stok'u ayrıca girmek gerekmez. Tek işlem." |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D251 | Kaynak önerisi (DEC-SAL-004 "Sistem/AI önerir" netleşmesi) | (1) Sistem her sipariş satırı için kaynak **önerir**: satırın serbest stoğu yeterliyse **"stok"**, değilse ürün tipine göre **"üretim"** veya **"satın alma"** (soru 1). (2) Öneri yalnız öneridir; SAT-D179'daki yetkili kullanıcı değiştirebilir; öneri kendi başına kayıt/ayırma yapmaz. (3) Öneri tek bir deterministik backend kuralında üretilir; ekran öneriyi kendisi hesaplamaz. Uygulama ayrıntısı (geri alınabilir): serbest stok Stok sahibinin okuma portundan, ürün tipi Ürün sahibinin portundan okunur (Satış başka modülün tablosunu okumaz); okunamazsa o satır için öneri **"kullanılamıyor"** gösterilir, uydurma öneri veya "stok yok" varsayımı yapılmaz; öneri "fason"u kendiliğinden önermez (seçenek metninde yoktur), kullanıcı seçebilir | API-11 `GET source-plan`, öneri kuralı (backend), sipariş ekranı |
| SAT-D252 | Karma kaynak | (1) Karma kaynaklı satır **miktar olarak bölünür** (ör. 6 ton stok + 4 ton üretim) (soru 2). (2) Parçaların toplamı **satır miktarına eşit olmak zorundadır**; eşit değilse kayıt reddedilir (fail-closed). (3) Parça miktarları satırın kendi birimi ve miktar semantiğiyle (Quantity value object) tutulur; yuvarlanmaz. SAT-D255 ile "stok" parçası tahsisten türetildiği için kullanıcı yalnız stok dışı parçaları girer; toplam kontrolü türetilmiş stok parçası dahil yapılır | API-11 `POST source-plan`, kaynak planı kaydı |
| SAT-D253 | Kaynak planı ne zamana kadar değişir | (1) Kaynak planı siparişin **teslimine kadar serbestçe** değiştirilebilir; onaylı siparişte de **yeniden onay, revizyon veya müşteri kabulü gerekmez** (soru 3: "teslime kadar serbest"). (2) Kaynak planı satır içeriği değildir: cari, ürün, miktar, fiyat, iskonto, para, vade, vergi veya risk istisnası değişmez; bu yüzden SAT-D167 / DEC-SAL-002 revizyonunu **tetiklemez** ve sipariş revizyon numarasını artırmaz. (3) **Tamamen teslim edilmiş** satırın kaynak planı değiştirilemez; kapanmış veya iptal edilmiş sipariş/satırın da değiştirilemez. (4) Her değişiklik kim, ne zaman, eski/yeni plan ile audit'e girer; expected version ve idempotency anahtarı ister (kritik komut iskeleti) | API-11 `POST source-plan`, sipariş durum makinesi, audit |
| SAT-D254 | Bağlı açık iş varken kaynak değişikliği | (1) Satıra bağlı **açık bir üretim planı/emri veya alış siparişi** varken kaynak değiştirilmek istenirse kullanıcı **uyarılır**; kullanıcı onaylarsa kaynak değişir (soru 4: "Uyarıyla izin ver"). (2) Bağlı iş **ayrıca ele alınır**: Satış öteki modülün işini **iptal etmez, değiştirmez, bağını çözmez**; bağlı iş sahibi modülde açık kalır. (3) Uyarı onayı komutta açık bir kabul alanıyla gelir ve audit'e girer; kabul olmadan bağlı iş görülürse komut uyarıyla reddedilir (409 türü, "uyarıyı onayla"). Uygulama ayrıntısı (geri alınabilir): bağ, SAT-D219'daki gibi `ISalesOrderLinkedSupplyReadPortV1` (S-1) ile okunur; "açık" olup olmadığı sahip modülün cevabıdır, Satış yorumlamaz; bağ **okunamazsa** (`Unreadable`) uyarıyla onaylanamaz, değişiklik reddedilir (fail-closed; neyin bağlı olduğu bilinmeden "onaylıyorum" alınmaz); bağı kaydeden hiçbir modül yoksa (`NoSource`) uyarı çıkmaz | API-11 `POST source-plan`, S-1 portu, audit |
| SAT-D255 | Plandaki "stok" ile tahsisin bağı (**Sinan'ın açık devriyle Claude'un seçtiği seçenek**; Sinan #66'da tek satırla geri alabilir) | (1) Kaynak planındaki **"stok" kısmı girilmez, tahsisten türetilir**: satıra Stok'ta yapılmış etkin tahsislerin miktarı planda kendiliğinden "stok" görünür. Kullanıcı yalnız tahsis yapar; tek işlem. (2) **İkinci bir ayırma yapısı kurulmaz**: plan hiçbir stok miktarını ayırmaz, rezerve etmez, kilitlemez; fiziksel ayırmanın tek kaynağı mevcut Stok tahsisidir (aynı ton bir kez; ALS-D30 ve Stok tahsis kararlarıyla aynı ilke). (3) Satış **Stok tablolarına yazmaz ve onları doğrudan okumaz**; türetilmiş miktar mevcut tahsis okuma yolundan (`ISalesOrderAllocationReadPortV1`, Stok sahibinin cevabı) okunur. Tahsis okunamazsa plandaki stok kısmı **"okunamadı"** gösterilir, sıfır varsayılmaz ve plan kaydı reddedilir (fail-closed). (4) SAT-D251'deki "stok" önerisi tahsis yapmaz; kullanıcıyı tahsise yönlendirmek ekran ayrıntısıdır. Uygulama ayrıntısı (geri alınabilir): kayıtta stok dışı parçaların toplamı + o anki tahsis miktarı satır miktarına eşit olmalıdır (SAT-D252); kayıttan sonra tahsis değişirse plan kendiliğinden yeniden yazılmaz, okumada "plan ile tahsis uyumsuz" uyarısı gösterilir ve kullanıcı planı günceller | API-11 `GET/POST source-plan`, `ISalesOrderAllocationReadPortV1`, Stok tahsis motoru (değişmez) |

## Kapatılan açık sorular

- Görev notu S-2 (`URETIM_SATIS_DEVIR_v1.md`) ve Satış planı API-11 satırındaki "sistem önerir" kuralının neye göre
  öneri yapacağı — SAT-D251.
- DEC-SAL-004 "karma kaynak"ın nasıl tutulacağı — SAT-D252.
- Kaynak planının onaylı siparişte değişip değişmeyeceği, revizyon isteyip istemediği — SAT-D253.
- Bağlı açık üretim/alış varken kaynak değişikliği — SAT-D254.
- Plandaki "stok" ile Stok tahsisinin ilişkisi (çift işin önlenmesi) — SAT-D255 (devredilmiş seçim).
- **Bu belge hiçbir `OPEN-*` kaydını kapatmaz.** Yukarıdaki soruların `OPEN-NNN` kimliği yoktur.

## Bilinçli değişiklikler

- **DEC-SAL-004:** metni değişmez; SAT-D251…SAT-D255 ile netleşir. KARAR_SICILI DEC-SAL-004 notuna bu belgeye atıf
  eklenir.
- **SAT-D179:** metni değişmez; kaynak planını yazanlar ve izin adı aynen geçerlidir.
- **SAT-D167 / DEC-SAL-002:** metni değişmez; SAT-D253 kaynak planı değişikliğinin revizyon kapsamında olmadığını
  açıkça yazar (DEC-SAL-002'nin revizyon listesi kaynak planını saymaz).
- **SAT-D219:** metni değişmez. SAT-D219 satırın **kaldırılması ve ürün değişikliği** için geçerlidir (bağlı açık iş
  varsa reddedilir); SAT-D254 yalnız **kaynak planı değişikliğini** kapsar (uyarıyla izin). İki kural ayrı işlemlere
  uygulanır; çelişmez.
- Başka hiçbir kararın anlamı değişmez.

## Uygulama listesi (S-2, API-11)

Koordinatörün bu iş için verdiği talimatla S-2 **Satış şeridinde (Claude) backend + frontend** olarak uygulanır.
Cross-module okumalar ve miktar/stok semantiği içerdiği için **K3**'tür (iki bağımsız exact-head review, gerçek-SQL
kanıtı).

| Karar | İş | Kapı / bağımlılık |
|---|---|---|
| SAT-D179 | `sales.orders.source-plan.write` izninin Sales permission manifest'ine eklenmesi (permission-capability-registration akışı; shell sağlayıcı sürümü, registry yeniden türetimi). **Hiçbir role atanmaz**; grant insan kapısıdır. Okuma ucunun hangi mevcut okuma iznini kullanacağı uygulama PR'ında belirlenir; yeni izin adı uydurulmaz. | K3 (permission) |
| SAT-D251…SAT-D255 | API-11 `GET drafts/{id}/source-plan` (satır başına öneri, türetilmiş stok kısmı, kayıtlı stok dışı parçalar, uyarılar) ve `POST drafts/{id}/source-plan` (kritik komut iskeleti: yetki, expected version, idempotency, audit, tek kayıt). Kaynak planı kalıcı modeli (Satış şemasında; migration geri alınabilir veya forward-fix planlı). Sözleşme + OpenAPI yeniden üretimi, birim + gerçek-SQL kanıtı (toplam eşitliği, teslim edilmiş satır reddi, okunamayan tahsis/bağ reddi, uyarı kabulü). | K3 (migration + cross-module contract + miktar semantiği) |
| SAT-D251 | Serbest stok okuması (Stok sahibinin portu) ve ürün tipi okuması (Ürün sahibinin portu). Satış'a uygun okuma main'de değilse öneri "kullanılamıyor" kalır. | Stok/Ürün portları |
| SAT-D251, SAT-D254 | "Satın alma" seçeneğinin gelecek arz bilgisi: Alış port v2 (`ALIS_SATIS_DEVIR_v1.md` S-A2, MOD-16). v2 main'de değilse bu bilgiye dayanan gösterim **kullanılamaz/fail-closed** kalır. Bağlı açık alış: mevcut Purchasing adaptörü (`ISalesOrderLinkedSupplyReadPortV1`). | Alış v2 |
| SAT-D251, SAT-D254 | "Üretim" bağı: Üretim bağ portu (`IManufacturingSalesOrderLinkReadPortV1`, görev notu S-1/S-2) ve host adaptörü. Main'e girene kadar üretim bağı SAT-D219'daki `NoSource`/`Unreadable` kuralıyla değerlendirilir; bağa dayanan seçenek fail-closed kalır. | Üretim bağ portu |
| SAT-D251…SAT-D255 | Frontend: sipariş ekranında kaynak planı paneli — öneri, stok dışı parça girişi, tahsisten türeyen stok kısmı (salt okunur, tahsis paneline bağlantı), toplam kontrol göstergesi, bağlı iş uyarısı ve onayı; loading / empty / forbidden / conflict / unavailable / malformed durumları; strict decoder. Ekran öneri, toplam veya stok miktarı hesaplamaz; backend'in cevabını gösterir. | Backend API-11 main'de |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, migration, OpenAPI veya Ayarlar verisi değiştirmez; yukarıdaki
  işler ayrı PR'lardır.
- **Kısmen teslim edilmiş satır (`NEEDS-SINAN`):** "teslime kadar serbest" tamamen teslim edilmiş satırın
  değişmeyeceğini söyler; kısmen teslim edilmiş satırda planın değişip değişemeyeceği, değişirse yalnız teslim
  edilmemiş kalan miktar için mi olacağı Sinan'ın cevabından türetilemez. Cevap gelene kadar uygulama kısmen teslim
  edilmiş satırda plan değişikliğini **reddeder** (fail-closed, geri alınabilir); "teslim" ölçüsü Lojistik teslim
  okumasıdır (`ISalesOrderDeliveryReadPortV1`).
- **Birim dönüşümü (`NEEDS-SINAN`):** tahsis okuması kilogram döndürür; satır birimi kg değilse tahsis kg'sinin satır
  birimine çevrilmesi kuralı bu belgede seçilmez. Çeviri kuralı yoksa türetilmiş stok kısmı "okunamadı" gösterilir ve
  plan kaydı reddedilir; tahminle dönüşüm yapılmaz.
- **Ürün tipi eşlemesi (`NEEDS-SINAN` gerekirse):** "ürün tipine göre üretim veya satın alma" için Ürün'de bunu
  söyleyen bir alan yoksa eşleme uydurulmaz; o satırda stok dışı öneri "kullanılamıyor" kalır.
- **Kaldırılması/ürün değişikliği onay bekleyen satıra plan değişikliği:** SAT-D219'daki açık `NEEDS-SINAN` aynen
  açıktır; bu belge onu kapatmaz.
- Bu belge yeni bir stok politikası açmaz: tahsis kuralları, serbest stok tanımı, gelecek mal ayırma (ALS-D30) ve stok
  değerleme aynen kalır; Satış hiçbir stok kaydı oluşturmaz.
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; grant/rol ataması veya production değişikliği yapmaz;
  `sales.orders.source-plan.write` dışında yeni izin adı açmaz (o ad da SAT-D179'dadır).
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
