# Sinan Kararları — 2026-11-07 (kaldırılması onay bekleyen satıra yeni bağ)

Bu belge önceki karar dosyalarını silmez; SAT-D220'yi ekler. EK38 SAT-D219'da `NEEDS-SINAN` olarak açık bırakılan soruyu
cevaplar (Issue #66). EK36 SAT-D211 ve EK38 SAT-D219 geçerlidir.

**Kaynak:** Sinan'ın 7 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevap: bir tur, bir soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevap

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Bir satırı kaldıran revizyon onay beklerken, o satıra yeni bir satın alma siparişi ya da üretim emri bağlanabilsin mi? | Hayır, bağlanamasın (Önerilen) | "Kaldırılması onay bekleyen satıra Alış/Üretim yeni bağ kuramaz; revizyon reddedilirse satır geri döner ve bağ kurulabilir. Sevkteki kuralın (SAT-D211) aynısı." (seçilmeyen: "Bağlanabilsin" — "Bağ kurulabilir; o durumda revizyonun onayı reddedilir (onay anında açık bağ görülür).") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D220 | Kaldırılması onay bekleyen satıra yeni bağ | Bir satırı kaldıran revizyon onay beklerken o satıra **yeni satın alma siparişi veya üretim emri bağlanamaz**; revizyon reddedilirse satır geri döner ve bağ kurulabilir (soru 1; SAT-D211 ile aynı kural). Uygulama ayrıntısı (geri alınabilir): Alış ve Üretim bağı Satış'ın sürümlü satır referans portu (`ISalesOrderLineReferencePortV1`) üzerinden kurar; kaldırılması bekleyen satır bu portta sevk edilebilir satır olarak sunulmaz, böylece yeni bağ Satış tablosuna dokunulmadan reddedilir. Bunun bağlayıcı olması için Alış (API-02/MOD-03) ve Üretim (U2-b/U5-a) bağ kurmadan önce bu portu okumak, `Approved` olmayan satırı reddetmek ve bunu kendi testleriyle göstermek zorundadır; portun bu davranışı PR #926 merge olunca yürürlüğe girer. Ürünü değiştirilen satır bu kararın kapsamı dışındadır | PR #926, Alış API-02/MOD-03, Üretim U2-b/U5-a |

## Sınırlar

- Bu karar merge kapılarını ve insan kapılarını değiştirmez.
