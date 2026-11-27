using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingJournalReversalStoreFollowUpV1Tests
{
    [Fact]
    public async Task Cost_difference_read_hooks_are_noops_for_the_existing_raw_receive_protocol_without_an_owned_marker()
    {
        // Existing InMemory material golden fixture only, not SQL/transaction/Audit acceptance.
        var book = await Book.CreateAsync();
        await using var scope = book.Open(PeriodPreparer);
        var request = book.World.Request("SENTETIK-cost-hooks-no-marker", amount: 100m);
        var posted = await scope.Store.ReceiveAsync(request, book.September, Metadata());
        Assert.Equal(PostingOutcomeKind.Posted, posted.Kind); Assert.NotNull(posted.Journal);
        var replay = await scope.Store.ReceiveAsync(request, book.September, Metadata());
        Assert.Equal(PostingOutcomeKind.DuplicateMessage, replay.Kind); Assert.Equal(posted.Journal.Id, replay.Journal?.Id);
        Assert.Single(await scope.Db.Journals.ToListAsync()); Assert.Single(await scope.Db.Deliveries.ToListAsync());
        Assert.False(await scope.Db.Commands.AnyAsync(row => row.CommandName == ManufacturingCostDifferenceCommandNamesV1.Approve));
        await book.AssertSourceAccountsNetAsync(100m);
    }
}
