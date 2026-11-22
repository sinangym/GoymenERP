using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the manufacturing consumed-cost differences Accounting lists for the Work inbox, on SYNTHETIC
/// rows. Only Prepared (status 2) records of the server-resolved company are listed, oldest preparation first; the preparer
/// is the maker; the rule carries the stored signed TL difference. The source writes nothing.
/// </summary>
public sealed class AccountingManufacturingCostDifferenceWorkInboxSourceV1Tests
{
    [Fact]
    public async Task Only_prepared_differences_of_the_company_are_listed_oldest_first_with_the_stored_tl_difference()
    {
        await using var kit = new Kit();
        var older = Difference(Kit.Start, Kit.Caller, "-125.5000");
        var newer = Difference(Kit.Start.AddHours(1), Kit.Maker, "10");
        var theirs = Difference(Kit.Start.AddDays(-1), Kit.Maker, "7", company: Kit.OtherCompany);
        var awaiting = Difference(Kit.Start.AddDays(-1), Kit.Maker, "3");
        awaiting.Status = 1;
        awaiting.PreparedBy = null;
        awaiting.PreparedAtUtc = null;
        var posted = Difference(Kit.Start.AddDays(-1), Kit.Maker, "4");
        posted.Status = 3;
        posted.ApprovedBy = Kit.Caller;
        posted.JournalId = Guid.NewGuid();
        await kit.SeedAsync(Kit.Company, db => db.ManufacturingCostDifferences.AddRange(newer, older, awaiting, posted));
        await kit.SeedAsync(Kit.OtherCompany, db => db.ManufacturingCostDifferences.Add(theirs));

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Kit.AssertContractShape(result, AccountingManufacturingCostDifferenceWorkInboxSourceV1.KindName);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(Kit.CallerText, first.PreparedBy);
        Assert.Null(first.ExcludedDeciders);
        Assert.Equal(AccountingManufacturingCostDifferenceWorkInboxSourceV1.ScreenPath, first.Link);
        Assert.Equal(Kit.Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal([new WorkApprovalRuleResultV1(AccountingManufacturingCostDifferenceWorkInboxSourceV1.ApprovalRequiredRule,
            null, "-125.5", null, "TRY")], first.Rules);
        Assert.Equal(["manufacturing-order", "period", "debit-account", "credit-account"],
            first.References.Select(reference => reference.Role));
        Assert.Equal(older.ManufacturingOrderId, first.References[0].RecordId);
        Assert.False(kit.Db.ChangeTracker.HasChanges());

        var other = await Source(kit, Kit.OtherCompany).ListPendingAsync(Kit.Context(Kit.OtherCompany), 10, CancellationToken.None);
        Assert.Equal([theirs.Id], other.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task More_differences_than_asked_set_has_more_and_an_unreadable_amount_makes_the_source_unavailable()
    {
        await using var kit = new Kit();
        var rows = Enumerable.Range(0, 3).Select(index => Difference(Kit.Start.AddMinutes(index), Kit.Maker, "1")).ToArray();
        await kit.SeedAsync(Kit.Company, db => db.ManufacturingCostDifferences.AddRange(rows));

        var page = await Source(kit).ListPendingAsync(Kit.Context(), 2, CancellationToken.None);
        Assert.True(page.HasMore);
        Assert.Equal(rows.Take(2).Select(row => row.Id), page.Items!.Select(item => item.ItemId));

        await kit.SeedAsync(Kit.Company, db => db.ManufacturingCostDifferences.Add(
            Difference(Kit.Start.AddMinutes(-1), Kit.Maker, "SENTETIK")));
        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    private static AccountingManufacturingCostDifferenceWorkInboxSourceV1 Source(Kit kit, Guid? company = null)
    {
        var fence = kit.Fence(company);
        return new AccountingManufacturingCostDifferenceWorkInboxSourceV1(kit.Db, fence);
    }

    private static ManufacturingCostDifferenceRow Difference(DateTimeOffset preparedAt, Guid preparedBy, string amount,
        Guid? company = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company ?? Kit.Company,
            ManufacturingOrderId = Guid.NewGuid(),
            GenerationId = Guid.NewGuid(),
            GenerationVersion = "1",
            SourceHash = new string('e', 64),
            SourceJson = "{}",
            SourceOccurredAtUtc = preparedAt.AddDays(-1).ToUniversalTime(),
            SourceCorrelationId = "SENTETIK-correlation",
            SignedTlDifference = amount,
            Status = 2,
            Version = 2,
            PeriodId = Guid.NewGuid(),
            AccountingDate = new DateOnly(2026, 11, 20),
            TransactionDate = new DateOnly(2026, 11, 20),
            CurrencyId = Guid.NewGuid(),
            DebitAccountId = Guid.NewGuid(),
            CreditAccountId = Guid.NewGuid(),
            PreparedBy = preparedBy,
            PreparedAtUtc = preparedAt.ToUniversalTime(),
            Reason = Kit.SyntheticReason,
        };
}

/// <summary>
/// W1-A (AI-D31, AI-D32): the invoice line COGS corrections Accounting lists for the Work inbox, on SYNTHETIC rows. A
/// proposal is listed only while no approved revision names it and the line's latest revision is the one it was prepared
/// on; the preparer is the maker. The source writes nothing.
/// </summary>
public sealed class AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1Tests
{
    [Fact]
    public async Task Only_unapproved_proposals_on_the_current_line_version_are_listed_oldest_first_with_the_preparer_as_maker()
    {
        await using var kit = new Kit();
        var invoice = Guid.NewGuid();
        var line = Guid.NewGuid();
        var older = Proposal(invoice, line, 1, Kit.Start, Kit.Caller);
        var newer = Proposal(Guid.NewGuid(), Guid.NewGuid(), 0, Kit.Start.AddHours(1), Kit.Maker);
        var approvedProposal = Proposal(invoice, line, 0, Kit.Start.AddDays(-2), Kit.Maker);
        // Prepared on line version 0, but version 1 has since been approved: stale, the owner would refuse it.
        var stale = Proposal(invoice, line, 0, Kit.Start.AddDays(-1), Kit.Maker);
        var theirs = Proposal(Guid.NewGuid(), Guid.NewGuid(), 0, Kit.Start.AddDays(-3), Kit.Maker, Kit.OtherCompany);
        await kit.SeedAsync(Kit.Company, db =>
        {
            db.InvoiceLineCogsProposals.AddRange(newer, older, approvedProposal, stale);
            db.InvoiceLineCogsRevisions.Add(new InvoiceLineCogsRevisionRow
            {
                CompanyId = Kit.Company,
                InvoiceId = invoice,
                InvoiceLineId = line,
                Version = 1,
                ProposalId = approvedProposal.Id,
                PreparedBy = Kit.Maker,
                ApprovedBy = Kit.Caller,
                ApprovedAtUtc = Kit.Start.AddDays(-2).ToUniversalTime(),
                ViewJson = "{}",
                RevisionJson = "{}",
            });
        });
        await kit.SeedAsync(Kit.OtherCompany, db => db.InvoiceLineCogsProposals.Add(theirs));

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Kit.AssertContractShape(result, AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1.KindName);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(Kit.CallerText, first.PreparedBy);
        Assert.Null(first.ExcludedDeciders);
        Assert.Equal(AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1.ScreenPath, first.Link);
        Assert.Equal(Kit.Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal([AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1.ApprovalRequiredRule], first.Rules.Select(rule => rule.RuleCode));
        Assert.Equal([("invoice", (Guid?)invoice), ("invoice-line", line)],
            first.References.Select(reference => (reference.Role, reference.RecordId)));
        Assert.False(kit.Db.ChangeTracker.HasChanges());

        var other = await Source(kit, Kit.OtherCompany).ListPendingAsync(Kit.Context(Kit.OtherCompany), 10, CancellationToken.None);
        Assert.Equal([theirs.Id], other.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task More_proposals_than_asked_set_has_more_and_a_broken_proposal_makes_the_source_unavailable()
    {
        await using var kit = new Kit();
        var rows = Enumerable.Range(0, 3)
            .Select(index => Proposal(Guid.NewGuid(), Guid.NewGuid(), 0, Kit.Start.AddMinutes(index), Kit.Maker)).ToArray();
        await kit.SeedAsync(Kit.Company, db => db.InvoiceLineCogsProposals.AddRange(rows));

        var page = await Source(kit).ListPendingAsync(Kit.Context(), 2, CancellationToken.None);
        Assert.True(page.HasMore);
        Assert.Equal(rows.Take(2).Select(row => row.Id), page.Items!.Select(item => item.ItemId));

        await kit.SeedAsync(Kit.Company, db => db.InvoiceLineCogsProposals.Add(
            Proposal(Guid.NewGuid(), Guid.NewGuid(), 0, Kit.Start.AddMinutes(-1), Guid.Empty)));
        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    private static AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1 Source(Kit kit, Guid? company = null)
    {
        var fence = kit.Fence(company);
        return new AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1(kit.Db, fence);
    }

    private static InvoiceLineCogsProposalRow Proposal(Guid invoice, Guid line, long expectedLineVersion,
        DateTimeOffset at, Guid preparedBy, Guid? company = null) =>
        new()
        {
            CompanyId = company ?? Kit.Company,
            Id = Guid.NewGuid(),
            InvoiceId = invoice,
            InvoiceLineId = line,
            ExpectedLineVersion = expectedLineVersion,
            PreparedBy = preparedBy,
            PreparedAtUtc = at.ToUniversalTime(),
            SourceJson = "{}",
            ViewJson = "{}",
        };
}

/// <summary>
/// W1-A (AI-D31, AI-D32): the inventory cost proposals Accounting lists for the Work inbox, one source per approve
/// permission the owner binds (invoiced shipment COGS, purchase cost difference), on SYNTHETIC rows. A proposal is listed
/// only while its LATEST version is Prepared and, for a shipment, its source invoice is not cancelled; the preparer is the
/// maker; the rule carries the prepared signed TL amount. The sources write nothing.
/// </summary>
public sealed class AccountingInventoryCostWorkInboxSourcesV1Tests
{
    [Fact]
    public async Task Only_latest_prepared_shipment_proposals_of_uncancelled_invoices_are_listed_oldest_first()
    {
        await using var kit = new Kit();
        var shipment = Source(1, Guid.NewGuid());
        var cancelledShipment = Source(1, Guid.NewGuid());
        var purchase = Source(2, Guid.NewGuid());
        var (olderFirst, older) = Versions(shipment, Kit.Start, Kit.Caller, "-12.3400", "SENTETIK-FTR-1");
        var (_, newer) = Versions(shipment, Kit.Start.AddHours(1), Kit.Maker, "5", null);
        var (_, cancelled) = Versions(cancelledShipment, Kit.Start.AddMinutes(-30), Kit.Maker, "6", null);
        var (_, purchaseRow) = Versions(purchase, Kit.Start.AddMinutes(-20), Kit.Maker, "7", null);
        // A proposal whose latest version is posted is no longer pending, even though an older version was Prepared.
        var (_, superseded) = Versions(shipment, Kit.Start.AddMinutes(-10), Kit.Maker, "8", null);
        var posted = Version(superseded.Id, shipment, superseded.Version + 1, 3, Kit.Maker, superseded.PeriodId, "8", null,
            Kit.Start.AddMinutes(-5));
        posted.ApprovedBy = Kit.Caller;
        posted.JournalId = Guid.NewGuid();
        await kit.SeedAsync(Kit.Company, db =>
        {
            db.InventoryCostSources.AddRange(shipment, cancelledShipment, purchase);
            db.InventoryCostProposals.AddRange(olderFirst, older, newer, cancelled, purchaseRow, superseded, posted);
            db.SourceInvoiceCancellations.Add(new AccountingSourceInvoiceCancellationRow
            {
                CompanyId = Kit.Company,
                CancellationId = Guid.NewGuid(),
                DocumentKind = "sales-invoice",
                SourceDocumentId = cancelledShipment.AdmissionId,
                InvoiceId = cancelledShipment.InvoiceId,
                FinalizedVersion = 1,
                CancelledBy = Kit.Maker,
                CancelledAtUtc = Kit.Start.ToUniversalTime(),
                Reason = Kit.SyntheticReason,
                RequestHash = new string('f', 64),
                CorrelationId = "SENTETIK-correlation",
                RecordedAtUtc = Kit.Start.ToUniversalTime(),
            });
        });

        var shipments = await ShipmentSource(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);
        var purchases = await PurchaseSource(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Kit.AssertContractShape(shipments, AccountingInvoicedShipmentCostWorkInboxSourceV1.KindName);
        Kit.AssertContractShape(purchases, AccountingPurchaseCostDifferenceWorkInboxSourceV1.KindName);
        Assert.Equal([older.Id, newer.Id], shipments.Items!.Select(item => item.ItemId));
        Assert.Equal([purchaseRow.Id], purchases.Items!.Select(item => item.ItemId));
        var first = shipments.Items![0];
        Assert.Equal(Kit.CallerText, first.PreparedBy);
        Assert.Null(first.ExcludedDeciders);
        Assert.Equal("SENTETIK-FTR-1", first.DocumentLabel);
        Assert.Equal(Kit.Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal(AccountingInvoicedShipmentCostWorkInboxSourceV1.ScreenPath, first.Link);
        Assert.Equal(AccountingPurchaseCostDifferenceWorkInboxSourceV1.ScreenPath, purchases.Items![0].Link);
        Assert.Equal([new WorkApprovalRuleResultV1(AccountingInvoicedShipmentCostWorkInboxSourceV1.ApprovalRequiredRule,
            null, "-12.34", null, "TRY")], first.Rules);
        Assert.Equal([AccountingPurchaseCostDifferenceWorkInboxSourceV1.ApprovalRequiredRule],
            purchases.Items![0].Rules.Select(rule => rule.RuleCode));
        Assert.Equal([("invoice", (Guid?)shipment.InvoiceId), ("period", older.PeriodId)],
            first.References.Select(reference => (reference.Role, reference.RecordId)));
        // Without a stored official invoice number the screens' short references are shown.
        Assert.Equal(newer.Id.ToString("D")[..8], shipments.Items![1].DocumentLabel);
        Assert.Equal(shipment.InvoiceId.ToString("D")[..8], shipments.Items![1].References[0].Label);
        Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Another_companys_proposals_never_appear_and_more_than_asked_set_has_more()
    {
        await using var kit = new Kit();
        var shipment = Source(1, Guid.NewGuid());
        var rows = Enumerable.Range(0, 3)
            .Select(index => Versions(shipment, Kit.Start.AddMinutes(index), Kit.Maker, "1", null).Prepared).ToArray();
        var theirSource = Source(1, Guid.NewGuid(), Kit.OtherCompany);
        var (_, theirs) = Versions(theirSource, Kit.Start.AddDays(-1), Kit.Maker, "1", null);
        await kit.SeedAsync(Kit.Company, db =>
        {
            db.InventoryCostSources.Add(shipment);
            db.InventoryCostProposals.AddRange(rows);
        });
        await kit.SeedAsync(Kit.OtherCompany, db =>
        {
            db.InventoryCostSources.Add(theirSource);
            db.InventoryCostProposals.Add(theirs);
        });

        var page = await ShipmentSource(kit).ListPendingAsync(Kit.Context(), 2, CancellationToken.None);
        var other = await ShipmentSource(kit, Kit.OtherCompany).ListPendingAsync(Kit.Context(Kit.OtherCompany), 10, CancellationToken.None);

        Assert.True(page.HasMore);
        Assert.Equal(rows.Take(2).Select(row => row.Id), page.Items!.Select(item => item.ItemId));
        Assert.Equal([theirs.Id], other.Items!.Select(item => item.ItemId));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"proposalId\":\"00000000-0000-0000-0000-000000000001\"}")]
    public async Task A_prepared_version_whose_stored_view_disagrees_with_its_row_makes_the_source_unavailable(string viewJson)
    {
        await using var kit = new Kit();
        var shipment = Source(1, Guid.NewGuid());
        var (_, prepared) = Versions(shipment, Kit.Start, Kit.Maker, "1", null);
        prepared.ViewJson = viewJson;
        await kit.SeedAsync(Kit.Company, db =>
        {
            db.InventoryCostSources.Add(shipment);
            db.InventoryCostProposals.Add(prepared);
        });

        var result = await ShipmentSource(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Items);
    }

    private static AccountingInvoicedShipmentCostWorkInboxSourceV1 ShipmentSource(Kit kit, Guid? company = null)
    {
        var fence = kit.Fence(company);
        return new AccountingInvoicedShipmentCostWorkInboxSourceV1(kit.Db, fence);
    }

    private static AccountingPurchaseCostDifferenceWorkInboxSourceV1 PurchaseSource(Kit kit)
    {
        var fence = kit.Fence();
        return new AccountingPurchaseCostDifferenceWorkInboxSourceV1(kit.Db, fence);
    }

    private static AccountingInventoryCostSourceRow Source(int kind, Guid invoice, Guid? company = null) =>
        new()
        {
            CompanyId = company ?? Kit.Company,
            EventId = Guid.NewGuid(),
            Kind = kind,
            InvoiceId = invoice,
            InvoiceVersion = 1,
            AdmissionId = Guid.NewGuid(),
            PayloadHash = new string('a', 64),
            SourceJson = "{}",
            OccurredAtUtc = Kit.Start.AddDays(-5).ToUniversalTime(),
            ReceivedAtUtc = Kit.Start.AddDays(-5).ToUniversalTime(),
        };

    /// <summary>An awaiting version 1 and the Prepared version 2 the owner appends on preparation.</summary>
    private static (AccountingInventoryCostProposalRow Awaiting, AccountingInventoryCostProposalRow Prepared) Versions(
        AccountingInventoryCostSourceRow source, DateTimeOffset preparedAt, Guid preparedBy, string amount, string? invoiceNumber)
    {
        var id = Guid.NewGuid();
        var awaiting = Version(id, source, 1, 1, null, null, null, invoiceNumber, preparedAt.AddDays(-1));
        var prepared = Version(id, source, 2, 2, preparedBy, Guid.NewGuid(), amount, invoiceNumber, preparedAt);
        return (awaiting, prepared);
    }

    private static AccountingInventoryCostProposalRow Version(Guid id, AccountingInventoryCostSourceRow source, long version,
        int status, Guid? preparedBy, Guid? period, string? amount, string? invoiceNumber, DateTimeOffset at) =>
        new()
        {
            CompanyId = source.CompanyId,
            Id = id,
            Version = version,
            EventId = source.EventId,
            ComponentKey = "SENTETIK-component",
            Status = status,
            PreparedBy = preparedBy,
            PeriodId = period,
            ViewJson = JsonSerializer.Serialize(new
            {
                proposalId = id,
                invoiceId = source.InvoiceId,
                officialInvoiceNumber = invoiceNumber,
                status = status switch { 1 => "AwaitingPreparation", 2 => "Prepared", _ => "Posted" },
                preparation = preparedBy is null ? null : new { preparedBy, periodId = period, signedAmount = amount },
            }),
            RecordedAtUtc = at.ToUniversalTime(),
        };
}

/// <summary>
/// W1-A (AI-D31, AI-D32): the invoiced-shipment cost component cancellations Accounting lists for the Work inbox, on
/// SYNTHETIC rows. A cancellation is listed only while its proposal's LATEST cancellation version is Prepared; the preparer
/// is the maker; the item is the inventory cost proposal the approve command addresses. The source writes nothing.
/// </summary>
public sealed class AccountingInventoryCostCancellationWorkInboxSourceV1Tests
{
    [Fact]
    public async Task Only_latest_prepared_cancellations_of_the_company_are_listed_oldest_first_with_the_preparer_as_maker()
    {
        await using var kit = new Kit();
        var older = Cancellation(Guid.NewGuid(), 1, 1, Kit.Start, Kit.Caller);
        var newer = Cancellation(Guid.NewGuid(), 1, 1, Kit.Start.AddHours(1), Kit.Maker);
        var approvedProposal = Guid.NewGuid();
        var preparedThenApproved = Cancellation(approvedProposal, 1, 1, Kit.Start.AddDays(-2), Kit.Maker);
        var approved = Cancellation(approvedProposal, 2, 2, Kit.Start.AddDays(-1), Kit.Maker);
        approved.ApprovedBy = Kit.Caller;
        var theirs = Cancellation(Guid.NewGuid(), 1, 1, Kit.Start.AddDays(-3), Kit.Maker, Kit.OtherCompany);
        await kit.SeedAsync(Kit.Company, db => db.InventoryCostCancellationVersions.AddRange(newer, older, preparedThenApproved, approved));
        await kit.SeedAsync(Kit.OtherCompany, db => db.InventoryCostCancellationVersions.Add(theirs));

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Kit.AssertContractShape(result, AccountingInventoryCostCancellationWorkInboxSourceV1.KindName);
        Assert.Equal([older.InventoryCostProposalId, newer.InventoryCostProposalId], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(Kit.CallerText, first.PreparedBy);
        Assert.Null(first.ExcludedDeciders);
        Assert.Equal(AccountingInventoryCostCancellationWorkInboxSourceV1.ScreenPath, first.Link);
        Assert.Equal(Kit.Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal([AccountingInventoryCostCancellationWorkInboxSourceV1.ApprovalRequiredRule], first.Rules.Select(rule => rule.RuleCode));
        Assert.Equal(["inventory-cost-proposal", "invoice-cancellation", "period"], first.References.Select(reference => reference.Role));
        Assert.False(kit.Db.ChangeTracker.HasChanges());

        var other = await Source(kit, Kit.OtherCompany).ListPendingAsync(Kit.Context(Kit.OtherCompany), 10, CancellationToken.None);
        Assert.Equal([theirs.InventoryCostProposalId], other.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task More_cancellations_than_asked_set_has_more_and_a_broken_one_makes_the_source_unavailable()
    {
        await using var kit = new Kit();
        var rows = Enumerable.Range(0, 3)
            .Select(index => Cancellation(Guid.NewGuid(), 1, 1, Kit.Start.AddMinutes(index), Kit.Maker)).ToArray();
        await kit.SeedAsync(Kit.Company, db => db.InventoryCostCancellationVersions.AddRange(rows));

        var page = await Source(kit).ListPendingAsync(Kit.Context(), 2, CancellationToken.None);
        Assert.True(page.HasMore);
        Assert.Equal(rows.Take(2).Select(row => row.InventoryCostProposalId), page.Items!.Select(item => item.ItemId));

        await kit.SeedAsync(Kit.Company, db => db.InventoryCostCancellationVersions.Add(
            Cancellation(Guid.NewGuid(), 1, 1, Kit.Start.AddMinutes(-1), Guid.Empty)));
        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    private static AccountingInventoryCostCancellationWorkInboxSourceV1 Source(Kit kit, Guid? company = null)
    {
        var fence = kit.Fence(company);
        return new AccountingInventoryCostCancellationWorkInboxSourceV1(kit.Db, fence);
    }

    private static InventoryCostCancellationVersionRow Cancellation(Guid proposal, long version, int status,
        DateTimeOffset at, Guid preparedBy, Guid? company = null) =>
        new()
        {
            CompanyId = company ?? Kit.Company,
            InventoryCostProposalId = proposal,
            Version = version,
            InitialProposalVersion = 3,
            Id = Guid.NewGuid(),
            CancellationId = Guid.NewGuid(),
            Status = status,
            PeriodId = Guid.NewGuid(),
            PreparedBy = preparedBy,
            ViewJson = "{}",
            Sha256 = new string('a', 64),
            RecordedAtUtc = at.ToUniversalTime(),
        };
}
