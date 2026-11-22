using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Sales.UnitTests;

/// <summary>W1-A: the shared fakes and SENTETIK fixtures of the Sales Work inbox source tests.</summary>
internal static class SalesWorkInboxTestDoubles
{
    public static readonly Guid Company = Guid.Parse("5a1e0000-0000-0000-0000-00000000c0a1");
    public static readonly Guid OtherCompany = Guid.Parse("5a1e0000-0000-0000-0000-00000000c0b2");
    public static readonly Guid PriceList = Guid.Parse("5a1e0000-0000-0000-0000-0000000001a1");
    public static readonly Guid PriceUnit = Guid.Parse("5a1e0000-0000-0000-0000-0000000001b1");
    public static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-20T09:00:00+03:00");

    public const string Approver = "SENTETIK-approver";
    public const string Maker = "SENTETIK-maker";

    public static WorkInboxSourceContextV1 Context(string userId = Approver) =>
        new(Company, userId, "SENTETIK-correlation", null);

    public static PriceReferenceSnapshot Reference(string listPrice, string? floor) =>
        new(PriceList, "SENTETIK Liste", 3, new DateOnly(2026, 1, 1), null, "TRY", PriceUnit, listPrice, floor, "Excluded",
            "0", null);

    public static SalesWorkInboxDraftLineV1 Line(int lineNo, CommercialDraftFields commercial) =>
        new(Guid.Parse($"5a1e0000-0000-0000-0000-{lineNo:D12}"), lineNo,
            Guid.Parse($"5a1e0000-0000-0000-0001-{lineNo:D12}"), $"SNT-{lineNo}", "SENTETIK Urun", commercial);

    public static SalesWorkInboxDraftV1 Draft(int no, IReadOnlyList<SalesWorkInboxDraftLineV1> lines, Guid? company = null,
        bool subcontract = false) =>
        new(Guid.Parse($"5a1e0000-0000-0000-0002-{no:D12}"), company ?? Company, $"SO-SNT-{no:D4}", subcontract, lines);

    public static PermissionScope Granted() =>
        new(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    internal sealed class Store : ISalesWorkInboxStoreV1
    {
        public IReadOnlyList<SalesWorkInboxDraftV1> Drafts { get; set; } = [];
        public IReadOnlyList<SalesWorkInboxRiskExceptionV1> RiskExceptions { get; set; } = [];
        public Exception? Failure { get; set; }
        public List<(Guid CompanyId, int? Take)> Calls { get; } = [];

        public Task<IReadOnlyList<SalesWorkInboxDraftV1>> ListOpenDraftsWithPricedLinesAsync(Guid companyId,
            CancellationToken cancellationToken)
        {
            Calls.Add((companyId, null));
            return Failure is null ? Task.FromResult(Drafts) : Task.FromException<IReadOnlyList<SalesWorkInboxDraftV1>>(Failure);
        }

        public Task<IReadOnlyList<SalesWorkInboxRiskExceptionV1>> ListPendingRiskExceptionsAsync(Guid companyId, int take,
            CancellationToken cancellationToken)
        {
            Calls.Add((companyId, take));
            return Failure is not null
                ? Task.FromException<IReadOnlyList<SalesWorkInboxRiskExceptionV1>>(Failure)
                : Task.FromResult<IReadOnlyList<SalesWorkInboxRiskExceptionV1>>([.. RiskExceptions.Take(take)]);
        }
    }

    internal sealed class CompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    internal sealed class User(string userId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    internal sealed class Authority(Func<string, bool> granted) : IErpAuthorityRevalidationService
    {
        public List<string> Asked { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(permission);
            return Task.FromResult(granted(permission) ? Granted() : PermissionScope.Denied);
        }
    }

    /// <summary>Every item satisfies the Work source contract's shape rules.</summary>
    public static void AssertContractShape(IWorkInboxSourceV1 source, WorkInboxSourceResultV1 result, bool ownerNamesMaker = true)
    {
        Assert.True(NativeApprovalQueueRulesV1.IsValidKind(source.Module, source.Kind));
        Assert.True(WorkInboxRulesV1.IsValidPermission(source.ApproverPermission));
        // WP1: the duration class is the Work rule table's (YOL-D18 default: business days), never the source's declaration.
        Assert.False(source.AcceptsScopedApprovers);
        foreach (var item in result.Items!)
        {
            Assert.Equal(source.Module, item.Module);
            Assert.Equal(source.Kind, item.Kind);
            Assert.NotEqual(Guid.Empty, item.ItemId);
            Assert.False(string.IsNullOrWhiteSpace(item.DocumentLabel));
            Assert.True(item.DocumentLabel.Length <= WorkInboxRulesV1.MaxDocumentLabelLength);
            Assert.Equal(TimeSpan.Zero, item.WaitingSinceUtc.Offset);
            Assert.True(NativeApprovalQueueRulesV1.IsValidLink(item.Link), item.Link);
            if (ownerNamesMaker)
            {
                Assert.False(string.IsNullOrWhiteSpace(item.PreparedBy));
            }
            else
            {
                Assert.Null(item.PreparedBy);
                Assert.Null(item.ExcludedDeciders);
            }

            Assert.True(item.ExcludedDeciders is null
                || item.ExcludedDeciders.Count is > 0 and <= WorkInboxRulesV1.MaxExcludedDecidersPerItem);
            Assert.InRange(item.Rules.Count, 1, WorkInboxRulesV1.MaxRulesPerItem);
            Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule(source.Module, rule), rule.RuleCode));
            Assert.InRange(item.References.Count, 0, WorkInboxRulesV1.MaxReferencesPerItem);
            Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference), reference.Role));
        }
    }
}
