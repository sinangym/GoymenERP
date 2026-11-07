---
name: architecture-guard-repin
description: Use when a change edits a reviewed database-bypass surface — a module DbContext, a *Store.cs / participant that uses Microsoft.Data.SqlClient, a *ModuleExtensions.cs, a migration that calls migrationBuilder.Sql (THROW guards, backfills), a design-time factory — or adds a SqlServerRelationalAcceptanceTests fact to tools/relational_inventory/<module>.txt, and the architecture test group fails with "does not match the exact fingerprint. Expected X, found Y" in PlatformTenantQueryFilterBypassGuardTests.
---

# Architecture guard re-pin (PlatformTenantQueryFilterBypassGuardTests)

`tests/GoymenERP.ArchitectureTests/PlatformTenantQueryFilterBypassGuardTests.cs` was the most-edited test file on
main since 2026-10-20 (19 commits). It pins reviewed fingerprints so that tenant-filter bypasses, raw SQL and
module wiring cannot change unseen. Example: #928 `81418f8` (Purchasing relational inventory, DbContext,
`PurchasingStore.cs`, new migration entry).

## Pinned dictionaries (search these names)

| Dictionary | What it pins |
|---|---|
| `ReviewedRelationalInventoryFingerprints` | one fingerprint per `tools/relational_inventory/<module>.txt` |
| `ReviewedPlatformDbContexts` | each module `*DbContext.cs` fingerprint |
| `ReviewedRuntimeDatabaseBypassSources` | stores, `*ModuleExtensions.cs`, runtimes: fingerprint + `ExpectedApiOccurrences` (e.g. `["Microsoft.Data.SqlClient"] = 1`) |
| `ReviewedPlatformContextFactories` | `*DesignTimeDbContextFactory.cs` (context name + fingerprint) |
| `ReviewedMigrationMutationSources` | every migration that uses `migrationBuilder.Sql`: fingerprint + `["Sql"] = <number of Sql calls>` |

## Steps

1. Finish the code change. Compute fingerprints locally (no dotnet needed; mirrors
   `ComputeReviewedSourceFingerprint`):
   ```bash
   python3 -I .claude/skills/architecture-guard-repin/scripts/guard_fingerprint.py \
     tools/relational_inventory/purchasing.txt \
     src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs
   ```
   (Verified: reproduces the pins on main for `purchasing.txt`, `PurchasingStore.cs` and migration
   `20261103012142_AddPurchaseRequestLineSource.cs`.)
2. Replace the old hash in the matching dictionary entry. Keep the existing comment chain style: one line
   "Re-fingerprinted for <slice>: <what changed>; <same token count | +N facts>" and, for the relational
   inventory, the `(old8… -> new8…)` note.
3. New migration with `migrationBuilder.Sql(...)`: add an entry to `ReviewedMigrationMutationSources` with the
   path (split with `+ "…"` like the neighbours), the fingerprint and `["Sql"] = <count>`, plus a comment saying
   what the statement does (e.g. "Down refuses (51167, used by no other migration) while …").
4. New SQL acceptance fact: add its FQN to `tools/relational_inventory/<module>.txt` in **ordinal sort order**
   (theory rows: `<name>  x<n>  # <reason>`), then re-pin `ReviewedRelationalInventoryFingerprints`. See
   `test-naming-and-groups`.
5. CI is authoritative: the architecture group (`tools/verify_backend_test_groups.py --group architecture`) prints
   "Expected …, found …" for anything still wrong. Locally, with .NET 10:
   ```bash
   dotnet test tests/GoymenERP.ArchitectureTests/GoymenERP.ArchitectureTests.csproj \
     --filter "FullyQualifiedName~PlatformTenantQueryFilterBypassGuard"
   ```
   (The cloud container has no `dotnet`; rely on CI there.)
6. Any edit here is a test change, so it does not move the recovery source surface — but the edited backend files
   do: run `recovery-inventory-repin` too.

## Pitfalls

- EF-generated migration files start with a UTF-8 BOM; hashing with plain `utf-8` gives a wrong value. The helper
  uses `utf-8-sig` like `File.ReadAllText`.
- Trailing whitespace and CRLF do not change the fingerprint; any other byte (comments included) does.
- `ExpectedApiOccurrences` counts are checked separately from the hash: adding a second `Sql(...)` call or a
  second `SqlClient` use needs the count bumped, and a reviewer's justification in the comment.
- Never add a file to an allow-list just to make the guard pass without saying in the PR why the bypass is
  needed; that is a K3 review item (tenant isolation).
- Sibling guards with their own pins: `PlatformTenantQueryFilterBypassGuardAuditTests.cs`,
  `…CounterpartyTests.cs`, `…IdempotencyTests.cs`, `ModuleDependencyGuardTests.cs`,
  `PlatformRequestMetadataProblemOwnershipGuardTests.cs` (reserved problem codes `company_context_invalid`,
  `correlation_metadata_invalid` belong to `src/backend/GoymenERP.Api/Errors/ErpProblemCodes.cs` only).
