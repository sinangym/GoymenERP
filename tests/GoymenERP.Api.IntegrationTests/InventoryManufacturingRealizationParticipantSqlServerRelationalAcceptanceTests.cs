using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private static readonly DateTimeOffset ManufacturingAt =
        new(2026, 9, 24, 16, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Manufacturing_issue_return_and_output_commit_once_replay_and_keep_exact_physical_trace()
    {
        await using var run = await Run.StartAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-PARENT-COMMIT", 100m);
        var orderId = Guid.NewGuid();
        var issue = IssueRequest(orderId, parent.Label.Id, "2", "30");
        var issueMetadata = ManufacturingMetadata(run, "issue");

        var issued = await CommitIssueAsync(run, issue, issueMetadata);
        Assert.False(issued.IsReplay);
        Assert.Equal("3", issued.ParentLabelVersion);

        var returnedRequest = ReturnRequest(orderId, parent.Label.Id, issued.ParentLabelVersion, "10");
        var returnMetadata = ManufacturingMetadata(run, "return");
        var returned = await CommitReturnAsync(run, returnedRequest, returnMetadata);
        Assert.False(returned.IsReplay);
        Assert.Equal("4", returned.ParentLabelVersion);

        var outputRequest = OutputRequest(orderId, parent.Label.Id, returned.ParentLabelVersion,
            parent.LocationId, "50");
        var outputMetadata = ManufacturingMetadata(run, "output");
        var output = await CommitOutputAsync(run, outputRequest, outputMetadata);
        Assert.False(output.IsReplay);
        Assert.Equal(("4", "2"), (output.ParentLabelVersion, output.LabelVersion));

        Assert.Equal(80m, await run.ScalarAsync<decimal>(
            "SELECT SUM(QuantityKg) FROM inventory.Balances WHERE LabelId = @label",
            ("@label", parent.Label.Id)));
        Assert.Equal(50m, await run.ScalarAsync<decimal>(
            "SELECT SUM(QuantityKg) FROM inventory.Balances WHERE LabelId = @label",
            ("@label", outputRequest.LabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @movement AND Kind = 2 "
            + "AND SourceKind = @kind AND SourceId = @order AND SourceLineId = @fact",
            ("@movement", issue.MovementId),
            ("@kind", InventoryManufacturingRealizationContractV1.InputIssueSourceKind),
            ("@order", orderId), ("@fact", issue.IssueId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @movement AND Kind = 5 "
            + "AND SourceKind = @kind AND SourceId = @order AND SourceLineId = @fact",
            ("@movement", returnedRequest.MovementId),
            ("@kind", InventoryManufacturingRealizationContractV1.InputReturnSourceKind),
            ("@order", orderId), ("@fact", returnedRequest.ReturnId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @movement AND Kind = 1 "
            + "AND SourceKind = @kind AND SourceId = @order AND SourceLineId = @fact",
            ("@movement", outputRequest.MovementId),
            ("@kind", InventoryManufacturingRealizationContractV1.OutputSourceKind),
            ("@order", orderId), ("@fact", outputRequest.OutputId)));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId IN (@issue, @return, @output)",
            ("@issue", issue.IssueId), ("@return", returnedRequest.ReturnId),
            ("@output", outputRequest.OutputId)));

        var issueReplay = await CommitIssueAsync(run, issue,
            issueMetadata with { CorrelationId = "manufacturing-issue-retry" });
        var returnReplay = await CommitReturnAsync(run, returnedRequest,
            returnMetadata with { CorrelationId = "manufacturing-return-retry" });
        var outputReplay = await CommitOutputAsync(run, outputRequest,
            outputMetadata with { CorrelationId = "manufacturing-output-retry" });
        Assert.True(issueReplay.IsReplay);
        Assert.True(returnReplay.IsReplay);
        Assert.True(outputReplay.IsReplay);
        Assert.Equal(issueMetadata.CorrelationId, issueReplay.OriginalCorrelationId);
        Assert.Equal(returnMetadata.CorrelationId, returnReplay.OriginalCorrelationId);
        Assert.Equal(outputMetadata.CorrelationId, outputReplay.OriginalCorrelationId);
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId IN (@issue, @return, @output)",
            ("@issue", issue.IssueId), ("@return", returnedRequest.ReturnId),
            ("@output", outputRequest.OutputId)));
    }

    [SqlServerFact]
    public async Task Manufacturing_customer_owned_parent_is_issued_without_caller_ownership_and_return_output_keep_owner_trace()
    {
        await using var run = await Run.StartAsync();
        var customer = Guid.NewGuid();
        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-CUSTOMER-PARENT", 100m,
            StockPartyKind.Counterparty, customer, StockPartyKind.Company, CompanyA);
        var orderId = Guid.NewGuid();
        var issue = IssueRequest(orderId, parent.Label.Id, "2", "30");
        var issueMetadata = ManufacturingMetadata(run, "customer-issue");

        var issued = await CommitIssueAsync(run, issue, issueMetadata);
        Assert.Equal(new InventoryManufacturingPositionSnapshotV1(
            StockPartyKind.Counterparty, customer, StockPartyKind.Company, CompanyA, parent.LocationId),
            issued.ParentPosition);

        var replay = await CommitIssueAsync(run, issue,
            issueMetadata with { CorrelationId = "manufacturing-customer-issue-retry" });
        Assert.True(replay.IsReplay);
        Assert.Equal(issued.ParentPosition, replay.ParentPosition);

        var returnedRequest = ReturnRequest(orderId, parent.Label.Id, issued.ParentLabelVersion, "10");
        var returned = await CommitReturnAsync(run, returnedRequest,
            ManufacturingMetadata(run, "customer-return"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.MovementLines WHERE MovementId = @movement "
            + "AND OwnerKind = @ownerKind AND OwnerId = @ownerId "
            + "AND CustodianKind = @custodianKind AND CustodianId = @custodianId AND LocationId = @location",
            ("@movement", returnedRequest.MovementId), ("@ownerKind", (int)StockPartyKind.Counterparty),
            ("@ownerId", customer), ("@custodianKind", (int)StockPartyKind.Company),
            ("@custodianId", CompanyA), ("@location", parent.LocationId)));

        var output = OutputRequest(orderId, parent.Label.Id, returned.ParentLabelVersion,
            parent.LocationId, "20") with
        {
            OwnerKind = StockPartyKind.Counterparty,
            OwnerId = customer,
            CustodianKind = StockPartyKind.Company,
            CustodianId = CompanyA,
        };
        await AssertOutputRefusalAsync(run, output with
        {
            OwnerKind = StockPartyKind.Company,
            OwnerId = CompanyA,
        }, ManufacturingMetadata(run, "customer-output-wrong-owner"), 409,
            InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict);
        await AssertOutputRefusalAsync(run, output with
        {
            CustodianKind = StockPartyKind.Counterparty,
            CustodianId = customer,
        }, ManufacturingMetadata(run, "customer-output-wrong-custodian"), 409,
            InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict);

        var outputResult = await CommitOutputAsync(run, output,
            ManufacturingMetadata(run, "customer-output"));
        Assert.False(outputResult.IsReplay);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.MovementLines WHERE MovementId = @movement "
            + "AND OwnerKind = @ownerKind AND OwnerId = @ownerId "
            + "AND CustodianKind = @custodianKind AND CustodianId = @custodianId",
            ("@movement", output.MovementId), ("@ownerKind", (int)StockPartyKind.Counterparty),
            ("@ownerId", customer), ("@custodianKind", (int)StockPartyKind.Company),
            ("@custodianId", CompanyA)));
    }

    [SqlServerFact]
    public async Task Manufacturing_participant_host_rollback_removes_input_and_output_effects()
    {
        await using var run = await Run.StartAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-PARENT-ROLLBACK", 100m);
        var orderId = Guid.NewGuid();
        var issue = IssueRequest(orderId, parent.Label.Id, "2", "25");
        var issueMetadata = ManufacturingMetadata(run, "rollback-issue");

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using (var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
                             connection, transaction, CompanyA, ManufacturingSettings(run)))
            {
                await participant.StageInputIssueAsync(issue, issueMetadata);
            }

            Assert.NotNull(transaction.Connection);
            await transaction.RollbackAsync();
        }

        Assert.Equal(100m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @label AND Block = 1",
            ("@label", parent.Label.Id)));
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @label", ("@label", parent.Label.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", issue.MovementId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", issue.IssueId)));

        var committedIssue = IssueRequest(orderId, parent.Label.Id, "2", "10");
        var committedIssueResult = await CommitIssueAsync(run, committedIssue,
            ManufacturingMetadata(run, "rollback-output-prerequisite"));
        var output = OutputRequest(orderId, parent.Label.Id, committedIssueResult.ParentLabelVersion,
            parent.LocationId, "40");
        var outputMetadata = ManufacturingMetadata(run, "rollback-output");
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using (var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
                             connection, transaction, CompanyA, ManufacturingSettings(run)))
            {
                await participant.StageOutputAsync(output, outputMetadata);
            }

            Assert.NotNull(transaction.Connection);
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Lots WHERE Id = @id", ("@id", output.LotId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @id", ("@id", output.LabelId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", output.MovementId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", output.OutputId)));
    }

    [SqlServerFact]
    public async Task Manufacturing_participant_refuses_stale_foreign_over_return_payload_and_profile_failures_without_partial_effect()
    {
        await using var run = await Run.StartAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-PARENT-REFUSAL", 20m);
        var orderId = Guid.NewGuid();
        var orphanOutput = OutputRequest(orderId, parent.Label.Id, "2", parent.LocationId, "1");
        await AssertOutputRefusalAsync(run, orphanOutput, ManufacturingMetadata(run, "output-without-issue"), 409,
            InventoryManufacturingRealizationCodesV1.ParentInputNotIssued);
        var issue = IssueRequest(orderId, parent.Label.Id, "2", "10");
        var metadata = ManufacturingMetadata(run, "refusal-issue");
        var issued = await CommitIssueAsync(run, issue, metadata);
        Assert.Equal("3", issued.ParentLabelVersion);

        await AssertIssueRefusalAsync(run, issue with
        {
            IssueId = Guid.NewGuid(), MovementId = Guid.NewGuid(), ExpectedParentLabelVersion = "2",
        }, ManufacturingMetadata(run, "stale"), 409,
            InventoryManufacturingRealizationCodesV1.LabelVersionConflict, ManufacturingSettings(run));
        await AssertIssueRefusalAsync(run, issue with
        {
            CompanyId = CompanyB, IssueId = Guid.NewGuid(), MovementId = Guid.NewGuid(),
            ExpectedParentLabelVersion = "3",
        }, ManufacturingMetadata(run, "foreign"), 404,
            InventoryManufacturingRealizationCodesV1.InvalidReference, ManufacturingSettings(run));
        await AssertIssueRefusalAsync(run, issue with
        {
            IssueId = Guid.NewGuid(), MovementId = Guid.NewGuid(), ExpectedParentLabelVersion = "3", QuantityKg = "1",
        }, ManufacturingMetadata(run, "profile"), 503,
            InventoryManufacturingRealizationCodesV1.QuantityProfileUnavailable, new UnavailableSettings());

        var returnRequest = ReturnRequest(orderId, parent.Label.Id, "3", "10.001");
        await AssertReturnRefusalAsync(run, returnRequest, ManufacturingMetadata(run, "over-return"), 409,
            InventoryManufacturingRealizationCodesV1.ReturnExceedsIssued);

        await AssertIssueRefusalAsync(run, issue with { QuantityKg = "9" },
            metadata with { CorrelationId = "manufacturing-payload-conflict" }, 409,
            InventoryManufacturingRealizationCodesV1.IdempotencyConflict, ManufacturingSettings(run));

        Assert.Equal(10m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @label AND Block = 1",
            ("@label", parent.Label.Id)));
        Assert.Equal(3L, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @label", ("@label", parent.Label.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", issue.IssueId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 5 AND SourceId = @order", ("@order", orderId)));
    }

    [SqlServerFact]
    public async Task Manufacturing_return_migration_down_refuses_while_a_durable_return_exists()
    {
        await using var run = await Run.StartAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-PARENT-DOWN", 20m);
        var orderId = Guid.NewGuid();
        var issue = IssueRequest(orderId, parent.Label.Id, "2", "10");
        var issued = await CommitIssueAsync(run, issue, ManufacturingMetadata(run, "down-issue"));
        await CommitReturnAsync(run, ReturnRequest(orderId, parent.Label.Id, issued.ParentLabelVersion, "4"),
            ManufacturingMetadata(run, "down-return"));

        await using var context = run.Context(CompanyA);
        var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>()
            .MigrateAsync("20260924013338_AddInventoryPhysicalAllocationLifecycle"));
        Assert.Equal(51053, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Kind = 5 AND SourceId = @order", ("@order", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.__EFMigrationsHistory "
            + "WHERE MigrationId = N'20260924193000_AddInventoryManufacturingReturn'"));
    }

    private static async Task<ManufacturingParentSeed> SeedManufacturingParentAsync(
        Run run, string code, decimal quantity,
        StockPartyKind ownerKind = StockPartyKind.Company, Guid? ownerId = null,
        StockPartyKind custodianKind = StockPartyKind.Company, Guid? custodianId = null)
    {
        var location = await run.LocationOfAsync(CompanyA);
        var label = NewLabel(CompanyA, code);
        await using var store = run.Store(CompanyA);
        await store.Labels.SaveLabelAsync(label,
            Metadata(run) with { CorrelationId = $"manufacturing-parent-label-{Guid.NewGuid():N}" });
        var receipt = new StockMovement(Guid.NewGuid(), CompanyA, StockMovementKind.Receipt, ManufacturingAt,
            new StockSourceReference("synthetic.manufacturing-parent", Guid.NewGuid()),
            [new StockMovementLine(new StockPosition(CompanyA,
                    new StockOwner(ownerKind, ownerId ?? CompanyA),
                    new StockCustodian(custodianKind, custodianId ?? CompanyA),
                    location, label, BlockState.Free),
                StockDirection.In, Kilograms.Positive(quantity))]);
        await store.Movements.RecordMovementAsync(receipt,
            Metadata(run) with { CorrelationId = $"manufacturing-parent-stock-{Guid.NewGuid():N}" });
        return new(label, location, ownerKind, ownerId ?? CompanyA, custodianKind, custodianId ?? CompanyA);
    }

    private static InventoryManufacturingInputIssueRequestV1 IssueRequest(
        Guid orderId, Guid parentLabelId, string labelVersion, string quantity) => new(
        CompanyA, orderId, Guid.NewGuid(), "2", parentLabelId, labelVersion,
        Guid.NewGuid(), quantity, ManufacturingAt.AddMinutes(1));

    private static InventoryManufacturingInputReturnRequestV1 ReturnRequest(
        Guid orderId, Guid parentLabelId, string labelVersion, string quantity) => new(
        CompanyA, orderId, Guid.NewGuid(), "3", parentLabelId, labelVersion,
        Guid.NewGuid(), quantity, ManufacturingAt.AddMinutes(2));

    private static InventoryManufacturingOutputRequestV1 OutputRequest(
        Guid orderId, Guid parentLabelId, string labelVersion, Guid locationId, string quantity) => new(
        CompanyA, orderId, Guid.NewGuid(), "4", parentLabelId, labelVersion, "COIL",
        Product, "product-v1", Guid.NewGuid(), Guid.NewGuid(), $"SYN-MFG-OUT-{Guid.NewGuid():N}",
        "SYN-HEAT", "SYN-COIL", [], StockPartyKind.Company, CompanyA,
        StockPartyKind.Company, CompanyA, locationId, BlockState.Free, Guid.NewGuid(), quantity,
        ManufacturingAt.AddMinutes(3));

    private static InventoryCommandMetadata ManufacturingMetadata(Run run, string operation) => new(
        run.Actor.ToString("D"), "manufacturing.realization.write", ["SYNTHETIC-MANUFACTURING"],
        $"manufacturing-{operation}-{Guid.NewGuid():N}", null,
        $"manufacturing-{operation}-{Guid.NewGuid():N}", "Integration");

    private static ISettingsPrerequisiteProvider ManufacturingSettings(Run run)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return new ApprovedSettingsPrerequisiteProvider(company, new FixedCurrentUser(run.Actor, CompanyA));
    }

    private static async Task<InventoryManufacturingInputIssueResultV1> CommitIssueAsync(
        Run run, InventoryManufacturingInputIssueRequestV1 request, InventoryCommandMetadata metadata)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var result = await participant.StageInputIssueAsync(request, metadata);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task<InventoryManufacturingInputReturnResultV1> CommitReturnAsync(
        Run run, InventoryManufacturingInputReturnRequestV1 request, InventoryCommandMetadata metadata)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var result = await participant.StageInputReturnAsync(request, metadata);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task<InventoryManufacturingOutputResultV1> CommitOutputAsync(
        Run run, InventoryManufacturingOutputRequestV1 request, InventoryCommandMetadata metadata)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var result = await participant.StageOutputAsync(request, metadata);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task AssertIssueRefusalAsync(Run run,
        InventoryManufacturingInputIssueRequestV1 request, InventoryCommandMetadata metadata,
        int status, string code, ISettingsPrerequisiteProvider settings)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, settings);
        var error = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            participant.StageInputIssueAsync(request, metadata));
        Assert.Equal((status, code), (error.Status, error.Code));
        await transaction.RollbackAsync();
    }

    private static async Task AssertReturnRefusalAsync(Run run,
        InventoryManufacturingInputReturnRequestV1 request, InventoryCommandMetadata metadata,
        int status, string code)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var error = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            participant.StageInputReturnAsync(request, metadata));
        Assert.Equal((status, code), (error.Status, error.Code));
        await transaction.RollbackAsync();
    }

    private static async Task AssertOutputRefusalAsync(Run run,
        InventoryManufacturingOutputRequestV1 request, InventoryCommandMetadata metadata,
        int status, string code)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var error = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            participant.StageOutputAsync(request, metadata));
        Assert.Equal((status, code), (error.Status, error.Code));
        await transaction.RollbackAsync();
    }

    private sealed record ManufacturingParentSeed(
        MaterialLabel Label,
        Guid LocationId,
        StockPartyKind OwnerKind,
        Guid OwnerId,
        StockPartyKind CustodianKind,
        Guid CustodianId);

    private sealed class UnavailableSettings : ISettingsPrerequisiteProvider
    {
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) =>
            throw new SettingsPrerequisiteException("settings_provider_unavailable");

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation,
            DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation,
            DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
