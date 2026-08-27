---
record_id: "DISPATCH-INVOICE-PDW-01"
source_namespace: "DISPATCH-INVOICE-SPEC-2026-08-24"
source_id: "PDW-01"
legacy_aliases: ["PW-01"]
canonical_ids: ["P-07", "L-10"]
mapping_status: CANDIDATE_SPLIT
source_status: DESIGNED_NOT_LOCKED
visual_status: PROTOTYPE_REFERENCE
source_date: "2026-08-24"
source_version: "v1.0"
source_sha256: "2F72CD7BC794C7465E94050EAA6708780448983EF5DD807F4EA75C863EC8BC8D"
prototype_sha256: "98B26CC31AC1AB7406B69CBCCBEBB335A4931FB8A781F0A32ED90D50C9876607"
---

# PDW-01 — Yeni Mal / Hizmet Kabulü

## Amaç ve yerleşim

Sipariş satırı, irsaliye, net kabul, red/eksik, kantar, lot/etiket ve kalite girişidir.

Satış ve alış kayıtları aynı tabloda karıştırılmaz. Ana merkez sekmeli; yoğun işlem tam ekran workspace, kısa kayıt incelemesi drawer, AI ise sağ paneldir.

## İş, yetki ve AI sınırı

Planlanan miktar stok hareketi değildir; fiilî/net kabul ve faturalama payı ayrı tutulur. Kesinleşmiş belge silinmez, ters kayıt/düzeltme ilişkisi kurulur. AI belge okur, eşleştirir, uyarı ve taslak üretir; nihai irsaliye/fatura/kabul onayını vermez. Bütün işlemler permission/scope, görev ayrılığı, idempotency, concurrency, transaction ve audit sözleşmelerine tabidir.

## Kanıt ve uzlaştırma

- Şartname: ../../sources/dispatch-invoice/dispatch-invoice-design-spec-v1.0.md
- HTML prototip: ../../prototypes/dispatch-invoice/dispatch-invoice-workspace.html
- Statik önizleme: ../../assets/original/dispatch-invoice-archive/irsaliye-fatura-calisma-alani-onizleme.png
- Legacy alias: PW-01
- Aday kanonik hedef: P-07, L-10
- Eşleme: CANDIDATE_SPLIT
- Statü notu: Kaynak kendi metnine göre kullanıcı kabulü sonrası kilitlenebilir; şu anda LOCKED değildir.
