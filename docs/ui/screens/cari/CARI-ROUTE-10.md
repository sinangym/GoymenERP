---
record_id: "CARI-ROUTE-10"
source_namespace: "CARI-HTML-2026-08-24"
source_id: "modal:critical-change"
canonical_ids: ["C-07", "C-08", "C-11"]
related_reference_ids: ["SY-04"]
mapping_status: SPLIT
decision_refs: ["UI-MAP-003"]
source_status: DESIGNED_NOT_LOCKED
visual_status: PROTOTYPE_REFERENCE
source_date: "2026-08-24"
source_version: "v3"
source_sha256: "757B6EB8983A58616214E05C206FB54CF91B8DD395B0931499CE11A46B9D2423"
---

# Kritik Değişiklik ve Etki / Onay

## Amaç ve yerleşim

VKN, unvan, MERSİS, IBAN, limit ve benzeri kritik değişikliklerin fark ve onay modallarıdır.

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
- Kanonik hedef: C-07, C-08, C-11
- İlişkili dış-domain referansı: SY-04
- Eşleme: SPLIT
- Kimlik notu: Bu route kaydı resmî yeni ekran ID değildir.
- Responsive notu: body min-width 900px olduğundan telefon akışı tamamlanmış sayılmaz.
