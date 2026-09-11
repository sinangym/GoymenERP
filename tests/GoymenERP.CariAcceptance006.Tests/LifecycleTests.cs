using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GoymenERP.CariAcceptance006;

namespace GoymenERP.CariAcceptance006.Tests;

// Ownership, process-id and start-time identity of the recorded host, the repeated stop and the
// propagation of a failed launch. Synthetic records live in temporary folders; the OS is either
// a double or a process this test started itself. No evidence folder, environment, container,
// database or credential material is touched.
public sealed class LifecycleTests
{
    private static readonly RunSettings Identity = new();
    private static readonly DateTimeOffset Started = DateTimeOffset.Parse("2026-09-06T15:51:36.9677889Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private const int HostPid = 17620;

    // Deterministic process doubles. A handle is bound to the FakeProcess it was opened for and
    // never to the current process-id mapping, so a test can change what the id points at after
    // the identity was verified and observe that the held handle is unaffected. Real process-id
    // recycling is never forced.
    private sealed class FakeProcess(FakeOs os, int pid, string name, DateTimeOffset startTimeUtc)
    {
        public string Name { get; } = name;
        public DateTimeOffset StartTimeUtc { get; } = startTimeUtc;
        public bool Exited { get; set; }
        public bool Killed { get; private set; }
        public int Opens { get; private set; }
        public int Disposals { get; private set; }
        public bool HandleOpen { get; private set; }
        public Exception? KillError { get; set; }
        public bool ExitsOnKill { get; set; } = true;
        public Action? AfterIdentityRead { get; set; }
        /// <summary>The process runs, but reading its metadata through the open handle fails.</summary>
        public bool MetadataUnreadable { get; set; }
        /// <summary>The process runs, but whether it has exited cannot be observed.</summary>
        public bool ExitStateUnknown { get; set; }

        public IProcessHandle Open()
        {
            Opens += 1;
            HandleOpen = true;
            return new FakeHandle(this);
        }

        internal void Terminate()
        {
            if (KillError is not null) throw KillError;
            Killed = true;
            os.Killed.Add(pid);
            if (!ExitsOnKill) return;
            Exited = true;
            if (os.Processes.TryGetValue(pid, out var current) && ReferenceEquals(current, this)) os.Processes.Remove(pid);
        }

        internal void Close()
        {
            Disposals += 1;
            HandleOpen = false;
        }
    }

    private sealed class FakeHandle(FakeProcess process) : IProcessHandle
    {
        private bool _identityRead;

        public ProcessObservation Read()
        {
            if (process.Exited) return new ProcessObservation(ObservationResult.VerifiedAbsent, null, "fake: the process has exited");
            if (process.MetadataUnreadable) return new ProcessObservation(ObservationResult.Unverified, null, "fake: process metadata could not be read");
            var facts = new ProcessFacts(process.Name, process.StartTimeUtc);
            if (!_identityRead)
            {
                _identityRead = true;
                process.AfterIdentityRead?.Invoke();
            }
            return new ProcessObservation(ObservationResult.Observed, facts, "fake: observed through the open handle");
        }

        public ExitObservation Exited => process.ExitStateUnknown ? ExitObservation.Unknown
            : process.Exited ? ExitObservation.Exited : ExitObservation.Running;

        public void Kill() => process.Terminate();
        public bool WaitForExit(TimeSpan timeout) => process.Exited;
        public void Dispose() => process.Close();
    }

    private sealed class FakeOs
    {
        public Dictionary<int, FakeProcess> Processes { get; } = [];
        public HashSet<int> ListeningPorts { get; } = [];
        public List<int> Killed { get; } = [];
        public List<int> Opens { get; } = [];
        public DateTimeOffset Clock { get; set; } = Started;
        public TimeSpan Tick { get; set; } = TimeSpan.FromSeconds(1);
        public int Waits { get; private set; }
        public bool RefuseToOpen { get; set; }

        public FakeProcess Add(int pid) => Add(pid, "dotnet", Started);

        public FakeProcess Add(int pid, string name, DateTimeOffset startTimeUtc)
        {
            var process = new FakeProcess(this, pid, name, startTimeUtc);
            Processes[pid] = process;
            return process;
        }

        // Observation goes through the production conversion (OsProbe.ObservationOf) over this
        // double's own open attempt: the double never answers an observation from its dictionary,
        // so the open -> metadata -> observation path under test is the one that really runs.
        public OsProbe Probe => new(
            pid => OsProbe.ObservationOf(OpenProcess(pid)),
            port => ListeningPorts.Contains(port),
            () => Clock,
            () => { Waits += 1; Clock += Tick; },
            OpenProcess);

        private ProcessOpen OpenProcess(int pid)
        {
            Opens.Add(pid);
            if (!Processes.TryGetValue(pid, out var process)) return new ProcessOpen(ProcessOpenResult.NotFound, null, "fake: no process with this id");
            if (RefuseToOpen) return new ProcessOpen(ProcessOpenResult.Unavailable, null, "fake: the process could not be opened");
            return new ProcessOpen(ProcessOpenResult.Opened, process.Open());
        }
    }

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "c006-lifecycle-" + Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static object Record(int pid, DateTimeOffset startTimeUtc) => new
    {
        Identity.RunId,
        Identity.InstanceId,
        Identity.ApiBaseUrl,
        pid,
        startTimeUtc,
        observedAtUtc = startTimeUtc,
        server = "Kestrel",
        executionRunId = "c012-778b7cbad73c",
    };

    private static void WriteRecord(string folder, object record) =>
        File.WriteAllText(Path.Combine(folder, "host-started.json"), JsonSerializer.Serialize(record, Evidence.Json));

    private static string FolderWithRecord(int pid, DateTimeOffset startTimeUtc)
    {
        var folder = NewFolder();
        WriteRecord(folder, Record(pid, startTimeUtc));
        return folder;
    }

    private static Process StartChild(string command) => Process.Start(new ProcessStartInfo("powershell.exe",
        "-NoProfile -NonInteractive -Command \"" + command + "\"")
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;

    private static void KillQuietly(Process child)
    {
        try { if (!child.HasExited) { child.Kill(); child.WaitForExit(15_000); } }
        catch (InvalidOperationException) { }
    }

    [Fact]
    public void MissingRecordIsNotAHost()
    {
        Assert.Null(Lifecycle.ReadHostIdentity(NewFolder(), Identity));
        Assert.Equal(HostState.NoRecord, Lifecycle.Stop(null, new FakeOs().Probe).State);
    }

    [Fact]
    public void RecordedHostIdentityIsReadFromTheOwnRecord()
    {
        var folder = FolderWithRecord(HostPid, Started);
        var host = Lifecycle.ReadHostIdentity(folder, Identity);
        Assert.NotNull(host);
        Assert.Equal(folder, host!.Folder);
        Assert.Equal(HostPid, host.Pid);
        Assert.Equal(Started, host.StartTimeUtc);
        Assert.Equal(Identity.RunId, host.RunId);
        Assert.Equal(Identity.InstanceId, host.InstanceId);
        Assert.Equal(Identity.ApiBaseUrl, host.ApiBaseUrl);
        Assert.Equal(Identity.ApiPort, host.ApiPort);
        Assert.Equal("c012-778b7cbad73c", host.ExecutionRunId);
    }

    [Fact]
    public void ARecordWithoutAnExecutionRunIdIsStillTheOwnHost()
    {
        var folder = NewFolder();
        WriteRecord(folder, new { Identity.RunId, Identity.InstanceId, Identity.ApiBaseUrl, pid = HostPid, startTimeUtc = Started, server = "Kestrel" });
        var host = Lifecycle.ReadHostIdentity(folder, Identity);
        Assert.Equal(HostPid, host!.Pid);
        Assert.Equal("", host.ExecutionRunId);
    }

    [Theory]
    [InlineData("RunId", "c006-other-run")]
    [InlineData("InstanceId", "11111111-2222-3333-4444-555555555555")]
    [InlineData("ApiBaseUrl", "http://127.0.0.1:15999")]
    public void ARecordOfAnotherRunOrInstanceIsRefused(string field, string value)
    {
        var folder = NewFolder();
        var record = new Dictionary<string, object>
        {
            ["RunId"] = Identity.RunId,
            ["InstanceId"] = Identity.InstanceId,
            ["ApiBaseUrl"] = Identity.ApiBaseUrl,
            ["pid"] = HostPid,
            ["startTimeUtc"] = Started,
        };
        record[field] = value;
        WriteRecord(folder, record);
        var error = Assert.Throws<InvalidOperationException>(() => Lifecycle.ReadHostIdentity(folder, Identity));
        Assert.StartsWith("C006", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordWithoutAUsableProcessIdIsRefused()
    {
        var folder = FolderWithRecord(0, Started);
        Assert.Throws<InvalidOperationException>(() => Lifecycle.ReadHostIdentity(folder, Identity));
    }

    [Fact]
    public void AMalformedRecordIsRefused()
    {
        var folder = NewFolder();
        File.WriteAllText(Path.Combine(folder, "host-started.json"), "{");
        Assert.ThrowsAny<JsonException>(() => Lifecycle.ReadHostIdentity(folder, Identity));
    }

    [Fact]
    public void TheRecordedProcessIsRunningOnlyWhenNameAndStartTimeMatch()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        os.Add(HostPid);
        Assert.Equal(HostState.Running, Lifecycle.Observe(host, os.Probe));
    }

    [Fact]
    public void AnAbsentProcessIsGone()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        Assert.Equal(HostState.Gone, Lifecycle.Observe(host, new FakeOs().Probe));
    }

    [Fact]
    public void ADifferentStartTimeMeansTheProcessIdWasReused()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        os.Add(HostPid, "dotnet", Started.AddTicks(1));
        Assert.Equal(HostState.Foreign, Lifecycle.Observe(host, os.Probe));
    }

    [Fact]
    public void ADifferentProcessNameMeansTheProcessIdWasReused()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        os.Add(HostPid, "notepad", Started);
        Assert.Equal(HostState.Foreign, Lifecycle.Observe(host, os.Probe));
    }

