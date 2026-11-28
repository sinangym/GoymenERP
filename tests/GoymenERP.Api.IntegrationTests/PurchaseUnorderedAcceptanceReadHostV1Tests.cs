using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

// Controlled owner/authority protocol; no SQL, physical-stock or normal-host acceptance claim.
public sealed class PurchaseUnorderedAcceptanceReadHostV1Tests
{
    private static readonly Guid CompanyId = Guid.NewGuid(), Acceptance = Guid.NewGuid(), Line = Guid.NewGuid();
    [Fact]
    public async Task Exact_owner_identity_uses_only_existing_companywide_orders_read()
    {
        var rig = new Rig(); var result = await rig.Read();
        Assert.Equal(rig.Source.Facts!.AcceptanceId, result.AcceptanceId); Assert.Equal(rig.Source.Facts.Evidence, result.Evidence); Assert.Equal((CompanyId, Acceptance, Line), Assert.Single(rig.Source.Reads));
        Assert.Equal(2, rig.Authority.Calls);
        Assert.All(rig.Authority.Permissions, p => Assert.Equal(PurchasingPermissionManifestV1.OrdersRead, p));
    }
    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("permission")] [InlineData("scope")]
    public async Task Late_source_completion_does_not_release_under_changed_context(string change)
    {
        var rig = new Rig(); rig.Source.After = () => rig.Change(change);
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => rig.Read())).Status);
        Assert.Single(rig.Source.Reads);
    }
    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("member")]
    public async Task Final_normal_completing_authority_callback_cannot_release_to_new_context(string change)
    {
        var rig = new Rig(); rig.Authority.After = call => { if (call == 2) rig.Change(change); };
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => rig.Read())).Status);
        Assert.Single(rig.Source.Reads);
    }
    [Fact]
    public async Task Completed_token_ignoring_callbacks_still_honor_cancellation()
    {
        var rig = new Rig(); using var cts = new CancellationTokenSource();
        rig.Authority.After = call => { if (call == 2) cts.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Read(cts.Token)); Assert.Single(rig.Source.Reads);
        var initial = new Rig(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initial.Read(cts.Token));
        Assert.Empty(initial.Source.Reads); Assert.Equal(0, initial.Authority.Calls);
    }
    [Theory]
    [InlineData("permission", 403)] [InlineData("scope", 403)] [InlineData("company", 403)]
    [InlineData("member", 403)] [InlineData("unauthenticated", 401)]
    public async Task Initial_refusal_has_no_owner_read(string change, int status)
    {
        var rig = new Rig(); rig.Change(change);
        Assert.Equal(status, (await Assert.ThrowsAsync<PurchasingServiceException>(() => rig.Read())).Status);
        Assert.Empty(rig.Source.Reads);
    }
    [Theory]
    [InlineData("missing", 404)] [InlineData("foreign", 404)] [InlineData("unavailable", 503)]
    [InlineData("null", 503)] [InlineData("identity", 503)] [InlineData("version", 503)]
    public async Task Missing_foreign_and_unreadable_owner_answers_are_closed(string defect, int status)
    {
        var rig = new Rig();
        if (defect == "missing") rig.Source.Outcome = PurchaseUnorderedAcceptanceSourceOutcomeV1.NotFound;
        if (defect == "unavailable") rig.Source.Outcome = PurchaseUnorderedAcceptanceSourceOutcomeV1.Unavailable;
        if (defect == "null") rig.Source.Facts = null;
        if (defect == "foreign") rig.Source.Facts = rig.Source.Facts! with { CompanyId = Guid.NewGuid() };
        if (defect == "identity") rig.Source.Facts = rig.Source.Facts! with { AcceptanceLineId = Guid.NewGuid() };
        if (defect == "version") rig.Source.Facts = rig.Source.Facts! with { Version = 0 };
        Assert.Equal(status, (await Assert.ThrowsAsync<PurchasingServiceException>(() => rig.Read())).Status);
        Assert.Single(rig.Source.Reads);
    }
    [Fact]
    public async Task Missing_port_and_authority_failure_are_unavailable_without_owner_fallback()
    {
        var rig = new Rig(); var host = new PurchaseUnorderedAcceptanceReadHostV1(rig.Company, rig.User, rig.Authority);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => host.ReadAsync(Acceptance, Line))).Status);
        rig.Authority.Fail = true;
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => rig.Read())).Status);
        Assert.Empty(rig.Source.Reads);
    }
    [Fact]
    public async Task Final_callback_cannot_mutate_the_released_control_evidence_collection()
    {
        var rig = new Rig(); var controls = new List<PurchaseAcceptanceDocumentEvidenceV1> { rig.Source.Facts!.Evidence };
        rig.Source.Facts = rig.Source.Facts with { ControlEvidence = controls };
        rig.Authority.After = call => { if (call == 2) controls.Clear(); };
        var result = await rig.Read(); Assert.Single(result.ControlEvidence); Assert.Empty(controls);
        Assert.Throws<NotSupportedException>(() => ((IList<PurchaseAcceptanceDocumentEvidenceV1>)result.ControlEvidence).Clear());
    }
    private sealed class Rig
    {
        public MutableCompany Company { get; } = new(); public User User { get; } = new();
        public Authority Authority { get; } = new(); public Owner Source { get; } = new();
        public Task<PurchaseUnorderedAcceptanceSourceV1> Read(CancellationToken ct = default) => new PurchaseUnorderedAcceptanceReadHostV1(Company, User, Authority, Source).ReadAsync(Acceptance, Line, ct);
        public void Change(string value)
        {
            if (value == "company") Company.Value = Guid.NewGuid();
            if (value == "actor") User.Actor = "SENTETIK-other";
            if (value == "member") User.Member = false;
            if (value == "unauthenticated") User.Authenticated = false;
            if (value == "permission") Authority.Granted = false;
            if (value == "scope") Authority.CompanyWide = false;
        }
    }
    private sealed class MutableCompany : ICompanyContext
    { public Guid Value { get; set; } = PurchaseUnorderedAcceptanceReadHostV1Tests.CompanyId; public Guid CompanyId => Value; public bool IsResolved => Value != Guid.Empty; public void Set(Guid id) => Value = id; }
    private sealed class User : ICurrentUser
    {
        public string Actor { get; set; } = "SENTETIK-reader"; public bool Member { get; set; } = true; public bool Authenticated { get; set; } = true;
        public string UserId => Actor; public bool IsAuthenticated => Authenticated;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { CompanyId }; public bool CanAccessCompany(Guid id) => Member && id == CompanyId;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true; public bool CompanyWide { get; set; } = true; public bool Fail { get; set; }
        public int Calls { get; private set; } public Action<int>? After { get; set; } public List<string> Permissions { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls++; Permissions.Add(permission); After?.Invoke(Calls); if (Fail) throw new InvalidOperationException("SENTETIK-unavailable");
            return Task.FromResult(new PermissionScope(Granted, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
    private sealed class Owner : IPurchaseUnorderedAcceptanceSourceV1
    {
        public PurchaseUnorderedAcceptanceSourceV1? Facts { get; set; } = new(CompanyId, Acceptance, Line, 1, Guid.NewGuid(), Guid.NewGuid(),
            new PurchaseUnorderedGoodsSubjectV1(Guid.NewGuid(), "7"), Guid.NewGuid(), "2", "2", new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero),
            new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-line"), []);
        public PurchaseUnorderedAcceptanceSourceOutcomeV1 Outcome { get; set; } = PurchaseUnorderedAcceptanceSourceOutcomeV1.Found;
        public Action? After { get; set; } public List<(Guid Company, Guid Acceptance, Guid Line)> Reads { get; } = [];
        public Task<PurchaseUnorderedAcceptanceSourceAnswerV1> ReadAsync(Guid companyId, Guid acceptanceId, Guid acceptanceLineId, CancellationToken cancellationToken = default)
        { Reads.Add((companyId, acceptanceId, acceptanceLineId)); After?.Invoke(); return Task.FromResult(new PurchaseUnorderedAcceptanceSourceAnswerV1(Outcome, Facts)); }
    }
}
