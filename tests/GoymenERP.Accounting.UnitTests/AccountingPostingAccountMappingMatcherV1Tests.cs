using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>B-04a: the pure resolution rule and selector shape, without a store.</summary>
public sealed class AccountingPostingAccountMappingMatcherV1Tests
{
    private const string Purchase = AccountingPostingAccountMappingPurposesV1.PurchaseInvoice;
    private const string Inventory = AccountingPostingAccountMappingKeysV1.Inventory;
    private static readonly Guid Wildcard = Guid.Parse("c0400000-0000-4000-8000-000000000001");
    private static readonly Guid SteelOnly = Guid.Parse("c0400000-0000-4000-8000-000000000002");
    private static readonly Guid SteelExport = Guid.Parse("c0400000-0000-4000-8000-000000000003");
    private static readonly Guid ExportOnly = Guid.Parse("c0400000-0000-4000-8000-000000000004");

    [Fact]
    public void The_row_naming_the_most_matching_selectors_wins_and_an_unknown_question_value_matches_no_named_selector()
    {
        var entries = new[]
        {
            Entry(1, null, null, null, Wildcard),
            Entry(2, "Steel", null, null, SteelOnly),
            Entry(3, "Steel", null, true, SteelExport),
        };

        Assert.Equal(SteelExport, Match(entries, "Steel", null, true).Entry?.AccountId);
        Assert.Equal(SteelOnly, Match(entries, "Steel", null, false).Entry?.AccountId);
        Assert.Equal(SteelOnly, Match(entries, "Steel", null, null).Entry?.AccountId);
        Assert.Equal(Wildcard, Match(entries, null, null, true).Entry?.AccountId);
        Assert.Equal(Wildcard, Match(entries, "Scrap", "raw-material", true).Entry?.AccountId);
    }

    [Fact]
    public void Two_equally_specific_rows_are_ambiguous_and_no_row_is_unmapped()
    {
        var entries = new[]
        {
            Entry(1, "Steel", null, null, SteelOnly),
            Entry(2, null, null, true, ExportOnly),
        };

        Assert.Equal((AccountingPostingAccountMappingMatchKindV1.Ambiguous, (AccountingPostingAccountMappingEntryV1?)null),
            Match(entries, "Steel", null, true));
        Assert.Equal(AccountingPostingAccountMappingMatchKindV1.Unmapped, Match(entries, "Scrap", null, false).Kind);
        Assert.Equal(AccountingPostingAccountMappingMatchKindV1.Unmapped, Match([], "Steel", null, true).Kind);
        Assert.Equal(AccountingPostingAccountMappingMatchKindV1.Unmapped,
            AccountingPostingAccountMappingMatcherV1.Match(entries, Purchase, AccountingPostingAccountMappingKeysV1.Payable,
                "Steel", null, true).Kind);
    }

    [Fact]
    public void An_optional_vat_rate_row_outranks_the_single_vat_account_only_for_its_own_rate()
    {
        const string sales = AccountingPostingAccountMappingPurposesV1.SalesInvoice;
        const string vat = AccountingPostingAccountMappingKeysV1.VatOutput;
        var entries = new AccountingPostingAccountMappingEntryV1[]
        {
            new(1, sales, vat, null, null, null, Wildcard, "391.01"),
            new(2, sales, vat, null, null, null, SteelOnly, "391.20", "KDV20"),
        };

        Assert.Equal(SteelOnly, AccountingPostingAccountMappingMatcherV1.Match(entries, sales, vat, null, null, null, "KDV20").Entry?.AccountId);
        Assert.Equal(Wildcard, AccountingPostingAccountMappingMatcherV1.Match(entries, sales, vat, null, null, null, "KDV10").Entry?.AccountId);
        Assert.Equal(Wildcard, AccountingPostingAccountMappingMatcherV1.Match(entries, sales, vat, null, null, null).Entry?.AccountId);
    }

    [Fact]
    public void Selectors_are_compared_exactly_without_trimming_or_case_folding()
    {
        var entries = new[] { Entry(1, "Steel", null, null, SteelOnly) };

        Assert.Equal(AccountingPostingAccountMappingMatchKindV1.Unmapped, Match(entries, "steel", null, null).Kind);
        Assert.Equal(AccountingPostingAccountMappingMatchKindV1.Unmapped, Match(entries, "Steel ", null, null).Kind);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Steel", true)]
    [InlineData("raw-material", true)]
    [InlineData("a.b_c-9", true)]
    [InlineData("", false)]
    [InlineData(" Steel", false)]
    [InlineData("-raw", false)]
    [InlineData("ham madde", false)]
    [InlineData("Çelik", false)]
    public void A_selector_is_an_exact_ascii_code(string? value, bool expected)
    {
        Assert.Equal(expected, AccountingPostingAccountMappingPayloadV1.IsSelector(value));
        Assert.False(AccountingPostingAccountMappingPayloadV1.IsSelector(new string('a', 65)));
        Assert.True(AccountingPostingAccountMappingPayloadV1.IsSelector(new string('a', 64)));
    }

    [Fact]
    public void Each_purpose_carries_only_its_own_mapping_keys()
    {
        Assert.All(AccountingPostingAccountMappingKeysV1.SalesInvoice, key =>
        {
            Assert.True(AccountingPostingAccountMappingKeysV1.IsAllowed(AccountingPostingAccountMappingPurposesV1.SalesInvoice, key));
            Assert.False(AccountingPostingAccountMappingKeysV1.IsAllowed(Purchase, key));
        });
        Assert.All(AccountingPostingAccountMappingKeysV1.PurchaseInvoice, key =>
        {
            Assert.True(AccountingPostingAccountMappingKeysV1.IsAllowed(Purchase, key));
            Assert.False(AccountingPostingAccountMappingKeysV1.IsAllowed(AccountingPostingAccountMappingPurposesV1.SalesInvoice, key));
        });
        Assert.False(AccountingPostingAccountMappingKeysV1.IsAllowed("Sales-Invoice", "receivable"));
    }

    [Fact]
    public void A_payload_above_the_row_limit_is_refused_before_any_row_is_read()
    {
        var rows = Enumerable.Range(0, AccountingPostingAccountMappingLimitsV1.MaximumRows + 1)
            .Select(_ => new AccountingPostingAccountMappingRowRequestV1(Purchase, Inventory, null, null, null, Wildcard))
            .ToArray();

        var issue = AccountingPostingAccountMappingPayloadV1.Validate(rows, out var validated);

        Assert.Equal(new AccountingPostingAccountMappingPayloadV1.Issue(
            AccountingPostingAccountMappingCodesV1.RowLimitExceeded, "rows"), issue);
        Assert.Empty(validated);
    }

    private static (AccountingPostingAccountMappingMatchKindV1 Kind, AccountingPostingAccountMappingEntryV1? Entry) Match(
        IEnumerable<AccountingPostingAccountMappingEntryV1> entries, string? itemType, string? linePurpose, bool? isExport) =>
        AccountingPostingAccountMappingMatcherV1.Match(entries, Purchase, Inventory, itemType, linePurpose, isExport);

    private static AccountingPostingAccountMappingEntryV1 Entry(
        int line, string? itemType, string? linePurpose, bool? isExport, Guid account) =>
        new(line, Purchase, Inventory, itemType, linePurpose, isExport, account, "150." + line);
}
