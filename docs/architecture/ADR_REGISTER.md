# ADR Kayıt Listesi

| ADR | Konu | Başlangıç kararı | Durum |
|---|---|---|---|
| ADR-001 | Mimari stil | Modüler monolit | Kabul |
| ADR-002 | Çok şirket veri modeli | Tek DB + zorunlu CompanyId + export/restore | Kabul |
| ADR-003 | Veritabanı | SQL Server birincil PoC | Geçici |
| ADR-004 | Kimlik | OIDC uyumlu; bootstrap local admin | Geçici |
| ADR-005 | Dosya | Şirket kapsamlı belge/version/link metadata + güvenli fiziksel blob deposu; ayrıntılar OPEN-026…029 ile bloklu | Öneri |
| ADR-006 | Olay/entegrasyon | Outbox/inbox + idempotency | Kabul |
| ADR-007 | Arama | İlişkisel full-text; gerekirse OpenSearch | Kabul |
| ADR-008 | AI | Backend tool gateway, local-first | Kabul |
| ADR-009 | Audit | Append-only/değiştirilemez uygulama erişimi | Kabul |
| ADR-010 | Yayın | Ayrı ortam, CI/CD, rollback manifesti | Kabul |
| ADR-011 | Idempotency/Concurrency | Kalıcı sonuç replay + SQL rowversion | Review |
