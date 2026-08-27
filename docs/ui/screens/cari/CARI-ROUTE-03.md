---
record_id: "CARI-ROUTE-03"
source_namespace: "CARI-HTML-2026-08-24"
source_id: "cari/:id/summary"
canonical_ids: ["C-03"]
mapping_status: CANDIDATE_EXACT
source_status: DESIGNED_NOT_LOCKED
visual_status: PROTOTYPE_REFERENCE
source_date: "2026-08-24"
source_version: "v3"
source_sha256: "757B6EB8983A58616214E05C206FB54CF91B8DD395B0931499CE11A46B9D2423"
---

# Cari 360 Özet

## Amaç ve yerleşim

Seçili carinin roller, şirket hesapları, risk ve zaman çizgisi özetidir.

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
- Aday kanonik hedef: C-03
- Eşleme: CANDIDATE_EXACT
- Kimlik notu: Bu route kaydı resmî yeni ekran ID değildir.
- Responsive notu: body min-width 900px olduğundan telefon akışı tamamlanmış sayılmaz.
