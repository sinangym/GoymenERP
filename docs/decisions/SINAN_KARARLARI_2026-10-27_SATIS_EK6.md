# Sinan Kararları — 2026-10-27 (Satış: listesiz satır onayında aynı kişi)

Bu belge `SINAN_KARARLARI_2026-10-27_SATIS_EK5.md` ve önceki Satış karar dosyalarını silmez; onlara
SAT-D45'i ekler ve EK5'teki "SAT-D43 onayında aynı kişi kuralı" açık maddesini kapatır.

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum
`session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru cevabı. Soru EK5'in "Açık kalanlar" bölümündeki maddeydi
(PR [#837](https://github.com/sinangym/GoymenERP/pull/837)). Sinan'a seçeneğin adı **ve açıklaması** birlikte
gösterildi.

| Soru | Cevap (seçilen seçenek) | Seçilen seçeneğin açıklaması |
|---|---|---|
| Liste fiyatı olmayan satırın fiyatını elle yazan kişi, o satırın yönetici onayını kendisi verebilir mi (onay yetkisi varsa)? | Evet, yetkisi varsa onaylar | "Talep ve süresi geçmiş teklif onayında olduğu gibi: onay yetkisi olan kişi kendi yazdığı fiyatı onaylayabilir." |

Seçilmeyen seçenek: "Hayır, farklı kişi onaylar" ("Fiyatı yazan kendi satırını onaylayamaz; onayı başka bir
yetkili verir (genel 'hazırlayan ≠ onaylayan' kuralı, şu anki durum).").

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D45 | Liste fiyatı olmayan satırın onayında aynı kişi (SAT-D43) | Liste fiyatı olmayan satırın fiyatını elle yazan kişi, onay yetkisine sahipse o satırın SAT-D43 onayını **kendisi verebilir**. Bu, `SINAN_KARARLARI_2026-10-21.md` P6'daki "hazırlayan ≠ onaylayan" ilkesine **yalnız SAT-D43 (listesiz satır) onayı için** açık bir istisnadır; SAT-D22 ve SAT-D44'teki istisnalar gibi. SAT-D37'deki iskonto istisnasında P6 aynen geçerlidir: iskontoyu giren kişi kendi iskontosunu onaylayamaz | API-12 (#834) |

## Uygulama notu

- Liste fiyatı olmayan satırda SAT-D35/SAT-D42'ye göre ölçülebilir bir iskonto yoktur; böyle bir satırın
  iskonto kontrolü SAT-D43 onayıdır (iskonto alanlarından bağımsız). SAT-D45 bu onayın tamamını kapsar:
  fiyatı veya iskonto alanını yazan kişi, onay yetkisi varsa satırı kendisi onaylayabilir. SAT-D37'deki
  "iskontoyu giren kendi iskontosunu onaylayamaz" kuralı liste fiyatı **olan** satırlarda aynen geçerlidir.
- "Onay yetkisi" SAT-D37'deki onaycı iznidir (`sales.orders.discount-exception.approve`, PR #834); kime
  verileceği grant kararıdır ve insan kapısında kalır.

## Açık kalanlar

- Yok.

## Sınırlar

- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings,
  grant/rol/Critical, production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol veya
  grant açılmaz.
