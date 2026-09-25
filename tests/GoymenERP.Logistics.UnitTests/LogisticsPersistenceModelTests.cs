using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// LOG-4a persistent model without a database: schema, tenant filters, the ADR-011 identities, the
/// share-chain check, the write guard (refuses before any connection) and the published shell declarations.
/// What SQL Server enforces is proven by the relational acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsPersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void Every_logistics_table_is_in_its_schema_and_filtered_by_the_active_company()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(LoadingInstructionRow), typeof(LoadingInstructionLineRow), typeof(LoadingShareRow), typeof(LoadingAttemptRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(LogisticsDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Contains(entity.GetForeignKeys(), key => key.Properties.Any(property => property.Name == "CompanyId"));
        }

        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        Assert.Equal(("organization", "Companies"), (context.Model.FindEntityType(typeof(OrganizationCompanyReference))!.GetSchema(),
            context.Model.FindEntityType(typeof(OrganizationCompanyReference))!.GetTableName()));
    }

    [Fact]
    public void The_identities_are_the_attempt_key_the_line_and_share_ids_and_one_create_attempt_per_instruction()
    {
        using var context = Context();
        // Check constraints, collations and index filters are design-time configuration, not in the read-optimized runtime model.
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var attempts = designModel.FindEntityType(typeof(LoadingAttemptRow))!;
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempts.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        var design = designModel.FindEntityType(typeof(LoadingAttemptRow))!;
        Assert.Equal(LogisticsDbContext.IdentityCollation, design.FindProperty(nameof(LoadingAttemptRow.IdempotencyKeyHash))!.GetCollation());
        Assert.Equal(LogisticsDbContext.IdentityCollation, design.FindProperty(nameof(LoadingAttemptRow.CommandName))!.GetCollation());
        var creates = design.GetIndexes().Single(index => index.GetDatabaseName() == "UX_LoadingAttempts_InstructionId");
        Assert.True(creates.IsUnique);
        Assert.Equal("[InstructionId] IS NOT NULL AND [CommandName] = N'logistics.loading-instruction.create.v1'", creates.GetFilter());
        var lines = designModel.FindEntityType(typeof(LoadingInstructionLineRow))!;
        Assert.True(lines.GetIndexes().Single(index => index.GetDatabaseName() == "UX_LoadingInstructionLines_LineId").IsUnique);
        Assert.Contains(lines.GetCheckConstraints(), check => check.Name == "CK_LoadingInstructionLines_Shares"
            && check.Sql.Contains("[Loaded] <= [Instructed] AND [Shipped] <= [Loaded] AND [Delivered] <= [Shipped]", StringComparison.Ordinal));
        var shares = designModel.FindEntityType(typeof(LoadingShareRow))!;
        Assert.True(shares.GetIndexes().Single(index => index.GetDatabaseName() == "UX_LoadingShares_ShareId").IsUnique);
        Assert.Equal(3, shares.FindProperty(nameof(LoadingShareRow.Quantity))!.GetScale());
        var instructions = designModel.FindEntityType(typeof(LoadingInstructionRow))!;
        Assert.True(instructions.FindProperty(nameof(LoadingInstructionRow.Version))!.IsConcurrencyToken);
        Assert.Equal(LogisticsCommands.CreateV1, "logistics.loading-instruction.create.v1");
    }

    [Fact]
    public async Task The_write_guard_refuses_another_company_a_delete_and_a_moved_line_identity_before_any_connection()
    {
        await using (var context = Context())
        {
            context.Instructions.Add(Instruction(Guid.NewGuid()));
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("aktif şirket", refusal.Message, StringComparison.Ordinal);
        }

        await using (var context = Context(resolved: false))
        {
            context.Instructions.Add(Instruction(CompanyA));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            var row = Instruction(CompanyA);
            context.Instructions.Attach(row);
            context.Entry(row).State = EntityState.Deleted;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("silinmez", refusal.Message, StringComparison.Ordinal);
        }

        await using (var context = Context())
        {
            var line = new LoadingInstructionLineRow { CompanyId = CompanyA, InstructionId = Guid.NewGuid(), LineNo = 1, LineId = Guid.NewGuid(),
                SourceOrderId = Guid.NewGuid(), SourceOrderLineId = Guid.NewGuid(), ProductId = Guid.NewGuid(), UnitId = Guid.NewGuid(), Instructed = 1m };
            context.Lines.Attach(line);
            line.Loaded = 1m; // allowed
            line.ProductId = Guid.NewGuid(); // not allowed: identity never moves
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_shell_declarations_are_published_and_bound_to_the_manifest_names()
    {
        var provider = new LogisticsShellCapabilityProvider();
        var declarations = provider.GetDeclarations();
        Assert.Equal("1.1", provider.ContractVersion);
        Assert.Equal(5, declarations.Count);
        Assert.All(declarations, declaration => Assert.True(declaration.ImplementationAvailable));
        Assert.Equal(
            [
                ("logistics.instructions.create", "/lojistik/yukleme-talimatlari/yeni", LogisticsPermissionManifestV1.InstructionsCreate),
                ("logistics.instructions.detail", "/lojistik/yukleme-talimatlari/:instructionId", LogisticsPermissionManifestV1.InstructionsRead),
                ("logistics.instructions.list", "/lojistik/yukleme-talimatlari", LogisticsPermissionManifestV1.InstructionsRead),
            ],
            declarations.Where(declaration => declaration.ParentCapabilityId is null)
                .Select(declaration => (declaration.CapabilityId, declaration.RoutePath, declaration.RequiredPermissions.Single()))
                .OrderBy(item => item.CapabilityId, StringComparer.Ordinal).ToArray());
        Assert.Equal(5, LogisticsPermissionManifestV1.Names.Count);
        Assert.Equal("logistics.shipments.record", LogisticsPermissionManifestV1.ShipmentsRecord);
        Assert.Equal("logistics.deliveries.record", LogisticsPermissionManifestV1.DeliveriesRecord);
        Assert.Equal(5, LogisticsPermissionManifestV1.Names.Distinct(StringComparer.Ordinal).Count());
        // Master v3 §27.8: L-02 is the instruction list, L-03 the new/edit workspace; the detail is a view of one L-02 row.
        Assert.Equal(["L-03", "L-02-DETAIL", "L-02-DETAIL", "L-02-DETAIL", "L-02"],
            declarations.OrderBy(declaration => declaration.CapabilityId, StringComparer.Ordinal).Select(declaration => declaration.ScreenId).ToArray());
        var shipment = declarations.Single(item => item.CapabilityId == LogisticsShellCapabilityProvider.ShipmentCapabilityId);
        Assert.Equal(LogisticsShellCapabilityProvider.DetailCapabilityId, shipment.ParentCapabilityId);
        Assert.Equal([LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.ShipmentsRecord,
            ErpPermissions.InventoryPhysicalAllocationWrite], shipment.RequiredPermissions);
        var delivery = declarations.Single(item => item.CapabilityId == LogisticsShellCapabilityProvider.DeliveryCapabilityId);
        Assert.Equal(LogisticsShellCapabilityProvider.DetailCapabilityId, delivery.ParentCapabilityId);
        Assert.Equal([LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.DeliveriesRecord],
            delivery.RequiredPermissions);
    }

    private static LoadingInstructionRow Instruction(Guid company) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company, CustomerPartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), Status = 1, Version = 1,
        EffectiveOn = new DateOnly(2026, 9, 19), CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "SENTETIK", CorrelationId = "SENTETIK",
    };

    private static LogisticsDbContext Context(bool resolved = true)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        return new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
