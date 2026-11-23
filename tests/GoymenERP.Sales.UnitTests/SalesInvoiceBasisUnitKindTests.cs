using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// AK-4 / K3 review (sales P2-8): the unit kind of an archived invoice-basis line comes from the order's own Settings answer for
/// that unit — the same line, else another line holding the same unit identity. A unit no line of the order answers is never
/// defaulted to a piece unit: the basis is unreadable (503 reference provider invalid) and nothing is invoiced. Synthetic ids.
/// </summary>
public sealed class SalesInvoiceBasisUnitKindTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 9, 27);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");

    [Fact]
    public void The_archived_line_takes_its_own_lines_settings_answer_for_the_unit()
    {
        var draft = Draft(out var line);

        Assert.True(SalesInvoiceService.IsKilogramUnit(draft, line.Id, KgUnit));
    }

    [Fact]
    public void A_line_whose_unit_changed_takes_the_answer_another_line_holds_for_the_same_unit()
    {
        var draft = Draft(out _);

        Assert.True(SalesInvoiceService.IsKilogramUnit(draft, Guid.NewGuid(), KgUnit));
    }

    [Fact]
    public void A_unit_no_line_of_the_order_answers_is_refused_never_defaulted_to_pieces()
    {
        var draft = Draft(out var line);

        var refused = Assert.Throws<SalesDraftServiceException>(() => SalesInvoiceService.IsKilogramUnit(draft, line.Id, Guid.NewGuid()));

        Assert.Equal((503, SalesDraftProblemCodesV1.ReferenceProviderInvalid), (refused.Status, refused.Code));
    }

    private static SalesOrderDraft Draft(out SalesOrderDraftLine line)
    {
        var header = new SalesDraftHeader(Guid.Parse("33333333-3333-4333-8333-333333333333"),
            Guid.Parse("44444444-4444-4444-8444-444444444444"), Guid.Parse("55555555-5555-4555-8555-555555555555"), Day, Day.AddDays(10),
            DueTerms: TestDueTerms.PointDue30);
        var money = new MoneyProfileSnapshot(Company, Guid.Parse("55555555-5555-4555-8555-555555555555"), SalesOrderDraft.MoneyOperation,
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3", Day, true, new DateOnly(2026, 9, 1), null, 2);
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Guid.Parse("99999999-9999-4999-8999-999999999999"), "7", "GDC-001", "Sentetik bant", Day,
            binding, binding, binding);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001", header, money, T0);
        line = draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "1.25", 1.25m, Day.AddDays(3)), T0);
        return draft;
    }
}
