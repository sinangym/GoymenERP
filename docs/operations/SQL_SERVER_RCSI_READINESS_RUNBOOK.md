# SQL Server RCSI readiness runbook

Scope: Issue #134, database-operations readiness only. This runbook does not
authorize deployment, production activation, provider/topology selection,
credential selection, a maintenance window, or an automatic database change.

## Invariant and proof boundary

Identity authorization snapshots require the exact application database to
report `sys.databases.is_read_committed_snapshot_on = 1`. The application and
operator CLI use the same fail-closed policy from
`GoymenERP.BuildingBlocks.SqlServer`.

The probe is read-only. It issues one `SELECT` against `sys.databases` for
`DB_ID()` and never issues `ALTER DATABASE`, migration, seed, or application
write commands.

The target binding is deliberately named **logical endpoint**. The normalized
SHA-256 value comes from `DbConnection.DataSource`, which is the configured or
connected logical endpoint text. It does not prove the physical SQL instance,
DNS resolution, TLS peer identity, listener membership, failover state, or
network path. Those staging/topology gates remain open and require independent
operational evidence.

The approved binding is a sorted, unique set of one or more logical-endpoint
hashes. This permits an independently approved primary/failover endpoint set
without selecting or assuming one topology in this repository. Identity startup
derives its runtime set from non-empty connection-string `DataSource` and
`FailoverPartner` values. The CLI receives the approved hashes from an external
inventory and never discovers or approves topology.

## Fresh evidence command

The connection string must already be injected into the named environment
variable by the approved secret manager or deployment runner. Never type or
paste a secret assignment into an interactive command history, repository,
issue, PR comment, or evidence file.

The following values are operator-owned inputs. `testedCommit` must be the exact
reviewed 40-hex commit, every logical-endpoint hash must come from the approved
inventory, and the timeout must be an explicitly approved positive value within
the runtime timer's technical range. The tool supplies no default timeout.

```powershell
$probeEnvName = 'GOYMEN_ERP_RCSI_PROBE_CONNECTION'
$runId = [Guid]::NewGuid().ToString('D')
$testedCommit = '<reviewed exact 40-hex commit>'
$approvedLogicalEndpointHashes = @(
  '<approved logical endpoint SHA-256>'
  # Add another independently approved hash when the deployment inventory has one.
)
$timeoutSeconds = '<approved positive technical timeout seconds>'
$evidenceDirectory = Join-Path 'artifacts/rcsi' $runId
$outputPath = Join-Path $evidenceDirectory 'readiness.json'

if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($probeEnvName))) {
  throw "The deployment runner did not inject the required probe environment variable."
}

New-Item -ItemType Directory -Path $evidenceDirectory -ErrorAction Stop | Out-Null
$arguments = @(
  '--connection-string-env', $probeEnvName,
  '--expected-database', '<exact approved database name>',
  '--target-label', '<approved non-secret environment/slot label>',
  '--run-id', $runId,
  '--tested-commit', $testedCommit,
  '--timeout-seconds', $timeoutSeconds,
  '--output', $outputPath
)
foreach ($hash in $approvedLogicalEndpointHashes) {
  $arguments += @('--expected-logical-endpoint-sha256', $hash)
}

try {
  & dotnet run --project `
    tools/GoymenERP.SqlServerRcsiReadiness/GoymenERP.SqlServerRcsiReadiness.csproj `
    -- @arguments
  $probeExitCode = $LASTEXITCODE
}
finally {
  Remove-Item -LiteralPath "Env:$probeEnvName" -ErrorAction SilentlyContinue
}
```

`target-label` accepts only ASCII letters/digits and `. _ - / :`. Logical
endpoint normalization is Unicode Form KC, trim, and invariant uppercase before
UTF-8 SHA-256. Database matching is ordinal and exact.

`--run-id`, `--tested-commit`, `--timeout-seconds`, and `--output` are required.
The run ID is a non-empty UUID. The tool captures `observedAtUtc` itself. The
output path must not already exist. Each invocation must use a new run ID and a
new empty evidence directory/path.

Evidence is written to a same-directory `CreateNew` temporary file, flushed,
and atomically moved without overwrite. Only a temporary file successfully
created by this invocation may be deleted on failure. A stale READY file is
never overwritten or accepted as evidence for a later run.

The JSON has a fixed property order and binds `runId`, exact tested commit,
UTC observation time, target label, approved/observed logical-endpoint hashes,
database names, RCSI result, and exit code. It never contains the connection
string, raw logical endpoint, user name, exception text, or credential.

| Exit | Meaning | Gate |
|---:|---|---|
| 0 | approved logical endpoint/database is reachable and RCSI is ON | readiness candidate |
| 2 | argument, freshness metadata, binding, timeout, or connection input is invalid | fail closed |
| 3 | configured or observed database differs from the expected database | fail closed |
| 4 | exact database reports RCSI OFF | fail closed |
| 5 | timeout, probe, or new-file evidence publication failed | fail closed |
| 6 | connected logical-endpoint hash is outside the approved set | fail closed |

An exit code is not production approval. The gate accepts evidence only when
all of these match the same invocation: process exit `0`, JSON `exitCode=0`,
the requested `runId`, the exact reviewed commit SHA, fresh UTC `observedAtUtc`,
and byte-identical stdout/file JSON. Archive the evidence with the external
change ticket and operator identity; the tool deliberately does not forge those
external records.

## RCSI-OFF remediation gate

The application and CLI do not remediate drift. When exit 4 is observed:

1. Stop activation. Confirm the exact database, independent logical/physical
   target evidence, owner, backup/restore point, connection inventory, and
   rollback plan through the approved change record.
2. A database owner schedules a maintenance window. Enabling RCSI may require
   exclusive database access; `WITH ROLLBACK IMMEDIATE` terminates active
   transactions and connections. The business and operators must explicitly
   accept that impact.
3. The DBA prepares and peer-reviews the environment-specific `ALTER DATABASE`
   command outside this repository. No application/tool command constructs or
   executes it. The exact database identifier must come from the approved change
   record, not discovery or a default.
4. After the human-run database change, restart the candidate application and
   rerun the read-only probe with a new run ID/output path and the same reviewed
   commit, database, target label, and approved logical-endpoint set.
5. Require both application startup and fresh exit-0 evidence. A mismatch,
   timeout, probe failure, stale file, or RCSI-OFF result remains a stop condition.

Do not automatically turn RCSI OFF as a deployment rollback. A deployment
rollback and database-option reversal are separate DBA changes with separate
connection/transaction impact analysis.

## Rehearsal evidence

`SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests` proves on disposable
SQL Server databases that:

- RCSI ON is accepted by both the shared probe and Identity startup;
- RCSI OFF is reported without changing the database;
- an expected-database mismatch is rejected before opening the connection;
- an unapproved logical endpoint is rejected without changing the database;
- RCSI OFF prevents startup;
- an explicit test-admin OFF-to-ON change allows a later startup and probe;
- the real CLI process produces canonical atomic files for exits 0, 4, and 6;
- stale output is rejected without overwrite or secret/raw-endpoint disclosure.

The test-only `ALTER DATABASE` helper is confined to relational acceptance code.
It is not linked into the runtime or readiness CLI.
