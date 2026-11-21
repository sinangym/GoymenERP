using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Documents.UnitTests;

/// <summary>SQL Server model and fail-before-connection guards. No SQL behaviour is simulated here.</summary>
public sealed class DocumentBrandingModelTests
{
    [Theory]
    [InlineData(DocumentBrandingCommandsV1.Set, "SetBranding")]
    [InlineData(DocumentBrandingCommandsV1.Prepare, "PrepareTemplate")]
    [InlineData(DocumentBrandingCommandsV1.Publish, "PublishTemplate")]
    public void Branding_refusal_operations_pass_the_real_closed_Audit_mapper_and_keep_the_versioned_command(string command, string operation)
    {
        var company = new CompanyContext(); company.Set(Harness.CompanyA);
        var collector = new CapturedBrandingAudit();
        var services = new ServiceCollection();
        services.AddSingleton<ICompanyContext>(company);
        services.AddAuditModule(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = "Server=SENTETIK.invalid;Database=SENTETIK;Connect Timeout=1;",
            ["Audit:RefusedAttempts:Retention:Period"] = "P2Y",
            ["Audit:RefusedAttempts:Retention:PolicyRef"] = "policy-ref-1",
            ["Audit:RefusedAttempts:Retention:AutoDisposalEnabled"] = "false",
            ["Audit:RefusedAttempts:StandaloneWrite:Timeout"] = "00:00:05",
        }).Build());
        services.AddSingleton<IAuditEventCollector>(collector);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IRefusedAttemptRecorderV1>();
        var attempt = new RefusedAttemptV1
        {
            CompanyId = Harness.CompanyA, CommandName = command, Operation = DocumentBrandingService.RefusalOperation(command),
            TargetType = "DocumentBranding", TargetId = Harness.CompanyA.ToString("D"),
            ActorType = AuditActorTypes.User, ActorId = "SENTETIK-maker", PermissionCode = DocumentBrandingPermissionsV1.Manage,
            RoleCodes = ["SENTETIK"], HttpStatus = 409, ProblemCode = "document_template_maker_cannot_publish",
            RefusalClass = RefusedAttemptClassesV1.BusinessRule, CorrelationId = "SENTETIK-branding-refusal",
            SourceChannel = SourceChannels.Web, IdempotencyKey = "SENTETIK-branding-refusal",
        };
        // Record invokes the actual Audit owner's closed mapper; no database or standalone write is requested.
        recorder.Record(attempt);
        var draft = Assert.Single(collector.Drafts);
        Assert.Equal((command, operation, "DocumentBranding", Harness.CompanyA),
            (draft.EventType, draft.Operation, draft.EntityType, draft.CompanyId));
        Assert.Throws<ArgumentException>(() => recorder.Record(attempt with { Operation = command }));
        Assert.Single(collector.Drafts);
    }

    private sealed class CapturedBrandingAudit : IAuditEventCollector
    {
        internal List<AuditEventDraft> Drafts { get; } = [];
        public void Enqueue(AuditEventDraft draft) => Drafts.Add(draft);
    }

    [Fact]
    public void Branding_migration_is_ddl_and_guards_every_dropped_source_with_51936_before_any_drop()
    {
        var migration = new GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations.AddDocumentBrandingPublications();
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51936,", guard, StringComparison.Ordinal);
        foreach (var table in new[] { "CompanyBrandingRevisions", "DocumentTemplateDrafts", "DocumentTemplatePublications", "DocumentBrandingAttempts" })
            Assert.Contains("[documents].[" + table + "]", guard, StringComparison.Ordinal);
        Assert.Contains("[TemplatePublicationJson] IS NOT NULL", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void Branding_drafts_publications_and_attempts_are_company_keyed_filtered_and_append_only()
    {
        using var db = Context();
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(CompanyBrandingRevisionRow), typeof(DocumentTemplateDraftRow),
            typeof(DocumentTemplatePublicationRow), typeof(DocumentBrandingAttemptRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("documents", entity.GetSchema());
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.Single(entity.GetDeclaredQueryFilters());
        }
        var publication = model.FindEntityType(typeof(DocumentTemplatePublicationRow))!;
        Assert.Contains(publication.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(DocumentTemplateDraftRow)
            && key.Properties.Select(x => x.Name).SequenceEqual(["CompanyId", "Module", "TemplateCode", "DraftVersion", "BrandingVersion", "PreparedBy"]));
        Assert.Contains(publication.GetCheckConstraints(), check => check.Name is { } name && name.EndsWith("MakerChecker", StringComparison.Ordinal)
            && check.Sql.Contains("[PreparedBy] <> [PublishedBy]", StringComparison.Ordinal));
        Assert.Equal(DocumentsDbContext.IdentityCollation, publication.FindProperty("PublishedBy")!.GetCollation());
        var branding = model.FindEntityType(typeof(CompanyBrandingRevisionRow))!;
        Assert.Equal(2, branding.GetForeignKeys().Count(key => key.PrincipalEntityType.ClrType == typeof(DocumentVersionRow)));
        Assert.All(branding.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Each_new_owner_row_refuses_a_foreign_company_before_opening_a_connection(int kind)
    {
        using var db = Context();
        db.Add(Row(kind, Harness.CompanyB));
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Theory]
    [InlineData(0, EntityState.Modified)] [InlineData(1, EntityState.Modified)]
    [InlineData(2, EntityState.Modified)] [InlineData(3, EntityState.Modified)]
    [InlineData(0, EntityState.Deleted)] [InlineData(1, EntityState.Deleted)]
    [InlineData(2, EntityState.Deleted)] [InlineData(3, EntityState.Deleted)]
    public void Existing_branding_and_publication_history_cannot_be_updated_or_deleted(int kind, EntityState state)
    {
        using var db = Context();
        db.Entry(Row(kind, Harness.CompanyA)).State = state;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void A_successful_attempt_requires_real_json_and_the_terminal_shape_has_no_result_json()
    {
        using var db = Context();
        var model = db.GetService<IDesignTimeModel>().Model;
        var attempts = model.FindEntityType(typeof(DocumentBrandingAttemptRow))!;
        var constraint = attempts.GetCheckConstraints().Single(x => x.Name == "CK_DocumentBrandingAttempts_Result").Sql;
        Assert.Contains("[ResultJson] IS NOT NULL", constraint, StringComparison.Ordinal);
        Assert.Contains("[Status] = 409", constraint, StringComparison.Ordinal);
        Assert.Contains("[ResultJson] IS NULL", constraint, StringComparison.Ordinal);
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempts.FindPrimaryKey()!.Properties.Select(x => x.Name));
    }

    [Theory]
    [InlineData(DocumentTemplateModuleV1.Sales, "sales.order-pdf")]
    [InlineData(DocumentTemplateModuleV1.Inventory, "inventory.material-label.technical")]
    [InlineData(DocumentTemplateModuleV1.Logistics, "logistics.dispatch-note")]
    public void A_template_identity_belongs_to_exactly_one_module_and_prepare_is_separate_from_publish(DocumentTemplateModuleV1 module, string code)
    {
        Assert.True(DocumentBrandingContractV1.IsKnown(module, code));
        Assert.NotEqual(DocumentBrandingPermissionsV1.Prepare(module), DocumentBrandingPermissionsV1.Publish(module));
        foreach (var other in Enum.GetValues<DocumentTemplateModuleV1>().Where(x => x != module))
            Assert.False(DocumentBrandingContractV1.IsKnown(other, code));
        Assert.False(DocumentBrandingContractV1.IsKnown(module, "<script>"));
    }

    private static object Row(int kind, Guid company) => kind switch
    {
        0 => new CompanyBrandingRevisionRow { CompanyId = company, Version = 1, PreparedBy = "SENTETIK-maker", Reason = "SENTETIK" },
        1 => new DocumentTemplateDraftRow { CompanyId = company, Module = 1, TemplateCode = DocumentBrandingContractV1.SalesTemplate,
            Version = 1, BrandingVersion = 1, PreparedBy = "SENTETIK-maker", Reason = "SENTETIK" },
        2 => new DocumentTemplatePublicationRow { CompanyId = company, Module = 1, TemplateCode = DocumentBrandingContractV1.SalesTemplate,
            Version = 1, DraftVersion = 1, BrandingVersion = 1, Id = Guid.NewGuid(), PreparedBy = "SENTETIK-maker", PublishedBy = "SENTETIK-checker", Reason = "SENTETIK" },
        _ => new DocumentBrandingAttemptRow { CompanyId = company, CommandName = DocumentBrandingCommandsV1.Prepare,
            IdempotencyKeyHash = new string('a', 64), RequestHash = new string('b', 64), ActorId = "SENTETIK-maker", CorrelationId = "SENTETIK", Status = 409, Code = "SENTETIK" },
    };
    private static DocumentsDbContext Context()
    {
        var company = new CompanyContext(); company.Set(Harness.CompanyA);
        return new(new DbContextOptionsBuilder<DocumentsDbContext>().UseSqlServer("Server=SENTETIK.invalid;Database=SENTETIK;Connect Timeout=1;").Options, company);
    }
}
