using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ACC-V1 approved-source catalog over the real host and real SQL Server. All package, Finance source,
/// account and grant facts are SENTETIK and live only in the disposable runner database. The tests call
/// the public catalog routes and the existing normal-draft source route; they do not install substitute
/// endpoints. Until the central API/DI/permission packet is carried, this file is integration-pending
/// source and must not be described as executed acceptance or real Mikro/UAT evidence.
/// </summary>
public sealed class AccountingApprovedSourceCatalogSqlServerRelationalAcceptanceTests
{
    private const string PackagesPath = "/api/v1/accounting/approved-source-catalog/packages";
    private const string NormalDraftSourcesPath = "/api/v1/accounting/normal-journal-draft-sources";
    private const string PreparePermission = "accounting.approved-source-catalog.prepare";
    private const string ActivatePermission = "accounting.approved-source-catalog.activate";
    private static readonly string DraftCreatePermission = NormalJournalPermissionNamesV1.DraftCreate;
    private const string Preparer = TestUsers.CompanyA;
    private const string OtherActor = TestUsers.CompanyAViewer;
    private const string ForeignActor = TestUsers.GroupAdmin;
    private const string ReceiptVersion = "v1";
    private const string ProfileId = "profile";
    private const string ProfileVersion = "profile-v1";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid SourceId = Guid.Parse("30000000-0000-4000-8000-000000000001");
    private static readonly Guid CurrencyId = Guid.Parse("40000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherCurrencyId = Guid.Parse("40000000-0000-4000-8000-000000000099");
    private static readonly Guid DebitAccountId = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid CreditAccountId = Guid.Parse("20000000-0000-4000-8000-000000000002");

    [SqlServerFact]
    public async Task A_preparer_with_separate_catalog_activation_authority_can_activate_the_same_package_and_the_existing_chooser_reopens_it()
    {
        var package = Package(Guid.NewGuid(), "mikro-synthetic-v1");
        await using var run = await Run.StartAsync();
        var actor = await run.GrantAsync(Preparer, CompanyA,
            PreparePermission, ActivatePermission, DraftCreatePermission);

        using (var prepareClient = run.Client(Preparer, CompanyA))
        using (var preparedResponse = await PrepareAsync(prepareClient, package, Key("prepare")))
        {
            Assert.Equal(HttpStatusCode.Created, preparedResponse.StatusCode);
            var prepared = await Json(preparedResponse);
            AssertResource(prepared, package.Id, "Prepared", 1, null);
            Assert.Equal(actor, prepared.GetProperty("preparedByUserId").GetGuid());
            Assert.Equal(JsonValueKind.Null, prepared.GetProperty("activatedByUserId").ValueKind);
        }

        // Close/reopen is a new client and a fresh GET, not the POST body reused in memory.
        using (var reopenedClient = run.Client(Preparer, CompanyA))
        using (var reopenedResponse = await reopenedClient.GetAsync(PackagePath(package.Id)))
        {
            Assert.Equal(HttpStatusCode.OK, reopenedResponse.StatusCode);
            AssertResource(await Json(reopenedResponse), package.Id, "Prepared", 1, null);
        }

        using (var activationClient = run.Client(Preparer, CompanyA))
        using (var activatedResponse = await ActivateAsync(activationClient, package.Id, 1, 0, Key("activate")))
        {
            Assert.Equal(HttpStatusCode.OK, activatedResponse.StatusCode);
            var activated = await Json(activatedResponse);
            AssertResource(activated, package.Id, "Active", 2, 1);
            Assert.Equal(actor, activated.GetProperty("preparedByUserId").GetGuid());
            Assert.Equal(actor, activated.GetProperty("activatedByUserId").GetGuid());
        }

        using (var reopenedClient = run.Client(Preparer, CompanyA))
        using (var reopenedResponse = await reopenedClient.GetAsync(PackagePath(package.Id)))
        {
            Assert.Equal(HttpStatusCode.OK, reopenedResponse.StatusCode);
            AssertResource(await Json(reopenedResponse), package.Id, "Active", 2, 1);
        }

        using (var chooserClient = run.Client(Preparer, CompanyA))
        using (var chooserResponse = await chooserClient.GetAsync($"{NormalDraftSourcesPath}/{SourceId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, chooserResponse.StatusCode);
            var chooser = await Json(chooserResponse);
            Assert.Equal(CompanyA, chooser.GetProperty("companyId").GetGuid());
            Assert.Equal(SourceId, chooser.GetProperty("sourceId").GetGuid());
            Assert.Equal(ReceiptVersion, chooser.GetProperty("sourceVersion").GetString());
            Assert.Equal(ProfileId, chooser.GetProperty("ownerProfileId").GetString());
            Assert.Equal(ProfileVersion, chooser.GetProperty("ownerProfileVersion").GetString());
            Assert.Equal(CurrencyId, chooser.GetProperty("currencyId").GetGuid());
            Assert.Equal("receipt", chooser.GetProperty("postingPurpose").GetString());
            Assert.Equal("rule-v1", chooser.GetProperty("postingRuleVersion").GetString());
            Assert.Equal(package.AccountPlanSourceVersion, chooser.GetProperty("accountPlanSourceVersion").GetString());
            Assert.Equal(package.AccountsSha256, chooser.GetProperty("accountPlanSourceSha256").GetString());
            Assert.True(NormalJournalDraftSourcePreparationV1.IsPreparationVersion(
                chooser.GetProperty("preparationVersion").GetString()));
            var mappings = chooser.GetProperty("mappings").EnumerateArray().ToArray();
            Assert.Equal(2, mappings.Length);
            Assert.Contains(mappings, item => item.GetProperty("mappingKey").GetString() == "debit"
                && item.GetProperty("side").GetString() == "Debit"
                && item.GetProperty("accountId").GetGuid() == DebitAccountId);
            Assert.Contains(mappings, item => item.GetProperty("mappingKey").GetString() == "credit"
                && item.GetProperty("side").GetString() == "Credit"
                && item.GetProperty("accountId").GetGuid() == CreditAccountId);
        }

        var stored = await run.CatalogAsync(package.Id);
        Assert.Equal((CompanyA, 2, 2L, 1L),
            (stored.CompanyId, stored.Status, stored.Version, stored.CatalogVersion));
        Assert.Equal(package.AccountsSha256, stored.AccountsSha256.ToUpperInvariant());
        Assert.Equal(package.MappingsSha256, stored.MappingsSha256.ToUpperInvariant());
        Assert.Equal(actor, stored.PreparedBy);
        Assert.Equal(actor, stored.ActivatedBy);
    }

    [SqlServerFact]
    public async Task Prepare_replay_is_single_effect_and_other_content_under_the_same_key_is_refused()
    {
        var package = Package(Guid.NewGuid(), "mikro-synthetic-replay-v1");
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, CompanyA, PreparePermission);
        using var client = run.Client(Preparer, CompanyA);
        var key = Key("prepare-replay");

        using (var first = await PrepareAsync(client, package, key))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.False(IsReplay(first));
        }
        var afterFirst = await run.CountsAsync(package.Id);

        using (var replay = await PrepareAsync(client, package, key))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.True(IsReplay(replay));
            AssertResource(await Json(replay), package.Id, "Prepared", 1, null);
        }
        Assert.Equal(afterFirst, await run.CountsAsync(package.Id));

