#requires -Version 7.2
<#
Prepare installs/builds and discovers tests; it executes no test body or browser server.
Verify also runs SDK/helper tests and exactly one controlled Chromium suite.
Use a new invocation after an independently reviewed source delta; no old result is reused.
The script never runs the acceptance tool's lifecycle, live API or SQL entry points.
#>
[CmdletBinding()]
param(
    [ValidateSet('Prepare', 'Verify', 'BackendVerify')][string]$Mode = 'Prepare',
    [Parameter(Mandatory)][string]$OutputParent
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows) { throw 'This test project requires Windows.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not [IO.Path]::IsPathFullyQualified($OutputParent)) { throw 'OutputParent must be absolute.' }
$parent = [IO.Path]::GetFullPath($OutputParent)
if ($parent -eq $repo -or $parent.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep evidence outside the checkout.'
}
if (Test-Path Env:CARI010_RUN_CLAIM) { throw 'Use a fresh process, not an existing harness worker.' }
$runId = 'c02-' + [Guid]::NewGuid().ToString('N')
$session = Join-Path $parent $runId
[IO.Directory]::CreateDirectory($parent) | Out-Null
# New-Item without Force refuses an existing directory; nothing is cleaned across runs.
New-Item -ItemType Directory -Path $session | Out-Null
$logs = New-Item -ItemType Directory -Path (Join-Path $session 'logs')
$cache = Join-Path ([IO.Path]::GetTempPath()) ('goymen-cari-c02-link-cache/' + $runId)
if (Test-Path -LiteralPath $cache) { throw 'A fresh cache is required.' }
$transformCache = Join-Path ([IO.Path]::GetTempPath()) ('goymen-cari-c02-link-transform/' + $runId)
if (Test-Path -LiteralPath $transformCache) { throw 'A fresh transform cache is required.' }
$steps = [Collections.Generic.List[object]]::new()
$summary = [ordered]@{
    mode = $Mode; run_id = $runId; started_at = [DateTimeOffset]::UtcNow.ToString('o')
    evidence = $session; cache = $cache; transform_cache = $transformCache; status = 'in_progress'
    backend_discovery_entries = 0; backend_executed = 0; chromium_discovered = 0; chromium_executed = 0
    helpers_executed = 0; source_before = $null; source_after = $null
    build_artifacts = @(); publish_ready = $false; erp_accepted = $false
}
function Invoke-Step([string]$Name, [string]$Executable, [string[]]$CommandArguments) {
    $log = Join-Path $logs.FullName ($Name + '.log')
    $started = [DateTimeOffset]::UtcNow.ToString('o')
    & $Executable @CommandArguments *> $log
    $code = $LASTEXITCODE
    $steps.Add([ordered]@{ name = $Name; executable = $Executable; arguments = $CommandArguments
        started_at = $started; finished_at = [DateTimeOffset]::UtcNow.ToString('o'); exit_code = $code; log = $log })
    Write-Host "$Name EXIT=$code"
    if ($code -ne 0) { throw "$Name failed (EXIT=$code); preserved log: $log" }
}
function Assert-CompleteTrx([xml]$Trx, [string[]]$DiscoveryEntries) {
    $counters = $Trx.TestRun.ResultSummary.Counters
    $total = [int]$counters.total
    # xUnit can defer non-serializable MemberData until execution: one discovery entry
    # may produce multiple TRX cases. Require all listed methods and all executed rows.
    $expectedMethods = @($DiscoveryEntries | ForEach-Object { $_.Trim().Split('(')[0] } | Sort-Object -Unique)
    $actualMethods = @($Trx.TestRun.TestDefinitions.UnitTest | ForEach-Object {
        $_.TestMethod.className + '.' + $_.TestMethod.name
    } | Sort-Object -Unique)
    $results = @($Trx.TestRun.Results.UnitTestResult)
    if ($total -lt $DiscoveryEntries.Count -or $expectedMethods.Count -eq 0 -or
        @($expectedMethods | Where-Object { $_ -notin $actualMethods }).Count -ne 0 -or
        $total -ne $results.Count -or @($results | Where-Object { $_.outcome -ne 'Passed' }).Count -ne 0 -or
        [int]$counters.passed -ne $total -or [int]$counters.executed -ne $total -or
        [int]$counters.notExecuted -ne 0 -or [int]$counters.failed -ne 0) {
        throw 'TRX must cover every discovered method and pass every execution row without skips.'
    }
    return $total
}
function Assert-NoUntrackedSources([string]$Phase) {
    Invoke-Step "source-untracked-$Phase" 'git' @('ls-files', '--others', '--exclude-standard', '--')
    if (-not [string]::IsNullOrWhiteSpace((Get-Content (Join-Path $logs.FullName "source-untracked-$Phase.log") -Raw))) {
        throw 'C006 verification refuses untracked source files.'
    }
    # Match SourceBinding: ignored source files still enter SDK builds; bin/obj do not.
    Invoke-Step "source-build-inputs-$Phase" 'git' @('ls-files', '--others', '--',
        'src/backend', 'tools/GoymenERP.CariAcceptance006', 'tests/GoymenERP.CariAcceptance006.Tests',
        ':(top,glob)*.props', ':(top,glob)*.targets', ':(top,glob)*.rsp', 'global.json', 'NuGet.Config',
        ':(exclude,glob)**/bin/**', ':(exclude,glob)**/obj/**')
    if (-not [string]::IsNullOrWhiteSpace((Get-Content (Join-Path $logs.FullName "source-build-inputs-$Phase.log") -Raw))) {
        throw 'C006 verification refuses untracked backend build inputs, including ignored sources.'
    }
}
function Get-C006TestBuildArtifacts {
    $directory = 'tests/GoymenERP.CariAcceptance006.Tests/bin/Release/net10.0-windows'
    $files = @(Get-ChildItem -LiteralPath $directory -Filter 'GoymenERP*.dll' -File | Sort-Object Name)
    if ($files.Count -eq 0) { throw 'The executed test output has no ERP assemblies.' }
    foreach ($file in $files) {
        [ordered]@{ path = "$directory/$($file.Name)"; bytes = $file.Length;
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}
$project = 'tests/GoymenERP.CariAcceptance006.Tests/GoymenERP.CariAcceptance006.Tests.csproj'
$frontend = 'src/frontend/goymen-erp-web'
$playwright = 'tests/e2e/node_modules/@playwright/test/cli.js'
$config = 'tests/e2e/cari-acceptance-010/playwright.config.ts'
$helpers = @('tests/e2e/cari-acceptance-009/gate-observations.test.mjs',
    'tests/e2e/cari-acceptance-009/verify-manifest.test.mjs',
    'tests/e2e/cari-acceptance-009/scan-evidence.test.mjs',
    'tests/e2e/cari-acceptance-010/run-ownership.test.mjs')
$identityCode = "import {gitHead,gitDiffSha256} from './tests/e2e/cari-acceptance-009/gate-observations.mjs'; console.log(JSON.stringify({head:gitHead(process.cwd()),diff:gitDiffSha256(process.cwd())}));"
Push-Location -LiteralPath $repo
$exitCode = 0
try {
    if ($Mode -eq 'BackendVerify') {
        # Backend-only candidate; full Verify keeps its helper/Chromium inventory.
        Invoke-Step 'dotnet-version' 'dotnet' @('--version')
        Invoke-Step 'source-before' 'git' @('rev-parse', 'HEAD')
        $head = (Get-Content (Join-Path $logs.FullName 'source-before.log') -Raw).Trim()
        Invoke-Step 'source-diff-before' 'git' @('diff', '--exit-code', '--name-only', 'HEAD', '--')
        Assert-NoUntrackedSources 'before'
        $summary.source_before = @{ head = $head }
        $summary.chromium_status = 'NOT_RUN'
        $summary.helpers_status = 'NOT_RUN'
        Invoke-Step 'backend-restore' 'dotnet' @('restore', $project, '-noAutoResponse', '--configfile', 'NuGet.Config', '--disable-parallel')
        Invoke-Step 'backend-build' 'dotnet' @('build', $project, '-noAutoResponse', '--configuration', 'Release',
            '--no-restore', '--disable-build-servers', "-p:SourceRevisionId=$head")
        $testedBuild = @(Get-C006TestBuildArtifacts)
        Invoke-Step 'backend-discovery' 'dotnet' @('test', $project, '-noAutoResponse', '--configuration', 'Release',
            '--no-build', '--no-restore', '--list-tests')
        $discovered = @(Get-Content (Join-Path $logs.FullName 'backend-discovery.log') | Where-Object {
            $_ -match '^\s+(?:GoymenERP\.CariAcceptance006\.Tests\.(?:ProfileTests|LifecycleTests|SourceBindingTests)|ResumeRecordTests)\.'
        })
        if ($discovered.Count -eq 0) { throw 'No C006 SDK tests discovered.' }
        $summary.backend_discovery_entries = $discovered.Count
        $trxDir = Join-Path $session 'backend-results'
        Invoke-Step 'backend-tests' 'dotnet' @('test', $project, '-noAutoResponse', '--configuration', 'Release',
            '--no-build', '--no-restore', '--logger', 'trx;LogFileName=cari-c02-link.trx', '--results-directory', $trxDir)
        [xml]$trx = Get-Content (Join-Path $trxDir 'cari-c02-link.trx') -Raw
        $summary.backend_executed = Assert-CompleteTrx $trx $discovered
        Invoke-Step 'python-version' 'python' @('--version')
        Invoke-Step 'publisher-tests' 'python' @('-I', '-S', '-B', '-m', 'unittest', 'discover',
            '-s', 'tests/GoymenERP.CariAcceptance006.Tests', '-p', 'test_publisher.py', '-v')
        $publisherLog = Get-Content (Join-Path $logs.FullName 'publisher-tests.log') -Raw
        if ($publisherLog -notmatch '(?m)^Ran (\d+) tests? in ' -or [int]$Matches[1] -le 0) {
            throw 'No publisher contract tests executed.'
        }
        $summary.publisher_executed = [int]$Matches[1]
        if ($publisherLog -notmatch '(?m)^OK\r?$') { throw 'Publisher tests must pass without skips.' }
        $summary.publisher_status = 'passed'
        $observedBuild = @(Get-C006TestBuildArtifacts)
        if (($testedBuild | ConvertTo-Json -Depth 5 -Compress) -cne ($observedBuild | ConvertTo-Json -Depth 5 -Compress)) {
            throw 'Executed ERP build artifacts changed during verification.'
        }
        $summary.build_artifacts = $testedBuild
        Invoke-Step 'source-after' 'git' @('rev-parse', 'HEAD')
        Invoke-Step 'source-diff-after' 'git' @('diff', '--exit-code', '--name-only', 'HEAD', '--')
        Assert-NoUntrackedSources 'after'
        $after = (Get-Content (Join-Path $logs.FullName 'source-after.log') -Raw).Trim()
        if ($head -ne $after) { throw 'Source HEAD changed during C006 verification.' }
        $summary.source_after = @{ head = $after }
        $summary.status = 'backend_checks_passed_not_ERP_acceptance'
    } else {
    Invoke-Step 'node-version' 'node' @('--version')
    Invoke-Step 'dotnet-version' 'dotnet' @('--version')
    Invoke-Step 'source-before' 'node' @('--input-type=module', '-e', $identityCode)
    $summary.source_before = Get-Content (Join-Path $logs.FullName 'source-before.log') -Raw | ConvertFrom-Json
    Invoke-Step 'frontend-install' 'npm.cmd' @('ci', '--prefix', $frontend, '--no-audit', '--no-fund')
    Invoke-Step 'e2e-install' 'npm.cmd' @('ci', '--prefix', 'tests/e2e', '--no-audit', '--no-fund')
    Invoke-Step 'backend-restore' 'dotnet' @('restore', $project, '-noAutoResponse', '--configfile', 'NuGet.Config', '--disable-parallel')
    Invoke-Step 'backend-build' 'dotnet' @('build', $project, '-noAutoResponse', '--configuration', 'Release', '--no-restore', '--disable-build-servers')
    foreach ($artifact in @('tests/GoymenERP.CariAcceptance006.Tests/bin/Release/net10.0-windows/GoymenERP.CariAcceptance006.Tests.dll',
        'tools/GoymenERP.CariAcceptance006/bin/Release/net10.0-windows/GoymenERP.CariAcceptance006.dll')) {
        $summary.build_artifacts += @{ path = $artifact; bytes = (Get-Item $artifact).Length; sha256 = (Get-FileHash $artifact -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    Invoke-Step 'backend-discovery' 'dotnet' @('test', $project, '-noAutoResponse', '--configuration', 'Release', '--no-build', '--no-restore', '--list-tests')
    $discovered = @(Get-Content (Join-Path $logs.FullName 'backend-discovery.log') | Where-Object {
        $_ -match '^\s+(?:GoymenERP\.CariAcceptance006\.Tests\.(?:ProfileTests|LifecycleTests|SourceBindingTests)|ResumeRecordTests)\.'
    })
    $summary.backend_discovery_entries = $discovered.Count
    if ($discovered.Count -eq 0) { throw 'No SDK tests discovered.' }
    Invoke-Step 'typecheck-010' 'node' @("$frontend/node_modules/typescript/bin/tsc", '--noEmit', '-p', 'tests/e2e/cari-acceptance-010/tsconfig.json')
    Invoke-Step 'lint-010' 'node' @("$frontend/node_modules/oxlint/bin/oxlint", 'tests/e2e/cari-acceptance-010')
    for ($index = 0; $index -lt $helpers.Count; $index++) {
        Invoke-Step "helper-syntax-$index" 'node' @('--check', $helpers[$index])
    }
    $env:CARI010_EVIDENCE_DIR = $session
    $env:CARI010_RUN_ID = $runId
    $env:CARI010_VITE_CACHE_DIR = $cache
    $env:PWTEST_CACHE_DIR = $transformCache
    if ($Mode -eq 'Prepare') {
        # --list loads the config and claims discovery-only paths, but starts no webServer/globalSetup.
        Invoke-Step 'chromium-discovery' 'node' @($playwright, 'test', '--config', $config, '--list', '--reporter=list')
        $listing = Get-Content (Join-Path $logs.FullName 'chromium-discovery.log') -Raw
        if ($listing -notmatch 'Total: (\d+) tests? in') { throw 'Missing Chromium discovery summary.' }
        $summary.chromium_discovered = [int]$Matches[1]
        if ($summary.chromium_discovered -ne 17) { throw 'Expected the 17 controlled scenarios.' }
    } else {
        # No filter, skip, retry or live lifecycle invocation. TRX, not discovery, proves execution.
        $trxDir = Join-Path $session 'backend-results'
        Invoke-Step 'backend-tests' 'dotnet' @('test', $project, '-noAutoResponse', '--configuration', 'Release', '--no-build', '--no-restore',
            '--logger', 'trx;LogFileName=cari-c02-link.trx', '--results-directory', $trxDir)
        [xml]$trx = Get-Content (Join-Path $trxDir 'cari-c02-link.trx') -Raw
        $summary.backend_executed = Assert-CompleteTrx $trx $discovered
        Invoke-Step 'helper-tests' 'node' (@('--test', '--test-concurrency=1', '--test-reporter=tap') + $helpers)
        $tap = Get-Content (Join-Path $logs.FullName 'helper-tests.log') -Raw
        if ($tap -notmatch '(?m)^# tests (\d+)\s*$') { throw 'Missing helper totals.' }
        $summary.helpers_executed = [int]$Matches[1]
        if ($summary.helpers_executed -eq 0 -or $tap -notmatch '(?m)^# fail 0\s*$' -or $tap -notmatch '(?m)^# skipped 0\s*$') {
            throw 'Helpers must pass without skips.'
        }
        Invoke-Step 'chromium-install' 'node' @($playwright, 'install', 'chromium')
        Invoke-Step 'chromium-tests' 'node' @($playwright, 'test', '--config', $config)
        $browser = Get-Content (Join-Path $session "runs/$runId/tests.json") -Raw | ConvertFrom-Json
        $summary.chromium_executed = $browser.tests.Count
        if ($browser.status -ne 'passed' -or $browser.passed -ne 17 -or $browser.failed -ne 0 -or
            $browser.globalErrors -ne 0 -or $browser.tests.Count -ne 17) { throw 'Controlled Chromium must pass all 17 scenarios.' }
    }
    Invoke-Step 'source-after' 'node' @('--input-type=module', '-e', $identityCode)
    $summary.source_after = Get-Content (Join-Path $logs.FullName 'source-after.log') -Raw | ConvertFrom-Json
    if ($summary.source_before.head -ne $summary.source_after.head -or $summary.source_before.diff.sha256 -ne $summary.source_after.diff.sha256) {
        throw 'Source changed during this invocation.'
    }
    $summary.status = if ($Mode -eq 'Prepare') { 'prepared_not_executed' } else { 'local_checks_passed_not_ERP_acceptance' }
    }
} catch {
    $summary.status = 'failed'
    $summary.failure = $_.Exception.Message
    $exitCode = 1
    Write-Host $summary.failure
} finally {
    $summary.finished_at = [DateTimeOffset]::UtcNow.ToString('o')
    $summary.steps = $steps.ToArray()
    $temporary = Join-Path $session 'summary.json.tmp'
    $summary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $temporary -Encoding utf8NoBOM
    [IO.File]::Move($temporary, (Join-Path $session 'summary.json'))
    Pop-Location
    Write-Host "Evidence: $session"
}
exit $exitCode
