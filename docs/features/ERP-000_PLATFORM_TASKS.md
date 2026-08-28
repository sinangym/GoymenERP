# ERP-000 — Temel Sözleşmeler ve Platform Omurgası

Bu epic tamamlanmadan iş modülleri kalıcı veri yazmamalıdır.
| Görev | Amaç | Kapsam | Kabul |
|---|---|---|---|
| ERP-000A | Repository ve solution iskeleti | Kök AGENTS.md, docs, src/tests/infra; .NET solution ve React app; build/test workflow. | Repo temiz makinede build olur. |
| ERP-000B | Çok şirket çekirdeği | Organization modülü, CompanyContext, şirket seçici, zorunlu CompanyId, izolasyon testleri. | Yetkisiz şirket verisi query/command/search/audit içinde sızmaz. |
| ERP-000C | Kimlik ve yetki iskeleti | Bootstrap local admin; OIDC abstraction; RBAC+ABAC policy model. | Backend yetki testleri; UI buton gizleme tek kontrol değildir. |
| ERP-000D | Audit ve korelasyon | Append-only audit event, before/after, actor, company, IP/channel, correlation/causation. | Normal kullanıcı audit değiştiremez; kritik command audit üretir. |
| ERP-000E | Idempotency ve concurrency | Idempotency key store, rowversion, duplicate command protection. | Aynı komut iki kez ikinci kayıt oluşturmaz; conflict farkı döner. |
| ERP-000F | Outbox/inbox | Transactional outbox, consumer inbox/dedup, retry/backoff. | DB commit ile mesaj kaybı/kopyası test edilir. |
| ERP-000G | Belge kasası çekirdeği | Company-scoped document/version/link metadata, tenant-safe blob integrity/dedup, quarantine/malware ve signed access; ayrıntı `ERP-000G_DOCUMENT_VAULT_SPEC.md`. | ADR-005 kabul edilir, OPEN-026…029 kapanır ve task-local kabul planı gerçek SQL/S3-compatible kanıt üretir. |
| ERP-000H | Türkçe PDF ve adlandırma PoC | Unicode dosya adı, gömülü font stratejisi, sözleşme PDF örneği. | Türkçe karakter golden testi geçer. |
| ERP-000I | Global arama PoC | Company/permission scoped exact/full-text search, keyset pagination. | Sentetik hacimde p95 hedefi ve veri tekrar/atlama testi. |
| ERP-000J | AI gateway iskeleti | Tool registry, policy check, audit envelope, no direct DB. | AI yetki aşamaz; write tool onaysız uygulanmaz. |
| ERP-000K | Ortak React kabuğu | Lacivert nav, top bar, company switcher, list/workspace/drawer patterns. | Responsive ve klavye smoke testleri. |
| ERP-000L | Gözlemlenebilirlik | Structured logs, metrics, traces, health checks, correlation. | Hata ve yavaş sorgu tanılanabilir. |
| ERP-000M | SQL Server PoC ve migrations | EF Core setup, dev container/env, migrations, constraints/indexes. | Concurrency/search/audit/restore testleri. |
| ERP-000N | Backup/restore runbook | DB + object store backup, restore and checksum script. | Gerçek prova başarılı ve dokümante. |
| ERP-000O | CI/CD ve kalite kapıları | Build, unit, integration, lint, security scan, artifact manifest. | Başarısız kapı merge/yayın engeller. |

ERP-000G'nin nesne deposu + DB metadata yönü korunur; ancak fiziksel blob paylaşımı tenant
yetkisi sayılmaz. ADR-005 ve açık kararlar kabul edilmeden feature/test-substrate kodu,
migration, object-store/scanner adapter'ı, signed URL veya pilot belge geliştirmesi
başlatılmaz. DB–object-store tutarlılığı asynchronous messaging seçerse ERP-000F bağımlılığı
aynı review'da açıkça eklenir; bu dal seçim yapmaz.
