using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// PRD-FAM-1 against a REAL SQL Server: the Product-owned family registry, its schema rules, the
/// source that reads it, the draft catalogue that binds to it and the family policy that judges a card
/// by it. The release inserted here is SYNTHETIC test data in this test's own database: the shape of the
/// decided release (#66 5739827887), never its values. No production release is loaded (Business Q6).
/// </summary>
public sealed class ProductFamilyRegistrySqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid GroupId = Guid.Parse("82000000-0000-0000-0000-000000000031");
    private static readonly Guid HotCoil = Guid.Parse("82000000-0000-4000-8000-000000000041");
    private static readonly Guid ColdStrip = Guid.Parse("82000000-0000-4000-8000-000000000042");
    private static readonly Guid TemplateId = Guid.Parse("82000000-0000-4000-8000-000000000051");

    private static readonly ProductMasterOperation[] DraftOperations =
        [ProductMasterOperation.Read, ProductMasterOperation.DraftCreate, ProductMasterOperation.DraftBasicEdit];

    [SqlServerFact]
    public Task An_empty_registry_is_unavailable_and_accepts_no_family_or_template() =>
        WithDatabaseAsync(release: false, async factory =>
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var source = new SqlProductFamilyDefinitionSource(scope.ServiceProvider.GetRequiredService<ProductDbContext>());
                var error = await Assert.ThrowsAsync<ProductOperationException>(() => source.GetAsync(default));
                Assert.Equal((503, UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode), (error.StatusCode, error.Code));
            }

            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var options = (await client.GetFromJsonAsync<ProductReferenceOptions>(
                $"/api/v1/products/reference-options?groupId={GroupId:D}"))!;
            Assert.Empty(options.Families);
            Assert.Empty(options.Templates);
            Assert.Contains("families", options.UnavailableCatalogs);
            Assert.Contains("templates", options.UnavailableCatalogs);

            using var withFamily = await CreateAsync(client, "family-registry-empty", HotCoil, templated: true, Valid());
            Assert.Equal(HttpStatusCode.BadRequest, withFamily.StatusCode);
            var fields = await FieldErrorsAsync(withFamily);
            Assert.Contains(("classification", "invalid_reference"), fields);
            Assert.Contains(("references.templateId", "invalid_reference"), fields);
            Assert.Equal(0, await ScalarAsync<int>(factory, "SELECT COUNT(*) FROM [product].[Products]"));
        });

    [SqlServerFact]
    public Task A_released_registry_is_read_whole_with_its_statuses_and_sources() =>
        WithDatabaseAsync(release: true, async factory =>
        {
            ProductFamilyDefinitionV2 definition;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                definition = await new SqlProductFamilyDefinitionSource(
                    scope.ServiceProvider.GetRequiredService<ProductDbContext>()).GetAsync(default);
            }

            ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);
            Assert.Equal([ColdStrip, HotCoil], definition.Families.Select(family => family.Id).Order());
            Assert.All(definition.Families, family => Assert.Equal("SYNTHETIC-TEST", family.SourceRef));
            var template = Assert.Single(definition.Templates);
            Assert.Equal((TemplateId, "1"), (template.Id, template.Version));
            Assert.Equal(new HashSet<Guid> { HotCoil, ColdStrip }, template.FamilyIds);
            Assert.Equal(["syn-thickness-mm", "syn-width-mm", "syn-grade", "syn-coating", "syn-designation", "syn-subgrade"],
                template.Fields.Select(field => field.Key));
            // A candidate stays a candidate; a mass is present only where the source stated one.
            Assert.Equal(ProductDefinitionEntryStatusV2.Candidate, definition.Entries.Single(entry => entry.Code == "SYN-C").Status);
            Assert.Equal(100m, definition.Entries.Single(entry => entry.Code == "SYN-Z1").MassGm2);
            Assert.Null(definition.Entries.Single(entry => entry.Code == "SYN-Z2").MassGm2);
            Assert.True(definition.Entries.Single(entry => entry.Code == "syn-none").IsExplicitNone);

            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var options = (await client.GetFromJsonAsync<ProductReferenceOptions>(
                $"/api/v1/products/reference-options?groupId={GroupId:D}"))!;
            Assert.Equal([ColdStrip, HotCoil], options.Families.Select(family => family.Id).Order());
            Assert.Equal((TemplateId, "1"), (Assert.Single(options.Templates).Id, options.Templates[0].Version));
            Assert.DoesNotContain("families", options.UnavailableCatalogs);
            Assert.DoesNotContain("templates", options.UnavailableCatalogs);
        });

    [SqlServerFact]
    public Task The_schema_refuses_what_the_release_rules_refuse_and_a_card_of_an_unreleased_family() =>
        WithDatabaseAsync(release: true, async factory =>
        {
            foreach (var (statement, constraint) in new[]
            {
                ("INSERT INTO [product].[DictionaryEntries] ([Dictionary], [Code], [DisplayName], [Status], [SourceRef], [IsExplicitNone]) "
                    + "VALUES ('syn-coatings', N'syn-other-none', N'x', 2, 'SYNTHETIC-TEST', 1);", "CK_DictionaryEntries_ExplicitNone"),
                ("INSERT INTO [product].[DictionaryEntries] ([Dictionary], [Code], [DisplayName], [Status], [SourceRef], [MassGm2], [IsExplicitNone]) "
                    + "VALUES ('syn-designations', N'SYN-Z0', N'x', 1, 'SYNTHETIC-TEST', 0, 0);", "CK_DictionaryEntries_Mass"),
                ("INSERT INTO [product].[DictionaryEntries] ([Dictionary], [Code], [DisplayName], [Status], [SourceRef], [IsExplicitNone]) "
                    + "VALUES ('syn-grades', N'SYN-X', N'x', 3, 'SYNTHETIC-TEST', 0);", "CK_DictionaryEntries_Status"),
                ("INSERT INTO [product].[TemplateFields] ([TemplateId], [TemplateVersion], [Key], [Position], [Kind], [Required], [AllowsCandidate]) "
                    + $"VALUES ('{TemplateId:D}', 1, 'syn-reference-without-dictionary', 99, 2, 0, 0);", "CK_TemplateFields_Kind"),
                ("INSERT INTO [product].[TemplateFamilies] ([TemplateId], [TemplateVersion], [FamilyId]) "
                    + $"VALUES ('{TemplateId:D}', 1, '82000000-0000-4000-8000-0000000000ff');", "FK_TemplateFamilies_Families_FamilyId"),
            })
            {
                var failure = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory, statement));
                Assert.Equal(547, failure.Number);
                Assert.Contains(constraint, failure.Message, StringComparison.Ordinal);
            }

            // A card names a released family or none: the foreign key holds it even without the store.
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var created = await CreateAsync(client, "family-registry-fk", HotCoil, templated: true, Valid());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product.Id;
            var orphan = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory,
                $"UPDATE [product].[Products] SET [FamilyId] = '82000000-0000-4000-8000-0000000000ff' WHERE [Id] = '{id:D}';"));
            Assert.Equal(547, orphan.Number);
            Assert.Contains("FK_Products_Families_FamilyId", orphan.Message, StringComparison.Ordinal);
        });

    [SqlServerFact]
    public Task A_draft_binds_a_released_family_and_template_and_the_policy_judges_the_stored_card() =>
        WithDatabaseAsync(release: true, async factory =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);

            // A Draft may be incomplete, and may hold a candidate: only the binding and the keys are checked at save.
            using var incomplete = await CreateAsync(client, "family-registry-incomplete", ColdStrip, templated: true,
                new Dictionary<string, object?> { ["syn-grade"] = "SYN-C" });
            Assert.Equal(HttpStatusCode.Created, incomplete.StatusCode);

            // An attribute the template does not have, or a template of no released release, is refused at save.
            using var unknownKey = await CreateAsync(client, "family-registry-unknown-key", HotCoil, templated: true,
                new Dictionary<string, object?>(Valid()) { ["syn-invented"] = "x" });
            Assert.Equal(HttpStatusCode.BadRequest, unknownKey.StatusCode);
            Assert.Equal([("references.attributes.syn-invented", "invalid_reference")], await FieldErrorsAsync(unknownKey));
            using var otherVersion = await CreateAsync(client, "family-registry-other-version", HotCoil, templated: true, Valid(),
                templateVersion: "2");
            Assert.Equal(HttpStatusCode.BadRequest, otherVersion.StatusCode);
            Assert.Equal([("references.templateId", "invalid_reference")], await FieldErrorsAsync(otherVersion));

            using var complete = await CreateAsync(client, "family-registry-complete", HotCoil, templated: true, Valid());
            Assert.Equal(HttpStatusCode.Created, complete.StatusCode);
            var completeId = (await complete.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product.Id;
            var incompleteId = (await incomplete.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product.Id;

            // The stored cards, read back, judged by the policy over the real registry. A valid card has no
            // FAMILY error; its only errors are the unit bindings a card cannot hold yet (PRD-UNIT-1).
            var stored = (await client.GetFromJsonAsync<ProductCardView>($"/api/v1/products/{completeId:D}"))!;
            var partial = (await client.GetFromJsonAsync<ProductCardView>($"/api/v1/products/{incompleteId:D}"))!;
            await using var scope = factory.Services.CreateAsyncScope();
            var policy = new ProductFamilyRequirementPolicyV2(
                new SqlProductFamilyDefinitionSource(scope.ServiceProvider.GetRequiredService<ProductDbContext>()));
            Assert.Equal(
                [("references.units.stock", ProductFamilyPolicyCodesV1.Required), ("references.units.sales", ProductFamilyPolicyCodesV1.Required)],
                (await policy.ValidateAsync(stored, default)).Select(error => (error.Field, error.Code)));
            Assert.Equal(
                [
                    ("references.attributes.syn-thickness-mm", ProductFamilyPolicyCodesV1.Required),
                    ("references.attributes.syn-width-mm", ProductFamilyPolicyCodesV1.Required),
                    ("references.attributes.syn-grade", ProductFamilyPolicyCodesV1.ValueCandidate),
                    ("references.attributes.syn-coating", ProductFamilyPolicyCodesV1.Required),
                    ("references.units.stock", ProductFamilyPolicyCodesV1.Required),
                    ("references.units.sales", ProductFamilyPolicyCodesV1.Required),
                ],
                (await policy.ValidateAsync(partial, default)).Select(error => (error.Field, error.Code)));
        });

    private static Dictionary<string, object?> Valid() => new()
    {
        ["syn-thickness-mm"] = 1.5m,
        ["syn-width-mm"] = 1250m,
        ["syn-grade"] = "SYN-A",
        ["syn-coating"] = "syn-none",
    };

    /// <summary>A SYNTHETIC release with the decided shape: two families, one template, approved and candidate entries.</summary>
    private static async Task ReleaseAsync(string connection)
    {
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = """
            INSERT INTO [product].[Families] ([Id], [Code], [Name], [Cins], [Tur], [Tip], [RuleSet], [SourceRef]) VALUES
                (@hot, 'SYN-HR-COIL', N'Sentetik sıcak rulo', N'Sentetik cins', N'Sentetik tür sıcak', N'Sentetik tip rulo', 'gdc-band-coil', 'SYNTHETIC-TEST'),
                (@cold, 'SYN-CR-STRIP', N'Sentetik soğuk bant', N'Sentetik cins', N'Sentetik tür soğuk', N'Sentetik tip bant', 'gdc-band-coil', 'SYNTHETIC-TEST');
            INSERT INTO [product].[Templates] ([Id], [Version], [Code], [Name], [SourceRef]) VALUES
                (@template, 1, 'SYN-BAND-COIL', N'Sentetik bant/rulo', 'SYNTHETIC-TEST');
            INSERT INTO [product].[TemplateFamilies] ([TemplateId], [TemplateVersion], [FamilyId]) VALUES
                (@template, 1, @hot), (@template, 1, @cold);
            INSERT INTO [product].[DictionaryEntries] ([Dictionary], [Code], [DisplayName], [Status], [SourceRef], [ParentCode], [MassGm2], [IsExplicitNone]) VALUES
                ('syn-grades', N'SYN-A', N'Sentetik A', 1, 'SYNTHETIC-TEST', NULL, NULL, 0),
                ('syn-grades', N'SYN-C', N'Sentetik aday C', 2, 'SYNTHETIC-TEST', NULL, NULL, 0),
                ('syn-coatings', N'syn-none', N'Sentetik kaplamasız', 1, 'SYNTHETIC-TEST', NULL, NULL, 1),
                ('syn-coatings', N'syn-zinc', N'Sentetik çinko', 1, 'SYNTHETIC-TEST', NULL, NULL, 0),
                ('syn-designations', N'SYN-Z1', N'Sentetik Z1', 1, 'SYNTHETIC-TEST', NULL, 100, 0),
                ('syn-designations', N'SYN-Z2', N'Sentetik aday Z2', 2, 'SYNTHETIC-TEST', NULL, NULL, 0),
                ('syn-subgrades', N'SYN-1', N'Sentetik aday alt kalite', 2, 'SYNTHETIC-TEST', N'SYN-A', NULL, 0);
            INSERT INTO [product].[TemplateFields] ([TemplateId], [TemplateVersion], [Key], [Position], [Kind], [Role], [Required], [Dictionary], [AllowsCandidate], [AppliesWhenField], [AppliesWhenCode], [ParentField]) VALUES
                (@template, 1, 'syn-thickness-mm', 1, 1, 'thickness', 1, NULL, 0, NULL, NULL, NULL),
                (@template, 1, 'syn-width-mm', 2, 1, 'width', 1, NULL, 0, NULL, NULL, NULL),
                (@template, 1, 'syn-grade', 3, 2, 'quality', 1, 'syn-grades', 0, NULL, NULL, NULL),
                (@template, 1, 'syn-coating', 4, 2, 'coating', 1, 'syn-coatings', 0, NULL, NULL, NULL),
                (@template, 1, 'syn-designation', 5, 2, NULL, 0, 'syn-designations', 0, 'syn-coating', N'syn-zinc', NULL),
                (@template, 1, 'syn-subgrade', 6, 2, NULL, 0, 'syn-subgrades', 1, NULL, NULL, 'syn-grade');
            """;
        command.Parameters.AddWithValue("@hot", HotCoil);
        command.Parameters.AddWithValue("@cold", ColdStrip);
        command.Parameters.AddWithValue("@template", TemplateId);
        await command.ExecuteNonQueryAsync();
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string key, Guid familyId, bool templated,
        Dictionary<string, object?> attributes, string templateVersion = "1")
    {
        var references = templated
            ? new ProductReferences(TemplateId, templateVersion,
                attributes.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value), StringComparer.Ordinal))
            : null;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDraftRequest($"SENTETIK {key}", GroupId, FamilyId: familyId,
                References: references)),
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static async Task<(string Field, string Code)[]> FieldErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ProductApiV1.ValidationFailed, document.RootElement.GetProperty("code").GetString());
        return [.. document.RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => (error.GetProperty("field").GetString()!, error.GetProperty("code").GetString()!))];
    }

    private static async Task ExecuteAsync(SqlServerRelationalApiFactory factory, string statement)
    {
        await using var sql = new SqlConnection(factory.DatabaseConnectionString);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = statement;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(SqlServerRelationalApiFactory factory, string statement)
    {
        await using var sql = new SqlConnection(factory.DatabaseConnectionString);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = statement;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task WithDatabaseAsync(bool release, Func<SqlServerRelationalApiFactory, Task> test)
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = databaseName }.ConnectionString;
        var factory = new SqlServerRelationalApiFactory(baseConnection, services =>
        {
            services.RemoveAll<ProductRuntimeOptions>();
            services.AddSingleton(new ProductRuntimeOptions(connection,
                new ProductPilotIssuerBinding(ActorId, ActorExternalId), Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()));
        }, databaseName: databaseName);
        try
        {
            using var startup = factory.CreateClient();
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.True(identity.Database.IsSqlServer());
                foreach (var operation in DraftOperations)
                    identity.Set<ProductMasterAssignment>().Add(new ProductMasterAssignment(Guid.NewGuid(), ActorId,
                        SystemRoleId, operation, ActorId, ActorExternalId, Guid.NewGuid(),
                        "Synthetic isolated SQL fixture; not a Sinan or production grant", DateTimeOffset.UtcNow.AddMinutes(-1), null));
                await identity.SaveChangesAsync();
            }

            await using (var sql = new SqlConnection(connection))
            {
                await sql.OpenAsync();
                await using var seed = sql.CreateCommand();
                seed.CommandText = "INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix]) VALUES (@id, N'SENTETIK family registry group', 'PFAM');";
                seed.Parameters.AddWithValue("@id", GroupId);
                Assert.Equal(1, await seed.ExecuteNonQueryAsync());
            }

            if (release)
            {
                await ReleaseAsync(connection);
            }

            await test(factory);
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
}
