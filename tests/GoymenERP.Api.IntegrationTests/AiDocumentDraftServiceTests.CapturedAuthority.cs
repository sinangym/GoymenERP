using GoymenERP.Api.Ai.DocumentDrafts;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Multitenancy;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiDocumentDraftServiceTests
{
    // Controlled callbacks only: no provider, database or implicit rollback of an already-stored source.
    [Theory]
    [InlineData("store", "company")] [InlineData("ocr", "actor")] [InlineData("model", "logout")]
    [InlineData("counterparty", "member")] [InlineData("product", "company")]
    [InlineData("store", "actor")] [InlineData("ocr", "company")] [InlineData("model", "actor")]
    [InlineData("counterparty", "company")] [InlineData("product", "logout")]
    public async Task Captured_context_loss_at_each_owner_boundary_stops_before_the_next_owner(string stage, string drift)
    {
        var h = new Harness(); ConfigureBoundary(h, stage, () => Drift(h, drift));
        var result = await h.Service().DraftAsync(BoundaryRequest(stage));
        Assert.Equal(AiDocumentDraftStatusV1.Refused, result.Status); Assert.Null(result.Draft);
        Assert.Equal(Company, Assert.Single(h.Store.Stored).CompanyId); // already-stored bytes stay with the original owner/company
        if (stage == "store") Assert.Empty(h.Ocr.MediaTypes);
        if (stage is "store" or "ocr" or "model") Assert.Empty(h.Counterparties.Roles);
        if (stage != "product") Assert.Empty(h.Products.Asked);
    }
    [Theory]
    [InlineData("store")] [InlineData("ocr")] [InlineData("model")] [InlineData("counterparty")] [InlineData("product")]
    public async Task Completed_owner_callbacks_cannot_ignore_cancellation_or_revoked_owner_permission(string stage)
    {
        var revoked = new Harness(); ConfigureBoundary(revoked, stage, () => revoked.Authority.Revoke(ErpPermissions.PurchasingInvoicesImport));
        var denied = await revoked.Service().DraftAsync(BoundaryRequest(stage));
        Assert.Equal(AiDocumentDraftStatusV1.Refused, denied.Status); Assert.Null(denied.Draft);
        var cancelled = new Harness(); using var cts = new CancellationTokenSource(); ConfigureBoundary(cancelled, stage, cts.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.Service().DraftAsync(BoundaryRequest(stage), cts.Token));
        Assert.Single(cancelled.Store.Stored);
    }
    [Theory]
    [InlineData(1, "company")] [InlineData(2, "actor")] [InlineData(3, "logout")] [InlineData(3, "member")]
    public async Task Initial_permission_callback_cannot_rebind_the_captured_identity(int call, string drift)
    {
        var h = new Harness(); h.Authority.AfterCheck = (_, n) => { if (n == call) Drift(h, drift); };
        var result = await h.Service().DraftAsync(BoundaryRequest("store"));
        Assert.Equal(AiDocumentDraftStatusV1.Refused, result.Status); Assert.Null(result.Draft); Assert.Empty(h.Store.Stored);
        Assert.Empty(h.Ocr.MediaTypes); Assert.Empty(h.Products.Asked);
    }
    [Fact]
    public async Task Observed_A_to_B_before_a_later_A_callback_stops_without_reaching_that_callback()
    {
        var h = new Harness(); h.Store.OnStore = () => h.Context.Set(Guid.NewGuid());
        h.Counterparties.OnMatch = () => h.Context.Set(Company);
        var result = await h.Service().DraftAsync(BoundaryRequest("counterparty"));
        Assert.Equal(AiDocumentDraftStatusV1.Refused, result.Status); Assert.Empty(h.Counterparties.Roles);
        Assert.NotEqual(Company, h.Context.CompanyId); Assert.Single(h.Store.Stored);
        // ICompanyContext has no revision: a hidden A→B→A entirely inside one callback is not claimed detectable.
    }
    [Fact]
    public async Task Final_permission_callback_is_checked_after_normal_completion_and_authority_fault_is_unavailable()
    {
        var h = new Harness();
        // An input with no matchable product/tax ID reaches final permissions after initial/store/read boundaries (15 checks).
        h.Authority.AfterCheck = (_, call) => { if (call == 18) h.User.ActorId = Guid.NewGuid().ToString("D"); };
        var result = await h.Service().DraftAsync(Text(AiDocumentDraftKindsV1.SalesOrder, "Sipariş No: SENTETIK-1", AiDocumentTextSourcesV1.Pasted));
        Assert.Equal(18, h.Authority.Calls); Assert.Equal(AiDocumentDraftStatusV1.Refused, result.Status); Assert.Null(result.Draft);
        var cancelled = new Harness(); using var finalCancellation = new CancellationTokenSource();
        cancelled.Authority.AfterCheck = (_, call) => { if (call == 18) finalCancellation.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.Service().DraftAsync(
            Text(AiDocumentDraftKindsV1.SalesOrder, "Sipariş No: SENTETIK-1", AiDocumentTextSourcesV1.Pasted), finalCancellation.Token));
        Assert.Equal(18, cancelled.Authority.Calls);
        var unavailable = new Harness(); unavailable.Authority.Fail = true;
        var failed = await unavailable.Service().DraftAsync(BoundaryRequest("store"));
        Assert.Equal(AiDocumentDraftStatusV1.Unavailable, failed.Status); Assert.Empty(unavailable.Store.Stored);
        var lateUnavailable = new Harness(); lateUnavailable.Store.OnStore = () => lateUnavailable.Authority.Fail = true;
        var lateFailure = await lateUnavailable.Service().DraftAsync(BoundaryRequest("store"));
        Assert.Equal(AiDocumentDraftStatusV1.Unavailable, lateFailure.Status); Assert.Null(lateFailure.Draft);
        Assert.Equal(Company, Assert.Single(lateUnavailable.Store.Stored).CompanyId); Assert.Empty(lateUnavailable.Ocr.MediaTypes);
        var switched = new Harness(); switched.Authority.AfterCheck = (_, _) => switched.Context.Set(Guid.NewGuid()); switched.Authority.Fail = true;
        Assert.Equal(AiDocumentDraftStatusV1.Refused, (await switched.Service().DraftAsync(BoundaryRequest("store"))).Status);
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new Harness().Service().DraftAsync(BoundaryRequest("store"), stopped.Token));
    }
    private static AiDocumentDraftRequestV1 BoundaryRequest(string stage) => stage == "ocr"
        ? File(AiDocumentDraftKindsV1.IncomingInvoice, AiDocumentMediaTypesV1.Png, [1, 2, 3])
        : stage == "model" ? Text(AiDocumentDraftKindsV1.IncomingInvoice, "Fatura No: SENTETIK-1", AiDocumentTextSourcesV1.Pasted)
        : File(AiDocumentDraftKindsV1.IncomingInvoice, AiDocumentMediaTypesV1.Xml, UblInvoice());
    private static void ConfigureBoundary(Harness h, string stage, Action change)
    {
        if (stage == "store") h.Store.OnStore = change;
        if (stage == "ocr") { h.Ocr.Text = "Fatura No: SENTETIK-1"; h.Ocr.OnRead = change; }
        if (stage == "model") { h.Options.LocalModelEnabled = true; h.LocalModel.Available = true; h.LocalModel.OnSuggest = change; }
        if (stage == "counterparty") h.Counterparties.OnMatch = change;
        if (stage == "product") h.Products.OnAsk = change;
    }
    // Production CompanyContext already refuses reassignment. This controlled interface implementation
    // exercises misbehaving external callbacks without adding a production revision/epoch protocol.
    private sealed class CapturedTestCompany : ICompanyContext
    {
        public Guid CompanyId { get; private set; }
        public bool IsResolved => CompanyId != Guid.Empty;
        public void Set(Guid value) => CompanyId = value;
    }
    private static void Drift(Harness h, string drift)
    {
        if (drift == "company") h.Context.Set(Guid.NewGuid());
        if (drift == "actor") h.User.ActorId = Guid.NewGuid().ToString("D");
        if (drift == "logout") h.User.Authenticated = false;
        if (drift == "member") h.User.Member = false;
    }
}
