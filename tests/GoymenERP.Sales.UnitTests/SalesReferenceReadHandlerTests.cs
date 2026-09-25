using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesReferenceReadHandlerTests
{
    [Fact]
    public async Task Missing_policy_denies_without_touching_owner_reads()
    {
        var f = new Fixture();
        var handler = f.Handler(new());
        Assert.Equal(403, (await handler.GetAccountsAsync(new(), "server-session")).StatusCode);
        Assert.Equal(403, (await handler.GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
        Assert.Equal(403, (await handler.GetProductAsync(f.ProductQuery)).StatusCode);
        Assert.Equal(0, f.Accounts.Calls + f.Settings.Calls + f.Products.Calls);
    }

    [Fact]
    public async Task Anonymous_or_foreign_company_cannot_probe_references()
    {
        var f = new Fixture();
        f.User.IsAuthenticated = false;
        Assert.Equal(401, (await f.Handler().GetAccountsAsync(new(), "session")).StatusCode);
        f.User.IsAuthenticated = true;
        f.Context.Set(Guid.NewGuid());
        Assert.Equal(403, (await f.Handler().GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
        Assert.Equal(0, f.Settings.Calls + f.Accounts.Calls);
    }

    [Fact]
    public async Task Accounts_use_real_read_port_active_pair_filter_and_roles_zero()
    {
        var f = new Fixture();
        var result = await f.Handler().GetAccountsAsync(new("search", 10), "server-session");
        Assert.Equal(200, result.StatusCode);
        var account = Assert.Single(result.Value!.Accounts);
        Assert.Equal(f.Company, result.Value.CompanyId);
        Assert.Equal("C001 - Example Party", account.Label);
        Assert.Equal("9007199254740993", account.AccountVersion);
        Assert.Equal(f.Company, f.Accounts.Request!.CompanyId);
        Assert.Equal("search", f.Accounts.Request.Q);
        Assert.Equal(10, f.Accounts.Request.PageSize);
        Assert.Equal("actor", f.Accounts.Request.ActorId);
        Assert.Equal("server-session", f.Accounts.Request.SessionBinding);
    }

    [Theory]
    [InlineData(403, 403)]
    [InlineData(400, 400)]
    [InlineData(503, 503)]
    public async Task Owner_failure_never_becomes_empty_success(int ownerStatus, int expected)
    {
        var f = new Fixture();
        f.Accounts.Result = new(ownerStatus, null, null);
        var result = await f.Handler().GetAccountsAsync(new(), "session");
        Assert.Equal(expected, result.StatusCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Authorized_empty_page_remains_distinct_from_unavailable()
    {
        var f = new Fixture();
        f.Accounts.Result = new(200, new([], null), null);
        var result = await f.Handler().GetAccountsAsync(new(), "session");
        Assert.Equal(200, result.StatusCode);
        Assert.Empty(result.Value!.Accounts);
    }

    [Fact]
    public async Task Late_company_change_discards_account_response()
    {
        var f = new Fixture();
        f.Accounts.OnRead = () => f.Context.Set(Guid.NewGuid());
        var result = await f.Handler().GetAccountsAsync(new(), "session");
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Current_permission_is_rechecked_after_provider_read()
    {
        var f = new Fixture();
        f.Settings.OnRead = () => f.Auth.Allowed = false;
        var result = await f.Handler().GetCurrencyAsync(f.CurrencyQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Value);
        Assert.Equal(2, f.Auth.Calls);
    }

    [Fact]
    public async Task Currency_resolves_owner_code_and_exact_profile_version_not_fake_name()
    {
        var f = new Fixture();
        var result = await f.Handler().GetCurrencyAsync(f.CurrencyQuery);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("XYZ", result.Value!.Label);
        Assert.Equal(result.Value.Code, result.Value.Label);
        Assert.Equal("9007199254740993", result.Value.ProfileVersion);
        Assert.Equal(new[] { (f.Currency, "controlled.operation", f.Day) }, f.Settings.MoneyCalls);
    }

    [Theory]
    [InlineData("settings_access_denied", 403)]
    [InlineData("settings_provider_unavailable", 503)]
    public async Task Settings_denial_and_unavailable_are_distinct(string code, int status)
    {
        var f = new Fixture();
        f.Settings.Error = new SettingsPrerequisiteException(code);
        var result = await f.Handler().GetCurrencyAsync(f.CurrencyQuery);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(code, result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Unbound_money_operation_cannot_call_provider_with_guess()
    {
        var f = new Fixture();
        var result = await f.Handler(Fixture.Bindings with { MoneyOperation = null }).GetCurrencyAsync(f.CurrencyQuery);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(0, f.Settings.Calls);
    }

    [Fact]
    public async Task Passive_expired_or_foreign_currency_cannot_be_offered()
    {
        var f = new Fixture();
        f.Settings.Money = f.Settings.Money with { IsActive = false };
        Assert.Equal(422, (await f.Handler().GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
        f.Settings.Money = f.Settings.Money with { IsActive = true, EffectiveThrough = f.Day.AddDays(-1) };
        Assert.Equal(422, (await f.Handler().GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
        f.Settings.Money = f.Settings.Money with { CompanyId = Guid.NewGuid() };
        Assert.Equal(403, (await f.Handler().GetCurrencyAsync(f.CurrencyQuery)).StatusCode);
    }

    [Fact]
    public async Task Missing_product_runtime_or_empty_precondition_errors_are_unavailable()
    {
        var f = new Fixture();
        var result = await f.Handler(productMissing: true).GetProductAsync(f.ProductQuery);
        Assert.Equal(503, result.StatusCode);
        f.Products.Result = new(null, []);
        result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(503, result.StatusCode);
        Assert.Null(result.Value);
        Assert.Equal(0, f.Settings.Calls);
    }

    [Fact]
    public async Task Owner_usable_snapshot_and_all_three_unit_bindings_are_revalidated()
    {
        var f = new Fixture();
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(new ProductSalesSelectionV1(f.Product, "7", f.Unit, f.Day), f.Products.Selection);
        Assert.Equal("P001 - Example Product", result.Value!.Label);
        Assert.Equal(new SalesUnitReferenceV1(f.Unit, "order-unit", f.OrderProfile, "3"),
            Assert.Single(result.Value.OrderUnits));
        Assert.Equal(new[] { (f.StockUnit, f.Day), (f.SalesUnit, f.Day), (f.Unit, f.Day) }, f.Settings.UnitCalls);
        Assert.Equal(5, f.Auth.Calls);
    }

    [Fact]
    public async Task Stale_product_or_profile_version_has_no_positive_option()
    {
        var f = new Fixture();
        f.Products.Result = new(f.Products.Result.Snapshot! with { ProductVersion = "8" }, []);
        Assert.Equal(409, (await f.Handler().GetProductAsync(f.ProductQuery)).StatusCode);
        f.Products.Result = new(f.Products.Result.Snapshot! with { ProductVersion = "7" }, []);
        f.Settings.Units[f.Unit] = f.Settings.Units[f.Unit] with { Profile = new(f.OrderProfile, 4) };
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(503, result.StatusCode);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(401, 401, null)]
    [InlineData(403, 403, null)]
    [InlineData(404, 404, null)]
    [InlineData(409, 409, "product_selection_changed")]
    [InlineData(400, 422, "unsupported_product")]
    [InlineData(422, 422, "unsupported_product")]
    [InlineData(500, 503, "product_provider_unavailable")]
    [InlineData(503, 503, "product_provider_unavailable")]
    public async Task Owner_operation_outcome_maps_to_the_reference_boundary(
        int ownerStatus, int expectedStatus, string? expectedCode)
    {
        var f = new Fixture();
        f.Products.Error = new ProductOperationException("owner-internal-code", ownerStatus);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.Value);
        Assert.Empty(f.Settings.UnitCalls);
    }

    [Fact]
    public async Task Owner_validation_exception_is_an_unsupported_product_not_an_outage()
    {
        var f = new Fixture();
        f.Products.Error = new ProductValidationException([new("productId", "invalid_reference")]);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(422, result.StatusCode);
        Assert.Equal("unsupported_product", result.Code);
        Assert.Null(result.Value);
        Assert.Empty(f.Settings.UnitCalls);
    }

    [Fact]
    public async Task Owner_authorization_exception_is_forbidden_without_detail()
    {
        var f = new Fixture();
        f.Products.Error = new UnauthorizedAccessException("owner-internal-detail");
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Owner_version_conflict_is_a_changed_selection()
    {
        var f = new Fixture();
        f.Products.Result = new(null,
            [new("expectedProductVersion", ProductApiV1.VersionConflict), new("productId", "not_sellable")]);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("product_selection_changed", result.Code);
        Assert.Null(result.Value);
        Assert.Empty(f.Settings.UnitCalls);
    }

    [Fact]
    public async Task A_version_conflict_beside_a_lifecycle_conflict_is_still_a_changed_selection()
    {
        var f = new Fixture();
        f.Products.Result = new(null,
            [new("productId", ProductApiV1.LifecycleConflict), new("expectedProductVersion", ProductApiV1.VersionConflict)]);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("product_selection_changed", result.Code);
        Assert.Null(result.Value);
    }

    [Theory]
    // A Draft was never selectable, and an Inactive product read at its current version
    // has not changed under the caller; neither is a changed selection.
    [InlineData("draft")]
    [InlineData("inactive")]
    public async Task A_lifecycle_conflict_at_the_current_version_is_an_unsupported_product(string state)
    {
        var f = new Fixture();
        f.Products.Result = new(null, state == "draft"
            ? [new("productId", ProductApiV1.LifecycleConflict), new("productId", "not_sellable")]
            : [new("productId", ProductApiV1.LifecycleConflict)]);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(422, result.StatusCode);
        Assert.Equal("unsupported_product", result.Code);
        Assert.Null(result.Value);
        Assert.Empty(f.Settings.UnitCalls);
    }

    [Fact]
    public async Task Owner_errors_reject_a_returned_snapshot()
    {
        var f = new Fixture();
        f.Products.Result = f.Products.Result with { Errors = [new("productId", "not_sellable")] };
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(422, result.StatusCode);
        Assert.Equal("unsupported_product", result.Code);
        Assert.Null(result.Value);
        Assert.Empty(f.Settings.UnitCalls);
    }

    [Fact]
    public async Task Owner_errors_without_a_snapshot_are_unsupported_not_unavailable()
    {
        var f = new Fixture();
        f.Products.Result = new(null, [new("orderUnitId", "unsupported_order_unit")]);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(422, result.StatusCode);
        Assert.Equal("unsupported_product", result.Code);
        Assert.Null(result.Value);
        Assert.Empty(f.Settings.UnitCalls);
    }

    [Theory]
    // Every state, at every binding: stock, sales and the selected order unit.
    [InlineData(0, "inactive")]
    [InlineData(0, "expired")]
    [InlineData(0, "not-yet-effective")]
    [InlineData(1, "inactive")]
    [InlineData(1, "expired")]
    [InlineData(1, "not-yet-effective")]
    [InlineData(2, "inactive")]
    [InlineData(2, "expired")]
    [InlineData(2, "not-yet-effective")]
    public async Task Inactive_or_out_of_range_unit_profile_is_an_unsupported_unit(int position, string variant)
    {
        var f = new Fixture();
        var unitId = f.UnitIds[position];
        var profile = f.Settings.Units[unitId];
        f.Settings.Units[unitId] = variant switch
        {
            "inactive" => profile with { IsActive = false },
            "expired" => profile with { EffectiveThrough = f.Day.AddDays(-1) },
            _ => profile with { EffectiveFrom = f.Day.AddDays(1) },
        };
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(422, result.StatusCode);
        Assert.Equal("unsupported_unit", result.Code);
        Assert.Null(result.Value);
        Assert.Equal(f.UnitIds.Take(position + 1).Select(id => (id, f.Day)), f.Settings.UnitCalls);
    }

    [Fact]
    public async Task An_empty_unit_id_in_the_snapshot_is_refused_before_the_owner_is_asked()
    {
        // A snapshot without a unit identity is the Product owner's malformed answer; Settings
        // is never asked about an empty id, and the answer stays the provider-invalid 503.
        var f = new Fixture();
        var snapshot = f.Products.Result.Snapshot!;
        f.Products.Result = new(snapshot with { SalesUnit = snapshot.SalesUnit with { UnitId = Guid.Empty } }, []);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("reference_provider_invalid", result.Code);
        Assert.Null(result.Value);
        Assert.Equal(new[] { (f.StockUnit, f.Day) }, f.Settings.UnitCalls);
    }

    [Fact]
    public async Task Foreign_company_unit_binding_is_denied_before_its_profile_read()
    {
        var f = new Fixture();
        var snapshot = f.Products.Result.Snapshot!;
        f.Products.Result = new(snapshot with { SalesUnit = snapshot.SalesUnit with { CompanyId = Guid.NewGuid() } }, []);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Null(result.Value);
        Assert.Equal(new[] { (f.StockUnit, f.Day) }, f.Settings.UnitCalls);
    }

    [Fact]
    public async Task Foreign_company_unit_profile_is_denied()
    {
        var f = new Fixture();
        f.Settings.Units[f.Unit] = f.Settings.Units[f.Unit] with { CompanyId = Guid.NewGuid() };
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task No_global_product_permission_fallback_on_owner_denial()
    {
        var f = new Fixture();
        f.Products.Error = new ProductOperationException("private-detail", 403);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Equal(0, f.Settings.Calls);
    }

    [Fact]
    public async Task The_account_search_bound_counts_runes_not_utf16_units()
    {
        var f = new Fixture();
        // 150 astral characters are 300 UTF-16 units: inside the bound, as on the route.
        var astral = string.Concat(Enumerable.Repeat("\U0001F600", 150));
        var accepted = await f.Handler().GetAccountsAsync(new(astral), "server-session");
        Assert.Equal(200, accepted.StatusCode);
        Assert.Equal(astral, f.Accounts.Request!.Q);

        var refused = await f.Handler().GetAccountsAsync(
            new(string.Concat(Enumerable.Repeat("\U0001F600", 201))), "server-session");
        Assert.Equal(400, refused.StatusCode);
        Assert.Equal("validation_failed", refused.Code);
        Assert.Equal(1, f.Accounts.Calls);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("007")]
    [InlineData("0")]
    [InlineData("99999999999999999999")]
    [InlineData(" ")]
    public async Task A_version_the_owner_could_not_parse_is_refused_before_the_owner_read(string version)
    {
        var f = new Fixture();
        var result = await f.Handler().GetProductAsync(f.ProductQuery with { ExpectedProductVersion = version });
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("validation_failed", result.Code);
        Assert.Equal(0, f.Products.Calls);
    }

    [Fact]
    public async Task An_owner_failure_is_not_released_after_the_permission_is_lost()
    {
        var f = new Fixture();
        f.Products.OnRead = () => f.Auth.Allowed = false;
        f.Products.Error = new ProductOperationException("owner-internal-code", 409);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task An_owner_401_keeps_its_status_even_after_the_permission_is_lost()
    {
        var f = new Fixture();
        f.Products.OnRead = () => f.Auth.Allowed = false;
        f.Products.Error = new ProductOperationException("owner-internal-code", 401);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(401, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task An_owner_validation_failure_is_not_released_after_the_permission_is_lost()
    {
        var f = new Fixture();
        f.Products.OnRead = () => f.Auth.Allowed = false;
        f.Products.Error = new ProductValidationException([new("productId", "invalid_reference")]);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("settings_access_denied", 403, "settings_access_denied")]
    [InlineData(SettingsPrerequisiteCodesV1.UnitProfileNotFound, 422, "unsupported_unit")]
    [InlineData("settings_provider_unavailable", 503, "settings_provider_unavailable")]
    [InlineData("settings_profile_invalid", 503, "settings_provider_unavailable")]
    public async Task Settings_codes_on_the_product_path_keep_their_meaning(string code, int status, string publicCode)
    {
        var f = new Fixture();
        f.Settings.Error = new SettingsPrerequisiteException(code);
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(publicCode, result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_unit_profile_code_on_the_currency_path_is_still_a_provider_failure()
    {
        // Only the product path reads unit profiles; the money read has no unit to refuse.
        var f = new Fixture();
        f.Settings.Error = new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitProfileNotFound);
        var result = await f.Handler().GetCurrencyAsync(f.CurrencyQuery);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("settings_provider_unavailable", result.Code);
    }

    [Fact]
    public async Task A_missing_unit_profile_is_not_released_after_the_permission_is_lost()
    {
        var f = new Fixture();
        f.Settings.Error = new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitProfileNotFound);
        f.Settings.OnRead = () => f.Auth.Allowed = false;
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_settings_failure_on_the_product_path_is_not_released_after_the_permission_is_lost()
    {
        var f = new Fixture();
        f.Settings.Error = new SettingsPrerequisiteException("settings_provider_unavailable");
        f.Settings.OnRead = () => f.Auth.Allowed = false;
        var result = await f.Handler().GetProductAsync(f.ProductQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_settings_failure_is_not_released_after_the_permission_is_lost()
    {
        var f = new Fixture();
        f.Settings.Error = new SettingsPrerequisiteException("settings_provider_unavailable");
        f.Settings.OnRead = () => f.Auth.Allowed = false;
        var result = await f.Handler().GetCurrencyAsync(f.CurrencyQuery);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Code);
        Assert.Null(result.Value);
    }

    // ---- SALES-A2a: the two list reads ------------------------------------------------------------

    [Fact]
    public async Task List_reads_deny_without_policy_and_without_touching_their_owners()
    {
        var f = new Fixture();
        Assert.Equal(403, (await f.Handler(new()).GetContactInformationAsync(f.ContactQuery)).StatusCode);
        Assert.Equal(403, (await f.Handler(new()).GetCurrenciesAsync(f.CurrenciesQuery)).StatusCode);
        f.User.IsAuthenticated = false;
        Assert.Equal(401, (await f.Handler().GetContactInformationAsync(f.ContactQuery)).StatusCode);
        Assert.Equal(401, (await f.Handler().GetCurrenciesAsync(f.CurrenciesQuery)).StatusCode);
        Assert.Equal(0, f.ContactInformation.Calls + f.Candidates.Calls + f.Settings.Calls);
    }

    [Fact]
    public async Task List_reads_without_their_owner_port_are_unavailable_not_empty()
    {
        var f = new Fixture();
        var contacts = await f.Handler(contactMissing: true).GetContactInformationAsync(f.ContactQuery);
        Assert.Equal((503, "counterparty_provider_unavailable"), (contacts.StatusCode, contacts.Code));
        var currencies = await f.Handler(candidatesMissing: true).GetCurrenciesAsync(f.CurrenciesQuery);
        Assert.Equal((503, "settings_provider_unavailable"), (currencies.StatusCode, currencies.Code));
        Assert.Null(contacts.Value);
        Assert.Null(currencies.Value);
        Assert.Equal(0, f.Settings.Calls);
    }

    [Fact]
    public async Task Contact_information_offers_only_active_items_with_the_owners_versions_and_a_joined_text()
    {
        var f = new Fixture();
        var result = await f.Handler().GetContactInformationAsync(f.ContactQuery);
        Assert.Equal(200, result.StatusCode);
        var value = result.Value!;
        Assert.Equal((f.Company, f.Party, f.Account, "9007199254740993"),
            (value.CompanyId, value.PartyId, value.CompanyAccountId, value.AccountVersion));
        var address = Assert.Single(value.Addresses);
        Assert.Equal((f.ActiveAddress, "3", "delivery", "TR", "Sanayi Cad. 1", "Kocaeli", "Gebze", "41400"),
            (address.Id, address.Version, address.Purpose, address.CountryCode, address.Line1, address.City, address.District, address.PostalCode));
        Assert.Equal("Sanayi Cad. 1, Gebze Kocaeli 41400, TR", address.Text);
        var contact = Assert.Single(value.Contacts);
        Assert.Equal((f.ActiveContact, "2", "Ayşe Yükleme", "Sevkiyat", "delivery"),
            (contact.Id, contact.Version, contact.DisplayName, contact.JobTitle, contact.Purpose));
        Assert.Equal((f.Party, f.Account), f.ContactInformation.Asked);
        // The permission is measured before and after the owner read.
        Assert.Equal(2, f.Auth.Calls);
    }

    [Fact]
    public void The_address_text_leaves_blank_parts_out()
    {
        var full = new SalesPartyAddressV1(Guid.NewGuid(), "1", true, "delivery", "TR", "Sanayi Cad. 1", "Kat 2", "Kocaeli", "Gebze", "41400");
        Assert.Equal("Sanayi Cad. 1, Kat 2, Gebze Kocaeli 41400, TR", SalesReferenceReadHandler.AddressText(full));
        var bare = full with { Line2 = " ", City = null, District = null, PostalCode = "" };
        Assert.Equal("Sanayi Cad. 1, TR", SalesReferenceReadHandler.AddressText(bare));
    }

    [Fact]
    public async Task Contact_information_with_empty_ids_is_refused_before_the_owner()
    {
        var f = new Fixture();
        var result = await f.Handler().GetContactInformationAsync(new(Guid.Empty, f.Account));
        Assert.Equal((400, "validation_failed"), (result.StatusCode, result.Code));
        Assert.Equal(400, (await f.Handler().GetContactInformationAsync(new(f.Party, Guid.Empty))).StatusCode);
        Assert.Equal(0, f.ContactInformation.Calls);
    }

    [Theory]
    [InlineData(SalesPartyContactInformationOutcomeV1.Denied, 403, null)]
    [InlineData(SalesPartyContactInformationOutcomeV1.NotFound, 404, null)]
    [InlineData(SalesPartyContactInformationOutcomeV1.Unavailable, 503, "counterparty_provider_unavailable")]
    public async Task Contact_information_owner_outcomes_keep_their_meaning(
        SalesPartyContactInformationOutcomeV1 outcome, int status, string? code)
    {
        var f = new Fixture();
        f.ContactInformation.Answer = new(outcome, null);
        var result = await f.Handler().GetContactInformationAsync(f.ContactQuery);
        Assert.Equal((status, code), (result.StatusCode, result.Code));
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Contact_information_of_another_company_is_denied_and_a_malformed_answer_is_unavailable()
    {
        var f = new Fixture();
        var information = f.ContactInformation.Answer.Information!;
        f.ContactInformation.Answer = new(SalesPartyContactInformationOutcomeV1.Found, information with { CompanyId = Guid.NewGuid() });
        Assert.Equal(403, (await f.Handler().GetContactInformationAsync(f.ContactQuery)).StatusCode);

        f.ContactInformation.Answer = new(SalesPartyContactInformationOutcomeV1.Found, information with { CompanyAccountId = Guid.NewGuid() });
        var other = await f.Handler().GetContactInformationAsync(f.ContactQuery);
        Assert.Equal((503, "reference_provider_invalid"), (other.StatusCode, other.Code));

        f.ContactInformation.Answer = new(SalesPartyContactInformationOutcomeV1.Found, information with { AccountVersion = "007" });
        Assert.Equal(503, (await f.Handler().GetContactInformationAsync(f.ContactQuery)).StatusCode);

        var blankLine = information.Addresses[0] with { Line1 = " " };
        f.ContactInformation.Answer = new(SalesPartyContactInformationOutcomeV1.Found, information with { Addresses = [blankLine] });
        Assert.Equal(503, (await f.Handler().GetContactInformationAsync(f.ContactQuery)).StatusCode);

        var blankName = information.Contacts[0] with { DisplayName = "" };
        f.ContactInformation.Answer = new(SalesPartyContactInformationOutcomeV1.Found, information with { Contacts = [blankName] });
        Assert.Equal(503, (await f.Handler().GetContactInformationAsync(f.ContactQuery)).StatusCode);

        // An INACTIVE malformed item is not offered, so it is not a reason to refuse the active ones.
        var inactiveBlank = information.Addresses[1] with { Line1 = "" };
        f.ContactInformation.Answer = new(SalesPartyContactInformationOutcomeV1.Found,
            information with { Addresses = [information.Addresses[0], inactiveBlank] });
        Assert.Equal(200, (await f.Handler().GetContactInformationAsync(f.ContactQuery)).StatusCode);
    }

    [Fact]
    public async Task Contact_information_is_not_released_after_the_permission_or_the_company_is_lost()
    {
        var f = new Fixture();
        f.ContactInformation.OnRead = () => f.Auth.Allowed = false;
        var lost = await f.Handler().GetContactInformationAsync(f.ContactQuery);
        Assert.Equal(403, lost.StatusCode);
        Assert.Null(lost.Value);

        var g = new Fixture();
        g.ContactInformation.OnRead = () => g.Context.Set(Guid.NewGuid());
        Assert.Equal(403, (await g.Handler().GetContactInformationAsync(g.ContactQuery)).StatusCode);

        var h = new Fixture();
        h.ContactInformation.Error = new UnauthorizedAccessException("owner-internal-detail");
        var refused = await h.Handler().GetContactInformationAsync(h.ContactQuery);
        Assert.Equal(403, refused.StatusCode);
        Assert.Null(refused.Code);
    }

    [Fact]
    public async Task Currencies_revalidate_every_candidate_for_the_operation_and_date_and_offer_the_usable_ones_in_order()
    {
        var f = new Fixture();
        var result = await f.Handler().GetCurrenciesAsync(f.CurrenciesQuery);
        Assert.Equal(200, result.StatusCode);
        var value = result.Value!;
        Assert.Equal((f.Company, f.Day), (value.CompanyId, value.EffectiveOn));
        Assert.Equal(new[] { f.Currency, f.SecondCurrency }, value.Currencies.Select(c => c.CurrencyId).ToArray());
        var first = value.Currencies[0];
        Assert.Equal((f.Company, f.Day, "XYZ", "XYZ", f.Profile, "9007199254740993"),
            (first.CompanyId, first.EffectiveOn, first.Code, first.Label, first.ProfileId, first.ProfileVersion));
        Assert.Equal("4", value.Currencies[1].ProfileVersion);
        Assert.Equal(1, f.Candidates.Calls);
        Assert.Equal(new[] { (f.Currency, "controlled.operation", f.Day), (f.SecondCurrency, "controlled.operation", f.Day) },
            f.Settings.MoneyCalls.ToArray());
        // Measured before, after the list read, and after each profile read.
        Assert.Equal(4, f.Auth.Calls);
    }

    [Fact]
    public async Task A_candidate_without_a_profile_for_the_operation_or_not_in_effect_is_left_out_not_an_outage()
    {
        var f = new Fixture();
        f.Settings.MoneyErrors[f.SecondCurrency] = new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.MoneyProfileNotFound);
        var missing = await f.Handler().GetCurrenciesAsync(f.CurrenciesQuery);
        Assert.Equal(200, missing.StatusCode);
        Assert.Equal(f.Currency, Assert.Single(missing.Value!.Currencies).CurrencyId);

        var g = new Fixture();
        g.Settings.Money = g.Settings.Money with { IsActive = false };
        var inactive = await g.Handler().GetCurrenciesAsync(g.CurrenciesQuery);
        Assert.Equal(g.SecondCurrency, Assert.Single(inactive.Value!.Currencies).CurrencyId);

        var h = new Fixture();
        h.Settings.Money = h.Settings.Money with { EffectiveFrom = h.Day.AddDays(1) };
        Assert.Equal(h.SecondCurrency, Assert.Single((await h.Handler().GetCurrenciesAsync(h.CurrenciesQuery)).Value!.Currencies).CurrencyId);
    }

    [Fact]
    public async Task An_empty_candidate_list_is_an_empty_answer_and_a_malformed_one_is_unavailable()
    {
        var f = new Fixture();
        f.Candidates.List = [];
        var empty = await f.Handler().GetCurrenciesAsync(f.CurrenciesQuery);
        Assert.Equal(200, empty.StatusCode);
        Assert.Empty(empty.Value!.Currencies);
        Assert.Equal(0, f.Settings.Calls);

        f.Candidates.List = [new(f.Currency, "XYZ", "XYZ"), new(f.Currency, "XYZ", "XYZ again")];
        var duplicate = await f.Handler().GetCurrenciesAsync(f.CurrenciesQuery);
        Assert.Equal((503, "settings_provider_unavailable"), (duplicate.StatusCode, duplicate.Code));
        Assert.Equal(0, f.Settings.Calls);
    }

    [Theory]
    [InlineData("settings_access_denied", 403)]
    [InlineData("settings_provider_unavailable", 503)]
    public async Task A_settings_failure_on_the_list_or_on_a_candidate_fails_the_whole_read(string code, int status)
    {
        var f = new Fixture();
        f.Candidates.Error = new SettingsPrerequisiteException(code);
        var list = await f.Handler().GetCurrenciesAsync(f.CurrenciesQuery);
        Assert.Equal((status, code), (list.StatusCode, list.Code));

        var g = new Fixture();
        g.Settings.MoneyErrors[g.SecondCurrency] = new SettingsPrerequisiteException(code);
        var candidate = await g.Handler().GetCurrenciesAsync(g.CurrenciesQuery);
        Assert.Equal((status, code), (candidate.StatusCode, candidate.Code));
        Assert.Null(candidate.Value);
    }

    [Fact]
    public async Task A_foreign_or_malformed_candidate_profile_cannot_be_offered()
    {
        var f = new Fixture();
        f.Settings.MoneyProfiles[f.SecondCurrency] = f.Settings.MoneyProfiles[f.SecondCurrency] with { CompanyId = Guid.NewGuid() };
        Assert.Equal(403, (await f.Handler().GetCurrenciesAsync(f.CurrenciesQuery)).StatusCode);

        var g = new Fixture();
        g.Settings.MoneyProfiles[g.SecondCurrency] = g.Settings.MoneyProfiles[g.SecondCurrency] with { CurrencyId = Guid.NewGuid() };
        var other = await g.Handler().GetCurrenciesAsync(g.CurrenciesQuery);
        Assert.Equal((503, "reference_provider_invalid"), (other.StatusCode, other.Code));

        var h = new Fixture();
        h.Settings.MoneyProfiles[h.SecondCurrency] = h.Settings.MoneyProfiles[h.SecondCurrency] with { CurrencyCode = " " };
        Assert.Equal(503, (await h.Handler().GetCurrenciesAsync(h.CurrenciesQuery)).StatusCode);
    }

    [Fact]
    public async Task Currencies_need_the_bound_operation_and_are_not_released_after_the_permission_is_lost()
    {
        var f = new Fixture();
        var unbound = await f.Handler(Fixture.Bindings with { MoneyOperation = null }).GetCurrenciesAsync(f.CurrenciesQuery);
        Assert.Equal((503, "settings_operation_unavailable"), (unbound.StatusCode, unbound.Code));
        Assert.Equal(0, f.Candidates.Calls + f.Settings.Calls);

        var g = new Fixture();
        g.Candidates.OnRead = () => g.Auth.Allowed = false;
        Assert.Equal(403, (await g.Handler().GetCurrenciesAsync(g.CurrenciesQuery)).StatusCode);
        Assert.Equal(0, g.Settings.Calls);

        var h = new Fixture();
        h.Settings.OnRead = () => h.Auth.Allowed = false;
        var lost = await h.Handler().GetCurrenciesAsync(h.CurrenciesQuery);
        Assert.Equal(403, lost.StatusCode);
        Assert.Null(lost.Value);

        var i = new Fixture();
        i.Settings.MoneyErrors[i.Currency] = new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.MoneyProfileNotFound);
        i.Settings.OnRead = () => i.Auth.Allowed = false;
        Assert.Equal(403, (await i.Handler().GetCurrenciesAsync(i.CurrenciesQuery)).StatusCode);
    }

    private sealed class Fixture
    {
        public static readonly SalesReferenceReadBindings Bindings = new(
            "controlled.accounts", "controlled.currency", "controlled.product", "controlled.operation");
        public Guid Company { get; } = Guid.NewGuid();
        public Guid Currency { get; } = Guid.NewGuid();
        public Guid Product { get; } = Guid.NewGuid();
        public Guid StockUnit { get; } = Guid.NewGuid();
        public Guid SalesUnit { get; } = Guid.NewGuid();
        public Guid Unit { get; } = Guid.NewGuid();
        public Guid Profile { get; } = Guid.NewGuid();
        public Guid OrderProfile { get; } = Guid.NewGuid();
        public Guid[] UnitIds => [StockUnit, SalesUnit, Unit];
        public DateOnly Day { get; } = new(2026, 9, 13);
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Auth Auth { get; } = new();
        public Accounts Accounts { get; } = new();
        public Settings Settings { get; } = new();
        public Products Products { get; } = new();
        // SALES-A2a: the Settings candidate reader and the Cari contact-information port.
        public Candidates Candidates { get; } = new();
        public ContactInformation ContactInformation { get; } = new();
        public Guid Party { get; } = Guid.NewGuid();
        public Guid Account { get; } = Guid.NewGuid();
        public Guid ActiveAddress { get; } = Guid.NewGuid();
        public Guid ActiveContact { get; } = Guid.NewGuid();
        public Guid SecondCurrency { get; } = Guid.NewGuid();
        public SalesCurrencyQueryV1 CurrencyQuery => new(Currency, Day);
        public SalesProductQueryV1 ProductQuery => new(Product, "7", Unit, Day);
        public SalesContactInformationQueryV1 ContactQuery => new(Party, Account);
        public SalesCurrencyListQueryV1 CurrenciesQuery => new(Day);
        public Fixture()
        {
            Context.Set(Company);
            User.Company = Company;
            var active = new SalesAccountSelectionAccountV1(Guid.NewGuid(), Company, 0, true, "9007199254740993");
            var foreign = active with { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid() };
            var passive = active with { Id = Guid.NewGuid(), IsActive = false };
            var party = new SalesAccountSelectionPartyV1(Guid.NewGuid(), "C001", 1,
                "Example Party", null, true, "1", [active, foreign, passive]);
            Accounts.Result = new(200, new([party, party with { IsActive = false }], null), null);
            Settings.Money = new(Company, Currency, "XYZ", "controlled.operation",
                new(Profile, 9007199254740993), 2, true, Day.AddDays(-1), null);
            // Three distinct units and profiles, so a lookup of the wrong binding cannot pass.
            var stock = AddUnit(StockUnit, "stock-unit", Guid.NewGuid(), 1);
            var sales = AddUnit(SalesUnit, "sales-unit", Guid.NewGuid(), 2);
            var order = AddUnit(Unit, "order-unit", OrderProfile, 3);
            Products.Result = new(new(Product, "7", "P001", "Example Product", stock, sales, order, Day), []);
            // SALES-A2a: two candidates of the company; the second has its own profile (in effect too).
            Candidates.List = [new(Currency, "XYZ", "XYZ - Example"), new(SecondCurrency, "ABC", "ABC - Second")];
            Settings.MoneyProfiles[SecondCurrency] = new(Company, SecondCurrency, "ABC", "controlled.operation",
                new(Guid.NewGuid(), 4), 2, true, Day.AddDays(-2), null);
            // One active and one inactive address and contact; the inactive ones must never be offered.
            ContactInformation.Answer = new(SalesPartyContactInformationOutcomeV1.Found, new SalesPartyContactInformationV1(
                Party, Account, Company, "9007199254740993",
                [
                    new(ActiveAddress, "3", true, "delivery", "TR", "Sanayi Cad. 1", null, "Kocaeli", "Gebze", "41400"),
                    new(Guid.NewGuid(), "1", false, "delivery", "TR", "Old street", null, null, null, null),
                ],
                [
                    new(ActiveContact, "2", true, "Ayşe Yükleme", "Sevkiyat", "delivery"),
                    new(Guid.NewGuid(), "5", false, "Former Person", null, "delivery"),
                ]));
        }
        public SalesReferenceReadHandler Handler(SalesReferenceReadBindings? bindings = null, bool productMissing = false,
            bool candidatesMissing = false, bool contactMissing = false) =>
            new(Context, User, Auth, Accounts, Settings, productMissing ? null : Products, bindings ?? Bindings,
                candidatesMissing ? null : Candidates, contactMissing ? null : ContactInformation);
        private ProductSalesUnitProfileV1 AddUnit(Guid unitId, string code, Guid profileId, long version)
        {
            Settings.Units[unitId] = new(Company, unitId, code, "owner-dimension", false,
                new(profileId, version), 3, true, Day.AddDays(-1), null);
            return new(Company, unitId, profileId, version.ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class Context : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId) => CompanyId = companyId;
    }
    private sealed class User : ICurrentUser
    {
        public Guid Company { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId => "actor";
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class Auth : IErpAuthorizationService
    {
        public bool Allowed { get; set; } = true;
        public int Calls { get; private set; }
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Allowed); }
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Accounts : ISalesAccountSelectionSourceV1
    {
        public int Calls { get; private set; }
        public Action? OnRead { get; set; }
        public SalesAccountSelectionRequestV1? Request { get; private set; }
        public SalesReferenceReadResult<SalesAccountSelectionPageV1> Result { get; set; } = new(403, null, null);
        public Task<SalesReferenceReadResult<SalesAccountSelectionPageV1>> GetPageAsync(SalesAccountSelectionRequestV1 request, CancellationToken cancellationToken = default)
        { Calls++; Request = request; OnRead?.Invoke(); return Task.FromResult(Result); }
    }
    private sealed class Settings : ISettingsPrerequisiteProvider
    {
        public List<(Guid UnitId, DateOnly EffectiveOn)> UnitCalls { get; } = [];
        public List<(Guid CurrencyId, string Operation, DateOnly EffectiveOn)> MoneyCalls { get; } = [];
        public int Calls => UnitCalls.Count + MoneyCalls.Count;
        public Action? OnRead { get; set; }
        public Exception? Error { get; set; }
        public MoneyProfileV1 Money { get; set; } = null!;
        public Dictionary<Guid, UnitProfileV1> Units { get; } = [];
        /// <summary>SALES-A2a: a per-currency money answer (or error) for the list read; a currency without one answers <see cref="Money"/>.</summary>
        public Dictionary<Guid, MoneyProfileV1> MoneyProfiles { get; } = [];
        public Dictionary<Guid, Exception> MoneyErrors { get; } = [];
        private void Read(CancellationToken token) { token.ThrowIfCancellationRequested(); OnRead?.Invoke(); if (Error is not null) throw Error; }
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            UnitCalls.Add((unitId, effectiveOn));
            Read(cancellationToken);
            // An unknown id fails the test instead of silently serving another unit's profile.
            return Units.TryGetValue(unitId, out var profile) ? Task.FromResult(profile)
                : throw new InvalidOperationException($"Unexpected unit profile read {unitId:D}.");
        }
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            MoneyCalls.Add((currencyId, operation, effectiveOn));
            Read(cancellationToken);
            if (MoneyErrors.TryGetValue(currencyId, out var error)) throw error;
            return Task.FromResult(MoneyProfiles.TryGetValue(currencyId, out var profile) ? profile : Money);
        }
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Products : IProductSalesEligibilityV1
    {
        public int Calls { get; private set; }
        public Action? OnRead { get; set; }
        public ProductSalesSelectionV1? Selection { get; private set; }
        public ProductSalesEligibilityV1 Result { get; set; } = new(null, []);
        public Exception? Error { get; set; }
        public Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection, CancellationToken cancellationToken)
        { Calls++; Selection = selection; OnRead?.Invoke(); if (Error is not null) throw Error; return Task.FromResult(Result); }
    }
    /// <summary>SALES-A2a: the Settings owner's candidate list, as given (the handler revalidates each).</summary>
    private sealed class Candidates : ISettingsMoneyCandidateReader
    {
        public int Calls { get; private set; }
        public Action? OnRead { get; set; }
        public Exception? Error { get; set; }
        public IReadOnlyList<MoneyCurrencyCandidateV1> List { get; set; } = [];
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default)
        { Calls++; OnRead?.Invoke(); if (Error is not null) throw Error; return Task.FromResult(List); }
    }
    /// <summary>SALES-A2a: the Cari owner's contact-information answer for the party account asked.</summary>
    private sealed class ContactInformation : ISalesPartyContactInformationPortV1
    {
        public int Calls { get; private set; }
        public Action? OnRead { get; set; }
        public Exception? Error { get; set; }
        public (Guid PartyId, Guid CompanyAccountId)? Asked { get; private set; }
        public SalesPartyContactInformationAnswerV1 Answer { get; set; } = new(SalesPartyContactInformationOutcomeV1.Unavailable, null);
        public Task<SalesPartyContactInformationAnswerV1> GetContactInformationAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default)
        { Calls++; Asked = (partyId, companyAccountId); OnRead?.Invoke(); if (Error is not null) throw Error; return Task.FromResult(Answer); }
    }
}
