using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// ACC-4 (#294/#298 P3s): null request parts are caller errors in the domain and the store, and a
/// message's canonical content ignores delivery details but not business facts.
/// </summary>
public sealed class Acc4Tests
{
    [Theory]
    [InlineData("source")]
    [InlineData("dates")]
    [InlineData("lines")]
    public void A_null_request_part_is_a_caller_error_in_the_domain(string part)
    {
        var world = new AccountingWorld();
        var request = Broken(world.Request("m-1"), part);

        Assert.Throws<ArgumentException>(() => world.Ledger.Receive(request, world.OpenPeriod));
        Assert.Empty(world.Ledger.Journals);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("dates")]
    [InlineData("lines")]
    public async Task A_null_request_part_is_a_caller_error_in_the_store_before_the_database(string part)
    {
        var world = new AccountingWorld();
        var companyContext = new CompanyContext();
        companyContext.Set(world.Company);
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        await using var dbContext = new AccountingDbContext(options, companyContext);
        var store = new AccountingStore(dbContext, companyContext, new AuditEventCollector(), TimeProvider.System);
        var metadata = new AccountingCommandMetadata("synthetic-actor", "synthetic.accounting", ["SYNTHETIC"],
            "synthetic-correlation", null, "synthetic-key", "Web");

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.ReceiveAsync(Broken(world.Request("m-1"), part), Guid.NewGuid(), metadata));
    }

    [Fact]
    public void Delivery_details_are_not_content_but_business_facts_are()
    {
        var world = new AccountingWorld();
        var request = world.Request("m-1");
        var hash = AccountingStore.ReceiveHash(request);

        // The journal id a redelivery proposes, the recording instant, line order and trailing zeros are not content.
        var redelivered = request with
        {
            JournalId = Guid.NewGuid(),
            Dates = new PostingDates(request.Dates.TransactionDate, request.Dates.AccountingDate, request.Dates.ValueDate,
                request.Dates.RecordedAtUtc.AddHours(1)),
            Lines = [.. request.Lines.Reverse().Select(line => line with { Amount = line.Amount + 0.00m })],
        };
        Assert.Equal(hash, AccountingStore.ReceiveHash(redelivered));

        // Amount, rule version, purpose, currency and business dates are.
        Assert.NotEqual(hash, AccountingStore.ReceiveHash(world.Request("m-1", amount: 101m)));
        Assert.NotEqual(hash, AccountingStore.ReceiveHash(world.Request("m-1", ruleVersion: "synthetic-rule-2")));
        Assert.NotEqual(hash, AccountingStore.ReceiveHash(world.Request("m-1", purpose: "synthetic-purpose-b")));
        Assert.NotEqual(hash, AccountingStore.ReceiveHash(request with { CurrencyId = Guid.NewGuid() }));
        Assert.NotEqual(hash, AccountingStore.ReceiveHash(request with { Dates = AccountingWorld.Dates(new DateOnly(2026, 9, 16)) }));
        Assert.NotEqual(hash, AccountingStore.ReceiveHash(world.Request("m-1", mapCredit: false)));
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Each_operation_has_its_own_content()
    {
        var world = new AccountingWorld();
        var journal = Guid.NewGuid();
        var lines = new[] { new JournalLine(world.DebitAccount, 5m, 0m), new JournalLine(world.CreditAccount, 0m, 5m) };
        var correction = AccountingStore.CorrectHash(Guid.NewGuid(), journal, "synthetic-rule-2", world.InPeriod, lines);

        Assert.NotEqual(AccountingStore.ReverseHash(journal, world.InPeriod), correction);
        Assert.NotEqual(AccountingStore.ReverseHash(journal, world.InPeriod),
            AccountingStore.ReverseHash(journal, AccountingWorld.Dates(new DateOnly(2026, 9, 16))));
        // A repeated line is content: two equal lines are two movements, not one.
        Assert.NotEqual(AccountingStore.CorrectHash(Guid.Empty, journal, "r", world.InPeriod, lines),
            AccountingStore.CorrectHash(Guid.Empty, journal, "r", world.InPeriod, [.. lines, lines[0]]));
    }

    private static PostingRequest Broken(PostingRequest request, string part) => part switch
    {
        "source" => request with { Source = null! },
        "dates" => request with { Dates = null! },
        _ => request with { Lines = null! },
    };
}
