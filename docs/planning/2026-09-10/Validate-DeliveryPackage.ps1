param([switch]$SkipSelfTests)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$packageRoot = $PSScriptRoot

function Read-Json([string]$Path) {
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Copy-Json($Value) {
    return $Value | ConvertTo-Json -Depth 40 | ConvertFrom-Json
}

function Get-ValidationErrors($Inventory, $Mapping) {
    $errors = [System.Collections.Generic.List[string]]::new()
    if ($Inventory.status -cne 'PROPOSAL_NOT_EXECUTION_AUTHORITY' -or
        $Mapping.status -cne 'PROPOSAL_NOT_EXECUTION_AUTHORITY') {
        $errors.Add('Proposal status must not imply execution authority.')
    }
    if ($Inventory.base_sha -cnotmatch '^[0-9a-f]{40}$') { $errors.Add('Invalid base SHA.') }
    $byId = @{}
    $allowedReadiness = @('SPEC_REQUIRED', 'EXISTING_CANDIDATE_RECONCILE', 'INPUT_GATED', 'DEFERRED')
    $usedSlices = @{}
    foreach ($item in $Inventory.packages) {
        foreach ($field in @('id','title','outcome','owners','existing_slices','depends_on','scope','acceptance_refs','open_refs','readiness','evidence_required')) {
            if ($null -eq $item.PSObject.Properties[$field]) { $errors.Add("Missing package field: $field") }
        }
        if ($item.id -cnotmatch '^PKG-[0-9]{2}$' -or $byId.ContainsKey($item.id)) {
            $errors.Add("Invalid/duplicate package: $($item.id)")
        }
        $byId[$item.id] = $item
        if ($item.readiness -cnotin $allowedReadiness) { $errors.Add("Invalid readiness: $($item.id)") }
        if (@($item.scope).Count -eq 0 -or @($item.evidence_required).Count -eq 0) {
            $errors.Add("Missing scope/evidence: $($item.id)")
        }
        if ($item.readiness -cne 'DEFERRED' -and @($item.acceptance_refs).Count -eq 0) {
            $errors.Add("Missing acceptance source: $($item.id)")
        }
        foreach ($slice in $item.existing_slices) {
            if ($slice -cnotin $script:knownSlices) { $errors.Add("Unknown existing slice: $slice") }
            $usedSlices[$slice] = $true
        }
        foreach ($ref in $item.acceptance_refs) {
            if ($ref -cmatch '^AT-') {
                if ($ref -cnotin $script:knownAcceptance) { $errors.Add("Unknown acceptance: $ref") }
            } elseif (-not (Test-Path -LiteralPath (Join-Path $repoRoot $ref) -PathType Leaf)) {
                $errors.Add("Missing acceptance source: $ref")
            }
        }
        foreach ($ref in $item.open_refs) {
            if ($ref -cnotin $script:knownOpen) { $errors.Add("Unknown OPEN reference: $ref") }
        }
    }
    foreach ($slice in $script:knownSlices) {
        if (-not $usedSlices.ContainsKey($slice)) { $errors.Add("Unmapped existing slice: $slice") }
    }
    foreach ($item in $Inventory.packages) {
        foreach ($dependency in $item.depends_on) {
            if (-not $byId.ContainsKey($dependency)) { $errors.Add("Unknown dependency: $dependency") }
            elseif ($item.readiness -cne 'DEFERRED' -and $byId[$dependency].readiness -ceq 'DEFERRED') {
                $errors.Add("Active package depends on deferred scope: $($item.id)")
            }
        }
    }
    # Kahn's algorithm: missing references are errors above, never silently accepted.
    $remaining = @{}
    foreach ($item in $Inventory.packages) { $remaining[$item.id] = @($item.depends_on) }
    while ($remaining.Count -gt 0) {
        $ready = @($remaining.Keys | Where-Object {
            $pending = @($remaining[$_] | Where-Object { $remaining.ContainsKey($_) })
            $pending.Count -eq 0
        })
        if ($ready.Count -eq 0) { $errors.Add('Dependency cycle detected.'); break }
        foreach ($id in $ready) { $remaining.Remove($id) }
    }
    $requirementIds = @{}
    $scenarioIds = @{}
    $decisionKeys = @{}
    $coverage = @{}
    foreach ($req in $Mapping.requirements) {
        if ($req.id -cnotmatch '^REQ-[0-9]{3}$' -or $requirementIds.ContainsKey($req.id)) {
            $errors.Add("Invalid/duplicate requirement: $($req.id)")
        }
        $requirementIds[$req.id] = $true
        if ($decisionKeys.ContainsKey($req.decision_key)) { $errors.Add("Duplicate decision key: $($req.decision_key)") }
        $decisionKeys[$req.decision_key] = $true
        if ($req.scenario.id -cnotmatch '^SCN-[0-9]{3}$' -or $scenarioIds.ContainsKey($req.scenario.id)) {
            $errors.Add("Invalid/duplicate scenario: $($req.scenario.id)")
        }
        $scenarioIds[$req.scenario.id] = $true
        if ($req.status -cne 'PROPOSED_ACCEPTANCE_NOT_RUN' -or $req.scenario.execution_status -cne 'NOT_RUN') {
            $errors.Add("Planning scenario claims runtime evidence: $($req.id)")
        }
        foreach ($field in @('given','when','then','negative_boundary')) {
            if ([string]::IsNullOrWhiteSpace($req.scenario.$field)) { $errors.Add("Empty scenario $field in $($req.id)") }
        }
        if (@($req.package_ids).Count -eq 0 -or @($req.required_evidence).Count -eq 0 -or @($req.source_refs).Count -eq 0) {
            $errors.Add("Incomplete mapping: $($req.id)")
        }
        foreach ($id in $req.package_ids) {
            if (-not $byId.ContainsKey($id)) { $errors.Add("Unknown mapped package: $id") }
            $coverage[$id] = $true
        }
        foreach ($ref in @($req.source_refs) + @($req.legacy_acceptance_refs)) {
            if ($ref -cmatch '^AT-') {
                if ($ref -cnotin $script:knownAcceptance) { $errors.Add("Unknown mapped acceptance: $ref") }
            } elseif (-not (Test-Path -LiteralPath (Join-Path $repoRoot $ref) -PathType Leaf)) {
                $errors.Add("Missing mapping source: $ref")
            }
        }
    }
    foreach ($id in $byId.Keys) {
        if (-not $coverage.ContainsKey($id)) { $errors.Add("Uncovered package: $id") }
    }
    $latest = @((1..13 | ForEach-Object { 'Q{0:d2}' -f $_ })) + @('F01','KUL-01','KUL-02')
    foreach ($key in $latest) {
        if (-not $decisionKeys.ContainsKey($key)) { $errors.Add("Missing latest decision: $key") }
    }
    foreach ($entry in $Mapping.legacy_reconciliation) {
        foreach ($ref in $entry.refs) {
            if ($ref -cnotin $script:knownAcceptance) { $errors.Add("Unknown reconciliation acceptance: $ref") }
        }
    }
    return $errors.ToArray()
}

try {
    $backlog = Read-Json (Join-Path $repoRoot 'docs/backlog/DELIVERY_BACKLOG_v1.json')
    $script:knownSlices = @($backlog.waves | ForEach-Object { $_.slices } | ForEach-Object { $_.id })
    $acceptanceText = Get-Content -LiteralPath (Join-Path $repoRoot 'docs/acceptance/ACCEPTANCE_70.md') -Raw -Encoding UTF8
    $openText = Get-Content -LiteralPath (Join-Path $repoRoot 'docs/decisions/ACIK_KARARLAR.md') -Raw -Encoding UTF8
    $script:knownAcceptance = @([regex]::Matches($acceptanceText, '\bAT-\d{3}\b') | ForEach-Object { $_.Value } | Sort-Object -Unique)
    $script:knownOpen = @([regex]::Matches($openText, '\bOPEN-\d{3}\b') | ForEach-Object { $_.Value } | Sort-Object -Unique)
    $inventory = Read-Json (Join-Path $packageRoot 'TESLIM_ENVANTERI.json')
    $mapping = Read-Json (Join-Path $packageRoot 'KARAR_ISLEV_TEST_ESLEMESI.json')
    $problems = @(Get-ValidationErrors $inventory $mapping)
    $markdownFiles = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.md') + @(
        Get-Item -LiteralPath (Join-Path $repoRoot 'docs/decisions/SINAN_KARARLARI_2026-09-10.md')
    )
    $linkCount = 0
    foreach ($file in $markdownFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
        if (@([regex]::Matches($content, '(?m)^```')).Count % 2 -ne 0) { $problems += "Unbalanced fences: $($file.Name)" }
        if ($content -match '(?m)^(<<<<<<<|=======|>>>>>>>)') { $problems += "Conflict marker: $($file.Name)" }
        foreach ($match in [regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')) {
            $target = $match.Groups[1].Value.Trim('<','>')
            if ($target -match '^(https?://|#)') { continue }
            $target = [uri]::UnescapeDataString(($target -split '#')[0])
            $resolvedTarget = [System.IO.Path]::GetFullPath((Join-Path $file.DirectoryName $target))
            if (-not (Test-Path -LiteralPath $resolvedTarget -PathType Leaf)) { $problems += "Broken local link in $($file.Name): $target" }
            $linkCount++
        }
    }
    if ($problems.Count -gt 0) { throw ($problems -join "`n") }
    $negativeTests = 0
    if (-not $SkipSelfTests) {
        foreach ($case in @('cycle','unknown-package','duplicate-package','missing-decision','false-pass','unknown-at','missing-source')) {
            $badInventory = Copy-Json $inventory
            $badMapping = Copy-Json $mapping
            switch ($case) {
                'cycle' { $badInventory.packages[0].depends_on = @($badInventory.packages[0].id) }
                'unknown-package' { $badMapping.requirements[0].package_ids += 'PKG-99' }
                'duplicate-package' { $badInventory.packages += $badInventory.packages[0] }
                'missing-decision' { $badMapping.requirements = @($badMapping.requirements | Where-Object { $_.decision_key -cne 'KUL-02' }) }
                'false-pass' { $badMapping.requirements[0].scenario.execution_status = 'PASSED' }
                'unknown-at' { $badInventory.packages[0].acceptance_refs += 'AT-999' }
                'missing-source' { $badMapping.requirements[0].source_refs = @('docs/nonexistent-delivery-test-fixture.md') }
            }
            $rejected = @(Get-ValidationErrors $badInventory $badMapping)
            if ($rejected.Count -eq 0) { throw "Negative self-test was accepted: $case" }
            $negativeTests++
        }
    }
    $activeCount = @($inventory.packages | Where-Object { $_.readiness -cne 'DEFERRED' }).Count
    Write-Output 'Delivery package structure PASSED (not product acceptance).'
    Write-Output "Packages: $($inventory.packages.Count); active target: $activeCount; existing slices: $($script:knownSlices.Count)"
    Write-Output "Requirements/scenarios: $($mapping.requirements.Count); latest decision keys: 16; local links: $linkCount"
    Write-Output "Dependency graph: acyclic; negative self-tests rejected: $negativeTests"
    exit 0
} catch {
    Write-Error "Delivery package structure FAILED: $($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
