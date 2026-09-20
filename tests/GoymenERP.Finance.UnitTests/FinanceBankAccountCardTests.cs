using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// K2 (Sinan #66 <c>5748258299</c>): the SHAPE of the bank account card in the model. The card's
/// RULES — bank name and account type mandatory, a given IBAN structurally valid — are measured where
/// they are applied, through the real <c>ProposeAsync</c> in
/// <see cref="FinanceBankAccountTests.A_proposal_is_refused_before_anything_is_read"/>, so that the
/// cases prove the propose path USES the rule rather than that the rule exists.
///
/// <para>
/// WHAT THIS SLICE DELIBERATELY LEAVES OUT, so it is not read as missed work. K2 also says a
/// money-capable account must carry an IBAN. That half is not here: <c>IsUsableForReceipt</c> derives
/// from approval alone today, so a card declaring itself "not transfer-capable" would still read
/// usable — a field whose meaning the system contradicts is worse than no field. The declaration and
/// the derivation are ONE change and land together. The gap PREDATES this slice: on main every
/// approved card already reads usable regardless of IBAN, because no IBAN existed at all.
/// </para>
///
/// <para>
/// And the moment that derivation takes a third input, rows written before K2 have no value for it, so
/// every meaning given to their NULL — "leave as is" included — answers the open question
/// <c>#66 5748390243</c>. That is why the remainder waits on a human rather than sitting in a queue.
/// </para>
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinanceBankAccountCardTests
{
    /// <summary>
    /// Every card column is NULLABLE, including the two K2 calls mandatory — and that is the point, not
    /// a weakening. A <c>NOT NULL</c> column would need a value for the rows that already exist, and any
    /// value chosen for them would answer <c>#66 5748390243</c> from inside a migration. The mandatory
    /// pair is enforced where a card is WRITTEN instead.
    /// </summary>
    [Fact]
    public void The_card_columns_are_nullable_because_the_rows_that_predate_K2_are_not_this_slice_to_decide()
    {
        using var context = Context();
        var accounts = context.Model.FindEntityType(typeof(BankAccountRow))!;

        foreach (var name in new[] { "BankName", "AccountType", "BranchName", "BranchCode", "Iban", "AccountNumber" })
        {
            var property = accounts.FindProperty(name);
            Assert.NotNull(property);
            Assert.True(property!.IsNullable, $"{name} must stay nullable: rows written before K2 have no value for it");
        }

        // Stored normalised, so the column never has to hold display spacing (ISO 13616 maximum is 34).
        Assert.Equal(34, accounts.FindProperty("Iban")!.GetMaxLength());
        // Still no money on this table: a card is a card, and this slice moves nothing.
        Assert.DoesNotContain(accounts.GetProperties(), property => property.ClrType == typeof(decimal));
    }

    private static FinanceDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        return new FinanceDbContext(
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
                .Options,
            company);
    }
}
