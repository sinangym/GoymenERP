using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D175/D176 (EK25), SAT-D195 (EK29) in the aggregate: the "fason hizmet" flag fixed at create and kept in every archived header, only service lines
/// sold and priced per kilogram, no price list and no discount check (the order's internal approval decides the price), the read
/// port's accepted-revision basis (SAT-D115), and the customer lock (fail-closed). Synthetic identities, NOT_ENV.
/// </summary>
public sealed class SalesOrderDraftSubcontractTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 2);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static SalesOrderDraft Draft(bool subcontract = true, bool acceptance = false) => SalesOrderDraft.Create(Guid.NewGuid(), Company,
        "SO-2026-000001",
        new SalesDraftHeader(Party, Account, Currency, Day, DueTerms: TestDueTerms.PointDue30, IsSubcontractService: subcontract),
        new MoneyProfileSnapshot(Company, Currency, SalesOrderDraft.MoneyOperation, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3",
            Day, true, new DateOnly(2026, 9, 1), null, 2), T0, customerAcceptanceRequired: acceptance);

    [Fact]
    public void K3_review_B_P2_2_the_read_port_says_rejected_and_answers_the_customer_accepted_revisions_lines()
    {
        var draft = Draft(acceptance: true);
        var line = draft.AddLine(draft.Version, Line(commercial: PerKg()), T0);
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        draft.Approve(draft.Version, "SENTETIK-approver", T0);
        var pending = SalesSubcontractServiceOrderReadPort.Order(draft);
        Assert.Equal((SalesSubcontractCustomerAcceptanceStatesV1.Pending, SalesSubcontractServiceOrderBasesV1.CurrentRevision, 0),
            (pending.CustomerAcceptanceState, pending.Basis, pending.BasisRevision));
        draft.RecordCustomerAcceptance(draft.Version, "SENTETIK-sales", T0);
        Assert.Equal(SalesSubcontractCustomerAcceptanceStatesV1.Accepted, SalesSubcontractServiceOrderReadPort.Order(draft).CustomerAcceptanceState);

        // Rev.1 raises the quantity, is approved internally and REJECTED by the customer (SAT-D115): the order goes on as Rev.0.
        draft.ReviseQuantities(draft.Version, [new LineQuantityInput(line.Id, "300", 300m)], "SENTETIK-writer", "SENTETIK 300 kg", T0);
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        draft.Approve(draft.Version, "SENTETIK-approver", T0);
        draft.RecordCustomerRejection(draft.Version, "SENTETIK-sales", "SENTETIK müşteri reddetti", T0);

        var order = SalesSubcontractServiceOrderReadPort.Order(draft);
        Assert.Equal((1, true, SalesSubcontractCustomerAcceptanceStatesV1.Rejected, SalesSubcontractServiceOrderBasesV1.CustomerAcceptedRevision, 0),
            (order.Revision, order.IsApproved, order.CustomerAcceptanceState, order.Basis, order.BasisRevision));
        var basisLine = Assert.Single(order.Lines);
        Assert.Equal((line.Id, "250", 250m, "Service"), (basisLine.LineId, basisLine.QuantityText, basisLine.Quantity, basisLine.ItemType));
    }

    [Fact]
    public void K3_review_A_P3_1_a_rejected_revision_whose_accepted_fallback_names_another_currency_has_no_basis()
    {
        // This history can also be made through the commands (SAT-D128's path: a commercial revision clears the prices → a currency
        // change → a new per-kg price → approval → the customer's rejection); the test builds it directly the way the store restores a
        // stored order, which is shorter and pins the port's answer to exactly this state.
        var draft = Draft(acceptance: true);
        var line = draft.AddLine(draft.Version, Line(commercial: PerKg()), T0);
        draft.RestoreCreator("SENTETIK-writer");
        draft.RestoreRevision(1);
        var otherCurrency = Guid.Parse("5d5d5d5d-5d5d-45d5-85d5-5d5d5d5d5d5d");
        draft.RestoreArchivedRevision(new ApprovedRevisionSnapshot(0,
            new ApprovalFacts("SENTETIK-writer", T0, "SENTETIK-approver", T0, null), Guid.Parse("acacacac-acac-4cac-8cac-acacacacacac"), "1", 2,
            T0, "SENTETIK-writer", [new RevisionLineSnapshot(line.Id, 1, Product, "7", KgUnit, "250", PerKg())], "SENTETIK para birimi",
            new RevisionHeaderSnapshot(Party, Account, otherCurrency, null, null, null, TestDueTerms.PointDue30, null, IsSubcontractService: true)));
        draft.RestoreCustomerAcceptance(new CustomerAcceptanceFacts(0, "SENTETIK-sales", T0));
        draft.RestoreCustomerRejection(new CustomerRejectionFacts(1, "SENTETIK-sales", T0, "SENTETIK müşteri reddetti"));
        draft.RestoreApproval(InternalApprovalState.Approved, new ApprovalFacts("SENTETIK-writer", T0, "SENTETIK-approver", T0, null), 20, T0);
        Assert.NotNull(draft.CustomerAcceptedFallback);

        var answer = SalesSubcontractServiceOrderReadPort.Answer(draft);
        Assert.Equal((SalesSubcontractServiceOrderOutcomeV1.Unreadable, (SalesSubcontractServiceOrderV1?)null,
            SalesSubcontractServiceOrderUnreadableReasonsV1.NoBasis), (answer.Outcome, answer.Order, answer.Reason));
    }

    [Fact]
    public void SAT_D201_a_fason_order_with_a_service_line_without_a_per_kg_price_is_not_sent_for_approval()
    {
        var draft = Draft();
        var line = draft.AddLine(draft.Version, Line(), T0);
        Assert.Equal(SalesRuleCodes.SubcontractPriceRequired, CodeOf(() => draft.Submit(draft.Version, "SENTETIK-writer", T0)));
        Assert.Equal(InternalApprovalState.Preparing, draft.ApprovalState);

        draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, PerKg())], "SENTETIK-writer", T0);
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        Assert.Equal(InternalApprovalState.PendingApproval, draft.ApprovalState);

        // A normal order's unpriced submission is unchanged.
        var normal = Draft(subcontract: false);
        normal.AddLine(normal.Version, Line(itemType: null), T0);
        normal.Submit(normal.Version, "SENTETIK-writer", T0);
        Assert.Equal(InternalApprovalState.PendingApproval, normal.ApprovalState);
    }

    private static readonly Guid PieceUnit = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid PieceProfile = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>A service line of 250 in the order unit (kg by default, piece when <paramref name="kilogram"/> is false); the stock unit is a piece.</summary>
    private static SalesDraftLineInput Line(string? itemType = SalesOrderDraft.ServiceItemType, CommercialDraftFields? commercial = null,
        Guid? lineId = null, bool kilogram = true)
    {
        var unit = kilogram
            ? new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, new DateOnly(2026, 9, 1), null)
            : new UnitProfileSnapshot(Company, PieceUnit, "adet", "count", PieceProfile, "1", false, 0, 0, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, unit.UnitId, unit.ProfileId, unit.ProfileVersion);
        var piece = new UnitBindingSnapshot(Company, PieceUnit, PieceProfile, "1");
        return new SalesDraftLineInput(lineId ?? Guid.NewGuid(), new ProductSnapshot(Product, "7", "HZM-001", "Sentetik işçilik", Day,
            piece, binding, binding), unit, "250", 250m, Commercial: commercial, ItemType: itemType);
    }

    /// <summary>SAT-D195: a per-kg manual price (the price unit is the line's kg order unit); no list, no check.</summary>
    private static CommercialDraftFields PerKg(string price = "37.5") => new(price, KgUnit, null);

    [Fact]
    public void The_service_item_type_is_the_product_and_settings_wire_value()
    {
        Assert.Equal(ProductItemTypesV1.Service, SalesOrderDraft.ServiceItemType);
        Assert.Equal(TaxItemTypesV1.Service, SalesOrderDraft.ServiceItemType);
    }

    [Fact]
    public void A_subcontract_order_takes_only_service_lines_and_a_normal_order_carries_no_item_type()
    {
        var draft = Draft();
        Assert.True(draft.IsSubcontractService);
        Assert.Equal(SalesRuleCodes.SubcontractLineNotService, CodeOf(() => draft.AddLine(draft.Version, Line(itemType: null), T0)));
        Assert.Equal(SalesRuleCodes.SubcontractLineNotService, CodeOf(() => draft.AddLine(draft.Version, Line(itemType: "Steel"), T0)));
        var line = draft.AddLine(draft.Version, Line(), T0);
        Assert.Equal(SalesOrderDraft.ServiceItemType, line.ItemType);

        var normal = Draft(subcontract: false);
        Assert.False(normal.IsSubcontractService);
        Assert.Equal(SalesRuleCodes.InternalInvariant, CodeOf(() => normal.AddLine(normal.Version, Line(), T0)));
        Assert.Null(normal.AddLine(normal.Version, Line(itemType: null), T0).ItemType);
    }

    [Fact]
    public void SAT_D195_a_per_kg_manual_price_carries_no_discount_check_and_is_approved_by_the_orders_internal_approval()
    {
        var draft = Draft();
        var line = draft.AddLine(draft.Version, Line(commercial: PerKg()), T0);
        Assert.Null(line.Commercial.DiscountCheck);
        // A normal order's typed price without a list reference still goes to the discount exception (SAT-D43, unchanged).
        Assert.True(SalesLinePriceCalculator.NeedsDiscountCheck(PerKg()));

        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        draft.Approve(draft.Version, "SENTETIK-approver", T0);
        Assert.Equal(InternalApprovalState.Approved, draft.ApprovalState);

        // A quantity or price change re-enters the internal approval like any order (DEC-SAL-002); still no discount routing.
        draft.ReviseQuantities(draft.Version, [new LineQuantityInput(line.Id, "5000", 5000m)], "SENTETIK-writer", "SENTETIK 5000 kg", T0);
        draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, PerKg("41"))], "SENTETIK-writer", T0);
        Assert.Equal(("41", (DiscountLimitCheckSnapshot?)null), (draft.Lines[0].Commercial.UnitPrice, draft.Lines[0].Commercial.DiscountCheck));
        Assert.Equal(SalesRuleCodes.RevisionNoChange,
            CodeOf(() => draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, PerKg("41"))], "SENTETIK-writer", T0)));
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        Assert.Equal(InternalApprovalState.PendingApproval, draft.ApprovalState);
    }

    [Fact]
    public void SAT_D195_a_subcontract_line_never_uses_the_price_list_and_is_priced_and_sold_per_kilogram()
    {
        var draft = Draft();
        var reference = new PriceReferenceSnapshot(Guid.NewGuid(), "SENTETIK liste", 1, Day, null, "TRY", KgUnit, "40", "35", "ExcludingVat", "0", null);
        Assert.Equal(SalesRuleCodes.SubcontractPriceListNotUsed,
            CodeOf(() => draft.AddLine(draft.Version, Line(commercial: PerKg() with { Reference = reference }), T0)));
        Assert.Equal(SalesRuleCodes.SubcontractUnitNotKilogram,
            CodeOf(() => draft.AddLine(draft.Version, Line(commercial: PerKg() with { PriceUnitId = PieceUnit }), T0)));
        Assert.Equal(SalesRuleCodes.SubcontractUnitNotKilogram,
            CodeOf(() => draft.AddLine(draft.Version, Line(kilogram: false), T0)));
        Assert.Equal(SalesRuleCodes.InternalInvariant, CodeOf(() => draft.AddLine(draft.Version, Line(commercial:
            SalesOrderDraft.WithDiscountLimitCheck(PerKg(), SalesDiscountLimit.Undefined, "SENTETIK-writer", T0)), T0)));
        Assert.Empty(draft.Lines);

        var line = draft.AddLine(draft.Version, Line(commercial: PerKg()), T0);
        Assert.Equal(SalesRuleCodes.SubcontractPriceListNotUsed, CodeOf(() => draft.ReviseCommercial(draft.Version,
            [new LineCommercialInput(line.Id, PerKg("40") with { Reference = reference })], "SENTETIK-writer", T0)));
        // No discount exception exists to approve on a subcontract order.
        Assert.Equal(SalesRuleCodes.DiscountExceptionLinesMismatch, CodeOf(() => draft.ApproveDiscountExceptions(draft.Version,
            [new LineDiscountExceptionApprovalInput(line.Id, "SENTETIK")], "SENTETIK-approver", T0)));

        // A normal order's rules are unchanged: a piece unit and a list reference are its own business.
        var normal = Draft(subcontract: false);
        normal.AddLine(normal.Version, Line(itemType: null, kilogram: false), T0);
        Assert.Single(normal.Lines);
    }

    [Fact]
    public void The_flag_is_kept_in_the_archived_header_and_a_stored_history_naming_another_flag_is_refused()
    {
        var draft = Draft();
        var line = draft.AddLine(draft.Version, Line(commercial: PerKg()), T0);
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        draft.Approve(draft.Version, "SENTETIK-approver", T0);
        var archived = draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, PerKg("41"))], "SENTETIK-writer", T0)!;

        Assert.True(archived.Header!.IsSubcontractService);
        Assert.True(draft.IsSubcontractService);

        var normal = Draft(subcontract: false);
        normal.AddLine(normal.Version, Line(itemType: null), T0);
        normal.RestoreRevision(1);
        Assert.Throws<InvalidOperationException>(() => normal.RestoreArchivedRevision(archived));
    }

    [Fact]
    public void The_customer_of_a_subcontract_order_changes_only_with_the_owners_confirmation_that_nothing_is_linked()
    {
        var draft = Draft();
        draft.AddLine(draft.Version, Line(), T0);
        var other = Guid.Parse("5c5c5c5c-5c5c-45c5-85c5-5c5c5c5c5c5c");
        var input = new SalesHeaderRevisionInput(Party, other, Currency, null, null, null, null, TestDueTerms.PointDue30);

        Assert.Equal(SalesRuleCodes.SubcontractCustomerLocked, CodeOf(() => draft.ReviseHeader(draft.Version, input, "SENTETIK", "SENTETIK", T0)));

        draft.ReviseHeader(draft.Version, input with { SubcontractCustomerUnbound = true }, "SENTETIK", "SENTETIK yanlış cari", T0);
        Assert.Equal((other, true), (draft.Header.CompanyAccountId, draft.IsSubcontractService));

        // A normal order's customer change is unchanged by the lock (SAT-D128/D152 rules only).
        var normal = Draft(subcontract: false);
        normal.AddLine(normal.Version, Line(itemType: null), T0);
        normal.ReviseHeader(normal.Version, input, "SENTETIK", "SENTETIK yanlış cari", T0);
        Assert.Equal(other, normal.Header.CompanyAccountId);
    }

    [Fact]
    public void The_subcontract_migration_down_refuses_with_51141_before_it_drops_anything()
    {
        var migration = new AddDraftSubcontractService();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51141,", guard.Sql, StringComparison.Ordinal);
        foreach (var condition in new[]
                 {
                     "[sales].[Drafts] WHERE [IsSubcontractService] = 1", "[sales].[DraftRevisionHeaders] WHERE [IsSubcontractService] = 1",
                     "[sales].[DraftLines] WHERE [ItemType] IS NOT NULL",
                 })
        {
            Assert.Contains(condition, guard.Sql, StringComparison.Ordinal);
        }

        Assert.All(migration.DownOperations.Skip(1), operation => Assert.IsNotType<SqlOperation>(operation));
        var added = migration.UpOperations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(2, added.Count(column => column.Name == "IsSubcontractService" && !column.IsNullable && Equals(column.DefaultValue, false)));
        Assert.Single(added, column => column.Name == "ItemType" && column.IsNullable);
    }
}
