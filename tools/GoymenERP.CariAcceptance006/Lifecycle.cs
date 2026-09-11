using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace GoymenERP.CariAcceptance006;

// Ownership decisions for this local acceptance host: which recorded process may be stopped,
// when the owned resources may be released, and whether a launch actually became a live host.
// Every decision is a pure function over a recorded identity plus observed OS facts, so the
// control script never decides on its own and every branch is testable with process doubles.
public sealed record ProcessFacts(string Name, DateTimeOffset StartTimeUtc);

public sealed record HostIdentity(string Folder, string RunId, string InstanceId, string ApiBaseUrl, int ApiPort, int Pid, DateTimeOffset StartTimeUtc, string ExecutionRunId);

// Uncertain is not a state of the process but of the observation: the host may well be running,
// and nothing that assumes absence may follow from it.
public enum HostState { NoRecord, Gone, Foreign, Running, Uncertain }

public sealed record HostStatus(string Folder, int? Pid, HostState State);

public sealed record StopOutcome(HostState State, bool Stopped, int ExitCode, string Message);

public sealed record ReleaseOutcome(bool Allowed, int ExitCode, string Message, IReadOnlyList<HostStatus> Hosts);

public sealed record StartOutcome(bool Started, int ExitCode, string Message, HostState State, int WaitedMs, HostIdentity? Host);

public enum ProcessOpenResult { Opened, NotFound, Unavailable }

// What an observation established. VerifiedAbsent means the operating system reported that no
// such process exists or that it has exited. Unverified means the observation itself failed —
// the process could not be opened or its metadata could not be read — which proves nothing
// about the process and must never be turned into absence.
public enum ObservationResult { Observed, VerifiedAbsent, Unverified }

public enum ExitObservation { Running, Exited, Unknown }

public sealed record ProcessObservation(ObservationResult Result, ProcessFacts? Facts, string Detail);

// One open OS process handle. Identity is read, the terminate request is sent and the exit is
// awaited through this same handle, so nothing that happens to the process id in between can
// redirect the operation to another process.
public interface IProcessHandle : IDisposable
{
    ProcessObservation Read();
    ExitObservation Exited { get; }
    void Kill();
    bool WaitForExit(TimeSpan timeout);
}

public sealed record ProcessOpen(ProcessOpenResult Result, IProcessHandle? Handle, string Detail = "");

