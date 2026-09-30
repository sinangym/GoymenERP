using System.ComponentModel;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using GoymenERP.Modules.Documents.Infrastructure;
using GoymenERP.Modules.Documents.Infrastructure.Scanning;
using GoymenERP.Modules.Documents.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Documents.UnitTests;

/// <summary>
/// The production adapters (user decision 2026-09-30): the server-directory object store on the company's own disk/NAS
/// and the Windows Defender scanner, with the fail-closed registration. Defender itself is NOT exercised here: the
/// adapter's verdict mapping runs against an injected process runner, and the real runner against the OS shell only.
/// A real Defender scan is a manual runbook step (README), not a test. SENTETIK / NOT_ENV.
/// </summary>
public sealed class DocumentsProductionAdaptersTests : IDisposable
{
    private static readonly Guid CompanyA = Harness.CompanyA;
    private readonly string temp = Path.Combine(Path.GetTempPath(), $"goymen-docvault-prod-{Guid.NewGuid():N}");
    private readonly string root;
    private readonly string quarantine;
    private readonly string defender;

    public DocumentsProductionAdaptersTests()
    {
        root = Directory.CreateDirectory(Path.Combine(temp, "store")).FullName;
        quarantine = Path.Combine(temp, "scan");
        var bin = Directory.CreateDirectory(Path.Combine(temp, "defender")).FullName;
        defender = Path.Combine(bin, "MpCmdRun.exe");
        File.WriteAllBytes(defender, [0]);
    }

