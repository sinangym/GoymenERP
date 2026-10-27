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

- Aynı satır hem listesizlik (SAT-D43) hem de girilmiş bir iskonto nedeniyle onaya gidiyorsa iki kural
  birlikte uygulanır: SAT-D45 listesizlik gerekçesi için aynı kişiye izin verir, ama iskonto gerekçesi için
  SAT-D37'nin "iskontoyu giren onaylayamaz" kuralı kalkmaz. Bu, iki kararın birleşimidir; yeni bir politika
  seçmez. Sinan aksine karar verirse bu not değişir.

## Açık kalanlar

- Yok.

## Sınırlar

- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings,
  grant/rol/Critical, production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol veya
  grant açılmaz.
