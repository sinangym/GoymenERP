using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Product card deactivation (Active -> Inactive) and reactivation (Inactive -> Active) through the REAL host on a
/// REAL SQL Server (DEC-CAR-003, PRD-SCOPE-001 #98 §7.9, PRD-PERM-001 #99): each move under its own explicit global
/// grant, the shared command boundary (replay, conflicts, late revocation, origin fence), the canonical audit
/// (reason, SourceChannel Web), the history read, and the next module's view through the existing owner ports:
/// the Inventory opening's stock port and the stock candidate search refuse the Inactive card while the card
/// itself stays readable. Grants and the family release are synthetic rows in this test's own disposable database,
/// never a Sinan or production grant.
/// </summary>
public sealed class ProductDeactivationSqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid GroupId = Guid.Parse("85000000-0000-0000-0000-000000000011");
    private static readonly Guid HotCoil = Guid.Parse("85000000-0000-4000-8000-000000000041");
    private static readonly Guid TemplateId = Guid.Parse("85000000-0000-4000-8000-000000000051");
    private static readonly DateOnly CountedOn = new DateOnly(2026, 9, 19);

    private static readonly ProductMasterOperation[] ApprovalOperations =
    [
        ProductMasterOperation.Read, ProductMasterOperation.DraftCreate, ProductMasterOperation.DraftBasicEdit,
        ProductMasterOperation.Activate,
    ];

    private static readonly ProductMasterOperation[] LifecycleOperations =
        [.. ApprovalOperations, ProductMasterOperation.Deactivate, ProductMasterOperation.Reactivate];

    [SqlServerFact]
    public Task A_card_is_deactivated_with_a_reason_replayed_by_key_shown_in_history_and_reactivated() =>
        WithDatabaseAsync(LifecycleOperations, async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var product = await ActiveCardAsync(client, "deactivation-1");

            using var deactivated = await MoveAsync(client, "deactivation", product.Id, "deactivate-1", "2", "  Üretimden kalktı ");
            Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
            Assert.False(deactivated.Headers.Contains("Idempotency-Replayed"));
            var result = (await deactivated.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.Equal(("Inactive", "3", false), (result.Card.Product.Lifecycle, result.Card.Product.Version, result.IsReplay));
            // Every card value is kept: name, code, family, references.
            Assert.Equal((product.Code, product.Name, product.FamilyId), (result.Card.Product.Code, result.Card.Product.Name,
                result.Card.Product.FamilyId));
            var correlation = Assert.Single(deactivated.Headers.GetValues("X-Correlation-Id"));

            // A lost answer retried with the same key (and the same reason up to surrounding blanks) is the stored one.
            using var replay = await MoveAsync(client, "deactivation", product.Id, "deactivate-1", "2", "Üretimden kalktı");
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(correlation, Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            Assert.True((await replay.Content.ReadFromJsonAsync<ProductMutationResponse>())!.IsReplay);

            // The same key with another reason is a conflict that discloses nothing.
            using var other = await MoveAsync(client, "deactivation", product.Id, "deactivate-1", "2", "Başka gerekçe");
            Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
            Assert.Equal(ProductApiV1.IdempotencyConflict, await CodeAsync(other));

            // Close and reopen: the card reads Inactive at v3, and so does the stored row.
            using var read = await client.GetAsync($"/api/v1/products/{product.Id:D}");
            var reopened = (await read.Content.ReadFromJsonAsync<ProductCardView>())!.Product;
            Assert.Equal(("Inactive", "3"), (reopened.Lifecycle, reopened.Version));
            Assert.Equal((2, 3L), (await ScalarAsync<int>(factory, "SELECT [Lifecycle] FROM [product].[Products] WHERE [Id] = @id", product.Id),
                await ScalarAsync<long>(factory, "SELECT [Version] FROM [product].[Products] WHERE [Id] = @id", product.Id)));

            // The history shows when and why, and nothing about who or how (the #181 §5.4 shape).
            using (var history = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/products/{product.Id:D}/history")))
            {
                var newest = history.RootElement.GetProperty("entries")[0];
                Assert.Equal(["eventId", "operation", "occurredAtUtc", "versionBefore", "versionAfter", "reason", "changedFields"],
                    newest.EnumerateObject().Select(property => property.Name));
                Assert.Equal(("Deactivate", "2", "3", "Üretimden kalktı"), (newest.GetProperty("operation").GetString(),
                    newest.GetProperty("versionBefore").GetString(), newest.GetProperty("versionAfter").GetString(),
                    newest.GetProperty("reason").GetString()));
                Assert.Equal(["lifecycle"], newest.GetProperty("changedFields").EnumerateArray().Select(field => field.GetString()));
                Assert.Equal(["Deactivate", "Activate", "DraftCreate"], history.RootElement.GetProperty("entries").EnumerateArray()
                    .Select(entry => entry.GetProperty("operation").GetString()));
            }

            // Exactly one canonical audit event and one replay terminal: the actor, the Web channel, the reason,
            // and before/after snapshots that show exactly this move.
            var audit = await AuditRowAsync(factory, "product.card.deactivated.v1", product.Id);
            Assert.Equal(("Deactivate", "product.card.deactivate", "Web", "Üretimden kalktı", ActorId.ToString("D"), 2L, 3L),
                (audit.Operation, audit.PermissionCode, audit.SourceChannel, audit.Reason, audit.ActorId, audit.VersionBefore, audit.VersionAfter));
            Assert.Equal(("Active", "Inactive"), (Lifecycle(audit.BeforeJson), Lifecycle(audit.AfterJson)));
            Assert.Equal(1, await CountAsync(factory,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'product.card.deactivated.v1' AND [EntityId] = @entity", product.Id));
            Assert.Equal(1, await CountAsync(factory,
                "SELECT COUNT(*) FROM [idempotency].[GroupSharedCommandTerminals] WHERE [CommandName] = 'product.card.deactivate.v1'", product.Id));

            // The list keeps it, under Inactive, never under Active.
            using var inactive = await client.GetAsync("/api/v1/products?lifecycle=Inactive");
            Assert.Equal([product.Id], (await inactive.Content.ReadFromJsonAsync<ProductPage>())!.Items.Select(item => item.Id));
            using var active = await client.GetAsync("/api/v1/products?lifecycle=Active");
            Assert.Empty((await active.Content.ReadFromJsonAsync<ProductPage>())!.Items);

            // Reactivation: its own move, its own reason, one version up.
            using var reactivated = await MoveAsync(client, "reactivation", product.Id, "reactivate-1", "3", "Tedarik yeniden başladı");
            Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
            var back = (await reactivated.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.Equal(("Active", "4"), (back.Card.Product.Lifecycle, back.Card.Product.Version));
            var reactivation = await AuditRowAsync(factory, "product.card.reactivated.v1", product.Id);
            Assert.Equal(("Reactivate", "product.card.reactivate", "Web", "Tedarik yeniden başladı"),
                (reactivation.Operation, reactivation.PermissionCode, reactivation.SourceChannel, reactivation.Reason));
            Assert.Equal(("Inactive", "Active"), (Lifecycle(reactivation.BeforeJson), Lifecycle(reactivation.AfterJson)));
        });

    [SqlServerFact]
    public Task Each_move_needs_its_own_grant_and_a_late_revocation_or_another_origin_writes_nothing() =>
        WithDatabaseAsync(ApprovalOperations, async (factory, observer) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var product = await ActiveCardAsync(client, "deactivation-2");

            // Read + draft + approval grants never take a card out of use.
            using var denied = await MoveAsync(client, "deactivation", product.Id, "deactivate-denied", "2", "Gerekçe");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            // A company administrator without any global Product grant is refused too.
            using var companyOnly = factory.CreateClientFor("company-a-user", CompanyA);
            using var companyDenied = await MoveAsync(companyOnly, "deactivation", product.Id, "deactivate-company", "2", "Gerekçe");
            Assert.Equal(HttpStatusCode.Forbidden, companyDenied.StatusCode);
            await AssertUnchangedAsync(factory, product.Id, 1, 2);

            // With the Deactivate grant the card leaves use; the Reactivate grant is separate and still missing.
            await GrantAsync(factory, ProductMasterOperation.Deactivate);
            using var deactivated = await MoveAsync(client, "deactivation", product.Id, "deactivate-2", "2", "Gerekçe");
            Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
            // The same key from another origin company is not the same command: a conflict, nothing written.
            using var otherOrigin = factory.CreateClientFor("group-bootstrap-admin", CompanyB);
            using var fenced = await MoveAsync(otherOrigin, "deactivation", product.Id, "deactivate-2", "2", "Gerekçe");
            Assert.Equal(HttpStatusCode.Conflict, fenced.StatusCode);
            using var noReactivate = await MoveAsync(client, "reactivation", product.Id, "reactivate-denied", "3", "Gerekçe");
            Assert.Equal(HttpStatusCode.Forbidden, noReactivate.StatusCode);
            await AssertUnchangedAsync(factory, product.Id, 2, 3);

            // Granted, then revoked while the move is in flight: 403 and nothing is written. (A user holds at most
            // one assignment row per operation — IX_ProductMasterAssignments_UserId_Operation — so a revoked grant
            // is not re-issued as a new row; the late revocation is therefore proven on the Reactivate grant.)
            await GrantAsync(factory, ProductMasterOperation.Reactivate);
            observer.Callback = async (stage, _, cancellationToken) =>
            {
                if (stage != ProductCommandAttemptStage.BeforeCommit) return;
                await using var scope = factory.Services.CreateAsyncScope();
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var grant = await identity.Set<ProductMasterAssignment>().SingleAsync(row => row.UserId == ActorId
                    && row.Operation == ProductMasterOperation.Reactivate && row.IsActive, cancellationToken);
                grant.Revoke(Guid.NewGuid(), "Synthetic isolated SQL late revocation", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync(cancellationToken);
            };
            using var revoked = await MoveAsync(client, "reactivation", product.Id, "reactivate-revoked", "3", "Gerekçe");
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
            observer.Callback = null;
            await AssertUnchangedAsync(factory, product.Id, 2, 3);
            Assert.Equal(0, await CountAsync(factory,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'product.card.reactivated.v1' AND [EntityId] = @entity", product.Id));
        });

    [SqlServerFact]
    public Task A_stale_version_the_wrong_state_or_a_bad_reason_is_refused_and_an_inactive_card_is_not_edited_or_approved() =>
        WithDatabaseAsync(LifecycleOperations, async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var draft = await DraftAsync(client, "deactivation-3-draft");
            // A Draft was never in use: it is not deactivated (approval is its way forward) or "reactivated".
            using var draftDeactivated = await MoveAsync(client, "deactivation", draft.Id, "deactivate-draft", "1", "Gerekçe");
            Assert.Equal((HttpStatusCode.Conflict, ProductApiV1.LifecycleConflict), (draftDeactivated.StatusCode, await CodeAsync(draftDeactivated)));

            var product = await ActiveCardAsync(client, "deactivation-3");
            using var stale = await MoveAsync(client, "deactivation", product.Id, "deactivate-stale", "1", "Gerekçe");
            Assert.Equal((HttpStatusCode.Conflict, ProductApiV1.VersionConflict), (stale.StatusCode, await CodeAsync(stale)));
            using var notInactive = await MoveAsync(client, "reactivation", product.Id, "reactivate-active", "2", "Gerekçe");
            Assert.Equal((HttpStatusCode.Conflict, ProductApiV1.LifecycleConflict), (notInactive.StatusCode, await CodeAsync(notInactive)));
            using var unknown = await MoveAsync(client, "deactivation", Guid.NewGuid(), "deactivate-unknown", "2", "Gerekçe");
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            using var blank = await MoveAsync(client, "deactivation", product.Id, "deactivate-blank", "2", "   ");
            Assert.Equal((HttpStatusCode.BadRequest, ProductApiV1.ValidationFailed), (blank.StatusCode, await CodeAsync(blank)));
            await AssertUnchangedAsync(factory, product.Id, 1, 2);

            using var deactivated = await MoveAsync(client, "deactivation", product.Id, "deactivate-3", "2", "Gerekçe");
            Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
            using var twice = await MoveAsync(client, "deactivation", product.Id, "deactivate-twice", "3", "Gerekçe");
            Assert.Equal((HttpStatusCode.Conflict, ProductApiV1.LifecycleConflict), (twice.StatusCode, await CodeAsync(twice)));
            // An Inactive card is neither approved again nor edited as a draft.
            var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{product.Id:D}/activation")
            {
                Content = JsonContent.Create(new ActivateProductRequest("3")),
            };
            approve.Headers.Add("Idempotency-Key", "deactivation-approve-again");
            using var approved = await client.SendAsync(approve);
            Assert.Equal((HttpStatusCode.Conflict, ProductApiV1.LifecycleConflict), (approved.StatusCode, await CodeAsync(approved)));
            var edit = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/products/{product.Id:D}/draft")
            {
                Content = JsonContent.Create(new { expectedVersion = "3", changes = new { name = "SENTETIK edited" }, reason = "Isolated SQL edit" }),
            };
            edit.Headers.Add("Idempotency-Key", "deactivation-edit-after");
            using var edited = await client.SendAsync(edit);
            Assert.Equal((HttpStatusCode.Conflict, ProductApiV1.LifecycleConflict), (edited.StatusCode, await CodeAsync(edited)));
            await AssertUnchangedAsync(factory, product.Id, 2, 3);
        });

    [SqlServerFact]
    public Task The_next_module_refuses_new_use_of_an_inactive_card_while_the_card_stays_readable() =>
        WithDatabaseAsync(LifecycleOperations, async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var product = await ActiveCardAsync(client, "deactivation-4");

            // Before: the Inventory opening's stock port admits the card, and the stock candidate search offers it.
            var before = await PortIn(factory, CompanyA).EvaluateAsync(product.Id, "2", CountedOn, default);
            Assert.True(before.IsEligible);
            Assert.Contains((await CandidatesIn(factory, CompanyA).SearchAsync(new ProductStockCandidateQueryV1(null, CountedOn), default)).Items,
                candidate => candidate.ProductId == product.Id);

            using var deactivated = await MoveAsync(client, "deactivation", product.Id, "deactivate-4", "2", "Üretimden kalktı");
            Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

            // After: a new count line is refused at the current version for the lifecycle, and the version the
            // counter read earlier is stale; the candidate search no longer offers it.
            var current = await PortIn(factory, CompanyA).EvaluateAsync(product.Id, "3", CountedOn, default);
            Assert.False(current.IsEligible);
            Assert.Contains(current.Issues, issue => (issue.Field, issue.Code) == ("productId", ProductApiV1.LifecycleConflict));
            var earlier = await PortIn(factory, CompanyA).EvaluateAsync(product.Id, "2", CountedOn, default);
            Assert.False(earlier.IsEligible);
            Assert.Contains(earlier.Issues, issue => issue.Code == ProductApiV1.VersionConflict);
            Assert.DoesNotContain((await CandidatesIn(factory, CompanyA).SearchAsync(new ProductStockCandidateQueryV1(null, CountedOn), default)).Items,
                candidate => candidate.ProductId == product.Id);

            // The card and its values stay readable for the records that already name it.
            using var read = await client.GetAsync($"/api/v1/products/{product.Id:D}");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var card = (await read.Content.ReadFromJsonAsync<ProductCardView>())!;
            Assert.Equal(("Inactive", product.Code, product.Name), (card.Product.Lifecycle, card.Product.Code, card.Product.Name));
            Assert.Equal(TemplateId, card.References.TemplateId);

            // Reactivated, it is offered again at its new version.
            using var reactivated = await MoveAsync(client, "reactivation", product.Id, "reactivate-4", "3", "Tedarik yeniden başladı");
            Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
            Assert.True((await PortIn(factory, CompanyA).EvaluateAsync(product.Id, "4", CountedOn, default)).IsEligible);
        });

    private static ProductInventoryStockPort PortIn(SqlServerRelationalApiFactory factory, Guid company) =>
        new ProductInventoryStockPort(Eligibility(factory, company));

    private static ProductStockCandidateService CandidatesIn(SqlServerRelationalApiFactory factory, Guid company)
    {
        var scope = factory.Services.CreateScope();
        var context = new CompanyContext();
        context.Set(company);
        var user = new User(company);
        var product = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        return new ProductStockCandidateService(context, user, new GrantedIn(company),
            new SqlProductStockCandidateReader(new ProductReadStore(product)), Eligibility(factory, company),
            new ProductUnitProfileAdapter(new ApprovedSettingsPrerequisiteProvider(context, user), context));
    }

    /// <summary>The stock eligibility as the host composes it, over the real Product chain in one company.</summary>
    private static ProductStockEligibilityService Eligibility(SqlServerRelationalApiFactory factory, Guid company)
    {
        var scope = factory.Services.CreateScope();
        var context = new CompanyContext();
        context.Set(company);
        var user = new User(company);
        var product = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        return new ProductStockEligibilityService(context, user, new GrantedIn(company),
            new ProductReadStore(product), new ProductFamilyRequirementPolicyV2(new SqlProductFamilyDefinitionSource(product)),
            new ProductUnitProfileAdapter(new ApprovedSettingsPrerequisiteProvider(context, user), context));
    }

    private static async Task<ProductDraftView> DraftAsync(HttpClient client, string key, ProductReferences? references = null,
        Guid? familyId = null)
    {
        var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDraftRequest($"SENTETIK {key}", GroupId, FamilyId: familyId,
                References: references)),
        };
        create.Headers.Add("Idempotency-Key", key);
        using var created = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product;
    }

    /// <summary>A kg card of the synthetic family, approved: Active at version 2.</summary>
    private static async Task<ProductDraftView> ActiveCardAsync(HttpClient client, string key)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["syn-thickness-mm"] = 1.5m,
            ["syn-width-mm"] = 1250m,
            ["syn-grade"] = "SYN-A",
            ["syn-coating"] = "syn-none",
        };
        var references = new ProductReferences(TemplateId, "1",
            attributes.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value), StringComparer.Ordinal),
            new ProductUnitReferences(Stock: SettingsUnitIdentitiesV1.Kilogram, Sales: SettingsUnitIdentitiesV1.Kilogram));
        var draft = await DraftAsync(client, key, references, HotCoil);
        var activate = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{draft.Id:D}/activation")
        {
            Content = JsonContent.Create(new ActivateProductRequest(draft.Version)),
        };
        activate.Headers.Add("Idempotency-Key", $"{key}-activate");
        using var activated = await client.SendAsync(activate);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var product = (await activated.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product;
        Assert.Equal(("Active", "2"), (product.Lifecycle, product.Version));
        return product;
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, string route, Guid id, string key, string version,
        string reason)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{id:D}/{route}")
        {
            Content = JsonContent.Create(new ChangeProductLifecycleRequest(version, reason)),
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static async Task AssertUnchangedAsync(SqlServerRelationalApiFactory factory, Guid id, int lifecycle, long version)
    {
        Assert.Equal((lifecycle, version), (await ScalarAsync<int>(factory, "SELECT [Lifecycle] FROM [product].[Products] WHERE [Id] = @id", id),
            await ScalarAsync<long>(factory, "SELECT [Version] FROM [product].[Products] WHERE [Id] = @id", id)));
    }

    private static string? Lifecycle(string cardJson)
    {
        using var card = JsonDocument.Parse(cardJson);
        return card.RootElement.GetProperty("product").GetProperty("lifecycle").GetString();
    }

    private sealed record AuditRow(string Operation, string PermissionCode, string SourceChannel, string Reason,
        string ActorId, long VersionBefore, long VersionAfter, string BeforeJson, string AfterJson);

    private static async Task<AuditRow> AuditRowAsync(SqlServerRelationalApiFactory factory, string eventType, Guid id)
    {
        await using var connection = new SqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT [Operation], [PermissionCode], [SourceChannel], [Reason], [ActorId], [EntityVersionBefore],
                   [EntityVersionAfter], [BeforeJson], [AfterJson]
            FROM [audit].[AuditEvents] WHERE [EventType] = @event AND [EntityId] = @entity
            """, connection);
        command.Parameters.AddWithValue("@event", eventType);
        command.Parameters.AddWithValue("@entity", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var row = new AuditRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetString(7), reader.GetString(8));
        Assert.False(await reader.ReadAsync());
        return row;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<int> CountAsync(SqlServerRelationalApiFactory factory, string sql, Guid id)
    {
        await using var connection = new SqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@entity", id.ToString("D"));
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<T> ScalarAsync<T>(SqlServerRelationalApiFactory factory, string sql, Guid id)
    {
        await using var connection = new SqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task GrantAsync(SqlServerRelationalApiFactory factory, params ProductMasterOperation[] operations)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        foreach (var operation in operations)
            identity.Set<ProductMasterAssignment>().Add(new ProductMasterAssignment(Guid.NewGuid(), ActorId,
                SystemRoleId, operation, ActorId, ActorExternalId, Guid.NewGuid(),
                "Synthetic isolated SQL fixture; not a Sinan or production grant", DateTimeOffset.UtcNow.AddMinutes(-1), null));
        await identity.SaveChangesAsync();
    }

    /// <summary>A SYNTHETIC release: one family, one template with the four role fields.</summary>
    private static async Task ReleaseAsync(string connection)
    {
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = """
            INSERT INTO [product].[Families] ([Id], [Code], [Name], [Cins], [Tur], [Tip], [RuleSet], [SourceRef]) VALUES
                (@hot, 'SYN-HR-COIL', N'Sentetik sıcak rulo', N'Sentetik cins', N'Sentetik tür sıcak', N'Sentetik tip rulo', 'gdc-band-coil', 'SYNTHETIC-TEST');
            INSERT INTO [product].[Templates] ([Id], [Version], [Code], [Name], [SourceRef]) VALUES
                (@template, 1, 'SYN-BAND-COIL', N'Sentetik bant/rulo', 'SYNTHETIC-TEST');
            INSERT INTO [product].[TemplateFamilies] ([TemplateId], [TemplateVersion], [FamilyId]) VALUES (@template, 1, @hot);
            INSERT INTO [product].[DictionaryEntries] ([Dictionary], [Code], [DisplayName], [Status], [SourceRef], [ParentCode], [MassGm2], [IsExplicitNone]) VALUES
                ('syn-grades', N'SYN-A', N'Sentetik A', 1, 'SYNTHETIC-TEST', NULL, NULL, 0),
                ('syn-coatings', N'syn-none', N'Sentetik kaplamasız', 1, 'SYNTHETIC-TEST', NULL, NULL, 1);
            INSERT INTO [product].[TemplateFields] ([TemplateId], [TemplateVersion], [Key], [Position], [Kind], [Role], [Required], [Dictionary], [AllowsCandidate], [AppliesWhenField], [AppliesWhenCode], [ParentField]) VALUES
                (@template, 1, 'syn-thickness-mm', 1, 1, 'thickness', 1, NULL, 0, NULL, NULL, NULL),
                (@template, 1, 'syn-width-mm', 2, 1, 'width', 1, NULL, 0, NULL, NULL, NULL),
                (@template, 1, 'syn-grade', 3, 2, 'quality', 1, 'syn-grades', 0, NULL, NULL, NULL),
                (@template, 1, 'syn-coating', 4, 2, 'coating', 1, 'syn-coatings', 0, NULL, NULL, NULL);
            """;
        command.Parameters.AddWithValue("@hot", HotCoil);
        command.Parameters.AddWithValue("@template", TemplateId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WithDatabaseAsync(ProductMasterOperation[] operations,
        Func<SqlServerRelationalApiFactory, TestObserver, Task> test)
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = databaseName }.ConnectionString;
        var observer = new TestObserver();
        var factory = new SqlServerRelationalApiFactory(baseConnection, services =>
        {
            services.RemoveAll<ProductRuntimeOptions>();
            services.AddSingleton(new ProductRuntimeOptions(connection,
                new ProductPilotIssuerBinding(ActorId, ActorExternalId), Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()));
            services.RemoveAll<IProductCommandAttemptObserver>();
            services.AddSingleton<IProductCommandAttemptObserver>(observer);
        }, databaseName: databaseName);
        try
        {
            using var startup = factory.CreateClient();
            await using (var scope = factory.Services.CreateAsyncScope())
                Assert.True(scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.IsSqlServer());
            await GrantAsync(factory, operations);
            await using (var sql = new SqlConnection(connection))
            {
                await sql.OpenAsync();
                await using var seed = sql.CreateCommand();
                seed.CommandText = "INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix]) VALUES (@id, N'SENTETIK deactivation group', 'PDEA');";
                seed.Parameters.AddWithValue("@id", GroupId);
                Assert.Equal(1, await seed.ExecuteNonQueryAsync());
            }

            await ReleaseAsync(connection);
            await test(factory, observer);
        }
        finally
        {
            await factory.DisposeAsync();
            if (!Regex.IsMatch(databaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Refusing cleanup outside this test's generated database identity.");
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END;";
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>The narrow selection read, granted company-wide in exactly one company.</summary>
    private sealed class GrantedIn(Guid company) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == company && permission == ProductPermissionManifestV1.OperationalSelectionRead
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal) { "SYNTHETIC-SELECTION" },
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class User(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "synthetic-deactivation-reader";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class TestObserver : IProductCommandAttemptObserver
    {
        public Func<ProductCommandAttemptStage, GroupSharedMasterCommandContext, CancellationToken, Task>? Callback { get; set; }

        public Task ObserveAsync(ProductCommandAttemptStage stage, GroupSharedMasterCommandContext authority,
            CancellationToken cancellationToken) => Callback?.Invoke(stage, authority, cancellationToken) ?? Task.CompletedTask;
    }
}
