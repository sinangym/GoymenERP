using System.Reflection;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// #276 in <see cref="AccountingApiService"/> (SİPARİŞ #354 P3-1): the permission is measured before the stored-answer
/// lookup and AGAIN after it, right before the one write. A grant lost in between stores nothing: the store's write is
/// never called.
/// </summary>
public sealed class AccountingApiFenceTests
{
    private static readonly Guid Company = Guid.Parse("a0000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Actor = Guid.Parse("a0000000-0000-4000-8000-0000000000b1");

    [Fact]
    public async Task A_grant_lost_after_the_lookup_refuses_before_the_write()
    {
        var authority = new SequencedAuthority(Granted(), PermissionScope.Denied);
        var writes = new List<string>();
        var service = Service(authority, writes);

        var result = await service.ProposePeriodAsync(
            new ProposePeriodRequestV1(Guid.NewGuid(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), "SENTETIK-fence-1",
            CancellationToken.None);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(2, authority.Calls);
        Assert.Equal(["FindCommandAsync"], writes);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("close")]
    public async Task The_period_transitions_are_fenced_again_before_the_write(string command)
    {
        var authority = new SequencedAuthority(Granted(), PermissionScope.Denied);
        var writes = new List<string>();
        var service = Service(authority, writes);
        var request = new PeriodCommandRequestV1(1);

        var result = command == "approve"
            ? await service.ApprovePeriodAsync(Guid.NewGuid(), request, "SENTETIK-fence-2", CancellationToken.None)
            : await service.ClosePeriodAsync(Guid.NewGuid(), request, "SENTETIK-fence-3", CancellationToken.None);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(2, authority.Calls);
        Assert.Equal(["FindCommandAsync"], writes);
    }

    private static PermissionScope Granted() => new PermissionScope(true, true, new HashSet<string>(["SENTETIK-ROLE"], StringComparer.Ordinal),
        new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private static AccountingApiService Service(IErpAuthorityRevalidationService authority, List<string> calls) =>
        new AccountingApiService(
            Recording<IAccountingPeriodStore>.Create(calls),
            Recording<IAccountingPostingStore>.Create(calls),
            Recording<IAccountingCommandLog>.Create(calls),
            authority,
            new FixedCompany(),
            new FixedUser(),
            new FixedCorrelation());

    private sealed class SequencedAuthority(params PermissionScope[] answers) : IErpAuthorityRevalidationService
    {
        public int Calls { get; private set; }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(answers[Math.Min(Calls++, answers.Length - 1)]);
    }

    /// <summary>Records every store call; the stored-answer lookup answers "none", anything else must not be reached.</summary>
    public class Recording<T> : DispatchProxy
        where T : class
    {
        private List<string> _calls = [];

        public static T Create(List<string> calls)
        {
            var proxy = DispatchProxy.Create<T, Recording<T>>();
            ((Recording<T>)(object)proxy)._calls = calls;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _calls.Add(targetMethod!.Name);
            if (targetMethod.Name == nameof(IAccountingCommandLog.FindCommandAsync))
            {
                return Task.FromResult<AccountingCommandRecord?>(null);
            }

            throw new InvalidOperationException($"The fence must refuse before {targetMethod.Name} is called.");
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

        public string? UserId => Actor.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);

        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;

        public string CorrelationId => "SENTETIK-correlation";

        public string? CausationId => null;

        public string SourceChannel => "Test";

        public string? ClientIpAddress => null;

        public string? UserAgent => null;

        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) =>
            throw new NotSupportedException();
    }
}
