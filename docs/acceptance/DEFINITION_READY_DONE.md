# Definition of Ready

Bir iş paketi şu bilgiler olmadan kodlanmaz:

- Amaç ve kapsam dışı
- İş sahibi ve karar ID’leri
- Veri sahibi ve CompanyId davranışı
- Durum geçişleri
- Birim/formül/yuvarlama/tolerans
- Yetki/onay/görev ayrılığı
- Audit, ters işlem ve idempotency
- UI durumları ve erişilebilirlik
- API/event/tool sözleşmesi
- Kabul örnekleri

# Definition of Done

- Bütün kabul ve kritik otomatik testler geçti
- Yetki/CompanyId/audit/concurrency/ters işlem test edildi
- Boş/yükleniyor/hata/çakışma tamamlandı
- Masaüstü/tablet/mobil ve klavye kontrolü yapıldı
- Log/metrik/alarm/runbook var
- Migration/rollback etkisi belgelendi
- P0/P1 açık hata veya güvenlik riski yok
- Yayın artefaktı ve migration manifesti doğrulandı
