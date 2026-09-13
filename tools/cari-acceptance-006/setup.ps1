param([switch]$Product)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$toolPrefix=@()
$stateRoot=$PSScriptRoot
if ($Product) {
    if ($root -ne 'C:\src\GoymenERP-product-owner-boundary-20260912') { throw 'Product unexpected worktree.' }
    $dll=Join-Path $root 'tools/GoymenERP.CariAcceptance006/bin/Release/net10.0-windows/GoymenERP.CariAcceptance006.dll'
    $run=(& dotnet $dll product-env profile) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Product accepted profile validation failed.' }
    $toolPrefix=@('product-env')
    $evidence=$run.EvidenceRoot
    $stateRoot=$evidence
} else {
$expectedRoot = 'C:\src\GoymenERP-cari-acceptance-envelope-20260911'
if ($root -ne $expectedRoot) { throw 'C006 unexpected worktree.' }
$run = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'run.json') -Raw | ConvertFrom-Json
if ($run.RunId -ne 'c006-5b7b30940e9a' -or $run.InstanceId -ne '1bd0f50d-f882-45b6-8d24-175f032ef1d5') { throw 'C006 unexpected run.' }
$evidence = 'C:\src\GoymenERP-cari-acceptance-envelope-20260911\artifacts\cari-acceptance-006\account-status-20260913\c006-5b7b30940e9a'
$stateRoot=$evidence
}
if (Test-Path -LiteralPath (Join-Path $evidence 'resources.json')) { throw 'C006 resources already recorded; no duplicate setup.' }
$dll=Join-Path $root 'tools/GoymenERP.CariAcceptance006/bin/Release/net10.0-windows/GoymenERP.CariAcceptance006.dll'
& dotnet $dll @toolPrefix source-check
if ($LASTEXITCODE -ne 0) { throw 'C006 source binding failed before resource creation.' }
if (Test-Path -LiteralPath (Join-Path $stateRoot 'compose.json')) { throw 'C006 previous compose attempt is preserved.' }
if (@(Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -in @($run.SqlPort,$run.ApiPort) }).Count) { throw 'C006 port occupied; preserve listener and allocate another port explicitly.' }
$prior = & docker ps -a --filter ('name=^/' + $run.ContainerName + '$') --format '{{.ID}}'
if ($LASTEXITCODE -ne 0 -or $prior) { throw 'C006 container is not new.' }
$priorVolume = & docker volume ls --filter ('name=^' + $run.VolumeName + '$') --format '{{.Name}}'
if ($LASTEXITCODE -ne 0 -or $priorVolume) { throw 'C006 volume is not new.' }
$labels = [ordered]@{'com.goymen.acceptance.task'=$run.TaskId;'com.goymen.acceptance.run'=$run.RunId;'com.goymen.acceptance.instance'=$run.InstanceId;'com.goymen.acceptance.contract'=$run.ContractId}
$compose = [ordered]@{
    name=$run.ComposeProject
    services=@{sql=@{
        image=$run.SqlImage;container_name=$run.ContainerName
        ports=@('127.0.0.1:'+$run.SqlPort+':1433')
        environment=@{ACCEPT_EULA='Y';MSSQL_PID='Developer';MSSQL_MEMORY_LIMIT_MB='2048';MSSQL_SA_PASSWORD='${C006_SQL_PASSWORD}'}
        volumes=@('data:/var/opt/mssql');labels=$labels;restart='no';mem_limit='3g'
    }}
    volumes=@{data=@{name=$run.VolumeName;labels=$labels}}
    networks=@{default=@{name=($run.ComposeProject+'-network');labels=$labels}}
}
$encoding = [Text.UTF8Encoding]::new($false)
if ($Product) { New-Item -ItemType Directory -Path $evidence -Force | Out-Null }
$composePath = Join-Path $stateRoot 'compose.json'
[IO.File]::WriteAllText($composePath,($compose|ConvertTo-Json -Depth 8),$encoding)
$private = if ($Product) { $run.PrivateRoot } else { Join-Path $root ('secrets/cari-acceptance-006/'+$run.RunId) }
if (Test-Path -LiteralPath $private) { throw 'C006 private state already exists; no overwrite.' }
New-Item -ItemType Directory -Path $private -Force | Out-Null
$password = 'Aa!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$plain = [Text.Encoding]::UTF8.GetBytes($password)
try {
    $protected = [Security.Cryptography.ProtectedData]::Protect($plain,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes((Join-Path $private 'password.dpapi'),$protected)
} finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($plain) }
$started = [DateTime]::UtcNow.ToString('o')
try {
    $env:C006_SQL_PASSWORD = $password
    $output = & docker compose --project-name $run.ComposeProject --file $composePath up --detach 2>&1
    $exitCode = $LASTEXITCODE
} finally {
    Remove-Item -LiteralPath 'Env:C006_SQL_PASSWORD' -ErrorAction SilentlyContinue
    $password = $null
}
$log = "START=$started`nCOMMAND=docker compose (C006 isolated profile) up --detach`n$($output -join "`n")`nEND=$([DateTime]::UtcNow.ToString('o'))`nEXIT=$exitCode`n"
[IO.File]::WriteAllText((Join-Path $evidence '10-sql-setup.log'),$log,$encoding)
if ($exitCode -ne 0) { throw 'C006 isolated compose startup failed; see sanitized setup log.' }
$container = & docker inspect --format '{"id":{{json .Id}},"created":{{json .Created}},"image":{{json .Image}},"labels":{{json .Config.Labels}},"ports":{{json .NetworkSettings.Ports}}}' $run.ContainerName | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $container.labels.'com.goymen.acceptance.instance' -ne $run.InstanceId) { throw 'C006 container ownership mismatch.' }
$volume = & docker volume inspect --format '{"name":{{json .Name}},"labels":{{json .Labels}}}' $run.VolumeName | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $volume.labels.'com.goymen.acceptance.instance' -ne $run.InstanceId) { throw 'C006 volume ownership mismatch.' }
$network = & docker network inspect --format '{"id":{{json .Id}},"name":{{json .Name}},"labels":{{json .Labels}}}' ($run.ComposeProject+'-network') | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $network.labels.'com.goymen.acceptance.instance' -ne $run.InstanceId) { throw 'C006 network ownership mismatch.' }
$resources=[ordered]@{recordedAtUtc=[DateTime]::UtcNow.ToString('o');runId=$run.RunId;instanceId=$run.InstanceId;composeProject=$run.ComposeProject;databaseName=$run.DatabaseName;container=$container;volume=$volume;network=$network}
[IO.File]::WriteAllText((Join-Path $evidence 'resources.json'),($resources|ConvertTo-Json -Depth 8),$encoding)
Write-Output 'C006 isolated SQL container created; no existing database or volume reused.'

& dotnet $dll @toolPrefix wait-sql
if ($LASTEXITCODE -ne 0) { throw 'C006 SQL readiness failed; preserve the recorded resources for owned cleanup.' }