    [Fact]
    public void OnlyTheRecordedProcessIsStopped()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        os.Add(HostPid);
        os.Add(4242);
        var outcome = Lifecycle.Stop(host, os.Probe);
        Assert.True(outcome.Stopped);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal([HostPid], os.Killed);
        Assert.True(os.Processes.ContainsKey(4242));
    }

    [Fact]
    public void StoppingTwiceStopsNothingTheSecondTime()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        os.Add(HostPid);
        Assert.True(Lifecycle.Stop(host, os.Probe).Stopped);
        var again = Lifecycle.Stop(host, os.Probe);
        Assert.False(again.Stopped);
        Assert.Equal(HostState.Gone, again.State);
        Assert.Equal(0, again.ExitCode);
        Assert.Single(os.Killed);
    }

    [Fact]
    public void AReusedProcessIdIsRefusedInsteadOfStopped()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        os.Add(HostPid, "dotnet", Started.AddSeconds(1));
        var outcome = Lifecycle.Stop(host, os.Probe);
        Assert.False(outcome.Stopped);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Empty(os.Killed);
        Assert.StartsWith("C006 refusing to stop", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseIsRefusedWhileAnyRecordedHostRuns()
    {
        var live = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        os.Add(HostPid);
        var outcome = Lifecycle.PlanRelease([("C-DESKTOP-006", null), (live.Folder, live)], os.Probe);
        Assert.False(outcome.Allowed);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains(live.Folder, outcome.Message, StringComparison.Ordinal);
        Assert.Contains(HostPid.ToString(CultureInfo.InvariantCulture), outcome.Message, StringComparison.Ordinal);
        Assert.Equal([HostState.NoRecord, HostState.Running], outcome.Hosts.Select(host => host.State));
        Assert.Empty(os.Killed);
    }

    [Fact]
    public void ReleaseIsAllowedOnceNoRecordedHostRuns()
    {
        var gone = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var reused = Lifecycle.ReadHostIdentity(FolderWithRecord(4242, Started), Identity)!;
        var os = new FakeOs();
        os.Add(4242, "dotnet", Started.AddSeconds(1));
        var outcome = Lifecycle.PlanRelease([(gone.Folder, gone), (reused.Folder, reused), ("C-DESKTOP-006", null)], os.Probe);
        Assert.True(outcome.Allowed);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal([HostState.Gone, HostState.Foreign, HostState.NoRecord], outcome.Hosts.Select(host => host.State));
        Assert.Empty(os.Killed);
    }

    [Fact]
    public void AStartedHostIsAcceptedOnlyAfterItRecordsItselfAndHoldsThePort()
    {
        var folder = NewFolder();
        var os = new FakeOs();
        os.Add(HostPid);
        os.ListeningPorts.Add(Identity.ApiPort);
        var reads = 0;
        var outcome = Lifecycle.AwaitStart(() =>
        {
            reads += 1;
            if (reads == 3) WriteRecord(folder, Record(HostPid, Started));
            return Lifecycle.ReadHostIdentity(folder, Identity);
        }, HostPid, TimeSpan.FromSeconds(30), os.Probe);
        Assert.True(outcome.Started);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(HostState.Running, outcome.State);
        Assert.Equal(2, os.Waits);
        Assert.Equal(2000, outcome.WaitedMs);
    }

    [Fact]
    public void AHostThatNeverRecordsItselfFailsTheStartCheck()
    {
        var folder = NewFolder();
        var os = new FakeOs();
        os.Add(HostPid);
        var outcome = Lifecycle.AwaitStart(() => Lifecycle.ReadHostIdentity(folder, Identity), HostPid, TimeSpan.FromSeconds(5), os.Probe);
        Assert.False(outcome.Started);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal(HostState.NoRecord, outcome.State);
        Assert.Equal(5, os.Waits);
        Assert.Contains("within 5 s", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStartupRecordOfAnotherProcessFailsTheStartCheck()
    {
        var folder = FolderWithRecord(HostPid, Started);
        var os = new FakeOs();
        os.Add(HostPid);
        os.Add(4242);
        os.ListeningPorts.Add(Identity.ApiPort);
        var outcome = Lifecycle.AwaitStart(() => Lifecycle.ReadHostIdentity(folder, Identity), 4242, TimeSpan.FromSeconds(30), os.Probe);
        Assert.False(outcome.Started);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("launched process is 4242", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostThatDiesAfterRecordingItselfFailsTheStartCheck()
    {
        var folder = FolderWithRecord(HostPid, Started);
        var os = new FakeOs();
        os.ListeningPorts.Add(Identity.ApiPort);
        var outcome = Lifecycle.AwaitStart(() => Lifecycle.ReadHostIdentity(folder, Identity), HostPid, TimeSpan.FromSeconds(30), os.Probe);
        Assert.False(outcome.Started);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal(HostState.Gone, outcome.State);
        Assert.Contains("is not running", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostThatDoesNotHoldTheApiPortFailsTheStartCheck()
    {
        var folder = FolderWithRecord(HostPid, Started);
        var os = new FakeOs();
        os.Add(HostPid);
        var outcome = Lifecycle.AwaitStart(() => Lifecycle.ReadHostIdentity(folder, Identity), HostPid, TimeSpan.FromSeconds(30), os.Probe);
        Assert.False(outcome.Started);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains(Identity.ApiPort.ToString(CultureInfo.InvariantCulture), outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOwnStartedProcessIsStoppedOnceAndTheRepeatFindsNothing()
    {
        var child = StartChild("Start-Sleep -Seconds 30");
        try
        {
            var observation = OsProbe.Real.ReadProcess(child.Id);
            Assert.Equal(ObservationResult.Observed, observation.Result);
            var facts = observation.Facts;
            Assert.NotNull(facts);
            var probe = OsProbe.Real with { HostProcessName = facts!.Name };
            var host = Lifecycle.ReadHostIdentity(FolderWithRecord(child.Id, facts.StartTimeUtc), Identity);
            Assert.Equal(HostState.Running, Lifecycle.Observe(host!, probe));
            var first = Lifecycle.Stop(host, probe);
            Assert.True(first.Stopped);
            Assert.Equal(0, first.ExitCode);
            Assert.True(child.WaitForExit(15_000));
            var again = Lifecycle.Stop(host, probe);
            Assert.False(again.Stopped);
            Assert.Equal(HostState.Gone, again.State);
            Assert.Equal(0, again.ExitCode);
        }
        finally { KillQuietly(child); child.Dispose(); }
    }

    [Fact]
    public void ALiveProcessWithAReusedIdIsNeverStopped()
    {
        var child = StartChild("Start-Sleep -Seconds 30");
        try
        {
            var observation = OsProbe.Real.ReadProcess(child.Id);
            Assert.Equal(ObservationResult.Observed, observation.Result);
            var facts = observation.Facts;
            Assert.NotNull(facts);
            var probe = OsProbe.Real with { HostProcessName = facts!.Name };
            var host = Lifecycle.ReadHostIdentity(FolderWithRecord(child.Id, facts.StartTimeUtc.AddTicks(1)), Identity);
            var outcome = Lifecycle.Stop(host, probe);
            Assert.Equal(HostState.Foreign, outcome.State);
            Assert.False(outcome.Stopped);
            Assert.Equal(1, outcome.ExitCode);
            Assert.False(child.HasExited);
        }
        finally { KillQuietly(child); child.Dispose(); }
    }

    [Fact]
    public void AFailedChildLaunchEndsTheStartCheckWithANonZeroExitCode()
    {
        var child = StartChild("exit 3");
        try
        {
            Assert.True(child.WaitForExit(30_000));
            Assert.Equal(3, child.ExitCode);
            var folder = NewFolder();
            var outcome = Lifecycle.AwaitStart(() => Lifecycle.ReadHostIdentity(folder, Identity), child.Id, TimeSpan.FromSeconds(5), OsProbe.Real);
            Assert.False(outcome.Started);
            Assert.Equal(1, outcome.ExitCode);
            Assert.Equal(HostState.Gone, outcome.State);
            Assert.Contains("exited before it recorded a started host", outcome.Message, StringComparison.Ordinal);
        }
        finally { KillQuietly(child); child.Dispose(); }
    }

    [Fact]
    public async Task AFailedOperationEndsTheCommandWithExitCodeOne() => Assert.Equal(1, await EntryPoint.Main([]));

    // C016-P2-001: the identity that is verified and the process that is terminated must be the
    // same open handle. The cases below change what the process id points at after verification.
    [Fact]
    public void TheIdentityIsVerifiedOnTheSameHandleThatIsStopped()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        var recorded = os.Add(HostPid);
        var outcome = Lifecycle.Stop(host, os.Probe);
        Assert.True(outcome.Stopped);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal([HostPid], os.Opens);
        Assert.Equal(1, recorded.Opens);
        Assert.True(recorded.Killed);
        Assert.Equal(1, recorded.Disposals);
        Assert.False(recorded.HandleOpen);
    }

    [Fact]
    public void AProcessThatExitsAfterVerificationIsNotStoppedAndItsReplacementIsUntouched()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        var recorded = os.Add(HostPid);
        FakeProcess? replacement = null;
        recorded.AfterIdentityRead = () =>
        {
            recorded.Exited = true;
            replacement = os.Add(HostPid);
        };
        var outcome = Lifecycle.Stop(host, os.Probe);
        Assert.Equal(HostState.Gone, outcome.State);
        Assert.False(outcome.Stopped);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Empty(os.Killed);
        Assert.NotNull(replacement);
        Assert.False(replacement!.Killed);
        Assert.Equal(0, replacement.Opens);
        Assert.Single(os.Opens);
        Assert.Equal(1, recorded.Disposals);
        Assert.False(recorded.HandleOpen);
    }

    [Fact]
    public void TheTerminateFollowsTheOpenHandleAndNotTheProcessIdMapping()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        var recorded = os.Add(HostPid);
        FakeProcess? replacement = null;
        recorded.AfterIdentityRead = () => replacement = os.Add(HostPid, "dotnet", Started.AddMinutes(5));
        var outcome = Lifecycle.Stop(host, os.Probe);
        Assert.True(outcome.Stopped);
        Assert.True(recorded.Killed);
        Assert.NotNull(replacement);
        Assert.False(replacement!.Killed);
        Assert.Equal(0, replacement.Opens);
        Assert.Same(replacement, os.Processes[HostPid]);
        Assert.Single(os.Opens);
        Assert.Equal(1, recorded.Disposals);
    }

    [Fact]
    public void AnIdentityMismatchAtTheFirstReadStopsNothingAndClosesTheHandle()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        var foreign = os.Add(HostPid, "dotnet", Started.AddSeconds(1));
        var outcome = Lifecycle.Stop(host, os.Probe);
        Assert.Equal(HostState.Foreign, outcome.State);
        Assert.Equal(1, outcome.ExitCode);
        Assert.False(foreign.Killed);
        Assert.False(foreign.Exited);
        Assert.Empty(os.Killed);
        Assert.Equal(1, foreign.Disposals);
        Assert.False(foreign.HandleOpen);
    }

    [Fact]
    public void AProcessThatCannotBeOpenedIsNeverStopped()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs { RefuseToOpen = true };
        var process = os.Add(HostPid);
        var outcome = Lifecycle.Stop(host, os.Probe);
        // An open failure is uncertainty, not absence: the stop refuses instead of reporting Gone.
        Assert.Equal(HostState.Uncertain, outcome.State);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("verified", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(os.Killed);
        Assert.Equal(0, process.Opens);
        Assert.False(process.Exited);
    }

    [Fact]
    public void AFailedTerminateIsNotReportedAsAStop()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        var process = os.Add(HostPid);
        process.KillError = new System.ComponentModel.Win32Exception(5);
        var outcome = Lifecycle.Stop(host, os.Probe);
        Assert.False(outcome.Stopped);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("stop failed", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(os.Killed);
        Assert.False(process.Exited);
        Assert.Equal(1, process.Disposals);
        Assert.False(process.HandleOpen);
    }

    [Fact]
    public void AStopThatDoesNotCompleteWithinTheWaitIsNotReportedAsAStop()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        var process = os.Add(HostPid);
        process.ExitsOnKill = false;
        var outcome = Lifecycle.Stop(host, os.Probe, TimeSpan.FromSeconds(3));
        Assert.False(outcome.Stopped);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("still running 3 s", outcome.Message, StringComparison.Ordinal);
        Assert.True(process.Killed);
        Assert.False(process.Exited);
        Assert.Equal(1, process.Disposals);
        Assert.False(process.HandleOpen);
    }

    [Fact]
    public void TheHandleIsClosedOnEveryResultPath()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var arrangements = new Func<FakeOs, FakeProcess>[]
        {
            os => os.Add(HostPid),
            os => os.Add(HostPid, "notepad", Started),
            os => { var process = os.Add(HostPid); process.KillError = new InvalidOperationException("terminate refused"); return process; },
            os => { var process = os.Add(HostPid); process.ExitsOnKill = false; return process; },
            os => { var process = os.Add(HostPid); process.AfterIdentityRead = () => process.Exited = true; return process; },
            os => { var process = os.Add(HostPid); process.MetadataUnreadable = true; return process; },
            os => { var process = os.Add(HostPid); process.ExitStateUnknown = true; return process; },
        };
        foreach (var arrange in arrangements)
        {
            var os = new FakeOs();
            var process = arrange(os);
            Lifecycle.Stop(host, os.Probe, TimeSpan.FromSeconds(1));
            Assert.Equal(1, process.Disposals);
            Assert.False(process.HandleOpen);
        }
    }

    [Fact]
    public void TheChosenApiKeepsTheHandleBoundToTheProcessItWasOpenedFor()
    {
        var child = StartChild("Start-Sleep -Seconds 30");
        var unrelated = StartChild("Start-Sleep -Seconds 30");
        try
        {
            using var held = Process.GetProcessById(child.Id);
            var handle = held.SafeHandle;
            Assert.False(handle.IsInvalid);
            Assert.False(handle.IsClosed);
            Assert.Equal(child.Id, held.Id);
            child.Kill();
            Assert.True(child.WaitForExit(15_000));
            Assert.True(held.HasExited);
            var afterExit = Xunit.Record.Exception(() => held.Kill());
            Assert.Null(afterExit);
            Assert.False(unrelated.HasExited);
            held.Dispose();
            Assert.True(handle.IsClosed);
        }
        finally
        {
            KillQuietly(child);
            KillQuietly(unrelated);
            child.Dispose();
            unrelated.Dispose();
        }
    }

    // C017-P2-001: a failed observation must never become verified absence. The cases below drive
    // the production open -> metadata -> observation conversion (OsProbe.ObservationOf) through
    // the double's own open attempt, so the mapping under test is the one that runs.
    [Fact]
    public void AnOpenFailureLeavesTheHostStateUncertainInsteadOfGone()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs { RefuseToOpen = true };
        var process = os.Add(HostPid);
        Assert.Equal(HostState.Uncertain, Lifecycle.Observe(host, os.Probe));
        Assert.Equal([HostPid], os.Opens);
        Assert.Equal(ObservationResult.Unverified, os.Probe.ReadProcess(HostPid).Result);
        Assert.Equal(0, process.Opens);
        Assert.False(process.Exited);
    }

    [Fact]
    public void AnOpenFailureRefusesTheRelease()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs { RefuseToOpen = true };
        os.Add(HostPid);
        var outcome = Lifecycle.PlanRelease([(host.Folder, host)], os.Probe);
        Assert.False(outcome.Allowed);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal([HostState.Uncertain], outcome.Hosts.Select(status => status.State));
        Assert.Contains("could not be observed", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(os.Killed);
    }

    [Fact]
    public void AnOpenFailureFailsTheStartCheck()
    {
        var os = new FakeOs { RefuseToOpen = true };
        os.Add(HostPid);
        os.ListeningPorts.Add(Identity.ApiPort);
        var folder = NewFolder();
        var outcome = Lifecycle.AwaitStart(() => Lifecycle.ReadHostIdentity(folder, Identity), HostPid, TimeSpan.FromSeconds(30), os.Probe);
        Assert.False(outcome.Started);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal(HostState.Uncertain, outcome.State);
        Assert.Contains("could not be observed", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMetadataFailureAfterOpeningLeavesTheHostStateUncertainAndClosesTheHandle()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        var process = os.Add(HostPid);
        process.MetadataUnreadable = true;
        Assert.Equal(HostState.Uncertain, Lifecycle.Observe(host, os.Probe));
        Assert.Equal(1, process.Opens);
        Assert.Equal(1, process.Disposals);
        Assert.False(process.HandleOpen);
        Assert.False(process.Exited);
    }

    [Fact]
    public void AMetadataFailureAfterOpeningIsNeverStopped()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        var process = os.Add(HostPid);
        process.MetadataUnreadable = true;
        var outcome = Lifecycle.Stop(host, os.Probe);
        Assert.Equal(HostState.Uncertain, outcome.State);
        Assert.False(outcome.Stopped);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("unverified", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(os.Killed);
        Assert.False(process.Killed);
        Assert.False(process.Exited);
        Assert.Equal(1, process.Disposals);
        Assert.False(process.HandleOpen);
    }

    [Fact]
    public void AMetadataFailureAfterOpeningRefusesTheRelease()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        var process = os.Add(HostPid);
        process.MetadataUnreadable = true;
        var outcome = Lifecycle.PlanRelease([(host.Folder, host)], os.Probe);
        Assert.False(outcome.Allowed);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal([HostState.Uncertain], outcome.Hosts.Select(status => status.State));
        Assert.Empty(os.Killed);
    }

    [Fact]
    public void AMetadataFailureAfterOpeningFailsTheStartCheck()
    {
        var os = new FakeOs();
        var process = os.Add(HostPid);
        process.MetadataUnreadable = true;
        os.ListeningPorts.Add(Identity.ApiPort);
        var folder = NewFolder();
        var outcome = Lifecycle.AwaitStart(() => Lifecycle.ReadHostIdentity(folder, Identity), HostPid, TimeSpan.FromSeconds(30), os.Probe);
        Assert.False(outcome.Started);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal(HostState.Uncertain, outcome.State);
        Assert.Equal(1, process.Disposals);
    }

    [Fact]
    public void AnUnknownExitStateAfterVerificationIsNeverStopped()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        var process = os.Add(HostPid);
        process.ExitStateUnknown = true;
        var outcome = Lifecycle.Stop(host, os.Probe);
        Assert.Equal(HostState.Uncertain, outcome.State);
        Assert.False(outcome.Stopped);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("could not be observed", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(os.Killed);
        Assert.False(process.Killed);
        Assert.Equal(1, process.Disposals);
    }

    [Fact]
    public void OneUncertainHostAmongSeveralRecordsBlocksTheRelease()
    {
        var absent = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var foreign = Lifecycle.ReadHostIdentity(FolderWithRecord(4242, Started), Identity)!;
        var unreadable = Lifecycle.ReadHostIdentity(FolderWithRecord(4343, Started), Identity)!;
        var os = new FakeOs();
        os.Add(4242, "dotnet", Started.AddSeconds(1));
        var uncertain = os.Add(4343);
        uncertain.MetadataUnreadable = true;
        var outcome = Lifecycle.PlanRelease([(absent.Folder, absent), (foreign.Folder, foreign), (unreadable.Folder, unreadable), ("C-DESKTOP-006", null)], os.Probe);
        Assert.False(outcome.Allowed);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal([HostState.Gone, HostState.Foreign, HostState.Uncertain, HostState.NoRecord], outcome.Hosts.Select(status => status.State));
        Assert.Contains("4343", outcome.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("4242", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(os.Killed);
    }

    [Fact]
    public void VerifiedAbsenceStaysGoneForTheStopAndAllowsTheRelease()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        Assert.Equal(ObservationResult.VerifiedAbsent, os.Probe.ReadProcess(HostPid).Result);
        Assert.Equal(HostState.Gone, Lifecycle.Observe(host, os.Probe));
        var stop = Lifecycle.Stop(host, os.Probe);
        Assert.Equal(HostState.Gone, stop.State);
        Assert.False(stop.Stopped);
        Assert.Equal(0, stop.ExitCode);
        var release = Lifecycle.PlanRelease([(host.Folder, host)], os.Probe);
        Assert.True(release.Allowed);
        Assert.Equal(0, release.ExitCode);
        Assert.Empty(os.Killed);
    }

    [Fact]
    public void AHostRecordThatCannotBeReadIsNotAnAbsentRecord()
    {
        var folder = NewFolder();
        // A directory in the place of the file: the read fails, which is not the same as a
        // folder that simply has no record. No OS permission is changed by this test.
        Directory.CreateDirectory(Path.Combine(folder, "host-started.json"));
        var error = Assert.Throws<InvalidOperationException>(() => Lifecycle.ReadHostIdentity(folder, Identity));
        Assert.StartsWith("C006", error.Message, StringComparison.Ordinal);
        Assert.Contains("could not be read", error.Message, StringComparison.Ordinal);
        Assert.Null(Lifecycle.ReadHostIdentity(NewFolder(), Identity));
    }

    [Fact]
    public void TheObservationRunsThroughTheProductionOpenAndMetadataConversion()
    {
        var host = Lifecycle.ReadHostIdentity(FolderWithRecord(HostPid, Started), Identity)!;
        var os = new FakeOs();
        var process = os.Add(HostPid);
        Assert.Equal(HostState.Running, Lifecycle.Observe(host, os.Probe));
        Assert.Equal([HostPid], os.Opens);
        Assert.Equal(1, process.Opens);
        Assert.Equal(1, process.Disposals);
        Assert.False(process.HandleOpen);
        var direct = OsProbe.ObservationOf(new ProcessOpen(ProcessOpenResult.Unavailable, null, "denied"));
        Assert.Equal(ObservationResult.Unverified, direct.Result);
        Assert.Equal("denied", direct.Detail);
        Assert.Null(direct.Facts);
    }

    [Fact]
    public void AnOwnProcessThatAlreadyExitedIsAStopThatTerminatesNothing()
    {
        var child = StartChild("Start-Sleep -Seconds 30");
        try
        {
            var observation = OsProbe.Real.ReadProcess(child.Id);
            Assert.Equal(ObservationResult.Observed, observation.Result);
            var facts = observation.Facts;
            Assert.NotNull(facts);
            var host = Lifecycle.ReadHostIdentity(FolderWithRecord(child.Id, facts!.StartTimeUtc), Identity);
            child.Kill();
            Assert.True(child.WaitForExit(15_000));
            var outcome = Lifecycle.Stop(host, OsProbe.Real with { HostProcessName = facts.Name });
            Assert.False(outcome.Stopped);
            Assert.Equal(HostState.Gone, outcome.State);
            Assert.Equal(0, outcome.ExitCode);
        }
        finally { KillQuietly(child); child.Dispose(); }
    }
}