    public void Dispose()
    {
        if (!Directory.Exists(temp))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(temp, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(temp, recursive: true);
    }

    // ---- server-directory object store ----------------------------------------------------------

    [Fact]
    public void The_server_store_opens_only_an_existing_fully_qualified_root_and_never_creates_it()
    {
        Assert.Throws<InvalidOperationException>(() => ServerDirectoryDocumentObjectStore.Open("relative/store"));
        Assert.Throws<InvalidOperationException>(() => ServerDirectoryDocumentObjectStore.Open(" "));
        var missing = Path.Combine(temp, "missing");
        Assert.Throws<InvalidOperationException>(() => ServerDirectoryDocumentObjectStore.Open(missing));
        Assert.False(Directory.Exists(missing));
        Assert.Throws<InvalidOperationException>(() => ServerDirectoryDocumentObjectStore.Open(defender));

        var store = ServerDirectoryDocumentObjectStore.Open(root);
        Assert.Equal(Path.GetFullPath(root), store.RootPath);
        Assert.Empty(Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task The_server_store_writes_once_read_only_leaves_no_temporary_file_and_a_repeat_put_is_a_no_op()
    {
        var store = ServerDirectoryDocumentObjectStore.Open(root);
        var pdf = DocumentContentRulesTests.Pdf;
        var sha = DocumentContentHash.Compute(pdf);
        var key = DocumentObjectKeys.For(CompanyA, sha);

        Assert.Null(await store.ReadAsync(key));
        await store.PutAsync(key, pdf, sha);
        var path = Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        Assert.Equal(Path.Combine(root, "companies", CompanyA.ToString("N"), "sha256", sha), path);
        Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
        var written = File.GetLastWriteTimeUtc(path);

        await store.PutAsync(key, pdf, sha);
        Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
        Assert.Equal(pdf, await store.ReadAsync(key));

        // Bytes that are not the key's hash are refused before anything is written.
        await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(key, DocumentContentRulesTests.Png, sha));
        await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(key, pdf, DocumentContentHash.Compute(DocumentContentRulesTests.Png)));
    }

    [Fact]
    public async Task Concurrent_puts_of_one_key_leave_exactly_one_object()
    {
        var store = ServerDirectoryDocumentObjectStore.Open(root);
        var png = DocumentContentRulesTests.Png;
        var sha = DocumentContentHash.Compute(png);
        var key = DocumentObjectKeys.For(CompanyA, sha);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.PutAsync(key, png, sha))));

        Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        Assert.Equal(png, await store.ReadAsync(key));
    }

    [Fact]
    public async Task An_object_altered_on_disk_is_never_served_and_never_overwritten()
    {
        var store = ServerDirectoryDocumentObjectStore.Open(root);
        var pdf = DocumentContentRulesTests.Pdf;
        var sha = DocumentContentHash.Compute(pdf);
        var key = DocumentObjectKeys.For(CompanyA, sha);
        await store.PutAsync(key, pdf, sha);
        var path = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Single();
        File.SetAttributes(path, FileAttributes.Normal);
        await File.WriteAllBytesAsync(path, DocumentContentRulesTests.Png);

        await Assert.ThrowsAsync<DocumentObjectStoreUnavailableException>(() => store.ReadAsync(key));
        await Assert.ThrowsAsync<DocumentObjectStoreUnavailableException>(() => store.PutAsync(key, pdf, sha));
        Assert.Equal(DocumentContentRulesTests.Png, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task The_server_store_accepts_only_the_content_key_shape_inside_its_root()
    {
        var store = ServerDirectoryDocumentObjectStore.Open(root);
        var sha = DocumentContentHash.Compute(DocumentContentRulesTests.Pdf);
        foreach (var bad in new[]
                 {
                     "../x", $"companies/{CompanyA:N}/sha256/../../x", $"companies/{CompanyA:D}/sha256/{sha}",
                     $"companies/{CompanyA:N}/sha256/{sha.ToUpperInvariant()}", $"/companies/{CompanyA:N}/sha256/{sha}",
                     $"companies\\{CompanyA:N}\\sha256\\{sha}", $"companies/{CompanyA:N}/sha256/.{sha[1..]}",
                 })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadAsync(bad));
            await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(bad, DocumentContentRulesTests.Pdf, sha));
        }

        Assert.Empty(Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories));
    }

    // ---- Windows Defender adapter (injected runner) -------------------------------------------

    [Fact]
    public void The_defender_adapter_refuses_a_missing_or_relative_executable_a_bad_timeout_and_a_relative_quarantine()
    {
        var runner = new FakeRunner(_ => DocumentScanProcessResult.Exited(0));
        Assert.Throws<InvalidOperationException>(() => WindowsDefenderDocumentScanner.Create(Options(Path.Combine(temp, "none", "MpCmdRun.exe")), runner));
        Assert.Throws<InvalidOperationException>(() => WindowsDefenderDocumentScanner.Create(Options("MpCmdRun.exe"), runner));
        Assert.Throws<InvalidOperationException>(() => WindowsDefenderDocumentScanner.Create(Options() with { Timeout = TimeSpan.Zero }, runner));
        Assert.Throws<InvalidOperationException>(() => WindowsDefenderDocumentScanner.Create(Options() with { Timeout = TimeSpan.FromHours(1) }, runner));
        Assert.Throws<InvalidOperationException>(() => WindowsDefenderDocumentScanner.Create(Options() with { QuarantineDirectory = "scan" }, runner));
        Assert.Throws<InvalidOperationException>(() => WindowsDefenderDocumentScanner.Create(Options() with { MaximumConcurrentScans = 0 }, runner));

        using var scanner = WindowsDefenderDocumentScanner.Create(Options(), runner);
        Assert.Equal(WindowsDefenderDocumentScanner.Id, scanner.ScannerId);
        Assert.True(Directory.Exists(quarantine));
        Assert.Empty(Directory.GetFiles(quarantine));
    }

    [Fact]
    public async Task Exit_code_0_is_clean_for_the_quarantined_copy_scanned_with_the_exact_arguments_and_the_copy_is_removed()
    {
        var pdf = DocumentContentRulesTests.Pdf;
        string? scanned = null;
        var runner = new FakeRunner(call =>
        {
            scanned = call.Arguments[4];
            Assert.Equal(pdf, File.ReadAllBytes(scanned));
            return DocumentScanProcessResult.Exited(0);
        });
        using var scanner = WindowsDefenderDocumentScanner.Create(Options(), runner);

        Assert.Equal(DocumentScanVerdictV1.Clean, await scanner.ScanAsync(pdf, DocumentFileKind.Pdf));

        var call = Assert.Single(runner.Calls);
        Assert.Equal(defender, call.ExecutablePath);
        Assert.Equal(["-Scan", "-ScanType", "3", "-File", scanned!, "-DisableRemediation"], call.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(45), call.Timeout);
        Assert.Equal(Path.GetFullPath(quarantine), Path.GetDirectoryName(scanned));
        Assert.EndsWith(".pdf", scanned, StringComparison.Ordinal);
        Assert.False(File.Exists(scanned));
        Assert.Empty(Directory.GetFiles(quarantine));
    }

    [Fact]
    public async Task Exit_code_2_is_infected_and_the_copy_is_removed()
    {
        using var scanner = WindowsDefenderDocumentScanner.Create(Options(), new FakeRunner(_ => DocumentScanProcessResult.Exited(2)));

        Assert.Equal(DocumentScanVerdictV1.Infected, await scanner.ScanAsync(DocumentContentRulesTests.Png, DocumentFileKind.Png));
        Assert.Empty(Directory.GetFiles(quarantine));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(unchecked((int)0x80508023))]
    public async Task Any_other_exit_code_is_not_scanned(int exitCode)
    {
        using var scanner = WindowsDefenderDocumentScanner.Create(Options(), new FakeRunner(_ => DocumentScanProcessResult.Exited(exitCode)));

        Assert.Equal(DocumentScanVerdictV1.Unavailable, await scanner.ScanAsync(DocumentContentRulesTests.Pdf, DocumentFileKind.Pdf));
        Assert.Empty(Directory.GetFiles(quarantine));
    }

    [Fact]
    public async Task A_timeout_a_process_that_cannot_start_and_a_copy_changed_or_removed_during_the_scan_are_not_scanned()
    {
        var pdf = DocumentContentRulesTests.Pdf;
        var cases = new Func<RunnerCall, DocumentScanProcessResult>[]
        {
            _ => DocumentScanProcessResult.Timeout,
            _ => throw new Win32Exception(2, "SENTETIK: the system cannot find the file"),
            _ => throw new InvalidOperationException("SENTETIK: process did not start"),
            call =>
            {
                File.Delete(call.Arguments[4]);
                return DocumentScanProcessResult.Exited(0);
            },
            call =>
            {
                File.WriteAllBytes(call.Arguments[4], DocumentContentRulesTests.Png);
                return DocumentScanProcessResult.Exited(0);
            },
        };

        foreach (var behaviour in cases)
        {
            using var scanner = WindowsDefenderDocumentScanner.Create(Options(), new FakeRunner(behaviour));
            Assert.Equal(DocumentScanVerdictV1.Unavailable, await scanner.ScanAsync(pdf, DocumentFileKind.Pdf));
            Assert.Empty(Directory.GetFiles(quarantine));
        }
    }

    [Fact]
    public async Task The_callers_cancellation_propagates_and_the_copy_is_removed()
    {
        using var cancellation = new CancellationTokenSource();
        using var scanner = WindowsDefenderDocumentScanner.Create(Options(), new FakeRunner((Func<RunnerCall, DocumentScanProcessResult>)(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        })));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(DocumentContentRulesTests.Pdf, DocumentFileKind.Pdf, cancellation.Token));
        Assert.Empty(Directory.GetFiles(quarantine));
    }

    [Fact]
    public async Task At_most_the_configured_number_of_scans_run_at_once()
    {
        var running = 0;
        var peak = 0;
        var runner = new FakeRunner(async _ =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await Task.Delay(40);
            Interlocked.Decrement(ref running);
            return DocumentScanProcessResult.Exited(0);
        });
        using var scanner = WindowsDefenderDocumentScanner.Create(Options() with { MaximumConcurrentScans = 2 }, runner);

        var verdicts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => scanner.ScanAsync(DocumentContentRulesTests.Jpeg, DocumentFileKind.Jpeg)));

        Assert.All(verdicts, verdict => Assert.Equal(DocumentScanVerdictV1.Clean, verdict));
        Assert.InRange(peak, 1, 2);
        Assert.Equal(6, runner.Calls.Count);
    }

    // ---- the real process runner (the OS shell, never Defender) --------------------------------

    [Fact]
    public async Task The_system_runner_reports_the_exit_code_and_kills_a_process_that_outlives_the_timeout()
    {
        var runner = new SystemDocumentScanProcessRunner();
        var (shell, exitTwo, sleep) = OperatingSystem.IsWindows()
            ? (Path.Combine(Environment.SystemDirectory, "cmd.exe"), new[] { "/d", "/c", "exit 2" }, new[] { "/d", "/c", "ping -n 30 127.0.0.1 >NUL" })
            : ("/bin/sh", new[] { "-c", "exit 2" }, new[] { "-c", "sleep 30" });

        Assert.Equal(DocumentScanProcessResult.Exited(2), await runner.RunAsync(shell, exitTwo, TimeSpan.FromSeconds(30)));

        var started = DateTime.UtcNow;
        Assert.Equal(DocumentScanProcessResult.Timeout, await runner.RunAsync(shell, sleep, TimeSpan.FromMilliseconds(500)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20));
    }

    // ---- registration ------------------------------------------------------------------------

    [Fact]
    public void Production_registers_the_server_store_and_defender_only_together_and_fails_startup_on_any_partial_or_invalid_setting()
    {
        var rootSetting = (DocumentsModuleExtensions.ObjectStoreRootPathKey, root);
        var defenderSetting = (DocumentsModuleExtensions.DefenderExecutablePathKey, defender);
        var quarantineSetting = (DocumentsModuleExtensions.DefenderQuarantinePathKey, quarantine);

        // Nothing configured: fail-closed 503 (no store, no scanner), 20 MiB bound.
        var unconfigured = Services(false, (DocumentsModuleExtensions.ScannerModeKey, DocumentsModuleExtensions.NoScannerMode));
        Assert.Null(unconfigured.GetService<IDocumentObjectStoreV1>());
        Assert.Null(unconfigured.GetService<IDocumentContentScannerV1>());
        Assert.Equal(20L * 1024 * 1024, unconfigured.GetRequiredService<DocumentsServiceBindings>().MaximumUploadBytes);

        // One without the other, the development scanner, or an explicit "no scanner" beside a store: startup fails.
        Assert.Throws<InvalidOperationException>(() => Services(false, rootSetting));
        Assert.Throws<InvalidOperationException>(() => Services(false, defenderSetting, quarantineSetting));
        Assert.Throws<InvalidOperationException>(() => Services(false,
            (DocumentsModuleExtensions.ScannerModeKey, DocumentsModuleExtensions.WindowsDefenderScannerMode)));
        Assert.Throws<InvalidOperationException>(() => Services(false, rootSetting, defenderSetting, quarantineSetting,
            (DocumentsModuleExtensions.ScannerModeKey, DocumentsModuleExtensions.DevelopmentScannerMode)));
        Assert.Throws<InvalidOperationException>(() => Services(false, rootSetting, defenderSetting, quarantineSetting,
            (DocumentsModuleExtensions.ScannerModeKey, DocumentsModuleExtensions.NoScannerMode)));

        // Invalid paths or numbers: startup fails.
        Assert.Throws<InvalidOperationException>(() => Services(false, (DocumentsModuleExtensions.ObjectStoreRootPathKey, Path.Combine(temp, "missing")),
            defenderSetting, quarantineSetting));
        Assert.Throws<InvalidOperationException>(() => Services(false, rootSetting,
            (DocumentsModuleExtensions.DefenderExecutablePathKey, Path.Combine(temp, "none", "MpCmdRun.exe")), quarantineSetting));
        Assert.Throws<InvalidOperationException>(() => Services(false, rootSetting, defenderSetting, quarantineSetting,
            (DocumentsModuleExtensions.DefenderTimeoutSecondsKey, "0")));

        if (!OperatingSystem.IsWindows())
        {
            // Defender exists only on Windows: a configured production vault elsewhere fails startup.
            Assert.Throws<InvalidOperationException>(() => Services(false, rootSetting, defenderSetting, quarantineSetting));
            return;
        }

        var configured = Services(false, rootSetting, defenderSetting, quarantineSetting,
            (DocumentsModuleExtensions.ScannerModeKey, DocumentsModuleExtensions.WindowsDefenderScannerMode),
            (DocumentsModuleExtensions.DefenderTimeoutSecondsKey, "90"));
        Assert.Equal(Path.GetFullPath(root), Assert.IsType<ServerDirectoryDocumentObjectStore>(configured.GetRequiredService<IDocumentObjectStoreV1>()).RootPath);
        var scanner = Assert.IsType<WindowsDefenderDocumentScanner>(configured.GetRequiredService<IDocumentContentScannerV1>());
        Assert.Equal(defender, scanner.Options.ExecutablePath);
        Assert.Equal(TimeSpan.FromSeconds(90), scanner.Options.Timeout);
        Assert.Equal(Path.GetFullPath(quarantine), scanner.Options.QuarantineDirectory);

        // The mode may be left out: the two paths are the explicit configuration.
        Assert.IsType<WindowsDefenderDocumentScanner>(Services(false, rootSetting, defenderSetting, quarantineSetting)
            .GetRequiredService<IDocumentContentScannerV1>());
    }

    [Fact]
    public void Development_may_opt_into_the_defender_adapter_beside_its_stand_in_store()
    {
        var settings = new[]
        {
            (DocumentsModuleExtensions.ObjectStoreRootPathKey, root),
            (DocumentsModuleExtensions.ScannerModeKey, DocumentsModuleExtensions.WindowsDefenderScannerMode),
            (DocumentsModuleExtensions.DefenderExecutablePathKey, defender),
            (DocumentsModuleExtensions.DefenderQuarantinePathKey, quarantine),
        };
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<InvalidOperationException>(() => Services(true, settings));
            return;
        }

        var development = Services(true, settings);
        Assert.IsType<FileSystemDocumentObjectStore>(development.GetRequiredService<IDocumentObjectStoreV1>());
        Assert.IsType<WindowsDefenderDocumentScanner>(development.GetRequiredService<IDocumentContentScannerV1>());
        Assert.Throws<InvalidOperationException>(() => Services(true, settings[0], settings[1]));
    }

    private WindowsDefenderScannerOptions Options(string? executable = null) =>
        new(executable ?? defender, TimeSpan.FromSeconds(45), quarantine);

    private static ServiceProvider Services(bool allowDevelopment, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(
                [new("DB_CONNECTION_STRING", "Server=SENTETIK.invalid;Database=SENTETIK;"), .. settings.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))]))
            .Build();
        return new ServiceCollection().AddDocumentsModule(configuration, allowDevelopmentStorageAndScanner: allowDevelopment).BuildServiceProvider();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private sealed record RunnerCall(string ExecutablePath, IReadOnlyList<string> Arguments, TimeSpan Timeout);

    private sealed class FakeRunner : IDocumentScanProcessRunner
    {
        private readonly Func<RunnerCall, Task<DocumentScanProcessResult>> behaviour;

        public FakeRunner(Func<RunnerCall, DocumentScanProcessResult> behaviour) => this.behaviour = call => Task.FromResult(behaviour(call));

        public FakeRunner(Func<RunnerCall, Task<DocumentScanProcessResult>> behaviour) => this.behaviour = behaviour;

        public List<RunnerCall> Calls { get; } = [];

        public Task<DocumentScanProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var call = new RunnerCall(executablePath, [.. arguments], timeout);
            lock (Calls)
            {
                Calls.Add(call);
            }

            return behaviour(call);
        }
    }
}
