using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyMutationViewProjectorTests
{
    private static readonly Guid CompanyId = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");

    [Fact]
    public void ValidResource_ProjectsOneTargetAccountAndLosslessVersions()
    {
        var source = ValidResource() with
        {
            Version = long.MaxValue,
            CompanyAccounts =
            [
                ValidAccount() with
                {
                    ExternalIntegrationCode = "must-not-cross-api-boundary",
                },
            ],
        };

        var result = CounterpartyMutationViewProjector.ProjectCompanyAccountCreate(
            source,
            CompanyId,
            source.Id,
            new CreateCompanyPartyAccount(PartyRole.Customer | PartyRole.Supplier));

        Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture), result.Version);
        var account = Assert.Single(result.CompanyAccounts);
        Assert.Equal("1", account.Version);
        Assert.Equal(CompanyId, account.CompanyId);
        Assert.Equal(
            [
                "Code", "CompanyAccounts", "Id", "IsActive", "Kind",
                "LegalName", "TradeName", "Version",
            ],
            typeof(CounterpartyMutationView).GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ["CompanyId", "Id", "IsActive", "Roles", "Version"],
            typeof(CounterpartyMutationAccountView).GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void MalformedOrForeignResource_FailsClosed()
    {
        var foreignCompanyId = Guid.Parse(
            "10000000-0000-0000-0000-000000000002");
        var invalidResources = new PartyCoreView[]
        {
            ValidResource() with { Id = Guid.Empty },
            ValidResource() with { Code = "NOT-CANONICAL" },
            ValidResource() with { Kind = (PartyKind)999 },
            ValidResource() with { LegalName = " " },
            ValidResource() with { LegalName = " padded" },
            ValidResource() with { LegalName = new string('x', 251) },
            ValidResource() with { TradeName = " " },
            ValidResource() with { TradeName = new string('x', 201) },
            ValidResource() with { IsActive = false },
            ValidResource() with { Version = 0 },
            ValidResource() with { CompanyAccounts = [] },
            ValidResource() with { CompanyAccounts = [ValidAccount(), ValidAccount()] },
            ValidResource() with { CompanyAccounts = null! },
            ValidResource() with { CompanyAccounts = [ValidAccount() with { Id = Guid.Empty }] },
            ValidResource() with
            {
                CompanyAccounts = [ValidAccount() with { CompanyId = Guid.Empty }],
            },
            ValidResource() with
            {
                CompanyAccounts = [ValidAccount() with { CompanyId = foreignCompanyId }],
            },
            ValidResource() with
            {
                CompanyAccounts = [ValidAccount() with { Roles = PartyRole.None }],
            },
            ValidResource() with
            {
                CompanyAccounts = [ValidAccount() with { Roles = (PartyRole)16 }],
            },
            ValidResource() with
            {
                CompanyAccounts = [ValidAccount() with { IsActive = false }],
            },
            ValidResource() with
            {
                CompanyAccounts = [ValidAccount() with { Version = 0 }],
            },
        };

        Assert.Throws<InvalidOperationException>(
            () => CounterpartyMutationViewProjector.ProjectCompanyAccountCreate(
                ValidResource(),
                Guid.Empty,
                ValidResource().Id,
                new CreateCompanyPartyAccount(PartyRole.Customer | PartyRole.Supplier)));
        Assert.Throws<InvalidOperationException>(() =>
            CounterpartyMutationViewProjector.ProjectPartyCreate(
                ValidResource() with { Id = Guid.Empty },
                CompanyId,
                new CreateCounterparty(
                    PartyKind.LegalEntity,
                    "Geçerli Cari A.Ş.",
                    "Geçerli Cari",
                    PartyRole.Customer | PartyRole.Supplier)));
        foreach (var resource in invalidResources)
        {
            Assert.Throws<InvalidOperationException>(
                () => CounterpartyMutationViewProjector.ProjectCompanyAccountCreate(
                    resource,
                    CompanyId,
                    resource.Id,
                    new CreateCompanyPartyAccount(PartyRole.Customer | PartyRole.Supplier)));
        }
    }

    [Fact]
    public void SubmittedSnapshotOrRouteMismatch_FailsClosed()
    {
        var source = ValidResource();
        var validCreate = new CreateCounterparty(
            PartyKind.LegalEntity,
            source.LegalName,
            source.TradeName,
            PartyRole.Customer | PartyRole.Supplier);

        Assert.Equal(
            source.Id,
            CounterpartyMutationViewProjector.ProjectPartyCreate(
                source,
                CompanyId,
                validCreate).Id);
        Assert.Throws<InvalidOperationException>(() =>
            CounterpartyMutationViewProjector.ProjectPartyCreate(
                source with { Version = 2 },
                CompanyId,
                validCreate));
        Assert.Throws<InvalidOperationException>(() =>
            CounterpartyMutationViewProjector.ProjectPartyCreate(
                source,
                CompanyId,
                validCreate with { LegalName = "Başka Unvan" }));
        Assert.Throws<InvalidOperationException>(() =>
            CounterpartyMutationViewProjector.ProjectPartyCreate(
                source,
                CompanyId,
                validCreate with { Kind = PartyKind.NaturalPerson }));
        Assert.Throws<InvalidOperationException>(() =>
            CounterpartyMutationViewProjector.ProjectPartyCreate(
                source,
                CompanyId,
                validCreate with { TradeName = "Başka Ticari Ad" }));
        Assert.Throws<InvalidOperationException>(() =>
            CounterpartyMutationViewProjector.ProjectPartyCreate(
                source,
                CompanyId,
                validCreate with { Roles = PartyRole.Service }));
        Assert.Throws<InvalidOperationException>(() =>
            CounterpartyMutationViewProjector.ProjectCompanyAccountCreate(
                source,
                CompanyId,
                Guid.NewGuid(),
                new CreateCompanyPartyAccount(PartyRole.Customer | PartyRole.Supplier)));
        Assert.Throws<InvalidOperationException>(() =>
            CounterpartyMutationViewProjector.ProjectCompanyAccountCreate(
                source,
                CompanyId,
                source.Id,
                new CreateCompanyPartyAccount(PartyRole.Service)));
        Assert.Throws<InvalidOperationException>(() =>
            CounterpartyMutationViewProjector.ProjectCompanyAccountCreate(
                source with
                {
                    CompanyAccounts = [ValidAccount() with { Version = 2 }],
                },
                CompanyId,
                source.Id,
                new CreateCompanyPartyAccount(PartyRole.Customer | PartyRole.Supplier)));
    }

    [Fact]
    public async Task CreateAndReplay_ReturnExactSafeWireProjection()
    {
        await using var factory = new GoymenErpApiFactory();
        using var healthClient = factory.CreateClient();
        using var healthResponse = await healthClient.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

        using var client = factory.CreateClientFor(TestUsers.GroupAdmin, CompanyId);
        const string idempotencyKey = "c02-safe-wire-response";
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        var command = new CreateCounterparty(
            PartyKind.LegalEntity,
            "Güvenli Yanıt Testi A.Ş.",
            "Güvenli Yanıt",
            PartyRole.Customer | PartyRole.Supplier);

        using var first = await client.PostAsJsonAsync("/api/v1/counterparties", command);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstJson = await first.Content.ReadAsStringAsync();
        AssertSafeWireShape(firstJson, CompanyId);

        using var replay = await client.PostAsJsonAsync("/api/v1/counterparties", command);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(
            "true",
            Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        var replayJson = await replay.Content.ReadAsStringAsync();
        Assert.Equal(firstJson, replayJson);
        AssertSafeWireShape(replayJson, CompanyId);
    }

    private static void AssertSafeWireShape(string json, Guid expectedCompanyId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(
            [
                "code", "companyAccounts", "id", "isActive", "kind",
                "legalName", "tradeName", "version",
            ],
            root.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.String, root.GetProperty("version").ValueKind);
        Assert.True(long.Parse(
            root.GetProperty("version").GetString()!,
            CultureInfo.InvariantCulture) > 0);

        var account = Assert.Single(root.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(
            ["companyId", "id", "isActive", "roles", "version"],
            account.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(expectedCompanyId, account.GetProperty("companyId").GetGuid());
        Assert.Equal(JsonValueKind.String, account.GetProperty("version").ValueKind);
        Assert.True(long.Parse(
            account.GetProperty("version").GetString()!,
            CultureInfo.InvariantCulture) > 0);
        Assert.DoesNotContain("externalIntegrationCode", json, StringComparison.Ordinal);
    }

    private static PartyCoreView ValidResource()
    {
        return new PartyCoreView(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            "CAR-000001",
            PartyKind.LegalEntity,
            "Geçerli Cari A.Ş.",
            "Geçerli Cari",
            true,
            1,
            [ValidAccount()]);
    }

    private static CompanyPartyAccountView ValidAccount()
    {
        return new CompanyPartyAccountView(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            CompanyId,
            PartyRole.Customer | PartyRole.Supplier,
            null,
            true,
            1);
    }
}
