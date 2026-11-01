using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-04 (DEC-SAL-002, DEC-SAL-008, SAT-D27, SAT-D103) against a REAL SQL Server and the REAL host composition: an approved order's
/// header revision (due terms + sales owner) through the real route is ONE save — the archived Rev.0 with the header it was
/// approved with (<c>sales.DraftRevisionHeaders</c>), the header at Rev.1 back in Preparing with the due terms JSON and the
/// sales owner (an Identity user who holds <c>sales.drafts.create</c>, measured by the real Identity directory), the attempt and the
/// audit event; the line port keeps shipping on Rev.0 (SAT-D103) and the existing routes re-approve Rev.1. A customer / currency
/// change written through the real store moves the header and the money profile, archives the old ones and stops shipping
/// (HeaderRevisionPending). Another company sees nothing; an actor without the header name or without the price read is refused
/// before anything is read; the migration backfills older archives and refuses to roll back over a recorded header revision
/// (51133). Drafts are stored and approved through the real Sales store from ISOLATED SYNTHETIC owner snapshots; every grant is a
/// fresh synthetic role. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderHeaderRevisionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("9d3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e01");
    private static readonly Guid OtherCurrency = Guid.Parse("9d3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e05");
    private static readonly Guid MoneyProfile = Guid.Parse("9d3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e02");
    private static readonly Guid KgProfile = Guid.Parse("9d3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e03");
    private static readonly Guid Product = Guid.Parse("9d3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e04");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit HeaderRevised = new("sales.order-draft.header-revised.v1", "ReviseHeader", "SENTETIK başlık revizyonu");
    private static readonly SalesDraftStateAudit QuantityRevised = new("sales.order-draft.quantity-revised.v1", "ReviseQuantity", "SENTETIK miktar revizyonu");

    [SqlServerFact]
    public async Task An_approved_orders_due_terms_and_sales_owner_revision_archives_rev0_with_its_header_in_one_save_through_the_real_routes_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var orderLine = Assert.Single(draft.Lines);
        var (subject, actorId) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.OrdersApprove, SalesPermissionManifestV1.OrdersHeaderWrite,
            SalesPermissionManifestV1.OrdersPriceRead);
        using var client = run.Client(subject, CompanyA);
        var draftPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";
        var terms = new SalesDueTermsV1([
            new SalesDueInstalmentV1(1, SalesDueBasisNamesV1.OrderDate, 30, "40"),
            new SalesDueInstalmentV1(2, SalesDueBasisNamesV1.ShipmentDate, 60, "60"),
        ]);
        var body = new SalesDraftHeaderRevisionRequest("4", "  SENTETIK iki taksit ", draft.Header.PartyId, draft.Header.CompanyAccountId,
            Currency, null, null, actorId, terms);

        using (var revised = await client.SendAsync(Post($"{draftPath}/header-revision", "SENTETIK-header-1", body)))
        {
            Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
            var detail = (await revised.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("draft");
            Assert.Equal(("Preparing", "5", "Rev.1"), (detail.GetProperty("approvalState").GetString(), detail.GetProperty("version").GetString(),
                detail.GetProperty("revision").GetString()));
        }

        // ONE save, as SQL Server holds it.
        Assert.Equal((1, 5L, actorId), (
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<Guid>("SELECT [SalesOwnerUserId] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        var stored = JsonDocument.Parse(await run.ScalarAsync<string>("SELECT [DueTermsJson] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)));
        Assert.Equal(2, stored.RootElement.GetProperty("instalments").GetArrayLength());
        Assert.Equal((1, 0, "SENTETIK iki taksit"), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisionHeaders] WHERE [DraftId] = @id AND [Revision] = 0 "
                + "AND [CurrencyId] = @currency AND [SalesOwnerUserId] IS NULL AND [DueTermsJson] IS NOT NULL", ("@id", draft.Id), ("@currency", Currency)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisionHeaders] WHERE [DraftId] = @id AND [Revision] = 1", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [Reason] FROM [sales].[DraftRevisions] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", draft.Id))));
        Assert.Equal((1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.header-revised.v1' "
                + "AND [EntityId] = @id AND [SourceChannel] = N'Web' AND [EntityVersionBefore] = 4 AND [EntityVersionAfter] = 5", ("@id", draft.Id.ToString("D"))),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.revise-header.v1' "
                + "AND [Outcome] = 1 AND [DraftId] = @id", ("@id", draft.Id))));

        // The revisable header read: the new values and the owner's Identity name.
        var header = await run.JsonAsync(client, $"{draftPath}/header");
        var ownerName = await run.ScalarAsync<string>("SELECT [DisplayName] FROM [identity].[Users] WHERE [Id] = @id", ("@id", actorId));
        Assert.Equal(("5", "Rev.1", actorId.ToString("D"), ownerName, "Visible", false), (header.GetProperty("draftVersion").GetString(),
            header.GetProperty("revision").GetString(), header.GetProperty("salesOwnerUserId").GetString(),
            header.GetProperty("salesOwnerDisplayName").GetString(), header.GetProperty("dueTermsDisclosure").GetString(),
            header.GetProperty("customerOrCurrencyRevisionPending").GetBoolean()));
        Assert.Equal("ShipmentDate", header.GetProperty("dueTerms").GetProperty("instalments")[1].GetProperty("basis").GetString());

        // SAT-D103: shipping goes on under Rev.0 (same customer and currency).
        var pending = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 0, SalesOrderLineRevisionBasisV1.LastApprovedRevision),
            (pending.Outcome, pending.Line!.Revision, pending.Line.RevisionBasis));

        // The same key and content: replayed, nothing written twice.
        var effects = await run.EffectsAsync(draft.Id);
        using (var replayed = await client.SendAsync(Post($"{draftPath}/header-revision", "SENTETIK-header-1", body)))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(effects, await run.EffectsAsync(draft.Id));

        // A stale version is the key's recorded 409; the existing routes approve Rev.1.
        using (var stale = await client.SendAsync(Post($"{draftPath}/header-revision", "SENTETIK-header-stale", body with { DueTerms = null })))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        using (var submitted = await client.SendAsync(State($"{draftPath}/submission", "SENTETIK-submit-rev1", "5")))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        }

        using (var approved = await client.SendAsync(State($"{draftPath}/approval", "SENTETIK-approve-rev1", "6")))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        var answer = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 1, SalesOrderLineRevisionBasisV1.CurrentRevision),
            (answer.Outcome, answer.Line!.Revision, answer.Line.RevisionBasis));
    }

    [SqlServerFact]
    public async Task SAT_D126_every_header_revision_records_its_own_reason_in_the_audit_and_never_an_earlier_archives_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var (subject, actorId) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.OrdersApprove, SalesPermissionManifestV1.OrdersHeaderWrite,
            SalesPermissionManifestV1.OrdersPriceRead);
        using var client = run.Client(subject, CompanyA);
        var draftPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";
        var terms = new SalesDueTermsV1([new SalesDueInstalmentV1(1, SalesDueBasisNamesV1.OrderDate, 45, "100")]);
        SalesDraftHeaderRevisionRequest Body(string version, string reason, Guid? owner) => new(version, reason, draft.Header.PartyId,
            draft.Header.CompanyAccountId, Currency, null, null, owner, terms);
        async Task SendAsync(HttpRequestMessage request)
        {
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // An earlier commercial revision archives Rev.0 with ITS reason (v4 → v5, Rev.1, Preparing).
        await SendAsync(Post($"{draftPath}/header-revision", "SENTETIK-r-archive", Body("4", "SENTETIK-ESKI-ARSIV-GEREKCESI", null)));
        // (b) A Preparing order's in-place change: its own reason, trimmed (v5 → v6).
        await SendAsync(Post($"{draftPath}/header-revision", "SENTETIK-r-preparing", Body("5", "  SENTETIK-HAZIRLIK-GEREKCESI  ", actorId)));
        await SendAsync(State($"{draftPath}/submission", "SENTETIK-r-submit", "6"));
        await SendAsync(State($"{draftPath}/approval", "SENTETIK-r-approve", "7"));
        // (a) SAT-D126: the approved order (with an earlier archive) changes only its sales owner — in place, still approved (v8 → v9).
        await SendAsync(Post($"{draftPath}/header-revision", "SENTETIK-r-d126", Body("8", "SENTETIK-D126-GEREKCESI", null)));
        Assert.Equal(("Approved", 1, 1), (
            await run.ScalarAsync<string>("SELECT CASE [ApprovalState] WHEN 3 THEN N'Approved' ELSE N'Other' END FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions] WHERE [DraftId] = @id", ("@id", draft.Id))));

        // "-" stands for a JSON null (no value).
        async Task<(string Reason, string AfterReason, string Superseded)> AuditAsync(int versionAfter)
        {
            const string Where = "FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.header-revised.v1' AND [EntityId] = @id AND [EntityVersionAfter] = @v";
            (string, object)[] parameters = [("@id", draft.Id.ToString("D")), ("@v", versionAfter)];
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) " + Where, parameters));
            return (await run.ScalarAsync<string>("SELECT [Reason] " + Where, parameters),
                await run.ScalarAsync<string>("SELECT ISNULL(JSON_VALUE([AfterJson], '$.reason'), N'-') " + Where, parameters),
                await run.ScalarAsync<string>("SELECT ISNULL(JSON_VALUE([AfterJson], '$.supersededReason'), N'-') " + Where, parameters));
        }

        // The audit event's own short Reason is the command's fixed text; the user's reason is the "after" picture's.
        const string Fixed = "Satış siparişi başlık revizyonu (SAT-D126)";
        Assert.Equal((Fixed, "SENTETIK-ESKI-ARSIV-GEREKCESI", "SENTETIK-ESKI-ARSIV-GEREKCESI"), await AuditAsync(5));
        Assert.Equal((Fixed, "SENTETIK-HAZIRLIK-GEREKCESI", "-"), await AuditAsync(6));
        Assert.Equal((Fixed, "SENTETIK-D126-GEREKCESI", "-"), await AuditAsync(9));
        // The earlier archive's reason is attributed to no in-place change.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.header-revised.v1' "
            + "AND [EntityId] = @id AND ([AfterJson] LIKE N'%ESKI-ARSIV%' OR [Reason] LIKE N'%ESKI-ARSIV%')", ("@id", draft.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task SAT_D126_a_reason_of_1000_1001_or_4000_characters_is_recorded_whole_in_the_audit_after_picture_through_the_real_route_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        // sales.drafts.create makes the caller an eligible sales owner (SAT-D27).
        var (subject, actorId) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.OrdersHeaderWrite, SalesPermissionManifestV1.OrdersPriceRead);
        using var client = run.Client(subject, CompanyA);
        var draftPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";
        var terms = SalesDraftHeaderApiV1.ToWire(draft.Header.DueTerms);

        // The audit Reason column is nvarchar(1000); the domain, the strict reader and the screen allow 4000. Every length is
        // 200, the order stays approved (SAT-D126: owner-only, in place) and AfterJson (nvarchar(max)) holds the reason whole.
        var version = 4L;
        Guid? owner = actorId;
        foreach (var length in new[] { 1000, 1001, 4000 })
        {
            var reason = new string('g', length - 1) + "Z";
            var body = new SalesDraftHeaderRevisionRequest(version.ToString(System.Globalization.CultureInfo.InvariantCulture), reason,
                draft.Header.PartyId, draft.Header.CompanyAccountId, Currency, null, null, owner, terms);
            using (var revised = await client.SendAsync(Post($"{draftPath}/header-revision", $"SENTETIK-len-{length}", body)))
            {
                Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
            }

            version++;
            const string Where = "FROM [audit].[AuditEvents] AS e WHERE e.[EventType] = N'sales.order-draft.header-revised.v1' AND e.[EntityId] = @id AND e.[EntityVersionAfter] = @v";
            (string, object)[] parameters = [("@id", draft.Id.ToString("D")), ("@v", version)];
            Assert.Equal((reason, "Satış siparişi başlık revizyonu (SAT-D126)"), (
                // OPENJSON with nvarchar(max): the whole value, not JSON_VALUE's 4000-character window.
                await run.ScalarAsync<string>("SELECT j.[reason] FROM [audit].[AuditEvents] AS e CROSS APPLY OPENJSON(e.[AfterJson]) "
                    + "WITH ([reason] nvarchar(max) '$.reason') AS j WHERE e.[EventType] = N'sales.order-draft.header-revised.v1' "
                    + "AND e.[EntityId] = @id AND e.[EntityVersionAfter] = @v", parameters),
                await run.ScalarAsync<string>("SELECT e.[Reason] " + Where, parameters)));
            owner = owner is null ? actorId : null;
        }

        Assert.Equal((version, 0, 3), (
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.revise-header.v1' "
                + "AND [Outcome] = 1 AND [DraftId] = @id", ("@id", draft.Id))));
    }

    [SqlServerFact]
    public async Task A_customer_and_currency_change_through_the_real_store_moves_the_money_profile_archives_the_old_header_and_stops_shipping_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var orderLine = Assert.Single(draft.Lines);
        var party = Guid.NewGuid();
        var account = Guid.NewGuid();
        var money = new MoneyProfileSnapshot(CompanyA, OtherCurrency, SalesOrderDraft.MoneyOperation, Guid.NewGuid(), "7", Day, true, From, null, 4);
        await using (var scope = run.SalesStore(CompanyA))
        {
            var result = await scope.Store.ReviseDraftHeaderAsync(run.SalesEnvelope("k-header"), Hash("k-header"), SalesDraftCommands.ReviseHeaderV1, draft.Id,
                (d, input, now) => d.ReviseHeader(4, input, "SENTETIK-reviser", "SENTETIK cari değişti", now), HeaderRevised, "SENTETIK cari değişti",
                (_, _) => Task.FromResult(new SalesHeaderRevisionInput(party, account, OtherCurrency, money, null, null, null, null)));
            Assert.Equal((1, OtherCurrency, 4), (result!.Draft.Revision, result.Draft.Money.CurrencyId, result.Draft.Money.Scale));
        }

        Assert.Equal((party, account, OtherCurrency, 4), (
            await run.ScalarAsync<Guid>("SELECT [PartyId] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<Guid>("SELECT [CompanyAccountId] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<Guid>("SELECT [CurrencyId] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [MoneyScale] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal((draft.Header.PartyId, Currency, MoneyProfile), (
            await run.ScalarAsync<Guid>("SELECT [PartyId] FROM [sales].[DraftRevisionHeaders] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", draft.Id)),
            await run.ScalarAsync<Guid>("SELECT [CurrencyId] FROM [sales].[DraftRevisionHeaders] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", draft.Id)),
            await run.ScalarAsync<Guid>("SELECT [MoneyProfileId] FROM [sales].[DraftRevisions] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", draft.Id))));

        // Rebuilt from SQL through the domain: the archive carries its header; nothing ships while the change waits.
        await using (var reader = run.SalesStore(CompanyA))
        {
            var reread = await reader.Store.FindDraftAsync(draft.Id);
            Assert.Equal((party, Currency, true), (reread!.Header.PartyId, reread.ArchivedRevisions[0].Header!.CurrencyId,
                reread.CustomerOrCurrencyRevisionPending));
        }

        var line = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "HeaderRevisionPending"), (line.Outcome, line.ApprovalState));
        Assert.DoesNotContain(await run.ApprovedLinesAsync(CompanyA), item => item.OrderId == draft.Id);
    }

    [SqlServerFact]
    public async Task Another_company_cannot_revise_or_read_the_header_and_an_actor_without_the_header_name_or_the_price_read_is_refused_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";
        var body = new SalesDraftHeaderRevisionRequest("4", "neden", draft.Header.PartyId, draft.Header.CompanyAccountId, Currency, null, null, null,
            new SalesDueTermsV1([new SalesDueInstalmentV1(1, SalesDueBasisNamesV1.OrderDate, 30, "100")]));

        var (subjectOfB, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersHeaderWrite, SalesPermissionManifestV1.OrdersPriceRead);
        using (var clientOfB = run.Client(subjectOfB, CompanyB))
        {
            using var refused = await clientOfB.SendAsync(Post($"{path}/header-revision", "SENTETIK-b", body));
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync($"{path}/header")).StatusCode);
        }

        var (reader, _) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceRead);
        using (var client = run.Client(reader, CompanyA))
        {
            using var forbidden = await client.SendAsync(Post($"{path}/header-revision", "SENTETIK-a", body));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        // The header name without the price read: the due terms cannot be seen, so the header is not revised either.
        var (writer, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersHeaderWrite);
        using (var client = run.Client(writer, CompanyA))
        {
            using var forbidden = await client.SendAsync(Post($"{path}/header-revision", "SENTETIK-c", body));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.revise-header.v1'"));
        Assert.Equal((0, 4L), (await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
    }

    [SqlServerFact]
    public async Task The_header_migration_backfills_older_archives_and_refuses_to_roll_back_over_a_recorded_header_revision()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var lineId = Assert.Single(draft.Lines).Id;
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.ReviseDraftQuantityAsync(run.SalesEnvelope("k-quantity"), Hash("k-quantity"), SalesDraftCommands.ReviseQuantityV1, draft.Id,
                (d, now) => d.ReviseQuantities(4, [new LineQuantityInput(lineId, "8", 8m)], "SENTETIK-reviser", "SENTETIK neden", now), QuantityRevised,
                (_, _) => Task.CompletedTask);
        }

        // An order approved before this migration had no due-terms column: SAT-D123 approvals now always carry them, so the
        // pre-migration state is reproduced as SQL Server would have held it (no terms) before the rollback is tried.
        await run.ScalarAsync<int>("UPDATE [sales].[Drafts] SET [DueTermsJson] = NULL WHERE [Id] = @id; SELECT @@ROWCOUNT", ("@id", draft.Id));
        await run.ScalarAsync<int>("UPDATE [sales].[DraftRevisionHeaders] SET [DueTermsJson] = NULL WHERE [DraftId] = @id; SELECT @@ROWCOUNT", ("@id", draft.Id));
        await using (var context = run.SalesContext(CompanyA))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var headers = Array.FindIndex(applied, migration => migration.EndsWith("_AddDraftHeaderRevisions", StringComparison.Ordinal));
            Assert.True(headers > 0);
            var migrator = context.GetService<IMigrator>();
            // No header revision yet: the rollback is free, and the re-applied migration backfills Rev.0's header from the draft.
            await migrator.MigrateAsync(applied[headers - 1]);
            await context.Database.MigrateAsync();
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisionHeaders] WHERE [DraftId] = @id AND [Revision] = 0 "
                + "AND [PartyId] = @party AND [CurrencyId] = @currency", ("@id", draft.Id), ("@party", draft.Header.PartyId), ("@currency", Currency)));
        }

        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.ReviseDraftHeaderAsync(run.SalesEnvelope("k-header"), Hash("k-header"), SalesDraftCommands.ReviseHeaderV1, draft.Id,
                (d, input, now) => d.ReviseHeader(5, input, "SENTETIK-reviser", "SENTETIK vade", now), HeaderRevised, "SENTETIK vade",
                (d, _) => Task.FromResult(new SalesHeaderRevisionInput(d.Header.PartyId, d.Header.CompanyAccountId, d.Header.CurrencyId, null, null, null,
                    null, SalesDueTerms.Create([new SalesDueInstalment(1, SalesDueBasis.OrderDate, 15, "100")]))));
        }

        await using (var context = run.SalesContext(CompanyA))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var headers = Array.FindIndex(applied, migration => migration.EndsWith("_AddDraftHeaderRevisions", StringComparison.Ordinal));
            var migrator = context.GetService<IMigrator>();
            // Every later migration (SAT-D115 AddDraftCustomerRejections: no rejection here) rolls back first; stop on this one.
            await migrator.MigrateAsync(applied[headers]);
            var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[headers - 1]));
            Assert.Equal(51133, error.Number);
            Assert.Equal(applied[..(headers + 1)], await context.Database.GetAppliedMigrationsAsync());
            await context.Database.MigrateAsync();
            Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());

            await using (var reader = run.SalesStore(CompanyA))
            {
                var reread = await reader.Store.FindDraftAsync(draft.Id);
                Assert.Equal((1, 15), (reread!.Revision, reread.Header.DueTerms!.Instalments[0].DaysAfter));
            }

            // The recorded-attempt arm alone: with no draft carrying a sales owner or due terms, the recorded header revision
            // still refuses the rollback.
            await run.ScalarAsync<int>("UPDATE [sales].[Drafts] SET [DueTermsJson] = NULL, [SalesOwnerUserId] = NULL; SELECT @@ROWCOUNT");
            await migrator.MigrateAsync(applied[headers]);
            var attemptArm = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[headers - 1]));
            Assert.Equal(51133, attemptArm.Number);
            Assert.Equal(applied[..(headers + 1)], await context.Database.GetAppliedMigrationsAsync());
        }
    }

    [SqlServerFact]
    public async Task The_header_migration_refuses_to_roll_back_over_a_draft_carrying_only_a_sales_owner_or_only_due_terms()
    {
        // The column arms alone: no header revision was ever recorded here (approved through the store, no revise-header attempt).
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.revise-header.v1'"));
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var headers = Array.FindIndex(applied, migration => migration.EndsWith("_AddDraftHeaderRevisions", StringComparison.Ordinal));
        Assert.True(headers > 0);
        var migrator = context.GetService<IMigrator>();
        // Every later migration is empty and rolls back first; stop on this one so each arm hits exactly its guard.
        await migrator.MigrateAsync(applied[headers]);

        // Only due terms (SAT-D123: the approved order carries them; no sales owner).
        var dueTermsArm = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[headers - 1]));
        Assert.Equal(51133, dueTermsArm.Number);

        // Only a sales owner.
        await run.ScalarAsync<int>("UPDATE [sales].[Drafts] SET [DueTermsJson] = NULL, [SalesOwnerUserId] = @owner WHERE [Id] = @id; SELECT @@ROWCOUNT",
            ("@owner", Guid.NewGuid()), ("@id", draft.Id));
        var ownerArm = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[headers - 1]));
        Assert.Equal(51133, ownerArm.Number);
        Assert.Equal(applied[..(headers + 1)], await context.Database.GetAppliedMigrationsAsync());

        // Neither: the rollback is free, and the migration re-applies.
        await run.ScalarAsync<int>("UPDATE [sales].[Drafts] SET [SalesOwnerUserId] = NULL WHERE [Id] = @id; SELECT @@ROWCOUNT", ("@id", draft.Id));
        await migrator.MigrateAsync(applied[headers - 1]);
        Assert.Equal(applied[..headers], await context.Database.GetAppliedMigrationsAsync());
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static HttpRequestMessage Post<T>(string path, string key, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"SENTETIK-{Guid.NewGuid():N}");
        return request;
    }

    private static HttpRequestMessage State(string path, string key, string expectedVersion) =>
        Post(path, key, new SalesDraftStateCommandRequest(expectedVersion));

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line 6 kg draft from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30);
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "6", 6m, Day.AddDays(3)), T0);
        return draft;
    }

    /// <summary>(sales attempts, audit events, archived revisions, archived headers, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int Revisions, int RevisionHeaders, long DraftVersion);

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = "SENTETIK-preparer-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-api-04-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A stored draft of the company, submitted and approved (version 4), through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new Effects(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisionHeaders]"),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

        public async Task<SalesOrderLineReferenceAnswerV1> LineAsync(Guid company, Guid orderId, Guid lineId)
        {
            await using var scope = SalesStore(company);
            var context = new CompanyContext();
            context.Set(company);
            return await new SalesOrderLineReferencePort(scope.Store, context).GetApprovedLineAsync(orderId, lineId);
        }

        public async Task<IReadOnlyList<SalesApprovedLineReferenceV1>> ApprovedLinesAsync(Guid company)
        {
            await using var scope = SalesStore(company);
            var context = new CompanyContext();
            context.Set(company);
            return (await new SalesOrderLineReferencePort(scope.Store, context).ListApprovedLinesAsync(100, null)).Items;
        }

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new SalesDraftWriteEnvelope(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesDbContext SalesContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new GoymenERP.Modules.Audit.Infrastructure.Persistence.AuditSaveChangesInterceptor(collector, companyContext)).Options,
                companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        /// <summary>A fresh synthetic role of the given names for the bootstrap subject in one company; returns the subject and its user id.</summary>
        public async Task<(string Subject, Guid UserId)> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales header user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"API-04-{roleId:N}", "Synthetic Sales header role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return (developmentUser, actor);
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
