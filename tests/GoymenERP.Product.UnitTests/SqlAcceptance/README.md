# Product SQL assertion and fixture source

These helpers are compiled in the existing Product test project. They do not start
SQL/API, create a schema, acquire a connection, configure Identity or publish an
environment. Real SQL scenarios are **NOT_RUN** until the accepted Shared Backend
host/schema/fixture is consumed. The existing C006 host is not reused or restarted.

The actual host owner confirmed a borrowed `SqlConnection` / `SqlTransaction` test
boundary. `ProductSqlFixture` takes only the verified contract's run, instance,
database name and exact `codeGroupId`; it is not a second manifest parser. The
publisher's source/resource verification remains mandatory before invoking it.

`SeedCodeGroupsAsync` inserts the explicit reviewed groups into `product.CodeGroups`
inside that transaction. It must include exactly the contract group, allows distinct
reviewed groups with a shared prefix, and neither upserts nor commits. The owner
rolls back the complete seed attempt on any exception. `ReserveRetiredCodeAsync`
reserves a test alias for an already created Product; the real FK/unique constraints
must succeed. Both are fixed parameterized test queries, not runtime SQL tools.

`ProductSqlSnapshot.ReadAsync` verifies the actual database name on the already open
isolated `127.0.0.1,26433` connection, then reads **all four** Product tables through
the existing EF mappings. The context borrows its handles. The caller either holds
the owner transaction or waits for all writers before taking quiescent before/after
snapshots; these reads do not manufacture a race barrier or atomic snapshot.

| Actual scenario once the owner host is available | Product assertion |
| --- | --- |
| Create minimal Draft in exact fixture group | `Created`: one new Product/registration, only relevant prefix counter advances; original rows/groups unchanged; returned identity, roles and references match stored card |
| Basic edit | `Edited`: only target row changes, UUID/code/group/creation time survive, one version advance; code counter/registrations/catalog unchanged |
| Rejection, stale version, failed attempt or owner rollback | `Unchanged`: complete values of all four tables equal their before snapshot, including counter/retired registrations |
| Replay create after a later edit | `Replayed`: original canonical card returned with replay flag; present database unchanged |
| Two successful fresh keys sharing a prefix | `CreatedBatch`: exactly the expected unique new Products/codes/current registrations, one shared counter per prefix, no prior-row mutation or retired-code collision; no general gapless policy |

Run absent-counter and existing-counter races separately. For a same-key race, one
committed creation plus a replay uses `Created` and `Replayed`; two new rows are not
an acceptable outcome. The caller must prove the actual interleaving and inspect
HTTP status/error/correlation. A successful submitted identical-value edit is not
assigned a new no-op policy by the `Unchanged` assertion.

The Shared Backend owns canonical Audit/terminal counts, fresh Identity fixtures,
the sole commit and rollback, and its forthcoming test observer. Those actual
source ports will be consumed for faults after Product/Audit/terminal flush and
before commit. There is no guessed adapter implementation in this directory.
Deterministic late edit concurrency needs a barrier that reaches the participant's
own context; an interceptor on a separate DI context does not establish that fact.

`ProductSqlEvidenceBoundaryTests` corrupts synthetic observations to demonstrate
that the assertion helpers detect out-of-scope table effects, duplicate codes,
wrong replay cards and lost role provenance. It also verifies refusal before opening
a connection. These are assertion/boundary tests, **not SQL persistence evidence**.
