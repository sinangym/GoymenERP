# Sinan Kararları — 2026-10-29 (Satış: iskonto limit grubu bağının şirket kapsamı)

Bu belge önceki karar dosyalarını silmez; SAT-D116'yı ekler. EK11'deki "üye olmadığı şirket kuralı uygulama PR'ında
belirlenir" notunu, PR #852'nin K3 review'unun (review B, P3-2) açtığı soruyla cevaplar. SAT-D61…SAT-D70 (EK10/EK11)
aynen geçerlidir; varsayılan bağ üç şirkette ortaktır.

**Kaynak:** Sinan'ın 29 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevabı. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi; önerilen seçenek seçildi.

## Soru ve cevap

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| İskonto limit grubuna kişi bağlanırken (varsayılan: 3 şirkette ortak), kişi yalnız bazı şirketlerde çalışıyorsa (örn. yalnız GSP'de) ne olsun? | Çalıştığı şirketlere bağlansın | "Bağ yalnız kişinin üye olduğu şirketlere yazılır; diğer şirketler atlanır ve ekranda bu gösterilir." (seçilmeyenler: "Bağlanamasın" — "Kişi 3 şirketin hepsinde üye değilse bağ reddedilir; o kişinin her iskontosu onaya gider (şu anki kod)."; "Şirket seçilsin" — "Bağlayan kişi hangi şirketlere yazılacağını ekranda seçer (kişinin üye olduğu şirketler arasından).") |

## Karar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D116 | Bağın şirket kapsamı | Kişi iskonto limit grubuna bağlanırken bağ, bağlayanın yetkili olduğu şirketler içinde **yalnız kişinin üye olduğu şirketlere** yazılır; üye olmadığı şirketler atlanır ve atlanan şirketler ekranda gösterilir. Kişinin üye olduğu bir şirkette bağlayanın yetkisi yoksa istek reddedilir (bugünkü yetki kuralı) | Ayarlar iskonto limit bağı (PR #852) |

## Sınırlar

- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
- Bağların uzun vadede hangi ekrandan yönetileceği ve şirkete özel bağların ne zaman açılacağı (PR #852 NEEDS-SINAN) açık kalır.
