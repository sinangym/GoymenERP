using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using FixedAuthority = GoymenERP.Finance.UnitTests.FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceOfficialSalesInvoiceServiceV1Tests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Work_source_preserves_native_item_header_and_full_recorded_digest_but_never_serializes_human_facts(bool terminal)
    {
        var fixture = Fixture.Create(); await Record(fixture); fixture.Db.ChangeTracker.Clear();
        var owner = fixture.Service("checker", new Documents());
        if (terminal) Assert.Equal(CommandOutcome.Succeeded, (await owner.RejectAsync(Context("checker"), new("source-reject"), new(Invoice, "1", Reason), default)).Outcome);
        var authority = new FixedAuthority(true, true); var user = new PurposeUser(); var company = new PurposeCompany(Company);
        var source = new FinanceOfficialSalesInvoiceWorkInboxSourceV1(fixture.Db, new(company, user, authority), owner);
        var snapshot = await source.ReadDecisionAsync(new(Company, "checker", "source", null), Invoice, default);
        Assert.NotNull(snapshot); Assert.Equal(!terminal, snapshot.Pending); Assert.Equal(terminal ? "2" : "1", snapshot.SourceVersion);
        Assert.Equal("/finans/resmi-satis-faturasi-onaylari", snapshot.Item.Link);
        Assert.Equal("maker", snapshot.Item.PreparedBy); Assert.Equal(Request().OfficialNo, snapshot.Item.DocumentLabel);
        Assert.Equal(3, snapshot.Item.References.Count); Assert.Equal("1250.5", Assert.Single(snapshot.Item.Rules).Actual);
        var header = (await owner.GetAsync(Context("checker"), Invoice, default)).Resource!;
        Assert.True(FinanceOfficialSalesInvoiceServiceV1.ValidDecisionView(Company, Invoice, header));
        Assert.Equal(FinanceOfficialSalesInvoiceServiceV1.RecordedFactsDigest(header), snapshot.OfficialSalesInvoiceProof!.RecordedFactsDigest);
        Assert.Equal(FinanceOfficialSalesInvoiceServiceV1.DecisionPreviewDigest(header), snapshot.OfficialSalesInvoiceProof.PreviewDigest);
        var wire = JsonSerializer.Serialize(snapshot, FinanceJson.CreateOptions());
        Assert.DoesNotContain("officialSalesInvoiceProof", wire); Assert.DoesNotContain("payableFunctionalAmount", wire); Assert.DoesNotContain("pdfSha256", wire);
        Assert.All(authority.Asked, request => Assert.Equal((FinanceOfficialSalesInvoicePermissionNamesV1.Approve, Company), request));
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Work_source_requires_the_real_decision_read_interface_and_current_companywide_approval()
    {
        var fixture = Fixture.Create(); await Record(fixture); fixture.Db.ChangeTracker.Clear();
        var company = new PurposeCompany(Company); var user = new PurposeUser(); var context = new WorkInboxSourceContextV1(Company, "checker", "source", null);
        var missing = new FinanceOfficialSalesInvoiceWorkInboxSourceV1(fixture.Db, new(company, user, new FixedAuthority(true, true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => missing.ReadDecisionAsync(context, Invoice, default));
        foreach (var permission in new[] { new FixedAuthority(false, true), new FixedAuthority(true, false) })
        {
            var source = new FinanceOfficialSalesInvoiceWorkInboxSourceV1(fixture.Db, new(company, user, permission), fixture.Service("checker", new Documents()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(context, Invoice, default));
        }
    }

    [Theory]
    [InlineData("actual")] [InlineData("threshold")] [InlineData("unit")] [InlineData("deviation")]
    public async Task Decision_rule_matcher_reuses_the_native_canonical_amount_and_all_null_rule_facts(string fault)
    {
        var fixture = Fixture.Create(); await Record(fixture);
        var view = (await fixture.Service("checker", new Documents()).GetAsync(Context("checker"), Invoice, default)).Resource!;
        var rule = new WorkApprovalRuleResultV1(FinanceOfficialSalesInvoiceWorkInboxSourceV1.ApprovalRequiredRule, null, "1250.5", null, null);
        Assert.True(FinanceOfficialSalesInvoiceServiceV1.MatchesDecisionRule(view, [rule]));
        var changed = fault switch { "actual" => rule with { Actual = "999" }, "threshold" => rule with { Threshold = "1" },
            "unit" => rule with { Unit = "kg" }, _ => rule with { Deviation = "1" } };
        Assert.False(FinanceOfficialSalesInvoiceServiceV1.MatchesDecisionRule(view, [changed]));
    }

    [Fact]
    public async Task Recorded_digest_keeps_all_native_recorded_facts_separate_from_decision_and_dynamic_import_warning_display()
    {
        var fixture = Fixture.Create(); await Record(fixture);
        var v = (await fixture.Service("checker", new Documents()).GetAsync(Context("checker"), Invoice, default)).Resource!;
        var digest = FinanceOfficialSalesInvoiceServiceV1.RecordedFactsDigest(v);
        var dynamic = v with { Status = FinanceOfficialSalesInvoiceStatusV1.Rejected, Version = "2", RejectedBy = "checker", RejectedAtUtc = DateTimeOffset.UtcNow,
            RejectionReason = Reason, SourceDifferences = [new(Guid.NewGuid(), Guid.NewGuid(), "SENTETIK", Guid.NewGuid().ToString("D"), ["amount"], null, "9.00", "manual")],
            TaxWarning = new("SENTETIK", null, "9", "8", "1") };
        Assert.Equal(digest, FinanceOfficialSalesInvoiceServiceV1.RecordedFactsDigest(dynamic));
        Assert.NotEqual(FinanceOfficialSalesInvoiceServiceV1.DecisionPreviewDigest(v), FinanceOfficialSalesInvoiceServiceV1.DecisionPreviewDigest(dynamic));
        foreach (var changed in new[] { v with { OfficialNo = "OTHER" }, v with { Ettn = Guid.NewGuid().ToString("D") }, v with { InvoiceDate = new(2026, 11, 21) },
            v with { CustomerPartyId = Guid.NewGuid() }, v with { CurrencyId = Guid.NewGuid() }, v with { PayableAmount = "2" }, v with { PayableFunctionalAmount = "2" },
            v with { TaxBaseFunctionalAmount = "2" }, v with { VatFunctionalAmount = "2" }, v with { WithholdingFunctionalAmount = "2" },
            v with { PdfDocumentId = Guid.NewGuid() }, v with { PdfDocumentVersionNo = 2 }, v with { PdfSha256 = new string('b', 64) },
            v with { RecordedBy = "other" }, v with { RecordedAtUtc = v.RecordedAtUtc.AddSeconds(1) } })
            Assert.NotEqual(digest, FinanceOfficialSalesInvoiceServiceV1.RecordedFactsDigest(changed));
    }
}
