using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Satış modül bağlantıları 5d / 5e / 5f / 5k2: the Sales draft routes against the REAL Cari owner of the host composition
/// (the account port, the contact-information port and the account selection source are the host's own adapters over the
/// Counterparty reads; only the Settings money profile, the Product eligibility and the item type are SENTETIK, as in the
/// SALES-A2a facts above) and the REAL Identity persona reader (SALES_SUPPORT). The parties, accounts and contact information
/// are Counterparty rows written by the test as the owner stores them. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class SalesDraftApiSqlServerRelationalAcceptanceTests
{
    private static readonly string[] DraftAndCariReadGrants = [.. DraftGrants, ErpPermissions.CounterpartyPartiesRead];

    /// <summary>
    /// 5d (SAT-D2 / SAT-D3): a create asks the REAL Cari account port. An active party with an active account of this company is
    /// stored with that party and account; a passive account or a passive party is <c>passive_company_account</c>; an account
    /// of another company only, or an actor without the Cari read, is <c>invalid_reference</c>. A refusal stores nothing.
    /// </summary>
    [SqlServerFact]
    public async Task A_create_asks_the_real_cari_account_port_and_refuses_a_passive_or_foreign_account_on_real_sql()
    {
        await using var run = await Run.StartAsync(RealCariOwner);
        var seeded = await SeedCariAsync(run);
        var saver = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftAndCariReadGrants);
        using var client = CommandClient(run, saver);

        using (var created = await client.SendAsync(Post("SENTETIK-cari-active", Json(FixedProduct), Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var draftId = (await Result(created)).GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid();
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sales.Drafts WHERE Id = @id AND CompanyId = @c AND PartyId = @p AND CompanyAccountId = @a",
                ("@id", draftId), ("@c", CompanyA), ("@p", FixedParty), ("@a", FixedAccount)));
        }

        foreach (var (key, party, account) in new[]
                 {
                     ("SENTETIK-cari-passive-account", seeded.PassiveAccountParty, seeded.PassiveAccount),
                     ("SENTETIK-cari-passive-party", seeded.PassiveParty, seeded.PassivePartyAccount),
                 })
        {
            using var refused = await client.SendAsync(Post(key, JsonFor(party, account), Guid.NewGuid().ToString("N")));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var problem = await Result(refused);
            Assert.Equal(SalesDraftProblemCodesV1.AccountState, problem.GetProperty("code").GetString());
            Assert.Equal([("companyAccountId", "passive_company_account")], Errors(problem));
        }

        using (var refused = await client.SendAsync(Post("SENTETIK-cari-foreign", JsonFor(seeded.ForeignParty, seeded.ForeignAccount),
                   Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var problem = await Result(refused);
            Assert.Equal(SalesDraftProblemCodesV1.AccountState, problem.GetProperty("code").GetString());
            Assert.Equal([("partyId", "invalid_reference"), ("companyAccountId", "invalid_reference")], Errors(problem));
        }

        // The Sales create name alone is not the Cari read: the owner shows this actor no party, so the same active pair is unknown.
        var withoutCariRead = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, DraftGrants);
        using (var viewer = CommandClient(run, withoutCariRead))
        using (var refused = await viewer.SendAsync(Post("SENTETIK-cari-no-read", Json(FixedProduct), Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var problem = await Result(refused);
            Assert.Equal(SalesDraftProblemCodesV1.AccountState, problem.GetProperty("code").GetString());
            Assert.Equal([("partyId", "invalid_reference"), ("companyAccountId", "invalid_reference")], Errors(problem));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft'"));
    }

    /// <summary>
    /// 5e (SALES-A2a): the contact-information route and the create read the REAL Cari contact-information port. Only the
    /// owner's ACTIVE addresses and contacts are offered with the owner's versions; a pair that is not the owner's in this
    /// company is 404; an actor without the Cari read is 403; a create stores the owner's versions and a stale address version
    /// is <c>version_conflict</c>.
    /// </summary>
    [SqlServerFact]
    public async Task The_contact_information_route_and_the_create_read_the_real_cari_contact_port_on_real_sql()
    {
        await using var run = await Run.StartAsync(RealCariOwner);
        var seeded = await SeedCariAsync(run);
        var saver = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftAndCariReadGrants);
        using var client = CommandClient(run, saver);

        using (var response = await client.GetAsync(ContactInformation(FixedParty, FixedAccount)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var options = await Result(response);
            Assert.Equal((CompanyA, FixedParty, FixedAccount, "2"),
                (options.GetProperty("companyId").GetGuid(), options.GetProperty("partyId").GetGuid(),
                    options.GetProperty("companyAccountId").GetGuid(), options.GetProperty("accountVersion").GetString()));
            var address = Assert.Single(options.GetProperty("addresses").EnumerateArray());
            Assert.Equal((FixedAddress, FixedAddressVersion, "delivery", "TR", "SENTETIK Sanayi Cad. 1", "Kocaeli"),
                (address.GetProperty("id").GetGuid(), address.GetProperty("version").GetString(), address.GetProperty("purpose").GetString(),
                    address.GetProperty("countryCode").GetString(), address.GetProperty("line1").GetString(), address.GetProperty("city").GetString()));
            var contact = Assert.Single(options.GetProperty("contacts").EnumerateArray());
            Assert.Equal((FixedContact, FixedContactVersion, "SENTETIK Ayse Yukleme", "delivery"),
                (contact.GetProperty("id").GetGuid(), contact.GetProperty("version").GetString(),
                    contact.GetProperty("displayName").GetString(), contact.GetProperty("purpose").GetString()));
        }

        // Not the owner's pair in this company: the account of another party, or a party whose account is in company B only.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(ContactInformation(FixedParty, seeded.PassiveAccount))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync(ContactInformation(seeded.ForeignParty, seeded.ForeignAccount))).StatusCode);

        var withoutCariRead = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, DraftGrants);
        using (var viewer = CommandClient(run, withoutCariRead))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(ContactInformation(FixedParty, FixedAccount))).StatusCode);
        }

        var referenced = Json(FixedProduct, deliveryAddress: new(FixedAddress, FixedAddressVersion), contact: new(FixedContact, FixedContactVersion));
        Guid draftId;
        using (var created = await client.SendAsync(Post("SENTETIK-cari-contact", referenced, Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            draftId = (await Result(created)).GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid();
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.Drafts WHERE Id = @id AND DeliveryAddressId = @a AND DeliveryAddressVersion = @av AND ContactId = @c AND ContactVersion = @cv",
            ("@id", draftId), ("@a", FixedAddress), ("@av", FixedAddressVersion), ("@c", FixedContact), ("@cv", FixedContactVersion)));

        var stale = Json(FixedProduct, deliveryAddress: new(FixedAddress, "1"), contact: new(FixedContact, FixedContactVersion));
        using (var refused = await client.SendAsync(Post("SENTETIK-cari-contact-stale", stale, Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var problem = await Result(refused);
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, problem.GetProperty("code").GetString());
            Assert.Equal("deliveryAddress", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
    }

    /// <summary>
    /// 5f (SALES-A2a): the accounts route reads the REAL Cari account selection source. It offers the active party's active
    /// account of this company with the owner's code, legal name and versions, and never a passive party, a passive account or
    /// an account of another company. An actor without the Cari read is 403.
    /// </summary>
    [SqlServerFact]
    public async Task The_accounts_route_offers_only_active_accounts_of_the_company_from_the_real_cari_source_on_real_sql()
    {
        await using var run = await Run.StartAsync(RealCariOwner);
        var seeded = await SeedCariAsync(run);
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftAndCariReadGrants);
        using (var client = CommandClient(run, reader))
        using (var response = await client.GetAsync($"{SalesEndpoints.AccountsPath}?pageSize=100"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await Result(response);
            Assert.Equal(CompanyA, page.GetProperty("companyId").GetGuid());
            var seededAccounts = new[] { FixedAccount, seeded.PassiveAccount, seeded.PassivePartyAccount, seeded.ForeignAccount };
            var option = Assert.Single(page.GetProperty("accounts").EnumerateArray(),
                candidate => seededAccounts.Contains(candidate.GetProperty("companyAccountId").GetGuid()));
            Assert.Equal((FixedParty, FixedAccount, $"{seeded.FixedPartyCode} - {FixedPartyLegalName}", "3", "2"),
                (option.GetProperty("partyId").GetGuid(), option.GetProperty("companyAccountId").GetGuid(),
                    option.GetProperty("label").GetString(), option.GetProperty("partyVersion").GetString(),
                    option.GetProperty("accountVersion").GetString()));
        }

        var withoutCariRead = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, DraftGrants);
        using (var viewer = CommandClient(run, withoutCariRead))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{SalesEndpoints.AccountsPath}?pageSize=100")).StatusCode);
        }
    }

    /// <summary>
    /// 5k2 (SAT-D9): the draft detail's own-draft flags read the REAL Identity persona reader through the host. The actor's own
    /// draft is restricted only while the actor holds an ACTIVE SALES_SUPPORT assignment in the context company: an assignment
    /// in another company or a revoked one is not, and another actor's draft is never "own".
    /// </summary>
    [SqlServerFact]
    public async Task The_draft_detail_reads_the_sales_support_persona_from_the_real_identity_assignments_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        var actor = await ActorOfAsync(run, reader);
        var own = await run.StoredDraftAsync(CompanyA);
        await run.ExecuteAsync("UPDATE sales.Drafts SET DocumentNo = N'SO-2026-000002', CreatedBy = @actor WHERE Id = @id",
            ("@actor", actor.ToString("D")), ("@id", own.DraftId));
        var other = await run.StoredDraftAsync(CompanyA);
        using var client = run.Factory.CreateClientFor(reader, CompanyA);

        Assert.Equal((true, false), await OwnFlagsAsync(client, own.DraftId));
        Assert.Equal((false, false), await OwnFlagsAsync(client, other.DraftId));

        var salesSupport = await SalesSupportRoleAsync(run);
        await AssignAsync(run, actor, salesSupport, CompanyB);
        Assert.Equal((true, false), await OwnFlagsAsync(client, own.DraftId));

        var assignmentInA = await AssignAsync(run, actor, salesSupport, CompanyA);
        Assert.Equal((true, true), await OwnFlagsAsync(client, own.DraftId));
        Assert.Equal((false, false), await OwnFlagsAsync(client, other.DraftId));

        await run.ExecuteAsync("UPDATE [identity].[RoleAssignments] SET Status = N'Revoked' WHERE Id = @id", ("@id", assignmentInA));
        Assert.Equal((true, false), await OwnFlagsAsync(client, own.DraftId));
    }

    private const string FixedPartyLegalName = "SENTETIK Aktif Musteri A.S.";

    /// <summary>The host's real Cari adapters stay; the Settings money profile, the Product eligibility and the item type are SENTETIK.</summary>
    private static void RealCariOwner(IServiceCollection services)
    {
        services.RemoveAll<ISettingsPrerequisiteProvider>();
        services.AddScoped<ISettingsPrerequisiteProvider, SyntheticSettings>();
        services.RemoveAll<IProductSalesEligibilityV1>();
        services.AddSingleton<SyntheticGate>();
        services.AddScoped<IProductSalesEligibilityV1, SyntheticProducts>();
        services.RemoveAll<ISettingsProductItemTypeSourceV1>();
        services.AddScoped<ISettingsProductItemTypeSourceV1, SyntheticItemTypes>();
    }

    private sealed record SeededCari(
        string FixedPartyCode,
        Guid PassiveAccountParty,
        Guid PassiveAccount,
        Guid PassiveParty,
        Guid PassivePartyAccount,
        Guid ForeignParty,
        Guid ForeignAccount);

    /// <summary>
    /// Counterparty rows as the owner stores them: the fixed party (version 3) with its active account in company A (version 2,
    /// with one active and one inactive address and one active contact), a party whose account in A is passive, a passive party
    /// with an active account in A, and a party whose only account is in company B.
    /// </summary>
    private static async Task<SeededCari> SeedCariAsync(Run run)
    {
        var contactInformation = CounterpartyContactInformationEditor.Serialize(new CounterpartyContactInformation(null, null, null, null,
            [
                new CounterpartyOperationalAddress(FixedAddress, FixedAddressVersion, true, "delivery", "TR", "SENTETIK Sanayi Cad. 1", null,
                    "Kocaeli", null, null),
                new CounterpartyOperationalAddress(FixedInactiveAddress, "3", false, "delivery", "TR", "SENTETIK eski adres", null, null, null, null),
            ],
            [new CounterpartyOperationalContact(FixedContact, FixedContactVersion, true, "SENTETIK Ayse Yukleme", null, "delivery", [])]));
        var fixedCode = await SeedPartyAsync(run, FixedParty, partyActive: true, partyVersion: 3, FixedPartyLegalName,
            (FixedAccount, CompanyA, true, 2, contactInformation));

        var passiveAccountParty = Guid.NewGuid();
        var passiveAccount = Guid.NewGuid();
        await SeedPartyAsync(run, passiveAccountParty, partyActive: true, partyVersion: 1, "SENTETIK Pasif Hesap A.S.",
            (passiveAccount, CompanyA, false, 1, null));

        var passiveParty = Guid.NewGuid();
        var passivePartyAccount = Guid.NewGuid();
        await SeedPartyAsync(run, passiveParty, partyActive: false, partyVersion: 1, "SENTETIK Pasif Cari A.S.",
            (passivePartyAccount, CompanyA, true, 1, null));

        var foreignParty = Guid.NewGuid();
        var foreignAccount = Guid.NewGuid();
        await SeedPartyAsync(run, foreignParty, partyActive: true, partyVersion: 1, "SENTETIK Diger Sirket A.S.",
            (foreignAccount, CompanyB, true, 1, null));

        return new SeededCari(fixedCode, passiveAccountParty, passiveAccount, passiveParty, passivePartyAccount, foreignParty, foreignAccount);
    }

    private static async Task<string> SeedPartyAsync(Run run, Guid partyId, bool partyActive, long partyVersion, string legalName,
        (Guid Id, Guid Company, bool Active, long Version, string? ContactInformationJson) account)
    {
        var code = $"CAR-{Interlocked.Increment(ref nextSyntheticPartyCode)}";
        await run.ExecuteAsync(
            "INSERT INTO counterparty.Parties (Id, Code, Kind, LegalName, TradeName, IsActive, Version, CreatedAtUtc, UpdatedAtUtc) "
            + "VALUES (@party, @code, N'LegalEntity', @legal, NULL, @partyActive, @partyVersion, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()); "
            + "INSERT INTO counterparty.CompanyPartyAccounts (Id, CompanyId, PartyId, Roles, ExternalIntegrationCode, IsActive, Version, "
            + "ContactInformationJson, CreatedAtUtc, UpdatedAtUtc) "
            + "VALUES (@account, @c, @party, 0, NULL, @accountActive, @accountVersion, @contact, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
            ("@party", partyId), ("@code", code), ("@legal", legalName), ("@partyActive", partyActive), ("@partyVersion", partyVersion),
            ("@account", account.Id), ("@c", account.Company), ("@accountActive", account.Active), ("@accountVersion", account.Version),
            ("@contact", (object?)account.ContactInformationJson ?? DBNull.Value));
        return code;
    }

    /// <summary>A #257 body for another party and account (the fixed product, unit, currency and line).</summary>
    private static string JsonFor(Guid partyId, Guid companyAccountId)
    {
        var line = new SalesOrderDraftLineRequest(FixedLine, FixedProduct, "7", FixedUnit, "1.25");
        var request = new CreateSalesOrderDraftRequest("Order", partyId, companyAccountId, OrderDay, FixedCurrency, [line]);
        return JsonSerializer.Serialize(request, SalesDraftJson.Options);
    }

    private static HttpClient CommandClient(Run run, string subject)
    {
        var client = run.Factory.CreateClientFor(subject, CompanyA);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        return client;
    }

    private static string ContactInformation(Guid partyId, Guid companyAccountId) =>
        $"{SalesEndpoints.ContactInformationPath}?partyId={partyId:D}&companyAccountId={companyAccountId:D}";

    private static (string Field, string Code)[] Errors(JsonElement problem) =>
        [.. problem.GetProperty("errors").EnumerateArray()
            .Select(error => (error.GetProperty("field").GetString()!, error.GetProperty("code").GetString()!))];

    private static async Task<(bool IsOwnDraft, bool IsOwnDecisionRestricted)> OwnFlagsAsync(HttpClient client, Guid draftId)
    {
        using var response = await client.GetAsync($"{SalesEndpoints.DraftsPath}/{draftId:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await Result(response);
        return (detail.GetProperty("isOwnDraft").GetBoolean(), detail.GetProperty("isOwnDecisionRestricted").GetBoolean());
    }

    private static async Task<Guid> ActorOfAsync(Run run, string developmentUser)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return (await dbContext.ExternalIdentities.SingleAsync(
            link => link.Issuer == BootstrapIdentityOptions.Issuer && link.Subject == developmentUser)).UserId;
    }

    /// <summary>The decided persona role code (SALES_SUPPORT) as a synthetic test role without any permission.</summary>
    private static async Task<Guid> SalesSupportRoleAsync(Run run)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var roleId = Guid.NewGuid();
        dbContext.Roles.Add(new RoleDefinition(roleId, ErpPersonaRoleCodes.SalesSupport, "Synthetic sales support", true));
        await dbContext.SaveChangesAsync();
        return roleId;
    }

    private static async Task<Guid> AssignAsync(Run run, Guid actor, Guid roleId, Guid company)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
            approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, company);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
        return assignment.Id;
    }
}
