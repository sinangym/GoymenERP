---
name: test-naming-and-groups
description: Use when adding or renaming any backend xUnit test (class or method), adding a SqlServerRelationalAcceptanceTests fact, adding a test project, or when the "Backend test group" / "SQL Server relational shard" / "RCSI" CI jobs fail with "identity is not canonical", "discovery … wrong", inventory unsorted/missing, or count mismatch. Covers the ASCII-only FQN rule, naming style, the per-module relational inventory files and the group verifier pins.
---

# Backend test naming, groups and relational inventory

CI does not run `dotnet test` freely: `tools/verify_backend_test_groups.py` (groups `architecture`, `api`,
`modules`) and `tools/verify_rcsi_relational_acceptance.py` (SQL shards A/B/C + RCSI) discover every test, bind it to
the exact commit and fail closed on anything unexpected.

## Rules

1. **Class + method names are ASCII only.** Both verifiers require
   `FQN_PATTERN = GoymenERP\.[A-Za-z0-9_.+\`]+` on `FullyQualifiedName` and on the TRX `className.name`
   (`tools/verify_backend_test_groups.py`). C# accepts `Ö`, `ş`, `ı` in identifiers — the compiler is happy, CI is
   not (a test name with "Ö" broke CI). Turkish belongs in `InlineData` arguments, assertion messages and
   comments, not in identifiers. Write `Musteri`, `Odeme`, `Siparis` or use English.
2. Style used on main: an English sentence in snake case that states the behaviour and the evidence, e.g.
   `A_request_decision_is_one_save_the_maker_cannot_decide_and_the_checks_hold_the_facts`,
   `The_line_source_migrations_Down_passes_with_only_manual_lines_and_refuses_while_a_sourced_line_exists`.
   Older suites use `Subject_Condition_Result` PascalCase (`ValidateOnly_MissingDatabaseReturnsRedacted503…`); match
   the file you are in.
3. Big suites are `partial` classes split per slice: `PurchasingServiceTests.RequestSource.cs`,
   `PurchasingSqlServerRelationalAcceptanceTests.RequestSource.cs`.
4. Real-SQL facts live in `tests/GoymenERP.Api.IntegrationTests/` in a class whose name ends with
   `SqlServerRelationalAcceptanceTests` and **starts with the module name** (`Purchasing…`, `Sales…`); that prefix
   decides the owning inventory file (`MODULE_CLASS_PREFIXES`). Other names fall to `platform.txt`; the bare
   `SqlServerRelationalAcceptanceTests` class is Counterparty's.

## Adding a relational (SQL) fact

1. Add its FQN `GoymenERP.Api.IntegrationTests.<Class>.<Method>` to `tools/relational_inventory/<module>.txt`
   (one file per module, single writer = that module's lane).
2. Keep the file **ordinal-sorted** (Python `sorted` / `LC_ALL=C`: uppercase < `_` < lowercase), no trailing
   whitespace, no tabs, no duplicates. A theory that runs n > 1 rows: `<FQN>  x<n>  # <reason>` (two spaces each side).
   Check the order quickly:
   ```bash
   grep -v '^#' tools/relational_inventory/<module>.txt | grep -v '^$' | sed 's/  x[0-9].*//' | LC_ALL=C sort -c && echo sorted
   ```
3. Re-pin its fingerprint in `ReviewedRelationalInventoryFingerprints` (`architecture-guard-repin`).
4. The shard is chosen by module (`SHARD_MODULES` in `tools/verify_rcsi_relational_acceptance.py`: A = inventory,
   manufacturing, purchasing, work; B = finance, accounting, product, identity, organization; C = counterparty,
   logistics, sales, platform). In the `api` group these facts are the only allowed skips.

## Adding a test project

Update `GROUP_PROJECTS`, `TEST_PROJECT_COUNT` and `SOLUTION_PROJECT_COUNT` in `tools/verify_backend_test_groups.py`
(and the `.slnx`), then run `python3 -I -S -B -m unittest discover -s tests/backend_test_group_verifier -p "test_*.py"`.
That is a `tools/` change — K3 if it touches the quality gate semantics.

## Local commands

```bash
python3 -I -S -B -m unittest discover -s tests/backend_test_group_verifier -p "test_*.py"
python3 -I -S -B -m unittest discover -s tests/rcsi_relational_acceptance_verifier -p "test_*.py"
# with .NET 10 and a test SQL Server (README.md):
dotnet test tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj --configuration Release \
  --filter "FullyQualifiedName~PurchasingSqlServerRelationalAcceptanceTests"
```
The cloud container has no `dotnet`; the exact verifiers run in `.github/workflows/ci.yml`
(`backend-test-group`, `relational-shard`, `relational-rcsi`).

## Frontend (Vitest) names

`describe`/`it` strings may be Turkish or English; they are not FQN-checked. Keep SENTETIK markers in fixtures
(`'SENTETIK-context-a'`) so test data never looks real.

## Pitfalls

- Renaming a relational fact needs the inventory line renamed, re-sorted and re-pinned in the same commit.
- A fact listed in the inventory but not discovered (or the reverse) fails the shard: never add a line for a test
  that is conditionally compiled or skipped.
- Do not put the module prefix on a class whose tests belong to another module's file; the verifier refuses
  "a name whose class belongs to another module's file".
