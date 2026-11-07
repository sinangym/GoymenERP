---
name: ef-migration-forward-fix
description: Use when adding an EF Core migration to any module DbContext (new column, table, check constraint, index, backfill, widened state check). Covers dotnet-ef generation, the forward-fix Down that refuses with THROW 51xxx while data would be lost, the unit persistence-model test, the real-SQL Down test, and the inventories every new migration must update (recovery, architecture guard, relational inventory).
---

# EF migration with forward-fix Down

AGENTS.md: "Migration geri alınabilir veya açık forward-fix planlı olmalı." On main the pattern is: `Down` first runs
one reviewed SQL guard that **refuses** (`THROW 51xxx`) while any row would lose meaning, then drops. Canonical
example: `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261103012142_AddPurchaseRequestLineSource.cs`
(#928 `81418f8`, THROW 51167). Others: `…/Settings/…/20261102143743_AddExchangeRateRecords.cs` (51201),
`…/Manufacturing/…/20261102181335_AddManufacturingPlanLifecycle.cs` (51187/51188).

## Steps

1. Change rows/`*DbContext.cs` (`OnModelCreating`): columns nullable unless a backfill is in Up; check constraints as
   an `internal const string …Check` on the DbContext (e.g. `PurchasingDbContext.RequestLineSourceCheck`) so tests can compare `check.Sql`; filtered index where useful.
   Every new table carries `CompanyId` and the module's company query filter.
2. Generate (needs .NET 10; tool pinned in `dotnet-tools.json`, `dotnet-ef` 10.0.8):
   ```bash
   dotnet tool restore
   dotnet tool run dotnet-ef migrations add <PascalName> \
     --project src/backend/Modules/<M>/GoymenERP.Modules.<M>.Infrastructure/GoymenERP.Modules.<M>.Infrastructure.csproj \
     --startup-project src/backend/GoymenERP.Api/GoymenERP.Api.csproj \
     --context <M>DbContext --output-dir Persistence/Migrations
   ```
   Commit the migration, its `.Designer.cs` and the updated `<M>DbContextModelSnapshot.cs` together. File names must
   be `yyyyMMddHHmmss_<Name>.cs` and the class name must equal `<Name>` (the recovery generator enforces it).
3. Write `Down` as forward-fix: **first** operation is one `migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [schema].[Table] WHERE <data that would be lost>) BEGIN THROW 51xxx, '<English reason>; <Migration> is not rolled back (forward fix).', 1; END;""")`, then the generated drops.
   An empty store must still roll back freely.
4. Pick the THROW number: next unused one, checked against main **and open PRs of other lanes** (lanes add
   migrations in parallel):
   ```bash
   grep -rhoE 'THROW 5[0-9]{4}' src/backend --include=*.cs | sort -u | tail -5
   ```
   Note it in the module README migration table (e.g. `src/backend/Modules/Settings/README.md`, Purchasing README
   "Down guard 51166" note).
5. Unit test in `tests/GoymenERP.<M>.UnitTests/<M>PersistenceModelTests.cs`: (a) the "migrations are the last ones
   and the snapshot matches the model" fact — extend its `TakeLast(n)` list with the new name; (b) a model fact:
   nullability, `GetCheckConstraints()` SQL equals the DbContext const, index columns/filter, `UpOperations` has no
   `SqlOperation` (when Up is pure DDL), `DownOperations[0]` is the `SqlOperation` containing `THROW 51xxx`, and
   exactly one `SqlOperation` in Down.
6. Real-SQL test in `tests/GoymenERP.Api.IntegrationTests/<M>SqlServerRelationalAcceptanceTests*.cs` (partial class
   per slice): `var guard = Assert.IsType<SqlOperation>(new <Name>().DownOperations[0]).Sql;` → `await run.ExecuteAsync(guard)`
   passes with only harmless rows → store a row that the guard protects → `Assert.ThrowsAsync<SqlException>` with
   `Number == 51xxx` → the protected row is still there. Name it like
   `The_<x>_migrations_Down_passes_with_<harmless>_and_refuses_while_<protected>_exists` (ASCII only).
   Locally it needs `GOYMEN_ERP_TEST_SQLSERVER_CONNECTION` (see README.md); without it the class shows `Skipped`.
7. Inventories (all required, same PR):
   - add each new SQL fact's FQN to `tools/relational_inventory/<module>.txt` (sorted) → `test-naming-and-groups`;
   - `ReviewedMigrationMutationSources` entry (`["Sql"] = 1`) and re-pinned DbContext/relational fingerprints →
     `architecture-guard-repin`;
   - migration count +1 in generator, schema, recovery and release-manifest tests → `recovery-inventory-repin`.
8. PR body: migration name, THROW number, "Down refuses while … (forward fix)", rollback impact. Migration/constraint
   is **K3** (two independent exact-head reviews, real-SQL evidence).

## Pitfalls

- The guard must come **before** every drop; a drop-then-check loses data on the first failing statement.
- Reusing a THROW number of another migration makes the failing guard ambiguous in logs; reviewers flag it.
- Use binary collation (`COLLATE Latin1_General_100_BIN2`) when a check compares actor ids (maker-checker), as in
  `AddPurchaseRequestWithdrawal` (#907).
- Changing a column that feeds an idempotency request hash changes replay results; keep `null` out of the hash so
  old keys still replay (#928 "null kaynak idempotency hash'ine girmez").
- Migration/retention policy choices (deleting data, archival) are not yours to pick: `NEEDS-SINAN`.
