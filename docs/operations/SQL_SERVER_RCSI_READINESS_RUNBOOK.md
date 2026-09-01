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

The executable proof is a prebuilt runtime payload, not a source checkout. Its
external canonical manifest is
`GOYMEN-ERP-RCSI-RUNTIME-PAYLOAD-V1`: exact `sourceRevisionId` plus a sorted,
unique list of every payload file's normalized relative path, byte length, and
SHA-256. The CLI rejects missing/extra files, path traversal, reparse points,
manifest drift, dependency drift, and a tool DLL/commit mismatch both before
reading the connection secret and again after the probe. The manifest itself
must be outside the payload directory. The evidence output must also be outside
that directory.

## Fresh fail-closed invocation

The approved immutable build inventory supplies all of these values together:

- absolute prebuilt payload directory and tool DLL path;
- absolute external canonical payload-manifest path and its SHA-256;
- exact 40-hex reviewed commit from that manifest;
- exact tool DLL SHA-256 from that manifest;
- approved logical-endpoint hashes.

The deployment runner or secret manager must inject the connection string into
the named environment variable before this wrapper starts. Never type a secret
assignment into interactive history. Both the probe timeout and the whole-child
process timeout are operator-owned, required positive values; this repository
chooses no operational default. The CLI timeout covers only connection/probe
work. The wrapper timeout covers the complete child process and kills the full
child tree before cleanup if exceeded.

Save and review the block below as a non-interactive `.ps1` runner step. Do not
paste it into an interactive shell. An uncaught `throw` is the runner failure;
the orchestration layer must require the PowerShell process itself to exit zero,
not merely inspect `$LASTEXITCODE` from a nested command.