public sealed record OsProbe(
    Func<int, ProcessObservation> ReadProcess,
    Func<int, bool> IsListening,
    Func<DateTimeOffset> Now,
    Action Wait,
    Func<int, ProcessOpen> Open,
    string HostProcessName = "dotnet")
{
    public static OsProbe Real { get; } = new(pid => ObservationOf(LiveOpen(pid)), LiveListening, () => DateTimeOffset.UtcNow, () => Thread.Sleep(250), LiveOpen);

    // The single conversion from an open attempt into an observation. The real probe and every
    // double go through it, so no caller and no test can bypass the separation between verified
    // absence and a failed observation. Observation only: the handle is closed here, while Stop
    // opens its own handle and keeps it.
    public static ProcessObservation ObservationOf(ProcessOpen opened)
    {
        using var handle = opened.Handle;
        if (opened.Result == ProcessOpenResult.NotFound)
            return new ProcessObservation(ObservationResult.VerifiedAbsent, null, Detail(opened.Detail, "the operating system reports no process with this id"));
        if (opened.Result != ProcessOpenResult.Opened || handle is null)
            return new ProcessObservation(ObservationResult.Unverified, null, Detail(opened.Detail, "the process could not be opened"));
        return handle.Read();
    }

    internal static string Detail(string detail, string fallback) => detail.Length == 0 ? fallback : detail;

    private static ProcessOpen LiveOpen(int pid)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);
            // Touching SafeHandle makes this object hold an open OS handle to that exact
            // process. Windows does not reuse a process id while a handle to it is open, so the
            // identity verified below stays the identity that is terminated afterwards.
            _ = process.SafeHandle;
            return new ProcessOpen(ProcessOpenResult.Opened, new LiveProcessHandle(process));
        }
        // The operating system enumerated its processes and found none with this id, or the
        // object states that the process has exited: both are verified absence.
        catch (ArgumentException) { process?.Dispose(); return new ProcessOpen(ProcessOpenResult.NotFound, null, "no process with this id is running"); }
        catch (InvalidOperationException) { process?.Dispose(); return new ProcessOpen(ProcessOpenResult.NotFound, null, "the process has exited"); }
        // Opening failed. That says nothing about whether the process runs, so it is not absence.
        catch (System.ComponentModel.Win32Exception error) { process?.Dispose(); return new ProcessOpen(ProcessOpenResult.Unavailable, null, "the process could not be opened (" + error.GetType().Name + ")"); }
    }

    private static bool LiveListening(int port) => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
        .Any(endpoint => endpoint.Port == port && IPAddress.IsLoopback(endpoint.Address));

    private sealed class LiveProcessHandle(Process process) : IProcessHandle
    {
        public ProcessObservation Read()
        {
            try
            {
                if (process.HasExited) return new ProcessObservation(ObservationResult.VerifiedAbsent, null, "the process has exited");
                return new ProcessObservation(ObservationResult.Observed, new ProcessFacts(process.ProcessName, process.StartTime.ToUniversalTime()), "observed through the open handle");
            }
            catch (InvalidOperationException) { return new ProcessObservation(ObservationResult.VerifiedAbsent, null, "the process has exited"); }
            // Metadata could not be read through the open handle: unverified, never absence.
            catch (System.ComponentModel.Win32Exception error) { return new ProcessObservation(ObservationResult.Unverified, null, "process metadata could not be read (" + error.GetType().Name + ")"); }
        }

        public ExitObservation Exited
        {
            get
            {
                try { return process.HasExited ? ExitObservation.Exited : ExitObservation.Running; }
                catch (InvalidOperationException) { return ExitObservation.Exited; }
                catch (System.ComponentModel.Win32Exception) { return ExitObservation.Unknown; }
            }
        }

        public void Kill() => process.Kill();

        public bool WaitForExit(TimeSpan timeout) => process.WaitForExit((int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue));

        public void Dispose() => process.Dispose();
    }
}

public static class Lifecycle
{
    public const int StartTimeoutSeconds = 30;
    public const int StopWaitSeconds = 15;

