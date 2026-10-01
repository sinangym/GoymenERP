using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Infrastructure;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Accounting.UnitTests;

public sealed class FinanceManualReceiptNormalJournalSourceProviderV1Tests
{
    private static readonly Guid Company = Guid.Parse("85000000-0000-0000-0000-000000000001");
    private static readonly Guid Actor = Guid.Parse("85000000-0000-0000-0000-000000000002");
    private static readonly Guid Source = Guid.Parse("85000000-0000-0000-0000-000000000003");
    private static readonly Guid OtherSource = Guid.Parse("85000000-0000-0000-0000-00000000000a");
    private static readonly Guid Currency = Guid.Parse("85000000-0000-0000-0000-000000000004");
    private static readonly Guid Debit = Guid.Parse("85000000-0000-0000-0000-000000000005");
    private static readonly Guid Credit = Guid.Parse("85000000-0000-0000-0000-000000000006");

    [Fact]
    public async Task Recorded_visible_exact_binding_rechecks_both_permissions_and_releases_rule()
    {
        var receipt = new FixedReceipt(Detail());
        var permissions = new SequencePermissions(PermissionDecision.Allowed, PermissionDecision.Allowed);
        var rule = Rule();
        var provider = Provider(receipt, permissions, Catalog(rule));

        var resolved = await provider.ResolveAsync(Company, Source);

        Assert.NotNull(resolved);
        Assert.Same(rule, resolved!.Rule);
        Assert.Equal(Currency, resolved.CurrencyId);
        Assert.Equal([FinanceAction.ReadAmount, FinanceAction.ReadSources], permissions.Actions);
        Assert.Equal(Company, receipt.Context!.CompanyId);
        Assert.Equal(Actor.ToString("D"), receipt.Context.ActorId);
        Assert.Equal("acc-source-test", receipt.Context.CorrelationId);
        Assert.Equal(Source, receipt.Request!.SourceId);
    }

    [Theory]
    [InlineData(SourceStage.Draft, AmountDisclosure.Visible)]
    [InlineData(SourceStage.Reversed, AmountDisclosure.Visible)]
    [InlineData(SourceStage.Recorded, AmountDisclosure.Redacted)]
    [InlineData(SourceStage.Recorded, AmountDisclosure.Unavailable)]
    public async Task Non_recorded_or_hidden_source_fails_closed(
        SourceStage stage, AmountDisclosure disclosure)
    {
        var provider = Provider(
            new FixedReceipt(Detail(stage, disclosure)),
            new SequencePermissions(PermissionDecision.Allowed, PermissionDecision.Allowed),
            Catalog(Rule()));

        Assert.Null(await provider.ResolveAsync(Company, Source));
    }

    [Fact]
    public async Task Reversing_source_fails_closed()
    {
        var provider = Provider(
            new FixedReceipt(Detail(reversesSourceId: Guid.NewGuid())),
            new SequencePermissions(PermissionDecision.Allowed, PermissionDecision.Allowed),
            Catalog(Rule()));

        Assert.Null(await provider.ResolveAsync(Company, Source));
    }

    [Fact]
    public async Task Missing_exact_source_version_binding_fails_closed()
    {
        var rule = Rule();
        var mismatched = new ApprovedNormalJournalSourceBindingCatalogV1(
        [
            Binding(rule) with { SourceVersion = "3" },
        ]);
        var provider = Provider(new FixedReceipt(Detail()),
            new SequencePermissions(PermissionDecision.Allowed, PermissionDecision.Allowed), mismatched);

        Assert.Null(await provider.ResolveAsync(Company, Source));
    }

    [Fact]
    public async Task Same_version_profile_and_currency_cannot_reuse_another_sources_binding()
    {
        var provider = Provider(
            new FixedReceipt(Detail(sourceId: OtherSource)),
            new SequencePermissions(PermissionDecision.Allowed, PermissionDecision.Allowed),
            Catalog(Rule()));

        Assert.Null(await provider.ResolveAsync(Company, OtherSource));
    }

