using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.CariAcceptance006;

public static class Evidence
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public const string ResumeEvidence = @"C:\src\GoymenERP-cari-acceptance-envelope-20260911\artifacts\cari-acceptance-006\account-status-20260913\c006-d861550bfb2e\resume";

    public static string ValidateResumeRecord(string json)
    {
        using var document = JsonDocument.Parse(json);
        var record = document.RootElement;
        if (record.GetProperty("task_id").GetString() != "C-DESKTOP-012"
            || record.GetProperty("resourceRunId").GetString() != RunSettings.ExpectedRun
            || record.GetProperty("instanceId").GetString() != RunSettings.ExpectedInstance
            || record.GetProperty("status").GetString() != "in_progress")
            throw new InvalidOperationException("C006 resume record identity mismatch.");
        var execution = record.GetProperty("executionRunId").GetString() ?? "";
        if (!Regex.IsMatch(execution, "\\Ac012-[0-9a-f]{12}\\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("C006 resume execution ID mismatch.");
        return execution;
    }

    public static string ResumeRunId() => ValidateResumeRecord(File.ReadAllText(Path.Combine(ResumeEvidence, "started.json")));

    public static void RequireFreshResume()
    {
        _ = ResumeRunId();
        foreach (var name in new[] { "host-started.json", "ENV_READY.json", "report.json" })
            if (File.Exists(Path.Combine(ResumeEvidence, name)))
                throw new InvalidOperationException("C006 resume already started or released; no duplicate.");
    }

    public static void Write(string name, object value, bool resume = false)
    {
        var path = Path.Combine(resume ? ResumeEvidence : RunSettings.ApprovedEvidence, name);
        if (File.Exists(path)) throw new InvalidOperationException("Existing C006 evidence is preserved: " + name);
        var temporary = path + ".tmp";
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json));
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: false);
    }
}

