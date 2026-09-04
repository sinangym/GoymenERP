# Export Report

## Kimlik

- Run ID: `GOYMENERP-KNOWLEDGE-V4-01`
- Provider: `codex`
- Mode: `SOURCE_EXPORT_GIT`
- Source ID: `codex-pr-181-k3-takibi-1ddb47fa532a`
- Conversation ID: `codex-pr-181-k3-takibi`
- Conversation title: `PR 181 K3 Takibi`
- Coverage: `visible_only`
- Status: `partial`
- Repository base: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`

## Sonuç

- Normalized items: 27
- Attachments: 0
- Conflict items: 0
- Redactions: 0
- Secret scan: passed
- PII review: passed
- Behavior/schema/API/workflow change: none

## Kaynak sınırı

Snapshot, bu çalışma sırasında gerçekten görünen proje konuşması mesajları ile canonical repository base kaydının LF-sonlandırılmış compact UTF-8 NDJSON temsilinden üretildi. Evrensel operasyon promptu ve bu paketin ürettiği alanlar snapshot dışında bırakıldı.

Erişilemeyen kapsam:

- otomatik özetlerde atlanmış veya kısaltılmış eski ham konuşma turları
- diğer Codex konuşmalarının ham içerikleri
- Claude ve ChatGPT sağlayıcılarındaki ayrı konuşmaların ham içerikleri
- kullanıcı tarafından doğrulanmış tam kaynak envanteri ve inventory attestation

## İşleme notları

- Kullanıcı kararları, gereksinimler, reddetmeler, mevcut durum ve kanıtlar atomik maddelere ayrıldı.
- Deterministik kimlikler canonical alan birleşiminin SHA-256 özetinden üretildi.
- Aynı anlamı tekrarlayan saatlik durum bildirimleri tek current-state veya evidence maddesinde birleştirildi.
- Zamansal mergeability ve divergence farklılıkları conflict olarak değil, son canlı değer ve stale-evidence kuralı ile temsil edildi.
- Final Master V4 entegrasyonu yapılmadı; registry collecting durumunda ve eksik kaynak/envanter kapıları açıktır.

## Beklenen sonuç

`SOURCE_EXPORTED`. Bu paket tek başına `FINAL_V4_READY` veya Product implementation GO anlamına gelmez.
