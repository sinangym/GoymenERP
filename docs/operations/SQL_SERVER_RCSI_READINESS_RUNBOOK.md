# SQL Server RCSI readiness runbook

Scope: Issue #134 database-operations readiness only. This runbook does not
authorize deployment, production activation, provider/topology selection,
credentials, a maintenance window, or an automatic database change.

## Proof boundary

Identity authorization snapshots require the exact application database to
report `sys.databases.is_read_committed_snapshot_on = 1`. Identity startup and
the operator CLI use the same read-only policy from
`GoymenERP.BuildingBlocks.SqlServer`. The probe issues one `SELECT` against
`sys.databases`; it never issues `ALTER DATABASE`, a migration, seed, or an
application write.

The target binding is a **logical endpoint** SHA-256 set. It does not prove a
physical SQL instance, DNS resolution, TLS peer, listener membership, failover
state, or network path. Those staging/topology gates remain separate. Identity
derives the approved runtime set from non-empty connection-string `DataSource`
and `FailoverPartner`; the CLI receives one or more approved hashes from an
external inventory.

The executable proof is an approved **self-contained publish payload**. It is
not a source checkout, a framework-dependent DLL, or a `dotnet` host resolved
from `PATH`. Its external canonical manifest uses
`GOYMEN-ERP-RCSI-RUNTIME-PAYLOAD-V2` and binds:

- the exact `sourceRevisionId`;
- the root-level self-contained `entryExecutablePath`;
- every payload file's sorted, unique normalized path, byte length, and SHA-256;
- the tool assembly, shared SQL policy assembly, deps/runtimeconfig, native
  apphost, managed runtime, native runtime, and all transitive payload files.

The manifest is outside the payload. The approved runner must expose both the
payload and that external manifest through an OS-enforced read-only mount/ACL
that the wrapper identity cannot modify for the whole invocation. Pre/post
hashing detects drift but does not by itself defeat an active
replace-execute-restore writer. The tool verifies the complete payload,
executing apphost, tool assembly and source revision before reading the secret
and again after probing. Missing/extra files, dependency drift, reparse paths,
device/UNC/8.3 aliases, or manifest drift fail closed. Windows runners must
also prohibit `SUBST`/DOS-device aliases for payload, manifest, and evidence
paths; the wrapper does not prove drive-letter alias identity. Evidence is
written outside the payload to a new invocation directory.

This repository does not prove that external staging prerequisite. Until a
trusted artifact owner supplies and verifies the read-only staging boundary,
the operational state remains `STAGING_REHEARSAL_PENDING`; even a `READY`
probe is not deployment, cutover, or production-activation evidence.

## Approved wrapper

Use the reviewed PowerShell 7.2+ artifact:

`tools/GoymenERP.SqlServerRcsiReadiness/Invoke-GoymenERP.SqlServerRcsiReadiness.ps1`

Do not copy an inline script from documentation and do not use `dotnet run`.
The deployment runner or secret manager injects the connection string only as
`GOYMEN_ERP_RCSI_PROBE_CONNECTION` into a **dedicated** wrapper process. The
wrapper is never dot-sourced or invoked as an in-process PowerShell script.
Parameter binding and `#requires` run before script `try/finally`; therefore the
outer runner must create a fresh process with a sanitized environment and clear
its own injected secret in an outer `finally`, including binding/start failure.
Never type a secret assignment into interactive history.

The same approved immutable build/change inventory supplies:

- absolute self-contained payload directory;
- absolute external manifest path and exact SHA-256;
- exact reviewed 40-hex commit;
- exact tool DLL SHA-256;
- one or more approved logical-endpoint hashes;
- exact database and non-secret target label;
- operator-owned positive probe and overall-process timeout values.

Invoke the wrapper through a runner-owned, approved **absolute** PowerShell 7.2+
path with `-NoProfile -NonInteractive -File`. The runner passes endpoint hashes
as a canonical JSON string array so native `-File` argument binding remains
unambiguous for one or many values. Every invocation uses a fresh, previously
absent evidence directory and output path. The following is an argument
template for that dedicated process; it is not an in-process command block:

```powershell
<approved absolute pwsh> -NoLogo -NoProfile -NonInteractive -File `
  '<reviewed absolute wrapper path>' `
  -PayloadRoot '<approved absolute self-contained payload directory>' `
  -PayloadManifest '<approved absolute external manifest path>' `
  -ExpectedPayloadManifestSha256 '<approved manifest SHA-256>' `
  -ExpectedToolAssemblySha256 '<approved tool DLL SHA-256>' `
  -TestedCommit '<approved exact 40-hex source revision>' `
  -ApprovedLogicalEndpointSha256Json '["<approved SHA-256>","<optional failover SHA-256>"]' `
  -ExpectedDatabase '<exact approved database name>' `
  -TargetLabel '<approved non-secret environment/slot label>' `
  -ProbeTimeoutSeconds <operator-owned positive seconds> `
  -OverallProcessTimeoutSeconds <operator-owned positive seconds> `
  -OutputPath '<fresh absolute evidence directory>\readiness.json'
```

The outer runner must place the dedicated wrapper process in its own
operator-managed Windows Job Object or Unix service/cgroup/process containment
before injecting the secret. This outer boundary handles abrupt wrapper
termination; the reviewed wrapper additionally assigns the managed apphost to a
kill-on-close Windows job or the apphost's self-created Unix process group.

The wrapper normalizes and validates commit/hash inputs, validates the apphost
against the approved manifest before launch, invokes its absolute payload path
with an empty child environment except for the Windows loader prerequisites
`SystemRoot` and `WINDIR`, establishes OS containment, then
passes the secret through redirected standard input (never the child
environment). One monotonic operator-owned deadline covers launch, stdin
handoff, process wait, output drain, containment release and cleanup. It kills
and joins the child tree on timeout, verifies the payload again, compares
raw stdout bytes with evidence bytes, validates every evidence field before
emitting it, clears the fixed secret from the wrapper environment on every
script-controlled path, and propagates non-zero child exit codes. The dedicated
wrapper process lifetime also clears the secret on parameter-binding or
`#requires` failure; the outer deployment runner remains responsible for its
own `finally` cleanup and for treating every non-zero native exit as failure.
Successful
evidence must bind the same run ID, commit, apphost, tool DLL, payload manifest,
UTC observation interval, target, database, endpoint inventory, and RCSI state.

The wrapper reserves one fifth of that same hard deadline for containment and
pipe cleanup, with reviewed technical bounds of 250 milliseconds minimum and
5 seconds maximum. This is not a second timeout: normal child execution is
cancelled before `T` so kill/join/drain can still finish by the original `T`.
`OverallProcessTimeoutSeconds` binds the contained apphost process phase; it
does not bound PowerShell startup or canonical pre/post payload inventory
hashing. The required outer deployment runner must enforce its own hard wrapper
process ceiling and kill its outer containment on expiry.

Any wrapper or child non-zero exit is a failed gate. Stale READY evidence is
never overwritten. Every retry uses a new absent directory/path.

## RCSI-OFF remediation

The application, CLI and wrapper never remediate RCSI drift. Exit 4 stops
activation. A DBA independently confirms the exact database and physical/
topology evidence, backup/restore point, owner, rollback plan, and maintenance
window. Any environment-specific `ALTER DATABASE` command is prepared and
peer-reviewed outside this repository. Enabling RCSI may terminate active
connections; that impact requires explicit operational acceptance.

After the human-run change, restart the candidate and rerun the exact approved
self-contained payload with a fresh output. Do not automatically turn RCSI OFF
as a deployment rollback: deployment rollback and database-option reversal are
separate DBA changes.

## Rehearsal evidence

`SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests` uses disposable SQL
Server databases and the real self-contained apphost to prove ON readiness, OFF
no-change behavior, startup/probe parity, target mismatch, CLI exits 0/4/6,
canonical raw-byte parity, payload/apphost binding, stale-output rejection and
kill-and-await cleanup. It also exercises the versioned PowerShell wrapper for a
sorted multi-endpoint inventory, native exits 0/2/4, timeout cleanup and secret
removal. Test-only `ALTER DATABASE` remains confined to tests.
