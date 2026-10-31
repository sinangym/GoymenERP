# Sinan Kararları — 2026-10-29 (Satış: pasif iskonto limit grubu)

Bu belge önceki karar dosyalarını silmez; SAT-D117 ve SAT-D118'i ekler. PR #852'nin K3 review'unda (review A, P3-2) açılan,
pasife alınan iskonto limit grubunun anlamı sorusunu cevaplar. SAT-D61…SAT-D70 (EK10/EK11), SAT-D116 (EK15) ve müşteri
grupları için SAT-D77 geçerlidir.

**Kaynak:** Sinan'ın 29 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevapları. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi; iki soruda da önerilen seçenek seçildi.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| İskonto limit grubu (örn. 'Satış Destek') pasife alınırsa, o gruba henüz bağlı olan kişilerin limiti ne olsun? | Mevcut kişiler için sayılsın | "Pasif grup yalnız YENİ bağlamaya kapanır; zaten bağlı kişiler ve ona bağlı limitler (örn. Müdür = Satıcı + 3) çalışmaya devam eder. Müşteri gruplarındaki kararla (SAT-D77) aynı." (seçilmeyen: "Hemen limitsiz olsun" — "Pasife alma anında bağlı kişilerin limiti sıfırlanır, tüm iskontoları onaya gider; ona bağlı limitler de düşer (şu anki kod).") |
| Pasife alınan iskonto limit grubu sonradan yeniden aktif yapılabilsin mi? | Evet, gerekçeyle | "Limit değişikliği gibi tek kişi + gerekçe + kayıt (audit) ile yeniden aktif yapılır." (seçilmeyen: "Hayır" — "Pasif grup kalıcıdır; yeni bir grup açılır (aynı kod tekrar kullanılamaz).") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D117 | Pasif iskonto limit grubu | Pasife alınan iskonto limit grubu yalnız **yeni bağlamaya** kapanır. Gruba zaten bağlı kişilerin limiti ve bu gruba bağlı limitler (örn. Müdür = Satıcı + 3) çalışmaya devam eder. Müşteri grupları için SAT-D77 ile aynı ilke | Ayarlar iskonto limit hesaplayıcısı ve bağ komutu (PR #852) |
| SAT-D118 | Yeniden aktif yapma | Pasif iskonto limit grubu, limit değişikliğiyle aynı kuralla (SAT-D68: tek kişi + zorunlu gerekçe + audit; SAT-D69 izni) yeniden aktif yapılabilir | Ayarlar iskonto limit grubu komutları (PR #852) |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