    [Fact]
    public async Task Different_sources_can_resolve_different_explicitly_approved_rules()
    {
        var first = Rule();
        var second = Rule("synthetic-purpose-2", "synthetic-rule-v2");
        var catalog = new ApprovedNormalJournalSourceBindingCatalogV1(
        [
            Binding(first),
            Binding(second, OtherSource),
        ]);
        var provider = Provider(
            new FixedReceipt(Detail(sourceId: OtherSource)),
            new SequencePermissions(PermissionDecision.Allowed, PermissionDecision.Allowed),
            catalog);

        var resolved = await provider.ResolveAsync(Company, OtherSource);

        Assert.NotNull(resolved);
        Assert.Same(second, resolved!.Rule);
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, PermissionDecision.Allowed)]
    [InlineData(PermissionDecision.Allowed, PermissionDecision.Denied)]
    [InlineData(PermissionDecision.Unavailable, PermissionDecision.Allowed)]
    public async Task Fresh_permission_refusal_releases_nothing(
        PermissionDecision amount, PermissionDecision read)
    {
        var provider = Provider(new FixedReceipt(Detail()),
            new SequencePermissions(amount, read), Catalog(Rule()));

        Assert.Null(await provider.ResolveAsync(Company, Source));
    }

    [Fact]
    public void Catalog_rejects_duplicate_exact_binding()
    {
        var binding = Binding(Rule());

        Assert.Throws<ArgumentException>(() =>
            new ApprovedNormalJournalSourceBindingCatalogV1([binding, binding]));
    }

    private static FinanceManualReceiptNormalJournalSourceProviderV1 Provider(
        IManualReceiptService receipts,
        IFinancePermissionPort permissions,
        IApprovedNormalJournalSourceBindingCatalogV1 bindings) => new(
            receipts,
            permissions,
            bindings,
            new FixedCompany(),
            new FixedUser(),
            new FixedCorrelation());

    private static ApprovedNormalJournalSourceBindingCatalogV1 Catalog(
        NormalJournalPostingRuleV1 rule) => new([Binding(rule)]);

    private static ApprovedNormalJournalSourceBindingV1 Binding(
        NormalJournalPostingRuleV1 rule, Guid? sourceId = null) => new(
        Company,
        sourceId ?? Source,
        NormalJournalSourceKindsV1.RecordedManualReceipt,
        "2",
        "finance-profile",
        "7",
        Currency,
        rule);

    private static NormalJournalPostingRuleV1 Rule(
        string postingPurpose = "synthetic-purpose",
        string version = "synthetic-rule-v1")
    {
        var at = DateTimeOffset.Parse("2026-10-01T09:00:00Z");
        var debit = new AccountRef(Company, Debit);
        var credit = new AccountRef(Company, Credit);
        var mirror = new AccountPlanMirrorV1(Company, "synthetic-mirror-v1", new string('a', 64), Actor, at,
        [
            new AccountPlanMirrorRowV1(debit, "SYNTHETIC-001", "Synthetic debit"),
            new AccountPlanMirrorRowV1(credit, "SYNTHETIC-002", "Synthetic credit"),
        ]);
        return new NormalJournalPostingRuleV1(mirror, postingPurpose, version, Actor, at,
        [
            new NormalJournalMappingV1("synthetic-debit", PostingSide.Debit, debit),
            new NormalJournalMappingV1("synthetic-credit", PostingSide.Credit, credit),
        ]);
    }

    private static SourceDetail Detail(
        SourceStage stage = SourceStage.Recorded,
        AmountDisclosure disclosure = AmountDisclosure.Visible,
        Guid? reversesSourceId = null,
        Guid? sourceId = null)
    {
        var money = disclosure == AmountDisclosure.Visible ? new MoneyText("10.25", Currency) : null;
        var amount = new AmountView(disclosure, money);
        var summary = new SourceSummary(
            sourceId ?? Source,
            Company,
            "FIN-0001",
            "2",
            stage,
            Guid.Parse("85000000-0000-0000-0000-000000000007"),
            Guid.Parse("85000000-0000-0000-0000-000000000008"),
            Guid.Parse("85000000-0000-0000-0000-000000000009"),
            Currency,
            new DateOnly(2026, 10, 1),
            null,
            amount);
        return new SourceDetail(
            summary,
            new ProfileReference("finance-profile", "7"),
            null,
            null,
            DateTimeOffset.Parse("2026-10-01T08:00:00Z"),
            stage == SourceStage.Reversed ? Guid.NewGuid() : null,
            reversesSourceId,
            new UnallocatedView(AllocationKnowledge.Known, amount));
    }

    private sealed class FixedReceipt(SourceDetail detail) : IManualReceiptService
    {
        public FinanceContext? Context { get; private set; }
        public SourceDetailRequest? Request { get; private set; }

        public Task<ReadResult<SourceDetail>> DetailAuthorizedAsync(
            FinanceContext context, SourceDetailRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Context = context;
            Request = request;
            return Task.FromResult(new ReadResult<SourceDetail>(ReadOutcome.Found, detail));
        }

        public Task<CommandResult> DraftSaveAsync(FinanceContext context, CommandMetadata metadata,
            DraftSaveRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CommandResult> RecordAsync(FinanceContext context, CommandMetadata metadata,
            RecordRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CommandResult> ReverseAsync(FinanceContext context, CommandMetadata metadata,
            ReverseRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReadResult<SourcePage>> ListAuthorizedAsync(FinanceContext context,
            SourceListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SequencePermissions(params PermissionDecision[] decisions) : IFinancePermissionPort
    {
        private int _index;
        public List<FinanceAction> Actions { get; } = [];

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action,
            Guid? sourceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add(action);
            return Task.FromResult(decisions[Math.Min(_index++, decisions.Length - 1)]);
        }
    }

    private sealed class FixedCompany : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId => Company;
        public void Set(Guid companyId) => throw new NotSupportedException();
    }

    private sealed class FixedUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "acc-source-test";
        public string? CausationId => "acc-source-parent";
        public string SourceChannel => "UnitTest";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;

        public void Set(string correlationId, string? causationId, string sourceChannel,
            string? clientIpAddress, string? userAgent) => throw new NotSupportedException();
    }
}
