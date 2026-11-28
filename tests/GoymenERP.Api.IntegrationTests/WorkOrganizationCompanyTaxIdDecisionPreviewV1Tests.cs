using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.WorkInbox;
using static GoymenERP.Api.IntegrationTests.InventoryCostWorkDecisionHostTests;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkOrganizationCompanyTaxIdDecisionPreviewV1Tests
{
    [Theory]
    [InlineData(7L, 7L)] [InlineData(9007199254740993L, 9007199254740991L)] [InlineData(long.MaxValue, 1L)]
    public void Six_human_fields_and_ten_native_request_fields_keep_both_Int64_versions_as_canonical_text(long current, long requested)
    {
        var detail = OrganizationCompanyTaxIdWorkDecisionHostTests.Facts(current, requested);
        var human = WorkOrganizationCompanyTaxIdDecisionPreviewV1.FromNative(detail);
        var wire = JsonSerializer.SerializeToElement(human, Json);
        Assert.Equal(new[] { "companyId", "companyVersion", "companyVkn", "request", "companyVersionAtRequest", "isCurrent" }, wire.EnumerateObject().Select(x => x.Name));
        Assert.Equal(JsonValueKind.String, wire.GetProperty("companyVersion").ValueKind);
        Assert.Equal(current.ToString(CultureInfo.InvariantCulture), wire.GetProperty("companyVersion").GetString());
        Assert.Equal(requested.ToString(CultureInfo.InvariantCulture), wire.GetProperty("companyVersionAtRequest").GetString());
        Assert.Equal(new[] { "id", "currentVkn", "proposedVkn", "reason", "status", "requestedBy", "requestedAtUtc", "decidedBy", "decidedAtUtc", "decisionNote" },
            wire.GetProperty("request").EnumerateObject().Select(x => x.Name));
        Assert.Equal(JsonSerializer.Serialize(detail.Request, Json), wire.GetProperty("request").GetRawText());
        // The old native company screen still emits its real numeric Int64; only the Work projection is textual.
        var native = new CompanyTaxIdView(Company, "SENTETIK", "SENTETIK legal name", detail.CompanyVkn, false, current, detail.Request, null, new(false, true, false));
        var nativeWire = JsonSerializer.SerializeToElement(native, Json);
        Assert.Equal(JsonValueKind.Number, nativeWire.GetProperty("version").ValueKind); Assert.Equal(current, nativeWire.GetProperty("version").GetInt64());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Full_human_values_preserve_nullable_first_VKN_note_and_native_dates_without_chronology_defaults(bool first)
    {
        var d = OrganizationCompanyTaxIdWorkDecisionHostTests.Facts(99, 7, "rejected");
        d = d with { Request = d.Request with { CurrentVkn = first ? null : "9000000017", Reason = new string('x', 1000),
            RequestedAtUtc = DateTimeOffset.MinValue, DecidedAtUtc = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(3)), DecisionNote = "SENTETIK human note" } };
        var human = WorkOrganizationCompanyTaxIdDecisionPreviewV1.FromNative(d);
        Assert.Equal(d.Request, human.Request); Assert.Equal(d.Request.CurrentVkn, human.Request.CurrentVkn);
        Assert.Equal(d.Request.RequestedAtUtc, human.Request.RequestedAtUtc); Assert.Equal(d.Request.DecidedAtUtc, human.Request.DecidedAtUtc);
        Assert.Equal(1000, human.Request.Reason.Length); Assert.Equal("SENTETIK human note", human.Request.DecisionNote);
    }
    [Fact]
    public void Public_human_and_snapshot_JSON_never_emit_private_digests_receipt_or_source_carrier()
    {
        var d = OrganizationCompanyTaxIdWorkDecisionHostTests.Facts();
        var human = WorkOrganizationCompanyTaxIdDecisionPreviewV1.FromNative(d);
        var item = OrganizationCompanyTaxIdDecisionBindingV1.Item(Id, d.Request.ProposedVkn, d.Request.RequestedBy, d.Request.RequestedAtUtc, false, true)!;
        var proof = new WorkOrganizationCompanyTaxIdProofV1(Company, Id, 7, 7, d.Request.RequestedBy, d.Request.RequestedAtUtc,
            "pending", true, false, d.Request.ProposedVkn, d.SourceFactsDigest, d.PreviewDigest);
        var snapshot = new WorkApprovalDecisionSnapshotV1(Company, item, "7", true) { OrganizationCompanyTaxIdProof = proof };
        var preview = new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.OrganizationCompanyTaxId, Id, item.DocumentLabel, "7", true, false, [], item.Rules)
            { OrganizationCompanyTaxId = human };
        var publicJson = JsonSerializer.Serialize(preview, Json); var snapshotJson = JsonSerializer.Serialize(snapshot, Json);
        foreach (var privateName in new[] { "sourceFactsDigest", "previewDigest", "organizationCompanyTaxIdProof", "receipt", "payloadHash", "idempotencyKeyHash" })
        {
            Assert.DoesNotContain(privateName, publicJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(privateName, snapshotJson, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain(d.SourceFactsDigest, publicJson); Assert.DoesNotContain(d.PreviewDigest, publicJson);
        var other = preview with { Kind = WorkDecisionHostV1.SalesOrder, OrganizationCompanyTaxId = null };
        Assert.DoesNotContain("organizationCompanyTaxId", JsonSerializer.Serialize(other, Json));
    }
    [Theory]
    [InlineData(true, null)] [InlineData(false, null)] [InlineData(true, "  note  ")] [InlineData(false, "")]
    public void Four_field_Work_body_reuses_exact_native_hash_and_keeps_note_optional(bool approve, string? note)
    {
        var request = new WorkDecisionRequestV1("7", approve, note);
        var wire = JsonSerializer.SerializeToElement(request, Json); Assert.Equal(4, wire.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.String, wire.GetProperty("expectedVersion").ValueKind);
        Assert.Equal(note, wire.GetProperty("reason").ValueKind == JsonValueKind.Null ? null : wire.GetProperty("reason").GetString());
        var command = new DecideCompanyTaxIdChange(7, approve ? "approve" : "reject", note);
        var expected = IdempotencyHash.ComputePayload(new { CompanyId = Company, RequestId = Id, Decision = command.Decision,
            ExpectedVersion = 7L, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() });
        Assert.Equal(expected, OrganizationCompanyTaxIdDecisionBindingV1.DecisionPayloadHash(Company, Id, command));
        Assert.Equal(IdempotencyHash.ComputeKey("SENTETIK-key"), OrganizationCompanyTaxIdDecisionBindingV1.DecisionKeyHash("SENTETIK-key"));
        var nativeWire = JsonSerializer.SerializeToElement(command, Json); Assert.Equal(3, nativeWire.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Number, nativeWire.GetProperty("expectedVersion").ValueKind);
    }
}