        var changed = package with { AccountPlanSourceVersion = package.AccountPlanSourceVersion + "-changed" };
        using (var conflict = await PrepareAsync(client, changed, key))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        }
        Assert.Equal(afterFirst, await run.CountsAsync(package.Id));
        Assert.Equal(1, afterFirst.Catalogs);
        Assert.Equal(1, afterFirst.Commands);
        Assert.True(afterFirst.Audits >= 1);
    }

    [SqlServerFact]
    public async Task Activation_replay_is_single_effect_and_preserves_prepared_and_activation_provenance()
    {
        var package = Package(Guid.NewGuid(), "mikro-synthetic-activation-replay-v1");
        await using var run = await Run.StartAsync();
        var actor = await run.GrantAsync(Preparer, CompanyA, PreparePermission, ActivatePermission);
        using var client = run.Client(Preparer, CompanyA);
        using (var prepared = await PrepareAsync(client, package, Key("activation-replay-prepare")))
        {
            Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
        }

        var key = Key("activation-replay");
        JsonElement firstBody;
        using (var first = await ActivateAsync(client, package.Id, 1, 0, key))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.False(IsReplay(first));
            firstBody = await Json(first);
        }
        var afterFirst = await run.CountsAsync(package.Id);

        using (var replay = await ActivateAsync(client, package.Id, 1, 0, key))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.True(IsReplay(replay));
            Assert.Equal(firstBody.GetRawText(), (await Json(replay)).GetRawText());
        }
        Assert.Equal(afterFirst, await run.CountsAsync(package.Id));

        using (var conflict = await ActivateAsync(client, package.Id, 2, 1, key))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        }
        Assert.Equal(afterFirst, await run.CountsAsync(package.Id));
        var stored = await run.CatalogAsync(package.Id);
        Assert.Equal(actor, stored.PreparedBy);
        Assert.Equal(actor, stored.ActivatedBy);
        Assert.NotNull(stored.ActivatedAtUtc);
    }

    [SqlServerFact]
    public async Task Prepare_without_prepare_authority_and_activation_without_activate_authority_write_nothing()
    {
        var package = Package(Guid.NewGuid(), "mikro-synthetic-authority-v1");
        await using var run = await Run.StartAsync();
        using (var ungranted = run.Client(OtherActor, CompanyA))
        {
            var before = await run.CountsAsync(package.Id);
            using var refused = await PrepareAsync(ungranted, package, Key("prepare-denied"));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(before, await run.CountsAsync(package.Id));
        }

        await run.GrantAsync(Preparer, CompanyA, PreparePermission);
        using var prepareOnly = run.Client(Preparer, CompanyA);
        using (var prepared = await PrepareAsync(prepareOnly, package, Key("prepare-only")))
        {
            Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
        }
        var afterPrepare = await run.CountsAsync(package.Id);
        using (var refused = await ActivateAsync(prepareOnly, package.Id, 1, 0, Key("activate-denied")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }
        Assert.Equal(afterPrepare, await run.CountsAsync(package.Id));
        Assert.Equal(1, (await run.CatalogAsync(package.Id)).Status);
    }

    [SqlServerFact]
    public async Task Permission_revoked_during_reads_refuses_prepare_and_activation_before_save()
    {
        var preparePackage = Package(Guid.NewGuid(), "mikro-synthetic-revoked-prepare-v1");
        var authority = new SwitchableCatalogAuthority();
        await using (var run = await Run.StartAsync(authority))
        {
            await run.GrantAsync(Preparer, CompanyA, PreparePermission, ActivatePermission);
            using var client = run.Client(Preparer, CompanyA);
            authority.DenyAfterFirstCatalogRead();
            var before = await run.CountsAsync(preparePackage.Id);
            using var refused = await PrepareAsync(client, preparePackage, Key("revoked-prepare"));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(before, await run.CountsAsync(preparePackage.Id));
        }

        var activationPackage = Package(Guid.NewGuid(), "mikro-synthetic-revoked-activation-v1");
        authority = new SwitchableCatalogAuthority();
        await using (var run = await Run.StartAsync(authority))
        {
            await run.GrantAsync(Preparer, CompanyA, PreparePermission, ActivatePermission);
            using var client = run.Client(Preparer, CompanyA);
            using (var prepared = await PrepareAsync(client, activationPackage, Key("revoked-activation-prepare")))
            {
                Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
            }
            var before = await run.CountsAsync(activationPackage.Id);
            authority.DenyAfterFirstCatalogRead();
            using var refused = await ActivateAsync(client, activationPackage.Id, 1, 0, Key("revoked-activation"));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(before, await run.CountsAsync(activationPackage.Id));
            Assert.Equal(1, (await run.CatalogAsync(activationPackage.Id)).Status);
        }
    }

    [SqlServerFact]
    public async Task Stale_package_or_active_catalog_version_is_refused_and_preserves_the_current_active_catalog()
    {
        var first = Package(Guid.NewGuid(), "mikro-synthetic-stale-v1");
        var second = Package(Guid.NewGuid(), "mikro-synthetic-stale-v2", " v2");
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, CompanyA, PreparePermission, ActivatePermission);
        using var client = run.Client(Preparer, CompanyA);
        await PrepareAndActivateAsync(client, first, 0, "stale-first");
        using (var prepared = await PrepareAsync(client, second, Key("stale-second-prepare")))
        {
            Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
        }
        var before = await run.CountsAsync(second.Id);

        using (var stalePackage = await ActivateAsync(client, second.Id, 0, 1, Key("stale-package")))
        {
            Assert.Equal(HttpStatusCode.Conflict, stalePackage.StatusCode);
        }
        using (var staleActive = await ActivateAsync(client, second.Id, 1, 0, Key("stale-active")))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleActive.StatusCode);
        }

        Assert.Equal(before, await run.CountsAsync(second.Id));
        Assert.Equal((2, (long?)1), ((await run.CatalogAsync(first.Id)).Status,
            (await run.CatalogAsync(first.Id)).CatalogVersion));
        Assert.Equal((1, (long?)null), ((await run.CatalogAsync(second.Id)).Status,
            (await run.CatalogAsync(second.Id)).CatalogVersion));
    }

    [SqlServerFact]
    public async Task Activating_a_new_version_supersedes_but_never_deletes_the_historical_catalog()
    {
        var first = Package(Guid.NewGuid(), "mikro-synthetic-history-v1");
        var second = Package(Guid.NewGuid(), "mikro-synthetic-history-v2", " v2");
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, CompanyA, PreparePermission, ActivatePermission);
        using var client = run.Client(Preparer, CompanyA);
        await PrepareAndActivateAsync(client, first, 0, "history-first");
        await PrepareAndActivateAsync(client, second, 1, "history-second");

        var old = await run.CatalogAsync(first.Id);
        var current = await run.CatalogAsync(second.Id);
        Assert.Equal((3, (long?)1), (old.Status, old.CatalogVersion));
        Assert.Equal((2, (long?)2), (current.Status, current.CatalogVersion));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.ApprovedSourceCatalogs WHERE CompanyId = @c",
            ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.ApprovedSourceCatalogs WHERE CompanyId = @c AND Status = 2",
            ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task Another_company_cannot_read_activate_or_resolve_the_catalog()
    {
        var package = Package(Guid.NewGuid(), "mikro-synthetic-company-v1");
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, CompanyA, PreparePermission, ActivatePermission, DraftCreatePermission);
        using (var owner = run.Client(Preparer, CompanyA))
        {
            await PrepareAndActivateAsync(owner, package, 0, "company-owner");
        }
        await run.GrantAsync(ForeignActor, CompanyB,
            PreparePermission, ActivatePermission, DraftCreatePermission);
        using var foreign = run.Client(ForeignActor, CompanyB);
        var before = await run.CountsAsync(package.Id);

        using (var read = await foreign.GetAsync(PackagePath(package.Id)))
        {
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        }
        using (var activate = await ActivateAsync(foreign, package.Id, 2, 1, Key("foreign-activate")))
        {
            Assert.Equal(HttpStatusCode.NotFound, activate.StatusCode);
        }
        using (var chooser = await foreign.GetAsync($"{NormalDraftSourcesPath}/{SourceId:D}"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, chooser.StatusCode);
        }
        Assert.Equal(before, await run.CountsAsync(package.Id));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.ApprovedSourceCatalogs WHERE CompanyId = @c",
            ("@c", CompanyB)));
    }

    [SqlServerFact]
    public async Task Inactive_or_mismatched_source_profile_currency_never_reaches_the_existing_chooser()
    {
        var package = Package(Guid.NewGuid(), "mikro-synthetic-source-refusal-v1");
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, CompanyA, PreparePermission, ActivatePermission, DraftCreatePermission);
        using var client = run.Client(Preparer, CompanyA);
        await PrepareAndActivateAsync(client, package, 0, "source-refusal");
        var before = await run.CountsAsync(package.Id);

        foreach (var mode in new[]
                 {
                     SyntheticReceiptMode.Inactive,
                     SyntheticReceiptMode.CurrencyMismatch,
                     SyntheticReceiptMode.ProfileMismatch,
                 })
        {
            run.Receipts.Mode = mode;
            using var response = await client.GetAsync($"{NormalDraftSourcesPath}/{SourceId:D}");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(before, await run.CountsAsync(package.Id));
        }
    }

    private static async Task PrepareAndActivateAsync(
        HttpClient client, PackageData package, long expectedActiveVersion, string keyPrefix)
    {
        using (var prepared = await PrepareAsync(client, package, Key($"{keyPrefix}-prepare")))
        {
            Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
        }
        using (var activated = await ActivateAsync(
                   client, package.Id, 1, expectedActiveVersion, Key($"{keyPrefix}-activate")))
        {
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        }
    }

    private static Task<HttpResponseMessage> PrepareAsync(HttpClient client, PackageData package, string key)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(package.Id.ToString("D")), "packageId" },
            { new StringContent(package.AccountPlanSourceVersion), "accountPlanSourceVersion" },
            { new StringContent(package.AccountsSha256), "expectedAccountsSourceSha256" },
            { new StringContent(package.MappingsSha256), "expectedPostingMappingsSourceSha256" },
            { File(package.Accounts), "accountsCsv", "accounts.csv" },
            { File(package.Mappings), "postingMappingsCsv", "posting-mappings.csv" },
        };
        var request = Command(HttpMethod.Post, PackagesPath, key);
        request.Content = form;
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ActivateAsync(
        HttpClient client, Guid packageId, long expectedPackageVersion, long expectedActiveCatalogVersion, string key)
    {
        var request = Command(HttpMethod.Post, $"{PackagePath(packageId)}/activation", key);
        request.Content = JsonContent.Create(new { expectedPackageVersion, expectedActiveCatalogVersion });
        return client.SendAsync(request);
    }

    private static HttpRequestMessage Command(HttpMethod method, string path, string key)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return request;
    }

    private static ByteArrayContent File(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("text/csv") { CharSet = "utf-8" };
        return content;
    }

    private static PackageData Package(Guid id, string sourceVersion, string accountSuffix = "")
    {
        var accounts = Encoding.UTF8.GetBytes(
            ApprovedAccountingSourcePackageTemplateV1.AccountsHeader + "\n"
            + $"{DebitAccountId:D},100.01,Synthetic debit{accountSuffix}\n"
            + $"{CreditAccountId:D},320.01,Synthetic credit{accountSuffix}\n");
        var mappings = Encoding.UTF8.GetBytes(
            ApprovedAccountingSourcePackageTemplateV1.MappingsHeader + "\n"
            + Mapping("debit", "Debit", DebitAccountId) + "\n"
            + Mapping("credit", "Credit", CreditAccountId) + "\n");
        return new PackageData(id, sourceVersion, accounts, mappings, Sha(accounts), Sha(mappings));
    }

    private static string Mapping(string key, string side, Guid accountId) =>
        $"{SourceId:D},{NormalJournalSourceKindsV1.RecordedManualReceipt},{ReceiptVersion},{ProfileId},{ProfileVersion},"
        + $"{CurrencyId:D},receipt,rule-v1,{key},{side},{accountId:D}";

    private static string PackagePath(Guid packageId) => $"{PackagesPath}/{packageId:D}";
    private static string Key(string purpose) => $"SENTETIK-catalog-{purpose}-{Guid.NewGuid():N}";
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static bool IsReplay(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Idempotency-Replayed", out var values)
        && values.Single().Equals("true", StringComparison.OrdinalIgnoreCase);

    private static void AssertResource(
        JsonElement resource, Guid packageId, string status, long version, long? catalogVersion)
    {
        Assert.Equal(packageId, resource.GetProperty("packageId").GetGuid());
        Assert.Equal(status, resource.GetProperty("status").GetString());
        Assert.Equal(version, resource.GetProperty("version").GetInt64());
        var catalog = resource.GetProperty("catalogVersion");
        if (catalogVersion is null) Assert.Equal(JsonValueKind.Null, catalog.ValueKind);
        else Assert.Equal(catalogVersion.Value, catalog.GetInt64());
    }

    private sealed record PackageData(
        Guid Id,
        string AccountPlanSourceVersion,
        byte[] Accounts,
        byte[] Mappings,
        string AccountsSha256,
        string MappingsSha256);

    private sealed record Counts(int Catalogs, int Commands, int Audits);

    private sealed record CatalogRow(
        Guid CompanyId,
        int Status,
        long Version,
        long? CatalogVersion,
        string AccountsSha256,
        string MappingsSha256,
        Guid PreparedBy,
        Guid? ActivatedBy,
        DateTimeOffset PreparedAtUtc,
        DateTimeOffset? ActivatedAtUtc);

    private enum SyntheticReceiptMode
    {
        Valid,
        Inactive,
        CurrencyMismatch,
        ProfileMismatch,
    }

    private sealed class SyntheticRecordedReceiptService : IManualReceiptService
    {
        public SyntheticReceiptMode Mode { get; set; }

        public Task<ReadResult<SourceDetail>> DetailAuthorizedAsync(
            FinanceContext context, SourceDetailRequest request, CancellationToken cancellationToken)
        {
            if (context.CompanyId != CompanyA || request.SourceId != SourceId)
            {
                return Task.FromResult(new ReadResult<SourceDetail>(ReadOutcome.NotFound, null));
            }
            var stage = Mode == SyntheticReceiptMode.Inactive ? SourceStage.Draft : SourceStage.Recorded;
            var moneyCurrency = Mode == SyntheticReceiptMode.CurrencyMismatch ? OtherCurrencyId : CurrencyId;
            var profileVersion = Mode == SyntheticReceiptMode.ProfileMismatch ? "profile-mismatch" : ProfileVersion;
            var amount = new AmountView(AmountDisclosure.Visible, new MoneyText("10.00", moneyCurrency));
            var source = new SourceSummary(
                SourceId, CompanyA, "SENTETIK-REC-1", ReceiptVersion, stage,
                Guid.Parse("50000000-0000-4000-8000-000000000001"),
                Guid.Parse("50000000-0000-4000-8000-000000000002"),
                Guid.Parse("50000000-0000-4000-8000-000000000003"),
                CurrencyId, new DateOnly(2026, 10, 2), null, amount);
            var detail = new SourceDetail(
                source, new ProfileReference(ProfileId, profileVersion), null, "SENTETIK catalog source",
                new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero), null, null,
                new UnallocatedView(AllocationKnowledge.Known, amount));
            return Task.FromResult(new ReadResult<SourceDetail>(ReadOutcome.Found, detail));
        }

        public Task<ReadResult<SourcePage>> ListAuthorizedAsync(
            FinanceContext context, SourceListRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ReadResult<SourcePage>(ReadOutcome.Found,
                new SourcePage(context.CompanyId, [], null)));

        public Task<CommandResult> DraftSaveAsync(
            FinanceContext context, CommandMetadata metadata, DraftSaveRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CommandResult> RecordAsync(
            FinanceContext context, CommandMetadata metadata, RecordRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CommandResult> ReverseAsync(
            FinanceContext context, CommandMetadata metadata, ReverseRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SyntheticFinancePermissionPort : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(
            FinanceContext context, FinanceAction action, Guid? sourceId, CancellationToken cancellationToken) =>
            Task.FromResult(context.CompanyId == CompanyA && sourceId == SourceId
                && action is FinanceAction.ReadAmount or FinanceAction.ReadSources
                    ? PermissionDecision.Allowed
                    : PermissionDecision.Denied);
    }

    private sealed class SwitchableCatalogAuthority : IErpAuthorityRevalidationService
    {
        private int catalogReads;
        private int allowedCatalogReads = int.MaxValue;

        public void DenyAfterFirstCatalogRead()
        {
            Volatile.Write(ref catalogReads, 0);
            Volatile.Write(ref allowedCatalogReads, 1);
        }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            var isCatalog = permission is PreparePermission or ActivatePermission;
            var granted = !isCatalog || Interlocked.Increment(ref catalogReads) <= Volatile.Read(ref allowedCatalogReads);
            return Task.FromResult(granted ? GrantedScope() : PermissionScope.Denied);
        }

        private static PermissionScope GrantedScope() => new(
            true,
            true,
            new HashSet<string>(["SENTETIK-CATALOG-AUTHORITY"], StringComparer.Ordinal),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>());
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(
            string baseConnectionString,
            SqlServerRelationalApiFactory factory,
            SyntheticRecordedReceiptService receipts)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
            Receipts = receipts;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public SyntheticRecordedReceiptService Receipts { get; }

        public static async Task<Run> StartAsync(SwitchableCatalogAuthority? catalogAuthority = null)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var receipts = new SyntheticRecordedReceiptService();
            var permissions = new SyntheticFinancePermissionPort();
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, services =>
            {
                services.RemoveAll<IManualReceiptService>();
                services.AddSingleton<IManualReceiptService>(receipts);
                services.RemoveAll<IFinancePermissionPort>();
                services.AddSingleton<IFinancePermissionPort>(permissions);
                if (catalogAuthority is not null)
                {
                    services.RemoveAll<IErpAuthorityRevalidationService>();
                    services.AddSingleton<IErpAuthorityRevalidationService>(catalogAuthority);
                }
            });
            var run = new Run(baseConnectionString, factory, receipts);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var company = new CompanyContext();
            company.Set(CompanyA);
            await using var context = run.Context(company);
            await context.Database.MigrateAsync();
            return run;
        }

        public HttpClient Client(string developmentUser, Guid company)
        {
            var client = Factory.CreateClientFor(developmentUser, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public AccountingDbContext Context(CompanyContext companyContext) =>
            new(new DbContextOptionsBuilder<AccountingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema))
                .Options, companyContext);

        public async Task<Guid> GrantAsync(
            string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                identity => identity.Issuer == BootstrapIdentityOptions.Issuer
                    && identity.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK Accounting catalog actor", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(
                    Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(
                roleId, $"ACC-CATALOG-{roleId:N}", "SENTETIK Accounting catalog test role", true);
            foreach (var permission in permissions.Distinct(StringComparer.Ordinal))
            {
                role.AddPermission(permission);
            }
            var assignment = new RoleAssignment(
                Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
                approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return actor;
        }

        public async Task<Counts> CountsAsync(Guid packageId) => new(
            await ScalarAsync<int>(
                "SELECT COUNT(*) FROM accounting.ApprovedSourceCatalogs WHERE Id = @p",
                ("@p", packageId)),
            await ScalarAsync<int>(
                "SELECT COUNT(*) FROM accounting.Commands WHERE CompanyId = @c AND ResultJson LIKE @p",
                ("@c", CompanyA), ("@p", $"%{packageId:D}%")),
            await ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @c AND EntityId = @p",
                ("@c", CompanyA), ("@p", packageId.ToString("D"))));

        public async Task<CatalogRow> CatalogAsync(Guid packageId)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT CompanyId, Status, Version, CatalogVersion, AccountsSourceSha256, "
                + "PostingMappingsSourceSha256, PreparedByUserId, ActivatedByUserId, PreparedAtUtc, ActivatedAtUtc "
                + "FROM accounting.ApprovedSourceCatalogs WHERE Id = @p", connection);
            command.Parameters.AddWithValue("@p", packageId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return new CatalogRow(
                reader.GetGuid(0), reader.GetInt32(1), reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                reader.GetString(4), reader.GetString(5), reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
