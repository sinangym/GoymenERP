---
record_id: "MASTER3-P-12"
source_namespace: "MASTER-DOCX-2026-08-23"
source_id: "P-12"
canonical_ids: ["P-04"]
conditional_workspace_ids: ["U-02"]
mapping_status: EMBEDDED_COMPONENT
decision_refs: ["UI-MAP-001"]
source_status: DESIGNED_FINAL_QA
visual_status: DESIGNED_FINAL_QA
capture_quality: PARTIAL_CROP
source_date: "2026-08-23"
source_version: "v3.0"
source_page: 41
source_sha256: "10B6E89E8D05AE25124CF2A2C527ADBEC76DE13E2E4270925E8CCCC858A7272A"
asset: "../../assets/original/master-v3-docx/P-12__Ürün-Kırılımı-Oluştur.png"
---

# P-12 — Ürün Kırılımı Oluştur

## Amaç ve yerleşim

Genel alış satırını çoklu ürün kırılımına dönüştürür; kaynak, kırılan toplam ve kalanı gösterir.

Ortak kabuk koyu lacivert sol menü, beyaz araç çubuğu, kompakt rozet/KPI, yoğun tablo veya tam ekran workspace ve gerektiğinde sağ bağlamsal panel kullanır.

## Yetki, AI ve kayıt sınırı

Görsel permission, CompanyId, onay, formül veya transaction kaynağı değildir. Erişim backend RBAC+ABAC ile fail-closed uygulanır. AI yalnız yetki kapsamındaki veriyi okuyup açıklama/öneri/önizleme hazırlar; kritik kayıt kullanıcı onayı ve audit olmadan uygulanmaz.

## Kanıt ve uzlaştırma

- Özgün varlık: `../../assets/original/master-v3-docx/P-12__Ürün-Kırılımı-Oluştur.png`
- Master render sayfası: 41
- Kaynak statüsü: `DESIGNED_FINAL_QA`
- Yakalama kalitesi: `PARTIAL_CROP`
- Kanonik hedef: `P-04`
- Koşullu ilişkili workspace: `U-02`
- Eşleme statüsü: `EMBEDDED_COMPONENT`
- Karar: Kaynak `P-12`, kanonik `P-04` alış sipariş satırı akışındaki ürün kırılımı workspace'idir; yeni ürün kartı gereksiniminde kanonik `U-02` akışına bağlanır. Yeni kanonik ekran ID oluşturmaz.
