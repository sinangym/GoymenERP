# tren-1 W5 entegrasyon düzeltmeleri

Dal: `claude/tren-1-entegre-w5` (başlangıç `56e43075f` = `origin/claude/tren-1`).
PR açılmadı. İnsan kapılarına dokunulmadı: grant/rol/Critical, OPEN-* kapatma, deployment, environment, secret.
`.github/**` değişmedi. Boş commit, force-push, rebase ve amend yapılmadı.

## Adımlar

| # | Adım | Komut | Exit | Commit |
|---|---|---|---|---|
| 1 | THROW 51851 çakışması: Finance AddCreditAccounts Down guard'ı 51861'e alındı | git commit | 0 | `1d3a41731` |
| 2 | Kod ihlalleri: Linq.Expressions, migration dışı `Sql`, ICompanyContext dört kez kayıtlı, Idempotency hosted service, AuditEvent sahipliği (davranış değişikliği yok) | git commit | 0 | `7c892c8b9` |
| 3 | Company-filter bypass yüzeyleri: 12 `ReviewedRuntimeDatabaseBypassSources` girdisi ve 5 `ReviewedRuntimeContextConstructions` girdisi, hepsi "Pending the owning team's K3 review" notlu | git commit | 0 | `5d8920c6e` |
| 4 | OpenApiContractGenerationTests: 82 yeni operasyon eklendi, UpdateCounterpartyLegalNameV1 kaldırıldı, değişen component şemaları ve 35 operasyonun problem kodları hizalandı. FinanceShellCapabilityProviderTests güncel provider'a hizalandı (39 deklarasyon, 17 kontrol) | git commit | 0 | `1dcc856cb` |
| 4b | Recovery risky-surface re-pin (sayı 1008 aynı kaldı, yalnız fingerprint değişti) | `python3 -I -S -B tools/generate_recovery_surface_inventory.py` | 0 | `24fd735d7` |
| 5a | Build: Api | `dotnet build src/backend/GoymenERP.Api` | 0 | `24fd735d7` |
| 5b | Build: IntegrationTests | `dotnet build tests/GoymenERP.Api.IntegrationTests` | 0 | `24fd735d7` |
| 5c | Build: ArchitectureTests | `dotnet build tests/GoymenERP.ArchitectureTests` | 0 | `24fd735d7` |
| 5d | Build: 18 UnitTests projesinin hepsi | `dotnet build tests/*UnitTests` (tek tek) | 0 (hepsi) | `24fd735d7` |
| 5e | ArchitectureTests, tam koşu | `dotnet test tests/GoymenERP.ArchitectureTests --no-build` | 1 (401/403; 2 kasıtlı K3 kırmızısı, aşağıda) | `24fd735d7` |
| 5f | OpenAPI testleri | `dotnet test ... --filter FullyQualifiedName~OpenApiContractGeneration` | 1 (9/10; yalnız ortamdan kaynaklanan makine adı hatası, aşağıda). Makine adı geçici olarak maskelenince 10/10 | `24fd735d7` |
| 5g | Finance unit testleri | `dotnet test tests/GoymenERP.Finance.UnitTests --no-build` | 1 (1393/1399). Shell testleri yeşil; kalan 6 hata başlangıç head'inde de var | `24fd735d7` |
| 5g2 | Diğer 17 UnitTests projesi | `dotnet test tests/<proje> --no-build` | 8 proje 0; 9 proje 1 (hepsi başlangıç head'inde aynı, aşağıda) | `24fd735d7` |
| 5h | Recovery python testleri | `python3 -I -S -B -m unittest discover -s tests/recovery_surface_inventory -p "test_*.py"` | 0 (36 test, OK) | `24fd735d7` |
| 5i | Release python testleri | `python3 -I -S -B -m unittest discover -s tests/release_manifest -p "test_*.py"` | 0 (16 test, OK) | `24fd735d7` |
| 5j | Delivery backlog doğrulayıcısı | `python3 -I -S -B tools/validate_delivery_backlog.py` | 0 | `24fd735d7` |
| 5k | .NET bootstrap bütünlük doğrulayıcısı | `python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py` | 0 | `24fd735d7` |
| 5l | Hafif python test paketleri: delivery_backlog_validator, dotnet_bootstrap_integrity, backend_test_group_verifier, rcsi_relational_acceptance_verifier, openapi_contract_drift | `python3 -I -S -B -m unittest discover -s tests/<paket> -p "test_*.py"` | 0 (hepsi) | `24fd735d7` |
| 5m | SQL facts | koşulmadı: bu ortamda gerçek SQL Server yok | n/a | n/a |
| 6 | Push | `git push origin claude/tren-1-entegre-w5`, ardından fast-forward ile `git push origin claude/tren-1-entegre-w5:claude/tren-1` | 0 / 0 (fast-forward `56e43075f..24fd735d7`, ilk denemede) | `24fd735d7` |

## K3 review listesi

Bu kalemler sahip ekibin ve iki bağımsız exact-head review'un onayını bekler.

1. **Bypass inventory, `ReviewedRuntimeDatabaseBypassSources` (12 girdi).** Hepsi "Pending the owning team's K3 review" notlu:
   - PurchasingFutureAllocationLinkHostParticipant
   - PurchaseSupplierReturnHost
   - FinanceCreditAccountService
   - FinanceOpeningBalanceService
   - FounderRecoveryService
   - LogisticsDefiniteShipmentOutboxEventSource
   - LogisticsWeighingDifferenceStore
   - PurchasingService.SupplierReturn
   - PurchaseSupplierReturnStore
   - SettingsReferenceListAdministration
   - SettingsReferenceListReaderV1
   - SettingsWorkCenterCapacityAdministration
2. **`ReviewedRuntimeContextConstructions` (5 girdi):**

   | Girdi | Modül | Sayı |
   |---|---|---|
   | PurchaseSupplierReturnHost | Purchasing | 1 |
   | FinanceCreditAccountService | Finance | 4 |
   | FinanceOpeningBalanceService | Finance | 6 |
   | FounderRecoveryService | Identity | 4 |
   | LogisticsWeighingDifferenceStore | Logistics | 11 |
3. **Yetkisiz `IgnoreQueryFilters` (kasıtlı kırmızı).** Envantere eklenmedi; sahip ekibin K3 kararını bekliyor:
   - `PurchaseGoodsReceiptBatchStore.cs:84`
   - `PurchaseSupplierReturnStore.cs:80`
4. **`Audit_event_context_inventory_is_complete` kırmızı.** IdentityDbContext AuditEvent'i doğrudan taşıyor. Envantere sessizce eklenmedi.
   - `NEEDS-SINAN`: Identity'nin AuditEvent yazması kabul mü, yoksa Audit modülüne port mu açılmalı?
5. **Guard genişletmeleri:**
   - Idempotency hosted-service gate kontrolü
   - RefusedAttempt yolları: `IAuditQueryService.cs`, `AuditQueryService.cs`, `ProductPackageStandardContractsV3.cs`
6. **FinanceModuleExtensions.** Statik factory'lere geçti ve yeniden fingerprint'lendi (`46d85fde…`).
7. **Finance THROW 51861.** Yeni kod THROW sicilinde çakışmasız.
8. **OpenAPI beklenti güncellemesi** (K3: cross-module contract).
   - 35 operasyonda problem kodu eklendi. ReceiveInventoryTransferLineV1:409'dan `inventory_transfer_receipt_split_undecided` kaldırıldı.
   - Kaynak, `contracts/openapi/goymen-erp-v1.json` sözleşmesi. Test, sözleşmeyi izler.

## Kalan problemler

1. **Ortam notu, CI'ı etkilemez.** `BuildTimeContract_IsDeterministicSecretFreeAndDoesNotContactConfiguredDependencies` bu sandbox'ta kırmızı.
   - Sebep: sandbox makine adı kısa bir dizgi, sözleşmedeki `GetSettingsBankCsvMappingsV1` gibi alt dizgilerde geçiyor. CI runner adları bu çakışmayı üretmez.
   - Makine adı geçici olarak maskelenince test yeşil. Maske commit'e girmedi.
2. **ArchitectureTests'teki 2 kasıtlı kırmızı.** Yukarıda K3 madde 3 ve 4. Karar olmadan yeşile çekilmedi.
3. **Finance.UnitTests'te başlangıç head'inden gelen 6 hata.** `56e43075f`'te de aynı hatalar var, bu dalın regresyonu değil. Davranış kararı gerektirdikleri için dokunulmadı:
   - FinanceCreditAccountTests `A_proposal_names_a_group_...(group)`: `$.Group` için JsonException.
   - FinanceCreditAccountTests `The_credit_account_migration_is_pure_DDL_up_...`: tablo sırası beklentisi farklı.
   - FinanceBankAccountTests `The_bank_account_migration_is_discoverable`: son migration artık AddCreditAccounts.
   - FinanceOpeningBalanceTests `Authority_lost_before_the_save_stores_nothing`: Denied beklenirken Succeeded geldi.
   - FinanceInvoiceAcceptanceTests `The_prefill_lists_every_outstanding_installment_...`: taksit sırası tersine döndü.
   - ExpensePaymentServiceTests `A_cash_account_whose_passivation_waits_...`: kasa hesabı değişim guard'ı fırlatıyor.
4. **SQL facts koşulmadı.** Gerçek SQL Server yok. Relational shard ve RCSI kanıtı CI'da alınmalı.
5. **Diğer modüllerin UnitTests'i (`56e43075f` ile aynı, regresyon yok).** 18 unit test projesinin hepsi koşuldu.
   - 8 proje yeşil: Accounting, Ai, Audit, Idempotency, Inventory, SecurityEvents, Settings, Work.
   - Aşağıdaki 9 projedeki hatalar başlangıç head'inde birebir aynı isimlerle var; bu dal yeni hata eklemedi:

     | Proje | Hata sayısı |
     |---|---|
     | Counterparty | 3 |
     | Documents | 1 |
     | Identity | 1 |
     | Logistics | 1 (shell) |
     | Manufacturing | 13 |
     | Organization | 25 (TaxId) |
     | Product | 3 |
     | Purchasing | 1 (shell) |
     | Sales | 5 |

   - Bunlar W1–W5 entegrasyonunda dalgalar arası çakışmalardan geliyor. Sahip şeritlerin hizalaması gerekiyor. Bir kısmı davranış sorusu, bu yüzden bu görevde değiştirilmedi.
