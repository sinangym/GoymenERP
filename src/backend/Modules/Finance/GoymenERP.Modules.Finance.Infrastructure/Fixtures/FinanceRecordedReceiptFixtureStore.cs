using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Finance.Infrastructure.Fixtures;

public sealed record FinanceRecordedReceiptFixtureOutput(
    Guid CompanyId,
    Guid SourceId,
    string SourceVersion,
    string OwnerProfileId,
    string OwnerProfileVersion,
    Guid CurrencyId);

public interface IFinanceRecordedReceiptFixtureStore
{
    Task<FinanceRecordedReceiptFixtureOutput> EnsureAsync(CancellationToken cancellationToken);
}

public static class FinanceRecordedReceiptFixtureStoreRegistration
{
    public static IServiceCollection AddFinanceRecordedReceiptFixtureStore(this IServiceCollection services)
    {
        services.AddScoped<IFinanceRecordedReceiptFixtureStore>(provider =>
            new FinanceRecordedReceiptFixtureStore(
                provider.GetRequiredService<FinanceDbContext>(),
                provider.GetRequiredService<IAuditEventCollector>(),
                provider.GetRequiredService<TimeProvider>()));
        return services;
    }
}

/// <summary>
/// ACC01's one synthetic recorded receipt, created and recorded through Finance's real service. This
/// store has no public registration: the central host exposes it only under the complete browser-runner
/// gate. There is no raw SQL, grant, production catalog or caller-selected identity/value.
/// </summary>
internal sealed class FinanceRecordedReceiptFixtureStore(
    FinanceDbContext dbContext,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider) : IFinanceRecordedReceiptFixtureStore
{
    private static readonly Guid ActorId = Guid.Parse("b2100000-0000-4000-8000-00000000ac01");
    private static readonly Guid PartyId = Guid.Parse("b7100000-0000-4000-8000-00000000ac01");
    private static readonly Guid CompanyAccountId = Guid.Parse("b7200000-0000-4000-8000-00000000ac01");
    private static readonly Guid TreasuryAccountId = Guid.Parse("b7300000-0000-4000-8000-00000000ac01");
    private const string Actor = "acc01-preparer";
    private const string Role = "BROWSER_ACC01_PREPARER";

    public async Task<FinanceRecordedReceiptFixtureOutput> EnsureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = ApprovedMoneyProfileCatalog.GdcTurkishLiraV1;
        var identity = new FixtureIdentity(ActorId, profile.CompanyId);
        var permissions = new FixturePermissions();
        var service = new ReceiptDraftService(
            dbContext,
            identity,
            identity,
            permissions,
            new FixtureBankAccountReader(profile.CompanyId, profile.CurrencyId),
            new FixturePartyAccountPort(profile.CompanyId),
            new ApprovedSettingsPrerequisiteProvider(identity, identity),
            auditEvents,
            new FinanceReceiptDraftBinding(FinanceMoneyOperations.ManualBankReceiptV1),
            new FinanceAuditEnvelope(FinancePermissionNames.DraftSave, [Role], SourceChannels.System),
            timeProvider,
            new FinanceAuditEnvelope(FinancePermissionNames.Record, [Role], SourceChannels.System),
            new FixtureOpenPeriodRule());

        var context = new FinanceContext(profile.CompanyId, Actor, "sentetik-acc01-receipt-save", null);
        var save = await service.DraftSaveAsync(
            context,
            new CommandMetadata("sentetik-acc01-receipt-save-v1"),
            new DraftSaveRequest(
                null,
                null,
                new ReceiptPreparation(
                    PartyId,
                    CompanyAccountId,
                    TreasuryAccountId,
                    new MoneyText("10.25", profile.CurrencyId),
                    new ProfileReference(
                        profile.ProfileId.ToString("D"),
                        profile.Version.ToString(CultureInfo.InvariantCulture)),
                    new DateOnly(2026, 9, 24),
                    null,
                    "SENTETIK-ACC01-RECEIPT",
                    "SENTETIK browser-suite:acc01 recorded Finance source")),
            cancellationToken);
        var draft = RequireSucceeded(save, "save");

        var record = await service.RecordAsync(
            context with { CorrelationId = "sentetik-acc01-receipt-record" },
            new CommandMetadata("sentetik-acc01-receipt-record-v1"),
            new RecordRequest(draft.Source.Id, draft.Source.Version),
            cancellationToken);
        var recorded = RequireSucceeded(record, "record");
        if (recorded.Source.Stage != SourceStage.Recorded
            || recorded.Source.CompanyId != profile.CompanyId
            || recorded.Source.CurrencyId != profile.CurrencyId)
        {
            throw new InvalidOperationException("The ACC01 Finance fixture did not produce the exact recorded source.");
        }

        return new FinanceRecordedReceiptFixtureOutput(
            profile.CompanyId,
            recorded.Source.Id,
            recorded.Source.Version,
            recorded.Profile.ProfileId,
            recorded.Profile.Version,
            recorded.Source.CurrencyId);
    }

    private static SourceDetail RequireSucceeded(CommandResult result, string action) =>
        result is { Outcome: CommandOutcome.Succeeded, Resource: not null }
            ? result.Resource
            : throw new InvalidOperationException(
                $"The ACC01 Finance fixture {action} was refused ({result.Outcome}: "
                + string.Join(',', result.Issues.Select(issue => $"{issue.Field}={issue.Code}")) + ").");

    private sealed class FixtureIdentity(Guid userId, Guid companyId) : ICompanyContext, ICurrentUser
    {
        private static readonly IReadOnlySet<Guid> Companies =
            new HashSet<Guid> { ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.CompanyId };

        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = companyId;
        public bool IsAuthenticated => true;
        public string UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds => Companies;
        public bool CanAccessCompany(Guid candidate) => Companies.Contains(candidate);

        public void Set(Guid candidate)
        {
            if (!CanAccessCompany(candidate))
            {
                throw new InvalidOperationException("The ACC01 Finance fixture identity is bound only to GDC.");
            }

            CompanyId = candidate;
        }
    }

    private sealed class FixturePermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PermissionDecision.Allowed);
        }
    }

    private sealed class FixtureBankAccountReader(Guid companyId, Guid currencyId) : IFinanceBankAccountReader
    {
        private readonly BankAccountView _account =
            new(TreasuryAccountId, companyId, currencyId, "SENTETIK ACC01 Treasury", true, true, "1");

        public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ReadResult<BankAccountPage>(ReadOutcome.Found,
                new BankAccountPage(companyId, [_account])));
        }

        public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(accountId == _account.Id
                ? new ReadResult<BankAccountView>(ReadOutcome.Found, _account)
                : new ReadResult<BankAccountView>(ReadOutcome.NotFound, null));
        }
    }

    private sealed class FixturePartyAccountPort(Guid companyId) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = partyId == PartyId && companyAccountId == CompanyAccountId;
            return Task.FromResult(found
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(PartyId, CompanyAccountId, companyId, true, null, true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
        }
    }

    private sealed class FixtureOpenPeriodRule : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
        }
    }
}
