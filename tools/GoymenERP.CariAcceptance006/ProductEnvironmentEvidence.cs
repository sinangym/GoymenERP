using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Product.UnitTests.SqlAcceptance;
using Microsoft.Data.SqlClient;

namespace GoymenERP.CariAcceptance006;

internal sealed record ProductEnvironmentSnapshot(string DatabaseName, bool Rcsi, ProductSqlSnapshot Product,
    JsonElement Audit, JsonElement Terminals);

internal static class ProductEnvironmentEvidence
{
    internal static ProductSqlFixture Fixture(ProductEnvironmentSettings settings) => new(settings.RunId,
        Guid.Parse(settings.InstanceId), settings.DatabaseName, ProductEnvironmentSettings.CodeGroupId);

    internal static async Task<ProductEnvironmentSnapshot> SnapshotAsync(ProductEnvironmentSettings settings, CancellationToken cancellationToken)
    {
        await using var sql = new SqlConnection(settings.DatabaseConnection());
        await sql.OpenAsync(cancellationToken);
        await using var metadata = sql.CreateCommand();
        metadata.CommandText = "SELECT DB_NAME(), is_read_committed_snapshot_on FROM sys.databases WHERE database_id=DB_ID();";
        await using var metadataReader = await metadata.ExecuteReaderAsync(cancellationToken);
        if (!await metadataReader.ReadAsync(cancellationToken) || metadataReader.GetString(0) != settings.DatabaseName
            || !metadataReader.GetBoolean(1)) throw new InvalidOperationException("Product isolated database/RCSI identity is not ready.");
        await metadataReader.DisposeAsync();
        var product = await ProductSqlSnapshot.ReadAsync(Fixture(settings), sql, null, cancellationToken);
        var shared = await ProductSharedStateSnapshot.ReadAsync(sql, cancellationToken);
        return new(settings.DatabaseName, true, product, shared.Audit, shared.Terminals);
    }

