# Teknik Mimari Kararları

## Stack

- Frontend: React + TypeScript, PWA
- Backend: ASP.NET Core / .NET 10
- Veri: SQL Server birincil PoC; EF Core ile kontrollü taşınabilirlik
- Dosya: S3 uyumlu nesne deposu/MinIO veya kurumsal eşdeğer
- Realtime: SignalR benzeri bildirim
- Dağıtım: container, reverse proxy, dev/test/staging/prod

## Mimari

Modüler monolit. Ortak platform modülleri: Organization, Identity/Authorization, Approval, Audit, Documents, Search, AI Gateway, Outbox/Inbox.

Her komut:

- CompanyId ve kullanıcı bağlamı
- Beklenen kayıt sürümü
- Idempotency key
- Korelasyon/causation
- Yetki ve iş kuralı kontrolü
- Audit sonucu

Dış entegrasyonlar transactional outbox ile yayınlanır. Rapor/aramanın türev projeksiyonu SoT değildir.
