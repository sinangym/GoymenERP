---
record_id: "CARI-ROUTE-05"
source_namespace: "CARI-HTML-2026-08-24"
source_id: "cari/:id/orders/*"
canonical_ids: ["C-03", "S-02", "P-03"]
mapping_status: CANDIDATE_SPLIT
source_status: DESIGNED_NOT_LOCKED
visual_status: PROTOTYPE_REFERENCE
source_date: "2026-08-24"
source_version: "v3"
source_sha256: "757B6EB8983A58616214E05C206FB54CF91B8DD395B0931499CE11A46B9D2423"
---

# Cari Siparişleri

## Amaç ve yerleşim

Satış, alış ve fason siparişlerini seçili cari bağlamında gösterir.

Referans; sabit lacivert kabuk, hash tabanlı route, yoğun liste/360 içeriği, tam ekran cari workspace’i, sağ bağlamsal AI paneli ve kritik değişiklik önizlemesi kullanır.

## İş ve güvenlik sınırı

- Tek hukuki taraf ile CompanyId bazlı şirket hesabı ayrılır.
- Müşteri/tedarikçi hesapları ve para birimleri sessizce netleştirilmez.
- Beklenen alış fiziki stok değildir; mal sahibi ile fiziki lokasyon ayrıdır.
- AI önerir ve farkı açıklar; kritik sicil/risk/IBAN değişikliğini tek başına uygulayamaz.
- HTML mock backend, auth, permission, upload, audit veya kalıcılık içermez.
- Prototipte kontrolsüz innerHTML kullanımları vardır; üretim React koduna kopyalanmaz.

## Kanıt ve uzlaştırma

- Prototip: ../../prototypes/cari/cari-workspace-v3.html
- Aday kanonik hedef: C-03, S-02, P-03
- Eşleme: CANDIDATE_SPLIT
- Kimlik notu: Bu route kaydı resmî yeni ekran ID değildir.
- Responsive notu: body min-width 900px olduğundan telefon akışı tamamlanmış sayılmaz.
