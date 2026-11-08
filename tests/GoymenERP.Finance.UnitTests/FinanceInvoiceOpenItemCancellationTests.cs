using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// F2b / API-11 (SAT-D96): the pure rules of the invoice cancellation — the producer fixes the source kind, the
/// identities and version are required, the reason is short canonical text — the stored model (a receipt per invoice,
/// one reversal per open item, status 3) and the ledger side of a reversal: the reverse of the item it reverses.
/// SENTETIK values only.
/// </summary>
public sealed class FinanceInvoiceOpenItemCancellationTests
{
    private static readonly Guid Currency = Guid.Parse("99999999-9999-4999-8999-999999999999");

    [Theory]
    [InlineData(FinanceInvoiceProducerV1.Sales, FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment)]
    [InlineData(FinanceInvoiceProducerV1.Purchasing, FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment)]
    public void The_producer_fixes_the_source_kind(FinanceInvoiceProducerV1 producer, string kind)
    {
        Assert.Empty(FinanceInvoiceOpenItemCancellationService.Validate(Cancellation(producer), out var actual));
        Assert.Equal(kind, actual);
    }

    [Fact]
    public void An_unknown_producer_and_empty_identities_are_refused()
    {
        var issues = FinanceInvoiceOpenItemCancellationService.Validate(
            Cancellation((FinanceInvoiceProducerV1)9) with { CancellationId = Guid.Empty }, out _);

        Assert.Contains(issues, issue => issue.Code == "finance_invoice_producer_invalid");
        Assert.Contains(issues, issue => issue.Field == "cancellation" && issue.Code == "identity_required");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" v1")]
    [InlineData("v 1")]
    public void The_invoice_version_is_a_token(string version)
    {
        var issues = FinanceInvoiceOpenItemCancellationService.Validate(
            Cancellation(FinanceInvoiceProducerV1.Sales) with { InvoiceVersion = version }, out _);

        Assert.Contains(issues, issue => issue.Code == "finance_invoice_version_invalid");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" RED")]
    [InlineData("RED\nnot")]
    public void The_reason_is_required_canonical_text(string reason)
    {
        var issues = FinanceInvoiceOpenItemCancellationService.Validate(
            Cancellation(FinanceInvoiceProducerV1.Sales) with { Reason = reason }, out _);

        Assert.Contains(issues, issue => issue.Code == "finance_invoice_cancellation_reason_invalid");
    }

    [Fact]
    public void A_reason_longer_than_the_stored_column_is_refused()
    {
        var reason = new string('R', FinanceInvoiceCancellationRules.ReasonMaximumLength + 1);

        Assert.Contains(FinanceInvoiceOpenItemCancellationService.Validate(
                Cancellation(FinanceInvoiceProducerV1.Sales) with { Reason = reason }, out _),
            issue => issue.Code == "finance_invoice_cancellation_reason_invalid");
    }

    [Fact]
    public void A_cancellation_is_the_reverse_side_of_the_open_item_it_reverses()
    {
        Assert.Equal(PartyLedgerSide.Credit, PartyLedgerPolicy.SideOf(PartyLedgerEntryKind.OpenItemCancellation));
        Assert.Equal(PartyLedgerSide.Debit, PartyLedgerPolicy.SideOf(PartyLedgerEntryKind.PayableOpenItemCancellation));

        var admitted = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
        var cancelled = admitted.AddDays(2);
        var item = Guid.NewGuid();
        var currency = Assert.Single(PartyLedgerPolicy.Build(
        [
            new PartyLedgerFact(PartyLedgerEntryKind.OpenItem, item, new DateOnly(2026, 11, 3), admitted, Currency,
                "120.00", item, null, null, Guid.NewGuid(), new DateOnly(2026, 12, 3)),
            new PartyLedgerFact(PartyLedgerEntryKind.OpenItemCancellation, item, new DateOnly(2026, 11, 5), cancelled,
                Currency, "120.00", item, item, null, Guid.NewGuid(), null),
        ], AmountDisclosure.Visible));

        Assert.Equal([PartyLedgerSide.Debit, PartyLedgerSide.Credit], currency.Entries.Select(entry => entry.Side));
        Assert.Equal(PartyLedgerBalanceSide.Zero, currency.ClosingBalance.Side);
    }

    [Fact]
    public void The_model_stores_one_receipt_per_invoice_one_reversal_per_item_and_a_cancelled_status()
    {
        using var context = Context();
        var model = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var items = model.FindEntityType(typeof(FinanceOpenItemRow))!;
        Assert.Equal("[Status] IN (1, 2, 3)",
            Assert.Single(items.GetCheckConstraints(), check => check.Name == "CK_OpenItems_Status").Sql);

        var header = model.FindEntityType(typeof(FinanceInvoiceCancellationRow))!;
        Assert.Contains(header.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(FinanceInvoiceCancellationRow.CompanyId), nameof(FinanceInvoiceCancellationRow.SourceKind),
                    nameof(FinanceInvoiceCancellationRow.InvoiceId)]));
        Assert.Contains(header.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(FinanceInvoiceCancellationRow.CompanyId), nameof(FinanceInvoiceCancellationRow.IdempotencyKeyHash)]));
        Assert.NotEmpty(header.GetDeclaredQueryFilters());

        var reversal = model.FindEntityType(typeof(FinanceInvoiceCancellationItemRow))!;
        Assert.Equal([nameof(FinanceInvoiceCancellationItemRow.CompanyId), nameof(FinanceInvoiceCancellationItemRow.OpenItemId)],
            reversal.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.NotEmpty(reversal.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_migration_rollback_refuses_first_while_a_cancellation_is_stored()
    {
        var migration = new AddInvoiceCancellations();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51051", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[InvoiceCancellations]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }

    [Fact]
    public void A_cancelled_item_may_change_only_its_status_outstanding_and_update_stamp()
    {
        using var context = Context();
        var row = new FinanceOpenItemRow
        {
            Direction = FinanceOpenItemDirectionCodes.Receivable,
            Id = Guid.NewGuid(),
            CompanyId = TestCompany,
            SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
            OriginalAmountText = "10.00",
            OutstandingAmountText = "10.00",
            Status = FinanceOpenItemStatuses.Open,
            Version = 1,
        };
        context.Attach(row);
        row.Status = FinanceOpenItemStatuses.Cancelled;
        row.OriginalAmountText = "0";

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    private static readonly Guid TestCompany = Guid.Parse("0a000000-0000-4000-8000-0000000000aa");

    private static FinanceInvoiceOpenItemCancellationV1 Cancellation(FinanceInvoiceProducerV1 producer) =>
        new(Guid.NewGuid(), producer, Guid.NewGuid(), "invoice-v1", "SENTETIK e-Arsiv iptal");

    private static FinanceDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(TestCompany);
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new FinanceDbContext(options, company);
    }
}
