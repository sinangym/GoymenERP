# Sinan Kararları — 2026-11-01 (Satış: Muhasebe personasının temel okumaları)

Bu belge önceki karar dosyalarını silmez, SAT-D164'ü ekler. `PERSONA_GRANT_MATRISI_KARTI_v0` güncellemesinin (PR #871)
docs review'unun (B, P2-1) açtığı soruyu cevaplar: EK20 SAT-D142'nin Muhasebe'ye verdiği "satışta her şeyi görür" yetkisi ve
EK21 SAT-D155'in e-belge izni, temel okumalar olmadan çalışmıyordu. EK20 (SAT-D141…D151), EK21 (SAT-D152…D163) ve
`docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` geçerlidir.

**Bilinçli değişiklik (Sinan'ın açık cevabıyla):**
- **Muhasebe'nin temel okumaları:** SAT-D164, Muhasebe personasına dört temel okuma iznini verir. Kartta bu dört hücre
  "kararın sustuğu satır" olarak `?` duruyordu. Muhasebe/Finans okumaları seçenek olarak gösterildi ve **seçilmedi**.

**Kaynak:** Sinan'ın 1 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği seçmeli
soru cevabı: bir tur, bir soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi. Metinler aynen verilir.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Muhasebe'nin satış ekranlarını açabilmesi için temel okuma izinleri gerekiyor: şirket/şube bilgisi, kendi kullanıcı profili, cari kartları ve ürün seçimi. Muhasebe bunları okuyabilsin mi? | Evet, dördünü de okusun (Önerilen) | "Şirket bilgisi, kendi profili, cari kartları ve ürün seçimini yalnız okur; değiştiremez. Bunlar olmadan 'her şeyi görür' yetkisi çalışmaz." (seçilmeyen: "Evet, ek olarak muhasebe/finans okumaları" — "Bu dördüne ek olarak muhasebe ve finans ekranlarını da okuyabilir (fatura eşleştirme için faydalı).") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D164 | Muhasebe'nin temel okumaları | Muhasebe personası şu dört izni **yalnız okuma** olarak alır: `organization.context.read`, `identity.profile.read`, `counterparty.parties.read` ve `product.operational-selection.read` (soru 1). Bu izinlerin yazma karşılıkları verilmez. **Accounting ve Finance okumaları verilmez**: "ek olarak muhasebe/finans okumaları" seçeneği gösterildi ve seçilmedi | Persona grant matrisi |

## Sınırlar

- Grant, rol ve Critical ataması insan kapısıdır. Bu belge, SAT-D164'teki atamalar için Sinan'ın somut kararıdır. Uygulama
  PR'ı bu satırların dışında grant, rol veya Critical açamaz. Gerçek kullanıcıya rol atanması ve production etkinleştirmesi
  insan kapısında kalır (SAT-D110).
- Bu kararlar merge kapılarını ve diğer insan kapılarını değiştirmez: deployment, environment, secret, Settings
  etkinleştirme, production, `OPEN-*` kapatma, `.claude/settings.json`.
