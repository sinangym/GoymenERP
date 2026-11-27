using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

// Pure projection/serialization contracts, not actual HTTP, authority or SQL acceptance.
public sealed class WorkApprovedSourceCatalogDecisionPreviewV1Tests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Company = Guid.Parse("ac510000-0000-4000-8000-000000000001"),
        PackageId = Guid.Parse("ac510000-0000-4000-8000-000000000002"),
        Maker = Guid.Parse("ac510000-0000-4000-8000-000000000003"),
        Checker = Guid.Parse("ac510000-0000-4000-8000-000000000004"),
        Currency = Guid.Parse("ac510000-0000-4000-8000-000000000005");
    private static readonly DateTimeOffset PreparedAt = new DateTimeOffset(2026, 11, 27, 9, 10, 11, TimeSpan.Zero).AddTicks(1234567);
    private static readonly DateTimeOffset ActivatedAt = PreparedAt.AddMinutes(5).AddTicks(3);

    [Theory]
    [InlineData(1L)]
    [InlineData(9007199254740993L)]
    [InlineData(long.MaxValue)]
    public void All_long_versions_cross_Work_transport_as_exact_invariant_text_without_changing_native_numbers(long version)
    {
        var native = Detail(version, version, version, version);
        var nativeBefore = JsonSerializer.Serialize(native, Json);
        var work = WorkApprovedSourceCatalogPreviewV1.FromNative(native);
        var wire = JsonSerializer.SerializeToElement(work, Json);
        var expected = version.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(expected, wire.GetProperty("catalog").GetProperty("version").GetString());
        Assert.Equal(expected, wire.GetProperty("catalog").GetProperty("catalogVersion").GetString());
        Assert.Equal(expected, wire.GetProperty("expectedPackageVersion").GetString());
        Assert.Equal(expected, wire.GetProperty("expectedActiveCatalogVersion").GetString());
        var nativeWire = JsonSerializer.SerializeToElement(native, Json);
        Assert.Equal(version, nativeWire.GetProperty("catalog").GetProperty("version").GetInt64());
        Assert.Equal(version, nativeWire.GetProperty("catalog").GetProperty("catalogVersion").GetInt64());
        Assert.Equal(version, nativeWire.GetProperty("expectedPackageVersion").GetInt64());
        Assert.Equal(version, nativeWire.GetProperty("expectedActiveCatalogVersion").GetInt64());
        Assert.Equal(nativeBefore, JsonSerializer.Serialize(native, Json));
    }

    [Fact]
    public void Prepared_null_catalog_version_and_initial_active_zero_are_preserved_without_defaults()
    {
        var native = Detail(1, null, 1, 0);
        native = native with { Catalog = native.Catalog with { Status = "Prepared", ActivatedByUserId = null, ActivatedAtUtc = null } };
        var work = WorkApprovedSourceCatalogPreviewV1.FromNative(native);
        Assert.Null(work.Catalog.CatalogVersion);
        Assert.Null(work.Catalog.ActivatedByUserId);
        Assert.Null(work.Catalog.ActivatedAtUtc);
        Assert.Equal("0", work.ExpectedActiveCatalogVersion);
        var header = JsonSerializer.SerializeToElement(work.Catalog, Json);
        Assert.Equal(15, header.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Null, header.GetProperty("catalogVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, header.GetProperty("activatedAtUtc").ValueKind);
    }

    [Fact]
    public void Human_wire_has_exact_six_detail_and_fifteen_header_fields_and_preserves_dates_and_native_receipt_privacy()
    {
        var native = Detail(2, 1, 1, 0);
        var receipt = new ApprovedAccountingSourceCatalogActivationReceiptV1(Company, PackageId,
            ApprovedAccountingSourceCatalogCommandNamesV1.Activate, new string('A', 64), new string('B', 64), Checker,
            1, 0, 2, 1, "SENTETIK-private-receipt-correlation", ActivatedAt, new string('C', 64), new string('D', 64));
        native = native with { Catalog = native.Catalog with { ActivationReceipt = receipt } };
        var work = WorkApprovedSourceCatalogPreviewV1.FromNative(native);
        var wire = JsonSerializer.SerializeToElement(work, Json);
        Assert.Equal(new[] { "catalog", "expectedActiveCatalogVersion", "expectedPackageVersion", "package", "previewDigest", "sourceFactsDigest" },
            wire.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "accountCount", "accountPlanSourceVersion", "accountsSourceSha256", "activatedAtUtc", "activatedByUserId", "bindingCount", "catalogVersion", "companyId", "packageId", "postingMappingsSourceSha256", "preparedAtUtc", "preparedByUserId", "status", "templateVersion", "version" },
            wire.GetProperty("catalog").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        var expectedHeader = JsonSerializer.SerializeToNode(native.Catalog, Json)!.AsObject();
        expectedHeader["version"] = "2"; expectedHeader["catalogVersion"] = "1";
        Assert.True(JsonNode.DeepEquals(expectedHeader, JsonSerializer.SerializeToNode(work.Catalog, Json)));
        Assert.Equal(PreparedAt, work.Catalog.PreparedAtUtc);
        Assert.Equal(ActivatedAt, work.Catalog.ActivatedAtUtc);
        Assert.Equal(native.SourceFactsDigest, work.SourceFactsDigest);
        Assert.Equal(native.PreviewDigest, work.PreviewDigest);
        Assert.DoesNotContain("activationReceipt", wire.GetRawText());
        Assert.DoesNotContain(receipt.OriginalCorrelationId, wire.GetRawText());
        var nativeResult = new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(AccountingApiStatus.Ok, native.Catalog, false, null);
        var resultWire = JsonSerializer.SerializeToElement(nativeResult, Json);
        Assert.Equal(8, resultWire.EnumerateObject().Count());
        Assert.Equal(15, resultWire.GetProperty("resource").EnumerateObject().Count());
        Assert.Equal(2L, resultWire.GetProperty("resource").GetProperty("version").GetInt64());
        Assert.False(resultWire.GetProperty("resource").TryGetProperty("activationReceipt", out _));
        Assert.Same(receipt, native.Catalog.ActivationReceipt);
    }

    [Fact]
    public void Full_account_and_binding_rows_are_reused_once_without_truncation_or_nested_mirror_copies()
    {
        var native = Detail(2, 1, 1, 0, 13);
        var work = WorkApprovedSourceCatalogPreviewV1.FromNative(native);
        Assert.Same(native.Package, work.Package);
        Assert.Equal(13, work.Package.Accounts.Count);
        Assert.Equal(13, work.Package.Bindings.Count);
        var wire = JsonSerializer.SerializeToElement(work.Package, Json);
        Assert.Equal(new[] { "accounts", "bindings" }, wire.EnumerateObject().Select(property => property.Name));
        Assert.Equal(13, wire.GetProperty("accounts").GetArrayLength());
        Assert.Equal(13, wire.GetProperty("bindings").GetArrayLength());
        Assert.All(wire.GetProperty("bindings").EnumerateArray(), binding =>
        {
            Assert.Equal(2, binding.GetProperty("mappings").GetArrayLength());
            Assert.False(binding.TryGetProperty("mirror", out _));
            Assert.False(binding.TryGetProperty("accounts", out _));
        });
        Assert.Equal(JsonSerializer.Serialize(native.Package, Json), wire.GetRawText());
    }

    [Fact]
    public void Selection_wire_contains_only_the_explicit_frozen_active_version_text()
    {
        var selection = new WorkApprovedSourceCatalogSelectionV1("9223372036854775807");
        var wire = JsonSerializer.SerializeToElement(selection, Json);
        var property = Assert.Single(wire.EnumerateObject());
        Assert.Equal("expectedActiveCatalogVersion", property.Name);
        Assert.Equal("9223372036854775807", property.Value.GetString());
    }

    private static ApprovedAccountingSourceCatalogDecisionDetailV1 Detail(long version, long? catalogVersion,
        long expectedPackageVersion, long expectedActiveVersion, int count = 2)
    {
        var accounts = Enumerable.Range(1, count).Select(index => new ApprovedAccountingSourceCatalogDecisionAccountV1(
            Guid.NewGuid(), "SENTETIK-" + index.ToString(CultureInfo.InvariantCulture), "Sentetik hesap " + index.ToString(CultureInfo.InvariantCulture))).ToArray();
        var bindings = Enumerable.Range(1, count).Select(index => new ApprovedAccountingSourceCatalogDecisionBindingV1(
            Guid.NewGuid(), NormalJournalSourceKindsV1.RecordedManualReceipt, "source-0001", "SENTETIK-profile", "profile-0001", Currency,
            "SENTETIK-posting-purpose", "rule-0001", [new("SENTETIK-debit", "Debit", accounts[0].AccountId), new("SENTETIK-credit", "Credit", accounts[1].AccountId)])).ToArray();
        var header = new ApprovedAccountingSourceCatalogViewV1(PackageId, Company, "Active", version, catalogVersion,
            ApprovedAccountingSourcePackageTemplateV1.Version, "account-plan-0001", new string('E', 64), new string('F', 64), Maker, PreparedAt, Checker, ActivatedAt,
            accounts.Length, bindings.Length);
        return new(header, new(accounts, bindings), expectedPackageVersion, expectedActiveVersion, new string('C', 64), new string('D', 64));
    }
}
