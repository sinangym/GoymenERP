param(
    [Parameter(Mandatory=$true)][ValidateSet('start','smoke','snapshot','verify','verify-contact-edit','stop','release-check','teardown')][string]$Action,
    [ValidateRange(1,100)][int]$ExpectedNewParties=1,
    [string]$BrowserMeasurement,
    [switch]$ResumeC012
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($root -ne 'C:\src\GoymenERP-cari-party-card-backend-20260912') { throw 'C006 wrong worktree.' }
$evidence='C:\src\GoymenERP-cari-party-card-backend-20260912\artifacts\cari-acceptance-006\contact-edit-20260912\c006-406abcfedbec'
$run=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'run.json') -Raw | ConvertFrom-Json
$resources=Get-Content -LiteralPath (Join-Path $evidence 'resources.json') -Raw | ConvertFrom-Json
if ($ResumeC012) {
    if ($Action -notin @('start','snapshot','verify','stop')) { throw 'C006 resume supports only start, snapshot, verify and stop; teardown and the original empty-DB smoke stay with the C006 owner.' }
    $evidence='C:\src\GoymenERP-cari-party-card-backend-20260912\artifacts\cari-acceptance-006\contact-edit-20260912\c006-406abcfedbec\resume'
    $execution=Get-Content -LiteralPath (Join-Path $evidence 'started.json') -Raw | ConvertFrom-Json
    if ($execution.task_id -ne 'C-DESKTOP-012' -or $execution.resourceRunId -ne $run.RunId -or $execution.instanceId -ne $run.InstanceId -or $execution.status -ne 'in_progress' -or $execution.executionRunId -cnotmatch '\Ac012-[0-9a-f]{12}\z') { throw 'C006 resume record identity mismatch.' }
}
if ($run.RunId -ne 'c006-406abcfedbec' -or $run.InstanceId -ne '16a207bd-f437-4960-a151-1f5c1137b180' -or $resources.instanceId -ne $run.InstanceId) { throw 'C006 identity mismatch.' }
$container=& docker inspect --format '{"id":{{json .Id}},"labels":{{json .Config.Labels}}}' $run.ContainerName | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $container.id -ne $resources.container.id -or $container.labels.'com.goymen.acceptance.instance' -ne $run.InstanceId) { throw 'C006 container ownership mismatch.' }
$dll=Join-Path $root 'tools/GoymenERP.CariAcceptance006/bin/Release/net10.0-windows/GoymenERP.CariAcceptance006.dll'
if ($Action -eq 'start') {
    if ($ResumeC012) {
        foreach ($name in @('ENV_READY.json','report.json','host.stdout.log','host.stderr.log')) {
            if (Test-Path -LiteralPath (Join-Path $evidence $name)) { throw 'C006 resume already attempted or released; preserve evidence.' }
        }
    }
    if (Test-Path -LiteralPath (Join-Path $evidence 'host-started.json')) { throw 'C006 host already started; no duplicate.' }
    if (@(Get-NetTCPConnection -State Listen | Where-Object {$_.LocalPort -eq $run.ApiPort}).Count) { throw 'C006 API port occupied; preserve existing process.' }
    $serveAction=if ($ResumeC012) { 'serve-resume' } else { 'serve' }
    $process=Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @($dll,$serveAction) -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'host.stdout.log') -RedirectStandardError (Join-Path $evidence 'host.stderr.log') -PassThru
    # A launch is not a started host. The tested acceptance tool waits bounded for the child's
    # own startup record, then requires that exact process to be alive; the API port owner is
    # confirmed here. A failed launch ends this command with a non-zero exit code.
    $checkAction=if ($ResumeC012) { 'start-check-resume' } else { 'start-check' }
    & 'C:\Program Files\dotnet\dotnet.exe' $dll $checkAction $process.Id
    $checkExit=$LASTEXITCODE
    $owned=@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object {$_.LocalPort -eq $run.ApiPort -and $_.OwningProcess -eq $process.Id}).Count
    $started=($checkExit -eq 0) -and ($owned -ge 1)
    [pscustomobject]@{pid=$process.Id;startTimeUtc=$process.StartTime.ToUniversalTime().ToString('o');started=$started;startCheckExit=$checkExit;apiPortListenersOwnedByLaunchedProcess=$owned;meaning='Recorded identity, live process and owned API port were verified; full readiness still requires health and smoke.'}|ConvertTo-Json
    if (-not $started) { throw 'C006 launch did not become a verified host; nothing was stopped or removed. Read host.stderr.log and use -Action stop for a stale recorded host.' }
    exit 0
}
if ($Action -eq 'stop') {
    # Ownership, process id and start-time identity are decided by the tested acceptance tool:
    # only a process recorded by this environment and still matching its record is stopped, a
    # second stop finds nothing to stop, and a reused process id is refused instead of killed.
    $stopAction=if ($ResumeC012) { 'stop-resume' } else { 'stop' }
    & 'C:\Program Files\dotnet\dotnet.exe' $dll $stopAction
    exit $LASTEXITCODE
}
if ($Action -eq 'release-check') {
    & 'C:\Program Files\dotnet\dotnet.exe' $dll release-check
    exit $LASTEXITCODE
}
if ($Action -eq 'teardown') {
    # Every recorded host of this environment (C006 and the C012 resume) must already be gone;
    # this action never stops a process and removes nothing while one is still running.
    & 'C:\Program Files\dotnet\dotnet.exe' $dll release-check
    if ($LASTEXITCODE -ne 0) { throw 'C006 a recorded host is still running; run -Action stop (with -ResumeC012 for a resumed host) first. Nothing removed.' }
    $volume=& docker volume inspect --format '{"name":{{json .Name}},"labels":{{json .Labels}}}' $run.VolumeName | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $volume.name -ne $resources.volume.name -or $volume.labels.'com.goymen.acceptance.instance' -ne $run.InstanceId) { throw 'C006 volume ownership mismatch; nothing removed.' }
    $network=& docker network inspect --format '{"id":{{json .Id}},"labels":{{json .Labels}},"containers":{{json .Containers}}}' $resources.network.id | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $network.id -ne $resources.network.id -or $network.labels.'com.goymen.acceptance.instance' -ne $run.InstanceId -or @($network.containers.PSObject.Properties | Where-Object {$_.Name -ne $resources.container.id}).Count) { throw 'C006 network ownership mismatch; nothing removed.' }
    $private=[IO.Path]::GetFullPath((Join-Path $root 'secrets/cari-acceptance-006/password.dpapi'))
    if ($private -ne 'C:\src\GoymenERP-cari-party-card-backend-20260912\secrets\cari-acceptance-006\password.dpapi') { throw 'C006 private path mismatch.' }
    & docker rm --force $resources.container.id
    if ($LASTEXITCODE -ne 0) { throw 'C006 owned container removal failed.' }
    & docker volume rm $resources.volume.name
    if ($LASTEXITCODE -ne 0) { throw 'C006 owned volume removal failed.' }
    & docker network rm $resources.network.id
    if ($LASTEXITCODE -ne 0) { throw 'C006 owned network removal failed.' }
    if (Test-Path -LiteralPath $private) { Remove-Item -LiteralPath $private }
    Write-Output 'Only the recorded C006 container/volume/network and its new private password file were removed; the recorded API hosts were already stopped. No Docker Desktop shutdown.'
    exit 0
}
if ($Action -eq 'verify-contact-edit') {
    if ([string]::IsNullOrWhiteSpace($BrowserMeasurement)) { throw 'C006 actual browser measurement path is required.' }
    & 'C:\Program Files\dotnet\dotnet.exe' $dll verify-contact-edit ([IO.Path]::GetFullPath($BrowserMeasurement))
}
elseif ($Action -eq 'verify') {
    $verifyAction=if ($ResumeC012) { 'verify-resume' } else { 'verify' }
    & 'C:\Program Files\dotnet\dotnet.exe' $dll $verifyAction $ExpectedNewParties
}
else { & 'C:\Program Files\dotnet\dotnet.exe' $dll $Action }
exit $LASTEXITCODE
