using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.Data.SqlClient;

namespace GoymenERP.CariAcceptance006;

// Product routes through the existing C006 executable and same-handle Lifecycle.
// Setup/control remain the existing scripts; accepted inputs are the owner's
// existing three-file contract, never a second manifest or normal provider.
public static class ProductEnvironment
{
    public static CreateProductDraftRequest DeniedCreateRequest() =>
        new("Must not be created", ProductEnvironmentSettings.CodeGroupId);

    public static byte[] CreateReadyBytes(JsonElement contract, JsonElement binding, string acceptedHash, string bindingHash)
    {
        string[] bindingFields = ["contractId", "runId", "instanceId", "acceptedContractSha256", "source", "ownership", "verifiedAtUtc"];
        if (!binding.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(bindingFields.Order(StringComparer.Ordinal))
            || binding.GetProperty("contractId").GetString() != contract.GetProperty("contractId").GetString()
            || binding.GetProperty("runId").GetString() != contract.GetProperty("runId").GetString()
            || binding.GetProperty("instanceId").GetString() != contract.GetProperty("instanceId").GetString()
            || binding.GetProperty("acceptedContractSha256").GetString() != acceptedHash
            || !JsonElement.DeepEquals(binding.GetProperty("source"), contract.GetProperty("source"))
            || !binding.GetProperty("verifiedAtUtc").TryGetDateTimeOffset(out var timestamp) || timestamp.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("Product publication binding differs from the accepted contract.");
        var ready = new JsonObject();
        foreach (var name in new[] { "contractId", "taskId", "environmentName", "runId", "instanceId", "apiBaseUrl", "actor",
            "unauthorizedActor", "companyId", "codeGroupId", "browserPrefix", "erpAccepted", "productionActivated" })
            ready.Add(name, JsonNode.Parse(contract.GetProperty(name).GetRawText()));
        ready.Add("readyForBrowser", true);
        ready.Add("acceptedContractSha256", acceptedHash);
        ready.Add("sourceBindingSha256", bindingHash);
        ready.Add("verifiedAtUtc", JsonNode.Parse(binding.GetProperty("verifiedAtUtc").GetRawText()));
        return JsonSerializer.SerializeToUtf8Bytes(ready, Evidence.Json);
    }

    public static void ValidateReadyBytes(JsonElement contract, JsonElement binding, byte[] ready, string acceptedHash, string bindingHash)
    {
        if (!ready.AsSpan().SequenceEqual(CreateReadyBytes(contract, binding, acceptedHash, bindingHash)))
            throw new InvalidOperationException("Product immutable readiness bytes differ from the complete accepted publication.");
    }

    public static void ValidateCapabilities(JsonElement body, bool authorized)
    {
        var capabilities = body.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()).ToArray();
        string[] expected = authorized ? ["product.items.create", "product.items.detail", "product.items.edit", "product.items.list"] : [];
        if (body.GetProperty("contractVersion").GetString() != GoymenERP.Api.Shell.ShellCapabilityRegistry.ContractVersion
            || string.IsNullOrWhiteSpace(body.GetProperty("generation").GetString()) || !capabilities.SequenceEqual(expected))
            throw new InvalidOperationException("Product Shell does not reflect the exact global assignment capability contract.");
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0) throw new InvalidOperationException("Product environment operation is required.");
        var settings = ProductEnvironmentSettings.Load();
        var action = args[0];
        if (args.Length != (action == "start-check" ? 2 : action == "verify" ? 3 : 1))
            throw new InvalidOperationException("Product environment arguments differ from the operation contract.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        switch (action)
        {
            case "profile":
                Console.WriteLine(JsonSerializer.Serialize(new { settings.RunId, settings.InstanceId, settings.DatabaseName,
                    ContractId = "PRODUCT-U0102-L-v1", TaskId = "U-01-U-02", ComposeProject = settings.RunId,
                    ContainerName = settings.RunId + "-sql", VolumeName = settings.RunId + "-data",
                    ProductEnvironmentSettings.ApiPort, ProductEnvironmentSettings.SqlPort,
                    ProductEnvironmentSettings.ApiBaseUrl, ProductEnvironmentSettings.SqlEndpoint,
                    SqlImage = RunSettings.ExpectedSqlImage, settings.EvidenceRoot, settings.PrivateRoot }));
                break;
            case "source-check":
                _ = settings.VerifySource();
                Console.WriteLine("Product exact accepted source/local build/fixture verified; not ENV_READY.");
                break;
            case "wait-sql":
                _ = settings.VerifySource();
                var master = new SqlConnectionStringBuilder(settings.DatabaseConnection()) { InitialCatalog = "master", ConnectTimeout = 5 };
                while (true)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    await using var connection = new SqlConnection(master.ConnectionString);
                    try { await connection.OpenAsync(timeout.Token); break; }
                    catch (SqlException) when (!timeout.IsCancellationRequested) { await Task.Delay(1000, timeout.Token); }
                }
                Console.WriteLine("Product owned SQL server accepts connections; schema/browser readiness is not asserted.");
                break;
            case "serve":
                var source = settings.VerifySource();
                if (File.Exists(Path.Combine(settings.EvidenceRoot, "host-started.json")))
                    throw new InvalidOperationException("Product host already attempted; preserve the run.");
                await using (var factory = new ProductAcceptanceFactory(settings))
                {
                    await factory.StartLoopbackAsync(timeout.Token);
                    using var process = Process.GetCurrentProcess();
                    settings.Write("host-started.json", new { settings.RunId, settings.InstanceId,
                        ApiBaseUrl = ProductEnvironmentSettings.ApiBaseUrl, pid = process.Id,
                        startTimeUtc = process.StartTime.ToUniversalTime(), executionRunId = settings.RunId,
                        capabilityRevision = ProductAcceptanceFactory.Revision, source,
                        acceptedContractSha256 = settings.ContractHash, observedAtUtc = DateTimeOffset.UtcNow });
                    Console.WriteLine("Product exact isolated host and fixture started; browser readiness still requires smoke/publication.");
                    await Task.Delay(Timeout.InfiniteTimeSpan);
                }
                break;
            case "start-check":
                var outcome = Lifecycle.AwaitStart(() => Lifecycle.ReadHostIdentity(settings.EvidenceRoot, settings.LifecycleIdentity),
                    int.Parse(args[1], CultureInfo.InvariantCulture), TimeSpan.FromSeconds(Lifecycle.StartTimeoutSeconds), OsProbe.Real);
                Console.WriteLine(JsonSerializer.Serialize(outcome, Evidence.Json));
                return outcome.ExitCode;
            case "stop":
                var stopped = Lifecycle.Stop(Lifecycle.ReadHostIdentity(settings.EvidenceRoot, settings.LifecycleIdentity), OsProbe.Real);
                Console.WriteLine(JsonSerializer.Serialize(stopped, Evidence.Json));
                return stopped.ExitCode;
            case "release-check":
                var release = Lifecycle.PlanRelease([(settings.EvidenceRoot, Lifecycle.ReadHostIdentity(settings.EvidenceRoot, settings.LifecycleIdentity))], OsProbe.Real);
                Console.WriteLine(JsonSerializer.Serialize(release, Evidence.Json));
                return release.ExitCode;
            case "smoke":
                await ProductEnvironmentEvidence.SmokeAsync(settings, timeout.Token);
                Console.WriteLine("Product authorized reads, explicit capability grants and denied writes passed without durable changes.");
                break;
            case "snapshot":
                var ownership = await ProductEnvironmentEvidence.VerifyLiveAsync(settings, timeout.Token);
                Console.WriteLine(JsonSerializer.Serialize(new { settings.RunId, settings.InstanceId,
                    acceptedContractSha256 = settings.ContractHash, source = settings.Source, ownership,
                    observedAtUtc = DateTimeOffset.UtcNow, snapshot = await ProductEnvironmentEvidence.SnapshotAsync(settings, timeout.Token) }, Evidence.Json));
                break;
            case "publish":
                await PublishAsync(settings, timeout.Token);
                break;
            case "verify":
                var expectedProducts = int.Parse(args[1], CultureInfo.InvariantCulture);
                var expectedMutations = int.Parse(args[2], CultureInfo.InvariantCulture);
                if (expectedProducts < 0 || expectedMutations < expectedProducts || expectedMutations > 100)
                    throw new InvalidOperationException("Product effect expectation is outside the explicit isolated run bound.");
                await VerifyPublishedAsync(settings, timeout.Token);
                var baseline = JsonSerializer.Deserialize<ProductEnvironmentSnapshot>(File.ReadAllBytes(Path.Combine(settings.EvidenceRoot, "baseline.json")))!;
                var current = await ProductEnvironmentEvidence.SnapshotAsync(settings, timeout.Token);
                if (current.Product.Products.Count != baseline.Product.Products.Count + expectedProducts
                    || current.Product.Registrations.Count != baseline.Product.Registrations.Count + expectedProducts
                    || current.Audit.GetArrayLength() != baseline.Audit.GetArrayLength() + expectedMutations
                    || current.Terminals.GetArrayLength() != baseline.Terminals.GetArrayLength() + expectedMutations
                    || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(current.Product.Groups), JsonSerializer.SerializeToElement(baseline.Product.Groups)))
                    throw new InvalidOperationException("Product actual durable effects differ from the explicit browser journey expectation.");
                Console.WriteLine(JsonSerializer.Serialize(new { settings.RunId, settings.InstanceId,
                    expectedProducts, expectedMutations, aggregateEffectsMatched = true, baseline, current,
                    browserVerdict = "NOT_ASSESSED_BY_AGGREGATE_SQL_CHECK", productOwnerAssertions = "SEPARATE_REQUIRED_GATE", erpAccepted = false }, Evidence.Json));
                break;
            case "live-check":
                await VerifyPublishedAsync(settings, timeout.Token);
                Console.WriteLine("Product current source, host, ownership and immutable published bytes verified.");
                break;
            default: throw new InvalidOperationException("Product unknown environment operation.");
        }
        return 0;
    }

    private static async Task PublishAsync(ProductEnvironmentSettings settings, CancellationToken cancellationToken)
    {
        using var smoke = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(settings.EvidenceRoot, "smoke.json")));
        if (!smoke.RootElement.GetProperty("passed").GetBoolean()
            || smoke.RootElement.GetProperty("RunId").GetString() != settings.RunId
            || smoke.RootElement.GetProperty("InstanceId").GetString() != settings.InstanceId
            || smoke.RootElement.GetProperty("acceptedContractSha256").GetString() != settings.ContractHash)
            throw new InvalidOperationException("Product smoke evidence is not bound to this accepted run.");
        SourceBinding.RequireProductSourceMatch(settings.Source, smoke.RootElement.GetProperty("source"));
        using var host = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(settings.EvidenceRoot, "host-started.json")));
        var testsSummarySha256 = ProductEnvironmentEvidence.VerifyTestedBuild(settings, host.RootElement.GetProperty("source"));
        var baseline = JsonSerializer.Deserialize<ProductEnvironmentSnapshot>(File.ReadAllBytes(Path.Combine(settings.EvidenceRoot, "baseline.json")))!;
        var current = await ProductEnvironmentEvidence.SnapshotAsync(settings, cancellationToken);
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(baseline), JsonSerializer.SerializeToElement(current)))
            throw new InvalidOperationException("Product fixture changed before initial publication.");
        var ownership = await ProductEnvironmentEvidence.VerifyLiveAsync(settings, cancellationToken);
        var timestamp = DateTimeOffset.UtcNow;
        var binding = JsonSerializer.SerializeToUtf8Bytes(new { contractId = "PRODUCT-U0102-L-v1", runId = settings.RunId,
            instanceId = settings.InstanceId, acceptedContractSha256 = settings.ContractHash, source = settings.Source,
            ownership, verifiedAtUtc = timestamp }, Evidence.Json);
        settings.WriteBytes("accepted-contract.json", settings.ContractBytes);
        settings.WriteBytes("source-binding.json", binding);
        using var bindingDocument = JsonDocument.Parse(binding);
        var ready = CreateReadyBytes(settings.Contract, bindingDocument.RootElement, settings.ContractHash, SourceBinding.Hash(binding));
        settings.Write("backend-verification.json", new { acceptedContractSha256 = settings.ContractHash,
            testsSummarySha256, source = settings.Source, verifiedAtUtc = timestamp,
            sourceBindingSha256 = SourceBinding.Hash(binding), environmentReadySha256 = SourceBinding.Hash(ready),
            meaning = "Existing C006 validator matched the executed local test-output DLL inventory to this runtime; not browser acceptance." });
        settings.WriteBytes("environment-ready.json", ready);
        Console.WriteLine("Product verified three-file publication written in publisher worktree; owner copies exact bytes before browser use.");
    }

    private static async Task VerifyPublishedAsync(ProductEnvironmentSettings settings, CancellationToken cancellationToken)
    {
        var accepted = File.ReadAllBytes(Path.Combine(settings.EvidenceRoot, "accepted-contract.json"));
        var binding = File.ReadAllBytes(Path.Combine(settings.EvidenceRoot, "source-binding.json"));
        using var b = JsonDocument.Parse(binding);
        var readyBytes = File.ReadAllBytes(Path.Combine(settings.EvidenceRoot, "environment-ready.json"));
        using var ready = JsonDocument.Parse(readyBytes);
        ValidateReadyBytes(settings.Contract, b.RootElement, readyBytes, settings.ContractHash, SourceBinding.Hash(binding));
        using var verification = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(settings.EvidenceRoot, "backend-verification.json")));
        if (verification.RootElement.GetProperty("sourceBindingSha256").GetString() != SourceBinding.Hash(binding)
            || verification.RootElement.GetProperty("environmentReadySha256").GetString() != SourceBinding.Hash(readyBytes)
            || verification.RootElement.GetProperty("acceptedContractSha256").GetString() != settings.ContractHash)
            throw new InvalidOperationException("Product publication no longer matches the initial publisher receipt.");
        if (SourceBinding.Hash(accepted) != settings.ContractHash
            || b.RootElement.GetProperty("acceptedContractSha256").GetString() != settings.ContractHash
            || ready.RootElement.GetProperty("acceptedContractSha256").GetString() != settings.ContractHash
            || ready.RootElement.GetProperty("sourceBindingSha256").GetString() != SourceBinding.Hash(binding)
            || b.RootElement.GetProperty("runId").GetString() != settings.RunId
            || b.RootElement.GetProperty("instanceId").GetString() != settings.InstanceId)
            throw new InvalidOperationException("Product immutable publication differs from its accepted run.");
        SourceBinding.RequireProductSourceMatch(settings.Source, b.RootElement.GetProperty("source"));
        var actual = await ProductEnvironmentEvidence.VerifyLiveAsync(settings, cancellationToken);
        if (!JsonElement.DeepEquals(actual, b.RootElement.GetProperty("ownership")))
            throw new InvalidOperationException("Product live resource/host identity changed after publication.");
    }
}
