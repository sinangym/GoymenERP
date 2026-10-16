#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('guards','catalog','frontend','full')][string]$Profile='guards',
    [string]$LinuxRepository='/home/ci/GoymenERP-local-ci-source',
    [switch]$PlanOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
if($env:COMPUTERNAME -ne 'DESKTOP-11CDRHD'){throw 'This launcher is bound to DESKTOP-11CDRHD.'}
if($LinuxRepository -notmatch '^/home/ci/[A-Za-z0-9_./-]+$' -or $LinuxRepository.Contains('..')){throw 'Unexpected Linux repository path.'}
$root='E:\GoymenERP-CI'
$runId='launcher-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$runDir=Join-Path $root ('evidence\'+$runId)
New-Item -ItemType Directory -Path $runDir | Out-Null
$started=[DateTime]::UtcNow.ToString('o')
$availableBytes=[long](Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory*1024
$record=[ordered]@{runId=$runId;computer=$env:COMPUTERNAME;profile=$Profile;repository=$LinuxRepository;
 startUtc=$started;availableMemoryBytes=$availableBytes;status='RUNNING';githubJobStarted=$false;githubStorageUsed=$false;
 command=@('wsl','-d','GoymenERP-CI','-u','ci','--','python3','-B',($LinuxRepository+'/tools/ci/local_check.py'),'--profile',$Profile)}
$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDir 'launcher-start.json') -Encoding utf8
$lock=$null
$lease=$null
$leaseFile=$null
$code=2
try {
 $lock=[IO.File]::Open((Join-Path $root 'local-heavy.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 if(($Profile -eq 'frontend' -or $Profile -eq 'full') -and -not $PlanOnly -and $availableBytes -lt 2GB){
  $record.status='BLOCKED_RESOURCE'
  $record.reason='Less than 2 GiB free physical memory; no heavy test was started.'
 } else {
  $path='/opt/goymen-ci/tools/dotnet-10.0.303:/opt/goymen-ci/tools/node-v24.20.0-linux-x64/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin'
  $expected=(wsl.exe -d GoymenERP-CI -u ci -- /usr/bin/env -i PATH=/usr/bin:/bin HOME=/home/ci /usr/bin/git -C $LinuxRepository rev-parse HEAD).Trim()
  if($LASTEXITCODE -ne 0 -or $expected -notmatch '^[0-9a-f]{40}$'){throw 'Cannot bind the expected source commit.'}
  $nonce=[Guid]::NewGuid().ToString('N')
  $linuxLease='/home/ci/local-ci-leases/lease-'+$nonce+'.json'
  wsl.exe -d GoymenERP-CI -u ci -- /usr/bin/mkdir -p /home/ci/local-ci-leases
  if($LASTEXITCODE -ne 0){throw 'Cannot create local lease directory.'}
  $leaseFile='\\wsl.localhost\GoymenERP-CI'+$linuxLease.Replace('/','\')
  $lease=[ordered]@{host='DESKTOP-11CDRHD';status='ACTIVE';repository=$LinuxRepository;expectedHead=$expected;
   profile=$Profile;nonce=$nonce;availableMemoryBytes=$availableBytes;createdUtc=$started;expiresUtc=[DateTime]::UtcNow.AddHours(1).ToString('o')}
  [IO.File]::WriteAllText($leaseFile,($lease|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
  wsl.exe -d GoymenERP-CI -u ci -- /usr/bin/chmod 600 $linuxLease
  if($LASTEXITCODE -ne 0){throw 'Cannot protect local lease file.'}
  $arguments=@('-d' ,'GoymenERP-CI','-u','ci','--','/usr/bin/env','-i',('PATH='+$path),'HOME=/home/ci',
   'DOTNET_ROOT=/opt/goymen-ci/tools/dotnet-10.0.303','DOTNET_CLI_HOME=/home/ci/.dotnet',
   'NUGET_PACKAGES=/home/ci/.cache/nuget/packages','NPM_CONFIG_CACHE=/home/ci/.cache/npm',
   '/usr/bin/python3','-I','-B',($LinuxRepository+'/tools/ci/local_check.py'),'--repository',$LinuxRepository,'--expected-head',$expected,'--launcher-lease',$linuxLease,
   '--profile',$Profile,'--evidence-root','/home/ci/local-ci-evidence')
  if($PlanOnly){$arguments+='--plan'}
  $record.command=@('wsl.exe')+$arguments
  $record.expectedHead=$expected
  & wsl.exe @arguments *>&1 | Tee-Object -FilePath (Join-Path $runDir 'console.log')
  $code=$LASTEXITCODE
  $line=Get-Content -LiteralPath (Join-Path $runDir 'console.log') | Where-Object {$_ -match '^LOCAL_EVIDENCE='} | Select-Object -Last 1
  if(-not $line){throw 'Local run produced no closed evidence directory.'}
  $linuxEvidence=$line.Substring('LOCAL_EVIDENCE='.Length)
  if($linuxEvidence -notmatch '^/home/ci/local-ci-evidence/local-[A-Za-z0-9TZ-]+$'){throw 'Unexpected local evidence path.'}
  $source='\\wsl.localhost\GoymenERP-CI'+$linuxEvidence.Replace('/','\')
  $manifest=Get-Content -LiteralPath (Join-Path $source 'SHA256_MANIFEST.json') -Raw | ConvertFrom-Json
  $bundle=Join-Path $runDir 'bundle'
  New-Item -ItemType Directory -Path $bundle | Out-Null
  foreach($entry in $manifest){
   if($entry.path -notmatch '^[A-Za-z0-9_.-]+$'){throw 'Evidence manifest path is not a flat regular file.'}
   $file=Get-Item -LiteralPath (Join-Path $source $entry.path)
   if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $file.PSIsContainer){throw 'Symlink/directory evidence rejected.'}
   $observed=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
   if($file.Length -ne $entry.bytes -or $observed -cne $entry.sha256){throw 'Linux evidence hash/size mismatch.'}
   $destination=Join-Path $bundle $entry.path
   Copy-Item -LiteralPath $file.FullName -Destination $destination
   if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.sha256){throw 'Windows evidence hash mismatch.'}
  }
  Copy-Item -LiteralPath (Join-Path $source 'SHA256_MANIFEST.json') -Destination $bundle
  $result=Get-Content -LiteralPath (Join-Path $bundle 'result.json') -Raw | ConvertFrom-Json
  if($result.exitCode -ne $code){throw 'Launcher/native exit code mismatch.'}
  $record.status=$result.status
  $record.bundle=$bundle
  $record.source=$result.source
 }
} catch {
 $record.status='BLOCKED'
 $record.reason=$_.Exception.Message
 $code=2
} finally {
 if($leaseFile -and $lease){
  $lease.status='CLOSED'
  [IO.File]::WriteAllText($leaseFile,($lease|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
 }
 if($lock){$lock.Dispose()}
 $record.finishUtc=[DateTime]::UtcNow.ToString('o')
 $record.exitCode=$code
 $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDir 'launcher-result.json') -Encoding utf8
 Write-Output ('WINDOWS_EVIDENCE='+$runDir)
 Write-Output ('LOCAL_RESULT='+$record.status)
}
exit $code
