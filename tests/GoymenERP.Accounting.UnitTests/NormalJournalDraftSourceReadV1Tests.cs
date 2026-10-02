using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

public sealed class NormalJournalDraftSourceReadV1Tests
{
    private static readonly Guid Company = Guid.Parse("86000000-0000-0000-0000-000000000001");
    private static readonly Guid Actor = Guid.Parse("86000000-0000-0000-0000-000000000002");
    private static readonly Guid Source = Guid.Parse("86000000-0000-0000-0000-000000000003");
    private static readonly Guid Currency = Guid.Parse("86000000-0000-0000-0000-000000000004");
    private static readonly Guid Debit = Guid.Parse("86000000-0000-0000-0000-000000000005");
    private static readonly Guid Credit = Guid.Parse("86000000-0000-0000-0000-000000000006");

    [Fact]
    public void Fingerprint_is_order_independent_but_binds_source_and_rule_facts()
    {
        var first = SourceSnapshot(reverseMappings: false);
        var reordered = SourceSnapshot(reverseMappings: true);
        var a = NormalJournalDraftSourcePreparationV1.TryCreate(Company, Source, first);
        var b = NormalJournalDraftSourcePreparationV1.TryCreate(Company, Source, reordered);
        var changedSource = NormalJournalDraftSourcePreparationV1.TryCreate(Company, Guid.NewGuid(), first);
        var changedRule = NormalJournalDraftSourcePreparationV1.TryCreate(
            Company, Source, SourceSnapshot(ruleVersion: "rule-v2"));

        Assert.NotNull(a);
        Assert.Equal(a!.PreparationVersion, b!.PreparationVersion);
        Assert.NotEqual(a.PreparationVersion, changedSource!.PreparationVersion);
        Assert.NotEqual(a.PreparationVersion, changedRule!.PreparationVersion);
        Assert.Collection(a.Mappings,
            mapping => Assert.Equal("credit", mapping.MappingKey),
            mapping => Assert.Equal("debit", mapping.MappingKey));
    }

    [Fact]
    public async Task Read_rechecks_create_authority_after_source_resolution()
    {
        var company = new CompanyContext();
        company.Set(Company);
        var service = new NormalJournalDraftSourceReadServiceV1(
            new FixedSource(SourceSnapshot()),
            new NormalJournalPermissionBindingV1(),
            new SequencedAuthority(Granted(), PermissionScope.Denied),
            company,
            new FixedUser());

        var result = await service.ReadAsync(Source);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Missing_legacy_provenance_fails_closed()
    {
        var company = new CompanyContext();
        company.Set(Company);
        var legacy = SourceSnapshot() with
        {
            SourceVersion = null,
            OwnerProfileId = null,
            OwnerProfileVersion = null,
        };
        var service = new NormalJournalDraftSourceReadServiceV1(
            new FixedSource(legacy),
            new NormalJournalPermissionBindingV1(),
            new SequencedAuthority(Granted(), Granted()),
            company,
            new FixedUser());

        var result = await service.ReadAsync(Source);

        Assert.Equal(AccountingApiStatus.Unavailable, result.Status);
    }

    private static NormalJournalDraftServerSourceV1 SourceSnapshot(
        bool reverseMappings = false,
        string ruleVersion = "rule-v1")
    {
        var at = DateTimeOffset.Parse("2026-10-01T10:00:00Z");
        var debit = new AccountRef(Company, Debit);
        var credit = new AccountRef(Company, Credit);
        var mirror = new AccountPlanMirrorV1(Company, "mirror-v1", new string('a', 64), Actor, at,
        [
            new AccountPlanMirrorRowV1(debit, "100", "Cash"),
            new AccountPlanMirrorRowV1(credit, "120", "Customer"),
        ]);
        NormalJournalMappingV1[] mappings =
        [
            new NormalJournalMappingV1("debit", PostingSide.Debit, debit),
            new NormalJournalMappingV1("credit", PostingSide.Credit, credit),
        ];
        if (reverseMappings) Array.Reverse(mappings);
        var rule = new NormalJournalPostingRuleV1(
            mirror, "manual-receipt", ruleVersion, Actor, at, mappings);
        return new NormalJournalDraftServerSourceV1(
            rule, Currency, "2", "finance-profile", "7");
    }

    private static PermissionScope Granted() => new(
        true, true, new HashSet<string>(["ACC-V1"]),
        new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private sealed class FixedSource(NormalJournalDraftServerSourceV1 source)
        : INormalJournalDraftServerSourceProviderV1
    {
        public Task<NormalJournalDraftServerSourceV1?> ResolveAsync(
            Guid companyId, Guid sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<NormalJournalDraftServerSourceV1?>(source);
    }

    private sealed class SequencedAuthority(params PermissionScope[] answers)
        : IErpAuthorityRevalidationService
    {
        private int _index;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(answers[Math.Min(_index++, answers.Length - 1)]);
    }

    private sealed class FixedUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
}
