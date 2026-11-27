using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

// Projection contracts only; neither actual HTTP nor SQL acceptance.
public sealed class WorkPostingAccountMappingDecisionPreviewV1Tests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Company = Guid.Parse("ac520000-0000-8000-8000-000000000001");
    private static readonly Guid Maker = Guid.Parse("ac520000-0000-8000-8000-000000000002");
    private static readonly Guid Checker = Guid.Parse("ac520000-0000-8000-8000-000000000003");
    private static readonly DateTimeOffset PreparedAt = DateTimeOffset.UnixEpoch.AddTicks(1234567);

    [Theory]
    [InlineData(1L)]
    [InlineData(9007199254740993L)]
    [InlineData(long.MaxValue)]
    public void Native_identity_and_both_independent_versions_cross_Work_as_exact_text(long version)
    {
        var native = Detail(version, version - 1, version - 1);
        var before = JsonSerializer.Serialize(native, Json);
        var work = WorkPostingAccountMappingPreviewV1.FromNative(native);
        var wire = JsonSerializer.SerializeToElement(work, Json);
        Assert.Equal(version.ToString(CultureInfo.InvariantCulture), wire.GetProperty("mapping").GetProperty("mappingVersion").GetString());
        Assert.Equal((version - 1).ToString(CultureInfo.InvariantCulture), wire.GetProperty("mapping").GetProperty("version").GetString());
        Assert.Equal((version - 1).ToString(CultureInfo.InvariantCulture), wire.GetProperty("expectedVersion").GetString());
        Assert.Equal((version - 1).ToString(CultureInfo.InvariantCulture), wire.GetProperty("expectedActiveMappingVersion").GetString());
        Assert.Equal(before, JsonSerializer.Serialize(native, Json));
        var nativeWire = JsonSerializer.SerializeToElement(native, Json);
        Assert.Equal(version, nativeWire.GetProperty("mapping").GetProperty("mappingVersion").GetInt64());
        Assert.Equal(version - 1, nativeWire.GetProperty("mapping").GetProperty("version").GetInt64());
        Assert.Equal(version - 1, nativeWire.GetProperty("expectedActiveMappingVersion").GetInt64());
    }

    [Fact]
    public void Full_mapping_rows_and_distinct_prepared_and_current_plan_are_reused_without_private_receipt()
    {
        var native = Detail(9007199254740993, 2, 1);
        var receipt = new AccountingPostingAccountMappingActivationReceiptV1(Company, native.Mapping.MappingVersion,
            AccountingPostingAccountMappingCommandNamesV1.Activate, new string('A', 64), new string('B', 64), Checker,
            1, 0, 2, "SENTETIK-private-original-correlation", PreparedAt.AddMinutes(5), new string('C', 64), new string('D', 64));
        native = native with { Mapping = native.Mapping with { ActivationReceipt = receipt } };
        var work = WorkPostingAccountMappingPreviewV1.FromNative(native);
        Assert.Same(native.Mapping.Rows, work.Mapping.Rows);
        Assert.Same(native.PreparedAccountPlan, work.PreparedAccountPlan);
        Assert.Same(native.CurrentActiveAccountPlan, work.CurrentActiveAccountPlan);
        Assert.NotEqual(work.PreparedAccountPlan, work.CurrentActiveAccountPlan.Reference);
        var wire = JsonSerializer.SerializeToElement(work, Json);
        Assert.Equal(7, wire.EnumerateObject().Count());
        Assert.Equal(12, wire.GetProperty("mapping").EnumerateObject().Count());
        Assert.Equal(13, wire.GetProperty("mapping").GetProperty("rows").GetArrayLength());
        Assert.All(wire.GetProperty("mapping").GetProperty("rows").EnumerateArray(), row => Assert.Equal(8, row.EnumerateObject().Count()));
        var expected = JsonSerializer.SerializeToNode(native.Mapping, Json)!.AsObject();
        expected["mappingVersion"] = native.Mapping.MappingVersion.ToString(CultureInfo.InvariantCulture);
        expected["version"] = "2";
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(work.Mapping, Json)));
        Assert.Equal(PreparedAt, work.Mapping.PreparedAtUtc);
        Assert.DoesNotContain("activationReceipt", wire.GetRawText());
        Assert.DoesNotContain(receipt.OriginalCorrelationId, wire.GetRawText());
        var result = new AccountingApiResult<AccountingPostingAccountMappingViewV1>(AccountingApiStatus.Ok, native.Mapping, false, null);
        var nativeWire = JsonSerializer.SerializeToElement(result, Json);
        Assert.Equal(8, nativeWire.EnumerateObject().Count());
        Assert.Equal(12, nativeWire.GetProperty("resource").EnumerateObject().Count());
        Assert.Equal(9007199254740993L, nativeWire.GetProperty("resource").GetProperty("mappingVersion").GetInt64());
        Assert.Same(receipt, native.Mapping.ActivationReceipt);
    }

    [Fact]
    public void Prepared_null_activation_metadata_and_zero_active_version_are_not_defaulted_or_omitted()
    {
        var native = Detail(1, 1, 0);
        native = native with { Mapping = native.Mapping with { Status = "Prepared", ActivatedByUserId = null, ActivatedAtUtc = null } };
        var work = WorkPostingAccountMappingPreviewV1.FromNative(native);
        Assert.Equal("0", work.ExpectedActiveMappingVersion);
        var header = JsonSerializer.SerializeToElement(work.Mapping, Json);
        Assert.Equal(JsonValueKind.Null, header.GetProperty("activatedByUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, header.GetProperty("activatedAtUtc").ValueKind);
        Assert.Equal(12, header.EnumerateObject().Count());
        var selection = JsonSerializer.SerializeToElement(new WorkPostingAccountMappingSelectionV1("9223372036854775807"), Json);
        var only = Assert.Single(selection.EnumerateObject());
        Assert.Equal("expectedActiveMappingVersion", only.Name);
        Assert.Equal("9223372036854775807", only.Value.GetString());
    }

    private static AccountingPostingAccountMappingDecisionDetailV1 Detail(long identity, long version, long active)
    {
        var rows = Enumerable.Range(1, 13).Select(index => new AccountingPostingAccountMappingRowViewV1(
            "SENTETIK-purpose", "SENTETIK-key-" + index.ToString(CultureInfo.InvariantCulture), index % 2 == 0 ? "TradeGoods" : null,
            index % 3 == 0 ? "Production" : null, index % 2 == 0, Guid.NewGuid(), "SENTETIK-account-" + index.ToString(CultureInfo.InvariantCulture),
            index % 2 == 0 ? "SENTETIK-vat" : null)).ToArray();
        var mapping = new AccountingPostingAccountMappingViewV1(Company, identity, new DateOnly(2026, 11, 27), "Active", version,
            "SENTETIK-prepared-plan", new string('E', 64), Maker, PreparedAt, Checker, PreparedAt.AddMinutes(5), rows);
        var plan = new AccountingPostingMappingDecisionPlanV1(Company, new("SENTETIK-current-plan", new string('F', 64)),
            rows.Select(row => new AccountingPostingMappingAccountV1(row.AccountId, row.AccountCode, "SENTETIK account name")).ToArray());
        return new(mapping, new(mapping.AccountPlanSourceVersion, mapping.AccountPlanSourceSha256), plan, version, active,
            new string('C', 64), new string('D', 64));
    }
}
