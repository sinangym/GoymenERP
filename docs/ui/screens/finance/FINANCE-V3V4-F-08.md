---
record_id: "FINANCE-V3V4-F-08"
source_namespace: "FINANCE-V3-BASE-V4-DELTA"
source_id: "F-08"
canonical_ids: ["F-11", "F-12"]
mapping_status: CANDIDATE_SPLIT
source_status: ACTIVE_BASE_PLUS_DELTA
visual_status: PROTOTYPE_REFERENCE
source_version: "v3 + v4"
source_date: "2026-08-24"
v3_prototype_sha256: "08E7E7304E39F62EA297537109204EBD3152BCA36A0CA9F4EF4BA0E2077AA8BE"
v4_prototype_sha256: "CE3B49A518D0CF9244EA338ABA89B35190DAA277236C9164CC58A04C1109E2EF"
---

# F-08 — Nakit Planı

## Amaç ve kullanıcı deneyimi

Baz/iyimser/stres nakit görünümü ve fonlama senaryolarını gösterir.

v3; ekran, sekme, satır detayı, rol, yetki, kabul ve 73 workspace temelidir. v4 bu tabanı kaldırmaz; varsayılan Benim İşlerim + Sade görünümü, beş menü grubu, görünür AI dikkat noktası ve ortak AI işlem kontrolünü ekler.

## AI, yetki ve finansal doğruluk

AI rolü: Stres testi ve fonlama seçenekleri üretir. AI finansal kaynak, dağıtım, bakiye, kur, vade veya formül gerçeği oluşturmaz. Kesin kayıt, dağıtım, ters kayıt ve onay backend servisleri, permission/scope, görev ayrılığı, idempotency, concurrency ve audit ile uygulanır.

## Kanıt ve uzlaştırma

- v3 ekran kaynağı: ../../sources/finance/v3/02_EKRAN_KATALOGU.md
- v3 workspace kaynağı: ../../sources/finance/v3/03_ISLEM_WORKSPACE_KATALOGU.md
- v3 UI/UX: ../../sources/finance/v3/FINANS_UI_UX_KARARLARI.md
- v4 navigasyon/AI deltası: ../../sources/finance/v4/02_EKRAN_VE_NAVIGASYON_MIMARISI.md
- Prototipler: ../../prototypes/finance/v3/Goymen_ERP_Finans_Tam_Calisma_Alani_v3.html ve ../../prototypes/finance/v4/Goymen_ERP_Finans_AI_Kullanici_Odakli_v4.html
- Aday kanonik hedef: F-11, F-12
- Eşleme: CANDIDATE_SPLIT
- Kimlik notu: v3/v4 kaynak F-* anlamı repo kanonik F-* ile her satırda aynı değildir.