public static class EntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "product-source")
            {
                if (args.Length < 5)
                    throw new InvalidOperationException("Use product-source <backend-dependency> <frontend-worktree> <frontend-diff-base> <fixture-path> [...].");
                Console.WriteLine(SourceBinding.CaptureProductSource(args[1], args[2], args[3], args[4..]).GetRawText());
                return 0; // Source inputs only: no resource, grant, host or readiness publication.
            }
            if (args.Length == 0) throw new InvalidOperationException("Use serve | start-check <launched-pid> | smoke | snapshot | verify [expected-new-parties] | stop | release-check.");
            if (args[0] == "product-env") return await ProductEnvironment.RunAsync(args[1..]);
            var identity = RunSettings.Load();
            var exitCode = 0;
            switch (args[0])
            {
                case "bind":
                    if (args.Length != 1) throw new InvalidOperationException("C006 unexpected bind arguments.");
                    SourceBinding.Bind(identity);
                    Console.WriteLine("C006 exact source/run/build binding recorded; browser readiness is not asserted.");
                    break;
                case "source-check":
                    if (args.Length != 1) throw new InvalidOperationException("C006 unexpected source check arguments.");
                    _ = SourceBinding.Verify(identity);
                    Console.WriteLine("C006 exact source/run/build binding verified.");
                    break;
                case "contract-check":
                    if (args.Length != 1) throw new InvalidOperationException("C006 unexpected contract check arguments.");
                    var contractSource = SourceBinding.Verify(identity);
                    var contractBytes = File.ReadAllBytes(SourceBinding.ContractPath);
                    var contractHash = SourceBinding.ValidateAndHashExternalContract(contractBytes,
                        contractSource.AcceptanceHead, contractSource.CompiledHead);
                    Console.WriteLine(JsonSerializer.Serialize(new { contractSha256 = contractHash }));
                    break;
                case "wait-sql":
                    if (args.Length != 1) throw new InvalidOperationException("C006 unexpected SQL readiness arguments.");
                    _ = SourceBinding.Verify(identity);
                    await DatabaseEvidence.WaitForServerAsync(identity);
                    Console.WriteLine("C006 owned SQL server accepts connections.");
                    break;
                case "serve":
                case "serve-resume":
                    var resume = args[0] == "serve-resume";
                    if (args.Length != 1) throw new InvalidOperationException("C006 unexpected serve arguments.");
                    if (resume) Evidence.RequireFreshResume();
                    var executionRunId = resume ? Evidence.ResumeRunId() : identity.RunId;
                    var sourceBinding = SourceBinding.Verify(identity);
                    var startupDiskArtifacts = Directory.GetFiles(AppContext.BaseDirectory, "GoymenERP*.dll")
                        .Order(StringComparer.Ordinal).Select(file => new
                        {
                            path = Path.GetRelativePath(RunSettings.ApprovedRoot, file).Replace('\\', '/'),
                            sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))
                        }).ToArray();
                    await using (var factory = new AcceptanceFactory(identity))
                    {
                        factory.StartLoopback();
                        var process = Process.GetCurrentProcess();
                        Evidence.Write("host-started.json", new { identity.RunId, identity.InstanceId, identity.ApiBaseUrl,
                            pid = process.Id, startTimeUtc = process.StartTime.ToUniversalTime(),
                            observedAtUtc = DateTimeOffset.UtcNow, server = "Kestrel", capabilityRevision = AcceptanceProfile.Revision,
                            executionRunId, startupDiskArtifacts, sourceBinding,
                            artifactEvidenceMeaning = "Disk hashes captured before Kestrel startup; not memory-image attestation." }, resume);
                        Console.WriteLine("C006 exact-instance Kestrel started on " + identity.ApiBaseUrl);
                        await Task.Delay(Timeout.InfiniteTimeSpan);
                    }
                    break;
                case "start-check":
                case "start-check-resume":
                    var resumeStart = args[0] == "start-check-resume";
                    if (args.Length != 2) throw new InvalidOperationException("C006 start check needs exactly the launched process id.");
                    if (resumeStart) _ = Evidence.ResumeRunId();
                    var startFolder = resumeStart ? Evidence.ResumeEvidence : RunSettings.ApprovedEvidence;
                    var launchedPid = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
                    var startOutcome = Lifecycle.AwaitStart(() => Lifecycle.ReadHostIdentity(startFolder, identity), launchedPid,
                        TimeSpan.FromSeconds(Lifecycle.StartTimeoutSeconds), OsProbe.Real);
                    Console.WriteLine(JsonSerializer.Serialize(new { startOutcome.Started, state = startOutcome.State.ToString(),
                        startOutcome.WaitedMs, startOutcome.Message, folder = startFolder, launchedPid,
                        recordedPid = startOutcome.Host?.Pid, recordedStartTimeUtc = startOutcome.Host?.StartTimeUtc }, Evidence.Json));
                    exitCode = startOutcome.ExitCode;
                    break;
                case "stop":
                case "stop-resume":
                    var resumeStop = args[0] == "stop-resume";
                    if (args.Length != 1) throw new InvalidOperationException("C006 unexpected stop arguments.");
                    if (resumeStop) _ = Evidence.ResumeRunId();
                    var stopFolder = resumeStop ? Evidence.ResumeEvidence : RunSettings.ApprovedEvidence;
                    var stopOutcome = Lifecycle.Stop(Lifecycle.ReadHostIdentity(stopFolder, identity), OsProbe.Real);
                    Console.WriteLine(JsonSerializer.Serialize(new { stopOutcome.Stopped, state = stopOutcome.State.ToString(),
                        stopOutcome.Message, folder = stopFolder }, Evidence.Json));
                    exitCode = stopOutcome.ExitCode;
                    break;
                case "release-check":
                    if (args.Length != 1) throw new InvalidOperationException("C006 unexpected release check arguments.");
                    var recordedHosts = new (string Folder, HostIdentity? Host)[]
                    {
                        (RunSettings.ApprovedEvidence, Lifecycle.ReadHostIdentity(RunSettings.ApprovedEvidence, identity)),
                        (Evidence.ResumeEvidence, Lifecycle.ReadHostIdentity(Evidence.ResumeEvidence, identity)),
                    };
                    var releaseOutcome = Lifecycle.PlanRelease(recordedHosts, OsProbe.Real);
                    Console.WriteLine(JsonSerializer.Serialize(new { releaseOutcome.Allowed, releaseOutcome.Message,
                        hosts = releaseOutcome.Hosts.Select(host => new { host.Folder, host.Pid, state = host.State.ToString() }) }, Evidence.Json));
                    exitCode = releaseOutcome.ExitCode;
                    break;
                case "smoke":
                    _ = SourceBinding.Verify(identity);
                    await LiveSmoke.RunAsync(identity);
                    Console.WriteLine("C006 live HTTP/SQL smoke PASS; baseline.json and smoke.json written.");
                    break;
                case "snapshot":
                    Console.WriteLine(JsonSerializer.Serialize(await DatabaseEvidence.ReadAsync(identity), Evidence.Json));
                    break;
                case "status-snapshot":
                    _ = SourceBinding.Verify(identity);
                    Console.WriteLine(JsonSerializer.Serialize(await DatabaseEvidence.ReadStatusJourneyAsync(identity), Evidence.Json));
                    break;
                case "verify-account-status":
                    if (args.Length != 2) throw new InvalidOperationException("C006 status verification needs the actual browser measurement path.");
                    var statusSource = SourceBinding.Verify(identity);
                    var statusEnvironmentBytes = File.ReadAllBytes(Path.Combine(RunSettings.ApprovedEvidence, "environment.json"));
                    using (var statusEnvironment = JsonDocument.Parse(statusEnvironmentBytes))
                    using (var statusReady = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(RunSettings.ApprovedEvidence, "ENV_READY.json"))))
                    {
                        var baseBytes = File.ReadAllBytes(Path.Combine(RunSettings.ApprovedEvidence, "baseline.json"));
                        ContactJourneyMeasurement.ValidatePublication(identity, statusSource,
                            statusEnvironment.RootElement, statusReady.RootElement, SourceBinding.Hash(statusEnvironmentBytes),
                            SourceBinding.Hash(baseBytes), SourceBinding.Hash(File.ReadAllBytes(SourceBinding.ContractPath)));
                        var statusBaseBytes = File.ReadAllBytes(Path.Combine(RunSettings.ApprovedEvidence, "status-baseline.json"));
                        if (statusEnvironment.RootElement.GetProperty("statusBaselineSha256").GetString() != SourceBinding.Hash(statusBaseBytes))
                            throw new InvalidOperationException("C006 status baseline changed since publication.");
                        var statusBase = JsonSerializer.Deserialize<StatusJourneySnapshot>(statusBaseBytes)!;
                        if (JsonSerializer.Serialize(statusBase.Database) != JsonSerializer.Serialize(JsonSerializer.Deserialize<DatabaseSnapshot>(baseBytes)))
                            throw new InvalidOperationException("C006 baseline projections differ.");
                        var measurementBytes = File.ReadAllBytes(Path.GetFullPath(args[1]));
                        using var measurement = JsonDocument.Parse(measurementBytes);
                        var statusCurrent = await DatabaseEvidence.ReadStatusJourneyAsync(identity);
                        StatusJourneyMeasurement.Verify(identity, statusBase, statusCurrent, measurement.RootElement);
                        Evidence.Write("status-verification.json", new
                        {
                            identity.RunId, identity.InstanceId, observedAtUtc = DateTimeOffset.UtcNow,
                            source = statusSource, sqlEffectsVerified = true, expectedNewParties = 0, expectedNewAccounts = 0,
                            expectedLogicalUpdates = 2, expectedDurableResults = 3, expectedAudits = 2,
                            measurementPath = Path.GetFullPath(args[1]), measurementSha256 = SourceBinding.Hash(measurementBytes),
                            baselineSha256 = SourceBinding.Hash(statusBaseBytes), current = statusCurrent,
                            browserAcceptance = "NOT_ASSESSED_BY_SQL_VERIFIER", erpAccepted = false,
                        });
                    }
                    Console.WriteLine("C006 L-04 SQL effects verified; browser verdict remains a separate gate.");
                    break;
                case "verify":
                case "verify-resume":
                    _ = SourceBinding.Verify(identity);
                    var resumeVerify = args[0] == "verify-resume";
                    if (resumeVerify) _ = Evidence.ResumeRunId();
                    var count = args.Length == 2 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
                    var baseline = JsonSerializer.Deserialize<DatabaseSnapshot>(File.ReadAllText(
                        Path.Combine(resumeVerify ? Evidence.ResumeEvidence : RunSettings.ApprovedEvidence, "baseline.json")))!;
                    var current = await DatabaseEvidence.ReadAsync(identity);
                    DatabaseEvidence.VerifyBrowserJourney(identity, baseline, current, count);
                    Console.WriteLine(JsonSerializer.Serialize(new { passed = true, expectedNewParties = count, baseline, current }, Evidence.Json));
                    break;
                case "verify-contact-edit":
                    if (args.Length != 2) throw new InvalidOperationException("C006 contact verification needs its actual browser measurement path.");
                    var contactSource = SourceBinding.Verify(identity);
                    var environmentBytes = File.ReadAllBytes(Path.Combine(RunSettings.ApprovedEvidence, "environment.json"));
                    using (var environment = JsonDocument.Parse(environmentBytes))
                    using (var ready = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(RunSettings.ApprovedEvidence, "ENV_READY.json"))))
                    {
                        var baselineBytes = File.ReadAllBytes(Path.Combine(RunSettings.ApprovedEvidence, "baseline.json"));
                        ContactJourneyMeasurement.ValidatePublication(identity, contactSource,
                            environment.RootElement, ready.RootElement, SourceBinding.Hash(environmentBytes), SourceBinding.Hash(baselineBytes),
                            SourceBinding.Hash(File.ReadAllBytes(SourceBinding.ContractPath)));
                        var contactBaseline = JsonSerializer.Deserialize<DatabaseSnapshot>(baselineBytes)!;
                        var measurementBytes = File.ReadAllBytes(Path.GetFullPath(args[1]));
                        var contactCurrent = await DatabaseEvidence.ReadAsync(identity);
                        using var measurement = JsonDocument.Parse(measurementBytes);
                        ContactJourneyMeasurement.Verify(identity, contactBaseline, contactCurrent, measurement.RootElement);
                        Evidence.Write("contact-verification.json", new
                        {
                            identity.RunId, identity.InstanceId, observedAtUtc = DateTimeOffset.UtcNow,
                            source = contactSource, sqlEffectsVerified = true, expectedNewParties = 0,
                            expectedLogicalUpdates = 6, expectedDurableResults = 7,
                            measurementPath = Path.GetFullPath(args[1]), measurementSha256 = SourceBinding.Hash(measurementBytes),
                            baselineSha256 = SourceBinding.Hash(baselineBytes), current = contactCurrent,
                            browserAcceptance = "NOT_ASSESSED_BY_SQL_VERIFIER", erpAccepted = false,
                        });
                        Console.WriteLine("C006 L-03 exact SQL effects verified; browser verdict remains a separate gate.");
                    }
                    break;
                default: throw new InvalidOperationException("Unknown C006 operation.");
            }
            return exitCode;
        }
        catch (Exception error)
        {
            // Connection strings/secrets and arbitrary exception payloads never enter logs.
            Console.Error.WriteLine("C006 operation failed: " + error.GetType().Name);
            if (error is Microsoft.Data.SqlClient.SqlException sql) Console.Error.WriteLine("SQL error number: " + sql.Number);
            if (error is InvalidOperationException && error.Message.StartsWith("C006", StringComparison.Ordinal))
                Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
