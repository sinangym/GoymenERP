using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Infrastructure;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// INV-OPEN-2: the record route's problem codes are CLOSED per status (the OpenAPI lists exactly these). Every
/// store, scale or domain refusal the command maps lands inside the set of its status; an unknown code is not
/// mapped at all (an internal fault, never a guessed 4xx).
/// </summary>
public sealed class InventoryOpeningCodesTests
{
    public static TheoryData<string, int> Mapped => new()
    {
        { InventoryStoreCodes.OpeningConflict, 409 },
        { InventoryStoreCodes.LabelConflict, 409 },
        { InventoryStoreCodes.LabelCodeTaken, 409 },
        { InventoryStoreCodes.LotConflict, 409 },
        { InventoryStoreCodes.MovementIdConflict, 409 },
        { InventoryStoreCodes.DuplicateSource, 409 },
        { InventoryRuleCodes.LabelAlreadyInStock, 409 },
        { InventoryStoreCodes.CompanyContextMismatch, 403 },
        { InventoryStoreCodes.ValueDoesNotFit, 400 },
        { InventoryScaleCodes.ScaleExceeded, 400 },
        { InventoryScaleCodes.ProfileUnusable, 503 },
        { "settings_provider_unavailable", 503 },
        { "settings_unit_profile_not_found", 503 },
        { "settings_profile_invalid", 503 },
        { "settings_access_denied", 503 },
        { "settings_decimal_invalid", 503 },
        { "settings_decimal_scale_exceeded", 503 },
    };

    [Theory]
    [MemberData(nameof(Mapped))]
    public void A_mapped_refusal_answers_inside_the_closed_set_of_its_status(string code, int status)
    {
        var refusal = InventoryOpeningCommandService.RefusalFor(code);

        Assert.NotNull(refusal);
        Assert.Equal(status, refusal.Status);
        var set = status switch
        {
            400 => InventoryOpeningCodesV1.Status400Codes,
            409 => InventoryOpeningCodesV1.Status409Codes,
            503 => InventoryOpeningCodesV1.Status503Codes,
            _ => null,
        };
        if (set is not null)
        {
            Assert.Contains(refusal.Code, set);
        }

        Assert.All(refusal.Issues, issue => Assert.Contains(issue.Code, InventoryOpeningCodesV1.IssueCodes));
    }

    [Theory]
    [InlineData(InventoryStoreCodes.ConcurrentChange)]
    [InlineData(InventoryStoreCodes.LotProductMismatch)]
    [InlineData(InventoryStoreCodes.LotNotFound)]
    [InlineData(InventoryRuleCodes.NegativePhysicalStock)]
    [InlineData("settings_something_new")]
    [InlineData("an_unknown_code")]
    public void An_unknown_or_unreachable_code_is_not_mapped(string code)
    {
        // ConcurrentChange is answered by the §8 path before the mapping; the others are internal faults.
        Assert.Null(InventoryOpeningCommandService.RefusalFor(code));
    }

    [Fact]
    public void The_sets_do_not_overlap_and_a_location_not_found_is_422()
    {
        HashSet<string>[] sets =
        [
            [.. InventoryOpeningCodesV1.Status400Codes], [.. InventoryOpeningCodesV1.Status409Codes],
            [.. InventoryOpeningCodesV1.Status422Codes], [.. InventoryOpeningCodesV1.Status503Codes],
        ];
        Assert.Equal(sets.Sum(set => set.Count), sets.SelectMany(set => set).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(InventoryOpeningCodesV1.LocationNotFound, InventoryOpeningCodesV1.Status422Codes);
        Assert.DoesNotContain(InventoryOpeningCodesV1.LocationNotFound, InventoryOpeningCodesV1.Status409Codes);
    }
}
