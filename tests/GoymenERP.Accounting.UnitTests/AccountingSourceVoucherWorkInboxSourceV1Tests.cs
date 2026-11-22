using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the source invoice voucher drafts Accounting lists for the Work inbox, on SYNTHETIC rows. Only
/// draft vouchers (status 1) of uncancelled invoices of the server-resolved company are listed, oldest first; the invoice's
/// finalizer is the maker the owner refuses; the rule carries the stored functional payable total, and a missing open
/// period is shown as a second rule. The source writes nothing.
/// </summary>
public sealed class AccountingSourceVoucherWorkInboxSourceV1Tests
{
    [Fact]
    public async Task Only_draft_vouchers_of_uncancelled_invoices_are_listed_oldest_first_with_the_invoice_finalizer_as_maker()
    {
        await using var kit = new Kit();
        var older = Voucher(Kit.Start, Kit.Caller, "SENTETIK-FTR-9", 1180.50m);
        var newer = Voucher(Kit.Start.AddHours(1), Kit.Maker, null, 10m);
        var posted = Voucher(Kit.Start.AddDays(-1), Kit.Maker, null, 1m);
        posted.Status = 2;
        posted.ApprovedBy = Kit.Caller;
        posted.JournalId = Guid.NewGuid();
        var cancelled = Voucher(Kit.Start.AddDays(-1), Kit.Maker, null, 2m);
        var theirs = Voucher(Kit.Start.AddDays(-2), Kit.Maker, null, 3m, Kit.OtherCompany);
        await kit.SeedAsync(Kit.Company, db =>
        {
            db.SourceDocumentVoucherDrafts.AddRange(newer, older, posted, cancelled);
            db.Periods.Add(AccountingPeriodOpeningWorkInboxSourceV1Tests.Period(new DateOnly(2026, 11, 1), Kit.Start.AddDays(-30),
                Kit.Maker, GoymenERP.Modules.Accounting.Domain.Periods.AccountingPeriodStatus.Open));
            db.SourceInvoiceCancellations.Add(new AccountingSourceInvoiceCancellationRow
            {
                CompanyId = Kit.Company,
                CancellationId = Guid.NewGuid(),
                DocumentKind = "sales-invoice",
                SourceDocumentId = cancelled.SourceDocumentId,
                InvoiceId = cancelled.InvoiceId,
                FinalizedVersion = 1,
                CancelledBy = Kit.Maker,
                CancelledAtUtc = Kit.Start.ToUniversalTime(),
                Reason = Kit.SyntheticReason,
                RequestHash = new string('f', 64),
                CorrelationId = "SENTETIK-correlation",
                RecordedAtUtc = Kit.Start.ToUniversalTime(),
            });
        });
        await kit.SeedAsync(Kit.OtherCompany, db => db.SourceDocumentVoucherDrafts.Add(theirs));

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Kit.AssertContractShape(result, AccountingSourceVoucherWorkInboxSourceV1.KindName);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(Kit.CallerText, first.PreparedBy);
        Assert.Null(first.ExcludedDeciders);
        Assert.Equal("SENTETIK-FTR-9", first.DocumentLabel);
        Assert.Equal(Kit.Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal(AccountingSourceVoucherWorkInboxSourceV1.ScreenPath, first.Link);
        Assert.Equal([new WorkApprovalRuleResultV1(AccountingSourceVoucherWorkInboxSourceV1.ApprovalRequiredRule,
            null, "1180.5", null, null)], first.Rules);
        Assert.Equal(["invoice", "source-document", "party", "currency"], first.References.Select(reference => reference.Role));
        Assert.Equal(older.InvoiceId, first.References[0].RecordId);
        Assert.Equal(newer.InvoiceId.ToString("D")[..8], result.Items![1].DocumentLabel);
        Assert.False(kit.Db.ChangeTracker.HasChanges());

        var other = await Source(kit, Kit.OtherCompany).ListPendingAsync(Kit.Context(Kit.OtherCompany), 10, CancellationToken.None);
        Assert.Equal([theirs.Id], other.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task Without_an_open_period_covering_the_document_date_the_owners_waiting_reason_is_a_second_rule()
    {
        await using var kit = new Kit();
        var voucher = Voucher(Kit.Start, Kit.Maker, null, 5m);
        await kit.SeedAsync(Kit.Company, db => db.SourceDocumentVoucherDrafts.Add(voucher));

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        var item = Assert.Single(result.Items!);
        Assert.Equal(
            [AccountingSourceVoucherWorkInboxSourceV1.ApprovalRequiredRule, AccountingSourceVoucherWorkInboxSourceV1.PeriodWaitingRule],
            item.Rules.Select(rule => rule.RuleCode));
    }

    [Fact]
    public async Task More_vouchers_than_asked_set_has_more_and_a_broken_voucher_makes_the_source_unavailable()
    {
        await using var kit = new Kit();
        var rows = Enumerable.Range(0, 3).Select(index => Voucher(Kit.Start.AddMinutes(index), Kit.Maker, null, 1m)).ToArray();
        await kit.SeedAsync(Kit.Company, db => db.SourceDocumentVoucherDrafts.AddRange(rows));

        var page = await Source(kit).ListPendingAsync(Kit.Context(), 2, CancellationToken.None);
        Assert.True(page.HasMore);
        Assert.Equal(rows.Take(2).Select(row => row.Id), page.Items!.Select(item => item.ItemId));

        await kit.SeedAsync(Kit.Company, db => db.SourceDocumentVoucherDrafts.Add(
            Voucher(Kit.Start.AddMinutes(-1), Guid.Empty, null, 1m)));
        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    private static AccountingSourceVoucherWorkInboxSourceV1 Source(Kit kit, Guid? company = null)
    {
        var fence = kit.Fence(company);
        return new AccountingSourceVoucherWorkInboxSourceV1(kit.Db, fence);
    }

    private static AccountingSourceDocumentVoucherDraftRow Voucher(DateTimeOffset createdAt, Guid finalizedBy,
        string? invoiceNumber, decimal payable, Guid? company = null) =>
        new()
        {
            CompanyId = company ?? Kit.Company,
            Id = Guid.NewGuid(),
            DocumentKind = "sales-invoice",
            SourceDocumentId = Guid.NewGuid(),
            InvoiceId = Guid.NewGuid(),
            FinalizedVersion = 1,
            SourceActorId = finalizedBy,
            PartyId = Guid.NewGuid(),
            CurrencyId = Guid.NewGuid(),
            DocumentDate = new DateOnly(2026, 11, 20),
            MappingVersion = 1,
            Status = 1,
            OfficialInvoiceNo = invoiceNumber,
            DocumentNet = payable,
            DocumentPayable = payable,
            FunctionalNet = payable,
            FunctionalPayable = payable,
            Version = 1,
            CreatedAtUtc = createdAt.ToUniversalTime(),
            UpdatedAtUtc = createdAt.ToUniversalTime(),
        };
}
