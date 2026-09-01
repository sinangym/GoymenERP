# SQL Server RCSI readiness runbook

Scope: Issue #134, database-operations readiness only. This runbook does not
authorize deployment, production activation, topology selection, credential
selection, a maintenance window, or an automatic database change.

## Invariant

Identity authorization snapshots require the exact application database to
report `sys.databases.is_read_committed_snapshot_on = 1`. The application and
the operator probe use the same fail-closed policy from
`GoymenERP.BuildingBlocks.SqlServer`.

The probe is read-only. It issues one `SELECT` against `sys.databases` for
`DB_ID()` and never issues `ALTER DATABASE`, migration, seed, or application
write commands.

## Evidence command

Keep the connection string in an operator-owned environment variable. Do not
place it in the command line, evidence file, repository, issue, or PR comment.

```powershell
$env:GOYMEN_ERP_RCSI_PROBE_CONNECTION = '<operator-supplied secret connection string>'
$expectedTargetSha256 = '<SHA-256 from the approved target inventory>'
dotnet run --project tools/GoymenERP.SqlServerRcsiReadiness/GoymenERP.SqlServerRcsiReadiness.csproj -- `
  --connection-string-env GOYMEN_ERP_RCSI_PROBE_CONNECTION `
  --expected-database '<exact database name>' `
  --target-label '<approved non-secret environment/slot label>' `
  --expected-data-source-sha256 $expectedTargetSha256 `
  --output artifacts/rcsi/readiness.json
Remove-Item Env:GOYMEN_ERP_RCSI_PROBE_CONNECTION
```

`target-label` accepts only ASCII letters/digits and `. _ - / :`. It is paired
with the expected SHA-256 of the approved SQL Server `DataSource`. The expected
hash must come from the independently approved target inventory; deriving it
from the same connection string immediately before the probe would not verify
the label-to-target binding. The probe normalizes the connected
`DbConnection.DataSource` using Unicode Form KC, trim, and invariant uppercase,
then compares its UTF-8 SHA-256 exactly. Database matching is ordinal and exact.

The JSON has a fixed property order. It contains only the target label,
expected/observed target hashes and database names; it never contains a raw
connection string, server endpoint, user name, exception text, or timestamp.
The contract is `GOYMEN-ERP-SQL-RCSI-READINESS-V1`.

| Exit | Meaning | Gate |
|---:|---|---|
| 0 | exact target is reachable and RCSI is ON | readiness candidate |
| 2 | arguments/binding/connection-string input is invalid | fail closed |
| 3 | configured or observed database differs from the expected database | fail closed |
| 4 | exact database reports RCSI OFF | fail closed |
| 5 | probe or evidence write failed | fail closed |
| 6 | connected SQL data-source hash differs from the label's approved hash | fail closed |

An exit code is not production approval. Archive the JSON with tested commit,
change ticket, operator identity, and execution time in the deployment evidence
system; those fields are deliberately not forged by this tool.

## RCSI-OFF remediation gate

The application and probe do not remediate drift. When exit 4 is observed:

1. Stop the activation. Confirm the expected database, owner, backup/restore
   point, connection inventory, and rollback plan through the approved change
   record.
2. A database owner schedules a maintenance window. Enabling RCSI may require
   exclusive database access; `WITH ROLLBACK IMMEDIATE` terminates active
   transactions and connections. The business and operators must explicitly
   accept that impact.
3. The DBA prepares and peer-reviews the environment-specific `ALTER DATABASE`
   command outside this repository. No application/tool command constructs or
   executes it. The exact database identifier must come from the approved change
   record, not from discovery or a default.
4. After the human-run database change, restart the candidate application and
   rerun the read-only probe with the same expected database, target label, and
   independently approved target fingerprint.
5. Require both application startup and exit-0 JSON evidence. A mismatch,
   probe failure, or RCSI-OFF result remains a stop condition.

Do not automatically turn RCSI OFF as a deployment rollback. A deployment
rollback and a database-option reversal are separate DBA changes with separate
connection/transaction impact analysis.

## Rehearsal evidence

`SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests` proves on disposable
SQL Server databases that:

- RCSI ON is accepted by both the shared probe and Identity startup;
- RCSI OFF is reported without changing the database;
- an expected-database mismatch is rejected before opening the connection;
- an expected data-source fingerprint mismatch is rejected without changing the
  database;
- RCSI OFF prevents startup;
- an explicit test-admin OFF-to-ON change allows a later startup and probe.

The test-only `ALTER DATABASE` helper is confined to relational acceptance code.
It is not linked into the runtime or readiness CLI.