    // The record a host writes about itself. A record of another run, or a damaged one, is a
    // refusal: nothing is stopped or released on an identity this environment does not own.
    public static HostIdentity? ReadHostIdentity(string evidenceFolder, RunSettings identity)
    {
        var path = Path.Combine(evidenceFolder, "host-started.json");
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        // Only a file that is really not there means there is no recorded host. Anything that
        // merely prevents the read is a refusal, never an absent record.
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            throw new InvalidOperationException("C006 recorded host file could not be read (" + error.GetType().Name + "): " + path, error);
        }
        using var document = JsonDocument.Parse(text);
        var record = document.RootElement;
        var runId = record.GetProperty("RunId").GetString() ?? "";
        var instanceId = record.GetProperty("InstanceId").GetString() ?? "";
        var apiBaseUrl = record.GetProperty("ApiBaseUrl").GetString() ?? "";
        if (runId != identity.RunId || instanceId != identity.InstanceId || apiBaseUrl != identity.ApiBaseUrl)
            throw new InvalidOperationException("C006 recorded host belongs to another run or instance: " + path);
        var pid = record.GetProperty("pid").GetInt32();
        if (pid <= 0) throw new InvalidOperationException("C006 recorded host has no usable process id: " + path);
        var startTimeUtc = record.GetProperty("startTimeUtc").GetDateTimeOffset().ToUniversalTime();
        var executionRunId = record.TryGetProperty("executionRunId", out var execution) ? execution.GetString() ?? "" : "";
        return new HostIdentity(evidenceFolder, runId, instanceId, apiBaseUrl, identity.ApiPort, pid, startTimeUtc, executionRunId);
    }

    // A process id alone is never ownership: the live process must also carry the recorded
    // name and the exact recorded start time, otherwise the id was reused by something else.
    private static bool IsTheRecordedHost(ProcessFacts facts, HostIdentity host, OsProbe probe) =>
        string.Equals(facts.Name, probe.HostProcessName, StringComparison.OrdinalIgnoreCase) && facts.StartTimeUtc == host.StartTimeUtc;

    // Verified absence is Gone; a failed observation is Uncertain and never Gone.
    public static HostState Observe(HostIdentity host, OsProbe probe)
    {
        var observation = probe.ReadProcess(host.Pid);
        if (observation.Result == ObservationResult.VerifiedAbsent) return HostState.Gone;
        if (observation.Result != ObservationResult.Observed || observation.Facts is null) return HostState.Uncertain;
        return IsTheRecordedHost(observation.Facts, host, probe) ? HostState.Running : HostState.Foreign;
    }

    public static StopOutcome Stop(HostIdentity? host, OsProbe probe) => Stop(host, probe, TimeSpan.FromSeconds(StopWaitSeconds));

    // The expected identity travels into the stop itself: one handle is opened for the recorded
    // process id, the identity is verified on that handle, and the terminate request and the
    // wait for the result use the same handle, which is closed on every path. The process id is
    // never opened a second time after verification, so a recycled id cannot redirect the kill.
    public static StopOutcome Stop(HostIdentity? host, OsProbe probe, TimeSpan waitForExit)
    {
        if (host is null) return new StopOutcome(HostState.NoRecord, false, 0, "C006 no recorded host in this evidence folder; nothing to stop.");
        var pid = host.Pid.ToString(CultureInfo.InvariantCulture);
        var opened = probe.Open(host.Pid);
        using var handle = opened.Handle;
        // Stopping twice is not an error: the second call finds no process and stops nothing.
        if (opened.Result == ProcessOpenResult.NotFound)
            return new StopOutcome(HostState.Gone, false, 0, "C006 recorded host is no longer running; nothing to stop.");
        // An open that failed proves nothing: refuse instead of assuming the host is gone.
        if (opened.Result != ProcessOpenResult.Opened || handle is null)
            return new StopOutcome(HostState.Uncertain, false, 1, "C006 refusing to stop pid " + pid + ": " + OsProbe.Detail(opened.Detail, "the process could not be opened") + ", so nothing about it is verified.");
        var observation = handle.Read();
        if (observation.Result == ObservationResult.VerifiedAbsent)
            return new StopOutcome(HostState.Gone, false, 0, "C006 recorded host has exited; nothing to stop.");
        // Metadata that could not be read is not an exit either.
        if (observation.Result != ObservationResult.Observed || observation.Facts is null)
            return new StopOutcome(HostState.Uncertain, false, 1, "C006 refusing to stop pid " + pid + ": " + OsProbe.Detail(observation.Detail, "the process metadata could not be read") + ", so its identity is unverified.");
        if (!IsTheRecordedHost(observation.Facts, host, probe))
            return new StopOutcome(HostState.Foreign, false, 1, "C006 refusing to stop pid " + pid + ": the live process is not the recorded host (name or start time differ).");
        var exited = handle.Exited;
        if (exited == ExitObservation.Exited)
            return new StopOutcome(HostState.Gone, false, 0, "C006 recorded host exited after its identity was verified; nothing was terminated.");
        if (exited == ExitObservation.Unknown)
            return new StopOutcome(HostState.Uncertain, false, 1, "C006 refusing to stop pid " + pid + ": whether it is still running could not be observed.");
        try
        {
            handle.Kill();
        }
        catch (Exception error)
        {
            // A failed terminate is never reported as a stop.
            return new StopOutcome(HostState.Running, false, 1, "C006 stop failed for pid " + pid + ": " + error.GetType().Name + ".");
        }
        if (!handle.WaitForExit(waitForExit))
            return new StopOutcome(HostState.Running, false, 1, "C006 stop did not complete: pid " + pid + " was still running " + ((int)waitForExit.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " s after the terminate request.");
        return new StopOutcome(HostState.Running, true, 0, "C006 recorded host stopped: pid " + pid + ".");
    }

    // Owned SQL container, volume, network and the private password file may only be removed
    // when every recorded host of this environment is verified not to be running, in every
    // evidence folder. A host whose state could not be observed blocks the release exactly like
    // a running one: the permission is only ever given on verified absence.
    public static ReleaseOutcome PlanRelease(IReadOnlyList<(string Folder, HostIdentity? Host)> records, OsProbe probe)
    {
        var hosts = records
            .Select(record => new HostStatus(record.Folder, record.Host?.Pid, record.Host is null ? HostState.NoRecord : Observe(record.Host, probe)))
            .ToArray();
        var blocking = hosts.Where(host => host.State is HostState.Running or HostState.Uncertain).ToArray();
        if (blocking.Length == 0) return new ReleaseOutcome(true, 0, "C006 every recorded host is verified absent or foreign; the owned resources may be released.", hosts);
        var names = string.Join("; ", blocking.Select(host => host.Folder + " pid " + host.Pid?.ToString(CultureInfo.InvariantCulture) + " (" + host.State + ")"));
        return new ReleaseOutcome(false, 1, "C006 refusing to release owned resources: a recorded host is running or could not be observed: " + names + ". Stop it or resolve the observation failure first.", hosts);
    }

    // A launch is not a started host. Either the launched process records its own identity and
    // is then observed alive on the contract API port, or this returns a failing exit code that
    // the caller propagates; a child that dies during startup is reported as such.
    public static StartOutcome AwaitStart(Func<HostIdentity?> readRecord, int launchedPid, TimeSpan timeout, OsProbe probe)
    {
        var startedAt = probe.Now();
        var deadline = startedAt + timeout;
        while (true)
        {
            var waited = (int)(probe.Now() - startedAt).TotalMilliseconds;
            var host = readRecord();
            if (host is not null)
            {
                if (host.Pid != launchedPid)
                    return new StartOutcome(false, 1, "C006 startup record names pid " + host.Pid.ToString(CultureInfo.InvariantCulture) + " but the launched process is " + launchedPid.ToString(CultureInfo.InvariantCulture) + ".", HostState.Foreign, waited, host);
                var state = Observe(host, probe);
                if (state == HostState.Uncertain)
                    return new StartOutcome(false, 1, "C006 launched host wrote its startup record but its state could not be observed.", state, waited, host);
                if (state != HostState.Running)
                    return new StartOutcome(false, 1, "C006 launched host wrote its startup record but is not running (" + state + ").", state, waited, host);
                if (!probe.IsListening(host.ApiPort))
                    return new StartOutcome(false, 1, "C006 launched host does not listen on the contract API port " + host.ApiPort.ToString(CultureInfo.InvariantCulture) + ".", state, waited, host);
                return new StartOutcome(true, 0, "C006 launched host is running and holds the API port: pid " + host.Pid.ToString(CultureInfo.InvariantCulture) + ".", state, waited, host);
            }
            var child = probe.ReadProcess(launchedPid);
            if (child.Result == ObservationResult.VerifiedAbsent)
                return new StartOutcome(false, 1, "C006 launched process " + launchedPid.ToString(CultureInfo.InvariantCulture) + " exited before it recorded a started host; see host.stderr.log.", HostState.Gone, waited, null);
            // An observation that failed is not an exit and not a reason to keep waiting.
            if (child.Result != ObservationResult.Observed)
                return new StartOutcome(false, 1, "C006 launched process " + launchedPid.ToString(CultureInfo.InvariantCulture) + " could not be observed while waiting for its startup record: " + OsProbe.Detail(child.Detail, "the observation failed") + ".", HostState.Uncertain, waited, null);
            if (probe.Now() >= deadline)
                return new StartOutcome(false, 1, "C006 no startup record within " + ((int)timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " s of the launch.", HostState.NoRecord, waited, null);
            probe.Wait();
        }
    }
}
