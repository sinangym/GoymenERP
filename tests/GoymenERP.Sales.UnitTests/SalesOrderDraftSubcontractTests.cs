using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D175…D178 (EK25): the subcontract service ("fason hizmet") order in the aggregate — the flag fixed at create and kept in every
/// archived header, only service lines (SAT-D176), the free price only for a service line whose list held neither a price nor a floor
/// (SAT-D178; bound to the exact typed price and its writer), and the customer lock (fail-closed). Synthetic identities, NOT_ENV.
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

    private static SalesOrderDraft Draft(bool subcontract = true) => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001",
        new SalesDraftHeader(Party, Account, Currency, Day, DueTerms: TestDueTerms.PointDue30, IsSubcontractService: subcontract),
        new MoneyProfileSnapshot(Company, Currency, SalesOrderDraft.MoneyOperation, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3",
            Day, true, new DateOnly(2026, 9, 1), null, 2), T0);

    private static SalesDraftLineInput Line(string? itemType = SalesOrderDraft.ServiceItemType, CommercialDraftFields? commercial = null,
        Guid? lineId = null)
    {
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, KgProfile, "4");
        return new SalesDraftLineInput(lineId ?? Guid.NewGuid(), new ProductSnapshot(Product, "7", "HZM-001", "Sentetik işçilik", Day,
            binding, binding, binding), unit, "250", 250m, Commercial: commercial, ItemType: itemType);
    }

    private static PriceListAbsenceSnapshot Absence(string price = "37.5") => new(KgUnit, "TRY", Day, price, "SENTETIK-writer", T0);

    private static CommercialDraftFields Free(string price = "37.5") => new(price, KgUnit, null, ListAbsence: Absence(price));

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
    public void A_free_price_needs_no_discount_check_and_the_order_is_submitted_and_approved_like_an_unpriced_one()
    {
        var free = Free();
        Assert.True(free.IsFreeSubcontractPrice);
        Assert.False(SalesLinePriceCalculator.NeedsDiscountCheck(free));
        Assert.Null(SalesOrderDraft.WithDiscountLimitCheck(free, SalesDiscountLimit.Undefined, "SENTETIK-writer", T0).DiscountCheck);
        // A typed price WITHOUT the absence keeps SAT-D43: every such line goes to approval.
        Assert.True(SalesLinePriceCalculator.NeedsDiscountCheck(new CommercialDraftFields("37.5", KgUnit, null)));

        var draft = Draft();
        draft.AddLine(draft.Version, Line(commercial: free), T0);
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        draft.Approve(draft.Version, "SENTETIK-approver", T0);
        Assert.Equal(InternalApprovalState.Approved, draft.ApprovalState);
    }

    [Fact]
    public void A_price_list_absence_is_admitted_only_on_a_subcontract_line_bound_to_its_exact_typed_price()
    {
        var normal = Draft(subcontract: false);
        Assert.Equal(SalesRuleCodes.InternalInvariant, CodeOf(() => normal.AddLine(normal.Version, Line(itemType: null, commercial: Free()), T0)));

        var draft = Draft();
        Assert.Equal(SalesRuleCodes.InternalInvariant,
            CodeOf(() => draft.AddLine(draft.Version, Line(commercial: Free() with { UnitPrice = "38" }), T0)));
        Assert.Equal(SalesRuleCodes.InternalInvariant,
            CodeOf(() => draft.AddLine(draft.Version, Line(commercial: Free() with { ListAbsence = Absence() with { EffectiveOn = Day.AddDays(1) } }), T0)));
        Assert.Equal(SalesRuleCodes.InternalInvariant,
            CodeOf(() => draft.AddLine(draft.Version, Line(commercial: Free() with { ListAbsence = Absence() with { WrittenBy = " " } }), T0)));
        var reference = new PriceReferenceSnapshot(Guid.NewGuid(), "SENTETIK liste", 1, Day, null, "TRY", KgUnit, "40", "35", "ExcludingVat", "0", null);
        Assert.Equal(SalesRuleCodes.InternalInvariant,
            CodeOf(() => draft.AddLine(draft.Version, Line(commercial: Free() with { Reference = reference }), T0)));
        Assert.Empty(draft.Lines);
    }

    [Fact]
    public void A_revision_resending_the_same_free_price_changes_nothing_and_a_new_price_is_bound_again()
    {
        var draft = Draft();
        var line = draft.AddLine(draft.Version, Line(commercial: Free()), T0);
        var later = T0.AddMinutes(5);
        var again = new CommercialDraftFields("37.5", KgUnit, null, ListAbsence: Absence() with { WrittenBy = "SENTETIK-other", WrittenAtUtc = later });

        Assert.Equal(SalesRuleCodes.RevisionNoChange,
            CodeOf(() => draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, again)], "SENTETIK-other", later)));

        draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, Free("41"))], "SENTETIK-writer", later);
        Assert.Equal(("41", (DiscountLimitCheckSnapshot?)null), (draft.Lines[0].Commercial.ListAbsence!.UnitPrice, draft.Lines[0].Commercial.DiscountCheck));
    }

    [Fact]
    public void The_flag_is_kept_in_the_archived_header_and_a_stored_history_naming_another_flag_is_refused()
    {
        var draft = Draft();
        var line = draft.AddLine(draft.Version, Line(commercial: Free()), T0);
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        draft.Approve(draft.Version, "SENTETIK-approver", T0);
        var archived = draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, Free("41"))], "SENTETIK-writer", T0)!;

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
    public void The_pricing_json_keeps_the_absence_and_a_line_without_one_keeps_its_exact_older_json()
    {
        var free = Free();
        var json = SalesLinePricingJson.Serialize(free)!;
        Assert.Contains("\"listAbsence\":{", json, StringComparison.Ordinal);
        Assert.Equal(free, SalesLinePricingJson.Restore(free.UnitPrice, free.PriceUnitId, free.DiscountPercent, json));

        var plain = new CommercialDraftFields("37.5", KgUnit, null, DiscountAmount: "1");
        Assert.DoesNotContain("listAbsence", SalesLinePricingJson.Serialize(plain)!, StringComparison.Ordinal);
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
                     "[sales].[DraftLines] WHERE [ItemType] IS NOT NULL", "[sales].[DraftLines] WHERE JSON_QUERY([PricingJson], '$.listAbsence') IS NOT NULL",
                     "[sales].[DraftRevisionLines] WHERE JSON_QUERY([PricingJson], '$.listAbsence') IS NOT NULL",
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
