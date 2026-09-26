using System.Data.Common;
using System.Globalization;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Manufacturing_return_and_output_commit_once_and_replay_after_the_order_advances()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var customer = Guid.NewGuid();
        // The issued parent is the company's own, company-custodied roll: the input-issue entry refuses any other
        // position until NEEDS-SINAN #66/5830378162 is decided (ManufacturingCustomerOwnedInputGuardV1), so a
        // customer-owned issue trace cannot be produced through the public path. The output is still bound to the
        // exact issued position: a customer owner or a customer custodian on the output is refused below.
        // TODO(#66/5830378162): once the decision enables customer-owned input, add the customer-owned
        // return/output journey back here on top of the decided commercial link.
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-RETURN-OUTPUT", 100m);
        Assert.Equal((StockPartyKind.Company, CompanyA, StockPartyKind.Company, CompanyA),
            (seed.OwnerKind, seed.OwnerId, seed.CustodianKind, seed.CustodianId));
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingInventoryOutputAdmission(
            new ManufacturingOutputProducts(), new ManufacturingOutputLocations(seed.LocationId));
        await using var scope = ReturnOutputHostScope.Create(run, authority, admission);
        var returned = ReturnCommand(seed, 10m);
        var returnedMetadata = ManufacturingHostMetadata(run, "return-commit");

        var returnResult = await scope.Host.ReturnAsync(returned, returnedMetadata);
        var outputSeed = seed with
        {
            OrderVersion = returnResult.OrderVersion,
            ParentLabelVersion = returnResult.ParentLabelVersion,
        };
        var output = OutputCommand(outputSeed, returnResult.OrderVersion, returnResult.ParentLabelVersion, 80m);
        var outputMetadata = ManufacturingHostMetadata(run, "output-commit");
        var wrongOwner = output with { OwnerKind = StockPartyKind.Counterparty, OwnerId = customer };
        var wrongOwnerMetadata = ManufacturingHostMetadata(run, "output-wrong-owner");
        var wrongOwnerError = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            scope.Host.DeclareOutputAsync(wrongOwner, wrongOwnerMetadata));
        Assert.Equal((409, InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict),
            (wrongOwnerError.Status, wrongOwnerError.Code));
        await AssertNoOutputEffectAsync(run, outputSeed, wrongOwner, wrongOwnerMetadata.CorrelationId);

        var wrongCustodian = output with
        {
            CustodianKind = StockPartyKind.Counterparty,
            CustodianId = customer,
        };
        var wrongCustodianMetadata = ManufacturingHostMetadata(run, "output-wrong-custodian");
        var wrongCustodianError = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            scope.Host.DeclareOutputAsync(wrongCustodian, wrongCustodianMetadata));
        Assert.Equal((409, InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict),
            (wrongCustodianError.Status, wrongCustodianError.Code));
        await AssertNoOutputEffectAsync(run, outputSeed, wrongCustodian, wrongCustodianMetadata.CorrelationId);

        var outputResult = await scope.Host.DeclareOutputAsync(output, outputMetadata);

        Assert.Equal(ManufacturingWriteOutcome.Recorded, await scope.Store.RecordWorkInProgressAsync(
            seed.OrderId, outputResult.OrderVersion, Guid.NewGuid(), seed.ParentLabelId,
            new Kilograms(10m), seed.OccurredAtUtc.AddMinutes(3),
            ManufacturingHostMetadata(run, "advance-return-output-order")));
        var returnReplayMetadata = ManufacturingHostMetadata(run, "return-replay");
        var outputReplayMetadata = ManufacturingHostMetadata(run, "output-replay");
        var returnReplay = await scope.Host.ReturnAsync(returned, returnReplayMetadata);
        var outputReplay = await scope.Host.DeclareOutputAsync(output, outputReplayMetadata);

        Assert.False(returnResult.IsReplay);
        Assert.False(outputResult.IsReplay);
        Assert.True(returnReplay.IsReplay);
        Assert.True(outputReplay.IsReplay);
        Assert.Equal(3, returnResult.OrderVersion);
        Assert.Equal(returnResult.OrderVersion, returnReplay.OrderVersion);
        Assert.Equal(4, outputResult.OrderVersion);
        Assert.Equal(outputResult.OrderVersion, outputReplay.OrderVersion);
        Assert.Equal(4, returnResult.ParentLabelVersion);
        Assert.Equal(4, outputResult.ParentLabelVersion);
        Assert.Equal(2, outputResult.LabelVersion);
        Assert.Equal(5L, await run.ScalarAsync<long>(
            "SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", seed.OrderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", returned.ReturnId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", output.OutputId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", returned.MovementId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", output.MovementId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.MovementLines WHERE MovementId = @movement "
            + "AND OwnerKind = @ownerKind AND OwnerId = @ownerId "
            + "AND CustodianKind = @custodianKind AND CustodianId = @custodianId",
            ("@movement", output.MovementId), ("@ownerKind", (int)StockPartyKind.Company),
            ("@ownerId", CompanyA), ("@custodianKind", (int)StockPartyKind.Company),
            ("@custodianId", CompanyA)));
        Assert.Equal(10m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 1",
            ("@id", seed.ParentLabelId)));
        Assert.Equal(80m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 1",
            ("@id", output.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", returned.ReturnId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", output.OutputId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId IN (@returnReplay, @outputReplay)",
            ("@returnReplay", returnReplayMetadata.CorrelationId),
            ("@outputReplay", outputReplayMetadata.CorrelationId)));
    }

    [SqlServerFact]
    public async Task Manufacturing_return_rolls_back_both_owners_when_inventory_stage_fails()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-RETURN-ROLLBACK", 50m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        await using var scope = ReturnOutputHostScope.Create(run, authority, new ManufacturingOutputAdmission(),
            (stage, _) => stage == ManufacturingInventoryReturnOutputStage.ReturnInventoryStaged
                ? Task.FromException(new SyntheticReturnOutputFailure())
                : Task.CompletedTask);
        var command = ReturnCommand(seed, 10m);
        var metadata = ManufacturingHostMetadata(run, "return-stage-failure");

        await Assert.ThrowsAsync<SyntheticReturnOutputFailure>(() => scope.Host.ReturnAsync(command, metadata));

        await AssertNoReturnEffectAsync(run, seed, command, metadata.CorrelationId);
    }

    [SqlServerFact]
    public async Task Manufacturing_output_rolls_back_both_owners_when_fresh_admission_is_revoked()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-OUTPUT-ROLLBACK", 50m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingOutputAdmission(true, false);
        await using var scope = ReturnOutputHostScope.Create(run, authority, admission);
        var command = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 40m);
        var metadata = ManufacturingHostMetadata(run, "output-admission-revoked");

        await Assert.ThrowsAsync<SyntheticOutputAdmissionFailure>(() =>
            scope.Host.DeclareOutputAsync(command, metadata));

        Assert.Equal(2, admission.Calls);
        await AssertNoOutputEffectAsync(run, seed, command, metadata.CorrelationId);
    }

    [SqlServerFact]
    public async Task Manufacturing_return_refuses_a_legacy_fact_without_backfilling_inventory()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-RETURN-LEGACY", 50m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        await using var scope = ReturnOutputHostScope.Create(run, authority, new ManufacturingOutputAdmission());
        var command = ReturnCommand(seed, 10m);
        Assert.Equal(ManufacturingWriteOutcome.Recorded, await scope.Store.ReturnUnusedInputAsync(
            command.OrderId, command.ExpectedOrderVersion, command.ReturnId, command.ParentLabelId,
            command.Quantity, command.OccurredAtUtc, ManufacturingHostMetadata(run, "legacy-return-fact")));

        var error = await Assert.ThrowsAsync<ManufacturingInventoryReturnOutputHostException>(() =>
            scope.Host.ReturnAsync(command, ManufacturingHostMetadata(run, "legacy-return-retry")));

        Assert.Equal(ManufacturingInventoryReturnOutputHostCodes.IncompleteRealization, error.Code);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", command.ReturnId)));
        Assert.Equal(3L, await run.ScalarAsync<long>(
            "SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", seed.OrderId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", command.MovementId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", command.ReturnId)));
        Assert.Equal(0m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 1",
            ("@id", seed.ParentLabelId)));
    }

    [SqlServerFact]
    public async Task Manufacturing_output_refuses_an_inventory_orphan_without_backfilling_the_fact()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-OUTPUT-ORPHAN", 50m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        var admission = new ManufacturingOutputAdmission();
        await using var scope = ReturnOutputHostScope.Create(run, authority, admission);
        var command = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 40m);
        var metadata = ManufacturingHostMetadata(run, "output-inventory-orphan");
        var orphan = await CommitOutputOrphanAsync(run, command, metadata);
        Assert.False(orphan.IsReplay);
        var auditCount = await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation",
            ("@correlation", metadata.CorrelationId));

        var error = await Assert.ThrowsAsync<ManufacturingInventoryReturnOutputHostException>(() =>
            scope.Host.DeclareOutputAsync(command, metadata));

        Assert.Equal(ManufacturingInventoryReturnOutputHostCodes.IncompleteRealization, error.Code);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", command.OutputId)));
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", seed.OrderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", command.MovementId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", command.OutputId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @id", ("@id", command.LabelId)));
        Assert.Equal(40m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 1",
            ("@id", command.LabelId)));
        Assert.Equal(auditCount, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation",
            ("@correlation", metadata.CorrelationId)));
    }

    private static async Task<ReturnOutputSeed> PrepareIssuedOrderAsync(Run run, string code, decimal quantity)
        => await PrepareIssuedOrderAsync(run, code, quantity,
            StockPartyKind.Company, CompanyA, StockPartyKind.Company, CompanyA);

    private static async Task<ReturnOutputSeed> PrepareIssuedOrderAsync(
        Run run,
        string code,
        decimal quantity,
        StockPartyKind ownerKind,
        Guid ownerId,
        StockPartyKind custodianKind,
        Guid custodianId)
    {
        var parent = await SeedManufacturingParentAsync(run, code, quantity,
            ownerKind, ownerId, custodianKind, custodianId);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        await using var issueScope = ManufacturingHostScope.Create(run, CompanyA, authority);
        var issue = await SeedManufacturingOrderAsync(
            run, issueScope.Store, parent.Label.Id, expectedLabelVersion: 2, quantity);
        var issued = await issueScope.Host.IssueAsync(issue, ManufacturingHostMetadata(run, "prepare-issued-order"));
        return new ReturnOutputSeed(
            issue.OrderId, issue.ParentLabelId, issued.OrderVersion, issued.ParentLabelVersion,
            Enum.Parse<StockPartyKind>(issued.ParentPosition.OwnerKind, ignoreCase: false),
            issued.ParentPosition.OwnerId,
            Enum.Parse<StockPartyKind>(issued.ParentPosition.CustodianKind, ignoreCase: false),
            issued.ParentPosition.CustodianId,
            issued.ParentPosition.LocationId,
            issue.OccurredAtUtc);
    }

    private static ManufacturingInventoryInputReturnCommand ReturnCommand(ReturnOutputSeed seed, decimal quantity) =>
        new(seed.OrderId, seed.OrderVersion, Guid.NewGuid(), seed.ParentLabelId, seed.ParentLabelVersion,
            Guid.NewGuid(), new Kilograms(quantity), seed.OccurredAtUtc.AddMinutes(1));

    private static ManufacturingInventoryOutputCommand OutputCommand(
        ReturnOutputSeed seed, long orderVersion, long parentLabelVersion, decimal quantity) => new(
        seed.OrderId,
        orderVersion,
        Guid.NewGuid(),
        seed.ParentLabelId,
        parentLabelVersion,
        OutputClass.GoodProduct,
        InventoryHostProduct,
        "1",
        Guid.NewGuid(),
        Guid.NewGuid(),
        $"SYN-MFG-OUTPUT-{Guid.NewGuid():N}",
        "SYN-HEAT",
        "SYN-COIL",
        [],
        seed.OwnerKind,
        seed.OwnerId,
        seed.CustodianKind,
        seed.CustodianId,
        seed.LocationId,
        BlockState.Free,
        Guid.NewGuid(),
        new Kilograms(quantity),
        seed.OccurredAtUtc.AddMinutes(2));

    private static async Task<InventoryManufacturingOutputResultV1> CommitOutputOrphanAsync(
        Run run, ManufacturingInventoryOutputCommand command, ManufacturingCommandMetadata metadata)
    {
        var request = new InventoryManufacturingOutputRequestV1(
            CompanyA,
            command.OrderId,
            command.OutputId,
            checked(command.ExpectedOrderVersion + 1).ToString(CultureInfo.InvariantCulture),
            command.ParentLabelId,
            command.ExpectedParentLabelVersion.ToString(CultureInfo.InvariantCulture),
            command.OutputClass.ToString(),
            command.ProductId,
            command.ExpectedProductVersion,
            command.LotId,
            command.LabelId,
            command.LabelCode,
            command.HeatNumber,
            command.CoilNumber,
            command.RequiredDocuments,
            command.OwnerKind,
            command.OwnerId,
            command.CustodianKind,
            command.CustodianId,
            command.LocationId,
            command.InitialBlock,
            command.MovementId,
            command.Quantity.Value.ToString("0.###", CultureInfo.InvariantCulture),
            command.OccurredAtUtc);
        var inventoryMetadata = new InventoryCommandMetadata(
            metadata.ActorId,
            metadata.PermissionCode,
            metadata.RoleCodes,
            metadata.CorrelationId,
            metadata.CausationId,
            $"mfg-output:{CompanyA:N}:{command.OrderId:N}:{command.OutputId:N}",
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent);
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var result = await participant.StageOutputAsync(request, inventoryMetadata);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task AssertNoReturnEffectAsync(
        Run run, ReturnOutputSeed seed, ManufacturingInventoryInputReturnCommand command, string correlationId)
    {
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", command.ReturnId)));
        Assert.Equal(seed.OrderVersion, await run.ScalarAsync<long>(
            "SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", seed.OrderId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", command.MovementId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", command.ReturnId)));
        Assert.Equal(0m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @id AND Block = 1",
            ("@id", seed.ParentLabelId)));
        Assert.Equal(seed.ParentLabelVersion, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @id", ("@id", seed.ParentLabelId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation",
            ("@correlation", correlationId)));
    }

    private static async Task AssertNoOutputEffectAsync(
        Run run, ReturnOutputSeed seed, ManufacturingInventoryOutputCommand command, string correlationId)
    {
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", command.OutputId)));
        Assert.Equal(seed.OrderVersion, await run.ScalarAsync<long>(
            "SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", seed.OrderId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", command.MovementId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", command.OutputId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @id", ("@id", command.LabelId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Lots WHERE Id = @id", ("@id", command.LotId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation",
            ("@correlation", correlationId)));
    }

    private sealed class ReturnOutputHostScope(
        ManufacturingDbContext context,
        ManufacturingStore store,
        ManufacturingInventoryReturnOutputHost host) : IAsyncDisposable
    {
        public ManufacturingStore Store => store;
        public ManufacturingInventoryReturnOutputHost Host => host;

        public static ReturnOutputHostScope Create(
            Run run,
            IErpAuthorityRevalidationService authority,
            IManufacturingInventoryOutputAdmission admission,
            Func<ManufacturingInventoryReturnOutputStage, CancellationToken, Task>? observer = null)
        {
            var company = new CompanyContext();
            company.Set(CompanyA);
            var audit = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<ManufacturingDbContext>()
                .UseSqlServer(run.Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ManufacturingDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(audit, company))
                .Options;
            var context = new ManufacturingDbContext(options, company);
            var store = new ManufacturingStore(context, company, audit, TimeProvider.System);
            var host = new ManufacturingInventoryReturnOutputHost(
                context, store, company, authority, ManufacturingSettings(run), admission, observer);
            return new ReturnOutputHostScope(context, store, host);
        }

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }

    private sealed class ManufacturingOutputAdmission(params bool[] decisions)
        : IManufacturingInventoryOutputAdmission
    {
        private int calls;
        public int Calls => calls;

        public Task ValidateAsync(
            Guid companyId, ManufacturingInventoryOutputCommand command, CancellationToken cancellationToken)
        {
            Assert.Equal(CompanyA, companyId);
            Assert.NotEqual(Guid.Empty, command.ProductId);
            cancellationToken.ThrowIfCancellationRequested();
            var index = Math.Min(Interlocked.Increment(ref calls) - 1, decisions.Length - 1);
            if (decisions.Length > 0 && !decisions[index])
            {
                throw new SyntheticOutputAdmissionFailure();
            }

            return Task.CompletedTask;
        }
    }

    private sealed record ReturnOutputSeed(
        Guid OrderId,
        Guid ParentLabelId,
        long OrderVersion,
        long ParentLabelVersion,
        StockPartyKind OwnerKind,
        Guid OwnerId,
        StockPartyKind CustodianKind,
        Guid CustodianId,
        Guid LocationId,
        DateTimeOffset OccurredAtUtc);

    private sealed class ManufacturingOutputProducts : IProductStockEligibilityV1
    {
        public Task<ProductStockEligibilityV1> EvaluateAsync(
            ProductStockSelectionV1 selection,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProductStockEligibilityV1(
                new ProductStockSnapshotV1(
                    selection.ProductId,
                    selection.ExpectedProductVersion,
                    "SYN-MFG-OUTPUT",
                    "Sentetik üretim çıktısı",
                    new ProductSalesUnitProfileV1(
                        CompanyA,
                        Guid.Parse("77000000-0000-0000-0000-000000000001"),
                        Guid.Parse("77000000-0000-0000-0000-000000000002"),
                        "1"),
                    selection.EffectiveOn),
                []));
        }
    }

    private sealed class ManufacturingOutputLocations(Guid expectedLocationId)
        : IOrganizationLocationStatusReaderV1
    {
        public Task<OrganizationLocationStatusV1?> ReadAsync(
            Guid companyId,
            Guid locationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<OrganizationLocationStatusV1?>(
                companyId == CompanyA && locationId == expectedLocationId
                    ? new OrganizationLocationStatusV1(locationId, true, false)
                    : null);
        }
    }

    private sealed class SyntheticReturnOutputFailure : Exception;
    private sealed class SyntheticOutputAdmissionFailure : Exception;
}
