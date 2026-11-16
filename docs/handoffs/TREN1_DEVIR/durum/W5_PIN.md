# tren-1 W5 regen — durum

- Dal: `claude/tren-1-entegre-w5` (taban `origin/claude/tren-1` 7fdf2aa10)
- **Final head: `56e43075f795d1cb6c228aefce3110a1dff3c100`**. Pushlandı; `claude/tren-1` bu head'e fast-forward edildi (`ls-remote` iki ref'i de aynı sha'da gösteriyor). PR açılmadı, force kullanılmadı.

| Adım | Komut | Exit | Commit |
|---|---|---|---|
| 1 ShellCapabilityRegistryAuthorities | permission-capability-registration üreticisi + `dotnet test` (dar filtre); FinanceShellRegistrationTests 36→39 | 0 | cb6f8588f |
| 2 OpenAPI | openapi-regenerate üreticisi (608 operasyon) + validator | 0 | 424924823 |
| 3 Recovery/release pin'leri | `python3 -I -S -B tools/generate_recovery_surface_inventory.py` (migration 223→251, store 46→53) | 0 | b38390437 |
| 3b Recovery/release test beklentileri | `unittest discover -s tests/recovery_surface_inventory` / `tests/release_manifest` (tam: 1 ve 1 exit; düzeltme sonrası dar `-k`: 0) | 0 | 59302f5db, 56e43075f |
| 4 Mimari guard fingerprint + ReviewedMigrationMutationSources | `dotnet test tests/GoymenERP.ArchitectureTests --filter FullyQualifiedName~PlatformTenantQueryFilterBypassGuard` + `guard_fingerprint.py` (43 fingerprint, 27 migration kaydı) | 1 (5 pin dışı hata kaldı) | 05ead3c6d |
| 5 Relational inventory + test-grup pin'leri | `guard_fingerprint.py` + RCSI guard filtresi (4/4); test projesi değişmedi, grup pin'leri aynı | 0 | 8eb0b8c1f |
| 6 Hafif doğrulayıcılar | validate_delivery_backlog, delivery_backlog_validator (64), validate_dotnet_bootstrap_integrity, dotnet_bootstrap_integrity (56), backend_test_group_verifier (28), rcsi_relational_acceptance_verifier (48) | 0 | (8eb0b8c1f kaynağı) |
| 7 Recovery üretici tekrar (head) | `python3 -I -S -B tools/generate_recovery_surface_inventory.py > /dev/null` | 0 | 56e43075f |
| 8 diff check + push | `git diff --check origin/claude/tren-1 HEAD`; `push origin claude/tren-1-entegre-w5`; `push origin claude/tren-1-entegre-w5:claude/tren-1` | 0 / 0 / 0 | 56e43075f |

## Kalanlar (üreticisi yok / pin değil)

- OpenApiContractGenerationTests beklentileri güncellenmedi: 82 yeni operasyon eklenecek, eski UpdateCounterpartyLegalNameV1 çıkarılacak.
- Guard'da pin dışı 5 hata: (1) Platform_non_counterparty_bypass_surfaces — yeni gözden geçirilmemiş bypass yüzeyleri (IgnoreQueryFilters, SqlClient, DbConnection, SqlQueryRaw, FindAsync, yetkisiz DbContext kurulumu; Finance/Logistics/Purchasing/FounderRecovery/Settings) ve kod ihlalleri (ProductPackageStandardService.Approvals.cs:2 System.Linq.Expressions; ProductDimensionShape.cs:43/:238 migration dışı Sql) — K3 allow-list kararı; (2) Company_context_runtime_registration — 4 ICompanyContext kaydı; (3) OpenAPI başlangıcında Idempotency hosted service; (4) Audit_event_context_inventory — IdentityDbContext AuditEvent sahibi.
- THROW kodu çakışması: 51851 hem Finance AddCreditAccounts hem SecurityEvents AddFounderRecoverySecurityEvents.
- 7 yeni durable store sınıflandırması ve 27 migration kaydı K3 review bekliyor.
- FinanceShellCapabilityProviderTests eski (şerit borcu).
- Tam recovery (36) / release (16) suite'leri b38390437'de koşuldu; yalnız 3 sayı/anahtar literali kırmızıydı, düzeltildi ve dar `-k` ile yeşil; tam suite head'de yeniden koşulmadı.
