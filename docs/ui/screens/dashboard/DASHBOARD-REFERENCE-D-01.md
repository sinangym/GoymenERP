---
record_id: "DASHBOARD-REFERENCE-D-01"
source_namespace: "USER-DASHBOARD-REFERENCE-2026-08-25"
source_id: "dashboard-main"
canonical_ids: ["D-01"]
related_reference_ids: ["GLOBAL-SHELL"]
mapping_status: CANDIDATE_EXACT
source_status: DESIGNED_NOT_LOCKED
visual_status: PROTOTYPE_REFERENCE
capture_quality: FULL_SCREEN
source_date: "2026-08-25"
source_sha256: "B74CDB2DC8A93D70C4DB5ABA83697B8EF45399C25CF8C8EFC3889E57AE98A392"
asset: "../../assets/original/dashboard-reference/D-01__Ana-Sayfa-ve-Global-Kabuk-Referansi.png"
---

# Ana Sayfa ve Global Kabuk Referansı

## Amaç ve yerleşim

Rol bazlı ERP ana sayfası için koyu lacivert sabit sol menü, beyaz global toolbar, şirket seçimi, global arama, hızlı yeni işlem, KPI kartları, dikkat gerektiren işler, günlük görevler, risk tablosu ve sağ bağlamsal AI panelini tek kompozisyonda gösterir.

## İş ve güvenlik sınırı

- Görsel kullanıcı tarafından tasarım yönü olarak verilmiştir; kilit veya production kabulü değildir.
- Şirket seçici backend CompanyContext, permission veya veri izolasyonu yerine geçmez.
- KPI ve toplamlar kullanıcının RBAC/ABAC kapsamının dışındaki veriyi sızdıramaz.
- AI paneli yalnız yetkili bağlamı okur; kritik kayıt önizleme, kullanıcı onayı ve audit olmadan uygulanmaz.
- Mock isim, tarih, tutar ve sayılar iş kuralı ya da seed verisi değildir.

## Kanıt ve uzlaştırma

- Özgün varlık: `../../assets/original/dashboard-reference/D-01__Ana-Sayfa-ve-Global-Kabuk-Referansi.png`
- Kanonik aday: `D-01 Rol Bazlı Ana Sayfa`
- İlişkili referans: global uygulama kabuğu
- Eşleme: `CANDIDATE_EXACT`
- Statü: `DESIGNED_NOT_LOCKED / PROTOTYPE_REFERENCE`
