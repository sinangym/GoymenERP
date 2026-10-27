# Sinan Kararları — 2026-10-27 (Satış: iskonto limit değerleri ve limit grupları)

Bu belge önceki karar dosyalarını silmez; SAT-D61…SAT-D64'ü ekler. SAT-D34 (çok rollü kullanıcıda en yüksek limit),
SAT-D36 (limitler Ayarlar'da rol/grup bazında) ve SAT-D37'nin (limiti aşan iskontoyu Müdür onaylar; giren kendi
iskontosunu onaylayamaz) uygulamasıdır. SAT-D36'daki "rol/grup bazında" ifadesini SAT-D63 ile **ayrı limit grupları**
olarak netleştirir.

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevapları. Soru, API-12'nin ([#834](https://github.com/sinangym/GoymenERP/pull/834)) açık bıraktığı "Identity rol → limit
grubu eşlemesi ve limit değerleri" maddesiydi. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi; bir soruda Sinan
kendi metnini yazdı, o metin aynen verilir.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| Satış Destek (siparişi giren satıcı) onay almadan en fazla yüzde kaç iskonto yapabilsin? Liste fiyatına göre ölçülür. | %0 | "Her iskonto, ne kadar küçük olursa olsun, Müdür onayına gider." (seçilmeyenler: "%5", "%10", "Şimdilik girme") |
| Müdür kendi girdiği siparişte en fazla yüzde kaç iskonto yapabilsin? Limiti aşarsa kendi iskontosunu onaylayamaz (SAT-D37), başka bir Müdür onaylar. | Kendi metni: "Şimdilik %100 sınırsız olsun, yani müdürün iskontosu hiç onaya gitmesin ama daha değiştirmek istediğimde girilen mak min iskontoya ek %? Yapabilsin üstü 2.müdür onayına gitsin yapalım,( yani daha sonra örneğin maks iskonto satıcı için %35 ise müdür için %3 oran verelim ve %38 ile satabilsin yetkisi verelim müdüre ama %3 oranınıda değiştirebilelim )" | — (seçenekler: "%100 (sınırsız)" — "Müdür'ün iskontosu hiç onaya gitmez."; "%10"; "%20"; "Satıcıyla aynı") |
| Limit grupları nasıl kurulsun? | Ayrı limit grupları | "Rollerden bağımsız gruplar (örn. Kıdemli Satıcı) tanımlanır, kişiler gruplara bağlanır. Daha esnek ama daha çok iş." (seçilmeyen: "Her rol bir grup") |
| Bu limitler üç şirkette (GDC, GSP, GIN) aynı mı olsun? | Üçünde aynı | "Aynı değerler üç şirkete de girilir." (seçilmeyen: "Şirkete göre farklı") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D61 | Satış Destek iskonto limiti | Satış Destek'in (siparişi giren satıcı) limit grubunun onaysız iskonto limiti **%0**'dır: liste fiyatının altındaki her iskonto SAT-D37 onaycısına (Müdür) gider. Davranış bugünkü "limit tanımlı değil" durumuyla aynıdır; fark, değerin Ayarlar'da açıkça kayıtlı olmasıdır | Ayarlar iskonto limit kaydı, API-12 (#834) |
| SAT-D62 | Müdür iskonto limiti | **Bugün:** Müdür'ün limit grubunun limiti **%100**'dür; Müdür'ün girdiği iskonto onaya gitmez. **Sonra (Sinan'ın istediği yapı):** Müdür'ün limiti, Ayarlar'da **başka bir limit grubunun limiti + ek yüzde puan** olarak tanımlanabilmelidir (örnek: satıcı grubu %35, Müdür'e ek 3 puan → Müdür %38'e kadar onaysız). Ek oran Ayarlar'dan değiştirilebilir. Bu limiti aşan Müdür iskontosu **başka bir Müdür'ün** onayına gider (SAT-D37: giren kendi iskontosunu onaylayamaz; P6 aynen). Yani bir limit kaydı ya **sabit yüzde** ya da **"bağlı grup limiti + ek puan"** biçimindedir; bağlı grubun limiti değişince Müdür'ün limiti kendiliğinden değişir | Ayarlar iskonto limit kaydı (yeni "bağlı limit" biçimi), API-12 |
| SAT-D63 | Limit grupları | Limit grupları **rollerden ayrı** tanımlanır (örn. Satış Destek, Kıdemli Satıcı, Müdür); **kişiler gruplara bağlanır**. Bir kişi birden fazla gruptaysa SAT-D34 gereği **en yüksek** limit geçerlidir; hiçbir grubu olmayan veya grubunun limiti olmayan kişide her iskonto onaya gider (SAT-D34/D36 aynen). Bu, SAT-D36'daki "rol/grup bazında" ifadesini netleştirir | Ayarlar (grup tanımı ve limit), Identity (kişi → grup bağı), API-12 `ISalesDiscountLimitGroupSourceV1` |
| SAT-D64 | Şirketler | Limit değerleri **üç şirkette (GDC, GSP, GIN) aynıdır**; kayıtlar şirket kapsamlı kalır ve aynı değerler üç şirkete de girilir | Ayarlar iskonto limit kaydı |

## Uygulama notları

- Kayıtların **girilmesi** (değerlerin Ayarlar'a yazılması, kişilerin gruplara bağlanması) veri işlemidir ve yetkili
  kullanıcı tarafından Ayarlar/Identity ekranlarından yapılır; kod veya migration ile tohumlanmaz.
- SAT-D62'deki "%100" bugünkü değerdir; "bağlı grup + ek puan" biçimi uygulandığında Müdür grubunun kaydı o biçime
  çevrilir. Ek puan ve bağlı grup Ayarlar'da değiştirilebilir; döngüsel bağlantı (A→B→A) reddedilir, bağlı grubun
  limiti yoksa veya okunamazsa Müdür'ün limiti de "yok" sayılır ve iskonto onaya gider (fail-closed). Bu iki ayrıntı
  geri alınabilir teknik ayrıntıdır.
- Hesaplanan limit %100'ü aşamaz.
- `sales.orders.discount-exception.approve` izninin Müdür rolüne verilmesi SAT-D37 ile kararlaştırılmıştır; iznin
  gerçek ortamda açılması production aktivasyonu insan kapısıdır, bu belge izni açmaz.

## Açık kalanlar

- Kişi → limit grubu bağının hangi ekrandan (Ayarlar mı, kullanıcı/rol yönetimi mi) yönetileceği sorulmadı; uygulama
  PR'ı Identity sahipliğinde bir kayıt olarak önerir.
- Müdür'ün ek puanının ilk değeri sorulmadı (bugün %100 sabit).

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol veya grant açılmaz.