    internal static HttpClient Client(string actor)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri(ProductEnvironmentSettings.ApiBaseUrl), Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Add("X-Development-User", actor);
        client.DefaultRequestHeaders.Add("X-Company-Id", ProductEnvironmentSettings.CompanyId.ToString("D"));
        return client;
    }

    internal static async Task SmokeAsync(ProductEnvironmentSettings settings, CancellationToken cancellationToken)
    {
        _ = await VerifyLiveAsync(settings, cancellationToken);
        var before = await SnapshotAsync(settings, cancellationToken);
        if (before.Product.Products.Count != 0 || before.Product.Registrations.Count != 0
            || before.Product.Counters.Count != 0 || before.Audit.GetArrayLength() != 0 || before.Terminals.GetArrayLength() != 0
            || before.Product.Groups.Count != 1 || before.Product.Groups[0].Id != ProductEnvironmentSettings.CodeGroupId)
            throw new InvalidOperationException("Product smoke requires this run's fresh empty Product fixture.");
        using var authorized = Client(ProductEnvironmentSettings.Actor);
        using var denied = Client(ProductEnvironmentSettings.UnauthorizedActor);
        foreach (var path in new[] { "/api/v1/products", "/api/v1/products/code-groups" })
        {
            using var response = await authorized.GetAsync(path, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Product authorized read smoke failed.");
            using var forbidden = await denied.GetAsync(path, cancellationToken);
            if (forbidden.StatusCode != HttpStatusCode.Forbidden) throw new InvalidOperationException("Product company-only read was not denied.");
        }
        using (var response = await authorized.GetAsync("/api/v1/products/82000000-0000-0000-0000-000000000099", cancellationToken))
            if (response.StatusCode != HttpStatusCode.NotFound) throw new InvalidOperationException("Product missing detail smoke failed.");
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products"))
        {
            request.Headers.Add("Idempotency-Key", settings.RunId + "-denied-smoke");
            request.Content = JsonContent.Create(ProductEnvironment.DeniedCreateRequest());
            using var response = await denied.SendAsync(request, cancellationToken);
            // A stable forbidden body code is not part of the Product contract.
            if (response.StatusCode != HttpStatusCode.Forbidden) throw new InvalidOperationException("Product company-only write was not denied.");
        }
        await RequireCapabilitiesAsync(authorized, true, cancellationToken);
        await RequireCapabilitiesAsync(denied, false, cancellationToken);
        var after = await SnapshotAsync(settings, cancellationToken);
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(before), JsonSerializer.SerializeToElement(after)))
            throw new InvalidOperationException("Product read/forbidden smoke changed durable state.");
        _ = await VerifyLiveAsync(settings, cancellationToken);
        settings.Write("baseline.json", before);
        settings.Write("smoke.json", new { settings.RunId, settings.InstanceId, observedAtUtc = DateTimeOffset.UtcNow,
            source = settings.Source, acceptedContractSha256 = settings.ContractHash,
            passed = true, readsAndDeniedWriteHaveNoEffects = true, browserAcceptance = "NOT_RUN", erpAccepted = false });
    }

    private static async Task RequireCapabilitiesAsync(HttpClient client, bool authorized, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/api/v1/shell/capabilities", cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Product Shell capability read failed.");
        using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        ProductEnvironment.ValidateCapabilities(body.RootElement, authorized);
    }

    internal static async Task<JsonElement> VerifyLiveAsync(ProductEnvironmentSettings settings, CancellationToken cancellationToken)
    {
        var source = settings.VerifySource();
        using var hostDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(settings.EvidenceRoot, "host-started.json")));
        SourceBinding.RequireProductSourceMatch(source, hostDocument.RootElement.GetProperty("source"));
        var host = Lifecycle.ReadHostIdentity(settings.EvidenceRoot, settings.LifecycleIdentity)
            ?? throw new InvalidOperationException("Product has no recorded host.");
        var release = Lifecycle.PlanRelease([(settings.EvidenceRoot, host)], OsProbe.Real);
        if (release.Hosts.Count != 1 || release.Hosts[0].State != HostState.Running)
            throw new InvalidOperationException("Product host PID/start identity is not live.");
        // Fixed observation command; neither endpoint nor process selector is
        // supplied by an untrusted manifest. Do not equate an open port with owner.
        var listeners = Run("powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
            "@(Get-NetTCPConnection -State Listen -LocalPort 26080 -ErrorAction Stop | Where-Object { $_.LocalAddress -eq '127.0.0.1' } | Select-Object -ExpandProperty OwningProcess -Unique) | ConvertTo-Json -Compress");
        using var listenerJson = JsonDocument.Parse(listeners);
        var listenerPids = listenerJson.RootElement.ValueKind == JsonValueKind.Array
            ? listenerJson.RootElement.EnumerateArray().Select(p => p.GetInt32()).ToArray() : [listenerJson.RootElement.GetInt32()];
        if (listenerPids.Length != 1 || listenerPids[0] != host.Pid)
            throw new InvalidOperationException("Product API listener is not owned by the recorded host.");
        using var resources = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(settings.EvidenceRoot, "resources.json")));
        var r = resources.RootElement;
        if (r.GetProperty("runId").GetString() != settings.RunId || r.GetProperty("instanceId").GetString() != settings.InstanceId
            || r.GetProperty("composeProject").GetString() != settings.RunId || r.GetProperty("databaseName").GetString() != settings.DatabaseName)
            throw new InvalidOperationException("Product resource record identity differs from the contract.");
        using var container = JsonDocument.Parse(Run("docker", "inspect", "--format",
            "{\"id\":{{json .Id}},\"name\":{{json .Name}},\"labels\":{{json .Config.Labels}},\"ports\":{{json .NetworkSettings.Ports}},\"running\":{{json .State.Running}},\"mounts\":{{json .Mounts}},\"networks\":{{json .NetworkSettings.Networks}}}", settings.RunId + "-sql"));
        using var volume = JsonDocument.Parse(Run("docker", "volume", "inspect", "--format",
            "{\"name\":{{json .Name}},\"labels\":{{json .Labels}}}", settings.RunId + "-data"));
        using var network = JsonDocument.Parse(Run("docker", "network", "inspect", "--format",
            "{\"id\":{{json .Id}},\"name\":{{json .Name}},\"labels\":{{json .Labels}}}", settings.RunId + "-network"));
        var c = container.RootElement; var v = volume.RootElement; var n = network.RootElement;
        RequireLabels(c.GetProperty("labels"), settings); RequireLabels(v.GetProperty("labels"), settings); RequireLabels(n.GetProperty("labels"), settings);
        var ports = c.GetProperty("ports").GetProperty("1433/tcp");
        if (!c.GetProperty("running").GetBoolean() || c.GetProperty("id").GetString() != r.GetProperty("container").GetProperty("id").GetString()
            || c.GetProperty("name").GetString() != "/" + settings.RunId + "-sql"
            || v.GetProperty("name").GetString() != settings.RunId + "-data"
            || n.GetProperty("name").GetString() != settings.RunId + "-network"
            || n.GetProperty("id").GetString() != r.GetProperty("network").GetProperty("id").GetString()
            || ports.GetArrayLength() != 1 || ports[0].GetProperty("HostIp").GetString() != "127.0.0.1"
            || ports[0].GetProperty("HostPort").GetString() != "26433"
            || !c.GetProperty("mounts").EnumerateArray().Any(m => m.GetProperty("Type").GetString() == "volume"
                && m.GetProperty("Name").GetString() == settings.RunId + "-data" && m.GetProperty("Destination").GetString() == "/var/opt/mssql")
            || c.GetProperty("networks").GetProperty(settings.RunId + "-network").GetProperty("NetworkID").GetString() != n.GetProperty("id").GetString())
            throw new InvalidOperationException("Product actual container/network/volume/SQL-port ownership differs.");
        await using var sql = new SqlConnection(settings.DatabaseConnection());
        await sql.OpenAsync(cancellationToken);
        await Fixture(settings).VerifyDatabaseAsync(sql, null, cancellationToken);
        using var authorized = Client(ProductEnvironmentSettings.Actor);
        using var unauthorized = Client(ProductEnvironmentSettings.UnauthorizedActor);
        await RequireCapabilitiesAsync(authorized, true, cancellationToken);
        await RequireCapabilitiesAsync(unauthorized, false, cancellationToken);
        return JsonSerializer.SerializeToElement(new
        {
            databaseName = settings.DatabaseName, sqlEndpoint = ProductEnvironmentSettings.SqlEndpoint, apiBaseUrl = ProductEnvironmentSettings.ApiBaseUrl,
            composeProject = settings.RunId, containerId = c.GetProperty("id").GetString(), containerName = settings.RunId + "-sql",
            volumeName = settings.RunId + "-data", networkId = n.GetProperty("id").GetString(), networkName = settings.RunId + "-network",
            resourceRunId = settings.RunId, resourceInstanceId = settings.InstanceId, hostPid = host.Pid, hostStartTimeUtc = host.StartTimeUtc,
        });
    }

    private static void RequireLabels(JsonElement labels, ProductEnvironmentSettings settings)
    {
        if (labels.GetProperty("com.goymen.acceptance.task").GetString() != "U-01-U-02"
            || labels.GetProperty("com.goymen.acceptance.contract").GetString() != "PRODUCT-U0102-L-v1"
            || labels.GetProperty("com.goymen.acceptance.run").GetString() != settings.RunId
            || labels.GetProperty("com.goymen.acceptance.instance").GetString() != settings.InstanceId)
            throw new InvalidOperationException("Product resource labels differ from the accepted run.");
    }

    internal static string VerifyTestedBuild(ProductEnvironmentSettings settings, JsonElement hostSource)
    {
        SourceBinding.RequireProductSourceMatch(settings.Source, hostSource);
        var summaryPath = Environment.GetEnvironmentVariable("GOYMEN_PRODUCT_BACKEND_TESTS_SUMMARY") ?? "";
        if (!Path.IsPathFullyQualified(summaryPath) || Path.GetFileName(summaryPath) != "summary.json")
            throw new InvalidOperationException("Product requires the actual local BackendVerify summary path.");
        var summaryBytes = File.ReadAllBytes(summaryPath);
        using var tests = JsonDocument.Parse(summaryBytes);
        // Only adapt field spelling. Both source objects above came from actual
        // capture/startup and must match in full before the established C006
        // tested-DLL validator can compare their local executed build inventory.
        static object Project(JsonElement source) => new { AcceptanceHead = source.GetProperty("acceptanceHead").GetString(),
            DiskArtifacts = source.GetProperty("diskArtifacts") };
        var payload = JsonSerializer.Serialize(new { source = Project(settings.Source), host = new { sourceBinding = Project(hostSource) }, tests = tests.RootElement });
        const string script = "import importlib.util,sys; s=importlib.util.spec_from_file_location('publisher',sys.argv[1]); "
            + "m=importlib.util.module_from_spec(s); s.loader.exec_module(m); p=m.strict_json(sys.stdin.read()); "
            + "m.validate_tested_build(p['source'],p['host'],p['tests']); print('Product local tested runtime binding PASS')";
        _ = RunWithInput(payload, "python", "-I", "-S", "-B", "-X", "utf8", "-c", script,
            Path.Combine(SourceBinding.ProductWorktree, "tools/cari-acceptance-006/publish.py"));
        return SourceBinding.Hash(summaryBytes);
    }

    private static string Run(string executable, params string[] arguments) => RunWithInput(null, executable, arguments);

    private static string RunWithInput(string? input, string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null,
            StandardInputEncoding = input is null ? null : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Product observation could not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (input is not null) { process.StandardInput.Write(input); process.StandardInput.Close(); }
        if (!process.WaitForExit(30_000))
        { process.Kill(true); process.WaitForExit(); throw new InvalidOperationException("Product observation timed out."); }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("Product ownership observation failed; no readiness or cleanup is inferred.");
        return output.Result;
    }
}