```powershell
$probeEnvName = 'GOYMEN_ERP_RCSI_PROBE_CONNECTION'
$process = $null
$startInfo = $null
$processStarted = $false
$nativeExitCode = $null
$stdoutBuffer = $null
$stderrBuffer = $null
$stdoutTask = $null
$stderrTask = $null
$waitCts = $null

try {
  # All values below come from the same approved immutable build/change inventory.
  $payloadRoot = [IO.Path]::GetFullPath('<approved prebuilt payload directory>')
  $toolDll = [IO.Path]::GetFullPath('<approved prebuilt tool DLL path>')
  $payloadManifest = [IO.Path]::GetFullPath('<approved external payload manifest path>')
  $expectedPayloadManifestSha256 = '<approved payload manifest SHA-256>'
  $expectedToolAssemblySha256 = '<approved tool DLL SHA-256>'
  $testedCommit = '<approved exact 40-hex source revision>'
  $approvedLogicalEndpointHashes = @(
    '<approved logical endpoint SHA-256>'
  ) | Sort-Object -Unique
  $expectedDatabase = '<exact approved database name>'
  $targetLabel = '<approved non-secret environment/slot label>'
  $probeTimeoutSeconds = [long]'<operator-owned positive probe timeout seconds>'
  $overallProcessTimeoutSeconds = [long]'<operator-owned positive overall timeout seconds>'

  if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($probeEnvName))) {
    throw 'The approved runner did not inject the probe secret environment variable.'
  }
  if ($probeTimeoutSeconds -le 0 -or $overallProcessTimeoutSeconds -le 0) {
    throw 'Both operator-owned timeouts must be positive.'
  }
  try {
    $probeTimeout = [TimeSpan]::FromSeconds($probeTimeoutSeconds)
    $overallProcessTimeout = [TimeSpan]::FromSeconds($overallProcessTimeoutSeconds)
  }
  catch {
    throw 'An operator-owned timeout is outside the supported technical range.'
  }
  $maximumTimeoutMilliseconds = [double][uint32]::MaxValue - 1
  if ($probeTimeout.TotalMilliseconds -gt $maximumTimeoutMilliseconds -or
      $overallProcessTimeout.TotalMilliseconds -gt $maximumTimeoutMilliseconds) {
    throw 'An operator-owned timeout is outside the supported technical range.'
  }
  if ($approvedLogicalEndpointHashes.Count -eq 0 -or
      @($approvedLogicalEndpointHashes | Where-Object {
        $_ -notmatch '\A[0-9a-f]{64}\z'
      }).Count -ne 0) {
    throw 'The approved logical-endpoint inventory must contain lowercase SHA-256 values.'
  }
  if (-not [IO.Path]::IsPathFullyQualified($payloadRoot) -or
      -not [IO.Path]::IsPathFullyQualified($toolDll) -or
      -not [IO.Path]::IsPathFullyQualified($payloadManifest)) {
    throw 'Payload, tool, and manifest paths must be absolute.'
  }
  if (-not (Test-Path -LiteralPath $toolDll -PathType Leaf) -or
      -not (Test-Path -LiteralPath $payloadManifest -PathType Leaf)) {
    throw 'The approved prebuilt tool or external manifest is missing.'
  }

  $runId = [Guid]::NewGuid().ToString('D')
  $evidenceDirectory = [IO.Path]::GetFullPath((Join-Path 'artifacts/rcsi' $runId))
  $outputPath = Join-Path $evidenceDirectory 'readiness.json'
  if (Test-Path -LiteralPath $evidenceDirectory) {
    throw 'The invocation evidence directory is not fresh.'
  }
  New-Item -ItemType Directory -Path $evidenceDirectory -ErrorAction Stop | Out-Null

  $startInfo = [Diagnostics.ProcessStartInfo]::new('dotnet')
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.ArgumentList.Add($toolDll)
  $arguments = @(
    '--connection-string-env', $probeEnvName,
    '--expected-database', $expectedDatabase,
    '--target-label', $targetLabel,
    '--run-id', $runId,
    '--tested-commit', $testedCommit,
    '--expected-tool-assembly-sha256', $expectedToolAssemblySha256,
    '--runtime-payload-root', $payloadRoot,
    '--runtime-payload-manifest', $payloadManifest,
    '--expected-runtime-payload-manifest-sha256', $expectedPayloadManifestSha256,
    '--timeout-seconds', $probeTimeoutSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--output', $outputPath
  )
  foreach ($hash in $approvedLogicalEndpointHashes) {
    $arguments += @('--expected-logical-endpoint-sha256', $hash)
  }
  foreach ($argument in $arguments) { $startInfo.ArgumentList.Add([string]$argument) }

  # Do not allow host/runtime injection into the reviewed prebuilt child.
  @(
    'DOTNET_STARTUP_HOOKS','DOTNET_ADDITIONAL_DEPS','DOTNET_SHARED_STORE',
    'CORECLR_ENABLE_PROFILING','CORECLR_PROFILER','CORECLR_PROFILER_PATH',
    'CORECLR_PROFILER_PATH_32','CORECLR_PROFILER_PATH_64',
    'COR_ENABLE_PROFILING','COR_PROFILER','COR_PROFILER_PATH'
  ) | ForEach-Object { $startInfo.Environment.Remove($_) | Out-Null }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  $startedAtUtc = [DateTimeOffset]::UtcNow
  if (-not $process.Start()) { throw 'The prebuilt readiness process did not start.' }
  $processStarted = $true

  $stdoutBuffer = [IO.MemoryStream]::new()
  $stderrBuffer = [IO.MemoryStream]::new()
  $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutBuffer)
  $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrBuffer)
  $waitCts = [Threading.CancellationTokenSource]::new($overallProcessTimeout)
  $processTimedOut = $false
  try {
    try {
      $process.WaitForExitAsync($waitCts.Token).GetAwaiter().GetResult()
    }
    catch [OperationCanceledException] {
      if (-not $process.HasExited) { $process.Kill($true) }
      $process.WaitForExitAsync([Threading.CancellationToken]::None).GetAwaiter().GetResult()
      $processTimedOut = $true
    }
    $stdoutTask.GetAwaiter().GetResult()
    $stderrTask.GetAwaiter().GetResult()
    if ($processTimedOut) {
      throw 'The operator-owned overall readiness timeout expired; child tree was terminated.'
    }
  }
  finally { $waitCts.Dispose(); $waitCts = $null }
  $completedAtUtc = [DateTimeOffset]::UtcNow
  $nativeExitCode = $process.ExitCode
  $stdoutBytes = $stdoutBuffer.ToArray()
  $stderrBytes = $stderrBuffer.ToArray()
  if ($nativeExitCode -ne 0) {
    throw "RCSI readiness child failed closed with native exit $nativeExitCode."
  }
  if ($stderrBytes.Length -ne 0) { throw 'Successful readiness emitted stderr.' }
  if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
    throw 'Exit zero did not produce a fresh evidence file.'
  }

  $fileBytes = [IO.File]::ReadAllBytes($outputPath)
  if ($stdoutBytes.Length -ne $fileBytes.Length -or
      -not [Convert]::ToHexString($stdoutBytes).Equals(
        [Convert]::ToHexString($fileBytes), [StringComparison]::Ordinal)) {
    throw 'Raw stdout bytes and evidence file bytes differ.'
  }
  if ($fileBytes.Length -ge 3 -and $fileBytes[0] -eq 0xEF -and
      $fileBytes[1] -eq 0xBB -and $fileBytes[2] -eq 0xBF) {
    throw 'Canonical evidence must be UTF-8 without BOM.'
  }
  $utf8 = [Text.UTF8Encoding]::new($false, $true)
  $evidence = ($utf8.GetString($fileBytes) | ConvertFrom-Json -ErrorAction Stop)
  $observedAtUtc = [DateTimeOffset]::ParseExact(
    [string]$evidence.observedAtUtc,
    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal -bor
      [Globalization.DateTimeStyles]::AdjustToUniversal)
  $evidenceEndpointHashes = @($evidence.approvedLogicalEndpointSha256)
  $endpointInventoryMatches =
    $evidenceEndpointHashes.Count -eq $approvedLogicalEndpointHashes.Count
  if ($endpointInventoryMatches) {
    for ($index = 0; $index -lt $approvedLogicalEndpointHashes.Count; $index++) {
      if ($evidenceEndpointHashes[$index] -cne $approvedLogicalEndpointHashes[$index]) {
        $endpointInventoryMatches = $false
        break
      }
    }
  }

  if ($evidence.contractVersion -ne 'GOYMEN-ERP-SQL-RCSI-READINESS-V1' -or
      $evidence.status -ne 'READY' -or [int]$evidence.exitCode -ne 0 -or
      $evidence.reasonCode -ne 'RCSI_READY' -or
      $evidence.queryMode -ne 'READ_ONLY_SYS_DATABASES_CURRENT_V1' -or
      $evidence.runId -ne $runId -or $evidence.testedCommit -ne $testedCommit -or
      $evidence.expectedToolAssemblySha256 -ne $expectedToolAssemblySha256 -or
      $evidence.observedToolAssemblySha256 -ne $expectedToolAssemblySha256 -or
      $evidence.runtimePayloadManifestSha256 -ne $expectedPayloadManifestSha256 -or
      $evidence.targetLabel -ne $targetLabel -or
      $evidence.expectedDatabase -ne $expectedDatabase -or
      $evidence.configuredDatabase -ne $expectedDatabase -or
      $evidence.observedDatabase -ne $expectedDatabase -or
      -not $endpointInventoryMatches -or
      $evidence.logicalEndpointMatches -ne $true -or
      $evidence.readCommittedSnapshotEnabled -ne $true -or
      $approvedLogicalEndpointHashes -notcontains $evidence.observedLogicalEndpointSha256 -or
      $observedAtUtc -lt $startedAtUtc -or $observedAtUtc -gt $completedAtUtc) {
    throw 'Evidence fields do not bind the same successful invocation.'
  }
}
finally {
  try {
    try {
      if ($processStarted) {
        try {
          if (-not $process.HasExited) { $process.Kill($true) }
        }
        catch [InvalidOperationException] {
          # The child won the exit race; WaitForExitAsync below still joins it.
        }
        $process.WaitForExitAsync(
          [Threading.CancellationToken]::None).GetAwaiter().GetResult()
      }
      if ($stdoutTask -ne $null) { $stdoutTask.GetAwaiter().GetResult() }
      if ($stderrTask -ne $null) { $stderrTask.GetAwaiter().GetResult() }
    }
    finally {
      try {
        if ($startInfo -ne $null) {
          $startInfo.Environment.Remove($probeEnvName) | Out-Null
        }
      }
      finally {
        [Environment]::SetEnvironmentVariable(
          $probeEnvName, $null, [EnvironmentVariableTarget]::Process)
      }
    }
  }
  finally {
    if ($waitCts -ne $null) { $waitCts.Dispose() }
    if ($stdoutBuffer -ne $null) { $stdoutBuffer.Dispose() }
    if ($stderrBuffer -ne $null) { $stderrBuffer.Dispose() }
    if ($process -ne $null) { $process.Dispose() }
  }
}
```

Any exception or native exit `2`–`6` is a real failed gate. The wrapper never
converts it to success. A new invocation requires a new run ID and a path that
did not previously exist; stale READY evidence is never overwritten.

## RCSI-OFF remediation

The application and CLI never remediate RCSI drift. Exit 4 stops activation.
A DBA must independently confirm the exact database and physical/topology
evidence, backup/restore point, owner, rollback plan, and maintenance window.
Any environment-specific `ALTER DATABASE` command is prepared and peer-reviewed
outside this repository. Enabling RCSI may terminate active connections; that
impact requires explicit operational acceptance. After the human-run change,
restart the candidate and rerun the prebuilt tool with a new run ID/output.

Do not automatically turn RCSI OFF as a deployment rollback. Deployment
rollback and database-option reversal are separate DBA changes.

## Rehearsal evidence

`SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests` uses disposable SQL
Server databases to prove ON readiness, OFF no-change behavior, startup/probe
parity, logical endpoint/database mismatch, real CLI exits 0/4/6, canonical raw
stdout/file parity, prebuilt payload binding, stale-output rejection, and
kill-and-await cleanup. Test-only `ALTER DATABASE` remains confined to tests.
