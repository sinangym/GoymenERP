#requires -Version 7.2

[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$PayloadRoot,
  [Parameter(Mandatory)][string]$PayloadManifest,
  [Parameter(Mandatory)][string]$ExpectedPayloadManifestSha256,
  [Parameter(Mandatory)][string]$ExpectedToolAssemblySha256,
  [Parameter(Mandatory)][string]$ExpectedWrapperScriptSha256,
  [Parameter(Mandatory)][string]$TestedCommit,
  [Parameter(Mandatory)][string]$ApprovedLogicalEndpointSha256Json,
  [Parameter(Mandatory)][string]$ExpectedDatabase,
  [Parameter(Mandatory)][string]$TargetLabel,
  [Parameter(Mandatory)][string]$ProbeTimeoutSeconds,
  [Parameter(Mandatory)][string]$OverallProcessTimeoutSeconds,
  [Parameter(Mandatory)][string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class GoymenErpRcsiProcessContainment
{
    private const uint JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int SigKill = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        uint informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);

    public static IntPtr CreateWindowsJob()
    {
        var job = CreateJobObject(IntPtr.Zero, null!);
        if (job == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var limits = new ExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        var size = Marshal.SizeOf<ExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, buffer, false);
            if (!SetInformationJobObject(
                    job,
                    JobObjectExtendedLimitInformation,
                    buffer,
                    checked((uint)size)))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch
        {
            CloseHandle(job);
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return job;
    }

    public static void AssignWindowsJob(IntPtr job, IntPtr processHandle)
    {
        if (!AssignProcessToJobObject(job, processHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public static void CloseWindowsJob(IntPtr job)
    {
        if (job != IntPtr.Zero && !CloseHandle(job))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public static void KillUnixProcessGroup(int processId)
    {
        if (kill(-processId, SigKill) != 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 3)
            {
                throw new Win32Exception(error);
            }
        }
    }
}
'@

function ConvertTo-LowerHex {
  param(
    [Parameter(Mandatory)][string]$Value,
    [Parameter(Mandatory)][ValidateSet(40, 64)][int]$Length,
    [Parameter(Mandatory)][string]$Name
  )

  $normalized = $Value.Trim().ToLowerInvariant()
  if ($normalized.Length -ne $Length -or $normalized -cnotmatch "\A[0-9a-f]{$Length}\z") {
    throw "$Name must be an exact lowercase hexadecimal value after normalization."
  }

  return $normalized
}

function Test-CanonicalAbsolutePath {
  param([Parameter(Mandatory)][string]$Path)

  if (-not [IO.Path]::IsPathFullyQualified($Path)) { return $false }
  $fullPath = [IO.Path]::GetFullPath($Path)
  if ($IsWindows) {
    if ($Path.Length -lt 3 -or $Path[1] -ne ':' -or $Path[2] -ne '\' -or
        $Path[0] -notmatch '[A-Za-z]' -or $Path.Contains('/') -or
        $Path.Substring(2).Contains(':') -or
        @($Path.Split('\') | Where-Object { $_.Contains('~') }).Count -ne 0) {
      return $false
    }

    return [string]::Equals(
      [IO.Path]::TrimEndingDirectorySeparator($Path),
      [IO.Path]::TrimEndingDirectorySeparator($fullPath),
      [StringComparison]::OrdinalIgnoreCase)
  }

  return [string]::Equals(
    [IO.Path]::TrimEndingDirectorySeparator($Path),
    [IO.Path]::TrimEndingDirectorySeparator($fullPath),
    [StringComparison]::Ordinal)
}

function Get-FileSha256 {
  param([Parameter(Mandatory)][string]$Path)
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-VerifiedWrapperScriptIdentity {
  param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$ExpectedSha256
  )

  if (-not (Test-CanonicalAbsolutePath $Path) -or
      -not (Test-Path -LiteralPath $Path -PathType Leaf) -or
      -not (Test-NoReparsePointInExistingAncestry $Path)) {
    throw 'The approved wrapper script path is not canonical, present and reparse-free.'
  }

  # Hash one raw byte snapshot. This is defense in depth and evidence binding;
  # the required outer runner remains the trust root and must perform its own
  # pre-secret check while holding an OS-enforced immutable/read-only boundary.
  $bytes = [IO.File]::ReadAllBytes($Path)
  $actualSha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
  if ($actualSha256 -cne $ExpectedSha256) {
    throw 'The wrapper script SHA-256 does not match the approved outer inventory.'
  }

  return [pscustomobject]@{
    Path = [IO.Path]::GetFullPath($Path)
    Sha256 = $actualSha256
  }
}

function Test-NoReparsePointInExistingAncestry {
  param([Parameter(Mandatory)][string]$Path)

  $current = [IO.Path]::GetFullPath($Path)
  while (-not [IO.File]::Exists($current) -and -not [IO.Directory]::Exists($current)) {
    $parent = [IO.Directory]::GetParent($current)
    if ($null -eq $parent) { return $false }
    $current = $parent.FullName
  }

  while ($true) {
    if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      return $false
    }
    $parent = [IO.Directory]::GetParent($current)
    if ($null -eq $parent) { return $true }
    $current = $parent.FullName
  }
}

function Test-IsUnderCanonicalRoot {
  param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$Root
  )

  $comparison = if ($IsWindows) {
    [StringComparison]::OrdinalIgnoreCase
  }
  else {
    [StringComparison]::Ordinal
  }
  $rootPrefix = [IO.Path]::TrimEndingDirectorySeparator($Root) +
    [IO.Path]::DirectorySeparatorChar
  return $Path.StartsWith($rootPrefix, $comparison)
}

function Get-RuntimePayloadInventory {
  param([Parameter(Mandatory)][string]$Root)

  if (-not (Test-NoReparsePointInExistingAncestry $Root)) {
    throw 'The runtime payload root or one of its ancestors is a reparse point.'
  }

  $inventory = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
  $directories = [Collections.Generic.Stack[string]]::new()
  $directories.Push($Root)
  while ($directories.Count -gt 0) {
    $directory = $directories.Pop()
    if (([IO.File]::GetAttributes($directory) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      throw 'The runtime payload contains a reparse-point directory.'
    }

    foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
      $attributes = [IO.File]::GetAttributes($entry)
      if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'The runtime payload contains a reparse-point entry.'
      }
      if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
        $directories.Push($entry)
        continue
      }

      $relativePath = [IO.Path]::GetRelativePath($Root, $entry).Replace('\', '/')
      if ([string]::IsNullOrWhiteSpace($relativePath) -or
          [IO.Path]::IsPathFullyQualified($relativePath) -or
          $relativePath.Contains('\') -or $relativePath.Contains(':') -or
          $relativePath.IndexOfAny([char[]]@('*', '?')) -ge 0 -or
          @($relativePath.Split('/') | Where-Object {
              $_.Length -eq 0 -or $_ -in @('.', '..')
            }).Count -ne 0) {
        throw 'The runtime payload contains an unsafe relative path.'
      }

      $file = [IO.FileInfo]::new($entry)
      $item = [pscustomobject]@{
        Length = $file.Length
        Sha256 = Get-FileSha256 $entry
      }
      if (-not $inventory.TryAdd($relativePath, $item)) {
        throw 'The runtime payload contains a duplicate relative path.'
      }
    }
  }

  return $inventory
}

function Get-VerifiedRuntimePayloadEntry {
  param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$Manifest,
    [Parameter(Mandatory)][string]$ManifestSha256,
    [Parameter(Mandatory)][string]$ToolAssemblySha256,
    [Parameter(Mandatory)][string]$SourceRevisionId
  )

  if (-not (Test-CanonicalAbsolutePath $Root) -or
      -not (Test-CanonicalAbsolutePath $Manifest) -or
      -not (Test-Path -LiteralPath $Root -PathType Container) -or
      -not (Test-Path -LiteralPath $Manifest -PathType Leaf) -or
      -not (Test-NoReparsePointInExistingAncestry $Manifest)) {
    throw 'The approved payload root or external manifest path is not canonical and present.'
  }

  # Hash and parse one immutable byte snapshot. Hashing the path and then reading it
  # again would permit an unapproved manifest to be swapped between the two reads.
  $manifestBytes = [IO.File]::ReadAllBytes($Manifest)
  $actualManifestSha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData($manifestBytes)).ToLowerInvariant()
  if ($actualManifestSha256 -cne $ManifestSha256) {
    throw 'The external runtime payload manifest hash does not match the approved inventory.'
  }

  $manifestText = [Text.UTF8Encoding]::new($false, $true).GetString($manifestBytes)
  $manifestDocument = [Text.Json.JsonDocument]::Parse($manifestText)
  $manifestRoot = $manifestDocument.RootElement
  [string[]]$manifestPropertyNames = @(
    $manifestRoot.EnumerateObject() | ForEach-Object { $_.Name }
  )
  [string[]]$expectedManifestPropertyNames = @(
    'contractVersion', 'sourceRevisionId', 'entryExecutablePath', 'files'
  )
  $manifestShapeMatches =
    $manifestRoot.ValueKind -eq [Text.Json.JsonValueKind]::Object -and
    $manifestPropertyNames.Count -eq $expectedManifestPropertyNames.Count
  if ($manifestShapeMatches) {
    for ($index = 0; $index -lt $expectedManifestPropertyNames.Count; $index++) {
      if ($manifestPropertyNames[$index] -cne $expectedManifestPropertyNames[$index]) {
        $manifestShapeMatches = $false
        break
      }
    }
  }
  if (-not $manifestShapeMatches -or
      $manifestRoot.GetProperty('contractVersion').ValueKind -ne
        [Text.Json.JsonValueKind]::String -or
      $manifestRoot.GetProperty('sourceRevisionId').ValueKind -ne
        [Text.Json.JsonValueKind]::String -or
      $manifestRoot.GetProperty('entryExecutablePath').ValueKind -ne
        [Text.Json.JsonValueKind]::String -or
      $manifestRoot.GetProperty('files').ValueKind -ne
        [Text.Json.JsonValueKind]::Array) {
    $manifestDocument.Dispose()
    throw 'The runtime payload manifest has an invalid contract shape.'
  }
  $manifestContract = $manifestRoot.GetProperty('contractVersion').GetString()
  $manifestSourceRevision = $manifestRoot.GetProperty('sourceRevisionId').GetString()
  if ($manifestContract -cne 'GOYMEN-ERP-RCSI-RUNTIME-PAYLOAD-V2' -or
      $manifestSourceRevision -cne $SourceRevisionId) {
    $manifestDocument.Dispose()
    throw 'The runtime payload manifest contract or source revision is not approved.'
  }

  $entryRelativePath = $manifestRoot.GetProperty('entryExecutablePath').GetString()
  if ([string]::IsNullOrWhiteSpace($entryRelativePath) -or
      $entryRelativePath.Length -ne $entryRelativePath.Trim().Length -or
      [IO.Path]::IsPathFullyQualified($entryRelativePath) -or
      $entryRelativePath.Contains('/') -or $entryRelativePath.Contains('\') -or
      $entryRelativePath.Contains(':') -or $entryRelativePath -in @('.', '..')) {
    throw 'The self-contained apphost must be a root-level manifest entry.'
  }

  $files = [Collections.Generic.List[object]]::new()
  $manifestInventory = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
  $previousPath = $null
  foreach ($row in $manifestRoot.GetProperty('files').EnumerateArray()) {
    [string[]]$rowPropertyNames = @($row.EnumerateObject() | ForEach-Object { $_.Name })
    if ($row.ValueKind -ne [Text.Json.JsonValueKind]::Object -or
        $rowPropertyNames.Count -ne 3 -or
        $rowPropertyNames[0] -cne 'path' -or
        $rowPropertyNames[1] -cne 'length' -or
        $rowPropertyNames[2] -cne 'sha256' -or
        $row.GetProperty('path').ValueKind -ne [Text.Json.JsonValueKind]::String -or
        $row.GetProperty('length').ValueKind -ne [Text.Json.JsonValueKind]::Number -or
        $row.GetProperty('sha256').ValueKind -ne [Text.Json.JsonValueKind]::String) {
      $manifestDocument.Dispose()
      throw 'The runtime payload manifest file inventory has an invalid shape.'
    }
    $relativePath = $row.GetProperty('path').GetString()
    $rowSha256 = ConvertTo-LowerHex `
      $row.GetProperty('sha256').GetString() 64 'Manifest file SHA-256'
    $rowLength = 0L
    if ([string]::IsNullOrWhiteSpace($relativePath) -or
        $relativePath.Length -ne $relativePath.Trim().Length -or
        [IO.Path]::IsPathFullyQualified($relativePath) -or
        $relativePath.Contains('\') -or $relativePath.Contains(':') -or
        $relativePath.IndexOfAny([char[]]@('*', '?')) -ge 0 -or
        @($relativePath.Split('/') | Where-Object {
            $_.Length -eq 0 -or $_ -in @('.', '..')
          }).Count -ne 0 -or
        -not $row.GetProperty('length').TryGetInt64([ref]$rowLength) -or
        $rowLength -lt 0 -or
        ($null -ne $previousPath -and
          [StringComparer]::Ordinal.Compare($previousPath, $relativePath) -ge 0) -or
        -not $manifestInventory.TryAdd(
          $relativePath,
          [pscustomobject]@{ Length = $rowLength; Sha256 = $rowSha256 })) {
      throw 'The runtime payload manifest file inventory is invalid or not canonical.'
    }
    $files.Add([pscustomobject]@{
        path = $relativePath
        length = $rowLength
        sha256 = $rowSha256
      })
    $previousPath = $relativePath
  }

  $canonicalManifestStream = [IO.MemoryStream]::new()
  $canonicalManifestWriter = [Text.Json.Utf8JsonWriter]::new($canonicalManifestStream)
  try {
    $canonicalManifestWriter.WriteStartObject()
    $canonicalManifestWriter.WriteString(
      'contractVersion', 'GOYMEN-ERP-RCSI-RUNTIME-PAYLOAD-V2')
    $canonicalManifestWriter.WriteString('sourceRevisionId', $manifestSourceRevision)
    $canonicalManifestWriter.WriteString('entryExecutablePath', $entryRelativePath)
    $canonicalManifestWriter.WriteStartArray('files')
    foreach ($file in $files) {
      $canonicalManifestWriter.WriteStartObject()
      $canonicalManifestWriter.WriteString('path', [string]$file.path)
      $canonicalManifestWriter.WriteNumber('length', [long]$file.length)
      $canonicalManifestWriter.WriteString('sha256', [string]$file.sha256)
      $canonicalManifestWriter.WriteEndObject()
    }
    $canonicalManifestWriter.WriteEndArray()
    $canonicalManifestWriter.WriteEndObject()
    $canonicalManifestWriter.Flush()
    $canonicalManifestBytes = $canonicalManifestStream.ToArray()
  }
  finally {
    $canonicalManifestWriter.Dispose()
    $canonicalManifestStream.Dispose()
    $manifestDocument.Dispose()
  }
  if ($canonicalManifestBytes.Length -ne $manifestBytes.Length -or
      -not [Convert]::ToHexString($canonicalManifestBytes).Equals(
        [Convert]::ToHexString($manifestBytes), [StringComparison]::Ordinal)) {
    throw 'The runtime payload manifest bytes are not canonical.'
  }

  $actualInventory = Get-RuntimePayloadInventory $Root
  if ($manifestInventory.Count -ne $actualInventory.Count) {
    throw 'The runtime payload contains missing or extra files.'
  }
  foreach ($relativePath in $manifestInventory.Keys) {
    if (-not $actualInventory.ContainsKey($relativePath) -or
        [long]$manifestInventory[$relativePath].Length -ne
          [long]$actualInventory[$relativePath].Length -or
        [string]$manifestInventory[$relativePath].Sha256 -cne
          [string]$actualInventory[$relativePath].Sha256) {
      throw 'The runtime payload bytes do not match the approved full inventory.'
    }
  }

  $requiredRuntimeFiles = @(
    'GoymenERP.BuildingBlocks.SqlServer.dll',
    'GoymenERP.SqlServerRcsiReadiness.deps.json',
    'GoymenERP.SqlServerRcsiReadiness.runtimeconfig.json',
    'System.Private.CoreLib.dll'
  )
  if ($IsWindows) {
    $requiredRuntimeFiles += @('hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll')
  }
  elseif ($IsMacOS) {
    $requiredRuntimeFiles += @('libhostfxr.dylib', 'libhostpolicy.dylib', 'libcoreclr.dylib')
  }
  else {
    $requiredRuntimeFiles += @('libhostfxr.so', 'libhostpolicy.so', 'libcoreclr.so')
  }
  if (@($requiredRuntimeFiles | Where-Object {
        -not $manifestInventory.ContainsKey($_)
      }).Count -ne 0) {
    throw 'The approved payload is missing a required self-contained runtime file.'
  }

  $runtimeConfigPath = Join-Path $Root `
    'GoymenERP.SqlServerRcsiReadiness.runtimeconfig.json'
  $runtimeConfigText = [Text.UTF8Encoding]::new($false, $true).GetString(
    [IO.File]::ReadAllBytes($runtimeConfigPath))
  $runtimeConfigDocument = [Text.Json.JsonDocument]::Parse($runtimeConfigText)
  $runtimeRoot = $runtimeConfigDocument.RootElement
  $runtimeOptions = [Text.Json.JsonElement]::new()
  $frameworks = [Text.Json.JsonElement]::new()
  $ignoredFramework = [Text.Json.JsonElement]::new()
  $ignoredFrameworks = [Text.Json.JsonElement]::new()
  $runtimeConfigValid =
    $runtimeRoot.ValueKind -eq [Text.Json.JsonValueKind]::Object -and
    $runtimeRoot.TryGetProperty('runtimeOptions', [ref]$runtimeOptions) -and
    $runtimeOptions.ValueKind -eq [Text.Json.JsonValueKind]::Object -and
    -not $runtimeOptions.TryGetProperty('framework', [ref]$ignoredFramework) -and
    -not $runtimeOptions.TryGetProperty('frameworks', [ref]$ignoredFrameworks) -and
    $runtimeOptions.TryGetProperty('includedFrameworks', [ref]$frameworks) -and
    $frameworks.ValueKind -eq [Text.Json.JsonValueKind]::Array -and
    $frameworks.GetArrayLength() -gt 0
  if ($runtimeConfigValid) {
    foreach ($framework in $frameworks.EnumerateArray()) {
      $name = [Text.Json.JsonElement]::new()
      $version = [Text.Json.JsonElement]::new()
      if ($framework.ValueKind -ne [Text.Json.JsonValueKind]::Object -or
          -not $framework.TryGetProperty('name', [ref]$name) -or
          $name.ValueKind -ne [Text.Json.JsonValueKind]::String -or
          [string]::IsNullOrWhiteSpace($name.GetString()) -or
          -not $framework.TryGetProperty('version', [ref]$version) -or
          $version.ValueKind -ne [Text.Json.JsonValueKind]::String -or
          [string]::IsNullOrWhiteSpace($version.GetString())) {
        $runtimeConfigValid = $false
        break
      }
    }
  }
  $runtimeConfigDocument.Dispose()
  if (-not $runtimeConfigValid) {
    throw 'The approved runtimeconfig does not prove a self-contained payload.'
  }

  $entryRows = @($files | Where-Object { [string]$_.path -ceq $entryRelativePath })
  $toolRows = @($files | Where-Object {
      [string]$_.path -ceq 'GoymenERP.SqlServerRcsiReadiness.dll'
    })
  if ($entryRows.Count -ne 1 -or $toolRows.Count -ne 1) {
    throw 'The approved manifest must contain exactly one apphost and tool assembly entry.'
  }

  $entrySha256 = ConvertTo-LowerHex ([string]$entryRows[0].sha256) 64 'Manifest apphost SHA-256'
  $manifestToolSha256 = ConvertTo-LowerHex ([string]$toolRows[0].sha256) 64 'Manifest tool SHA-256'
  if ($manifestToolSha256 -cne $ToolAssemblySha256) {
    throw 'The manifest tool assembly hash does not match the approved build inventory.'
  }

  $entryPath = [IO.Path]::GetFullPath((Join-Path $Root $entryRelativePath))
  $toolPath = [IO.Path]::GetFullPath((Join-Path $Root 'GoymenERP.SqlServerRcsiReadiness.dll'))
  if (-not (Test-CanonicalAbsolutePath $entryPath) -or
      -not (Test-CanonicalAbsolutePath $toolPath) -or
      -not (Test-Path -LiteralPath $entryPath -PathType Leaf) -or
      -not (Test-Path -LiteralPath $toolPath -PathType Leaf) -or
      (Get-FileSha256 $entryPath) -cne $entrySha256 -or
      (Get-FileSha256 $toolPath) -cne $ToolAssemblySha256) {
    throw 'The approved self-contained apphost or tool assembly bytes do not match the manifest.'
  }

  return [pscustomobject]@{
    EntryPath = $entryPath
    EntrySha256 = $entrySha256
    ManifestSha256 = $actualManifestSha256
  }
}

function Stop-AndJoinReadinessChild {
  param(
    [Parameter(Mandatory)][Diagnostics.Process]$process,
    [Parameter(Mandatory)][AllowNull()][Threading.Tasks.Task]$StandardOutputTask,
    [Parameter(Mandatory)][AllowNull()][Threading.Tasks.Task]$StandardErrorTask,
    [Parameter(Mandatory)][int]$TimeoutMilliseconds,
    [Parameter(Mandatory)][Diagnostics.Stopwatch]$DeadlineStopwatch,
    [Parameter(Mandatory)][ref]$WindowsJobHandle,
    [Parameter(Mandatory)][int]$UnixProcessGroupId
  )

  $containmentReleased = $true
  try {
    if ($IsWindows -and $WindowsJobHandle.Value -ne [IntPtr]::Zero) {
      [GoymenErpRcsiProcessContainment]::CloseWindowsJob(
        [IntPtr]$WindowsJobHandle.Value)
      $WindowsJobHandle.Value = [IntPtr]::Zero
    }
    elseif (-not $IsWindows -and $UnixProcessGroupId -gt 0) {
      [GoymenErpRcsiProcessContainment]::KillUnixProcessGroup($UnixProcessGroupId)
    }
  }
  catch {
    $containmentReleased = $false
  }

  try {
    if (-not $process.HasExited) { $process.Kill($true) }
  }
  catch {
    if (-not $process.HasExited) { return $false }
  }

  $remaining = [Math]::Max(
    0, $TimeoutMilliseconds - [int]$DeadlineStopwatch.ElapsedMilliseconds)
  if (-not $process.WaitForExit($remaining)) {
    return $false
  }
  if ($null -ne $StandardOutputTask) {
    $remaining = [Math]::Max(
      0, $TimeoutMilliseconds - [int]$DeadlineStopwatch.ElapsedMilliseconds)
    if (-not $StandardOutputTask.Wait($remaining)) {
      return $false
    }
  }
  if ($null -ne $StandardErrorTask) {
    $remaining = [Math]::Max(
      0, $TimeoutMilliseconds - [int]$DeadlineStopwatch.ElapsedMilliseconds)
    if (-not $StandardErrorTask.Wait($remaining)) {
      return $false
    }
  }

  if ($null -ne $StandardOutputTask) {
    [void]$StandardOutputTask.GetAwaiter().GetResult()
  }
  if ($null -ne $StandardErrorTask) {
    [void]$StandardErrorTask.GetAwaiter().GetResult()
  }
  return $containmentReleased
}

$process = $null
$startInfo = $null
$processStarted = $false
$stdoutBuffer = $null
$stderrBuffer = $null
$stdoutTask = $null
$stderrTask = $null
$waitCts = $null
$probeSecret = $null
$probeSecretBytes = $null
$connectionStringEnvironmentVariable = 'GOYMEN_ERP_RCSI_PROBE_CONNECTION'
$scriptExitCode = 0
$failureMessage = $null
$evidenceValidated = $false
$validatedStdoutBytes = $null
$outputPathFull = $null
$cleanupTimeoutMilliseconds = $null
$childTerminationAttempted = $false
$childTerminationSucceeded = $false
$windowsJobHandle = [IntPtr]::Zero
$unixProcessGroupId = 0
$postPayloadVerified = $false
$wrapperScriptVerifiedBeforeSecret = $false
$verifiedWrapperScript = $null
$evidenceDirectoryCreatedByWrapper = $false
$temporaryOutputPath = $null
$overallDeadlineStopwatch = $null

try {
  $testedCommitNormalized = ConvertTo-LowerHex $TestedCommit 40 'Tested commit'
  $expectedPayloadManifestNormalized = ConvertTo-LowerHex `
    $ExpectedPayloadManifestSha256 64 'Runtime payload manifest SHA-256'
  $expectedToolAssemblyNormalized = ConvertTo-LowerHex `
    $ExpectedToolAssemblySha256 64 'Tool assembly SHA-256'
  $expectedWrapperScriptNormalized = ConvertTo-LowerHex `
    $ExpectedWrapperScriptSha256 64 'Wrapper script SHA-256'
  if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
    throw 'The approved wrapper must be invoked from a script file.'
  }
  $verifiedWrapperScript = Get-VerifiedWrapperScriptIdentity `
    $PSCommandPath $expectedWrapperScriptNormalized
  $wrapperScriptVerifiedBeforeSecret = $true
  $endpointDocument = [Text.Json.JsonDocument]::Parse($ApprovedLogicalEndpointSha256Json)
  if ($endpointDocument.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Array) {
    throw 'The approved logical endpoint inventory must be a JSON string array.'
  }
  [string[]]$rawApprovedLogicalEndpointHashes = @(
    foreach ($element in $endpointDocument.RootElement.EnumerateArray()) {
      if ($element.ValueKind -ne [Text.Json.JsonValueKind]::String) {
        throw 'The approved logical endpoint inventory must contain only strings.'
      }
      ConvertTo-LowerHex $element.GetString() 64 'Logical endpoint SHA-256'
    }
  )
  $endpointDocument.Dispose()
  [string[]]$approvedLogicalEndpointHashes = @(
    $rawApprovedLogicalEndpointHashes | Sort-Object -Unique
  )
  $canonicalEndpointInventoryJson = '[' + (($approvedLogicalEndpointHashes | ForEach-Object {
    '"' + $_ + '"'
  }) -join ',') + ']'
  if ($approvedLogicalEndpointHashes.Count -eq 0 -or
      $approvedLogicalEndpointHashes.Count -ne $rawApprovedLogicalEndpointHashes.Count -or
      $ApprovedLogicalEndpointSha256Json -cne $canonicalEndpointInventoryJson) {
    throw 'The approved logical endpoint inventory must be non-empty, unique and canonical.'
  }
  $probeTimeoutSecondsValue = 0L
  $overallProcessTimeoutSecondsValue = 0L
  if (-not [long]::TryParse(
        $ProbeTimeoutSeconds,
        [Globalization.NumberStyles]::None,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$probeTimeoutSecondsValue) -or
      -not [long]::TryParse(
        $OverallProcessTimeoutSeconds,
        [Globalization.NumberStyles]::None,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$overallProcessTimeoutSecondsValue) -or
      $probeTimeoutSecondsValue -le 0 -or $overallProcessTimeoutSecondsValue -le 0) {
    throw 'Both operator-owned timeouts must be positive.'
  }

  try {
    $probeTimeout = [TimeSpan]::FromSeconds($probeTimeoutSecondsValue)
    $overallProcessTimeout = [TimeSpan]::FromSeconds($overallProcessTimeoutSecondsValue)
  }
  catch {
    throw 'An operator-owned timeout is outside the supported technical range.'
  }
  $maximumTimeoutMilliseconds = [double][int]::MaxValue
  if ($probeTimeout.TotalMilliseconds -gt $maximumTimeoutMilliseconds -or
      $overallProcessTimeout.TotalMilliseconds -gt $maximumTimeoutMilliseconds) {
    throw 'An operator-owned timeout is outside the supported technical range.'
  }

  if (-not (Test-CanonicalAbsolutePath $PayloadRoot) -or
      -not (Test-CanonicalAbsolutePath $PayloadManifest) -or
      -not (Test-CanonicalAbsolutePath $OutputPath)) {
    throw 'Payload, manifest and output paths must be original canonical absolute paths.'
  }
  $payloadRootFull = [IO.Path]::GetFullPath($PayloadRoot)
  $payloadManifestFull = [IO.Path]::GetFullPath($PayloadManifest)
  $outputPathFull = [IO.Path]::GetFullPath($OutputPath)
  if (-not (Test-CanonicalAbsolutePath $outputPathFull) -or
      -not (Test-NoReparsePointInExistingAncestry $outputPathFull) -or
      (Test-IsUnderCanonicalRoot $outputPathFull $payloadRootFull) -or
      [string]::IsNullOrWhiteSpace([IO.Path]::GetFileName($outputPathFull)) -or
      (Test-Path -LiteralPath $outputPathFull)) {
    throw 'The evidence output path must be canonical and fresh.'
  }
  $evidenceDirectory = [IO.Path]::GetDirectoryName($outputPathFull)
  if ([string]::IsNullOrWhiteSpace($evidenceDirectory) -or
      (Test-Path -LiteralPath $evidenceDirectory)) {
    throw 'Each invocation requires a new, previously absent evidence directory.'
  }

  $verifiedPayload = Get-VerifiedRuntimePayloadEntry `
    $payloadRootFull $payloadManifestFull $expectedPayloadManifestNormalized `
    $expectedToolAssemblyNormalized $testedCommitNormalized
  if ($IsWindows) {
    $windowsJobHandle = [GoymenErpRcsiProcessContainment]::CreateWindowsJob()
  }

  $probeSecret = [Environment]::GetEnvironmentVariable(
    $connectionStringEnvironmentVariable,
    [EnvironmentVariableTarget]::Process)
  if ([string]::IsNullOrWhiteSpace($probeSecret)) {
    throw 'The approved runner did not inject the probe secret environment variable.'
  }
  $probeSecretBytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($probeSecret)
  if ($probeSecretBytes.Length -gt [ushort]::MaxValue) {
    throw 'The injected connection secret exceeds the reviewed technical byte bound.'
  }
  [Environment]::SetEnvironmentVariable(
    $connectionStringEnvironmentVariable,
    $null,
    [EnvironmentVariableTarget]::Process)
  $probeSecret = $null

  $runId = [Guid]::NewGuid().ToString('D')
  $temporaryOutputPath = Join-Path $evidenceDirectory (
    ".{0}.{1}.tmp" -f [IO.Path]::GetFileName($outputPathFull), $runId)
  New-Item -ItemType Directory -Path $evidenceDirectory -ErrorAction Stop | Out-Null
  $evidenceDirectoryCreatedByWrapper = $true
  $startInfo = [Diagnostics.ProcessStartInfo]::new($verifiedPayload.EntryPath)
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardInput = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.Environment.Clear()
  if ($IsWindows) {
    $windowsDirectory = [IO.Directory]::GetParent([Environment]::SystemDirectory).FullName
    $startInfo.Environment['SystemRoot'] = $windowsDirectory
    $startInfo.Environment['WINDIR'] = $windowsDirectory
  }
  $arguments = @(
    '--connection-secret-stdin', 'true',
    '--expected-database', $ExpectedDatabase,
    '--target-label', $TargetLabel,
    '--run-id', $runId,
    '--tested-commit', $testedCommitNormalized,
    '--expected-wrapper-script-sha256', $expectedWrapperScriptNormalized,
    '--observed-wrapper-script-sha256', $verifiedWrapperScript.Sha256,
    '--expected-tool-assembly-sha256', $expectedToolAssemblyNormalized,
    '--runtime-payload-root', $payloadRootFull,
    '--runtime-payload-manifest', $payloadManifestFull,
    '--expected-runtime-payload-manifest-sha256', $expectedPayloadManifestNormalized,
    '--timeout-seconds', $probeTimeoutSecondsValue.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--output', $outputPathFull
  )
  foreach ($hash in $approvedLogicalEndpointHashes) {
    $arguments += @('--expected-logical-endpoint-sha256', $hash)
  }
  foreach ($argument in $arguments) { $startInfo.ArgumentList.Add([string]$argument) }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  $cleanupTimeoutMilliseconds = [int][Math]::Ceiling(
    $overallProcessTimeout.TotalMilliseconds)
  # One fifth of the operator-owned hard deadline is reserved for containment
  # kill/join and pipe drain, bounded only by reviewed technical limits. It is
  # not an additional timeout: all phases still share the same stopwatch/T.
  $cleanupReserveMilliseconds = [int][Math]::Min(
    5000,
    [Math]::Max(
      250,
      [Math]::Ceiling($cleanupTimeoutMilliseconds / 5.0)))
  $operationDeadlineMilliseconds =
    $cleanupTimeoutMilliseconds - $cleanupReserveMilliseconds
  $startedAtUtc = [DateTimeOffset]::UtcNow
  $overallDeadlineStopwatch = [Diagnostics.Stopwatch]::StartNew()
  if (-not $process.Start()) { throw 'The approved self-contained readiness apphost did not start.' }
  $processStarted = $true
  $remainingOverallMilliseconds = [Math]::Max(
    0,
    $operationDeadlineMilliseconds - [int]$overallDeadlineStopwatch.ElapsedMilliseconds)
  $waitCts = [Threading.CancellationTokenSource]::new(
    [TimeSpan]::FromMilliseconds($remainingOverallMilliseconds))
  $stdoutBuffer = [IO.MemoryStream]::new()
  $stderrBuffer = [IO.MemoryStream]::new()
  $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutBuffer)
  $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrBuffer)
  if ($IsWindows) {
    [GoymenErpRcsiProcessContainment]::AssignWindowsJob(
      $windowsJobHandle, $process.Handle)
  }
  else {
    # The reviewed apphost enters its own process group before reading the probe secret.
    $unixProcessGroupId = $process.Id
  }
  $processTimedOut = $false
  try {
    try {
      $secretMemory = [ReadOnlyMemory[byte]]::new($probeSecretBytes)
      [void]$process.StandardInput.BaseStream.WriteAsync(
        $secretMemory, $waitCts.Token).AsTask().GetAwaiter().GetResult()
      [void]$process.StandardInput.BaseStream.FlushAsync(
        $waitCts.Token).GetAwaiter().GetResult()
      $process.StandardInput.Close()
      [Array]::Clear($probeSecretBytes, 0, $probeSecretBytes.Length)
      $probeSecretBytes = $null
      [void]$process.WaitForExitAsync($waitCts.Token).GetAwaiter().GetResult()
    }
    catch [OperationCanceledException] {
      try { $process.StandardInput.Close() } catch { }
      $childTerminationAttempted = $true
      $childTerminationSucceeded = Stop-AndJoinReadinessChild `
        $process $stdoutTask $stderrTask $cleanupTimeoutMilliseconds `
        $overallDeadlineStopwatch `
        ([ref]$windowsJobHandle) $unixProcessGroupId
      if (-not $childTerminationSucceeded) {
        throw 'The readiness child could not be terminated within the operator-owned timeout.'
      }
      $processTimedOut = $true
    }
    if (-not $processTimedOut) {
      $childTerminationAttempted = $true
      $childTerminationSucceeded = Stop-AndJoinReadinessChild `
        $process $stdoutTask $stderrTask $cleanupTimeoutMilliseconds `
        $overallDeadlineStopwatch `
        ([ref]$windowsJobHandle) $unixProcessGroupId
      if (-not $childTerminationSucceeded) {
        throw 'Readiness process or output streams exceeded the single operator-owned deadline.'
      }
    }
  }
  finally {
    $waitCts.Dispose()
    $waitCts = $null
  }

  $completedAtUtc = [DateTimeOffset]::UtcNow
  $nativeExitCode = $process.ExitCode
  $stdoutBytes = $stdoutBuffer.ToArray()
  $stderrBytes = $stderrBuffer.ToArray()
  try {
    $postPayload = Get-VerifiedRuntimePayloadEntry `
      $payloadRootFull $payloadManifestFull $expectedPayloadManifestNormalized `
      $expectedToolAssemblyNormalized $testedCommitNormalized
  }
  catch {
    $scriptExitCode = 2
    throw
  }
  if ($postPayload.EntryPath -cne $verifiedPayload.EntryPath -or
      $postPayload.EntrySha256 -cne $verifiedPayload.EntrySha256 -or
      $postPayload.ManifestSha256 -cne $verifiedPayload.ManifestSha256) {
    $scriptExitCode = 2
    throw 'The approved runtime payload identity changed during the probe.'
  }
  $postPayloadVerified = $true
  try {
    $postWrapperScript = Get-VerifiedWrapperScriptIdentity `
      $verifiedWrapperScript.Path $expectedWrapperScriptNormalized
    if ($postWrapperScript.Path -cne $verifiedWrapperScript.Path -or
        $postWrapperScript.Sha256 -cne $verifiedWrapperScript.Sha256) {
      throw 'The approved wrapper script identity changed during the probe.'
    }
  }
  catch {
    $scriptExitCode = 2
    throw
  }
  if ($processTimedOut) {
    $scriptExitCode = 5
    throw 'The operator-owned overall readiness timeout expired; child tree was terminated.'
  }

  if ($nativeExitCode -eq 2 -and $stdoutBytes.Length -eq 0 -and
      -not (Test-Path -LiteralPath $outputPathFull)) {
    $scriptExitCode = 2
    throw 'RCSI readiness child failed closed before a bound evidence artifact was possible.'
  }
  if ($nativeExitCode -notin @(0, 2, 3, 4, 5, 6)) {
    $scriptExitCode = 1
    throw "RCSI readiness child returned unsupported native exit $nativeExitCode."
  }
  if ($stderrBytes.Length -ne 0) { throw 'Readiness child emitted unexpected stderr.' }
  if (-not (Test-Path -LiteralPath $outputPathFull -PathType Leaf)) {
    throw 'Readiness child did not produce a fresh evidence file.'
  }

  $fileBytes = [IO.File]::ReadAllBytes($outputPathFull)
  if ($stdoutBytes.Length -ne $fileBytes.Length -or
      -not [Convert]::ToHexString($stdoutBytes).Equals(
        [Convert]::ToHexString($fileBytes), [StringComparison]::Ordinal)) {
    throw 'Raw stdout bytes and evidence file bytes differ.'
  }
  if ($fileBytes.Length -ge 3 -and $fileBytes[0] -eq 0xEF -and
      $fileBytes[1] -eq 0xBB -and $fileBytes[2] -eq 0xBF) {
    throw 'Canonical evidence must be UTF-8 without BOM.'
  }

  $evidenceJson = [Text.UTF8Encoding]::new($false, $true).GetString($fileBytes)
  $evidenceDocument = [Text.Json.JsonDocument]::Parse($evidenceJson)
  $evidenceRoot = $evidenceDocument.RootElement
  [string[]]$expectedEvidenceProperties = @(
    'contractVersion',
    'runId',
    'testedCommit',
    'expectedWrapperScriptSha256',
    'observedWrapperScriptSha256',
    'expectedToolAssemblySha256',
    'observedToolAssemblySha256',
    'runtimePayloadManifestSha256',
    'runtimePayloadEntryExecutableSha256',
    'observedAtUtc',
    'targetLabel',
    'approvedLogicalEndpointSha256',
    'observedLogicalEndpointSha256',
    'logicalEndpointMatches',
    'expectedDatabase',
    'configuredDatabase',
    'observedDatabase',
    'readCommittedSnapshotEnabled',
    'status',
    'reasonCode',
    'exitCode',
    'queryMode'
  )
  [string[]]$actualEvidenceProperties = @(
    $evidenceRoot.EnumerateObject() | ForEach-Object { $_.Name }
  )
  $evidencePropertyShapeMatches =
    $actualEvidenceProperties.Count -eq $expectedEvidenceProperties.Count
  if ($evidencePropertyShapeMatches) {
    for ($index = 0; $index -lt $expectedEvidenceProperties.Count; $index++) {
      if ($actualEvidenceProperties[$index] -cne $expectedEvidenceProperties[$index]) {
        $evidencePropertyShapeMatches = $false
        break
      }
    }
  }
  if (-not $evidencePropertyShapeMatches) {
    $evidenceDocument.Dispose()
    throw 'Evidence JSON properties are not the exact canonical contract shape.'
  }

  $canonicalEvidenceStream = [IO.MemoryStream]::new()
  $canonicalEvidenceWriterOptions = [Text.Json.JsonWriterOptions]::new()
  $canonicalEvidenceWriterOptions.Encoder = [Text.Encodings.Web.JavaScriptEncoder]::Create(
    [Text.Unicode.UnicodeRanges]::All)
  $canonicalEvidenceWriter = [Text.Json.Utf8JsonWriter]::new(
    $canonicalEvidenceStream, $canonicalEvidenceWriterOptions)
  try {
    $evidenceRoot.WriteTo($canonicalEvidenceWriter)
    $canonicalEvidenceWriter.Flush()
    $canonicalEvidenceBytes = $canonicalEvidenceStream.ToArray()
  }
  finally {
    $canonicalEvidenceWriter.Dispose()
    $canonicalEvidenceStream.Dispose()
  }
  if ($canonicalEvidenceBytes.Length -ne $fileBytes.Length -or
      -not [Convert]::ToHexString($canonicalEvidenceBytes).Equals(
        [Convert]::ToHexString($fileBytes), [StringComparison]::Ordinal)) {
    $evidenceDocument.Dispose()
    throw 'Evidence JSON bytes are not the exact canonical representation.'
  }
  foreach ($propertyName in @(
      'contractVersion', 'runId', 'testedCommit',
      'expectedWrapperScriptSha256', 'observedWrapperScriptSha256',
      'expectedToolAssemblySha256',
      'observedToolAssemblySha256', 'runtimePayloadManifestSha256',
      'runtimePayloadEntryExecutableSha256', 'observedAtUtc',
      'status', 'reasonCode', 'queryMode')) {
    if ($evidenceRoot.GetProperty($propertyName).ValueKind -ne
        [Text.Json.JsonValueKind]::String) {
      $evidenceDocument.Dispose()
      throw 'Evidence JSON contains a non-string required property.'
    }
  }
  foreach ($propertyName in @(
      'targetLabel', 'expectedDatabase', 'observedLogicalEndpointSha256',
      'configuredDatabase', 'observedDatabase')) {
    if ($evidenceRoot.GetProperty($propertyName).ValueKind -notin @(
        [Text.Json.JsonValueKind]::String,
        [Text.Json.JsonValueKind]::Null)) {
      $evidenceDocument.Dispose()
      throw 'Evidence JSON contains an invalid nullable string property.'
    }
  }
  foreach ($propertyName in @('logicalEndpointMatches', 'readCommittedSnapshotEnabled')) {
    if ($evidenceRoot.GetProperty($propertyName).ValueKind -notin @(
        [Text.Json.JsonValueKind]::True,
        [Text.Json.JsonValueKind]::False,
        [Text.Json.JsonValueKind]::Null)) {
      $evidenceDocument.Dispose()
      throw 'Evidence JSON contains an invalid nullable Boolean property.'
    }
  }
  $endpointArray = $evidenceRoot.GetProperty('approvedLogicalEndpointSha256')
  if ($endpointArray.ValueKind -notin @(
        [Text.Json.JsonValueKind]::Array,
        [Text.Json.JsonValueKind]::Null) -or
      ($endpointArray.ValueKind -eq [Text.Json.JsonValueKind]::Array -and
        @($endpointArray.EnumerateArray() | Where-Object {
            $_.ValueKind -ne [Text.Json.JsonValueKind]::String
          }).Count -ne 0)) {
    $evidenceDocument.Dispose()
    throw 'Evidence JSON endpoint inventory must be a string array.'
  }
  $evidenceExitCode = 0
  if ($evidenceRoot.GetProperty('exitCode').ValueKind -ne
      [Text.Json.JsonValueKind]::Number -or
      -not $evidenceRoot.GetProperty('exitCode').TryGetInt32([ref]$evidenceExitCode)) {
    $evidenceDocument.Dispose()
    throw 'Evidence JSON exit code must be an Int32 number.'
  }
  $evidence = ($evidenceJson | ConvertFrom-Json -ErrorAction Stop)
  $observedAtUtcText = $evidenceDocument.RootElement.GetProperty(
    'observedAtUtc').GetString()
  $observedAtUtc = [DateTimeOffset]::ParseExact(
    $observedAtUtcText,
    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal -bor
      [Globalization.DateTimeStyles]::AdjustToUniversal)
  [string[]]$evidenceEndpointHashes = @($evidence.approvedLogicalEndpointSha256)
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

  $reasonByExitCode = @{
    0 = 'RCSI_READY'
    2 = 'BINDING_INVALID'
    3 = 'EXPECTED_DATABASE_MISMATCH'
    4 = 'RCSI_DISABLED'
    5 = 'PROBE_FAILED'
    6 = 'EXPECTED_LOGICAL_ENDPOINT_MISMATCH'
  }
  $observedEndpointHashCanonical =
    $null -eq $evidence.observedLogicalEndpointSha256 -or
    ([string]$evidence.observedLogicalEndpointSha256 -cmatch '\A[0-9a-f]{64}\z')
  $expectedStatus = if ($nativeExitCode -eq 0) { 'READY' } else { 'NOT_READY' }
  $semanticEvidenceMatches = switch ($nativeExitCode) {
    0 {
      $evidence.configuredDatabase -ceq $ExpectedDatabase -and
        $evidence.observedDatabase -ceq $ExpectedDatabase -and
        $evidence.logicalEndpointMatches -eq $true -and
        $evidence.readCommittedSnapshotEnabled -eq $true -and
        $approvedLogicalEndpointHashes -ccontains $evidence.observedLogicalEndpointSha256
    }
    2 {
      $null -eq $evidence.targetLabel -and
        $null -eq $evidence.approvedLogicalEndpointSha256 -and
        $null -eq $evidence.expectedDatabase -and
        $null -eq $evidence.configuredDatabase -and
        $null -eq $evidence.observedDatabase -and
        $null -eq $evidence.logicalEndpointMatches -and
        $null -eq $evidence.observedLogicalEndpointSha256 -and
        $null -eq $evidence.readCommittedSnapshotEnabled
    }
    3 {
      ($evidence.configuredDatabase -cne $ExpectedDatabase -and
          $null -eq $evidence.observedDatabase -and
          $null -eq $evidence.logicalEndpointMatches -and
          $null -eq $evidence.observedLogicalEndpointSha256 -and
          $null -eq $evidence.readCommittedSnapshotEnabled) -or
        ($evidence.configuredDatabase -ceq $ExpectedDatabase -and
          $null -ne $evidence.observedDatabase -and
          $evidence.observedDatabase -cne $ExpectedDatabase -and
          $evidence.logicalEndpointMatches -eq $true -and
          $approvedLogicalEndpointHashes -ccontains
            $evidence.observedLogicalEndpointSha256 -and
          $null -eq $evidence.readCommittedSnapshotEnabled)
    }
    4 {
      $evidence.configuredDatabase -ceq $ExpectedDatabase -and
        $evidence.observedDatabase -ceq $ExpectedDatabase -and
        $evidence.logicalEndpointMatches -eq $true -and
        $evidence.readCommittedSnapshotEnabled -eq $false -and
        $approvedLogicalEndpointHashes -ccontains $evidence.observedLogicalEndpointSha256
    }
    5 {
      $null -eq $evidence.observedDatabase -and
        $null -eq $evidence.readCommittedSnapshotEnabled -and
        (($null -eq $evidence.configuredDatabase -and
            $null -eq $evidence.observedLogicalEndpointSha256 -and
            $null -eq $evidence.logicalEndpointMatches) -or
          ($evidence.configuredDatabase -ceq $ExpectedDatabase -and
            (($null -eq $evidence.observedLogicalEndpointSha256 -and
                $null -eq $evidence.logicalEndpointMatches) -or
              ($evidence.logicalEndpointMatches -eq $true -and
                $approvedLogicalEndpointHashes -ccontains
                  $evidence.observedLogicalEndpointSha256) -or
              ($evidence.logicalEndpointMatches -eq $false -and
                $approvedLogicalEndpointHashes -cnotcontains
                  $evidence.observedLogicalEndpointSha256))))
    }
    6 {
      $evidence.configuredDatabase -ceq $ExpectedDatabase -and
        $null -eq $evidence.observedDatabase -and
        $evidence.logicalEndpointMatches -eq $false -and
        $null -eq $evidence.readCommittedSnapshotEnabled -and
        $null -ne $evidence.observedLogicalEndpointSha256 -and
        $approvedLogicalEndpointHashes -cnotcontains $evidence.observedLogicalEndpointSha256
    }
  }
  if ($evidence.contractVersion -cne 'GOYMEN-ERP-SQL-RCSI-READINESS-V1' -or
      $evidence.status -cne $expectedStatus -or
      $evidenceExitCode -ne $nativeExitCode -or
      $evidence.reasonCode -cne $reasonByExitCode[$nativeExitCode] -or
      $evidence.queryMode -cne 'READ_ONLY_SYS_DATABASES_CURRENT_V1' -or
      $evidence.runId -cne $runId -or
      $evidence.testedCommit -cne $testedCommitNormalized -or
      $evidence.expectedWrapperScriptSha256 -cne $expectedWrapperScriptNormalized -or
      $evidence.observedWrapperScriptSha256 -cne $verifiedWrapperScript.Sha256 -or
      $evidence.expectedToolAssemblySha256 -cne $expectedToolAssemblyNormalized -or
      $evidence.observedToolAssemblySha256 -cne $expectedToolAssemblyNormalized -or
      $evidence.runtimePayloadManifestSha256 -cne $expectedPayloadManifestNormalized -or
      $evidence.runtimePayloadEntryExecutableSha256 -cne $verifiedPayload.EntrySha256 -or
      ($nativeExitCode -ne 2 -and $evidence.targetLabel -cne $TargetLabel) -or
      ($nativeExitCode -ne 2 -and $evidence.expectedDatabase -cne $ExpectedDatabase) -or
      ($nativeExitCode -ne 2 -and -not $endpointInventoryMatches) -or
      -not $observedEndpointHashCanonical -or
      -not $semanticEvidenceMatches -or
      $observedAtUtc -lt $startedAtUtc -or $observedAtUtc -gt $completedAtUtc) {
    $evidenceDocument.Dispose()
    throw 'Evidence fields do not bind the same successful invocation.'
  }

  $evidenceDocument.Dispose()
  $validatedStdoutBytes = $stdoutBytes
  $evidenceValidated = $true
  if ($nativeExitCode -ne 0) {
    $scriptExitCode = $nativeExitCode
    throw "RCSI readiness child failed closed with native exit $nativeExitCode."
  }
}
catch {
  if ($scriptExitCode -eq 0) { $scriptExitCode = 1 }
  $failureMessage = $_.Exception.Message
}
finally {
  try {
    try {
      if ($processStarted) {
        try { $process.StandardInput.Close() } catch { }
      }
      if ($processStarted -and $null -ne $cleanupTimeoutMilliseconds -and
          (($childTerminationAttempted -and -not $childTerminationSucceeded) -or
          (-not $childTerminationAttempted -and
          -not (Stop-AndJoinReadinessChild `
            $process $stdoutTask $stderrTask $cleanupTimeoutMilliseconds `
            $overallDeadlineStopwatch `
            ([ref]$windowsJobHandle) $unixProcessGroupId)))) {
        throw 'Readiness child cleanup exceeded the operator-owned timeout.'
      }
      if ($processStarted -and $process.HasExited -and -not $postPayloadVerified) {
        try {
          $cleanupPostPayload = Get-VerifiedRuntimePayloadEntry `
            $payloadRootFull $payloadManifestFull $expectedPayloadManifestNormalized `
            $expectedToolAssemblyNormalized $testedCommitNormalized
          if ($cleanupPostPayload.EntryPath -cne $verifiedPayload.EntryPath -or
              $cleanupPostPayload.EntrySha256 -cne $verifiedPayload.EntrySha256 -or
              $cleanupPostPayload.ManifestSha256 -cne $verifiedPayload.ManifestSha256) {
            throw 'The approved runtime payload identity changed during child cleanup.'
          }
          $postPayloadVerified = $true
        }
        catch {
          $scriptExitCode = 2
          if ($failureMessage -eq $null) {
            $failureMessage = 'Post-execution runtime payload verification failed closed.'
          }
        }
      }
    }
    catch {
      if ($scriptExitCode -eq 0) { $scriptExitCode = 1 }
      if ($failureMessage -eq $null) { $failureMessage = 'Readiness child cleanup failed closed.' }
      $evidenceValidated = $false
      $validatedStdoutBytes = $null
    }
    finally {
      try {
        if ($startInfo -ne $null) {
          $startInfo.Environment.Remove($connectionStringEnvironmentVariable) | Out-Null
        }
      }
      finally {
        [Environment]::SetEnvironmentVariable(
          $connectionStringEnvironmentVariable,
          $null,
          [EnvironmentVariableTarget]::Process)
        $probeSecret = $null
        if ($null -ne $probeSecretBytes) {
          [Array]::Clear($probeSecretBytes, 0, $probeSecretBytes.Length)
          $probeSecretBytes = $null
        }
      }
    }
  }
  finally {
    if ($wrapperScriptVerifiedBeforeSecret) {
      try {
        $finalWrapperScript = Get-VerifiedWrapperScriptIdentity `
          $verifiedWrapperScript.Path $expectedWrapperScriptNormalized
        if ($finalWrapperScript.Path -cne $verifiedWrapperScript.Path -or
            $finalWrapperScript.Sha256 -cne $verifiedWrapperScript.Sha256) {
          throw 'The approved wrapper script identity changed during cleanup.'
        }
      }
      catch {
        $scriptExitCode = 2
        $evidenceValidated = $false
        $validatedStdoutBytes = $null
        $failureMessage = 'Final wrapper script verification failed closed.'
      }
    }
    if (-not $evidenceValidated -and $null -ne $outputPathFull -and
        (Test-Path -LiteralPath $outputPathFull -PathType Leaf)) {
      try { Remove-Item -LiteralPath $outputPathFull -Force -ErrorAction Stop }
      catch {
        if ($scriptExitCode -eq 0) { $scriptExitCode = 1 }
        if ($failureMessage -eq $null) {
          $failureMessage = 'Invalid readiness evidence could not be removed.'
        }
      }
    }
    if (-not $evidenceValidated -and $evidenceDirectoryCreatedByWrapper -and
        $null -ne $temporaryOutputPath -and
        (Test-Path -LiteralPath $temporaryOutputPath -PathType Leaf)) {
      try { Remove-Item -LiteralPath $temporaryOutputPath -Force -ErrorAction Stop }
      catch {
        if ($scriptExitCode -eq 0) { $scriptExitCode = 1 }
        if ($failureMessage -eq $null) {
          $failureMessage = 'Interrupted readiness evidence temp could not be removed.'
        }
      }
    }
    if ($waitCts -ne $null) { $waitCts.Dispose() }
    if ($stdoutBuffer -ne $null) { $stdoutBuffer.Dispose() }
    if ($stderrBuffer -ne $null) { $stderrBuffer.Dispose() }
    if ($process -ne $null) { $process.Dispose() }
    if ($windowsJobHandle -ne [IntPtr]::Zero) {
      try {
        [GoymenErpRcsiProcessContainment]::CloseWindowsJob($windowsJobHandle)
        $windowsJobHandle = [IntPtr]::Zero
      }
      catch {
        if ($scriptExitCode -eq 0) { $scriptExitCode = 1 }
        if ($failureMessage -eq $null) {
          $failureMessage = 'Readiness OS containment could not be released.'
        }
      }
    }
  }
}

if ($evidenceValidated -and $null -ne $validatedStdoutBytes) {
  [Console]::OpenStandardOutput().Write(
    $validatedStdoutBytes,
    0,
    $validatedStdoutBytes.Length)
  [Console]::OpenStandardOutput().Flush()
}
if ($failureMessage -ne $null) {
  [Console]::Error.WriteLine($failureMessage)
}
exit $scriptExitCode
