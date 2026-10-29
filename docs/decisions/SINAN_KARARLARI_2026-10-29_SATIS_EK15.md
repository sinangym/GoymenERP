# Sinan Kararları — 2026-10-29 (Satış: iskonto limit grubu bağının şirket kapsamı)

Bu belge önceki karar dosyalarını silmez; SAT-D116'yı ekler. EK11'deki "üye olmadığı şirket kuralı uygulama PR'ında
belirlenir" notunu, PR #852'nin K3 review'unun (review B, P3-2) açtığı soruyla cevaplar. SAT-D61…SAT-D70 (EK10/EK11)
geçerlidir; SAT-D66'nın "varsayılan bağ üç şirkette ortaktır" kuralı, kişinin üye olmadığı şirketler için SAT-D116 ile
netleşir.

**Kaynak:** Sinan'ın 29 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevabı. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi; önerilen seçenek seçildi.

## Soru ve cevap

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| İskonto limit grubuna kişi bağlanırken (varsayılan: 3 şirkette ortak), kişi yalnız bazı şirketlerde çalışıyorsa (örn. yalnız GSP'de) ne olsun? | Çalıştığı şirketlere bağlansın | "Bağ yalnız kişinin üye olduğu şirketlere yazılır; diğer şirketler atlanır ve ekranda bu gösterilir." (seçilmeyenler: "Bağlanamasın" — "Kişi 3 şirketin hepsinde üye değilse bağ reddedilir; o kişinin her iskontosu onaya gider (şu anki kod)."; "Şirket seçilsin" — "Bağlayan kişi hangi şirketlere yazılacağını ekranda seçer (kişinin üye olduğu şirketler arasından).") |

## Karar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D116 | Bağın şirket kapsamı | Kişi iskonto limit grubuna bağlanırken bağ **yalnız kişinin üye olduğu şirketlere** yazılır; üye olmadığı şirketler atlanır ve atlanan şirketler ekranda gösterilir (Sinan'ın cevabı). **Uygulama ayrıntısı (geri alınabilir, bugünkü fail-closed kural, EK11 "her şirkette yetki, ya hepsi ya hiçbiri"):** komut yalnız bağlayanın erişebildiği şirketleri kapsar; bunlardan kişinin üye olduğu **herhangi birinde** bağlayanın `settings.sales-discount-limits.manage` izni yoksa istek tamamen reddedilir ve hiçbir şirkete yazılmaz. Bağlayanın erişemediği bir şirketteki bağ bu komutla yazılmaz; o şirkette kişinin limiti olmaz ve iskontosu onaya gider. Kişi bağlayanın erişebildiği şirketlerin hiçbirinde üye değilse yazılacak şirket yoktur ve istek reddedilir | Ayarlar iskonto limit bağı (PR #852) |

## Sınırlar

- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
- Kişi sonradan bir şirkete üye olursa veya üyelikten çıkarsa mevcut bağ kendiliğinden genişlemez ve silinmez; yeniden
  bağlama gerekir (fail-closed ara durum; sorulmadı).
- Bağların uzun vadede hangi ekrandan yönetileceği ve şirkete özel bağların ne zaman açılacağı (PR #852 NEEDS-SINAN) açık kalır.
